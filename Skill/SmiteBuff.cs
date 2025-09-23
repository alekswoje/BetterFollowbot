using System;
using System.Linq;
using System.Windows.Forms;
using BetterFollowbotLite.Interfaces;
using BetterFollowbotLite.Core.Skills;
using BetterFollowbotLite.Core.TaskManagement;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using GameOffsets.Native;
using SharpDX;

namespace BetterFollowbotLite.Skills
{
    internal class SmiteBuff : ISkill
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public SmiteBuff(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.smiteEnabled;

        public string SkillName => "Smite Buff";

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

            // Loop through all skills to find Smite skill
            foreach (var skill in _instance.skills)
            {
                if (skill.Id == SkillInfo.smite.Id)
                {
                    // Less restrictive cooldown for smite buff (reduced from 100ms to 25ms)
                    if (SkillInfo.smite.Cooldown <= 0 &&
                        !(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                    {
                        // Check mana cost
                        if (!skill.Stats.TryGetValue(GameStat.ManaCost, out var manaCost))
                            manaCost = 0;

                        if (_instance.player.CurMana >= manaCost ||
                            (_instance.localPlayer.Stats.TryGetValue(GameStat.VirtualEnergyShieldProtectsMana, out var hasEldritchBattery) &&
                             hasEldritchBattery > 0 && (_instance.player.CurES + _instance.player.CurMana) >= manaCost))
                        {
                            // Check if we don't have the smite buff or it's about to expire
                            var smiteBuff = _instance.Buffs.FirstOrDefault(x => x.Name == "smite_buff");
                            var hasSmiteBuff = smiteBuff != null;
                            var buffTimeLeft = smiteBuff?.Timer ?? 0;

                            // More aggressive buff refresh: refresh if no buff OR buff has less than 4 seconds left (increased from 2)
                            if (!hasSmiteBuff || buffTimeLeft < 4.0f)
                            {
                                // Smite is a buff skill - just cast it without needing specific monster targets
                                // The buff will automatically apply to nearby enemies
                                var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                                if (skillKey != default(Keys))
                                {
                                    Keyboard.KeyPress(skillKey);
                                    // Reduced cooldown from 100ms to 25ms for more frequent casting
                                    SkillInfo.smite.Cooldown = 25;
                                    _instance.LastTimeAny = DateTime.Now; // Update global cooldown

                                    _instance.LogMessage($"SMITE: Cast successfully (Buff: {hasSmiteBuff}, TimeLeft: {buffTimeLeft:F1}s)");
                                }
                            }
                        }
                    }
                }
            }
        }

    }
}
