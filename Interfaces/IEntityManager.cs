using System.Collections.Generic;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;

namespace BetterFollowbot.Interfaces
{
    /// <summary>
    /// Interface for managing entity access and filtering
    /// </summary>
    public interface IEntityManager
    {
        /// <summary>
        /// Get all entities of a specific type
        /// </summary>
        IEnumerable<Entity> GetEntitiesByType(EntityType entityType);
        
        /// <summary>
        /// Get all player entities in the current area
        /// </summary>
        IEnumerable<Entity> GetPlayerEntities();
        
        /// <summary>
        /// Get all monster entities in the current area
        /// </summary>
        IEnumerable<Entity> GetMonsterEntities();
        
        /// <summary>
        /// Find a player entity by name
        /// </summary>
        Entity FindPlayerByName(string playerName);
        
        /// <summary>
        /// Get entities within a specific distance from a position
        /// </summary>
        IEnumerable<Entity> GetEntitiesInRange(SharpDX.Vector3 position, float range, EntityType entityType = EntityType.Monster);
        
        /// <summary>
        /// Check if an entity is valid and alive
        /// </summary>
        bool IsEntityValid(Entity entity);
        
        /// <summary>
        /// Get all valid entities from the game controller
        /// </summary>
        IEnumerable<Entity> GetAllValidEntities();
        
        /// <summary>
        /// Get hostile entities within range
        /// </summary>
        IEnumerable<Entity> GetHostileEntitiesInRange(SharpDX.Vector3 position, float range);
    }
}
