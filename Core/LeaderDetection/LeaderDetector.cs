using System;
using System.Linq;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using BetterFollowbotLite.Interfaces;

namespace BetterFollowbotLite.Core.LeaderDetection
{
    /// <summary>
    /// Handles detection and tracking of the party leader
    /// </summary>
    public class LeaderDetector : ILeaderDetector
    {
        private readonly IFollowbotCore _core;

        public LeaderDetector(IFollowbotCore core)
        {
            _core = core ?? throw new ArgumentNullException(nameof(core));
        }

        #region ILeaderDetector Implementation

        public string LeaderName => _core.Settings.autoPilotLeader.Value;

        public Entity LeaderEntity => FindLeaderEntity();

        public PartyElementWindow LeaderPartyElement => GetLeaderPartyElement();

        public bool IsLeaderInDifferentZone
        {
            get
            {
                var leaderPartyElement = GetLeaderPartyElement();
                if (leaderPartyElement == null) return false;

                var currentZone = BetterFollowbotLite.Instance.GameController?.Area.CurrentArea.DisplayName;
                return !leaderPartyElement.ZoneName.Equals(currentZone);
            }
        }

        public void SetLeaderName(string leaderName)
        {
            _core.Settings.autoPilotLeader.Value = leaderName;
        }

        public Entity FindLeaderEntity()
        {
            try
            {
                // ZONE LOADING PROTECTION: If we're loading or don't have a valid game state, don't try to find leader
                if (BetterFollowbotLite.Instance.GameController.IsLoading ||
                    BetterFollowbotLite.Instance.GameController.Area.CurrentArea == null ||
                    string.IsNullOrEmpty(BetterFollowbotLite.Instance.GameController.Area.CurrentArea.DisplayName))
                {
                    return null;
                }

                string leaderName = LeaderName?.ToLower();
                if (string.IsNullOrEmpty(leaderName))
                {
                    return null;
                }

                var players = BetterFollowbotLite.Instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player];
                if (players == null)
                {
                    return null;
                }

                var leader = players.FirstOrDefault(x =>
                {
                    if (x == null || !x.IsValid)
                        return false;

                    var playerComponent = x.GetComponent<Player>();
                    if (playerComponent == null)
                        return false;

                    var playerName = playerComponent.PlayerName;
                    if (string.IsNullOrEmpty(playerName))
                        return false;

                    return string.Equals(playerName.ToLower(), leaderName, StringComparison.OrdinalIgnoreCase);
                });

                return leader;
            }
            // Sometimes we can get "Collection was modified; enumeration operation may not execute" exception
            catch (Exception ex)
            {
                _core.LogMessage($"LEADER DETECTION: Entity collection exception during leader search: {ex.Message} - this may cause detection delays");
                return null;
            }
        }

        public PartyElementWindow GetLeaderPartyElement()
        {
            try
            {
                string leaderName = LeaderName;
                if (string.IsNullOrEmpty(leaderName))
                {
                    return null;
                }

                foreach (var partyElementWindow in PartyElements.GetPlayerInfoElementList())
                {
                    if (string.Equals(partyElementWindow?.PlayerName?.ToLower(), leaderName.ToLower(), StringComparison.CurrentCultureIgnoreCase))
                    {
                        return partyElementWindow;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _core.LogMessage($"GetLeaderPartyElement exception: {ex.Message}");
                return null;
            }
        }

        public void UpdateLeaderDetection()
        {
            // No caching - detection happens in real-time each call
            // This method exists for interface compliance but doesn't need to do anything
        }

        public void ClearLeaderCache()
        {
            // No caching - this method exists for interface compliance but doesn't need to do anything
        }

        public bool IsLeaderValid()
        {
            var leader = FindLeaderEntity();
            return leader != null && leader.IsValid && leader.IsAlive;
        }

        #endregion
    }
}
