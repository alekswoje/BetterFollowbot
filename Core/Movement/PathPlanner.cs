using System;
using System.Collections.Generic;
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
                            _core.LogMessage($"ARENA PORTAL: Creating transition task for portal '{selectedPortalLabel}'");
                            _taskManager.AddTask(new TaskNode(selectedPortal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                            _core.LogMessage($"ARENA PORTAL: Portal transition task created for portal at {selectedPortal.ItemOnGround.Pos}");
                        }
                        else
                        {
                            _core.LogMessage($"ARENA PORTAL: Selected portal '{selectedPortalLabel}' is not special/arena, ignoring");
                        }
                    }
                    else if (leaderDistance > 100) // Fallback: if leader is far but no portals should be taken, still log
                    {
                        _core.LogMessage($"ARENA PORTAL: Leader is {leaderDistance:F1} units away but not far enough for any portal threshold");
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
                    //We have no path, set us to go to leader pos using A* pathfinding.
                    else if (_taskManager.TaskCount == 0 && distanceToLeader > 200)
                    {
                        // Validate followTarget position before creating tasks
                        if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                        {
                            // Check if terrain is loaded before using A* pathfinding
                            if (!_core.Pathfinding.IsTerrainLoaded)
                            {
                                _core.LogMessage($"A* PATH: Terrain not loaded, falling back to direct movement - Distance: {distanceToLeader:F1}");
                                _core.LogMessage($"A* PATH: Player pos: {_core.PlayerPosition}, Leader pos: {followTarget.Pos}");

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
                                        _core.LogMessage($"Adding Dash task (terrain not loaded) - Distance: {distanceToLeader:F1}, Dash enabled: {_core.Settings.autoPilotDashEnabled}");
                                        _taskManager.AddTask(new TaskNode(followTarget.Pos, 0, TaskNodeType.Dash));
                                    }
                                }
                                else
                                {
                                    _core.LogMessage($"Adding Movement task (terrain not loaded) - Distance: {distanceToLeader:F1}, Dash enabled: {_core.Settings.autoPilotDashEnabled}");
                                    _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                }
                            }
                            else
                            {
                                // Use A* pathfinding to create waypoint tasks instead of straight line
                                _core.LogMessage($"A* PATH: Attempting to find path - Player: {_core.PlayerPosition}, Leader: {followTarget.Pos}");
                                var pathWaypoints = _core.Pathfinding.GetPath(_core.PlayerPosition, followTarget.Pos);

                                if (pathWaypoints != null && pathWaypoints.Count > 1) // Need more than just start position
                                {
                                    _core.LogMessage($"A* PATH: Found path with {pathWaypoints.Count} waypoints to leader - following waypoint path");

                                    // Create movement tasks along the A* path waypoints
                                    // Skip the first waypoint (current position) and create tasks for the rest
                                    var gridToWorldMultiplier = 250f / 23f; // Same conversion as in Pathfinding.cs
                                    var waypointsAdded = 0;

                                    // Always follow the A* path - dash to first waypoint if far, then walk the rest
                                    var firstWaypointIndex = 1;

                                    // Check if we should dash to the first waypoint (if distance is large enough)
                                    var shouldDashToFirstWaypoint = distanceToLeader > _core.Settings.autoPilotDashDistance.Value &&
                                                                   _core.Settings.autoPilotDashEnabled.Value &&
                                                                   pathWaypoints.Count > 2;

                                    var conflictingTasksExist = _taskManager.Tasks.Any(t =>
                                        t.Type == TaskNodeType.Transition ||
                                        t.Type == TaskNodeType.TeleportConfirm ||
                                        t.Type == TaskNodeType.TeleportButton ||
                                        t.Type == TaskNodeType.Dash);

                                    if (shouldDashToFirstWaypoint && !conflictingTasksExist && !AutoPilot.IsTeleportInProgress)
                                    {
                                        // Dash to the first waypoint instead of direct to leader
                                        var firstWaypoint = pathWaypoints[1];
                                        var firstWaypointWorld = new Vector3(
                                            firstWaypoint.X * gridToWorldMultiplier,
                                            firstWaypoint.Y * gridToWorldMultiplier,
                                            followTarget.Pos.Z
                                        );
                                        _core.LogMessage($"A* PATH: Large distance ({distanceToLeader:F1} > {_core.Settings.autoPilotDashDistance.Value}) - dashing to first waypoint instead of direct to leader");
                                        _taskManager.AddTask(new TaskNode(firstWaypointWorld, 0, TaskNodeType.Dash));
                                        firstWaypointIndex = 2; // Skip the first waypoint since we're dashing to it
                                    }
                                    else
                                    {
                                        _core.LogMessage($"A* PATH: Following waypoint path (dash={shouldDashToFirstWaypoint}, conflicts={conflictingTasksExist}, teleport={AutoPilot.IsTeleportInProgress})");
                                    }

                                    // For short paths (< 200 units), simplify to just start and end waypoints
                                    if (distanceToLeader < 200)
                                    {
                                        _core.LogMessage($"A* PATH: Short path detected ({distanceToLeader:F1} units) - using simplified 2-waypoint path");

                                        // Just use start and end waypoints for short paths
                                        var startWaypoint = pathWaypoints[0];
                                        var endWaypoint = pathWaypoints[pathWaypoints.Count - 1];

                                        // Add start waypoint (skip if dashing)
                                        if (firstWaypointIndex == 1)
                                        {
                                            var startWorldPos = new Vector3(
                                                startWaypoint.X * gridToWorldMultiplier,
                                                startWaypoint.Y * gridToWorldMultiplier,
                                                followTarget.Pos.Z
                                            );
                                            _core.LogMessage($"A* PATH: Adding start waypoint: grid({startWaypoint.X},{startWaypoint.Y}) -> world({startWorldPos.X:F1},{startWorldPos.Y:F1},{startWorldPos.Z:F1})");
                                            _taskManager.AddTask(new TaskNode(startWorldPos, _core.Settings.autoPilotPathfindingNodeDistance));
                                            waypointsAdded++;
                                        }

                                        // Add end waypoint
                                        var endWorldPos = new Vector3(
                                            endWaypoint.X * gridToWorldMultiplier,
                                            endWaypoint.Y * gridToWorldMultiplier,
                                            followTarget.Pos.Z
                                        );
                                        _core.LogMessage($"A* PATH: Adding end waypoint: grid({endWaypoint.X},{endWaypoint.Y}) -> world({endWorldPos.X:F1},{endWorldPos.Y:F1},{endWorldPos.Z:F1})");
                                        _taskManager.AddTask(new TaskNode(endWorldPos, _core.Settings.autoPilotPathfindingNodeDistance));
                                        waypointsAdded++;
                                    }
                                    else
                                    {
                                        // For longer paths, be very conservative with waypoints
                                        var maxWaypoints = distanceToLeader > 1000 ? 3 : 5; // Very few waypoints for long distances
                                        var waypointsToAdd = Math.Min(maxWaypoints, pathWaypoints.Count - firstWaypointIndex);

                                        for (int w = 0; w < waypointsToAdd; w++)
                                        {
                                            // Space waypoints evenly across the path
                                            var waypointIndex = firstWaypointIndex + (int)((float)w / (waypointsToAdd - 1) * (pathWaypoints.Count - firstWaypointIndex - 1));
                                            var waypoint = pathWaypoints[waypointIndex];

                                            var worldPos = new Vector3(
                                                waypoint.X * gridToWorldMultiplier,
                                                waypoint.Y * gridToWorldMultiplier, // Y is north-south position
                                                followTarget.Pos.Z // Keep same height
                                            );
                                            _core.LogMessage($"A* PATH: Adding waypoint {w+1}/{waypointsToAdd} (index {waypointIndex}/{pathWaypoints.Count}): grid({waypoint.X},{waypoint.Y}) -> world({worldPos.X:F1},{worldPos.Y:F1},{worldPos.Z:F1})");
                                            _taskManager.AddTask(new TaskNode(worldPos, _core.Settings.autoPilotPathfindingNodeDistance));
                                            waypointsAdded++;
                                        }
                                    }

                                    if (waypointsAdded == 0)
                                    {
                                        // If no waypoints were added, fall back to direct movement
                                        _core.LogMessage($"A* PATH: No valid waypoints found, falling back to direct movement");
                                        _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                    }
                                    else
                                    {
                                        _core.LogMessage($"A* PATH: Created {waypointsAdded} movement tasks along path");
                                    }
                                }
                                else
                                {
                                    // A* failed or returned only start position, try direct movement
                                    _core.LogMessage($"A* PATH: Pathfinding failed or returned insufficient waypoints (got {pathWaypoints?.Count ?? 0}), falling back to direct movement - Distance: {distanceToLeader:F1}");

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
                                            _core.LogMessage($"Adding Dash task (A* failed) - Distance: {distanceToLeader:F1}, Dash enabled: {_core.Settings.autoPilotDashEnabled}");
                                            _taskManager.AddTask(new TaskNode(followTarget.Pos, 0, TaskNodeType.Dash));
                                        }
                                    }
                                    else
                                    {
                                        _core.LogMessage($"Adding Movement task (A* failed) - Distance: {distanceToLeader:F1}, Dash enabled: {_core.Settings.autoPilotDashEnabled}");
                                        _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                    }
                                }
                            }
                        }
                        else
                        {
                            _core.LogError($"Invalid followTarget position: {followTarget?.Pos}, skipping task creation");
                        }
                    }
                    // Only extend path when we have very few tasks left (nearly finished current path)
                    else if (_taskManager.TaskCount > 0 && _taskManager.TaskCount <= 1)
                    {
                        // ADDITIONAL NULL CHECK: Ensure followTarget is still valid before extending path
                        if (followTarget != null && followTarget.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                        {
                            var distanceFromLastTask = Vector3.Distance(_taskManager.Tasks.Last().WorldPosition, followTarget.Pos);
                            // Only extend if leader has moved significantly from the end of current path
                            var extensionThreshold = Math.Max(300, _core.Settings.autoPilotPathfindingNodeDistance.Value * 5);
                            if (distanceFromLastTask >= extensionThreshold)
                            {
                                _core.LogMessage($"PATH EXTENSION: Extending path - Tasks left: {_taskManager.TaskCount}, Distance: {distanceFromLastTask:F1}, Threshold: {extensionThreshold:F1}");

                                // Use A* to find additional waypoints from current path end to target
                                var currentPathEnd = _taskManager.Tasks.Last().WorldPosition;

                                // Check if this is a short extension (< 150 units remaining)
                                var extensionDistance = Vector3.Distance(currentPathEnd, followTarget.Pos);
                                if (extensionDistance < 150)
                                {
                                    _core.LogMessage($"A* PATH EXTENSION: Short extension detected ({extensionDistance:F1} units) - using direct extension");
                                    _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                }
                                else if (!_core.Pathfinding.IsTerrainLoaded)
                                {
                                    _core.LogMessage($"A* PATH EXTENSION: Terrain not loaded, falling back to direct extension");
                                    _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                }
                                else
                                {
                                    var extensionWaypoints = _core.Pathfinding.GetPath(currentPathEnd, followTarget.Pos);

                                    if (extensionWaypoints != null && extensionWaypoints.Count > 1)
                                    {
                                        // Convert grid waypoints back to world positions and add as tasks
                                        // Skip the first waypoint (current path end position) and space them out
                                        var gridToWorldMultiplier = 250f / 23f; // Same conversion as in Pathfinding.cs
                                        var waypointsAdded = 0;
                                        var lastWaypointPos = extensionWaypoints[0]; // Current path end
                                        var minDistanceBetweenWaypoints = 8; // Minimum 8 grid units between waypoints for extensions

                                        for (int i = 1; i < extensionWaypoints.Count && waypointsAdded < 2; i++) // Limit to 2 additional waypoints
                                        {
                                            var waypoint = extensionWaypoints[i];
                                            var distanceFromLast = Math.Abs(waypoint.X - lastWaypointPos.X) + Math.Abs(waypoint.Y - lastWaypointPos.Y);

                                            if (distanceFromLast >= minDistanceBetweenWaypoints || i == extensionWaypoints.Count - 1) // Always include final waypoint
                                            {
                                                var worldPos = new Vector3(
                                                    waypoint.X * gridToWorldMultiplier,
                                                    waypoint.Y * gridToWorldMultiplier, // Y is north-south position
                                                    followTarget.Pos.Z // Keep same height
                                                );
                                                _taskManager.AddTask(new TaskNode(worldPos, _core.Settings.autoPilotPathfindingNodeDistance));
                                                waypointsAdded++;
                                                lastWaypointPos = waypoint;
                                            }
                                        }

                                        if (waypointsAdded == 0)
                                        {
                                            _core.LogMessage($"A* PATH EXTENSION: No valid waypoints found, falling back to direct extension");
                                            _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                        }
                                        else
                                        {
                                            _core.LogMessage($"A* PATH EXTENSION: Added {waypointsAdded} additional waypoints");
                                        }
                                    }
                                    else
                                    {
                                        // A* failed or returned insufficient waypoints, fall back to direct extension
                                        _core.LogMessage($"A* PATH EXTENSION: Pathfinding failed (got {extensionWaypoints?.Count ?? 0} waypoints), falling back to direct extension");
                                        _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                    }
                                }
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
                        // Close follow logic. Only create tasks if we have none and leader moved significantly
                        if (distanceToLeader >= _core.Settings.autoPilotPathfindingNodeDistance.Value)
                        {
                            // Check if leader has moved far enough from our last target position to warrant new path
                            var leaderMovedSinceLastTask = lastTargetPosition == Vector3.Zero ||
                                                         Vector3.Distance(lastTargetPosition, followTarget.Pos) > Math.Max(200, _core.Settings.autoPilotPathfindingNodeDistance.Value * 5);

                            if (leaderMovedSinceLastTask)
                            {
                                // Try A* pathfinding first if terrain is loaded
                                if (_core.Pathfinding.IsTerrainLoaded)
                                {
                                    _core.LogMessage($"A* PATH: Close follow - attempting A* pathfinding - Player: {_core.PlayerPosition}, Leader: {followTarget.Pos}");
                                    var pathWaypoints = _core.Pathfinding.GetPath(_core.PlayerPosition, followTarget.Pos);

                                    if (pathWaypoints != null && pathWaypoints.Count > 1)
                                    {
                                        _core.LogMessage($"A* PATH: Close follow found path with {pathWaypoints.Count} waypoints");

                                        // For close follow, use 2 waypoints max (start, end) to reduce micro-movements
                                        var gridToWorld = 250f / 23f;
                                        var totalWaypoints = Math.Min(2, pathWaypoints.Count); // Max 2 waypoints - only start and end

                                        for (int i = 0; i < totalWaypoints; i++)
                                        {
                                            var waypointIndex = (int)((float)i / (totalWaypoints - 1) * (pathWaypoints.Count - 1));
                                            var waypoint = pathWaypoints[waypointIndex];
                                            var worldPos = new Vector3(
                                                waypoint.X * gridToWorld,
                                                waypoint.Y * gridToWorld,
                                                followTarget.Pos.Z
                                            );

                                            _core.LogMessage($"A* PATH: Close follow - adding waypoint {i+1}/{totalWaypoints}: {worldPos}");
                                            _taskManager.AddTask(new TaskNode(worldPos, _core.Settings.autoPilotPathfindingNodeDistance));
                                        }
                                    }
                                    else
                                    {
                                        _core.LogMessage($"A* PATH: Close follow pathfinding failed, using direct movement");
                                        _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                    }
                                }
                                else
                                {
                                    _core.LogMessage($"Close follow: Using direct movement (terrain not loaded)");
                                    _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
                                }
                            }
                            else
                            {
                                _core.LogMessage($"Close follow: Leader hasn't moved significantly ({Vector3.Distance(lastTargetPosition, followTarget.Pos):F1} units), skipping path recalculation");
                            }
                        }
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

                var portalLabels = _core.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                    x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible &&
                    x.ItemOnGround != null &&
                    (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") ||
                     x.ItemOnGround.Metadata.ToLower().Contains("portal") ||
                     x.ItemOnGround.Metadata.ToLower().Contains("transition") ||
                     PortalManager.IsSpecialPortal(x.Label?.Text?.ToLower() ?? "")))
                    .ToList();

                return portalLabels ?? new List<LabelOnGround>();
            }
            catch (Exception ex)
            {
                _core.LogMessage($"PORTAL DEBUG: Exception in GetAllPortals: {ex.Message}");
                return new List<LabelOnGround>();
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
                    (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") ||
                     x.ItemOnGround.Metadata.ToLower().Contains("portal") ||
                     x.ItemOnGround.Metadata.ToLower().Contains("transition") ||
                     PortalManager.IsSpecialPortal(x.Label?.Text?.ToLower() ?? "")))
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
