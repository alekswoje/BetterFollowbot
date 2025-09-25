using System;
using System.Threading;
using BetterFollowbotLite.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BetterFollowbotLite.Automation
{
    internal class GemLeveler : IAutomation
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public GemLeveler(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        /// <summary>
        /// Checks if any blocking UI elements are open that should prevent gem leveling
        /// </summary>
        private bool IsBlockingUiOpen()
        {
            try
            {
                // Check common blocking UI elements
                var stashOpen = _instance.GameController?.IngameState?.IngameUi?.StashElement?.IsVisibleLocal == true;
                var npcDialogOpen = _instance.GameController?.IngameState?.IngameUi?.NpcDialog?.IsVisible == true;
                var sellWindowOpen = _instance.GameController?.IngameState?.IngameUi?.SellWindow?.IsVisible == true;
                var purchaseWindowOpen = _instance.GameController?.IngameState?.IngameUi?.PurchaseWindow?.IsVisible == true;
                var inventoryOpen = _instance.GameController?.IngameState?.IngameUi?.InventoryPanel?.IsVisible == true;
                var skillTreeOpen = _instance.GameController?.IngameState?.IngameUi?.TreePanel?.IsVisible == true;
                var atlasOpen = _instance.GameController?.IngameState?.IngameUi?.Atlas?.IsVisible == true;

                // Note: Map is non-obstructing in PoE, so we don't check it
                return stashOpen || npcDialogOpen || sellWindowOpen || purchaseWindowOpen || inventoryOpen || skillTreeOpen || atlasOpen;
            }
            catch
            {
                // If we can't check UI state, err on the side of caution
                return true;
            }
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
                // If we can't check death state, err on the side of caution
                return true;
            }
        }

        public bool IsEnabled => _settings.autoLevelGemsEnabled;

        public string AutomationName => "Auto Level Gems";

        private DateTime _lastGemLevelTime = DateTime.MinValue;

        public void Execute()
        {
            if (_settings.autoLevelGemsEnabled && _instance.Gcd())
            {
                try
                {
                    // Protection checks - don't level gems if:
                    // 1. Player is dead (resurrect panel is visible)
                    // 2. Inventory is open
                    // 3. Game is not focused
                    // 4. Game is paused/menu is open
                    // 5. Game is loading
                    // 6. Not in game
                    var playerDead = IsPlayerDead();
                    var inventoryOpen = _instance.GameController.IngameState.IngameUi.InventoryPanel.IsVisible;
                    var gameNotFocused = !_instance.GameController.IsForeGroundCache;
                    var blockingUiOpen = IsBlockingUiOpen();
                    var gameLoading = _instance.GameController.IsLoading;
                    var notInGame = !_instance.GameController.InGame;

                    if (playerDead || inventoryOpen || gameNotFocused || blockingUiOpen || gameLoading || notInGame)
                    {
                        BetterFollowbotLite.Instance.LogMessage($"AUTO LEVEL GEMS: Skipping - PlayerDead: {playerDead}, InventoryOpen: {inventoryOpen}, GameNotFocused: {gameNotFocused}, BlockingUiOpen: {blockingUiOpen}, Loading: {gameLoading}, NotInGame: {notInGame}");
                        return;
                    }

                    // Check if the gem level up panel is visible
                    var gemLvlUpPanel = _instance.GetGemLvlUpPanel();
                    if (gemLvlUpPanel != null && gemLvlUpPanel.IsVisible)
                    {
                        // Get the array of gems to level up
                        var gemsToLvlUp = gemLvlUpPanel.GemsToLvlUp;
                        if (gemsToLvlUp != null && gemsToLvlUp.Count > 0)
                        {

                            // Process each gem in the array
                            foreach (var gem in gemsToLvlUp)
                            {
                                if (gem != null && gem.IsVisible)
                                {
                                    try
                                    {
                                        // Get the children of the gem element
                                        var gemChildren = gem.Children;
                                        if (gemChildren != null && gemChildren.Count > 3)
                                        {
                                            // Check if gemChildren[3] exists and contains text about leveling up
                                            var gemStatusText = gemChildren[3]?.Text ?? "";
                                            BetterFollowbotLite.Instance.LogMessage($"AUTO LEVEL GEMS: Gem status text: '{gemStatusText}'");

                                            if (gemStatusText.Contains("Gem cannot level up"))
                                            {
                                                BetterFollowbotLite.Instance.LogMessage("AUTO LEVEL GEMS: Skipping gem that cannot level up");
                                                continue; // Skip this gem
                                            }

                                            if (!gemStatusText.Contains("Click to level up") && !string.IsNullOrWhiteSpace(gemStatusText))
                                            {
                                                BetterFollowbotLite.Instance.LogMessage($"AUTO LEVEL GEMS: Skipping gem with unknown status: '{gemStatusText}'");
                                                continue; // Skip gems with unknown status
                                            }

                                            // Check cooldown between gem leveling (configurable)
                                            var timeSinceLastLevel = DateTime.Now - _lastGemLevelTime;
                                            if (timeSinceLastLevel.TotalSeconds < _settings.gemLevelingCooldown.Value)
                                            {
                                                BetterFollowbotLite.Instance.LogMessage($"AUTO LEVEL GEMS: Waiting for cooldown, {_settings.gemLevelingCooldown.Value - timeSinceLastLevel.TotalSeconds:F2}s remaining");
                                                return; // Wait for cooldown
                                            }

                                            // Get the second child ([1]) which contains the level up button
                                            var levelUpButton = gemChildren[1];
                                            if (levelUpButton != null && levelUpButton.IsVisible)
                                            {
                                                // Get the center position of the level up button
                                                var buttonRect = levelUpButton.GetClientRectCache;
                                                var buttonCenter = buttonRect.Center;

                                                // Removed excessive gem leveling position logging

                                                // Move mouse to the button and click
                                                Mouse.SetCursorPos(buttonCenter);

                                                // Wait for mouse to settle
                                                Thread.Sleep(150);

                                                // Verify mouse position
                                                var currentMousePos = _instance.GetMousePosition();
                                                var distanceFromTarget = Vector2.Distance(currentMousePos, buttonCenter);
                                                // Removed excessive mouse distance logging

                                                if (distanceFromTarget < 5) // Close enough to target
                                                {
                                                    // Perform click with verification
                                                    // Removed excessive click attempt logging

                                                    // First click attempt - use synchronous mouse events
                                                    Mouse.LeftMouseDown();
                                                    Thread.Sleep(40);
                                                    Mouse.LeftMouseUp();
                                                Thread.Sleep(200);

                                                    // Check if button is still visible (if not, click was successful)
                                                    var buttonStillVisible = levelUpButton.IsVisible;
                                                    if (!buttonStillVisible)
                                                    {
// Removed excessive click success logging
                                                    }
                                                    else
                                                    {
                                                        // Removed excessive second click attempt logging

                                                        // Exponential backoff: wait longer before second attempt
                                                        Thread.Sleep(500);
                                                        Mouse.LeftMouseDown();
                                                        Thread.Sleep(40);
                                                        Mouse.LeftMouseUp();
                                                        Thread.Sleep(200);

                                                        // Final check
                                                        buttonStillVisible = levelUpButton.IsVisible;
                                                        if (!buttonStillVisible)
                                                        {
// Removed excessive second click success logging
                                                        }
                                                        else
                                                        {
                                                            // Removed excessive click failure logging
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    // Removed excessive mouse positioning failure logging
                                                }

                                                // Add delay between gem level ups
                                                Thread.Sleep(300);

                                                // Removed excessive gem level up completion logging

                                                // Update global cooldown after leveling a gem
                                                _instance.LastTimeAny = DateTime.Now;

                                                // Update gem leveling cooldown
                                                _lastGemLevelTime = DateTime.Now;

                                                // Only level up one gem per frame to avoid spam
                                                break;
                                            }
                                            else
                                            {
                                                BetterFollowbotLite.Instance.LogMessage("AUTO LEVEL GEMS: Level up button not found or not visible");
                                            }
                                        }
                                        else
                                        {
                                            BetterFollowbotLite.Instance.LogMessage("AUTO LEVEL GEMS: Gem children not found or insufficient count (need at least 4 children)");
                                        }
                                    }
                                    catch (Exception gemEx)
                                    {
                                        BetterFollowbotLite.Instance.LogMessage($"AUTO LEVEL GEMS: Error processing individual gem - {gemEx.Message}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            BetterFollowbotLite.Instance.LogMessage("AUTO LEVEL GEMS: No gems available for leveling");
                        }
                    }
                }
                catch (Exception e)
                {
                    BetterFollowbotLite.Instance.LogMessage($"AUTO LEVEL GEMS: Exception occurred - {e.Message}");
                }
            }
        }
    }
}
