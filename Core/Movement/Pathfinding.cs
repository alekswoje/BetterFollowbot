using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using BetterFollowbotLite.Interfaces;
using ExileCore;
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

        // A* pathfinding data structures
        private ConcurrentDictionary<Vector2i, Dictionary<Vector2i, float>> _exactDistanceField = new();
        private ConcurrentDictionary<Vector2i, byte[][]> _directionField = new();
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

                _grid = _processedTerrainData.Select(x => x.Select(y => pv.Contains(y)).ToArray()).ToArray();

                _processedTerrainTargetingData = BetterFollowbotLite.Instance.GameController.IngameState.Data.RawTerrainTargetingData;

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

        #region A* Pathfinding Methods

        private bool IsTilePathable(Vector2i tile)
        {
            if (tile.X < 0 || tile.X >= _dimension2)
                return false;

            if (tile.Y < 0 || tile.Y >= _dimension1)
                return false;

            return _grid[tile.Y][tile.X];
        }

        private static readonly List<Vector2i> NeighborOffsets = new List<Vector2i>
        {
            new Vector2i(0, 1),
            new Vector2i(1, 1),
            new Vector2i(1, 0),
            new Vector2i(1, -1),
            new Vector2i(0, -1),
            new Vector2i(-1, -1),
            new Vector2i(-1, 0),
            new Vector2i(-1, 1),
        };

        private static IEnumerable<Vector2i> GetNeighbors(Vector2i tile)
        {
            return NeighborOffsets.Select(offset => tile + offset);
        }

        private static float GetExactDistance(Vector2i tile, Dictionary<Vector2i, float> dict)
        {
            return dict.GetValueOrDefault(tile, float.PositiveInfinity);
        }

        public List<Vector2i> FindPath(Vector2i start, Vector2i target)
        {
            if (_directionField.GetValueOrDefault(target) is { } directionField)
            {
                if (directionField[start.Y][start.X] == 0)
                    return null;
                var path = new List<Vector2i>();
                var current = start;
                while (current != target)
                {
                    var directionIndex = directionField[current.Y][current.X];
                    if (directionIndex == 0)
                        return null;

                    var next = NeighborOffsets[directionIndex - 1] + current;
                    path.Add(next);
                    current = next;
                }
                return path;
            }
            else
            {
                var exactDistanceField = _exactDistanceField[target];
                if (float.IsPositiveInfinity(GetExactDistance(start, exactDistanceField)))
                    return null;
                var path = new List<Vector2i>();
                var current = start;
                while (current != target)
                {
                    var next = GetNeighbors(current).MinBy(x => GetExactDistance(x, exactDistanceField));
                    path.Add(next);
                    current = next;
                }
                return path;
            }
        }

        public IEnumerable<List<Vector2i>> RunFirstScan(Vector2i start, Vector2i target)
        {
            if (_directionField.ContainsKey(target))
            {
                yield break;
            }

            if (!_exactDistanceField.TryAdd(target, new Dictionary<Vector2i, float>()))
            {
                yield break;
            }

            var exactDistanceField = _exactDistanceField[target];
            exactDistanceField[target] = 0;
            var localBacktrackDictionary = new Dictionary<Vector2i, Vector2i>();
            var queue = new BinaryHeap<float, Vector2i>();
            queue.Add(0, target);

            void TryEnqueueTile(Vector2i coord, Vector2i previous, float previousScore)
            {
                if (!IsTilePathable(coord))
                    return;

                if (localBacktrackDictionary.ContainsKey(coord))
                    return;

                localBacktrackDictionary.Add(coord, previous);
                var exactDistance = previousScore + coord.DistanceF(previous);
                exactDistanceField.TryAdd(coord, exactDistance);
                queue.Add(exactDistance, coord);
            }

            var sw = Stopwatch.StartNew();

            localBacktrackDictionary.Add(target, target);
            var reversePath = new List<Vector2i>();
            while (queue.TryRemoveTop(out var top))
            {
                var current = top.Value;
                var currentDistance = top.Key;
                if (reversePath.Count == 0 && current.Equals(start))
                {
                    reversePath.Add(current);
                    var it = current;
                    while (it != target && localBacktrackDictionary.TryGetValue(it, out var previous))
                    {
                        reversePath.Add(previous);
                        it = previous;
                    }

                    yield return reversePath;
                }

                foreach (var neighbor in GetNeighbors(current))
                {
                    TryEnqueueTile(neighbor, current, currentDistance);
                }

                if (sw.ElapsedMilliseconds > 100)
                {
                    yield return reversePath;
                    sw.Restart();
                }
            }

            localBacktrackDictionary.Clear();

            if (_dimension1 * _dimension2 < exactDistanceField.Count * (sizeof(int) * 2 + Unsafe.SizeOf<Vector2i>() + Unsafe.SizeOf<float>()))
            {
                var directionGrid = _grid
                    .AsParallel().AsOrdered().Select((r, y) => r.Select((_, x) =>
                    {
                        var coordVec = new Vector2i(x, y);
                        if (float.IsPositiveInfinity(GetExactDistance(coordVec, exactDistanceField)))
                            return (byte)0;

                        var neighbors = GetNeighbors(coordVec);
                        var (closestNeighbor, clndistance) = neighbors.Select(n => (n, distance: GetExactDistance(n, exactDistanceField))).MinBy(p => p.distance);
                        if (float.IsPositiveInfinity(clndistance))
                            return (byte)0;

                        var bestDirection = closestNeighbor - coordVec;
                        return (byte)(1 + NeighborOffsets.IndexOf(bestDirection));
                    }).ToArray())
                    .ToArray();

                _directionField[target] = directionGrid;
                _exactDistanceField.TryRemove(target, out _);
            }
        }

        #endregion

        #region Utility Methods

        public List<Vector2i> GetPath(Vector3 startWorld, Vector3 targetWorld)
        {
            var startGrid = WorldToGrid(startWorld);
            var targetGrid = WorldToGrid(targetWorld);

            _core.LogMessage($"A* DEBUG: Finding path from grid ({startGrid.X}, {startGrid.Y}) to ({targetGrid.X}, {targetGrid.Y})");

            // For continuous pathfinding, don't use cache for long paths - only cache very short paths
            var cacheKey = $"{startGrid.X},{startGrid.Y}->{targetGrid.X},{targetGrid.Y}";
            var distance = Math.Abs(startGrid.X - targetGrid.X) + Math.Abs(startGrid.Y - targetGrid.Y);

            if (distance < 10 && _pathCache.TryGetValue(cacheKey, out var cachedPath)) // Only cache very short paths
            {
                _core.LogMessage($"A* DEBUG: Using cached path with {cachedPath.Count} waypoints");
                return cachedPath;
            }

            // Check if start and target are the same
            if (startGrid == targetGrid)
            {
                _core.LogMessage($"A* DEBUG: Start and target are the same, returning empty path");
                return new List<Vector2i>();
            }

            // Check if positions are walkable - be more lenient for target position
            if (!IsTilePathable(startGrid))
            {
                _core.LogMessage($"A* DEBUG: Start position ({startGrid.X}, {startGrid.Y}) is not walkable!");
                return null;
            }

            // For target position, try nearby walkable tiles if the exact position is blocked
            if (!IsTilePathable(targetGrid))
            {
                _core.LogMessage($"A* DEBUG: Target position ({targetGrid.X}, {targetGrid.Y}) is not walkable - trying nearby walkable tiles");

                // Search in expanding circles for a walkable target
                for (int radius = 1; radius <= 3; radius++) // Search up to 3 tiles away
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            if (Math.Abs(dx) + Math.Abs(dy) == radius) // Only check perimeter for efficiency
                            {
                                var nearbyTile = new Vector2i(targetGrid.X + dx, targetGrid.Y + dy);
                                if (IsTilePathable(nearbyTile))
                                {
                                    _core.LogMessage($"A* DEBUG: Found nearby walkable target at ({nearbyTile.X}, {nearbyTile.Y}) - using as target");
                                    targetGrid = nearbyTile;
                                    goto foundWalkableTarget;
                                }
                            }
                        }
                    }
                }

                _core.LogMessage($"A* DEBUG: No walkable target found within 3 tiles - A* pathfinding failed!");
                return null;

                foundWalkableTarget:;
            }

            _core.LogMessage($"A* DEBUG: Both positions are walkable, checking distance...");

            // For continuous pathfinding, prioritize shorter paths and direction field
            if (distance < 50) // For short distances, try first scan
            {
                _core.LogMessage($"A* DEBUG: Short distance ({distance}), running first scan...");

                // Run first scan if needed
                foreach (var path in RunFirstScan(startGrid, targetGrid))
                {
                    if (path != null && path.Count > 0)
                    {
                        _core.LogMessage($"A* DEBUG: First scan found path with {path.Count} waypoints");
                        if (distance < 10) _pathCache[cacheKey] = path; // Only cache very short paths
                        return path;
                    }
                }
            }

            // For longer distances or if first scan fails, use direction field for shorter paths
            _core.LogMessage($"A* DEBUG: Using direction field for shorter path...");

            // Find path using direction field (this gives shorter, more reliable paths)
            var directionPath = FindPath(startGrid, targetGrid);
            if (directionPath != null && directionPath.Count > 0)
            {
                // Limit path length to avoid zig-zagging and long paths that might fail
                var maxWaypoints = Math.Min(directionPath.Count, 10); // Limit to 10 waypoints max
                var limitedPath = directionPath.Take(maxWaypoints).ToList();

                _core.LogMessage($"A* DEBUG: Direction field found path with {limitedPath.Count} waypoints (limited from {directionPath.Count})");
                if (distance < 10) _pathCache[cacheKey] = limitedPath; // Only cache very short paths
                return limitedPath;
            }

            // If direction field fails, try first scan as last resort
            if (distance < 20) // Only try first scan for very short distances
            {
                _core.LogMessage($"A* DEBUG: Direction field failed, trying first scan...");

                foreach (var path in RunFirstScan(startGrid, targetGrid))
                {
                    if (path != null && path.Count > 0)
                    {
                        _core.LogMessage($"A* DEBUG: First scan found path with {path.Count} waypoints");
                        if (distance < 10) _pathCache[cacheKey] = path;
                        return path;
                    }
                }
            }

            _core.LogMessage($"A* DEBUG: No reliable path found - distance too great or terrain blocked");
            return null;
        }

        public void ClearPathCache()
        {
            _pathCache.Clear();
        }

        public Vector2i WorldToGrid(Vector3 worldPos)
        {
            // Convert world position to grid coordinates (similar to Radar's GridToWorldMultiplier)
            const float GridToWorldMultiplier = 250f / 23f; // TileToWorldConversion / TileToGridConversion
            var gridX = (int)(worldPos.X / GridToWorldMultiplier);
            var gridY = (int)(worldPos.Y / GridToWorldMultiplier); // Y is north-south in world space, Y in grid

            // Clamp coordinates to valid range
            gridX = Math.Max(0, Math.Min(gridX, _dimension2 - 1));
            gridY = Math.Max(0, Math.Min(gridY, _dimension1 - 1));

            return new Vector2i(gridX, gridY);
        }

        private Vector2i WorldToGrid(Vector2 worldPos)
        {
            return WorldToGrid(new Vector3(worldPos.X, worldPos.Y, 0));
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
