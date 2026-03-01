using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using BetterStacksF;
using BetterStacksF.Config;
using BetterStacksF.Networking;
using BetterStacksF.Patches;
using BetterStacksF.Utilities;

using HarmonyLib;

using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI.Stations;

using MelonLoader;
using MelonLoader.Utils;

using Newtonsoft.Json;

using S1API.Lifecycle;

[assembly: MelonInfo(typeof(BetterStacksFMod), "BetterStacksF", "0.0.5", "JakeRoxs")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace BetterStacksF;

public class BetterStacksFMod : MelonMod {
  // configuration now managed by ConfigManager


  // (previous reflection helpers have been migrated to Utilities.ReflectionHelper and are no longer present here)

  // Compute multiplier for a category from the supplied config (falling back to the
  // globally loaded config and logging missing defaults).
  internal static int GetModifierForCategory(ModConfig? cfg, EItemCategory category) {
    if (cfg?.CategoryMultipliers != null &&
        cfg.CategoryMultipliers.TryGetValue(category.ToString(), out var m))
      return Math.Max(1, m);

    var global = ConfigManager.CurrentConfig;
    if (global?.CategoryMultipliers != null &&
        global.CategoryMultipliers.TryGetValue(category.ToString(), out var m2))
      return Math.Max(1, m2);

    // warn if one of the baked-in defaults is missing
    switch (category) {
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

  public override void OnInitializeMelon() {
    // retrieve the saved configuration and make it the active value; the
    // manager no longer attempts to validate or mutate the config for us.
    var cfg = ConfigManager.LoadConfig();

    // ensure the category‑multiplier dictionary exists and schedule any
    // deferred game‑defs sync.  this used to be called from
    // ConfigManager.LoadConfig; the coupling has been removed.
    try {
      EnsureCategoryMultipliers(cfg, addEnumKeys: true);
    }
    catch (Exception ex) {
      LoggingHelper.Error("Initial EnsureCategoryMultipliers failed", ex);
    }

    // apply overrides immediately so that any patch logic running during the
    // first frame sees the correct values.  subsequent config updates will be
    // driven via ProcessPendingUpdates.
    StackOverrideManager.ApplyStackOverrides(cfg);

    // also ensure we re-apply overrides when the game does a pre-load pass;
    // this covers the case where item definitions weren't ready during the
    // initial call above and mirrors the behaviour of the old implementation.
    GameLifecycle.OnPreLoad += ApplyStackOverrides;

    // move the existing log/broadcast logic here so it reflects the config we
    // just loaded/validated
    // Ensure category-multiplier MelonPreferences entries are registered early so
    // ModsApp / other preference editors can modify the same MelonPreferences_Entry
    // instances we read from in PollAndApplyChanges.
    try { PreferencesMapper.RegisterCategoryMultipliersFromGameDefs(); } catch { }

    // local adapter when Steam is definitely not available.
    var steamAdapter = new SteamNetworkAdapter();
    NetworkingManager.Initialize(steamAdapter);

    if (!NetworkingManager.CurrentAdapter.IsInitialized) {
      // If the Steam adapter deferred initialization because Steam/Steamworks wasn't ready yet,
      // keep the Steam adapter as the active adapter so it can attempt initialization later.
      if (steamAdapter is SteamNetworkAdapter s && s.InitializationDeferred) {
        LoggingHelper.Msg("Steam adapter initialization deferred — will retry while running.");
      }
      else {
        LoggingHelper.Init("Steam adapter not available — falling back to local adapter.");
        NetworkingManager.Initialize(new LocalNetworkAdapter());
      }
    }
    else {
      LoggingHelper.Init("SteamNetworkAdapter initialized.");
    }

    // Log the loaded configuration so we can verify which category multipliers are active at runtime.
    // This message respects the verbose-logging preference, so end users may
    // enable it at runtime when investigating issues.
    LoggingHelper.Msg("Loaded config", cfg);

    // If we're the session host, immediately broadcast the authoritative HostConfig so clients apply the same settings.
    if (NetworkingManager.CurrentAdapter?.IsHost ?? false) {
      NetworkingManager.BroadcastHostConfig(new HostConfig { Config = cfg });
      LoggingHelper.Init("Broadcasted HostConfig (host).");
    }

    var harmony = new HarmonyLib.Harmony("com.jakeroxs.betterstacks");
    FileLog.LogWriter = new StreamWriter("harmony.log") { AutoFlush = true };

    // register all active patches

    // mixing station capacity
    harmony.Patch(
        AccessTools.Method(typeof(MixingStation), "Start"),
        prefix: new HarmonyMethod(typeof(MixingStationPatches), nameof(MixingStationPatches.PatchMixingStationCapacity))
    );

    // drying rack capacity
    harmony.Patch(
        AccessTools.Method(typeof(DryingRackCanvas), "SetIsOpen"),
        prefix: new HarmonyMethod(typeof(DryingRackPatches), nameof(DryingRackPatches.PatchDryingRackCapacity))
    );

    // cauldron: patch all StartCookOperation overloads
    {
      var cauldronType = typeof(Cauldron);
      var prefix = new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Prefix_StartCookOperation));
      foreach (var m in cauldronType.GetMethods(System.Reflection.BindingFlags.Instance |
                                                System.Reflection.BindingFlags.Public |
                                                System.Reflection.BindingFlags.NonPublic)) {
        if (m.Name == "StartCookOperation")
          harmony.Patch(m, prefix: prefix);
      }
    }

    // chemistry station (ops + UI)
    {
      harmony.Patch(
          AccessTools.Method(typeof(Il2CppScheduleOne.ObjectScripts.ChemistryStation), "SendCookOperation"),
          prefix: new HarmonyMethod(typeof(ChemistryStationPatches), nameof(ChemistryStationPatches.Prefix_SendCookOperation))
      );

      harmony.Patch(
          AccessTools.Method(typeof(Il2CppScheduleOne.ObjectScripts.ChemistryStation), "SetCookOperation"),
          prefix: new HarmonyMethod(typeof(ChemistryStationPatches), nameof(ChemistryStationPatches.Prefix_SetCookOperation))
      );

      harmony.Patch(
          AccessTools.Method(typeof(Il2CppScheduleOne.ObjectScripts.ChemistryStation), "FinalizeOperation"),
          prefix: new HarmonyMethod(typeof(ChemistryStationPatches), nameof(ChemistryStationPatches.Prefix_FinalizeOperation))
      );
    }

    // lab oven scaling is handled when cook operations are created, so only
    // SendCookOperation and SetCookOperation need to be patched here.
    // LabOven:
    // - Scaling is applied when cook operations are created, not during finalization.
    // - SendCookOperation is called on the host when it creates a new cook operation and
    //   sends it to clients over the network.
    // - SetCookOperation is called on clients when they receive the replicated cook operation
    //   from the host and apply it locally.
    // Patching these two points keeps host/client behavior in sync, so no FinalizeOperation
    // patch is required for LabOven.
    {
      harmony.Patch(
          AccessTools.Method(typeof(Il2CppScheduleOne.ObjectScripts.LabOven), "SendCookOperation"),
          prefix: new HarmonyMethod(typeof(LabOvenPatches), nameof(LabOvenPatches.Prefix_SendCookOperation))
      );

      harmony.Patch(
          AccessTools.Method(typeof(Il2CppScheduleOne.ObjectScripts.LabOven), "SetCookOperation"),
          prefix: new HarmonyMethod(typeof(LabOvenPatches), nameof(LabOvenPatches.Prefix_SetCookOperation))
      );
    }


  }


  // Ensure CategoryMultipliers exists and merge legacy typed properties into the dictionary.
  // When addEnumKeys is true the method may add/prune entries based on the game's item definitions.
  // If definitions aren't ready yet we don't touch the dictionary and schedule a deferred pass at OnPreLoad.
  private static bool _ensureCatMultScheduled = false;
  public static bool EnsureCategoryMultipliers(ModConfig cfg, bool addEnumKeys = true) {
    // wipe out any bogus entries that might creep in from modsapp or
    // earlier bugged configs; it's harmless even if called repeatedly.
    PreferencesMapper.SanitizeCategoryKeys(cfg);

    bool changed = EnsureCategoryDictionaryExists(cfg);

    if (addEnumKeys) {
      changed |= TryAddOrPruneEnumKeys(cfg);
    }

    return changed;
  }

  private static bool EnsureCategoryDictionaryExists(ModConfig cfg) {
    if (cfg.CategoryMultipliers == null) {
      cfg.CategoryMultipliers = new Dictionary<string, int>();
      return true;
    }
    return false;
  }

  private static bool TryAddOrPruneEnumKeys(ModConfig cfg) {
    bool changed = false;
    HashSet<string> presentNames;
    try {
      var defs = S1API.Items.ItemManager.GetAllItemDefinitions();
      if (defs == null || defs.Count == 0)
        return ScheduleCategoryMultiplierUpdate();

      presentNames = defs.Select(d => ((EItemCategory)d.Category).ToString()).Distinct().ToHashSet();
    }
    catch {
      // Unexpected failure; leave config alone.
      return changed;
    }

    // Add any categories that are present but not in config
    foreach (var name in presentNames) {
      if (!cfg.CategoryMultipliers.ContainsKey(name)) {
        cfg.CategoryMultipliers[name] = 1;
        changed = true;
      }
    }

    // Remove obsolete entries
    var toRemove = cfg.CategoryMultipliers.Keys.Where(k => !presentNames.Contains(k)).ToList();
    foreach (var k in toRemove) {
      cfg.CategoryMultipliers.Remove(k);
      changed = true;
      LoggingHelper.Warning($"Removing obsolete CategoryMultiplier '{k}' (not present in game data)");
    }

    return changed;
  }

  private static bool ScheduleCategoryMultiplierUpdate() {
    // Scheduling is purely about ensuring we run an update once the game
    // item definitions are available.  The handler will always use the
    // current configuration, so there's no need to accept or retain an
    // external ModConfig reference.
    if (!_ensureCatMultScheduled) {
      _ensureCatMultScheduled = true;
      GameLifecycle.OnPreLoad += DeferredCategoryMultiplierUpdate;
      LoggingHelper.Msg("Item definitions not ready; scheduled category-multiplier update for OnPreLoad.");

      return true;
    }

    return false;
  }

  // invoked from OnPreLoad when defs are ready.  handler is removed only after a
  // successful update
  private static void DeferredCategoryMultiplierUpdate() {
    try {
      EnsureCategoryMultipliers(ConfigManager.CurrentConfig, true);
      GameLifecycle.OnPreLoad -= DeferredCategoryMultiplierUpdate;
    }
    catch (Exception ex) {
      LoggingHelper.Error("DeferredCategoryMultiplierUpdate failed", ex);
      // leave the subscription intact so it can re‑run on the next pre‑load
    }
  }

  // Apply stack overrides using the global mod config file.  This method is
  // both invoked directly during initialization and is subscribed to
  // GameLifecycle.OnPreLoad (see OnInitializeMelon) so that overrides are
  // re‑applied once definitions become available.
  private static void ApplyStackOverrides() {
    StackOverrideManager.ApplyStackOverrides(ConfigManager.CurrentConfig);
  }

  // Public helper so other systems (network) can apply a supplied ModConfig.
  public static void ApplyStackOverridesUsing(ModConfig cfg) {
    StackOverrideManager.ApplyStackOverrides(cfg);
  }

  public static void EnqueueConfigUpdate(ModConfig cfg) => ConfigManager.EnqueueConfigUpdate(cfg);

  public override void OnUpdate() {
    // Drive incoming SteamNetworkLib callbacks / message processing.
    try { NetworkingManager.CurrentAdapter?.ProcessIncomingMessages(); } catch { }

    // forward pending config processing to manager
    ConfigManager.ProcessPendingUpdates();
  }

}
