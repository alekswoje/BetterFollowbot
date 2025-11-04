using System;
using System.Linq;
using System.Windows.Forms;
using BetterFollowbot.Interfaces;
using BetterFollowbot.Core.Skills;
using BetterFollowbot.Core.TaskManagement;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using GameOffsets.Native;
using SharpDX;

namespace BetterFollowbot.Skills
{
    internal class SmiteBuff : ISkill
    {
        private readonly BetterFollowbot _instance;
        private readonly BetterFollowbotSettings _settings;

        public SmiteBuff(BetterFollowbot instance, BetterFollowbotSettings settings)
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
            _instance.LogMessage($"SMITE DEBUG: Execute() called - Enabled: {_settings.smiteEnabled.Value}");
            
            // Block skill execution when blocking UI is open
            if (IsBlockingUiOpen())
            {
                _instance.LogMessage("SMITE DEBUG: Blocked by IsBlockingUiOpen()");
                return;
            }

            // Block skill execution in towns
            if (_instance.GameController?.Area?.CurrentArea?.IsTown == true)
            {
                _instance.LogMessage("SMITE DEBUG: Blocked by IsTown check");
                return;
            }

            // Block skill execution in hideouts (if setting is enabled)
            if (_instance.GameController?.Area?.CurrentArea?.IsHideout == true && _settings.disableSkillsInHideout)
            {
                _instance.LogMessage("SMITE DEBUG: Blocked by IsHideout check");
                return;
            }

            // Check individual skill cooldown
            if (!_instance.CanUseSkill("SmiteBuff"))
            {
                _instance.LogMessage("SMITE DEBUG: Blocked by CanUseSkill() check");
                return;
            }

            // Only use skills when within follow range of the leader
            if (!_instance.IsWithinFollowRange())
            {
                _instance.LogMessage("SMITE DEBUG: Blocked by IsWithinFollowRange() check");
                return;
            }

            _instance.LogMessage($"SMITE DEBUG: Passed all checks, looking for Smite skill (SkillInfo.smite.Id: {SkillInfo.smite.Id})");
            _instance.LogMessage($"SMITE DEBUG: Total skills count: {_instance.skills.Count}");

            // Loop through all skills to find Smite skill
            foreach (var skill in _instance.skills)
            {
                _instance.LogMessage($"SMITE DEBUG: Checking skill - Id: {skill.Id}, Name: {skill.Name}, InternalName: {skill.InternalName}");
                
                if (skill.Id == SkillInfo.smite.Id)
                {
                    _instance.LogMessage($"SMITE DEBUG: Found Smite skill! (Id: {skill.Id}, Name: {skill.Name})");
                    _instance.LogMessage($"SMITE DEBUG: SkillInfo.smite.Cooldown: {SkillInfo.smite.Cooldown}");
                    _instance.LogMessage($"SMITE DEBUG: skill.RemainingUses: {skill.RemainingUses}, skill.IsOnCooldown: {skill.IsOnCooldown}");
                    
                    // Less restrictive cooldown for smite buff (reduced from 100ms to 25ms)
                    if (SkillInfo.smite.Cooldown <= 0 &&
                        !(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                    {
                        _instance.LogMessage("SMITE DEBUG: Passed cooldown check");
                        
                        // Check mana cost
                        if (!skill.Stats.TryGetValue(GameStat.ManaCost, out var manaCost))
                            manaCost = 0;

                        _instance.LogMessage($"SMITE DEBUG: Mana cost: {manaCost}, Current mana: {_instance.player.CurMana}, Current ES: {_instance.player.CurES}");
                        
                        var hasEldritchBattery = _instance.localPlayer.Stats.TryGetValue(GameStat.VirtualEnergyShieldProtectsMana, out var ebValue) && ebValue > 0;
                        _instance.LogMessage($"SMITE DEBUG: Has Eldritch Battery: {hasEldritchBattery}");

                        if (_instance.player.CurMana >= manaCost ||
                            (hasEldritchBattery && (_instance.player.CurES + _instance.player.CurMana) >= manaCost))
                        {
                            _instance.LogMessage("SMITE DEBUG: Passed mana check");
                            
                            // Check if we don't have the smite buff or it's about to expire
                            var smiteBuff = _instance.Buffs.FirstOrDefault(x => x.Name == "smite_buff");
                            var hasSmiteBuff = smiteBuff != null;
                            var buffTimeLeft = smiteBuff?.Timer ?? 0;

                            _instance.LogMessage($"SMITE DEBUG: Buff status - HasBuff: {hasSmiteBuff}, TimeLeft: {buffTimeLeft:F1}s");
                            _instance.LogMessage($"SMITE DEBUG: Current buffs: {string.Join(", ", _instance.Buffs.Select(b => b.Name))}");

                            // More aggressive buff refresh: refresh if no buff OR buff has less than 4 seconds left (increased from 2)
                            if (!hasSmiteBuff || buffTimeLeft < 4.0f)
                            {
                                _instance.LogMessage("SMITE DEBUG: Need to cast Smite (no buff or expiring soon)");
                                
                                // Smite is a buff skill - just cast it without needing specific monster targets
                                // The buff will automatically apply to nearby enemies
                                var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                                _instance.LogMessage($"SMITE DEBUG: Skill slot index: {skill.SkillSlotIndex}, Key: {skillKey}");
                                
                                if (skillKey != default(Keys))
                                {
                                    _instance.LogMessage($"SMITE DEBUG: Pressing key {skillKey}");
                                    Keyboard.KeyPress(skillKey);
                                    _instance.RecordSkillUse("SmiteBuff");
                                    // Reduced cooldown from 100ms to 25ms for more frequent casting
                                    SkillInfo.smite.Cooldown = 25;
                                    _instance.LastTimeAny = DateTime.Now; // Update global cooldown

                                    _instance.LogMessage($"SMITE: Cast successfully (Buff: {hasSmiteBuff}, TimeLeft: {buffTimeLeft:F1}s)");
                                }
                                else
                                {
                                    _instance.LogMessage("SMITE DEBUG: ERROR - Skill key is default/invalid!");
                                }
                            }
                            else
                            {
                                _instance.LogMessage($"SMITE DEBUG: Buff still active ({buffTimeLeft:F1}s left), not casting");
                            }
                        }
                        else
                        {
                            _instance.LogMessage("SMITE DEBUG: BLOCKED - Not enough mana!");
                        }
                    }
                    else
                    {
                        _instance.LogMessage("SMITE DEBUG: BLOCKED - Skill on cooldown or no uses remaining");
                    }
                    
                    break; // Found the skill, no need to continue
                }
            }
            
            if (SkillInfo.smite.Id == 0)
            {
                _instance.LogMessage("SMITE DEBUG: WARNING - SkillInfo.smite.Id is 0! Smite skill not detected during initialization.");
            }
        }

    }
}
