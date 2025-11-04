using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using System.Windows.Forms;
using SharpDX;

namespace BetterFollowbot.Core.TaskManagement;

public class TaskNode
{
    /// <summary>
    /// The position of the task in world space.
    /// </summary>
    public Vector3 WorldPosition { get; set; }
    /// <summary>
    /// The position of the task in UI space.
    /// </summary>
    public Vector2 UiPosition { get; set; }
    /// <summary>
    /// Type of task we are performing. Different tasks have different underlying logic
    /// </summary>
    public TaskNodeType Type { get; set; }
    /// <summary>
    /// Bounds represents how close we must get to the node to complete it. 
    /// Some tasks require multiple actions within Bounds to be marked as complete.
    /// </summary>
    public int Bounds { get; set; }

    /// <summary>
    /// Counts the number of times the Task has been executed. Used for canceling invalid actions
    /// </summary>
    public int AttemptCount { get; set; }
    public LabelOnGround LabelOnGround { get; set; }
    
    // ===== NEW: Skill-specific fields =====
    
    /// <summary>
    /// Name of the skill for logging/debugging (e.g., "flame_link", "smite")
    /// </summary>
    public string SkillName { get; set; }
    
    /// <summary>
    /// Target entity for skills that need a target (e.g., links, smite)
    /// </summary>
    public Entity TargetEntity { get; set; }
    
    /// <summary>
    /// The skill bar slot index (1-12) for this skill
    /// </summary>
    public int SkillSlotIndex { get; set; }
    
    /// <summary>
    /// The keyboard key to press to activate this skill
    /// </summary>
    public Keys SkillKey { get; set; }
    
    /// <summary>
    /// Additional skill-specific data (player name, mine count, etc.)
    /// </summary>
    public SkillExecutionData SkillData { get; set; }



    public TaskNode(Vector3 position, int bounds, TaskNodeType type = TaskNodeType.Movement)
    {
        WorldPosition = position;
        Type = type;
        Bounds = bounds;
    }
    public TaskNode(LabelOnGround labelOnGround, int bounds, TaskNodeType type = TaskNodeType.Movement)
    {
        LabelOnGround = labelOnGround;
        Type = type;
        Bounds = bounds;
    }
}
public enum TaskNodeType
{
    // Movement and Navigation Tasks
    Movement,
    Transition,
    ClaimWaypoint,
    Dash,
    TeleportConfirm,
    TeleportButton,
    
    // Skill Tasks (NEW - for task-based skill execution)
    FlameLink,          // Target a party member for flame link
    ProtectiveLink,     // Target a party member for protective link
    DestructiveLink,    // Target a party member for destructive link
    SmiteBuff,          // Use smite skill
    Warcry,             // Use warcry skill
    Mine,               // Deploy/detonate mines
    VaalSkill,          // Use vaal skill
    Summon,             // Summon skill (spirits/skeletons)
    Totem,              // Place totem
    Blessing            // Use blessing skill
}

/// <summary>
/// Container for skill-specific execution data
/// </summary>
public class SkillExecutionData
{
    /// <summary>
    /// Target player name (for links and party-targeted skills)
    /// </summary>
    public string TargetPlayerName { get; set; }
    
    /// <summary>
    /// Number of mines to deploy (for mine skills)
    /// </summary>
    public int MineCount { get; set; }
    
    /// <summary>
    /// Whether skill requires detonation after placement (for mines)
    /// </summary>
    public bool RequiresDetonation { get; set; }
    
    /// <summary>
    /// Required mana cost for skill
    /// </summary>
    public float RequiredMana { get; set; }
    
    /// <summary>
    /// Reason for using this skill (for logging)
    /// </summary>
    public string Reason { get; set; }
    
    /// <summary>
    /// Distance to target when task was created
    /// </summary>
    public float DistanceToTarget { get; set; }
    
    /// <summary>
    /// Time since last use of this skill
    /// </summary>
    public float TimeSinceLastUse { get; set; }
}
