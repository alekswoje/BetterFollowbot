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
        private readonly object _lock = new object();
        private readonly IFollowbotCore _core;

        public TaskManager(IFollowbotCore core)
        {
            _core = core;
        }

        #region ITaskManager Implementation

        public IReadOnlyList<TaskNode> Tasks
        {
            get
            {
                lock (_lock)
                {
                    return _tasks.ToList(); // Return a snapshot to avoid enumeration issues
                }
            }
        }

        public int TaskCount
        {
            get
            {
                lock (_lock)
                {
                    return _tasks.Count;
                }
            }
        }

        public void AddTask(TaskNode task)
        {
            if (task != null)
            {
                lock (_lock)
                {
                    _tasks.Add(task);
                    _core.LogMessage($"TASK CREATION: Added {task.Type} task at {task.WorldPosition} (Total tasks: {_tasks.Count})");
                }
            }
        }

        public bool RemoveTask(TaskNode task)
        {
            if (task != null)
            {
                lock (_lock)
                {
                    return _tasks.Remove(task);
                }
            }
            return false;
        }

        public TaskNode GetAndRemoveFirstTask()
        {
            lock (_lock)
            {
                if (_tasks.Count > 0)
                {
                    var task = _tasks[0];
                    _tasks.RemoveAt(0);
                    return task;
                }
                return null;
            }
        }

        public void ClearTasks()
        {
            lock (_lock)
            {
                _tasks.Clear();
            }
        }

        public void ClearTasksPreservingTransitions()
        {
            lock (_lock)
            {
                var transitionTasks = _tasks.Where(t =>
                    t.Type == TaskNodeType.Transition ||
                    t.Type == TaskNodeType.TeleportConfirm ||
                    t.Type == TaskNodeType.TeleportButton).ToList();

                _tasks.Clear();

                foreach (var transitionTask in transitionTasks)
                {
                    _tasks.Add(transitionTask);
                    _core.LogMessage($"ZONE TRANSITION: Preserved {transitionTask.Type} task during efficiency clear");
                }
            }
        }

        public bool HasTransitionTasks()
        {
            lock (_lock)
            {
                return _tasks.Any(t =>
                    t.Type == TaskNodeType.Transition ||
                    t.Type == TaskNodeType.TeleportConfirm ||
                    t.Type == TaskNodeType.TeleportButton);
            }
        }

        public bool ExecuteNextTask()
        {
            return false;
        }

        #endregion

        #region Additional Task Management Methods

        /// <summary>
        /// Remove all tasks of a specific type
        /// </summary>
        public void RemoveTasksOfType(TaskNodeType taskType)
        {
            lock (_lock)
            {
                _tasks.RemoveAll(t => t.Type == taskType);
            }
        }

        /// <summary>
        /// Get first task of a specific type without removing it
        /// </summary>
        public TaskNode GetFirstTaskOfType(TaskNodeType taskType)
        {
            lock (_lock)
            {
                return _tasks.FirstOrDefault(t => t.Type == taskType);
            }
        }

        /// <summary>
        /// Check if there are any tasks of a specific type
        /// </summary>
        public bool HasTasksOfType(TaskNodeType taskType)
        {
            lock (_lock)
            {
                return _tasks.Any(t => t.Type == taskType);
            }
        }

        /// <summary>
        /// Remove task at specific index
        /// </summary>
        public void RemoveTaskAt(int index)
        {
            lock (_lock)
            {
                if (index >= 0 && index < _tasks.Count)
                {
                    _tasks.RemoveAt(index);
                }
            }
        }

        /// <summary>
        /// Remove all tasks matching a predicate
        /// </summary>
        public void RemoveTaskAll(System.Func<TaskNode, bool> predicate)
        {
            lock (_lock)
            {
                _tasks.RemoveAll(new System.Predicate<TaskNode>(predicate));
            }
        }

        /// <summary>
        /// Count tasks matching a predicate
        /// </summary>
        public int CountTasks(System.Func<TaskNode, bool> predicate)
        {
            lock (_lock)
            {
                return _tasks.Count(predicate);
            }
        }

        #endregion
    }
}
