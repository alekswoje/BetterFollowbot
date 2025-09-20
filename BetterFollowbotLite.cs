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
using BetterFollowbotLite.Automation;

namespace BetterFollowbotLite;

public class BetterFollowbotLite : BaseSettingsPlugin<BetterFollowbotLiteSettings>
{
    private const int Delay = 45;

    private const int MouseAutoSnapRange = 250;
    internal static BetterFollowbotLite Instance;
    internal AutoPilot autoPilot = new AutoPilot();
    private readonly Summons summons = new Summons();
    private SummonRagingSpirits summonRagingSpirits;
    private RespawnHandler respawnHandler;
    private GemLeveler gemLeveler;
    private PartyJoiner partyJoiner;

    private List<Buff> buffs;
    private List<Entity> enemys = new List<Entity>();
    private bool isAttacking;
    private bool isCasting;
    private bool isMoving;
    internal DateTime lastTimeAny;
    private DateTime lastAreaChangeTime = DateTime.MinValue;
    private DateTime lastGraceLogTime = DateTime.MinValue;
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

        // Initialize timestamps
        // lastAutoJoinPartyAttempt is now managed within PartyJoiner class

        GameController.LeftPanel.WantUse(() => Settings.Enable);
        skillCoroutine = new Coroutine(WaitForSkillsAfterAreaChange(), this);
        Core.ParallelRunner.Run(skillCoroutine);
        Input.RegisterKey(Settings.autoPilotToggleKey.Value);
        Settings.autoPilotToggleKey.OnValueChanged += () => { Input.RegisterKey(Settings.autoPilotToggleKey.Value); };
        autoPilot.StartCoroutine();

        // Initialize skill classes
        summonRagingSpirits = new SummonRagingSpirits(this, Settings, autoPilot, summons);

        // Initialize automation classes
        respawnHandler = new RespawnHandler(this, Settings);
        gemLeveler = new GemLeveler(this, Settings);
        partyJoiner = new PartyJoiner(this, Settings);

        return true;
    }
        

    private int GetMinnionsWithin(float maxDistance)
    {
        return localPlayer.GetComponent<Actor>().DeployedObjects.Where(x => x?.Entity != null && x.Entity.IsAlive).Select(minnion => Vector2.Distance(new Vector2(minnion.Entity.Pos.X, minnion.Entity.Pos.Y), new Vector2(playerPosition.X, playerPosition.Y))).Count(distance => distance <= maxDistance);
    }

    private int GetMonsterWithin(float maxDistance, MonsterRarity rarity = MonsterRarity.White)
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

        

    private bool MonsterCheck(int range, int minAny, int minRare, int minUnique)
    {
        int any = 0, rare = 0, unique = 0;
        foreach (var monster in enemys)
            switch (monster.Rarity)
            {
                case MonsterRarity.White:
                {
                    if (Vector2.Distance(new Vector2(monster.PosNum.X, monster.PosNum.Y),
                            new Vector2(playerPosition.X, playerPosition.Y)) <= range)
                        any++;
                    break;
                }
                case MonsterRarity.Magic:
                {
                    if (Vector2.Distance(new Vector2(monster.PosNum.X, monster.PosNum.Y),
                            new Vector2(playerPosition.X, playerPosition.Y)) <= range)
                        any++;
                    break;
                }
                case MonsterRarity.Rare:
                {
                    if (Vector2.Distance(new Vector2(monster.PosNum.X, monster.PosNum.Y),
                            new Vector2(playerPosition.X, playerPosition.Y)) <= range)
                    {
                        any++;
                        rare++;
                    }
                    break;
                }
                case MonsterRarity.Unique:
                {
                    if (Vector2.Distance(new Vector2(monster.PosNum.X, monster.PosNum.Y),
                            new Vector2(playerPosition.X, playerPosition.Y)) <= range)
                    {
                        any++;
                        rare++;
                        unique++;
                    }
                    break;
                }
            }

        if (minUnique > 0 && unique >= minUnique) return true;

        if (minRare > 0 && rare >= minRare) return true;

        if (minAny > 0 && any >= minAny) return true;

        return minAny == 0 && minRare == 0 && minUnique == 0;
    }

    internal Vector2 GetMousePosition()
    {
        return new Vector2(GameController.IngameState.MousePosX, GameController.IngameState.MousePosY);
    }


    public bool Gcd()
    {
        return (DateTime.Now - lastTimeAny).TotalMilliseconds > Delay;
    }

    private void Quit()
    {
        try
        {
            CommandHandler.KillTcpConnectionForProcess(GameController.Window.Process.Id);
        }
        catch (Exception e)
        {
            // Error handling without logging
        }
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

        // Log area change details
        var newAreaName = area?.DisplayName ?? "Unknown";
        var isHideout = area?.IsHideout ?? false;
        var realLevel = area?.RealLevel ?? 0;

        LogMessage($"AREA CHANGE: Transitioned to '{newAreaName}' - Hideout: {isHideout}, Level: {realLevel}");

        // Reset player position to prevent large distance calculations in grace period logic
        if (GameController?.Game?.IngameState?.Data?.LocalPlayer != null)
        {
            playerPosition = GameController.Game.IngameState.Data.LocalPlayer.Pos;
            LogMessage($"AREA CHANGE: Reset player position to ({playerPosition.X:F1}, {playerPosition.Y:F1})");
        }

        SkillInfo.ResetSkills();
        skills = null;

        var coroutine = new Coroutine(WaitForSkillsAfterAreaChange(), this);
        Core.ParallelRunner.Run(coroutine);

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
    }
        
    public override void DrawSettings()
    {
        //base.DrawSettings();

        // Draw Custom GUI
        if (Settings.Enable)
            ImGuiDrawSettings.DrawImGuiSettings();
    }

    private static bool HasStat(Entity monster, GameStat stat)
    {
        // Using this with GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster].Where
        // seems to cause Nullref errors on TC Fork. Where using the Code directly in a check seems fine, must have to do with Entity Parameter.
        // Maybe someone knows why, i dont :)
        try
        {
            var value = monster?.GetComponent<Stats>()?.StatDictionary?[stat];
            return value > 0;
        }
        catch (Exception)
        {
            return false;
        }
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
                        LogMessage($"GRACE PERIOD: Active grace period detected, time since area change: {timeSinceAreaChange:F1}s");
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
                            LogMessage("GRACE PERIOD: Zone stabilization period passed, checking if safe to remove grace");
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
                            var timeSinceLastAction = (DateTime.Now - lastTimeAny).TotalSeconds;

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
                                    LogMessage($"GRACE PERIOD: Moving mouse to ({targetX}, {targetY}) and pressing move key to break grace");
                                }

                                Keyboard.KeyPress(Settings.autoPilotMoveKey.Value);
                                lastTimeAny = DateTime.Now;
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

                    LogMessage($"GRACE CHECK: AutoPilot: {autopilotEnabled}, Grace Enabled: {graceEnabled}, Has Buffs: {hasBuffs}, Has Grace Buff: {hasGraceBuff}");
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
                                var timeSinceLastAction = (DateTime.Now - lastTimeAny).TotalSeconds;
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
                                    lastTimeAny = DateTime.Now;
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
                    LogMessage("AUTOPILOT: No follow target set - attempting to find leader");

                    // Try to find leader manually
                    var playerEntities = GameController.Entities.Where(x => x.Type == EntityType.Player).ToList();
                    var manualLeaderEntity = playerEntities.FirstOrDefault(x =>
                        x.GetComponent<Player>()?.PlayerName?.Equals(Settings.autoPilotLeader.Value, StringComparison.OrdinalIgnoreCase) == true);

                    if (manualLeaderEntity != null)
                    {
                        LogMessage($"AUTOPILOT: Found leader manually - setting as follow target: '{manualLeaderEntity.GetComponent<Player>()?.PlayerName}'");
                        autoPilot.SetFollowTarget(manualLeaderEntity);
                    }
                    else
                    {
                        LogMessage("AUTOPILOT: Still no leader found - bot will not move");
                    }
                }

                // CRITICAL: Update follow target position BEFORE AutoPilot logic
                // This prevents stale position data from causing incorrect movement
                autoPilot.UpdateFollowTargetPosition();

                // CRITICAL: Update AutoPilot logic BEFORE grace period check
                // AutoPilot should be able to create tasks even when grace period is active
                autoPilot.UpdateAutoPilotLogic();
                autoPilot.Render();

                // Debug AutoPilot tasks after update
                if (autoPilot != null)
                {
                    var followTarget = autoPilot.FollowTarget;
                    LogMessage($"AUTOPILOT: After update - Task count: {autoPilot.Tasks.Count}, FollowTarget: {(followTarget != null ? followTarget.GetComponent<Player>()?.PlayerName ?? "Unknown" : "null")}");

                    if (followTarget != null && autoPilot.Tasks.Count == 0)
                    {
                        LogMessage("AUTOPILOT: Has follow target but no tasks - AutoPilot may not be moving the bot");
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

            #region Auto Respawn

            respawnHandler?.Execute();

            #endregion

            #region Summon Skeletons

            if (Settings.summonSkeletonsEnabled && Gcd())
            {
                try
                {
                    // Check if we have a party leader to follow
                    var partyMembers = PartyElements.GetPlayerInfoElementList();
                    LogMessage($"PARTY: Checking for leader '{Settings.autoPilotLeader.Value}', Party members: {partyMembers.Count}");

                    var leaderPartyElement = partyMembers
                        .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                            Settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                    if (leaderPartyElement != null)
                    {
                        LogMessage($"PARTY: Found leader in party - Name: '{leaderPartyElement.PlayerName}'");
                    }
                    else
                    {
                        LogMessage("PARTY: Leader NOT found in party list");
                        // Debug all party members
                        foreach (var member in partyMembers)
                        {
                            LogMessage($"PARTY: Member - Name: '{member?.PlayerName}'");
                        }
                    }

                    if (leaderPartyElement != null)
                    {
                        // Find the actual leader entity
                        var playerEntities = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                            .Where(x => x != null && x.IsValid && !x.IsHostile);

                        var leaderEntity = playerEntities
                            .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                Settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                        if (leaderEntity != null)
                        {
                            // Check distance to leader
                            var distanceToLeader = Vector3.Distance(playerPosition, leaderEntity.Pos);

                            // Only summon if within range
                            if (distanceToLeader <= Settings.summonSkeletonsRange.Value)
                            {
                                // Count current skeletons
                                var skeletonCount = Summons.GetSkeletonCount();

                                // Summon if we have less than the minimum required
                                if (skeletonCount < Settings.summonSkeletonsMinCount.Value)
                                {
                                    // Find the summon skeletons skill
                                    var summonSkeletonsSkill = skills.FirstOrDefault(s =>
                                        s.Name.Contains("Summon Skeletons") ||
                                        s.Name.Contains("summon") && s.Name.Contains("skeleton"));

                                    if (summonSkeletonsSkill != null && summonSkeletonsSkill.IsOnSkillBar && summonSkeletonsSkill.CanBeUsed)
                                    {
                                        BetterFollowbotLite.Instance.LogMessage($"SUMMON SKELETONS: Current: {skeletonCount}, Required: {Settings.summonSkeletonsMinCount.Value}, Distance to leader: {distanceToLeader:F1}");

                                        // Use the summon skeletons skill
                                        Keyboard.KeyPress(GetSkillInputKey(summonSkeletonsSkill.SkillSlotIndex));
                                        lastTimeAny = DateTime.Now; // Update global cooldown

                                        BetterFollowbotLite.Instance.LogMessage("SUMMON SKELETONS: Summoned skeletons successfully");
                                    }
                                    else if (summonSkeletonsSkill == null)
                                    {
                                        BetterFollowbotLite.Instance.LogMessage("SUMMON SKELETONS: Summon Skeletons skill not found in skill bar");
                                    }
                                    else if (!summonSkeletonsSkill.CanBeUsed)
                                    {
                                        BetterFollowbotLite.Instance.LogMessage("SUMMON SKELETONS: Summon Skeletons skill is on cooldown or unavailable");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    BetterFollowbotLite.Instance.LogMessage($"SUMMON SKELETONS: Exception occurred - {e.Message}");
                }

                // SRS (Summon Raging Spirits) logic
                summonRagingSpirits?.Execute();
            }

            #endregion

            #region Auto Level Gems

            gemLeveler?.Execute();

            #endregion

            #region Auto Join Party & Accept Trade

            partyJoiner?.Execute();

            #endregion

            #region Auto Map Tabber

            try
            {
                if (Settings.autoMapTabber && !Keyboard.IsKeyDown((int)Settings.inputKeyPickIt.Value))
                    if (SkillInfo.ManageCooldown(SkillInfo.autoMapTabber))
                    {
                        bool shouldBeClosed = GameController.IngameState.IngameUi.Atlas.IsVisible ||
                                              GameController.IngameState.IngameUi.AtlasTreePanel.IsVisible ||
                                              GameController.IngameState.IngameUi.StashElement.IsVisible ||
                                              GameController.IngameState.IngameUi.TradeWindow.IsVisible || 
                                              GameController.IngameState.IngameUi.ChallengesPanel.IsVisible ||
                                              GameController.IngameState.IngameUi.CraftBench.IsVisible ||
                                              GameController.IngameState.IngameUi.DelveWindow.IsVisible ||
                                              GameController.IngameState.IngameUi.ExpeditionWindow.IsVisible || 
                                              GameController.IngameState.IngameUi.BanditDialog.IsVisible ||
                                              GameController.IngameState.IngameUi.MetamorphWindow.IsVisible ||
                                              GameController.IngameState.IngameUi.SyndicatePanel.IsVisible || 
                                              GameController.IngameState.IngameUi.SyndicateTree.IsVisible ||
                                              GameController.IngameState.IngameUi.QuestRewardWindow.IsVisible ||
                                              GameController.IngameState.IngameUi.SynthesisWindow.IsVisible ||
                                              //GameController.IngameState.IngameUi.UltimatumPanel.IsVisible || 
                                              GameController.IngameState.IngameUi.MapDeviceWindow.IsVisible ||
                                              GameController.IngameState.IngameUi.SellWindow.IsVisible ||
                                              GameController.IngameState.IngameUi.SettingsPanel.IsVisible ||
                                              GameController.IngameState.IngameUi.InventoryPanel.IsVisible || 
                                              //GameController.IngameState.IngameUi.NpcDialog.IsVisible ||
                                              GameController.IngameState.IngameUi.TreePanel.IsVisible;
                           
                            
                        if (!GameController.IngameState.IngameUi.Map.SmallMiniMap.IsVisibleLocal && shouldBeClosed)
                        {
                            Keyboard.KeyPress(Keys.Tab);
                            SkillInfo.autoMapTabber.Cooldown = 250;
                        }
                        else if (GameController.IngameState.IngameUi.Map.SmallMiniMap.IsVisibleLocal && !shouldBeClosed)
                        {
                            Keyboard.KeyPress(Keys.Tab);
                            SkillInfo.autoMapTabber.Cooldown = 250;
                        }
                    } 
            }
            catch (Exception e)
            {
                // Error handling without logging
            }

            #endregion
            if (GameController.Area.CurrentArea.IsHideout || GameController.Area.CurrentArea.IsTown ||
                /*GameController.IngameState.IngameUi.StashElement.IsVisible ||*/ // 3.15 Null
                GameController.IngameState.IngameUi.NpcDialog.IsVisible ||
                GameController.IngameState.IngameUi.SellWindow.IsVisible || MenuWindow.IsOpened ||
                !GameController.InGame || GameController.IsLoading) return;
                
            enemys = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster].Where(x =>
                x != null && x.IsAlive && x.IsHostile && x.GetComponent<Life>()?.CurHP > 0 && 
                x.GetComponent<Targetable>()?.isTargetable == true && !HasStat(x, GameStat.CannotBeDamaged) &&
                GameController.Window.GetWindowRectangleTimeCache.Contains(
                    GameController.Game.IngameState.Camera.WorldToScreen(x.Pos))).ToList();
            if (Settings.debugMode)
            {
                Graphics.DrawText("Enemys: " + enemys.Count, new System.Numerics.Vector2(100, 120), Color.White);
            }
                
            // Corpses collection removed since no longer needed



                

            // Do not Cast anything while we are untouchable or Chat is Open
            if (buffs.Exists(x => x.Name == "grace_period") ||
                /*GameController.IngameState.IngameUi.ChatBoxRoot.Parent.Parent.Parent.GetChildAtIndex(3).IsVisible || */ // 3.15 Bugged 
                !GameController.IsForeGroundCache)
                return;
                
            foreach (var skill in skills.Where(skill => skill.IsOnSkillBar && skill.SkillSlotIndex >= 1 && skill.SkillSlotIndex != 2 && skill.CanBeUsed))
            {
                    

                #region Aura Blessing

                if (Settings.auraBlessingEnabled)
                {
                    try
                    {
                        // Holy Relic summoning logic
                        if (skill.Id == SkillInfo.holyRelict.Id)
                        {
                            // Check cooldown to prevent double-spawning
                            if (SkillInfo.ManageCooldown(SkillInfo.holyRelict, skill))
                            {
                                var lowestMinionHp = Summons.GetLowestMinionHpp();
                                // Convert HP percentage from 0-1 range to 0-100 range for comparison
                                var lowestMinionHpPercent = lowestMinionHp * 100f;
                                // Check for Holy Relic minion presence
                                // Prioritize ReAgent buff names, then check for other indicators
                                // Note: Avoid "guardian_life_regen" as it's just the life regen effect, not minion presence
                                var hasGuardianBlessingMinion = buffs.Exists(x =>
                                    x.Name == "has_guardians_blessing_minion" ||
                                    (x.Name.Contains("holy") && x.Name.Contains("relic") && !x.Name.Contains("life")) ||
                                    x.Name.Contains("guardian_blessing_minion"));

                                // Check conditions
                                var healthLow = lowestMinionHpPercent < Settings.holyRelicHealthThreshold;
                                var missingBuff = !hasGuardianBlessingMinion;

                                // If Holy Relic health is below threshold OR we don't have any minion buff, summon new Holy Relic
                                if (healthLow || missingBuff)
                                {
                                    Keyboard.KeyPress(GetSkillInputKey(skill.SkillSlotIndex));
                                    SkillInfo.holyRelict.Cooldown = 200; // 2 second cooldown to prevent double-spawning
                                }
                            }
                        }

                        // Zealotry casting logic
                        else if (skill.Id == SkillInfo.auraZealotry.Id)
                        {
                            // Check for Zealotry aura buff
                            // Prioritize ReAgent buff names, then check for aura effects
                            var hasGuardianBlessingAura = buffs.Exists(x =>
                                x.Name == "has_guardians_blessing_aura" ||
                                x.Name == "zealotry" ||
                                x.Name == "player_aura_spell_damage" ||
                                (x.Name.Contains("blessing") && x.Name.Contains("aura")));

                            // Check for Holy Relic minion presence (same logic as Holy Relic section)
                            var hasGuardianBlessingMinion = buffs.Exists(x =>
                                x.Name == "has_guardians_blessing_minion" ||
                                (x.Name.Contains("holy") && x.Name.Contains("relic") && !x.Name.Contains("life")) ||
                                x.Name.Contains("guardian_blessing_minion"));

                            // Check conditions
                            var missingAura = !hasGuardianBlessingAura;
                            var hasMinion = hasGuardianBlessingMinion;

                            // If we have the minion but don't have the aura buff, cast Zealotry
                            if (missingAura && hasMinion)
                            {
                                Keyboard.KeyPress(GetSkillInputKey(skill.SkillSlotIndex));
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Error handling without logging
                    }
                }

                #endregion


                #region Link Skills

                if (Settings.flameLinkEnabled)
                    try
                    {
                        if (skill.Id == SkillInfo.flameLink.Id)
                        {
                            var linkSkill = SkillInfo.flameLink;
                            var targetBuffName = "flame_link_target";

                            if (SkillInfo.ManageCooldown(linkSkill, skill))
                            {
                                // Get party leader
                                var partyElements = PartyElements.GetPlayerInfoElementList();

                                var leaderPartyElement = partyElements
                                    .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                                        Settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                if (leaderPartyElement != null)
                                {
                                    // Find the actual player entity by name
                                    var playerEntities = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                        .Where(x => x != null && x.IsValid && !x.IsHostile);

                                    var leaderEntity = playerEntities
                                        .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                            Settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                    if (leaderEntity != null)
                                    {
                                        // Set the player entity
                                        leaderPartyElement.Data.PlayerEntity = leaderEntity;

                                        var leader = leaderPartyElement.Data.PlayerEntity;
                                        var leaderBuffs = leader.GetComponent<Buffs>().BuffsList;

                                        // Check if leader has the target buff
                                        var hasLinkTarget = leaderBuffs.Exists(x => x.Name == targetBuffName);

                                        // Check if we have the source buff and its timer
                                        var linkSourceBuff = buffs.FirstOrDefault(x => x.Name == linkSkill.BuffName);
                                        var linkSourceTimeLeft = linkSourceBuff?.Timer ?? 0;

                                        // Check distance from leader to mouse cursor in screen space
                                        var mouseScreenPos = GetMousePosition();
                                        var leaderScreenPos = Helper.WorldToValidScreenPosition(leader.Pos);
                                        var distanceToCursor = Vector2.Distance(mouseScreenPos, leaderScreenPos);

                                        // Logic: Aggressive flame link maintenance - refresh much earlier and with larger distance tolerance
                                        // Emergency linking (no source buff): ignore distance
                                        // Normal linking: use distance check
                                        var shouldActivate = (!hasLinkTarget || linkSourceTimeLeft < 8 || linkSourceBuff == null) &&
                                                             (linkSourceBuff == null || distanceToCursor < 100);

                                        if (shouldActivate)
                                        {
                                            // Move mouse to leader position
                                            var leaderScreenPosForMouse = GameController.IngameState.Camera.WorldToScreen(leader.Pos);
                                            Mouse.SetCursorPos(leaderScreenPosForMouse);

                                            // Activate the skill
                                            Keyboard.KeyPress(GetSkillInputKey(skill.SkillSlotIndex));
                                            linkSkill.Cooldown = 100;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Error handling without logging
                    }

                #endregion

                #region Smite Buff

                if (Settings.smiteEnabled)
                    try
                    {
                        if (skill.Id == SkillInfo.smite.Id)
                        {
                            BetterFollowbotLite.Instance.LogMessage("SMITE: Smite skill detected");

                            // Custom cooldown check for smite that bypasses GCD since it's a buff skill
                            if (SkillInfo.smite.Cooldown <= 0 &&
                                !(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                            {
                                // Check mana cost
                                if (!skill.Stats.TryGetValue(GameStat.ManaCost, out var manaCost))
                                    manaCost = 0;

                                if (BetterFollowbotLite.Instance.player.CurMana >= manaCost ||
                                    (BetterFollowbotLite.Instance.localPlayer.Stats.TryGetValue(GameStat.VirtualEnergyShieldProtectsMana, out var hasEldritchBattery) &&
                                     hasEldritchBattery > 0 && (BetterFollowbotLite.Instance.player.CurES + BetterFollowbotLite.Instance.player.CurMana) >= manaCost))
                                {
                                    BetterFollowbotLite.Instance.LogMessage("SMITE: Cooldown check passed");

                                    // Check if we don't have the smite buff or it's about to expire
                                var smiteBuff = buffs.FirstOrDefault(x => x.Name == "smite_buff");
                                var hasSmiteBuff = smiteBuff != null;
                                var buffTimeLeft = smiteBuff?.Timer ?? 0;
                                BetterFollowbotLite.Instance.LogMessage($"SMITE: Has smite buff: {hasSmiteBuff}, Time left: {buffTimeLeft:F1}s");

                                // Refresh if no buff or buff has less than 2 seconds left
                                if (!hasSmiteBuff || buffTimeLeft < 2.0f)
                                {
                                    BetterFollowbotLite.Instance.LogMessage("SMITE: No smite buff found, looking for targets");

                                    // Find monsters within 250 units of player (smite attack range)
                                    var targetMonster = enemys
                                        .Where(monster =>
                                        {
                                            // Check if monster is within 250 units of player
                                            var distanceToPlayer = Vector3.Distance(playerPosition, monster.Pos);
                                            // Check if monster is on screen (can be targeted)
                                            var screenPos = GameController.IngameState.Camera.WorldToScreen(monster.Pos);
                                            var isOnScreen = GameController.Window.GetWindowRectangleTimeCache.Contains(screenPos);
                                            BetterFollowbotLite.Instance.LogMessage($"SMITE: Monster at distance {distanceToPlayer:F1} from player, on screen: {isOnScreen}");
                                            return distanceToPlayer <= 250 && isOnScreen;
                                        })
                                        .OrderBy(monster => Vector3.Distance(playerPosition, monster.Pos)) // Closest first
                                        .FirstOrDefault();

                                    if (targetMonster != null)
                                    {
                                        BetterFollowbotLite.Instance.LogMessage("SMITE: Found suitable target, activating smite!");

                                        // Move mouse to monster position
                                        var monsterScreenPos = GameController.IngameState.Camera.WorldToScreen(targetMonster.Pos);
                                        Mouse.SetCursorPos(monsterScreenPos);

                                        // Small delay to ensure mouse movement is registered
                                        System.Threading.Thread.Sleep(50);

                                        // Double-check mouse position is still valid
                                        var currentMousePos = GetMousePosition();
                                        var distanceFromTarget = Vector2.Distance(currentMousePos, monsterScreenPos);
                                        if (distanceFromTarget < 50) // Within reasonable tolerance
                                        {
                                            // Activate the skill
                                            Keyboard.KeyPress(GetSkillInputKey(skill.SkillSlotIndex));
                                            SkillInfo.smite.Cooldown = 100;
                                            lastTimeAny = DateTime.Now; // Update global cooldown
                                            BetterFollowbotLite.Instance.LogMessage("SMITE: Smite activated successfully");
                                        }
                                        else
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"SMITE: Mouse positioning failed, distance: {distanceFromTarget:F1}");
                                        }
                                    }
                                    else
                                    {
                                        BetterFollowbotLite.Instance.LogMessage("SMITE: No suitable targets found within range, dashing to leader");

                                        // Dash to leader to get near monsters
                                        if (Settings.autoPilotDashEnabled && (DateTime.Now - autoPilot.lastDashTime).TotalMilliseconds >= 3000 && autoPilot.FollowTarget != null)
                                        {
                                            var leaderPos = autoPilot.FollowTarget.Pos;
                                            var distanceToLeader = Vector3.Distance(playerPosition, leaderPos);

                                            // CRITICAL: Don't dash if teleport is in progress (strongest protection)
                                            if (AutoPilot.IsTeleportInProgress)
                                            {
                                                BetterFollowbotLite.Instance.LogMessage("SMITE: TELEPORT IN PROGRESS - blocking all dash attempts");
                                            }
                                            else
                                            {
                                                // Fallback: Check for transition tasks
                                                var hasTransitionTask = autoPilot.Tasks.Any(t =>
                                                    t.Type == TaskNodeType.Transition ||
                                                    t.Type == TaskNodeType.TeleportConfirm ||
                                                    t.Type == TaskNodeType.TeleportButton);

                                                if (hasTransitionTask)
                                                {
                                                    BetterFollowbotLite.Instance.LogMessage($"SMITE: Transition/teleport task active ({autoPilot.Tasks.Count} tasks), skipping dash");
                                                }
                                                else if (distanceToLeader > Settings.autoPilotDashDistance) // Only dash if we're not already close to leader
                                                {
                                                    BetterFollowbotLite.Instance.LogMessage($"SMITE: Dashing to leader - Distance: {distanceToLeader:F1}");

                                                    // Position mouse towards leader
                                                    var leaderScreenPos = GameController.IngameState.Camera.WorldToScreen(leaderPos);
                                                    Mouse.SetCursorPos(leaderScreenPos);

                                                    // Small delay to ensure mouse movement is registered
                                                    System.Threading.Thread.Sleep(50);

                                                    // Execute dash
                                                    Keyboard.KeyPress(Settings.autoPilotDashKey);
                                                    autoPilot.lastDashTime = DateTime.Now;

                                                    BetterFollowbotLite.Instance.LogMessage("SMITE: Dash to leader executed");
                                                }
                                                else
                                                {
                                                    BetterFollowbotLite.Instance.LogMessage("SMITE: Already close to leader, skipping dash");
                                                }
                                            }
                                        }
                                        else
                                        {
                                            BetterFollowbotLite.Instance.LogMessage("SMITE: Dash not available or not enabled");
                                        }
                                    }
                                }
                                    else
                                    {
                                        BetterFollowbotLite.Instance.LogMessage("SMITE: Already have smite buff, skipping");
                                    }
                                }
                                else
                                {
                                    BetterFollowbotLite.Instance.LogMessage("SMITE: Not enough mana for smite");
                                }
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage("SMITE: Cooldown check failed");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Error handling without logging
                    }
                else
                {
                    BetterFollowbotLite.Instance.LogMessage("SMITE: Smite is not enabled");
                }

                #endregion

                #region Rejuvenation Totem

                if (Settings.rejuvenationTotemEnabled)
                {
                    try
                    {
                        if (skill.Id == SkillInfo.rejuvenationTotem.Id)
                        {
                            BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: 🔍 Skill detected - ID: {skill.Id}, SlotIndex: {skill.SkillSlotIndex}, RemainingUses: {skill.RemainingUses}, IsOnCooldown: {skill.IsOnCooldown}, SkillCooldown: {SkillInfo.rejuvenationTotem.Cooldown:F0}ms");

                            if (SkillInfo.ManageCooldown(SkillInfo.rejuvenationTotem, skill))
                            {
                                BetterFollowbotLite.Instance.LogMessage("REJUVENATION TOTEM: ✅ Cooldown check passed, processing totem logic");

                                // Check if we already have the totem buff
                                var hasTotemBuff = buffs.Exists(x => x.Name == "totem_aura_life_regen");
                                if (!hasTotemBuff)
                                {
                                    // Check for unique/rare monsters within range
                                    var monsterCount = GetMonsterWithin(Settings.rejuvenationTotemRange, MonsterRarity.Rare);
                                    var uniqueMonsterCount = GetMonsterWithin(Settings.rejuvenationTotemRange, MonsterRarity.Unique);
                                    var hasRareOrUniqueNearby = monsterCount > 0 || uniqueMonsterCount > 0;

                                    // Check if any party member total pool (Life + ES) is below threshold
                                    var partyMembersLowHp = false;
                                    var partyElements = PartyElements.GetPlayerInfoElementList();

                                    foreach (var partyMember in partyElements)
                                    {
                                        if (partyMember != null)
                                        {
                                            // Get the actual player entity for detailed Life/ES info
                                            var playerEntity = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                                .FirstOrDefault(x => x != null && x.IsValid && !x.IsHostile &&
                                                                   string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                                                   partyMember.PlayerName?.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                            if (playerEntity != null)
                                            {
                                                // Get Life component for detailed health info
                                                var lifeComponent = playerEntity.GetComponent<Life>();
                                                if (lifeComponent != null)
                                                {
                                                    // Get actual values for total pool calculation
                                                    var currentLife = lifeComponent.Health.Current;
                                                    var maxLife = lifeComponent.Health.Unreserved; // Unreserved life only
                                                    var currentES = lifeComponent.EnergyShield.Current;
                                                    var maxES = lifeComponent.EnergyShield.Unreserved;

                                                    // Debug raw values
                                                    BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Raw values for {partyMember.PlayerName} - Life Current: {currentLife}, Life Max: {maxLife}, ES Current: {currentES}, ES Max: {maxES}");

                                                    // Calculate meaningful thresholds - only consider ES if they have a meaningful amount
                                                    var hasMeaningfulES = maxES >= 500; // Only consider ES if they have 500+ max ES
                                                    var effectiveCurrentES = hasMeaningfulES ? currentES : 0;
                                                    var effectiveMaxES = hasMeaningfulES ? maxES : 0;

                                                    // Calculate total pool percentage using unreserved life + meaningful ES
                                                    var totalCurrent = currentLife + effectiveCurrentES;
                                                    var totalMax = maxLife + effectiveMaxES;
                                                    var totalPoolPercentage = totalMax > 0 ? ((double)totalCurrent / (double)totalMax) * 100 : 100;

                                                    // Debug calculation
                                                    BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Calculation for {partyMember.PlayerName} - Total Current: {totalCurrent}, Total Max: {totalMax}, Percentage: {totalPoolPercentage:F2}%");

                                                    BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Party member {partyMember.PlayerName} - Life: {currentLife:F0}/{maxLife:F0}, ES: {currentES:F0}/{maxES:F0} ({(hasMeaningfulES ? "meaningful" : "negligible")}), Total Pool: {totalPoolPercentage:F1}%");

                                                    if (totalPoolPercentage < Settings.rejuvenationTotemHpThreshold.Value)
                                                    {
                                                        partyMembersLowHp = true;
                                                        BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Party member {partyMember.PlayerName} total pool below threshold ({totalPoolPercentage:F1}% < {Settings.rejuvenationTotemHpThreshold.Value}%) - placing totem");
                                                        break;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // Fallback: Skip this party member if we can't get entity info
                                                BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Could not get entity info for party member {partyMember.PlayerName}, skipping");
                                            }
                                        }
                                    }

                                    // Check if we're within following distance of the leader
                                    var withinFollowingDistance = true;
                                    var leaderName = Settings.autoPilotLeader.Value;
                                    BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Checking leader distance - Leader: '{leaderName}'");

                                    if (!string.IsNullOrWhiteSpace(leaderName))
                                    {
                                        var leaderPartyElement = partyElements
                                            .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                                                leaderName.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                        if (leaderPartyElement != null)
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Found leader in party: {leaderPartyElement.PlayerName}");

                                            // Get distance to leader
                                            var leaderEntity = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                                .FirstOrDefault(x => x != null && x.IsValid && !x.IsHostile &&
                                                                   string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                                                   leaderName.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                            if (leaderEntity != null)
                                            {
                                                var distanceToLeader = Vector2.Distance(
                                                    new Vector2(playerPosition.X, playerPosition.Y),
                                                    new Vector2(leaderEntity.PosNum.X, leaderEntity.PosNum.Y));

                                                // Use the following distance setting
                                                withinFollowingDistance = distanceToLeader <= Settings.autoPilotPathfindingNodeDistance.Value;
                                                BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Distance to leader: {distanceToLeader:F1}, Max allowed: {Settings.autoPilotPathfindingNodeDistance.Value}, Within range: {withinFollowingDistance}");
                                            }
                                            else
                                            {
                                                BetterFollowbotLite.Instance.LogMessage("REJUVENATION TOTEM: Could not find leader entity");
                                            }
                                        }
                                        else
                                        {
                                            BetterFollowbotLite.Instance.LogMessage("REJUVENATION TOTEM: Leader not found in party list");
                                        }
                                    }
                                    else
                                    {
                                        BetterFollowbotLite.Instance.LogMessage("REJUVENATION TOTEM: No leader set, allowing totem placement");
                                    }

                                    // Debug logging for placement conditions
                                    BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Placement check - Rare/Unique: {hasRareOrUniqueNearby}, Party low HP: {partyMembersLowHp}, Within distance: {withinFollowingDistance}");

                                    // Place totem if conditions are met
                                    if ((hasRareOrUniqueNearby || partyMembersLowHp) && withinFollowingDistance)
                                    {
                                        BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: 🔥 CONDITIONS MET - Rare/Unique: {hasRareOrUniqueNearby}, Party low HP: {partyMembersLowHp}, Within distance: {withinFollowingDistance}");

                                        // Check UI menu status
                                        var stashOpen = GameController.IngameState.IngameUi.StashElement.IsVisibleLocal;
                                        var npcDialogOpen = GameController.IngameState.IngameUi.NpcDialog.IsVisible;
                                        var sellWindowOpen = GameController.IngameState.IngameUi.SellWindow.IsVisible;
                                        var purchaseWindowOpen = GameController.IngameState.IngameUi.PurchaseWindow.IsVisible;
                                        var mapOpen = GameController.IngameState.IngameUi.Map.IsVisible;
                                        var menuWindowOpen = MenuWindow.IsOpened;

                                        // Additional potential blocking elements
                                        var inventoryOpen = GameController.IngameState.IngameUi.InventoryPanel.IsVisible;
                                        var skillTreeOpen = GameController.IngameState.IngameUi.TreePanel.IsVisible;

                                        BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: UI Status - Stash: {stashOpen}, NPC: {npcDialogOpen}, Sell: {sellWindowOpen}, Purchase: {purchaseWindowOpen}, Map: {mapOpen}, Menu: {menuWindowOpen}, Inv: {inventoryOpen}, Tree: {skillTreeOpen}");

                                        // Check for blocking UI elements (map is non-obstructing in PoE)
                                        if (stashOpen || npcDialogOpen || sellWindowOpen || purchaseWindowOpen || menuWindowOpen || inventoryOpen || skillTreeOpen)
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: ❌ Skipping totem placement - blocking UI menu is open");
                                            return;
                                        }

                                        // Log if map is detected as open (for debugging) but don't block on it
                                        if (mapOpen)
                                        {
                                            BetterFollowbotLite.Instance.LogMessage("REJUVENATION TOTEM: ℹ️ Map detected as open but proceeding (non-obstructing in PoE)");
                                        }

                                        BetterFollowbotLite.Instance.LogMessage("REJUVENATION TOTEM: ✅ No UI menus detected, proceeding with placement");

                                        // Move cursor to screen center before placing totem
                                        var screenRect = GameController.Window.GetWindowRectangle();
                                        var screenCenter = new Vector2(screenRect.Width / 2, screenRect.Height / 2);
                                        Mouse.SetCursorPos(screenCenter);
                                        BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: 🎯 Moved cursor to screen center: {screenCenter}");

                                        // Small delay to ensure mouse movement is registered
                                        System.Threading.Thread.Sleep(50);

                                        // Check if the skill is available and can be used
                                        if (skill.SkillSlotIndex < 0 || skill.SkillSlotIndex >= 12)
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: ❌ Invalid skill slot index: {skill.SkillSlotIndex}");
                                            return;
                                        }

                                        // Get the skill slot for the totem
                                        var skillSlot = GetSkillInputKey(skill.SkillSlotIndex);
                                        BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: 🎮 Using skill slot: {skillSlot}, SkillSlotIndex: {skill.SkillSlotIndex}");

                                        // Check if skill has charges available
                                        if (skill.RemainingUses <= 0 && skill.IsOnCooldown)
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: ❌ Skill unavailable - RemainingUses: {skill.RemainingUses}, IsOnCooldown: {skill.IsOnCooldown}");
                                            return;
                                        }

                                        BetterFollowbotLite.Instance.LogMessage("REJUVENATION TOTEM: ✅ Skill is available, sending key press");

                                        // Place the totem
                                        Keyboard.KeyPress(skillSlot);
                                        BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: 🎉 Key press sent to place totem using key: {skillSlot}");

                                        // Set cooldown to prevent spamming (2 seconds as requested)
                                        SkillInfo.rejuvenationTotem.Cooldown = 2000;
                                        lastTimeAny = DateTime.Now; // Update global cooldown like other skills
                                        BetterFollowbotLite.Instance.LogMessage("REJUVENATION TOTEM: ⏰ Cooldown set to 2000ms (2 seconds) and global cooldown updated");

                                        BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: ✨ TOTEM PLACED SUCCESSFULLY - Rare/Unique nearby: {hasRareOrUniqueNearby}, Party low HP: {partyMembersLowHp}, Within distance: {withinFollowingDistance}");
                                    }
                                    else
                                    {
                                        if (!(hasRareOrUniqueNearby || partyMembersLowHp))
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Conditions not met - No rare/unique enemies AND no party members need healing");
                                        }
                                        else if (!withinFollowingDistance)
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Conditions not met - Too far from leader");
                                        }
                                        else
                                        {
                                            BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: Conditions not met - Rare/Unique: {hasRareOrUniqueNearby}, Party low HP: {partyMembersLowHp}, Within distance: {withinFollowingDistance}");
                                        }
                                    }
                                }
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage($"REJUVENATION TOTEM: ❌ Cooldown check FAILED (remaining: {SkillInfo.rejuvenationTotem.Cooldown:F0}ms), skipping totem");
                                return; // Exit early if on cooldown
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Error handling without logging
                    }
                }

                #endregion

                #region Vaal Skills

                if (Settings.vaalHasteEnabled)
                {
                    try
                    {
                        if (skill.Id == SkillInfo.vaalHaste.Id)
                        {
                            BetterFollowbotLite.Instance.LogMessage($"VAAL HASTE: Vaal Haste skill detected - ID: {skill.Id}, Name: {skill.Name}");

                            // Vaal skills use charges, not traditional cooldowns
                            if (!(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                            {
                                // Check if we don't already have the vaal haste buff
                                var hasVaalHasteBuff = buffs.Exists(x => x.Name == "vaal_haste");
                                BetterFollowbotLite.Instance.LogMessage($"VAAL HASTE: Has vaal haste buff: {hasVaalHasteBuff}");

                                if (!hasVaalHasteBuff)
                                {
                                    BetterFollowbotLite.Instance.LogMessage("VAAL HASTE: No vaal haste buff found, activating");

                                    // Activate the skill
                                    Keyboard.KeyPress(GetSkillInputKey(skill.SkillSlotIndex));

                                    BetterFollowbotLite.Instance.LogMessage("VAAL HASTE: Vaal Haste activated successfully");
                                }
                                else
                                {
                                    BetterFollowbotLite.Instance.LogMessage("VAAL HASTE: Already have vaal haste buff, skipping");
                                }
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage("VAAL HASTE: Cooldown check failed");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Error handling without logging
                    }
                }

                if (Settings.vaalDisciplineEnabled)
                {
                    try
                    {
                        if (skill.Id == SkillInfo.vaalDiscipline.Id)
                        {
                            BetterFollowbotLite.Instance.LogMessage($"VAAL DISCIPLINE: Vaal Discipline skill detected - ID: {skill.Id}, Name: {skill.Name}");

                            // Vaal skills use charges, not traditional cooldowns
                            if (!(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                            {
                                // Check if ES is below threshold for player or any party member
                                var playerEsPercentage = player.ESPercentage;
                                var threshold = (float)Settings.vaalDisciplineEsp / 100;
                                var esBelowThreshold = playerEsPercentage < threshold;

                                BetterFollowbotLite.Instance.LogMessage($"VAAL DISCIPLINE: Player ES%: {playerEsPercentage:F1}, Threshold: {threshold:F2}");

                                // Check party members' ES if player's ES is not below threshold
                                if (!esBelowThreshold)
                                {
                                    var partyElements = PartyElements.GetPlayerInfoElementList();

                                    foreach (var partyMember in partyElements)
                                    {
                                        if (partyMember != null)
                                        {
                                            // Get the actual player entity for ES info
                                            var playerEntity = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                                .FirstOrDefault(x => x != null && x.IsValid && !x.IsHostile &&
                                                                   string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                                                   partyMember.PlayerName?.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                            if (playerEntity != null)
                                            {
                                                // Get Life component for ES info
                                                var lifeComponent = playerEntity.GetComponent<Life>();
                                                if (lifeComponent != null)
                                                {
                                                    var partyEsPercentage = lifeComponent.ESPercentage; // Already in 0-1 range
                                                    BetterFollowbotLite.Instance.LogMessage($"VAAL DISCIPLINE: Party member {partyMember.PlayerName} ES%: {partyEsPercentage:F1}");

                                                    if (partyEsPercentage < threshold)
                                                    {
                                                        esBelowThreshold = true;
                                                        BetterFollowbotLite.Instance.LogMessage($"VAAL DISCIPLINE: Party member {partyMember.PlayerName} ES below threshold");
                                                        break;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // Skip this party member if we can't get entity info
                                                BetterFollowbotLite.Instance.LogMessage($"VAAL DISCIPLINE: Could not get entity info for party member {partyMember.PlayerName}, skipping");
                                            }
                                        }
                                    }
                                }

                                if (esBelowThreshold)
                                {
                                    // Check if we don't already have the vaal discipline buff
                                    var hasVaalDisciplineBuff = buffs.Exists(x => x.Name == "vaal_discipline");
                                    BetterFollowbotLite.Instance.LogMessage($"VAAL DISCIPLINE: Has vaal discipline buff: {hasVaalDisciplineBuff}");

                                    if (!hasVaalDisciplineBuff)
                                    {
                                        BetterFollowbotLite.Instance.LogMessage("VAAL DISCIPLINE: ES below threshold and no buff found, activating");

                                        // Activate the skill
                                        Keyboard.KeyPress(GetSkillInputKey(skill.SkillSlotIndex));

                                        BetterFollowbotLite.Instance.LogMessage("VAAL DISCIPLINE: Vaal Discipline activated successfully");
                                    }
                                    else
                                    {
                                        BetterFollowbotLite.Instance.LogMessage("VAAL DISCIPLINE: Already have vaal discipline buff, skipping");
                                    }
                                }
                                else
                                {
                                    BetterFollowbotLite.Instance.LogMessage("VAAL DISCIPLINE: ES above threshold, skipping");
                                }
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage("VAAL DISCIPLINE: Cooldown check failed");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Error handling without logging
                    }
                }

                #endregion

                #region Mines

                if (Settings.minesEnabled)
                {
                    try
                    {
                        // Check if we have either stormblast or pyroclast mine skills enabled
                        var hasStormblastMine = Settings.minesStormblastEnabled && skill.Id == SkillInfo.stormblastMine.Id;
                        var hasPyroclastMine = Settings.minesPyroclastEnabled && skill.Id == SkillInfo.pyroclastMine.Id;

                        if (hasStormblastMine || hasPyroclastMine)
                        {
                            // Check cooldown
                            var mineSkill = hasStormblastMine ? SkillInfo.stormblastMine : SkillInfo.pyroclastMine;
                            if (SkillInfo.ManageCooldown(mineSkill, skill))
                            {
                                // Find nearby rare/unique enemies within range
                                var nearbyRareUniqueEnemies = enemys
                                    .Where(monster =>
                                    {
                                        // Check if monster is rare or unique
                                        if (monster.Rarity != MonsterRarity.Rare && monster.Rarity != MonsterRarity.Unique)
                                            return false;

                                        // Check distance from player to monster
                                        var distanceToMonster = Vector2.Distance(
                                            new Vector2(monster.PosNum.X, monster.PosNum.Y),
                                            new Vector2(playerPosition.X, playerPosition.Y));

                                        // Parse mines range from text input, default to 35 if invalid
                                        if (!int.TryParse(Settings.minesRange.Value, out var minesRange))
                                            minesRange = 35;

                                        return distanceToMonster <= minesRange;
                                    })
                                    .ToList();

                                if (nearbyRareUniqueEnemies.Any())
                                {
                                    // Check if we're close to the party leader
                                    var shouldThrowMine = false;
                                    var leaderPos = Vector2.Zero;

                                    if (!string.IsNullOrEmpty(Settings.autoPilotLeader.Value))
                                    {
                                        // Get party elements
                                        var partyElements = PartyElements.GetPlayerInfoElementList();
                                        var leaderPartyElement = partyElements
                                            .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                                                Settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                        if (leaderPartyElement != null)
                                        {
                                            // Find the actual player entity by name
                                            var playerEntities = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                                .Where(x => x != null && x.IsValid && !x.IsHostile);

                                            var leaderEntity = playerEntities
                                                .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                                    Settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                            if (leaderEntity != null)
                                            {
                                                // Check distance to leader
                                                var distanceToLeader = Vector2.Distance(
                                                    new Vector2(playerPosition.X, playerPosition.Y),
                                                    new Vector2(leaderEntity.Pos.X, leaderEntity.Pos.Y));

                                                // Parse leader distance from text input, default to 50 if invalid
                                                if (!int.TryParse(Settings.minesLeaderDistance.Value, out var leaderDistance))
                                                    leaderDistance = 50;

                                                if (distanceToLeader <= leaderDistance)
                                                {
                                                    shouldThrowMine = true;
                                                    leaderPos = new Vector2(leaderEntity.Pos.X, leaderEntity.Pos.Y);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // If no leader set, always throw mines when enemies are nearby
                                        shouldThrowMine = true;
                                    }

                                    if (shouldThrowMine)
                                    {
                                        // Find the best position to throw the mine (near enemies but not too close to leader if we have one)
                                        var bestTarget = nearbyRareUniqueEnemies
                                            .OrderBy(monster =>
                                            {
                                                var monsterPos = new Vector2(monster.PosNum.X, monster.PosNum.Y);
                                                var distanceToMonster = Vector2.Distance(new Vector2(playerPosition.X, playerPosition.Y), monsterPos);

                                                // If we have a leader, prefer targets that are closer to the leader
                                                if (leaderPos != Vector2.Zero)
                                                {
                                                    var distanceToLeader = Vector2.Distance(monsterPos, leaderPos);
                                                    return distanceToLeader + distanceToMonster * 0.5f; // Weight both distances
                                                }

                                                return distanceToMonster;
                                            })
                                            .FirstOrDefault();

                                        if (bestTarget != null)
                                        {
                                            // Move mouse to target position
                                            var targetScreenPos = GameController.IngameState.Camera.WorldToScreen(bestTarget.Pos);
                                            Mouse.SetCursorPos(targetScreenPos);

                                            // Small delay to ensure mouse movement is registered
                                            System.Threading.Thread.Sleep(50);

                                            // Activate the mine skill
                                            Keyboard.KeyPress(GetSkillInputKey(skill.SkillSlotIndex));
                                            mineSkill.Cooldown = 100; // Set cooldown to prevent spam
                                            lastTimeAny = DateTime.Now;

                                            if (Settings.debugMode)
                                            {
                                                LogMessage($"MINES: Threw {(hasStormblastMine ? "Stormblast" : "Pyroclast")} mine at {bestTarget.Path} (Rarity: {bestTarget.Rarity})");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // Error handling without logging
                    }
                }

                #endregion

                /*
                #region Spider

                if (false)
                {
                    if (skill.Id == SkillInfo.summonSpiders.Id && SkillInfo.ManageCooldown(SkillInfo.summonSpiders, skill))
                    {
                        var spidersSummoned = buffs.Count(x => x.Name == SkillInfo.summonSpiders.BuffName);

                        if (spidersSummoned < 20 && GetCorpseWithin(30) >= 2)
                        {

                        }
                    }
                }


                #endregion
                */
                #region Detonate Mines ( to be done )
/*
                    if (Settings.minesEnabled)
                    {
                        try
                        {
                            var remoteMines = localPlayer.GetComponent<Actor>().DeployedObjects.Where(x =>
                                    x.Entity != null && x.Entity.Path == "Metadata/MiscellaneousObjects/RemoteMine")
                                .ToList();

                            // Removed Logic
                            // What should a proper Detonator do and when ?
                            // Detonate Mines when they have the chance to hit a target (Range), include min. mines ?
                            // Internal delay 500-1000ms ?
                        }
                        catch (Exception e)
                        {
                            // Error handling without logging
                        }
                    }
                    */
                #endregion
            }

        }
        catch (Exception e)
        {
            // Error handling without logging
        }
    }

    // Taken from ->
    // https://www.reddit.com/r/pathofexiledev/comments/787yq7/c_logout_app_same_method_as_lutbot/
        
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