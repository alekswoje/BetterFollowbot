using SharpDX;

namespace BetterFollowbot.Interfaces
{
    /// <summary>
    /// Interface for terrain analysis functionality, specifically for terrain dashing
    /// </summary>
    public interface ITerrainAnalyzer
    {
        /// <summary>
        /// Analyze terrain to determine if dashing is possible to the target position
        /// </summary>
        /// <param name="targetPosition">Target position in grid coordinates</param>
        /// <param name="getTerrainTile">Function to get terrain tile value at coordinates</param>
        /// <returns>True if dashing is possible</returns>
        bool AnalyzeTerrainForDashing(Vector2 targetPosition, System.Func<int, int, byte> getTerrainTile);
    }
}
