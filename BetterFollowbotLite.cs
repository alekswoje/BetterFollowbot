using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
    
    private ILeaderDetector leaderDetector;
    private ITaskManager taskManager;
    private ITerrainAnalyzer terrainAnalyzer;
    private IPathfinding pathfinding;
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

        leaderDetector = new LeaderDetector(this);
        taskManager = new TaskManager(this);
        terrainAnalyzer = new TerrainAnalyzer();
        pathfinding = new Core.Movement.Pathfinding(this, terrainAnalyzer);

        var portalManager = new PortalManager();
        var pathPlanner = new Core.Movement.PathPlanner(this, leaderDetector, taskManager, portalManager);

        autoPilot = new AutoPilot(leaderDetector, taskManager, pathfinding, null, pathPlanner);
        movementExecutor = new MovementExecutor(this, taskManager, pathfinding, autoPilot);
        autoPilot.SetMovementExecutor(movementExecutor);

        GameController.LeftPanel.WantUse(() => Settings.Enable);
        Task.Run(() => WaitForSkillsAfterAreaChange());
        Input.RegisterKey(Settings.autoPilotToggleKey.Value);
        Settings.autoPilotToggleKey.OnValueChanged += () => { Input.RegisterKey(Settings.autoPilotToggleKey.Value); };
        autoPilot.StartCoroutine();

        summonRagingSpirits = new SummonRagingSpirits(this, Settings, autoPilot, summons);
        summonSkeletons = new SummonSkeletons(this, Settings, autoPilot, summons);
        rejuvenationTotem = new RejuvenationTotem(this, Settings);
        auraBlessing = new AuraBlessing(this, Settings);
        flameLink = new FlameLink(this, Settings);
        smiteBuff = new SmiteBuff(this, Settings);
        vaalSkillsAutomation = new VaalSkills(this, Settings);
        mines = new Mines(this, Settings);

        respawnHandler = new RespawnHandler(this, Settings);
        gemLeveler = new GemLeveler(this, Settings);
        partyJoiner = new PartyJoiner(this, Settings);
        autoMapTabber = new AutoMapTabber(this, Settings);

        automationManager = new AutomationManager();
        automationManager.RegisterSkill(summonRagingSpirits);
        automationManager.RegisterSkill(summonSkeletons);
        automationManager.RegisterSkill(rejuvenationTotem);
        automationManager.RegisterSkill(auraBlessing);
        automationManager.RegisterSkill(flameLink);
        automationManager.RegisterSkill(smiteBuff);
        automationManager.RegisterSkill(vaalSkillsAutomation);
        automationManager.RegisterSkill(mines);

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
    public IPathfinding Pathfinding => pathfinding;
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
        return (from monster in enemys
                let rarityComponent = monster.GetComponent<ObjectMagicProperties>()
                where rarityComponent != null && rarityComponent.Rarity >= rarity
                select Vector2.Distance(new Vector2(monster.PosNum.X, monster.PosNum.Y), new Vector2(playerPosition.X, playerPosition.Y)))
                .Count(distance => distance <= maxDistance);
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
    /// Dash to leader when no suitable smite targets are found nearby
    /// </summary>
    public void DashToLeaderForSmite()
    {
        if (Settings.autoPilotDashEnabled &&
            (DateTime.Now - movementExecutor.LastDashTime).TotalMilliseconds >= 3000 &&
            autoPilot.FollowTarget != null)
        {
            var leaderPos = autoPilot.FollowTarget.Pos;
            var distanceToLeader = Vector3.Distance(playerPosition, leaderPos);

            // CRITICAL: Don't dash if teleport is in progress
            if (!AutoPilot.IsTeleportInProgress)
            {
                // Check for transition tasks
                var hasTransitionTask = autoPilot.Tasks.Any(t =>
                    t.Type == TaskNodeType.Transition ||
                    t.Type == TaskNodeType.TeleportConfirm ||
                    t.Type == TaskNodeType.TeleportButton);

                if (!hasTransitionTask && distanceToLeader > Settings.autoPilotDashDistance)
                {
                    // Position mouse towards leader
                    var leaderScreenPos = GameController.IngameState.Camera.WorldToScreen(leaderPos);
                    Mouse.SetCursorPos(leaderScreenPos);

                    // Small delay to ensure mouse movement is registered
                    System.Threading.Thread.Sleep(50);

                    // Execute dash
                    Keyboard.KeyPress(Settings.autoPilotDashKey);
                    movementExecutor.UpdateLastDashTime(DateTime.Now);
                }
            }
        }
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

    private void WaitForSkillsAfterAreaChange()
    {
        while (skills == null || localPlayer == null || GameController.IsLoading || !GameController.InGame)
            System.Threading.Thread.Sleep(200);

        System.Threading.Thread.Sleep(1000);
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
            var oldPosition = playerPosition;
            playerPosition = GameController.Game.IngameState.Data.LocalPlayer.Pos;
            var positionChange = Vector3.Distance(oldPosition, playerPosition);
            LogMessage($"AREA CHANGE: Reset player position from ({oldPosition.X:F1}, {oldPosition.Y:F1}) to ({playerPosition.X:F1}, {playerPosition.Y:F1}) - Change: {positionChange:F1} units");
        }

        SkillInfo.ResetSkills();
        skills = null;

        System.Threading.Tasks.Task.Run(() => WaitForSkillsAfterAreaChange());

        autoPilot.AreaChange();

        LogMessage("AREA CHANGE: Area change processing completed");
        LogMessage($"AREA CHANGE: Enhanced leader detection - Leader: '{Settings.autoPilotLeader.Value}'");

        var partyMembers = PartyElements.GetPlayerInfoElementList();
        LogMessage($"AREA CHANGE: Party members found: {partyMembers.Count}");

        var playerEntities = GameController.Entities.Where(x => x.Type == EntityType.Player).ToList();
        LogMessage($"AREA CHANGE: Found {playerEntities.Count} player entities total");

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

            var allEntities = GameController.Entities.Where(x => x.Type == EntityType.Player).ToList();

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
                // CRITICAL FIX: Move AutoPilot logic BEFORE all checks so it works even when game is not in focus
                // Leader detection and task management must work in background
                if (autoPilot != null && autoPilot.FollowTarget == null)
                {
                    var leaderDetectionTime = DateTime.Now;
                    var timeSinceZoneLoad = (DateTime.Now - lastAreaChangeTime).TotalSeconds;

                    var playerEntities = GameController.Entities.Where(x => x.Type == EntityType.Player).ToList();

                    var manualLeaderEntity = playerEntities.FirstOrDefault(x =>
                        x.GetComponent<Player>()?.PlayerName?.Equals(Settings.autoPilotLeader.Value, StringComparison.OrdinalIgnoreCase) == true);

                    if (manualLeaderEntity != null)
                    {
                        var detectionDuration = DateTime.Now - leaderDetectionTime;
                        LogMessage($"AUTOPILOT: [{DateTime.Now:HH:mm:ss.fff}] ✓ Found leader '{Settings.autoPilotLeader.Value}' in {detectionDuration.TotalSeconds:F2}s ({timeSinceZoneLoad:F1}s since zone load) - distance: {Vector3.Distance(playerPosition, manualLeaderEntity.Pos):F1}");
                        autoPilot.SetFollowTarget(manualLeaderEntity);
                    }
                }

                // CRITICAL: AutoPilot task management must work in background
                autoPilot.UpdateFollowTargetPosition();
                autoPilot.UpdateAutoPilotLogic();
                autoPilot.Render();

                if (autoPilot != null)
                {
                    var timeSinceLastAutoPilotLog = (DateTime.Now - lastAutoPilotUpdateLogTime).TotalSeconds;
                    if (timeSinceLastAutoPilotLog > 5.0)
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
                            var currentPos = localPlayer.Pos;
                            var distanceMoved = Vector3.Distance(currentPos, playerPosition);

                            if (distanceMoved > 10.0f)
                            {
                                shouldRemoveGrace = false;
                                if (timeSinceLastGraceLog > 2.0)
                                {
                                    LogMessage($"GRACE PERIOD: Player moving ({distanceMoved:F1} units) - waiting to remove grace");
                                }
                            }
                            else
                            {
                                if (timeSinceLastGraceLog > 2.0)
                                {
                                    LogMessage($"GRACE PERIOD: Player stationary ({distanceMoved:F1} units moved) - safe to remove grace");
                                }
                            }

                            playerPosition = currentPos;
                        }

                        if (shouldRemoveGrace)
                        {
                            var timeSinceLastAction = (DateTime.Now - LastTimeAny).TotalSeconds;

                            if (timeSinceLastAction > 0.2)
                            {
                                var screenRect = GameController.Window.GetWindowRectangle();
                                var screenCenterX = screenRect.Width / 2;
                                var screenCenterY = screenRect.Height / 2;

                                var random = new Random();
                                int randomOffsetX, randomOffsetY;

                                do
                                {
                                    randomOffsetX = random.Next(-35, 36);
                                }
                                while (randomOffsetX >= -5 && randomOffsetX <= 5);

                                do
                                {
                                    randomOffsetY = random.Next(-35, 36);
                                }
                                while (randomOffsetY >= -5 && randomOffsetY <= 5);

                                var targetX = screenCenterX + randomOffsetX;
                                var targetY = screenCenterY + randomOffsetY;

                                targetX = Math.Max(0, Math.Min(screenRect.Width, targetX));
                                targetY = Math.Max(0, Math.Min(screenRect.Height, targetY));

                                var randomMousePos = new Vector2(targetX, targetY);
                                Mouse.SetCursorPos(randomMousePos);

                                if (timeSinceLastGraceLog > 2.0)
                                {
                                    LogMessage($"GRACE PERIOD: [{DateTime.Now:HH:mm:ss.fff}] Moving mouse to ({targetX}, {targetY}) and pressing move key to break grace");
                                }

                                Keyboard.KeyPress(Settings.autoPilotMoveKey.Value);
                                LastTimeAny = DateTime.Now;

                                LogMessage($"GRACE PERIOD: [{DateTime.Now:HH:mm:ss.fff}] Grace period broken successfully after {timeSinceAreaChange:F1}s");
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
                    var timeSinceLastGraceCheckLog = (DateTime.Now - lastGraceCheckLogTime).TotalSeconds;
                    if (timeSinceLastGraceCheckLog > 10.0)
                    {
                    var autopilotEnabled = Settings.autoPilotEnabled.Value;
                    var graceEnabled = Settings.autoPilotGrace.Value;
                    var hasBuffs = buffs != null;
                    var hasGraceBuff = buffs != null && buffs.Exists(x => x.Name == "grace_period");
                        LogMessage($"GRACE CHECK: [{DateTime.Now:HH:mm:ss.fff}] AutoPilot: {autopilotEnabled}, Grace Enabled: {graceEnabled}, Has Buffs: {hasBuffs}, Has Grace Buff: {hasGraceBuff}");
                        lastGraceCheckLogTime = DateTime.Now;
                    }
                }

                if (Settings.autoPilotEnabled.Value && Settings.autoPilotGrace.Value && buffs != null && buffs.Exists(x => x.Name == "grace_period"))
                {
                    try
                    {
                        var screenWidth = GameController.Game.IngameState.Camera.Width;
                        var screenHeight = GameController.Game.IngameState.Camera.Height;
                        var screenCenterX = screenWidth / 2;
                        var screenCenterY = screenHeight / 2;

                        var mousePos = new Vector2(GameController.IngameState.MousePosX, GameController.IngameState.MousePosY);

                        var distanceFromCenter = Math.Sqrt(
                            Math.Pow(mousePos.X - screenCenterX, 2) +
                            Math.Pow(mousePos.Y - screenCenterY, 2)
                        );

                        if (distanceFromCenter <= 100.0)
                        {
                            if (Keyboard.IsKeyDown((int)Settings.autoPilotMoveKey.Value))
                            {
                                var timeSinceLastAction = (DateTime.Now - LastTimeAny).TotalSeconds;
                                if (timeSinceLastAction > 0.5)
                                {
                                    var timeSinceLastGraceLog = (DateTime.Now - lastGraceLogTime).TotalSeconds;
                                    if (timeSinceLastGraceLog > 2.0)
                                    {
                                        LogMessage($"GRACE PERIOD: Manual break - mouse at center ({distanceFromCenter:F1}px from center)");
                                        lastGraceLogTime = DateTime.Now;
                                    }

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

                    var hasGrace = buffs != null && buffs.Exists(x => x.Name == "grace_period");
                    if (!hasGrace && lastHadGrace)
                    {
                        LogMessage("GRACE: Grace period ended");
                    }

                    lastFollowTarget = followTarget;
                    lastHadGrace = hasGrace;
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
                
            // MODIFIED: Only block UI interactions when game is not in foreground, but allow AutoPilot to work
            // AutoPilot logic runs before this check, so it continues working in background
            if (!GameController.IsForeGroundCache)
            {
                LogMessage("FOREGROUND CHECK: Game not in foreground - blocking UI interactions but AutoPilot continues");
                // Don't return here - let AutoPilot work but skip UI-dependent operations
                return;
            }
                
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