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

                var gemLvlUpPanel = _instance.GetGemLvlUpPanel();
                if (gemLvlUpPanel?.IsVisible != true) return;

                var gemsToLvlUp = gemLvlUpPanel.GemsToLvlUp;
                if (gemsToLvlUp?.Count == 0) return;

                foreach (var gem in gemsToLvlUp)
                {
                    if (gem?.IsVisible != true) continue;

                    try
                    {
                        var gemChildren = gem.Children;
                        if (gemChildren?.Count <= 3) continue;

                        var gemStatusText = gemChildren[3]?.Text ?? "";
                        
                        if (gemStatusText.Contains("Gem cannot level up"))
                            continue;

                        if (!gemStatusText.Contains("Click to level up") && !string.IsNullOrWhiteSpace(gemStatusText))
                            continue;

                        var timeSinceLastLevel = DateTime.Now - _lastGemLevelTime;
                        if (timeSinceLastLevel.TotalSeconds < _settings.gemLevelingCooldown.Value)
                            return;

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
                        break;
                    }
                    catch (Exception gemEx)
                    {
                        BetterFollowbot.Instance.LogMessage($"AUTO LEVEL GEMS: Error processing gem - {gemEx.Message}");
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
