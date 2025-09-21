using System;
using System.Collections.Generic;
using System.Drawing;
using BetterFollowbotLite.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using SharpDX;

namespace BetterFollowbotLite.Core.Movement
{
    /// <summary>
    /// Handles pathfinding and terrain analysis for movement
    /// </summary>
    public class Pathfinding : IPathfinding
    {
        private readonly IFollowbotCore _core;
        private readonly ITerrainAnalyzer _terrainAnalyzer;
        private int _numRows, _numCols;
        private byte[,] _tiles;

        public Pathfinding(IFollowbotCore core, ITerrainAnalyzer terrainAnalyzer)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _terrainAnalyzer = terrainAnalyzer ?? throw new ArgumentNullException(nameof(terrainAnalyzer));
        }

        #region IPathfinding Implementation

        public int TerrainCols => _numCols;
        public int TerrainRows => _numRows;
        public bool IsTerrainLoaded => _tiles != null;

        public void InitializeTerrain()
        {
            try
            {
                var terrain = BetterFollowbotLite.Instance.GameController.IngameState.Data.Terrain;
                var terrainBytes = BetterFollowbotLite.Instance.GameController.Memory.ReadBytes(terrain.LayerMelee.First, terrain.LayerMelee.Size);
                _numCols = (int)(terrain.NumCols - 1) * 23;
                _numRows = (int)(terrain.NumRows - 1) * 23;
                if ((_numCols & 1) > 0)
                    _numCols++;

                _tiles = new byte[_numCols, _numRows];
                var dataIndex = 0;
                for (var y = 0; y < _numRows; y++)
                {
                    for (var x = 0; x < _numCols; x += 2)
                    {
                        var b = terrainBytes[dataIndex + (x >> 1)];
                        _tiles[x, y] = (byte)((b & 0xf) > 0 ? 1 : 255);
                        _tiles[x + 1, y] = (byte)((b >> 4) > 0 ? 1 : 255);
                    }
                    dataIndex += terrain.BytesPerRow;
                }

                terrainBytes = BetterFollowbotLite.Instance.GameController.Memory.ReadBytes(terrain.LayerRanged.First, terrain.LayerRanged.Size);
                _numCols = (int)(terrain.NumCols - 1) * 23;
                _numRows = (int)(terrain.NumRows - 1) * 23;
                if ((_numCols & 1) > 0)
                    _numCols++;
                dataIndex = 0;
                for (var y = 0; y < _numRows; y++)
                {
                    for (var x = 0; x < _numCols; x += 2)
                    {
                        var b = terrainBytes[dataIndex + (x >> 1)];

                        var current = _tiles[x, y];
                        if (current == 255)
                            _tiles[x, y] = (byte)((b & 0xf) > 3 ? 2 : 255);
                        current = _tiles[x + 1, y];
                        if (current == 255)
                            _tiles[x + 1, y] = (byte)((b >> 4) > 3 ? 2 : 255);
                    }
                    dataIndex += terrain.BytesPerRow;
                }

                _core.LogMessage("PATHFINDING: Terrain data initialized successfully");
            }
            catch (Exception e)
            {
                _core.LogError($"PATHFINDING: Failed to initialize terrain data - {e.Message}");
                _tiles = null;
            }
        }

        public bool CheckDashTerrain(Vector2 targetPosition)
        {
            if (_tiles == null)
                return false;

            // Delegate terrain analysis to the specialized analyzer
            var shouldDash = _terrainAnalyzer.AnalyzeTerrainForDashing(targetPosition, GetTerrainTile);

            if (shouldDash)
            {
                // Note: Mouse positioning and key pressing should be handled by the caller
                // This method only determines if dashing is possible
                _core.LogMessage("PATHFINDING: Terrain dash conditions met");
                return true;
            }

            return false;
        }

        public byte GetTerrainTile(int x, int y)
        {
            if (_tiles == null || x < 0 || x >= _numCols || y < 0 || y >= _numRows)
                return 255; // Invalid tile

            return _tiles[x, y];
        }

        #endregion
    }
}
