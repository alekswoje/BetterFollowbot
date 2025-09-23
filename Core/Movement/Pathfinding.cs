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

        private IEnumerable<Vector2i> GetWalkableNeighbors(Vector2i tile)
        {
            return GetNeighbors(tile).Where(neighbor => IsTilePathable(neighbor));
        }

        /// <summary>
        /// Finds the nearest walkable tile to the given position, expanding outward in a spiral pattern
        /// </summary>
        private Vector2i FindNearestWalkableTile(Vector2i startPosition)
        {
            // Check if the position itself is walkable first
            if (IsTilePathable(startPosition))
            {
                return startPosition;
            }

            // Expand outward in a spiral pattern to find the nearest walkable tile
            for (int range = 1; range < 50; range++) // Limit search to reasonable distance
            {
                // Check the perimeter of each expanding square
                int minX = Math.Max(0, startPosition.X - range);
                int maxX = Math.Min(_dimension2 - 1, startPosition.X + range);
                int minY = Math.Max(0, startPosition.Y - range);
                int maxY = Math.Min(_dimension1 - 1, startPosition.Y + range);

                // Top and bottom edges
                for (int x = minX; x <= maxX; x++)
                {
                    // Top edge
                    var topPos = new Vector2i(x, minY);
                    if (IsTilePathable(topPos))
                    {
                        return topPos;
                    }
                    // Bottom edge
                    var bottomPos = new Vector2i(x, maxY);
                    if (IsTilePathable(bottomPos))
                    {
                        return bottomPos;
                    }
                }

                // Left and right edges (excluding corners already checked)
                for (int y = minY + 1; y < maxY; y++)
                {
                    // Left edge
                    var leftPos = new Vector2i(minX, y);
                    if (IsTilePathable(leftPos))
                    {
                        return leftPos;
                    }
                    // Right edge
                    var rightPos = new Vector2i(maxX, y);
                    if (IsTilePathable(rightPos))
                    {
                        return rightPos;
                    }
                }
            }

            // No walkable tile found within search range
            return Vector2i.Zero;
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
                    // Validate that the next tile is actually walkable
                    if (!IsTilePathable(next))
                    {
                        _core.LogMessage($"A* DEBUG: Path contains non-walkable tile at ({next.X}, {next.Y}) - path invalid");
                        return null;
                    }
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
                    var next = GetWalkableNeighbors(current).MinBy(x => GetExactDistance(x, exactDistanceField));
                    if (next == default(Vector2i) || !IsTilePathable(next))
                    {
                        _core.LogMessage($"A* DEBUG: No valid walkable neighbor found from ({current.X}, {current.Y}) - path invalid");
                        return null;
                    }
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
            // Get grid coordinates from the Render components like Radar does
            var player = _core.GameController.Game.IngameState.Data.LocalPlayer;
            var playerRender = player?.GetComponent<Render>();
            if (playerRender == null)
            {
                _core.LogMessage("A* DEBUG: Could not get player Render component");
                return null;
            }

            var startGrid = new Vector2i((int)playerRender.GridPos().X, (int)playerRender.GridPos().Y);

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

            // Check cache first - use both start and target positions as key
            var cacheKey = $"{startGrid.X},{startGrid.Y}->{targetGrid.X},{targetGrid.Y}";
            if (_pathCache.TryGetValue(cacheKey, out var cachedPath))
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

            // Check if positions are walkable, and find nearest walkable tile if not
            if (!IsTilePathable(startGrid))
            {
                _core.LogMessage($"A* DEBUG: Start position ({startGrid.X}, {startGrid.Y}) is not walkable, finding nearest walkable tile...");
                startGrid = FindNearestWalkableTile(startGrid);
                if (startGrid == Vector2i.Zero)
                {
                    _core.LogMessage($"A* DEBUG: Could not find walkable tile near start position!");
                    return null;
                }
                _core.LogMessage($"A* DEBUG: Using walkable start position ({startGrid.X}, {startGrid.Y})");
            }

            if (!IsTilePathable(targetGrid))
            {
                _core.LogMessage($"A* DEBUG: Target position ({targetGrid.X}, {targetGrid.Y}) is not walkable, finding nearest walkable tile...");
                targetGrid = FindNearestWalkableTile(targetGrid);
                if (targetGrid == Vector2i.Zero)
                {
                    _core.LogMessage($"A* DEBUG: Could not find walkable tile near target position!");
                    return null;
                }
                _core.LogMessage($"A* DEBUG: Using walkable target position ({targetGrid.X}, {targetGrid.Y})");
            }

            _core.LogMessage($"A* DEBUG: Both positions are walkable, running first scan...");

            // Run first scan if needed
            var pathFound = false;
            foreach (var path in RunFirstScan(startGrid, targetGrid))
            {
                if (path != null && path.Count > 0)
                {
                    _core.LogMessage($"A* DEBUG: First scan found path with {path.Count} waypoints");
                    _pathCache[cacheKey] = path;
                    pathFound = true;
                    return path;
                }
            }

            if (!pathFound)
            {
                _core.LogMessage($"A* DEBUG: First scan found no path, trying direction field...");
            }

            // Find path using direction field
            var finalPath = FindPath(startGrid, targetGrid);
            if (finalPath != null)
            {
                _core.LogMessage($"A* DEBUG: Direction field found path with {finalPath.Count} waypoints");
                _pathCache[cacheKey] = finalPath;
            }
            else
            {
                _core.LogMessage($"A* DEBUG: No path found by any method");
            }

            return finalPath;
        }

        public void ClearPathCache()
        {
            _directionField.Clear();
            _exactDistanceField.Clear();
            _pathCache.Clear();
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
