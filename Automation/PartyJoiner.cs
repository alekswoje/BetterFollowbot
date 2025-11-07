using System;
using System.Linq;
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
                                            catch
                                            {
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
                                                    shouldProcess = !isInParty;
                                                    if (isInParty)
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
                                                        var buttonRect = acceptButton.GetClientRectCache;
                                                        var buttonCenter = buttonRect.Center;

                                                        Mouse.SetCursorPos(buttonCenter);
                                                        Thread.Sleep(300);

                                                        var currentMousePos = _instance.GetMousePosition();
                                                        var distanceFromTarget = Vector2.Distance(currentMousePos, buttonCenter);

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
                                                                var tradePanel = GetTradePanel();
                                                                success = tradePanel != null && tradePanel.IsVisible;

                                                                if (success)
                                                                {
                                                                    BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Successfully opened trade window!");
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
                                                                    var tradePanel = GetTradePanel();
                                                                    success = tradePanel != null && tradePanel.IsVisible;

                                                                    if (success)
                                                                    {
                                                                        BetterFollowbot.Instance.LogMessage("AUTO JOIN PARTY & ACCEPT TRADE: Successfully opened trade window on second attempt!");
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
    }
}
