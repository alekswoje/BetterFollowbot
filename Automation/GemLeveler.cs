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

        public bool IsEnabled => _settings.autoLevelGemsEnabled;

        public string AutomationName => "Auto Level Gems";

        public void Execute()
        {
            if (_settings.autoLevelGemsEnabled && _instance.Gcd())
            {
                try
                {

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
                                        if (gemChildren != null && gemChildren.Count > 1)
                                        {
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
                                            BetterFollowbotLite.Instance.LogMessage("AUTO LEVEL GEMS: Gem children not found or insufficient count");
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
