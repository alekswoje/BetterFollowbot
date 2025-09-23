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

        public void Execute()
        {
            // Block skill execution when menu is open
            if (MenuWindow.IsOpened)
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
                            Keyboard.KeyPress(_instance.GetSkillInputKey(skill.SkillSlotIndex));
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
                                Keyboard.KeyPress(_instance.GetSkillInputKey(skill.SkillSlotIndex));
                            }
                        }
                    }
                }
            }
        }
    }
}
