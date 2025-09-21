using ExileCore.PoEMemory.Elements;

namespace BetterFollowbotLite.Interfaces
{
    /// <summary>
    /// Interface for portal detection and matching functionality
    /// </summary>
    public interface IPortalDetector
    {
        /// <summary>
        /// Finds the best portal to follow the leader through based on zone matching and proximity
        /// </summary>
        /// <param name="leaderPartyElement">The leader's party element for zone information</param>
        /// <param name="lastTargetPosition">The last known position of the follow target for distance calculations</param>
        /// <param name="forceSearch">Whether to force portal search regardless of zone conditions</param>
        /// <returns>The best matching portal label, or null if none found</returns>
        LabelOnGround GetBestPortalLabel(PartyElementWindow leaderPartyElement, SharpDX.Vector3 lastTargetPosition, bool forceSearch = false);

        /// <summary>
        /// Checks if a portal label matches a target zone using enhanced matching logic
        /// </summary>
        /// <param name="portalLabel">The portal's label text</param>
        /// <param name="zoneName">The target zone name</param>
        /// <param name="originalLabel">The original portal label for logging</param>
        /// <returns>True if the portal matches the zone</returns>
        bool MatchesPortalToZone(string portalLabel, string zoneName, string originalLabel);
    }
}
