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
        /// NOTE: Checks both IsVisible AND IsValid to prevent false positives
        /// </summary>
        public static bool IsAnyBlockingUIOpen()
        {
            try
            {
                var gameController = BetterFollowbot.Instance.GameController;
                var ui = gameController?.IngameState?.IngameUi;
                
                if (ui == null) return false;

                return (ui.StashElement?.IsVisibleLocal == true && ui.StashElement?.IsValid == true) ||
                       (ui.NpcDialog?.IsVisible == true && ui.NpcDialog?.IsValid == true) ||
                       (ui.SellWindow?.IsVisible == true && ui.SellWindow?.IsValid == true) ||
                       (ui.PurchaseWindow?.IsVisible == true && ui.PurchaseWindow?.IsValid == true) ||
                       (ui.InventoryPanel?.IsVisible == true && ui.InventoryPanel?.IsValid == true) ||
                       (ui.TreePanel?.IsVisible == true && ui.TreePanel?.IsValid == true) ||
                       (ui.Atlas?.IsVisible == true && ui.Atlas?.IsValid == true) ||
                       (ui.RitualWindow?.IsVisible == true && ui.RitualWindow?.IsValid == true) ||
                       (ui.OpenRightPanel?.IsVisible == true && ui.OpenRightPanel?.IsValid == true) ||
                       (ui.TradeWindow?.IsVisible == true && ui.TradeWindow?.IsValid == true) ||
                       (ui.ChallengesPanel?.IsVisible == true && ui.ChallengesPanel?.IsValid == true) ||
                       (ui.CraftBench?.IsVisible == true && ui.CraftBench?.IsValid == true) ||
                       (ui.DelveWindow?.IsVisible == true && ui.DelveWindow?.IsValid == true) ||
                       (ui.ExpeditionWindow?.IsVisible == true && ui.ExpeditionWindow?.IsValid == true) ||
                       (ui.BanditDialog?.IsVisible == true && ui.BanditDialog?.IsValid == true) ||
                       (ui.MetamorphWindow?.IsVisible == true && ui.MetamorphWindow?.IsValid == true) ||
                       (ui.SyndicatePanel?.IsVisible == true && ui.SyndicatePanel?.IsValid == true) ||
                       (ui.SyndicateTree?.IsVisible == true && ui.SyndicateTree?.IsValid == true) ||
                       (ui.QuestRewardWindow?.IsVisible == true && ui.QuestRewardWindow?.IsValid == true) ||
                       (ui.SynthesisWindow?.IsVisible == true && ui.SynthesisWindow?.IsValid == true) ||
                       (ui.MapDeviceWindow?.IsVisible == true && ui.MapDeviceWindow?.IsValid == true) ||
                       (ui.SettingsPanel?.IsVisible == true && ui.SettingsPanel?.IsValid == true);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a list of all currently open blocking UI elements
        /// NOTE: Checks both IsVisible AND IsValid to prevent false positives
        /// </summary>
        public static List<string> GetOpenBlockingUIs()
        {
            var openUIs = new List<string>();
            
            try
            {
                var gameController = BetterFollowbot.Instance.GameController;
                var ui = gameController?.IngameState?.IngameUi;
                
                if (ui == null) return openUIs;

                if (ui.StashElement?.IsVisibleLocal == true && ui.StashElement?.IsValid == true)
                    openUIs.Add("StashElement");
                if (ui.NpcDialog?.IsVisible == true && ui.NpcDialog?.IsValid == true)
                    openUIs.Add("NpcDialog");
                if (ui.SellWindow?.IsVisible == true && ui.SellWindow?.IsValid == true)
                    openUIs.Add("SellWindow");
                if (ui.PurchaseWindow?.IsVisible == true && ui.PurchaseWindow?.IsValid == true)
                    openUIs.Add("PurchaseWindow");
                if (ui.InventoryPanel?.IsVisible == true && ui.InventoryPanel?.IsValid == true)
                    openUIs.Add("InventoryPanel");
                if (ui.TreePanel?.IsVisible == true && ui.TreePanel?.IsValid == true)
                    openUIs.Add("TreePanel");
                if (ui.Atlas?.IsVisible == true && ui.Atlas?.IsValid == true)
                    openUIs.Add("Atlas");
                if (ui.RitualWindow?.IsVisible == true && ui.RitualWindow?.IsValid == true)
                    openUIs.Add("RitualWindow");
                if (ui.OpenRightPanel?.IsVisible == true && ui.OpenRightPanel?.IsValid == true)
                    openUIs.Add("OpenRightPanel");
                if (ui.TradeWindow?.IsVisible == true && ui.TradeWindow?.IsValid == true)
                    openUIs.Add("TradeWindow");
                if (ui.ChallengesPanel?.IsVisible == true && ui.ChallengesPanel?.IsValid == true)
                    openUIs.Add("ChallengesPanel");
                if (ui.CraftBench?.IsVisible == true && ui.CraftBench?.IsValid == true)
                    openUIs.Add("CraftBench");
                if (ui.DelveWindow?.IsVisible == true && ui.DelveWindow?.IsValid == true)
                    openUIs.Add("DelveWindow");
                if (ui.ExpeditionWindow?.IsVisible == true && ui.ExpeditionWindow?.IsValid == true)
                    openUIs.Add("ExpeditionWindow");
                if (ui.BanditDialog?.IsVisible == true && ui.BanditDialog?.IsValid == true)
                    openUIs.Add("BanditDialog");
                if (ui.MetamorphWindow?.IsVisible == true && ui.MetamorphWindow?.IsValid == true)
                    openUIs.Add("MetamorphWindow");
                if (ui.SyndicatePanel?.IsVisible == true && ui.SyndicatePanel?.IsValid == true)
                    openUIs.Add("SyndicatePanel");
                if (ui.SyndicateTree?.IsVisible == true && ui.SyndicateTree?.IsValid == true)
                    openUIs.Add("SyndicateTree");
                if (ui.QuestRewardWindow?.IsVisible == true && ui.QuestRewardWindow?.IsValid == true)
                    openUIs.Add("QuestRewardWindow");
                if (ui.SynthesisWindow?.IsVisible == true && ui.SynthesisWindow?.IsValid == true)
                    openUIs.Add("SynthesisWindow");
                if (ui.MapDeviceWindow?.IsVisible == true && ui.MapDeviceWindow?.IsValid == true)
                    openUIs.Add("MapDeviceWindow");
                if (ui.SettingsPanel?.IsVisible == true && ui.SettingsPanel?.IsValid == true)
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
