# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this
project might one day adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [Unreleased]


## [0.0.4] - 2026-02-28
### User-facing
- Added speed options for the lab oven and chemistry station.
- Patches now register more reliably and log failures more clearly.

### Developer
- Refactored patch registration logic and enhanced logging output.


## [0.0.3] - 2026-02-24
### User-facing
- Adjusted default values for mixing station and drying rack capacities (previously 1, now 3).

### Developer
- Removed or quieted several UI tooltips on multiplier sliders.
- Refactored startup and configuration logic into `ConfigManager`; helpers now live under `Utilities`.
- Logging methods differentiate between verbose and standard messages.

### Bug Fixes
- Drying rack capacity was not being enforced correctly; fixed.
- Host-client enforcement logic for server-authoritative config was inverted.
- Warn on missing default multipliers rather than crashing.


## [0.0.2] - 2026-02-24
### User-facing
- Introduced adjustable cauldron cook speed.  The related ingredient-multiplier setting is currently a placeholder and has no effect on input/output stacks.
- Configuration is stored in `BetterStacksF.cfg` under user data; visible in ModsApp.

### Developer
- Added networking adapter and SteamNetworkLib integration for multiplayer settings propagation.
- Migrated preferences to MelonPreferences; removed old JSON store.
- Introduced `LoggingHelper` utility.
- Added helper for locating user data directory and updated README.
- Cleaned up reflection code calculating stack limits.


## [0.0.1] - 2026-02-21
### Notes
- First commit after the fork.  The repository history stretches back into
  the original BetterStacks project (see earlier commits in `git log`).  This
  entry doesn’t represent an independent BetterStacksF feature release; it
  simply records the state of the code when development began herein.

### User-facing
- Baseline behaviour inherited from the original mod: increased stack sizes
  for all item categories and adjusted workstation capacities in Schedule I.

### Developer
- Included an early logging refactor separating verbose and standard outputs
  and the initial migration to MelonPreferences.


[Unreleased]: https://github.com/JakeRoxs/BetterStacksF/compare/v0.0.4...HEAD
[0.0.4]: https://github.com/JakeRoxs/BetterStacksF/compare/v0.0.3...v0.0.4
[0.0.3]: https://github.com/JakeRoxs/BetterStacksF/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/JakeRoxs/BetterStacksF/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/JakeRoxs/BetterStacksF/releases/tag/v0.0.1
