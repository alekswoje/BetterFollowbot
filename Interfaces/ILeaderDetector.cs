using ExileCore.PoEMemory.MemoryObjects;

namespace BetterFollowbot.Interfaces
{
    /// <summary>
    /// Interface for detecting and tracking the party leader
    /// </summary>
    public interface ILeaderDetector
    {
        /// <summary>
        /// Name of the current leader being followed
        /// </summary>
        string LeaderName { get; }
        
        /// <summary>
        /// Current leader entity (null if not found)
        /// </summary>
        Entity LeaderEntity { get; }
        
        /// <summary>
        /// Leader's party element information
        /// </summary>
        PartyElementWindow LeaderPartyElement { get; }
        
        /// <summary>
        /// Check if leader is currently in a different zone
        /// </summary>
        bool IsLeaderInDifferentZone { get; }
        
        /// <summary>
        /// Set the leader name to follow
        /// </summary>
        void SetLeaderName(string leaderName);
        
        /// <summary>
        /// Find the leader entity in the current area
        /// </summary>
        Entity FindLeaderEntity();
        
        /// <summary>
        /// Get the leader's party element information
        /// </summary>
        PartyElementWindow GetLeaderPartyElement();
        
        /// <summary>
        /// Update leader detection and caching
        /// </summary>
        void UpdateLeaderDetection();
        
        /// <summary>
        /// Clear cached leader information (used on area change)
        /// </summary>
        void ClearLeaderCache();
        
        /// <summary>
        /// Check if the leader is valid and accessible
        /// </summary>
        bool IsLeaderValid();
    }
}
