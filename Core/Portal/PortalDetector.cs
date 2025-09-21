using System;
using System.Linq;
using BetterFollowbotLite.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BetterFollowbotLite.Core.Portal
{
    /// <summary>
    /// Handles portal detection and matching logic
    /// </summary>
    public class PortalDetector : IPortalDetector
    {
        private readonly IFollowbotCore _core;
        private readonly PortalManager _portalManager;

        public PortalDetector(IFollowbotCore core, PortalManager portalManager)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
            _portalManager = portalManager ?? throw new ArgumentNullException(nameof(portalManager));
        }

        public LabelOnGround GetBestPortalLabel(PartyElementWindow leaderPartyElement, Vector3 lastTargetPosition, bool forceSearch = false)
        {
            try
            {
                if (leaderPartyElement == null)
                {
                    _core.LogMessage("PORTAL DEBUG: GetBestPortalLabel called with null leaderPartyElement");
                    return null;
                }

                var currentZoneName = _core.GameController?.Area.CurrentArea.DisplayName;
                var leaderZoneName = leaderPartyElement.ZoneName;
                var isHideout = (bool)_core?.GameController?.Area?.CurrentArea?.IsHideout;
                var realLevel = _core.GameController?.Area?.CurrentArea?.RealLevel ?? 0;
                var zonesAreDifferent = !leaderPartyElement.ZoneName.Equals(currentZoneName);

                _core.LogMessage($"PORTAL DEBUG: Checking for portals - Current: '{currentZoneName}', Leader: '{leaderZoneName}', Hideout: {isHideout}, Level: {realLevel}, ZonesDifferent: {zonesAreDifferent}, ForceSearch: {forceSearch}");

                // Look for portals when leader is in different zone, or when in hideout, or in high level areas
                // But be smarter about it - don't look for portals if zones are the same unless in hideout
                // If forceSearch is true, override the zone checking logic
                if (forceSearch || zonesAreDifferent || isHideout || (realLevel >= 68 && zonesAreDifferent)) // TODO: or is chamber of sins a7 or is epilogue
                {
                    if (forceSearch)
                    {
                        _core.LogMessage($"PORTAL DEBUG: Portal search condition met - FORCE SEARCH enabled");
                    }
                    else if (zonesAreDifferent)
                    {
                        _core.LogMessage($"PORTAL DEBUG: Portal search condition met - leader in different zone");
                    }
                    else if (isHideout)
                    {
                        _core.LogMessage($"PORTAL DEBUG: Portal search condition met - in hideout");
                    }
                    else
                    {
                        _core.LogMessage($"PORTAL DEBUG: Portal search condition met - high level area (level {realLevel}) but same zone, searching anyway");
                    }

                    var allPortalLabels =
                        _core.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                                x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible && x.ItemOnGround != null &&
                                (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal") ))
                            .ToList();

                    _core.LogMessage($"PORTAL DEBUG: Found {allPortalLabels?.Count ?? 0} total portal labels");

                    if (allPortalLabels == null || allPortalLabels.Count == 0)
                    {
                        _core.LogMessage("PORTAL DEBUG: No portal labels found on ground");

                        // If we're looking for an Arena portal specifically, add some additional debugging
                        if (leaderPartyElement.ZoneName?.ToLower().Contains("arena") ?? false)
                        {
                            _core.LogMessage("PORTAL DEBUG: Looking for Arena portal - checking all entities on ground");

                            // Look for any entities that might be portals even without labels
                            var allEntities = _core.GameController?.EntityListWrapper?.Entities;
                            if (allEntities != null)
                            {
                                var potentialPortals = allEntities.Where(e =>
                                    e?.Type == EntityType.WorldItem &&
                                    e.IsTargetable &&
                                    e.HasComponent<WorldItem>() &&
                                    (e.Metadata.ToLower().Contains("areatransition") || e.Metadata.ToLower().Contains("portal"))
                                ).ToList();

                                _core.LogMessage($"PORTAL DEBUG: Found {potentialPortals.Count} potential portal entities without labels");
                                foreach (var portal in potentialPortals)
                                {
                                    // During portal transition, look for portals near bot's current position
                                    // Otherwise use portal manager location if available, or fall back to lastTargetPosition
                                    var referencePosition = _portalManager.IsInPortalTransition ? _core.PlayerPosition :
                                                          _portalManager.PortalLocation != Vector3.Zero ? _portalManager.PortalLocation : lastTargetPosition;
                                    var distance = Vector3.Distance(referencePosition, portal.Pos);
                                    _core.LogMessage($"PORTAL DEBUG: Portal entity at distance {distance:F1}, Metadata: {portal.Metadata}");
                                }
                            }
                        }

                        return null;
                    }

                    // Log all available portals for debugging
                    foreach (var portal in allPortalLabels)
                    {
                        var labelText = portal.Label?.Text ?? "NULL";
                        // During portal transition, look for portals near bot's current position
                        // Otherwise use portal manager location if available, or fall back to lastTargetPosition
                        var referencePosition = _portalManager.IsInPortalTransition ? _core.PlayerPosition :
                                              _portalManager.PortalLocation != Vector3.Zero ? _portalManager.PortalLocation : lastTargetPosition;
                        var distance = Vector3.Distance(referencePosition, portal.ItemOnGround.Pos);
                        _core.LogMessage($"PORTAL DEBUG: Available portal - Text: '{labelText}', Distance: {distance:F1}");
                    }

                    // First, try to find portals that lead to the leader's zone by checking the label text
                    var matchingPortals = allPortalLabels.Where(x =>
                    {
                        try
                        {
                            var labelText = x.Label?.Text?.ToLower() ?? "";
                            var leaderZone = leaderPartyElement.ZoneName?.ToLower() ?? "";
                            var currentZone = currentZoneName?.ToLower() ?? "";

                            _core.LogMessage($"PORTAL DEBUG: Evaluating portal '{x.Label?.Text}' for leader zone '{leaderZone}'");

                            // Enhanced portal matching logic
                            var matchesLeaderZone = MatchesPortalToZone(labelText, leaderZone, x.Label?.Text ?? "");
                            var notCurrentZone = !string.IsNullOrEmpty(labelText) &&
                                               !string.IsNullOrEmpty(currentZone) &&
                                               !MatchesPortalToZone(labelText, currentZone, x.Label?.Text ?? "");

                            // Special handling for Arena portals (like Warden's Quarters) - they're interzone portals
                            // even if they're in the same zone, so we should accept them
                            var isSpecialPortal = PortalManager.IsSpecialPortal(labelText);

                            _core.LogMessage($"PORTAL DEBUG: Portal '{x.Label?.Text}' - Matches leader: {matchesLeaderZone}, Not current: {notCurrentZone}, Special: {isSpecialPortal}");

                            // Accept portal if it matches leader zone OR if it's a special portal (Arena/Warden's Quarters)
                            return matchesLeaderZone || isSpecialPortal;
                        }
                        catch (Exception ex)
                        {
                            _core.LogMessage($"PORTAL DEBUG: Error evaluating portal: {ex.Message}");
                            return false;
                        }
                    }).OrderBy(x => Vector3.Distance(lastTargetPosition, x.ItemOnGround.Pos)).ToList();

                    _core.LogMessage($"PORTAL DEBUG: Found {matchingPortals.Count} portals matching leader zone");

                    // If we found portals that match the leader's zone, use those
                    if (matchingPortals.Count > 0)
                    {
                        var selectedPortal = matchingPortals.First();
                        var labelText = selectedPortal.Label?.Text ?? "NULL";
                        var distance = Vector3.Distance(lastTargetPosition, selectedPortal.ItemOnGround.Pos);
                        _core.LogMessage($"PORTAL FOUND: Using portal '{labelText}' that matches leader zone '{leaderPartyElement.ZoneName}' (Distance: {distance:F1})");
                        return selectedPortal;
                    }

                    // No fallback portal selection - let the caller handle party teleport instead
                    _core.LogMessage($"PORTAL: No matching portal found for leader zone '{leaderPartyElement.ZoneName}' - will use party teleport");

                    // Log some portal suggestions for debugging
                    foreach (var portal in allPortalLabels.Take(3))
                    {
                        var labelText = portal.Label?.Text ?? "NULL";
                        _core.LogMessage($"PORTAL SUGGESTION: Available portal '{labelText}'");
                    }

                    return null;
                }

                else
                {
                    if (!zonesAreDifferent && !isHideout && !(realLevel >= 68 && zonesAreDifferent))
                    {
                        _core.LogMessage("PORTAL DEBUG: Portal search condition not met - same zone, not hideout, and not high-level zone transition");
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                _core.LogMessage($"PORTAL DEBUG: Exception in GetBestPortalLabel: {ex.Message}");
                return null;
            }
        }

        public bool MatchesPortalToZone(string portalLabel, string zoneName, string originalLabel)
        {
            if (string.IsNullOrEmpty(portalLabel) || string.IsNullOrEmpty(zoneName))
                return false;

            portalLabel = portalLabel.ToLower();
            zoneName = zoneName.ToLower();

            _core.LogMessage($"PORTAL MATCH: Checking '{originalLabel}' against zone '{zoneName}'");

            // Exact match
            if (portalLabel.Contains(zoneName))
            {
                _core.LogMessage($"PORTAL MATCH: Exact match found for '{zoneName}'");
                return true;
            }

            // Handle common portal prefixes/suffixes
            var portalPatterns = new[]
            {
                $"portal to {zoneName}",
                $"portal to the {zoneName}",
                $"{zoneName} portal",
                $"{zoneName} (portal)",
                $"to {zoneName}",
                $"to the {zoneName}",
                zoneName
            };

            foreach (var pattern in portalPatterns)
            {
                if (portalLabel.Contains(pattern))
                {
                    _core.LogMessage($"PORTAL MATCH: Pattern match found '{pattern}' for '{zoneName}'");
                    return true;
                }
            }

            // Special portal handling
            if (PortalManager.IsSpecialPortal(portalLabel))
            {
                var portalType = PortalManager.GetSpecialPortalType(portalLabel);
                _core.LogMessage($"PORTAL MATCH: Special portal '{portalType}' detected");
                return true;
            }

            // Handle zone name variations
            if (portalLabel.Equals(zoneName, StringComparison.OrdinalIgnoreCase))
            {
                _core.LogMessage($"PORTAL MATCH: Exact zone match for '{zoneName}'");
                return true;
            }

            // Handle partial matches
            if (portalLabel.Contains(zoneName, StringComparison.OrdinalIgnoreCase))
            {
                _core.LogMessage($"PORTAL MATCH: Partial match found for '{zoneName}'");
                return true;
            }

            // Handle hideout portals
            if (zoneName.Contains("hideout") && (portalLabel.Contains("hideout") || portalLabel.Contains("home")))
            {
                _core.LogMessage($"PORTAL MATCH: Hideout portal match for '{zoneName}'");
                return true;
            }

            // Handle town portals
            if (zoneName.Contains("town") && (portalLabel.Contains("town") || portalLabel.Contains("waypoint")))
            {
                _core.LogMessage($"PORTAL MATCH: Town portal match for '{zoneName}'");
                return true;
            }

            _core.LogMessage($"PORTAL MATCH: No match found for '{zoneName}'");
            return false;
        }
    }
}
