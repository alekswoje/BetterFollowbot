using System.Collections.Generic;
using System.Linq;
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
                       // ui.SyndicateTree?.IsVisible == true || // REMOVED: False positive - always reports as open
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

        /// <summary>
        /// Gets a list of all currently open blocking UI elements
        /// </summary>
        public static List<string> GetOpenBlockingUIs()
        {
            var openUIs = new List<string>();
            
            try
            {
                var gameController = BetterFollowbot.Instance.GameController;
                var ui = gameController?.IngameState?.IngameUi;
                
                if (ui == null) return openUIs;

                if (ui.StashElement?.IsVisibleLocal == true)
                    openUIs.Add("StashElement");
                if (ui.NpcDialog?.IsVisible == true)
                    openUIs.Add("NpcDialog");
                if (ui.SellWindow?.IsVisible == true)
                    openUIs.Add("SellWindow");
                if (ui.PurchaseWindow?.IsVisible == true)
                    openUIs.Add("PurchaseWindow");
                if (ui.InventoryPanel?.IsVisible == true)
                    openUIs.Add("InventoryPanel");
                if (ui.TreePanel?.IsVisible == true)
                    openUIs.Add("TreePanel");
                if (ui.Atlas?.IsVisible == true)
                    openUIs.Add("Atlas");
                if (ui.RitualWindow?.IsVisible == true)
                    openUIs.Add("RitualWindow");
                if (ui.OpenRightPanel?.IsVisible == true)
                    openUIs.Add("OpenRightPanel");
                if (ui.TradeWindow?.IsVisible == true)
                    openUIs.Add("TradeWindow");
                if (ui.ChallengesPanel?.IsVisible == true)
                    openUIs.Add("ChallengesPanel");
                if (ui.CraftBench?.IsVisible == true)
                    openUIs.Add("CraftBench");
                if (ui.DelveWindow?.IsVisible == true)
                    openUIs.Add("DelveWindow");
                if (ui.ExpeditionWindow?.IsVisible == true)
                    openUIs.Add("ExpeditionWindow");
                if (ui.BanditDialog?.IsVisible == true)
                    openUIs.Add("BanditDialog");
                if (ui.MetamorphWindow?.IsVisible == true)
                    openUIs.Add("MetamorphWindow");
                if (ui.SyndicatePanel?.IsVisible == true)
                    openUIs.Add("SyndicatePanel");
                // SyndicateTree removed - false positive, always reports as open
                // if (ui.SyndicateTree?.IsVisible == true)
                //     openUIs.Add("SyndicateTree");
                if (ui.QuestRewardWindow?.IsVisible == true)
                    openUIs.Add("QuestRewardWindow");
                if (ui.SynthesisWindow?.IsVisible == true)
                    openUIs.Add("SynthesisWindow");
                if (ui.MapDeviceWindow?.IsVisible == true)
                    openUIs.Add("MapDeviceWindow");
                if (ui.SettingsPanel?.IsVisible == true)
                    openUIs.Add("SettingsPanel");
            }
            catch
            {
                // Return empty list on error
            }
            
            return openUIs;
        }

        /// <summary>
        /// Gets a formatted string of all open blocking UIs (for logging)
        /// </summary>
        public static string GetOpenBlockingUIsString()
        {
            var openUIs = GetOpenBlockingUIs();
            return openUIs.Count > 0 ? string.Join(", ", openUIs) : "None";
        }
    }
}
