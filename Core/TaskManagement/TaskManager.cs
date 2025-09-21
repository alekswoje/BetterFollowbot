using System.Collections.Generic;
using System.Linq;
using BetterFollowbotLite.Interfaces;

namespace BetterFollowbotLite.Core.TaskManagement
{
    /// <summary>
    /// Manages the task queue for AutoPilot movement and actions
    /// </summary>
    public class TaskManager : ITaskManager
    {
        private readonly List<TaskNode> _tasks = new List<TaskNode>();
        private readonly IFollowbotCore _core;

        public TaskManager(IFollowbotCore core)
        {
            _core = core;
        }

        #region ITaskManager Implementation

        public IReadOnlyList<TaskNode> Tasks => _tasks;

        public int TaskCount => _tasks.Count;

        public void AddTask(TaskNode task)
        {
            if (task != null)
            {
                _tasks.Add(task);
            }
        }

        public bool RemoveTask(TaskNode task)
        {
            if (task != null)
            {
                return _tasks.Remove(task);
            }
            return false;
        }

        public TaskNode GetAndRemoveFirstTask()
        {
            if (_tasks.Count > 0)
            {
                var task = _tasks[0];
                _tasks.RemoveAt(0);
                return task;
            }
            return null;
        }

        public void ClearTasks()
        {
            _tasks.Clear();
        }

        public void ClearTasksPreservingTransitions()
        {
            // CRITICAL: Preserve ALL transition-related tasks during efficiency clears
            // This includes portal transitions, teleport confirmations, and teleport buttons
            var transitionTasks = _tasks.Where(t =>
                t.Type == TaskNodeType.Transition ||
                t.Type == TaskNodeType.TeleportConfirm ||
                t.Type == TaskNodeType.TeleportButton).ToList();

            _tasks.Clear();

            // Re-add transition tasks to preserve zone transition functionality
            foreach (var transitionTask in transitionTasks)
            {
                _tasks.Add(transitionTask);
                _core.LogMessage($"ZONE TRANSITION: Preserved {transitionTask.Type} task during efficiency clear");
            }
        }

        public bool HasTransitionTasks()
        {
            return _tasks.Any(t => 
                t.Type == TaskNodeType.Transition || 
                t.Type == TaskNodeType.TeleportConfirm || 
                t.Type == TaskNodeType.TeleportButton);
        }

        public bool ExecuteNextTask()
        {
            // This method is for interface compliance but task execution 
            // remains in the original location to preserve exact behavior
            return false;
        }

        #endregion

        #region Additional Task Management Methods

        /// <summary>
        /// Remove all tasks of a specific type
        /// </summary>
        public void RemoveTasksOfType(TaskNodeType taskType)
        {
            _tasks.RemoveAll(t => t.Type == taskType);
        }

        /// <summary>
        /// Get first task of a specific type without removing it
        /// </summary>
        public TaskNode GetFirstTaskOfType(TaskNodeType taskType)
        {
            return _tasks.FirstOrDefault(t => t.Type == taskType);
        }

        /// <summary>
        /// Check if there are any tasks of a specific type
        /// </summary>
        public bool HasTasksOfType(TaskNodeType taskType)
        {
            return _tasks.Any(t => t.Type == taskType);
        }

        /// <summary>
        /// Remove task at specific index
        /// </summary>
        public void RemoveTaskAt(int index)
        {
            if (index >= 0 && index < _tasks.Count)
            {
                _tasks.RemoveAt(index);
            }
        }

        /// <summary>
        /// Remove all tasks matching a predicate
        /// </summary>
        public void RemoveTaskAll(System.Func<TaskNode, bool> predicate)
        {
            _tasks.RemoveAll(predicate);
        }

        /// <summary>
        /// Count tasks matching a predicate
        /// </summary>
        public int CountTasks(System.Func<TaskNode, bool> predicate)
        {
            return _tasks.Count(predicate);
        }

        #endregion
    }
}
