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

        public PathPlanner(IFollowbotCore core, ILeaderDetector leaderDetector, ITaskManager taskManager, PortalManager portalManager)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _leaderDetector = leaderDetector ?? throw new ArgumentNullException(nameof(leaderDetector));
            _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            _portalManager = portalManager ?? throw new ArgumentNullException(nameof(portalManager));
        }

        /// <summary>
        /// Plans and creates movement tasks based on leader position and current game state
        /// </summary>
        public void PlanPath(Entity followTarget, PartyElementWindow leaderPartyElement, Vector3 lastTargetPosition, Vector3 lastPlayerPosition)
        {
            try
            {
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

                // Check for nearby ascendancy trial plaques and auto-click them (ONCE per plaque)
                CheckAndClickNearbyPlaques();

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
                        _core.LogMessage("ZONE TRANSITION: Leader in different zone, prioritizing party teleport over portals");

                        var tpConfirmation = GetTpConfirmation();
                        if (tpConfirmation != null)
                        {
                            _core.LogMessage("ZONE TRANSITION: Teleport confirmation dialog already open, handling it");
                            var center = tpConfirmation.GetClientRect().Center;
                            _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                        }
                        else
                        {
                            // SPECIAL CASE: Labyrinth areas don't support party TP - always use portals
                            // EXCEPTION: Aspirants' Plaza allows party TP as fallback
                            if (PortalManager.IsInLabyrinthArea && !(_core.GameController?.Area?.CurrentArea?.DisplayName?.Contains("Aspirants' Plaza") ?? false))
                            {
                                _core.LogMessage("ZONE TRANSITION: In labyrinth area, skipping party TP and using portal search");
                                var portal = GetBestPortalLabel(leaderPartyElement);
                                if (portal != null)
                                {
                                    // Only create transition task if there isn't already one pending
                                    var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                                    if (!hasExistingTransitionTask)
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Found portal '{portal.Label?.Text}' for labyrinth navigation");
                                        _taskManager.AddTask(new TaskNode(portal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                        _core.LogMessage("ZONE TRANSITION: Labyrinth portal transition task added to queue");
                                    }
                                    else
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Transition task already exists, skipping labyrinth portal creation for '{portal.Label?.Text}'");
                                    }
                                }
                                else
                                {
                                    _core.LogMessage("ZONE TRANSITION: No portals found in labyrinth area, cannot follow through transition");
                                }
                            }
                            // SPECIAL CASE: Special areas like Maligaro's Sanctum don't support party TP - use matching portals
                            else if (PortalManager.IsSpecialArea(leaderPartyElement.ZoneName))
                            {
                                _core.LogMessage($"ZONE TRANSITION: Leader in special area '{leaderPartyElement.ZoneName}' - using matching portal search instead of party TP");
                                var matchingPortal = PortalManager.FindMatchingPortal(leaderPartyElement.ZoneName, preferHideoutPortals: true);
                                if (matchingPortal != null)
                                {
                                    // Only create transition task if there isn't already one pending
                                    var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                                    if (!hasExistingTransitionTask)
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Found matching portal '{matchingPortal.Label?.Text}' for special area '{leaderPartyElement.ZoneName}'");
                                        _taskManager.AddTask(new TaskNode(matchingPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                        _core.LogMessage("ZONE TRANSITION: Special area portal transition task added to queue");
                                    }
                                    else
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Transition task already exists, skipping special area portal creation for '{matchingPortal.Label?.Text}'");
                                    }
                                }
                                else
                                {
                                    _core.LogMessage($"ZONE TRANSITION: No matching portals found for special area '{leaderPartyElement.ZoneName}'");
                                }
                            }
                            else
                            {
                                // FIRST: Check for portals that match the leader's area name
                                _core.LogMessage("ZONE TRANSITION: Checking for portals matching leader's area name first");
                                var matchingPortal = PortalManager.FindMatchingPortal(leaderPartyElement.ZoneName, preferHideoutPortals: true);
                                if (matchingPortal != null)
                                {
                                    // Only create transition task if there isn't already one pending
                                    var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                                    if (!hasExistingTransitionTask)
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Found matching portal '{matchingPortal.Label?.Text}' for leader area '{leaderPartyElement.ZoneName}'");
                                        _taskManager.AddTask(new TaskNode(matchingPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                        _core.LogMessage("ZONE TRANSITION: Matching portal transition task added to queue");
                                    }
                                    else
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Transition task already exists, skipping matching portal creation for '{matchingPortal.Label?.Text}'");
                                    }
                                }
                                else
                                {
                                    _core.LogMessage("ZONE TRANSITION: No matching portals found, falling back to party teleport button");
                                    var tpButton = GetTpButton(leaderPartyElement);
                                    if (!tpButton.Equals(Vector2.Zero))
                                    {
                                        _core.LogMessage("ZONE TRANSITION: Using party teleport button (blue swirly icon) for zone transition");
                                        AutoPilot.IsTeleportInProgress = true;
                                        _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                                    }
                                    else
                                    {
                                        _core.LogMessage("ZONE TRANSITION: Party teleport button not available, falling back to general portal search");
                                        var portal = GetBestPortalLabel(leaderPartyElement);
                                        if (portal != null)
                                        {
                                            // Only create transition task if there isn't already one pending
                                            var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                                            if (!hasExistingTransitionTask)
                                            {
                                                _core.LogMessage($"ZONE TRANSITION: Found portal '{portal.Label?.Text}' leading to leader zone '{leaderPartyElement.ZoneName}'");
                                                _taskManager.AddTask(new TaskNode(portal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                                _core.LogMessage("ZONE TRANSITION: Portal transition task added to queue");
                                            }
                                            else
                                            {
                                                _core.LogMessage($"ZONE TRANSITION: Transition task already exists, skipping portal creation for '{portal.Label?.Text}'");
                                            }
                                        }
                                        else
                                        {
                                            _core.LogMessage("ZONE TRANSITION: No teleport button or portal available, cannot follow through transition");
                                        }
                                    }
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

                            // Prioritize party teleport over portal clicking (but not in labyrinth areas or special areas)
                            if (!HasConflictingTransitionTasks())
                            {
                                // SPECIAL CASE: Labyrinth areas don't support party TP
                                // EXCEPTION: Aspirants' Plaza allows party TP as fallback
                                if (PortalManager.IsInLabyrinthArea && !(_core.GameController?.Area?.CurrentArea?.DisplayName?.Contains("Aspirants' Plaza") ?? false))
                                {
                                    _core.LogMessage("ZONE TRANSITION: In labyrinth area, party TP not available - this is normal");
                                }
                                // SPECIAL CASE: Special areas like Maligaro's Sanctum don't support party TP
                                else if (PortalManager.IsSpecialArea(leaderPartyElement.ZoneName))
                                {
                                    _core.LogMessage($"ZONE TRANSITION: Leader in special area '{leaderPartyElement.ZoneName}' - party TP not available, using matching portal search");
                                    var matchingPortal = PortalManager.FindMatchingPortal(leaderPartyElement.ZoneName, preferHideoutPortals: true);
                                    if (matchingPortal != null)
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Found matching portal '{matchingPortal.Label?.Text}' for special area '{leaderPartyElement.ZoneName}'");
                                        _taskManager.AddTask(new TaskNode(matchingPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                        _core.LogMessage("ZONE TRANSITION: Special area portal transition task added to queue");
                                    }
                                    else
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: No matching portals found for special area '{leaderPartyElement.ZoneName}'");
                                    }
                                }
                                else
                                {
                                    // FIRST: Check for portals that match the leader's area name
                                    _core.LogMessage("ZONE TRANSITION: Checking for portals matching leader's area name first (null entity case)");
                                    var matchingPortal = PortalManager.FindMatchingPortal(leaderPartyElement.ZoneName, preferHideoutPortals: true);
                                    if (matchingPortal != null)
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Found matching portal '{matchingPortal.Label?.Text}' for leader area '{leaderPartyElement.ZoneName}' (null entity case)");
                                        _taskManager.AddTask(new TaskNode(matchingPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                        _core.LogMessage("ZONE TRANSITION: Matching portal transition task added to queue (null entity case)");
                                    }
                                    else
                                    {
                                        _core.LogMessage("ZONE TRANSITION: No matching portals found, falling back to party teleport button (null entity case)");
                                        var tpButton = GetTpButton(leaderPartyElement);
                                        if (!tpButton.Equals(Vector2.Zero))
                                        {
                                            _core.LogMessage("ZONE TRANSITION: Using party teleport button (blue swirly icon) for zone transition");
                                            AutoPilot.IsTeleportInProgress = true;
                                            _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                                            _core.LogMessage("ZONE TRANSITION: Party teleport task added to queue");
                                        }
                                        else
                                        {
                                            _core.LogMessage("ZONE TRANSITION: Party teleport button not available for null entity transition");
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
                            else
                            {
                                // SPECIAL CASE: Labyrinth areas don't support party TP - use portals instead
                                // EXCEPTION: Aspirants' Plaza allows party TP as fallback
                                if (PortalManager.IsInLabyrinthArea && !(_core.GameController?.Area?.CurrentArea?.DisplayName?.Contains("Aspirants' Plaza") ?? false))
                                {
                                    _core.LogMessage("ZONE TRANSITION: In labyrinth area, using portal search instead of party TP");
                                    var portal = GetBestPortalLabel(leaderPartyElement);
                                    if (portal != null)
                                    {
                                        // Only create transition task if there isn't already one pending
                                        var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                                        if (!hasExistingTransitionTask)
                                        {
                                            _core.LogMessage($"ZONE TRANSITION: Found portal '{portal.Label?.Text}' for labyrinth navigation after large movement");
                                            _taskManager.AddTask(new TaskNode(portal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                            _core.LogMessage("ZONE TRANSITION: Labyrinth portal transition task added to queue");
                                        }
                                        else
                                        {
                                            _core.LogMessage($"ZONE TRANSITION: Transition task already exists, skipping labyrinth portal creation for '{portal.Label?.Text}'");
                                        }
                                    }
                                    else
                                    {
                                        _core.LogMessage("ZONE TRANSITION: No portals found in labyrinth area after large movement");
                                    }
                                }
                                // SPECIAL CASE: Special areas like Maligaro's Sanctum don't support party TP - use matching portals
                                else if (leaderPartyElement != null && PortalManager.IsSpecialArea(leaderPartyElement.ZoneName))
                                {
                                    _core.LogMessage($"ZONE TRANSITION: Leader in special area '{leaderPartyElement.ZoneName}' - using matching portal search instead of party TP");
                                    var matchingPortal = PortalManager.FindMatchingPortal(leaderPartyElement.ZoneName, preferHideoutPortals: true);
                                    if (matchingPortal != null)
                                    {
                                        // Only create transition task if there isn't already one pending
                                        var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                                        if (!hasExistingTransitionTask)
                                        {
                                            _core.LogMessage($"ZONE TRANSITION: Found matching portal '{matchingPortal.Label?.Text}' for special area '{leaderPartyElement.ZoneName}'");
                                            _taskManager.AddTask(new TaskNode(matchingPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                            _core.LogMessage("ZONE TRANSITION: Special area portal transition task added to queue");
                                        }
                                        else
                                        {
                                            _core.LogMessage($"ZONE TRANSITION: Transition task already exists, skipping special area portal creation for '{matchingPortal.Label?.Text}'");
                                        }
                                    }
                                    else
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: No matching portals found for special area '{leaderPartyElement.ZoneName}'");
                                    }
                                }
                                else
                                {
                                    // FIRST: Check for portals that match the leader's area name (endgame maps, etc.)
                                    _core.LogMessage("ZONE TRANSITION: Checking for portals matching leader's area name first (large movement case)");
                                    var matchingPortal = PortalManager.FindMatchingPortal(leaderPartyElement.ZoneName, preferHideoutPortals: true);
                                    if (matchingPortal != null)
                                    {
                                        // Only create transition task if there isn't already one pending
                                        var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                                        if (!hasExistingTransitionTask)
                                        {
                                            _core.LogMessage($"ZONE TRANSITION: Found matching portal '{matchingPortal.Label?.Text}' for leader area '{leaderPartyElement.ZoneName}' (large movement)");
                                            _taskManager.AddTask(new TaskNode(matchingPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                            _core.LogMessage("ZONE TRANSITION: Matching portal transition task added to queue (large movement)");
                                        }
                                        else
                                        {
                                            _core.LogMessage($"ZONE TRANSITION: Transition task already exists, skipping matching portal creation for '{matchingPortal.Label?.Text}'");
                                        }
                                    }
                                    else
                                    {
                                        _core.LogMessage("ZONE TRANSITION: No matching portals found, falling back to party teleport button");
                                        // SECOND: Try party teleport button if no matching portal
                                        var tpButton = GetTpButton(leaderPartyElement);
                                        if (!tpButton.Equals(Vector2.Zero))
                                        {
                                            _core.LogMessage("ZONE TRANSITION: Using party teleport button (blue swirly icon) for zone transition");
                                            // SET GLOBAL FLAG: Prevent SMITE and other skills from interfering
                                            AutoPilot.IsTeleportInProgress = true;
                                            _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                                        }
                                        else
                                        {
                                            _core.LogMessage("ZONE TRANSITION: Party teleport button not available, checking for portals as fallback");

                                            var transition = GetBestPortalLabel(leaderPartyElement, forceSearch: true);

                                            if (transition == null)
                                            {
                                                _core.LogMessage("ZONE TRANSITION: No portal matched by name, looking for closest portal");

                                                var allPortals = _core.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                                                    x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible &&
                                                    x.ItemOnGround != null &&
                                                    (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal")))
                                                    .OrderBy(x => Vector3.Distance(_core.PlayerPosition, x.ItemOnGround.Pos))
                                                    .ToList();

                                                if (allPortals != null && allPortals.Count > 0)
                                                {
                                                    var selectedPortal = allPortals.First();
                                                    var selectedDistance = Vector3.Distance(_core.PlayerPosition, selectedPortal.ItemOnGround.Pos);

                                                    _core.LogMessage($"ZONE TRANSITION: Found portal '{selectedPortal.Label?.Text}' at distance {selectedDistance:F1}");
                                                    if (selectedDistance < 200)
                                                    {
                                                        _core.LogMessage($"ZONE TRANSITION: Using portal '{selectedPortal.Label?.Text}' as fallback");
                                                        transition = selectedPortal; // Set transition so we use this portal

                                                        // Add the transition task since party teleport failed
                                                        // Only create transition task if there isn't already one pending
                                                        var hasExistingTransitionTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition);
                                                        if (!hasExistingTransitionTask)
                                                        {
                                                            _taskManager.AddTask(new TaskNode(selectedPortal, 200, TaskNodeType.Transition));
                                                            _core.LogMessage("ZONE TRANSITION: Portal transition task added as fallback");
                                                        }
                                                        else
                                                        {
                                                            _core.LogMessage("ZONE TRANSITION: Transition task already exists, skipping fallback portal creation");
                                                        }
                                                    }
                                                    else
                                                    {
                                                        _core.LogMessage($"ZONE TRANSITION: Portal too far ({selectedDistance:F1}), no transition method available");
                                                    }
                                                }
                                            }

                                            // Check for Portal within Screen Distance (original logic) - only if we haven't already added a task
                                            if (transition != null && transition.ItemOnGround.DistancePlayer < 80)
                                            {
                                                if (!_taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition))
                                                {
                                                    _core.LogMessage($"ZONE TRANSITION: Found nearby portal '{transition.Label?.Text}', adding transition task");
                                                    _taskManager.AddTask(new TaskNode(transition, 200, TaskNodeType.Transition));
                                                }
                                                else
                                                {
                                                    _core.LogMessage($"ZONE TRANSITION: Transition task already exists, portal handling already in progress");
                                                }
                                            }
                                            else
                                            {
                                                _core.LogMessage("ZONE TRANSITION: No party teleport button or suitable portal found, cannot follow through transition");
                                            }
                                        }
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

        /// <summary>
        /// Checks for nearby ascendancy trial plaques and creates a task to click them once
        /// </summary>
        private void CheckAndClickNearbyPlaques()
        {
            try
            {
                // Only check if autopilot is enabled
                if (!_core.Settings.Enable.Value || !_core.Settings.autoPilotEnabled.Value)
                    return;

                // Don't check for plaques if we have transition tasks (priority to portals/teleports)
                if (HasConflictingTransitionTasks())
                    return;

                // Find all IngameIcon entities with the trial plaque metadata
                var plaques = _core.GameController?.EntityListWrapper?.ValidEntitiesByType[EntityType.IngameIcon]?
                    .Where(x =>
                    {
                        if (x == null || !x.IsValid) return false;
                        
                        var metadata = x.Metadata?.ToLower() ?? "";
                        return metadata.Contains("labyrinthtrialplaque");
                    })
                    .ToList();

                if (plaques == null || plaques.Count == 0)
                    return;

                // Get all labels on ground to match with plaque entities
                var allLabels = _core.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels?.ToList();
                if (allLabels == null)
                    return;

                // Check each plaque
                foreach (var plaque in plaques)
                {
                    try
                    {
                        var distance = plaque.DistancePlayer;
                        var entityAddress = plaque.Address;

                        // Only process plaques within 100 units
                        if (distance > 100)
                            continue;

                        // Skip if we've already clicked this plaque
                        if (BetterFollowbot.Instance.autoPilot.HasClickedPlaque(entityAddress))
                            continue;

                        // Skip if we already have a plaque click task pending
                        var hasPendingPlaqueTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.ClickPlaque);
                        if (hasPendingPlaqueTask)
                            continue;

                        // Find the label associated with this plaque entity
                        var plaqueLabel = allLabels.FirstOrDefault(label =>
                            label?.ItemOnGround != null &&
                            label.ItemOnGround.Address == entityAddress);

                        if (plaqueLabel == null)
                        {
                            _core.LogMessage($"PLAQUE: Found plaque entity but no matching label at distance {distance:F1}");
                            continue;
                        }

                        // Use the label for the task (just like portals) - must be within 20 units to click
                        var plaqueTask = new TaskNode(plaqueLabel, 20, TaskNodeType.ClickPlaque)
                        {
                            Data = entityAddress // Store the entity address so we can mark it as clicked
                        };

                        _taskManager.AddTask(plaqueTask);
                        var labelText = plaqueLabel.Label?.Text ?? "Unknown";
                        _core.LogMessage($"PLAQUE: Found trial plaque '{labelText}' at distance {distance:F1}, creating click task");
                        
                        // Only create one task at a time
                        break;
                    }
                    catch (Exception ex)
                    {
                        _core.LogMessage($"PLAQUE: Error processing plaque: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _core.LogMessage($"PLAQUE: Error in CheckAndClickNearbyPlaques: {ex.Message}");
            }
        }
    }
}