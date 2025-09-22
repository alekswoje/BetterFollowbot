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

        public void Execute()
        {
            // Mines logic is handled in the main skill processing loop
            // This method is called but the actual logic is in the main loop
            // to handle both Stormblast and Pyroclast mines
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
                    // Check cooldown
                    var mineSkill = hasStormblastMine ? SkillInfo.stormblastMine : SkillInfo.pyroclastMine;
                    if (SkillInfo.ManageCooldown(mineSkill, skill))
                    {
                        // Find nearby rare/unique enemies within range
                        var nearbyRareUniqueEnemies = _instance.Enemys
                            .Where(monster =>
                            {
                                // Check if monster is rare or unique
                                var rarityComponent = monster.GetComponent<ObjectMagicProperties>();
                                if (rarityComponent == null || (rarityComponent.Rarity != MonsterRarity.Rare && rarityComponent.Rarity != MonsterRarity.Unique))
                                    return false;

                                // Check distance from player to monster
                                var distanceToMonster = Vector2.Distance(
                                    new Vector2(monster.PosNum.X, monster.PosNum.Y),
                                    new Vector2(_instance.playerPosition.X, _instance.playerPosition.Y));

                                // Parse mines range from text input, default to 35 if invalid
                                if (!int.TryParse(_settings.minesRange.Value, out var minesRange))
                                    minesRange = 35;

                                return distanceToMonster <= minesRange;
                            })
                            .ToList();

                        if (nearbyRareUniqueEnemies.Any())
                        {
                            // Check if we're close to the party leader
                            var shouldThrowMine = false;
                            var leaderPos = Vector2.Zero;

                            if (!string.IsNullOrEmpty(_settings.autoPilotLeader.Value))
                            {
                                // Get party elements
                                var partyElements = PartyElements.GetPlayerInfoElementList();
                                var leaderPartyElement = partyElements
                                    .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                                        _settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                if (leaderPartyElement != null)
                                {
                                    // Find the actual player entity by name
                                    var playerEntities = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                        .Where(x => x != null && x.IsValid && !x.IsHostile);

                                    var leaderEntity = playerEntities
                                        .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                            _settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                    if (leaderEntity != null)
                                    {
                                        // Check distance to leader
                                        var distanceToLeader = Vector2.Distance(
                                            new Vector2(_instance.playerPosition.X, _instance.playerPosition.Y),
                                            new Vector2(leaderEntity.Pos.X, leaderEntity.Pos.Y));

                                        // Parse leader distance from text input, default to 50 if invalid
                                        if (!int.TryParse(_settings.minesLeaderDistance.Value, out var leaderDistance))
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
                                        var distanceToMonster = Vector2.Distance(new Vector2(_instance.playerPosition.X, _instance.playerPosition.Y), monsterPos);

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
                                    var targetScreenPos = _instance.GameController.IngameState.Camera.WorldToScreen(bestTarget.Pos);
                                    Mouse.SetCursorPos(targetScreenPos);

                                    // Small delay to ensure mouse movement is registered
                                    System.Threading.Thread.Sleep(50);

                                    // Activate the mine skill
                                    Keyboard.KeyPress(_instance.GetSkillInputKey(skill.SkillSlotIndex));
                                    mineSkill.Cooldown = 100; // Set cooldown to prevent spam
                                    _instance.LastTimeAny = DateTime.Now;

                                    if (_settings.debugMode)
                                    {
                                        var rarityComponent = bestTarget.GetComponent<ObjectMagicProperties>();
                                        var rarity = rarityComponent?.Rarity.ToString() ?? "Unknown";
                                        _instance.LogMessage($"MINES: Threw {(hasStormblastMine ? "Stormblast" : "Pyroclast")} mine at {bestTarget.Path} (Rarity: {rarity})");
                                    }

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
    }
}
