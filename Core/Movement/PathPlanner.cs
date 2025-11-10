using System;
using System.Collections.Generic;
using System.Linq;
using BetterFollowbot.Interfaces;
using BetterFollowbot.Core.TaskManagement;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;

namespace BetterFollowbot.Core.Movement
{
    public class PathPlanner
    {
        private readonly IFollowbotCore _core;
        private readonly ILeaderDetector _leaderDetector;
        private readonly ITaskManager _taskManager;
        private readonly PortalManager _portalManager;

        // Throttle frequent log messages
        private DateTime _lastPortalThresholdLog = DateTime.MinValue;
        
        // Zone transition retry management
        private int _zoneTransitionAttempts = 0;
        private DateTime _lastZoneTransitionAttemptTime = DateTime.MinValue;
        private string _lastAttemptedZoneTransition = "";
        private bool _triedPortalMethod = false;
        private bool _triedSwirlyMethod = false;
        private const int MAX_ZONE_TRANSITION_ATTEMPTS = 3;
        private const double ZONE_TRANSITION_RETRY_COOLDOWN_SECONDS = 5.0;

        /// <summary>
        /// Checks if any blocking UI elements are open that should prevent path planning
        /// </summary>
        private bool IsBlockingUiOpen()
        {
            try
            {
                // Check common blocking UI elements
                var stashOpen = _core.GameController?.IngameState?.IngameUi?.StashElement?.IsVisibleLocal == true;
                var npcDialogOpen = _core.GameController?.IngameState?.IngameUi?.NpcDialog?.IsVisible == true;
                var sellWindowOpen = _core.GameController?.IngameState?.IngameUi?.SellWindow?.IsVisible == true;
                var purchaseWindowOpen = _core.GameController?.IngameState?.IngameUi?.PurchaseWindow?.IsVisible == true;
                var inventoryOpen = _core.GameController?.IngameState?.IngameUi?.InventoryPanel?.IsVisible == true;
                var skillTreeOpen = _core.GameController?.IngameState?.IngameUi?.TreePanel?.IsVisible == true;
                var atlasOpen = _core.GameController?.IngameState?.IngameUi?.Atlas?.IsVisible == true;

                // Note: Map is non-obstructing in PoE, so we don't check it
                return stashOpen || npcDialogOpen || sellWindowOpen || purchaseWindowOpen || inventoryOpen || skillTreeOpen || atlasOpen;
            }
            catch
            {
                // If we can't check UI state, err on the side of caution
                return true;
            }
        }
        
        /// <summary>
        /// Resets the zone transition retry state (called when transition succeeds or zone changes)
        /// </summary>
        private void ResetZoneTransitionRetryState()
        {
            if (_zoneTransitionAttempts > 0)
            {
                _core.LogMessage($"ZONE TRANSITION RETRY: Resetting retry state (was at {_zoneTransitionAttempts} attempts)");
            }
            _zoneTransitionAttempts = 0;
            _lastZoneTransitionAttemptTime = DateTime.MinValue;
            _lastAttemptedZoneTransition = "";
            _triedPortalMethod = false;
            _triedSwirlyMethod = false;
        }
        
        /// <summary>
        /// Checks if we're currently in the retry cooldown period after failed zone transition attempts
        /// </summary>
        private bool IsInZoneTransitionRetryCooldown()
        {
            if (_lastZoneTransitionAttemptTime == DateTime.MinValue)
                return false;
                
            var timeSinceLastAttempt = (DateTime.Now - _lastZoneTransitionAttemptTime).TotalSeconds;
            return timeSinceLastAttempt < ZONE_TRANSITION_RETRY_COOLDOWN_SECONDS;
        }
        
        /// <summary>
        /// Records a zone transition attempt
        /// </summary>
        private void RecordZoneTransitionAttempt(string transitionKey, string methodName)
        {
            // If this is a new transition (different zone), reset counters
            if (_lastAttemptedZoneTransition != transitionKey)
            {
                _core.LogMessage($"ZONE TRANSITION RETRY: New transition detected ('{transitionKey}'), resetting counters");
                ResetZoneTransitionRetryState();
                _lastAttemptedZoneTransition = transitionKey;
            }
            
            _zoneTransitionAttempts++;
            _lastZoneTransitionAttemptTime = DateTime.Now;
            _core.LogMessage($"ZONE TRANSITION RETRY: Attempt {_zoneTransitionAttempts}/{MAX_ZONE_TRANSITION_ATTEMPTS} using {methodName}");
        }
        
        /// <summary>
        /// Determines the next transition method to try based on retry state
        /// Returns: "portal", "swirly", "wait", or "failed"
        /// </summary>
        private string GetNextTransitionMethod()
        {
            // If we've exceeded max attempts, wait for cooldown
            if (_zoneTransitionAttempts >= MAX_ZONE_TRANSITION_ATTEMPTS)
            {
                if (IsInZoneTransitionRetryCooldown())
                {
                    var timeRemaining = ZONE_TRANSITION_RETRY_COOLDOWN_SECONDS - (DateTime.Now - _lastZoneTransitionAttemptTime).TotalSeconds;
                    _core.LogMessage($"ZONE TRANSITION RETRY: Waiting {timeRemaining:F1}s before next retry cycle");
                    return "wait";
                }
                else
                {
                    // Cooldown expired, reset and start a new cycle
                    _core.LogMessage("ZONE TRANSITION RETRY: Cooldown expired, starting new retry cycle");
                    _zoneTransitionAttempts = 0;
                    _triedPortalMethod = false;
                    _triedSwirlyMethod = false;
                }
            }
            
            // Try portal method first (attempts 1-2)
            if (!_triedPortalMethod || _zoneTransitionAttempts < 2)
            {
                return "portal";
            }
            // Then try swirly method (attempt 3)
            else if (!_triedSwirlyMethod)
            {
                return "swirly";
            }
            // All methods exhausted in this cycle
            else
            {
                return "wait";
            }
        }

        public PathPlanner(IFollowbotCore core, ILeaderDetector leaderDetector, ITaskManager taskManager, PortalManager portalManager)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _leaderDetector = leaderDetector ?? throw new ArgumentNullException(nameof(leaderDetector));
            _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            _portalManager = portalManager ?? throw new ArgumentNullException(nameof(portalManager));
        }
        
        /// <summary>
        /// Attempts to create a zone transition task using the appropriate method (portal or swirly)
        /// Returns true if a task was created successfully
        /// </summary>
        private bool TryCreateZoneTransitionTask(PartyElementWindow leaderPartyElement, string method)
        {
            if (leaderPartyElement == null)
                return false;
                
            var currentZone = _core.GameController?.Area?.CurrentArea?.DisplayName ?? "";
            var leaderZone = leaderPartyElement.ZoneName ?? "";
            var transitionKey = $"{currentZone}->{leaderZone}";
            const float MAX_PORTAL_DISTANCE = 750f; // Don't try portals farther than this
            
            if (method == "portal")
            {
                _core.LogMessage($"ZONE TRANSITION RETRY: Attempting portal method for transition '{transitionKey}'");
                
                // Try matching portal first (for special areas, endgame maps, etc.)
                var matchingPortal = PortalManager.FindMatchingPortal(leaderZone, preferHideoutPortals: true);
                if (matchingPortal != null)
                {
                    var portalDistance = matchingPortal.ItemOnGround?.DistancePlayer ?? 9999f;
                    if (portalDistance > MAX_PORTAL_DISTANCE)
                    {
                        _core.LogMessage($"ZONE TRANSITION RETRY: Found matching portal '{matchingPortal.Label?.Text}' but it's too far away ({portalDistance:F1} > {MAX_PORTAL_DISTANCE}), skipping portal method");
                        _triedPortalMethod = true;
                        return false; // Skip to swirly method
                    }
                    
                    _core.LogMessage($"ZONE TRANSITION RETRY: Found matching portal '{matchingPortal.Label?.Text}' at distance {portalDistance:F1}");
                    _taskManager.AddTask(new TaskNode(matchingPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                    _triedPortalMethod = true;
                    RecordZoneTransitionAttempt(transitionKey, "Portal (Matching)");
                    return true;
                }
                
                // Try best portal (closest, etc.)
                var bestPortal = GetBestPortalLabel(leaderPartyElement, forceSearch: true);
                if (bestPortal != null)
                {
                    var portalDistance = bestPortal.ItemOnGround?.DistancePlayer ?? 9999f;
                    if (portalDistance > MAX_PORTAL_DISTANCE)
                    {
                        _core.LogMessage($"ZONE TRANSITION RETRY: Found nearby portal '{bestPortal.Label?.Text}' but it's too far away ({portalDistance:F1} > {MAX_PORTAL_DISTANCE}), skipping portal method");
                        _triedPortalMethod = true;
                        return false; // Skip to swirly method
                    }
                    
                    _core.LogMessage($"ZONE TRANSITION RETRY: Found nearby portal '{bestPortal.Label?.Text}' at distance {portalDistance:F1}");
                    _taskManager.AddTask(new TaskNode(bestPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                    _triedPortalMethod = true;
                    RecordZoneTransitionAttempt(transitionKey, "Portal (Nearby)");
                    return true;
                }
                
                _core.LogMessage("ZONE TRANSITION RETRY: No portals found for portal method");
                _triedPortalMethod = true;
                return false;
            }
            else if (method == "swirly")
            {
                _core.LogMessage($"ZONE TRANSITION RETRY: Attempting swirly method for transition '{transitionKey}'");
                
                // Try party teleport button (swirly)
                var tpButton = GetTpButton(leaderPartyElement);
                if (!tpButton.Equals(Vector2.Zero))
                {
                    _core.LogMessage("ZONE TRANSITION RETRY: Found party teleport button (swirly)");
                    AutoPilot.IsTeleportInProgress = true;
                    _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                    _triedSwirlyMethod = true;
                    RecordZoneTransitionAttempt(transitionKey, "Swirly (Party TP)");
                    return true;
                }
                
                _core.LogMessage("ZONE TRANSITION RETRY: Party teleport button not available");
                _triedSwirlyMethod = true;
                return false;
            }
            
            return false;
        }

        /// <summary>
        /// Plans and creates movement tasks based on leader position and current game state
        /// </summary>
        public void PlanPath(Entity followTarget, PartyElementWindow leaderPartyElement, Vector3 lastTargetPosition, Vector3 lastPlayerPosition)
        {
            try
            {
                // Reset retry state if we're in the same zone as the leader (successful transition or no transition needed)
                if (leaderPartyElement != null && _core.GameController?.Area?.CurrentArea != null)
                {
                    var currentZone = _core.GameController.Area.CurrentArea.DisplayName ?? "";
                    var leaderZone = leaderPartyElement.ZoneName ?? "";
                    var zonesAreEqual = PortalManager.AreZonesEqual(currentZone, leaderZone);
                    
                    // If zones are equal and we had a pending transition, reset (successful transition)
                    if (zonesAreEqual && _zoneTransitionAttempts > 0)
                    {
                        _core.LogMessage($"ZONE TRANSITION RETRY: Bot reached leader zone '{currentZone}', resetting retry state");
                        ResetZoneTransitionRetryState();
                    }
                }
                
                // Reset retry state if we're close to the leader (successful follow)
                if (followTarget != null && followTarget.Pos != null && _zoneTransitionAttempts > 0)
                {
                    var distanceForReset = Vector3.Distance(_core.PlayerPosition, followTarget.Pos);
                    if (distanceForReset < 500) // Within reasonable follow distance
                    {
                        _core.LogMessage($"ZONE TRANSITION RETRY: Bot is close to leader ({distanceForReset:F1} units), resetting retry state");
                        ResetZoneTransitionRetryState();
                    }
                }
                
                if (AutoPilot.IsTeleportInProgress)
                {
                    _core.LogMessage($"TELEPORT: Blocking all task creation - teleport in progress ({_taskManager.TaskCount} tasks)");
                    return;
                }

                // Check if we should block transition tasks due to post-respawn waiting
                if (_core.ShouldBlockTransitionTasks())
                {
                    _core.LogMessage($"POST-RESPAWN: Blocking transition task creation - waiting for leader to return to zone");
                    return;
                }

                if (_portalManager.IsInPortalTransition)
                {
                    _core.LogMessage($"PORTAL: In portal transition mode - actively searching for portals to follow leader");

                    if (leaderPartyElement != null)
                    {
                        var portal = GetBestPortalLabel(leaderPartyElement, forceSearch: true);
                        if (portal != null)
                        {
                            // Only create transition task if there isn't already one pending
                            var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                            if (!hasExistingTransitionTask)
                            {
                                _core.LogMessage($"PORTAL: Found portal '{portal.Label?.Text}' during transition - creating transition task");
                                _taskManager.AddTask(new TaskNode(portal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                _core.LogMessage($"PORTAL: Portal transition task created for portal at {portal.ItemOnGround.Pos}");
                            }
                            else
                            {
                                _core.LogMessage($"PORTAL: Transition task already exists, skipping portal creation for '{portal.Label?.Text}'");
                            }
                        }
                        else
                        {
                            _core.LogMessage($"PORTAL: No portals found during transition - will retry on next update");
                        }
                    }
                    else
                    {
                        _core.LogMessage($"PORTAL: Cannot search for portals - no leader party element found");
                    }
                }

                if (_portalManager.IsInPortalTransition && followTarget != null)
                {
                    var portalTransitionDistance = Vector3.Distance(_core.PlayerPosition, followTarget.Pos);
                    if (portalTransitionDistance < 1000)
                    {
                        _core.LogMessage($"PORTAL: Bot successfully reached leader after portal transition - clearing portal transition mode");
                        _portalManager.SetPortalTransitionMode(false);
                    }
                }

                // Check for special portals (arena portals) that should be clicked even when not in portal transition mode
                if (!_portalManager.IsInPortalTransition && leaderPartyElement != null && followTarget != null)
                {
                    // Check if leader is far away (might have gone through arena portal)
                    var leaderDistance = Vector3.Distance(_core.PlayerPosition, followTarget.Pos);

                    // Check if leader is far enough away to warrant taking a portal
                    var allPortals = GetAllPortals(leaderPartyElement, forceSearch: true);
                    var validPortals = allPortals.Where(portal =>
                    {
                        var portalLabel = portal.Label?.Text ?? "";
                        return PortalManager.IsSpecialPortal(portalLabel.ToLower()) ||
                               PortalManager.GetSpecialPortalType(portalLabel.ToLower()) == "Arena";
                    }).ToList();

                    // Only proceed if leader is far enough away from any valid portal
                    var shouldTakePortal = false;
                    LabelOnGround selectedPortal = null;

                    foreach (var portal in validPortals)
                    {
                        var portalLabel = portal.Label?.Text ?? "";
                        var portalDistanceThreshold = PortalManager.GetPortalDistanceThreshold(portalLabel);

                        if (leaderDistance > portalDistanceThreshold)
                        {
                            shouldTakePortal = true;
                            selectedPortal = portal;
                            _core.LogMessage($"ARENA PORTAL: Leader is {leaderDistance:F1} units away (> {portalDistanceThreshold:F1} threshold) - taking portal '{portalLabel}'");
                            break;
                        }
                    }

                    if (shouldTakePortal && selectedPortal != null)
                    {
                        var selectedPortalLabel = selectedPortal.Label?.Text ?? "";
                        var isSpecialPortal = PortalManager.IsSpecialPortal(selectedPortalLabel.ToLower());
                        var isArenaPortal = PortalManager.GetSpecialPortalType(selectedPortalLabel.ToLower()) == "Arena";

                        if (isSpecialPortal || isArenaPortal)
                        {
                            // Only create transition task if there isn't already one pending
                            var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                            if (!hasExistingTransitionTask)
                            {
                                _core.LogMessage($"ARENA PORTAL: Creating transition task for portal '{selectedPortalLabel}'");
                                _taskManager.AddTask(new TaskNode(selectedPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                _core.LogMessage($"ARENA PORTAL: Portal transition task created for portal at {selectedPortal.ItemOnGround.Pos}");
                            }
                            else
                            {
                                _core.LogMessage($"ARENA PORTAL: Transition task already exists for portal '{selectedPortalLabel}', skipping creation");
                            }
                        }
                        else
                        {
                            _core.LogMessage($"ARENA PORTAL: Selected portal '{selectedPortalLabel}' is not special/arena, ignoring");
                        }
                    }
                    else if (leaderDistance > 100 && (DateTime.Now - _lastPortalThresholdLog).TotalSeconds > 5) // Fallback: if leader is far but no portals should be taken, log occasionally
                    {
                        _core.LogMessage($"ARENA PORTAL: Leader is {leaderDistance:F1} units away but not far enough for any portal threshold");
                        _lastPortalThresholdLog = DateTime.Now;
                    }
                }

                if (!_core.Settings.Enable.Value || !_core.Settings.autoPilotEnabled.Value || _core.LocalPlayer == null || !_core.LocalPlayer.IsAlive ||
                    !_core.GameController.IsForeGroundCache || IsBlockingUiOpen() || _core.GameController.IsLoading || !_core.GameController.InGame)
                {
                    return;
                }

                if (_core.GameController.IsLoading ||
                    _core.GameController.Area.CurrentArea == null ||
                    string.IsNullOrEmpty(_core.GameController.Area.CurrentArea.DisplayName))
                {
                    _core.LogMessage("ZONE LOADING: Blocking all task creation during zone loading to prevent random movement");
                    if (_taskManager.TaskCount > 0)
                    {
                        var clearedTasks = _taskManager.TaskCount;
                        _taskManager.ClearTasks();
                        _core.LogMessage($"ZONE LOADING: Cleared {clearedTasks} tasks during zone loading");
                    }
                    return;
                }

                bool hasTransitionTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);
                if (!hasTransitionTasks)
                {
                    var tpConfirmation = GetTpConfirmation();
                    if (tpConfirmation != null)
                    {
                        _core.LogMessage("TELEPORT: Found open confirmation dialog, handling it immediately");
                        var center = tpConfirmation.GetClientRect().Center;
                        _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                        return;
                    }
                }

                if (followTarget == null && leaderPartyElement != null && !PortalManager.AreZonesEqual(leaderPartyElement.ZoneName, _core.GameController?.Area.CurrentArea.DisplayName))
                {
                    var currentZone = _core.GameController?.Area.CurrentArea.DisplayName ?? "Unknown";
                    var leaderZone = leaderPartyElement.ZoneName ?? "Unknown";

                    _core.LogMessage($"ZONE TRANSITION: Leader is in different zone - Current: '{currentZone}', Leader: '{leaderZone}'");

                    if (!hasTransitionTasks)
                    {
                        // Check for teleport confirmation dialog first
                        var tpConfirmation = GetTpConfirmation();
                        if (tpConfirmation != null)
                        {
                            _core.LogMessage("ZONE TRANSITION: Teleport confirmation dialog already open, handling it");
                            var center = tpConfirmation.GetClientRect().Center;
                            _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                        }
                        // SPECIAL CASE: Labyrinth areas don't support party TP - always use portals
                        else if (PortalManager.IsInLabyrinthArea && !(_core.GameController?.Area?.CurrentArea?.DisplayName?.Contains("Aspirants' Plaza") ?? false))
                        {
                            _core.LogMessage("ZONE TRANSITION: In labyrinth area, using portal-only method");
                            TryCreateZoneTransitionTask(leaderPartyElement, "portal");
                        }
                        // SPECIAL CASE: Special areas like Maligaro's Sanctum don't support party TP
                        else if (PortalManager.IsSpecialArea(leaderPartyElement.ZoneName))
                        {
                            _core.LogMessage($"ZONE TRANSITION: Leader in special area '{leaderPartyElement.ZoneName}', using portal-only method");
                            TryCreateZoneTransitionTask(leaderPartyElement, "portal");
                        }
                        // Normal zones: Use retry system with portal-first, swirly-second strategy
                        else
                        {
                            var nextMethod = GetNextTransitionMethod();
                            
                            if (nextMethod == "wait")
                            {
                                // In cooldown period, do nothing
                                var timeRemaining = ZONE_TRANSITION_RETRY_COOLDOWN_SECONDS - (DateTime.Now - _lastZoneTransitionAttemptTime).TotalSeconds;
                                if ((DateTime.Now - _lastZoneTransitionAttemptTime).TotalSeconds % 2 < 0.1) // Log every ~2 seconds
                                {
                                    _core.LogMessage($"ZONE TRANSITION RETRY: Waiting {timeRemaining:F1}s before next retry (both portal and swirly failed)");
                                }
                            }
                            else
                            {
                                _core.LogMessage($"ZONE TRANSITION: Using retry system - trying {nextMethod} method");
                                var success = TryCreateZoneTransitionTask(leaderPartyElement, nextMethod);
                                
                                if (!success)
                                {
                                    _core.LogMessage($"ZONE TRANSITION: {nextMethod} method failed, will try alternative next time");
                                }
                            }
                        }
                    }
                }

                // Handle null followTarget gracefully - can still do zone transitions via party element
                if (followTarget == null || followTarget.Pos == null)
                {
                    // Still check for zone transitions even if entity is null
                    if (leaderPartyElement != null && _core.GameController?.Area.CurrentArea != null)
                    {
                        var zonesAreDifferent = !PortalManager.AreZonesEqual(leaderPartyElement.ZoneName, _core.GameController.Area.CurrentArea.DisplayName);
                        if (zonesAreDifferent)
                        {
                            _core.LogMessage($"ZONE TRANSITION: Leader in different zone via party element - Current: '{_core.GameController.Area.CurrentArea.DisplayName}', Leader: '{leaderPartyElement.ZoneName}'");

                            if (!HasConflictingTransitionTasks())
                            {
                                // SPECIAL CASE: Labyrinth areas don't support party TP - always use portals
                                if (PortalManager.IsInLabyrinthArea && !(_core.GameController?.Area?.CurrentArea?.DisplayName?.Contains("Aspirants' Plaza") ?? false))
                                {
                                    _core.LogMessage("ZONE TRANSITION: In labyrinth area (null entity case), using portal-only method");
                                    TryCreateZoneTransitionTask(leaderPartyElement, "portal");
                                }
                                // SPECIAL CASE: Special areas like Maligaro's Sanctum don't support party TP
                                else if (PortalManager.IsSpecialArea(leaderPartyElement.ZoneName))
                                {
                                    _core.LogMessage($"ZONE TRANSITION: Leader in special area '{leaderPartyElement.ZoneName}' (null entity case), using portal-only method");
                                    TryCreateZoneTransitionTask(leaderPartyElement, "portal");
                                }
                                // Normal zones: Use retry system with portal-first, swirly-second strategy
                                else
                                {
                                    var nextMethod = GetNextTransitionMethod();
                                    
                                    if (nextMethod == "wait")
                                    {
                                        // In cooldown period, do nothing
                                        var timeRemaining = ZONE_TRANSITION_RETRY_COOLDOWN_SECONDS - (DateTime.Now - _lastZoneTransitionAttemptTime).TotalSeconds;
                                        if ((DateTime.Now - _lastZoneTransitionAttemptTime).TotalSeconds % 2 < 0.1) // Log every ~2 seconds
                                        {
                                            _core.LogMessage($"ZONE TRANSITION RETRY: Waiting {timeRemaining:F1}s before next retry (null entity case)");
                                        }
                                    }
                                    else
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Using retry system (null entity case) - trying {nextMethod} method");
                                        var success = TryCreateZoneTransitionTask(leaderPartyElement, nextMethod);
                                        
                                        if (!success)
                                        {
                                            _core.LogMessage($"ZONE TRANSITION: {nextMethod} method failed (null entity case)");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    return; // Can't do normal path planning without followTarget
                }

                var distanceToLeader = Vector3.Distance(_core.PlayerPosition, followTarget.Pos);
                var distanceMoved = Vector3.Distance(lastTargetPosition, followTarget.Pos);
                
                // DEBUG: Log when bot is far from leader but has no tasks
                if (_taskManager.TaskCount == 0 && distanceToLeader > 200)
                {
                    _core.LogMessage($"PATH DEBUG: No tasks, checking conditions - Distance: {distanceToLeader:F1}, DistanceMoved: {distanceMoved:F1}, ClearPathThreshold: {_core.Settings.autoPilotClearPathDistance.Value}, LastTargetPos: {(lastTargetPosition == Vector3.Zero ? "Zero" : "Valid")}");
                }
                
                if (distanceToLeader >= _core.Settings.autoPilotClearPathDistance.Value)
                {
                    if (_taskManager.Tasks.Any(t =>
                        t.Type == TaskNodeType.Transition ||
                        t.Type == TaskNodeType.TeleportConfirm ||
                        t.Type == TaskNodeType.TeleportButton))
                    {
                        _core.LogMessage("ZONE TRANSITION: Transition/teleport task already active, skipping movement processing");
                        return;
                    }
                    if (lastTargetPosition != Vector3.Zero && distanceMoved > _core.Settings.autoPilotClearPathDistance.Value)
                    {
                        var isLikelyZoneTransition = distanceMoved > 1000;

                        if (isLikelyZoneTransition)
                        {
                            _core.LogMessage($"ZONE TRANSITION DETECTED: Leader moved {distanceMoved:F1} units, likely zone transition");

                            var zonesAreDifferent = leaderPartyElement != null && !PortalManager.AreZonesEqual(leaderPartyElement.ZoneName, _core.GameController?.Area.CurrentArea.DisplayName);

                            if (zonesAreDifferent)
                            {
                                _core.LogMessage($"ZONE TRANSITION: Confirmed different zones - Current: '{_core.GameController?.Area.CurrentArea.DisplayName}', Leader: '{leaderPartyElement?.ZoneName}'");
                            }
                            else
                            {
                                _core.LogMessage($"ZONE TRANSITION: Zone names same but large distance, assuming transition anyway");
                            }

                            _core.LogMessage("ZONE TRANSITION: Leader moved far, prioritizing party teleport for following");

                            // Check if teleport confirmation dialog is already open
                            var tpConfirmation = GetTpConfirmation();
                            if (tpConfirmation != null)
                            {
                                _core.LogMessage("ZONE TRANSITION: Teleport confirmation dialog already open, handling it");
                                var center = tpConfirmation.GetClientRect().Center;
                                _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                            }
                            // SPECIAL CASE: Labyrinth areas don't support party TP - always use portals
                            else if (PortalManager.IsInLabyrinthArea && !(_core.GameController?.Area?.CurrentArea?.DisplayName?.Contains("Aspirants' Plaza") ?? false))
                            {
                                _core.LogMessage("ZONE TRANSITION: In labyrinth area (large movement case), using portal-only method");
                                TryCreateZoneTransitionTask(leaderPartyElement, "portal");
                            }
                            // SPECIAL CASE: Special areas like Maligaro's Sanctum don't support party TP
                            else if (leaderPartyElement != null && PortalManager.IsSpecialArea(leaderPartyElement.ZoneName))
                            {
                                _core.LogMessage($"ZONE TRANSITION: Leader in special area '{leaderPartyElement.ZoneName}' (large movement case), using portal-only method");
                                TryCreateZoneTransitionTask(leaderPartyElement, "portal");
                            }
                            // Normal zones: Use retry system with portal-first, swirly-second strategy
                            else
                            {
                                var nextMethod = GetNextTransitionMethod();
                                
                                if (nextMethod == "wait")
                                {
                                    // In cooldown period, do nothing
                                    var timeRemaining = ZONE_TRANSITION_RETRY_COOLDOWN_SECONDS - (DateTime.Now - _lastZoneTransitionAttemptTime).TotalSeconds;
                                    if ((DateTime.Now - _lastZoneTransitionAttemptTime).TotalSeconds % 2 < 0.1) // Log every ~2 seconds
                                    {
                                        _core.LogMessage($"ZONE TRANSITION RETRY: Waiting {timeRemaining:F1}s before next retry (large movement case)");
                                    }
                                }
                                else
                                {
                                    _core.LogMessage($"ZONE TRANSITION: Using retry system (large movement case) - trying {nextMethod} method");
                                    var success = TryCreateZoneTransitionTask(leaderPartyElement, nextMethod);
                                    
                                    if (!success)
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: {nextMethod} method failed (large movement case)");
                                    }
                                }
                            }
                        }
                        else
                        {
                            _core.LogMessage($"LEADER MOVED FAR: Leader moved {distanceMoved:F1} units but within reasonable distance, using normal movement/dash");
                        }
                    }
                    else if (lastTargetPosition != Vector3.Zero && distanceMoved <= _core.Settings.autoPilotClearPathDistance.Value)
                    {
                        _core.LogMessage($"PATH DEBUG: LastTargetPos valid but distanceMoved too small ({distanceMoved:F1} <= {_core.Settings.autoPilotClearPathDistance.Value}) - no zone transition detected");
                        
                        // Leader is far but hasn't moved enough to trigger zone transition - create normal movement tasks
                        // No distance cap - bot should follow regardless of distance in same zone
                        if (_taskManager.TaskCount == 0 && distanceToLeader > 200)
                        {
                            if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                            {
                                _core.LogMessage($"ROUTE RECORDING: Creating waypoint to distant but stationary leader - Distance: {distanceToLeader:F1}");
                                
                                if (distanceToLeader > _core.Settings.autoPilotDashDistance && _core.Settings.autoPilotDashEnabled)
                                {
                                    _core.LogMessage($"ROUTE RECORDING: Adding Dash task to distant leader - Distance: {distanceToLeader:F1}");
                                    _taskManager.AddTask(new TaskNode(followTarget.Pos, 0, TaskNodeType.Dash));
                                }
                                else
                                {
                                    _core.LogMessage($"ROUTE RECORDING: Adding Movement task to distant leader");
                                    _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                }
                            }
                        }
                    }
                    //We have no path, set us to go to leader pos using Route Recording.
                    else if (_taskManager.TaskCount == 0 && distanceMoved < 2000 && distanceToLeader > 200 && distanceToLeader < 2000)
                    {
                        _core.LogMessage($"PATH DEBUG: Reached task creation block - DistanceMoved: {distanceMoved:F1}, Distance: {distanceToLeader:F1}");
                        // Validate followTarget position before creating tasks
                        if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                        {
                            _core.LogMessage($"ROUTE RECORDING: Creating initial waypoint to leader - Distance: {distanceToLeader:F1}");
                            
                            if (distanceToLeader > _core.Settings.autoPilotDashDistance && _core.Settings.autoPilotDashEnabled)
                            {
                                var shouldSkipDashTasks = _taskManager.Tasks.Any(t =>
                                    t.Type == TaskNodeType.Transition ||
                                    t.Type == TaskNodeType.TeleportConfirm ||
                                    t.Type == TaskNodeType.TeleportButton ||
                                    t.Type == TaskNodeType.Dash);

                                if (shouldSkipDashTasks || AutoPilot.IsTeleportInProgress)
                                {
                                    _core.LogMessage($"ZONE TRANSITION: Skipping dash task creation - conflicting tasks active");
                                }
                                else
                                {
                                    _core.LogMessage($"ROUTE RECORDING: Adding Dash task - Distance: {distanceToLeader:F1}");
                                    _taskManager.AddTask(new TaskNode(followTarget.Pos, 0, TaskNodeType.Dash));
                                }
                            }
                            else
                            {
                                _core.LogMessage($"ROUTE RECORDING: Adding Movement task to leader position");
                                _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                            }
                        }
                        else
                        {
                            _core.LogError($"Invalid followTarget position: {followTarget?.Pos}, skipping task creation");
                        }
                    }
                    //We have a path. Check if the last task is far enough away from current one to add a new task node.
                    else if (_taskManager.TaskCount > 0)
                    {
                        // ADDITIONAL NULL CHECK: Ensure followTarget is still valid before extending path
                        if (followTarget != null && followTarget.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                        {
                            var distanceFromLastTask = Vector3.Distance(_taskManager.Tasks.Last().WorldPosition, followTarget.Pos);
                            // Use the configured pathfinding node distance as threshold
                            var waypointThreshold = _core.Settings.autoPilotPathfindingNodeDistance.Value;
                            
                            if (distanceFromLastTask >= waypointThreshold)
                            {
                                _core.LogMessage($"ROUTE RECORDING: Extending path - Leader moved {distanceFromLastTask:F1} units from last waypoint");
                                _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                            }
                        }
                        else
                        {
                            _core.LogMessage("PATH EXTENSION: followTarget became null during path extension, skipping task creation");
                        }
                    }
                }
                else
                {
                    //Clear all tasks except for looting/claim portal (as those only get done when we're within range of leader.
                    if (_taskManager.TaskCount > 0)
                    {
                        for (var i = _taskManager.TaskCount - 1; i >= 0; i--)
                            if (_taskManager.Tasks[i].Type == TaskNodeType.Movement || _taskManager.Tasks[i].Type == TaskNodeType.Transition)
                                _taskManager.RemoveTaskAt(i);
                    }
                    if (_core.Settings.autoPilotCloseFollow.Value)
                    {
                        //Close follow logic. We have no current tasks. Check if we should move towards leader
                        if (distanceToLeader >= _core.Settings.autoPilotPathfindingNodeDistance.Value)
                            _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                    }
                }
            }
            catch (Exception e)
            {
                _core.LogError($"PathPlanner.PlanPath Error: {e}");
            }
        }

        private List<LabelOnGround> GetAllPortals(PartyElementWindow leaderPartyElement, bool forceSearch = false)
        {
            try
            {
                if (leaderPartyElement == null)
                {
                    _core.LogMessage("PORTAL DEBUG: GetAllPortals called with null leaderPartyElement");
                    return new List<LabelOnGround>();
                }

                var portalLabels = PortalManager.GetPortalsUsingEntities();
                _core.LogMessage($"PORTAL DEBUG: GetAllPortals found {portalLabels?.Count ?? 0} portals using entities");

                return portalLabels ?? new List<LabelOnGround>();
            }
            catch (Exception ex)
            {
                _core.LogMessage($"PORTAL DEBUG: Exception in GetAllPortals: {ex.Message}");
                return new List<LabelOnGround>();
            }
        }

        /// <summary>
        /// Validates if a portal is actually a real portal using entity type
        /// </summary>
        private bool IsValidPortal(LabelOnGround portal)
        {
            return PortalManager.IsValidPortal(portal);
        }

        private LabelOnGround GetBestPortalLabel(PartyElementWindow leaderPartyElement, bool forceSearch = false)
        {
            try
            {
                if (leaderPartyElement == null)
                {
                    _core.LogMessage("PORTAL DEBUG: GetBestPortalLabel called with null leaderPartyElement");
                    return null;
                }

                var portalLabels = PortalManager.GetPortalsUsingEntities();
                _core.LogMessage($"PORTAL DEBUG: GetBestPortalLabel found {portalLabels?.Count ?? 0} portals using entities");

                if (portalLabels == null || portalLabels.Count == 0)
                    return null;

                // First, try to find a portal that matches the leader's zone name
                var leaderZone = leaderPartyElement.ZoneName;
                if (!string.IsNullOrEmpty(leaderZone) && !forceSearch)
                {
                    // Try exact match first
                    var exactMatch = portalLabels.FirstOrDefault(p =>
                        p.Label?.Text?.ToLower().Contains(leaderZone.ToLower()) == true);

                    if (exactMatch != null)
                        return exactMatch;

                    // Try partial match (for zones like "The Chamber of Sins Level 1" -> "Chamber of Sins")
                    var partialMatch = portalLabels.FirstOrDefault(p =>
                        leaderZone.ToLower().Contains("chamber of sins") && p.Label?.Text?.ToLower().Contains("chamber of sins") == true);

                    if (partialMatch != null)
                        return partialMatch;
                }

                // If no name match or force search, return the closest portal
                return portalLabels.OrderBy(x => Vector3.Distance(_core.PlayerPosition, x.ItemOnGround.Pos)).FirstOrDefault();
            }
            catch (Exception ex)
            {
                _core.LogMessage($"PORTAL DEBUG: Exception in GetBestPortalLabel: {ex.Message}");
                return null;
            }
        }

        private Vector2 GetTpButton(PartyElementWindow leaderPartyElement)
        {
            try
            {
                if (leaderPartyElement == null)
                    return Vector2.Zero;

                var windowOffset = _core.GameController.Window.GetWindowRectangle().TopLeft;
                var elemCenter = (Vector2) leaderPartyElement?.TpButton?.GetClientRectCache.Center;
                var finalPos = new Vector2(elemCenter.X + windowOffset.X, elemCenter.Y + windowOffset.Y);

                return finalPos;
            }
            catch
            {
                return Vector2.Zero;
            }
        }

        private dynamic GetTpConfirmation()
        {
            try
            {
                var ui = _core.GameController?.IngameState?.IngameUi?.PopUpWindow;

                if (ui.Children[0].Children[0].Children[0].Text.Equals("Are you sure you want to teleport to this player's location?"))
                    return ui.Children[0].Children[0].Children[3].Children[0];

                return null;
            }
            catch
            {
                return null;
            }
        }

        private bool HasConflictingTransitionTasks()
        {
            return _taskManager.Tasks.Any(t =>
                t.Type == TaskNodeType.Transition ||
                t.Type == TaskNodeType.TeleportConfirm ||
                t.Type == TaskNodeType.TeleportButton);
        }
    }
}