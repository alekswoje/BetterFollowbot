using System;
using System.Linq;
using BetterFollowbotLite.Interfaces;
using BetterFollowbotLite.Core.Skills;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using GameOffsets.Native;
using SharpDX;

namespace BetterFollowbotLite.Skill
{
    internal class Mines : ISkill
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public Mines(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public bool IsEnabled => _settings.minesEnabled;

        public string SkillName => "Mines";

        /// <summary>
        /// Checks if any blocking UI elements are open that should prevent skill execution
        /// </summary>
        private bool IsBlockingUiOpen()
        {
            try
            {
                // Check common blocking UI elements
                var stashOpen = _instance.GameController?.IngameState?.IngameUi?.StashElement?.IsVisibleLocal == true;
                var npcDialogOpen = _instance.GameController?.IngameState?.IngameUi?.NpcDialog?.IsVisible == true;
                var sellWindowOpen = _instance.GameController?.IngameState?.IngameUi?.SellWindow?.IsVisible == true;
                var purchaseWindowOpen = _instance.GameController?.IngameState?.IngameUi?.PurchaseWindow?.IsVisible == true;
                var inventoryOpen = _instance.GameController?.IngameState?.IngameUi?.InventoryPanel?.IsVisible == true;
                var skillTreeOpen = _instance.GameController?.IngameState?.IngameUi?.TreePanel?.IsVisible == true;
                var atlasOpen = _instance.GameController?.IngameState?.IngameUi?.Atlas?.IsVisible == true;

                // Note: Map is non-obstructing in PoE, so we don't check it
                return stashOpen || npcDialogOpen || sellWindowOpen || purchaseWindowOpen || inventoryOpen || skillTreeOpen || atlasOpen;
            }
            catch
            {
                // If we can't check UI state, err on the side of caution
                return true;
            }
        }

        /// <summary>
        /// Executes mines logic by processing all mine skills
        /// </summary>
        public void Execute()
        {
            // Block skill execution when blocking UI is open
            if (IsBlockingUiOpen())
                return;

            // Block skill execution in towns
            if (_instance.GameController?.Area?.CurrentArea?.IsTown == true)
                return;

            // Always log that Execute was called for debugging
            _instance.LogMessage($"MINES: Execute called (Enabled: {_settings.minesEnabled.Value})");

            if (!_settings.minesEnabled)
                return;

            try
            {
                // Loop through all skills to find mine skills
                foreach (var skill in _instance.skills)
                {
                    ProcessMineSkill(skill);
                }
            }
            catch (Exception e)
            {
                _instance.LogError($"Mines Execute Error: {e}");
            }
        }

        /// <summary>
        /// Processes mines logic for a specific skill
        /// </summary>
        public bool ProcessMineSkill(ActorSkill skill)
        {
            if (!_settings.minesEnabled)
                return false;

            try
            {
                // Check if we have either stormblast or pyroclast mine skills enabled
                var hasStormblastMine = _settings.minesStormblastEnabled && skill.Id == SkillInfo.stormblastMine.Id;
                var hasPyroclastMine = _settings.minesPyroclastEnabled && skill.Id == SkillInfo.pyroclastMine.Id;

                if (hasStormblastMine || hasPyroclastMine)
                {
                    _instance.LogMessage($"MINES: Found {(hasStormblastMine ? "Stormblast" : "Pyroclast")} mine skill (ID: {skill.Id})");

                    // Check cooldown
                    var mineSkill = hasStormblastMine ? SkillInfo.stormblastMine : SkillInfo.pyroclastMine;
                    if (SkillInfo.ManageCooldown(mineSkill, skill))
                    {
                        // Parse mines range from text input, default to 35 if invalid
                        if (!int.TryParse(_settings.minesRange.Value, out var minesRange))
                            minesRange = 35;

                        // Find nearby rare/unique enemies within range using ReAgent-style detection
                        var nearbyRareUniqueEnemies = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                            .Where(monster => IsValidMonsterForMines(monster, minesRange))
                            .ToList();

                        // Only log the count, no per-monster spam
                        if (nearbyRareUniqueEnemies.Any())
                        {
                            _instance.LogMessage($"MINES: Found {nearbyRareUniqueEnemies.Count} rare/unique enemies within range (range: {minesRange})");
                        }

                        if (nearbyRareUniqueEnemies.Any())
                        {
                            // Check if we're close to the party leader
                            var shouldThrowMine = false;
                            Entity leaderEntity = null;

                            if (!string.IsNullOrEmpty(_settings.autoPilotLeader.Value))
                            {
                                _instance.LogMessage($"MINES: Checking for leader '{_settings.autoPilotLeader.Value}'");

                                // Get party elements
                                var partyElements = PartyElements.GetPlayerInfoElementList();
                                var leaderPartyElement = partyElements
                                    .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                                        _settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                if (leaderPartyElement != null)
                                {
                                    _instance.LogMessage($"MINES: Found leader party element for '{_settings.autoPilotLeader.Value}'");

                                    // Find the actual player entity by name
                                    var playerEntities = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                        .Where(x => x != null && x.IsValid && !x.IsHostile);

                                    leaderEntity = playerEntities
                                        .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                            _settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                    if (leaderEntity != null)
                                    {
                                        _instance.LogMessage($"MINES: Found leader entity '{leaderEntity.GetComponent<Player>()?.PlayerName}'");

                                        // Check distance to leader
                                        var distanceToLeader = Vector3.Distance(_instance.playerPosition, leaderEntity.Pos);

                                        // Parse leader distance from text input, default to 50 if invalid
                                        if (!int.TryParse(_settings.minesLeaderDistance.Value, out var leaderDistance))
                                            leaderDistance = 50;

                                        _instance.LogMessage($"MINES: Distance to leader: {distanceToLeader:F1}, threshold: {leaderDistance}");

                                        if (distanceToLeader <= leaderDistance)
                                        {
                                            shouldThrowMine = true;
                                            _instance.LogMessage("MINES: Close enough to leader, allowing mine throw");
                                        }
                                        else
                                        {
                                            _instance.LogMessage("MINES: Too far from leader, not throwing mines");
                                        }
                                    }
                                    else
                                    {
                                        _instance.LogMessage("MINES: Leader entity not found in world");
                                    }
                                }
                                else
                                {
                                    _instance.LogMessage($"MINES: Leader party element not found for '{_settings.autoPilotLeader.Value}'");
                                }
                            }
                            else
                            {
                                // If no leader set, always throw mines when enemies are nearby
                                _instance.LogMessage("MINES: No leader set, allowing mine throw");
                                shouldThrowMine = true;
                            }

                            if (shouldThrowMine)
                            {
                                // Find the best position to throw the mine (near enemies but not too close to leader if we have one)
                                var bestTarget = nearbyRareUniqueEnemies
                                    .OrderBy(monster =>
                                    {
                                        var monsterPos = monster.Pos;
                                        var distanceToMonster = Vector3.Distance(_instance.playerPosition, monsterPos);

                                        // If we have a leader, prefer targets that are closer to the leader
                                        if (leaderEntity != null)
                                        {
                                            var distanceToLeader = Vector3.Distance(monsterPos, leaderEntity.Pos);
                                            return distanceToLeader + distanceToMonster * 0.5f; // Weight both distances
                                        }

                                        return distanceToMonster;
                                    })
                                    .FirstOrDefault();

                                if (bestTarget != null)
                                {
                                    // Move mouse to target position
                                    var targetScreenPos = _instance.GameController.IngameState.Camera.WorldToScreen(bestTarget.Pos);
                                    Mouse.SetCursorPos(new Vector2(targetScreenPos.X, targetScreenPos.Y));

                                    // Small delay to ensure mouse movement is registered
                                    System.Threading.Thread.Sleep(50);

                                    // Activate the mine skill
                                    var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                                    if (skillKey != Keys.None)
                                    {
                                        Keyboard.KeyPress(skillKey);
                                    }
                                    mineSkill.Cooldown = 100; // Set cooldown to prevent spam
                                    _instance.LastTimeAny = DateTime.Now;

                                    var rarityComponent = bestTarget.GetComponent<ObjectMagicProperties>();
                                    var rarity = rarityComponent?.Rarity.ToString() ?? "Unknown";
                                    var distance = Vector3.Distance(_instance.playerPosition, bestTarget.Pos);
                                    _instance.LogMessage($"MINES: Threw {(hasStormblastMine ? "Stormblast" : "Pyroclast")} mine at {bestTarget.Path} (Rarity: {rarity}, Distance: {distance:F1})");

                                    return true; // Skill was executed
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

            return false; // Skill was not executed
        }

        /// <summary>
        /// Validates if a monster is valid for mines targeting (based on ReAgent logic)
        /// </summary>
        private bool IsValidMonsterForMines(Entity monster, int minesRange)
        {
            try
            {
                // ReAgent-style validation checks
                if (monster.DistancePlayer > minesRange)
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

                // Use Entity.Rarity directly like ReAgent does
                var rarity = monster.Rarity;

                // Only target Rare or Unique monsters
                if (rarity != ExileCore.Shared.Enums.MonsterRarity.Rare &&
                    rarity != ExileCore.Shared.Enums.MonsterRarity.Unique)
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

                return true;
            }
            catch (Exception ex)
            {
                _instance.LogError($"MINES: Error validating monster {monster?.Path}: {ex.Message}");
                return false;
            }
        }
    }
}
