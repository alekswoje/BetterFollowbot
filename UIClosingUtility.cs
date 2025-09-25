using System.Threading;
using System.Windows.Forms;
using ExileCore;

namespace BetterFollowbot
{
    /// <summary>
    /// Centralized utility for smart UI closing logic
    /// </summary>
    public static class UIClosingUtility
    {
        /// <summary>
        /// Determines if UI should be closed based on context
        /// </summary>
        public static bool ShouldCloseUI()
        {
            try
            {
                var gameController = BetterFollowbot.Instance.GameController;
                var ui = gameController?.IngameState?.IngameUi;
                
                if (ui == null) return false;

                var tradeWindowOpen = ui.TradeWindow?.IsVisible == true;
                var purchaseWindowOpen = ui.PurchaseWindow?.IsVisible == true;
                var sellWindowOpen = ui.SellWindow?.IsVisible == true;
                
                if (tradeWindowOpen || purchaseWindowOpen || sellWindowOpen) return false;

                return UIBlockingUtility.IsAnyBlockingUIOpen();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Closes blocking UI if appropriate
        /// </summary>
        public static void CloseBlockingUI()
        {
            if (ShouldCloseUI())
            {
                Keyboard.KeyPress(Keys.Escape);
                Thread.Sleep(100);
            }
        }
    }
}
