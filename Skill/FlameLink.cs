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
    internal class FlameLink : ISkill
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public FlameLink(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.flameLinkEnabled;

        public string SkillName => "Flame Link";

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

            // Loop through all skills to find Flame Link skill
            foreach (var skill in _instance.skills)
            {
                if (skill.Id == SkillInfo.flameLink.Id)
                {
                    var linkSkill = SkillInfo.flameLink;
                    var targetBuffName = "flame_link_target";

                    if (SkillInfo.ManageCooldown(linkSkill, skill))
                    {
                        // Get all party members
                        var partyElements = PartyElements.GetPlayerInfoElementList();

                        // Get player entities for all party members
                        var playerEntities = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                            .Where(x => x != null && x.IsValid && !x.IsHostile)
                            .ToList();

                        // Find party members that need linking (no target buff or low timer)
                        var partyMembersNeedingLink = new List<(PartyElement partyElement, Entity playerEntity, float priority)>();

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

                                // Calculate priority (lower is better)
                                // Priority 1: No buff at all (most urgent)
                                // Priority 2: Buff timer < 5 seconds
                                // Priority 3: Buff timer < 10 seconds
                                // Priority 4+: Everything else
                                float priority = 10; // Default priority

                                if (linkTargetBuff == null)
                                {
                                    priority = 1; // Most urgent - no buff
                                }
                                else if (linkTargetBuff.Timer < 5)
                                {
                                    priority = 2; // Very urgent - buff expiring soon
                                }
                                else if (linkTargetBuff.Timer < 10)
                                {
                                    priority = 3; // Moderately urgent - buff low
                                }

                                partyMembersNeedingLink.Add((partyElement, playerEntity, priority));
                            }
                        }

                        // Sort by priority (lowest first) and distance (closest first)
                        var mouseScreenPos = _instance.GetMousePosition();
                        var bestTarget = partyMembersNeedingLink
                            .OrderBy(x => x.priority) // Priority first
                            .ThenBy(x =>
                            {
                                var screenPos = Helper.WorldToValidScreenPosition(x.playerEntity.Pos);
                                return Vector2.Distance(mouseScreenPos, screenPos);
                            }) // Then by distance
                            .FirstOrDefault();

                        if (bestTarget != default)
                        {
                            var targetPartyElement = bestTarget.partyElement;
                            var targetEntity = bestTarget.playerEntity;
                            var targetPriority = bestTarget.priority;

                            // Check if we have the source buff and its timer
                            var linkSourceBuff = _instance.Buffs.FirstOrDefault(x => x.Name == linkSkill.BuffName);
                            var linkSourceTimeLeft = linkSourceBuff?.Timer ?? 0;

                            // Check distance from target to mouse cursor in screen space
                            var targetScreenPos = Helper.WorldToValidScreenPosition(targetEntity.Pos);
                            var distanceToCursor = Vector2.Distance(mouseScreenPos, targetScreenPos);

                            // Logic: Aggressive flame link maintenance - refresh much earlier and with larger distance tolerance
                            // Emergency linking (no source buff): ignore distance
                            // Normal linking: use distance check
                            // Higher priority targets get more lenient distance checks
                            var maxDistance = targetPriority <= 2 ? 150 : 100; // More urgent targets allow longer distance
                            var shouldActivate = (linkSourceTimeLeft < 8 || linkSourceBuff == null) &&
                                                 (linkSourceBuff == null || distanceToCursor < maxDistance);

                            if (shouldActivate)
                            {
                                // Move mouse to target position
                                var targetScreenPosForMouse = _instance.GameController.IngameState.Camera.WorldToScreen(targetEntity.Pos);
                                Mouse.SetCursorPos(targetScreenPosForMouse);

                                // Activate the skill
                                    var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                                    if (skillKey != default(Keys))
                                    {
                                        Keyboard.KeyPress(skillKey);
                                    }
                                linkSkill.Cooldown = 100;

                                _instance.LogMessage($"FLAME LINK: Linked to {targetPartyElement.PlayerName} (Priority: {targetPriority}, Distance: {distanceToCursor:F1})");
                            }
                        }
                    }
                }
            }
        }
    }
}
