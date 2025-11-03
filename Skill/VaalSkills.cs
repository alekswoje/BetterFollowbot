using System;
using System.Linq;
using System.Windows.Forms;
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
    internal class VaalSkills : ISkill
    {
        private readonly BetterFollowbot _instance;
        private readonly BetterFollowbotSettings _settings;

        public VaalSkills(BetterFollowbot instance, BetterFollowbotSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.vaalHasteEnabled || _settings.vaalDisciplineEnabled;

        public string SkillName => "Vaal Skills";

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

            // Check individual skill cooldown
            if (!_instance.CanUseSkill("VaalSkills"))
                return;

            // Only use skills when within follow range of the leader
            if (!_instance.IsWithinFollowRange())
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
                    // Vaal skills - check if they can be used (includes charge validation)
                    if (skill.CanBeUsed)
                    {
                        // Check if we don't already have the vaal haste buff
                        var hasVaalHasteBuff = _instance.Buffs.Exists(x => x.Name == "vaal_haste");

                        if (!hasVaalHasteBuff)
                        {
                            // Activate the skill
                            var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                            if (skillKey != default(Keys))
                            {
                                Keyboard.KeyPress(skillKey);
                                _instance.RecordSkillUse("VaalSkills");
                                _instance.LogMessage($"VAAL HASTE: Used successfully");
                            }
                        }
                    }
                    else
                    {
                        // Log when we skip (occasionally to avoid spam)
                        var timeSinceLastLog = (DateTime.Now - _instance.LastTimeAny).TotalSeconds;
                        if (timeSinceLastLog > 30) // Log every 30 seconds
                        {
                            _instance.LogMessage($"VAAL HASTE: Skipped - cannot be used");
                            _instance.LastTimeAny = DateTime.Now;
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
                    // Vaal skills - check if they can be used (includes charge validation)
                    if (skill.CanBeUsed)
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
                                var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                                if (skillKey != default(Keys))
                                {
                                    Keyboard.KeyPress(skillKey);
                                    _instance.RecordSkillUse("VaalSkills");
                                    _instance.LogMessage($"VAAL DISCIPLINE: Used successfully (ES below {threshold:P0})");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Log when we skip (occasionally to avoid spam)
                        var timeSinceLastLog = (DateTime.Now - _instance.LastTimeAny).TotalSeconds;
                        if (timeSinceLastLog > 30) // Log every 30 seconds
                        {
                            _instance.LogMessage($"VAAL DISCIPLINE: Skipped - cannot be used");
                            _instance.LastTimeAny = DateTime.Now;
                        }
                    }
                }
            }
        }
    }
}
