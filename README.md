# BetterStacksF Schedule I Mod

This repo contains the source code for the BetterStacksF melonloader mod.

It’s basically the same idea as before, just smarter, more flexible, and more multiplayer-safe.

- Stack sizes can be adjusted for every category individually.
- In multiplayer the host sends the settings to everyone else, so all players share the same rules.
- The mod remembers the original values so it never multiplies the same stack twice.
- Configuration is now stored in MelonPreferences (in `MelonPreferences.cfg` under the "BetterStacks" category) and may be edited via ModsApp or by hand.  The old `BetterStacks.json` file is ignored; if it exists you can delete it.
- The code is organised into separate pieces (config, networking, patches) to keep it easier to work on.

---

## Local Build Configuration

The project needs to know where your local copy of Schedule I is installed so it can reference the correct assemblies (MelonLoader, S1API, etc.).

Rather than specifying individual paths the csproj uses a single `ScheduleRoot` value and derives all of the required locations (MelonLoader path, network library location, mod files, etc.) from it.

To keep personal paths out of source control we use a `Directory.Build.local.props` file that defines the `ScheduleRoot` property:

1. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props`.
2. Edit the `<ScheduleRoot>` element to point at your own Schedule I installation directory.

The main `Directory.Build.props` checked in to the repo simply imports the `.local.props` file if it exists, and otherwise contains a placeholder value for `ScheduleRoot`.  The `Directory.Build.local.props` filename is ignored by Git (see `.gitignore`).

This way everyone can have a different path without polluting commits.

---
