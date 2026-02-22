using BetterStacks;
using BetterStacks.Networking;
using BetterStacks.Utilities;
using HarmonyLib;
using MelonLoader;

using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using MelonLoader.Utils;
using Newtonsoft.Json;
using System.Reflection;
using S1API.Lifecycle;
using System.Linq;
using System.Collections.Generic;
using System;


using BetterStacks.Config;
using BetterStacks.Patches;

[assembly: MelonInfo(typeof(BetterStacksMod), "BetterStacksF", "0.0.1", "JakeRoxs")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace BetterStacks;

public class BetterStacksMod : MelonMod
{
    // configuration now managed by ConfigManager


    // (previous reflection helpers have been migrated to Utilities.ReflectionHelper and are no longer present here)

    // Compute multiplier for a category from the supplied config (falling back to the
    // globally loaded config and logging missing defaults).
    internal static int GetModifierForCategory(ModConfig? cfg, EItemCategory category)
    {
        if (cfg?.CategoryMultipliers != null &&
            cfg.CategoryMultipliers.TryGetValue(category.ToString(), out var m))
            return Math.Max(1, m);

        var global = ConfigManager.CurrentConfig;
        if (global?.CategoryMultipliers != null &&
            global.CategoryMultipliers.TryGetValue(category.ToString(), out var m2))
            return Math.Max(1, m2);

        // warn if one of the baked-in defaults is missing
        switch (category)
        {
            case EItemCategory.Product:
            case EItemCategory.Packaging:
            case EItemCategory.Agriculture:
            case EItemCategory.Ingredient:
                LoggingHelper.Warning($"Expected default multiplier for category '{category}' not found in config/game defs.");
                break;
        }

        return 1;
    }

    // Indicates whether the current instance is being constrained by a host/server-authoritative config.
    public static bool ServerAuthoritative { get; internal set; } = false;

    // Convenience: whether the loaded mod config requests server-authoritative behavior.
    public static bool ServerAuthoritativeEnabled => ConfigManager.CurrentConfig?.EnableServerAuthoritativeConfig ?? false;

    // Expose current config for helpers/patches.
    public static ModConfig CurrentConfig => ConfigManager.CurrentConfig;

    public override void OnInitializeMelon()
    {
        ConfigManager.LoadConfig();

        // Ensure category-multiplier MelonPreferences entries are registered early so
        // ModsApp / other preference editors can modify the same MelonPreferences_Entry
        // instances we read from in PollAndApplyChanges.
        try { PreferencesMapper.RegisterCategoryMultipliersFromGameDefs(); } catch { }

        // local adapter when Steam is definitely not available.
        var steamAdapter = new SteamNetworkAdapter();
        NetworkingManager.Initialize(steamAdapter);

        if (!NetworkingManager.CurrentAdapter.IsInitialized)
        {
            // If the Steam adapter deferred initialization because Steam/Steamworks wasn't ready yet,
            // keep the Steam adapter as the active adapter so it can attempt initialization later.
            if (steamAdapter is SteamNetworkAdapter s && s.InitializationDeferred)
            {
#if DEBUG
                LoggingHelper.Msg("Steam adapter initialization deferred — will retry while running.");
#endif
            }
            else
            {
                LoggingHelper.Init("Steam adapter not available — falling back to local adapter.");
                NetworkingManager.Initialize(new LocalNetworkAdapter());
            }
        }
        else
        {
            LoggingHelper.Init("SteamNetworkAdapter initialized.");
        }

        // Log the loaded configuration so we can verify which category multipliers are active at runtime.
        LoggingHelper.Init($"Loaded config: {JsonConvert.SerializeObject(ConfigManager.CurrentConfig)}");

        // If we're the session host, immediately broadcast the authoritative HostConfig so clients apply the same settings.
        if (NetworkingManager.CurrentAdapter?.IsHost ?? false)
        {
            NetworkingManager.BroadcastHostConfig(new HostConfig { Config = ConfigManager.CurrentConfig });
            LoggingHelper.Init("Broadcasted HostConfig (host).");
        }

        var harmony = new HarmonyLib.Harmony("com.jakeroxs.betterstacks");
        FileLog.LogWriter = new StreamWriter("harmony.log") { AutoFlush = true };



        // Patch Mixing Station capacity — implementation moved to Patches/MixingStationPatches.cs
        harmony.Patch(
            AccessTools.Method(typeof(MixingStation), "Start"),
            prefix: new HarmonyMethod(typeof(MixingStationPatches), nameof(MixingStationPatches.PatchMixingStationCapacity))
        );

        // Patch Drying Rack capacity — implementation moved to Patches/DryingRackPatches.cs
        // Use TypeByName to avoid compile-time dependency on the class (some builds lack the
        // DryingRackCanvas type).  AccessTools.Method gracefully handles a null type.
        harmony.Patch(
            AccessTools.Method(AccessTools.TypeByName("DryingRackCanvas"), "SetIsOpen"),
            prefix: new HarmonyMethod(typeof(DryingRackPatches), nameof(DryingRackPatches.PatchDryingRackCapacity))
        );

        // Delivery stack limit handled by S1API.ApplyStackOverrides

    }

    public static ModConfig LoadConfig()
    {
        // kept for backwards compatibility but forwarded to ConfigManager
        return ConfigManager.LoadConfig();
    }

    // Ensure CategoryMultipliers exists and merge legacy typed properties into the dictionary.
    // When addEnumKeys is true the method may add/prune entries based on the game's item definitions.
    // If definitions aren't ready yet we don't touch the dictionary and schedule a deferred pass at OnPreLoad.
    private static bool _ensureCatMultScheduled = false;
    public static bool EnsureCategoryMultipliers(ModConfig cfg, bool addEnumKeys = true)
    {
        bool changed = false;
        if (cfg.CategoryMultipliers == null)
        {
            cfg.CategoryMultipliers = new Dictionary<string, int>();
            changed = true;
        }

        if (addEnumKeys)
        {
            HashSet<string> presentNames;
            try
            {
                var defs = S1API.Items.ItemManager.GetAllItemDefinitions();
                if (defs == null || defs.Count == 0)
                {
                    // definitions not ready: schedule a pass and return without altering keys
                    if (!_ensureCatMultScheduled)
                    {
                        _ensureCatMultScheduled = true;
                        GameLifecycle.OnPreLoad += () => { try { EnsureCategoryMultipliers(cfg, true); } catch { } };
#if DEBUG
                        LoggingHelper.Msg("Item definitions not ready; scheduled category-multiplier update for OnPreLoad.");
#endif
                    }
                    return changed;
                }
                presentNames = defs.Select(d => ((EItemCategory)d.Category).ToString()).Distinct().ToHashSet();
            }
            catch
            {
                // Unexpected failure; do nothing.
                return changed;
            }

            // Add missing present categories.
            foreach (var name in presentNames)
            {
                if (!cfg.CategoryMultipliers.ContainsKey(name))
                {
                    cfg.CategoryMultipliers[name] = 1;
                    changed = true;
                }
            }

            // Prune keys that aren't actually in game defs and log them.
            var toRemove = cfg.CategoryMultipliers.Keys.Where(k => !presentNames.Contains(k)).ToList();
            foreach (var k in toRemove)
            {
                cfg.CategoryMultipliers.Remove(k);
                changed = true;
                LoggingHelper.Warning($"Removing obsolete CategoryMultiplier '{k}' (not present in game data)");
            }
        }

        return changed;
    }

    // Apply stack overrides using the global mod config file (registered on OnPreLoad).
    // This is the method subscribed to GameLifecycle.OnPreLoad.
    private static void ApplyStackOverrides()
    {
        StackOverrideManager.ApplyStackOverrides(ConfigManager.CurrentConfig);
    }

    // Public helper so other systems (network) can apply a supplied ModConfig.
    public static void ApplyStackOverridesUsing(ModConfig cfg)
    {
        StackOverrideManager.ApplyStackOverrides(cfg);
    }

    public static void EnqueueConfigUpdate(ModConfig cfg) => ConfigManager.EnqueueConfigUpdate(cfg);

    public override void OnUpdate()
    {
        // Drive incoming SteamNetworkLib callbacks / message processing.
        try { NetworkingManager.CurrentAdapter?.ProcessIncomingMessages(); } catch { }

        // forward pending config processing to manager
        ConfigManager.ProcessPendingUpdates();
    }






}
