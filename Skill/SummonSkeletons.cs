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

        // Throttling for repetitive logs to reduce spam
        private DateTime _lastExecuteLog = DateTime.MinValue;
        private DateTime _lastSummonLog = DateTime.MinValue;

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
                // Debug: Log when Summon Skeletons Execute is called (throttled to reduce spam)
                if ((DateTime.Now - _lastExecuteLog).TotalSeconds >= 3)
                {
                    _instance.LogMessage($"SUMMON SKELETONS: Execute called - Enabled: {_settings.summonSkeletonsEnabled.Value}, AutoPilot: {_autoPilot != null}, GCD: {_instance.Gcd()}");
                    _lastExecuteLog = DateTime.Now;
                }

                if (_settings.summonSkeletonsEnabled.Value && _instance.Gcd())
                {
                    // Check if we have a party leader to follow
                    var partyMembers = PartyElements.GetPlayerInfoElementList();
                    _instance.LogMessage($"PARTY: Checking for leader '{_settings.autoPilotLeader.Value}', Party members: {partyMembers.Count}");

                    var leaderPartyElement = partyMembers
                        .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                            _settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                    if (leaderPartyElement != null)
                    {
                        _instance.LogMessage($"PARTY: Found leader in party - Name: '{leaderPartyElement.PlayerName}'");
                    }
                    else
                    {
                        _instance.LogMessage("PARTY: Leader NOT found in party list");
                        // Debug all party members
                        foreach (var member in partyMembers)
                        {
                            _instance.LogMessage($"PARTY: Member - Name: '{member?.PlayerName}'");
                        }
                    }

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
                                        _instance.LogMessage($"SUMMON SKELETONS: Current: {skeletonCount}, Required: {_settings.summonSkeletonsMinCount.Value}, Distance to leader: {distanceToLeader:F1}");

                                        // Use the summon skeletons skill
                                        Keyboard.KeyPress(_instance.GetSkillInputKey(summonSkeletonsSkill.SkillSlotIndex));
                                        _instance.LastTimeAny = DateTime.Now; // Update global cooldown

                                        _instance.LogMessage("SUMMON SKELETONS: Summoned skeletons successfully");
                                    }
                                    else if (summonSkeletonsSkill == null)
                                    {
                                        _instance.LogMessage("SUMMON SKELETONS: Summon Skeletons skill not found in skill bar");
                                    }
                                    else if (!summonSkeletonsSkill.CanBeUsed)
                                    {
                                        _instance.LogMessage("SUMMON SKELETONS: Summon Skeletons skill is on cooldown or unavailable");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _instance.LogMessage($"SUMMON SKELETONS: Exception occurred - {e.Message}");
            }
        }
    }
}
