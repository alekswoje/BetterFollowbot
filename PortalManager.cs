using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using SharpDX;

namespace BetterFollowbot
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
                    "passage", "the brine king's throne", "the fortress encampment", "den of despair", "arakali's web"
                }, "Arena"
            }
        };

        // Close arena portals - these use a shorter distance threshold (300 units)
        private static readonly string[] CloseArenaPortals = new[]
        {
            "the bowels of the beast", "shavronne's arena", "maligaro's arena", "deodre's arena", "cathedral apex",
            "maligaro's workshop"
        };
    
        // Special portal names that should be treated as high-priority interzone portals
        // This is auto-generated from PortalTypeMappings for consistency
        private static readonly string[] SpecialPortalNames = PortalTypeMappings.Keys.SelectMany(keywords => keywords).ToArray();

        // Areas that cannot use party teleport and require special portal handling
        private static readonly string[] SpecialAreas = new[]
        {
            "maligaro's sanctum",
            "maligaros sanctum", 
            "maligaro's arena",
            "maligaros arena",
            "shavronne's arena",
            "shavronnes arena",
            "deodre's arena",
            "deodres arena",
            "the bowels of the beast",
            "cathedral apex",
            "prison rooftop",
            "valley of the fire drinker",
            "valley of the soul drinker",
            "the spawning ground",
            "the cloven pass"
        };

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
        /// Validates if a portal is actually a real portal using entity type
        /// </summary>
        public static bool IsValidPortal(LabelOnGround portal)
        {
            try
            {
                if (portal?.ItemOnGround == null) return false;

                var entityType = portal.ItemOnGround.Type;
                var metadata = portal.ItemOnGround.Metadata?.ToLower() ?? "";
                
                // Use entity type for reliable portal detection
                if (entityType.ToString() == "TownPortal" || entityType.ToString() == "AreaTransition")
                {
                    return true;
                }

                // Check for map device portals (MultiplexPortal) using metadata
                if (metadata.Contains("multiplexportal"))
                {
                    return true;
                }

                // Check for special portals (arena portals, etc.) that might not have standard types
                var labelText = portal.Label?.Text?.ToLower() ?? "";
                var isSpecialPortal = IsSpecialPortal(labelText);
                
                return isSpecialPortal;
            }
            catch
            {
                return false;
            }
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
        /// Checks if an area name is a special area that cannot use party teleport
        /// </summary>
        public static bool IsSpecialArea(string areaName)
        {
            if (string.IsNullOrEmpty(areaName)) return false;
            return SpecialAreas.Any(specialArea =>
                areaName.Contains(specialArea, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Finds portals that match a specific area name (for special areas that can't use party teleport)
        /// </summary>
        public static LabelOnGround FindMatchingPortal(string targetAreaName)
        {
            try
            {
                if (string.IsNullOrEmpty(targetAreaName))
                    return null;

                BetterFollowbot.Instance.LogMessage($"SPECIAL AREA: Looking for portals matching '{targetAreaName}'");

                // Get all portal-like objects using clean entity type filtering
                // For map portals (MultiplexPortal), relax visibility requirements since they may not be fully loaded
                var allPortalLabels = BetterFollowbot.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                    {
                        if (x == null || x.ItemOnGround == null) return false;
                        
                        // Map device portals (MultiplexPortal) - check without strict visibility requirements
                        var metadata = x.ItemOnGround.Metadata?.ToLower() ?? "";
                        if (metadata.Contains("multiplexportal"))
                        {
                            // Only require the label exists, not that it's fully visible yet
                            return x.Label != null && x.Label.IsValid;
                        }
                        
                        // For other portals, use normal visibility checks
                        return x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible && IsValidPortal(x);
                    })
                    .ToList();

                if (allPortalLabels == null || allPortalLabels.Count == 0)
                {
                    BetterFollowbot.Instance.LogMessage("SPECIAL AREA: No portal objects found");
                    return null;
                }

                // Look for portals that match the target area name
                var matchingPortals = allPortalLabels.Where(x =>
                {
                    try
                    {
                        var labelText = x.Label?.Text?.ToLower() ?? "";
                        var targetAreaLower = targetAreaName.ToLower();

                        // Check if portal label contains the target area name
                        var matchesArea = labelText.Contains(targetAreaLower) || targetAreaLower.Contains(labelText);
                        
                        // Also check for common variations
                        var areaVariations = new[]
                        {
                            targetAreaLower,
                            targetAreaLower.Replace("'s", "s"),
                            targetAreaLower.Replace("s", "'s"),
                            targetAreaLower.Replace(" ", ""),
                            targetAreaLower.Replace(" ", "_"),
                            targetAreaLower.Replace(" ", "-")
                        };

                        var matchesVariation = areaVariations.Any(variation => 
                            labelText.Contains(variation) || variation.Contains(labelText));

                        if (matchesArea || matchesVariation)
                        {
                            BetterFollowbot.Instance.LogMessage($"SPECIAL AREA: Found matching portal '{x.Label?.Text}' for area '{targetAreaName}'");
                        }

                        return matchesArea || matchesVariation;
                    }
                    catch (Exception ex)
                    {
                        BetterFollowbot.Instance.LogError($"SPECIAL AREA: Error checking portal '{x.Label?.Text}': {ex.Message}");
                        return false;
                    }
                }).OrderBy(x => x.ItemOnGround.DistancePlayer).ToList();

                if (matchingPortals.Count > 0)
                {
                    var selectedPortal = matchingPortals.First();
                    BetterFollowbot.Instance.LogMessage($"SPECIAL AREA: Selected portal '{selectedPortal.Label?.Text}' at distance {selectedPortal.ItemOnGround.DistancePlayer:F1}");
                    return selectedPortal;
                }
                else
                {
                    BetterFollowbot.Instance.LogMessage($"SPECIAL AREA: No portals found matching '{targetAreaName}'");
                    return null;
                }
            }
            catch (Exception ex)
            {
                BetterFollowbot.Instance.LogError($"SPECIAL AREA: Error finding matching portal: {ex.Message}");
                return null;
            }
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
                if (distanceMoved > BetterFollowbot.Instance.Settings.autoPilotClearPathDistance.Value)
                {
                    BetterFollowbot.Instance.LogMessage($"PORTAL TRANSITION: Leader moved {distanceMoved:F0} units - interzone portal detected (threshold: {BetterFollowbot.Instance.Settings.autoPilotClearPathDistance.Value})");
                    BetterFollowbot.Instance.LogMessage($"PORTAL TRANSITION: Portal transition detected - bot should look for portals near current position to follow leader");

                    // Don't set portalLocation to leader's old position - the portal object stays in the same world location
                    // Instead, mark that we're in a portal transition state so the bot will look for portals to follow
                    portalLocation = Vector3.One; // Use as a flag to indicate portal transition mode is active
                    BetterFollowbot.Instance.LogMessage($"PORTAL TRANSITION: Portal transition mode activated - IsInPortalTransition: {IsInPortalTransition}");
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
                    var rawName = BetterFollowbot.Instance.GameController.Area.CurrentArea.Area.RawName;
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
                var portalLabels = BetterFollowbot.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels
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
                BetterFollowbot.Instance.LogError($"PortalManager: Error finding nearest labyrinth portal: {ex.Message}");
                return null;
            }
        }
    }
}
