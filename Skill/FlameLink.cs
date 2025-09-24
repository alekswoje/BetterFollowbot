using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BetterFollowbotLite;
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
    internal class FlameLink : ISkill
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;
        private readonly System.Collections.Generic.Dictionary<string, DateTime> _lastLinkTime;

        public FlameLink(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
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

                // Check for ExileCore overlay/menu activity (when user is interacting with the plugin GUI)
                // If mouse cursor is not over the game window or if we're not in foreground, block
                var gameWindowRect = _instance.GameController?.Window?.GetWindowRectangleTimeCache;
                var mousePos = _instance.GetMousePosition();

                // If mouse is outside game window bounds, user is likely in ExileCore overlay
                var mouseOutsideGame = gameWindowRect == null ||
                    mousePos.X < gameWindowRect.Value.X || mousePos.X > gameWindowRect.Value.X + gameWindowRect.Value.Width ||
                    mousePos.Y < gameWindowRect.Value.Y || mousePos.Y > gameWindowRect.Value.Y + gameWindowRect.Value.Height;

                // If not in foreground, definitely block (covers overlay scenarios)
                var notInForeground = !_instance.GameController.IsForeGroundCache;

                // If chat is open (user might be typing), block
                var chatField = _instance.GameController?.IngameState?.IngameUi?.ChatPanel?.ChatInputElement?.IsVisible;
                var chatOpen = chatField != null && (bool)chatField;

                // Note: Map is non-obstructing in PoE, so we don't check it
                return stashOpen || npcDialogOpen || sellWindowOpen || purchaseWindowOpen ||
                       inventoryOpen || skillTreeOpen || atlasOpen || mouseOutsideGame ||
                       notInForeground || chatOpen;
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

            // Block skill execution in hideouts (if setting is enabled)
            if (_instance.GameController?.Area?.CurrentArea?.IsHideout == true && _settings.disableSkillsInHideout)
                return;

            // Process Flame Link if enabled
            if (_settings.flameLinkEnabled)
            {
                ProcessLinkSkill(SkillInfo.flameLink, "flame_link_target", "flame_link");
            }

            // Process Protective Link if enabled
            if (_settings.protectiveLinkEnabled)
            {
                ProcessLinkSkill(SkillInfo.protectiveLink, "bulwark_link_target", "protective_link");
            }
        }

        private void ProcessLinkSkill(Core.Skills.Skill linkSkill, string targetBuffName, string linkType)
        {
            // Find the skill in the player's skills
            var skill = _instance.skills.FirstOrDefault(s => s.Id == linkSkill.Id);
            if (skill == null) return;

            if (SkillInfo.ManageCooldown(linkSkill, skill))
            {
                // Get all party members
                var partyElements = PartyElements.GetPlayerInfoElementList();

                // Get player entities for all party members
                var playerEntities = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                    .Where(x => x != null && x.IsValid && !x.IsHostile)
                    .ToList();

                // Check if we have the source buff and its timer
                var linkSourceBuff = _instance.Buffs.FirstOrDefault(x => x.Name == linkSkill.BuffName);
                var linkSourceTimeLeft = linkSourceBuff?.Timer ?? 0;

                // Process ALL party members that need linking
                foreach (var partyElement in partyElements)
                {
                    if (partyElement?.PlayerName == null) continue;

                    // Find the corresponding player entity
                    var playerEntity = playerEntities
                        .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                            partyElement.PlayerName.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                    if (playerEntity != null)
                    {
                        // Set the player entity for reference
                        partyElement.Data.PlayerEntity = playerEntity;

                        var playerBuffs = playerEntity.GetComponent<Buffs>()?.BuffsList ?? new System.Collections.Generic.List<Buff>();
                        var linkTargetBuff = playerBuffs.FirstOrDefault(x => x.Name == targetBuffName);

                        // Check if this party member needs linking
                        bool needsLinking = false;
                        string reason = "";

                        // Check our internal timer for this party member
                        var timerKey = $"{partyElement.PlayerName}_{linkType}";
                        if (!_lastLinkTime.ContainsKey(timerKey))
                        {
                            _lastLinkTime[timerKey] = DateTime.MinValue;
                        }

                        var timeSinceLastLink = (DateTime.Now - _lastLinkTime[timerKey]).TotalSeconds;

                        if (linkTargetBuff == null)
                        {
                            // No buff at all - needs linking
                            needsLinking = true;
                            reason = "no buff";
                        }
                        else if (linkTargetBuff.Timer < 5)
                        {
                            // Buff expiring soon - needs linking
                            needsLinking = true;
                            reason = $"buff low ({linkTargetBuff.Timer:F1}s)";
                        }
                        else if (timeSinceLastLink >= 4)
                        {
                            // Time-based refresh every 4 seconds
                            needsLinking = true;
                            reason = $"refresh ({timeSinceLastLink:F1}s since last link)";
                        }

                        if (needsLinking)
                        {
                            // Check distance from target to mouse cursor in screen space
                            var mouseScreenPos = _instance.GetMousePosition();
                            var targetScreenPos = Helper.WorldToValidScreenPosition(playerEntity.Pos);
                            var distanceToCursor = Vector2.Distance(mouseScreenPos, targetScreenPos);

                            // Allow linking within reasonable distance
                            var maxDistance = 150; // Allow longer distance for maintenance linking
                            var canLink = (linkSourceBuff == null || distanceToCursor < maxDistance) &&
                                          (linkSourceTimeLeft > 2 || linkSourceBuff == null); // Don't link if our source buff is about to expire

                            if (canLink)
                            {
                                // Move mouse to target position
                                var targetScreenPosForMouse = _instance.GameController.IngameState.Camera.WorldToScreen(playerEntity.Pos);
                                Mouse.SetCursorPos(targetScreenPosForMouse);

                                // Activate the skill
                                var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                                if (skillKey != default(Keys))
                                {
                                    Keyboard.KeyPress(skillKey);
                                }

                                // Update our internal timer
                                _lastLinkTime[timerKey] = DateTime.Now;

                                linkSkill.Cooldown = 100;

                                _instance.LogMessage($"{linkType.ToUpper()}: Linked to {partyElement.PlayerName} ({reason}, Distance: {distanceToCursor:F1})");
                                return; // Exit after linking to one player
                            }
                        }
                    }
                }

                // Clean up old entries from the timer dictionary (party members who left)
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
