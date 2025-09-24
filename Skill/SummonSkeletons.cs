using System;
using System.Linq;
using System.Windows.Forms;
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

            // Block skill execution in towns
            if (_instance.GameController?.Area?.CurrentArea?.IsTown == true)
                return;

            // Block skill execution in hideouts (if setting is enabled)
            if (_instance.GameController?.Area?.CurrentArea?.IsHideout == true && _settings.disableSkillsInHideout)
                return;

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
                                        var skillKey = _instance.GetSkillInputKey(summonSkeletonsSkill.SkillSlotIndex);
                                        if (skillKey != default(Keys))
                                        {
                                            Keyboard.KeyPress(skillKey);
                                        }
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
