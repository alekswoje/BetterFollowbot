using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using SharpDX;

namespace BetterFollowbotLite
{
    /// <summary>
    /// Manages all portal-related functionality for the bot
    /// </summary>
    public class PortalManager
    {
        // Portal type mappings - maps keywords to display names
        // Using comprehensive patterns to match various arena portal labels
        private static readonly Dictionary<string[], string> PortalTypeMappings = new()
        {
            {
                new[]
                {
                    "ascendancy chamber", "arena", "the pit", "warden's quarters", "portal", "the ring of blades",
                    "combat", "merveil's lair", "merveils lair", "the weaver's nest", "stairs", "pyramid apex",
                    "tower rooftop", "caldera of the king", "the black core", "the black heart", "sanctum of innocence",
                    "the chamber of innocence", "tukohama's keep", "the karui fortress", "prison rooftop",
                    "valley of the fire drinker", "the cloven pass", "valley of the soul drinker", "the spawning ground",
                    ""
                }, "Arena"
            }
        };

        // Close arena portals - these use a shorter distance threshold (300 units)
        private static readonly string[] CloseArenaPortals = new[]
        {
            "the bowels of the beast", "shavronne's arena", "maligaro's arena", "deodre's arena", "cathedral apex",
            ""
        };
    
        // Special portal names that should be treated as high-priority interzone portals
        // This is auto-generated from PortalTypeMappings for consistency
        private static readonly string[] SpecialPortalNames = PortalTypeMappings.Keys.SelectMany(keywords => keywords).ToArray();

        // Distance thresholds for leader distance - portals are only taken if leader is MORE than this distance away
        private const float CloseArenaPortalDistance = 300f; // Close portals (Pit, Warden's Quarters, Stairs)
        private const float RegularArenaPortalDistance = 1350; // Regular arena portals

        // Portal transition state
        private Vector3 portalLocation = Vector3.Zero; // Where the portal actually is (leader's position before transition)

        /// <summary>
        /// Determines if a portal label contains any of the special portal names
        /// </summary>
        public static bool IsSpecialPortal(string portalLabel)
        {
            if (string.IsNullOrEmpty(portalLabel)) return false;
            return SpecialPortalNames.Any(specialName =>
                portalLabel.Contains(specialName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the display name for a special portal type using dynamic mapping
        /// </summary>
        public static string GetSpecialPortalType(string portalLabel)
        {
            if (string.IsNullOrEmpty(portalLabel)) return "Unknown";

            return GetPortalTypeFromMappings(portalLabel, PortalTypeMappings) ?? "Unknown";
        }

        /// <summary>
        /// Checks if a portal is a "close" arena portal that uses shorter distance threshold
        /// </summary>
        public static bool IsCloseArenaPortal(string portalLabel)
        {
            if (string.IsNullOrEmpty(portalLabel)) return false;

            var labelLower = portalLabel.ToLower();
            return CloseArenaPortals.Any(closePortal =>
                labelLower.Contains(closePortal, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the appropriate distance threshold for leader distance - portals are only taken when leader is farther than this
        /// </summary>
        public static float GetPortalDistanceThreshold(string portalLabel)
        {
            return IsCloseArenaPortal(portalLabel) ? CloseArenaPortalDistance : RegularArenaPortalDistance;
        }


        /// <summary>
        /// Dynamically determines portal type from mappings
        /// Handles both individual keywords and exact phrases
        /// </summary>
        private static string GetPortalTypeFromMappings(string portalLabel, Dictionary<string[], string> mappings)
        {
            if (string.IsNullOrEmpty(portalLabel)) return null;

            var labelLower = portalLabel.ToLower();

            // Check each mapping to see if the portal label contains any of the keywords
            foreach (var mapping in PortalTypeMappings)
            {
                foreach (var keyword in mapping.Key)
                {
                    var keywordLower = keyword.ToLower();

                    // For exact phrases (containing spaces), match the entire phrase
                    if (keywordLower.Contains(" "))
                    {
                        if (labelLower.Contains(keywordLower))
                        {
                            return mapping.Value;
                        }
                    }
                    // For single keywords, match as before
                    else
                    {
                        if (labelLower.Contains(keywordLower))
                        {
                            return mapping.Value;
                        }
                    }
                }
            }

            return null; // No match found
        }

        /// <summary>
        /// Gets the current portal transition state
        /// </summary>
        public Vector3 PortalLocation => portalLocation;

        /// <summary>
        /// Checks if we're currently in portal transition mode
        /// </summary>
        public bool IsInPortalTransition => portalLocation == Vector3.One;

        /// <summary>
        /// Sets the portal transition state
        /// </summary>
        public void SetPortalTransitionMode(bool active)
        {
            portalLocation = active ? Vector3.One : Vector3.Zero;
        }

        /// <summary>
        /// Detects portal transitions based on leader movement
        /// </summary>
        public void DetectPortalTransition(Vector3 lastTargetPosition, Vector3 newPosition)
        {
            if (lastTargetPosition != Vector3.Zero && newPosition != Vector3.Zero)
            {
                var distanceMoved = Vector3.Distance(lastTargetPosition, newPosition);
                // Use the existing autoPilotClearPathDistance setting to detect portal transitions
                if (distanceMoved > BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value)
                {
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL TRANSITION: Leader moved {distanceMoved:F0} units - interzone portal detected (threshold: {BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value})");
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL TRANSITION: Portal transition detected - bot should look for portals near current position to follow leader");

                    // Don't set portalLocation to leader's old position - the portal object stays in the same world location
                    // Instead, mark that we're in a portal transition state so the bot will look for portals to follow
                    portalLocation = Vector3.One; // Use as a flag to indicate portal transition mode is active
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL TRANSITION: Portal transition mode activated - IsInPortalTransition: {IsInPortalTransition}");
                }
            }
        }

        /// <summary>
        /// Checks if we're currently in a labyrinth area where party TP doesn't work
        /// </summary>
        public static bool IsInLabyrinthArea
        {
            get
            {
                try
                {
                    var rawName = BetterFollowbotLite.Instance.GameController.Area.CurrentArea.Area.RawName;
                    return rawName != null && rawName.Contains("labyrinth", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Finds the nearest portal/transition in labyrinth areas for following the leader
        /// </summary>
        public static LabelOnGround FindNearestLabyrinthPortal()
        {
            try
            {
                if (!IsInLabyrinthArea)
                    return null;

                // In labyrinth areas, find any portal-like object within reasonable distance
                var portalLabels = BetterFollowbotLite.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels
                    .Where(x =>
                        x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible &&
                        x.ItemOnGround != null &&
                        (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") ||
                         x.ItemOnGround.Metadata.ToLower().Contains("portal") ||
                         x.ItemOnGround.Metadata.ToLower().Contains("transition") ||
                         x.ItemOnGround.Metadata.ToLower().Contains("labyrinth")) &&
                        x.ItemOnGround.DistancePlayer < 150) // Within 150 units
                    .OrderBy(x => x.ItemOnGround.DistancePlayer) // Nearest first
                    .ToList();

                return portalLabels?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                BetterFollowbotLite.Instance.LogError($"PortalManager: Error finding nearest labyrinth portal: {ex.Message}");
                return null;
            }
        }

        // TODO: Add more portal management methods here as we move them from AutoPilot.cs
    }
}
