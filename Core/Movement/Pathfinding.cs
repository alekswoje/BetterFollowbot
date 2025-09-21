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
        private int _numRows, _numCols;
        private byte[,] _tiles;

        public Pathfinding(IFollowbotCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
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

            //TODO: Completely re-write this garbage.
            //It's not taking into account a lot of stuff, horribly inefficient and just not the right way to do this.
            //Calculate the straight path from us to the target (this would be waypoints normally)
            var dir = targetPosition - BetterFollowbotLite.Instance.GameController.Player.GridPos;
            dir.Normalize();

            var distanceBeforeWall = 0;
            var distanceInWall = 0;

            var shouldDash = false;
            var points = new List<System.Drawing.Point>();
            for (var i = 0; i < 500; i++)
            {
                var v2Point = BetterFollowbotLite.Instance.GameController.Player.GridPos + i * dir;
                var point = new System.Drawing.Point((int)(BetterFollowbotLite.Instance.GameController.Player.GridPos.X + i * dir.X),
                    (int)(BetterFollowbotLite.Instance.GameController.Player.GridPos.Y + i * dir.Y));

                if (points.Contains(point))
                    continue;
                if (Vector2.Distance(v2Point, targetPosition) < 2)
                    break;

                points.Add(point);
                var tile = _tiles[point.X, point.Y];


                //Invalid tile: Block dash
                if (tile == 255)
                {
                    shouldDash = false;
                    break;
                }
                else if (tile == 2)
                {
                    if (shouldDash)
                        distanceInWall++;
                    shouldDash = true;
                }
                else if (!shouldDash)
                {
                    distanceBeforeWall++;
                    if (distanceBeforeWall > 10)
                        break;
                }
            }

            if (distanceBeforeWall > 10 || distanceInWall < 5)
                shouldDash = false;

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
