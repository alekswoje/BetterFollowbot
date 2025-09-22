using System;
using System.Linq;
using BetterFollowbotLite.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BetterFollowbotLite.Skills
{
    internal class SummonSkeletons : ISkill
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;
        private readonly AutoPilot _autoPilot;
        private readonly Summons _summons;


        public SummonSkeletons(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings,
                              AutoPilot autoPilot, Summons summons)
        {
            _instance = instance;
            _settings = settings;
            _autoPilot = autoPilot;
            _summons = summons;
        }

        public bool IsEnabled => _settings.summonSkeletonsEnabled.Value;

        public string SkillName => "Summon Skeletons";

        public void Execute()
        {
            try
            {
                if (_settings.summonSkeletonsEnabled.Value && _instance.Gcd())
                {
                    // Check if we have a party leader to follow
                    var partyMembers = PartyElements.GetPlayerInfoElementList();

                    var leaderPartyElement = partyMembers
                        .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                            _settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                    if (leaderPartyElement != null)
                    {
                        // Find the actual leader entity
                        var playerEntities = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                            .Where(x => x != null && x.IsValid && !x.IsHostile);

                        var leaderEntity = playerEntities
                            .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                _settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                        if (leaderEntity != null)
                        {
                            // Check distance to leader
                            var distanceToLeader = Vector3.Distance(_instance.playerPosition, leaderEntity.Pos);

                            // Only summon if within range
                            if (distanceToLeader <= _settings.summonSkeletonsRange.Value)
                            {
                                // Count current skeletons
                                var skeletonCount = Summons.GetSkeletonCount();

                                // Summon if we have less than the minimum required
                                if (skeletonCount < _settings.summonSkeletonsMinCount.Value)
                                {
                                    // Find the summon skeletons skill
                                    var summonSkeletonsSkill = _instance.skills.FirstOrDefault(s =>
                                        s.Name.Contains("Summon Skeletons") ||
                                        s.Name.Contains("summon") && s.Name.Contains("skeleton"));

                                    if (summonSkeletonsSkill != null && summonSkeletonsSkill.IsOnSkillBar && summonSkeletonsSkill.CanBeUsed)
                                    {
                                        // Use the summon skeletons skill
                                        Keyboard.KeyPress(_instance.GetSkillInputKey(summonSkeletonsSkill.SkillSlotIndex));
                                        _instance.LastTimeAny = DateTime.Now; // Update global cooldown
                                    }
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
