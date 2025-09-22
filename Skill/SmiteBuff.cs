using System;
using System.Linq;
using BetterFollowbotLite.Interfaces;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
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
            // Loop through all skills to find Smite skill
            foreach (var skill in _instance.skills)
            {
                if (skill.Id == SkillInfo.smite.Id)
                {
                    _instance.LogMessage("SMITE: Smite skill detected");

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
                            _instance.LogMessage("SMITE: Cooldown check passed");

                            // Check if we don't have the smite buff or it's about to expire
                            var smiteBuff = _instance.buffs.FirstOrDefault(x => x.Name == "smite_buff");
                            var hasSmiteBuff = smiteBuff != null;
                            var buffTimeLeft = smiteBuff?.Timer ?? 0;
                            _instance.LogMessage($"SMITE: Has smite buff: {hasSmiteBuff}, Time left: {buffTimeLeft:F1}s");

                            // Refresh if no buff or buff has less than 2 seconds left
                            if (!hasSmiteBuff || buffTimeLeft < 2.0f)
                            {
                                _instance.LogMessage("SMITE: No smite buff found, looking for targets");

                                // Find monsters within 250 units of player (smite attack range)
                                var targetMonster = _instance.enemys
                                    .Where(monster =>
                                    {
                                        // Check if monster is within 250 units of player
                                        var distanceToPlayer = Vector3.Distance(_instance.playerPosition, monster.Pos);
                                        // Check if monster is on screen (can be targeted)
                                        var screenPos = _instance.GameController.IngameState.Camera.WorldToScreen(monster.Pos);
                                        var isOnScreen = _instance.GameController.Window.GetWindowRectangleTimeCache.Contains(screenPos);
                                        _instance.LogMessage($"SMITE: Monster at distance {distanceToPlayer:F1} from player, on screen: {isOnScreen}");
                                        return distanceToPlayer <= 250 && isOnScreen;
                                    })
                                    .OrderBy(monster => Vector3.Distance(_instance.playerPosition, monster.Pos)) // Closest first
                                    .FirstOrDefault();

                                if (targetMonster != null)
                                {
                                    _instance.LogMessage("SMITE: Found suitable target, activating smite!");

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
                                        _instance.LogMessage("SMITE: Smite activated successfully");
                                    }
                                    else
                                    {
                                        _instance.LogMessage($"SMITE: Mouse positioning failed, distance: {distanceFromTarget:F1}");
                                    }
                                }
                            }
                            else
                            {
                                _instance.LogMessage("SMITE: Smite buff still active, skipping");
                            }
                        }
                        else
                        {
                            _instance.LogMessage("SMITE: Insufficient mana for smite");
                        }
                    }
                    else
                    {
                        _instance.LogMessage("SMITE: Cooldown check failed");
                    }
                }
            }
        }
    }
}
