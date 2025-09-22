using System;
using System.Linq;
using BetterFollowbotLite.Interfaces;
using BetterFollowbotLite.Core.Skills;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BetterFollowbotLite.Skills
{
    internal class RejuvenationTotem : ISkill
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public RejuvenationTotem(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.rejuvenationTotemEnabled;

        public string SkillName => "Rejuvenation Totem";

        public void Execute()
        {
            try
            {
                // Loop through all skills to find the rejuvenation totem skill
                foreach (var skill in _instance.skills)
                {
                    if (skill.Id == SkillInfo.rejuvenationTotem.Id)
                    {
                        _instance.LogMessage($"REJUVENATION TOTEM: 🔍 Skill detected - ID: {skill.Id}, SlotIndex: {skill.SkillSlotIndex}, RemainingUses: {skill.RemainingUses}, IsOnCooldown: {skill.IsOnCooldown}, SkillCooldown: {SkillInfo.rejuvenationTotem.Cooldown:F0}ms");

                        if (SkillInfo.ManageCooldown(SkillInfo.rejuvenationTotem, skill))
                        {
                            _instance.LogMessage("REJUVENATION TOTEM: ✅ Cooldown check passed, processing totem logic");

                            // Check if we already have the totem buff
                            var hasTotemBuff = _instance.Buffs.Exists(x => x.Name == "totem_aura_life_regen");
                            if (!hasTotemBuff)
                            {
                                // Check for unique/rare monsters within range
                                var monsterCount = _instance.GetMonsterWithin(_settings.rejuvenationTotemRange, MonsterRarity.Rare);
                                var uniqueMonsterCount = _instance.GetMonsterWithin(_settings.rejuvenationTotemRange, MonsterRarity.Unique);
                                var hasRareOrUniqueNearby = monsterCount > 0 || uniqueMonsterCount > 0;

                                // Check if any party member total pool (Life + ES) is below threshold
                                var partyMembersLowHp = false;
                                var partyElements = PartyElements.GetPlayerInfoElementList();

                                foreach (var partyMember in partyElements)
                                {
                                    if (partyMember != null)
                                    {
                                        // Get the actual player entity for detailed Life/ES info
                                        var playerEntity = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
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
                                                _instance.LogMessage($"REJUVENATION TOTEM: Raw values for {partyMember.PlayerName} - Life Current: {currentLife}, Life Max: {maxLife}, ES Current: {currentES}, ES Max: {maxES}");

                                                // Calculate meaningful thresholds - only consider ES if they have a meaningful amount
                                                var hasMeaningfulES = maxES >= 500; // Only consider ES if they have 500+ max ES
                                                var effectiveCurrentES = hasMeaningfulES ? currentES : 0;
                                                var effectiveMaxES = hasMeaningfulES ? maxES : 0;

                                                // Calculate total pool percentage using unreserved life + meaningful ES
                                                var totalCurrent = currentLife + effectiveCurrentES;
                                                var totalMax = maxLife + effectiveMaxES;
                                                var totalPoolPercentage = totalMax > 0 ? ((double)totalCurrent / (double)totalMax) * 100 : 100;

                                                // Debug calculation
                                                _instance.LogMessage($"REJUVENATION TOTEM: Calculation for {partyMember.PlayerName} - Total Current: {totalCurrent}, Total Max: {totalMax}, Percentage: {totalPoolPercentage:F2}%");

                                                _instance.LogMessage($"REJUVENATION TOTEM: Party member {partyMember.PlayerName} - Life: {currentLife:F0}/{maxLife:F0}, ES: {currentES:F0}/{maxES:F0} ({(hasMeaningfulES ? "meaningful" : "negligible")}), Total Pool: {totalPoolPercentage:F1}%");

                                                if (totalPoolPercentage < _settings.rejuvenationTotemHpThreshold.Value)
                                                {
                                                    partyMembersLowHp = true;
                                                    _instance.LogMessage($"REJUVENATION TOTEM: Party member {partyMember.PlayerName} total pool below threshold ({totalPoolPercentage:F1}% < {_settings.rejuvenationTotemHpThreshold.Value}%) - placing totem");
                                                    break;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Fallback: Skip this party member if we can't get entity info
                                            _instance.LogMessage($"REJUVENATION TOTEM: Could not get entity info for party member {partyMember.PlayerName}, skipping");
                                        }
                                    }
                                }

                                // Check if we're within following distance of the leader
                                var withinFollowingDistance = true;
                                var leaderName = _settings.autoPilotLeader.Value;
                                _instance.LogMessage($"REJUVENATION TOTEM: Checking leader distance - Leader: '{leaderName}'");

                                if (!string.IsNullOrWhiteSpace(leaderName))
                                {
                                    var leaderPartyElement = partyElements
                                        .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                                            leaderName.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                    if (leaderPartyElement != null)
                                    {
                                        _instance.LogMessage($"REJUVENATION TOTEM: Found leader in party: {leaderPartyElement.PlayerName}");

                                        // Get distance to leader
                                        var leaderEntity = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                            .FirstOrDefault(x => x != null && x.IsValid && !x.IsHostile &&
                                                               string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                                               leaderName.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                        if (leaderEntity != null)
                                        {
                                            var distanceToLeader = Vector2.Distance(
                                                new Vector2(_instance.playerPosition.X, _instance.playerPosition.Y),
                                                new Vector2(leaderEntity.PosNum.X, leaderEntity.PosNum.Y));

                                            // Use the following distance setting
                                            withinFollowingDistance = distanceToLeader <= _settings.autoPilotPathfindingNodeDistance.Value;
                                            _instance.LogMessage($"REJUVENATION TOTEM: Distance to leader: {distanceToLeader:F1}, Max allowed: {_settings.autoPilotPathfindingNodeDistance.Value}, Within range: {withinFollowingDistance}");
                                        }
                                        else
                                        {
                                            _instance.LogMessage("REJUVENATION TOTEM: Could not find leader entity");
                                        }
                                    }
                                    else
                                    {
                                        _instance.LogMessage("REJUVENATION TOTEM: Leader not found in party list");
                                    }
                                }
                                else
                                {
                                    _instance.LogMessage("REJUVENATION TOTEM: No leader set, allowing totem placement");
                                }

                                // Debug logging for placement conditions
                                _instance.LogMessage($"REJUVENATION TOTEM: Placement check - Rare/Unique: {hasRareOrUniqueNearby}, Party low HP: {partyMembersLowHp}, Within distance: {withinFollowingDistance}");

                                // Place totem if conditions are met
                                if ((hasRareOrUniqueNearby || partyMembersLowHp) && withinFollowingDistance)
                                {
                                    _instance.LogMessage($"REJUVENATION TOTEM: 🔥 CONDITIONS MET - Rare/Unique: {hasRareOrUniqueNearby}, Party low HP: {partyMembersLowHp}, Within distance: {withinFollowingDistance}");

                                    // Check UI menu status
                                    var stashOpen = _instance.GameController.IngameState.IngameUi.StashElement.IsVisibleLocal;
                                    var npcDialogOpen = _instance.GameController.IngameState.IngameUi.NpcDialog.IsVisible;
                                    var sellWindowOpen = _instance.GameController.IngameState.IngameUi.SellWindow.IsVisible;
                                    var purchaseWindowOpen = _instance.GameController.IngameState.IngameUi.PurchaseWindow.IsVisible;
                                    var mapOpen = _instance.GameController.IngameState.IngameUi.Map.IsVisible;
                                    var menuWindowOpen = MenuWindow.IsOpened;

                                    // Additional potential blocking elements
                                    var inventoryOpen = _instance.GameController.IngameState.IngameUi.InventoryPanel.IsVisible;
                                    var skillTreeOpen = _instance.GameController.IngameState.IngameUi.TreePanel.IsVisible;

                                    _instance.LogMessage($"REJUVENATION TOTEM: UI Status - Stash: {stashOpen}, NPC: {npcDialogOpen}, Sell: {sellWindowOpen}, Purchase: {purchaseWindowOpen}, Map: {mapOpen}, Menu: {menuWindowOpen}, Inv: {inventoryOpen}, Tree: {skillTreeOpen}");

                                    // Check for blocking UI elements (map is non-obstructing in PoE)
                                    if (stashOpen || npcDialogOpen || sellWindowOpen || purchaseWindowOpen || menuWindowOpen || inventoryOpen || skillTreeOpen)
                                    {
                                        _instance.LogMessage($"REJUVENATION TOTEM: ❌ Skipping totem placement - blocking UI menu is open");
                                        return;
                                    }

                                    // Log if map is detected as open (for debugging) but don't block on it
                                    if (mapOpen)
                                    {
                                        _instance.LogMessage("REJUVENATION TOTEM: ℹ️ Map detected as open but proceeding (non-obstructing in PoE)");
                                    }

                                    _instance.LogMessage("REJUVENATION TOTEM: ✅ No UI menus detected, proceeding with placement");

                                    // Move cursor to screen center before placing totem
                                    var screenRect = _instance.GameController.Window.GetWindowRectangle();
                                    var screenCenter = new Vector2(screenRect.Width / 2, screenRect.Height / 2);
                                    Mouse.SetCursorPos(screenCenter);
                                    _instance.LogMessage($"REJUVENATION TOTEM: 🎯 Moved cursor to screen center: {screenCenter}");

                                    // Small delay to ensure mouse movement is registered
                                    System.Threading.Thread.Sleep(50);

                                    // Check if the skill is available and can be used
                                    if (skill.SkillSlotIndex < 0 || skill.SkillSlotIndex >= 12)
                                    {
                                        _instance.LogMessage($"REJUVENATION TOTEM: ❌ Invalid skill slot index: {skill.SkillSlotIndex}");
                                        return;
                                    }

                                    // Get the skill slot for the totem
                                    var skillSlot = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                                    _instance.LogMessage($"REJUVENATION TOTEM: 🎮 Using skill slot: {skillSlot}, SkillSlotIndex: {skill.SkillSlotIndex}");

                                    // Check if skill has charges available
                                    if (skill.RemainingUses <= 0 && skill.IsOnCooldown)
                                    {
                                        _instance.LogMessage($"REJUVENATION TOTEM: ❌ Skill unavailable - RemainingUses: {skill.RemainingUses}, IsOnCooldown: {skill.IsOnCooldown}");
                                        return;
                                    }

                                    _instance.LogMessage("REJUVENATION TOTEM: ✅ Skill is available, sending key press");

                                    // Place the totem
                                    Keyboard.KeyPress(skillSlot);
                                    _instance.LogMessage($"REJUVENATION TOTEM: 🎉 Key press sent to place totem using key: {skillSlot}");

                                    // Set cooldown to prevent spamming (2 seconds as requested)
                                    SkillInfo.rejuvenationTotem.Cooldown = 2000;
                                    _instance.LastTimeAny = DateTime.Now; // Update global cooldown like other skills
                                    _instance.LogMessage("REJUVENATION TOTEM: ⏰ Cooldown set to 2000ms (2 seconds) and global cooldown updated");

                                    _instance.LogMessage($"REJUVENATION TOTEM: ✨ TOTEM PLACED SUCCESSFULLY - Rare/Unique nearby: {hasRareOrUniqueNearby}, Party low HP: {partyMembersLowHp}, Within distance: {withinFollowingDistance}");
                                }
                                else
                                {
                                    if (!(hasRareOrUniqueNearby || partyMembersLowHp))
                                    {
                                        _instance.LogMessage($"REJUVENATION TOTEM: Conditions not met - No rare/unique enemies AND no party members need healing");
                                    }
                                    else if (!withinFollowingDistance)
                                    {
                                        _instance.LogMessage($"REJUVENATION TOTEM: Conditions not met - Too far from leader");
                                    }
                                    else
                                    {
                                        _instance.LogMessage($"REJUVENATION TOTEM: Conditions not met - Rare/Unique: {hasRareOrUniqueNearby}, Party low HP: {partyMembersLowHp}, Within distance: {withinFollowingDistance}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            _instance.LogMessage($"REJUVENATION TOTEM: ❌ Cooldown check FAILED (remaining: {SkillInfo.rejuvenationTotem.Cooldown:F0}ms), skipping totem");
                            return; // Exit early if on cooldown
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _instance.LogMessage($"REJUVENATION TOTEM: Exception occurred - {e.Message}");
            }
        }
    }
}
