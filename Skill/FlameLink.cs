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
                        // Get party leader
                        var partyElements = PartyElements.GetPlayerInfoElementList();

                        var leaderPartyElement = partyElements
                            .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                                _settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                        if (leaderPartyElement != null)
                        {
                            // Find the actual player entity by name
                            var playerEntities = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                .Where(x => x != null && x.IsValid && !x.IsHostile);

                            var leaderEntity = playerEntities
                                .FirstOrDefault(x => string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                    _settings.autoPilotLeader.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                            if (leaderEntity != null)
                            {
                                // Set the player entity
                                leaderPartyElement.Data.PlayerEntity = leaderEntity;

                                var leader = leaderPartyElement.Data.PlayerEntity;
                                var leaderBuffs = leader.GetComponent<Buffs>().BuffsList;

                                // Check if leader has the target buff
                                var hasLinkTarget = leaderBuffs.Exists(x => x.Name == targetBuffName);

                                // Check if we have the source buff and its timer
                                var linkSourceBuff = _instance.Buffs.FirstOrDefault(x => x.Name == linkSkill.BuffName);
                                var linkSourceTimeLeft = linkSourceBuff?.Timer ?? 0;

                                // Check distance from leader to mouse cursor in screen space
                                var mouseScreenPos = _instance.GetMousePosition();
                                var leaderScreenPos = Helper.WorldToValidScreenPosition(leader.Pos);
                                var distanceToCursor = Vector2.Distance(mouseScreenPos, leaderScreenPos);

                                // Logic: Aggressive flame link maintenance - refresh much earlier and with larger distance tolerance
                                // Emergency linking (no source buff): ignore distance
                                // Normal linking: use distance check
                                var shouldActivate = (!hasLinkTarget || linkSourceTimeLeft < 8 || linkSourceBuff == null) &&
                                                     (linkSourceBuff == null || distanceToCursor < 100);

                                if (shouldActivate)
                                {
                                    // Move mouse to leader position
                                    var leaderScreenPosForMouse = _instance.GameController.IngameState.Camera.WorldToScreen(leader.Pos);
                                    Mouse.SetCursorPos(leaderScreenPosForMouse);

                                    // Activate the skill
                                    var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                                    if (skillKey != Keys.None)
                                    {
                                        Keyboard.KeyPress(skillKey);
                                    }
                                    linkSkill.Cooldown = 100;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
