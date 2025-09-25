using ExileCore;

namespace BetterFollowbot
{
    /// <summary>
    /// Centralized utility for detecting blocking UI elements
    /// </summary>
    public static class UIBlockingUtility
    {
        /// <summary>
        /// Checks if any blocking UI elements are currently open
        /// </summary>
        public static bool IsAnyBlockingUIOpen()
        {
            try
            {
                var gameController = BetterFollowbot.Instance.GameController;
                var ui = gameController?.IngameState?.IngameUi;
                
                if (ui == null) return false;

                return ui.StashElement?.IsVisibleLocal == true ||
                       ui.NpcDialog?.IsVisible == true ||
                       ui.SellWindow?.IsVisible == true ||
                       ui.PurchaseWindow?.IsVisible == true ||
                       ui.InventoryPanel?.IsVisible == true ||
                       ui.TreePanel?.IsVisible == true ||
                       ui.Atlas?.IsVisible == true ||
                       ui.RitualWindow?.IsVisible == true ||
                       ui.OpenRightPanel?.IsVisible == true ||
                       ui.TradeWindow?.IsVisible == true ||
                       ui.ChallengesPanel?.IsVisible == true ||
                       ui.CraftBench?.IsVisible == true ||
                       ui.DelveWindow?.IsVisible == true ||
                       ui.ExpeditionWindow?.IsVisible == true ||
                       ui.BanditDialog?.IsVisible == true ||
                       ui.MetamorphWindow?.IsVisible == true ||
                       ui.SyndicatePanel?.IsVisible == true ||
                       ui.SyndicateTree?.IsVisible == true ||
                       ui.QuestRewardWindow?.IsVisible == true ||
                       ui.SynthesisWindow?.IsVisible == true ||
                       ui.MapDeviceWindow?.IsVisible == true ||
                       ui.SettingsPanel?.IsVisible == true;
            }
            catch
            {
                return false;
            }
        }
    }
}
