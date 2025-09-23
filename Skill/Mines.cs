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

        /// <summary>
        /// Executes mines logic by processing all mine skills
        /// </summary>
        public void Execute()
        {
            if (!_settings.minesEnabled)
                return;

            try
            {
                _instance.LogMessage($"MINES: Execute called, processing {(_instance.skills?.Count ?? 0)} skills");

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

                        // Find nearby rare/unique enemies within range
                        var nearbyRareUniqueEnemies = _instance.Enemys
                            .Where(monster =>
                            {
                                // Check if monster is rare or unique
                                var rarityComponent = monster.GetComponent<ObjectMagicProperties>();
                                if (rarityComponent == null || (rarityComponent.Rarity != MonsterRarity.Rare && rarityComponent.Rarity != MonsterRarity.Unique))
                                    return false;

                                // Check distance from player to monster
                                var distanceToMonster = Vector3.Distance(monster.Pos, _instance.playerPosition);

                                return distanceToMonster <= minesRange;
                            })
                            .ToList();

                        _instance.LogMessage($"MINES: Found {nearbyRareUniqueEnemies.Count} rare/unique enemies within range (range: {minesRange})");

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
                                    Keyboard.KeyPress(_instance.GetSkillInputKey(skill.SkillSlotIndex));
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
    }
}
