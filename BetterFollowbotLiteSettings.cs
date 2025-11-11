using System.Windows.Forms;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
// ReSharper disable FieldCanBeMadeReadOnly.Global
// Need non readonly to save settings.

namespace BetterFollowbot;

public class BetterFollowbotSettings : ISettings
{

    public ToggleNode debugMode = new ToggleNode(false);

    public BetterFollowbotSettings()
    {
        Enable = new ToggleNode(false);
    }

    public ToggleNode Enable { get; set; }

    #region AutoPilot
        
    public ToggleNode autoPilotEnabled = new ToggleNode(false);
    public ToggleNode autoPilotGrace = new ToggleNode(false);
    public TextNode autoPilotLeader = new TextNode("");
    public ToggleNode autoPilotDashEnabled = new ToggleNode(false);
    public ToggleNode autoPilotCloseFollow = new ToggleNode(true);
    public HotkeyNode autoPilotDashKey = new HotkeyNode(Keys.W);
    public HotkeyNode autoPilotMoveKey = new HotkeyNode(Keys.Q);
    public HotkeyNode autoPilotToggleKey = new HotkeyNode(Keys.NumPad9);
    public RangeNode<int> autoPilotRandomClickOffset = new RangeNode<int>(10, 1, 100);
    public RangeNode<int> autoPilotInputFrequency = new RangeNode<int>(50, 1, 100);
    public RangeNode<int> autoPilotPathfindingNodeDistance = new RangeNode<int>(200, 10, 1000);
    public RangeNode<int> autoPilotClearPathDistance = new RangeNode<int>(500, 100, 5000);
    public RangeNode<int> autoPilotDashDistance = new RangeNode<int>(500, 50, 2000);
    public RangeNode<int> autoPilotRandomActionDelay = new RangeNode<int>(15, 0, 100);
    public RangeNode<int> autoPilotMaxPortalDistance = new RangeNode<int>(2000, 500, 5000);

    #endregion
        

    #region Aura Blessing

    public ToggleNode auraBlessingEnabled = new ToggleNode(false);
    public RangeNode<int> holyRelicHealthThreshold = new RangeNode<int>(25, 1, 100);

    #endregion

    #region Link Skills

    public ToggleNode linksEnabled = new ToggleNode(false);
    public RangeNode<int> flameLinkRange = new RangeNode<int>(40, 10, 100);
    public RangeNode<int> flameLinkTimeThreshold = new RangeNode<int>(4, 1, 10);

    #endregion

    #region Smite Buff

    public ToggleNode smiteEnabled = new ToggleNode(false);
    public RangeNode<int> smiteRange = new RangeNode<int>(250, 10, 1000);

    #endregion

    #region Vaal Skills

    public ToggleNode vaalHasteEnabled = new ToggleNode(false);
    public ToggleNode vaalDisciplineEnabled = new ToggleNode(false);
    public RangeNode<int> vaalDisciplineEsp = new RangeNode<int>(70, 0, 100);

    #endregion

    #region Warcries

    public ToggleNode warcriesEnabled = new ToggleNode(false);

    #endregion

    #region Mines

    public ToggleNode minesEnabled = new ToggleNode(false);
    public TextNode minesRange = new TextNode("100");
    public TextNode minesLeaderDistance = new TextNode("500");
    public ToggleNode minesStormblastEnabled = new ToggleNode(true);
    public ToggleNode minesPyroclastEnabled = new ToggleNode(true);
    public ToggleNode minesPortalEnabled = new ToggleNode(true);
    public RangeNode<int> maxMines = new RangeNode<int>(15, 1, 50);

    #endregion

    #region Summon Skeletons

    public ToggleNode summonSkeletonsEnabled = new ToggleNode(false);
    public RangeNode<int> summonSkeletonsRange = new RangeNode<int>(500, 100, 2000);
    public RangeNode<int> summonSkeletonsMinCount = new RangeNode<int>(5, 1, 20);

    // SRS (Summon Raging Spirits) settings
    public ToggleNode summonRagingSpiritsEnabled = new ToggleNode(false);
    public RangeNode<int> summonRagingSpiritsMinCount = new RangeNode<int>(10, 1, 15);
    public ToggleNode summonRagingSpiritsMagicNormal = new ToggleNode(false);

    #endregion

    #region Rejuvenation Totem

    public ToggleNode rejuvenationTotemEnabled = new ToggleNode(false);
    public RangeNode<int> rejuvenationTotemRange = new RangeNode<int>(1000, 100, 2000);
    public RangeNode<int> rejuvenationTotemHpThreshold = new RangeNode<int>(90, 1, 100);

    #endregion

    #region Auto Respawn

    public ToggleNode autoRespawnEnabled = new ToggleNode(false);
    public ToggleNode waitForLeaderAfterRespawn = new ToggleNode(false);

    #endregion
    
    #region Auto Level Gems

    public ToggleNode autoLevelGemsEnabled = new ToggleNode(false);
    public RangeNode<float> gemLevelingCooldown = new RangeNode<float>(0.25f, 0.1f, 2.0f);

    #endregion

    #region Auto Join Party & Accept Trade

    public ToggleNode autoJoinPartyEnabled = new ToggleNode(false);
    public ToggleNode autoJoinPartyEvenIfInParty = new ToggleNode(true);
    public ToggleNode autoDumpInventoryOnTrade = new ToggleNode(false);
    public ToggleNode autoClickTradeAcceptButton = new ToggleNode(false);

    #endregion

    #region General Settings

    public ToggleNode disableSkillsInHideout = new ToggleNode(true);
    public RangeNode<float> skillCooldown = new RangeNode<float>(0.2f, 0.05f, 1.0f);

    #endregion

    #region Input Keys

    public HotkeyNode inputKey1 = new HotkeyNode(Keys.Z);
    public HotkeyNode inputKey3 = new HotkeyNode(Keys.Q);
    public HotkeyNode inputKey4 = new HotkeyNode(Keys.W);
    public HotkeyNode inputKey5 = new HotkeyNode(Keys.E);
    public HotkeyNode inputKey6 = new HotkeyNode(Keys.R);
    public HotkeyNode inputKey7 = new HotkeyNode(Keys.T);
    public HotkeyNode inputKey8 = new HotkeyNode(Keys.NumPad1);
    public HotkeyNode inputKey9 = new HotkeyNode(Keys.NumPad2);
    public HotkeyNode inputKey10 = new HotkeyNode(Keys.NumPad3);
    public HotkeyNode inputKey11 = new HotkeyNode(Keys.NumPad4);
    public HotkeyNode inputKey12 = new HotkeyNode(Keys.NumPad5);
    public HotkeyNode inputKeyPickIt = new HotkeyNode(Keys.Space);

    #endregion

}