using System;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using SharpDX;

namespace BetterFollowbotLite.Interfaces
{
    /// <summary>
    /// Core interface for the main followbot functionality
    /// </summary>
    public interface IFollowbotCore
    {
        /// <summary>
        /// Plugin settings
        /// </summary>
        BetterFollowbotLiteSettings Settings { get; }
        
        /// <summary>
        /// Current player position
        /// </summary>
        Vector3 PlayerPosition { get; }
        
        /// <summary>
        /// Local player entity
        /// </summary>
        Entity LocalPlayer { get; }

        /// <summary>
        /// Game controller
        /// </summary>
        GameController GameController { get; }
        
        /// <summary>
        /// Check if global cooldown allows action
        /// </summary>
        bool Gcd();
        
        /// <summary>
        /// Log a message
        /// </summary>
        void LogMessage(string message);
        
        /// <summary>
        /// Log an error message
        /// </summary>
        void LogError(string message);
        
        /// <summary>
        /// Get current mouse position
        /// </summary>
        Vector2 GetMousePosition();
        
        /// <summary>
        /// Update last action time for GCD
        /// </summary>
        DateTime LastTimeAny { get; set; }
    }
}
