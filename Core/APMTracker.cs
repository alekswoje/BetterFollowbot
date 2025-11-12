using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterFollowbot.Core
{
    /// <summary>
    /// Tracks Actions Per Minute (APM) for all bot inputs
    /// </summary>
    public class APMTracker
    {
        private static readonly List<DateTime> _actionTimestamps = new List<DateTime>();
        private static readonly object _lock = new object();
        
        /// <summary>
        /// Records a single action (key press, mouse click, etc.)
        /// </summary>
        public static void RecordAction()
        {
            lock (_lock)
            {
                _actionTimestamps.Add(DateTime.Now);
                
                // Clean up old timestamps (older than 1 minute)
                CleanupOldActions();
            }
        }
        
        /// <summary>
        /// Records multiple actions at once
        /// </summary>
        public static void RecordActions(int count)
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                for (int i = 0; i < count; i++)
                {
                    _actionTimestamps.Add(now);
                }
                
                CleanupOldActions();
            }
        }
        
        /// <summary>
        /// Gets the current APM (actions in the last 60 seconds)
        /// </summary>
        public static int GetCurrentAPM()
        {
            lock (_lock)
            {
                CleanupOldActions();
                return _actionTimestamps.Count;
            }
        }
        
        /// <summary>
        /// Gets the average APM over different time windows
        /// </summary>
        public static (int last10s, int last30s, int last60s) GetAPMBreakdown()
        {
            lock (_lock)
            {
                CleanupOldActions();
                var now = DateTime.Now;
                
                var last10s = _actionTimestamps.Count(t => (now - t).TotalSeconds <= 10) * 6; // Multiply by 6 to get per-minute rate
                var last30s = _actionTimestamps.Count(t => (now - t).TotalSeconds <= 30) * 2; // Multiply by 2 to get per-minute rate
                var last60s = _actionTimestamps.Count; // Already per minute
                
                return (last10s, last30s, last60s);
            }
        }
        
        /// <summary>
        /// Removes action timestamps older than 60 seconds
        /// </summary>
        private static void CleanupOldActions()
        {
            var cutoffTime = DateTime.Now.AddSeconds(-60);
            _actionTimestamps.RemoveAll(t => t < cutoffTime);
        }
        
        /// <summary>
        /// Resets all tracked actions (useful for testing)
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _actionTimestamps.Clear();
            }
        }
    }
}

