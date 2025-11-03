using System;
using System.Linq;
using System.Windows.Forms;
using BetterFollowbot.Interfaces;
using BetterFollowbot.Core.Skills;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;

namespace BetterFollowbot.Skills
{
    internal class RejuvenationTotem : ISkill
    {
        private readonly BetterFollowbot _instance;
        private readonly BetterFollowbotSettings _settings;


        public RejuvenationTotem(BetterFollowbot instance, BetterFollowbotSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.rejuvenationTotemEnabled;

        public string SkillName => "Rejuvenation Totem";

        /// <summary>
        /// Checks if any blocking UI elements are open that should prevent skill execution
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

                // Check for ExileCore overlay/menu activity (when user is interacting with the plugin GUI)
                // If mouse cursor is not over the game window or if we're not in foreground, block
                var gameWindowRect = _instance.GameController?.Window?.GetWindowRectangleTimeCache;
                var mousePos = _instance.GetMousePosition();

                // If mouse is outside game window bounds, user is likely in ExileCore overlay
                var mouseOutsideGame = gameWindowRect == null ||
                    mousePos.X < gameWindowRect.Value.X || mousePos.X > gameWindowRect.Value.X + gameWindowRect.Value.Width ||
                    mousePos.Y < gameWindowRect.Value.Y || mousePos.Y > gameWindowRect.Value.Y + gameWindowRect.Value.Height;

                // If not in foreground, definitely block (covers overlay scenarios)
                var notInForeground = !_instance.GameController.IsForeGroundCache;

                // If chat is open (user might be typing), block
                var chatField = _instance.GameController?.IngameState?.IngameUi?.ChatPanel?.ChatInputElement?.IsVisible;
                var chatOpen = chatField != null && (bool)chatField;

                // Note: Map is non-obstructing in PoE, so we don't check it
                return stashOpen || npcDialogOpen || sellWindowOpen || purchaseWindowOpen ||
                       inventoryOpen || skillTreeOpen || atlasOpen || mouseOutsideGame ||
                       notInForeground || chatOpen;
            }
            catch
            {
                // If we can't check UI state, err on the side of caution
                return true;
            }
        }

        public void Execute()
        {
            // Block skill execution when blocking UI is open
            if (IsBlockingUiOpen())
                return;

            // Block skill execution in towns
            if (_instance.GameController?.Area?.CurrentArea?.IsTown == true)
                return;

            // Block skill execution in hideouts (if setting is enabled)
            if (_instance.GameController?.Area?.CurrentArea?.IsHideout == true && _settings.disableSkillsInHideout)
                return;

            // Check individual skill cooldown
            if (!_instance.CanUseSkill("RejuvenationTotem"))
                return;

            // Only use skills when within follow range of the leader
            if (!_instance.IsWithinFollowRange())
                return;

            try
            {
                // Loop through all skills to find the rejuvenation totem skill
                foreach (var skill in _instance.skills)
                {
                    if (skill.Id == SkillInfo.rejuvenationTotem.Id)
                    {
                        if (SkillInfo.ManageCooldown(SkillInfo.rejuvenationTotem, skill))
                        {

                            // Check if we already have the totem buff
                            var hasTotemBuff = _instance.Buffs.Exists(x => x.Name == "totem_aura_life_regen");
                            if (!hasTotemBuff)
                            {
                                // Check for unique/rare monsters within range
                                var monsterCount = _instance.GetMonsterWithin(_settings.rejuvenationTotemRange, ExileCore.Shared.Enums.MonsterRarity.Rare);
                                var uniqueMonsterCount = _instance.GetMonsterWithin(_settings.rejuvenationTotemRange, ExileCore.Shared.Enums.MonsterRarity.Unique);
                                var hasRareOrUniqueNearby = monsterCount > 0 || uniqueMonsterCount > 0;

                                // Check if any party member total pool (Life + ES) is below threshold
                                var partyMembersLowHp = false;
                                var partyElements = PartyElements.GetPlayerInfoElementList();

                                foreach (var partyMember in partyElements)
                                {
                                    if (partyMember != null)
                                    {
                                        // Get the actual player entity for detailed Life/ES info
                                        var playerEntity = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                            .FirstOrDefault(x => x != null && x.IsValid && !x.IsHostile &&
                                                               string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                                               partyMember.PlayerName?.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                        if (playerEntity != null)
                                        {
                                            // Get Life component for detailed health info
                                            var lifeComponent = playerEntity.GetComponent<Life>();
                                            if (lifeComponent != null)
                                            {
                                                // Get actual values for total pool calculation
                                                var currentLife = lifeComponent.Health.Current;
                                                var maxLife = lifeComponent.Health.Unreserved; // Unreserved life only
                                                var currentES = lifeComponent.EnergyShield.Current;
                                                var maxES = lifeComponent.EnergyShield.Unreserved;

                                                // Calculate meaningful thresholds - only consider ES if they have a meaningful amount
                                                var hasMeaningfulES = maxES >= 500; // Only consider ES if they have 500+ max ES
                                                var effectiveCurrentES = hasMeaningfulES ? currentES : 0;
                                                var effectiveMaxES = hasMeaningfulES ? maxES : 0;

                                                // Calculate total pool percentage using unreserved life + meaningful ES
                                                var totalCurrent = currentLife + effectiveCurrentES;
                                                var totalMax = maxLife + effectiveMaxES;
                                                var totalPoolPercentage = totalMax > 0 ? ((double)totalCurrent / (double)totalMax) * 100 : 100;

                                                if (totalPoolPercentage < _settings.rejuvenationTotemHpThreshold.Value)
                                                {
                                                    partyMembersLowHp = true;
                                                    break;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Fallback: Skip this party member if we can't get entity info
                                        }
                                    }
                                }

                                // Check if we're within following distance of the leader
                                var withinFollowingDistance = true;
                                var leaderName = _settings.autoPilotLeader.Value;

                                if (!string.IsNullOrWhiteSpace(leaderName))
                                {
                                    var leaderPartyElement = partyElements
                                        .FirstOrDefault(x => string.Equals(x?.PlayerName?.ToLower(),
                                            leaderName.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                    if (leaderPartyElement != null)
                                    {
                                        // Get distance to leader
                                        var leaderEntity = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player]
                                            .FirstOrDefault(x => x != null && x.IsValid && !x.IsHostile &&
                                                               string.Equals(x.GetComponent<Player>()?.PlayerName?.ToLower(),
                                                               leaderName.ToLower(), StringComparison.CurrentCultureIgnoreCase));

                                        if (leaderEntity != null)
                                        {
                                            var distanceToLeader = Vector2.Distance(
                                                new Vector2(_instance.playerPosition.X, _instance.playerPosition.Y),
                                                new Vector2(leaderEntity.PosNum.X, leaderEntity.PosNum.Y));

                                            // Use the following distance setting
                                            withinFollowingDistance = distanceToLeader <= _settings.autoPilotPathfindingNodeDistance.Value;
                                        }
                                    }
                                }

                                // Place totem if conditions are met
                                if ((hasRareOrUniqueNearby || partyMembersLowHp) && withinFollowingDistance)
                                {
                                    // Check UI menu status
                                    var stashOpen = _instance.GameController.IngameState.IngameUi.StashElement.IsVisibleLocal;
                                    var npcDialogOpen = _instance.GameController.IngameState.IngameUi.NpcDialog.IsVisible;
                                    var sellWindowOpen = _instance.GameController.IngameState.IngameUi.SellWindow.IsVisible;
                                    var purchaseWindowOpen = _instance.GameController.IngameState.IngameUi.PurchaseWindow.IsVisible;
                                    var mapOpen = _instance.GameController.IngameState.IngameUi.Map.IsVisible;

                                    // Additional potential blocking elements
                                    var inventoryOpen = _instance.GameController.IngameState.IngameUi.InventoryPanel.IsVisible;
                                    var skillTreeOpen = _instance.GameController.IngameState.IngameUi.TreePanel.IsVisible;

                                    // Check for blocking UI elements (map is non-obstructing in PoE)
                                    if (stashOpen || npcDialogOpen || sellWindowOpen || purchaseWindowOpen || inventoryOpen || skillTreeOpen)
                                    {
                                        return;
                                    }

                                    // Move cursor to screen center before placing totem
                                    var screenRect = _instance.GameController.Window.GetWindowRectangle();
                                    var screenCenter = new Vector2(screenRect.Width / 2, screenRect.Height / 2);
                                    Mouse.SetCursorPos(screenCenter);

                                    // Small delay to ensure mouse movement is registered
                                    System.Threading.Thread.Sleep(50);

                                    // Check if the skill is available and can be used
                                    if (skill.SkillSlotIndex < 0 || skill.SkillSlotIndex >= 12)
                                    {
                                        return;
                                    }

                                    // Get the skill slot for the totem
                                    var skillKey = _instance.GetSkillInputKey(skill.SkillSlotIndex);

                                    // Check if skill has charges available
                                    if (skill.RemainingUses <= 0 && skill.IsOnCooldown)
                                    {
                                        return;
                                    }

                                    // Place the totem
                                    if (skillKey != default(Keys))
                                    {
                                        Keyboard.KeyPress(skillKey);
                                        _instance.RecordSkillUse("RejuvenationTotem");
                                    }

                                    // Set cooldown to prevent spamming (2 seconds as requested)
                                    SkillInfo.rejuvenationTotem.Cooldown = 2000;
                                    _instance.LastTimeAny = DateTime.Now; // Update global cooldown like other skills
                                }
                            }
                        }
                        else
                        {
                            return; // Exit early if on cooldown
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Silent exception handling
            }
        }
    }
}
