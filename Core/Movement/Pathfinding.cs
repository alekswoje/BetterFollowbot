using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using BetterFollowbotLite.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using GameOffsets;
using GameOffsets.Native;
using SharpDX;

namespace BetterFollowbotLite.Core.Movement
{
    /// <summary>
    /// Handles A* pathfinding and terrain analysis for movement
    /// Based on Radar plugin's implementation
    /// </summary>
    public class Pathfinding : IPathfinding
    {
        private readonly IFollowbotCore _core;
        private readonly ITerrainAnalyzer _terrainAnalyzer;
        private bool[][] _grid;
        private TerrainData _terrainMetadata;
        private int[][] _processedTerrainData;
        private int[][] _processedTerrainTargetingData;
        private int _dimension2;
        private int _dimension1;

        // Path finding
        private PathFinder _pathFinder;
        private ConcurrentDictionary<string, List<Vector2i>> _pathCache = new();

        public Pathfinding(IFollowbotCore core, ITerrainAnalyzer terrainAnalyzer)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _terrainAnalyzer = terrainAnalyzer ?? throw new ArgumentNullException(nameof(terrainAnalyzer));
        }

        #region IPathfinding Implementation

        public int TerrainCols => _dimension2;
        public int TerrainRows => _dimension1;
        public bool IsTerrainLoaded => _grid != null;

        public void InitializeTerrain()
        {
            // Clear caches when terrain is reinitialized
            ClearPathCache();

            try
            {
                _terrainMetadata = BetterFollowbotLite.Instance.GameController.IngameState.Data.DataStruct.Terrain;
                var terrainBytes = BetterFollowbotLite.Instance.GameController.Memory.ReadBytes(_terrainMetadata.LayerMelee.First, _terrainMetadata.LayerMelee.Size);

                var numCols = (int)(_terrainMetadata.NumCols - 1) * 23;
                var numRows = (int)(_terrainMetadata.NumRows - 1) * 23;
                if ((numCols & 1) > 0)
                    numCols++;

                _dimension1 = numRows;
                _dimension2 = numCols;

                _core.LogMessage($"PATHFINDING: Terrain dimensions - Cols: {_dimension2}, Rows: {_dimension1}");

                // Initialize grid with walkable values (1, 2, 3, 4, 5 are walkable)
                var pathableValues = new[] { 1, 2, 3, 4, 5 };
                var pv = pathableValues.ToHashSet();

                _processedTerrainData = BetterFollowbotLite.Instance.GameController.IngameState.Data.RawPathfindingData;
                if (_processedTerrainData == null)
                {
                    _core.LogError("PATHFINDING: RawPathfindingData is null!");
                    _grid = null;
                    return;
                }

                _core.LogMessage($"PATHFINDING: Processing {_processedTerrainData.Length} terrain rows");
                if (_processedTerrainData.Length == 0)
                {
                    _core.LogError("PATHFINDING: RawPathfindingData is empty!");
                    _grid = null;
                    return;
                }

                _grid = _processedTerrainData.Select(x => x.Select(y => pv.Contains(y)).ToArray()).ToArray();

                _processedTerrainTargetingData = BetterFollowbotLite.Instance.GameController.IngameState.Data.RawTerrainTargetingData;

                // Initialize Radar-style PathFinder
                try
                {
                    _core.LogMessage($"PATHFINDING: Creating PathFinder with terrain data dimensions {_processedTerrainData.Length}x{_processedTerrainData[0].Length}");
                    _pathFinder = new PathFinder(_processedTerrainData, pathableValues);
                    if (_pathFinder != null)
                    {
                        _core.LogMessage("PATHFINDING: A* PathFinder initialized successfully");
                    }
                    else
                    {
                        _core.LogError("PATHFINDING: PathFinder constructor returned null!");
                    }
                }
                catch (Exception ex)
                {
                    _core.LogError($"PATHFINDING: Failed to initialize PathFinder: {ex.Message}");
                    _core.LogError($"PATHFINDING: Stack trace: {ex.StackTrace}");
                    _pathFinder = null;
                }

                _core.LogMessage("PATHFINDING: A* terrain data initialized successfully");

                // Count walkable tiles for debugging
                var walkableCount = 0;
                foreach (var row in _grid)
                {
                    foreach (var cell in row)
                    {
                        if (cell) walkableCount++;
                    }
                }
                _core.LogMessage($"PATHFINDING: Found {walkableCount} walkable tiles out of {_dimension1 * _dimension2} total tiles");

                // Check if we have any walkable tiles at all
                if (walkableCount == 0)
                {
                    _core.LogError("PATHFINDING: No walkable tiles found in terrain data!");
                }
            }
            catch (Exception e)
            {
                _core.LogError($"PATHFINDING: Failed to initialize A* terrain data - {e.Message}");
                _grid = null;
            }
        }

        public bool CheckDashTerrain(Vector2 targetPosition)
        {
            if (_grid == null)
                return false;

            // Convert world position to grid coordinates
            var gridPos = WorldToGrid(targetPosition);

            // Delegate terrain analysis to the specialized analyzer
            var shouldDash = _terrainAnalyzer.AnalyzeTerrainForDashing(targetPosition, GetTerrainTileFromGrid);

            if (shouldDash)
            {
                _core.LogMessage("PATHFINDING: Terrain dash conditions met");
                return true;
            }

            return false;
        }

        public byte GetTerrainTile(int x, int y)
        {
            return GetTerrainTileFromGrid(x, y);
        }

        #endregion



        #region Utility Methods

        public List<Vector2i> GetPath(Vector3 startWorld, Vector3 targetWorld)
        {
            _core.LogMessage($"A* DEBUG: GetPath called - Start: {startWorld}, Target: {targetWorld}");

            // Get grid coordinates from the Render components like Radar does
            var player = _core.GameController.Game.IngameState.Data.LocalPlayer;
            var playerRender = player?.GetComponent<Render>();
            if (playerRender == null)
            {
                _core.LogMessage("A* DEBUG: Could not get player Render component");
                return null;
            }

            var startGrid = new Vector2i((int)playerRender.GridPos().X, (int)playerRender.GridPos().Y);
            _core.LogMessage($"A* DEBUG: Player grid pos: ({startGrid.X}, {startGrid.Y})");

            // For target, find the entity at the target position
            var targetEntity = _core.GameController.EntityListWrapper.OnlyValidEntities
                .FirstOrDefault(e => Vector3.Distance(e.Pos, targetWorld) < 1f && e.HasComponent<Render>());
            Vector2i targetGrid;
            if (targetEntity != null)
            {
                var targetRender = targetEntity.GetComponent<Render>();
                targetGrid = new Vector2i((int)targetRender.GridPos().X, (int)targetRender.GridPos().Y);
            }
            else
            {
                // Fallback to world-to-grid conversion for target if entity not found
                targetGrid = WorldToGrid(targetWorld);
                _core.LogMessage($"A* DEBUG: Using fallback grid conversion for target: world {targetWorld} -> grid ({targetGrid.X}, {targetGrid.Y})");
            }

            _core.LogMessage($"A* DEBUG: Finding path from grid ({startGrid.X}, {startGrid.Y}) to ({targetGrid.X}, {targetGrid.Y})");

            // Check bounds
            _core.LogMessage($"A* DEBUG: Grid bounds check - Dimensions: {_dimension2}x{_dimension1}, Start: ({startGrid.X}, {startGrid.Y}), Target: ({targetGrid.X}, {targetGrid.Y})");
            if (startGrid.X < 0 || startGrid.X >= _dimension2 || startGrid.Y < 0 || startGrid.Y >= _dimension1 ||
                targetGrid.X < 0 || targetGrid.X >= _dimension2 || targetGrid.Y < 0 || targetGrid.Y >= _dimension1)
            {
                _core.LogMessage($"A* DEBUG: Grid coordinates out of bounds - Start: ({startGrid.X}, {startGrid.Y}), Target: ({targetGrid.X}, {targetGrid.Y}), Dimensions: {_dimension2}x{_dimension1}");
                return null;
            }

            // Check if start and target are the same
            if (startGrid == targetGrid)
            {
                _core.LogMessage("A* DEBUG: Start and target positions are the same");
                return new List<Vector2i>();
            }

            // Check cache first
            var cacheKey = $"{startGrid.X},{startGrid.Y}->{targetGrid.X},{targetGrid.Y}";
            if (_pathCache.TryGetValue(cacheKey, out var cachedPath))
            {
                _core.LogMessage($"A* DEBUG: Using cached path with {cachedPath.Count} waypoints");
                return cachedPath;
            }

            // Use Radar-style path finding
            if (_pathFinder == null)
            {
                _core.LogMessage("A* DEBUG: PathFinder not initialized");
                return null;
            }

            // Run first scan to populate direction/exact distance fields
            var scanResults = _pathFinder.RunFirstScan(startGrid, targetGrid).ToList();
            if (scanResults.Any() && scanResults.First().Any())
            {
                var path = scanResults.First();
                _pathCache[cacheKey] = path;
                _core.LogMessage($"A* DEBUG: Found path with {path.Count} waypoints");
                return path;
            }

            // If first scan didn't find a path, try direct FindPath
            var directPath = _pathFinder.FindPath(startGrid, targetGrid);
            if (directPath != null)
            {
                _pathCache[cacheKey] = directPath;
                _core.LogMessage($"A* DEBUG: Found direct path with {directPath.Count} waypoints");
                return directPath;
            }

            _core.LogMessage("A* DEBUG: No path found");
            return null;
        }

        public void ClearPathCache()
        {
            _pathCache.Clear();
            _pathFinder = null; // Force recreation of PathFinder
            _core.LogMessage("A* DEBUG: All path caches cleared");
        }

        private Vector2i WorldToGrid(Vector3 worldPos)
        {
            // Convert world position to grid coordinates (similar to Radar's GridToWorldMultiplier)
            const float GridToWorldMultiplier = 250f / 23f; // TileToWorldConversion / TileToGridConversion
            var gridX = (int)(worldPos.X / GridToWorldMultiplier);
            var gridY = (int)(worldPos.Z / GridToWorldMultiplier); // Z is up in world space, Y in grid
            return new Vector2i(gridX, gridY);
        }

        private Vector2i WorldToGrid(Vector2 worldPos)
        {
            return WorldToGrid(new Vector3(worldPos.X, 0, worldPos.Y));
        }

        private byte GetTerrainTileFromGrid(int x, int y)
        {
            if (_processedTerrainData == null || x < 0 || x >= _dimension2 || y < 0 || y >= _dimension1)
                return 255; // Invalid tile

            return (byte)_processedTerrainData[y][x];
        }

        #endregion
    }
}
