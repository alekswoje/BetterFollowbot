using System;
using System.Collections;
using System.Linq;
using BetterFollowbotLite.Core.TaskManagement;
using BetterFollowbotLite.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using SharpDX;

namespace BetterFollowbotLite.Core.Movement
{
    /// <summary>
    /// Handles movement-related decision making and coordination logic
    /// </summary>
    public class MovementLogic
    {
        private readonly IFollowbotCore _core;
        private readonly ILeaderDetector _leaderDetector;
        private readonly ITaskManager _taskManager;
        private readonly IPathfinding _pathfinding;
        private readonly IMovementExecutor _movementExecutor;
        private readonly PortalManager _portalManager;
        private readonly Random _random;

        // State tracking
        private Vector3 _lastPlayerPosition;
        private bool _instantPathOptimization;
        private bool _hasUsedWp;

        public MovementLogic(IFollowbotCore core, ILeaderDetector leaderDetector, ITaskManager taskManager,
            IPathfinding pathfinding, IMovementExecutor movementExecutor, PortalManager portalManager)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _leaderDetector = leaderDetector ?? throw new ArgumentNullException(nameof(leaderDetector));
            _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            _pathfinding = pathfinding ?? throw new ArgumentNullException(nameof(pathfinding));
            _movementExecutor = movementExecutor ?? throw new ArgumentNullException(nameof(movementExecutor));
            _portalManager = portalManager ?? throw new ArgumentNullException(nameof(portalManager));
            _random = new Random();
        }

        /// <summary>
        /// Processes movement logic for a single task
        /// </summary>
        public IEnumerator ProcessMovementTask(TaskNode currentTask)
        {
            // Check if path should be cleared for responsiveness
            if (ShouldClearPathForResponsiveness())
            {
                _instantPathOptimization = true;
                _taskManager.ClearTasksPreservingTransitions();
                _hasUsedWp = false;

                // Force immediate path creation
                if (TryCreateInstantPath())
                {
                    yield return null;
                    yield break; // Skip current task processing
                }
            }

            // Check if current path is inefficient and should be abandoned
            if (ShouldAbandonPathForEfficiency())
            {
                _instantPathOptimization = true;
                _taskManager.ClearTasksPreservingTransitions();
                _hasUsedWp = false;

                // Force immediate path creation
                if (TryCreateInstantPath())
                {
                    yield return null;
                    yield break; // Skip current task processing
                }
            }

            // Mark transition task as done if moved significantly
            if (currentTask.Type == TaskNodeType.Transition &&
                Vector3.Distance(_core.PlayerPosition, _lastPlayerPosition) >= _core.Settings.autoPilotClearPathDistance.Value)
            {
                _taskManager.RemoveTask(currentTask);
                _lastPlayerPosition = _core.PlayerPosition;
                yield return null;
                yield break;
            }

            // Pre-movement override check for movement tasks
            if (currentTask.Type == TaskNodeType.Movement && ShouldOverrideMovementTask(currentTask))
            {
                // Handle dash override towards player
                yield return HandleDashOverride();
                yield break;
            }

            // Execute the task via MovementExecutor
            var executionResult = _movementExecutor.ExecuteTask(currentTask,
                Vector3.Distance(_core.PlayerPosition, currentTask.WorldPosition),
                Vector3.Distance(_core.PlayerPosition, _lastPlayerPosition));

            // Process execution results
            yield return ProcessExecutionResults(currentTask, executionResult);

            // Update last player position
            _lastPlayerPosition = _core.PlayerPosition;
        }

        /// <summary>
        /// Handles portal transition logic
        /// </summary>
        public void HandlePortalTransitions()
        {
            // Portal transition active search
            if (_portalManager.IsInPortalTransition)
            {
                _core.LogMessage("PORTAL: In portal transition mode - actively searching for portals to follow leader");

                var leaderElement = _leaderDetector.GetLeaderPartyElement();
                if (leaderElement != null)
                {
                    var portal = GetBestPortalLabel(leaderElement, forceSearch: true);
                    if (portal != null)
                    {
                        _core.LogMessage($"PORTAL: Found portal '{portal.Label?.Text}' during transition - creating transition task");
                        _taskManager.AddTask(new TaskNode(portal, _core.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                        _core.LogMessage($"PORTAL: Portal transition task created for portal at {portal.ItemOnGround.Pos}");
                    }
                    else
                    {
                        _core.LogMessage("PORTAL: No portals found during transition - will retry on next update");
                    }
                }
                else
                {
                    _core.LogMessage("PORTAL: Cannot search for portals - no leader party element found");
                }
            }

            // Portal transition reset
            if (_portalManager.IsInPortalTransition && GetFollowTarget() != null)
            {
                var distanceToLeader = Vector3.Distance(_core.PlayerPosition, GetFollowTarget().Pos);
                if (distanceToLeader < 1000) // Increased from 300 to 1000 for portal transitions
                {
                    _core.LogMessage("PORTAL: Bot successfully reached leader after portal transition - clearing portal transition mode");
                    _portalManager.SetPortalTransitionMode(false);
                }
            }
        }

        /// <summary>
        /// Prioritizes and selects the next task to execute
        /// </summary>
        public TaskNode SelectNextTask()
        {
            // Priority: Check for teleport tasks first
            var teleportTasks = _taskManager.Tasks.Where(t => t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);
            if (teleportTasks.Any())
            {
                var currentTask = teleportTasks.First();
                _core.LogMessage($"PRIORITY: Processing teleport task {currentTask.Type} instead of {_taskManager.Tasks.First().Type}");
                return currentTask;
            }

            // Return first task normally
            return _taskManager.Tasks.FirstOrDefault();
        }

        // Helper methods
        private bool ShouldClearPathForResponsiveness()
        {
            // Check if player moved significantly since last position
            var playerMovement = Vector3.Distance(_core.PlayerPosition, _lastPlayerPosition);
            return playerMovement > 50; // Configurable threshold
        }

        private bool ShouldAbandonPathForEfficiency()
        {
            // Check if current path efficiency is below threshold
            return CalculatePathEfficiency() < 0.3f; // Configurable threshold
        }

        private bool ShouldOverrideMovementTask(TaskNode currentTask)
        {
            // Check if target is too far from player position
            var targetToPlayerDistance = Vector3.Distance(currentTask.WorldPosition, _core.PlayerPosition);
            return targetToPlayerDistance > 400f;
        }

        private bool TryCreateInstantPath()
        {
            var followTarget = GetFollowTarget();
            if (followTarget?.Pos == null || float.IsNaN(followTarget.Pos.X) || float.IsNaN(followTarget.Pos.Y) || float.IsNaN(followTarget.Pos.Z))
                return false;

            var instantDistanceToLeader = Vector3.Distance(_core.PlayerPosition, followTarget.Pos);

            if (instantDistanceToLeader > _core.Settings.autoPilotDashDistance && _core.Settings.autoPilotDashEnabled)
            {
                // Check for conflicting tasks
                var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                if (!hasConflictingTasks)
                {
                    _taskManager.AddTask(new TaskNode(followTarget.Pos, 0, TaskNodeType.Dash));
                }
            }
            else
            {
                _taskManager.AddTask(new TaskNode(followTarget.Pos, _core.Settings.autoPilotPathfindingNodeDistance));
            }

            return true;
        }

        private IEnumerator ProcessExecutionResults(TaskNode currentTask, TaskExecutionResult result)
        {
            // Handle various execution result flags
            if (result.ShouldDashToLeader)
            {
                yield return Mouse.SetCursorPosHuman(Helper.WorldToValidScreenPosition(GetFollowTargetPosition()));
                _core.LogMessage("Movement task: Dash mouse positioned, pressing key");

                if (_instantPathOptimization)
                {
                    Keyboard.KeyPress(_core.Settings.autoPilotDashKey);
                    _movementExecutor.UpdateLastDashTime(DateTime.Now);
                    _instantPathOptimization = false;
                }
                else
                {
                    yield return new WaitTime(_random.Next(25) + 30);
                    Keyboard.KeyPress(_core.Settings.autoPilotDashKey);
                    _movementExecutor.UpdateLastDashTime(DateTime.Now);
                    yield return new WaitTime(_random.Next(25) + 30);
                }
                yield return null;
                yield break;
            }

            if (result.ShouldTerrainDash)
            {
                _movementExecutor.UpdateLastDashTime(DateTime.Now);
                yield return null;
                yield break;
            }

            if (result.ScreenPosError)
            {
                yield return new WaitTime(50);
                yield break;
            }

            if (!result.ScreenPosError && currentTask.Type == TaskNodeType.Movement)
            {
                // Last chance responsiveness check
                if (ShouldClearPathForResponsiveness())
                {
                    _core.LogMessage("LAST CHANCE 180 CHECK: Player direction changed before movement execution, aborting current task");
                    _taskManager.ClearTasksPreservingTransitions();
                    _hasUsedWp = false;
                    yield return null;
                    yield break;
                }

                _core.LogMessage("Movement task: Mouse positioned, pressing move key down");
                yield return Mouse.SetCursorPosHuman(result.MovementScreenPos);

                if (_instantPathOptimization)
                {
                    _instantPathOptimization = false;
                }
                else
                {
                    yield return new WaitTime(_random.Next(25) + 30);
                    yield return new WaitTime(_random.Next(25) + 30);
                }
                yield return null;
                yield break;
            }

            // Handle other task types...
            if (result.ShouldLootAndContinue)
            {
                // Loot logic would go here
                yield return new WaitTime(50);
                yield break;
            }

            if (result.ShouldTransitionAndContinue)
            {
                // Transition logic handled by MovementExecutor
                yield return new WaitTime(100);
                yield break;
            }

            if (result.ShouldClaimWaypointAndContinue)
            {
                // Waypoint logic would go here
                yield return new WaitTime(50);
                yield break;
            }

            if (result.ShouldDashAndContinue)
            {
                // Dash logic handled by MovementExecutor
                yield return null;
                yield break;
            }

            if (result.ShouldTeleportConfirmAndContinue)
            {
                // Teleport confirm logic handled by MovementExecutor
                yield return new WaitTime(50);
                yield break;
            }

            if (result.ShouldTeleportButtonAndContinue)
            {
                // Teleport button logic handled by MovementExecutor
                yield return new WaitTime(50);
                yield break;
            }

            if (result.ShouldMovementContinue)
            {
                // Movement completion handled
                yield return new WaitTime(50);
                yield break;
            }
        }

        private IEnumerator HandleDashOverride()
        {
            // Calculate correction target towards player
            var playerPos = _core.PlayerPosition;
            var botPos = _core.LocalPlayer?.Pos ?? _core.PlayerPosition;

            var directionToPlayer = playerPos - botPos;
            if (directionToPlayer.Length() > 10f)
            {
                directionToPlayer = Vector3.Normalize(directionToPlayer);
                var correctionTarget = botPos + (directionToPlayer * 400f);

                var correctScreenPos = Helper.WorldToValidScreenPosition(correctionTarget);
                yield return Mouse.SetCursorPosHuman(correctScreenPos);
                Keyboard.KeyPress(_core.Settings.autoPilotDashKey);
                _movementExecutor.UpdateLastDashTime(DateTime.Now);
                _core.LogMessage("DASH OVERRIDE: Dashed towards player position to override old dash");
            }
            else
            {
                _core.LogMessage("DEBUG: Dash override skipped - player too close to bot");
            }
            yield return null;
        }

        // Placeholder methods - these would need to be implemented or moved from AutoPilot
        private float CalculatePathEfficiency() => 0.5f; // Placeholder
        private Entity GetFollowTarget() => _leaderDetector.LeaderEntity;
        private Vector3 GetFollowTargetPosition() => _leaderDetector.LeaderEntity?.Pos ?? Vector3.Zero;
        private LabelOnGround GetBestPortalLabel(PartyElementWindow leaderPartyElement, bool forceSearch) => null; // Placeholder
    }
}
