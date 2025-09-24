using System;
using System.Linq;
using ImGuiNET;
using SharpDX;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace BetterFollowbotLite;

internal class ImGuiDrawSettings
{

    internal static void DrawImGuiSettings()
    {
        var green = new Vector4(0.102f, 0.388f, 0.106f, 1.000f);
        var red = new Vector4(0.388f, 0.102f, 0.102f, 1.000f);

        var collapsingHeaderFlags = ImGuiTreeNodeFlags.CollapsingHeader;
        ImGui.Text("Plugin by alekswoje (forked from Totalschaden). https://github.com/alekswoje/BetterFollowbotLite");

        try
        {
            // Input Keys
            ImGui.PushStyleColor(ImGuiCol.Header, green);
            ImGui.PushID(1000);
            if (ImGui.TreeNodeEx("Input Keys", collapsingHeaderFlags))
            {
                BetterFollowbotLite.Instance.Settings.inputKey1.Value = ImGuiExtension.HotkeySelector(
                    "Skill 2: " + BetterFollowbotLite.Instance.Settings.inputKey1.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey1.Value);
                BetterFollowbotLite.Instance.Settings.inputKey3.Value = ImGuiExtension.HotkeySelector(
                    "Skill 4: " + BetterFollowbotLite.Instance.Settings.inputKey3.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey3.Value);
                BetterFollowbotLite.Instance.Settings.inputKey4.Value = ImGuiExtension.HotkeySelector(
                    "Skill 5: " + BetterFollowbotLite.Instance.Settings.inputKey4.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey4.Value);
                BetterFollowbotLite.Instance.Settings.inputKey5.Value = ImGuiExtension.HotkeySelector(
                    "Skill 6: " + BetterFollowbotLite.Instance.Settings.inputKey5.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey5.Value);
                BetterFollowbotLite.Instance.Settings.inputKey6.Value = ImGuiExtension.HotkeySelector(
                    "Skill 7: " + BetterFollowbotLite.Instance.Settings.inputKey6.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey6.Value);
                BetterFollowbotLite.Instance.Settings.inputKey7.Value = ImGuiExtension.HotkeySelector(
                    "Skill 8: " + BetterFollowbotLite.Instance.Settings.inputKey7.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey7.Value);
                BetterFollowbotLite.Instance.Settings.inputKey8.Value = ImGuiExtension.HotkeySelector(
                    "Skill 9: " + BetterFollowbotLite.Instance.Settings.inputKey8.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey8.Value);
                BetterFollowbotLite.Instance.Settings.inputKey9.Value = ImGuiExtension.HotkeySelector(
                    "Skill 10: " + BetterFollowbotLite.Instance.Settings.inputKey9.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey9.Value);
                BetterFollowbotLite.Instance.Settings.inputKey10.Value = ImGuiExtension.HotkeySelector(
                    "Skill 11: " + BetterFollowbotLite.Instance.Settings.inputKey10.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey10.Value);
                BetterFollowbotLite.Instance.Settings.inputKey11.Value = ImGuiExtension.HotkeySelector(
                    "Skill 12: " + BetterFollowbotLite.Instance.Settings.inputKey11.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey11.Value);
                BetterFollowbotLite.Instance.Settings.inputKey12.Value = ImGuiExtension.HotkeySelector(
                    "Skill 13: " + BetterFollowbotLite.Instance.Settings.inputKey12.Value,
                    BetterFollowbotLite.Instance.Settings.inputKey12.Value);
                BetterFollowbotLite.Instance.Settings.inputKeyPickIt.Value = ImGuiExtension.HotkeySelector(
                    "PickIt: " + BetterFollowbotLite.Instance.Settings.inputKeyPickIt.Value,
                    BetterFollowbotLite.Instance.Settings.inputKeyPickIt.Value);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }


        try
        {
            // Auto Pilot
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.autoPilotEnabled ? green : red);
            ImGui.PushID(0);
            if (ImGui.TreeNodeEx("Auto Pilot", collapsingHeaderFlags))
            {
                BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value =
                    ImGuiExtension.Checkbox("Enabled", BetterFollowbotLite.Instance.Settings.autoPilotEnabled.Value);
                BetterFollowbotLite.Instance.Settings.autoPilotGrace.Value =
                    ImGuiExtension.Checkbox("No Grace Period", BetterFollowbotLite.Instance.Settings.autoPilotGrace.Value);
                BetterFollowbotLite.Instance.Settings.autoPilotLeader = ImGuiExtension.InputText("Leader: ", BetterFollowbotLite.Instance.Settings.autoPilotLeader, 60, ImGuiInputTextFlags.None);
                if (string.IsNullOrWhiteSpace(BetterFollowbotLite.Instance.Settings.autoPilotLeader.Value))
                {
                    // Show error message or set a default value
                    BetterFollowbotLite.Instance.Settings.autoPilotLeader.Value = "DefaultLeader";
                }
                else
                {
                    // Remove any invalid characters from the input
                    BetterFollowbotLite.Instance.Settings.autoPilotLeader.Value = new string(BetterFollowbotLite.Instance.Settings.autoPilotLeader.Value.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                }
                BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled.Value = ImGuiExtension.Checkbox(
                    "Dash", BetterFollowbotLite.Instance.Settings.autoPilotDashEnabled.Value);
                BetterFollowbotLite.Instance.Settings.autoPilotCloseFollow.Value = ImGuiExtension.Checkbox(
                    "Close Follow", BetterFollowbotLite.Instance.Settings.autoPilotCloseFollow.Value);
                BetterFollowbotLite.Instance.Settings.autoPilotDashKey.Value = ImGuiExtension.HotkeySelector(
                    "Dash Key: " + BetterFollowbotLite.Instance.Settings.autoPilotDashKey.Value, BetterFollowbotLite.Instance.Settings.autoPilotDashKey);
                BetterFollowbotLite.Instance.Settings.autoPilotDashDistance.Value =
                    ImGuiExtension.IntSlider("Dash Distance", BetterFollowbotLite.Instance.Settings.autoPilotDashDistance);
                BetterFollowbotLite.Instance.Settings.autoPilotMoveKey.Value = ImGuiExtension.HotkeySelector(
                    "Move Key: " + BetterFollowbotLite.Instance.Settings.autoPilotMoveKey.Value, BetterFollowbotLite.Instance.Settings.autoPilotMoveKey);
                BetterFollowbotLite.Instance.Settings.autoPilotToggleKey.Value = ImGuiExtension.HotkeySelector(
                    "Toggle: " + BetterFollowbotLite.Instance.Settings.autoPilotToggleKey.Value, BetterFollowbotLite.Instance.Settings.autoPilotToggleKey);
                /*
                BetterFollowbotLite.instance.Settings.autoPilotRandomClickOffset.Value =
                    ImGuiExtension.IntSlider("Random Click Offset", BetterFollowbotLite.instance.Settings.autoPilotRandomClickOffset);
                */
                BetterFollowbotLite.Instance.Settings.autoPilotInputFrequency.Value =
                    ImGuiExtension.IntSlider("Input Freq", BetterFollowbotLite.Instance.Settings.autoPilotInputFrequency);
                BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance.Value =
                    ImGuiExtension.IntSlider("Follow Distance", BetterFollowbotLite.Instance.Settings.autoPilotPathfindingNodeDistance);
                BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance.Value =
                    ImGuiExtension.IntSlider("Transition Dist", BetterFollowbotLite.Instance.Settings.autoPilotClearPathDistance);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }
            




        try
        {
            // Aura Blessing
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.auraBlessingEnabled ? green : red);
            ImGui.PushID(9);
            if (ImGui.TreeNodeEx("Aura Blessing", collapsingHeaderFlags))
            {
                BetterFollowbotLite.Instance.Settings.auraBlessingEnabled.Value = ImGuiExtension.Checkbox("Enabled",
                    BetterFollowbotLite.Instance.Settings.auraBlessingEnabled.Value);
                BetterFollowbotLite.Instance.Settings.holyRelicHealthThreshold.Value =
                    ImGuiExtension.IntSlider("Holy Relic Health %", BetterFollowbotLite.Instance.Settings.holyRelicHealthThreshold);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Flame Link
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.flameLinkEnabled ? green : red);
            ImGui.PushID(28);
            if (ImGui.TreeNodeEx("Flame Link", collapsingHeaderFlags))
            {
                BetterFollowbotLite.Instance.Settings.flameLinkEnabled.Value = ImGuiExtension.Checkbox("Enabled",
                    BetterFollowbotLite.Instance.Settings.flameLinkEnabled.Value);
                BetterFollowbotLite.Instance.Settings.flameLinkRange.Value =
                    ImGuiExtension.IntSlider("Range", BetterFollowbotLite.Instance.Settings.flameLinkRange);
                BetterFollowbotLite.Instance.Settings.flameLinkTimeThreshold.Value =
                    ImGuiExtension.IntSlider("Recast Timer", BetterFollowbotLite.Instance.Settings.flameLinkTimeThreshold);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Smite Buff - independent of Flame Link
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.smiteEnabled ? green : red);
            ImGui.PushID(29);
            if (ImGui.TreeNodeEx("Smite Buff", collapsingHeaderFlags))
            {
                bool currentValue = BetterFollowbotLite.Instance.Settings.smiteEnabled.Value;
                if (ImGuiExtension.Checkbox("Enabled", currentValue) != currentValue)
                {
                    BetterFollowbotLite.Instance.Settings.smiteEnabled.Value = !currentValue;
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
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.rejuvenationTotemEnabled ? green : red);
            ImGui.PushID(36);
            if (ImGui.TreeNodeEx("Rejuvenation Totem", collapsingHeaderFlags))
            {
                BetterFollowbotLite.Instance.Settings.rejuvenationTotemEnabled.Value = ImGuiExtension.Checkbox("Enabled",
                    BetterFollowbotLite.Instance.Settings.rejuvenationTotemEnabled.Value);
                BetterFollowbotLite.Instance.Settings.rejuvenationTotemRange.Value =
                    ImGuiExtension.IntSlider("Monster Detection Range", BetterFollowbotLite.Instance.Settings.rejuvenationTotemRange);
                BetterFollowbotLite.Instance.Settings.rejuvenationTotemHpThreshold.Value =
                    ImGuiExtension.IntSlider("Total Pool Threshold %", BetterFollowbotLite.Instance.Settings.rejuvenationTotemHpThreshold);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Vaal Skills - independent of Flame Link
            bool vaalSkillsEnabled = BetterFollowbotLite.Instance.Settings.vaalHasteEnabled || BetterFollowbotLite.Instance.Settings.vaalDisciplineEnabled;
            ImGui.PushStyleColor(ImGuiCol.Header, vaalSkillsEnabled ? green : red);
            ImGui.PushID(30);
            if (ImGui.TreeNodeEx("Vaal Skills", collapsingHeaderFlags))
            {
                // Single checkbox that controls both Vaal skills
                bool combinedVaalEnabled = BetterFollowbotLite.Instance.Settings.vaalHasteEnabled && BetterFollowbotLite.Instance.Settings.vaalDisciplineEnabled;
                bool newCombinedState = ImGuiExtension.Checkbox("Enable Vaal Skills", combinedVaalEnabled);

                if (newCombinedState != combinedVaalEnabled)
                {
                    BetterFollowbotLite.Instance.Settings.vaalHasteEnabled.Value = newCombinedState;
                    BetterFollowbotLite.Instance.Settings.vaalDisciplineEnabled.Value = newCombinedState;
                }

                // Individual skill checkboxes (always available)
                ImGui.Indent();

                bool vaalHasteValue = BetterFollowbotLite.Instance.Settings.vaalHasteEnabled.Value;
                if (ImGuiExtension.Checkbox("Vaal Haste", vaalHasteValue) != vaalHasteValue)
                {
                    BetterFollowbotLite.Instance.Settings.vaalHasteEnabled.Value = !vaalHasteValue;
                }

                bool vaalDisciplineValue = BetterFollowbotLite.Instance.Settings.vaalDisciplineEnabled.Value;
                if (ImGuiExtension.Checkbox("Vaal Discipline", vaalDisciplineValue) != vaalDisciplineValue)
                {
                    BetterFollowbotLite.Instance.Settings.vaalDisciplineEnabled.Value = !vaalDisciplineValue;
                }
                BetterFollowbotLite.Instance.Settings.vaalDisciplineEsp.Value =
                    ImGuiExtension.IntSlider("Vaal Discipline ES%", BetterFollowbotLite.Instance.Settings.vaalDisciplineEsp);
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
            bool warcriesEnabled = BetterFollowbotLite.Instance.Settings.warcriesEnabled.Value;
            ImGui.PushStyleColor(ImGuiCol.Header, warcriesEnabled ? green : red);
            ImGui.PushID(37);
            if (ImGui.TreeNodeEx("Warcries", collapsingHeaderFlags))
            {
                // Master enable/disable for all warcries
                bool masterWarcryEnabled = BetterFollowbotLite.Instance.Settings.warcriesEnabled.Value;
                bool newMasterState = ImGuiExtension.Checkbox("Enable Warcries", masterWarcryEnabled);

                if (newMasterState != masterWarcryEnabled)
                {
                    BetterFollowbotLite.Instance.Settings.warcriesEnabled.Value = newMasterState;
                }

                if (warcriesEnabled)
                {
                    ImGui.Indent();
                    
                    // Individual warcry checkboxes
                    bool ancestralCryValue = BetterFollowbotLite.Instance.Settings.ancestralCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Ancestral Cry", ancestralCryValue) != ancestralCryValue)
                    {
                        BetterFollowbotLite.Instance.Settings.ancestralCryEnabled.Value = !ancestralCryValue;
                    }

                    bool infernalCryValue = BetterFollowbotLite.Instance.Settings.infernalCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Infernal Cry", infernalCryValue) != infernalCryValue)
                    {
                        BetterFollowbotLite.Instance.Settings.infernalCryEnabled.Value = !infernalCryValue;
                    }

                    bool generalsCryValue = BetterFollowbotLite.Instance.Settings.generalsCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("General's Cry", generalsCryValue) != generalsCryValue)
                    {
                        BetterFollowbotLite.Instance.Settings.generalsCryEnabled.Value = !generalsCryValue;
                    }

                    bool intimidatingCryValue = BetterFollowbotLite.Instance.Settings.intimidatingCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Intimidating Cry", intimidatingCryValue) != intimidatingCryValue)
                    {
                        BetterFollowbotLite.Instance.Settings.intimidatingCryEnabled.Value = !intimidatingCryValue;
                    }

                    bool rallyingCryValue = BetterFollowbotLite.Instance.Settings.rallyingCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Rallying Cry", rallyingCryValue) != rallyingCryValue)
                    {
                        BetterFollowbotLite.Instance.Settings.rallyingCryEnabled.Value = !rallyingCryValue;
                    }

                    bool vengefulCryValue = BetterFollowbotLite.Instance.Settings.vengefulCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Vengeful Cry", vengefulCryValue) != vengefulCryValue)
                    {
                        BetterFollowbotLite.Instance.Settings.vengefulCryEnabled.Value = !vengefulCryValue;
                    }

                    bool enduringCryValue = BetterFollowbotLite.Instance.Settings.enduringCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Enduring Cry", enduringCryValue) != enduringCryValue)
                    {
                        BetterFollowbotLite.Instance.Settings.enduringCryEnabled.Value = !enduringCryValue;
                    }

                    bool seismicCryValue = BetterFollowbotLite.Instance.Settings.seismicCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Seismic Cry", seismicCryValue) != seismicCryValue)
                    {
                        BetterFollowbotLite.Instance.Settings.seismicCryEnabled.Value = !seismicCryValue;
                    }

                    bool battlemagesCryValue = BetterFollowbotLite.Instance.Settings.battlemagesCryEnabled.Value;
                    if (ImGuiExtension.Checkbox("Battlemage's Cry", battlemagesCryValue) != battlemagesCryValue)
                    {
                        BetterFollowbotLite.Instance.Settings.battlemagesCryEnabled.Value = !battlemagesCryValue;
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
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.minesEnabled ? green : red);
            ImGui.PushID(31);
            if (ImGui.TreeNodeEx("Mines", collapsingHeaderFlags))
            {
                BetterFollowbotLite.Instance.Settings.minesEnabled.Value = ImGuiExtension.Checkbox("Enabled",
                    BetterFollowbotLite.Instance.Settings.minesEnabled.Value);
                BetterFollowbotLite.Instance.Settings.minesRange = ImGuiExtension.InputText("Range",
                    BetterFollowbotLite.Instance.Settings.minesRange, 60, ImGuiInputTextFlags.None);
                BetterFollowbotLite.Instance.Settings.minesLeaderDistance = ImGuiExtension.InputText("Leader Distance",
                    BetterFollowbotLite.Instance.Settings.minesLeaderDistance, 60, ImGuiInputTextFlags.None);
                BetterFollowbotLite.Instance.Settings.minesStormblastEnabled.Value = ImGuiExtension.Checkbox("Stormblast",
                    BetterFollowbotLite.Instance.Settings.minesStormblastEnabled.Value);
                BetterFollowbotLite.Instance.Settings.minesPyroclastEnabled.Value = ImGuiExtension.Checkbox("Pyroclast",
                    BetterFollowbotLite.Instance.Settings.minesPyroclastEnabled.Value);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Summon Minions
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.summonSkeletonsEnabled ? green : red);
            ImGui.PushID(33);
            if (ImGui.TreeNodeEx("Summon Minions", collapsingHeaderFlags))
            {
                BetterFollowbotLite.Instance.Settings.summonSkeletonsEnabled.Value = ImGuiExtension.Checkbox("Auto Summon Skeletons",
                    BetterFollowbotLite.Instance.Settings.summonSkeletonsEnabled.Value);

                BetterFollowbotLite.Instance.Settings.summonSkeletonsRange.Value =
                    ImGuiExtension.IntSlider("Range", BetterFollowbotLite.Instance.Settings.summonSkeletonsRange);

                BetterFollowbotLite.Instance.Settings.summonSkeletonsMinCount.Value =
                    ImGuiExtension.IntSlider("Min Count", BetterFollowbotLite.Instance.Settings.summonSkeletonsMinCount);

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                // SRS (Summon Raging Spirits) toggle
                BetterFollowbotLite.Instance.Settings.summonRagingSpiritsEnabled.Value = ImGuiExtension.Checkbox("Enable SRS (Summon Raging Spirits)",
                    BetterFollowbotLite.Instance.Settings.summonRagingSpiritsEnabled.Value);

                BetterFollowbotLite.Instance.Settings.summonRagingSpiritsMinCount.Value =
                    ImGuiExtension.IntSlider("SRS Min Count", BetterFollowbotLite.Instance.Settings.summonRagingSpiritsMinCount);

                BetterFollowbotLite.Instance.Settings.summonRagingSpiritsMagicNormal.Value = ImGuiExtension.Checkbox("Include Magic/White enemies",
                    BetterFollowbotLite.Instance.Settings.summonRagingSpiritsMagicNormal.Value);
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Auto Respawn
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.autoRespawnEnabled ? green : red);
            ImGui.PushID(32);
            if (ImGui.TreeNodeEx("Auto Respawn", collapsingHeaderFlags))
            {
                // Simple Enable/Disable button
                bool isRespawnEnabled = BetterFollowbotLite.Instance.Settings.autoRespawnEnabled.Value;
                string buttonText = isRespawnEnabled ? "Disable" : "Enable";
                if (ImGui.Button($"{buttonText} Auto Respawn"))
                {
                    BetterFollowbotLite.Instance.Settings.autoRespawnEnabled.Value = !isRespawnEnabled;
                    BetterFollowbotLite.Instance.LogMessage($"AUTO RESPAWN: {buttonText}d - new value: {!isRespawnEnabled}");
                }

                // Show current status
                ImGui.Text($"Status: {(isRespawnEnabled ? "Enabled" : "Disabled")}");
            }
        }
        catch (Exception e)
        {
            // Error handling without logging
        }

        try
        {
            // Auto Level Gems
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.autoLevelGemsEnabled ? green : red);
            ImGui.PushID(34);
            if (ImGui.TreeNodeEx("Auto Level Gems", collapsingHeaderFlags))
            {
                // Simple Enable/Disable button
                bool isGemsEnabled = BetterFollowbotLite.Instance.Settings.autoLevelGemsEnabled.Value;
                string buttonText = isGemsEnabled ? "Disable" : "Enable";
                if (ImGui.Button($"{buttonText} Auto Level Gems"))
                {
                    BetterFollowbotLite.Instance.Settings.autoLevelGemsEnabled.Value = !isGemsEnabled;
                    BetterFollowbotLite.Instance.LogMessage($"AUTO LEVEL GEMS: {buttonText}d - new value: {!isGemsEnabled}");
                }

                // Show current status
                ImGui.Text($"Status: {(isGemsEnabled ? "Enabled" : "Disabled")}");

                if (ImGui.Button("Test Toggle"))
                {
                    var testOldValue = BetterFollowbotLite.Instance.Settings.autoLevelGemsEnabled.Value;
                    BetterFollowbotLite.Instance.Settings.autoLevelGemsEnabled.Value = !testOldValue;
                    BetterFollowbotLite.Instance.LogMessage($"AUTO LEVEL GEMS: Test toggled from {testOldValue} to {BetterFollowbotLite.Instance.Settings.autoLevelGemsEnabled.Value}");
                }
            }
        }
        catch (Exception e)
        {
            BetterFollowbotLite.Instance.LogMessage($"AUTO LEVEL GEMS UI ERROR: {e.Message}");
        }

        try
        {
            // Auto Join Party & Accept Trade
            ImGui.PushStyleColor(ImGuiCol.Header, BetterFollowbotLite.Instance.Settings.autoJoinPartyEnabled ? green : red);
            ImGui.PushID(35);
            if (ImGui.TreeNodeEx("Auto Join Party & Accept Trade", collapsingHeaderFlags))
            {
                BetterFollowbotLite.Instance.Settings.autoJoinPartyEnabled.Value =
                    ImGuiExtension.Checkbox("Auto Join Party & Accept Trade Invites", BetterFollowbotLite.Instance.Settings.autoJoinPartyEnabled.Value);

                // Debug: Show current value
                ImGui.Text($"Current: {BetterFollowbotLite.Instance.Settings.autoJoinPartyEnabled.Value}");
            }
        }
        catch (Exception e)
        {
            BetterFollowbotLite.Instance.LogMessage($"AUTO JOIN PARTY & ACCEPT TRADE UI ERROR: {e.Message}");
        }

        try
        {
            // General Settings
            ImGui.PushStyleColor(ImGuiCol.Header, green);
            ImGui.PushID(38);
            if (ImGui.TreeNodeEx("General Settings", collapsingHeaderFlags))
            {
                BetterFollowbotLite.Instance.Settings.disableSkillsInHideout.Value =
                    ImGuiExtension.Checkbox("Disable Skills in Hideouts", BetterFollowbotLite.Instance.Settings.disableSkillsInHideout.Value);

                ImGui.Text("When enabled, skills will be blocked in hideouts for safety.");
                ImGui.Text("Disable this to test skills in hideouts.");
            }
        }
        catch (Exception e)
        {
            BetterFollowbotLite.Instance.LogMessage($"GENERAL SETTINGS UI ERROR: {e.Message}");
        }

        //ImGui.End();
    }
}
