using System;
using System.Windows.Forms;
using BetterFollowbotLite.Interfaces;
using BetterFollowbotLite.Core.Skills;
using ExileCore;

namespace BetterFollowbotLite.Automation
{
    internal class AutoMapTabber : IAutomation
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public AutoMapTabber(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.autoMapTabber;

        public string AutomationName => "Auto Map Tabber";

        public void Execute()
        {
            try
            {
                if (_settings.autoMapTabber && !Keyboard.IsKeyDown((int)_settings.inputKeyPickIt.Value))
                    if (SkillInfo.ManageCooldown(SkillInfo.autoMapTabber))
                    {
                        bool shouldBeClosed = _instance.GameController.IngameState.IngameUi.Atlas.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.AtlasTreePanel.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.StashElement.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.TradeWindow.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.ChallengesPanel.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.CraftBench.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.DelveWindow.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.ExpeditionWindow.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.BanditDialog.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.MetamorphWindow.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.SyndicatePanel.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.SyndicateTree.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.QuestRewardWindow.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.SynthesisWindow.IsVisible ||
                                              //GameController.IngameState.IngameUi.UltimatumPanel.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.MapDeviceWindow.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.SellWindow.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.SettingsPanel.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.InventoryPanel.IsVisible ||
                                              //GameController.IngameState.IngameUi.NpcDialog.IsVisible ||
                                              _instance.GameController.IngameState.IngameUi.TreePanel.IsVisible;


                        if (!_instance.GameController.IngameState.IngameUi.Map.SmallMiniMap.IsVisibleLocal && shouldBeClosed)
                        {
                            Keyboard.KeyPress(Keys.Tab);
                            SkillInfo.autoMapTabber.Cooldown = 250;
                        }
                        else if (_instance.GameController.IngameState.IngameUi.Map.SmallMiniMap.IsVisibleLocal && !shouldBeClosed)
                        {
                            Keyboard.KeyPress(Keys.Tab);
                            SkillInfo.autoMapTabber.Cooldown = 250;
                        }
                    }
            }
            catch (Exception e)
            {
                // Error handling without logging
            }
        }
    }
}
