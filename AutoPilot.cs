using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using BetterFollowbotLite.Interfaces;
using BetterFollowbotLite.Core.TaskManagement;
using BetterFollowbotLite.Core.Movement;

namespace BetterFollowbotLite;

    public class AutoPilot
    {
        // Portal management moved to PortalManager class
        private PortalManager portalManager;
        
        // Leader detection service
        private readonly ILeaderDetector _leaderDetector;
        
        // Task management service
        private readonly ITaskManager _taskManager;

        // Pathfinding service
        private readonly IPathfinding _pathfinding;

        // Most Logic taken from Alpha Plugin
        private Coroutine autoPilotCoroutine;
        private readonly Random random = new Random();

        /// <summary>
        /// Constructor for AutoPilot
        /// </summary>
        public AutoPilot(ILeaderDetector leaderDetector, ITaskManager taskManager, IPathfinding pathfinding)
        {
            _leaderDetector = leaderDetector ?? throw new ArgumentNullException(nameof(leaderDetector));
            _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
            _pathfinding = pathfinding ?? throw new ArgumentNullException(nameof(pathfinding));
            portalManager = new PortalManager();
        }

        private Vector3 lastTargetPosition;
        private Vector3 lastPlayerPosition;
        private Entity followTarget;

        // Portal transition tracking for interzone portals

        // GLOBAL FLAG: Prevents SMITE and other skills from interfering during teleport
        public static bool IsTeleportInProgress { get; private set; } = false;

    public Entity FollowTarget => followTarget;

    /// <summary>
    /// Gets the current position of the follow target, using updated position data
    /// </summary>
    public Vector3 FollowTargetPosition => lastTargetPosition;

    /// <summary>
    /// Sets the follow target entity
    /// </summary>
    /// <param name="target">The entity to follow</param>
    public void SetFollowTarget(Entity target)
    {
        followTarget = target;
        if (target != null)
        {
            lastTargetPosition = target.Pos;
            BetterFollowbotLite.Instance.LogMessage($"AUTOPILOT: Set follow target '{target.GetComponent<Player>()?.PlayerName ?? "Unknown"}' at position: {target.Pos}");
        }
        else
        {
            BetterFollowbotLite.Instance.LogMessage("AUTOPILOT: Cleared follow target");
        }
    }

    /// <summary>
    /// Updates the follow target's position if it exists
    /// This is crucial for zone transitions where the entity's position changes
    /// </summary>
    public void UpdateFollowTargetPosition()
    {
        if (followTarget != null && followTarget.IsValid)
        {
            var newPosition = followTarget.Pos;

            // Check if position has changed significantly (zone transition or major movement)
            if (lastTargetPosition != Vector3.Zero)
            {
                var distanceMoved = Vector3.Distance(lastTargetPosition, newPosition);

                // If the target moved more than 500 units, it's likely a zone transition
                if (distanceMoved > 500)
                {
                    BetterFollowbotLite.Instance.LogMessage($"AUTOPILOT: Follow target moved {distanceMoved:F0} units (possible zone transition) from {lastTargetPosition} to {newPosition}");
                }
                else if (newPosition != lastTargetPosition)
                {
                    // Removed excessive follow target position logging
                }
            }

            // PORTAL TRANSITION DETECTION: Detect when leader enters an interzone portal
            portalManager.DetectPortalTransition(lastTargetPosition, newPosition);

            lastTargetPosition = newPosition;
        }
        else if (followTarget != null && !followTarget.IsValid)
        {
            // Follow target became invalid, clear it
            BetterFollowbotLite.Instance.LogMessage("AUTOPILOT: Follow target became invalid, clearing");
            followTarget = null;
            lastTargetPosition = Vector3.Zero;
        }
    }

    private bool hasUsedWp;

    /// <summary>
    /// Public accessor for the tasks list (read-only)
    /// </summary>
    public IReadOnlyList<TaskNode> Tasks => _taskManager.Tasks;
    internal DateTime lastDashTime = DateTime.MinValue; // Track last dash time for cooldown
    private bool instantPathOptimization = false; // Flag for instant response when path efficiency is detected
    private DateTime lastPathClearTime = DateTime.MinValue; // Track last path clear to prevent spam
    private DateTime lastResponsivenessCheck = DateTime.MinValue; // Track last responsiveness check to prevent spam
    private DateTime lastEfficiencyCheck = DateTime.MinValue; // Track last efficiency check to prevent spam


    /// <summary>
    /// Checks if the cursor is pointing roughly towards the target direction in screen space
    /// Improved to handle off-screen targets
    /// </summary>
    private bool IsCursorPointingTowardsTarget(Vector3 targetPosition)
    {
        try
        {
            // Get the current mouse position in screen coordinates
            var mouseScreenPos = BetterFollowbotLite.Instance.GetMousePosition();

            // Get the player's screen position
            var playerScreenPos = Helper.WorldToValidScreenPosition(BetterFollowbotLite.Instance.playerPosition);

            // Get the target's screen position - handle off-screen targets
            var targetScreenPos = Helper.WorldToValidScreenPosition(targetPosition);

            // If target is off-screen, calculate direction based on world positions
            if (targetScreenPos.X < 0 || targetScreenPos.Y < 0 ||
                targetScreenPos.X > BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width ||
                targetScreenPos.Y > BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height)
            {
                // For off-screen targets, calculate direction from world positions
                var playerWorldPos = BetterFollowbotLite.Instance.playerPosition;
                var directionToTarget = targetPosition - playerWorldPos;

                if (directionToTarget.Length() < 10) // Target is very close in world space
                    return true;

                directionToTarget.Normalize();

                // Convert world direction to screen space approximation
                // This is a simplified approximation - we assume forward direction is towards positive X in screen space
                var screenDirection = new Vector2(directionToTarget.X, -directionToTarget.Z); // Z is depth, flip for screen Y
                screenDirection.Normalize();

                // Calculate direction from player to cursor in screen space (off-screen version)
                var playerToCursorOffscreen = mouseScreenPos - playerScreenPos;
                if (playerToCursorOffscreen.Length() < 30) // Cursor is too close to player in screen space
                    return false; // Can't determine direction reliably

                playerToCursorOffscreen.Normalize();

                // Calculate the angle between the two directions (off-screen version)
                var dotProductOffscreen = Vector2.Dot(screenDirection, playerToCursorOffscreen);
                var angleOffscreen = Math.Acos(Math.Max(-1, Math.Min(1, dotProductOffscreen))) * (180.0 / Math.PI);

                // Allow up to 90 degrees difference for off-screen targets (more lenient)
                return angleOffscreen <= 90.0;
            }

            // Original logic for on-screen targets
            // Calculate the direction from player to target in screen space
            var playerToTarget = targetScreenPos - playerScreenPos;
            if (playerToTarget.Length() < 20) // Target is too close in screen space
                return true; // Consider it pointing towards target

            playerToTarget.Normalize();

            // Calculate the direction from player to cursor in screen space
            var playerToCursor = mouseScreenPos - playerScreenPos;
            if (playerToCursor.Length() < 30) // Cursor is too close to player in screen space
                return false; // Can't determine direction reliably

            playerToCursor.Normalize();

            // Calculate the angle between the two directions
            var dotProduct = Vector2.Dot(playerToTarget, playerToCursor);
            var angle = Math.Acos(Math.Max(-1, Math.Min(1, dotProduct))) * (180.0 / Math.PI);

            // Allow up to 60 degrees difference (cursor should be roughly pointing towards target)
            return angle <= 60.0;
        }
        catch (Exception e)
        {
            BetterFollowbotLite.Instance.LogMessage($"IsCursorPointingTowardsTarget error: {e.Message}");
            return false; // Default to false if we can't determine direction
        }
    }


    /// <summary>
    /// Checks if the player has moved significantly and we should clear the current path for better responsiveness
    /// More aggressive for 180-degree turns
    /// </summary>
    private bool ShouldClearPathForResponsiveness()
    {
        return ShouldClearPathForResponsiveness(false);
    }

    /// <summary>
    /// Checks if the player has moved significantly and we should clear the current path for better responsiveness
    /// More aggressive for 180-degree turns
    /// </summary>
    private bool ShouldClearPathForResponsiveness(bool isOverrideCheck)
    {
        try
        {
            // For override checks (after click), be much less aggressive with timing
            int rateLimitMs = isOverrideCheck ? 2000 : 5000; // Much less aggressive - increased from 100/500 to 2000/5000ms
            if ((DateTime.Now - lastPathClearTime).TotalMilliseconds < rateLimitMs)
                return false;

            // Additional cooldown for responsiveness checks to prevent excessive path clearing
            if ((DateTime.Now - lastResponsivenessCheck).TotalMilliseconds < 1000) // Much slower - increased from 200 to 1000ms
                return false;

            // Need a follow target to check responsiveness
            if (followTarget == null)
                return false;

            // Need existing tasks to clear
            if (_taskManager.TaskCount == 0)
                return false;

            // Calculate how much the player has moved since last update
            var playerMovement = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, lastPlayerPosition);
            
            // Much less aggressive: Only clear path if player moved significantly more
            if (playerMovement > 300f) // Increased from 100f to 300f to be much less aggressive
            {
                // Reduced logging frequency to prevent lag
                lastPathClearTime = DateTime.Now;
                lastResponsivenessCheck = DateTime.Now;
                return true;
            }

            // Check for 180-degree turn detection - VERY AGGRESSIVE
            if (_taskManager.TaskCount > 0 && _taskManager.Tasks[0].WorldPosition != null)
            {
                Vector3 botPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;
                Vector3 playerPos = BetterFollowbotLite.Instance.playerPosition;
                Vector3 currentTaskTarget = _taskManager.Tasks[0].WorldPosition;

                // Calculate direction from bot to current task
                Vector3 botToTask = currentTaskTarget - botPos;
                // Calculate direction from bot to player
                Vector3 botToPlayer = playerPos - botPos;

                if (botToTask.Length() > 10f && botToPlayer.Length() > 10f)
                {
                    botToTask = Vector3.Normalize(botToTask);
                    botToPlayer = Vector3.Normalize(botToPlayer);

                    // Calculate dot product - if negative, player is behind the current task direction
                    float dotProduct = Vector3.Dot(botToTask, botToPlayer);
                    
                    // Much less aggressive: Only clear path for extreme direction changes
                    if (dotProduct < -0.5f) // 120 degrees - very conservative
                    {
                        lastPathClearTime = DateTime.Now;
                        lastResponsivenessCheck = DateTime.Now;
                        return true;
                    }
                }
            }

            // Also check if we're following an old position that's now far from current player position
            var distanceToCurrentPlayer = Vector3.Distance(BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition, lastTargetPosition);
            if (distanceToCurrentPlayer > 400f) // Much less aggressive - increased from 150f to reduce constant path clearing
            {
                lastPathClearTime = DateTime.Now;
                lastResponsivenessCheck = DateTime.Now;
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    /// <summary>
    /// Calculates the efficiency of the current path compared to moving directly to player
    /// Returns efficiency ratio: direct_distance / path_distance
    /// Lower values mean the direct path is much shorter (more efficient)
    /// </summary>
    private float CalculatePathEfficiency()
    {
        try
        {
            if (_taskManager.TaskCount == 0 || followTarget == null)
                return 1.0f; // No path or no target, consider efficient

            // Check efficiency even for single tasks if they're movement tasks
            bool hasMovementTask = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Movement);

            // Calculate direct distance from bot to player
            float directDistance = Vector3.Distance(BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition, followTarget?.Pos ?? BetterFollowbotLite.Instance.playerPosition);

            // If we're already very close to the player, don't bother with efficiency calculations
            if (directDistance < 30f) // Reduced from 50f
                return 1.0f;

            // Calculate distance along current path
            float pathDistance = 0f;
            Vector3 currentPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;

            // Add distance to each path node
            foreach (var task in _taskManager.Tasks)
            {
                if (task.WorldPosition != null)
                {
                    pathDistance += Vector3.Distance(currentPos, task.WorldPosition);
                    currentPos = task.WorldPosition;
                }
            }

            // If no valid path distance, return 1.0 (neutral)
            if (pathDistance <= 0)
                return 1.0f;

            // If the path is very short, it's already efficient
            if (pathDistance < 50f) // Reduced from 100f
                return 1.0f;

            // Calculate efficiency ratio
            float efficiency = directDistance / pathDistance;

            // More detailed logging for debugging
            // Reduced logging frequency to prevent lag
            return efficiency;
        }
        catch (Exception e)
        {
            return 1.0f; // Default to neutral on error
        }
    }

    /// <summary>
    /// Checks if the current path is inefficient and should be abandoned
    /// Returns true if path should be cleared for direct movement to player
    /// </summary>
    private bool ShouldAbandonPathForEfficiency()
    {
        try
        {
            // Check even single tasks if they're movement tasks and we have a follow target
            bool shouldCheckEfficiency = _taskManager.TaskCount >= 1 && followTarget != null;

            if (!shouldCheckEfficiency)
            {
                return false;
            }
            
            // Add cooldown to prevent excessive efficiency checks
            if ((DateTime.Now - lastEfficiencyCheck).TotalMilliseconds < 300) // 300ms cooldown between checks
                return false;

            float efficiency = CalculatePathEfficiency();

            // If direct path is much shorter (more than 20% shorter) than following current path - VERY AGGRESSIVE
            if (efficiency < 0.8f) // Changed from 0.7f to 0.8f for even more aggressive clearing
            {
                lastEfficiencyCheck = DateTime.Now;
                return true;
            }

            // Also check if player is now behind us relative to path direction (more aggressive check)
            if (_taskManager.TaskCount >= 1 && _taskManager.Tasks[0].WorldPosition != null)
            {
                Vector3 botPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;
                Vector3 playerPos = BetterFollowbotLite.Instance.playerPosition;
                Vector3 pathTarget = _taskManager.Tasks[0].WorldPosition;

                // Calculate vectors
                Vector3 botToPath = pathTarget - botPos;
                Vector3 botToPlayer = playerPos - botPos;

                // Normalize vectors for dot product calculation
                if (botToPath.Length() > 0 && botToPlayer.Length() > 0)
                {
                    botToPath = Vector3.Normalize(botToPath);
                    botToPlayer = Vector3.Normalize(botToPlayer);

                            // If player is behind us on the path (negative dot product) - VERY SENSITIVE
                            float dotProduct = Vector3.Dot(botToPath, botToPlayer);
                            if (dotProduct < -0.1f) // Changed from -0.3f to -0.1f (95 degrees) - even more sensitive
                    {
                        lastEfficiencyCheck = DateTime.Now;
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    /// <summary>
    /// Clears all pathfinding values. Used on area transitions primarily.
    /// </summary>
    private void ResetPathing()
    {
        _taskManager.ClearTasks();
        followTarget = null;
        lastTargetPosition = Vector3.Zero;
        lastPlayerPosition = Vector3.Zero;
        hasUsedWp = false;
        lastDashTime = DateTime.MinValue; // Reset dash cooldown on area change
        instantPathOptimization = false; // Reset instant optimization flag
        lastPathClearTime = DateTime.MinValue; // Reset responsiveness tracking
        lastResponsivenessCheck = DateTime.MinValue; // Reset responsiveness check cooldown
        lastEfficiencyCheck = DateTime.MinValue; // Reset efficiency check cooldown

        // CLEAR GLOBAL FLAG: Zone change means any ongoing teleport is complete
        IsTeleportInProgress = false;
    }



    private LabelOnGround GetBestPortalLabel(PartyElementWindow leaderPartyElement, bool forceSearch = false)
    {
        try
        {
            if (leaderPartyElement == null)
            {
                BetterFollowbotLite.Instance.LogMessage("PORTAL DEBUG: GetBestPortalLabel called with null leaderPartyElement");
                return null;
            }

            var currentZoneName = BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName;
            var leaderZoneName = leaderPartyElement.ZoneName;
            var isHideout = (bool)BetterFollowbotLite.Instance?.GameController?.Area?.CurrentArea?.IsHideout;
            var realLevel = BetterFollowbotLite.Instance.GameController?.Area?.CurrentArea?.RealLevel ?? 0;
            var zonesAreDifferent = !leaderPartyElement.ZoneName.Equals(currentZoneName);

            BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Checking for portals - Current: '{currentZoneName}', Leader: '{leaderZoneName}', Hideout: {isHideout}, Level: {realLevel}, ZonesDifferent: {zonesAreDifferent}, ForceSearch: {forceSearch}");

            // Look for portals when leader is in different zone, or when in hideout, or in high level areas
            // But be smarter about it - don't look for portals if zones are the same unless in hideout
            // If forceSearch is true, override the zone checking logic
            if (forceSearch || zonesAreDifferent || isHideout || (realLevel >= 68 && zonesAreDifferent)) // TODO: or is chamber of sins a7 or is epilogue
            {
                if (forceSearch)
                {
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Portal search condition met - FORCE SEARCH enabled");
                }
                else if (zonesAreDifferent)
                {
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Portal search condition met - leader in different zone");
                }
                else if (isHideout)
                {
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Portal search condition met - in hideout");
                }
                else
                {
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Portal search condition met - high level area (level {realLevel}) but same zone, searching anyway");
                }

                var allPortalLabels =
                    BetterFollowbotLite.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                            x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible && x.ItemOnGround != null &&
                            (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal") ))
                        .ToList();

                BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Found {allPortalLabels?.Count ?? 0} total portal labels");

                if (allPortalLabels == null || allPortalLabels.Count == 0)
                {
                    BetterFollowbotLite.Instance.LogMessage("PORTAL DEBUG: No portal labels found on ground");

                    // If we're looking for an Arena portal specifically, add some additional debugging
                    if (leaderPartyElement.ZoneName?.ToLower().Contains("arena") ?? false)
                    {
                        BetterFollowbotLite.Instance.LogMessage("PORTAL DEBUG: Looking for Arena portal - checking all entities on ground");

                        // Look for any entities that might be portals even without labels
                        var allEntities = BetterFollowbotLite.Instance.GameController?.EntityListWrapper?.Entities;
                        if (allEntities != null)
                        {
                            var potentialPortals = allEntities.Where(e =>
                                e?.Type == EntityType.WorldItem &&
                                e.IsTargetable &&
                                e.HasComponent<WorldItem>() &&
                                (e.Metadata.ToLower().Contains("areatransition") || e.Metadata.ToLower().Contains("portal"))
                            ).ToList();

                            BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Found {potentialPortals.Count} potential portal entities without labels");
                            foreach (var portal in potentialPortals)
                            {
                                // During portal transition, look for portals near bot's current position
                                // Otherwise use portal manager location if available, or fall back to lastTargetPosition
                                var referencePosition = portalManager.IsInPortalTransition ? BetterFollowbotLite.Instance.playerPosition :
                                                      portalManager.PortalLocation != Vector3.Zero ? portalManager.PortalLocation : lastTargetPosition;
                                var distance = Vector3.Distance(referencePosition, portal.Pos);
                                BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Portal entity at distance {distance:F1}, Metadata: {portal.Metadata}");
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
                    var referencePosition = portalManager.IsInPortalTransition ? BetterFollowbotLite.Instance.playerPosition :
                                          portalManager.PortalLocation != Vector3.Zero ? portalManager.PortalLocation : lastTargetPosition;
                    var distance = Vector3.Distance(referencePosition, portal.ItemOnGround.Pos);
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Available portal - Text: '{labelText}', Distance: {distance:F1}");
                }

                // First, try to find portals that lead to the leader's zone by checking the label text
                var matchingPortals = allPortalLabels.Where(x =>
                {
                    try
                    {
                        var labelText = x.Label?.Text?.ToLower() ?? "";
                        var leaderZone = leaderPartyElement.ZoneName?.ToLower() ?? "";
                        var currentZone = currentZoneName?.ToLower() ?? "";

                        BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Evaluating portal '{x.Label?.Text}' for leader zone '{leaderZone}'");

                        // Enhanced portal matching logic
                        var matchesLeaderZone = MatchesPortalToZone(labelText, leaderZone, x.Label?.Text ?? "");
                        var notCurrentZone = !string.IsNullOrEmpty(labelText) &&
                                           !string.IsNullOrEmpty(currentZone) &&
                                           !MatchesPortalToZone(labelText, currentZone, x.Label?.Text ?? "");

                        // Special handling for Arena portals (like Warden's Quarters) - they're interzone portals
                        // even if they're in the same zone, so we should accept them
                        var isSpecialPortal = PortalManager.IsSpecialPortal(labelText);

                        BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Portal '{x.Label?.Text}' - Matches leader: {matchesLeaderZone}, Not current: {notCurrentZone}, Special: {isSpecialPortal}");

                        // Accept portal if it matches leader zone OR if it's a special portal (Arena/Warden's Quarters)
                        return matchesLeaderZone || isSpecialPortal;
                    }
                    catch (Exception ex)
                    {
                        BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Error evaluating portal: {ex.Message}");
                        return false;
                    }
                }).OrderBy(x => Vector3.Distance(lastTargetPosition, x.ItemOnGround.Pos)).ToList();

                BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Found {matchingPortals.Count} portals matching leader zone");

                // If we found portals that match the leader's zone, use those
                if (matchingPortals.Count > 0)
                {
                    var selectedPortal = matchingPortals.First();
                    var labelText = selectedPortal.Label?.Text ?? "NULL";
                    var distance = Vector3.Distance(lastTargetPosition, selectedPortal.ItemOnGround.Pos);
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL FOUND: Using portal '{labelText}' that matches leader zone '{leaderPartyElement.ZoneName}' (Distance: {distance:F1})");
                    return selectedPortal;
                }

                // No fallback portal selection - let the caller handle party teleport instead
                BetterFollowbotLite.Instance.LogMessage($"PORTAL: No matching portal found for leader zone '{leaderPartyElement.ZoneName}' - will use party teleport");

                // Log some portal suggestions for debugging
                foreach (var portal in allPortalLabels.Take(3))
                {
                    var labelText = portal.Label?.Text ?? "NULL";
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL SUGGESTION: Available portal '{labelText}'");
                }

                return null;
            }

            else
            {
                if (!zonesAreDifferent && !isHideout && !(realLevel >= 68 && zonesAreDifferent))
                {
                    BetterFollowbotLite.Instance.LogMessage("PORTAL DEBUG: Portal search condition not met - same zone, not hideout, and not high-level zone transition");
                }
                return null;
            }
        }
        catch (Exception ex)
        {
            BetterFollowbotLite.Instance.LogMessage($"PORTAL DEBUG: Exception in GetBestPortalLabel: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Enhanced portal-to-zone matching that handles various portal text formats
    /// </summary>
    private bool MatchesPortalToZone(string portalLabel, string zoneName, string originalLabel)
    {
        if (string.IsNullOrEmpty(portalLabel) || string.IsNullOrEmpty(zoneName))
            return false;

        portalLabel = portalLabel.ToLower();
        zoneName = zoneName.ToLower();

        BetterFollowbotLite.Instance.LogMessage($"PORTAL MATCH: Checking '{originalLabel}' against zone '{zoneName}'");

        // Exact match
        if (portalLabel.Contains(zoneName))
        {
            BetterFollowbotLite.Instance.LogMessage($"PORTAL MATCH: Exact match found for '{zoneName}'");
            return true;
        }

        // Handle common portal prefixes/suffixes
        var portalPatterns = new[]
        {
            $"portal to {zoneName}",
            $"portal to the {zoneName}",
            $"{zoneName} portal",
            $"enter {zoneName}",
            $"enter the {zoneName}",
            $"go to {zoneName}",
            $"go to the {zoneName}",
            $"{zoneName} entrance",
            $"{zoneName} gate"
        };

        foreach (var pattern in portalPatterns)
        {
            if (portalLabel.Contains(pattern))
            {
                BetterFollowbotLite.Instance.LogMessage($"PORTAL MATCH: Pattern match found '{pattern}' for '{zoneName}'");
                return true;
            }
        }

        // Handle special cases like Arena and Warden's Quarters portals
        if (PortalManager.IsSpecialPortal(portalLabel))
        {
            var portalType = PortalManager.GetSpecialPortalType(portalLabel);
            BetterFollowbotLite.Instance.LogMessage($"PORTAL MATCH: Special case - {portalType} portal detected for zone '{zoneName}'");
            return true;
        }

        // Handle exact portal label matches (case-insensitive)
        if (portalLabel.Equals(zoneName, StringComparison.OrdinalIgnoreCase))
        {
            BetterFollowbotLite.Instance.LogMessage($"PORTAL MATCH: Exact zone name match for '{zoneName}'");
            return true;
        }

        // Handle partial matches where zone name is contained in portal label
        if (portalLabel.Contains(zoneName, StringComparison.OrdinalIgnoreCase))
        {
            BetterFollowbotLite.Instance.LogMessage($"PORTAL MATCH: Zone name contained in portal label for '{zoneName}'");
            return true;
        }

        // Handle hideout portals
        if (zoneName.Contains("hideout") && (portalLabel.Contains("hideout") || portalLabel.Contains("home")))
        {
            BetterFollowbotLite.Instance.LogMessage($"PORTAL MATCH: Hideout portal detected for zone '{zoneName}'");
            return true;
        }

        // Handle town portals
        if (zoneName.Contains("town") && (portalLabel.Contains("town") || portalLabel.Contains("waypoint")))
        {
            BetterFollowbotLite.Instance.LogMessage($"PORTAL MATCH: Town portal detected for zone '{zoneName}'");
            return true;
        }

        BetterFollowbotLite.Instance.LogMessage($"PORTAL MATCH: No match found for '{originalLabel}' against zone '{zoneName}'");
        return false;
    }

    private Vector2 GetTpButton(PartyElementWindow leaderPartyElement)
    {
        try
        {
            if (leaderPartyElement == null)
                return Vector2.Zero;

            var windowOffset = BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().TopLeft;
            var elemCenter = (Vector2) leaderPartyElement?.TpButton?.GetClientRectCache.Center;
            var finalPos = new Vector2(elemCenter.X + windowOffset.X, elemCenter.Y + windowOffset.Y);

            return finalPos;
        }
        catch
        {
            return Vector2.Zero;
        }
    }
    private Element GetTpConfirmation()
    {
        try
        {
            var ui = BetterFollowbotLite.Instance.GameController?.IngameState?.IngameUi?.PopUpWindow;

            if (ui.Children[0].Children[0].Children[0].Text.Equals("Are you sure you want to teleport to this player's location?"))
                return ui.Children[0].Children[0].Children[3].Children[0];

            return null;
        }
        catch
        {
            return null;
        }
    }
    public void AreaChange()
    {
        ResetPathing();

        // Initialize terrain data through the pathfinding service
        _pathfinding.InitializeTerrain();
    }

    public void StartCoroutine()
    {
        autoPilotCoroutine = new Coroutine(AutoPilotLogic(), BetterFollowbotLite.Instance, "AutoPilot");
        ExileCore.Core.ParallelRunner.Run(autoPilotCoroutine);
    }
    private IEnumerator MouseoverItem(Entity item)
    {
        var uiLoot = BetterFollowbotLite.Instance.GameController.IngameState.IngameUi.ItemsOnGroundLabels.FirstOrDefault(I => I.IsVisible && I.ItemOnGround.Id == item.Id);
        if (uiLoot == null) yield return null;
        var clickPos = uiLoot?.Label?.GetClientRect().Center;
        if (clickPos != null)
        {
            Mouse.SetCursorPos(new Vector2(
                clickPos.Value.X + random.Next(-15, 15),
                clickPos.Value.Y + random.Next(-10, 10)));
        }

        yield return new WaitTime(30 + random.Next(BetterFollowbotLite.Instance.Settings.autoPilotInputFrequency));
    }
    private IEnumerator AutoPilotLogic()
    {
        while (true)
        {
            if (!BetterFollowbotLite.Instance.Settings.Enable.Value || !BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value || BetterFollowbotLite.Instance.localPlayer == null || !BetterFollowbotLite.Instance.localPlayer.IsAlive ||
                !BetterFollowbotLite.Instance.GameController.IsForeGroundCache || MenuWindow.IsOpened || BetterFollowbotLite.Instance.GameController.IsLoading || !BetterFollowbotLite.Instance.GameController.InGame)
            {
                yield return new WaitTime(100);
                continue;
            }

            // ADDITIONAL SAFEGUARD: Don't execute tasks during zone loading or when game state is unstable
            if (BetterFollowbotLite.Instance.GameController.IsLoading ||
                BetterFollowbotLite.Instance.GameController.Area.CurrentArea == null ||
                string.IsNullOrEmpty(BetterFollowbotLite.Instance.GameController.Area.CurrentArea.DisplayName))
            {
                BetterFollowbotLite.Instance.LogMessage("TASK EXECUTION: Blocking task execution during zone loading");
                yield return new WaitTime(200); // Wait longer during zone loading
                continue;
            }

            // Only execute input tasks here - decision making moved to Render method
            if (_taskManager.TaskCount > 0)
            {
                TaskNode currentTask = null;
                bool taskAccessError = false;

                // PRIORITY: Check if there are any teleport tasks and process them first
                var teleportTasks = _taskManager.Tasks.Where(t => t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);
                if (teleportTasks.Any())
                {
                    try
                    {
                        currentTask = teleportTasks.First();
                        BetterFollowbotLite.Instance.LogMessage($"PRIORITY: Processing teleport task {currentTask.Type} instead of {_taskManager.Tasks.First().Type}");
                    }
                    catch (Exception e)
                    {
                        taskAccessError = true;
                        BetterFollowbotLite.Instance.LogMessage($"PRIORITY: Error accessing teleport task - {e.Message}");
                    }
                }
                else
                {
                    try
                    {
                        currentTask = _taskManager.Tasks.First();
                    }
                    catch (Exception e)
                    {
                        taskAccessError = true;
                    }
                }

                if (taskAccessError)
                {
                    yield return new WaitTime(50);
                    continue;
                }

                if (currentTask?.WorldPosition == null)
                {
                    // Remove the task from its actual position, not just index 0
                    _taskManager.RemoveTask(currentTask);
                    yield return new WaitTime(50);
                    continue;
                }

                var taskDistance = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, currentTask.WorldPosition);
                var playerDistanceMoved = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, lastPlayerPosition);

                // Check if we should clear path for better responsiveness to player movement
                if (ShouldClearPathForResponsiveness())
                {
                    instantPathOptimization = true; // Enable instant mode for immediate response
                    _taskManager.ClearTasksPreservingTransitions(); // Clear all tasks and reset related state
                    hasUsedWp = false; // Allow waypoint usage again
                    
                    // FORCE IMMEDIATE PATH CREATION - Don't wait for UpdateAutoPilotLogic
                    if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                    {
                        var instantDistanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, FollowTargetPosition);

                        if (instantDistanceToLeader > BetterFollowbotLite.Instance.Settings.autoPilotDashDistance && BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled) // Use configured dash distance
                        {
                            // CRITICAL: Don't add dash tasks if we have an active transition task OR another dash task
                            var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                            if (!hasConflictingTasks)
                            {
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, 0, TaskNodeType.Dash));
                                // Removed excessive INSTANT PATH OPTIMIZATION logging
                            }
                            else
                            {
                                // Removed excessive INSTANT PATH OPTIMIZATION logging
                            }
                        }
                        else
                        {
                            _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                        }
                    }
                    
                    yield return null; // INSTANT: No delay, immediate path recalculation
                    continue; // Skip current task processing, will recalculate path immediately
                }

                // Check if current path is inefficient and should be abandoned - INSTANT RESPONSE
                if (ShouldAbandonPathForEfficiency())
                {
                    instantPathOptimization = true; // Enable instant mode for immediate response
                    _taskManager.ClearTasksPreservingTransitions(); // Clear all tasks and reset related state
                    hasUsedWp = false; // Allow waypoint usage again
                    
                    // FORCE IMMEDIATE PATH CREATION - Don't wait for UpdateAutoPilotLogic
                    if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                    {
                        var instantDistanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, FollowTargetPosition);

                        if (instantDistanceToLeader > BetterFollowbotLite.Instance.Settings.autoPilotDashDistance && BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled) // Use configured dash distance
                        {
                            // CRITICAL: Don't add dash tasks if we have an active transition task OR another dash task
                            var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                            if (!hasConflictingTasks)
                            {
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, 0, TaskNodeType.Dash));
                                // Removed excessive INSTANT PATH OPTIMIZATION logging
                            }
                            else
                            {
                                // Removed excessive INSTANT PATH OPTIMIZATION logging
                            }
                        }
                        else
                        {
                            _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                        }
                    }
                    
                    yield return null; // INSTANT: No delay, immediate path recalculation
                    continue; // Skip current task processing, will recalculate path immediately
                }

                //We are using a same map transition and have moved significnatly since last tick. Mark the transition task as done.
                if (currentTask.Type == TaskNodeType.Transition &&
                    playerDistanceMoved >= BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value)
                {
                    _taskManager.RemoveTask(currentTask);
                    lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;
                    yield return null;
                    continue;
                }

                // Variables to track state outside try-catch blocks
                bool shouldDashToLeader = false;
                bool shouldTerrainDash = false;
                Vector2 movementScreenPos = Vector2.Zero;
                bool screenPosError = false;
                bool keyDownError = false;
                bool keyUpError = false;
                bool taskExecutionError = false;

                // Action flags for different task types
                bool shouldLootAndContinue = false;
                bool shouldTransitionAndContinue = false;
                bool shouldClaimWaypointAndContinue = false;
                bool shouldDashAndContinue = false;
                bool shouldTeleportConfirmAndContinue = false;
                bool shouldTeleportButtonAndContinue = false;
                bool shouldMovementContinue = false;

                // Loot-related variables
                Entity questLoot = null;
                Targetable targetInfo = null;

                // Transition-related variables
                Vector2 transitionPos = Vector2.Zero;

                // Waypoint-related variables
                Vector2 waypointScreenPos = Vector2.Zero;

                // PRE-MOVEMENT OVERRIDE CHECK: Check if we should override BEFORE executing movement
                if (currentTask.Type == TaskNodeType.Movement)
                {
                    // SIMPLIFIED OVERRIDE: Just check if target is far from current player position
                    var playerPos = BetterFollowbotLite.Instance.playerPosition;
                    var botPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;
                    var targetPos = currentTask.WorldPosition;
                    
                    // Calculate direction from bot to target vs bot to player
                    var botToTarget = targetPos - botPos;
                    var botToPlayer = playerPos - botPos;
                    
                    bool shouldOverride = false;
                    string overrideReason = "";
                    
                    // Check 1: Is target far from player?
                    var targetToPlayerDistance = Vector3.Distance(targetPos, playerPos);
                    if (targetToPlayerDistance > 400f)
                    {
                        shouldOverride = true;
                        overrideReason = $"Target {targetToPlayerDistance:F1} units from player";
                    }
                    
                    // Check 2: Are we going opposite direction from player?
                    if (!shouldOverride && botToTarget.Length() > 10f && botToPlayer.Length() > 10f)
                    {
                        var dotProduct = Vector3.Dot(Vector3.Normalize(botToTarget), Vector3.Normalize(botToPlayer));
                        if (dotProduct < 0.3f) // Going more than 72 degrees away from player
                        {
                            shouldOverride = true;
                            overrideReason = $"Direction conflict (dot={dotProduct:F2})";
                        }
                    }

                    if (shouldOverride)
                    {
                        _taskManager.ClearTasksPreservingTransitions();
                        hasUsedWp = false; // Allow waypoint usage again
                        
                        // INSTANT OVERRIDE: Click towards the player's current position instead of stale followTarget
                        // Calculate a position closer to the player (not the exact player position to avoid issues)
                        var directionToPlayer = playerPos - botPos;
                        if (directionToPlayer.Length() > 10f) // Only if player is far enough away
                        {
                            directionToPlayer = Vector3.Normalize(directionToPlayer);
                            var correctionTarget = botPos + (directionToPlayer * 200f); // Move 200 units towards player
                            
                            var correctScreenPos = Helper.WorldToValidScreenPosition(correctionTarget);
                            yield return Mouse.SetCursorPosHuman(correctScreenPos);

                            // Skip the rest of this movement task since we've overridden it
                            continue;
                        }
                    }
                }

                switch (currentTask.Type)
                    {
                        case TaskNodeType.Movement:
                            // Check for distance-based dashing to keep up with leader
                            if (BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled && followTarget != null && followTarget.Pos != null && (DateTime.Now - lastDashTime).TotalMilliseconds >= 3000)
                            {
                                try
                                {
                                    var distanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, FollowTargetPosition);
                                    if (distanceToLeader > BetterFollowbotLite.Instance.Settings.autoPilotDashDistance && IsCursorPointingTowardsTarget(followTarget.Pos)) // Dash if more than configured distance away and cursor is pointing towards leader
                                    {
                                        shouldDashToLeader = true;
                                    }
                                }
                                catch (Exception e)
                                {
                                    // Error handling without logging
                                }
                            }
                            else
                            {
                                // Dash check skipped
                            }

                            // Check for terrain-based dashing
                            if (BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled && (DateTime.Now - lastDashTime).TotalMilliseconds >= 3000)
                            {
                                // Terrain dash check
                                if (_pathfinding.CheckDashTerrain(currentTask.WorldPosition.WorldToGrid()) && IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                                {
                                    // Terrain dash executed
                                    shouldTerrainDash = true;
                                }
                                else if (_pathfinding.CheckDashTerrain(currentTask.WorldPosition.WorldToGrid()) && !IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                                {
                                    // Terrain dash blocked - cursor not pointing towards target
                                }
                                else
                                {
                                    // No terrain dash needed
                                }
                            }

                            // Skip movement logic if dashing
                            if (!shouldDashToLeader && !shouldTerrainDash)
                            {
                                try
                                {
                                    movementScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);
                                }
                                catch (Exception e)
                                {
                                    screenPosError = true;
                                }

                                
                                if (!screenPosError)
                                {
                                    
                                    try
                                    {
                                        Input.KeyDown(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
                                        BetterFollowbotLite.Instance.LogMessage("Movement task: Move key down pressed, waiting");
                                    }
                                    catch (Exception e)
                                    {
                                        BetterFollowbotLite.Instance.LogError($"Movement task: KeyDown error: {e}");
                                        keyDownError = true;
                                    }

                                    try
                                    {
                                        Input.KeyUp(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
                                        BetterFollowbotLite.Instance.LogMessage("Movement task: Move key released");
                                    }
                                    catch (Exception e)
                                    {
                                        BetterFollowbotLite.Instance.LogError($"Movement task: KeyUp error: {e}");
                                        keyUpError = true;
                                    }

                                    //Within bounding range. Task is complete
                                    //Note: Was getting stuck on close objects... testing hacky fix.
                                    if (taskDistance <= BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance.Value * 1.5)
                                    {
                                        // Removed excessive movement completion logging
                                        _taskManager.RemoveTask(currentTask);
                                        lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;
                                    }
                                    else
                                    {
                                        // Timeout mechanism - if we've been trying to reach this task for too long, give up
                                        currentTask.AttemptCount++;
                                        if (currentTask.AttemptCount > 10) // 10 attempts = ~5 seconds
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"Movement task timeout - Distance: {taskDistance:F1}, Attempts: {currentTask.AttemptCount}");
                                            _taskManager.RemoveTask(currentTask);
                                            lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;
                                        }
                                    }
                                    shouldMovementContinue = true;
                                }
                            }
                            break;
                        case TaskNodeType.Loot:
                        {
                            currentTask.AttemptCount++;
                            try
                            {
                                questLoot = BetterFollowbotLite.Instance.GameController.EntityListWrapper.Entities
                                    .Where(e => e?.Type == EntityType.WorldItem && e.IsTargetable && e.HasComponent<WorldItem>())
                                    .FirstOrDefault(e =>
                                    {
                                        var itemEntity = e.GetComponent<WorldItem>().ItemEntity;
                                        return BetterFollowbotLite.Instance.GameController.Files.BaseItemTypes.Translate(itemEntity.Path).ClassName ==
                                               "QuestItem";
                                    });
                            }
                            catch
                            {
                                questLoot = null;
                            }
                            if (questLoot == null
                                || currentTask.AttemptCount > 2
                                || Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, questLoot.Pos) >=
                                BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value)
                            {
                                _taskManager.RemoveTask(currentTask);
                                shouldLootAndContinue = true;
                            }
                            else
                            {
                                Input.KeyUp(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
                                if (questLoot != null)
                                {
                                    targetInfo = questLoot.GetComponent<Targetable>();
                                }
                                shouldLootAndContinue = true; // Set flag to execute loot logic outside try-catch
                            }
                            break;
                        }
                        case TaskNodeType.Transition:
                        {
                            BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Executing transition task - Attempt {currentTask.AttemptCount + 1}/6");

                            // Initialize flag to true - will be set to false if portal is invalid
                            shouldTransitionAndContinue = true;

                            // Log portal information
                            var portalLabel = currentTask.LabelOnGround?.Label?.Text ?? "NULL";
                            var portalPos = currentTask.LabelOnGround?.ItemOnGround?.Pos ?? Vector3.Zero;
                            var distanceToPortal = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, portalPos);

                            BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Portal '{portalLabel}' at distance {distanceToPortal:F1}");

                            // Check if portal is still visible and valid
                            var isPortalVisible = currentTask.LabelOnGround?.Label?.IsVisible ?? false;
                            var isPortalValid = currentTask.LabelOnGround?.Label?.IsValid ?? false;

                            BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Portal visibility - Visible: {isPortalVisible}, Valid: {isPortalValid}");

                            if (!isPortalVisible || !isPortalValid)
                            {
                                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Portal no longer visible or valid, removing task");
                                _taskManager.RemoveTask(currentTask);
                                shouldTransitionAndContinue = false; // Don't continue with transition
                                break; // Exit the switch case
                            }

                            //Click the transition
                            Input.KeyUp(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);

                            // Get the portal click position with more detailed logging
                            var portalRect = currentTask.LabelOnGround.Label.GetClientRect();
                            transitionPos = new Vector2(portalRect.Center.X, portalRect.Center.Y);

                            BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Portal click position - X: {transitionPos.X:F1}, Y: {transitionPos.Y:F1}");
                            BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Portal screen rect - Left: {portalRect.Left:F1}, Top: {portalRect.Top:F1}, Width: {portalRect.Width:F1}, Height: {portalRect.Height:F1}");

                            currentTask.AttemptCount++;
                            if (currentTask.AttemptCount > 6)
                            {
                                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Max attempts reached (6), removing transition task");
                                _taskManager.RemoveTask(currentTask);
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage("TRANSITION: Transition task queued for execution");
                            }
                            shouldTransitionAndContinue = true;
                            break;
                        }

                        case TaskNodeType.ClaimWaypoint:
                        {
                            if (Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, currentTask.WorldPosition) > 150)
                            {
                                waypointScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);
                                Input.KeyUp(BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
                            }
                            currentTask.AttemptCount++;
                            if (currentTask.AttemptCount > 3)
                                _taskManager.RemoveTask(currentTask);
                            shouldClaimWaypointAndContinue = true;
                            break;
                        }

                         case TaskNodeType.Dash:
                         {
                             BetterFollowbotLite.Instance.LogMessage($"Executing Dash task - Target: {currentTask.WorldPosition}, Distance: {Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, currentTask.WorldPosition):F1}, Attempts: {currentTask.AttemptCount}");

                             // TIMEOUT MECHANISM: If dash task has been tried too many times, give up
                             currentTask.AttemptCount++;
                             if (currentTask.AttemptCount > 15) // Allow more attempts for dash tasks
                             {
                                 BetterFollowbotLite.Instance.LogMessage($"Dash task timeout - Too many attempts ({currentTask.AttemptCount}), removing task");
                                 _taskManager.RemoveTask(currentTask);
                                 break;
                             }

                             if ((DateTime.Now - lastDashTime).TotalMilliseconds >= 3000)
                             {
                                 // Check if cursor is pointing towards target
                                 if (IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                                 {
                                     BetterFollowbotLite.Instance.LogMessage("Dash task: Cursor direction valid, executing dash");

                                     // Position mouse towards target if needed
                                     var targetScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);
                                     if (targetScreenPos.X >= 0 && targetScreenPos.Y >= 0 &&
                                         targetScreenPos.X <= BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width &&
                                         targetScreenPos.Y <= BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height)
                                     {
                                         // Target is on-screen, position mouse
                                         yield return Mouse.SetCursorPosHuman(targetScreenPos);
                                     }

                                     // Execute the dash
                                     Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                                     lastDashTime = DateTime.Now; // Record dash time for cooldown
                                     lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;

                                     // Remove the task since dash was executed
                                     _taskManager.RemoveTask(currentTask);
                                     BetterFollowbotLite.Instance.LogMessage("Dash task completed successfully");
                                     shouldDashAndContinue = true;
                                 }
                                 else
                                 {
                                     BetterFollowbotLite.Instance.LogMessage("Dash task: Cursor not pointing towards target, positioning cursor");

                                     // Try to position cursor towards the target
                                     var targetScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);

                                     // If target is off-screen, position towards the edge of screen in the target's direction
                                     if (targetScreenPos.X < 0 || targetScreenPos.Y < 0 ||
                                         targetScreenPos.X > BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width ||
                                         targetScreenPos.Y > BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height)
                                     {
                                         // Calculate direction to target and position mouse at screen edge
                                         var playerPos = BetterFollowbotLite.Instance.playerPosition;
                                         var directionToTarget = currentTask.WorldPosition - playerPos;
                                         directionToTarget.Normalize();

                                         // Position mouse at screen center (simplified approach)
                                         var screenCenter = new Vector2(
                                             BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width / 2,
                                             BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height / 2
                                         );

                                         yield return Mouse.SetCursorPosHuman(screenCenter);
                                         BetterFollowbotLite.Instance.LogMessage("Dash task: Positioned cursor at screen center for off-screen target");
                                     }
                                     else
                                     {
                                         // Target is on-screen but cursor isn't pointing towards it
                                         yield return Mouse.SetCursorPosHuman(targetScreenPos);
                                         BetterFollowbotLite.Instance.LogMessage("Dash task: Repositioned cursor towards target");
                                     }

                                     // Wait a bit for cursor to settle
                                     yield return new WaitTime(100);

                                     // Check again if cursor is now pointing towards target
                                     if (IsCursorPointingTowardsTarget(currentTask.WorldPosition))
                                     {
                                         BetterFollowbotLite.Instance.LogMessage("Dash task: Cursor now pointing correctly, executing dash");
                                         Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                                         lastDashTime = DateTime.Now;
                                         lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;
                                         _taskManager.RemoveTask(currentTask);
                                         shouldDashAndContinue = true;
                                     }
                                     else
                                     {
                                         BetterFollowbotLite.Instance.LogMessage("Dash task: Still can't position cursor correctly, will retry");
                                         // Don't remove the task - let it try again later
                                         yield return new WaitTime(300); // Longer delay before retry
                                     }
                                 }
                             }
                             else
                             {
                                 BetterFollowbotLite.Instance.LogMessage("Dash task blocked - Cooldown active");
                                 // Don't remove the task - wait for cooldown to expire
                                 yield return new WaitTime(600); // Wait before retry during cooldown
                             }
                             break;
                         }

                        case TaskNodeType.TeleportConfirm:
                        {
                            _taskManager.RemoveTask(currentTask);
                            shouldTeleportConfirmAndContinue = true;
                            break;
                        }

                        case TaskNodeType.TeleportButton:
                        {
                            _taskManager.RemoveTask(currentTask);
                            // CLEAR GLOBAL FLAG: Teleport task completed
                            IsTeleportInProgress = false;
                            shouldTeleportButtonAndContinue = true;
                            break;
                        }
                    }

                // Handle error cleanup (simplified without try-catch)
                if (currentTask != null && currentTask.AttemptCount > 20)
                {
                    // Remove task if it's been attempted too many times
                    BetterFollowbotLite.Instance.LogMessage($"Task timeout - Too many attempts ({currentTask.AttemptCount}), removing task");
                    _taskManager.RemoveTask(currentTask);
                }

                // Handle portal invalidation after try-catch
                if (currentTask != null && currentTask.Type == TaskNodeType.Transition && !shouldTransitionAndContinue)
                {
                    // Portal was invalidated, wait and continue
                    yield return new WaitTime(100);
                    continue;
                }
                // Execute actions outside try-catch blocks
                else
                {
                    if (shouldDashToLeader)
                    {
                        yield return Mouse.SetCursorPosHuman(Helper.WorldToValidScreenPosition(FollowTargetPosition));
                        BetterFollowbotLite.Instance.LogMessage("Movement task: Dash mouse positioned, pressing key");
                        if (instantPathOptimization)
                        {
                            // INSTANT MODE: Skip delays for immediate path correction
                            // Removed excessive INSTANT PATH OPTIMIZATION logging
                            Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                            lastDashTime = DateTime.Now; // Record dash time for cooldown
                            instantPathOptimization = false; // Reset flag after use
                        }
                        else
                        {
                            // Normal delays
                            yield return new WaitTime(random.Next(25) + 30);
                            Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                            lastDashTime = DateTime.Now; // Record dash time for cooldown
                            yield return new WaitTime(random.Next(25) + 30);
                        }
                        yield return null;
                        continue;
                    }

                    if (shouldTerrainDash)
                    {
                        lastDashTime = DateTime.Now; // Record dash time for cooldown (CheckDashTerrain already performed the dash)
                        yield return null;
                        continue;
                    }

                    if (screenPosError)
                    {
                        yield return new WaitTime(50);
                        continue;
                    }

                    if (!screenPosError && currentTask.Type == TaskNodeType.Movement)
                    {
                        // LAST CHANCE CHECK: Before executing movement, check if player has turned around
                        if (ShouldClearPathForResponsiveness())
                        {
                            BetterFollowbotLite.Instance.LogMessage("LAST CHANCE 180 CHECK: Player direction changed before movement execution, aborting current task");
                            _taskManager.ClearTasksPreservingTransitions();
                            hasUsedWp = false; // Allow waypoint usage again
                            yield return null; // Skip this movement and recalculate
                            continue;
                        }

                        BetterFollowbotLite.Instance.LogMessage("Movement task: Mouse positioned, pressing move key down");
                        BetterFollowbotLite.Instance.LogMessage($"Movement task: Move key: {BetterFollowbotLite.Instance.Settings.autoPilotMoveKey}");
                        // Removed excessive DEBUG logging for cleaner logs
                        yield return Mouse.SetCursorPosHuman(movementScreenPos);
                        
                        if (instantPathOptimization)
                        {
                            // INSTANT MODE: Skip delays for immediate path correction
                            // Removed excessive INSTANT PATH OPTIMIZATION logging
                            instantPathOptimization = false; // Reset flag after use
                        }
                        else
                        {
                            // Normal delays
                            yield return new WaitTime(random.Next(25) + 30);
                            yield return new WaitTime(random.Next(25) + 30);
                        }
                        yield return null;
                        continue;
                    }

                    if (shouldLootAndContinue)
                    {
                        yield return null;
                        continue;
                    }

                    if (shouldLootAndContinue && questLoot != null && targetInfo != null)
                    {
                        yield return new WaitTime(BetterFollowbotLite.Instance.Settings.autoPilotInputFrequency);
                        switch (targetInfo.isTargeted)
                        {
                            case false:
                                yield return MouseoverItem(questLoot);
                                break;
                            case true:
                                yield return Mouse.LeftClick();
                                yield return new WaitTime(1000);
                                break;
                        }
                    }

                    if (shouldTransitionAndContinue)
                    {
                        BetterFollowbotLite.Instance.LogMessage("TRANSITION: Starting portal click sequence");

                        // Move mouse to portal position
                        BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Moving mouse to portal position ({transitionPos.X:F1}, {transitionPos.Y:F1})");
                        yield return Mouse.SetCursorPosHuman(transitionPos);

                        // Wait a bit for mouse to settle
                        yield return new WaitTime(60);

                        // Perform the click with additional logging
                        BetterFollowbotLite.Instance.LogMessage("TRANSITION: Performing left click on portal");
                        var currentMousePos = BetterFollowbotLite.Instance.GetMousePosition();
                        BetterFollowbotLite.Instance.LogMessage($"TRANSITION: Mouse position before click - X: {currentMousePos.X:F1}, Y: {currentMousePos.Y:F1}");

                        yield return Mouse.LeftClick();

                        // Wait for transition to start
                        BetterFollowbotLite.Instance.LogMessage("TRANSITION: Waiting for transition to process");
                        yield return new WaitTime(300);

                        BetterFollowbotLite.Instance.LogMessage("TRANSITION: Portal click sequence completed");
                        yield return null;
                        continue;
                    }

                    if (shouldClaimWaypointAndContinue)
                    {
                        if (Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, currentTask.WorldPosition) > 150)
                        {
                            yield return new WaitTime(BetterFollowbotLite.Instance.Settings.autoPilotInputFrequency);
                            yield return Mouse.SetCursorPosAndLeftClickHuman(waypointScreenPos, 100);
                            yield return new WaitTime(1000);
                        }
                        yield return null;
                        continue;
                    }

                    if (shouldDashAndContinue)
                    {
                        // LAST CHANCE CHECK: Before executing dash, check if player has turned around
                        if (ShouldClearPathForResponsiveness())
                        {
                            BetterFollowbotLite.Instance.LogMessage("LAST CHANCE 180 CHECK: Player direction changed before dash execution, aborting current task");
                            _taskManager.ClearTasksPreservingTransitions();
                            hasUsedWp = false; // Allow waypoint usage again
                            yield return null; // Skip this dash and recalculate
                            continue;
                        }

                        yield return Mouse.SetCursorPosHuman(Helper.WorldToValidScreenPosition(currentTask.WorldPosition));
                        BetterFollowbotLite.Instance.LogMessage("Dash: Mouse positioned, pressing dash key");
                        
                        // IMMEDIATE OVERRIDE CHECK: After positioning cursor, check if we need to override
                        if (ShouldClearPathForResponsiveness(true)) // Use aggressive override timing
                        {
                            BetterFollowbotLite.Instance.LogMessage("IMMEDIATE OVERRIDE: 180 detected after dash positioning - overriding with new position!");
                            _taskManager.ClearTasksPreservingTransitions();
                            hasUsedWp = false; // Allow waypoint usage again
                            
                            // INSTANT OVERRIDE: Position cursor towards player and dash there instead
                            var playerPos = BetterFollowbotLite.Instance.playerPosition;
                            var botPos = BetterFollowbotLite.Instance.localPlayer?.Pos ?? BetterFollowbotLite.Instance.playerPosition;
                            
                            // Calculate a position closer to the player for dash correction
                            var directionToPlayer = playerPos - botPos;
                            if (directionToPlayer.Length() > 10f) // Only if player is far enough away
                            {
                                directionToPlayer = Vector3.Normalize(directionToPlayer);
                                var correctionTarget = botPos + (directionToPlayer * 400f); // Dash 400 units towards player
                                
                                var correctScreenPos = Helper.WorldToValidScreenPosition(correctionTarget);
                                BetterFollowbotLite.Instance.LogMessage($"DEBUG: Dash override - Old position: {currentTask.WorldPosition}, Player position: {playerPos}");
                                BetterFollowbotLite.Instance.LogMessage($"DEBUG: Dash override - Correction target: {correctionTarget}");
                                yield return Mouse.SetCursorPosHuman(correctScreenPos);
                                Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                                lastDashTime = DateTime.Now; // Record dash time for cooldown
                                BetterFollowbotLite.Instance.LogMessage("DASH OVERRIDE: Dashed towards player position to override old dash");
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage("DEBUG: Dash override skipped - player too close to bot");
                            }
                            yield return null;
                            continue;
                        }
                        
                        if (instantPathOptimization)
                        {
                            // INSTANT MODE: Skip delays for immediate path correction
                            // Removed excessive INSTANT PATH OPTIMIZATION logging
                            Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                            lastDashTime = DateTime.Now; // Record dash time for cooldown
                            instantPathOptimization = false; // Reset flag after use
                        }
                        else
                        {
                            // Normal delays
                            yield return new WaitTime(random.Next(25) + 30);
                            Keyboard.KeyPress(BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                            lastDashTime = DateTime.Now; // Record dash time for cooldown
                            yield return new WaitTime(random.Next(25) + 30);
                        }
                        yield return null;
                        continue;
                    }

                    if (shouldTeleportConfirmAndContinue)
                    {
                        yield return Mouse.SetCursorPosHuman(new Vector2(currentTask.WorldPosition.X, currentTask.WorldPosition.Y));
                        yield return new WaitTime(200);
                        yield return Mouse.LeftClick();
                        yield return new WaitTime(200);
                        // CRITICAL: Move mouse to center of screen after teleport confirm to prevent unwanted movement
                        var screenCenter = new Vector2(BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width / 2, BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height / 2);
                        Mouse.SetCursorPos(screenCenter);
                        yield return new WaitTime(1000);
                        yield return null;
                        continue;
                    }

                    if (shouldTeleportButtonAndContinue)
                    {
                        yield return Mouse.SetCursorPosHuman(new Vector2(currentTask.WorldPosition.X, currentTask.WorldPosition.Y), false);
                        yield return new WaitTime(200);
                        yield return Mouse.LeftClick();
                        yield return new WaitTime(200);
                        // CRITICAL: Move mouse to center of screen after teleport button to prevent unwanted movement
                        var screenCenter = new Vector2(BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Width / 2, BetterFollowbotLite.Instance.GameController.Window.GetWindowRectangle().Height / 2);
                        Mouse.SetCursorPos(screenCenter);
                        yield return new WaitTime(200);
                        yield return null;
                        continue;
                    }
                }
            }

            lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;
            yield return new WaitTime(50);
        }
        // ReSharper disable once IteratorNeverReturns
    }

    // New method for decision making that runs every game tick
    public void UpdateAutoPilotLogic()
    {
        try
        {
            // GLOBAL TELEPORT PROTECTION: Block ALL task creation and responsiveness during teleport
            if (IsTeleportInProgress)
            {
                BetterFollowbotLite.Instance.LogMessage($"TELEPORT: Blocking all task creation - teleport in progress ({_taskManager.TaskCount} tasks)");
                return; // Exit immediately to prevent any interference
            }

            // PORTAL TRANSITION HANDLING: Actively search for portals during portal transition mode
            // TODO: Add logic to check how close the leader was to this portal before teleporting
            // This would help determine if we should click this portal or if there might be a closer one
            if (portalManager.IsInPortalTransition)
            {
                BetterFollowbotLite.Instance.LogMessage($"PORTAL: In portal transition mode - actively searching for portals to follow leader");

                // Get leader party element for portal search
                var leaderElement = _leaderDetector.GetLeaderPartyElement();
                if (leaderElement != null)
                {
                    // Force portal search during portal transition
                    var portal = GetBestPortalLabel(leaderElement, forceSearch: true);
                    if (portal != null)
                    {
                        BetterFollowbotLite.Instance.LogMessage($"PORTAL: Found portal '{portal.Label?.Text}' during transition - creating transition task");
                        _taskManager.AddTask(new TaskNode(portal, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                        BetterFollowbotLite.Instance.LogMessage($"PORTAL: Portal transition task created for portal at {portal.ItemOnGround.Pos}");
                    }
                    else
                    {
                        BetterFollowbotLite.Instance.LogMessage($"PORTAL: No portals found during transition - will retry on next update");
                    }
                }
                else
                {
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL: Cannot search for portals - no leader party element found");
                }
            }

            // PORTAL TRANSITION RESET: Clear portal transition mode when bot successfully reaches leader
            if (portalManager.IsInPortalTransition && this.followTarget != null)
            {
                var distanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, this.followTarget.Pos);
                // If bot is now close to leader after being far away, portal transition was successful
                if (distanceToLeader < 1000) // Increased from 300 to 1000 for portal transitions
                {
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL: Bot successfully reached leader after portal transition - clearing portal transition mode");
                    portalManager.SetPortalTransitionMode(false); // Clear portal transition mode to allow normal operation
                }
            }

            if (!BetterFollowbotLite.Instance.Settings.Enable.Value || !BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value || BetterFollowbotLite.Instance.localPlayer == null || !BetterFollowbotLite.Instance.localPlayer.IsAlive ||
                !BetterFollowbotLite.Instance.GameController.IsForeGroundCache || MenuWindow.IsOpened || BetterFollowbotLite.Instance.GameController.IsLoading || !BetterFollowbotLite.Instance.GameController.InGame)
            {
                return;
            }

            // COMPREHENSIVE ZONE LOADING PROTECTION: Prevent random movement during zone transitions
            // When loading into a new zone, entity lists might not be fully populated yet
            if (BetterFollowbotLite.Instance.GameController.IsLoading ||
                BetterFollowbotLite.Instance.GameController.Area.CurrentArea == null ||
                string.IsNullOrEmpty(BetterFollowbotLite.Instance.GameController.Area.CurrentArea.DisplayName))
            {
                BetterFollowbotLite.Instance.LogMessage("ZONE LOADING: Blocking all task creation during zone loading to prevent random movement");
                // Clear any existing tasks to prevent stale movement
                if (_taskManager.TaskCount > 0)
                {
                    var clearedTasks = _taskManager.TaskCount;
                    _taskManager.ClearTasks();
                    BetterFollowbotLite.Instance.LogMessage($"ZONE LOADING: Cleared {clearedTasks} tasks during zone loading");
                }
                return;
            }

            // ADDITIONAL SAFEGUARD: If no leader is found and no tasks exist, don't create any movement
            var leaderPartyElement = _leaderDetector.GetLeaderPartyElement();
            var followTarget = _leaderDetector.FindLeaderEntity();

            if (followTarget == null && _taskManager.TaskCount == 0)
            {
                // No leader found and no tasks - this is likely zone loading or leader not in range
                if (leaderPartyElement == null)
                {
                    BetterFollowbotLite.Instance.LogMessage("NO LEADER: No party leader found and no tasks - waiting for stable game state");
                    return; // Don't create any tasks when no leader exists
                }

                // Leader exists in party but not found in current zone - this is normal for zone transitions
                if (!leaderPartyElement.ZoneName.Equals(BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName))
                {
                    BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION DETECTED: Leader '{leaderPartyElement.PlayerName}' is in zone '{leaderPartyElement.ZoneName}' but we're in '{BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName}'");
                    // Continue with transition logic below
                }
                else
                {
                    BetterFollowbotLite.Instance.LogMessage("LEADER WAIT: Party leader exists but entity not found yet - waiting for entity loading");
                    return; // Wait for entity to become available
                }
            }

            // PRIORITY: Check for any open teleport confirmation dialogs and handle them immediately
            bool hasTransitionTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);
            if (!hasTransitionTasks)
            {
                var tpConfirmation = GetTpConfirmation();
                if (tpConfirmation != null)
                {
                    BetterFollowbotLite.Instance.LogMessage("TELEPORT: Found open confirmation dialog, handling it immediately");
                    var center = tpConfirmation.GetClientRect().Center;
                    _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                    // Return early to handle this task immediately
                    return;
                }
            }

            // Update player position for responsiveness detection - MORE FREQUENT UPDATES
            lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;

            // Update hasTransitionTasks check for the rest of the logic
            hasTransitionTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);

            if (followTarget == null && leaderPartyElement != null && !leaderPartyElement.ZoneName.Equals(BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName))
            {
                var currentZone = BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName ?? "Unknown";
                var leaderZone = leaderPartyElement.ZoneName ?? "Unknown";

                BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Leader is in different zone - Current: '{currentZone}', Leader: '{leaderZone}'");

                // Only add transition tasks if we don't already have any pending
                if (!hasTransitionTasks)
                {
                    BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: No pending transition tasks, searching for portal");
                    var portal = GetBestPortalLabel(leaderPartyElement);
                    if (portal != null)
                    {
                        // Hideout -> Map || Chamber of Sins A7 -> Map
                        BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Found portal '{portal.Label?.Text}' leading to leader zone '{leaderPartyElement.ZoneName}'");
                        _taskManager.AddTask(new TaskNode(portal, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                        BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: Portal transition task added to queue");
                    }
                    else
                    {
                        // No matching portal found, use party teleport (blue swirl)
                        BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: No matching portal found for '{leaderPartyElement.ZoneName}', falling back to party teleport");

                        // FIRST: Check if teleport confirmation dialog is already open (handle it immediately)
                        var tpConfirmation = GetTpConfirmation();
                        if (tpConfirmation != null)
                        {
                            BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: Teleport confirmation dialog already open, handling it");
                            // Add teleport confirmation task
                            var center = tpConfirmation.GetClientRect().Center;
                            _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                        }
                        else
                        {
                            // SECOND: Check if we can click the teleport button
                            var tpButton = leaderPartyElement != null ? GetTpButton(leaderPartyElement) : Vector2.Zero;
                            if(!tpButton.Equals(Vector2.Zero))
                            {
                                BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: Clicking teleport button to initiate party teleport");
                                _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: No teleport button available - cannot transition to leader's zone");
                            }
                        }
                    }
                }
                else
                {
                    BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: Transition tasks already pending, waiting for completion");
                }
            }
            else if (followTarget == null)
            {
                // Leader is not in current zone - look for portals to follow them
                if (leaderPartyElement != null && !leaderPartyElement.ZoneName.Equals(BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName))
                {
                    var currentZone = BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName ?? "Unknown";
                    var leaderZone = leaderPartyElement.ZoneName ?? "Unknown";

                    BetterFollowbotLite.Instance.LogMessage($"FOLLOW TARGET NULL: Leader in different zone - Current: '{currentZone}', Leader: '{leaderZone}'");

                    // Only add transition tasks if we don't already have any pending
                    if (!hasTransitionTasks)
                    {
                        BetterFollowbotLite.Instance.LogMessage("FOLLOW TARGET NULL: No pending transition tasks, searching for portal");
                        // Leader is in different zone, look for portals
                        var portal = GetBestPortalLabel(leaderPartyElement, forceSearch: false);
                        if (portal != null)
                        {
                            // Clear any existing movement tasks and add portal task
                            var movementTaskCount = _taskManager.CountTasks(t => t.Type == TaskNodeType.Movement);
                            _taskManager.RemoveTaskAll(t => t.Type == TaskNodeType.Movement);
                            BetterFollowbotLite.Instance.LogMessage($"FOLLOW TARGET NULL: Cleared {movementTaskCount} movement tasks, adding portal transition");
                            BetterFollowbotLite.Instance.LogMessage($"FOLLOW TARGET NULL: Found portal '{portal.Label?.Text}' for leader in zone '{leaderPartyElement.ZoneName}'");
                            _taskManager.AddTask(new TaskNode(portal, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance.Value, TaskNodeType.Transition));
                            BetterFollowbotLite.Instance.LogMessage("FOLLOW TARGET NULL: Portal transition task added to queue");
                        }
                        else
                        {
                            // No matching portal found, use party teleport
                            BetterFollowbotLite.Instance.LogMessage($"FOLLOW TARGET NULL: No matching portal found for '{leaderPartyElement.ZoneName}', using party teleport fallback");

                            // FIRST: Check if teleport confirmation dialog is already open (handle it immediately)
                            var tpConfirmation = GetTpConfirmation();
                            if (tpConfirmation != null)
                            {
                                BetterFollowbotLite.Instance.LogMessage("FOLLOW TARGET NULL: Teleport confirmation dialog already open, handling it");
                                // Add teleport confirmation task
                                var center = tpConfirmation.GetClientRect().Center;
                                _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                            }
                            else
                            {
                                // SECOND: Check if we can click the teleport button
                                var tpButton = leaderPartyElement != null ? GetTpButton(leaderPartyElement) : Vector2.Zero;
                                if(!tpButton.Equals(Vector2.Zero))
                                {
                                    BetterFollowbotLite.Instance.LogMessage("FOLLOW TARGET NULL: Clicking teleport button to initiate party teleport");
                                    _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                                }
                                else
                                {
                                    BetterFollowbotLite.Instance.LogMessage("FOLLOW TARGET NULL: No teleport button available - cannot follow leader to new zone");
                                }
                            }
                        }
                    }
                    else
                    {
                        BetterFollowbotLite.Instance.LogMessage("FOLLOW TARGET NULL: Transition tasks already pending, waiting for completion");
                    }
                }
                else
                {
                    // Leader party element not available or in same zone, clear movement tasks
                    var movementTaskCount = _taskManager.CountTasks(t => t.Type == TaskNodeType.Movement);
                    if (movementTaskCount > 0)
                    {
                        _taskManager.RemoveTaskAll(t => t.Type == TaskNodeType.Movement);
                        BetterFollowbotLite.Instance.LogMessage($"FOLLOW TARGET NULL: Cleared {movementTaskCount} movement tasks - leader in same zone or party element unavailable");
                    }
                }
            } 
            else if (followTarget != null)
            {
                // CHECK RESPONSIVENESS FIRST - Clear paths when player moves significantly
                if (ShouldClearPathForResponsiveness())
                {
                    BetterFollowbotLite.Instance.LogMessage("RESPONSIVENESS: Preventing inefficient path creation - clearing for better tracking");
                    instantPathOptimization = true; // Enable instant mode for immediate response
                    _taskManager.ClearTasksPreservingTransitions();
                    hasUsedWp = false; // Allow waypoint usage again
                    
                    // FORCE IMMEDIATE PATH RECALCULATION - Skip normal logic and create direct path
                    // ADDITIONAL NULL CHECK: Ensure followTarget is still valid during responsiveness check
                    if (followTarget != null && followTarget.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                    {
                        var instantDistanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, FollowTargetPosition);
                        BetterFollowbotLite.Instance.LogMessage($"RESPONSIVENESS: Creating direct path to leader - Distance: {instantDistanceToLeader:F1}");

                        if (instantDistanceToLeader > 3000 && BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled) // Increased from 1000 to 1500 to reduce dash spam
                        {
                            // CRITICAL: Don't add dash tasks if we have an active transition task OR another dash task
                            var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                            if (!hasConflictingTasks)
                            {
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, 0, TaskNodeType.Dash));
                                // Removed excessive INSTANT PATH OPTIMIZATION logging
                            }
                            else
                            {
                                // Removed excessive INSTANT PATH OPTIMIZATION logging
                            }
                        }
                        else
                        {
                            _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                        }
                    }
                    else
                    {
                        BetterFollowbotLite.Instance.LogMessage("RESPONSIVENESS: followTarget became null during responsiveness check, skipping path creation");
                    }
                    return; // Skip the rest of the path creation logic
                }

                // CHECK PATH EFFICIENCY BEFORE CREATING NEW PATHS - PREVENT INEFFICIENT PATHS
                if (ShouldAbandonPathForEfficiency())
                {
                    // Removed excessive INSTANT PATH OPTIMIZATION logging
                    instantPathOptimization = true; // Enable instant mode for immediate response
                    _taskManager.ClearTasksPreservingTransitions();
                    hasUsedWp = false; // Allow waypoint usage again
                    
                    // FORCE IMMEDIATE PATH RECALCULATION - Skip normal logic and create direct path
                    // ADDITIONAL NULL CHECK: Ensure followTarget is still valid during efficiency check
                    if (followTarget != null && followTarget.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                    {
                        var instantDistanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, FollowTargetPosition);
                        // Reduced logging frequency to prevent lag
                        if (instantDistanceToLeader > 200f) // Only log for significant distances
                            // Removed excessive INSTANT PATH OPTIMIZATION logging

                        if (instantDistanceToLeader > 3000 && BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled) // Increased from 1000 to 1500 to reduce dash spam
                        {
                            // CRITICAL: Don't add dash tasks if we have an active transition task OR another dash task
                            var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                            if (!hasConflictingTasks)
                            {
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, 0, TaskNodeType.Dash));
                                // Removed excessive INSTANT PATH OPTIMIZATION logging
                            }
                            else
                            {
                                // Removed excessive INSTANT PATH OPTIMIZATION logging
                            }
                        }
                        else
                        {
                            _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                        }
                    }
                    else
                    {
                        // Removed excessive INSTANT PATH OPTIMIZATION logging
                    }
                    return; // Skip the rest of the path creation logic
                }

                // TODO: If in town, do not follow (optional)
                var distanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, followTarget.Pos);
                //We are NOT within clear path distance range of leader. Logic can continue
                if (distanceToLeader >= BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value)
                {
                    // IMPORTANT: Don't process large movements if we already have any transition-related task active
                    // This prevents zone transition detection from interfering with active transitions/teleports
                    if (_taskManager.Tasks.Any(t =>
                        t.Type == TaskNodeType.Transition ||
                        t.Type == TaskNodeType.TeleportConfirm ||
                        t.Type == TaskNodeType.TeleportButton))
                    {
                        BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: Transition/teleport task already active, skipping movement processing");
                        return; // Exit early to prevent interference
                    }

                    //Leader moved VERY far in one frame. Check for transition to use to follow them.
                    var distanceMoved = Vector3.Distance(lastTargetPosition, followTarget.Pos);
                    if (lastTargetPosition != Vector3.Zero && distanceMoved > BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value)
                    {
                        // Check if this is likely a zone transition (moved extremely far)
                        var isLikelyZoneTransition = distanceMoved > 1000; // Very large distance suggests zone transition

                        if (isLikelyZoneTransition)
                        {
                            BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION DETECTED: Leader moved {distanceMoved:F1} units, likely zone transition");

                            // First check if zone names are different (immediate detection)
                            var zonesAreDifferent = leaderPartyElement != null && !leaderPartyElement.ZoneName.Equals(BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName);

                            if (zonesAreDifferent)
                            {
                                BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Confirmed different zones - Current: '{BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName}', Leader: '{leaderPartyElement?.ZoneName}'");
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Zone names same but large distance, assuming transition anyway");
                            }

                            // Look for portals regardless of zone name confirmation - force portal search
                            var transition = GetBestPortalLabel(leaderPartyElement, forceSearch: true);

                            // If no portal matched by name, try to find the closest portal (likely the one the leader used)
                            if (transition == null)
                            {
                                BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: No portal matched by name, looking for closest portal");

                                // Get all portal labels again and find the closest one
                                var allPortals = BetterFollowbotLite.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                                    x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible &&
                                    x.ItemOnGround != null &&
                                    (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal")))
                                    .OrderBy(x => Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, x.ItemOnGround.Pos))
                                    .ToList();

                                if (allPortals != null && allPortals.Count > 0)
                                {
                                    // First, check if there's a special portal (Arena or Warden's Quarters) - give them priority
                                    var specialPortal = allPortals.FirstOrDefault(p =>
                                        PortalManager.IsSpecialPortal(p.Label?.Text ?? ""));
                                    LabelOnGround selectedPortal;

                                    if (specialPortal != null)
                                    {
                                        var portalType = PortalManager.GetSpecialPortalType(specialPortal.Label?.Text ?? "");
                                        var portalDistance = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, specialPortal.ItemOnGround.Pos);
                                        BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Found {portalType} portal at distance {portalDistance:F1}");

                                        if (portalDistance < 200)
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Using {portalType} portal as likely destination");
                                            selectedPortal = specialPortal;
                                        }
                                        else
                                        {
                                            // Special portal too far, fall back to closest
                                            selectedPortal = allPortals.First();
                                            BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: {portalType} portal too far, using closest instead");
                                        }
                                    }
                                    else
                                    {
                                        selectedPortal = allPortals.First();
                                    }

                                    var selectedDistance = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, selectedPortal.ItemOnGround.Pos);

                                    BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Selected portal '{selectedPortal.Label?.Text}' at distance {selectedDistance:F1}");

                                    // If the selected portal is reasonably close (within 800 units), it's likely the one the leader used
                                    // Increased from 500 to 800 to handle cases where leader transitions quickly
                                    if (selectedDistance < 800)
                                    {
                                        BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Using selected portal '{selectedPortal.Label?.Text}' as likely destination");
                                        transition = selectedPortal; // Set transition so we use this portal

                                        // CRITICAL: Clear all existing tasks when adding a transition task to ensure it executes immediately
                                        BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: Clearing all existing tasks to prioritize transition");
                                        _taskManager.ClearTasks();

                                        // Add the transition task immediately since we found a suitable portal
                                        _taskManager.AddTask(new TaskNode(selectedPortal, 200, TaskNodeType.Transition));
                                        BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: Transition task added and prioritized");
                                    }
                                    else
                                    {
                                        BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Selected portal too far ({selectedDistance:F1}), using party teleport");
                                    }
                                }
                            }

                            // Check for Portal within Screen Distance (original logic) - only if we haven't already added a task
                            if (transition != null && transition.ItemOnGround.DistancePlayer < 80)
                            {
                                // Since we cleared all tasks above when adding the transition task, this check is now simpler
                                // We only add if we don't have any transition tasks (which we shouldn't after clearing)
                                if (!_taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition))
                                {
                                    BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Found nearby portal '{transition.Label?.Text}', adding transition task");
                                    _taskManager.AddTask(new TaskNode(transition,200, TaskNodeType.Transition));
                                }
                                else
                                {
                                    BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Transition task already exists, portal handling already in progress");
                                }
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: No suitable portal found for zone transition");

                                // If no portal found but this looks like a zone transition, try party teleport as fallback
                                if (zonesAreDifferent || distanceMoved > 1500) // Even more aggressive for very large distances
                                {
                                    BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: No portal found, trying party teleport fallback");

                                    // Check if teleport confirmation dialog is already open
                                    var tpConfirmation = GetTpConfirmation();
                                    if (tpConfirmation != null)
                                    {
                                        BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: Teleport confirmation dialog already open, handling it");
                                        var center = tpConfirmation.GetClientRect().Center;
                                        _taskManager.AddTask(new TaskNode(new Vector3(center.X, center.Y, 0), 0, TaskNodeType.TeleportConfirm));
                                    }
                                    else
                                    {
                                        // Try to click the teleport button
                                        var tpButton = leaderPartyElement != null ? GetTpButton(leaderPartyElement) : Vector2.Zero;
                                        if(!tpButton.Equals(Vector2.Zero))
                                        {
                                            BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: Clicking teleport button to initiate party teleport");
                                            // SET GLOBAL FLAG: Prevent SMITE and other skills from interfering
                                            IsTeleportInProgress = true;
                                            _taskManager.AddTask(new TaskNode(new Vector3(tpButton.X, tpButton.Y, 0), 0, TaskNodeType.TeleportButton));
                                        }
                                        else
                                        {
                                            BetterFollowbotLite.Instance.LogMessage("ZONE TRANSITION: No teleport button available, cannot follow through transition");
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            BetterFollowbotLite.Instance.LogMessage($"LEADER MOVED FAR: Leader moved {distanceMoved:F1} units but within reasonable distance, using normal movement/dash");
                        }
                    }
                    //We have no path, set us to go to leader pos.
                    else if (_taskManager.TaskCount == 0 && distanceMoved < 2000 && distanceToLeader > 200 && distanceToLeader < 2000)
                    {
                        // Validate followTarget position before creating tasks
                        if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                        {
                            // If very far away, add dash task instead of movement task
                            if (distanceToLeader > 3000 && BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled) // Increased from 1500 to 3000 to reduce dash spam significantly
                            {
                            // CRITICAL: Don't add dash tasks if we have any active transition-related task OR another dash task OR teleport in progress
                            var shouldSkipDashTasks = _taskManager.Tasks.Any(t =>
                                t.Type == TaskNodeType.Transition ||
                                t.Type == TaskNodeType.TeleportConfirm ||
                                t.Type == TaskNodeType.TeleportButton ||
                                t.Type == TaskNodeType.Dash);

                            if (shouldSkipDashTasks || IsTeleportInProgress)
                            {
                                BetterFollowbotLite.Instance.LogMessage($"ZONE TRANSITION: Skipping dash task creation - conflicting tasks active ({_taskManager.CountTasks(t => t.Type == TaskNodeType.Dash)} dash tasks, {_taskManager.CountTasks(t => t.Type == TaskNodeType.Transition)} transition tasks, teleport={IsTeleportInProgress})");
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage($"Adding Dash task - Distance: {distanceToLeader:F1}, Dash enabled: {BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled}");
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, 0, TaskNodeType.Dash));
                            }
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage($"Adding Movement task - Distance: {distanceToLeader:F1}, Dash enabled: {BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled}, Dash threshold: 700");
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                            }
                        }
                        else
                        {
                            BetterFollowbotLite.Instance.LogError($"Invalid followTarget position: {followTarget?.Pos}, skipping task creation");
                        }
                    }
                    //We have a path. Check if the last task is far enough away from current one to add a new task node.
                    else if (_taskManager.TaskCount > 0)
                    {
                        // ADDITIONAL NULL CHECK: Ensure followTarget is still valid before extending path
                        if (followTarget != null && followTarget.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                        {
                            var distanceFromLastTask = Vector3.Distance(_taskManager.Tasks.Last().WorldPosition, followTarget.Pos);
                            // More responsive: reduce threshold by half for more frequent path updates
                            var responsiveThreshold = BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance.Value / 2;
                            if (distanceFromLastTask >= responsiveThreshold)
                            {
                        BetterFollowbotLite.Instance.LogMessage($"RESPONSIVENESS: Adding new path node - Distance: {distanceFromLastTask:F1}, Threshold: {responsiveThreshold:F1}");
                        BetterFollowbotLite.Instance.LogMessage($"DEBUG: Creating task to position: {FollowTargetPosition} (Player at: {BetterFollowbotLite.Instance.playerPosition})");
                        _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                            }
                        }
                        else
                        {
                            BetterFollowbotLite.Instance.LogMessage("PATH EXTENSION: followTarget became null during path extension, skipping task creation");
                        }
                    }
                }
                else
                {
                    //Clear all tasks except for looting/claim portal (as those only get done when we're within range of leader. 
                    if (_taskManager.TaskCount > 0)
                    {
                        for (var i = _taskManager.TaskCount - 1; i >= 0; i--)
                            if (_taskManager.Tasks[i].Type == TaskNodeType.Movement || _taskManager.Tasks[i].Type == TaskNodeType.Transition)
                                _taskManager.RemoveTaskAt(i);
                    }
                    if (BetterFollowbotLite.Instance.Settings.autoPilotCloseFollow.Value)
                    {
                        //Close follow logic. We have no current tasks. Check if we should move towards leader
                        if (distanceToLeader >= BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance.Value)
                            _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                    }

                    //Check if we should add quest loot logic. We're close to leader already
                    Entity questLoot = null;
                    try
                    {
                        questLoot = BetterFollowbotLite.Instance.GameController.EntityListWrapper.Entities
                            .Where(e => e?.Type == EntityType.WorldItem && e.IsTargetable && e.HasComponent<WorldItem>())
                            .FirstOrDefault(e =>
                            {
                                var itemEntity = e.GetComponent<WorldItem>().ItemEntity;
                                return BetterFollowbotLite.Instance.GameController.Files.BaseItemTypes.Translate(itemEntity.Path).ClassName ==
                                       "QuestItem";
                            });
                    }
                    catch
                    {
                        questLoot = null;
                    }
                    if (questLoot != null &&
                        Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, questLoot.Pos) < BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value &&
                        _taskManager.Tasks.FirstOrDefault(I => I.Type == TaskNodeType.Loot) == null)
                        _taskManager.AddTask(new TaskNode(questLoot.Pos, BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance, TaskNodeType.Loot));

                }
            }
            if (followTarget?.Pos != null)
                lastTargetPosition = followTarget.Pos;
        }
        catch (Exception e)
        {
            BetterFollowbotLite.Instance.LogError($"UpdateAutoPilotLogic Error: {e}");
        }
    }
    // ReSharper disable once IteratorNeverReturns




    public void Render()
    {
        if (BetterFollowbotLite.Instance.Settings.autoPilotToggleKey.PressedOnce())
        {
            BetterFollowbotLite.Instance.Settings.autoPilotEnabled.SetValueNoEvent(!BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value);
            _taskManager.ClearTasks();				
        }

        // Restart coroutine if it died
        if (BetterFollowbotLite.Instance.Settings.autoPilotEnabled && (autoPilotCoroutine == null || !autoPilotCoroutine.Running))
        {
            // Removed excessive coroutine restart logging
            StartCoroutine();
        }
        else if (BetterFollowbotLite.Instance.Settings.autoPilotEnabled)
        {
            if (_taskManager.TaskCount > 0)
            {
                // Removed excessive coroutine status logging
            }
            else
            {
                // Removed excessive coroutine status logging
            }
        }

        if (!BetterFollowbotLite.Instance.Settings.autoPilotEnabled || BetterFollowbotLite.Instance.GameController.IsLoading || !BetterFollowbotLite.Instance.GameController.InGame)
            return;

        try
        {
            var portalLabels =
                BetterFollowbotLite.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                    x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible &&
                    x.ItemOnGround != null &&
                    (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") ||
                     x.ItemOnGround.Metadata.ToLower().Contains("portal"))).ToList();

            foreach (var portal in portalLabels)
            {
                var portalLabel = portal.Label?.Text ?? "Unknown";
                var portalPos = Helper.WorldToValidScreenPosition(portal.ItemOnGround.Pos);
                var labelRect = portal.Label.GetClientRectCache;

                // Draw portal outline
                BetterFollowbotLite.Instance.Graphics.DrawLine(labelRect.TopLeft, labelRect.TopRight, 2f, Color.Firebrick);
                BetterFollowbotLite.Instance.Graphics.DrawLine(labelRect.TopRight, labelRect.BottomRight, 2f, Color.Firebrick);
                BetterFollowbotLite.Instance.Graphics.DrawLine(labelRect.BottomRight, labelRect.BottomLeft, 2f, Color.Firebrick);
                BetterFollowbotLite.Instance.Graphics.DrawLine(labelRect.BottomLeft, labelRect.TopLeft, 2f, Color.Firebrick);

                // Draw portal label above the portal
                var labelPos = new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 20);
                BetterFollowbotLite.Instance.Graphics.DrawText($"Portal: {portalLabel}", labelPos, Color.Yellow);

                // Draw distance from player to portal
                var distance = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, portal.ItemOnGround.Pos);
                var distancePos = new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 35);
                BetterFollowbotLite.Instance.Graphics.DrawText($"{distance:F1}m", distancePos, Color.Cyan);

                // Highlight special portals (Arena and Warden's Quarters)
                if (PortalManager.IsSpecialPortal(portalLabel))
                {
                    var portalType = PortalManager.GetSpecialPortalType(portalLabel);
                    BetterFollowbotLite.Instance.Graphics.DrawText(portalType, new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 50), Color.OrangeRed);
                }
            }
        }
        catch (Exception)
        {
            //ignore
        }
        /*
        // Debug for UI Element
        try
        {
            foreach (var partyElementWindow in PartyElements.GetPlayerInfoElementList())
            {
                if (string.Equals(partyElementWindow.PlayerName.ToLower(), BetterFollowbotLite.instance.Settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase))
                {
                    var windowOffset = BetterFollowbotLite.instance.GameController.Window.GetWindowRectangle().TopLeft;

                    var elemCenter = partyElementWindow.TPButton.GetClientRectCache.Center;
                    var finalPos = new Vector2(elemCenter.X + windowOffset.X, elemCenter.Y + windowOffset.Y);

                    BetterFollowbotLite.instance.Graphics.DrawText("Offset: " +windowOffset.ToString("F2"),new Vector2(300, 560));
                    BetterFollowbotLite.instance.Graphics.DrawText("Element: " +elemCenter.ToString("F2"),new Vector2(300, 580));
                    BetterFollowbotLite.instance.Graphics.DrawText("Final: " +finalPos.ToString("F2"),new Vector2(300, 600));
                }
            }
        }
        catch (Exception e)
        {

        }
        */

        // Cache Task to prevent access while Collection is changing.
        try
        {
            var taskCount = 0;
            var dist = 0f;
            var cachedTasks = _taskManager.Tasks;
            if (cachedTasks?.Count > 0)
            {
                BetterFollowbotLite.Instance.Graphics.DrawText(
                    "Current Task: " + cachedTasks[0].Type,
                    new Vector2(500, 160));
                foreach (var task in cachedTasks.TakeWhile(task => task?.WorldPosition != null))
                {
                    if (taskCount == 0)
                    {
                        BetterFollowbotLite.Instance.Graphics.DrawLine(
                            Helper.WorldToValidScreenPosition(BetterFollowbotLite.Instance.playerPosition),
                            Helper.WorldToValidScreenPosition(task.WorldPosition), 2f, Color.Pink);
                        dist = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, task.WorldPosition);
                    }
                    else
                    {
                        BetterFollowbotLite.Instance.Graphics.DrawLine(Helper.WorldToValidScreenPosition(task.WorldPosition),
                            Helper.WorldToValidScreenPosition(cachedTasks[taskCount - 1].WorldPosition), 2f, Color.Pink);
                    }

                    taskCount++;
                }
            }
            if (BetterFollowbotLite.Instance.localPlayer != null)
            {
                var targetDist = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, lastTargetPosition);
                BetterFollowbotLite.Instance.Graphics.DrawText(
                    $"Follow Enabled: {BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value}", new System.Numerics.Vector2(500, 120));
                BetterFollowbotLite.Instance.Graphics.DrawText(
                    $"Task Count: {taskCount:D} Next WP Distance: {dist:F} Target Distance: {targetDist:F}",
                    new System.Numerics.Vector2(500, 140));

            }
        }
        catch (Exception)
        {
            // ignored
        }

        BetterFollowbotLite.Instance.Graphics.DrawText("AutoPilot: Active", new System.Numerics.Vector2(350, 120));
        BetterFollowbotLite.Instance.Graphics.DrawText("Coroutine: " + (autoPilotCoroutine.Running ? "Active" : "Dead"), new System.Numerics.Vector2(350, 140));
        BetterFollowbotLite.Instance.Graphics.DrawText("Leader: " + (followTarget != null ? "Found" : "Null"), new System.Numerics.Vector2(350, 160));

        // Add transition task debugging
        var transitionTasks = _taskManager.Tasks.Where(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);
        if (transitionTasks.Any())
        {
            var currentTransitionTask = transitionTasks.First();
            BetterFollowbotLite.Instance.Graphics.DrawText($"Transition: {currentTransitionTask.Type}", new System.Numerics.Vector2(350, 180), Color.Yellow);

            if (currentTransitionTask.Type == TaskNodeType.Transition && currentTransitionTask.LabelOnGround != null)
            {
                var portalLabel = currentTransitionTask.LabelOnGround.Label?.Text ?? "Unknown";
                BetterFollowbotLite.Instance.Graphics.DrawText($"Portal: {portalLabel}", new System.Numerics.Vector2(350, 200), Color.Yellow);
            }
        }

        BetterFollowbotLite.Instance.Graphics.DrawLine(new System.Numerics.Vector2(490, 120), new System.Numerics.Vector2(490,220), 1, Color.White);
    }
}