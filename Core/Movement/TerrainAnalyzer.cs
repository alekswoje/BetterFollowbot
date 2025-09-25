using System.Collections.Generic;
using BetterFollowbot.Interfaces;
using SharpDX;

namespace BetterFollowbot.Core.Movement
{
    /// <summary>
    /// Analyzes terrain for movement purposes, specifically terrain dashing
    /// </summary>
    public class TerrainAnalyzer : ITerrainAnalyzer
    {
        #region ITerrainAnalyzer Implementation

        public bool AnalyzeTerrainForDashing(Vector2 targetPosition, System.Func<int, int, byte> getTerrainTile)
        {
            //TODO: Completely re-write this garbage.
            //It's not taking into account a lot of stuff, horribly inefficient and just not the right way to do this.
            //Calculate the straight path from us to the target (this would be waypoints normally)
            var dir = targetPosition - BetterFollowbot.Instance.GameController.Player.GridPos;
            dir.Normalize();

            var distanceBeforeWall = 0;
            var distanceInWall = 0;

            var shouldDash = false;
            var points = new List<System.Drawing.Point>();
            for (var i = 0; i < 500; i++)
            {
                var v2Point = BetterFollowbot.Instance.GameController.Player.GridPos + i * dir;
                var point = new System.Drawing.Point((int)(BetterFollowbot.Instance.GameController.Player.GridPos.X + i * dir.X),
                    (int)(BetterFollowbot.Instance.GameController.Player.GridPos.Y + i * dir.Y));

                if (points.Contains(point))
                    continue;
                if (Vector2.Distance(v2Point, targetPosition) < 2)
                    break;

                points.Add(point);
                var tile = getTerrainTile(point.X, point.Y);


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

            return shouldDash;
        }

        #endregion
    }
}
