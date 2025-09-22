using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using BetterFollowbotLite.Skills;
using BetterFollowbotLite.Skill;
using BetterFollowbotLite.Automation;
using BetterFollowbotLite.Core.TaskManagement;
using BetterFollowbotLite.Core.Movement;
using BetterFollowbotLite.Interfaces;
using BetterFollowbotLite.Core.LeaderDetection;
using BetterFollowbotLite.Core.Skills;

namespace BetterFollowbotLite;

public class BetterFollowbotLite : BaseSettingsPlugin<BetterFollowbotLiteSettings>, IFollowbotCore
{
    private const int Delay = 45;

    private const int MouseAutoSnapRange = 250;
    internal static BetterFollowbotLite Instance;
    
    // Leader detection service
    private ILeaderDetector leaderDetector;
    
    // Task management service
    private ITaskManager taskManager;

    // Terrain analyzer service
    private ITerrainAnalyzer terrainAnalyzer;

    // Pathfinding service
    private IPathfinding pathfinding;

    // Movement executor service
    private IMovementExecutor movementExecutor;

    internal AutoPilot autoPilot;
    private readonly Summons summons = new Summons();
    private SummonRagingSpirits summonRagingSpirits;
    private SummonSkeletons summonSkeletons;
    private RejuvenationTotem rejuvenationTotem;
    private AuraBlessing auraBlessing;
    private FlameLink flameLink;
    private SmiteBuff smiteBuff;
    private VaalSkills vaalSkillsAutomation;
    private Mines mines;
    private RespawnHandler respawnHandler;
    private GemLeveler gemLeveler;
    private PartyJoiner partyJoiner;
    private AutoMapTabber autoMapTabber;
    private AutomationManager automationManager;

    private List<Buff> buffs;
    private List<Entity> enemys = new List<Entity>();
    private bool isAttacking;
    private bool isCasting;
    private bool isMoving;
    private DateTime lastAreaChangeTime = DateTime.MinValue;
    private DateTime lastGraceLogTime = DateTime.MinValue;
    private DateTime lastGraceCheckLogTime = DateTime.MinValue;
    private DateTime lastAutoPilotUpdateLogTime = DateTime.MinValue;
    private Entity lastFollowTarget;
    private bool lastHadGrace;
    internal Entity localPlayer;
    internal Life player;
    internal Vector3 playerPosition;
    private Coroutine skillCoroutine;
    internal List<ActorSkill> skills = new List<ActorSkill>();
    private List<ActorVaalSkill> vaalSkills = new List<ActorVaalSkill>();



    public override bool Initialise()
    {
        if (Instance == null)
            Instance = this;

        // Initialize services with dependency injection
        leaderDetector = new LeaderDetector(this);
        taskManager = new TaskManager(this);
        terrainAnalyzer = new TerrainAnalyzer();
        pathfinding = new Core.Movement.Pathfinding(this, terrainAnalyzer);

        // Create portal manager (needed by path planner)
        var portalManager = new PortalManager();

        // Create path planner
        var pathPlanner = new Core.Movement.PathPlanner(this, leaderDetector, taskManager, portalManager);

        autoPilot = new AutoPilot(leaderDetector, taskManager, pathfinding, null, pathPlanner); // Create AutoPilot with pathPlanner
        movementExecutor = new MovementExecutor(this, taskManager, pathfinding, autoPilot); // Now create movementExecutor with autoPilot instance
        // Set movementExecutor in autoPilot (assuming we add a setter method)
        autoPilot.SetMovementExecutor(movementExecutor);

        // Initialize timestamps
        // lastAutoJoinPartyAttempt is now managed within PartyJoiner class

        GameController.LeftPanel.WantUse(() => Settings.Enable);
        skillCoroutine = new Coroutine(WaitForSkillsAfterAreaChange(), this);
        ExileCore.Core.ParallelRunner.Run(skillCoroutine);
        Input.RegisterKey(Settings.autoPilotToggleKey.Value);
        Settings.autoPilotToggleKey.OnValueChanged += () => { Input.RegisterKey(Settings.autoPilotToggleKey.Value); };
        autoPilot.StartCoroutine();

        // Initialize skill classes
        summonRagingSpirits = new SummonRagingSpirits(this, Settings, autoPilot, summons);
        summonSkeletons = new SummonSkeletons(this, Settings, autoPilot, summons);
        rejuvenationTotem = new RejuvenationTotem(this, Settings);
        auraBlessing = new AuraBlessing(this, Settings);
        flameLink = new FlameLink(this, Settings);
        smiteBuff = new SmiteBuff(this, Settings);
        vaalSkillsAutomation = new VaalSkills(this, Settings);
        mines = new Mines(this, Settings);

        // Initialize automation classes
        respawnHandler = new RespawnHandler(this, Settings);
        gemLeveler = new GemLeveler(this, Settings);
        partyJoiner = new PartyJoiner(this, Settings);
        autoMapTabber = new AutoMapTabber(this, Settings);

        // Initialize automation manager and register all features
        automationManager = new AutomationManager();

        // Register skills
        automationManager.RegisterSkill(summonRagingSpirits);
        automationManager.RegisterSkill(summonSkeletons);
        automationManager.RegisterSkill(rejuvenationTotem);
        automationManager.RegisterSkill(auraBlessing);
        automationManager.RegisterSkill(flameLink);
        automationManager.RegisterSkill(smiteBuff);
        automationManager.RegisterSkill(vaalSkillsAutomation);
        automationManager.RegisterSkill(mines);

        // Register automation features
        automationManager.RegisterAutomation(respawnHandler);
        automationManager.RegisterAutomation(gemLeveler);
        automationManager.RegisterAutomation(partyJoiner);
        automationManager.RegisterAutomation(autoMapTabber);

        return true;
    }
        
    #region IFollowbotCore Implementation
    
    // Settings property is already inherited from BaseSettingsPlugin<BetterFollowbotLiteSettings>
    public Vector3 PlayerPosition => playerPosition;
    public Entity LocalPlayer => localPlayer;
    GameController IFollowbotCore.GameController => GameController;
    public DateTime LastTimeAny { get; set; } = DateTime.MinValue;
    
    public void LogMessage(string message)
    {
        LogMsg(message);
    }
    
    public void LogError(string message)
    {
        LogMsg($"ERROR: {message}");
    }
    
    #endregion
        


    public int GetMonsterWithin(float maxDistance, MonsterRarity rarity = MonsterRarity.White)
    {
        return (from monster in enemys where monster.Rarity >= rarity select Vector2.Distance(new Vector2(monster.PosNum.X, monster.PosNum.Y), new Vector2(playerPosition.X, playerPosition.Y))).Count(distance => distance <= maxDistance);
    }

    // Method to get entities from GameController (used by skill classes)
    internal IEnumerable<Entity> GetEntitiesFromGameController()
    {
        try
        {
            // Try using EntityListWrapper first (as mentioned in original code comments)
            return GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster];
        }
        catch
        {
            try
            {
                // Fallback to direct Entities access
                return GameController.Entities.Where(x => x.Type == EntityType.Monster);
            }
            catch
            {
                // Return empty collection if all access methods fail
                return new List<Entity>();
            }
        }
    }

    // Method to get resurrect panel (used by RespawnHandler)
    internal dynamic GetResurrectPanel()
    {
        try
        {
            return GameController.IngameState.IngameUi.ResurrectPanel;
        }
        catch
        {
            return null;
        }
    }

    // Method to get gem level up panel (used by GemLeveler)
    internal dynamic GetGemLvlUpPanel()
    {
        try
        {
            return GameController.IngameState.IngameUi.GemLvlUpPanel;
        }
        catch
        {
            return null;
        }
    }

    // Method to get invites panel (used by PartyJoiner)
    internal dynamic GetInvitesPanel()
    {
        try
        {
            return GameController.IngameState.IngameUi.InvitesPanel;
        }
        catch
        {
            return null;
        }
    }

    // Method to get party elements (used by PartyJoiner)
    internal dynamic GetPartyElements()
    {
        try
        {
            return PartyElements.GetPlayerInfoElementList();
        }
        catch
        {
            return null;
        }
    }

        


    public Vector2 GetMousePosition()
    {
        return new Vector2(GameController.IngameState.MousePosX, GameController.IngameState.MousePosY);
    }

    /// <summary>
    /// Player buffs
    /// </summary>
    public List<Buff> Buffs => buffs;

    /// <summary>
    /// Enemy entities
    /// </summary>
    public List<Entity> Enemys => enemys;

    /// <summary>
    /// Summons manager
    /// </summary>
    public Summons Summons => summons;

    public bool Gcd()
    {
        return (DateTime.Now - LastTimeAny).TotalMilliseconds > Delay;
    }


    internal Keys GetSkillInputKey(int index)
    {
        return index switch
        {
            1 => Settings.inputKey1.Value,
            3 => Settings.inputKey3.Value,
            4 => Settings.inputKey4.Value,
            5 => Settings.inputKey5.Value,
            6 => Settings.inputKey6.Value,
            7 => Settings.inputKey7.Value,
            8 => Settings.inputKey8.Value,
            9 => Settings.inputKey9.Value,
            10 => Settings.inputKey10.Value,
            11 => Settings.inputKey11.Value,
            12 => Settings.inputKey12.Value,
            _ => Keys.Escape
        };
    }

    private IEnumerator WaitForSkillsAfterAreaChange()
    {
        while (skills == null || localPlayer == null || GameController.IsLoading || !GameController.InGame)
            yield return new WaitTime(200);

        yield return new WaitTime(1000);
        SkillInfo.UpdateSkillInfo(true);
    }

    public override void AreaChange(AreaInstance area)
    {
        base.AreaChange(area);

        // Track area change time to prevent random movement during transitions
        lastAreaChangeTime = DateTime.Now;

        // Log area change details with timestamp for debugging zone transition delays
        var areaChangeStartTime = DateTime.Now;
        var newAreaName = area?.DisplayName ?? "Unknown";
        var isHideout = area?.IsHideout ?? false;
        var realLevel = area?.RealLevel ?? 0;

        LogMessage($"AREA CHANGE: [{areaChangeStartTime:HH:mm:ss.fff}] Transitioned to '{newAreaName}' - Hideout: {isHideout}, Level: {realLevel}");

        // Reset player position to prevent large distance calculations in grace period logic
        if (GameController?.Game?.IngameState?.Data?.LocalPlayer != null)
        {
            playerPosition = GameController.Game.IngameState.Data.LocalPlayer.Pos;
            LogMessage($"AREA CHANGE: Reset player position to ({playerPosition.X:F1}, {playerPosition.Y:F1})");
        }

        SkillInfo.ResetSkills();
        skills = null;

        var coroutine = new Coroutine(WaitForSkillsAfterAreaChange(), this);
        ExileCore.Core.ParallelRunner.Run(coroutine);

        autoPilot.AreaChange();

        LogMessage("AREA CHANGE: Area change processing completed");

        // Enhanced leader detection after area change
        LogMessage($"AREA CHANGE: Enhanced leader detection - Leader: '{Settings.autoPilotLeader.Value}'");

        // Debug party information
        var partyMembers = PartyElements.GetPlayerInfoElementList();
        LogMessage($"AREA CHANGE: Party members found: {partyMembers.Count}");
        foreach (var member in partyMembers)
        {
            LogMessage($"AREA CHANGE: Party member - Name: '{member?.PlayerName}'");
        }

        var playerEntities = GameController.Entities.Where(x => x.Type == EntityType.Player).ToList();
        LogMessage($"AREA CHANGE: Found {playerEntities.Count} player entities total");

        // Debug all player entities
        foreach (var player in playerEntities)
        {
            var playerComp = player.GetComponent<Player>();
            if (playerComp != null)
            {
                LogMessage($"AREA CHANGE: Player entity - Name: '{playerComp.PlayerName}', Distance: {Vector3.Distance(playerPosition, player.Pos):F1}");
            }
        }

        var leaderEntity = playerEntities.FirstOrDefault(x =>
            x.GetComponent<Player>()?.PlayerName?.Equals(Settings.autoPilotLeader.Value, StringComparison.OrdinalIgnoreCase) == true);

        if (leaderEntity != null)
        {
            LogMessage($"AREA CHANGE: Leader entity found immediately - Name: '{leaderEntity.GetComponent<Player>()?.PlayerName}', Distance: {Vector3.Distance(playerPosition, leaderEntity.Pos):F1}");
            autoPilot.SetFollowTarget(leaderEntity);
        }
        else
        {
            LogMessage($"AREA CHANGE: Leader entity NOT found immediately - will rely on AutoPilot's built-in detection");

            // Additional debugging for leader search
            LogMessage($"AREA CHANGE: Searching for leader '{Settings.autoPilotLeader.Value}' among {playerEntities.Count} entities");

            foreach (var entity in playerEntities)
            {
                var playerComp = entity.GetComponent<Player>();
                if (playerComp != null)
                {
                    LogMessage($"AREA CHANGE: Entity found - Name: '{playerComp.PlayerName}', Match: {string.Equals(playerComp.PlayerName, Settings.autoPilotLeader.Value, StringComparison.OrdinalIgnoreCase)}");
                }
            }

            // Try alternative entity sources
            var allEntities = GameController.Entities.Where(x => x.Type == EntityType.Player).ToList();
            LogMessage($"AREA CHANGE: Alternative search - Found {allEntities.Count} entities total");

            var altLeaderEntity = allEntities.FirstOrDefault(x =>
                x.GetComponent<Player>()?.PlayerName?.Equals(Settings.autoPilotLeader.Value, StringComparison.OrdinalIgnoreCase) == true);

            if (altLeaderEntity != null)
            {
                LogMessage($"AREA CHANGE: Leader found with alternative search - Name: '{altLeaderEntity.GetComponent<Player>()?.PlayerName}'");
                autoPilot.SetFollowTarget(altLeaderEntity);
            }
            else
            {
                LogMessage("AREA CHANGE: Leader still not found - may not be loaded in zone yet");
            }
        }

        // Log completion time for debugging
        var areaChangeDuration = DateTime.Now - areaChangeStartTime;
        LogMessage($"AREA CHANGE: [{DateTime.Now:HH:mm:ss.fff}] Area change processing completed in {areaChangeDuration.TotalSeconds:F2}s");
    }
        
    public override void DrawSettings()
    {
        //base.DrawSettings();

        // Draw Custom GUI
        if (Settings.Enable)
            ImGuiDrawSettings.DrawImGuiSettings();
    }


    public override void Render()
    {
        try
        {
            if (!Settings.Enable) return;
            SkillInfo.GetDeltaTime();
                
            try
            {
                // Grace period removal with movement safeguards
                if (Settings.autoPilotEnabled.Value && Settings.autoPilotGrace.Value && buffs != null && buffs.Exists(x => x.Name == "grace_period"))
                {
                    var timeSinceAreaChange = (DateTime.Now - lastAreaChangeTime).TotalSeconds;
                    var timeSinceLastGraceLog = (DateTime.Now - lastGraceLogTime).TotalSeconds;

                    // Only log grace period status every 2 seconds to reduce spam
                    if (timeSinceLastGraceLog > 2.0)
                    {
                        LogMessage($"GRACE PERIOD: [{DateTime.Now:HH:mm:ss.fff}] Active grace period detected, time since area change: {timeSinceAreaChange:F1}s");
                        lastGraceLogTime = DateTime.Now;
                    }

                    // Check if leader is available and AutoPilot can work - if so, reduce stabilization time
                    var leaderAvailable = false;
                    try
                    {
                        var partyMembers = PartyElements.GetPlayerInfoElementList();
                        var leaderElement = partyMembers?.FirstOrDefault(x => x?.PlayerName?.Equals(Settings.autoPilotLeader.Value, StringComparison.OrdinalIgnoreCase) == true);
                        leaderAvailable = leaderElement != null;
                    }
                    catch { /* Ignore errors in leader check */ }

                    var stabilizationThreshold = leaderAvailable ? 0.3 : 1.0; // Very fast if leader is available

                    // Additional check: If AutoPilot already has a follow target, be extremely aggressive
                    if (autoPilot != null && autoPilot.FollowTarget != null)
                    {
                        stabilizationThreshold = 0.1; // Ultra fast if AutoPilot is already working
                    }

                    if (timeSinceAreaChange > stabilizationThreshold)
                    {
                        // Only log stabilization completion once
                        if (timeSinceLastGraceLog > 2.0)
                        {
                            LogMessage($"GRACE PERIOD: [{DateTime.Now:HH:mm:ss.fff}] Zone stabilization period passed, checking if safe to remove grace");
                        }

                        // Check player position to ensure they're not moving
                        var shouldRemoveGrace = true;

                        if (localPlayer != null)
                        {
                            // Use the same position method as the main render loop for consistency
                            var currentPos = localPlayer.Pos;

                            // Check if we've moved significantly since last check (use same position tracking as render)
                            var distanceMoved = Vector3.Distance(currentPos, playerPosition);

                            // Use a more reasonable threshold and add some tolerance for position updates
                            if (distanceMoved > 10.0f)
                            {
                                shouldRemoveGrace = false;
                                // Only log movement detection every 2 seconds to reduce spam
                                if (timeSinceLastGraceLog > 2.0)
                                {
                                    LogMessage($"GRACE PERIOD: Player moving ({distanceMoved:F1} units) - waiting to remove grace");
                                }
                            }
                            else
                            {
                                // Only log stationary detection when we first become stationary
                                if (timeSinceLastGraceLog > 2.0)
                                {
                                    LogMessage($"GRACE PERIOD: Player stationary ({distanceMoved:F1} units moved) - safe to remove grace");
                                }
                            }

                            // Update stored position for next check (sync with render loop)
                            playerPosition = currentPos;
                        }

                        if (shouldRemoveGrace)
                        {
                            // Check if we've recently pressed a key to avoid spam
                            var timeSinceLastAction = (DateTime.Now - LastTimeAny).TotalSeconds;

                            if (timeSinceLastAction > 0.2) // Very fast cooldown for immediate grace removal
                            {
                                // Move mouse to random position near center (±35 pixels, excluding -5 to +5 dead zone) before pressing move key
                                var screenRect = GameController.Window.GetWindowRectangle();
                                var screenCenterX = screenRect.Width / 2;
                                var screenCenterY = screenRect.Height / 2;

                                // Add random offset of ±35 pixels (excluding -5 to +5 dead zone)
                                var random = new Random();
                                int randomOffsetX, randomOffsetY;

                                // Generate X offset excluding -5 to +5 range
                                do
                                {
                                    randomOffsetX = random.Next(-35, 36); // -35 to +35
                                }
                                while (randomOffsetX >= -5 && randomOffsetX <= 5); // Exclude -5 to +5

                                // Generate Y offset excluding -5 to +5 range
                                do
                                {
                                    randomOffsetY = random.Next(-35, 36); // -35 to +35
                                }
                                while (randomOffsetY >= -5 && randomOffsetY <= 5); // Exclude -5 to +5

                                var targetX = screenCenterX + randomOffsetX;
                                var targetY = screenCenterY + randomOffsetY;

                                // Ensure mouse position stays within screen bounds
                                targetX = Math.Max(0, Math.Min(screenRect.Width, targetX));
                                targetY = Math.Max(0, Math.Min(screenRect.Height, targetY));

                                var randomMousePos = new Vector2(targetX, targetY);
                                Mouse.SetCursorPos(randomMousePos);

                                // Only log grace removal every 2 seconds to reduce spam
                                if (timeSinceLastGraceLog > 2.0)
                                {
                                    LogMessage($"GRACE PERIOD: [{DateTime.Now:HH:mm:ss.fff}] Moving mouse to ({targetX}, {targetY}) and pressing move key to break grace");
                                }

                                Keyboard.KeyPress(Settings.autoPilotMoveKey.Value);
                                LastTimeAny = DateTime.Now;

                                // Log successful grace removal
                                LogMessage($"GRACE PERIOD: [{DateTime.Now:HH:mm:ss.fff}] Grace period broken successfully after {timeSinceAreaChange:F1}s");
                            }
                            else
                            {
                                LogMessage("GRACE PERIOD: Waiting for action cooldown before removing grace");
                            }
                        }
                    }
                    else
                    {
                        var timeRemaining = Math.Max(0, stabilizationThreshold - timeSinceAreaChange);
                        // Only log stabilization progress every 2 seconds to reduce spam
                        if (timeSinceLastGraceLog > 2.0)
                        {
                            var hasFollowTarget = autoPilot != null && autoPilot.FollowTarget != null;
                            LogMessage($"GRACE PERIOD: Still stabilizing after zone change ({timeRemaining:F1}s remaining, leader available: {leaderAvailable}, has follow target: {hasFollowTarget})");
                        }
                    }
                }
                else
                {
                    // Log why grace period removal isn't active
                    var autopilotEnabled = Settings.autoPilotEnabled.Value;
                    var graceEnabled = Settings.autoPilotGrace.Value;
                    var hasBuffs = buffs != null;
                    var hasGraceBuff = buffs != null && buffs.Exists(x => x.Name == "grace_period");

                    // Throttle grace check logs to reduce spam (only log every 10 seconds when not in grace period)
                    var timeSinceLastGraceCheckLog = (DateTime.Now - lastGraceCheckLogTime).TotalSeconds;
                    if (timeSinceLastGraceCheckLog > 10.0)
                    {
                        LogMessage($"GRACE CHECK: [{DateTime.Now:HH:mm:ss.fff}] AutoPilot: {autopilotEnabled}, Grace Enabled: {graceEnabled}, Has Buffs: {hasBuffs}, Has Grace Buff: {hasGraceBuff}");
                        lastGraceCheckLogTime = DateTime.Now;
                    }
                }

                // Manual grace period breaking by pressing move key near screen center
                if (Settings.autoPilotEnabled.Value && Settings.autoPilotGrace.Value && buffs != null && buffs.Exists(x => x.Name == "grace_period"))
                {
                    try
                    {
                        // Get screen dimensions and center
                        var screenWidth = GameController.Game.IngameState.Camera.Width;
                        var screenHeight = GameController.Game.IngameState.Camera.Height;
                        var screenCenterX = screenWidth / 2;
                        var screenCenterY = screenHeight / 2;

                        // Get current mouse position
                        var mousePos = new Vector2(GameController.IngameState.MousePosX, GameController.IngameState.MousePosY);

                        // Check if mouse is within 100 pixels of screen center
                        var distanceFromCenter = Math.Sqrt(
                            Math.Pow(mousePos.X - screenCenterX, 2) +
                            Math.Pow(mousePos.Y - screenCenterY, 2)
                        );

                        if (distanceFromCenter <= 100.0)
                        {
                            // Check if move key is being pressed
                            if (Keyboard.IsKeyDown((int)Settings.autoPilotMoveKey.Value))
                            {
                                // Check cooldown to prevent spam
                                var timeSinceLastAction = (DateTime.Now - LastTimeAny).TotalSeconds;
                                if (timeSinceLastAction > 0.5) // 500ms cooldown
                                {
                                    // Log manual grace breaking
                                    var timeSinceLastGraceLog = (DateTime.Now - lastGraceLogTime).TotalSeconds;
                                    if (timeSinceLastGraceLog > 2.0) // Log every 2 seconds
                                    {
                                        LogMessage($"GRACE PERIOD: Manual break - mouse at center ({distanceFromCenter:F1}px from center)");
                                        lastGraceLogTime = DateTime.Now;
                                    }

                                    // Press move key to break grace period
                                    Keyboard.KeyPress(Settings.autoPilotMoveKey);
                                    LastTimeAny = DateTime.Now;
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Silent error handling for mouse/screen detection
                    }
                }

                // Debug AutoPilot status (only log significant changes or errors)
                if (autoPilot != null && Settings.debugMode.Value)
                {
                    var followTarget = autoPilot.FollowTarget;
                    if (followTarget == null && lastFollowTarget != null)
                    {
                        LogMessage("AUTOPILOT: Lost follow target");
                    }
                    else if (followTarget != null && lastFollowTarget == null)
                    {
                        LogMessage($"AUTOPILOT: Acquired follow target: {followTarget.GetComponent<Player>()?.PlayerName ?? "Unknown"}");
                    }

                    // Debug grace period status only when relevant
                    var hasGrace = buffs != null && buffs.Exists(x => x.Name == "grace_period");
                    if (!hasGrace && lastHadGrace)
                    {
                        LogMessage("GRACE: Grace period ended");
                    }

                    lastFollowTarget = followTarget;
                    lastHadGrace = hasGrace;
                }
                else
                {
                    LogMessage("AUTOPILOT: AutoPilot instance is null!");
                }

                // Check if AutoPilot has a follow target before updating
                if (autoPilot != null && autoPilot.FollowTarget == null)
                {
                    var leaderDetectionTime = DateTime.Now;
                    LogMessage($"AUTOPILOT: [{leaderDetectionTime:HH:mm:ss.fff}] No follow target set - attempting to find leader '{Settings.autoPilotLeader.Value}'");

                    // Try to find leader manually
                    var playerEntities = GameController.Entities.Where(x => x.Type == EntityType.Player).ToList();
                    LogMessage($"AUTOPILOT: [{DateTime.Now:HH:mm:ss.fff}] Found {playerEntities.Count} player entities to search");

                    var manualLeaderEntity = playerEntities.FirstOrDefault(x =>
                        x.GetComponent<Player>()?.PlayerName?.Equals(Settings.autoPilotLeader.Value, StringComparison.OrdinalIgnoreCase) == true);

                    if (manualLeaderEntity != null)
                    {
                        var detectionDuration = DateTime.Now - leaderDetectionTime;
                        LogMessage($"AUTOPILOT: [{DateTime.Now:HH:mm:ss.fff}] Found leader manually in {detectionDuration.TotalSeconds:F2}s - setting as follow target: '{manualLeaderEntity.GetComponent<Player>()?.PlayerName}' at distance {Vector3.Distance(playerPosition, manualLeaderEntity.Pos):F1}");
                        autoPilot.SetFollowTarget(manualLeaderEntity);
                    }
                    else
                    {
                        var detectionDuration = DateTime.Now - leaderDetectionTime;
                        LogMessage($"AUTOPILOT: [{DateTime.Now:HH:mm:ss.fff}] Still no leader found after {detectionDuration.TotalSeconds:F2}s - bot will not move. Available players:");
                        foreach (var player in playerEntities)
                        {
                            var playerComp = player.GetComponent<Player>();
                            if (playerComp != null)
                            {
                                LogMessage($"AUTOPILOT:   - '{playerComp.PlayerName}' (distance: {Vector3.Distance(playerPosition, player.Pos):F1})");
                            }
                        }
                    }
                }

                // CRITICAL: Update follow target position BEFORE AutoPilot logic
                // This prevents stale position data from causing incorrect movement
                autoPilot.UpdateFollowTargetPosition();

                // CRITICAL: Update AutoPilot logic BEFORE grace period check
                // AutoPilot should be able to create tasks even when grace period is active
                autoPilot.UpdateAutoPilotLogic();
                autoPilot.Render();

                // Debug AutoPilot tasks after update (throttled to reduce spam)
                if (autoPilot != null)
                {
                    var timeSinceLastAutoPilotLog = (DateTime.Now - lastAutoPilotUpdateLogTime).TotalSeconds;
                    if (timeSinceLastAutoPilotLog > 5.0) // Log every 5 seconds
                    {
                        var followTarget = autoPilot.FollowTarget;
                        LogMessage($"AUTOPILOT: After update - Task count: {autoPilot.Tasks.Count}, FollowTarget: {(followTarget != null ? followTarget.GetComponent<Player>()?.PlayerName ?? "Unknown" : "null")}");

                        if (followTarget != null && autoPilot.Tasks.Count == 0)
                        {
                            LogMessage("AUTOPILOT: Has follow target but no tasks - AutoPilot may not be moving the bot");
                        }
                        lastAutoPilotUpdateLogTime = DateTime.Now;
                    }
                }
            }
            catch (Exception e)
            {
                // Error handling without logging
            }


            if (GameController?.Game?.IngameState?.Data?.LocalPlayer == null || GameController?.IngameState?.IngameUi == null )
                return;
            var chatField = GameController?.IngameState?.IngameUi?.ChatPanel?.ChatInputElement?.IsVisible;
            if (chatField != null && (bool)chatField)
                return;
                    
            localPlayer = GameController.Game.IngameState.Data.LocalPlayer;
            player = localPlayer.GetComponent<Life>();
            buffs = localPlayer.GetComponent<Buffs>().BuffsList;
            isAttacking = localPlayer.GetComponent<Actor>().isAttacking;
            isCasting = localPlayer.GetComponent<Actor>().Action.HasFlag(ActionFlags.UsingAbility);
            isMoving = localPlayer.GetComponent<Actor>().isMoving;
            skills = localPlayer.GetComponent<Actor>().ActorSkills;
            vaalSkills = localPlayer.GetComponent<Actor>().ActorVaalSkills;
            playerPosition = localPlayer.Pos;

            #region Automation Execution

            // Execute all registered automation features through the automation manager
            automationManager?.ExecuteAll();

            #endregion
            if (GameController.Area.CurrentArea.IsHideout || GameController.Area.CurrentArea.IsTown ||
                /*GameController.IngameState.IngameUi.StashElement.IsVisible ||*/ // 3.15 Null
                GameController.IngameState.IngameUi.NpcDialog.IsVisible ||
                GameController.IngameState.IngameUi.SellWindow.IsVisible || MenuWindow.IsOpened ||
                !GameController.InGame || GameController.IsLoading) return;
                
            enemys = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster].Where(x =>
                x != null && x.IsAlive && x.IsHostile && x.GetComponent<Life>()?.CurHP > 0 && 
                x.GetComponent<Targetable>()?.isTargetable == true && 
                !(x?.GetComponent<Stats>()?.StatDictionary?[GameStat.CannotBeDamaged] > 0) &&
                GameController.Window.GetWindowRectangleTimeCache.Contains(
                    GameController.Game.IngameState.Camera.WorldToScreen(x.Pos))).ToList();
            if (Settings.debugMode)
            {
                Graphics.DrawText("Enemys: " + enemys.Count, new System.Numerics.Vector2(100, 120), Color.White);
            }
                
            // Corpses collection removed since no longer needed



                

            if (buffs.Exists(x => x.Name == "grace_period") ||
                !GameController.IsForeGroundCache)
                return;
                
            // Skill processing is now handled by the AutomationManager.ExecuteAll() method
            // Individual skill classes (SmiteBuff, VaalSkills, SummonSkeletons, etc.) contain their own Execute() logic
                    







        }
        catch (Exception e)
        {
            // Error handling without logging
        }
    }

    // Wont work when Private, No Touchy Touchy !!!
    // ReSharper disable once MemberCanBePrivate.Global
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Global")]
    public static partial class CommandHandler
    {
        public static void KillTcpConnectionForProcess(int processId)
        {
            MibTcprowOwnerPid[] table;
            const int afInet = 2;
            var buffSize = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref buffSize, true, afInet, TableClass.TcpTableOwnerPidAll);
            var buffTable = Marshal.AllocHGlobal(buffSize);
            try
            {
                var ret = GetExtendedTcpTable(buffTable, ref buffSize, true, afInet, TableClass.TcpTableOwnerPidAll);
                if (ret != 0)
                    return;
                var tab = (MibTcptableOwnerPid)Marshal.PtrToStructure(buffTable, typeof(MibTcptableOwnerPid));
                var rowPtr = (IntPtr)((long)buffTable + Marshal.SizeOf(tab.dwNumEntries));
                table = new MibTcprowOwnerPid[tab.dwNumEntries];
                for (var i = 0; i < tab.dwNumEntries; i++)
                {
                    var tcpRow = (MibTcprowOwnerPid)Marshal.PtrToStructure(rowPtr, typeof(MibTcprowOwnerPid));
                    table[i] = tcpRow;
                    rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf(tcpRow));

                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffTable);
            }

            //Kill Path Connection
            var pathConnection = table.FirstOrDefault(t => t.owningPid == processId);
            pathConnection.state = 12;
            var ptr = Marshal.AllocCoTaskMem(Marshal.SizeOf(pathConnection));
            Marshal.StructureToPtr(pathConnection, ptr, false);
            SetTcpEntry(ptr);


        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, TableClass tblClass, uint reserved = 0);

        [DllImport("iphlpapi.dll")]
        private static extern int SetTcpEntry(IntPtr pTcprow);

        [StructLayout(LayoutKind.Sequential)]
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public struct MibTcprowOwnerPid
        {
            public uint state;
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] localPort;
            public uint remoteAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] remotePort;
            public uint owningPid;

        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MibTcptableOwnerPid
        {
            public uint dwNumEntries;
            private readonly MibTcprowOwnerPid table;
        }

        private enum TableClass
        {
            TcpTableBasicListener,
            TcpTableBasicConnections,
            TcpTableBasicAll,
            TcpTableOwnerPidListener,
            TcpTableOwnerPidConnections,
            TcpTableOwnerPidAll,
            TcpTableOwnerModuleListener,
            TcpTableOwnerModuleConnections,
            TcpTableOwnerModuleAll
        }
    }
}