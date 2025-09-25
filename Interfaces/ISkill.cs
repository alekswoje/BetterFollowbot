namespace BetterFollowbot.Interfaces
{
    /// <summary>
    /// Interface for skill-based automation features
    /// </summary>
    public interface ISkill
    {
        /// <summary>
        /// Execute the skill logic
        /// </summary>
        void Execute();

        /// <summary>
        /// Gets whether this skill is enabled
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Gets the name of the skill for logging/debugging
        /// </summary>
        string SkillName { get; }
    }
}
