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
    internal class RespawnHandler : IAutomation
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public RespawnHandler(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.autoRespawnEnabled;

        public string AutomationName => "Auto Respawn";

        public void Execute()
        {
            try
            {
                if (_settings.autoRespawnEnabled && _instance.Gcd())
                {
                    // Check if the resurrect panel is visible
                    var resurrectPanel = _instance.GetResurrectPanel();
                    if (resurrectPanel != null && resurrectPanel.IsVisible)
                    {
                        // Check if the resurrect at checkpoint button is available
                        var resurrectAtCheckpoint = resurrectPanel.ResurrectAtCheckpoint;
                        if (resurrectAtCheckpoint != null && resurrectAtCheckpoint.IsVisible)
                        {
                            BetterFollowbotLite.Instance.LogMessage("AUTO RESPAWN: Respawn panel detected, attempting checkpoint respawn");

                            // Get the center position of the checkpoint respawn button
                            var checkpointRect = resurrectAtCheckpoint.GetClientRectCache;
                            var checkpointCenter = checkpointRect.Center;

                            BetterFollowbotLite.Instance.LogMessage($"AUTO RESPAWN: Checkpoint button position - X: {checkpointCenter.X:F1}, Y: {checkpointCenter.Y:F1}");

                            // Move mouse to the checkpoint respawn button with proper timing
                            Mouse.SetCursorPos(checkpointCenter);

                            // Wait longer to ensure mouse movement is registered and UI is ready
                            Thread.Sleep(200);

                            // Verify the mouse is actually at the target position
                            var currentMousePos = _instance.GetMousePosition();
                            var distanceFromTarget = Vector2.Distance(currentMousePos, checkpointCenter);

                            if (distanceFromTarget < 10) // Within reasonable tolerance
                            {
                                BetterFollowbotLite.Instance.LogMessage($"AUTO RESPAWN: Mouse positioned correctly (distance: {distanceFromTarget:F1}), performing click");

                                // Perform the click with proper timing
                                Mouse.LeftMouseDown();
                                Thread.Sleep(40);
                                Mouse.LeftMouseUp();
                                Thread.Sleep(150); // Wait after click

                                // Verify click was successful by checking if panel is still visible
                                Thread.Sleep(500); // Give time for respawn to process

                                var panelStillVisible = resurrectPanel.IsVisible;
                                if (!panelStillVisible)
                                {
                                    BetterFollowbotLite.Instance.LogMessage("AUTO RESPAWN: Checkpoint respawn successful - panel disappeared");
                                    
                                    // Set waiting state if the setting is enabled
                                    if (_settings.waitForLeaderAfterRespawn)
                                    {
                                        _instance.SetWaitingForLeaderAfterRespawn(true);
                                    }
                                }
                                else
                                {
                                    BetterFollowbotLite.Instance.LogMessage("AUTO RESPAWN: Checkpoint respawn may have failed - panel still visible, retrying...");

                                    // Retry with a longer delay
                                    Thread.Sleep(300);
                                    Mouse.SetCursorPos(checkpointCenter);
                                    Thread.Sleep(300);
                                    Mouse.LeftMouseDown();
                                    Thread.Sleep(40);
                                    Mouse.LeftMouseUp();
                                    Thread.Sleep(200);
                                    
                                    // Check again after retry
                                    Thread.Sleep(500);
                                    var panelStillVisibleAfterRetry = resurrectPanel.IsVisible;
                                    if (!panelStillVisibleAfterRetry && _settings.waitForLeaderAfterRespawn)
                                    {
                                        _instance.SetWaitingForLeaderAfterRespawn(true);
                                    }
                                }

                                _instance.LastTimeAny = DateTime.Now; // Update global cooldown
                            }
                            else
                            {
                                BetterFollowbotLite.Instance.LogMessage($"AUTO RESPAWN: Mouse positioning failed - distance from target: {distanceFromTarget:F1}");
                            }
                        }
                        else
                        {
                            BetterFollowbotLite.Instance.LogMessage("AUTO RESPAWN: Checkpoint respawn button not available or not visible");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                BetterFollowbotLite.Instance.LogMessage($"AUTO RESPAWN: Exception occurred - {e.Message}");
            }
        }
    }
}
