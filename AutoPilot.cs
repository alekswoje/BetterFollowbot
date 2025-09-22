using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using BetterFollowbotLite.Interfaces;
using BetterFollowbotLite.Core.TaskManagement;
using BetterFollowbotLite.Core.Movement;

namespace BetterFollowbotLite;

    public class AutoPilot
    {
        private PortalManager portalManager;
        private readonly ILeaderDetector _leaderDetector;
        private readonly ITaskManager _taskManager;
        private readonly IPathfinding _pathfinding;
        private IMovementExecutor _movementExecutor;
        private PathPlanner _pathPlanner;
        private Coroutine autoPilotCoroutine;
        private readonly Random random = new Random();

        /// <summary>
        /// Constructor for AutoPilot
        /// </summary>
    public AutoPilot(ILeaderDetector leaderDetector, ITaskManager taskManager, IPathfinding pathfinding, IMovementExecutor movementExecutor, PathPlanner pathPlanner)
        {
        _leaderDetector = leaderDetector ?? throw new ArgumentNullException(nameof(leaderDetector));
        _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
        _pathfinding = pathfinding ?? throw new ArgumentNullException(nameof(pathfinding));
        _movementExecutor = movementExecutor; // Allow null for circular dependency resolution
        _pathPlanner = pathPlanner ?? throw new ArgumentNullException(nameof(pathPlanner));
            portalManager = new PortalManager();
        }

    /// <summary>
    /// Sets the movement executor (used to resolve circular dependency)
    /// </summary>
    public void SetMovementExecutor(IMovementExecutor movementExecutor)
    {
        _movementExecutor = movementExecutor ?? throw new ArgumentNullException(nameof(movementExecutor));
    }

        private Vector3 lastTargetPosition;
        private Vector3 lastPlayerPosition;
        private Entity followTarget;

        public static bool IsTeleportInProgress { get; set; } = false;

    public Entity FollowTarget => followTarget;

    /// <summary>
    /// Gets the current position of the follow target, using updated position data
    /// </summary>
    public Vector3 FollowTargetPosition => lastTargetPosition;

    /// <summary>
    /// Sets the follow target entity
    /// </summary>
    /// <param name="target">The entity to follow</param>
    public void SetFollowTarget(Entity target)
    {
        followTarget = target;
        if (target != null)
        {
            lastTargetPosition = target.Pos;
            BetterFollowbotLite.Instance.LogMessage($"AUTOPILOT: Set follow target '{target.GetComponent<Player>()?.PlayerName ?? "Unknown"}' at position: {target.Pos}");
        }
        else
        {
            BetterFollowbotLite.Instance.LogMessage("AUTOPILOT: Cleared follow target");
        }
    }

    /// <summary>
    /// Updates the follow target's position if it exists
    /// This is crucial for zone transitions where the entity's position changes
    /// </summary>
    public void UpdateFollowTargetPosition()
    {
        if (followTarget != null && followTarget.IsValid)
        {
            var updateStartTime = DateTime.Now;
            var newPosition = followTarget.Pos;

            if (lastTargetPosition != Vector3.Zero)
            {
                var distanceMoved = Vector3.Distance(lastTargetPosition, newPosition);

                if (distanceMoved > 500)
                {
                    var updateDuration = DateTime.Now - updateStartTime;
                    BetterFollowbotLite.Instance.LogMessage($"AUTOPILOT: [{DateTime.Now:HH:mm:ss.fff}] Follow target moved {distanceMoved:F0} units (possible zone transition) from {lastTargetPosition} to {newPosition} (update took {updateDuration.TotalMilliseconds:F0}ms)");
                }
                else if (newPosition != lastTargetPosition)
                {
                    lastTargetPosition = newPosition;
                }
            }

            portalManager.DetectPortalTransition(lastTargetPosition, newPosition);

            lastTargetPosition = newPosition;

            var totalUpdateDuration = DateTime.Now - updateStartTime;
            if (totalUpdateDuration.TotalMilliseconds > 10)
            {
                BetterFollowbotLite.Instance.LogMessage($"AUTOPILOT: [{DateTime.Now:HH:mm:ss.fff}] Position update completed in {totalUpdateDuration.TotalMilliseconds:F0}ms");
            }
        }
        else if (followTarget != null && !followTarget.IsValid)
        {
            BetterFollowbotLite.Instance.LogMessage("AUTOPILOT: Follow target became invalid, clearing");
            followTarget = null;
            lastTargetPosition = Vector3.Zero;
        }
    }

    private bool hasUsedWp;

    /// <summary>
    /// Public accessor for the tasks list (read-only)
    /// </summary>
    public IReadOnlyList<TaskNode> Tasks => _taskManager.Tasks;
    // Dash time tracking moved to MovementExecutor
    private bool instantPathOptimization = false; // Flag for instant response when path efficiency is detected
    private DateTime lastPathClearTime = DateTime.MinValue; // Track last path clear to prevent spam
    private DateTime lastResponsivenessCheck = DateTime.MinValue; // Track last responsiveness check to prevent spam
    private DateTime lastEfficiencyCheck = DateTime.MinValue; // Track last efficiency check to prevent spam


    /// <summary>
    /// Checks if the cursor is pointing roughly towards the target direction in screen space
    /// Improved to handle off-screen targets
    /// </summary>
    private bool IsCursorPointingTowardsTarget(Vector3 targetPosition)
    {
        try
        {
            var mouseScreenPos = BetterFollowbotLite.Instance.GetMousePosition();
            var playerScreenPos = Helper.WorldToValidScreenPosition(BetterFollowbotLite.Instance.playerPosition);
            var targetScreenPos = Helper.WorldToValidScreenPosition(targetPosition);

            if (targetScreenPos.X < 0 || targetScreenPos.Y < 0 ||
                targetScreenPos.X > BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width ||
                targetScreenPos.Y > BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height)
            {
                var playerWorldPos = BetterFollowbotLite.Instance.playerPosition;
                var directionToTarget = targetPosition - playerWorldPos;

                if (directionToTarget.Length() < 10)
                    return true;

                directionToTarget.Normalize();
                var screenDirection = new Vector2(directionToTarget.X, -directionToTarget.Z);
                screenDirection.Normalize();

                var playerToCursorOffscreen = mouseScreenPos - playerScreenPos;
                if (playerToCursorOffscreen.Length() < 30)
                    return false;

                playerToCursorOffscreen.Normalize();

                var dotProductOffscreen = Vector2.Dot(screenDirection, playerToCursorOffscreen);
                var angleOffscreen = Math.Acos(Math.Max(-1, Math.Min(1, dotProductOffscreen))) * (180.0 / Math.PI);

                return angleOffscreen <= 90.0;
            }

            var playerToTarget = targetScreenPos - playerScreenPos;
            if (playerToTarget.Length() < 20)
                return true;

            playerToTarget.Normalize();

            var playerToCursor = mouseScreenPos - playerScreenPos;
            if (playerToCursor.Length() < 30)
                return false;

            playerToCursor.Normalize();

            var dotProduct = Vector2.Dot(playerToTarget, playerToCursor);
            var angle = Math.Acos(Math.Max(-1, Math.Min(1, dotProduct))) * (180.0 / Math.PI);

            return angle <= 60.0;
        }
        catch (Exception e)
        {
            BetterFollowbotLite.Instance.LogMessage($"IsCursorPointingTowardsTarget error: {e.Message}");
            return false;
        }
    }


    private bool ShouldClearPathForResponsiveness()
    {
        return ShouldClearPathForResponsiveness(false);
    }

    private bool ShouldClearPathForResponsiveness(bool isOverrideCheck)
    {
        try
        {
            int rateLimitMs = isOverrideCheck ? 2000 : 5000;
            if ((DateTime.Now - lastPathClearTime).TotalMilliseconds < rateLimitMs)
                return false;

            if ((DateTime.Now - lastResponsivenessCheck).TotalMilliseconds < 1000)
                return false;

            if (followTarget == null || _taskManager.TaskCount == 0)
                return false;

            var playerMovement = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, lastPlayerPosition);

            if (playerMovement > 300f)
            {
                lastPathClearTime = DateTime.Now;
                lastResponsivenessCheck = DateTime.Now;
                return true;
            }

            if (_taskManager.TaskCount > 0 && _taskManager.Tasks[0].WorldPosition != null)
            {
                Vector3 botPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;
                Vector3 playerPos = BetterFollowbotLite.Instance.playerPosition;
                Vector3 currentTaskTarget = _taskManager.Tasks[0].WorldPosition;

                Vector3 botToTask = currentTaskTarget - botPos;
                Vector3 botToPlayer = playerPos - botPos;

                if (botToTask.Length() > 10f && botToPlayer.Length() > 10f)
                {
                    botToTask = Vector3.Normalize(botToTask);
                    botToPlayer = Vector3.Normalize(botToPlayer);

                    float dotProduct = Vector3.Dot(botToTask, botToPlayer);

                    if (dotProduct < -0.5f)
                    {
                        lastPathClearTime = DateTime.Now;
                        lastResponsivenessCheck = DateTime.Now;
                        return true;
                    }
                }
            }

            var distanceToCurrentPlayer = Vector3.Distance(BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition, lastTargetPosition);
            if (distanceToCurrentPlayer > 400f)
            {
                lastPathClearTime = DateTime.Now;
                lastResponsivenessCheck = DateTime.Now;
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    private float CalculatePathEfficiency()
    {
        try
        {
            if (_taskManager.TaskCount == 0 || followTarget == null)
                return 1.0f;

            float directDistance = Vector3.Distance(BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition, followTarget?.Pos ?? BetterFollowbotLite.Instance.playerPosition);

            if (directDistance < 30f)
                return 1.0f;

            float pathDistance = 0f;
            Vector3 currentPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;

            foreach (var task in _taskManager.Tasks)
            {
                if (task.WorldPosition != null)
                {
                    pathDistance += Vector3.Distance(currentPos, task.WorldPosition);
                    currentPos = task.WorldPosition;
                }
            }

            if (pathDistance <= 0 || pathDistance < 50f)
                return 1.0f;

            float efficiency = directDistance / pathDistance;
            return efficiency;
        }
        catch (Exception e)
        {
            return 1.0f;
        }
    }

    private bool ShouldAbandonPathForEfficiency()
    {
        try
        {
            if (_taskManager.TaskCount < 1 || followTarget == null)
                return false;

            if ((DateTime.Now - lastEfficiencyCheck).TotalMilliseconds < 300)
                return false;

            float efficiency = CalculatePathEfficiency();

            if (efficiency < 0.8f)
            {
                lastEfficiencyCheck = DateTime.Now;
                return true;
            }

            if (_taskManager.TaskCount >= 1 && _taskManager.Tasks[0].WorldPosition != null)
            {
                Vector3 botPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;
                Vector3 playerPos = BetterFollowbotLite.Instance.playerPosition;
                Vector3 pathTarget = _taskManager.Tasks[0].WorldPosition;

                Vector3 botToPath = pathTarget - botPos;
                Vector3 botToPlayer = playerPos - botPos;

                if (botToPath.Length() > 0 && botToPlayer.Length() > 0)
                {
                    botToPath = Vector3.Normalize(botToPath);
                    botToPlayer = Vector3.Normalize(botToPlayer);

                    float dotProduct = Vector3.Dot(botToPath, botToPlayer);
                    if (dotProduct < -0.1f)
                    {
                        lastEfficiencyCheck = DateTime.Now;
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    private void ResetPathing()
    {
        _taskManager.ClearTasks();
        followTarget = null;
        lastTargetPosition = Vector3.Zero;
        lastPlayerPosition = Vector3.Zero;
        hasUsedWp = false;
        _movementExecutor.UpdateLastDashTime(DateTime.MinValue);
        instantPathOptimization = false;
        lastPathClearTime = DateTime.MinValue;
        lastResponsivenessCheck = DateTime.MinValue;
        lastEfficiencyCheck = DateTime.MinValue;

        IsTeleportInProgress = false;
    }



    private LabelOnGround GetBestPortalLabel(PartyElementWindow leaderPartyElement, bool forceSearch = false)
    {
        try
        {
            if (leaderPartyElement == null)
                return null;

            var currentZoneName = BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName;
            var leaderZoneName = leaderPartyElement.ZoneName;
            var isHideout = (bool)BetterFollowbotLite.Instance?.GameController?.Area?.CurrentArea?.IsHideout;
            var realLevel = BetterFollowbotLite.Instance.GameController?.Area?.CurrentArea?.RealLevel ?? 0;
            var zonesAreDifferent = !leaderPartyElement.ZoneName.Equals(currentZoneName);

            if (forceSearch || zonesAreDifferent || isHideout || (realLevel >= 68 && zonesAreDifferent))
            {
                var allPortalLabels = BetterFollowbotLite.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                        x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible && x.ItemOnGround != null &&
                        (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal")))
                    .ToList();

                if (allPortalLabels == null || allPortalLabels.Count == 0)
                    return null;

                var matchingPortals = allPortalLabels.Where(x =>
                {
                    try
                    {
                        var labelText = x.Label?.Text?.ToLower() ?? "";
                        var leaderZone = leaderPartyElement.ZoneName?.ToLower() ?? "";
                        var currentZone = currentZoneName?.ToLower() ?? "";

                        var matchesLeaderZone = MatchesPortalToZone(labelText, leaderZone, x.Label?.Text ?? "");
                        var isSpecialPortal = PortalManager.IsSpecialPortal(labelText);

                        return matchesLeaderZone || isSpecialPortal;
                    }
                    catch (Exception ex)
                    {
                        return false;
                    }
                }).OrderBy(x => Vector3.Distance(lastTargetPosition, x.ItemOnGround.Pos)).ToList();

                if (matchingPortals.Count > 0)
                {
                    return matchingPortals.First();
                }

                return null;
            }

            return null;
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    private bool MatchesPortalToZone(string portalLabel, string zoneName, string originalLabel)
    {
        if (string.IsNullOrEmpty(portalLabel) || string.IsNullOrEmpty(zoneName))
            return false;

        portalLabel = portalLabel.ToLower();
        zoneName = zoneName.ToLower();

        if (portalLabel.Contains(zoneName))
            return true;

        var portalPatterns = new[]
        {
            $"portal to {zoneName}",
            $"portal to the {zoneName}",
            $"{zoneName} portal",
            $"enter {zoneName}",
            $"enter the {zoneName}",
            $"go to {zoneName}",
            $"go to the {zoneName}",
            $"{zoneName} entrance",
            $"{zoneName} gate"
        };

        foreach (var pattern in portalPatterns)
        {
            if (portalLabel.Contains(pattern))
                return true;
        }

        if (PortalManager.IsSpecialPortal(portalLabel))
            return true;

        if (portalLabel.Equals(zoneName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (portalLabel.Contains(zoneName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (zoneName.Contains("hideout") && (portalLabel.Contains("hideout") || portalLabel.Contains("home")))
            return true;

        if (zoneName.Contains("town") && (portalLabel.Contains("town") || portalLabel.Contains("waypoint")))
            return true;

        return false;
    }

    private Vector2 GetTpButton(PartyElementWindow leaderPartyElement)
    {
        try
        {
            if (leaderPartyElement == null)
                return Vector2.Zero;

            var windowOffset = BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().TopLeft;
            var elemCenter = (Vector2) leaderPartyElement?.TpButton?.GetClientRectCache.Center;
            var finalPos = new Vector2(elemCenter.X + windowOffset.X, elemCenter.Y + windowOffset.Y);

            return finalPos;
        }
        catch
        {
            return Vector2.Zero;
        }
    }
    private Element GetTpConfirmation()
    {
        try
        {
            var ui = BetterFollowbotLite.Instance.GameController?.IngameState?.IngameUi?.PopUpWindow;

            if (ui.Children[0].Children[0].Children[0].Text.Equals("Are you sure you want to teleport to this player's location?"))
                return ui.Children[0].Children[0].Children[3].Children[0];

            return null;
        }
        catch
        {
            return null;
        }
    }
    public void AreaChange()
    {
        ResetPathing();

        // Initialize terrain data through the pathfinding service
        _pathfinding.InitializeTerrain();
    }

    // Removed StartCoroutine - task execution now happens in Render() method
    // Replace iterator with state machine
    public enum TaskExecutionState
    {
        Idle,
        ExecutingMovement,
        ExecutingDash,
        ExecutingTransition,
        ExecutingWaypoint,
        ExecutingTeleportConfirm,
        ExecutingTeleportButton,
        WaitingForDelay
    }

    private TaskExecutionState _currentExecutionState = TaskExecutionState.Idle;
    private DateTime _stateStartTime = DateTime.MinValue;
    private int _executionAttempts = 0;
    private TaskNode _currentExecutingTask = null;

    public void ExecuteCurrentTask()
    {
        // Reset state if no tasks or invalid conditions
        if (!BetterFollowbotLite.Instance.Settings.Enable.Value ||
            !BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value ||
            BetterFollowbotLite.Instance.localPlayer == null ||
            !BetterFollowbotLite.Instance.localPlayer.IsAlive ||
            !BetterFollowbotLite.Instance.GameController.IsForeGroundCache ||
            MenuWindow.IsOpened ||
            BetterFollowbotLite.Instance.GameController.IsLoading ||
            !BetterFollowbotLite.Instance.GameController.InGame ||
            _taskManager.TaskCount == 0)
        {
            ResetExecutionState();
            return;
        }

        // Zone loading safeguard
        if (BetterFollowbotLite.Instance.GameController.IsLoading ||
            BetterFollowbotLite.Instance.GameController.Area.CurrentArea == null ||
            string.IsNullOrEmpty(BetterFollowbotLite.Instance.GameController.Area.CurrentArea.DisplayName))
        {
            BetterFollowbotLite.Instance.LogMessage("TASK EXECUTION: Blocking task execution during zone loading");
            ResetExecutionState();
            return;
        }

        ExecuteTaskStateMachine();
    }

    private void ResetExecutionState()
    {
        _currentExecutionState = TaskExecutionState.Idle;
        _currentExecutingTask = null;
        _executionAttempts = 0;
        _stateStartTime = DateTime.MinValue;
    }

    private void ExecuteTaskStateMachine()
    {
        // Get current task if we don't have one
        if (_currentExecutingTask == null && _taskManager.TaskCount > 0)
        {
            _currentExecutingTask = _taskManager.GetAndRemoveFirstTask();
            _currentExecutionState = GetStateForTaskType(_currentExecutingTask.Type);
            _stateStartTime = DateTime.Now;
            _executionAttempts = 1;
        }

        if (_currentExecutingTask == null) return;

        // Execute based on current state
        switch (_currentExecutionState)
        {
            case TaskExecutionState.ExecutingMovement:
                ExecuteMovementTask();
                break;
            case TaskExecutionState.ExecutingDash:
                ExecuteDashTask();
                break;
            case TaskExecutionState.ExecutingTransition:
                ExecuteTransitionTask();
                break;
            case TaskExecutionState.ExecutingWaypoint:
                ExecuteWaypointTask();
                break;
            case TaskExecutionState.ExecutingTeleportConfirm:
                ExecuteTeleportConfirmTask();
                break;
            case TaskExecutionState.ExecutingTeleportButton:
                ExecuteTeleportButtonTask();
                break;
            case TaskExecutionState.WaitingForDelay:
                CheckDelayCompletion();
                break;
        }
    }

    private TaskExecutionState GetStateForTaskType(TaskNodeType taskType)
    {
        return taskType switch
        {
            TaskNodeType.Movement => TaskExecutionState.ExecutingMovement,
            TaskNodeType.Dash => TaskExecutionState.ExecutingDash,
            TaskNodeType.Transition => TaskExecutionState.ExecutingTransition,
            TaskNodeType.ClaimWaypoint => TaskExecutionState.ExecutingWaypoint,
            TaskNodeType.TeleportConfirm => TaskExecutionState.ExecutingTeleportConfirm,
            TaskNodeType.TeleportButton => TaskExecutionState.ExecutingTeleportButton,
            _ => TaskExecutionState.Idle
        };
    }

    private void ExecuteMovementTask()
    {
        // Simplified movement execution - immediate instead of yielding
        try
        {
            var movementScreenPos = Helper.WorldToValidScreenPosition(_currentExecutingTask.WorldPosition);
            if (movementScreenPos.X >= 0 && movementScreenPos.Y >= 0 &&
                movementScreenPos.X <= BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width &&
                movementScreenPos.Y <= BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height)
            {
                Mouse.SetCursorPos(movementScreenPos);
                Keyboard.KeyDown(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
                // Set delay instead of yielding
                StartDelay(50 + new Random().Next(25)); // 50-75ms delay
            }
            else
            {
                // Invalid position, complete immediately
                CompleteCurrentTask();
            }
        }
        catch (Exception e)
        {
            BetterFollowbotLite.Instance.LogError($"Movement task error: {e}");
            CompleteCurrentTask();
        }
    }

    private void StartDelay(int milliseconds)
    {
        _currentExecutionState = TaskExecutionState.WaitingForDelay;
        _stateStartTime = DateTime.Now.AddMilliseconds(milliseconds);
    }

    private void CheckDelayCompletion()
    {
        if (DateTime.Now >= _stateStartTime)
        {
            // Delay complete, finish the task
            Keyboard.KeyUp(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
            CompleteCurrentTask();
        }
    }

    private void CompleteCurrentTask()
    {
        _currentExecutingTask = null;
        _currentExecutionState = TaskExecutionState.Idle;
        _executionAttempts = 0;
    }

    // Stub implementations - these would need to be implemented based on the original iterator logic
    private void ExecuteDashTask()
    {
        // TODO: Implement dash execution logic
        CompleteCurrentTask();
    }

    private void ExecuteTransitionTask()
    {
        // TODO: Implement transition execution logic
        CompleteCurrentTask();
    }

    private void ExecuteWaypointTask()
    {
        // TODO: Implement waypoint execution logic
        CompleteCurrentTask();
    }

    private void ExecuteTeleportConfirmTask()
    {
        // TODO: Implement teleport confirm execution logic
        CompleteCurrentTask();
    }

    private void ExecuteTeleportButtonTask()
    {
        // TODO: Implement teleport button execution logic
        CompleteCurrentTask();
    }

    /// <summary>
    /// Render method called every frame - handles task execution
    /// </summary>
    public void Render()
    {
        // Execute current task in the render loop
        ExecuteCurrentTask();
    }
}
