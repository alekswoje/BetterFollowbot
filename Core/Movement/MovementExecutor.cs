using System;
using System.Collections;
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
using SharpDX;

namespace BetterFollowbotLite.Core.Movement
{
    /// <summary>
    /// Handles execution of movement tasks (walking, dashing, teleporting, etc.)
    /// </summary>
    public class MovementExecutor : IMovementExecutor
    {
        private readonly IFollowbotCore _core;
        private readonly ITaskManager _taskManager;
        private readonly IPathfinding _pathfinding;
        private readonly AutoPilot _autoPilot;
        private readonly Random _random = new Random();
        private DateTime _lastDashTime = DateTime.MinValue;

        public MovementExecutor(IFollowbotCore core, ITaskManager taskManager, IPathfinding pathfinding, AutoPilot autoPilot)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            _pathfinding = pathfinding ?? throw new ArgumentNullException(nameof(pathfinding));
            _autoPilot = autoPilot ?? throw new ArgumentNullException(nameof(autoPilot));
        }

        #region IMovementExecutor Implementation

        public DateTime LastDashTime => _lastDashTime;

        public void UpdateLastDashTime(DateTime time)
        {
            _lastDashTime = time;
        }

        public bool IsCursorPointingTowardsTarget(Vector3 targetPosition)
        {
            try
            {
                var mousePos = _core.GetMousePosition();
                var screenCenter = new Vector2(
                    BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width / 2,
                    BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height / 2
                );

                var directionToMouse = mousePos - screenCenter;
                var directionToTarget = Helper.WorldToValidScreenPosition(targetPosition) - screenCenter;

                directionToMouse.Normalize();
                directionToTarget.Normalize();

                var dotProduct = Vector2.Dot(directionToMouse, directionToTarget);
                return dotProduct > 0.8f; // 36 degree tolerance
            }
            catch
            {
                return false;
            }
        }

        public bool ShouldClearPathForResponsiveness(bool aggressiveTiming = false)
        {
            // TODO: Re-implement this logic exactly as it was in AutoPilot
            // This was a complex method that checked for 180-degree turns
            // For now, return false to maintain existing behavior
            return false;
        }

        public IEnumerable ExecuteTask(TaskNode currentTask, float taskDistance, float playerDistanceMoved)
        {
            // Execute the task based on its type
            switch (currentTask.Type)
            {
                case TaskNodeType.Movement:
                    foreach (var result in ExecuteMovementTask(currentTask, taskDistance))
                        yield return result;
                    yield break;
                case TaskNodeType.Loot:
                    foreach (var result in ExecuteLootTask(currentTask))
                        yield return result;
                    yield break;
                case TaskNodeType.Transition:
                    foreach (var result in ExecuteTransitionTask(currentTask))
                        yield return result;
                    yield break;
                case TaskNodeType.ClaimWaypoint:
                    foreach (var result in ExecuteClaimWaypointTask(currentTask))
                        yield return result;
                    yield break;
                case TaskNodeType.Dash:
                    foreach (var result in ExecuteDashTask(currentTask))
                        yield return result;
                    yield break;
                case TaskNodeType.TeleportConfirm:
                    foreach (var result in ExecuteTeleportConfirmTask(currentTask))
                        yield return result;
                    yield break;
                case TaskNodeType.TeleportButton:
                    foreach (var result in ExecuteTeleportButtonTask(currentTask))
                        yield return result;
                    yield break;
                default:
                    // Unknown task type - remove it
                    _taskManager.RemoveTask(currentTask);
                    yield break;
            }
        }

        #endregion

        #region Movement Task Execution

        private IEnumerable ExecuteMovementTask(TaskNode currentTask, float taskDistance)
        {
            var shouldDashToLeader = false;
            var shouldTerrainDash = false;
            var shouldMovementContinue = false;
            var screenPosError = false;
            var keyDownError = false;
            var keyUpError = false;

            // Check for distance-based dashing to keep up with leader
            if (BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled &&
                _autoPilot.FollowTarget != null &&
                _autoPilot.FollowTarget.Pos != null &&
                (DateTime.Now - _lastDashTime).TotalMilliseconds >= 3000)
            {
                try
                {
                    var distanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, _autoPilot.FollowTargetPosition);
                    if (distanceToLeader > BetterFollowbotLite.Instance.Settings.autoPilotDashDistance &&
                        IsCursorPointingTowardsTarget(_autoPilot.FollowTarget.Pos)) // Dash if more than configured distance away and cursor is pointing towards leader
                    {
                        shouldDashToLeader = true;
                    }
                }
                catch (Exception e)
                {
                    // Error handling without logging
                }
            }
            else
            {
                // Dash check skipped
            }

            // Check for terrain-based dashing
            if (BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled &&
                (DateTime.Now - _lastDashTime).TotalMilliseconds >= 3000)
            {
                // Terrain dash check
                if (_pathfinding.CheckDashTerrain(currentTask.WorldPosition.WorldToGrid()) &&
                    IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                {
                    // Terrain dash executed
                    shouldTerrainDash = true;
                }
                else if (_pathfinding.CheckDashTerrain(currentTask.WorldPosition.WorldToGrid()) &&
                         !IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                {
                    // Terrain dash blocked - cursor not pointing towards target
                }
                else
                {
                    // No terrain dash needed
                }
            }

            // Skip movement logic if dashing
            Vector2 movementScreenPos = Vector2.Zero;
            if (!shouldDashToLeader && !shouldTerrainDash)
            {
                try
                {
                    movementScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);
                }
                catch (Exception e)
                {
                    screenPosError = true;
                }

                if (!screenPosError)
                {
                    try
                    {
                        Input.KeyDown(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
                        BetterFollowbotLite.Instance.LogMessage("Movement task: Move key down pressed, waiting");
                    }
                    catch (Exception e)
                    {
                        BetterFollowbotLite.Instance.LogError($"Movement task: KeyDown error: {e}");
                        keyDownError = true;
                    }

                    try
                    {
                        Input.KeyUp(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
                        BetterFollowbotLite.Instance.LogMessage("Movement task: Move key released");
                    }
                    catch (Exception e)
                    {
                        BetterFollowbotLite.Instance.LogError($"Movement task: KeyUp error: {e}");
                        keyUpError = true;
                    }

                    //Within bounding range. Task is complete
                    //Note: Was getting stuck on close objects... testing hacky fix.
                    if (taskDistance <= BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance.Value * 1.5)
                    {
                        // Removed excessive movement completion logging
                        _taskManager.RemoveTask(currentTask);
                    }
                    else
                    {
                        // Timeout mechanism - if we've been trying to reach this task for too long, give up
                        currentTask.AttemptCount++;
                        if (currentTask.AttemptCount > 10) // 10 attempts = ~5 seconds
                        {
                            BetterFollowbotLite.Instance.LogMessage($"Movement task timeout - Distance: {taskDistance:F1}, Attempts: {currentTask.AttemptCount}");
                            _taskManager.RemoveTask(currentTask);
                        }
                    }
                    shouldMovementContinue = true;
                }
            }

            // Handle execution outside try-catch blocks
            if (shouldDashToLeader)
            {
                    yield return Mouse.SetCursorPosHuman(Helper.WorldToValidScreenPosition(_autoPilot.FollowTargetPosition));
                BetterFollowbotLite.Instance.LogMessage("Movement task: Dash mouse positioned, pressing key");
                yield return new WaitTime(_random.Next(25) + 30);
                Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                _lastDashTime = DateTime.Now; // Record dash time for cooldown
                yield return new WaitTime(_random.Next(25) + 30);
                yield return null;
                yield break;
            }

            if (shouldTerrainDash)
            {
                _lastDashTime = DateTime.Now; // Record dash time for cooldown (CheckDashTerrain already performed the dash)
                yield return null;
                yield break;
            }

            if (screenPosError)
            {
                yield return new WaitTime(50);
                yield break;
            }

            if (!screenPosError && currentTask.Type == TaskNodeType.Movement)
            {
                // LAST CHANCE CHECK: Before executing movement, check if player has turned around
                if (ShouldClearPathForResponsiveness())
                {
                    BetterFollowbotLite.Instance.LogMessage("LAST CHANCE 180 CHECK: Player direction changed before movement execution, aborting current task");
                    _taskManager.ClearTasksPreservingTransitions();
                    // Note: hasUsedWp reset would be handled by caller
                    yield return null; // Skip this movement and recalculate
                    yield break;
                }

                BetterFollowbotLite.Instance.LogMessage("Movement task: Mouse positioned, pressing move key down");
                BetterFollowbotLite.Instance.LogMessage($"Movement task: Move key: {BetterFollowbotLite.Instance.Settings.autoPilotMoveKey}");
                // Removed excessive DEBUG logging for cleaner logs
                yield return Mouse.SetCursorPosHuman(movementScreenPos);

                yield return new WaitTime(_random.Next(25) + 30);
                yield return new WaitTime(_random.Next(25) + 30);
                yield return null;
                yield break;
            }
        }

        #endregion

        #region Loot Task Execution

        private IEnumerable ExecuteLootTask(TaskNode currentTask)
        {
            var shouldLootAndContinue = false;
            Entity questLoot = null;

            currentTask.AttemptCount++;
            try
            {
                questLoot = BetterFollowbotLite.Instance.GameController.EntityListWrapper.Entities
                    .Where(e => e?.Type == ExileCore.Shared.Enums.EntityType.WorldItem && e.IsTargetable && e.HasComponent<WorldItem>())
                    .FirstOrDefault(e =>
                    {
                        var itemEntity = e.GetComponent<WorldItem>().ItemEntity;
                        return BetterFollowbotLite.Instance.GameController.Files.BaseItemTypes.Translate(itemEntity.Path).ClassName ==
                               "QuestItem";
                    });
            }
            catch
            {
                questLoot = null;
            }

            if (questLoot == null ||
                currentTask.AttemptCount > 2 ||
                Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, questLoot.Pos) >=
                BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value)
            {
                _taskManager.RemoveTask(currentTask);
                shouldLootAndContinue = true;
            }
            else
            {
                Input.KeyUp(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
                shouldLootAndContinue = true; // Set flag to execute loot logic outside try-catch
            }

            // Execute loot logic outside try-catch
            if (shouldLootAndContinue && questLoot != null)
            {
                var targetInfo = questLoot.GetComponent<Targetable>();
                if (targetInfo != null)
                {
                    yield return new WaitTime(BetterFollowbotLite.Instance.Settings.autoPilotInputFrequency);
                    switch (targetInfo.isTargeted)
                    {
                        case false:
                            yield return MouseoverItem(questLoot);
                            break;
                        case true:
                            yield return Mouse.LeftClick();
                            yield return new WaitTime(1000);
                            break;
                    }
                }
            }

            yield return null;
        }

        #endregion

        #region Transition Task Execution

        private IEnumerable ExecuteTransitionTask(TaskNode currentTask)
        {
            var shouldTransitionAndContinue = false;

            // Initialize flag to true - will be set to false if portal is invalid
            shouldTransitionAndContinue = true;

            // Log portal information
            var portalLabel = currentTask.LabelOnGround?.Label?.Text ?? "NULL";
            var portalPos = currentTask.LabelOnGround?.ItemOnGround?.Pos ?? Vector3.Zero;
            var distanceToPortal = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, portalPos);

            BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Portal '{portalLabel}' at distance {distanceToPortal:F1}");

            // Check if portal is still visible and valid
            var isPortalVisible = currentTask.LabelOnGround?.Label?.IsVisible ?? false;
            var isPortalValid = currentTask.LabelOnGround?.Label?.IsValid ?? false;

            BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Portal visibility - Visible: {isPortalVisible}, Valid: {isPortalValid}");

            if (!isPortalVisible || !isPortalValid)
            {
                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Portal no longer visible or valid, removing task");
                _taskManager.RemoveTask(currentTask);
                shouldTransitionAndContinue = false;
                yield break; // Exit the switch case
            }

            //Click the transition
            Input.KeyUp(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);

            // Get the portal click position with more detailed logging
            var portalRect = currentTask.LabelOnGround.Label.GetClientRect();
            var transitionPos = new Vector2(portalRect.Center.X, portalRect.Center.Y);

            BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Portal click position - X: {transitionPos.X:F1}, Y: {transitionPos.Y:F1}");
            BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Portal screen rect - Left: {portalRect.Left:F1}, Top: {portalRect.Top:F1}, Width: {portalRect.Width:F1}, Height: {portalRect.Height:F1}");

            currentTask.AttemptCount++;
            if (currentTask.AttemptCount > 6)
            {
                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Max attempts reached (6), removing transition task");
                _taskManager.RemoveTask(currentTask);
            }
            else
            {
                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Transition task queued for execution");
            }
            shouldTransitionAndContinue = true;

            // Execute transition logic outside try-catch
            if (shouldTransitionAndContinue)
            {
                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Starting portal click sequence");

                // Move mouse to portal position
                BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Moving mouse to portal position ({transitionPos.X:F1}, {transitionPos.Y:F1})");
                yield return Mouse.SetCursorPosHuman(transitionPos);

                // Wait a bit for mouse to settle
                yield return new WaitTime(60);

                // Perform the click with additional logging
                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Performing left click on portal");
                var currentMousePos = BetterFollowbotLite.Instance.GetMousePosition();
                BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Mouse position before click - X: {currentMousePos.X:F1}, Y: {currentMousePos.Y:F1}");

                yield return Mouse.LeftClick();

                // Wait for transition to start
                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Waiting for transition to process");
                yield return new WaitTime(300);

                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Portal click sequence completed");
                yield return null;
                yield break;
            }
        }

        #endregion

        #region Claim Waypoint Task Execution

        private IEnumerable ExecuteClaimWaypointTask(TaskNode currentTask)
        {
            var shouldClaimWaypointAndContinue = false;
            Vector2 waypointScreenPos = Vector2.Zero;

            if (Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, currentTask.WorldPosition) > 150)
            {
                waypointScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);
                Input.KeyUp(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
            }

            currentTask.AttemptCount++;
            if (currentTask.AttemptCount > 3)
                _taskManager.RemoveTask(currentTask);

            shouldClaimWaypointAndContinue = true;

            // Execute waypoint logic outside try-catch
            if (shouldClaimWaypointAndContinue)
            {
                if (Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, currentTask.WorldPosition) > 150)
                {
                    yield return new WaitTime(BetterFollowbotLite.Instance.Settings.autoPilotInputFrequency);
                    yield return Mouse.SetCursorPosAndLeftClickHuman(waypointScreenPos, 100);
                    yield return new WaitTime(1000);
                }
                yield return null;
                yield break;
            }
        }

        #endregion

        #region Dash Task Execution

        private IEnumerable ExecuteDashTask(TaskNode currentTask)
        {
            var shouldDashAndContinue = false;

            BetterFollowbotLite.Instance.LogMessage($"Executing Dash task - Target: {currentTask.WorldPosition}, Distance: {Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, currentTask.WorldPosition):F1}, Attempts: {currentTask.AttemptCount}");

            // TIMEOUT MECHANISM: If dash task has been tried too many times, give up
            currentTask.AttemptCount++;
            if (currentTask.AttemptCount > 15) // Allow more attempts for dash tasks
            {
                BetterFollowbotLite.Instance.LogMessage($"Dash task timeout - Too many attempts ({currentTask.AttemptCount}), removing task");
                _taskManager.RemoveTask(currentTask);
                yield break;
            }

            if ((DateTime.Now - _lastDashTime).TotalMilliseconds >= 3000)
            {
                // Check if cursor is pointing towards target
                if (IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                {
                    BetterFollowbotLite.Instance.LogMessage("Dash task: Cursor direction valid, executing dash");

                    // Position mouse towards target if needed
                    var targetScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);
                    if (targetScreenPos.X >= 0 && targetScreenPos.Y >= 0 &&
                        targetScreenPos.X <= BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width &&
                        targetScreenPos.Y <= BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height)
                    {
                        // Target is on-screen, position mouse
                        yield return Mouse.SetCursorPosHuman(targetScreenPos);
                    }

                    // Execute the dash
                    Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                    _lastDashTime = DateTime.Now; // Record dash time for cooldown

                    // Remove the task since dash was executed
                    _taskManager.RemoveTask(currentTask);
                    BetterFollowbotLite.Instance.LogMessage("Dash task completed successfully");
                    shouldDashAndContinue = true;
                }
                else
                {
                    BetterFollowbotLite.Instance.LogMessage("Dash task: Cursor not pointing towards target, positioning cursor");

                    // Try to position cursor towards the target
                    var targetScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);

                    // If target is off-screen, position towards the edge of screen in the target's direction
                    if (targetScreenPos.X < 0 || targetScreenPos.Y < 0 ||
                        targetScreenPos.X > BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width ||
                        targetScreenPos.Y > BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height)
                    {
                        // Calculate direction to target and position mouse at screen edge
                        var playerPos = BetterFollowbotLite.Instance.playerPosition;
                        var directionToTarget = currentTask.WorldPosition - playerPos;
                        directionToTarget.Normalize();

                        // Position mouse at screen center (simplified approach)
                        var screenCenter = new Vector2(
                            BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width / 2,
                            BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height / 2
                        );

                        yield return Mouse.SetCursorPosHuman(screenCenter);
                        BetterFollowbotLite.Instance.LogMessage("Dash task: Positioned cursor at screen center for off-screen target");
                    }
                    else
                    {
                        // Target is on-screen but cursor isn't pointing towards it
                        yield return Mouse.SetCursorPosHuman(targetScreenPos);
                        BetterFollowbotLite.Instance.LogMessage("Dash task: Repositioned cursor towards target");
                    }

                    // Wait a bit for cursor to settle
                    yield return new WaitTime(100);

                    // Check again if cursor is now pointing towards target
                    if (IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                    {
                        BetterFollowbotLite.Instance.LogMessage("Dash task: Cursor now pointing correctly, executing dash");
                        Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                        _lastDashTime = DateTime.Now;
                        _taskManager.RemoveTask(currentTask);
                        shouldDashAndContinue = true;
                    }
                    else
                    {
                        BetterFollowbotLite.Instance.LogMessage("Dash task: Still can't position cursor correctly, will retry");
                        // Don't remove the task - let it try again later
                        yield return new WaitTime(300); // Longer delay before retry
                        yield break;
                    }
                }
            }
            else
            {
                BetterFollowbotLite.Instance.LogMessage("Dash task blocked - Cooldown active");
                // Don't remove the task - wait for cooldown to expire
                yield return new WaitTime(600); // Wait before retry during cooldown
                yield break;
            }

            // Execute dash logic outside try-catch
            if (shouldDashAndContinue)
            {
                // LAST CHANCE CHECK: Before executing dash, check if player has turned around
                if (ShouldClearPathForResponsiveness())
                {
                    BetterFollowbotLite.Instance.LogMessage("LAST CHANCE 180 CHECK: Player direction changed before dash execution, aborting current task");
                    _taskManager.ClearTasksPreservingTransitions();
                    // Note: hasUsedWp reset would be handled by caller
                    yield return null; // Skip this dash and recalculate
                    yield break;
                }

                yield return Mouse.SetCursorPosHuman(Helper.WorldToValidScreenPosition(currentTask.WorldPosition));
                BetterFollowbotLite.Instance.LogMessage("Dash: Mouse positioned, pressing dash key");

                // IMMEDIATE OVERRIDE CHECK: After positioning cursor, check if we need to override
                if (ShouldClearPathForResponsiveness(true)) // Use aggressive override timing
                {
                    BetterFollowbotLite.Instance.LogMessage("IMMEDIATE OVERRIDE: 180 detected after dash positioning - overriding with new position!");
                    _taskManager.ClearTasksPreservingTransitions();
                    // Note: hasUsedWp reset would be handled by caller

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
                        yield return Mouse.SetCursorPosHuman(correctScreenPos);
                        Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                        _lastDashTime = DateTime.Now; // Record dash time for cooldown
                        BetterFollowbotLite.Instance.LogMessage("DASH OVERRIDE: Dashed towards player position to override old dash");
                    }
                    else
                    {
                        BetterFollowbotLite.Instance.LogMessage("DEBUG: Dash override skipped - player too close to bot");
                    }
                    yield return null;
                    yield break;
                }

                yield return new WaitTime(_random.Next(25) + 30);
                Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                _lastDashTime = DateTime.Now; // Record dash time for cooldown
                yield return new WaitTime(_random.Next(25) + 30);
                yield return null;
                yield break;
            }
        }

        #endregion

        #region Teleport Task Execution

        private IEnumerable ExecuteTeleportConfirmTask(TaskNode currentTask)
        {
            var shouldTeleportConfirmAndContinue = false;

            _taskManager.RemoveTask(currentTask);
            shouldTeleportConfirmAndContinue = true;

            // Execute teleport confirm logic outside try-catch
            if (shouldTeleportConfirmAndContinue)
            {
                yield return Mouse.SetCursorPosHuman(new Vector2(currentTask.WorldPosition.X, currentTask.WorldPosition.Y));
                yield return new WaitTime(200);
                yield return Mouse.LeftClick();
                yield return new WaitTime(200);
                // CRITICAL: Move mouse to center of screen after teleport confirm to prevent unwanted movement
                var screenCenter = new Vector2(
                    BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width / 2,
                    BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height / 2
                );
                Mouse.SetCursorPos(screenCenter);
                yield return new WaitTime(1000);
                yield return null;
                yield break;
            }
        }

        private IEnumerable ExecuteTeleportButtonTask(TaskNode currentTask)
        {
            var shouldTeleportButtonAndContinue = false;

            _taskManager.RemoveTask(currentTask);
            // CLEAR GLOBAL FLAG: Teleport task completed
            AutoPilot.SetTeleportInProgress(false);
            shouldTeleportButtonAndContinue = true;

            // Execute teleport button logic outside try-catch
            if (shouldTeleportButtonAndContinue)
            {
                yield return Mouse.SetCursorPosHuman(new Vector2(currentTask.WorldPosition.X, currentTask.WorldPosition.Y), false);
                yield return new WaitTime(200);
                yield return Mouse.LeftClick();
                yield return new WaitTime(200);
                // CRITICAL: Move mouse to center of screen after teleport button to prevent unwanted movement
                var screenCenter = new Vector2(
                    BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width / 2,
                    BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height / 2
                );
                Mouse.SetCursorPos(screenCenter);
                yield return new WaitTime(200);
                yield return null;
                yield break;
            }
        }

        #endregion

        #region Helper Methods

        private IEnumerable MouseoverItem(Entity entity)
        {
            var entityPos = entity.Pos;
            var screenPos = Helper.WorldToValidScreenPosition(entityPos);
            yield return Mouse.SetCursorPosAndLeftClickHuman(screenPos, 100);
            yield return new WaitTime(100);
        }

        #endregion
    }
}
