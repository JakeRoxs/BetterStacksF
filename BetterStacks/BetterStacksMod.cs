using BetterStacks;
using BetterStacks.Networking;
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
    private static ModConfig _config = new ModConfig();
    // Pending host config updates queued from other threads (watcher, network callbacks).
    private static readonly Queue<ModConfig> _pendingConfigs = new Queue<ModConfig>();
    private static readonly object _pendingLock = new object();

    // Store original stack limits so re-applying config uses the original base value (pre-modification).
    private static Dictionary<string, int> _originalStackLimits = new Dictionary<string, int>();

    // True after the first ApplyStackOverridesUsing run (base/originals capture completed).
    private static bool _originalsCaptured = false;

    // Remember the multiplier we used for each category when we last modified stacks.  This lets us
    // adjust "new" definitions that appear after the original capture when the config has changed
    // (e.g. recipe-generated products).  We store only the integer multiplier, not the whole config.
    private static Dictionary<EItemCategory, int> _lastCategoryModifiers = new Dictionary<EItemCategory, int>();

    // Remove control characters (especially null) from strings before passing them to MelonLogger.
    // Some Schedule I definitions (e.g. "Grand Fizz") embed trailing '\0' characters which end up
    // as garbage lines in the log file; sanitizing here keeps the diagnostics clean.
    private static string SanitizeForLog(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        var builder = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c >= 0x20) // drop control codes (including '\0')
                builder.Append(c);
        }
        return builder.ToString();
    }

    // Reflection helpers for reading/writing StackLimit values on definitions.
    /// <summary>
    /// Attempts to read the <c>StackLimit</c> value from an item definition instance using reflection.
    /// </summary>
    /// <param name="def">
    /// The item definition object whose <c>StackLimit</c> property or field should be inspected.
    /// </param>
    /// <param name="currentStack">
    /// When this method returns <see langword="true"/>, contains the current stack limit value read from
    /// the definition. When the method returns <see langword="false"/>, this value is set to <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a readable <c>StackLimit</c> property or field was found and converted
    /// successfully; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Any reflection or conversion errors are caught internally; the method does not throw in these cases
    /// and instead returns <see langword="false"/>.
    /// </remarks>
    private static bool TryGetStackLimit(object def, out int currentStack)
    {
        currentStack = 0;
        var defType = def.GetType();
        var prop = defType.GetProperty("StackLimit");
        var field = prop == null ? defType.GetField("StackLimit") : null;
        if ((prop == null || !prop.CanRead) && field == null)
            return false;
        try
        {
            if (prop != null)
                currentStack = Convert.ToInt32(prop.GetValue(def));
            else if (field != null)
                currentStack = Convert.ToInt32(field.GetValue(def));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetStackLimit(object def, int value)
    {
        var defType = def.GetType();
        var prop = defType.GetProperty("StackLimit");
        var field = prop == null ? defType.GetField("StackLimit") : null;
        try
        {
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(def, value);
                return true;
            }
            else if (field != null)
            {
                field.SetValue(def, value);
                return true;
            }
        }
        catch { }
        return false;
    }

    // Compute multiplier for a category from the supplied config (falling back to the
    // globally loaded config and logging missing defaults).
    private static int GetModifierForCategory(ModConfig? cfg, EItemCategory category)
    {
        if (cfg?.CategoryMultipliers != null &&
            cfg.CategoryMultipliers.TryGetValue(category.ToString(), out var m))
            return Math.Max(1, m);

        if (_config?.CategoryMultipliers != null &&
            _config.CategoryMultipliers.TryGetValue(category.ToString(), out var m2))
            return Math.Max(1, m2);

        // warn if one of the baked-in defaults is missing
        switch (category)
        {
            case EItemCategory.Product:
            case EItemCategory.Packaging:
            case EItemCategory.Agriculture:
            case EItemCategory.Ingredient:
                MelonLogger.Warning($"Expected default multiplier for category '{category}' not found in config/game defs.");
                break;
        }

        return 1;
    }

    // Indicates whether the current instance is being constrained by a host/server-authoritative config.
    public static bool ServerAuthoritative { get; internal set; } = false;

    // Convenience: whether the loaded mod config requests server-authoritative behavior.
    public static bool ServerAuthoritativeEnabled => _config?.EnableServerAuthoritativeConfig ?? false;

    // Expose current config for helpers/patches.
    public static ModConfig CurrentConfig => _config;

    public override void OnInitializeMelon()
    {
        _config = LoadConfig();

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
                MelonLogger.Msg("Steam adapter initialization deferred — will retry while running.");
#endif
            }
            else
            {
                MelonLogger.Msg("Steam adapter not available — falling back to local adapter.");
                NetworkingManager.Initialize(new LocalNetworkAdapter());
            }
        }
        else
        {
            MelonLogger.Msg("SteamNetworkAdapter initialized.");
        }

        // Log the loaded configuration so we can verify which category multipliers are active at runtime.
        MelonLogger.Msg($"Loaded config: {JsonConvert.SerializeObject(_config)}");

        // If we're the session host, immediately broadcast the authoritative HostConfig so clients apply the same settings.
        if (NetworkingManager.CurrentAdapter?.IsHost ?? false)
        {
            NetworkingManager.BroadcastHostConfig(new HostConfig { Config = _config });
            MelonLogger.Msg("Broadcasted HostConfig (host).");
        }

        var harmony = new HarmonyLib.Harmony("com.jakeroxs.betterstacks");
        FileLog.LogWriter = new StreamWriter("harmony.log") { AutoFlush = true };

        // Use S1API lifecycle to apply stack overrides once at load time
        GameLifecycle.OnPreLoad += ApplyStackOverrides;

        // enforce StackLimit when quantities change (handles runtime config updates)
        harmony.Patch(
            AccessTools.Method(typeof(ItemInstance), "ChangeQuantity"),
            prefix: new HarmonyMethod(typeof(BetterStacksMod), nameof(ChangeQuantityPatch))
        );
        harmony.Patch(
            AccessTools.Method(typeof(ItemInstance), "SetQuantity"),
            prefix: new HarmonyMethod(typeof(BetterStacksMod), nameof(SetQuantityPatch))
        );



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
        try
        {
            PreferencesMapper.EnsureRegistered();
            var cfg = PreferencesMapper.ReadFromPreferences();
            EnsureCategoryMultipliers(cfg, addEnumKeys: true);
            return cfg;
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Failed to read MelonPreferences, falling back to defaults: {ex.Message}");
            var cfg = new ModConfig();
            EnsureCategoryMultipliers(cfg, addEnumKeys: true);
            return cfg;
        }
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
                        MelonLogger.Msg("Item definitions not ready; scheduled category-multiplier update for OnPreLoad.");
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
                MelonLogger.Warning($"Removing obsolete CategoryMultiplier '{k}' (not present in game data)");
            }
        }

        return changed;
    }

    // Apply stack overrides using the global mod config file (registered on OnPreLoad).
    // This is the method subscribed to GameLifecycle.OnPreLoad.
    private static void ApplyStackOverrides()
    {
        ApplyStackOverridesUsing(_config);
    }

    // Public helper so other systems (network) can apply a supplied ModConfig.
    public static void ApplyStackOverridesUsing(ModConfig cfg)
    {
        try
        {
            // Defensive: ensure cfg is non-null for downstream logic.
            if (cfg == null) cfg = new ModConfig();

                // If item definitions are not yet available (startup / main menu), defer work until they are loaded by the game.
                var defsCheck = S1API.Items.ItemManager.GetAllItemDefinitions();
                if (defsCheck == null || defsCheck.Count == 0)
                {
                    MelonLogger.Msg("Item definitions not ready yet — deferring ApplyStackOverrides.");
                    return;
                }

            // Diagnostic: enumerate *all* item definitions (not only storable) to detect categories/types present at this lifecycle point.
            var allDefs = S1API.Items.ItemManager.GetAllItemDefinitions().ToList();

#if DEBUG
            var categoryCounts = allDefs.GroupBy(d => (EItemCategory)d.Category).ToDictionary(g => g.Key, g => g.Count());
            MelonLogger.Msg($"ItemManager total definitions={allDefs.Count}, categories present={string.Join(", ", categoryCounts.Select(kv => kv.Key + "=" + kv.Value))}");

            // Log if any baked-in default multiplier keys are missing from the game's categories (helpful when enum names change).
            var presentNames = categoryCounts.Keys.Select(k => k.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var bakedDefaults = new[] { "Product", "Packaging", "Agriculture", "Ingredient" };
            var missingDefaults = bakedDefaults.Where(d => !presentNames.Contains(d)).ToList();
            if (missingDefaults.Count > 0)
            {
                MelonLogger.Warning($"Default CategoryMultiplier keys not found in game item categories: {string.Join(", ", missingDefaults)}");
            }

            // Choose diagnostic categories from the (now-sanitized) config keys; fall back to a small sensible set.
            var diagnosticNames = cfg?.CategoryMultipliers?.Keys?.ToList() ?? new List<string>();
            var diagnosticCategories = new List<EItemCategory>();
            foreach (var name in diagnosticNames)
                if (Enum.TryParse<EItemCategory>(name, out var parsed))
                    diagnosticCategories.Add(parsed);
            if (diagnosticCategories.Count == 0)
                diagnosticCategories = new List<EItemCategory> { EItemCategory.Product, EItemCategory.Packaging, EItemCategory.Cash };

            foreach (var cat in diagnosticCategories)
            {
                var matches = allDefs.Where(d => (EItemCategory)d.Category == cat).ToList();
               if (matches.Count == 0)
                    MelonLogger.Msg($"Diagnostic: no definitions found with category {cat}");
                else
                {
                    var types = matches.Select(m => m.GetType().Name).Distinct();
                    MelonLogger.Msg($"Diagnostic: {matches.Count} defs for {cat}, types=[{string.Join(',', types)}], examples=[{string.Join(',', matches.Take(8).Select(m => m.Name + "(" + m.GetType().Name + ")"))}]");
                }
            }
#endif

            // Select any item definition that exposes a StackLimit (property or field) so ProductDefinition
            // and other non-storable types that still have StackLimit are processed.
            var defs = allDefs.Where(d => d.GetType().GetProperty("StackLimit") != null || d.GetType().GetField("StackLimit") != null).ToList();
            MelonLogger.Msg($"Found {defs.Count} item definitions with StackLimit at ApplyStackOverridesUsing");

            var processedByCategory = new Dictionary<EItemCategory, int>();
            var changedByCategory = new Dictionary<EItemCategory, int>();
            var changedNamesByCategory = new Dictionary<EItemCategory, List<string>>();

            // Track whether we persisted any original base values during this run (only set on first capture).
            bool capturedThisRun = false;

            // Record the modifiers we compute per category during this pass.  We'll store it globally
            // at the end of the method so future passes know what multiplier was in effect previously.
            var currentModifiers = new Dictionary<EItemCategory, int>();

            foreach (var def in defs)
            {
                var category = (EItemCategory)def.Category;
                processedByCategory.TryGetValue(category, out var pCount);
                processedByCategory[category] = pCount + 1;

                var defType = def.GetType();

                // Skip 'effect' style Product entries (these represent effect metadata, not stackable items).
                if ((def.Name != null && def.Name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0)
                    || defType.Name.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) >= 0)
                {
#if DEBUG
                    MelonLogger.Msg($"Skipping non-stackable effect definition: {SanitizeForLog(def.Name)} ({defType.Name})");
#endif
                    continue;
                }

                if (!TryGetStackLimit(def, out int currentStack))
                {
                    MelonLogger.Msg($"Skipping {SanitizeForLog(def.Name)} ({defType.Name}) — unable to read StackLimit");
                    continue;
                }

                // capture/restore original base value if needed
                if (!_originalStackLimits.ContainsKey(def.ID))
                {
                    var saved = PreferencesMapper.GetSavedOriginalStackLimit(def.ID);
                    if (saved.HasValue)
                    {
                        _originalStackLimits[def.ID] = saved.Value;
                    }
                    else if (!_originalsCaptured)
                    {
                        _originalStackLimits[def.ID] = currentStack;
                        PreferencesMapper.PersistOriginalStackLimit(def.ID, currentStack);
                        capturedThisRun = true;
                    }
                    else
                    {
                        int prevMod = 1;
                        if (_lastCategoryModifiers.TryGetValue(category, out var pm))
                            prevMod = pm;

                        currentModifiers.TryGetValue(category, out var currentMod);
                        if (currentMod < 1) currentMod = 1;

                        if (prevMod != currentMod && prevMod > 0)
                        {
                            int adjusted = Math.Max(1, (int)Math.Round(currentStack * ((double)currentMod / prevMod)));
#if DEBUG
                            MelonLogger.Msg($"Adjusted newly-created {SanitizeForLog(def.Name)} ({category}) stack limit from {currentStack} to {adjusted} " +
                                            $"(oldMod={prevMod}, newMod={currentMod})");
#endif
                            TrySetStackLimit(def, adjusted);
                            currentStack = adjusted;
                        }

                        int orig = Math.Max(1, currentStack / currentMod);
                        _originalStackLimits[def.ID] = orig;
                        PreferencesMapper.PersistOriginalStackLimit(def.ID, orig);
                        capturedThisRun = true;
#if DEBUG
                        MelonLogger.Msg($"Recorded original StackLimit for new definition '{SanitizeForLog(def.Name)}' (ID={def.ID}) = {orig}");
#endif
                    }
                }

                int originalLimit = _originalStackLimits[def.ID];

                int modifier = GetModifierForCategory(cfg, category);
                if (!currentModifiers.ContainsKey(category))
                    currentModifiers[category] = modifier;

                int newLimit = Math.Max(1, originalLimit * modifier);

                if (newLimit != currentStack)
                {
                    changedByCategory.TryGetValue(category, out var cCount);
                    changedByCategory[category] = cCount + 1;

                    if (!changedNamesByCategory.TryGetValue(category, out var list))
                    {
                        list = new List<string>();
                        changedNamesByCategory[category] = list;
                    }
                    if (list.Count < 10)
                        list.Add($"{SanitizeForLog(def.Name)}(current {currentStack}->{newLimit}, orig {originalLimit})");

#if DEBUG
                    MelonLogger.Msg($"Set {SanitizeForLog(def.Name)} ({def.Category}) stack limit from {currentStack} to {newLimit}");
#endif

                    if (!TrySetStackLimit(def, newLimit))
                        MelonLogger.Msg($"Cannot set StackLimit on {SanitizeForLog(def.Name)} ({defType.Name}) — member is read-only.");
                }
            }

            // Summary logging so we can see which categories were present/changed at OnPreLoad.
            int totalChanged = changedByCategory.Values.Sum();
            if (totalChanged > 0)
            {
                MelonLogger.Msg("ApplyStackOverrides summary:");
                foreach (var kv in processedByCategory.OrderBy(k => k.Key.ToString()))
                {
                    changedByCategory.TryGetValue(kv.Key, out var changed);
                    changedNamesByCategory.TryGetValue(kv.Key, out var samples);
                    string sampleStr = samples is null || samples.Count == 0 ? "" : string.Join(", ", samples);
                    MelonLogger.Msg($"  {kv.Key}: processed={kv.Value}, changed={changed}, examples=[{sampleStr}]");
                }
            }
            else
            {
                MelonLogger.Msg("ApplyStackOverrides: no stack limits needed (config already applied)");
            }

            // Update our record of which multiplier we applied for each category.  This
            // allows later calls to adjust definitions that appear after the first capture
            // by comparing the previous multiplier to the current one.
            _lastCategoryModifiers = currentModifiers.ToDictionary(kv => kv.Key, kv => kv.Value);

            // Mark that we've captured originals for this save session so future ApplyStackOverridesUsing calls
            // will not persist or modify definitions that appear later (avoids compounding/new-created defs).
            if (capturedThisRun || PreferencesMapper.HasSavedOriginals())
                _originalsCaptured = true;

        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ApplyStackOverridesUsing failed: {ex}");
        }
    }

    // Update internal config from a host-provided authoritative config and re-apply overrides.
    // Public entry point for external systems (preferences watcher, network) to request
    // a configuration update. The call may originate on any thread; the actual application
    // of the config is deferred to OnUpdate, which runs on the main Unity thread.
    public static void EnqueueConfigUpdate(ModConfig cfg)
    {
        if (cfg == null) return;
        lock (_pendingLock)
        {
            // if cfg matches currently applied config, no need to enqueue
            if (_config != null && PreferencesMapper.AreConfigsEqual(_config, cfg))
                return;
            // avoid queuing the same value twice in a row
            if (_pendingConfigs.Count > 0)
            {
                var last = _pendingConfigs.Last();
                if (PreferencesMapper.AreConfigsEqual(last, cfg))
                    return;
            }
            // store a copy to avoid shared mutable state
            _pendingConfigs.Enqueue(cfg);
        }
    }

    // Internal helper invoked on main thread to apply a config immediately.
    private static void UpdateConfigFromHost(ModConfig cfg)
    {
        if (cfg == null) return;
        _config = cfg;
        ApplyStackOverridesUsing(cfg);
    }

    public override void OnUpdate()
    {
        // Drive incoming SteamNetworkLib callbacks / message processing.
        try { NetworkingManager.CurrentAdapter?.ProcessIncomingMessages(); } catch { }

        // Process any pending config updates queued from other threads.
        while (true)
        {
            ModConfig? next = null;
            lock (_pendingLock)
            {
                if (_pendingConfigs.Count > 0)
                    next = _pendingConfigs.Dequeue();
            }
            if (next == null) break;
            try { UpdateConfigFromHost(next); } catch { }
        }
    }



    // prefix patch used when the mod needs to enforce StackLimit on direct set operations
    private static bool SetQuantityPatch(ItemInstance __instance, int quantity)
    {
        if (quantity < 0)
        {
            MelonLogger.Error("SetQuantity called with negative quantity");
            return false;
        }
        // respect the current limit
        int limit = __instance.StackLimit;
        if (quantity > limit)
            quantity = limit;
        __instance.Quantity = quantity;
        __instance.InvokeDataChange();
        return false;
    }

    private static bool ChangeQuantityPatch(ItemInstance __instance, int change)
    {
        // original game code guards against negative quantities; preserve that check and then
        // enforce the active StackLimit for any additions.
        int num = __instance.Quantity + change;
        if (num < 0)
        {
            MelonLogger.Error("ChangeQuantity resulted in negative quantity");
            return false;
        }

        if (change > 0)
        {
            int limit = __instance.StackLimit;
            if (num > limit)
                num = limit;
        }

        __instance.Quantity = num;
        __instance.InvokeDataChange();
        return false; // skip original
    }



}
