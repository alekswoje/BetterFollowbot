using System.Collections.Generic;
using SharpDX;
using GameOffsets.Native;

namespace BetterFollowbotLite.Interfaces
{
    /// <summary>
    /// Interface for pathfinding and terrain analysis functionality
    /// </summary>
    public interface IPathfinding
    {
        /// <summary>
        /// Gets the number of terrain columns
        /// </summary>
        int TerrainCols { get; }

        /// <summary>
        /// Gets the number of terrain rows
        /// </summary>
        int TerrainRows { get; }

        /// <summary>
        /// Initialize terrain data for the current area
        /// </summary>
        void InitializeTerrain();

        /// <summary>
        /// Check if terrain dashing is possible to the target position
        /// </summary>
        /// <param name="targetPosition">Target position in grid coordinates</param>
        /// <returns>True if dashing is possible</returns>
        bool CheckDashTerrain(Vector2 targetPosition);

        /// <summary>
        /// Get terrain tile value at specific coordinates
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <returns>Terrain tile value</returns>
        byte GetTerrainTile(int x, int y);

        /// <summary>
        /// Check if terrain data is loaded
        /// </summary>
        bool IsTerrainLoaded { get; }

        /// <summary>
        /// Get A* path from start to target world positions
        /// </summary>
        /// <param name="startWorld">Start position in world coordinates</param>
        /// <param name="targetWorld">Target position in world coordinates</param>
        /// <returns>List of grid waypoints or null if no path found</returns>
        List<Vector2i> GetPath(Vector3 startWorld, Vector3 targetWorld);

        /// <summary>
        /// Clear the path cache
        /// </summary>
        void ClearPathCache();
    }
}
