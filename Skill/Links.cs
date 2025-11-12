using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BetterFollowbot;
using BetterFollowbot.Interfaces;
using BetterFollowbot.Core.Skills;
using BetterFollowbot.Core.TaskManagement;
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

        public bool IsEnabled => _settings.linksEnabled;

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
            
            // Don't use global skill cooldown - let links cast whenever buffs are needed
            // Only check GCD to prevent skill spam
            if (!_instance.Gcd()) return;
            
            // Only use skills when within follow range of the leader
            if (!_instance.IsWithinFollowRange()) return;

            // Auto-detect and use any available link skills
            // These will only cast if buffs are actually needed (checked inside ProcessLinkSkill)
            if (SkillInfo.flameLink.Id > 0)
            {
                ProcessLinkSkill(SkillInfo.flameLink, "flame_link_target", "flame_link");
            }

            if (SkillInfo.protectiveLink.Id > 0)
            {
                ProcessLinkSkill(SkillInfo.protectiveLink, "bulwark_link_target", "protective_link");
            }

            if (SkillInfo.destructiveLink.Id > 0)
            {
                ProcessLinkSkill(SkillInfo.destructiveLink, "destructive_link_target", "destructive_link");
            }

            if (SkillInfo.soulLink.Id > 0)
            {
                ProcessLinkSkill(SkillInfo.soulLink, "soul_link_target", "soul_link");
            }
        }

        /// <summary>
        /// NEW: Task-based skill execution
        /// Creates skill tasks for links instead of executing immediately
        /// </summary>
        public List<TaskNode> CreateSkillTasks()
        {
            var tasks = new List<TaskNode>();
            
            // Only create tasks if we're within follow range
            if (!_instance.IsWithinFollowRange())
                return tasks;
            
            // Auto-detect and create tasks for any available link skills
            if (SkillInfo.flameLink.Id > 0)
            {
                var flameLinkTasks = CreateLinkTasks(SkillInfo.flameLink, "flame_link_target", "flame_link", TaskNodeType.FlameLink);
                tasks.AddRange(flameLinkTasks);
            }
            
            if (SkillInfo.protectiveLink.Id > 0)
            {
                var protectiveLinkTasks = CreateLinkTasks(SkillInfo.protectiveLink, "bulwark_link_target", "protective_link", TaskNodeType.ProtectiveLink);
                tasks.AddRange(protectiveLinkTasks);
            }
            
            if (SkillInfo.destructiveLink.Id > 0)
            {
                var destructiveLinkTasks = CreateLinkTasks(SkillInfo.destructiveLink, "destructive_link_target", "destructive_link", TaskNodeType.DestructiveLink);
                tasks.AddRange(destructiveLinkTasks);
            }
            
            if (SkillInfo.soulLink.Id > 0)
            {
                var soulLinkTasks = CreateLinkTasks(SkillInfo.soulLink, "soul_link_target", "soul_link", TaskNodeType.SoulLink);
                tasks.AddRange(soulLinkTasks);
            }
            
            return tasks;
        }

        /// <summary>
        /// Creates link tasks for a specific link type
        /// </summary>
        private List<TaskNode> CreateLinkTasks(Core.Skills.Skill linkSkill, string targetBuffName, string linkType, TaskNodeType taskType)
        {
            var tasks = new List<TaskNode>();
            
            var skill = _instance.skills.FirstOrDefault(s => s.Id == linkSkill.Id);
            if (skill == null) return tasks;

            if (!SkillInfo.ManageCooldown(linkSkill, skill))
                return tasks;

            var partyElements = PartyElements.GetPlayerInfoElementList();
            var playerEntities = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                .Where(x => x != null && x.IsValid && !x.IsHostile)
                .ToList();

            foreach (var partyElement in partyElements)
            {
                if (partyElement?.PlayerName == null) continue;

                var playerEntity = playerEntities
                    .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                        partyElement.PlayerName.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                if (playerEntity != null)
                {
                    var linkSourceBuff = _instance.Buffs.FirstOrDefault(x => x.Name == linkSkill.BuffName);
                    var linkSourceTimeLeft = linkSourceBuff?.Timer ?? 0;
                    
                    // Always try to link when near leader - cast continuously
                    var mouseScreenPos = _instance.GetMousePosition();
                    var targetScreenPos = Helper.WorldToValidScreenPosition(playerEntity.Pos);
                    var distanceToCursor = Vector2.Distance(mouseScreenPos, targetScreenPos);

                    var maxDistance = 150;
                    var canLink = (linkSourceBuff == null || distanceToCursor < maxDistance) &&
                                  (linkSourceTimeLeft > 2 || linkSourceBuff == null);

                    if (canLink)
                    {
                        // Create a task to cast link on this party member
                        var linkTask = new TaskNode(playerEntity.Pos, 0, taskType)
                        {
                            SkillName = linkType,
                            TargetEntity = playerEntity,
                            SkillSlotIndex = skill.SkillSlotIndex,
                            SkillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex),
                            SkillData = new SkillExecutionData
                            {
                                TargetPlayerName = partyElement.PlayerName,
                                Reason = "auto-link",
                                DistanceToTarget = distanceToCursor,
                                TimeSinceLastUse = 0
                            }
                        };
                        
                        tasks.Add(linkTask);
                        _instance.LogMessage($"SKILL TASK CREATED: {linkType.ToUpper()} task for {partyElement.PlayerName} (auto-link, Distance: {distanceToCursor:F1})");
                        
                        // Only create one link task per update to avoid flooding the queue
                        break;
                    }
                }
            }

            return tasks;
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

                        // Always try to link when near leader - cast continuously
                        var mouseScreenPos = _instance.GetMousePosition();
                        var targetScreenPos = Helper.WorldToValidScreenPosition(playerEntity.Pos);
                        var distanceToCursor = Vector2.Distance(mouseScreenPos, targetScreenPos);

                        var maxDistance = 150;
                        var canLink = (linkSourceBuff == null || distanceToCursor < maxDistance) &&
                                      (linkSourceTimeLeft > 2 || linkSourceBuff == null);

                        if (canLink)
                        {
                            // Add randomness to cursor position and timing for more human-like behavior
                            var random = new Random();
                            var targetScreenPosForMouse = _instance.GameController.IngameState.Camera.WorldToScreen(playerEntity.Pos);
                            
                            // Add random offset to cursor position (±10 pixels)
                            var randomOffsetX = (float)(random.NextDouble() * 20 - 10);
                            var randomOffsetY = (float)(random.NextDouble() * 20 - 10);
                            var randomizedPos = new Vector2(
                                targetScreenPosForMouse.X + randomOffsetX,
                                targetScreenPosForMouse.Y + randomOffsetY
                            );
                            
                            Mouse.SetCursorPos(randomizedPos);
                            
                            // Random delay before casting (50-150ms)
                            var randomDelay = random.Next(50, 150);
                            System.Threading.Thread.Sleep(randomDelay);

                            var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                            if (skillKey != default(Keys))
                            {
                                Keyboard.KeyPressRandom(skillKey);
                                _instance.RecordSkillUse("Links");
                            }

                            var timerKey = $"{partyElement.PlayerName}_{linkType}";
                            _lastLinkTime[timerKey] = DateTime.Now;
                            linkSkill.Cooldown = 1000; // 1 second cooldown between link casts

                            _instance.LogMessage($"{linkType.ToUpper()}: Linked to {partyElement.PlayerName} (auto-link, Distance: {distanceToCursor:F1}, Delay: {randomDelay}ms)");
                            return;
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
