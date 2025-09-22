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
using Vector2i = GameOffsets.Native.Vector2i;

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
        private TerrainData _terrainMetadata;
        private float[][] _heightData; // Height data like in Radar
        private int[][] _processedTerrainData;
        private int[][] _processedTerrainTargetingData;
        private PathFinder _pathFinder;
        private int _dimension1;
        private int _dimension2;
        private DateTime _lastPathfindTime = DateTime.MinValue;

        // Constants from Radar
        private const int TileToGridConversion = 23;
        private const int TileToWorldConversion = 250;
        public const float GridToWorldMultiplier = TileToWorldConversion / (float)TileToGridConversion;

        public Pathfinding(IFollowbotCore core, ITerrainAnalyzer terrainAnalyzer)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _terrainAnalyzer = terrainAnalyzer ?? throw new ArgumentNullException(nameof(terrainAnalyzer));
        }

        #region IPathfinding Implementation

        public int TerrainCols => _processedTerrainData?[0]?.Length ?? 0;
        public int TerrainRows => _processedTerrainData?.Length ?? 0;
        public bool IsTerrainLoaded => _pathFinder != null;

        public float[][] GetHeightData()
        {
            return _heightData;
        }

        public void InitializeTerrain()
        {
            try
            {
                _terrainMetadata = BetterFollowbotLite.Instance.GameController.IngameState.Data.DataStruct.Terrain;
                _heightData = BetterFollowbotLite.Instance.GameController.IngameState.Data.RawTerrainHeightData;
                _processedTerrainData = BetterFollowbotLite.Instance.GameController.IngameState.Data.RawPathfindingData;
                _processedTerrainTargetingData = BetterFollowbotLite.Instance.GameController.IngameState.Data.RawTerrainTargetingData;

                if (_processedTerrainData == null)
                {
                    _core.LogError("PATHFINDING: RawPathfindingData is null!");
                    _pathFinder = null;
                    return;
                }

                _dimension1 = _processedTerrainData.Length;
                _dimension2 = _processedTerrainData[0].Length;

                // Create PathFinder like Radar does
                var pathableValues = new[] { 1, 2, 3, 4, 5 };
                _pathFinder = new PathFinder(_processedTerrainData, pathableValues);

                _core.LogMessage($"PATHFINDING: Terrain initialized - {_dimension1}x{_dimension2} grid");
                _core.LogMessage($"PATHFINDING: Height data available: {_heightData != null}");
            }
            catch (Exception ex)
            {
                _core.LogError($"PATHFINDING: Error initializing terrain: {ex.Message}");
                _pathFinder = null;
            }
        }

        public bool CheckDashTerrain(Vector2 targetPosition)
        {
            if (_grid == null)
                return false;

            // Convert world position to grid coordinates
            var gridPos = WorldToGrid(targetPosition); // Target position

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

        private bool IsTilePathable(Vector2i tile)
        {
            if (_pathFinder == null) return false;

            if (tile.X < 0 || tile.X >= _dimension2)
                return false;

            if (tile.Y < 0 || tile.Y >= _dimension1)
                return false;

            // Use PathFinder's walkability check (same as Radar)
            return _processedTerrainData[tile.Y][tile.X] is 5 or 4;
        }

        #region Utility Methods

        public List<Vector2i> GetPath(Vector3 startWorld, Vector3 targetWorld, ExileCore.PoEMemory.MemoryObjects.Entity targetEntity = null)
        {
            // Prevent pathfinding spam - limit to once per 500ms
            var timeSinceLastPathfind = DateTime.Now - _lastPathfindTime;
            if (timeSinceLastPathfind.TotalMilliseconds < 500)
            {
                return new List<Vector2i>(); // Return empty path during cooldown
            }
            _lastPathfindTime = DateTime.Now;

            if (_pathFinder == null)
            {
                _core.LogMessage("A* DEBUG: PathFinder not initialized");
                return new List<Vector2i>();
            }

            var startGrid = WorldToGrid(startWorld, true);  // Player position
            var targetGrid = WorldToGrid(targetWorld, false, targetEntity); // Target position

            _core.LogMessage($"A* DEBUG: Finding path from grid ({startGrid.X}, {startGrid.Y}) to ({targetGrid.X}, {targetGrid.Y})");

            // Check if target position is walkable - if not, find nearest walkable tile
            if (!IsTilePathable(targetGrid))
            {
                _core.LogMessage($"A* DEBUG: Target position ({targetGrid.X}, {targetGrid.Y}) is not walkable, finding nearest walkable tile...");
                var nearestWalkable = FindNearestWalkableTile(targetGrid);
                if (nearestWalkable != null)
                {
                    targetGrid = nearestWalkable.Value;
                    _core.LogMessage($"A* DEBUG: Using nearest walkable tile ({targetGrid.X}, {targetGrid.Y}) instead of target");
                }
                else
                {
                    _core.LogMessage($"A* DEBUG: No walkable tile found near target position!");
                    return new List<Vector2i>();
                }
            }

            // Check if start and target are the same
            if (startGrid == targetGrid)
            {
                _core.LogMessage($"A* DEBUG: Start and target are the same, returning empty path");
                return new List<Vector2i>();
            }

            // Use Radar's approach: RunFirstScan to precompute, then FindPath to get the path
            _core.LogMessage($"A* DEBUG: Running first scan for target {targetGrid}...");

            // Run first scan to precompute pathfinding data
            var scanResults = _pathFinder.RunFirstScan(startGrid, targetGrid).ToList();
            if (scanResults.Any(path => path != null && path.Count > 0))
            {
                var path = scanResults.First(path => path != null && path.Count > 0);
                _core.LogMessage($"A* DEBUG: First scan found path with {path.Count} waypoints");
                return path;
            }

            // If first scan didn't find a path, try FindPath
            _core.LogMessage($"A* DEBUG: First scan found no path, trying FindPath...");
            var finalPath = _pathFinder.FindPath(startGrid, targetGrid);

            if (finalPath != null)
            {
                _core.LogMessage($"A* DEBUG: FindPath found path with {finalPath.Count} waypoints");
            }
            else
            {
                _core.LogMessage($"A* DEBUG: No path found by any method");
            }

            return finalPath ?? new List<Vector2i>();
        }

        public void ClearPathCache()
        {
            _pathCache.Clear();
        }

        private Vector2i WorldToGrid(Vector3 worldPos, bool isPlayerPosition = false, ExileCore.PoEMemory.MemoryObjects.Entity targetEntity = null)
        {
            try
            {
                // For player position, try to get grid position from the Positioned component first (more accurate)
                if (isPlayerPosition)
                {
                    var localPlayer = BetterFollowbotLite.Instance.localPlayer;
                    if (localPlayer != null)
                    {
                        var positioned = localPlayer.GetComponent<Positioned>();
                        if (positioned != null)
                        {
                            var gridPos = new Vector2i(positioned.GridX, positioned.GridY);
                            _core.LogMessage($"PATHFINDING: Using Positioned component grid coords: ({gridPos.X}, {gridPos.Y})");
                            return gridPos;
                        }
                    }
                }

                // For target positions, use the provided entity if available
                if (targetEntity != null && targetEntity.IsValid)
                {
                    var positioned = targetEntity.GetComponent<Positioned>();
                    if (positioned != null)
                    {
                            var gridPos = new Vector2i(positioned.GridX, positioned.GridY);
                            _core.LogMessage($"PATHFINDING: Using target entity Positioned component grid coords: ({gridPos.X}, {gridPos.Y})");

                            // Check if this position is within terrain bounds and walkable
                            if (gridPos.X >= 0 && gridPos.X < _dimension2 && gridPos.Y >= 0 && gridPos.Y < _dimension1)
                            {
                                var terrainValue = _processedTerrainData[gridPos.Y][gridPos.X];
                                var isWalkable = terrainValue is 5 or 4;
                                _core.LogMessage($"PATHFINDING: Target position terrain check: coords({gridPos.X},{gridPos.Y}) terrain={terrainValue}, walkable={isWalkable}");
                            }
                            else
                            {
                                _core.LogMessage($"PATHFINDING: Target position OUT OF BOUNDS: coords({gridPos.X},{gridPos.Y}) vs terrain({_dimension2}x{_dimension1})");
                            }

                            return gridPos;
                    }
                }

                // Fallback to manual conversion (similar to Radar's GridToWorldMultiplier)
                const float GridToWorldMultiplier = 250f / 23f; // TileToWorldConversion / TileToGridConversion
                var gridX = (int)(worldPos.X / GridToWorldMultiplier);
                var gridY = (int)(worldPos.Z / GridToWorldMultiplier); // Z is north-south in world space, Y in grid
                _core.LogMessage($"PATHFINDING: Using manual conversion grid coords: ({gridX}, {gridY}) from world ({worldPos.X:F1}, {worldPos.Y:F1}, {worldPos.Z:F1})");
                return new Vector2i(gridX, gridY);
            }
            catch (Exception e)
            {
                _core.LogError($"PATHFINDING: Error in WorldToGrid conversion: {e.Message}");
                // Emergency fallback
                const float GridToWorldMultiplier = 250f / 23f;
                var gridX = (int)(worldPos.X / GridToWorldMultiplier);
                var gridY = (int)(worldPos.Z / GridToWorldMultiplier);
                return new Vector2i(gridX, gridY);
            }
        }

        private Vector2i WorldToGrid(Vector2 worldPos, bool isPlayerPosition = false, ExileCore.PoEMemory.MemoryObjects.Entity targetEntity = null)
        {
            return WorldToGrid(new Vector3(worldPos.X, 0, worldPos.Y), isPlayerPosition, targetEntity);
        }

        private Vector2i? FindNearestWalkableTile(Vector2i center)
        {
            // Search in expanding squares around the center
            for (int radius = 1; radius <= 20; radius++) // Search up to 20 tiles away
            {
                // Check all tiles at this radius
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        // Only check perimeter (corners and edges)
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;

                        var testPos = new Vector2i(center.X + dx, center.Y + dy);

                        // Check bounds
                        if (testPos.X >= 0 && testPos.X < _dimension2 &&
                            testPos.Y >= 0 && testPos.Y < _dimension1 &&
                            IsTilePathable(testPos))
                        {
                            return testPos;
                        }
                    }
                }
            }

            return null; // No walkable tile found
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
