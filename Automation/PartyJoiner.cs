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
                                                        // CRITICAL FIX: Add window offset to convert client coordinates to screen coordinates
                                                        var windowOffset = _instance.GameController.Window.GetWindowRectangle().TopLeft;
                                                        var buttonRect = acceptButton.GetClientRectCache;
                                                        var buttonClientCenter = buttonRect.Center;
                                                        var buttonScreenCenter = new Vector2(buttonClientCenter.X + windowOffset.X, buttonClientCenter.Y + windowOffset.Y);

                                                        Mouse.SetCursorPos(buttonScreenCenter);
                                                        Thread.Sleep(300);

                                                        var currentMousePos = _instance.GetMousePosition();
                                                        var distanceFromTarget = Vector2.Distance(currentMousePos, buttonScreenCenter);

                                                        if (distanceFromTarget < 15)
                                                        {
                                                            Mouse.LeftMouseDown();
                                                            Thread.Sleep(40);
                                                            Mouse.LeftMouseUp();
                                                            Thread.Sleep(300);

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
                                                                Thread.Sleep(600);
                                                                Mouse.LeftMouseDown();
                                                                Thread.Sleep(40);
                                                                Mouse.LeftMouseUp();
                                                                Thread.Sleep(300);

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
                    BetterFollowbot.Instance.LogMessage($"INVENTORY DUMP DEBUG: Item [{dumpedCount}] at ({item.PosX}, {item.PosY}), Screen center: ({itemCenter.X:F1}, {itemCenter.Y:F1})");

                    Keyboard.KeyDown(Keys.LControlKey);
                    Thread.Sleep(30);
                    Mouse.SetCursorPos(itemCenter);
                    Thread.Sleep(50);
                    Mouse.LeftMouseDown();
                    Thread.Sleep(30);
                    Mouse.LeftMouseUp();
                    Thread.Sleep(30);
                    Keyboard.KeyUp(Keys.LControlKey);
                    Thread.Sleep(40);
                    
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
                    // Give UI time to update button state after dump
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

                // Try up to 3 times with exponential backoff
                const int maxAttempts = 3;
                int baseDelay = 100; // milliseconds
                
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Attempt {attempt}/{maxAttempts}");
                    
                    // CRITICAL FIX: Refresh button position on EACH attempt (UI might shift between attempts)
                    var acceptButton = tradePanel.AcceptButton;
                    
                    // Enhanced debugging to understand button state
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
                    
                    // Check if button is ready (try both visibility properties)
                    bool isButtonReady = acceptButton != null && (acceptButton.IsVisible || acceptButton.IsVisibleLocal);
                    
                    if (!isButtonReady)
                    {
                        BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Accept button not ready on attempt {attempt}, waiting...");
                        Thread.Sleep(baseDelay * attempt); // Exponential backoff
                        continue;
                    }
                    
                    // Get fresh position right before clicking
                    var windowOffset = BetterFollowbot.Instance.GameController.Window.GetWindowRectangle().TopLeft;
                    var windowRect = BetterFollowbot.Instance.GameController.Window.GetWindowRectangle();
                    var buttonRect = acceptButton.GetClientRectCache;
                    
                    // Log all possible coordinate interpretations
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - Window rect: TopLeft:({windowRect.TopLeft.X:F1},{windowRect.TopLeft.Y:F1}) Size:({windowRect.Width:F1}x{windowRect.Height:F1})");
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - Button GetClientRectCache: X:{buttonRect.X:F1} Y:{buttonRect.Y:F1} W:{buttonRect.Width:F1} H:{buttonRect.Height:F1}");
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - Button Center property: ({buttonRect.Center.X:F1}, {buttonRect.Center.Y:F1})");
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - Button TopLeft: ({buttonRect.TopLeft.X:F1}, {buttonRect.TopLeft.Y:F1})");
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - Button BottomRight: ({buttonRect.BottomRight.X:F1}, {buttonRect.BottomRight.Y:F1})");
                    
                    // Calculate center manually
                    float calculatedCenterX = buttonRect.X + (buttonRect.Width / 2f);
                    float calculatedCenterY = buttonRect.Y + (buttonRect.Height / 2f);
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - Calculated center (X+W/2, Y+H/2): ({calculatedCenterX:F1}, {calculatedCenterY:F1})");
                    
                    // Try using the Center property directly this time
                    var buttonClientCenter = buttonRect.Center;
                    var buttonScreenCenter = new Vector2(buttonClientCenter.X + windowOffset.X, buttonClientCenter.Y + windowOffset.Y);
                    
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT DEBUG: Attempt {attempt} - Final target (Center + WindowOffset): ({buttonScreenCenter.X:F1}, {buttonScreenCenter.Y:F1})");
                    
                    // Use direct SetCursorPos instead of SetCursorPosHuman to avoid overshooting
                    Mouse.SetCursorPos(buttonScreenCenter);
                    Thread.Sleep(150); // Give mouse time to settle
                    
                    BetterFollowbot.Instance.LogMessage($"AUTO CLICK TRADE ACCEPT: Clicking at ({buttonScreenCenter.X:F1}, {buttonScreenCenter.Y:F1})");
                    
                    Mouse.LeftMouseDown();
                    Thread.Sleep(40);
                    Mouse.LeftMouseUp();

                    BetterFollowbot.Instance.LogMessage("AUTO CLICK TRADE ACCEPT: Clicked accept button");
                    
                    // Wait a bit for the trade to register
                    Thread.Sleep(200);
                    
                    // Check if SellerAccepted is true
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
