using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            // Instead of immediately clearing, try to find the leader again
            var retryTarget = _leaderDetector.FindLeaderEntity();
            if (retryTarget != null && retryTarget.IsValid)
            {
                BetterFollowbotLite.Instance.LogMessage("AUTOPILOT: Follow target was invalid but found again, continuing");
                followTarget = retryTarget;
                // Update lastTargetPosition to the new valid position
                if (followTarget.Pos != null)
                {
                    lastTargetPosition = followTarget.Pos;
                }
            }
            else
            {
                BetterFollowbotLite.Instance.LogMessage("AUTOPILOT: Follow target became invalid and retry failed, clearing");
                followTarget = null;
                lastTargetPosition = Vector3.Zero;
            }
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
        var terrainInitStart = DateTime.Now;
        _pathfinding.InitializeTerrain();
        var terrainInitTime = DateTime.Now - terrainInitStart;
        BetterFollowbotLite.Instance.LogMessage($"TERRAIN INIT: Terrain initialization completed in {terrainInitTime.TotalMilliseconds:F2}ms");
    }

    public void StartCoroutine()
    {
        Task.Run(() => AutoPilotLogic());
    }
    private async Task AutoPilotLogic()
    {
        while (true)
        {
            if (!BetterFollowbotLite.Instance.Settings.Enable.Value || !BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value || BetterFollowbotLite.Instance.localPlayer == null || !BetterFollowbotLite.Instance.localPlayer.IsAlive ||
                !BetterFollowbotLite.Instance.GameController.IsForeGroundCache || MenuWindow.IsOpened || BetterFollowbotLite.Instance.GameController.IsLoading || !BetterFollowbotLite.Instance.GameController.InGame)
            {
                // Log check failures that might cause delays
                if (!BetterFollowbotLite.Instance.GameController.IsForeGroundCache)
                {
                    BetterFollowbotLite.Instance.LogMessage("FOREGROUND CHECK: Game not in foreground - blocking task execution");
                }
                if (MenuWindow.IsOpened)
                {
                    BetterFollowbotLite.Instance.LogMessage("MENU CHECK: Menu window is open - blocking task execution");
                }
                await Task.Delay(100);
                continue;
            }

            // ADDITIONAL SAFEGUARD: Don't execute tasks during zone loading or when game state is unstable
            if (BetterFollowbotLite.Instance.GameController.IsLoading ||
                BetterFollowbotLite.Instance.GameController.Area.CurrentArea == null ||
                string.IsNullOrEmpty(BetterFollowbotLite.Instance.GameController.Area.CurrentArea.DisplayName))
            {
                BetterFollowbotLite.Instance.LogMessage("TASK EXECUTION: Blocking task execution during zone loading");
                await Task.Delay(200); // Wait longer during zone loading
                continue;
            }

            // Only execute input tasks here - decision making moved to Render method
            if (_taskManager.TaskCount > 0)
            {
                TaskNode currentTask = null;
                bool taskAccessError = false;

                // PRIORITY: Check if there are any teleport tasks and process them first
                var teleportTasks = _taskManager.Tasks.Where(t => t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);
                if (teleportTasks.Any())
                {
                    try
                    {
                        currentTask = teleportTasks.First();
                        BetterFollowbotLite.Instance.LogMessage($"PRIORITY: Processing teleport task {currentTask.Type} instead of {_taskManager.Tasks.First().Type}");
                    }
                    catch (Exception e)
                    {
                        taskAccessError = true;
                        BetterFollowbotLite.Instance.LogMessage($"PRIORITY: Error accessing teleport task - {e.Message}");
                    }
                }
                else
                {
                    try
                    {
                        currentTask = _taskManager.Tasks.First();
                    }
                    catch (Exception e)
                    {
                        taskAccessError = true;
                    }
                }

                if (taskAccessError)
                {
                    await Task.Delay(50);
                    continue;
                }

                if (currentTask?.WorldPosition == null)
                {
                    // Remove the task from its actual position, not just index 0
                    _taskManager.RemoveTask(currentTask);
                    await Task.Delay(50);
                    continue;
                }

                // Log task execution start
                BetterFollowbotLite.Instance.LogMessage($"TASK EXECUTION: Starting {currentTask.Type} task at {currentTask.WorldPosition} (Queue size: {_taskManager.TaskCount})");

                var taskDistance = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, currentTask.WorldPosition);
                var playerDistanceMoved = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, lastPlayerPosition);

                // Check if we should clear path for better responsiveness to player movement
                if (ShouldClearPathForResponsiveness())
                {
                    instantPathOptimization = true; // Enable instant mode for immediate response
                    _taskManager.ClearTasksPreservingTransitions(); // Clear all tasks and reset related state
                    hasUsedWp = false; // Allow waypoint usage again
                    
                    // FORCE IMMEDIATE PATH CREATION - Don't wait for UpdateAutoPilotLogic
                    if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                    {
                        var instantDistanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, FollowTargetPosition);

                        if (instantDistanceToLeader > BetterFollowbotLite.Instance.Settings.autoPilotDashDistance && BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled) // Use configured dash distance
                        {
                            // CRITICAL: Don't add dash tasks if we have an active transition task OR another dash task
                            var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                            if (!hasConflictingTasks)
                            {
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, 0, TaskNodeType.Dash));
                            }
                            else
                            {
                            }
                        }
                        else
                        {
                            _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                        }
                    }
                    
                    continue; // Skip current task processing, will recalculate path immediately
                }

                // Check if current path is inefficient and should be abandoned - INSTANT RESPONSE
                if (ShouldAbandonPathForEfficiency())
                {
                    instantPathOptimization = true; // Enable instant mode for immediate response
                    _taskManager.ClearTasksPreservingTransitions(); // Clear all tasks and reset related state
                    hasUsedWp = false; // Allow waypoint usage again
                    
                    // FORCE IMMEDIATE PATH CREATION - Don't wait for UpdateAutoPilotLogic
                    if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                    {
                        var instantDistanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, FollowTargetPosition);

                        if (instantDistanceToLeader > BetterFollowbotLite.Instance.Settings.autoPilotDashDistance && BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled) // Use configured dash distance
                        {
                            // CRITICAL: Don't add dash tasks if we have an active transition task OR another dash task
                            var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                            if (!hasConflictingTasks)
                            {
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, 0, TaskNodeType.Dash));
                            }
                            else
                            {
                            }
                        }
                        else
                        {
                            _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                        }
                    }
                    
                    continue; // Skip current task processing, will recalculate path immediately
                }

                //We are using a same map transition and have moved significnatly since last tick. Mark the transition task as done.
                if (currentTask.Type == TaskNodeType.Transition &&
                    playerDistanceMoved >= BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value)
                {
                    _taskManager.RemoveTask(currentTask);
                    lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;
                    continue;
                }

                // Variables to track state outside try-catch blocks
                bool shouldDashToLeader = false;
                bool shouldTerrainDash = false;
                Vector2 movementScreenPos = Vector2.Zero;
                bool screenPosError = false;
                bool keyDownError = false;
                bool keyUpError = false;
                bool taskExecutionError = false;

                bool shouldTransitionAndContinue = false;
                bool shouldClaimWaypointAndContinue = false;
                bool shouldDashAndContinue = false;
                bool shouldTeleportConfirmAndContinue = false;
                bool shouldTeleportButtonAndContinue = false;
                bool shouldMovementContinue = false;


                // Transition-related variables
                Vector2 transitionPos = Vector2.Zero;

                // Waypoint-related variables
                Vector2 waypointScreenPos = Vector2.Zero;

                // PRE-MOVEMENT OVERRIDE CHECK: Check if we should override BEFORE executing movement
                if (currentTask.Type == TaskNodeType.Movement)
                {
                    // SIMPLIFIED OVERRIDE: Just check if target is far from current player position
                    var playerPos = BetterFollowbotLite.Instance.playerPosition;
                    var botPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;
                    var targetPos = currentTask.WorldPosition;
                    
                    // Calculate direction from bot to target vs bot to player
                    var botToTarget = targetPos - botPos;
                    var botToPlayer = playerPos - botPos;
                    
                    bool shouldOverride = false;
                    string overrideReason = "";
                    
                    // Check 1: Is target far from player?
                    var targetToPlayerDistance = Vector3.Distance(targetPos, playerPos);
                    if (targetToPlayerDistance > 400f)
                    {
                        shouldOverride = true;
                        overrideReason = $"Target {targetToPlayerDistance:F1} units from player";
                    }
                    
                    // Check 2: Are we going opposite direction from player?
                    if (!shouldOverride && botToTarget.Length() > 10f && botToPlayer.Length() > 10f)
                    {
                        var dotProduct = Vector3.Dot(Vector3.Normalize(botToTarget), Vector3.Normalize(botToPlayer));
                        if (dotProduct < 0.3f) // Going more than 72 degrees away from player
                        {
                            shouldOverride = true;
                            overrideReason = $"Direction conflict (dot={dotProduct:F2})";
                        }
                    }

                    if (shouldOverride)
                    {
                        _taskManager.ClearTasksPreservingTransitions();
                        hasUsedWp = false; // Allow waypoint usage again
                        
                        // INSTANT OVERRIDE: Click towards the player's current position instead of stale followTarget
                        // Calculate a position closer to the player (not the exact player position to avoid issues)
                        var directionToPlayer = playerPos - botPos;
                        if (directionToPlayer.Length() > 10f) // Only if player is far enough away
                        {
                            directionToPlayer = Vector3.Normalize(directionToPlayer);
                            var correctionTarget = botPos + (directionToPlayer * 200f); // Move 200 units towards player
                            
                            var correctScreenPos = Helper.WorldToValidScreenPosition(correctionTarget);
                            Mouse.SetCursorPosHuman(correctScreenPos);

                            // Skip the rest of this movement task since we've overridden it
                            continue;
                        }
                    }
                }

                // Execute task through the movement executor
                var executionResult = _movementExecutor.ExecuteTask(currentTask, taskDistance, playerDistanceMoved);

                // Set local flags from execution result
                shouldDashToLeader = executionResult.ShouldDashToLeader;
                shouldTerrainDash = executionResult.ShouldTerrainDash;
                shouldTransitionAndContinue = executionResult.ShouldTransitionAndContinue;
                shouldClaimWaypointAndContinue = executionResult.ShouldClaimWaypointAndContinue;
                shouldDashAndContinue = executionResult.ShouldDashAndContinue;
                shouldTeleportConfirmAndContinue = executionResult.ShouldTeleportConfirmAndContinue;
                shouldTeleportButtonAndContinue = executionResult.ShouldTeleportButtonAndContinue;
                shouldMovementContinue = executionResult.ShouldMovementContinue;
                screenPosError = executionResult.ScreenPosError;
                keyDownError = executionResult.KeyDownError;
                keyUpError = executionResult.KeyUpError;
                taskExecutionError = executionResult.TaskExecutionError;
                movementScreenPos = executionResult.MovementScreenPos;
                transitionPos = executionResult.TransitionPos;
                waypointScreenPos = executionResult.WaypointScreenPos;


                // Handle error cleanup (simplified without try-catch)
                if (currentTask != null && currentTask.AttemptCount > 20)
                {
                    // Remove task if it's been attempted too many times
                    BetterFollowbotLite.Instance.LogMessage($"Task timeout - Too many attempts ({currentTask.AttemptCount}), removing task");
                    _taskManager.RemoveTask(currentTask);
                }

                // Handle portal invalidation after try-catch
                if (currentTask != null && currentTask.Type == TaskNodeType.Transition && !shouldTransitionAndContinue)
                {
                    // Portal was invalidated, wait and continue
                    await Task.Delay(100);
                    continue;
                }
                // Execute actions outside try-catch blocks
                else
                {
                    if (shouldDashToLeader)
                    {
                        Mouse.SetCursorPosHuman(Helper.WorldToValidScreenPosition(FollowTargetPosition));
                        BetterFollowbotLite.Instance.LogMessage("Movement task: Dash mouse positioned, pressing key");
                        if (instantPathOptimization)
                        {
                            // INSTANT MODE: Skip delays for immediate path correction
                            // Removed excessive INSTANT PATH OPTIMIZATION logging
                            Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                            _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                            instantPathOptimization = false; // Reset flag after use
                        }
                        else
                        {
                            // Normal delays
                            await Task.Delay(random.Next(25) + 30);
                            Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                            _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                            await Task.Delay(random.Next(25) + 30);
                        }
                        continue;
                    }

                    if (shouldTerrainDash)
                    {
                        _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown (CheckDashTerrain already performed the dash)
                        continue;
                    }

                    if (screenPosError)
                    {
                        await Task.Delay(50);
                        continue;
                    }

                    if (!screenPosError && currentTask.Type == TaskNodeType.Movement)
                    {
                        // LAST CHANCE CHECK: Before executing movement, check if player has turned around
                        if (ShouldClearPathForResponsiveness())
                        {
                            BetterFollowbotLite.Instance.LogMessage("LAST CHANCE 180 CHECK: Player direction changed before movement execution, aborting current task");
                            _taskManager.ClearTasksPreservingTransitions();
                            hasUsedWp = false; // Allow waypoint usage again
 // Skip this movement and recalculate
                            continue;
                        }

                        BetterFollowbotLite.Instance.LogMessage("Movement task: Mouse positioned, pressing move key down");
                        BetterFollowbotLite.Instance.LogMessage($"Movement task: Move key: {BetterFollowbotLite.Instance.Settings.autoPilotMoveKey}");
                        Mouse.SetCursorPosHuman(movementScreenPos);
                        
                        if (instantPathOptimization)
                        {
                            // INSTANT MODE: Skip delays for immediate path correction
                            // Removed excessive INSTANT PATH OPTIMIZATION logging
                            instantPathOptimization = false; // Reset flag after use
                        }
                        else
                        {
                            // Normal delays
                            await Task.Delay(random.Next(25) + 30);
                            await Task.Delay(random.Next(25) + 30);
                        }
                        continue;
                    }


                    if (shouldTransitionAndContinue)
                    {
                        BetterFollowbotLite.Instance.LogMessage("TRANSITION: Starting portal click sequence");

                        // Move mouse to portal position
                        BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Moving mouse to portal position ({transitionPos.X:F1}, {transitionPos.Y:F1})");
                        Mouse.SetCursorPosHuman(transitionPos);

                        // Wait a bit for mouse to settle
                        await Task.Delay(60);

                        // Perform the click with additional logging
                        BetterFollowbotLite.Instance.LogMessage("TRANSITION: Performing left click on portal");
                        var currentMousePos = BetterFollowbotLite.Instance.GetMousePosition();
                        BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Mouse position before click - X: {currentMousePos.X:F1}, Y: {currentMousePos.Y:F1}");

                        Mouse.LeftClick();

                        // Wait for transition to start
                        BetterFollowbotLite.Instance.LogMessage("TRANSITION: Waiting for transition to process");
                        await Task.Delay(300);

                        BetterFollowbotLite.Instance.LogMessage("TRANSITION: Portal click sequence completed");
                        continue;
                    }

                    if (shouldClaimWaypointAndContinue)
                    {
                        if (Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, currentTask.WorldPosition) > 150)
                        {
                            await Task.Delay(BetterFollowbotLite.Instance.Settings.autoPilotInputFrequency);
                            Mouse.SetCursorPosAndLeftClickHuman(waypointScreenPos, 100);
                            await Task.Delay(1000);
                        }
                        continue;
                    }

                    if (shouldDashAndContinue)
                    {
                        // LAST CHANCE CHECK: Before executing dash, check if player has turned around
                        if (ShouldClearPathForResponsiveness())
                        {
                            BetterFollowbotLite.Instance.LogMessage("LAST CHANCE 180 CHECK: Player direction changed before dash execution, aborting current task");
                            _taskManager.ClearTasksPreservingTransitions();
                            hasUsedWp = false; // Allow waypoint usage again
 // Skip this dash and recalculate
                            continue;
                        }

                        Mouse.SetCursorPosHuman(Helper.WorldToValidScreenPosition(currentTask.WorldPosition));
                        BetterFollowbotLite.Instance.LogMessage("Dash: Mouse positioned, pressing dash key");
                        
                        // IMMEDIATE OVERRIDE CHECK: After positioning cursor, check if we need to override
                        if (ShouldClearPathForResponsiveness(true)) // Use aggressive override timing
                        {
                            BetterFollowbotLite.Instance.LogMessage("IMMEDIATE OVERRIDE: 180 detected after dash positioning - overriding with new position!");
                            _taskManager.ClearTasksPreservingTransitions();
                            hasUsedWp = false; // Allow waypoint usage again
                            
                            // INSTANT OVERRIDE: Position cursor towards player and dash there instead
                            var playerPos = BetterFollowbotLite.Instance.playerPosition;
                            var botPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;
                            
                            // Calculate a position closer to the player for dash correction
                            var directionToPlayer = playerPos - botPos;
                            if (directionToPlayer.Length() > 10f) // Only if player is far enough away
                            {
                                directionToPlayer = Vector3.Normalize(directionToPlayer);
                                var correctionTarget = botPos + (directionToPlayer * 400f); // Dash 400 units towards player
                                
                                var correctScreenPos = Helper.WorldToValidScreenPosition(correctionTarget);
                                BetterFollowbotLite.Instance.LogMessage($"DEBUG: Dash override - Old position: {currentTask.WorldPosition}, Player position: {playerPos}");
                                BetterFollowbotLite.Instance.LogMessage($"DEBUG: Dash override - Correction target: {correctionTarget}");
                                Mouse.SetCursorPosHuman(correctScreenPos);
                                Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                                _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                                BetterFollowbotLite.Instance.LogMessage("DASH OVERRIDE: Dashed towards player position to override old dash");
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage("DEBUG: Dash override skipped - player too close to bot");
                            }
                            continue;
                        }
                        
                        if (instantPathOptimization)
                        {
                            // INSTANT MODE: Skip delays for immediate path correction
                            // Removed excessive INSTANT PATH OPTIMIZATION logging
                            Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                            _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                            instantPathOptimization = false; // Reset flag after use
                        }
                        else
                        {
                            // Normal delays
                            await Task.Delay(random.Next(25) + 30);
                            Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                            _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                            await Task.Delay(random.Next(25) + 30);
                        }
                        continue;
                    }

                    if (shouldTeleportConfirmAndContinue)
                    {
                        // FAST TELEPORT CONFIRM: Direct mouse movement for speed
                        Mouse.SetCursorPos(new Vector2(currentTask.WorldPosition.X, currentTask.WorldPosition.Y));
                        await Task.Delay(50); // Reduced from 200ms
                        Mouse.LeftClick();
                        await Task.Delay(100); // Reduced from 200ms, need some time for dialog
                        // CRITICAL: Move mouse to center of screen after teleport confirm to prevent unwanted movement
                        var screenCenter = new Vector2(BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width / 2, BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height / 2);
                        Mouse.SetCursorPos(screenCenter);
                        await Task.Delay(200); // Reduced from 1000ms, still need time for teleport
                        continue;
                    }

                    if (shouldTeleportButtonAndContinue)
                    {
                        // FAST TELEPORT: Direct mouse movement for speed
                        Mouse.SetCursorPos(new Vector2(currentTask.WorldPosition.X, currentTask.WorldPosition.Y));
                        await Task.Delay(50); // Reduced from 200ms
                        Mouse.LeftClick();
                        await Task.Delay(50); // Reduced from 200ms
                        // CRITICAL: Move mouse to center of screen after teleport button to prevent unwanted movement
                        var screenCenter = new Vector2(BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width / 2, BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height / 2);
                        Mouse.SetCursorPos(screenCenter);
                        await Task.Delay(100); // Reduced from 200ms
                        continue;
                    }
                }
            }

            lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;
            await Task.Delay(50);
        }
    }

    // New method for decision making that runs every game tick
    public void UpdateAutoPilotLogic()
    {
        try
        {
            var leaderPartyElement = _leaderDetector.GetLeaderPartyElement();
            var followTarget = _leaderDetector.FindLeaderEntity();

            lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;

            // Allow PlanPath to run even with null followTarget for zone transitions
            // PlanPath handles null followTarget gracefully
            _pathPlanner.PlanPath(followTarget, leaderPartyElement, lastTargetPosition, lastPlayerPosition);

            if (followTarget?.Pos != null)
                lastTargetPosition = followTarget.Pos;
        }
        catch (Exception e)
        {
            BetterFollowbotLite.Instance.LogError($"UpdateAutoPilotLogic Error: {e}");
        }
    }

    public void Render()
    {
        if (BetterFollowbotLite.Instance.Settings.autoPilotToggleKey.PressedOnce())
        {
            BetterFollowbotLite.Instance.Settings.autoPilotEnabled.SetValueNoEvent(!BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value);
            _taskManager.ClearTasks();
        }

        if (BetterFollowbotLite.Instance.Settings.autoPilotEnabled)
        {
            // AutoPilot runs continuously when enabled
        }

        if (!BetterFollowbotLite.Instance.Settings.autoPilotEnabled || BetterFollowbotLite.Instance.GameController.IsLoading || !BetterFollowbotLite.Instance.GameController.InGame)
            return;

        try
        {
            var portalLabels =
                BetterFollowbotLite.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                    x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible &&
                    x.ItemOnGround != null &&
                    (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") ||
                     x.ItemOnGround.Metadata.ToLower().Contains("portal"))).ToList();

            foreach (var portal in portalLabels)
            {
                var portalLabel = portal.Label?.Text ?? "Unknown";
                var labelRect = portal.Label.GetClientRectCache;

                BetterFollowbotLite.Instance.Graphics.DrawLine(labelRect.TopLeft, labelRect.TopRight, 2f, Color.Firebrick);
                BetterFollowbotLite.Instance.Graphics.DrawLine(labelRect.TopRight, labelRect.BottomRight, 2f, Color.Firebrick);
                BetterFollowbotLite.Instance.Graphics.DrawLine(labelRect.BottomRight, labelRect.BottomLeft, 2f, Color.Firebrick);
                BetterFollowbotLite.Instance.Graphics.DrawLine(labelRect.BottomLeft, labelRect.TopLeft, 2f, Color.Firebrick);

                var labelPos = new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 20);
                BetterFollowbotLite.Instance.Graphics.DrawText($"Portal: {portalLabel}", labelPos, Color.Yellow);

                var distance = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, portal.ItemOnGround.Pos);
                var distancePos = new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 35);
                BetterFollowbotLite.Instance.Graphics.DrawText($"{distance:F1}m", distancePos, Color.Cyan);

                if (PortalManager.IsSpecialPortal(portalLabel))
                {
                    var portalType = PortalManager.GetSpecialPortalType(portalLabel);
                    BetterFollowbotLite.Instance.Graphics.DrawText(portalType, new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 50), Color.OrangeRed);
                }
            }
        }
        catch (Exception)
        {
            //ignore
        }

        BetterFollowbotLite.Instance.Graphics.DrawText("AutoPilot: Active", new System.Numerics.Vector2(350, 120));
        BetterFollowbotLite.Instance.Graphics.DrawText("Task: Async", new System.Numerics.Vector2(350, 140));
        BetterFollowbotLite.Instance.Graphics.DrawText("Leader: " + (followTarget != null ? "Found" : "Null"), new System.Numerics.Vector2(350, 160));

        var transitionTasks = _taskManager.Tasks.Where(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);
        if (transitionTasks.Any())
        {
            var currentTransitionTask = transitionTasks.First();
            BetterFollowbotLite.Instance.Graphics.DrawText($"Transition: {currentTransitionTask.Type}", new System.Numerics.Vector2(350, 180), Color.Yellow);
        }

        try
        {
            var taskCount = 0;
            var dist = 0f;
            var cachedTasks = _taskManager.Tasks;
            if (cachedTasks?.Count > 0)
            {
                BetterFollowbotLite.Instance.Graphics.DrawText(
                    "Current Task: " + cachedTasks[0].Type,
                    new Vector2(500, 160));
                foreach (var task in cachedTasks.TakeWhile(task => task?.WorldPosition != null))
                {
                    if (taskCount == 0)
                    {
                        BetterFollowbotLite.Instance.Graphics.DrawLine(
                            Helper.WorldToValidScreenPosition(BetterFollowbotLite.Instance.playerPosition),
                            Helper.WorldToValidScreenPosition(task.WorldPosition), 2f, Color.Pink);
                        dist = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, task.WorldPosition);
                        BetterFollowbotLite.Instance.Graphics.DrawText(
                            "Distance: " + dist.ToString("F2") + "m",
                            new Vector2(500, 180));
                    }
                    taskCount++;
                }
                BetterFollowbotLite.Instance.Graphics.DrawText(
                    "Task Count: " + cachedTasks.Count,
                    new System.Numerics.Vector2(500, 140));
            }
        }
        catch (Exception)
        {
            //ignore
        }
    }
}
