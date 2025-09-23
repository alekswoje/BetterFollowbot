using System;
using System.Linq;
using System.Windows.Forms;
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
    internal class VaalSkills : ISkill
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public VaalSkills(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.vaalHasteEnabled || _settings.vaalDisciplineEnabled;

        public string SkillName => "Vaal Skills";

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

            // Block skill execution when game is not in foreground
            if (!_instance.GameController.IsForeGroundCache)
                return;

            // Block skill execution in towns
            if (_instance.GameController?.Area?.CurrentArea?.IsTown == true)
                return;

            // Handle Vaal Haste
            if (_settings.vaalHasteEnabled)
            {
                HandleVaalHaste();
            }

            // Handle Vaal Discipline
            if (_settings.vaalDisciplineEnabled)
            {
                HandleVaalDiscipline();
            }
        }

        private void HandleVaalHaste()
        {
            // Loop through all skills to find Vaal Haste skill
            foreach (var skill in _instance.skills)
            {
                if (skill.Id == SkillInfo.vaalHaste.Id)
                {
                    // Vaal skills use charges, not traditional cooldowns
                    if (!(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                    {
                        // Check if we don't already have the vaal haste buff
                        var hasVaalHasteBuff = _instance.Buffs.Exists(x => x.Name == "vaal_haste");

                        if (!hasVaalHasteBuff)
                        {
                            // Activate the skill
                            var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                            if (skillKey != default(Keys))
                            {
                                Keyboard.KeyPress(skillKey);
                            }
                        }
                    }
                }
            }
        }

        private void HandleVaalDiscipline()
        {
            // Loop through all skills to find Vaal Discipline skill
            foreach (var skill in _instance.skills)
            {
                if (skill.Id == SkillInfo.vaalDiscipline.Id)
                {
                    // Vaal skills use charges, not traditional cooldowns
                    if (!(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                    {
                        // Check if ES is below threshold for player or any party member
                        var playerEsPercentage = _instance.player.ESPercentage;
                        var threshold = (float)_settings.vaalDisciplineEsp / 100;
                        var esBelowThreshold = playerEsPercentage < threshold;

                        // Check party members' ES if player's ES is not below threshold
                        if (!esBelowThreshold)
                        {
                            var partyElements = PartyElements.GetPlayerInfoElementList();

                            foreach (var partyMember in partyElements)
                            {
                                if (partyMember != null)
                                {
                                    // Get the actual player entity for ES info
                                    var playerEntity = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
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

                                            if (partyEsPercentage < threshold)
                                            {
                                                esBelowThreshold = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (esBelowThreshold)
                        {
                            // Check if we don't already have the vaal discipline buff
                            var hasVaalDisciplineBuff = _instance.Buffs.Exists(x => x.Name == "vaal_discipline");

                            if (!hasVaalDisciplineBuff)
                            {
                                // Activate the skill
                                var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                            if (skillKey != default(Keys))
                            {
                                Keyboard.KeyPress(skillKey);
                            }
                            }
                        }
                    }
                }
            }
        }
    }
}
