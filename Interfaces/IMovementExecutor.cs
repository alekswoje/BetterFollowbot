using System;
using SharpDX;

namespace BetterFollowbotLite.Interfaces
{
    /// <summary>
    /// Interface for executing movement-related tasks
    /// </summary>
    public interface IMovementExecutor
    {
        /// <summary>
        /// Execute a movement task with dashing logic and key presses
        /// </summary>
        /// <param name="targetPosition">Target position for movement</param>
        /// <param name="taskDistance">Distance to the task target</param>
        /// <param name="followTarget">Current follow target entity</param>
        /// <param name="followTargetPosition">Position of the follow target</param>
        /// <returns>True if movement should continue</returns>
        bool ExecuteMovementTask(Vector3 targetPosition, float taskDistance, object followTarget, Vector3 followTargetPosition);

        /// <summary>
        /// Check if cursor is pointing towards target for dashing
        /// </summary>
        /// <param name="targetPosition">Target position to check</param>
        /// <returns>True if cursor is pointing towards target</returns>
        bool IsCursorPointingTowardsTarget(Vector3 targetPosition);

        /// <summary>
        /// Reset movement state (dashing timers, etc.)
        /// </summary>
        void ResetMovementState();
    }
}
