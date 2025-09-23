using System;
using System.Collections.Generic;
using System.Linq;
using BetterFollowbotLite.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using System.Windows.Forms;

namespace BetterFollowbotLite.Skills
{
    internal class SummonRagingSpirits : ISkill
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

        public bool IsEnabled => _settings.summonRagingSpiritsEnabled.Value;

        public string SkillName => "Summon Raging Spirits";

        public void Execute()
        {
            try
            {

                if (_settings.summonRagingSpiritsEnabled.Value && _autoPilot != null && _autoPilot.FollowTarget != null)
                {
                    var distanceToLeader = Vector3.Distance(_instance.playerPosition, _autoPilot.FollowTargetPosition);

                    // Check if we're close to the leader (within AutoPilot follow distance)
                    if (distanceToLeader <= _settings.autoPilotClearPathDistance.Value)
                    {
                        // Count current summoned raging spirits
                        var ragingSpiritCount = Summons.GetRagingSpiritCount();
                        var totalMinionCount = Summons.GetTotalMinionCount();

                        // Only cast SRS if we have less than the minimum required count
                        if (totalMinionCount < _settings.summonRagingSpiritsMinCount.Value)
                        {
                            // Check for HOSTILE rare/unique enemies within 500 units (exclude player's own minions)
                            bool rareOrUniqueNearby = false;
                            var entities = GetEntities().Where(x => x.Type == EntityType.Monster);

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
                                            if (rarity == ExileCore.Shared.Enums.MonsterRarity.Unique || rarity == ExileCore.Shared.Enums.MonsterRarity.Rare)
                                            {
                                                rareOrUniqueNearby = true;
                                                break;
                                            }
                                            // Also check for magic/white if enabled
                                            else if (_settings.summonRagingSpiritsMagicNormal.Value &&
                                                    (rarity == ExileCore.Shared.Enums.MonsterRarity.Magic || rarity == ExileCore.Shared.Enums.MonsterRarity.White))
                                            {
                                                rareOrUniqueNearby = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            if (rareOrUniqueNearby)
                            {
                                // Find the Summon Raging Spirits skill
                                var summonRagingSpiritsSkill = _instance.skills.FirstOrDefault(s =>
                                    s.Name.Contains("SummonRagingSpirit") ||
                                    s.Name.Contains("Summon Raging Spirit") ||
                                    (s.Name.Contains("summon") && s.Name.Contains("spirit") && s.Name.Contains("rag")));

                                if (summonRagingSpiritsSkill != null && summonRagingSpiritsSkill.IsOnSkillBar && summonRagingSpiritsSkill.CanBeUsed)
                                {
                                    // Use the Summon Raging Spirits skill
                                    Keyboard.KeyPress(_instance.GetSkillInputKey(summonRagingSpiritsSkill.SkillSlotIndex));
                                    _instance.LastTimeAny = DateTime.Now; // Update global cooldown
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Silent exception handling
            }
        }
    }
}
