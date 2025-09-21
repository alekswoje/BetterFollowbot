using System.Collections.Generic;
using ExileCore.PoEMemory.MemoryObjects;
using SharpDX;

namespace BetterFollowbotLite.Interfaces
{
    /// <summary>
    /// Interface for AutoPilot movement and pathfinding functionality
    /// </summary>
    public interface IAutoPilot
    {
        /// <summary>
        /// Current follow target entity
        /// </summary>
        Entity FollowTarget { get; }
        
        /// <summary>
        /// Last known position of the follow target
        /// </summary>
        Vector3 FollowTargetPosition { get; }
        
        /// <summary>
        /// Read-only access to current tasks
        /// </summary>
        IReadOnlyList<TaskNode> Tasks { get; }
        
        /// <summary>
        /// Check if teleport is currently in progress
        /// </summary>
        bool IsTeleportInProgress { get; }
        
        /// <summary>
        /// Set the entity to follow
        /// </summary>
        void SetFollowTarget(Entity target);
        
        /// <summary>
        /// Update the position of the follow target
        /// </summary>
        void UpdateFollowTargetPosition();
        
        /// <summary>
        /// Update the main autopilot logic
        /// </summary>
        void UpdateAutoPilotLogic();
        
        /// <summary>
        /// Handle area change events
        /// </summary>
        void AreaChange();
        
        /// <summary>
        /// Render autopilot information
        /// </summary>
        void Render();
        
        /// <summary>
        /// Get and remove the first task from the queue
        /// </summary>
        TaskNode GetAndRemoveFirstTask();
    }
}
