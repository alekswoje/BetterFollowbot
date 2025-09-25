using System.Collections.Generic;
using BetterFollowbot.Core.TaskManagement;

namespace BetterFollowbot.Interfaces
{
    /// <summary>
    /// Interface for managing and executing tasks
    /// </summary>
    public interface ITaskManager
    {
        /// <summary>
        /// Read-only access to current task queue
        /// </summary>
        IReadOnlyList<TaskNode> Tasks { get; }
        
        /// <summary>
        /// Number of tasks currently in queue
        /// </summary>
        int TaskCount { get; }
        
        /// <summary>
        /// Add a task to the queue
        /// </summary>
        void AddTask(TaskNode task);
        
        /// <summary>
        /// Remove a specific task from the queue
        /// </summary>
        bool RemoveTask(TaskNode task);
        
        /// <summary>
        /// Get and remove the first task from the queue
        /// </summary>
        TaskNode GetAndRemoveFirstTask();
        
        /// <summary>
        /// Clear all tasks from the queue
        /// </summary>
        void ClearTasks();
        
        /// <summary>
        /// Clear tasks but preserve transition-related tasks
        /// </summary>
        void ClearTasksPreservingTransitions();
        
        /// <summary>
        /// Check if there are any transition tasks in the queue
        /// </summary>
        bool HasTransitionTasks();
        
        /// <summary>
        /// Execute the next available task
        /// </summary>
        bool ExecuteNextTask();
        
        /// <summary>
        /// Remove task at specific index
        /// </summary>
        void RemoveTaskAt(int index);
        
        /// <summary>
        /// Remove all tasks matching a predicate
        /// </summary>
        void RemoveTaskAll(System.Func<TaskNode, bool> predicate);
        
        /// <summary>
        /// Count tasks matching a predicate
        /// </summary>
        int CountTasks(System.Func<TaskNode, bool> predicate);
    }
}
