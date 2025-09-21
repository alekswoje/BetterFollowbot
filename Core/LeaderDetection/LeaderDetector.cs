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
        private Entity _cachedLeaderEntity;
        private PartyElementWindow _cachedLeaderPartyElement;
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private const int CacheTimeoutSeconds = 2; // Cache leader for 2 seconds

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
            ClearLeaderCache(); // Clear cache when leader changes
        }

        public Entity FindLeaderEntity()
        {
            try
            {
                // Check cache first
                if (_cachedLeaderEntity != null && 
                    _cachedLeaderEntity.IsValid && 
                    (DateTime.Now - _lastCacheUpdate).TotalSeconds < CacheTimeoutSeconds)
                {
                    return _cachedLeaderEntity;
                }

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

                // Update cache
                _cachedLeaderEntity = leader;
                _lastCacheUpdate = DateTime.Now;

                return leader;
            }
            // Sometimes we can get "Collection was modified; enumeration operation may not execute" exception
            catch (Exception ex)
            {
                _core.LogMessage($"FindLeaderEntity exception: {ex.Message}");
                return null;
            }
        }

        public PartyElementWindow GetLeaderPartyElement()
        {
            try
            {
                // Check cache first
                if (_cachedLeaderPartyElement != null && 
                    (DateTime.Now - _lastCacheUpdate).TotalSeconds < CacheTimeoutSeconds)
                {
                    return _cachedLeaderPartyElement;
                }

                string leaderName = LeaderName;
                if (string.IsNullOrEmpty(leaderName))
                {
                    return null;
                }

                foreach (var partyElementWindow in PartyElements.GetPlayerInfoElementList())
                {
                    if (string.Equals(partyElementWindow?.PlayerName?.ToLower(), leaderName.ToLower(), StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Update cache
                        _cachedLeaderPartyElement = partyElementWindow;
                        _lastCacheUpdate = DateTime.Now;
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
            // Force refresh of cached data
            ClearLeaderCache();
            
            // Update both cached values
            FindLeaderEntity();
            GetLeaderPartyElement();
        }

        public void ClearLeaderCache()
        {
            _cachedLeaderEntity = null;
            _cachedLeaderPartyElement = null;
            _lastCacheUpdate = DateTime.MinValue;
        }

        public bool IsLeaderValid()
        {
            var leader = FindLeaderEntity();
            return leader != null && leader.IsValid && leader.IsAlive;
        }

        #endregion
    }
}
