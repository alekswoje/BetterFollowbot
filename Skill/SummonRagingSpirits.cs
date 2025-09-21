using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using System.Windows.Forms;

namespace BetterFollowbotLite.Skills
{
    internal class SummonRagingSpirits
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;
        private readonly AutoPilot _autoPilot;
        private readonly Summons _summons;

        public SummonRagingSpirits(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings,
                                   AutoPilot autoPilot, Summons summons)
        {
            _instance = instance;
            _settings = settings;
            _autoPilot = autoPilot;
            _summons = summons;
        }

        // Alternative method to access entities
        private IEnumerable<Entity> GetEntities()
        {
            try
            {
                // Use the main instance to access entities
                return _instance.GetEntitiesFromGameController();
            }
            catch
            {
                // Return empty collection if access fails
                return new List<Entity>();
            }
        }

        public void Execute()
        {
            try
            {
                // Debug: Always log when SRS Execute is called
                _instance.LogMessage($"SRS: Execute called - Enabled: {_settings.summonRagingSpiritsEnabled.Value}, AutoPilot: {_autoPilot != null}, FollowTarget: {_autoPilot?.FollowTarget != null}");

                if (_settings.summonRagingSpiritsEnabled.Value && _autoPilot != null && _autoPilot.FollowTarget != null)
                {
                    var distanceToLeader = Vector3.Distance(_instance.playerPosition, _autoPilot.FollowTargetPosition);

                    // Check if we're close to the leader (within AutoPilot follow distance)
                    if (distanceToLeader <= _settings.autoPilotClearPathDistance.Value)
                    {
                        // Count current summoned raging spirits
                        var ragingSpiritCount = Summons.GetRagingSpiritCount();
                        var totalMinionCount = Summons.GetTotalMinionCount();
                        _instance.LogMessage($"SRS: Minion count check - Raging spirits: {ragingSpiritCount}, Total minions: {totalMinionCount}, Required: {_settings.summonRagingSpiritsMinCount.Value}");

                        // Only cast SRS if we have less than the minimum required count
                        if (totalMinionCount < _settings.summonRagingSpiritsMinCount.Value)
                        {
                            // Check for HOSTILE rare/unique enemies within 500 units (exclude player's own minions)
                            bool rareOrUniqueNearby = false;
                            var entities = GetEntities().Where(x => x.Type == EntityType.Monster);
                            _instance.LogMessage($"SRS: Checking for enemies - Monster entities found: {entities.Count()}");

                            // Get list of deployed object IDs to exclude player's own minions
                            var deployedObjectIds = new HashSet<uint>();
                            if (_instance.localPlayer.TryGetComponent<Actor>(out var actorComponent))
                            {
                                foreach (var deployedObj in actorComponent.DeployedObjects)
                                {
                                    if (deployedObj?.Entity != null)
                                    {
                                        deployedObjectIds.Add(deployedObj.Entity.Id);
                                    }
                                }
                            }

                            foreach (var entity in entities)
                            {
                                if (entity.IsValid && entity.IsAlive && entity.IsHostile)
                                {
                                    var distanceToEntity = Vector3.Distance(_instance.playerPosition, entity.Pos);

                                    // Only check entities within 500 units and ensure they're not player's deployed objects
                                    if (distanceToEntity <= 500 && !deployedObjectIds.Contains(entity.Id))
                                    {
                                        var rarityComponent = entity.GetComponent<ObjectMagicProperties>();
                                        if (rarityComponent != null)
                                        {
                                            var rarity = rarityComponent.Rarity;

                                            // Always check for rare/unique
                                            if (rarity == MonsterRarity.Unique || rarity == MonsterRarity.Rare)
                                            {
                                                rareOrUniqueNearby = true;
                                                _instance.LogMessage($"SRS: Found hostile {rarity} enemy within range! Distance: {distanceToEntity:F1}");
                                                break;
                                            }
                                            // Also check for magic/white if enabled
                                            else if (_settings.summonRagingSpiritsMagicNormal.Value &&
                                                    (rarity == MonsterRarity.Magic || rarity == MonsterRarity.White))
                                            {
                                                rareOrUniqueNearby = true;
                                                _instance.LogMessage($"SRS: Found hostile {rarity} enemy within range! Distance: {distanceToEntity:F1}");
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            _instance.LogMessage($"SRS: Enemy detection result - Rare/Unique nearby: {rareOrUniqueNearby}");

                            if (rareOrUniqueNearby)
                            {
                                // Find the Summon Raging Spirits skill
                                var summonRagingSpiritsSkill = _instance.skills.FirstOrDefault(s =>
                                    s.Name.Contains("SummonRagingSpirit") ||
                                    s.Name.Contains("Summon Raging Spirit") ||
                                    (s.Name.Contains("summon") && s.Name.Contains("spirit") && s.Name.Contains("rag")));

                                _instance.LogMessage($"SRS: Skill search - Found: {summonRagingSpiritsSkill != null}, OnSkillBar: {summonRagingSpiritsSkill?.IsOnSkillBar}, CanBeUsed: {summonRagingSpiritsSkill?.CanBeUsed}");

                                if (summonRagingSpiritsSkill != null && summonRagingSpiritsSkill.IsOnSkillBar && summonRagingSpiritsSkill.CanBeUsed)
                                {
                                    var enemyType = _settings.summonRagingSpiritsMagicNormal.Value ? "Rare/Unique/Magic/White" : "Rare/Unique";
                                    _instance.LogMessage($"SRS: Current spirits: {ragingSpiritCount}, Total minions: {totalMinionCount}, Required: {_settings.summonRagingSpiritsMinCount.Value}, Distance to leader: {distanceToLeader:F1} (max: {_settings.autoPilotClearPathDistance.Value}), Hostile {enemyType} enemy within 500 units detected");

                                    // Use the Summon Raging Spirits skill
                                    Keyboard.KeyPress(_instance.GetSkillInputKey(summonRagingSpiritsSkill.SkillSlotIndex));
                                    _instance.LastTimeAny = DateTime.Now; // Update global cooldown

                                    _instance.LogMessage("SRS: Summoned Raging Spirit successfully");
                                }
                                else if (summonRagingSpiritsSkill == null)
                                {
                                    _instance.LogMessage("SRS: SummonRagingSpirit skill not found in skill bar");
                                }
                                else if (!summonRagingSpiritsSkill.CanBeUsed)
                                {
                                    _instance.LogMessage("SRS: SummonRagingSpirit skill is on cooldown or unavailable");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _instance.LogMessage($"SRS: Exception occurred - {e.Message}");
            }
        }
    }
}
