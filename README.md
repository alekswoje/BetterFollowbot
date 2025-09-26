# BetterFollowbot

A Path of Exile plugin for ExileCore/PoeHelper that provides automated follow bot functionality with intelligent skill usage and navigation.

## Features

- **Automated Following**: Intelligent pathfinding and following of party leader
- **Comprehensive Skill Automation**: Automated casting of 20+ different skills including minions, auras, warcries, mines, and more
- **Portal Management**: Smart portal detection and usage for zone transitions
- **Combat Support**: Automated monster targeting and attack routines
- **Party Management**: Auto-join party and accept trade requests
- **Quality of Life**: Auto-respawn, auto-level gems, and smart UI blocking
- **Customizable Settings**: Extensive configuration options for all features

## Installation

1. Download or clone this repository
2. Copy the `BetterFollowbot` folder to your `Plugins/Source/` directory
3. Launch Path of Exile with ExileCore/PoeHelper
4. The plugin will be automatically compiled and loaded

## Usage

### Basic Setup
1. Enable the plugin in the settings menu
2. Set your leader's character name in the AutoPilot settings
3. Configure skill hotkeys and automation preferences
4. Toggle AutoPilot mode with the configured hotkey

### Key Features Configuration

#### AutoPilot
- **Leader Name**: Set the character name to follow
- **Movement Keys**: Configure movement and dash controls
- **Pathfinding**: Adjust node distance and clear path settings

#### Skill Automation

**Minion Skills:**
- **Summon Raging Spirits**: Intelligent SRS casting when rare/unique enemies are nearby
- **Summon Skeletons**: Automated skeleton summoning with configurable count thresholds
- **Rejuvenation Totem**: Smart totem placement based on party health and enemy presence

**Aura & Buff Skills:**
- **Aura Blessing**: Smart Holy Relic + Zealotry management (proactive minion health monitoring, flexible buff detection)
- **Smite Buff**: Automated smite buff maintenance with aggressive refresh timing
- **Vaal Haste**: Automated vaal haste activation when available
- **Vaal Discipline**: Automated vaal discipline when energy shield drops below threshold

**Link Skills:**
- **Flame Link**: Party member linking functionality with smart targeting
- **Protective Link**: Automated protective link maintenance for party members

**Warcry Skills:**
- **Ancestral Cry**: Automated ancestral cry usage
- **Infernal Cry**: Automated infernal cry usage
- **General's Cry**: Automated general's cry usage
- **Intimidating Cry**: Automated intimidating cry usage
- **Rallying Cry**: Automated rallying cry usage
- **Vengeful Cry**: Automated vengeful cry usage
- **Enduring Cry**: Automated enduring cry usage
- **Seismic Cry**: Automated seismic cry usage
- **Battlemage's Cry**: Automated battlemage's cry usage

**Mine Skills:**
- **Stormblast Mine**: Automated mine throwing at rare/unique enemies
- **Pyroclast Mine**: Automated pyroclast mine deployment with smart targeting

#### Automation Features

**Party Management:**
- **Auto Join Party**: Automatically accept party invites when not already in a party
- **Auto Accept Trade**: Automatically accept trade requests from party members

**Quality of Life:**
- **Auto Respawn**: Automatically respawn at checkpoint when death screen appears
- **Auto Level Gems**: Automatically level up gems when the level-up panel appears
- **Smart UI Blocking**: Prevents skill execution when UI elements are open (stash, inventory, etc.)

**Advanced Features:**
- **Intelligent Targeting**: Smart enemy detection using ReAgent-style validation
- **Party Health Monitoring**: Monitors party member health for totem placement and vaal skills
- **Distance-Based Logic**: Skills only activate when within appropriate range of leader
- **Cooldown Management**: Sophisticated cooldown system prevents skill spam

## Settings

Access plugin settings through the ExileCore menu:

**General Settings:**
- Enable/Disable individual features
- Configure hotkeys and thresholds
- Debug mode for troubleshooting
- Skill cooldown timing
- Hideout skill disabling

**AutoPilot Configuration:**
- Leader name and following distance
- Movement keys and dash settings
- Pathfinding node distance
- Clear path detection range

**Skill-Specific Settings:**
- **Minion Skills**: Count thresholds, range settings, targeting options
- **Aura & Buffs**: Health thresholds, ES percentages, refresh timing
- **Link Skills**: Range settings, refresh intervals
- **Warcries**: Individual enable/disable for each warcry type
- **Mines**: Range settings, mine count limits, leader distance
- **Vaal Skills**: ES threshold for Vaal Discipline

**Automation Settings:**
- Auto-respawn and post-respawn behavior
- Gem leveling cooldown timing
- Party auto-join and trade acceptance

## TODO / Roadmap

### High Priority
- [ ] **Fix Smite Casting Issues**: Resolve issues with smite not always casting properly
- [ ] **Update Pathfinding**: Replace current system with A* pathfinding algorithm
- [ ] **Fix Party TP Button**: Prevent bot from clicking party TP button when unavailable
- [ ] **Fix Long Distance Following**: Resolve issues when leader is too far away
- [ ] **Clean Up Codebase**: Major code cleanup and refactoring (current state is quite messy)

### From FollowBot Examples
- [ ] **Area-Specific Logic**: Add special handling for specific areas like "The Rottin Core" with custom transition logic
- [ ] **Portal Request System**: Automatic portal request when no portals are found
- [ ] **Labyrinth Support**: Clean up and enhance Aspirants' Plaza navigation (partially implemented)
- [ ] **Movement Skill Integration**: Clean up and enhance movement skill usage with auto-detection of bound skills (no specific keybind settings needed)
- [ ] **Clear Cursor Task**: Add dedicated cursor clearing functionality

### From Copilot Examples
- [ ] **SyncTask Implementation**: Replace async/await with SyncTask for better performance
- [ ] **Modular Task System**: Implement flexible task management with individual enable/disable controls
- [ ] **Custom Task Support**: Add ability to create and manage custom tasks
- [ ] **Enhanced Logging**: Implement LoggerPlus system for better debugging
- [ ] **Settings Handler**: Add sophisticated settings management with submenus
- [ ] **Blink/Movement Enhancement**: Add space key movement option
- [ ] **Item Pickup System**: Add advanced item pickup with filtering (or integrate with existing PickIt)
- [ ] **Guild Stash Integration**: Add guild stash dumper functionality (may already be in TODO)
- [ ] **UI Checker Task**: Replace current UI checker system with one integrating tasks and coroutines
- [ ] **Task Runner Lifecycle**: Implement better task lifecycle management

### Skill Enhancements
- [ ] **High-Impact Mine Support**: Add support for high-impact mine support skills
- [ ] **Guardian Block Support**: Add "if you've attacked recently gain attack block" and "if you've cast a spell gain spell block" logic
- [ ] **Banner Logic**: Implement banner placing functionality
- [ ] **All Link Skills**: Add support for all remaining link skills (easy implementation)
- [ ] **All Mine Skills**: Add support for all remaining mine skills (easy implementation)
- [ ] **Dash Skill Options**: Add support for Shield Charge, Frost Blades of Wintery Blast, Leap Slam as dash alternatives

### Movement & Navigation
- [ ] **Better Dash System**: Replace hard-coded 3s cooldown with dynamic dash system
- [ ] **Portal Clicking Fix**: Fix issues with tall portals or portals not on screen
- [ ] **Entity Interaction Fix**: Prevent clicking ritual altars, NPCs, and other unwanted entities

### Automation Features
- [ ] **Auto Stash**: Automatically stash items
- [ ] **Auto Trade to Leader**: Automatically trade all items to party leader
- [ ] **Auto Guild Stash**: Support for guild stash automation
- [ ] **Quest Item Filtering**: Filter out low-value quest items
- [ ] **Auto NPC Interaction**: Auto-talk to NPCs and take quest rewards
- [ ] **Skill Point Books**: Auto-use skill point books
- [ ] **Trial of Ascendancy**: Auto-click trial altars
- [ ] **Lab Ascendancy**: Auto-click final ascendancy altar in lab
- [ ] **Quest State Doors**: Auto-click quest state doors like the tower in Act 4
- [ ] **Valuable Uniques**: Auto-pickup valuable unique items
- [ ] **5-Way Resetter**: Add support for 5-way resetter functionality
- [ ] **Auto Enable Auras**: Automatically enable auras when available

### Technical Improvements
- [ ] **Coroutine Focus**: Switch to a more coroutine-focused approach
- [ ] **SyncTask Integration**: Use SyncTask where applicable (see pickit implementation)
- [ ] **Task Management System**: Implement skills into task management system to avoid conflicting inputs
- [ ] **Multi-Task Support**: Support for multiple simultaneous tasks
- [ ] **Terrain Analyzer Rewrite**: Completely rewrite the terrain analyzer (currently inefficient and incomplete)

## Credits

This plugin is a fork of the original [CoPilot](https://github.com/totalschaden/copilot) by [totalschaden](https://github.com/totalschaden).

**Original Author**: totalschaden
**Fork Author**: alekswoje

Special thanks to:
- The ExileCore/PoeHelper development team
- All contributors to the original CoPilot project

## Contributing

Feel free to submit issues, feature requests, or pull requests to improve the plugin.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

The MIT License is a permissive license that allows anyone to do anything with this software as long as they include the original copyright and license notice.

## Disclaimer

This plugin is provided as-is for educational and entertainment purposes. Use at your own risk. The developers are not responsible for any consequences of using this software.
