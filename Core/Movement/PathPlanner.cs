using System;
using System.Linq;
using BetterFollowbotLite.Interfaces;
using BetterFollowbotLite.Core.TaskManagement;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;

namespace BetterFollowbotLite.Core.Movement
{
    public class PathPlanner
    {
        private readonly IFollowbotCore _core;
        private readonly ILeaderDetector _leaderDetector;
        private readonly ITaskManager _taskManager;
        private readonly PortalManager _portalManager;

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
                // GLOBAL TELEPORT PROTECTION: Block ALL task creation and responsiveness during teleport
                if (AutoPilot.IsTeleportInProgress)
                {
                    _core.LogMessage($"TELEPORT: Blocking all task creation - teleport in progress ({_taskManager.TaskCount} tasks)");
                    return; // Exit immediately to prevent any interference
                }

                // PORTAL TRANSITION HANDLING: Actively search for portals during portal transition mode
                // TODO: Add logic to check how close the leader was to this portal before teleporting
                // This would help determine if we should click this portal or if there might be a closer one
                if (_portalManager.IsInPortalTransition)
                {
                    _core.LogMessage($"PORTAL: In portal transition mode - actively searching for portals to follow leader");

                    if (leaderPartyElement != null)
                    {
                        // Force portal search during portal transition
                        var portal = GetBestPortalLabel(leaderPartyElement, forceSearch: true);
                        if (portal != null)
                        {
                            _core.LogMessage($"PORTAL: Found portal '{portal.Label?.Text}' during transition - creating transition task");
                            _taskManager.AddTask(new TaskNode(portal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                            _core.LogMessage($"PORTAL: Portal transition task created for portal at {portal.ItemOnGround.Pos}");
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

                // PORTAL TRANSITION RESET: Clear portal transition mode when bot successfully reaches leader
                if (_portalManager.IsInPortalTransition && followTarget != null)
                {
                    var portalTransitionDistance = Vector3.Distance(_core.PlayerPosition, followTarget.Pos);
                    // If bot is now close to leader after being far away, portal transition was successful
                    if (portalTransitionDistance < 1000) // Increased from 300 to 1000 for portal transitions
                    {
                        _core.LogMessage($"PORTAL: Bot successfully reached leader after portal transition - clearing portal transition mode");
                        _portalManager.SetPortalTransitionMode(false); // Clear portal transition mode to allow normal operation
                    }
                }

                if (!_core.Settings.Enable.Value || !_core.Settings.autoPilotEnabled.Value || _core.LocalPlayer == null || !_core.LocalPlayer.IsAlive ||
                    !_core.GameController.IsForeGroundCache || MenuWindow.IsOpened || _core.GameController.IsLoading || !_core.GameController.InGame)
                {
                    return;
                }

                // COMPREHENSIVE ZONE LOADING PROTECTION: Prevent random movement during zone transitions
                // When loading into a new zone, entity lists might not be fully populated yet
                if (_core.GameController.IsLoading ||
                    _core.GameController.Area.CurrentArea == null ||
                    string.IsNullOrEmpty(_core.GameController.Area.CurrentArea.DisplayName))
                {
                    _core.LogMessage("ZONE LOADING: Blocking all task creation during zone loading to prevent random movement");
                    // Clear any existing tasks to prevent stale movement
                    if (_taskManager.TaskCount > 0)
                    {
                        var clearedTasks = _taskManager.TaskCount;
                        _taskManager.ClearTasks();
                        _core.LogMessage($"ZONE LOADING: Cleared {clearedTasks} tasks during zone loading");
                    }
                    return;
                }

                // PRIORITY: Check for any open teleport confirmation dialogs and handle them immediately
                bool hasTransitionTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);
                if (!hasTransitionTasks)
                {
                    var tpConfirmation = GetTpConfirmation();
                    if (tpConfirmation != null)
                    {
                        _core.LogMessage("TELEPORT: Found open confirmation dialog, handling it immediately");
                        var center = tpConfirmation.GetClientRect().Center;
                        _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                        // Return early to handle this task immediately
                        return;
                    }
                }

                if (followTarget == null && leaderPartyElement != null && !leaderPartyElement.ZoneName.Equals(_core.GameController?.Area.CurrentArea.DisplayName))
                {
                    var currentZone = _core.GameController?.Area.CurrentArea.DisplayName ?? "Unknown";
                    var leaderZone = leaderPartyElement.ZoneName ?? "Unknown";

                    _core.LogMessage($"ZONE TRANSITION: Leader is in different zone - Current: '{currentZone}', Leader: '{leaderZone}'");

                    // Only add transition tasks if we don't already have any pending
                    if (!hasTransitionTasks)
                    {
                        _core.LogMessage("ZONE TRANSITION: No pending transition tasks, searching for portal");
                        var portal = GetBestPortalLabel(leaderPartyElement);
                        if (portal != null)
                        {
                            // Hideout -> Map || Chamber of Sins A7 -> Map
                            _core.LogMessage($"ZONE TRANSITION: Found portal '{portal.Label?.Text}' leading to leader zone '{leaderPartyElement.ZoneName}'");
                            _taskManager.AddTask(new TaskNode(portal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                            _core.LogMessage("ZONE TRANSITION: Portal transition task added to queue");
                        }
                        else
                        {
                            // No matching portal found, use party teleport (blue swirl)
                            _core.LogMessage($"ZONE TRANSITION: No matching portal found for '{leaderPartyElement.ZoneName}', falling back to party teleport");

                            // FIRST: Check if teleport confirmation dialog is already open (handle it immediately)
                            var tpConfirmation = GetTpConfirmation();
                            if (tpConfirmation != null)
                            {
                                _core.LogMessage("ZONE TRANSITION: Teleport confirmation dialog already open, handling it");
                                var center = tpConfirmation.GetClientRect().Center;
                                _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                            }
                            else
                            {
                                // Try to click the teleport button
                                var tpButton = GetTpButton(leaderPartyElement);
                                if (!tpButton.Equals(Vector2.Zero))
                                {
                                    _core.LogMessage("ZONE TRANSITION: Clicking teleport button to initiate party teleport");
                                    // SET GLOBAL FLAG: Prevent SMITE and other skills from interfering
                                    AutoPilot.IsTeleportInProgress = true;
                                    _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                                }
                                else
                                {
                                    _core.LogMessage("ZONE TRANSITION: No teleport button available, cannot follow through transition");
                                }
                            }
                        }
                    }
                }

                // TODO: If in town, do not follow (optional)
                var distanceToLeader = Vector3.Distance(_core.PlayerPosition, followTarget.Pos);
                //We are NOT within clear path distance range of leader. Logic can continue
                if (distanceToLeader >= _core.Settings.autoPilotClearPathDistance.Value)
                {
                    // IMPORTANT: Don't process large movements if we already have any transition-related task active
                    // This prevents zone transition detection from interfering with active transitions/teleports
                    if (_taskManager.Tasks.Any(t =>
                        t.Type == TaskNodeType.Transition ||
                        t.Type == TaskNodeType.TeleportConfirm ||
                        t.Type == TaskNodeType.TeleportButton))
                    {
                        _core.LogMessage("ZONE TRANSITION: Transition/teleport task already active, skipping movement processing");
                        return; // Exit early to prevent interference
                    }

                    //Leader moved VERY far in one frame. Check for transition to use to follow them.
                    var distanceMoved = Vector3.Distance(lastTargetPosition, followTarget.Pos);
                    if (lastTargetPosition != Vector3.Zero && distanceMoved > _core.Settings.autoPilotClearPathDistance.Value)
                    {
                        // Check if this is likely a zone transition (moved extremely far)
                        var isLikelyZoneTransition = distanceMoved > 1000; // Very large distance suggests zone transition

                        if (isLikelyZoneTransition)
                        {
                            _core.LogMessage($"ZONE TRANSITION DETECTED: Leader moved {distanceMoved:F1} units, likely zone transition");

                            // First check if zone names are different (immediate detection)
                            var zonesAreDifferent = leaderPartyElement != null && !leaderPartyElement.ZoneName.Equals(_core.GameController?.Area.CurrentArea.DisplayName);

                            if (zonesAreDifferent)
                            {
                                _core.LogMessage($"ZONE TRANSITION: Confirmed different zones - Current: '{_core.GameController?.Area.CurrentArea.DisplayName}', Leader: '{leaderPartyElement?.ZoneName}'");
                            }
                            else
                            {
                                _core.LogMessage($"ZONE TRANSITION: Zone names same but large distance, assuming transition anyway");
                            }

                            // Look for portals regardless of zone name confirmation - force portal search
                            var transition = GetBestPortalLabel(leaderPartyElement, forceSearch: true);

                            // If no portal matched by name, try to find the closest portal (likely the one the leader used)
                            if (transition == null)
                            {
                                _core.LogMessage("ZONE TRANSITION: No portal matched by name, looking for closest portal");

                                // Get all portal labels again and find the closest one
                                var allPortals = _core.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                                    x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible &&
                                    x.ItemOnGround != null &&
                                    (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal")))
                                    .OrderBy(x => Vector3.Distance(_core.PlayerPosition, x.ItemOnGround.Pos))
                                    .ToList();

                                if (allPortals != null && allPortals.Count > 0)
                                {
                                    // First, check if there's a special portal (Arena or Warden's Quarters) - give them priority
                                    var specialPortal = allPortals.FirstOrDefault(p =>
                                        PortalManager.IsSpecialPortal(p.Label?.Text ?? ""));
                                    LabelOnGround selectedPortal;

                                    if (specialPortal != null)
                                    {
                                        var portalType = PortalManager.GetSpecialPortalType(specialPortal.Label?.Text ?? "");
                                        var portalDistance = Vector3.Distance(_core.PlayerPosition, specialPortal.ItemOnGround.Pos);
                                        _core.LogMessage($"ZONE TRANSITION: Found {portalType} portal at distance {portalDistance:F1}");

                                        if (portalDistance < 200)
                                        {
                                            _core.LogMessage($"ZONE TRANSITION: Using {portalType} portal as likely destination");
                                            selectedPortal = specialPortal;
                                        }
                                        else
                                        {
                                            // Special portal too far, fall back to closest
                                            selectedPortal = allPortals.First();
                                            _core.LogMessage($"ZONE TRANSITION: {portalType} portal too far, using closest instead");
                                        }
                                    }
                                    else
                                    {
                                        selectedPortal = allPortals.First();
                                    }

                                    var selectedDistance = Vector3.Distance(_core.PlayerPosition, selectedPortal.ItemOnGround.Pos);

                                    _core.LogMessage($"ZONE TRANSITION: Selected portal '{selectedPortal.Label?.Text}' at distance {selectedDistance:F1}");

                                    // If the selected portal is reasonably close (within 800 units), it's likely the one the leader used
                                    // Increased from 500 to 800 to handle cases where leader transitions quickly
                                    if (selectedDistance < 800)
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Using selected portal '{selectedPortal.Label?.Text}' as likely destination");
                                        transition = selectedPortal; // Set transition so we use this portal

                                        // CRITICAL: Clear all existing tasks when adding a transition task to ensure it executes immediately
                                        _core.LogMessage("ZONE TRANSITION: Clearing all existing tasks to prioritize transition");
                                        _taskManager.ClearTasks();

                                        // Add the transition task immediately since we found a suitable portal
                                        _taskManager.AddTask(new TaskNode(selectedPortal, 200, TaskNodeType.Transition));
                                        _core.LogMessage("ZONE TRANSITION: Transition task added and prioritized");
                                    }
                                    else
                                    {
                                        _core.LogMessage($"ZONE TRANSITION: Selected portal too far ({selectedDistance:F1}), using party teleport");
                                    }
                                }
                            }

                            // Check for Portal within Screen Distance (original logic) - only if we haven't already added a task
                            if (transition != null && transition.ItemOnGround.DistancePlayer < 80)
                            {
                                // Since we cleared all tasks above when adding the transition task, this check is now simpler
                                // We only add if we don't have any transition tasks (which we shouldn't after clearing)
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
                                _core.LogMessage($"ZONE TRANSITION: No suitable portal found for zone transition");

                                // If no portal found but this looks like a zone transition, try party teleport as fallback
                                if (zonesAreDifferent || distanceMoved > 1500) // Even more aggressive for very large distances
                                {
                                    _core.LogMessage($"ZONE TRANSITION: No portal found, trying party teleport fallback");

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
                                        // Try to click the teleport button
                                        var tpButton = GetTpButton(leaderPartyElement);
                                        if (!tpButton.Equals(Vector2.Zero))
                                        {
                                            _core.LogMessage("ZONE TRANSITION: Clicking teleport button to initiate party teleport");
                                            // SET GLOBAL FLAG: Prevent SMITE and other skills from interfering
                                            AutoPilot.IsTeleportInProgress = true;
                                            _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                                        }
                                        else
                                        {
                                            _core.LogMessage("ZONE TRANSITION: No teleport button available, cannot follow through transition");
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
                    //We have no path, set us to go to leader pos.
                    else if (_taskManager.TaskCount == 0 && distanceMoved < 2000 && distanceToLeader > 200 && distanceToLeader < 2000)
                    {
                        // Validate followTarget position before creating tasks
                        if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                        {
                            // If very far away, add dash task instead of movement task
                            if (distanceToLeader > 3000 && _core.Settings.autoPilotDashEnabled) // Increased from 1500 to 3000 to reduce dash spam significantly
                            {
                                // CRITICAL: Don't add dash tasks if we have any active transition-related task OR another dash task OR teleport in progress
                                var shouldSkipDashTasks = _taskManager.Tasks.Any(t =>
                                    t.Type == TaskNodeType.Transition ||
                                    t.Type == TaskNodeType.TeleportConfirm ||
                                    t.Type == TaskNodeType.TeleportButton ||
                                    t.Type == TaskNodeType.Dash);

                                if (shouldSkipDashTasks || AutoPilot.IsTeleportInProgress)
                                {
                                    _core.LogMessage($"ZONE TRANSITION: Skipping dash task creation - conflicting tasks active ({_taskManager.CountTasks(t => t.Type == TaskNodeType.Dash)} dash tasks, {_taskManager.CountTasks(t => t.Type == TaskNodeType.Transition)} transition tasks, teleport={AutoPilot.IsTeleportInProgress})");
                                }
                                else
                                {
                                    _core.LogMessage($"Adding Dash task - Distance: {distanceToLeader:F1}, Dash enabled: {_core.Settings.autoPilotDashEnabled}");
                                    _taskManager.AddTask(new TaskNode(followTarget.Pos, 0, TaskNodeType.Dash));
                                }
                            }
                            else
                            {
                                _core.LogMessage($"Adding Movement task - Distance: {distanceToLeader:F1}, Dash enabled: {_core.Settings.autoPilotDashEnabled}, Dash threshold: 700");
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
                            // More responsive: reduce threshold by half for more frequent path updates
                            var responsiveThreshold = _core.Settings.autoPilotPathfindingNodeDistance.Value / 2;
                            if (distanceFromLastTask >= responsiveThreshold)
                            {
                                _core.LogMessage($"RESPONSIVENESS: Adding new path node - Distance: {distanceFromLastTask:F1}, Threshold: {responsiveThreshold:F1}");
                                _core.LogMessage($"DEBUG: Creating task to position: {followTarget.Pos} (Player at: {_core.PlayerPosition})");
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

        private LabelOnGround GetBestPortalLabel(PartyElementWindow leaderPartyElement, bool forceSearch = false)
        {
            try
            {
                if (leaderPartyElement == null)
                {
                    _core.LogMessage("PORTAL DEBUG: GetBestPortalLabel called with null leaderPartyElement");
                    return null;
                }

                var portalLabels = _core.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                    x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible &&
                    x.ItemOnGround != null &&
                    (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal")))
                    .ToList();

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
    }
}
