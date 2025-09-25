﻿using System;
using System.Linq;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace BetterFollowbot;

internal class ImGuiDrawSettings
{

    internal static void DrawImGuiSettings()
    {
        var green = new Vector4(0.102f, 0.388f, 0.106f, 1.000f);
        var red = new Vector4(0.388f, 0.102f, 0.102f, 1.000f);

        var collapsingHeaderFlags = ImGuiTreeNodeFlags.CollapsingHeader;
        ImGui.Text("Plugin by alekswoje (forked from Totalschaden). https://github.com/alekswoje/BetterFollowbot");

        try
        {
            // Input Keys
            ImGui.PushStyleColor(ImGuiCol.Header, green);
            ImGui.PushID(1000);
            if (ImGui.TreeNodeEx("Input Keys", collapsingHeaderFlags))
            {
                BetterFollowbot.Instance.Settings.inputKey1.Value = ImGuiExtension.HotkeySelector(
                    "Skill 2: " + BetterFollowbot.Instance.Settings.inputKey1.Value,
                    BetterFollowbot.Instance.Settings.inputKey1.Value);
                BetterFollowbot.Instance.Settings.inputKey3.Value = ImGuiExtension.HotkeySelector(
                    "Skill 4: " + BetterFollowbot.Instance.Settings.inputKey3.Value,
                    BetterFollowbot.Instance.Settings.inputKey3.Value);
                BetterFollowbot.Instance.Settings.inputKey4.Value = ImGuiExtension.HotkeySelector(
                    "Skill 5: " + BetterFollowbot.Instance.Settings.inputKey4.Value,
                    BetterFollowbot.Instance.Settings.inputKey4.Value);
                BetterFollowbot.Instance.Settings.inputKey5.Value = ImGuiExtension.HotkeySelector(
                    "Skill 6: " + BetterFollowbot.Instance.Settings.inputKey5.Value,
                    BetterFollowbot.Instance.Settings.inputKey5.Value);
                BetterFollowbot.Instance.Settings.inputKey6.Value = ImGuiExtension.HotkeySelector(
                    "Skill 7: " + BetterFollowbot.Instance.Settings.inputKey6.Value,
                    BetterFollowbot.Instance.Settings.inputKey6.Value);
                BetterFollowbot.Instance.Settings.inputKey7.Value = ImGuiExtension.HotkeySelector(
                    "Skill 8: " + BetterFollowbot.Instance.Settings.inputKey7.Value,
                    BetterFollowbot.Instance.Settings.inputKey7.Value);
                BetterFollowbot.Instance.Settings.inputKey8.Value = ImGuiExtension.HotkeySelector(
                    "Skill 9: " + BetterFollowbot.Instance.Settings.inputKey8.Value,
                    BetterFollowbot.Instance.Settings.inputKey8.Value);
                BetterFollowbot.Instance.Settings.inputKey9.Value = ImGuiExtension.HotkeySelector(
                    "Skill 10: " + BetterFollowbot.Instance.Settings.inputKey9.Value,
                    BetterFollowbot.Instance.Settings.inputKey9.Value);
                BetterFollowbot.Instance.Settings.inputKey10.Value = ImGuiExtension.HotkeySelector(
                    "Skill 11: " + BetterFollowbot.Instance.Settings.inputKey10.Value,
                    BetterFollowbot.Instance.Settings.inputKey10.Value);
                BetterFollowbot.Instance.Settings.inputKey11.Value = ImGuiExtension.HotkeySelector(
                    "Skill 12: " + BetterFollowbot.Instance.Settings.inputKey11.Value,
                    BetterFollowbot.Instance.Settings.inputKey11.Value);
                BetterFollowbot.Instance.Settings.inputKey12.Value = ImGuiExtension.HotkeySelector(
                    "Skill 13: " + BetterFollowbot.Instance.Settings.inputKey12.Value,
                    BetterFollowbot.Instance.Settings.inputKey12.Value);
                BetterFollowbot.Instance.Settings.inputKeyPickIt.Value = ImGuiExtension.HotkeySelector(
                    "PickIt: " + BetterFollowbot.Instance.Settings.inputKeyPickIt.Value,
                    BetterFollowbot.Instance.Settings.inputKeyPickIt.Value);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }


        try
        {
            // Auto Pilot
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbot.Instance.Settings.autoPilotEnabled ? green : red);
            ImGui.PushID(0);
            if (ImGui.TreeNodeEx("Auto Pilot", collapsingHeaderFlags))
            {
                BetterFollowbot.Instance.Settings.autoPilotEnabled.Value =
                    ImGuiExtension.Checkbox("Enabled", BetterFollowbot.Instance.Settings.autoPilotEnabled.Value);
                BetterFollowbot.Instance.Settings.autoPilotGrace.Value =
                    ImGuiExtension.Checkbox("No Grace Period", BetterFollowbot.Instance.Settings.autoPilotGrace.Value);
                BetterFollowbot.Instance.Settings.autoPilotLeader = ImGuiExtension.InputText("Leader: ", BetterFollowbot.Instance.Settings.autoPilotLeader, 60, ImGuiInputTextFlags.None);
                if (string.IsNullOrWhiteSpace(BetterFollowbot.Instance.Settings.autoPilotLeader.Value))
                {
                    // Show error message or set a default value
                    BetterFollowbot.Instance.Settings.autoPilotLeader.Value = "DefaultLeader";
                }
                else
                {
                    // Remove any invalid characters from the input
                    BetterFollowbot.Instance.Settings.autoPilotLeader.Value = new string(BetterFollowbot.Instance.Settings.autoPilotLeader.Value.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                }
                BetterFollowbot.Instance.Settings.autoPilotDashEnabled.Value = ImGuiExtension.Checkbox(
                    "Dash", BetterFollowbot.Instance.Settings.autoPilotDashEnabled.Value);
                BetterFollowbot.Instance.Settings.autoPilotCloseFollow.Value = ImGuiExtension.Checkbox(
                    "Close Follow", BetterFollowbot.Instance.Settings.autoPilotCloseFollow.Value);
                BetterFollowbot.Instance.Settings.autoPilotDashKey.Value = ImGuiExtension.HotkeySelector(
                    "Dash Key: " + BetterFollowbot.Instance.Settings.autoPilotDashKey.Value, BetterFollowbot.Instance.Settings.autoPilotDashKey);
                BetterFollowbot.Instance.Settings.autoPilotDashDistance.Value =
                    ImGuiExtension.IntSlider("Dash Distance", BetterFollowbot.Instance.Settings.autoPilotDashDistance);
                BetterFollowbot.Instance.Settings.autoPilotMoveKey.Value = ImGuiExtension.HotkeySelector(
                    "Move Key: " + BetterFollowbot.Instance.Settings.autoPilotMoveKey.Value, BetterFollowbot.Instance.Settings.autoPilotMoveKey);
                BetterFollowbot.Instance.Settings.autoPilotToggleKey.Value = ImGuiExtension.HotkeySelector(
                    "Toggle: " + BetterFollowbot.Instance.Settings.autoPilotToggleKey.Value, BetterFollowbot.Instance.Settings.autoPilotToggleKey);
                /*
                BetterFollowbot.instance.Settings.autoPilotRandomClickOffset.Value =
                    ImGuiExtension.IntSlider("Random Click Offset", BetterFollowbot.instance.Settings.autoPilotRandomClickOffset);
                */
                BetterFollowbot.Instance.Settings.autoPilotInputFrequency.Value =
                    ImGuiExtension.IntSlider("Input Freq", BetterFollowbot.Instance.Settings.autoPilotInputFrequency);
                BetterFollowbot.Instance.Settings.autoPilotPathfindingNodeDistance.Value =
                    ImGuiExtension.IntSlider("Follow Distance", BetterFollowbot.Instance.Settings.autoPilotPathfindingNodeDistance);
                BetterFollowbot.Instance.Settings.autoPilotClearPathDistance.Value =
                    ImGuiExtension.IntSlider("Transition Dist", BetterFollowbot.Instance.Settings.autoPilotClearPathDistance);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }
            




        try
        {
            // Aura Blessing
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbot.Instance.Settings.auraBlessingEnabled ? green : red);
            ImGui.PushID(9);
            if (ImGui.TreeNodeEx("Aura Blessing", collapsingHeaderFlags))
            {
                BetterFollowbot.Instance.Settings.auraBlessingEnabled.Value = ImGuiExtension.Checkbox("Enabled",
                    BetterFollowbot.Instance.Settings.auraBlessingEnabled.Value);
                BetterFollowbot.Instance.Settings.holyRelicHealthThreshold.Value =
                    ImGuiExtension.IntSlider("Holy Relic Health %", BetterFollowbot.Instance.Settings.holyRelicHealthThreshold);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Links
            bool linksEnabled = BetterFollowbot.Instance.Settings.linksEnabled || 
                               BetterFollowbot.Instance.Settings.flameLinkEnabled || 
                               BetterFollowbot.Instance.Settings.protectiveLinkEnabled;
            ImGui.PushStyleColor(ImGuiCol.Header, linksEnabled ? green : red);
            ImGui.PushID(28);
            if (ImGui.TreeNodeEx("Links", collapsingHeaderFlags))
            {
                BetterFollowbot.Instance.Settings.linksEnabled.Value = ImGuiExtension.Checkbox("Enable Links",
                    BetterFollowbot.Instance.Settings.linksEnabled.Value);
                
                ImGui.Separator();
                ImGui.Text("Flame Link:");
                BetterFollowbot.Instance.Settings.flameLinkEnabled.Value = ImGuiExtension.Checkbox("  Flame Link",
                    BetterFollowbot.Instance.Settings.flameLinkEnabled.Value);
                BetterFollowbot.Instance.Settings.flameLinkRange.Value =
                    ImGuiExtension.IntSlider("  Range", BetterFollowbot.Instance.Settings.flameLinkRange);
                BetterFollowbot.Instance.Settings.flameLinkTimeThreshold.Value =
                    ImGuiExtension.IntSlider("  Recast Timer", BetterFollowbot.Instance.Settings.flameLinkTimeThreshold);
                
                ImGui.Separator();
                ImGui.Text("Protective Link:");
                BetterFollowbot.Instance.Settings.protectiveLinkEnabled.Value = ImGuiExtension.Checkbox("  Protective Link",
                    BetterFollowbot.Instance.Settings.protectiveLinkEnabled.Value);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Smite Buff - independent of Flame Link
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbot.Instance.Settings.smiteEnabled ? green : red);
            ImGui.PushID(29);
            if (ImGui.TreeNodeEx("Smite Buff", collapsingHeaderFlags))
            {
                bool currentValue = BetterFollowbot.Instance.Settings.smiteEnabled.Value;
                if (ImGuiExtension.Checkbox("Enabled", currentValue) != currentValue)
                {
                    BetterFollowbot.Instance.Settings.smiteEnabled.Value = !currentValue;
                }
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Rejuvenation Totem
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbot.Instance.Settings.rejuvenationTotemEnabled ? green : red);
            ImGui.PushID(36);
            if (ImGui.TreeNodeEx("Rejuvenation Totem", collapsingHeaderFlags))
            {
                BetterFollowbot.Instance.Settings.rejuvenationTotemEnabled.Value = ImGuiExtension.Checkbox("Enabled",
                    BetterFollowbot.Instance.Settings.rejuvenationTotemEnabled.Value);
                BetterFollowbot.Instance.Settings.rejuvenationTotemRange.Value =
                    ImGuiExtension.IntSlider("Monster Detection Range", BetterFollowbot.Instance.Settings.rejuvenationTotemRange);
                BetterFollowbot.Instance.Settings.rejuvenationTotemHpThreshold.Value =
                    ImGuiExtension.IntSlider("Total Pool Threshold %", BetterFollowbot.Instance.Settings.rejuvenationTotemHpThreshold);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Vaal Skills - independent of Flame Link
            bool vaalSkillsEnabled = BetterFollowbot.Instance.Settings.vaalHasteEnabled || BetterFollowbot.Instance.Settings.vaalDisciplineEnabled;
            ImGui.PushStyleColor(ImGuiCol.Header, vaalSkillsEnabled ? green : red);
            ImGui.PushID(30);
            if (ImGui.TreeNodeEx("Vaal Skills", collapsingHeaderFlags))
            {
                // Single checkbox that controls both Vaal skills
                bool combinedVaalEnabled = BetterFollowbot.Instance.Settings.vaalHasteEnabled && BetterFollowbot.Instance.Settings.vaalDisciplineEnabled;
                bool newCombinedState = ImGuiExtension.Checkbox("Enable Vaal Skills", combinedVaalEnabled);

                if (newCombinedState != combinedVaalEnabled)
                {
                    BetterFollowbot.Instance.Settings.vaalHasteEnabled.Value = newCombinedState;
                    BetterFollowbot.Instance.Settings.vaalDisciplineEnabled.Value = newCombinedState;
                }

                // Individual skill checkboxes (always available)
                ImGui.Indent();

                bool vaalHasteValue = BetterFollowbot.Instance.Settings.vaalHasteEnabled.Value;
                if (ImGuiExtension.Checkbox("Vaal Haste", vaalHasteValue) != vaalHasteValue)
                {
                    BetterFollowbot.Instance.Settings.vaalHasteEnabled.Value = !vaalHasteValue;
                }

                bool vaalDisciplineValue = BetterFollowbot.Instance.Settings.vaalDisciplineEnabled.Value;
                if (ImGuiExtension.Checkbox("Vaal Discipline", vaalDisciplineValue) != vaalDisciplineValue)
                {
                    BetterFollowbot.Instance.Settings.vaalDisciplineEnabled.Value = !vaalDisciplineValue;
                }
                BetterFollowbot.Instance.Settings.vaalDisciplineEsp.Value =
                    ImGuiExtension.IntSlider("Vaal Discipline ES%", BetterFollowbot.Instance.Settings.vaalDisciplineEsp);
                ImGui.Unindent();
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Warcries
            bool warcriesEnabled = BetterFollowbot.Instance.Settings.warcriesEnabled.Value;
            ImGui.PushStyleColor(ImGuiCol.Header, warcriesEnabled ? green : red);
            ImGui.PushID(37);
            if (ImGui.TreeNodeEx("Warcries", collapsingHeaderFlags))
            {
                // Master enable/disable for all warcries
                bool masterWarcryEnabled = BetterFollowbot.Instance.Settings.warcriesEnabled.Value;
                bool newMasterState = ImGuiExtension.Checkbox("Enable Warcries", masterWarcryEnabled);

                if (newMasterState != masterWarcryEnabled)
                {
                    BetterFollowbot.Instance.Settings.warcriesEnabled.Value = newMasterState;
                }

                if (warcriesEnabled)
                {
                    ImGui.Indent();
                    
                    // Individual warcry checkboxes
                    bool ancestralCryValue = BetterFollowbot.Instance.Settings.ancestralCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Ancestral Cry", ancestralCryValue) != ancestralCryValue)
                    {
                        BetterFollowbot.Instance.Settings.ancestralCryEnabled.Value = !ancestralCryValue;
                    }

                    bool infernalCryValue = BetterFollowbot.Instance.Settings.infernalCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Infernal Cry", infernalCryValue) != infernalCryValue)
                    {
                        BetterFollowbot.Instance.Settings.infernalCryEnabled.Value = !infernalCryValue;
                    }

                    bool generalsCryValue = BetterFollowbot.Instance.Settings.generalsCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("General's Cry", generalsCryValue) != generalsCryValue)
                    {
                        BetterFollowbot.Instance.Settings.generalsCryEnabled.Value = !generalsCryValue;
                    }

                    bool intimidatingCryValue = BetterFollowbot.Instance.Settings.intimidatingCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Intimidating Cry", intimidatingCryValue) != intimidatingCryValue)
                    {
                        BetterFollowbot.Instance.Settings.intimidatingCryEnabled.Value = !intimidatingCryValue;
                    }

                    bool rallyingCryValue = BetterFollowbot.Instance.Settings.rallyingCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Rallying Cry", rallyingCryValue) != rallyingCryValue)
                    {
                        BetterFollowbot.Instance.Settings.rallyingCryEnabled.Value = !rallyingCryValue;
                    }

                    bool vengefulCryValue = BetterFollowbot.Instance.Settings.vengefulCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Vengeful Cry", vengefulCryValue) != vengefulCryValue)
                    {
                        BetterFollowbot.Instance.Settings.vengefulCryEnabled.Value = !vengefulCryValue;
                    }

                    bool enduringCryValue = BetterFollowbot.Instance.Settings.enduringCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Enduring Cry", enduringCryValue) != enduringCryValue)
                    {
                        BetterFollowbot.Instance.Settings.enduringCryEnabled.Value = !enduringCryValue;
                    }

                    bool seismicCryValue = BetterFollowbot.Instance.Settings.seismicCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Seismic Cry", seismicCryValue) != seismicCryValue)
                    {
                        BetterFollowbot.Instance.Settings.seismicCryEnabled.Value = !seismicCryValue;
                    }

                    bool battlemagesCryValue = BetterFollowbot.Instance.Settings.battlemagesCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Battlemage's Cry", battlemagesCryValue) != battlemagesCryValue)
                    {
                        BetterFollowbot.Instance.Settings.battlemagesCryEnabled.Value = !battlemagesCryValue;
                    }

                    ImGui.Unindent();
                }
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Mines
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbot.Instance.Settings.minesEnabled ? green : red);
            ImGui.PushID(31);
            if (ImGui.TreeNodeEx("Mines", collapsingHeaderFlags))
            {
                BetterFollowbot.Instance.Settings.minesEnabled.Value = ImGuiExtension.Checkbox("Enabled",
                    BetterFollowbot.Instance.Settings.minesEnabled.Value);
                BetterFollowbot.Instance.Settings.minesRange = ImGuiExtension.InputText("Range",
                    BetterFollowbot.Instance.Settings.minesRange, 60, ImGuiInputTextFlags.None);
                BetterFollowbot.Instance.Settings.minesLeaderDistance = ImGuiExtension.InputText("Leader Distance",
                    BetterFollowbot.Instance.Settings.minesLeaderDistance, 60, ImGuiInputTextFlags.None);
                BetterFollowbot.Instance.Settings.minesStormblastEnabled.Value = ImGuiExtension.Checkbox("Stormblast",
                    BetterFollowbot.Instance.Settings.minesStormblastEnabled.Value);
                BetterFollowbot.Instance.Settings.minesPyroclastEnabled.Value = ImGuiExtension.Checkbox("Pyroclast",
                    BetterFollowbot.Instance.Settings.minesPyroclastEnabled.Value);
                BetterFollowbot.Instance.Settings.maxMines.Value = ImGuiExtension.IntSlider("Max Mines",
                    BetterFollowbot.Instance.Settings.maxMines);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Summon Minions
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbot.Instance.Settings.summonSkeletonsEnabled ? green : red);
            ImGui.PushID(33);
            if (ImGui.TreeNodeEx("Summon Minions", collapsingHeaderFlags))
            {
                BetterFollowbot.Instance.Settings.summonSkeletonsEnabled.Value = ImGuiExtension.Checkbox("Auto Summon Skeletons",
                    BetterFollowbot.Instance.Settings.summonSkeletonsEnabled.Value);

                BetterFollowbot.Instance.Settings.summonSkeletonsRange.Value =
                    ImGuiExtension.IntSlider("Range", BetterFollowbot.Instance.Settings.summonSkeletonsRange);

                BetterFollowbot.Instance.Settings.summonSkeletonsMinCount.Value =
                    ImGuiExtension.IntSlider("Min Count", BetterFollowbot.Instance.Settings.summonSkeletonsMinCount);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // SRS (Summon Raging Spirits) toggle
                BetterFollowbot.Instance.Settings.summonRagingSpiritsEnabled.Value = ImGuiExtension.Checkbox("Enable SRS (Summon Raging Spirits)",
                    BetterFollowbot.Instance.Settings.summonRagingSpiritsEnabled.Value);

                BetterFollowbot.Instance.Settings.summonRagingSpiritsMinCount.Value =
                    ImGuiExtension.IntSlider("SRS Min Count", BetterFollowbot.Instance.Settings.summonRagingSpiritsMinCount);

                BetterFollowbot.Instance.Settings.summonRagingSpiritsMagicNormal.Value = ImGuiExtension.Checkbox("Include Magic/White enemies",
                    BetterFollowbot.Instance.Settings.summonRagingSpiritsMagicNormal.Value);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Auto Respawn
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbot.Instance.Settings.autoRespawnEnabled ? green : red);
            ImGui.PushID(32);
            if (ImGui.TreeNodeEx("Auto Respawn", collapsingHeaderFlags))
            {
                // Simple Enable/Disable button
                bool isRespawnEnabled = BetterFollowbot.Instance.Settings.autoRespawnEnabled.Value;
                string buttonText = isRespawnEnabled ? "Disable" : "Enable";
                if (ImGui.Button($"{buttonText} Auto Respawn"))
                {
                    BetterFollowbot.Instance.Settings.autoRespawnEnabled.Value = !isRespawnEnabled;
                    BetterFollowbot.Instance.LogMessage($"AUTO RESPAWN: {buttonText}d - new value: {!isRespawnEnabled}");
                }

                // Show current status
                ImGui.Text($"Status: {(isRespawnEnabled ? "Enabled" : "Disabled")}");
                
                // Wait for leader after respawn setting
                if (isRespawnEnabled)
                {
                    ImGui.Separator();
                    BetterFollowbot.Instance.Settings.waitForLeaderAfterRespawn.Value = 
                        ImGuiExtension.Checkbox("Wait for Leader After Respawn", BetterFollowbot.Instance.Settings.waitForLeaderAfterRespawn.Value);
                    
                    ImGui.Text("When enabled, the bot will not create transition tasks");
                    ImGui.Text("after respawning until the leader returns to the same zone.");
                }
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Auto Level Gems
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbot.Instance.Settings.autoLevelGemsEnabled ? green : red);
            ImGui.PushID(34);
            if (ImGui.TreeNodeEx("Auto Level Gems", collapsingHeaderFlags))
            {
                // Simple Enable/Disable button
                bool isGemsEnabled = BetterFollowbot.Instance.Settings.autoLevelGemsEnabled.Value;
                string buttonText = isGemsEnabled ? "Disable" : "Enable";
                if (ImGui.Button($"{buttonText} Auto Level Gems"))
                {
                    BetterFollowbot.Instance.Settings.autoLevelGemsEnabled.Value = !isGemsEnabled;
                    BetterFollowbot.Instance.LogMessage($"AUTO LEVEL GEMS: {buttonText}d - new value: {!isGemsEnabled}");
                }

                // Show current status
                ImGui.Text($"Status: {(isGemsEnabled ? "Enabled" : "Disabled")}");

                // Cooldown setting
                ImGui.Separator();
                ImGui.Text("Gem Leveling Cooldown:");
                BetterFollowbot.Instance.Settings.gemLevelingCooldown.Value = 
                    ImGuiExtension.FloatSlider("Cooldown (seconds)", BetterFollowbot.Instance.Settings.gemLevelingCooldown);
                ImGui.Text($"Current: {BetterFollowbot.Instance.Settings.gemLevelingCooldown.Value:F2}s between gem levels");

                if (ImGui.Button("Test Toggle"))
                {
                    var testOldValue = BetterFollowbot.Instance.Settings.autoLevelGemsEnabled.Value;
                    BetterFollowbot.Instance.Settings.autoLevelGemsEnabled.Value = !testOldValue;
                    BetterFollowbot.Instance.LogMessage($"AUTO LEVEL GEMS: Test toggled from {testOldValue} to {BetterFollowbot.Instance.Settings.autoLevelGemsEnabled.Value}");
                }
            }
        }
        catch (Exception e)
        {
            BetterFollowbot.Instance.LogMessage($"AUTO LEVEL GEMS UI ERROR: {e.Message}");
        }

        try
        {
            // Auto Join Party & Accept Trade
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbot.Instance.Settings.autoJoinPartyEnabled ? green : red);
            ImGui.PushID(35);
            if (ImGui.TreeNodeEx("Auto Join Party & Accept Trade", collapsingHeaderFlags))
            {
                BetterFollowbot.Instance.Settings.autoJoinPartyEnabled.Value =
                    ImGuiExtension.Checkbox("Auto Join Party & Accept Trade Invites", BetterFollowbot.Instance.Settings.autoJoinPartyEnabled.Value);

                // Debug: Show current value
                ImGui.Text($"Current: {BetterFollowbot.Instance.Settings.autoJoinPartyEnabled.Value}");
            }
        }
        catch (Exception e)
        {
            BetterFollowbot.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE UI ERROR: {e.Message}");
        }

        try
        {
            // General Settings
            ImGui.PushStyleColor(ImGuiCol.Header, green);
            ImGui.PushID(38);
            if (ImGui.TreeNodeEx("General Settings", collapsingHeaderFlags))
            {
                BetterFollowbot.Instance.Settings.disableSkillsInHideout.Value =
                    ImGuiExtension.Checkbox("Disable Skills in Hideouts", BetterFollowbot.Instance.Settings.disableSkillsInHideout.Value);

                ImGui.Text("When enabled, skills will be blocked in hideouts for safety.");
                ImGui.Text("Disable this to test skills in hideouts.");
                
                ImGui.Separator();
                ImGui.Text("Skill Cooldown:");
                BetterFollowbot.Instance.Settings.skillCooldown.Value = 
                    ImGuiExtension.FloatSlider("Individual Skill Cooldown (seconds)", BetterFollowbot.Instance.Settings.skillCooldown);
                ImGui.Text($"Each skill can be used every {BetterFollowbot.Instance.Settings.skillCooldown.Value:F2}s");
            }
        }
        catch (Exception e)
        {
            BetterFollowbot.Instance.LogMessage($"GENERAL SETTINGS UI ERROR: {e.Message}");
        }

        //ImGui.End();
    }
}
