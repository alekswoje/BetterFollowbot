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
using BetterFollowbot.Skills;
using BetterFollowbot.Skill;
using BetterFollowbot.Automation;
using BetterFollowbot.Core.TaskManagement;
using BetterFollowbot.Core.Movement;
using BetterFollowbot.Interfaces;
using BetterFollowbot.Core.LeaderDetection;
using BetterFollowbot.Core.Skills;

namespace BetterFollowbot;

public class BetterFollowbot : BaseSettingsPlugin<BetterFollowbotSettings>, IFollowbotCore
{
    private const int Delay = 45;
    private const int MouseAutoSnapRange = 250;
    internal static BetterFollowbot Instance;
    
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
    private Links links;
    private SmiteBuff smiteBuff;
    private VaalSkills vaalSkillsAutomation;
    private Mines mines;
    private Warcries warcries;
    private RespawnHandler respawnHandler;
    private GemLeveler gemLeveler;
    private PartyJoiner partyJoiner;
    private AutomationManager automationManager;
    
    // Post-respawn state tracking
    private bool _waitingForLeaderAfterRespawn = false;
    private DateTime _lastRespawnTime = DateTime.MinValue;

    private List<Buff> buffs;
    private List<Entity> enemys = new List<Entity>();
    private bool isAttacking;
    private bool isCasting;
    private bool isMoving;
    private DateTime lastAreaChangeTime = DateTime.MinValue;
    private DateTime lastGraceLogTime = DateTime.MinValue;
    private DateTime lastGraceCheckLogTime = DateTime.MinValue;
    private bool _leaderHasGrace = false;
    private const double GRACE_WAIT_AFTER_ZONE_CHANGE = 3.0;
    private DateTime lastAutoPilotUpdateLogTime = DateTime.MinValue;
    private DateTime lastSkillRangeCheckLogTime = DateTime.MinValue;
    private Entity lastFollowTarget;
    private bool lastHadGrace;
    private Dictionary<string, DateTime> skillLastUsedTimes = new Dictionary<string, DateTime>();
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

        autoPilot = new AutoPilot(leaderDetector, taskManager, pathfinding, null, pathPlanner, portalManager);
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
        links = new Links(this, Settings);
        smiteBuff = new SmiteBuff(this, Settings);
        vaalSkillsAutomation = new VaalSkills(this, Settings);
        mines = new Mines(this, Settings);
        warcries = new Warcries(this, Settings);

        respawnHandler = new RespawnHandler(this, Settings);
        gemLeveler = new GemLeveler(this, Settings);
        partyJoiner = new PartyJoiner(this, Settings);

        automationManager = new AutomationManager();
        automationManager.RegisterSkill(summonRagingSpirits);
        automationManager.RegisterSkill(summonSkeletons);
        automationManager.RegisterSkill(rejuvenationTotem);
        automationManager.RegisterSkill(auraBlessing);
        automationManager.RegisterSkill(links);
        automationManager.RegisterSkill(smiteBuff);
        automationManager.RegisterSkill(vaalSkillsAutomation);
        automationManager.RegisterSkill(mines);
        automationManager.RegisterSkill(warcries);

        automationManager.RegisterAutomation(respawnHandler);
        automationManager.RegisterAutomation(gemLeveler);
        automationManager.RegisterAutomation(partyJoiner);

        return true;
    }
        
    #region IFollowbotCore Implementation
    
    // Settings property is already inherited from BaseSettingsPlugin<BetterFollowbotSettings>
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
    public int GetMonsterWithin(float maxDistance, ExileCore.Shared.Enums.MonsterRarity rarity = ExileCore.Shared.Enums.MonsterRarity.White)
    {
        return GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
            .Count(monster => IsValidMonsterForCount(monster, maxDistance, rarity));
    }
    
    /// <summary>
    /// Gets the count of nearby allied players (party members and other friendly players)
    /// </summary>
    public int GetNearbyAlliedPlayersCount(float maxDistance = 2000f)
    {
        try
        {
            if (localPlayer == null) return 0;
            
            var alliedPlayers = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                .Where(player => 
                    player != null && 
                    player.IsValid && 
                    !player.IsHostile && 
                    player.Address != localPlayer.Address && // Exclude self
                    player.DistancePlayer <= maxDistance)
                .ToList();
            
            return alliedPlayers.Count;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Validates if a monster is valid for counting (used by GetMonsterWithin) with ReAgent-style validation
    /// </summary>
    private bool IsValidMonsterForCount(Entity monster, float maxDistance, ExileCore.Shared.Enums.MonsterRarity rarity)
    {
        try
        {
            // ReAgent-style validation checks
            if (monster.DistancePlayer > maxDistance)
                return false;

            if (!monster.HasComponent<Monster>() ||
                !monster.HasComponent<Positioned>() ||
                !monster.HasComponent<Render>() ||
                !monster.HasComponent<Life>() ||
                !monster.HasComponent<ObjectMagicProperties>())
                return false;

            if (!monster.IsAlive || !monster.IsHostile)
                return false;

            // Check for hidden monster buff (like ReAgent does)
            if (monster.TryGetComponent<Buffs>(out var buffs) && buffs.HasBuff("hidden_monster"))
                return false;

            // Additional checks for targetability
            var targetable = monster.GetComponent<Targetable>();
            if (targetable == null || !targetable.isTargetable)
                return false;

            // Check if not invincible (cannot be damaged)
            var stats = monster.GetComponent<Stats>();
            if (stats?.StatDictionary?.ContainsKey(GameStat.CannotBeDamaged) == true &&
                stats.StatDictionary[GameStat.CannotBeDamaged] > 0)
                return false;

            // Check rarity
            var rarityComponent = monster.GetComponent<ObjectMagicProperties>();
            if (rarityComponent == null)
                return false;

            return rarityComponent.Rarity >= rarity;
        }
        catch
        {
            return false;
        }
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

    // Method to check if leader is in the same zone as the player
    public bool IsLeaderInSameZone()
    {
        try
        {
            var leaderPartyElement = leaderDetector?.LeaderPartyElement;
            if (leaderPartyElement == null) return false;

            var currentZone = GameController?.Area?.CurrentArea?.DisplayName;
            if (string.IsNullOrEmpty(currentZone)) return false;

            return leaderPartyElement.ZoneName?.Equals(currentZone, StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    // Method to set post-respawn waiting state (called by RespawnHandler)
    public void SetWaitingForLeaderAfterRespawn(bool waiting)
    {
        _waitingForLeaderAfterRespawn = waiting;
        if (waiting)
        {
            _lastRespawnTime = DateTime.Now;
            LogMessage($"POST-RESPAWN: Waiting for leader to return to zone before creating transition tasks");
        }
        else
        {
            LogMessage($"POST-RESPAWN: Leader is back in zone, resuming normal task creation");
        }
    }

    // Method to check if we should block transition tasks due to post-respawn waiting
    public bool ShouldBlockTransitionTasks()
    {
        if (!_waitingForLeaderAfterRespawn || !Settings.waitForLeaderAfterRespawn) return false;
        
        // Check if leader is back in the same zone
        if (IsLeaderInSameZone())
        {
            SetWaitingForLeaderAfterRespawn(false);
            return false;
        }

        // Log occasionally to show we're still waiting
        var timeSinceRespawn = DateTime.Now - _lastRespawnTime;
        if (timeSinceRespawn.TotalSeconds > 0 && (int)timeSinceRespawn.TotalSeconds % 10 == 0)
        {
            LogMessage($"POST-RESPAWN: Still waiting for leader to return to zone ({timeSinceRespawn.TotalSeconds:F0}s since respawn)");
        }

        return true;
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
                    // Position mouse towards leader with random offset
                    var random = new Random();
                    var leaderScreenPos = GameController.IngameState.Camera.WorldToScreen(leaderPos);
                    
                    // Add random offset (±10 pixels)
                    var randomOffsetX = (float)(random.NextDouble() * 20 - 10);
                    var randomOffsetY = (float)(random.NextDouble() * 20 - 10);
                    var randomizedLeaderPos = new Vector2(
                        leaderScreenPos.X + randomOffsetX,
                        leaderScreenPos.Y + randomOffsetY
                    );
                    
                    Mouse.SetCursorPos(randomizedLeaderPos);

                    // Random delay to ensure mouse movement is registered
                    System.Threading.Thread.Sleep(random.Next(60, 120));

                    // Execute dash
                    Keyboard.KeyPressRandom(Settings.autoPilotDashKey);
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

    public bool ShouldWaitForLeaderGrace
    {
        get
        {
            if (!Settings.autoPilotGrace.Value)
                return false;
            
            var timeSinceZoneChange = (DateTime.Now - lastAreaChangeTime).TotalSeconds;
            if (timeSinceZoneChange < GRACE_WAIT_AFTER_ZONE_CHANGE)
                return true;
            
            return _leaderHasGrace;
        }
    }

    public bool Gcd()
    {
        return (DateTime.Now - LastTimeAny).TotalMilliseconds > Delay;
    }

    /// <summary>
    /// Checks if a skill can be used based on its individual cooldown
    /// </summary>
    public bool CanUseSkill(string skillName)
    {
        if (!skillLastUsedTimes.ContainsKey(skillName))
            return true;

        var timeSinceLastUse = DateTime.Now - skillLastUsedTimes[skillName];
        return timeSinceLastUse.TotalSeconds >= Settings.skillCooldown.Value;
    }

    /// <summary>
    /// Records that a skill was used for cooldown tracking
    /// </summary>
    public void RecordSkillUse(string skillName)
    {
        skillLastUsedTimes[skillName] = DateTime.Now;
    }

    /// <summary>
    /// Checks if the bot is within the configured follow range of the leader
    /// Skills should only be used when within this range to prevent lag behind
    /// </summary>
    public bool IsWithinFollowRange()
    {
        // Throttle logging to prevent spam (log every 2 seconds max)
        var shouldLog = (DateTime.Now - lastSkillRangeCheckLogTime).TotalSeconds >= 2.0;
        
        if (!Settings.autoPilotEnabled.Value)
        {
            if (shouldLog)
            {
                LogMessage("SKILL RANGE CHECK: AutoPilot disabled, allowing skills");
                lastSkillRangeCheckLogTime = DateTime.Now;
            }
            return true; // If autopilot is off, allow skills
        }

        var followTarget = autoPilot?.FollowTarget;
        if (followTarget == null || localPlayer == null)
        {
            if (shouldLog)
            {
                LogMessage($"SKILL RANGE CHECK: No follow target ({followTarget == null}) or local player ({localPlayer == null}), allowing skills");
                lastSkillRangeCheckLogTime = DateTime.Now;
            }
            return true; // If no leader, allow skills
        }

        // Don't use skills if there are multiple movement tasks queued (bot is actively catching up)
        var taskCount = autoPilot?.Tasks?.Count ?? 0;
        if (taskCount > 2)
        {
            if (shouldLog)
            {
                LogMessage($"SKILL RANGE CHECK: BLOCKED - Too many movement tasks queued ({taskCount} tasks), bot is catching up");
                lastSkillRangeCheckLogTime = DateTime.Now;
            }
            return false; // Bot is busy catching up, don't interrupt with skills
        }

        var distanceToLeader = Vector3.Distance(localPlayer.Pos, followTarget.Pos);
        var maxFollowDistance = Settings.autoPilotPathfindingNodeDistance.Value;
        
        // Use a tighter range (75% of pathfinding distance) for skills to ensure bot stays close
        var skillUseDistance = maxFollowDistance * 0.75f;
        
        var withinRange = distanceToLeader <= skillUseDistance;
        
        if (shouldLog)
        {
            if (withinRange)
            {
                LogMessage($"SKILL RANGE CHECK: ALLOWED - Distance: {distanceToLeader:F1} <= {skillUseDistance:F1} (75% of {maxFollowDistance}), Tasks: {taskCount}");
            }
            else
            {
                LogMessage($"SKILL RANGE CHECK: BLOCKED - Distance: {distanceToLeader:F1} > {skillUseDistance:F1} (75% of {maxFollowDistance}), Tasks: {taskCount}");
            }
            lastSkillRangeCheckLogTime = DateTime.Now;
        }
        
        return withinRange;
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
            _ => default(Keys)  // Return default instead of Escape to prevent spam
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
                var leaderPartyElement = leaderDetector?.GetLeaderPartyElement();
                autoPilot.UpdateFollowTargetPosition(leaderPartyElement?.ZoneName);
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
                        var distanceToLeader = localPlayer != null ? Vector3.Distance(localPlayer.Pos, followTarget.Pos) : -1;
                        var minDistanceForTask = Settings.autoPilotPathfindingNodeDistance.Value;
                        LogMessage($"AUTOPILOT: Has follow target but no tasks - Distance: {distanceToLeader:F1}, MinDistance: {minDistanceForTask}, AutoPilot may not be moving the bot");
                        LogMessage($"AUTOPILOT: Bot position: {(localPlayer != null ? $"X:{localPlayer.Pos.X:F1} Y:{localPlayer.Pos.Y:F1}" : "null")}");
                        LogMessage($"AUTOPILOT: Leader position: X:{followTarget.Pos.X:F1} Y:{followTarget.Pos.Y:F1}");
                        }
                        lastAutoPilotUpdateLogTime = DateTime.Now;
                    }
                }

                if (Settings.autoPilotEnabled.Value && Settings.autoPilotGrace.Value)
                {
                    var leaderEntity = autoPilot?.FollowTarget;
                    var previousLeaderGraceState = _leaderHasGrace;
                    
                    if (leaderEntity != null && leaderEntity.IsValid)
                    {
                        try
                        {
                            if (leaderEntity.TryGetComponent<Buffs>(out var leaderBuffs))
                            {
                                _leaderHasGrace = leaderBuffs.HasBuff("grace_period");
                                
                                if (_leaderHasGrace != previousLeaderGraceState)
                                {
                                    if (_leaderHasGrace)
                                    {
                                        LogMessage($"LEADER GRACE: Leader '{Settings.autoPilotLeader.Value}' has grace period - bot will wait before moving/casting");
                                    }
                                    else
                                    {
                                        LogMessage($"LEADER GRACE: Leader '{Settings.autoPilotLeader.Value}' broke grace period - bot can now move and cast skills");
                                    }
                                    lastGraceLogTime = DateTime.Now;
                                }
                                
                                if (_leaderHasGrace)
                                {
                                    var timeSinceLastLog = (DateTime.Now - lastGraceLogTime).TotalSeconds;
                                    if (timeSinceLastLog > 5.0)
                                    {
                                        LogMessage($"LEADER GRACE: Still waiting for leader to break grace period...");
                                        lastGraceLogTime = DateTime.Now;
                                    }
                                }
                            }
                            else
                            {
                                _leaderHasGrace = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            _leaderHasGrace = false;
                            if (Settings.debugMode.Value)
                            {
                                LogError($"Error checking leader grace buff: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        _leaderHasGrace = false;
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

            if (ShouldWaitForLeaderGrace)
            {
                if (Settings.debugMode)
                {
                    LogMessage("SKILL EXECUTION: Blocked - waiting for leader to break grace period");
                }
            }
            else if (GameController.Area.CurrentArea.IsHideout && Settings.disableSkillsInHideout)
            {
                automationManager?.ExecuteAutomations();
                
                if (Settings.debugMode)
                {
                    LogMessage("SKILL EXECUTION: Blocked in hideout - only automations allowed for safety");
                }
            }
            else
            {
                automationManager?.ExecuteAll();
            }

            #endregion
            if (GameController.Area.CurrentArea.IsHideout || GameController.Area.CurrentArea.IsTown ||
                /*GameController.IngameState.IngameUi.StashElement.IsVisible ||*/ // 3.15 Null
                GameController.IngameState.IngameUi.NpcDialog.IsVisible ||
                GameController.IngameState.IngameUi.SellWindow.IsVisible ||
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
            
            // Host Mode on-screen display
            if (Settings.hostModeEnabled.Value)
            {
                var nearbyAlliedPlayers = GetNearbyAlliedPlayersCount(2000f);
                Graphics.DrawText("=== HOST MODE ===", new System.Numerics.Vector2(100, 160), Color.Yellow);
                Graphics.DrawText($"Nearby Party Members: {nearbyAlliedPlayers}", new System.Numerics.Vector2(100, 180), Color.LightGreen);
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