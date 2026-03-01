using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using BetterStacksF.Utilities;

using Il2CppScheduleOne.ItemFramework;

using MelonLoader;

namespace BetterStacksF.Config {
  /// <summary>
  /// Performs the core logic of applying stack limit overrides based on a config.
  /// Maintains session‑scoped data such as original limits and category modifiers.
  /// </summary>
  internal static class StackOverrideManager {
    private static Dictionary<string, int> _originalStackLimits = new Dictionary<string, int>();
    private static bool _originalsCaptured = false;
    private static Dictionary<EItemCategory, int> _lastCategoryModifiers = new Dictionary<EItemCategory, int>();

    // definitions added since last run that still need their original limit
    // persisted; categories are kept here so we can process them even when the
    // modifier is 1 and unchanged.
    private static readonly HashSet<EItemCategory> _categoriesNeedingCapture =
        new HashSet<EItemCategory>();

    // session cache of definitions that have a StackLimit, grouped by category.
    // the underlying list returned by ItemManager is stable after initial load,
    // so we only rebuild the cache when the total definition count changes.
    private static int _cachedDefCount = -1;
    private static Dictionary<EItemCategory, List<S1API.Items.ItemDefinition>>
        _stackablesByCategory = new Dictionary<EItemCategory, List<S1API.Items.ItemDefinition>>();
    private static readonly ConcurrentDictionary<Type, bool> _typeHasStackLimitCache =
        new ConcurrentDictionary<Type, bool>();

    /// <summary>
    /// Hooked at <see cref="S1API.Lifecycle.GameLifecycle.OnPreLoad"/> by the mod initializer.
    /// </summary>
    public static void ApplyStackOverrides(ModConfig cfg) {
      // simplified stub for debugging syntax issues
      try {
        cfg ??= new ModConfig();

        var defsCheck = S1API.Items.ItemManager.GetAllItemDefinitions();
        if (defsCheck == null || defsCheck.Count == 0) {
          LoggingHelper.Msg("Item definitions not ready yet — deferring ApplyStackOverrides.");
          return;
        }

        var allDefs = defsCheck.ToList();

        if (LoggingHelper.EnableVerbose) {
          LogDebugDiagnostics(allDefs, cfg);
        }

        EnsureStackableCache(allDefs);
        int totalStackables = _stackablesByCategory.Values.Sum(list => list.Count);
        LoggingHelper.Msg($"Found {totalStackables} item definitions with StackLimit (cached) at ApplyStackOverrides");


        var processedByCategory = new Dictionary<EItemCategory, int>();
        var changedByCategory = new Dictionary<EItemCategory, int>();
        var changedNamesByCategory = new Dictionary<EItemCategory, List<string>>();

        bool capturedThisRun = false;
        var currentModifiers = new Dictionary<EItemCategory, int>();
        var capturedCategories = new HashSet<EItemCategory>();

        // determine which categories we actually need to touch.  Categories make
        // the cut if any of the following are true:
        //   * modifier != 1
        //   * previous modifier != current modifier (handles revert-to-1 case)
        //   * there are unrecorded definitions in the category (new data)
        var categoriesToProcess = new List<KeyValuePair<EItemCategory, List<S1API.Items.ItemDefinition>>>();
        foreach (var kv in _stackablesByCategory) {
          var category = kv.Key;
          int modifier = BetterStacksFMod.GetModifierForCategory(cfg, category);
          bool prevWasNonOne = _lastCategoryModifiers.TryGetValue(category, out var prevMod) && prevMod != 1;
          bool needCapture = _categoriesNeedingCapture.Contains(category);

          if (modifier != 1 || prevWasNonOne || needCapture)
            categoriesToProcess.Add(kv);
        }

        // iterate only the categories we flagged earlier; when a full pass isn't
        // required we avoid the overhead of examining every definition.
        foreach (var kv in categoriesToProcess) {
          var category = kv.Key;
          int modifier = BetterStacksFMod.GetModifierForCategory(cfg, category); // recalculated for clarity

          foreach (var def in kv.Value) {
            processedByCategory.TryGetValue(category, out var pCount);
            processedByCategory[category] = pCount + 1;

            var defType = def.GetType();

            if ((def.Name != null && def.Name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0)
                || defType.Name.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) >= 0) {
              LoggingHelper.Msg($"Skipping non-stackable effect definition: {def.Name} ({defType.Name})");
              continue;
            }

            if (!ReflectionHelper.TryGetStackLimit(def, out int currentStack)) {
              LoggingHelper.Warning($"Skipping {def.Name} ({defType.Name}) — unable to read StackLimit");
              continue;
            }

            if (!_originalStackLimits.ContainsKey(def.ID)) {
              // we encountered a new definition; capture its original limit and
              // mark the category so we can prune it later.
              var saved = PreferencesMapper.GetSavedOriginalStackLimit(def.ID);
              if (saved.HasValue) {
                _originalStackLimits[def.ID] = saved.Value;
              }
              else if (!_originalsCaptured) {
                _originalStackLimits[def.ID] = currentStack;
                PreferencesMapper.PersistOriginalStackLimit(def.ID, currentStack);
                capturedThisRun = true;
                capturedCategories.Add(category);
              }
              else {
                int prevCatMod = 1;
                if (_lastCategoryModifiers.TryGetValue(category, out var previousModifier))
                  prevCatMod = previousModifier;

                currentModifiers.TryGetValue(category, out var currentMod);
                if (currentMod < 1) currentMod = 1;

                if (prevCatMod != currentMod && prevCatMod > 0) {
                  int adjusted = Math.Max(1, (int)Math.Round(currentStack * ((double)currentMod / prevCatMod)));
                  LoggingHelper.Msg($"Adjusted newly-created {def.Name} ({category}) stack limit from {currentStack} to {adjusted} " +
                                  $"(oldMod={prevCatMod}, newMod={currentMod})");
                  ReflectionHelper.TrySetStackLimit(def, adjusted);
                  currentStack = adjusted;
                }

                int orig = Math.Max(1, currentStack / currentMod);
                _originalStackLimits[def.ID] = orig;
                PreferencesMapper.PersistOriginalStackLimit(def.ID, orig);
                capturedThisRun = true;
                capturedCategories.Add(category);
                LoggingHelper.Msg($"Recorded original StackLimit for new definition '{def.Name}' (ID={def.ID}) = {orig}");
              }
            }

            int originalLimit = _originalStackLimits[def.ID];

            int newLimit;
            {
              // reuse outer "modifier" calculated above
              int mod = modifier;
              if (!currentModifiers.ContainsKey(category))
                currentModifiers[category] = mod;
              newLimit = Math.Max(1, originalLimit * mod);
            }

            if (newLimit != currentStack) {
              changedByCategory.TryGetValue(category, out var cCount);
              changedByCategory[category] = cCount + 1;

              if (!changedNamesByCategory.TryGetValue(category, out var list)) {
                list = new List<string>();
                changedNamesByCategory[category] = list;
              }
              if (list.Count < 10)
                list.Add($"{def.Name}(current {currentStack}->{newLimit}, orig {originalLimit})");

              LoggingHelper.Msg($"Set {def.Name} ({def.Category}) stack limit from {currentStack} to {newLimit}");

              if (!ReflectionHelper.TrySetStackLimit(def, newLimit))
                LoggingHelper.Warning($"Cannot set StackLimit on {def.Name} ({defType.Name}) — member is read-only.");
            }
          }
        }

        // remove categories that no longer contain unrecorded definitions so we
        // won't waste work checking them again.
        foreach (var cat in capturedCategories) {
          _categoriesNeedingCapture.Remove(cat);
        }

        int totalChanged = changedByCategory.Values.Sum();
        if (totalChanged > 0) {
          LoggingHelper.Msg("ApplyStackOverrides summary:");
          foreach (var kv in processedByCategory.OrderBy(k => k.Key.ToString())) {
            changedByCategory.TryGetValue(kv.Key, out var changed);
            changedNamesByCategory.TryGetValue(kv.Key, out var samples);
            string sampleStr = samples is null || samples.Count == 0 ? "" : string.Join(", ", samples);
            LoggingHelper.Msg($"  {kv.Key}: processed={kv.Value}, changed={changed}, examples=[{sampleStr}]");
          }
        }
        else {
          LoggingHelper.Msg("ApplyStackOverrides: no stack limits needed (config already applied)");
        }

        _lastCategoryModifiers = currentModifiers.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (capturedThisRun || PreferencesMapper.HasSavedOriginals())
          _originalsCaptured = true;
      }
      catch (Exception ex) {
        LoggingHelper.Error("ApplyStackOverrides failed", ex);
      }
    }
    private static bool TypeHasStackLimit(Type t) {
      // ConcurrentDictionary handles the thread‑safe check/insert for us.  The
      // factory delegate will only be invoked once per key even if multiple
      // threads race to populate it.
      return _typeHasStackLimitCache.GetOrAdd(t, ReflectionHelper.HasStackLimit);
    }

    private static void EnsureStackableCache(List<S1API.Items.ItemDefinition> allDefs) {
      if (allDefs.Count == _cachedDefCount)
        return;

      // if the caller has added a bunch of new defs of types we've seen
      // before, the type cache avoids repeated reflection; if new types appear
      // we'll populate the cache as we go.
      _stackablesByCategory = allDefs
          .Where(d => TypeHasStackLimit(d.GetType()))
          .GroupBy(d => (EItemCategory)d.Category)
          .ToDictionary(g => g.Key, g => g.ToList());

      _cachedDefCount = allDefs.Count;

      // After rebuilding the cache identify any categories that contain
      // definitions we haven't yet recorded original stack limits for.  We
      // capture the category rather than the individual definition because the
      // cost of iterating the list once here is cheaper than having to probe the
      // data later on every ApplyStackOverrides call just to satisfy the early
      // skipping logic.
      _categoriesNeedingCapture.Clear();
      foreach (var kv in _stackablesByCategory) {
        foreach (var def in kv.Value) {
          if (!_originalStackLimits.ContainsKey(def.ID)) {
            _categoriesNeedingCapture.Add(kv.Key);
            break;
          }
        }
      }
    }

    private static void LogDebugDiagnostics(List<S1API.Items.ItemDefinition> allDefs, ModConfig cfg) {
      if (!LoggingHelper.EnableVerbose) return;
      var categoryCounts = allDefs.GroupBy(d => (EItemCategory)d.Category).ToDictionary(g => g.Key, g => g.Count());
      LoggingHelper.Msg($"ItemManager total definitions={allDefs.Count}, categories present={string.Join(", ", categoryCounts.Select(kv => kv.Key + "=" + kv.Value))}");
      var presentNames = categoryCounts.Keys.Select(k => k.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
      var bakedDefaults = new[] { "Product", "Packaging", "Agriculture", "Ingredient" };
      var missingDefaults = bakedDefaults.Where(d => !presentNames.Contains(d)).ToList();
      if (missingDefaults.Count > 0) {
        LoggingHelper.Warning($"Default CategoryMultiplier keys not found in game item categories: {string.Join(", ", missingDefaults)}");
      }

      var diagnosticNames = cfg?.CategoryMultipliers?.Keys?.ToList() ?? new List<string>();
      var diagnosticCategories = new List<EItemCategory>();
      foreach (var name in diagnosticNames)
        if (Enum.TryParse<EItemCategory>(name, out var parsed))
          diagnosticCategories.Add(parsed);
      if (diagnosticCategories.Count == 0)
        diagnosticCategories = new List<EItemCategory> { EItemCategory.Product, EItemCategory.Packaging, EItemCategory.Cash };

      foreach (var cat in diagnosticCategories) {
        var matches = allDefs.Where(d => (EItemCategory)d.Category == cat).ToList();
        if (matches.Count == 0)
          LoggingHelper.Msg($"Diagnostic: no definitions found with category {cat}");
        else {
          var types = matches.Select(m => m.GetType().Name).Distinct();
          LoggingHelper.Msg($"Diagnostic: {matches.Count} defs for {cat}, types=[{string.Join(',', types)}], examples=[{string.Join(',', matches.Take(8).Select(m => m.Name + "(" + m.GetType().Name + ")"))}]");
        }
      }
    }

  }
}
