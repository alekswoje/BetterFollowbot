using System;
using System.Collections;
using BetterFollowbotLite.Core.TaskManagement;
using SharpDX;

namespace BetterFollowbotLite.Interfaces
{
    /// <summary>
    /// Interface for executing movement tasks and handling different task types
    /// </summary>
    public interface IMovementExecutor
    {
        /// <summary>
        /// Executes a task and returns the execution result
        /// </summary>
        /// <param name="currentTask">The task to execute</param>
        /// <param name="taskDistance">Distance to the task position</param>
        /// <param name="playerDistanceMoved">How far the player has moved since last tick</param>
        /// <returns>Execution result containing flags and data</returns>
        TaskExecutionResult ExecuteTask(TaskNode currentTask, float taskDistance, float playerDistanceMoved);

        /// <summary>
        /// Updates the last dash time for cooldown tracking
        /// </summary>
        /// <param name="dashTime">The time when the dash was executed</param>
        void UpdateLastDashTime(DateTime dashTime);

        /// <summary>
        /// Gets the last dash time for cooldown checking
        /// </summary>
        DateTime LastDashTime { get; }

        /// <summary>
        /// Updates the last player position for distance calculations
        /// </summary>
        /// <param name="position">The current player position</param>
        void UpdateLastPlayerPosition(Vector3 position);
    }

    /// <summary>
    /// Result of task execution containing flags and data for the calling coroutine
    /// </summary>
    public class TaskExecutionResult
    {
        public bool ShouldContinue { get; set; }
        public bool ShouldDashToLeader { get; set; }
        public bool ShouldTerrainDash { get; set; }
        public bool ShouldTransitionAndContinue { get; set; }
        public bool ShouldClaimWaypointAndContinue { get; set; }
        public bool ShouldDashAndContinue { get; set; }
        public bool ShouldTeleportConfirmAndContinue { get; set; }
        public bool ShouldTeleportButtonAndContinue { get; set; }
        public bool ShouldMovementContinue { get; set; }
        public bool ScreenPosError { get; set; }
        public bool KeyDownError { get; set; }
        public bool KeyUpError { get; set; }
        public bool TaskExecutionError { get; set; }

        // Additional data for specific task types
        public Vector2 MovementScreenPos { get; set; }
        public Vector2 TransitionPos { get; set; }
        public Vector2 WaypointScreenPos { get; set; }

        public TaskExecutionResult()
        {
            ShouldContinue = true;
        }
    }
}
