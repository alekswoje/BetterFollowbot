using System;
using BetterFollowbotLite.Interfaces;
using ExileCore;
using SharpDX;

namespace BetterFollowbotLite.Core.Movement
{
    /// <summary>
    /// Handles execution of movement tasks including dashing and key presses
    /// </summary>
    public class MovementExecutor : IMovementExecutor
    {
        private readonly IFollowbotCore _core;
        private readonly IPathfinding _pathfinding;
        private DateTime _lastDashTime = DateTime.MinValue;
        private Vector3 _lastPlayerPosition = Vector3.Zero;

        public MovementExecutor(IFollowbotCore core, IPathfinding pathfinding)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _pathfinding = pathfinding ?? throw new ArgumentNullException(nameof(pathfinding));
        }

        #region IMovementExecutor Implementation

        public bool ExecuteMovementTask(Vector3 targetPosition, float taskDistance, object followTarget, Vector3 followTargetPosition)
        {
            bool shouldMovementContinue = false;
            bool screenPosError = false;
            bool keyDownError = false;
            bool keyUpError = false;
            bool shouldDashToLeader = false;
            bool shouldTerrainDash = false;

            // Check for distance-based dashing to keep up with leader
            if (_core.Settings.autoPilotDashEnabled && followTarget != null && followTargetPosition != null &&
                (DateTime.Now - _lastDashTime).TotalMilliseconds >= 3000)
            {
                try
                {
                    var distanceToLeader = Vector3.Distance(_core.PlayerPosition, followTargetPosition);
                    if (distanceToLeader > _core.Settings.autoPilotDashDistance && IsCursorPointingTowardsTarget(followTargetPosition))
                    {
                        shouldDashToLeader = true;
                    }
                }
                catch (Exception e)
                {
                    // Error handling without logging
                }
            }

            // Check for terrain-based dashing
            if (_core.Settings.autoPilotDashEnabled && (DateTime.Now - _lastDashTime).TotalMilliseconds >= 3000)
            {
                // Convert world position to grid coordinates (Vector2 from Vector3 X,Z)
                var gridTargetPosition = new Vector2(targetPosition.X, targetPosition.Z);
                if (_pathfinding.CheckDashTerrain(gridTargetPosition) && IsCursorPointingTowardsTarget(targetPosition))
                {
                    shouldTerrainDash = true;
                    _lastDashTime = DateTime.Now;
                }
            }

            // Skip movement logic if dashing
            if (!shouldDashToLeader && !shouldTerrainDash)
            {
                try
                {
                    // Convert world position to screen position for mouse movement
                    var movementScreenPos = BetterFollowbotLite.Helper.WorldToValidScreenPosition(targetPosition);
                    // Position mouse cursor at target location
                    BetterFollowbotLite.Mouse.SetCursorPosHuman(movementScreenPos);
                }
                catch (Exception e)
                {
                    screenPosError = true;
                }

                if (!screenPosError)
                {
                    try
                    {
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

                    // Within bounding range. Task is complete
                    if (taskDistance <= _core.Settings.autoPilotPathfindingNodeDistance.Value * 1.5)
                    {
                        _lastPlayerPosition = _core.PlayerPosition;
                    }

                    shouldMovementContinue = true;
                }
            }

            return shouldMovementContinue;
        }

        public bool IsCursorPointingTowardsTarget(Vector3 targetPosition)
        {
            try
            {
                var mousePos = _core.GetMousePosition();
                var playerPos = _core.PlayerPosition;
                var targetPos = targetPosition;

                // Convert world positions to screen space for angle calculation
                var playerScreen = BetterFollowbotLite.Helper.WorldToValidScreenPosition(playerPos);
                var targetScreen = BetterFollowbotLite.Helper.WorldToValidScreenPosition(targetPos);

                // Calculate vectors
                var toTarget = targetScreen - playerScreen;
                var toMouse = mousePos - playerScreen;

                // Normalize vectors
                var toTargetLength = Math.Sqrt(toTarget.X * toTarget.X + toTarget.Y * toTarget.Y);
                var toMouseLength = Math.Sqrt(toMouse.X * toMouse.X + toMouse.Y * toMouse.Y);

                if (toTargetLength == 0 || toMouseLength == 0)
                    return false;

                toTarget.X /= (float)toTargetLength;
                toTarget.Y /= (float)toTargetLength;
                toMouse.X /= (float)toMouseLength;
                toMouse.Y /= (float)toMouseLength;

                // Calculate dot product for angle
                var dotProduct = toTarget.X * toMouse.X + toTarget.Y * toMouse.Y;

                // Allow some tolerance (about 30 degrees)
                return dotProduct > 0.866; // cos(30°) ≈ 0.866
            }
            catch
            {
                return false;
            }
        }

        public void ResetMovementState()
        {
            _lastDashTime = DateTime.MinValue;
            _lastPlayerPosition = Vector3.Zero;
        }

        #endregion
    }
}
