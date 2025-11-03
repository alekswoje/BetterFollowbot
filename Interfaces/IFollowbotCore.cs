using System;
using System.Collections.Generic;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BetterFollowbot.Interfaces
{
    /// <summary>
    /// Core interface for the main followbot functionality
    /// </summary>
    public interface IFollowbotCore
    {
        /// <summary>
        /// Plugin settings
        /// </summary>
        BetterFollowbotSettings Settings { get; }

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
        /// Pathfinding system
        /// </summary>
        IPathfinding Pathfinding { get; }
        
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

        /// <summary>
        /// Player buffs
        /// </summary>
        List<Buff> Buffs { get; }

        /// <summary>
        /// Enemy entities
        /// </summary>
        List<Entity> Enemys { get; }

        /// <summary>
        /// Summons manager
        /// </summary>
        Summons Summons { get; }

        /// <summary>
        /// Get monster count within distance by rarity
        /// </summary>
        int GetMonsterWithin(float maxDistance, MonsterRarity rarity);

        /// <summary>
        /// Check if leader is in the same zone as the player
        /// </summary>
        bool IsLeaderInSameZone();

        /// <summary>
        /// Set post-respawn waiting state
        /// </summary>
        void SetWaitingForLeaderAfterRespawn(bool waiting);

        /// <summary>
        /// Check if we should block transition tasks due to post-respawn waiting
        /// </summary>
        bool ShouldBlockTransitionTasks();

        /// <summary>
        /// Record skill usage for tracking purposes
        /// </summary>
        void RecordSkillUse(string skillName);
    }
}
