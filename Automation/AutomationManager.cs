using System.Collections.Generic;
using BetterFollowbotLite.Interfaces;

namespace BetterFollowbotLite.Automation
{
    /// <summary>
    /// Manages and coordinates all automation features
    /// </summary>
    internal class AutomationManager
    {
        private readonly List<ISkill> _skills = new List<ISkill>();
        private readonly List<IAutomation> _automations = new List<IAutomation>();

        /// <summary>
        /// Registers a skill for execution
        /// </summary>
        public void RegisterSkill(ISkill skill)
        {
            if (skill != null && !_skills.Contains(skill))
            {
                _skills.Add(skill);
            }
        }

        /// <summary>
        /// Registers an automation feature for execution
        /// </summary>
        public void RegisterAutomation(IAutomation automation)
        {
            if (automation != null && !_automations.Contains(automation))
            {
                _automations.Add(automation);
            }
        }

        /// <summary>
        /// Unregisters a skill
        /// </summary>
        public void UnregisterSkill(ISkill skill)
        {
            if (skill != null)
            {
                _skills.Remove(skill);
            }
        }

        /// <summary>
        /// Unregisters an automation feature
        /// </summary>
        public void UnregisterAutomation(IAutomation automation)
        {
            if (automation != null)
            {
                _automations.Remove(automation);
            }
        }

        /// <summary>
        /// Executes all enabled skills and automation features
        /// </summary>
        public void ExecuteAll()
        {
            // Execute all enabled skills
            foreach (var skill in _skills)
            {
                if (skill.IsEnabled)
                {
                    skill.Execute();
                }
            }

            // Execute all enabled automation features
            foreach (var automation in _automations)
            {
                if (automation.IsEnabled)
                {
                    automation.Execute();
                }
            }
        }

        /// <summary>
        /// Gets all registered skills
        /// </summary>
        public IReadOnlyList<ISkill> Skills => _skills.AsReadOnly();

        /// <summary>
        /// Gets all registered automation features
        /// </summary>
        public IReadOnlyList<IAutomation> Automations => _automations.AsReadOnly();

        /// <summary>
        /// Gets the count of enabled skills
        /// </summary>
        public int EnabledSkillsCount
        {
            get
            {
                int count = 0;
                foreach (var skill in _skills)
                {
                    if (skill.IsEnabled) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Gets the count of enabled automation features
        /// </summary>
        public int EnabledAutomationsCount
        {
            get
            {
                int count = 0;
                foreach (var automation in _automations)
                {
                    if (automation.IsEnabled) count++;
                }
                return count;
            }
        }
    }
}
