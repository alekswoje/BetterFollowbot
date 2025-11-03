using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BetterFollowbot;
using BetterFollowbot.Interfaces;
using BetterFollowbot.Core.Skills;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BetterFollowbot.Skills
{
    internal class Links : ISkill
    {
        private readonly BetterFollowbot _instance;
        private readonly BetterFollowbotSettings _settings;
        private readonly System.Collections.Generic.Dictionary<string, DateTime> _lastLinkTime;

        public Links(BetterFollowbot instance, BetterFollowbotSettings settings)
        {
            _instance = instance;
            _settings = settings;
            _lastLinkTime = new System.Collections.Generic.Dictionary<string, DateTime>();
        }

        public bool IsEnabled => _settings.linksEnabled || _settings.flameLinkEnabled || _settings.protectiveLinkEnabled;

        public string SkillName => "Links";

        /// <summary>
        /// Checks if any blocking UI elements are open that should prevent skill execution
        /// </summary>
        private bool IsBlockingUiOpen()
        {
            return UIBlockingUtility.IsAnyBlockingUIOpen();
        }

        public void Execute()
        {
            if (IsBlockingUiOpen()) return;
            if (!_instance.GameController.IsForeGroundCache) return;
            if (_instance.GameController?.Area?.CurrentArea?.IsTown == true) return;
            if (_instance.GameController?.Area?.CurrentArea?.IsHideout == true && _settings.disableSkillsInHideout) return;
            if (!_instance.CanUseSkill("Links")) return;
            
            // Only use skills when within follow range of the leader
            if (!_instance.IsWithinFollowRange()) return;

            if (_settings.flameLinkEnabled)
            {
                ProcessLinkSkill(SkillInfo.flameLink, "flame_link_target", "flame_link");
            }

            if (_settings.protectiveLinkEnabled)
            {
                ProcessLinkSkill(SkillInfo.protectiveLink, "bulwark_link_target", "protective_link");
            }
        }

        private void ProcessLinkSkill(Core.Skills.Skill linkSkill, string targetBuffName, string linkType)
        {
            var skill = _instance.skills.FirstOrDefault(s => s.Id == linkSkill.Id);
            if (skill == null) return;

            if (SkillInfo.ManageCooldown(linkSkill, skill))
            {
                var partyElements = PartyElements.GetPlayerInfoElementList();
                var playerEntities = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                    .Where(x => x != null && x.IsValid && !x.IsHostile)
                    .ToList();

                var linkSourceBuff = _instance.Buffs.FirstOrDefault(x => x.Name == linkSkill.BuffName);
                var linkSourceTimeLeft = linkSourceBuff?.Timer ?? 0;

                foreach (var partyElement in partyElements)
                {
                    if (partyElement?.PlayerName == null) continue;

                    var playerEntity = playerEntities
                        .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                            partyElement.PlayerName.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                    if (playerEntity != null)
                    {
                        partyElement.Data.PlayerEntity = playerEntity;

                        var playerBuffs = playerEntity.GetComponent<Buffs>()?.BuffsList ?? new System.Collections.Generic.List<Buff>();
                        var linkTargetBuff = playerBuffs.FirstOrDefault(x => x.Name == targetBuffName);

                        bool needsLinking = false;
                        string reason = "";

                        var timerKey = $"{partyElement.PlayerName}_{linkType}";
                        if (!_lastLinkTime.ContainsKey(timerKey))
                        {
                            _lastLinkTime[timerKey] = DateTime.MinValue;
                        }

                        var timeSinceLastLink = (DateTime.Now - _lastLinkTime[timerKey]).TotalSeconds;

                        if (linkTargetBuff == null)
                        {
                            needsLinking = true;
                            reason = "no buff";
                        }
                        else if (linkTargetBuff.Timer < 5)
                        {
                            needsLinking = true;
                            reason = $"buff low ({linkTargetBuff.Timer:F1}s)";
                        }
                        else if (timeSinceLastLink >= 4)
                        {
                            needsLinking = true;
                            reason = $"refresh ({timeSinceLastLink:F1}s since last link)";
                        }

                        if (needsLinking)
                        {
                            var mouseScreenPos = _instance.GetMousePosition();
                            var targetScreenPos = Helper.WorldToValidScreenPosition(playerEntity.Pos);
                            var distanceToCursor = Vector2.Distance(mouseScreenPos, targetScreenPos);

                            var maxDistance = 150;
                            var canLink = (linkSourceBuff == null || distanceToCursor < maxDistance) &&
                                          (linkSourceTimeLeft > 2 || linkSourceBuff == null);

                            if (canLink)
                            {
                                var targetScreenPosForMouse = _instance.GameController.IngameState.Camera.WorldToScreen(playerEntity.Pos);
                                Mouse.SetCursorPos(targetScreenPosForMouse);

                                var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                                if (skillKey != default(Keys))
                                {
                                    Keyboard.KeyPress(skillKey);
                                    _instance.RecordSkillUse("Links");
                                }

                                _lastLinkTime[timerKey] = DateTime.Now;
                                linkSkill.Cooldown = 100;

                                _instance.LogMessage($"{linkType.ToUpper()}: Linked to {partyElement.PlayerName} ({reason}, Distance: {distanceToCursor:F1})");
                                return;
                            }
                        }
                    }
                }

                var currentPartyNames = partyElements
                    .Where(x => x?.PlayerName != null)
                    .Select(x => x.PlayerName)
                    .ToList();

                var keysToRemove = _lastLinkTime.Keys
                    .Where(key => key.EndsWith($"_{linkType}") && !currentPartyNames.Any(name => key.StartsWith($"{name}_")))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _lastLinkTime.Remove(key);
                }
            }
        }
    }
}
