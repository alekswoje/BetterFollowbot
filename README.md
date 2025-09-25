# BetterFollowbot

A Path of Exile plugin for ExileCore/PoeHelper that provides automated follow bot functionality with intelligent skill usage and navigation.

## Features

- **Automated Following**: Intelligent pathfinding and following of party leader
- **Skill Automation**: Automated casting of auras, blessings, and combat skills
- **Portal Management**: Smart portal detection and usage for zone transitions
- **Combat Support**: Automated monster targeting and attack routines
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
- **Aura Blessing**: Smart Holy Relic + Zealotry management (proactive minion health monitoring, flexible buff detection)
- **Flame Link**: Party member linking functionality
- **Smite**: Automated smite buff maintenance
- **Vaal Haste**: Automated vaal haste activation when available
- **Vaal Discipline**: Automated vaal discipline when energy shield drops below threshold

## Settings

Access plugin settings through the ExileCore menu:
- Enable/Disable individual features
- Configure hotkeys and thresholds
- Adjust automation parameters (e.g., Holy Relic health threshold, Vaal Discipline ES% threshold)
- Debug mode for troubleshooting

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
