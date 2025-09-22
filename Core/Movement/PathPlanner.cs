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
                if (AutoPilot.IsTeleportInProgress)
                {
                    _core.LogMessage($"TELEPORT: Blocking all task creation - teleport in progress ({_taskManager.TaskCount} tasks)");
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

                if (_portalManager.IsInPortalTransition && followTarget != null)
                {
                    var portalTransitionDistance = Vector3.Distance(_core.PlayerPosition, followTarget.Pos);
                    if (portalTransitionDistance < 1000)
                    {
                        _core.LogMessage($"PORTAL: Bot successfully reached leader after portal transition - clearing portal transition mode");
                        _portalManager.SetPortalTransitionMode(false);
                    }
                }

                if (!_core.Settings.Enable.Value || !_core.Settings.autoPilotEnabled.Value || _core.LocalPlayer == null || !_core.LocalPlayer.IsAlive ||
                    !_core.GameController.IsForeGroundCache || MenuWindow.IsOpened || _core.GameController.IsLoading || !_core.GameController.InGame)
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

                if (followTarget == null && leaderPartyElement != null && !leaderPartyElement.ZoneName.Equals(_core.GameController?.Area.CurrentArea.DisplayName))
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
                            var tpButton = GetTpButton(leaderPartyElement);
                            if (!tpButton.Equals(Vector2.Zero))
                            {
                                _core.LogMessage("ZONE TRANSITION: Using party teleport button (blue swirly icon) for zone transition");
                                AutoPilot.IsTeleportInProgress = true;
                                _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                            }
                            else
                            {
                                _core.LogMessage("ZONE TRANSITION: Party teleport button not available, falling back to portal search");
                                var portal = GetBestPortalLabel(leaderPartyElement);
                                if (portal != null)
                                {
                                    _core.LogMessage($"ZONE TRANSITION: Found portal '{portal.Label?.Text}' leading to leader zone '{leaderPartyElement.ZoneName}'");
                                    _taskManager.AddTask(new TaskNode(portal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                                    _core.LogMessage("ZONE TRANSITION: Portal transition task added to queue");
                                }
                                else
                                {
                                    _core.LogMessage("ZONE TRANSITION: No teleport button or portal available, cannot follow through transition");
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
                        var zonesAreDifferent = !leaderPartyElement.ZoneName.Equals(_core.GameController.Area.CurrentArea.DisplayName);
                        if (zonesAreDifferent)
                        {
                            _core.LogMessage($"ZONE TRANSITION: Leader in different zone via party element - Current: '{_core.GameController.Area.CurrentArea.DisplayName}', Leader: '{leaderPartyElement.ZoneName}'");

                            // Prioritize party teleport over portal clicking
                            if (!HasConflictingTransitionTasks())
                            {
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
                    return; // Can't do normal path planning without followTarget
                }

                var distanceToLeader = Vector3.Distance(_core.PlayerPosition, followTarget.Pos);
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

                    var distanceMoved = Vector3.Distance(lastTargetPosition, followTarget.Pos);
                    if (lastTargetPosition != Vector3.Zero && distanceMoved > _core.Settings.autoPilotClearPathDistance.Value)
                    {
                        var isLikelyZoneTransition = distanceMoved > 1000;

                        if (isLikelyZoneTransition)
                        {
                            _core.LogMessage($"ZONE TRANSITION DETECTED: Leader moved {distanceMoved:F1} units, likely zone transition");

                            var zonesAreDifferent = leaderPartyElement != null && !leaderPartyElement.ZoneName.Equals(_core.GameController?.Area.CurrentArea.DisplayName);

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
                                // Try to click the teleport button - this is the preferred method for following party members
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
                                                _taskManager.AddTask(new TaskNode(selectedPortal, 200, TaskNodeType.Transition));
                                                _core.LogMessage("ZONE TRANSITION: Portal transition task added as fallback");
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
                            if (distanceToLeader > _core.Settings.autoPilotDashDistance && _core.Settings.autoPilotDashEnabled)
                            {
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
                                _core.LogMessage($"Adding Movement task - Distance: {distanceToLeader:F1}, Dash enabled: {_core.Settings.autoPilotDashEnabled}");
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

        private bool HasConflictingTransitionTasks()
        {
            return _taskManager.Tasks.Any(t =>
                t.Type == TaskNodeType.Transition ||
                t.Type == TaskNodeType.TeleportConfirm ||
                t.Type == TaskNodeType.TeleportButton);
        }
    }
}
