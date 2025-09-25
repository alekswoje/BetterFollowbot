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

namespace BetterFollowbot.Skill
{
    internal class Warcries : ISkill
    {
        private readonly BetterFollowbot _instance;
        private readonly BetterFollowbotSettings _settings;

        public Warcries(BetterFollowbot instance, BetterFollowbotSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.warcriesEnabled;

        public string SkillName => "Warcries";

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
            if (!_instance.CanUseSkill("Warcries"))
                return;

            // Handle all warcries
            HandleWarcries();
        }

        private void HandleWarcries()
        {
            // Loop through all skills to find warcry skills
            foreach (var skill in _instance.skills)
            {
                ProcessWarcrySkill(skill);
            }
        }

        private void ProcessWarcrySkill(ActorSkill skill)
        {
            try
            {
                // Check each warcry type
                if (IsWarcryEnabled(skill) && skill.CanBeUsed)
                {
                    // Activate the skill
                    var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);
                    if (skillKey != default(Keys))
                    {
                        Keyboard.KeyPress(skillKey);
                        var warcryName = GetWarcryName(skill);
                        _instance.RecordSkillUse("Warcries");
                        _instance.LogMessage($"WARCRY: Used {warcryName} successfully");
                    }
                }
            }
            catch (Exception e)
            {
                // Error handling without logging
            }
        }

        private bool IsWarcryEnabled(ActorSkill skill)
        {
            // Check if this skill matches any enabled warcry
            var skillName = skill.InternalName?.ToLower() ?? "";
            
            return (skillName.Contains("ancestral_cry") && _settings.ancestralCryEnabled) ||
                   (skillName.Contains("infernal_cry") && _settings.infernalCryEnabled) ||
                   (skillName.Contains("generals_cry") && _settings.generalsCryEnabled) ||
                   (skillName.Contains("intimidating_cry") && _settings.intimidatingCryEnabled) ||
                   (skillName.Contains("rallying_cry") && _settings.rallyingCryEnabled) ||
                   (skillName.Contains("vengeful_cry") && _settings.vengefulCryEnabled) ||
                   (skillName.Contains("enduring_cry") && _settings.enduringCryEnabled) ||
                   (skillName.Contains("seismic_cry") && _settings.seismicCryEnabled) ||
                   (skillName.Contains("battlemages_cry") && _settings.battlemagesCryEnabled);
        }

        private string GetWarcryName(ActorSkill skill)
        {
            var skillName = skill.InternalName?.ToLower() ?? "";
            
            if (skillName.Contains("ancestral_cry")) return "Ancestral Cry";
            if (skillName.Contains("infernal_cry")) return "Infernal Cry";
            if (skillName.Contains("generals_cry")) return "General's Cry";
            if (skillName.Contains("intimidating_cry")) return "Intimidating Cry";
            if (skillName.Contains("rallying_cry")) return "Rallying Cry";
            if (skillName.Contains("vengeful_cry")) return "Vengeful Cry";
            if (skillName.Contains("enduring_cry")) return "Enduring Cry";
            if (skillName.Contains("seismic_cry")) return "Seismic Cry";
            if (skillName.Contains("battlemages_cry")) return "Battlemage's Cry";
            
            return "Unknown Warcry";
        }
    }
}
