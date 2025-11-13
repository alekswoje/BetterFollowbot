using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using BetterFollowbot.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BetterFollowbot.Automation
{
    internal class PartyJoiner : IAutomation
    {
        private readonly BetterFollowbot _instance;
        private readonly BetterFollowbotSettings _settings;
        private DateTime _lastAutoJoinPartyAttempt;

        public PartyJoiner(BetterFollowbot instance, BetterFollowbotSettings settings)
        {
            _instance = instance;
            _settings = settings;
            _lastAutoJoinPartyAttempt = DateTime.Now.AddSeconds(-1); // Initialize to 1 second ago to allow immediate execution
        }

        public bool IsEnabled => _settings.autoJoinPartyEnabled.Value;

        public string AutomationName => "Auto Join Party & Trade";

        private dynamic GetTradePanel()
        {
            try
            {
                return BetterFollowbot.Instance.GameController.IngameState.IngameUi.TradeWindow;
            }
            catch
            {
                return null;
            }
        }

        public void Execute()
        {
            var timeSinceLastAttempt = (DateTime.Now - _lastAutoJoinPartyAttempt).TotalSeconds;
            if (_settings.autoJoinPartyEnabled.Value && timeSinceLastAttempt >= 0.5 && _instance.Gcd())
            {
                try
                {
                    var partyElement = _instance.GetPartyElements();
                    var isInParty = partyElement != null && partyElement.Count > 0;

                    var invitesPanel = _instance.GetInvitesPanel();
                    if (invitesPanel != null && invitesPanel.IsVisible)
                    {
                        dynamic invites = null;

                        try
                        {
                            invites = invitesPanel.Invites;
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                invites = invitesPanel.InviteList;
                            }
                            catch
                            {
                                try
                                {
                                    invites = invitesPanel.Children;
                                }
                                catch (Exception ex2)
                                {
                                    BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Error accessing invites: {ex2.Message}");
                                }
                            }
                        }

                        if (invites != null)
                        {
                            int inviteCount = 0;
                            try
                            {
                                if (invites is System.Array)
                                {
                                    inviteCount = invites.Length;
                                }
                                else if (invites is System.Collections.ICollection)
                                {
                                    inviteCount = invites.Count;
                                }
                            }
                            catch (Exception ex)
                            {
                                BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Error getting invite count: {ex.Message}");
                            }

                            if (inviteCount > 0)
                            {
                                foreach (var invite in invites)
                                {
                                    if (invite != null)
                                    {
                                        try
                                        {
                                            string inviterName = "";
                                            try
                                            {
                                                inviterName = invite.Name ?? "";
                                            }
                                            catch (Exception nameEx)
                                            {
                                                BetterFollowbot.Instance.LogMessage($"INVITE DEBUG: Could not get invite.Name - {nameEx.Message}");
                                            }

                                            var leaderNames = _settings.autoPilotLeader.Value;
                                            bool isFromLeader = false;

                                            if (!string.IsNullOrEmpty(leaderNames) && !string.IsNullOrEmpty(inviterName))
                                            {
                                                var names = leaderNames.Split(',')
                                                    .Select(n => n.Trim())
                                                    .Where(n => !string.IsNullOrEmpty(n))
                                                    .ToList();

                                                isFromLeader = names.Any(name => 
                                                    inviterName.Equals(name, StringComparison.OrdinalIgnoreCase));
                                            }

                                            if (!isFromLeader)
                                            {
                                                if (timeSinceLastAttempt >= 15.0)
                                                {
                                                    BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Ignoring invite from '{inviterName}' (only accepting from: '{leaderNames}')");
                                                }
                                                continue;
                                            }

                                            var actionText = invite.ActionText;
                                            if (actionText != null)
                                            {
                                                string inviteType = "";
                                                bool shouldProcess = false;

                                                if (actionText.Contains("party invite") || actionText.Contains("sent you a party invite"))
                                                {
                                                    inviteType = "PARTY";
                                                    shouldProcess = _settings.autoJoinPartyEvenIfInParty.Value || !isInParty;
                                                    if (!shouldProcess)
                                                    {
                                                        if (timeSinceLastAttempt >= 15.0)
                                                        {
                                                            BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Skipping party invite from '{inviterName}' - already in party ({partyElement.Count} members)");
                                                        }
                                                        continue;
                                                    }
                                                }
                                                else if (actionText.Contains("trade request") || actionText.Contains("sent you a trade request"))
                                                {
                                                    inviteType = "TRADE";
                                                    shouldProcess = true;
                                                }
                                                else
                                                {
                                                    BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Unknown invite type with action text: '{actionText}'");
                                                    continue;
                                                }

                                                if (shouldProcess)
                                                {
                                                    var acceptButton = invite.AcceptButton;
                                                    if (acceptButton != null && acceptButton.IsVisible)
                                                    {
                                                        var random = new Random();
                                                        var buttonRect = acceptButton.GetClientRectCache;
                                                        var buttonCenter = buttonRect.Center;
                                                        
                                                        var randomOffsetX = (float)(random.NextDouble() * 10 - 5);
                                                        var randomOffsetY = (float)(random.NextDouble() * 10 - 5);
                                                        var buttonScreenCenter = new Vector2(
                                                            buttonCenter.X + randomOffsetX,
                                                            buttonCenter.Y + randomOffsetY
                                                        );
                                                        
                                                        var scaledPosition = Mouse.ApplyDpiScaling(buttonScreenCenter);
                                                        Mouse.SetCursorPos(scaledPosition);
                                                        Thread.Sleep(random.Next(250, 350));

                                                        var currentMousePos = _instance.GetMousePosition();
                                                        var distanceFromTarget = Vector2.Distance(currentMousePos, scaledPosition);

                                                        if (distanceFromTarget < 15)
                                                        {
                                                            Mouse.LeftMouseDown();
                                                            Thread.Sleep(random.Next(35, 50));
                                                            Mouse.LeftMouseUp();
                                                            Thread.Sleep(random.Next(250, 350));

                                                            bool success = false;
                                                            if (inviteType == "PARTY")
                                                            {
                                                                var partyAfterClick = _instance.GetPartyElements();
                                                                success = partyAfterClick != null && partyAfterClick.Count > 0;

                                                                if (success)
                                                                {
                                                                    BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Successfully joined party!");
                                                                }
                                                            }
                                                            else if (inviteType == "TRADE")
                                                            {
                                                                BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Trade invite accepted, waiting for trade window");
                                                                Thread.Sleep(500);
                                                                
                                                                var tradePanel = GetTradePanel();
                                                                success = tradePanel != null;

                                                                if (success && _settings.autoDumpInventoryOnTrade.Value)
                                                                {
                                                                    BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Trade window detected, dumping inventory");
                                                                    Thread.Sleep(300);
                                                                    DumpInventoryToTrade();
                                                                }
                                                            }

                                                            if (!success)
                                                            {
                                                                Thread.Sleep(random.Next(550, 650));
                                                                Mouse.LeftMouseDown();
                                                                Thread.Sleep(random.Next(35, 50));
                                                                Mouse.LeftMouseUp();
                                                                Thread.Sleep(random.Next(250, 350));

                                                                if (inviteType == "PARTY")
                                                                {
                                                                    var partyAfterClick = _instance.GetPartyElements();
                                                                    success = partyAfterClick != null && partyAfterClick.Count > 0;

                                                                    if (success)
                                                                    {
                                                                        BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Successfully joined party on second attempt!");
                                                                    }
                                                                }
                                                                else if (inviteType == "TRADE")
                                                                {
                                                                    BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Trade invite accepted on second attempt, waiting for trade window");
                                                                    Thread.Sleep(500);
                                                                    
                                                                    var tradePanel = GetTradePanel();
                                                                    success = tradePanel != null;

                                                                    if (success && _settings.autoDumpInventoryOnTrade.Value)
                                                                    {
                                                                        BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Trade window detected, dumping inventory (second attempt)");
                                                                        Thread.Sleep(300);
                                                                        DumpInventoryToTrade();
                                                                    }
                                                                }

                                                                if (!success)
                                                                {
                                                                    var timeSinceLastFailure = (DateTime.Now - _lastAutoJoinPartyAttempt).TotalSeconds;
                                                                    if (timeSinceLastFailure >= 30.0)
                                                                    {
                                                                        BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Failed to accept {inviteType} invite - may need manual intervention");
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        else
                                                        {
                                                            BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: {inviteType} mouse positioning failed - too far from target ({distanceFromTarget:F1})");
                                                        }

                                                        _instance.LastTimeAny = DateTime.Now;
                                                        _lastAutoJoinPartyAttempt = DateTime.Now;
                                                    }
                                                    else
                                                    {
                                                        var timeSinceLastLog = (DateTime.Now - _lastAutoJoinPartyAttempt).TotalSeconds;
                                                        if (timeSinceLastLog >= 20.0)
                                                        {
                                                            BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: {inviteType} accept button not found or not visible");
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Invite has no action text");
                                            }
                                        }
                                        catch (Exception inviteEx)
                                        {
                                            BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Exception processing invite - {inviteEx.Message}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Exception occurred - {e.Message}");
                }
            }
        }

        private void DumpInventoryToTrade()
        {
            BetterFollowbot.Instance.LogMessage("INVENTORY DUMP: Starting dump process");
            
            try
            {
                var inventoryPanel = BetterFollowbot.Instance.GameController?.IngameState?.IngameUi?.InventoryPanel;
                BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP DEBUG: Inventory panel - Null: {inventoryPanel == null}");
                
                if (inventoryPanel == null)
                {
                    BetterFollowbot.Instance.LogMessage("INVENTORY DUMP: Inventory panel not found");
                    return;
                }

                var tradePanel = GetTradePanel();
                BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP DEBUG: Trade panel - Null: {tradePanel == null}");
                
                if (tradePanel == null)
                {
                    BetterFollowbot.Instance.LogMessage("INVENTORY DUMP: Trade window not found");
                    return;
                }

                var playerInventories = BetterFollowbot.Instance.GameController?.IngameState?.ServerData?.PlayerInventories;
                BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP DEBUG: PlayerInventories - Null: {playerInventories == null}, Count: {playerInventories?.Count}");
                
                var inventoryItems = playerInventories?[0]?.Inventory?.InventorySlotItems;
                BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP DEBUG: InventorySlotItems - Null: {inventoryItems == null}, Count: {inventoryItems?.Count}");
                
                if (inventoryItems == null)
                {
                    BetterFollowbot.Instance.LogMessage("INVENTORY DUMP: No inventory items found");
                    return;
                }

                var itemsToDump = inventoryItems
                    .Where(x => x != null && x.Item != null)
                    .OrderBy(x => x.PosX)
                    .ThenBy(x => x.PosY)
                    .ToList();

                BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP: Found {itemsToDump.Count} items to dump");

                var prevMousePos = _instance.GetMousePosition();
                BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP DEBUG: Previous mouse position: {prevMousePos}");

                var random = new Random();
                int dumpedCount = 0;
                foreach (var item in itemsToDump)
                {
                    var tradePanelCheck = GetTradePanel();
                    if (tradePanelCheck == null)
                    {
                        BetterFollowbot.Instance.LogMessage("INVENTORY DUMP: Trade window closed, aborting");
                        break;
                    }

                    var itemRect = item.GetClientRect();
                    var itemCenter = itemRect.Center;
                    
                    var randomOffsetX = (float)(random.NextDouble() * 10 - 5);
                    var randomOffsetY = (float)(random.NextDouble() * 10 - 5);
                    var randomizedItemPos = new Vector2(
                        itemCenter.X + randomOffsetX,
                        itemCenter.Y + randomOffsetY
                    );
                    
                    BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP DEBUG: Item [{dumpedCount}] at ({item.PosX}, {item.PosY}), Screen center: ({randomizedItemPos.X:F1}, {randomizedItemPos.Y:F1})");

                    Keyboard.KeyDown(Keys.LControlKey);
                    Thread.Sleep(random.Next(25, 40));
                    Mouse.SetCursorPos(randomizedItemPos);
                    Thread.Sleep(random.Next(40, 70));
                    Mouse.LeftMouseDown();
                    Thread.Sleep(random.Next(25, 45));
                    Mouse.LeftMouseUp();
                    Thread.Sleep(random.Next(25, 40));
                    Keyboard.KeyUp(Keys.LControlKey);
                    Thread.Sleep(random.Next(35, 55));
                    
                    dumpedCount++;
                    BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP DEBUG: Dumped item {dumpedCount}/{itemsToDump.Count}");
                }

                if (prevMousePos.X > 0 && prevMousePos.Y > 0)
                {
                    Mouse.SetCursorPos(prevMousePos);
                }
                BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP: Completed dumping {dumpedCount} items");

                if (_settings.autoClickTradeAcceptButton.Value)
                {
                    Thread.Sleep(500);
                    ClickTradeAcceptButton();
                }
            }
            catch (Exception ex)
            {
                BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP: Error - {ex.Message}");
            }
        }

        private void ClickTradeAcceptButton()
        {
            BetterFollowbot.Instance.LogMessage("AUTO CLICK TRADE ACCEPT: Starting");
            
            try
            {
                var tradePanel = GetTradePanel();
                BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Trade panel - Null: {tradePanel == null}");
                
                if (tradePanel == null)
                {
                    BetterFollowbot.Instance.LogMessage("AUTO CLICK TRADE ACCEPT: Trade window not found");
                    return;
                }

                const int maxAttempts = 3;
                int baseDelay = 100;
                
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Attempt {attempt}/{maxAttempts}");
                    
                    // Refresh EVERYTHING right before clicking to ensure latest state
                    var freshTradePanel = GetTradePanel();
                    if (freshTradePanel == null)
                    {
                        BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Trade window disappeared on attempt {attempt}");
                        return;
                    }
                    
                    var acceptButton = freshTradePanel.AcceptButton;
                    
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - Button null: {acceptButton == null}");
                    if (acceptButton != null)
                    {
                        try
                        {
                            var isVisible = acceptButton.IsVisible;
                            var isVisibleLocal = acceptButton.IsVisibleLocal;
                            var isValid = acceptButton.IsValid;
                            BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - IsVisible: {isVisible}, IsVisibleLocal: {isVisibleLocal}, IsValid: {isValid}");
                        }
                        catch (Exception ex)
                        {
                            BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Error checking button properties: {ex.Message}");
                        }
                    }
                    
                    bool isButtonReady = acceptButton != null && (acceptButton.IsVisible || acceptButton.IsVisibleLocal);
                    
                    if (!isButtonReady)
                    {
                        BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Accept button not ready on attempt {attempt}, waiting...");
                        Thread.Sleep(baseDelay * attempt);
                        continue;
                    }
                    
                    // Use GetClientRectCache which properly accounts for container inheritance
                    var buttonRect = acceptButton.GetClientRectCache;
                    var buttonCenter = buttonRect.Center;
                    var scaledPosition = Mouse.ApplyDpiScaling(buttonCenter);
                    
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - Original: ({buttonCenter.X:F1}, {buttonCenter.Y:F1}), Scaled: ({scaledPosition.X:F1}, {scaledPosition.Y:F1}), DPI Factor: {Mouse.ScreenScaleFactor:F2}");
                    
                    Mouse.SetCursorPos(scaledPosition);
                    Thread.Sleep(150);
                    
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Clicking at ({scaledPosition.X:F1}, {scaledPosition.Y:F1})");
                    
                    Mouse.LeftMouseDown();
                    Thread.Sleep(40);
                    Mouse.LeftMouseUp();

                    BetterFollowbot.Instance.LogMessage("AUTO CLICK TRADE ACCEPT: Clicked accept button");
                    
                    Thread.Sleep(400);
                    
                    var tradePanelCheck = GetTradePanel();
                    if (tradePanelCheck != null)
                    {
                        try
                        {
                            bool sellerAccepted = tradePanelCheck.SellerAccepted;
                            BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: SellerAccepted = {sellerAccepted}");
                            
                            if (sellerAccepted)
                            {
                                BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Successfully accepted trade on attempt {attempt}");
                                return;
                            }
                            else
                            {
                                BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: SellerAccepted is false on attempt {attempt}");
                                
                                if (attempt < maxAttempts)
                                {
                                    // Exponential backoff: 100ms, 200ms, 400ms
                                    int delay = baseDelay * (int)Math.Pow(2, attempt - 1);
                                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Waiting {delay}ms before retry");
                                    Thread.Sleep(delay);
                                }
                            }
                        }
                        catch (Exception checkEx)
                        {
                            BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Could not check SellerAccepted - {checkEx.Message}");
                            // If we can't check, assume it worked and exit
                            return;
                        }
                    }
                    else
                    {
                        BetterFollowbot.Instance.LogMessage("AUTO CLICK TRADE ACCEPT: Trade window closed after click");
                        return;
                    }
                }
                
                BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Failed to accept after {maxAttempts} attempts");
            }
            catch (Exception ex)
            {
                BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Error - {ex.Message}");
            }
        }
    }
}
