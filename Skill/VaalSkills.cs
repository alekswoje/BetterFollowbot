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
                    _instance.LogMessage($"VAAL HASTE: Vaal Haste skill detected - ID: {skill.Id}, Name: {skill.Name}");

                    // Vaal skills use charges, not traditional cooldowns
                    if (!(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                    {
                        // Check if we don't already have the vaal haste buff
                        var hasVaalHasteBuff = _instance.Buffs.Exists(x => x.Name == "vaal_haste");
                        _instance.LogMessage($"VAAL HASTE: Has vaal haste buff: {hasVaalHasteBuff}");

                        if (!hasVaalHasteBuff)
                        {
                            _instance.LogMessage("VAAL HASTE: No vaal haste buff found, activating");

                            // Activate the skill
                            Keyboard.KeyPress(_instance.GetSkillInputKey(skill.SkillSlotIndex));

                            _instance.LogMessage("VAAL HASTE: Vaal Haste activated successfully");
                        }
                        else
                        {
                            _instance.LogMessage("VAAL HASTE: Already have vaal haste buff, skipping");
                        }
                    }
                    else
                    {
                        _instance.LogMessage("VAAL HASTE: Cooldown check failed");
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
                    _instance.LogMessage($"VAAL DISCIPLINE: Vaal Discipline skill detected - ID: {skill.Id}, Name: {skill.Name}");

                    // Vaal skills use charges, not traditional cooldowns
                    if (!(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                    {
                        // Check if ES is below threshold for player or any party member
                        var playerEsPercentage = _instance.player.ESPercentage;
                        var threshold = (float)_settings.vaalDisciplineEsp / 100;
                        var esBelowThreshold = playerEsPercentage < threshold;

                        _instance.LogMessage($"VAAL DISCIPLINE: Player ES%: {playerEsPercentage:F1}, Threshold: {threshold:F2}");

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
                                            _instance.LogMessage($"VAAL DISCIPLINE: Party member {partyMember.PlayerName} ES%: {partyEsPercentage:F1}");

                                            if (partyEsPercentage < threshold)
                                            {
                                                esBelowThreshold = true;
                                                _instance.LogMessage($"VAAL DISCIPLINE: Party member {partyMember.PlayerName} ES below threshold");
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Skip this party member if we can't get entity info
                                        _instance.LogMessage($"VAAL DISCIPLINE: Could not get entity info for party member {partyMember.PlayerName}, skipping");
                                    }
                                }
                            }
                        }

                        if (esBelowThreshold)
                        {
                            // Check if we don't already have the vaal discipline buff
                            var hasVaalDisciplineBuff = _instance.Buffs.Exists(x => x.Name == "vaal_discipline");
                            _instance.LogMessage($"VAAL DISCIPLINE: Has vaal discipline buff: {hasVaalDisciplineBuff}");

                            if (!hasVaalDisciplineBuff)
                            {
                                _instance.LogMessage("VAAL DISCIPLINE: ES below threshold and no buff found, activating");

                                // Activate the skill
                                Keyboard.KeyPress(_instance.GetSkillInputKey(skill.SkillSlotIndex));

                                _instance.LogMessage("VAAL DISCIPLINE: Vaal Discipline activated successfully");
                            }
                            else
                            {
                                _instance.LogMessage("VAAL DISCIPLINE: Already have vaal discipline buff, skipping");
                            }
                        }
                        else
                        {
                            _instance.LogMessage("VAAL DISCIPLINE: ES above threshold, skipping");
                        }
                    }
                    else
                    {
                        _instance.LogMessage("VAAL DISCIPLINE: Cooldown check failed");
                    }
                }
            }
        }
    }
}
