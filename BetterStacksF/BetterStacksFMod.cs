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
using S1API.Items;

using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI.Stations;

using MelonLoader;
using MelonLoader.Utils;

using Newtonsoft.Json;


[assembly: MelonInfo(typeof(BetterStacksFMod), "BetterStacksF", "0.0.8", "JakeRoxs")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace BetterStacksF;

public class BetterStacksFMod : MelonMod {
  // configuration now managed by ConfigManager

  // keep track of the last-known preferences state so we can compare when
  // `OnPreferencesSaved` fires.
  private ModConfig? _lastPrefs;

  // once created we hold a reference so update ticks can drive initialization
  // when SteamNetworkLib becomes ready.
  private SteamNetworkAdapter? _steamAdapter;

  // Compute multiplier for a category from the supplied config (falling back to the
  // globally loaded config and logging missing defaults).
  internal static string NormalizeCategoryKey(string categoryName) {
    if (string.IsNullOrWhiteSpace(categoryName))
      return categoryName ?? string.Empty;

    if (string.Equals(categoryName, "12", StringComparison.OrdinalIgnoreCase))
      return "Storage";

    if (string.Equals(categoryName, "Storage", StringComparison.OrdinalIgnoreCase))
      return "Storage";

    if (string.Equals(categoryName, "Agriculture", StringComparison.OrdinalIgnoreCase))
      return "Growing";

    return categoryName;
  }

  internal static string NormalizeCategoryKey(ItemCategory category) {
    var rawName = category.ToString();
    return NormalizeCategoryKey(rawName);
  }

  internal static int GetModifierForCategory(ModConfig? cfg, ItemCategory category) {
    var key = NormalizeCategoryKey(category);

    if (cfg?.CategoryMultipliers != null &&
        cfg.CategoryMultipliers.TryGetValue(key, out var m))
      return Math.Max(1, m);

    var global = ConfigManager.CurrentConfig;
    if (global?.CategoryMultipliers != null &&
        global.CategoryMultipliers.TryGetValue(key, out var m2))
      return Math.Max(1, m2);

    // warn if one of the baked-in defaults is missing
    switch (key) {
      case "Product":
      case "Packaging":
      case "Growing":
      case "Ingredient":
        LoggingHelper.Warning($"Expected default multiplier for category '{key}' not found in config/game defs.");
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
    _lastPrefs = cfg;

    // Log whether S1API is available
    LoggingHelper.Init($"S1API present: {S1ApiCompat.IsAvailable}");

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

    // create the Steam adapter now; we'll poll for Steam readiness in
    // OnUpdate and initialize it the first time the API is available.
    try {
      if (Type.GetType("SteamNetworkLib.SteamNetworkClient, SteamNetworkLib") != null) {
        _steamAdapter = new SteamNetworkAdapter();
        NetworkingManager.Initialize(_steamAdapter);
      } else {
        LoggingHelper.Warning("SteamNetworkLib not available at runtime; using local adapter.");
        _steamAdapter = null;
      }
    }
    catch (Exception ex) {
      LoggingHelper.Warning($"Steam network adapter initialization failed: {ex.Message}");
      _steamAdapter = null;
      NetworkingManager.Initialize(new LocalNetworkAdapter());
    }

    // clear reflection dump cache when the scene changes (e.g. returning to
    // main menu) so that subsequent DumpObject calls will log types again.
    S1ApiCompat.TrySubscribeToGameLifecycleEvent("OnPreSceneChange", () => ReflectionHelper.ResetDumpCache());

    // also ensure we re-apply overrides when the game does a pre-load pass;
    // this covers the case where item definitions weren't ready during the
    // initial call above and mirrors the behaviour of the old implementation.
    S1ApiCompat.TrySubscribeToGameLifecycleEvent("OnPreLoad", ApplyStackOverrides);

    // PreferenceMapper.EnsureRegistered (called by LoadConfig) has already
    // scheduled registration of category-multiplier entries.  an additional
    // explicit call here was previously causing a second disk read during
    // startup.


    // configuration logging is already handled by ConfigManager; the
    // previous call produces a nicely formatted snapshot.  suppress this
    // duplicate message to keep startup output brief.


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

    // cash stacking multiplier: apply category-derived cash chunking and cash instance limits
    {
      var npcInvType = AccessTools.TypeByName("Il2CppScheduleOne.NPCs.NPCInventory");
      if (npcInvType != null) {
        var addCashMethod = AccessTools.Method(npcInvType, "AddCash", new Type[] { typeof(float) });
        if (addCashMethod != null) {
          harmony.Patch(addCashMethod, prefix: new HarmonyMethod(typeof(CashPatches), nameof(CashPatches.Prefix_AddCash)));
          LoggingHelper.Msg("Patched NPCInventory.AddCash for cash multiplier stack behavior");
        } else {
          LoggingHelper.Warning("NPCInventory.AddCash method not found; cash multiplier patch not applied");
        }
      } else {
        LoggingHelper.Warning("NPCInventory type not found; cash multiplier patch not applied");
      }

      var moneyManagerType = AccessTools.TypeByName("Il2CppScheduleOne.Money.MoneyManager");
      if (moneyManagerType != null) {
        var getCashInstance = AccessTools.Method(moneyManagerType, "GetCashInstance", new Type[] { typeof(float) });
        if (getCashInstance != null) {
          harmony.Patch(getCashInstance, postfix: new HarmonyMethod(typeof(CashPatches), nameof(CashPatches.Postfix_GetCashInstance)));
          LoggingHelper.Msg("Patched MoneyManager.GetCashInstance for cash multiplier limit behavior");
        } else {
          LoggingHelper.Warning("MoneyManager.GetCashInstance method not found; cash multiplier patch not applied");
        }
      } else {
        LoggingHelper.Warning("MoneyManager type not found; cash multiplier patch not applied");
      }

      var cashInstanceType = AccessTools.TypeByName("Il2CppScheduleOne.ItemFramework.CashInstance");
      if (cashInstanceType != null) {
        var setBalanceMethod = AccessTools.Method(cashInstanceType, "SetBalance", new Type[] { typeof(float), typeof(bool) });
        var canStackMethod1 = AccessTools.Method(cashInstanceType, "CanStackWith", new Type[] { AccessTools.TypeByName("Il2CppScheduleOne.ItemFramework.ItemInstance"), typeof(bool) });
        var canStackMethod2 = AccessTools.Method(cashInstanceType, "CanStackWith", new Type[] { AccessTools.TypeByName("Il2CppScheduleOne.Core.Items.Framework.BaseItemInstance"), typeof(bool) });

        if (setBalanceMethod != null) {
          harmony.Patch(setBalanceMethod, prefix: new HarmonyMethod(typeof(CashPatches), nameof(CashPatches.Prefix_CashInstance_SetBalance)));
          LoggingHelper.Msg("Patched CashInstance.SetBalance for cash multiplier limit behavior");
        } else {
          LoggingHelper.Warning("CashInstance.SetBalance method not found; cash multiplier patch not applied");
        }

        if (canStackMethod1 != null) {
          harmony.Patch(canStackMethod1, prefix: new HarmonyMethod(typeof(CashPatches), nameof(CashPatches.Prefix_CashInstance_CanStackWith)));
          LoggingHelper.Msg("Patched CashInstance.CanStackWith(ItemInstance) for cash multiplier stacking behavior");
        } else {
          LoggingHelper.Warning("CashInstance.CanStackWith(ItemInstance) not found; cash multiplier stack patch not applied");
        }

        if (canStackMethod2 != null) {
          harmony.Patch(canStackMethod2, prefix: new HarmonyMethod(typeof(CashPatches), nameof(CashPatches.Prefix_CashInstance_CanStackWithBase)));
          LoggingHelper.Msg("Patched CashInstance.CanStackWith(BaseItemInstance) for cash multiplier stacking behavior");
        } else {
          LoggingHelper.Warning("CashInstance.CanStackWith(BaseItemInstance) not found; cash multiplier stack patch not applied");
        }
      } else {
        LoggingHelper.Warning("CashInstance type not found; cash multiplier patch not applied");
      }
    }

    // cauldron: patch all StartCookOperation overloads
    {
      var cauldronType = typeof(Cauldron);
      // also intercept UI open so we get slots before cooking starts
      var canvasType = AccessTools.TypeByName("Il2CppScheduleOne.UI.Stations.CauldronCanvas");
      if (canvasType != null) {
        var setOpen = FindMethodWithFallback(canvasType, "SetIsOpen");
        if (setOpen != null) {
          harmony.Patch(setOpen, prefix: new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Prefix_CauldronCanvas_SetIsOpen)));
          LoggingHelper.Msg("Patched CauldronCanvas.SetIsOpen");
        } else {
          LoggingHelper.Msg("CauldronCanvas.SetIsOpen method not found (skipping, may not exist in this build)");
        }
      } else {
        LoggingHelper.Warning("CauldronCanvas type not found");
      }
      var prefixStart = new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Prefix_StartCookOperation));
      var postfixStart = new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Postfix_StartCookOperation));
      int count = 0;
      foreach (var m in cauldronType.GetMethods(System.Reflection.BindingFlags.Instance |
                                                System.Reflection.BindingFlags.Public |
                                                System.Reflection.BindingFlags.NonPublic)) {
        if (m.Name == "StartCookOperation") {
          harmony.Patch(m, prefix: prefixStart, postfix: postfixStart);
          count++;
        }
      }
      LoggingHelper.Msg($"Patched {count} StartCookOperation overload(s) with prefix+postfix");

      // also intercept the remaining cook‑operation helpers so our flexible
      // consumption logic runs during networking/finalization paths.  these
      // methods are unique rather than overloads, so call Patch individually.
      var finish = FindMethodWithFallback(cauldronType, "FinishCookOperation", "FinalizeCookOperation", "CompleteCookOperation");
      if (finish != null) {
        harmony.Patch(finish, prefix: new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Prefix_FinishCookOperation)));
        LoggingHelper.Msg("Patched FinishCookOperation");
      } else {
        LoggingHelper.Msg("FinishCookOperation method not found on Cauldron (skipping, may not exist in this build)");
      }

      var send = FindMethodWithFallback(cauldronType, "SendCookOperation", "TransmitCookOperation", "NetworkCookOperation");
      if (send != null) {
        harmony.Patch(send,
            prefix: new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Prefix_SendCookOperation)),
            postfix: new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Postfix_SendCookOperation)));
        LoggingHelper.Msg("Patched SendCookOperation");
      } else {
        LoggingHelper.Msg("SendCookOperation method not found on Cauldron (skipping, may not exist in this build)");
      }

      var set = FindMethodByPattern(cauldronType, new[] {"SetCookOperation", "ApplyCookOperation", "ReceiveCookOperation"}, "Cauldron SetCookOperation");
      if (set != null) {
        harmony.Patch(set,
            prefix: new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Prefix_SetCookOperation)),
            postfix: new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Postfix_SetCookOperation)));
        LoggingHelper.Msg($"Patched Cauldron method {set.Name} as SetCookOperation-equivalent");
      } else {
        LoggingHelper.Msg("No SetCookOperation-equivalent method found on Cauldron (skipping). To match new versions, add candidate names to FindMethodByPattern.");
      }

      // ingredient visuals update – patch this to catch the moment when slots
      // become available for consumption (called whenever ingredients are
      // supplied to the cauldron)
      var updateVis = FindMethodWithFallback(cauldronType, "UpdateIngredientVisuals");
      if (updateVis != null) {
        harmony.Patch(updateVis, prefix: new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Prefix_UpdateIngredientVisuals)));
        LoggingHelper.Msg("Patched UpdateIngredientVisuals");
      } else {
        LoggingHelper.Msg("UpdateIngredientVisuals method not found on Cauldron (skipping, may not exist in this build)");
      }
    }

    // patch CauldronTask so we can capture ingredient state earlier in the
    // minigame while slots are still populated.  automated tasks invoke
    // CheckStep_CombineIngredients shortly before pressing Start.
    {
      var taskType = AccessTools.TypeByName("Il2CppScheduleOne.PlayerTasks.CauldronTask");
      if (taskType != null) {
        var method = FindMethodWithFallback(taskType, "CheckStep_CombineIngredients");
        if (method != null) {
          harmony.Patch(method, prefix: new HarmonyMethod(typeof(CauldronPatches), nameof(CauldronPatches.Prefix_TaskCombineIngredients)));
          LoggingHelper.Msg("Patched CauldronTask.CheckStep_CombineIngredients");
        } else {
          LoggingHelper.Msg("CauldronTask.CheckStep_CombineIngredients not found (skipping, may not exist in this build)");
        }
      } else {
        LoggingHelper.Warning("CauldronTask type not found");
      }
    }

    // chemistry station (ops + UI)
    {
      var chemType = typeof(Il2CppScheduleOne.ObjectScripts.ChemistryStation);
      var chemSend = FindMethodWithFallback(chemType, "SendCookOperation", "TransmitCookOperation", "NetworkCookOperation");
      if (chemSend != null) {
        harmony.Patch(chemSend, prefix: new HarmonyMethod(typeof(ChemistryStationPatches), nameof(ChemistryStationPatches.Prefix_SendCookOperation)));
        LoggingHelper.Msg("Patched ChemistryStation.SendCookOperation");
      } else {
        LoggingHelper.Msg("ChemistryStation SendCookOperation fallback method not found (skipping)");
      }

      var chemSet = FindMethodWithFallback(chemType, "SetCookOperation", "ApplyCookOperation", "ReceiveCookOperation");
      if (chemSet != null) {
        harmony.Patch(chemSet, prefix: new HarmonyMethod(typeof(ChemistryStationPatches), nameof(ChemistryStationPatches.Prefix_SetCookOperation)));
        LoggingHelper.Msg("Patched ChemistryStation.SetCookOperation");
      } else {
        LoggingHelper.Msg("ChemistryStation SetCookOperation fallback method not found (skipping)");
      }

      var chemFinalize = FindMethodWithFallback(chemType, "FinalizeOperation", "FinishOperation");
      if (chemFinalize != null) {
        harmony.Patch(chemFinalize, prefix: new HarmonyMethod(typeof(ChemistryStationPatches), nameof(ChemistryStationPatches.Prefix_FinalizeOperation)));
        LoggingHelper.Msg("Patched ChemistryStation.FinalizeOperation");
      } else {
        LoggingHelper.Msg("ChemistryStation FinalizeOperation method not found (skipping)");
      }
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
      var labType = typeof(Il2CppScheduleOne.ObjectScripts.LabOven);
      var labSend = FindMethodWithFallback(labType, "SendCookOperation", "TransmitCookOperation", "NetworkCookOperation");
      if (labSend != null) {
        harmony.Patch(labSend, prefix: new HarmonyMethod(typeof(LabOvenPatches), nameof(LabOvenPatches.Prefix_SendCookOperation)));
        LoggingHelper.Msg("Patched LabOven.SendCookOperation");
      } else {
        LoggingHelper.Msg("LabOven SendCookOperation fallback method not found (skipping)");
      }

      var labSet = FindMethodWithFallback(labType, "SetCookOperation", "ApplyCookOperation", "ReceiveCookOperation");
      if (labSet != null) {
        harmony.Patch(labSet, prefix: new HarmonyMethod(typeof(LabOvenPatches), nameof(LabOvenPatches.Prefix_SetCookOperation)));
        LoggingHelper.Msg("Patched LabOven.SetCookOperation");
      } else {
        LoggingHelper.Msg("LabOven SetCookOperation fallback method not found (skipping)");
      }
    }


  }

  // Keep _lastPrefs in sync when MelonLoader saves preferences.
  // Detailed logging and diffing is handled by PreferencesMapper / ApplyPreferencesNow.
  public override void OnPreferencesSaved() {
    _lastPrefs = PreferencesMapper.ReadFromPreferences();
  }

  private static readonly System.Reflection.BindingFlags FullMethodFlags =
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
      System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

  private static MethodBase? FindMethodWithFallback(Type type, params string[] methodNames) {
    // Prefer exact name matches; these are stable across builds when available.
    foreach (var name in methodNames) {
      var m = type.GetMethod(name, FullMethodFlags);
      if (m != null) {
        LoggingHelper.Msg($"Found method '{name}' on '{type.Name}' for patching.");
        return m;
      }
    }

    // Next, try an approximate (contains) match to handle renamed methods, without
    // depending on AccessTools warnings for absent methods.
    var candidate = type.GetMethods(FullMethodFlags)
      .FirstOrDefault(m => methodNames.Any(pattern =>
        m.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
        m.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0));

    if (candidate != null) {
      LoggingHelper.Msg($"Found method '{candidate.Name}' on '{type.Name}' as method name pattern match.");
      return candidate;
    }

    return null;
  }

  private static MethodBase? FindMethodByPattern(Type type, string[] methodNames, string friendlyName) {
    var method = FindMethodWithFallback(type, methodNames);
    if (method != null)
      return method;

    // Manually map methods that may be renamed or have version-specific suffixes.
    method = type.GetMethods(FullMethodFlags)
      .FirstOrDefault(m => methodNames.Any(pattern => 
        m.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
        m.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0));

    if (method != null) {
      LoggingHelper.Msg($"Patched {friendlyName} via approximated method '{method.Name}' on '{type.Name}'.");
      return method;
    }

    return null;
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

    if (addEnumKeys && S1ApiCompat.IsAvailable) {
      changed |= TryAddOrPruneEnumKeys(cfg);
    }

    return changed;
  }

  private static bool TryAddOrPruneEnumKeys(ModConfig cfg) {
    bool changed = false;
    HashSet<string> presentNames;
    try {
      var defs = S1API.Items.ItemManager.GetAllItemDefinitions();
      if (defs == null || defs.Count == 0)
        return ScheduleCategoryMultiplierUpdate();

      presentNames = defs
        .Select(d => BetterStacksFMod.NormalizeCategoryKey(d.Category.ToString()))
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
    var toRemove = cfg.CategoryMultipliers.Keys.Where(k => !presentNames.Contains(BetterStacksFMod.NormalizeCategoryKey(k))).ToList();
    foreach (var k in toRemove) {
      cfg.CategoryMultipliers.Remove(k);
      changed = true;
      LoggingHelper.Warning($"Removing obsolete CategoryMultiplier '{k}' (not present in game data)");
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

  private static bool ScheduleCategoryMultiplierUpdate() {
    // Scheduling is purely about ensuring we run an update once the game
    // item definitions are available.  The handler will always use the
    // current configuration, so there's no need to accept or retain an
    // external ModConfig reference.
    if (!_ensureCatMultScheduled) {
      _ensureCatMultScheduled = true;
      S1ApiCompat.TrySubscribeToGameLifecycleEvent("OnPreLoad", DeferredCategoryMultiplierUpdate);
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
      S1ApiCompat.TryUnsubscribeFromGameLifecycleEvent("OnPreLoad", DeferredCategoryMultiplierUpdate);
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
    // attempt Steam adapter initialization when the library becomes ready
    if (_steamAdapter != null && !_steamAdapter.IsInitialized) {
      bool steamReady = false;
      try {
        steamReady = SteamNetworkLib.Utilities.SteamNetworkUtils.IsSteamInitialized();
      }
      catch (Exception ex) {
        LoggingHelper.Warning($"Steam readiness check failed: {ex.Message}");
        steamReady = false;
      }

      if (steamReady) {
        LoggingHelper.Msg("Steam ready, trying adapter init");
        _steamAdapter.Initialize();
        if (_steamAdapter.IsInitialized) {
          LoggingHelper.Init("SteamNetworkAdapter initialized.");
        } else {
          // initialization failed; don't give up yet.
          LoggingHelper.Msg("Steam adapter init failed, will retry");
        }
      } else {
        // Steam not running yet or library missing
      }
    }

    // Drive incoming SteamNetworkLib callbacks / message processing.
    try { NetworkingManager.CurrentAdapter?.ProcessIncomingMessages(); } catch { }

    // forward pending config processing to manager
    ConfigManager.ProcessPendingUpdates();
  }

}
