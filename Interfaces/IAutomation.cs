namespace BetterFollowbotLite.Interfaces
{
    /// <summary>
    /// Interface for general automation features (non-skill based)
    /// </summary>
    public interface IAutomation
    {
        /// <summary>
        /// Execute the automation logic
        /// </summary>
        void Execute();

        /// <summary>
        /// Gets whether this automation is enabled
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Gets the name of the automation for logging/debugging
        /// </summary>
        string AutomationName { get; }
    }
}
