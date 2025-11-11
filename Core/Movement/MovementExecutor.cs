using System;
using System.Linq;
using BetterFollowbot.Core.TaskManagement;
using BetterFollowbot.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BetterFollowbot.Core.Movement
{
    /// <summary>
    /// Handles execution of different types of movement tasks
    /// </summary>
    public class MovementExecutor : IMovementExecutor
    {
        private readonly IFollowbotCore _core;
        private readonly ITaskManager _taskManager;
        private readonly IPathfinding _pathfinding;
        private readonly AutoPilot _autoPilot;

        private DateTime _lastDashTime = DateTime.MinValue;
        private Vector3 _lastPlayerPosition;

        public MovementExecutor(IFollowbotCore core, ITaskManager taskManager, IPathfinding pathfinding, AutoPilot autoPilot)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            _pathfinding = pathfinding ?? throw new ArgumentNullException(nameof(pathfinding));
            _autoPilot = autoPilot ?? throw new ArgumentNullException(nameof(autoPilot));
        }

        public DateTime LastDashTime => _lastDashTime;

        public void UpdateLastDashTime(DateTime dashTime)
        {
            _lastDashTime = dashTime;
        }

        public void UpdateLastPlayerPosition(Vector3 position)
        {
            _lastPlayerPosition = position;
        }

        public TaskExecutionResult ExecuteTask(TaskNode currentTask, float taskDistance, float playerDistanceMoved)
        {
            var result = new TaskExecutionResult();

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

            Vector2 transitionPos = Vector2.Zero;
            Vector2 waypointScreenPos = Vector2.Zero;

            switch (currentTask.Type)
            {
                case TaskNodeType.Movement:
                    if (_core.Settings.autoPilotDashEnabled && _autoPilot.FollowTarget != null && _autoPilot.FollowTarget.Pos != null && (DateTime.Now - _lastDashTime).TotalMilliseconds >= 3000)
                    {
                        try
                        {
                            var distanceToLeader = Vector3.Distance(_core.PlayerPosition, _autoPilot.FollowTargetPosition);
                            if (distanceToLeader > _core.Settings.autoPilotDashDistance && IsCursorPointingTowardsTarget(_autoPilot.FollowTarget.Pos))
                            {
                                shouldDashToLeader = true;
                            }
                        }
                        catch (Exception e)
                        {
                        }
                    }

                    if (_core.Settings.autoPilotDashEnabled && (DateTime.Now - _lastDashTime).TotalMilliseconds >= 3000)
                    {
                        if (_pathfinding.CheckDashTerrain(currentTask.WorldPosition.WorldToGrid()) && IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                        {
                            shouldTerrainDash = true;
                        }
                        else
                        {
                        }
                    }

                    if (!shouldDashToLeader && !shouldTerrainDash)
                    {
                        // CRITICAL FIX: Movement tasks must position cursor BEFORE pressing move key
                        // This prevents the bot from moving in random directions when cursor is not pointing towards target
                        try
                        {
                            movementScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);
                            // Position cursor towards the movement target BEFORE pressing move key
                            Mouse.SetCursorPosHuman(movementScreenPos);
                            _core.LogMessage($"Movement task: Cursor positioned to ({movementScreenPos.X:F1}, {movementScreenPos.Y:F1}) for movement target");
                        }
                        catch (Exception e)
                        {
                            screenPosError = true;
                            _core.LogError($"Movement task: Cursor positioning error: {e}");
                        }

                        if (!screenPosError)
                        {
                            try
                            {
                                // Give cursor positioning time to settle before pressing move key
                                System.Threading.Thread.Sleep(50);
                                Input.KeyDown(_core.Settings.autoPilotMoveKey);
                                _core.LogMessage("Movement task: Move key down pressed, waiting");
                            }
                            catch (Exception e)
                            {
                                _core.LogError($"Movement task: KeyDown error: {e}");
                                keyDownError = true;
                            }

                            try
                            {
                                Input.KeyUp(_core.Settings.autoPilotMoveKey);
                                _core.LogMessage("Movement task: Move key released");
                            }
                            catch (Exception e)
                            {
                                _core.LogError($"Movement task: KeyUp error: {e}");
                                keyUpError = true;
                            }

                            //Within bounding range. Task is complete
                            //Note: Was getting stuck on close objects... testing hacky fix.
                            if (taskDistance <= _core.Settings.autoPilotPathfindingNodeDistance.Value * 1.5)
                            {
                                _core.LogMessage($"TASK COMPLETION: Movement task completed at distance {taskDistance:F1} (Attempts: {currentTask.AttemptCount})");
                                _taskManager.RemoveTask(currentTask);
                                _lastPlayerPosition = _core.PlayerPosition;
                            }
                            else
                            {
                                // Timeout mechanism - if we've been trying to reach this task for too long, give up
                                currentTask.AttemptCount++;
                                if (currentTask.AttemptCount > 10) // 10 attempts = ~5 seconds
                                {
                                    _core.LogMessage($"Movement task timeout - Distance: {taskDistance:F1}, Attempts: {currentTask.AttemptCount}");
                                    _taskManager.RemoveTask(currentTask);
                                    _lastPlayerPosition = _core.PlayerPosition;
                                }
                            }
                            shouldMovementContinue = true;
                        }
                    }
                    break;
                case TaskNodeType.Transition:
                {
                    _core.LogMessage($"TRANSITION: Executing transition task - Attempt {currentTask.AttemptCount + 1} (no timeout)");

                    shouldTransitionAndContinue = true;

                    var portalLabel = currentTask.LabelOnGround?.Label?.Text ?? "NULL";
                    var portalPos = currentTask.LabelOnGround?.ItemOnGround?.Pos ?? Vector3.Zero;
                    var distanceToPortal = Vector3.Distance(_core.PlayerPosition, portalPos);

                    _core.LogMessage($"TRANSITION: Portal '{portalLabel}' at distance {distanceToPortal:F1}");

                    var isPortalVisible = currentTask.LabelOnGround?.Label?.IsVisible ?? false;
                    var isPortalValid = currentTask.LabelOnGround?.Label?.IsValid ?? false;
                    var isEntityValid = currentTask.LabelOnGround?.ItemOnGround?.IsValid ?? false;

                    _core.LogMessage($"TRANSITION: Portal validity - Label Visible: {isPortalVisible}, Label Valid: {isPortalValid}, Entity Valid: {isEntityValid}");

                    if (!isPortalVisible || !isPortalValid || !isEntityValid)
                    {
                        _core.LogMessage("TRANSITION: Portal no longer visible, valid, or entity destroyed - removing task to search for new portals");
                        _taskManager.RemoveTask(currentTask);
                        shouldTransitionAndContinue = false; // Don't continue with transition
                        break; // Exit the switch case - PathPlanner will find a new portal
                    }

                    //Click the transition
                    Input.KeyUp(_core.Settings.autoPilotMoveKey);

                    // Get the portal UI button position instead of world position
                    var portalLabelRect = currentTask.LabelOnGround.Label.GetClientRectCache;
                    var portalButtonCenter = portalLabelRect.Center;
                    transitionPos = new Vector2(portalButtonCenter.X, portalButtonCenter.Y);

                    _core.LogMessage($"TRANSITION: Portal '{portalLabel}' UI button center: ({portalButtonCenter.X:F1}, {portalButtonCenter.Y:F1})");
                    _core.LogMessage($"TRANSITION: Portal button rect: {portalLabelRect}");

                    // Check if the portal is actually clickable (on screen)
                    var screenBounds = _core.GameController.Window.GetWindowRectangle();
                    var isOnScreen = transitionPos.X >= 0 && transitionPos.X <= screenBounds.Width &&
                                   transitionPos.Y >= 0 && transitionPos.Y <= screenBounds.Height;

                    _core.LogMessage($"TRANSITION: Portal on screen: {isOnScreen}, Screen bounds: {screenBounds.Width}x{screenBounds.Height}");

                    if (!isOnScreen)
                    {
                        _core.LogMessage($"TRANSITION: Portal is not on screen at distance {distanceToPortal:F1}, need to move closer");
                        
                        // Increment attempt count to track progress
                        currentTask.AttemptCount++;
                        
                        // Check if portal is unreasonably far away (likely invalid or inaccessible)
                        var maxPortalDistance = _core.Settings.autoPilotMaxPortalDistance.Value;
                        if (distanceToPortal > maxPortalDistance)
                        {
                            _core.LogMessage($"TRANSITION: Portal is too far away ({distanceToPortal:F1} units > {maxPortalDistance} max), likely invalid or inaccessible");
                            _core.LogMessage("TRANSITION: Removing transition task, PathPlanner will try party teleport or another portal");
                            _taskManager.RemoveTask(currentTask);
                            shouldTransitionAndContinue = false;
                            break;
                        }
                        
                        // CHANGED: Never give up on transition attempts - keep trying different methods
                        // PathPlanner will cycle through: matching portal -> swirly -> any portal
                        _core.LogMessage($"TRANSITION: Will keep trying - attempt {currentTask.AttemptCount}");
                        
                        if (distanceToPortal > 100)
                        {
                            _core.LogMessage($"TRANSITION: Creating movement task to approach portal (distance: {distanceToPortal:F1}, attempt {currentTask.AttemptCount})");
                            var approachTask = new TaskNode(portalPos, 50, TaskNodeType.Movement);
                            _taskManager.RemoveTask(currentTask);
                            _taskManager.AddTask(approachTask);
                            _taskManager.AddTask(currentTask);
                            shouldTransitionAndContinue = false;
                            break;
                        }
                        else
                        {
                            _core.LogMessage($"TRANSITION: Close enough but portal UI not rendering properly, retrying (attempt {currentTask.AttemptCount})");
                            // CHANGED: Don't give up after 20 attempts - reset attempt count and try again
                            if (currentTask.AttemptCount > 20)
                            {
                                _core.LogMessage("TRANSITION: Many retry attempts, removing task to search for alternative portal or swirly");
                                _taskManager.RemoveTask(currentTask);
                            }
                            shouldTransitionAndContinue = false;
                            break;
                        }
                    }

                    _core.LogMessage("TRANSITION: Portal clicked, removing transition task");
                    _taskManager.RemoveTask(currentTask);
                    shouldTransitionAndContinue = true;
                    break;
                }

                case TaskNodeType.ClaimWaypoint:
                {
                    if (Vector3.Distance(_core.PlayerPosition, currentTask.WorldPosition) > 150)
                    {
                        waypointScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);
                        Input.KeyUp(_core.Settings.autoPilotMoveKey);
                    }
                    currentTask.AttemptCount++;
                    if (currentTask.AttemptCount > 3)
                        _taskManager.RemoveTask(currentTask);
                    shouldClaimWaypointAndContinue = true;
                    break;
                }

                 case TaskNodeType.Dash:
                 {
                     _core.LogMessage($"Executing Dash task - Target: {currentTask.WorldPosition}, Distance: {Vector3.Distance(_core.PlayerPosition, currentTask.WorldPosition):F1}, Attempts: {currentTask.AttemptCount}");

                     currentTask.AttemptCount++;
                     if (currentTask.AttemptCount > 15)
                     {
                         _core.LogMessage($"Dash task timeout - Too many attempts ({currentTask.AttemptCount}), removing task");
                         _taskManager.RemoveTask(currentTask);
                         break;
                     }

                     if ((DateTime.Now - _lastDashTime).TotalMilliseconds >= 3000)
                     {
                         if (IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                         {
                             _core.LogMessage("Dash task: Cursor direction valid, executing dash");

                             Keyboard.KeyPress(_core.Settings.autoPilotDashKey);
                             _lastDashTime = DateTime.Now;
                             _lastPlayerPosition = _core.PlayerPosition;

                             _taskManager.RemoveTask(currentTask);
                             _core.LogMessage("Dash task completed successfully");
                             shouldDashAndContinue = true;
                         }
                        else
                        {
                            _core.LogMessage("Dash task: Cursor not pointing towards target, positioning cursor");

                            var targetScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);

                            if (targetScreenPos.X >= 0 && targetScreenPos.Y >= 0 &&
                                targetScreenPos.X <= _core.GameController.Window.GetWindowRectangle().Width &&
                                targetScreenPos.Y <= _core.GameController.Window.GetWindowRectangle().Height)
                            {
                                // CRITICAL FIX: Use proper cursor positioning method with smoothing and delays
                                // This prevents the dash from going in wrong direction due to cursor positioning timing issues
                                Mouse.SetCursorPosHuman(targetScreenPos);
                                _core.LogMessage($"Dash task: Cursor positioned to ({targetScreenPos.X:F1}, {targetScreenPos.Y:F1})");

                                // Give cursor positioning more time to settle before dash
                                System.Threading.Thread.Sleep(75); // Increased from 25ms to ensure cursor settles

                                _core.LogMessage("Dash task: Cursor positioned, executing dash");

                                Keyboard.KeyPress(_core.Settings.autoPilotDashKey);
                                _lastDashTime = DateTime.Now;
                                _lastPlayerPosition = _core.PlayerPosition;

                                _taskManager.RemoveTask(currentTask);
                                _core.LogMessage("Dash task completed successfully");
                                shouldDashAndContinue = true;
                            }
                            else
                            {
                                // Target is off-screen, convert to movement task
                                _core.LogMessage("Dash task: Target off-screen, converting to movement task");
                                _taskManager.RemoveTask(currentTask);

                                if (_autoPilot.FollowTarget != null)
                                {
                                    _taskManager.AddTask(new TaskNode(currentTask.WorldPosition, _core.Settings.autoPilotPathfindingNodeDistance));
                                    _core.LogMessage("Dash task converted to movement task (off-screen target)");
                                }
                            }
                        }
                     }
                     else
                     {
                         // CRITICAL FIX: Instead of retrying dash task on cooldown, convert it to a movement task
                         // This prevents the dash task from blocking the entire task queue
                         _core.LogMessage($"Dash task: Cooldown active - {((DateTime.Now - _lastDashTime).TotalMilliseconds):F0}ms remaining, converting to movement task");

                         // Remove the dash task and create a movement task instead
                         _taskManager.RemoveTask(currentTask);

                         // Create a movement task to the same position
                         if (_autoPilot.FollowTarget != null)
                         {
                             _taskManager.AddTask(new TaskNode(currentTask.WorldPosition, _core.Settings.autoPilotPathfindingNodeDistance));
                             _core.LogMessage("Dash task converted to movement task due to cooldown");
                         }
                     }
                     break;
                 }

                 case TaskNodeType.TeleportConfirm:
                 {
                     _taskManager.RemoveTask(currentTask);
                     shouldTeleportConfirmAndContinue = true;
                     break;
                 }

                 case TaskNodeType.TeleportButton:
                 {
                     _taskManager.RemoveTask(currentTask);
                     // CLEAR GLOBAL FLAG: Teleport task completed
                     AutoPilot.IsTeleportInProgress = false;
                     shouldTeleportButtonAndContinue = true;
                     break;
                 }

                 // NEW: Skill task execution
                 case TaskNodeType.FlameLink:
                 case TaskNodeType.ProtectiveLink:
                 case TaskNodeType.DestructiveLink:
                 case TaskNodeType.SoulLink:
                 {
                     ExecuteLinkSkillTask(currentTask);
                     break;
                 }

                default:
                    // Unknown task type, remove it
                    _taskManager.RemoveTask(currentTask);
                    break;
            }

            // Set result flags
            result.ShouldDashToLeader = shouldDashToLeader;
            result.ShouldTerrainDash = shouldTerrainDash;
            result.ShouldTransitionAndContinue = shouldTransitionAndContinue;
            result.ShouldClaimWaypointAndContinue = shouldClaimWaypointAndContinue;
            result.ShouldDashAndContinue = shouldDashAndContinue;
            result.ShouldTeleportConfirmAndContinue = shouldTeleportConfirmAndContinue;
            result.ShouldTeleportButtonAndContinue = shouldTeleportButtonAndContinue;
            result.ShouldMovementContinue = shouldMovementContinue;
            result.ScreenPosError = screenPosError;
            result.KeyDownError = keyDownError;
            result.KeyUpError = keyUpError;
            result.TaskExecutionError = taskExecutionError;
            result.MovementScreenPos = movementScreenPos;
            result.TransitionPos = transitionPos;
            result.WaypointScreenPos = waypointScreenPos;

            return result;
        }

        private bool IsCursorPointingTowardsTarget(Vector3 targetPosition)
        {
            try
            {
                // Get the current mouse position in screen coordinates
                var mouseScreenPos = _core.GetMousePosition();

                // Get the player's screen position
                var playerScreenPos = Helper.WorldToValidScreenPosition(_core.PlayerPosition);

                // Get the target's screen position
                var targetScreenPos = Helper.WorldToValidScreenPosition(targetPosition);

                // Calculate distance from player to target in world space
                var worldDistanceToTarget = Vector3.Distance(_core.PlayerPosition, targetPosition);

                // If target is very close (within 20 units), cursor direction doesn't matter much
                if (worldDistanceToTarget < 20)
                    return true;

                // Check if target is on-screen
                var windowRect = _core.GameController.Window.GetWindowRectangle();
                var isTargetOnScreen = targetScreenPos.X >= 0 && targetScreenPos.Y >= 0 &&
                                     targetScreenPos.X <= windowRect.Width &&
                                     targetScreenPos.Y <= windowRect.Height;

                if (!isTargetOnScreen)
                {
                    // For off-screen targets, calculate direction from world positions
                    var playerWorldPos = _core.PlayerPosition;
                    var directionToTarget = targetPosition - playerWorldPos;
                    directionToTarget = Vector3.Normalize(directionToTarget);

                    // Calculate direction from player to mouse in screen space (approximation for off-screen)
                    var mouseDirection = new Vector3(mouseScreenPos.X - playerScreenPos.X, mouseScreenPos.Y - playerScreenPos.Y, 0);
                    if (mouseDirection.Length() > 0)
                        mouseDirection = Vector3.Normalize(mouseDirection);

                    // Check if mouse direction roughly matches target direction (within 60 degrees for off-screen targets)
                    var dotProduct = Vector3.Dot(mouseDirection, new Vector3(directionToTarget.X, directionToTarget.Y, 0));
                    return dotProduct > 0.5f; // ~60 degrees - more lenient for off-screen targets
                }
                else
                {
                    // Target is on-screen, check if mouse is pointing towards it
                    var mouseFromPlayer = mouseScreenPos - playerScreenPos;
                    var targetFromPlayer = targetScreenPos - playerScreenPos;

                    // If mouse is very close to target on screen, consider it pointing towards target
                    if (Vector2.Distance(mouseScreenPos, targetScreenPos) < 75)
                        return true;

                    // Check if mouse is generally pointing towards target direction
                    if (mouseFromPlayer.Length() > 0 && targetFromPlayer.Length() > 0)
                    {
                        var dotProduct = Vector2.Dot(Vector2.Normalize(mouseFromPlayer), Vector2.Normalize(targetFromPlayer));
                        return dotProduct > 0.8f; // ~35 degrees - more strict for on-screen targets
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _core.LogError($"IsCursorPointingTowardsTarget error: {ex.Message}");
                return false; // Default to false on error to force cursor repositioning
            }
        }

        /// <summary>
        /// NEW: Executes a link skill task (FlameLink or ProtectiveLink)
        /// </summary>
        private void ExecuteLinkSkillTask(TaskNode task)
        {
            try
            {
                // Check if target entity is still valid
                if (task.TargetEntity == null || !task.TargetEntity.IsValid)
                {
                    _core.LogMessage($"SKILL TASK: {task.SkillName} target entity invalid, removing task");
                    _taskManager.RemoveTask(task);
                    return;
                }

                // Position cursor on target entity
                var targetScreenPos = Helper.WorldToValidScreenPosition(task.TargetEntity.Pos);
                Mouse.SetCursorPos(targetScreenPos);
                
                _core.LogMessage($"SKILL TASK: Positioned cursor on {task.SkillData?.TargetPlayerName} at ({targetScreenPos.X:F1}, {targetScreenPos.Y:F1})");
                
                // Small delay to ensure cursor positioning
                System.Threading.Thread.Sleep(50);
                
                // Press the skill key
                if (task.SkillKey != default(System.Windows.Forms.Keys))
                {
                    Keyboard.KeyPress(task.SkillKey);
                    _core.LogMessage($"SKILL TASK: Executed {task.SkillName.ToUpper()} on {task.SkillData?.TargetPlayerName} ({task.SkillData?.Reason})");
                    
                    // Record skill use for cooldown tracking
                    _core.RecordSkillUse("Links");
                    
                    // Update last link time in the Links class
                    // This prevents the old Execute() method from double-linking
                    var timerKey = $"{task.SkillData?.TargetPlayerName}_{task.SkillName}";
                    // Note: We'll need to access the Links._lastLinkTime dictionary, but for now we'll rely on cooldown
                }
                else
                {
                    _core.LogMessage($"SKILL TASK: {task.SkillName} has invalid skill key, removing task");
                }
                
                // Remove task after execution
                _taskManager.RemoveTask(task);
            }
            catch (Exception ex)
            {
                _core.LogError($"SKILL TASK: Error executing {task.SkillName}: {ex.Message}");
                _taskManager.RemoveTask(task);
            }
        }
    }
}
