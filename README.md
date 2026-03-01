# BetterStacksF Schedule I Mod

This repo contains the source code for the BetterStacksF melonloader mod.

It’s basically the same idea as before, just smarter, more flexible, and more multiplayer-safe.  
Forked from the original Better Stacks project, which is licensed under the MIT License; this code continues under the same terms.

---

## Description

**Forked from Better Stacks**  
A smarter, more flexible stack-size and workstation adjustment mod that syncs in multiplayer.

* **Per-category multipliers** – every item category can now have its own stack-size setting, and new categories are auto-registered from the game data.  
* **Workstation tuning** – tweak the capacities and speeds of various crafting appliances.  You can adjust mixing-station capacity, drying-rack capacity, and the speeds of the mixing station, cauldron, lab oven, and chemistry station.  
* **Clean, exposed configuration** – all options live in a dedicated prefs file (`BetterStacksF.cfg`) and are editable via ModsApp or by hand.  
* **Verbose logging toggle** – a checkbox in the MelonPreferences UI allows you to enable extra debug/info output when troubleshooting; it’s off by default in release builds.  
* **Host-authoritative multiplayer** – the host sends its current settings to everyone; clients respect the `EnableServerAuthoritativeConfig` toggle and won’t save conflicting values.  

Nexus: https://www.nexusmods.com/schedule1/mods/1619?tab=description  
Thunderstore: Not yet published on Thunderstore.

---

## Local Build Configuration

The project needs to know where your local copy of Schedule I is installed so it can reference the correct assemblies (MelonLoader, S1API, etc.).

Rather than specifying individual paths the csproj uses a single `ScheduleRoot` value and derives all of the required locations (MelonLoader path, network library location, mod files, etc.) from it.

To keep personal paths out of source control we use a `Directory.Build.local.props` file that defines the `ScheduleRoot` property:

1. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props`.
2. Edit the `<ScheduleRoot>` element to point at your own Schedule I installation directory.

The main `Directory.Build.props` checked in to the repo simply imports the `.local.props` file if it exists, and otherwise contains a placeholder value for `ScheduleRoot`.  The `Directory.Build.local.props` filename is ignored by Git (see `.gitignore`).

---
