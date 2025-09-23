using System;
using System.Linq;
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
    internal class AuraBlessing : ISkill
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public AuraBlessing(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.auraBlessingEnabled;

        public string SkillName => "Aura Blessing";

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

            // Loop through all skills to find Holy Relic and Zealotry skills
            foreach (var skill in _instance.skills)
            {
                // Holy Relic summoning logic
                if (skill.Id == SkillInfo.holyRelict.Id)
                {
                    // Check cooldown to prevent double-spawning
                    if (SkillInfo.ManageCooldown(SkillInfo.holyRelict, skill))
                    {
                        var lowestMinionHp = Summons.GetLowestMinionHpp();
                        // Convert HP percentage from 0-1 range to 0-100 range for comparison
                        var lowestMinionHpPercent = lowestMinionHp * 100f;
                        // Check for Holy Relic minion presence
                        // Prioritize ReAgent buff names, then check for other indicators
                        // Note: Avoid "guardian_life_regen" as it's just the life regen effect, not minion presence
                        var hasGuardianBlessingMinion = _instance.Buffs.Exists(x =>
                            x.Name == "has_guardians_blessing_minion" ||
                            (x.Name.Contains("holy") && x.Name.Contains("relic") && !x.Name.Contains("life")) ||
                            x.Name.Contains("guardian_blessing_minion"));

                        // Check conditions
                        var healthLow = lowestMinionHpPercent < _settings.holyRelicHealthThreshold;
                        var missingBuff = !hasGuardianBlessingMinion;

                        // If Holy Relic health is below threshold OR we don't have any minion buff, summon new Holy Relic
                        if (healthLow || missingBuff)
                        {
                            var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                            if (skillKey != Keys.None)
                            {
                                Keyboard.KeyPress(skillKey);
                            }
                            SkillInfo.holyRelict.Cooldown = 200; // 2 second cooldown to prevent double-spawning
                        }
                    }
                }

                // Zealotry casting logic
                else if (skill.Id == SkillInfo.auraZealotry.Id)
                {
                    // Check for Zealotry aura buff
                    // Prioritize ReAgent buff names, then check for aura effects
                    var hasGuardianBlessingAura = _instance.Buffs.Exists(x =>
                        x.Name == "has_guardians_blessing_aura" ||
                        x.Name == "zealotry" ||
                        x.Name == "player_aura_spell_damage" ||
                        (x.Name.Contains("blessing") && x.Name.Contains("aura")));

                    // Check for Holy Relic minion presence (same logic as Holy Relic section)
                    var hasGuardianBlessingMinion = _instance.Buffs.Exists(x =>
                        x.Name == "has_guardians_blessing_minion" ||
                        (x.Name.Contains("holy") && x.Name.Contains("relic") && !x.Name.Contains("life")) ||
                        x.Name.Contains("guardian_blessing_minion"));

                    // Check conditions
                    var missingAura = !hasGuardianBlessingAura;
                    var hasMinion = hasGuardianBlessingMinion;

                    // If we have the minion but don't have the aura buff, cast Zealotry
                    if (missingAura && hasMinion)
                    {
                        Keyboard.KeyPress(_instance.GetSkillInputKey(skill.SkillSlotIndex));
                    }
                }
            }
        }
    }
}
