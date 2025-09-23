using System;
using System.Linq;
using BetterFollowbotLite.Interfaces;
using BetterFollowbotLite.Core.Skills;
using BetterFollowbotLite.Core.TaskManagement;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using GameOffsets.Native;
using SharpDX;

namespace BetterFollowbotLite.Skills
{
    internal class SmiteBuff : ISkill
    {
        private readonly BetterFollowbotLite _instance;
        private readonly BetterFollowbotLiteSettings _settings;

        public SmiteBuff(BetterFollowbotLite instance, BetterFollowbotLiteSettings settings)
        {
            _instance = instance;
            _settings = settings;
        }

        public bool IsEnabled => _settings.smiteEnabled;

        public string SkillName => "Smite Buff";

        public void Execute()
        {
            // Block skill execution when menu is open
            if (MenuWindow.IsOpened)
                return;

            // Block skill execution in towns
            if (_instance.GameController?.Area?.CurrentArea?.IsTown == true)
                return;

            // Loop through all skills to find Smite skill
            foreach (var skill in _instance.skills)
            {
                if (skill.Id == SkillInfo.smite.Id)
                {
                    // Custom cooldown check for smite that bypasses GCD since it's a buff skill
                    if (SkillInfo.smite.Cooldown <= 0 &&
                        !(skill.RemainingUses <= 0 && skill.IsOnCooldown))
                    {
                        // Check mana cost
                        if (!skill.Stats.TryGetValue(GameStat.ManaCost, out var manaCost))
                            manaCost = 0;

                        if (_instance.player.CurMana >= manaCost ||
                            (_instance.localPlayer.Stats.TryGetValue(GameStat.VirtualEnergyShieldProtectsMana, out var hasEldritchBattery) &&
                             hasEldritchBattery > 0 && (_instance.player.CurES + _instance.player.CurMana) >= manaCost))
                        {
                            // Check if we don't have the smite buff or it's about to expire
                            var smiteBuff = _instance.Buffs.FirstOrDefault(x => x.Name == "smite_buff");
                            var hasSmiteBuff = smiteBuff != null;
                            var buffTimeLeft = smiteBuff?.Timer ?? 0;

                            // Refresh if no buff or buff has less than 2 seconds left
                            if (!hasSmiteBuff || buffTimeLeft < 2.0f)
                            {
                                // Find valid monsters within 250 units of player (smite attack range) using ReAgent-style validation
                                var targetMonster = _instance.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                                    .Where(monster => IsValidMonsterForSmite(monster))
                                    .OrderBy(monster => monster.DistancePlayer) // Closest first
                                    .FirstOrDefault();

                                if (targetMonster != null)
                                {
                                    // Move mouse to monster position
                                    var monsterScreenPos = _instance.GameController.IngameState.Camera.WorldToScreen(targetMonster.Pos);
                                    Mouse.SetCursorPos(monsterScreenPos);

                                    // Small delay to ensure mouse movement is registered
                                    System.Threading.Thread.Sleep(50);

                                    // Double-check mouse position is still valid
                                    var currentMousePos = _instance.GetMousePosition();
                                    var distanceFromTarget = Vector2.Distance(currentMousePos, monsterScreenPos);
                                    if (distanceFromTarget < 50) // Within reasonable tolerance
                                    {
                                        // Activate the skill
                                        Keyboard.KeyPress(_instance.GetSkillInputKey(skill.SkillSlotIndex));
                                        SkillInfo.smite.Cooldown = 100;
                                        _instance.LastTimeAny = DateTime.Now; // Update global cooldown
                                    }
                                }
                                else
                                {
                                    // No suitable targets found - dash to leader to get near monsters
                                    _instance.DashToLeaderForSmite();
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validates if a monster is valid for smite targeting (based on ReAgent logic)
        /// </summary>
        private bool IsValidMonsterForSmite(Entity monster)
        {
            try
            {
                // ReAgent-style validation checks
                const int smiteRange = 250; // Smite has a 250 unit range
                if (monster.DistancePlayer > smiteRange)
                    return false;

                if (!monster.HasComponent<Monster>() ||
                    !monster.HasComponent<Positioned>() ||
                    !monster.HasComponent<Render>() ||
                    !monster.HasComponent<Life>() ||
                    !monster.HasComponent<ObjectMagicProperties>())
                    return false;

                if (!monster.IsAlive || !monster.IsHostile)
                    return false;

                // Check for hidden monster buff (like ReAgent does)
                if (monster.TryGetComponent<Buffs>(out var buffs) && buffs.HasBuff("hidden_monster"))
                    return false;

                // Check if monster is on screen (can be targeted)
                var screenPos = _instance.GameController.IngameState.Camera.WorldToScreen(monster.Pos);
                var isOnScreen = _instance.GameController.Window.GetWindowRectangleTimeCache.Contains(screenPos);
                if (!isOnScreen)
                    return false;

                // Additional checks for targetability
                var targetable = monster.GetComponent<Targetable>();
                if (targetable == null || !targetable.isTargetable)
                    return false;

                // Check if not invincible (cannot be damaged)
                var stats = monster.GetComponent<Stats>();
                if (stats?.StatDictionary?.ContainsKey(GameStat.CannotBeDamaged) == true &&
                    stats.StatDictionary[GameStat.CannotBeDamaged] > 0)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                _instance.LogError($"SMITE: Error validating monster {monster?.Path}: {ex.Message}");
                return false;
            }
        }
    }
}
