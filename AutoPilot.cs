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

    // Movement executor service
    private IMovementExecutor _movementExecutor;

    // Movement logic coordinator
    private MovementLogic _movementLogic;

        // Most Logic taken from Alpha Plugin
        private Coroutine autoPilotCoroutine;
        private readonly Random random = new Random();

    /// <summary>
    /// Constructor for AutoPilot
    /// </summary>
    public AutoPilot(ILeaderDetector leaderDetector, ITaskManager taskManager, IPathfinding pathfinding, IMovementExecutor movementExecutor)
    {
        _leaderDetector = leaderDetector ?? throw new ArgumentNullException(nameof(leaderDetector));
        _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
        _pathfinding = pathfinding ?? throw new ArgumentNullException(nameof(pathfinding));
        _movementExecutor = movementExecutor; // Allow null for circular dependency resolution
        portalManager = new PortalManager();
        _movementLogic = new MovementLogic(BetterFollowbotLite.Instance, leaderDetector, taskManager, pathfinding, movementExecutor, portalManager);
    }

    /// <summary>
    /// Sets the movement executor (used to resolve circular dependency)
    /// </summary>
    public void SetMovementExecutor(IMovementExecutor movementExecutor)
    {
        _movementExecutor = movementExecutor ?? throw new ArgumentNullException(nameof(movementExecutor));
        _movementLogic?.SetMovementExecutor(movementExecutor);
    }

        private Vector3 lastTargetPosition;
        private Vector3 lastPlayerPosition;
        private Entity followTarget;

        // Portal transition tracking for interzone portals

        // GLOBAL FLAG: Prevents SMITE and other skills from interfering during teleport
        public static bool IsTeleportInProgress { get; set; } = false;

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
                    // Position updated
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
    // Dash time tracking moved to MovementExecutor
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
        _movementExecutor.UpdateLastDashTime(DateTime.MinValue); // Reset dash cooldown on area change
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
            BetterFollowbotLite.Instance.LogMessage($"DEBUG: AutoPilotLogic loop - Enabled: {BetterFollowbotLite.Instance.Settings.Enable.Value}, AutoPilot: {BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value}, Tasks: {_taskManager.TaskCount}");

            if (!BetterFollowbotLite.Instance.Settings.Enable.Value || !BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value || BetterFollowbotLite.Instance.localPlayer == null || !BetterFollowbotLite.Instance.localPlayer.IsAlive ||
                !BetterFollowbotLite.Instance.GameController.IsForeGroundCache || MenuWindow.IsOpened || BetterFollowbotLite.Instance.GameController.IsLoading || !BetterFollowbotLite.Instance.GameController.InGame)
            {
                BetterFollowbotLite.Instance.LogMessage("DEBUG: AutoPilotLogic blocked - conditions not met");
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

            BetterFollowbotLite.Instance.LogMessage("DEBUG: AutoPilotLogic proceeding to task processing");

            // Process movement tasks using MovementLogic
            if (_taskManager.TaskCount > 0)
            {
                TaskNode currentTask = null;
                bool taskAccessError = false;

                try
                {
                    currentTask = _movementLogic.SelectNextTask();
                }
                catch (Exception e)
                {
                    taskAccessError = true;
                    BetterFollowbotLite.Instance.LogMessage($"Error selecting next task: {e.Message}");
                }

                if (taskAccessError || currentTask == null)
                {
                    yield return new WaitTime(50);
                    continue;
                }

                if (currentTask.WorldPosition == null)
                {
                    _taskManager.RemoveTask(currentTask);
                    yield return new WaitTime(50);
                    continue;
                }

                // Process the movement task using MovementLogic
                var taskProcessor = _movementLogic.ProcessMovementTask(currentTask);
                while (taskProcessor.MoveNext())
                {
                    yield return taskProcessor.Current;
                }

                // Update last player position and continue
                lastPlayerPosition = BetterFollowbotLite.Instance.playerPosition;



                // Task processing completed by MovementLogic
            }

            // Handle portal transitions using MovementLogic
            _movementLogic.HandlePortalTransitions();

            // Update last player position and wait
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
            BetterFollowbotLite.Instance.LogMessage($"DEBUG: UpdateAutoPilotLogic called - followTarget: {(followTarget != null ? "SET" : "NULL")}, Tasks: {_taskManager.TaskCount}");

            // CRITICAL: Update leader position tracking
            UpdateFollowTargetPosition();

            // DEBUG: Log current follow target status
            if (followTarget != null)
            {
                var playerName = followTarget.GetComponent<Player>()?.PlayerName ?? "Unknown";
                BetterFollowbotLite.Instance.LogMessage($"DEBUG: Current follow target '{playerName}' at {followTarget.Pos}, Valid: {followTarget.IsValid}");
            }
            else
            {
                BetterFollowbotLite.Instance.LogMessage("DEBUG: No follow target set");
            }

            // PROXIMITY-BASED LEADER DETECTION: If no follow target or invalid, look for nearby players
            if (followTarget == null || !followTarget.IsValid || followTarget.Pos == null ||
                float.IsNaN(followTarget.Pos.X) || float.IsNaN(followTarget.Pos.Y) || float.IsNaN(followTarget.Pos.Z))
            {
                BetterFollowbotLite.Instance.LogMessage("DEBUG: Follow target is invalid/null - checking for nearby players");

                // Look for the closest player within 300 units to automatically follow
                var closestPlayer = BetterFollowbotLite.Instance.GameController?.EntityListWrapper?.ValidEntitiesByType[EntityType.Player]
                    ?.Where(p => p.IsValid && p.Pos != null && !float.IsNaN(p.Pos.X) && !float.IsNaN(p.Pos.Y) && !float.IsNaN(p.Pos.Z))
                    ?.OrderBy(p => Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, p.Pos))
                    ?.FirstOrDefault(p => Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, p.Pos) < 300f);

                if (closestPlayer != null)
                {
                    var playerName = closestPlayer.GetComponent<Player>()?.PlayerName ?? "Unknown";
                    var distance = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, closestPlayer.Pos);
                    BetterFollowbotLite.Instance.LogMessage($"PROXIMITY LEADER: Auto-detected nearby player '{playerName}' at distance {distance:F1} - setting as follow target");
                    SetFollowTarget(closestPlayer);
                }
                else
                {
                    BetterFollowbotLite.Instance.LogMessage("DEBUG: No nearby players found within 300 units");
                }
            }

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
            if (portalManager.IsInPortalTransition && followTarget != null)
            {
                var distanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, followTarget.Pos);
                // If bot is now close to leader after being far away, portal transition was successful
                if (distanceToLeader < 1000) // Increased from 300 to 1000 for portal transitions
                {
                    BetterFollowbotLite.Instance.LogMessage($"PORTAL: Bot successfully reached leader after portal transition - clearing portal transition mode");
                    portalManager.SetPortalTransitionMode(false); // Clear portal transition mode to allow normal operation
                }
            }

            // Only create tasks if we have a valid follow target
            var followTargetValid = followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z);
            BetterFollowbotLite.Instance.LogMessage($"DEBUG: Follow target valid: {followTargetValid}, Portal transition: {portalManager.IsInPortalTransition}");

            if (followTargetValid)
            {
                var distanceToLeader = Vector3.Distance(BetterFollowbotLite.Instance.playerPosition, followTarget.Pos);
                var nodeDistance = BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance.Value;
                BetterFollowbotLite.Instance.LogMessage($"DEBUG: Distance to leader: {distanceToLeader:F1}, Node distance threshold: {nodeDistance}");

                // Create movement tasks if we're far from leader and not in portal transition
                if (distanceToLeader > nodeDistance && !portalManager.IsInPortalTransition)
                {
                    BetterFollowbotLite.Instance.LogMessage("DEBUG: Conditions met for task creation");
                    // Check if we should create a dash task instead
                    if (distanceToLeader > BetterFollowbotLite.Instance.Settings.autoPilotDashDistance &&
                        BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled)
                    {
                        // CRITICAL: Don't add dash tasks if we have an active transition task OR another dash task
                        var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                        if (!hasConflictingTasks)
                        {
                            _taskManager.AddTask(new TaskNode(followTarget.Pos, 0, TaskNodeType.Dash));
                            BetterFollowbotLite.Instance.LogMessage($"Created dash task to leader (distance: {distanceToLeader:F1})");
                        }
                    }
                    else
                    {
                        // Create movement task
                        _taskManager.AddTask(new TaskNode(followTarget.Pos, BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance));
                        BetterFollowbotLite.Instance.LogMessage($"Created movement task to leader (distance: {distanceToLeader:F1})");
                    }
                }
            }
            else
            {
                BetterFollowbotLite.Instance.LogMessage("No valid follow target - cannot create movement tasks");
            }

            // Update last target position for responsiveness tracking
            if (followTarget?.Pos != null)
                lastTargetPosition = followTarget.Pos;
        }
        catch (Exception e)
        {
            BetterFollowbotLite.Instance.LogError($"UpdateAutoPilotLogic Error: {e}");
        }
    }

    /// <summary>
    /// Render debug information for AutoPilot
    /// </summary>
    public void Render()
    {
        if (BetterFollowbotLite.Instance.Settings.debugMode)
        {
            // Task count display
            var taskCount = _taskManager?.TaskCount ?? 0;
            BetterFollowbotLite.Instance.Graphics.DrawText($"AutoPilot Tasks: {taskCount}", new System.Numerics.Vector2(10, 100), Color.White);

            // Portal transition status
            if (portalManager?.IsInPortalTransition ?? false)
            {
                BetterFollowbotLite.Instance.Graphics.DrawText("Portal Transition: ACTIVE", new System.Numerics.Vector2(10, 120), Color.Yellow);
            }

            // Movement logic status
            if (_movementLogic != null)
            {
                BetterFollowbotLite.Instance.Graphics.DrawText("Movement Logic: ACTIVE", new System.Numerics.Vector2(10, 140), Color.Green);
            }

            // Leader detection status
            var leaderEntity = _leaderDetector?.LeaderEntity;
            if (leaderEntity != null)
            {
                BetterFollowbotLite.Instance.Graphics.DrawText($"Leader: DETECTED", new System.Numerics.Vector2(10, 160), Color.Green);
            }
            else
            {
                BetterFollowbotLite.Instance.Graphics.DrawText("Leader: NOT FOUND", new System.Numerics.Vector2(10, 160), Color.Red);
            }
        }
    }
}
