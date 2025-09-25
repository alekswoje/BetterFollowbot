using System;
using System.Threading;
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

        // Method to get trade panel (used internally by this class)
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
            // Check if auto join party & accept trade is enabled and enough time has passed since last attempt (0.5 second cooldown)
            var timeSinceLastAttempt = (DateTime.Now - _lastAutoJoinPartyAttempt).TotalSeconds;
            if (_settings.autoJoinPartyEnabled.Value && timeSinceLastAttempt >= 0.5 && _instance.Gcd())
            {
                try
                {
                    // Check if player is already in a party - if so, don't accept party invites (but still accept trades)
                    var partyElement = _instance.GetPartyElements();
                    var isInParty = partyElement != null && partyElement.Count > 0;

                    // Check if the invites panel is visible
                    var invitesPanel = _instance.GetInvitesPanel();
                    if (invitesPanel != null && invitesPanel.IsVisible)
                    {
                        // Try to get invites using different property names
                        dynamic invites = null;

                        try
                        {
                            invites = invitesPanel.Invites;
                        }
                        catch (Exception ex)
                        {
                            // Try alternative property names if Invites fails
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
                                // Process each invite in the array
                                foreach (var invite in invites)
                                {
                                    if (invite != null)
                                    {
                                        try
                                        {
                                            // Check the action text to determine invite type
                                            var actionText = invite.ActionText;
                                            if (actionText != null)
                                            {
                                                string inviteType = "";
                                                bool shouldProcess = false;

                                                if (actionText.Contains("party invite") || actionText.Contains("sent you a party invite"))
                                                {
                                                    inviteType = "PARTY";
                                                    // Only process party invites if not already in party
                                                    shouldProcess = !isInParty;
                                                    if (isInParty)
                                                    {
                                                        if (timeSinceLastAttempt >= 15.0)
                                                        {
                                                            BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Skipping party invite - already in party ({partyElement.Count} members)");
                                                        }
                                                        continue;
                                                    }
                                                }
                                                else if (actionText.Contains("trade request") || actionText.Contains("sent you a trade request"))
                                                {
                                                    inviteType = "TRADE";
                                                    // Always process trade requests
                                                    shouldProcess = true;
                                                }
                                                else
                                                {
                                                    BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE: Unknown invite type with action text: '{actionText}'");
                                                    continue;
                                                }

                                                if (shouldProcess)
                                                {
                                                    // Get the accept button
                                                    var acceptButton = invite.AcceptButton;
                                                    if (acceptButton != null && acceptButton.IsVisible)
                                                    {
                                                        // Get the center position of the accept button
                                                        var buttonRect = acceptButton.GetClientRectCache;
                                                        var buttonCenter = buttonRect.Center;

                                                        // Move mouse to the accept button
                                                        Mouse.SetCursorPos(buttonCenter);

                                                        // Wait for mouse to settle - longer delay to avoid AutoPilot interference
                                                        Thread.Sleep(300);

                                                        // Verify mouse position
                                                        var currentMousePos = _instance.GetMousePosition();
                                                        var distanceFromTarget = Vector2.Distance(currentMousePos, buttonCenter);

                                                        if (distanceFromTarget < 15) // Allow slightly more tolerance
                                                        {
                                                            // First click attempt - use synchronous mouse events
                                                            Mouse.LeftMouseDown();
                                                            Thread.Sleep(40);
                                                            Mouse.LeftMouseUp();
                                                            Thread.Sleep(300); // Longer delay

                                                            // Check success based on invite type
                                                            bool success = false;
                                                            if (inviteType == "PARTY")
                                                            {
                                                                // Check if we successfully joined a party
                                                                var partyAfterClick = _instance.GetPartyElements();
                                                                success = partyAfterClick != null && partyAfterClick.Count > 0;

                                                                if (success)
                                                                {
                                                                    BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Successfully joined party!");
                                                                }
                                                            }
                                                            else if (inviteType == "TRADE")
                                                            {
                                                                // Check if trade window opened
                                                                var tradePanel = GetTradePanel();
                                                                success = tradePanel != null && tradePanel.IsVisible;

                                                                if (success)
                                                                {
                                                                    BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Successfully opened trade window!");
                                                                }
                                                            }

                                                            if (!success)
                                                            {
                                                                // Second click attempt with longer delay
                                                                Thread.Sleep(600);
                                                                Mouse.LeftMouseDown();
                                                                Thread.Sleep(40);
                                                                Mouse.LeftMouseUp();
                                                                Thread.Sleep(300);

                                                                // Check again
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
                                                                    var tradePanel = GetTradePanel();
                                                                    success = tradePanel != null && tradePanel.IsVisible;

                                                                    if (success)
                                                                    {
                                                                        BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Successfully opened trade window on second attempt!");
                                                                    }
                                                                }

                                                                if (!success)
                                                                {
                                                                    // Only log failures occasionally to avoid spam
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

                                                        // Update cooldowns
                                                        _instance.LastTimeAny = DateTime.Now;
                                                        _lastAutoJoinPartyAttempt = DateTime.Now;
                                                    }
                                                    else
                                                    {
                                                        // Only log button not found occasionally to avoid spam
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
    }
}
