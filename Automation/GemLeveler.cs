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
    internal class GemLeveler : IAutomation
    {
        private readonly BetterFollowbot _instance;
        private readonly BetterFollowbotSettings _settings;

        public GemLeveler(BetterFollowbot instance, BetterFollowbotSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        /// <summary>
        /// Checks if any blocking UI elements are open that should prevent gem leveling
        /// </summary>
        private bool IsBlockingUiOpen()
        {
            return UIBlockingUtility.IsAnyBlockingUIOpen();
        }

        /// <summary>
        /// Checks if the player is dead (resurrect panel is visible)
        /// </summary>
        private bool IsPlayerDead()
        {
            try
            {
                var resurrectPanel = _instance.GetResurrectPanel();
                return resurrectPanel != null && resurrectPanel.IsVisible;
            }
            catch
            {
                return true;
            }
        }

        public bool IsEnabled => _settings.autoLevelGemsEnabled;

        public string AutomationName => "Auto Level Gems";

        private DateTime _lastGemLevelTime = DateTime.MinValue;

        public void Execute()
        {
            if (!_settings.autoLevelGemsEnabled || !_instance.Gcd()) return;

            try
            {
                var playerDead = IsPlayerDead();
                var inventoryOpen = _instance.GameController.IngameState.IngameUi.InventoryPanel.IsVisible;
                var gameNotFocused = !_instance.GameController.IsForeGroundCache;
                var blockingUiOpen = IsBlockingUiOpen();
                var gameLoading = _instance.GameController.IsLoading;
                var notInGame = !_instance.GameController.InGame;

                if (playerDead || inventoryOpen || gameNotFocused || blockingUiOpen || gameLoading || notInGame)
                    return;

                // Only level gems when within follow range of the leader (don't interfere with other tasks)
                if (!_instance.IsWithinFollowRange())
                    return;

                var gemLvlUpPanel = _instance.GetGemLvlUpPanel();
                if (gemLvlUpPanel?.IsVisible != true) return;

                var gemsToLvlUp = gemLvlUpPanel.GemsToLvlUp;
                if (gemsToLvlUp?.Count == 0) return;

                // Check cooldown before attempting to level any gems
                var timeSinceLastLevel = DateTime.Now - _lastGemLevelTime;
                if (timeSinceLastLevel.TotalSeconds < _settings.gemLevelingCooldown.Value)
                    return;

                // NEW: Check for "Upgrade All" button (0th element)
                if (gemsToLvlUp.Count > 0 && gemsToLvlUp[0]?.IsVisible == true)
                {
                    try
                    {
                        var upgradeAllElement = gemsToLvlUp[0];
                        var upgradeAllChildren = upgradeAllElement.Children;
                        
                        if (upgradeAllChildren?.Count > 0)
                        {
                            var upgradeAllButton = upgradeAllChildren[0];
                            if (upgradeAllButton?.IsVisible == true)
                            {
                                var buttonRect = upgradeAllButton.GetClientRectCache;
                                var buttonCenter = buttonRect.Center;

                                Mouse.SetCursorPos(buttonCenter);
                                Thread.Sleep(150);

                                var currentMousePos = _instance.GetMousePosition();
                                var distanceFromTarget = Vector2.Distance(currentMousePos, buttonCenter);

                                if (distanceFromTarget < 5)
                                {
                                    Mouse.LeftMouseDown();
                                    Thread.Sleep(40);
                                    Mouse.LeftMouseUp();
                                    Thread.Sleep(200);

                                    _instance.LastTimeAny = DateTime.Now;
                                    _lastGemLevelTime = DateTime.Now;
                                    BetterFollowbot.Instance.LogMessage("AUTO LEVEL GEMS: Clicked 'Upgrade All' button");
                                    return; // Done, exit after clicking upgrade all
                                }
                            }
                        }
                    }
                    catch (Exception upgradeAllEx)
                    {
                        BetterFollowbot.Instance.LogMessage($"AUTO LEVEL GEMS: Error processing upgrade all button - {upgradeAllEx.Message}");
                    }
                }

                // NEW: If upgrade all button not visible, check for individual gems (1st element)
                if (gemsToLvlUp.Count > 1 && gemsToLvlUp[1]?.IsVisible == true)
                {
                    try
                    {
                        var gemsContainerElement = gemsToLvlUp[1];
                        var gemsContainerChildren = gemsContainerElement.Children;
                        
                        if (gemsContainerChildren?.Count > 0)
                        {
                            // Get the 0th child which contains the individual gems
                            var individualGemsContainer = gemsContainerChildren[0];
                            var individualGems = individualGemsContainer?.Children;
                            
                            if (individualGems?.Count > 0)
                            {
                                foreach (var gem in individualGems)
                                {
                                    if (gem?.IsVisible != true) continue;

                                    try
                                    {
                                        var gemChildren = gem.Children;
                                        if (gemChildren?.Count <= 3) continue;

                                        // Check the 3rd child for status text
                                        var gemStatusText = gemChildren[3]?.Text ?? "";
                                        
                                        // Only level up if it says "Click to level up"
                                        if (!gemStatusText.Contains("Click to level up"))
                                            continue;

                                        // Get the 1st child which is the level up button
                                        var levelUpButton = gemChildren[1];
                                        if (levelUpButton?.IsVisible != true) continue;

                                        var buttonRect = levelUpButton.GetClientRectCache;
                                        var buttonCenter = buttonRect.Center;

                                        Mouse.SetCursorPos(buttonCenter);
                                        Thread.Sleep(150);

                                        var currentMousePos = _instance.GetMousePosition();
                                        var distanceFromTarget = Vector2.Distance(currentMousePos, buttonCenter);

                                        if (distanceFromTarget < 5)
                                        {
                                            Mouse.LeftMouseDown();
                                            Thread.Sleep(40);
                                            Mouse.LeftMouseUp();
                                            Thread.Sleep(200);

                                            var buttonStillVisible = levelUpButton.IsVisible;
                                            if (buttonStillVisible)
                                            {
                                                Thread.Sleep(500);
                                                Mouse.LeftMouseDown();
                                                Thread.Sleep(40);
                                                Mouse.LeftMouseUp();
                                                Thread.Sleep(200);
                                            }
                                        }

                                        Thread.Sleep(300);
                                        _instance.LastTimeAny = DateTime.Now;
                                        _lastGemLevelTime = DateTime.Now;
                                        BetterFollowbot.Instance.LogMessage($"AUTO LEVEL GEMS: Leveled individual gem");
                                        break; // Only level one gem per execution
                                    }
                                    catch (Exception gemEx)
                                    {
                                        BetterFollowbot.Instance.LogMessage($"AUTO LEVEL GEMS: Error processing individual gem - {gemEx.Message}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception individualGemsEx)
                    {
                        BetterFollowbot.Instance.LogMessage($"AUTO LEVEL GEMS: Error processing individual gems - {individualGemsEx.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                BetterFollowbot.Instance.LogMessage($"AUTO LEVEL GEMS: Exception - {e.Message}");
            }
        }
    }
}
