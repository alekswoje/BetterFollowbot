using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using BetterFollowbot.Interfaces;
using BetterFollowbot.Core.TaskManagement;
using BetterFollowbot.Core.Movement;

namespace BetterFollowbot;

    public class AutoPilot
    {
        private PortalManager portalManager;
        private readonly ILeaderDetector _leaderDetector;
        private readonly ITaskManager _taskManager;
        private readonly IPathfinding _pathfinding;
        private IMovementExecutor _movementExecutor;
        private PathPlanner _pathPlanner;
        private readonly Random random = new Random();

        // Throttle frequent log messages
        private DateTime _lastMenuCheckLog = DateTime.MinValue;
        private DateTime _lastTeleportConfirmTime = DateTime.MinValue;

        // Portal retry tracking
        private int _portalClickAttempts = 0;
        private string _lastPortalAttemptZone = "";
        private string _failedPortalLabel = "";
        private const int MAX_PORTAL_ATTEMPTS = 3;

        // Plaque click tracking - stores entity addresses of clicked plaques to prevent spam clicking
        private HashSet<long> _clickedPlaques = new HashSet<long>();

        /// <summary>
        /// Resets the portal retry counter (called when successfully following leader or changing zones)
        /// </summary>
        private void ResetPortalRetryCounter()
        {
            if (_portalClickAttempts > 0)
            {
                BetterFollowbot.Instance.LogMessage($"PORTAL RETRY: Resetting portal retry counter (was at {_portalClickAttempts} attempts)");
            }
            _portalClickAttempts = 0;
            _lastPortalAttemptZone = "";
            _failedPortalLabel = "";
        }

        /// <summary>
        /// Checks if a plaque has already been clicked
        /// </summary>
        public bool HasClickedPlaque(long entityAddress)
        {
            return _clickedPlaques.Contains(entityAddress);
        }

        /// <summary>
        /// Marks a plaque as clicked to prevent spam clicking
        /// </summary>
        public void MarkPlaqueAsClicked(long entityAddress)
        {
            _clickedPlaques.Add(entityAddress);
            BetterFollowbot.Instance.LogMessage($"PLAQUE: Marked plaque at address {entityAddress} as clicked");
        }

        /// <summary>
        /// Gets a random action delay in milliseconds to make bot behavior less detectable
        /// </summary>
        private int GetRandomActionDelay()
        {
            if (BetterFollowbot.Instance.Settings.autoPilotRandomActionDelay.Value == 0)
                return 0;
            
            // Return a random value between 0 and the configured max delay
            return random.Next(0, BetterFollowbot.Instance.Settings.autoPilotRandomActionDelay.Value + 1);
        }

        /// <summary>
        /// Checks if any blocking UI elements are open that should prevent task execution
        /// </summary>
        private bool IsBlockingUiOpen()
        {
            return UIBlockingUtility.IsAnyBlockingUIOpen();
        }

        /// <summary>
        /// Validates if a portal is actually a real portal using entity type
        /// </summary>
        private bool IsValidPortal(LabelOnGround portal)
        {
            return PortalManager.IsValidPortal(portal);
        }

        /// <summary>
        /// Constructor for AutoPilot
        /// </summary>
    public AutoPilot(ILeaderDetector leaderDetector, ITaskManager taskManager, IPathfinding pathfinding, IMovementExecutor movementExecutor, PathPlanner pathPlanner, PortalManager portalManager)
        {
        _leaderDetector = leaderDetector ?? throw new ArgumentNullException(nameof(leaderDetector));
        _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
        _pathfinding = pathfinding ?? throw new ArgumentNullException(nameof(pathfinding));
        _movementExecutor = movementExecutor; // Allow null for circular dependency resolution
        _pathPlanner = pathPlanner ?? throw new ArgumentNullException(nameof(pathPlanner));
        this.portalManager = portalManager ?? throw new ArgumentNullException(nameof(portalManager));
        }

    /// <summary>
    /// Sets the movement executor (used to resolve circular dependency)
    /// </summary>
    public void SetMovementExecutor(IMovementExecutor movementExecutor)
    {
        _movementExecutor = movementExecutor ?? throw new ArgumentNullException(nameof(movementExecutor));
    }

        private Vector3 lastTargetPosition;
        private Vector3 lastPlayerPosition;
        private Entity followTarget;

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
            // Clear path cache when switching targets
            _pathfinding.ClearPathCache();
            BetterFollowbot.Instance.LogMessage($"AUTOPILOT: Set follow target '{target.GetComponent<Player>()?.PlayerName ?? "Unknown"}' at position: {target.Pos}");
        }
        else
        {
            // Clear path cache when clearing target
            _pathfinding.ClearPathCache();
            BetterFollowbot.Instance.LogMessage("AUTOPILOT: Cleared follow target");
        }
    }

    /// <summary>
    /// Updates the follow target's position if it exists
    /// This is crucial for zone transitions where the entity's position changes
    /// </summary>
    public void UpdateFollowTargetPosition(string leaderZoneName = null)
    {
        if (followTarget != null && followTarget.IsValid)
        {
            var updateStartTime = DateTime.Now;
            var newPosition = followTarget.Pos;

            if (lastTargetPosition != Vector3.Zero)
            {
                var distanceMoved = Vector3.Distance(lastTargetPosition, newPosition);

                if (distanceMoved > 500)
                {
                    var updateDuration = DateTime.Now - updateStartTime;
                    BetterFollowbot.Instance.LogMessage($"AUTOPILOT: [{DateTime.Now:HH:mm:ss.fff}] Follow target moved {distanceMoved:F0} units (possible zone transition) from {lastTargetPosition} to {newPosition} (update took {updateDuration.TotalMilliseconds:F0}ms)");
                }
            }

            portalManager.DetectPortalTransition(lastTargetPosition, newPosition, leaderZoneName);
            
            lastTargetPosition = newPosition;

            var totalUpdateDuration = DateTime.Now - updateStartTime;
            if (totalUpdateDuration.TotalMilliseconds > 10)
            {
                BetterFollowbot.Instance.LogMessage($"AUTOPILOT: [{DateTime.Now:HH:mm:ss.fff}] Position update completed in {totalUpdateDuration.TotalMilliseconds:F0}ms");
            }
        }
        else if (followTarget != null && !followTarget.IsValid)
        {
            // Instead of immediately clearing, try to find the leader again
            var retryTarget = _leaderDetector.FindLeaderEntity();
            if (retryTarget != null && retryTarget.IsValid)
            {
                BetterFollowbot.Instance.LogMessage("AUTOPILOT: Follow target was invalid but found again, continuing");
                followTarget = retryTarget;
                // Update lastTargetPosition to the new valid position
                if (followTarget.Pos != null)
                {
                    lastTargetPosition = followTarget.Pos;
                }
            }
            else
            {
                BetterFollowbot.Instance.LogMessage("AUTOPILOT: Follow target became invalid and retry failed, clearing");
                followTarget = null;
                lastTargetPosition = Vector3.Zero;
            }
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
            var mouseScreenPos = BetterFollowbot.Instance.GetMousePosition();
            var playerScreenPos = Helper.WorldToValidScreenPosition(BetterFollowbot.Instance.playerPosition);
            var targetScreenPos = Helper.WorldToValidScreenPosition(targetPosition);

            if (targetScreenPos.X < 0 || targetScreenPos.Y < 0 ||
                targetScreenPos.X > BetterFollowbot.Instance.GameController.Window.GetWindowRectangle().Width ||
                targetScreenPos.Y > BetterFollowbot.Instance.GameController.Window.GetWindowRectangle().Height)
            {
                var playerWorldPos = BetterFollowbot.Instance.playerPosition;
                var directionToTarget = targetPosition - playerWorldPos;

                if (directionToTarget.Length() < 10)
                    return true;

                directionToTarget.Normalize();
                var screenDirection = new Vector2(directionToTarget.X, -directionToTarget.Z);
                screenDirection.Normalize();

                var playerToCursorOffscreen = mouseScreenPos - playerScreenPos;
                if (playerToCursorOffscreen.Length() < 30)
                    return false;

                playerToCursorOffscreen.Normalize();

                var dotProductOffscreen = Vector2.Dot(screenDirection, playerToCursorOffscreen);
                var angleOffscreen = Math.Acos(Math.Max(-1, Math.Min(1, dotProductOffscreen))) * (180.0 / Math.PI);

                return angleOffscreen <= 90.0;
            }

            var playerToTarget = targetScreenPos - playerScreenPos;
            if (playerToTarget.Length() < 20)
                return true;

            playerToTarget.Normalize();

            var playerToCursor = mouseScreenPos - playerScreenPos;
            if (playerToCursor.Length() < 30)
                return false;

            playerToCursor.Normalize();

            var dotProduct = Vector2.Dot(playerToTarget, playerToCursor);
            var angle = Math.Acos(Math.Max(-1, Math.Min(1, dotProduct))) * (180.0 / Math.PI);

            return angle <= 60.0;
        }
        catch (Exception e)
        {
            BetterFollowbot.Instance.LogMessage($"IsCursorPointingTowardsTarget error: {e.Message}");
            return false;
        }
    }


    private bool ShouldClearPathForResponsiveness()
    {
        return ShouldClearPathForResponsiveness(false);
    }

    private bool ShouldClearPathForResponsiveness(bool isOverrideCheck)
    {
        try
        {
            int rateLimitMs = isOverrideCheck ? 2000 : 5000;
            if ((DateTime.Now - lastPathClearTime).TotalMilliseconds < rateLimitMs)
                return false;

            if ((DateTime.Now - lastResponsivenessCheck).TotalMilliseconds < 1000)
                return false;

            if (followTarget == null || _taskManager.TaskCount == 0)
                return false;

            var playerMovement = Vector3.Distance(BetterFollowbot.Instance.playerPosition, lastPlayerPosition);

            if (playerMovement > 300f)
            {
                lastPathClearTime = DateTime.Now;
                lastResponsivenessCheck = DateTime.Now;
                return true;
            }

            if (_taskManager.TaskCount > 0 && _taskManager.Tasks[0].WorldPosition != null)
            {
                Vector3 botPos = BetterFollowbot.Instance.localPlayer?.Pos ?? BetterFollowbot.Instance.playerPosition;
                Vector3 playerPos = BetterFollowbot.Instance.playerPosition;
                Vector3 currentTaskTarget = _taskManager.Tasks[0].WorldPosition;

                Vector3 botToTask = currentTaskTarget - botPos;
                Vector3 botToPlayer = playerPos - botPos;

                if (botToTask.Length() > 10f && botToPlayer.Length() > 10f)
                {
                    botToTask = Vector3.Normalize(botToTask);
                    botToPlayer = Vector3.Normalize(botToPlayer);

                    float dotProduct = Vector3.Dot(botToTask, botToPlayer);

                    if (dotProduct < -0.5f)
                    {
                        lastPathClearTime = DateTime.Now;
                        lastResponsivenessCheck = DateTime.Now;
                        return true;
                    }
                }
            }

            var distanceToCurrentPlayer = Vector3.Distance(BetterFollowbot.Instance.localPlayer?.Pos ?? BetterFollowbot.Instance.playerPosition, lastTargetPosition);
            if (distanceToCurrentPlayer > 400f)
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

    private float CalculatePathEfficiency()
    {
        try
        {
            if (_taskManager.TaskCount == 0 || followTarget == null)
                return 1.0f;

            float directDistance = Vector3.Distance(BetterFollowbot.Instance.localPlayer?.Pos ?? BetterFollowbot.Instance.playerPosition, followTarget?.Pos ?? BetterFollowbot.Instance.playerPosition);

            if (directDistance < 30f)
                return 1.0f;

            float pathDistance = 0f;
            Vector3 currentPos = BetterFollowbot.Instance.localPlayer?.Pos ?? BetterFollowbot.Instance.playerPosition;

            foreach (var task in _taskManager.Tasks)
            {
                if (task.WorldPosition != null)
                {
                    pathDistance += Vector3.Distance(currentPos, task.WorldPosition);
                    currentPos = task.WorldPosition;
                }
            }

            if (pathDistance <= 0 || pathDistance < 50f)
                return 1.0f;

            float efficiency = directDistance / pathDistance;
            return efficiency;
        }
        catch (Exception e)
        {
            return 1.0f;
        }
    }

    private bool ShouldAbandonPathForEfficiency()
    {
        try
        {
            if (_taskManager.TaskCount < 1 || followTarget == null)
                return false;

            if ((DateTime.Now - lastEfficiencyCheck).TotalMilliseconds < 300)
                return false;

            float efficiency = CalculatePathEfficiency();

            if (efficiency < 0.8f)
            {
                lastEfficiencyCheck = DateTime.Now;
                return true;
            }

            if (_taskManager.TaskCount >= 1 && _taskManager.Tasks[0].WorldPosition != null)
            {
                Vector3 botPos = BetterFollowbot.Instance.localPlayer?.Pos ?? BetterFollowbot.Instance.playerPosition;
                Vector3 playerPos = BetterFollowbot.Instance.playerPosition;
                Vector3 pathTarget = _taskManager.Tasks[0].WorldPosition;

                Vector3 botToPath = pathTarget - botPos;
                Vector3 botToPlayer = playerPos - botPos;

                if (botToPath.Length() > 0 && botToPlayer.Length() > 0)
                {
                    botToPath = Vector3.Normalize(botToPath);
                    botToPlayer = Vector3.Normalize(botToPlayer);

                    float dotProduct = Vector3.Dot(botToPath, botToPlayer);
                    if (dotProduct < -0.1f)
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

    private void ResetPathing()
    {
        _taskManager.ClearTasks();
        followTarget = null;
        lastTargetPosition = Vector3.Zero;
        lastPlayerPosition = Vector3.Zero;
        hasUsedWp = false;
        _movementExecutor.UpdateLastDashTime(DateTime.MinValue);
        instantPathOptimization = false;
        lastPathClearTime = DateTime.MinValue;
        lastResponsivenessCheck = DateTime.MinValue;
        lastEfficiencyCheck = DateTime.MinValue;
        _lastTeleportConfirmTime = DateTime.MinValue;

        IsTeleportInProgress = false;
    }



    private LabelOnGround GetBestPortalLabel(PartyElementWindow leaderPartyElement, bool forceSearch = false)
    {
        try
        {
            if (leaderPartyElement == null)
                return null;

            // SPECIAL CASE: Labyrinth areas - use different logic since party TP doesn't work
            if (PortalManager.IsInLabyrinthArea)
            {
                BetterFollowbot.Instance.LogMessage("PORTAL SEARCH: In labyrinth area, using nearest portal logic");
                var nearestPortal = PortalManager.FindNearestLabyrinthPortal();
                if (nearestPortal != null)
                {
                    BetterFollowbot.Instance.LogMessage($"PORTAL SEARCH: Found nearest labyrinth portal: {nearestPortal.Label?.Text ?? "Unknown"} at distance {nearestPortal.ItemOnGround.DistancePlayer:F1}");
                    return nearestPortal;
                }
                else
                {
                    BetterFollowbot.Instance.LogMessage("PORTAL SEARCH: No portals found in labyrinth area");
                    return null;
                }
            }

            var currentZoneName = BetterFollowbot.Instance.GameController?.Area.CurrentArea.DisplayName;
            var leaderZoneName = leaderPartyElement.ZoneName;
            var isHideout = (bool)BetterFollowbot.Instance?.GameController?.Area?.CurrentArea?.IsHideout;
            var realLevel = BetterFollowbot.Instance.GameController?.Area?.CurrentArea?.RealLevel ?? 0;
            var zonesAreDifferent = !leaderPartyElement.ZoneName.Equals(currentZoneName);

            // Search for portals in various conditions:
            // 1. Force search (portal transition mode)
            // 2. Different zones
            // 3. Hideout
            // 4. High level different zones
            var shouldSearchForPortals = forceSearch || zonesAreDifferent || isHideout || (realLevel >= 68 && zonesAreDifferent);

            // Additionally, always check for special portals (arena portals) that might need clicking
            // even when zones are the same (arena portals don't change zones)
            if (!shouldSearchForPortals && !zonesAreDifferent && leaderPartyElement != null)
            {
                // Check if there are any special portals visible that we should prioritize
                var visiblePortals = BetterFollowbot.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                    x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible &&
                    PortalManager.IsSpecialPortal(x.Label?.Text?.ToLower() ?? "")).ToList();

                if (visiblePortals != null && visiblePortals.Count > 0)
                {
                    shouldSearchForPortals = true;
                    BetterFollowbot.Instance.LogMessage($"PORTAL SEARCH: Found {visiblePortals.Count} special portals, enabling portal search");
                }
            }

            if (shouldSearchForPortals)
            {
                var allPortalLabels = PortalManager.GetPortalsUsingEntities();
                BetterFollowbot.Instance.LogMessage($"PORTAL SEARCH: Found {allPortalLabels.Count} portal objects using entities");

                if (allPortalLabels == null || allPortalLabels.Count == 0)
                    return null;

                var matchingPortals = allPortalLabels.Where(x =>
                {
                    try
                    {
                        var labelText = x.Label?.Text?.ToLower() ?? "";
                        var leaderZone = leaderPartyElement.ZoneName?.ToLower() ?? "";
                        var currentZone = currentZoneName?.ToLower() ?? "";

                        var matchesLeaderZone = MatchesPortalToZone(labelText, leaderZone, x.Label?.Text ?? "");
                        var isSpecialPortal = PortalManager.IsSpecialPortal(labelText);
                        var isArenaPortal = PortalManager.GetSpecialPortalType(labelText) == "Arena";

                        // Debug logging for portal detection
                        if (isArenaPortal || isSpecialPortal)
                        {
                            BetterFollowbot.Instance.LogMessage($"PORTAL DETECT: '{x.Label?.Text}' - Arena: {isArenaPortal}, Special: {isSpecialPortal}, ZoneMatch: {matchesLeaderZone}, ZonesDifferent: {zonesAreDifferent}");
                        }

                        // Prioritize arena portals for inter-zone transitions, otherwise use normal logic
                        return (zonesAreDifferent && isArenaPortal) || matchesLeaderZone || isSpecialPortal;
                    }
                    catch (Exception ex)
                    {
                        return false;
                    }
                }).OrderByDescending(x =>
                {
                    // Sort by priority: Arena portals first, then zone-matching portals, then other special portals
                    var labelText = x.Label?.Text?.ToLower() ?? "";
                    var isArenaPortal = PortalManager.GetSpecialPortalType(labelText) == "Arena";
                    var matchesLeaderZone = MatchesPortalToZone(labelText, leaderPartyElement.ZoneName?.ToLower() ?? "", x.Label?.Text ?? "");
                    var isSpecialPortal = PortalManager.IsSpecialPortal(labelText);

                    if (zonesAreDifferent && isArenaPortal) return 3; // Highest priority for arena portals in different zones
                    if (matchesLeaderZone) return 2; // Zone matching portals
                    if (isSpecialPortal) return 1; // Other special portals
                    return 0; // Shouldn't happen due to filtering above
                }).ThenBy(x => Vector3.Distance(lastTargetPosition, x.ItemOnGround.Pos)).ToList();

                if (matchingPortals.Count > 0)
                {
                    var selectedPortal = matchingPortals.First();
                    var selectedLabel = selectedPortal.Label?.Text ?? "Unknown";
                    var portalType = PortalManager.GetSpecialPortalType(selectedLabel.ToLower());
                    BetterFollowbot.Instance.LogMessage($"PORTAL SELECT: Chose '{selectedLabel}' (Type: {portalType}) from {matchingPortals.Count} matching portals");
                    return selectedPortal;
                }
                else
                {
                    BetterFollowbot.Instance.LogMessage($"PORTAL SELECT: No matching portals found (checked {allPortalLabels.Count} total portals)");
                }

                return null;
            }

            return null;
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    private bool MatchesPortalToZone(string portalLabel, string zoneName, string originalLabel)
    {
        if (string.IsNullOrEmpty(portalLabel) || string.IsNullOrEmpty(zoneName))
            return false;

        portalLabel = portalLabel.ToLower();
        zoneName = zoneName.ToLower();

        if (portalLabel.Contains(zoneName))
            return true;

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
                return true;
        }

        if (PortalManager.IsSpecialPortal(portalLabel))
            return true;

        if (portalLabel.Equals(zoneName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (portalLabel.Contains(zoneName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (zoneName.Contains("hideout") && (portalLabel.Contains("hideout") || portalLabel.Contains("home")))
            return true;

        if (zoneName.Contains("town") && (portalLabel.Contains("town") || portalLabel.Contains("waypoint")))
            return true;

        return false;
    }

    private Vector2 GetTpButton(PartyElementWindow leaderPartyElement)
    {
        try
        {
            if (leaderPartyElement == null)
                return Vector2.Zero;

            var windowOffset = BetterFollowbot.Instance.GameController.Window.GetWindowRectangle().TopLeft;
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
            var ui = BetterFollowbot.Instance.GameController?.IngameState?.IngameUi?.PopUpWindow;

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

        // Reset portal retry counter on area change
        ResetPortalRetryCounter();

        // Clear clicked plaques on area change
        _clickedPlaques.Clear();
        BetterFollowbot.Instance.LogMessage("PLAQUE: Cleared clicked plaques tracking on area change");

        // Clear A* path cache on area change
        _pathfinding.ClearPathCache();

        // Initialize terrain data through the pathfinding service
        var terrainInitStart = DateTime.Now;
        _pathfinding.InitializeTerrain();
        var terrainInitTime = DateTime.Now - terrainInitStart;
        BetterFollowbot.Instance.LogMessage($"TERRAIN INIT: Terrain initialization completed in {terrainInitTime.TotalMilliseconds:F2}ms");
    }

    public void StartCoroutine()
    {
        Task.Run(() => AutoPilotLogic());
    }
    private async Task AutoPilotLogic()
    {
        while (true)
        {
            if (!BetterFollowbot.Instance.Settings.Enable.Value || !BetterFollowbot.Instance.Settings.autoPilotEnabled.Value || BetterFollowbot.Instance.localPlayer == null || !BetterFollowbot.Instance.localPlayer.IsAlive ||
                !BetterFollowbot.Instance.GameController.IsForeGroundCache || IsBlockingUiOpen() || BetterFollowbot.Instance.GameController.IsLoading || !BetterFollowbot.Instance.GameController.InGame)
            {
                // Log check failures that might cause delays
                if (!BetterFollowbot.Instance.GameController.IsForeGroundCache)
                {
                    BetterFollowbot.Instance.LogMessage("FOREGROUND CHECK: Game not in foreground - blocking task execution");
                }
                if (IsBlockingUiOpen() && (DateTime.Now - _lastMenuCheckLog).TotalSeconds > 5)
                {
                    var openUIs = UIBlockingUtility.GetOpenBlockingUIsString();
                    BetterFollowbot.Instance.LogMessage($"UI CHECK: Blocking UI is open - blocking task execution. Open UIs: {openUIs}");
                    _lastMenuCheckLog = DateTime.Now;
                }
                await Task.Delay(100);
                continue;
            }

            if (BetterFollowbot.Instance.GameController.IsLoading ||
                BetterFollowbot.Instance.GameController.Area.CurrentArea == null ||
                string.IsNullOrEmpty(BetterFollowbot.Instance.GameController.Area.CurrentArea.DisplayName))
            {
                BetterFollowbot.Instance.LogMessage("TASK EXECUTION: Blocking task execution during zone loading");
                await Task.Delay(200);
                continue;
            }

            if (BetterFollowbot.Instance.ShouldWaitForLeaderGrace)
            {
                var hasTransitionTasks = _taskManager.Tasks.Any(t => 
                    t.Type == TaskNodeType.Transition || 
                    t.Type == TaskNodeType.TeleportConfirm || 
                    t.Type == TaskNodeType.TeleportButton);
                
                if (!hasTransitionTasks && _taskManager.TaskCount > 0)
                {
                    _taskManager.ClearTasks();
                    BetterFollowbot.Instance.LogMessage("LEADER GRACE: Cleared non-transition tasks - waiting for leader to break grace period");
                }
                
                if (!hasTransitionTasks)
                {
                    await Task.Delay(100);
                    continue;
                }
            }

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
                        BetterFollowbot.Instance.LogMessage($"PRIORITY: Processing teleport task {currentTask.Type} instead of {_taskManager.Tasks.First().Type}");
                    }
                    catch (Exception e)
                    {
                        taskAccessError = true;
                        BetterFollowbot.Instance.LogMessage($"PRIORITY: Error accessing teleport task - {e.Message}");
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
                    await Task.Delay(50);
                    continue;
                }

                if (currentTask?.WorldPosition == null)
                {
                    // Remove the task from its actual position, not just index 0
                    _taskManager.RemoveTask(currentTask);
                    await Task.Delay(50);
                    continue;
                }

                // Log task execution start
                BetterFollowbot.Instance.LogMessage($"TASK EXECUTION: Starting {currentTask.Type} task at {currentTask.WorldPosition} (Queue size: {_taskManager.TaskCount})");

                var taskDistance = Vector3.Distance(BetterFollowbot.Instance.playerPosition, currentTask.WorldPosition);
                var playerDistanceMoved = Vector3.Distance(BetterFollowbot.Instance.playerPosition, lastPlayerPosition);

                // Check if we should clear path for better responsiveness to player movement
                if (ShouldClearPathForResponsiveness())
                {
                    instantPathOptimization = true; // Enable instant mode for immediate response
                    _taskManager.ClearTasksPreservingTransitions(); // Clear all tasks and reset related state
                    hasUsedWp = false; // Allow waypoint usage again
                    
                    // FORCE IMMEDIATE PATH CREATION - Don't wait for UpdateAutoPilotLogic
                    if (followTarget?.Pos != null && !float.IsNaN(followTarget.Pos.X) && !float.IsNaN(followTarget.Pos.Y) && !float.IsNaN(followTarget.Pos.Z))
                    {
                        var instantDistanceToLeader = Vector3.Distance(BetterFollowbot.Instance.playerPosition, FollowTargetPosition);

                        if (instantDistanceToLeader > BetterFollowbot.Instance.Settings.autoPilotDashDistance && BetterFollowbot.Instance.Settings.autoPilotDashEnabled) // Use configured dash distance
                        {
                            // CRITICAL: Don't add dash tasks if we have an active transition task OR another dash task
                            var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                            if (!hasConflictingTasks)
                            {
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, 0, TaskNodeType.Dash));
                            }
                            else
                            {
                            }
                        }
                        else
                        {
                            _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbot.Instance.Settings.autoPilotPathfindingNodeDistance));
                        }
                    }
                    
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
                        var instantDistanceToLeader = Vector3.Distance(BetterFollowbot.Instance.playerPosition, FollowTargetPosition);

                        if (instantDistanceToLeader > BetterFollowbot.Instance.Settings.autoPilotDashDistance && BetterFollowbot.Instance.Settings.autoPilotDashEnabled) // Use configured dash distance
                        {
                            // CRITICAL: Don't add dash tasks if we have an active transition task OR another dash task
                            var hasConflictingTasks = _taskManager.Tasks.Any(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.Dash);
                            if (!hasConflictingTasks)
                            {
                                _taskManager.AddTask(new TaskNode(FollowTargetPosition, 0, TaskNodeType.Dash));
                            }
                            else
                            {
                            }
                        }
                        else
                        {
                            _taskManager.AddTask(new TaskNode(FollowTargetPosition, BetterFollowbot.Instance.Settings.autoPilotPathfindingNodeDistance));
                        }
                    }
                    
                    continue; // Skip current task processing, will recalculate path immediately
                }

                //We are using a same map transition and have moved significnatly since last tick. Mark the transition task as done.
                if (currentTask.Type == TaskNodeType.Transition &&
                    playerDistanceMoved >= BetterFollowbot.Instance.Settings.autoPilotClearPathDistance.Value)
                {
                    _taskManager.RemoveTask(currentTask);
                    lastPlayerPosition = BetterFollowbot.Instance.playerPosition;
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

                bool shouldTransitionAndContinue = false;
                bool shouldClaimWaypointAndContinue = false;
                bool shouldClickPlaqueAndContinue = false;
                bool shouldDashAndContinue = false;
                bool shouldTeleportConfirmAndContinue = false;
                bool shouldTeleportButtonAndContinue = false;
                bool shouldMovementContinue = false;


                // Transition-related variables
                Vector2 transitionPos = Vector2.Zero;

                // Waypoint-related variables
                Vector2 waypointScreenPos = Vector2.Zero;
                
                // Plaque-related variables
                Vector2 plaqueScreenPos = Vector2.Zero;

                // PRE-MOVEMENT OVERRIDE CHECK: Check if we should override BEFORE executing movement
                if (currentTask.Type == TaskNodeType.Movement)
                {
                    // SIMPLIFIED OVERRIDE: Just check if target is far from current player position
                    var playerPos = BetterFollowbot.Instance.playerPosition;
                    var botPos = BetterFollowbot.Instance.localPlayer?.Pos ?? BetterFollowbot.Instance.playerPosition;
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
                            Mouse.SetCursorPosHuman(correctScreenPos);

                            // Skip the rest of this movement task since we've overridden it
                            continue;
                        }
                    }
                }

                // Execute task through the movement executor
                var executionResult = _movementExecutor.ExecuteTask(currentTask, taskDistance, playerDistanceMoved);

                // Set local flags from execution result
                shouldDashToLeader = executionResult.ShouldDashToLeader;
                shouldTerrainDash = executionResult.ShouldTerrainDash;
                shouldTransitionAndContinue = executionResult.ShouldTransitionAndContinue;
                shouldClaimWaypointAndContinue = executionResult.ShouldClaimWaypointAndContinue;
                shouldClickPlaqueAndContinue = executionResult.ShouldClickPlaqueAndContinue;
                shouldDashAndContinue = executionResult.ShouldDashAndContinue;
                shouldTeleportConfirmAndContinue = executionResult.ShouldTeleportConfirmAndContinue;
                shouldTeleportButtonAndContinue = executionResult.ShouldTeleportButtonAndContinue;
                shouldMovementContinue = executionResult.ShouldMovementContinue;
                screenPosError = executionResult.ScreenPosError;
                keyDownError = executionResult.KeyDownError;
                keyUpError = executionResult.KeyUpError;
                taskExecutionError = executionResult.TaskExecutionError;
                movementScreenPos = executionResult.MovementScreenPos;
                transitionPos = executionResult.TransitionPos;
                waypointScreenPos = executionResult.WaypointScreenPos;


                // Handle error cleanup (simplified without try-catch)
                if (currentTask != null && currentTask.AttemptCount > 20)
                {
                    // Remove task if it's been attempted too many times
                    BetterFollowbot.Instance.LogMessage($"Task timeout - Too many attempts ({currentTask.AttemptCount}), removing task");
                    _taskManager.RemoveTask(currentTask);
                }

                // Handle portal invalidation after try-catch
                if (currentTask != null && currentTask.Type == TaskNodeType.Transition && !shouldTransitionAndContinue)
                {
                    // Portal was invalidated, wait and continue
                    await Task.Delay(100);
                    continue;
                }
                // Execute actions outside try-catch blocks
                else
                {
                    if (shouldDashToLeader)
                    {
                        // Add random delay for less detectable behavior
                        var randomDelay = GetRandomActionDelay();
                        if (randomDelay > 0)
                            await Task.Delay(randomDelay);

                        Mouse.SetCursorPosHuman(Helper.WorldToValidScreenPosition(FollowTargetPosition));
                        BetterFollowbot.Instance.LogMessage("Movement task: Dash mouse positioned, pressing key");
                        if (instantPathOptimization)
                        {
                            // INSTANT MODE: Skip delays for immediate path correction
                            // Removed excessive INSTANT PATH OPTIMIZATION logging
                            Keyboard.KeyPress(BetterFollowbot.Instance.Settings.autoPilotDashKey);
                            _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                            instantPathOptimization = false; // Reset flag after use
                        }
                        else
                        {
                            // Normal delays
                            await Task.Delay(random.Next(25) + 30);
                            Keyboard.KeyPress(BetterFollowbot.Instance.Settings.autoPilotDashKey);
                            _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                            await Task.Delay(random.Next(25) + 30);
                        }
                        continue;
                    }

                    if (shouldTerrainDash)
                    {
                        _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown (CheckDashTerrain already performed the dash)
                        continue;
                    }

                    if (screenPosError)
                    {
                        await Task.Delay(50);
                        continue;
                    }

                    if (!screenPosError && currentTask.Type == TaskNodeType.Movement)
                    {
                        // LAST CHANCE CHECK: Before executing movement, check if player has turned around
                        if (ShouldClearPathForResponsiveness())
                        {
                            BetterFollowbot.Instance.LogMessage("LAST CHANCE 180 CHECK: Player direction changed before movement execution, aborting current task");
                            _taskManager.ClearTasksPreservingTransitions();
                            hasUsedWp = false; // Allow waypoint usage again
 // Skip this movement and recalculate
                            continue;
                        }

                        // Add random delay for less detectable behavior
                        var randomDelay = GetRandomActionDelay();
                        if (randomDelay > 0)
                            await Task.Delay(randomDelay);

                        BetterFollowbot.Instance.LogMessage("Movement task: Mouse positioned, pressing move key down");
                        BetterFollowbot.Instance.LogMessage($"Movement task: Move key: {BetterFollowbot.Instance.Settings.autoPilotMoveKey}");
                        Mouse.SetCursorPosHuman(movementScreenPos);
                        
                        if (instantPathOptimization)
                        {
                            // INSTANT MODE: Skip delays for immediate path correction
                            // Removed excessive INSTANT PATH OPTIMIZATION logging
                            instantPathOptimization = false; // Reset flag after use
                        }
                        else
                        {
                            // Normal delays
                            await Task.Delay(random.Next(25) + 30);
                            await Task.Delay(random.Next(25) + 30);
                        }
                        continue;
                    }


                    if (shouldTransitionAndContinue)
                    {
                        var currentZone = BetterFollowbot.Instance.GameController?.Area?.CurrentArea?.DisplayName ?? "";
                        var portalLabel = currentTask.Data?.ToString() ?? "Unknown";
                        
                        // Check if we've exceeded max portal attempts and should fall back to town portal
                        if (_portalClickAttempts >= MAX_PORTAL_ATTEMPTS && _lastPortalAttemptZone == currentZone)
                        {
                            BetterFollowbot.Instance.LogMessage($"PORTAL RETRY: Exceeded {MAX_PORTAL_ATTEMPTS} failed attempts on portal '{_failedPortalLabel}' - falling back to blue town portal");
                            
                            // Try to find a town portal (blue portal) as fallback
                            var townPortals = BetterFollowbot.Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels
                                .Where(x => x?.ItemOnGround != null && 
                                           x.ItemOnGround.Type == ExileCore.Shared.Enums.EntityType.TownPortal &&
                                           x.ItemOnGround.DistancePlayer < 500)
                                .OrderBy(x => x.ItemOnGround.DistancePlayer)
                                .ToList();
                            
                            if (townPortals != null && townPortals.Any())
                            {
                                var townPortal = townPortals.First();
                                var townPortalPos = Helper.WorldToValidScreenPosition(townPortal.ItemOnGround.Pos);
                                
                                BetterFollowbot.Instance.LogMessage($"PORTAL RETRY: Found town portal at distance {townPortal.ItemOnGround.DistancePlayer:F1}, attempting to use it");
                                
                                // Click the town portal
                                Mouse.SetCursorPosHuman(townPortalPos);
                                await Task.Delay(150);
                                Mouse.LeftClick();
                                await Task.Delay(500);
                                
                                // Reset counters after using town portal
                                _portalClickAttempts = 0;
                                _lastPortalAttemptZone = "";
                                _failedPortalLabel = "";
                            }
                            else
                            {
                                BetterFollowbot.Instance.LogMessage("PORTAL RETRY: No town portals found nearby, resetting attempts and will try again");
                                _portalClickAttempts = 0;
                                _lastPortalAttemptZone = "";
                                _failedPortalLabel = "";
                            }
                            
                            portalManager.SetPortalTransitionMode(false);
                            continue;
                        }
                        
                        BetterFollowbot.Instance.LogMessage($"TRANSITION: Starting portal click sequence (attempt {_portalClickAttempts + 1}/{MAX_PORTAL_ATTEMPTS})");

                        // Add random delay for less detectable behavior
                        var randomDelay = GetRandomActionDelay();
                        if (randomDelay > 0)
                            await Task.Delay(randomDelay);

                        // Move mouse to portal position with multiple attempts
                        BetterFollowbot.Instance.LogMessage($"TRANSITION: Moving mouse to portal position ({transitionPos.X:F1}, {transitionPos.Y:F1})");

                        // First attempt
                        Mouse.SetCursorPosHuman(transitionPos);
                        await Task.Delay(100);

                        var mousePosAfterMove = BetterFollowbot.Instance.GetMousePosition();
                        BetterFollowbot.Instance.LogMessage($"TRANSITION: Mouse position after first move - X: {mousePosAfterMove.X:F1}, Y: {mousePosAfterMove.Y:F1}");

                        // Check if close enough (within 30 pixels)
                        var distanceFromTarget = Math.Sqrt(Math.Pow(mousePosAfterMove.X - transitionPos.X, 2) + Math.Pow(mousePosAfterMove.Y - transitionPos.Y, 2));
                        if (distanceFromTarget > 30)
                        {
                            // Second attempt with direct Windows API
                            BetterFollowbot.Instance.LogMessage($"TRANSITION: First move failed ({distanceFromTarget:F1} pixels), trying direct Windows API");
                            System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)transitionPos.X, (int)transitionPos.Y);
                            await Task.Delay(100);

                            mousePosAfterMove = BetterFollowbot.Instance.GetMousePosition();
                            distanceFromTarget = Math.Sqrt(Math.Pow(mousePosAfterMove.X - transitionPos.X, 2) + Math.Pow(mousePosAfterMove.Y - transitionPos.Y, 2));
                            BetterFollowbot.Instance.LogMessage($"TRANSITION: Mouse position after second move - X: {mousePosAfterMove.X:F1}, Y: {mousePosAfterMove.Y:F1}");

                            if (distanceFromTarget > 30)
                            {
                                BetterFollowbot.Instance.LogMessage($"TRANSITION: Mouse movement failed - {distanceFromTarget:F1} pixels from target, skipping click");
                                await Task.Delay(200);
                                continue;
                            }
                        }

                        // Perform the click with additional logging
                        BetterFollowbot.Instance.LogMessage("TRANSITION: Performing left click on portal");

                        Mouse.LeftClick();

                        // Wait for transition to start
                        BetterFollowbot.Instance.LogMessage("TRANSITION: Waiting for transition to process");
                        await Task.Delay(500); // Increased from 300ms to give more time for zone change

                        // Check if zone changed after portal click
                        var newZone = BetterFollowbot.Instance.GameController?.Area?.CurrentArea?.DisplayName ?? "";
                        if (newZone != currentZone)
                        {
                            BetterFollowbot.Instance.LogMessage($"TRANSITION: Portal click successful! Zone changed from '{currentZone}' to '{newZone}'");
                            // Reset retry counters on success
                            _portalClickAttempts = 0;
                            _lastPortalAttemptZone = "";
                            _failedPortalLabel = "";
                        }
                        else
                        {
                            // Portal click failed - still in same zone
                            _portalClickAttempts++;
                            _lastPortalAttemptZone = currentZone;
                            _failedPortalLabel = portalLabel;
                            BetterFollowbot.Instance.LogMessage($"TRANSITION: Portal click may have failed - still in zone '{currentZone}' (attempt {_portalClickAttempts}/{MAX_PORTAL_ATTEMPTS})");
                        }

                        BetterFollowbot.Instance.LogMessage("TRANSITION: Portal click sequence completed");

                        // Reset portal transition mode after clicking a portal
                        // This prevents the bot from getting stuck in portal transition mode
                        portalManager.SetPortalTransitionMode(false);
                        BetterFollowbot.Instance.LogMessage("TRANSITION: Portal transition mode reset after portal click");

                        continue;
                    }

                    if (shouldClaimWaypointAndContinue)
                    {
                        if (Vector3.Distance(BetterFollowbot.Instance.playerPosition, currentTask.WorldPosition) > 150)
                        {
                            await Task.Delay(BetterFollowbot.Instance.Settings.autoPilotInputFrequency);
                            Mouse.SetCursorPosAndLeftClickHuman(waypointScreenPos, 100);
                            await Task.Delay(1000);
                        }
                        continue;
                    }

                    if (shouldClickPlaqueAndContinue)
                    {
                        BetterFollowbot.Instance.LogMessage("PLAQUE: Attempting to click trial plaque");
                        
                        // Get the plaque label screen position (EXACTLY like portals do)
                        if (currentTask.LabelOnGround?.Label != null)
                        {
                            try
                            {
                                var labelElement = currentTask.LabelOnGround.Label;
                                var labelRect = labelElement.GetClientRectCache;
                                
                                // Click the center of the label (same as portals - no window offset needed)
                                plaqueScreenPos = new Vector2(labelRect.Center.X, labelRect.Center.Y);
                                
                                var labelText = labelElement.Text ?? "Unknown";
                                BetterFollowbot.Instance.LogMessage($"PLAQUE: Clicking label '{labelText}' at screen position ({plaqueScreenPos.X:F1}, {plaqueScreenPos.Y:F1})");
                            }
                            catch (Exception ex)
                            {
                                BetterFollowbot.Instance.LogMessage($"PLAQUE: Error getting label position, using world position: {ex.Message}");
                                // Fallback to world position if label is unavailable
                                plaqueScreenPos = Helper.WorldToValidScreenPosition(currentTask.LabelOnGround.ItemOnGround.Pos);
                            }
                        }
                        else
                        {
                            // Fallback to world position
                            plaqueScreenPos = Helper.WorldToValidScreenPosition(currentTask.WorldPosition);
                        }
                        
                        // Add random delay for less detectable behavior
                        var randomDelay = GetRandomActionDelay();
                        if (randomDelay > 0)
                            await Task.Delay(randomDelay);
                        
                        // Click the plaque label
                        Mouse.SetCursorPosAndLeftClickHuman(plaqueScreenPos, 100);
                        await Task.Delay(300);
                        
                        // Mark this plaque as clicked using its entity address from the task data
                        if (currentTask.Data is long entityAddress)
                        {
                            MarkPlaqueAsClicked(entityAddress);
                            BetterFollowbot.Instance.LogMessage($"PLAQUE: Successfully clicked trial plaque at address {entityAddress}");
                        }
                        
                        // Remove the task after clicking
                        _taskManager.RemoveTask(currentTask);
                        
                        continue;
                    }

                    if (shouldDashAndContinue)
                    {
                        // LAST CHANCE CHECK: Before executing dash, check if player has turned around
                        if (ShouldClearPathForResponsiveness())
                        {
                            BetterFollowbot.Instance.LogMessage("LAST CHANCE 180 CHECK: Player direction changed before dash execution, aborting current task");
                            _taskManager.ClearTasksPreservingTransitions();
                            hasUsedWp = false; // Allow waypoint usage again
 // Skip this dash and recalculate
                            continue;
                        }

                        // Add random delay for less detectable behavior
                        var randomDelay = GetRandomActionDelay();
                        if (randomDelay > 0)
                            await Task.Delay(randomDelay);

                        Mouse.SetCursorPosHuman(Helper.WorldToValidScreenPosition(currentTask.WorldPosition));
                        BetterFollowbot.Instance.LogMessage("Dash: Mouse positioned, pressing dash key");
                        
                        // IMMEDIATE OVERRIDE CHECK: After positioning cursor, check if we need to override
                        if (ShouldClearPathForResponsiveness(true)) // Use aggressive override timing
                        {
                            BetterFollowbot.Instance.LogMessage("IMMEDIATE OVERRIDE: 180 detected after dash positioning - overriding with new position!");
                            _taskManager.ClearTasksPreservingTransitions();
                            hasUsedWp = false; // Allow waypoint usage again
                            
                            // INSTANT OVERRIDE: Position cursor towards player and dash there instead
                            var playerPos = BetterFollowbot.Instance.playerPosition;
                            var botPos = BetterFollowbot.Instance.localPlayer?.Pos ?? BetterFollowbot.Instance.playerPosition;
                            
                            // Calculate a position closer to the player for dash correction
                            var directionToPlayer = playerPos - botPos;
                            if (directionToPlayer.Length() > 10f) // Only if player is far enough away
                            {
                                directionToPlayer = Vector3.Normalize(directionToPlayer);
                                var correctionTarget = botPos + (directionToPlayer * 400f); // Dash 400 units towards player
                                
                                var correctScreenPos = Helper.WorldToValidScreenPosition(correctionTarget);
                                BetterFollowbot.Instance.LogMessage($"DEBUG: Dash override - Old position: {currentTask.WorldPosition}, Player position: {playerPos}");
                                BetterFollowbot.Instance.LogMessage($"DEBUG: Dash override - Correction target: {correctionTarget}");
                                Mouse.SetCursorPosHuman(correctScreenPos);
                                Keyboard.KeyPress(BetterFollowbot.Instance.Settings.autoPilotDashKey);
                                _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                                BetterFollowbot.Instance.LogMessage("DASH OVERRIDE: Dashed towards player position to override old dash");
                            }
                            else
                            {
                                BetterFollowbot.Instance.LogMessage("DEBUG: Dash override skipped - player too close to bot");
                            }
                            continue;
                        }
                        
                        if (instantPathOptimization)
                        {
                            // INSTANT MODE: Skip delays for immediate path correction
                            // Removed excessive INSTANT PATH OPTIMIZATION logging
                            Keyboard.KeyPress(BetterFollowbot.Instance.Settings.autoPilotDashKey);
                            _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                            instantPathOptimization = false; // Reset flag after use
                        }
                        else
                        {
                            // Normal delays
                            await Task.Delay(random.Next(25) + 30);
                            Keyboard.KeyPress(BetterFollowbot.Instance.Settings.autoPilotDashKey);
                            _movementExecutor.UpdateLastDashTime(DateTime.Now); // Record dash time for cooldown
                            await Task.Delay(random.Next(25) + 30);
                        }
                        continue;
                    }

                    if (shouldTeleportConfirmAndContinue)
                    {
                        // Check cooldown to prevent rapid-fire Enter presses that could open chat
                        var timeSinceLastConfirm = (DateTime.Now - _lastTeleportConfirmTime).TotalMilliseconds;
                        if (timeSinceLastConfirm < 1000) // 1 second cooldown
                        {
                            BetterFollowbot.Instance.LogMessage($"TELEPORT CONFIRM: Cooldown active ({timeSinceLastConfirm:F0}ms), skipping Enter press");
                            await Task.Delay(100);
                            continue;
                        }

                        // Simple Enter key press to confirm teleport
                        BetterFollowbot.Instance.LogMessage("TELEPORT CONFIRM: Pressing Enter to confirm");
                        Keyboard.KeyPress(Keys.Enter);
                        _lastTeleportConfirmTime = DateTime.Now;
                        await Task.Delay(200); // Wait for teleport to process
                        continue;
                    }

                    if (shouldTeleportButtonAndContinue)
                    {
                        // FAST TELEPORT: Direct mouse movement for speed
                        Mouse.SetCursorPos(new Vector2(currentTask.WorldPosition.X, currentTask.WorldPosition.Y));
                        await Task.Delay(50); // Reduced from 200ms
                        Mouse.LeftClick();
                        await Task.Delay(50); // Reduced from 200ms
                        // CRITICAL: Move mouse to center of screen after teleport button to prevent unwanted movement
                        var screenCenter = new Vector2(BetterFollowbot.Instance.GameController.Window.GetWindowRectangle().Width / 2, BetterFollowbot.Instance.GameController.Window.GetWindowRectangle().Height / 2);
                        Mouse.SetCursorPos(screenCenter);
                        await Task.Delay(100); // Reduced from 200ms
                        continue;
                    }
                }
            }

            lastPlayerPosition = BetterFollowbot.Instance.playerPosition;
            await Task.Delay(50);
        }
    }

    // New method for decision making that runs every game tick
    public void UpdateAutoPilotLogic()
    {
        try
        {
            var leaderPartyElement = _leaderDetector.GetLeaderPartyElement();
            var followTarget = _leaderDetector.FindLeaderEntity();

            lastPlayerPosition = BetterFollowbot.Instance.playerPosition;

            // Allow PlanPath to run even with null followTarget for zone transitions
            // PlanPath handles null followTarget gracefully
            _pathPlanner.PlanPath(followTarget, leaderPartyElement, lastTargetPosition, lastPlayerPosition);

            if (followTarget?.Pos != null)
                lastTargetPosition = followTarget.Pos;
        }
        catch (Exception e)
        {
            BetterFollowbot.Instance.LogError($"UpdateAutoPilotLogic Error: {e}");
        }
    }

    public void Render()
    {
        if (BetterFollowbot.Instance.Settings.autoPilotToggleKey.PressedOnce())
        {
            BetterFollowbot.Instance.Settings.autoPilotEnabled.SetValueNoEvent(!BetterFollowbot.Instance.Settings.autoPilotEnabled.Value);
            _taskManager.ClearTasks();
        }

        if (BetterFollowbot.Instance.Settings.autoPilotEnabled)
        {
            // AutoPilot runs continuously when enabled
        }

        if (!BetterFollowbot.Instance.Settings.autoPilotEnabled || BetterFollowbot.Instance.GameController.IsLoading || !BetterFollowbot.Instance.GameController.InGame)
            return;

        try
        {
            var portalLabels = PortalManager.GetPortalsUsingEntities();
            
            if (BetterFollowbot.Instance.Settings.debugMode)
            {
                BetterFollowbot.Instance.LogMessage($"PORTAL RENDER DEBUG: Rendering {portalLabels?.Count ?? 0} portals using entities");
            }

            foreach (var portal in portalLabels)
            {
                var portalLabel = portal.Label?.Text ?? "Unknown";
                var labelRect = portal.Label.GetClientRectCache;

                BetterFollowbot.Instance.Graphics.DrawLine(labelRect.TopLeft, labelRect.TopRight, 2f, SharpDX.Color.Firebrick);
                BetterFollowbot.Instance.Graphics.DrawLine(labelRect.TopRight, labelRect.BottomRight, 2f, SharpDX.Color.Firebrick);
                BetterFollowbot.Instance.Graphics.DrawLine(labelRect.BottomRight, labelRect.BottomLeft, 2f, SharpDX.Color.Firebrick);
                BetterFollowbot.Instance.Graphics.DrawLine(labelRect.BottomLeft, labelRect.TopLeft, 2f, SharpDX.Color.Firebrick);

                var labelPos = new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 20);
                BetterFollowbot.Instance.Graphics.DrawText($"Portal: {portalLabel}", labelPos, SharpDX.Color.Yellow);

                var distance = Vector3.Distance(BetterFollowbot.Instance.playerPosition, portal.ItemOnGround.Pos);
                var distancePos = new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 35);
                BetterFollowbot.Instance.Graphics.DrawText($"{distance:F1}m", distancePos, SharpDX.Color.Cyan);

                if (PortalManager.IsSpecialPortal(portalLabel))
                {
                    var portalType = PortalManager.GetSpecialPortalType(portalLabel);
                    var color = portalType == "Arena" ? SharpDX.Color.Red : SharpDX.Color.OrangeRed;
                    BetterFollowbot.Instance.Graphics.DrawText(portalType, new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 50), color);

                    // Add extra highlighting for arena portals
                    if (portalType == "Arena")
                    {
                        BetterFollowbot.Instance.Graphics.DrawText("PRIORITY", new System.Numerics.Vector2(labelRect.TopLeft.X, labelRect.TopLeft.Y - 65), SharpDX.Color.Yellow);
                    }

                    // Show where we will actually click (UI button center)
                    var clickPos = new System.Numerics.Vector2(labelRect.Center.X, labelRect.Center.Y);

                    // Draw a small cross at the click position
                    const int crossSize = 5;
                    BetterFollowbot.Instance.Graphics.DrawLine(
                        clickPos - new System.Numerics.Vector2(crossSize, 0),
                        clickPos + new System.Numerics.Vector2(crossSize, 0),
                        2f, SharpDX.Color.Green);
                    BetterFollowbot.Instance.Graphics.DrawLine(
                        clickPos - new System.Numerics.Vector2(0, crossSize),
                        clickPos + new System.Numerics.Vector2(0, crossSize),
                        2f, SharpDX.Color.Green);

                    BetterFollowbot.Instance.Graphics.DrawText("CLICK", new System.Numerics.Vector2(clickPos.X + 8, clickPos.Y - 8), SharpDX.Color.Green);
                }
            }
        }
        catch (Exception)
        {
            //ignore
        }

        BetterFollowbot.Instance.Graphics.DrawText("AutoPilot: Active", new System.Numerics.Vector2(350, 120));
        BetterFollowbot.Instance.Graphics.DrawText("Task: Async", new System.Numerics.Vector2(350, 140));
        BetterFollowbot.Instance.Graphics.DrawText("Leader: " + (followTarget != null ? "Found" : "Null"), new System.Numerics.Vector2(350, 160));

        var transitionTasks = _taskManager.Tasks.Where(t => t.Type == TaskNodeType.Transition || t.Type == TaskNodeType.TeleportConfirm || t.Type == TaskNodeType.TeleportButton);
        if (transitionTasks.Any())
        {
            var currentTransitionTask = transitionTasks.First();
            BetterFollowbot.Instance.Graphics.DrawText($"Transition: {currentTransitionTask.Type}", new System.Numerics.Vector2(350, 180), SharpDX.Color.Yellow);
        }

        try
        {
            var taskCount = 0;
            var dist = 0f;
            var cachedTasks = _taskManager.Tasks;
            if (cachedTasks?.Count > 0)
            {
                BetterFollowbot.Instance.Graphics.DrawText(
                    "Current Task: " + cachedTasks[0].Type,
                    new Vector2(500, 160));
                foreach (var task in cachedTasks.TakeWhile(task => task?.WorldPosition != null))
                {
                    if (taskCount == 0)
                    {
                        BetterFollowbot.Instance.Graphics.DrawLine(
                            Helper.WorldToValidScreenPosition(BetterFollowbot.Instance.playerPosition),
                            Helper.WorldToValidScreenPosition(task.WorldPosition), 2f, SharpDX.Color.Pink);
                        dist = Vector3.Distance(BetterFollowbot.Instance.playerPosition, task.WorldPosition);
                        BetterFollowbot.Instance.Graphics.DrawText(
                            "Distance: " + dist.ToString("F2") + "m",
                            new Vector2(500, 180));
                    }
                    taskCount++;
                }
                BetterFollowbot.Instance.Graphics.DrawText(
                    "Task Count: " + cachedTasks.Count,
                    new System.Numerics.Vector2(500, 140));
            }
        }
        catch (Exception)
        {
            //ignore
        }
    }
}
