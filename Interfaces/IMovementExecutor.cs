using System;
using System.Collections;
using BetterFollowbotLite.Core.TaskManagement;
using SharpDX;

namespace BetterFollowbotLite.Interfaces
{
    /// <summary>
    /// Interface for movement task execution functionality
    /// </summary>
    public interface IMovementExecutor
    {
        /// <summary>
        /// Executes a single task and returns an enumerator for coroutine execution
        /// </summary>
        /// <param name="currentTask">The task to execute</param>
        /// <param name="taskDistance">Distance to the task target</param>
        /// <param name="playerDistanceMoved">How much the player has moved since last tick</param>
        /// <returns>Enumerator for coroutine execution</returns>
        IEnumerable ExecuteTask(TaskNode currentTask, float taskDistance, float playerDistanceMoved);

        /// <summary>
        /// Checks if the cursor is pointing towards the target position
        /// </summary>
        /// <param name="targetPosition">Target position to check</param>
        /// <returns>True if cursor is pointing towards target</returns>
        bool IsCursorPointingTowardsTarget(Vector3 targetPosition);

        /// <summary>
        /// Checks if the path should be cleared for better responsiveness (180-degree turn detection)
        /// </summary>
        /// <param name="aggressiveTiming">Use more aggressive timing for override checks</param>
        /// <returns>True if path should be cleared</returns>
        bool ShouldClearPathForResponsiveness(bool aggressiveTiming = false);

        /// <summary>
        /// Gets the last dash time for cooldown tracking
        /// </summary>
        DateTime LastDashTime { get; }

        /// <summary>
        /// Sets the last dash time for cooldown tracking
        /// </summary>
        void UpdateLastDashTime(DateTime time);
    }
}
