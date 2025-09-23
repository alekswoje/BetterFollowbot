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

        public void Execute()
        {
            // Block skill execution when blocking UI is open
            if (IsBlockingUiOpen())
                return;

            // Block skill execution in towns
            if (_instance.GameController?.Area?.CurrentArea?.IsTown == true)
                return;

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
                                // Use comprehensive ReAgent-style validation
                                if (IsValidMonsterForSummonRagingSpirits(entity, deployedObjectIds))
                                {
                                    rareOrUniqueNearby = true;
                                    break;
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
                                    var skillKey = _instance.GetSkillInputKey(summonRagingSpiritsSkill.SkillSlotIndex);
                                    if (skillKey != Keys.None)
                                    {
                                        Keyboard.KeyPress(skillKey);
                                    }
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

        /// <summary>
        /// Validates if a monster is valid for Summon Raging Spirits targeting (based on ReAgent logic)
        /// </summary>
        private bool IsValidMonsterForSummonRagingSpirits(Entity monster, HashSet<uint> deployedObjectIds)
        {
            try
            {
                // ReAgent-style validation checks
                const int srsRange = 500; // SRS checks within 500 units
                if (monster.DistancePlayer > srsRange)
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

                // Ensure it's not player's deployed object
                if (deployedObjectIds.Contains(monster.Id))
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

                // Get rarity using component-based approach (consistent with existing code)
                var rarityComponent = monster.GetComponent<ObjectMagicProperties>();
                if (rarityComponent == null)
                    return false;

                var rarity = rarityComponent.Rarity;

                // Always check for rare/unique
                if (rarity == ExileCore.Shared.Enums.MonsterRarity.Unique || rarity == ExileCore.Shared.Enums.MonsterRarity.Rare)
                    return true;

                // Also check for magic/white if enabled
                if (_settings.summonRagingSpiritsMagicNormal.Value &&
                    (rarity == ExileCore.Shared.Enums.MonsterRarity.Magic || rarity == ExileCore.Shared.Enums.MonsterRarity.White))
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                _instance.LogError($"SRS: Error validating monster {monster?.Path}: {ex.Message}");
                return false;
            }
        }
    }
}
