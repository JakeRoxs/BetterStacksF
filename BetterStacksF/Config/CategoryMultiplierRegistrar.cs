using System;
using System.Collections.Generic;
using System.Linq;

using BetterStacksF.Utilities;

using Il2CppScheduleOne.ItemFramework;

using MelonLoader;

using S1API.Lifecycle;

namespace BetterStacksF.Config {
  /// <summary>
  /// Helper responsible for synchronizing the CategoryMultipliers dictionary with
  /// the game definitions. Adds missing categories and prunes obsolete ones by
  /// writing values into the mod's MelonPreferences entries.
  /// </summary>
  internal static class CategoryMultiplierRegistrar {
    private static bool _registrationScheduled = false;
    private static bool _registering = false;

    public static void RegisterCategoryMultipliersFromGameDefs() {
      var present = GetPresentCategoryNames();
      if (present.Count == 0) {
        if (!_registrationScheduled) {
          _registrationScheduled = true;
          GameLifecycle.OnPreLoad += () => {
            try { RegisterCategoryMultipliersFromGameDefs(); } catch { }
          };
          // we deliberately do not log here; the general "item definitions not
          // ready" message from the stack override manager is sufficient and
          // avoids spamming the log when verbose logging is enabled.
        }
        return;
      }

      if (_registering) return;
      _registering = true;
      try {
        PreferencesMapper.EnsureRegistered();

        // we need a copy of the hard‑coded defaults so we can look up the
        // built-in multiplier for any categories that don't yet exist in the
        // user's config.  the defaults object is never modified.
        var defaults = new ModConfig();

        // prefer an already-loaded configuration to avoid touching the file
        // multiple times during startup.  EnsureRegistered guarantees that
        // _lastAppliedPrefs is populated, so GetCachedPreferences() will almost
        // always return a value.  We still fall back to ReadFromPreferences in
        // the unlikely case the cache is empty.
        var cfg = PreferencesMapper.GetCachedPreferences() ??
                  PreferencesMapper.ReadFromPreferences() ??
                  new ModConfig();
        if (cfg.CategoryMultipliers == null)
          cfg.CategoryMultipliers = new Dictionary<string, int>();

        // drop any malformed keys that might have snuck in from earlier bugs
        PreferencesMapper.SanitizeCategoryKeys(cfg);

        bool madeChanges = false;

        foreach (var name in present) {
          if (!cfg.CategoryMultipliers.ContainsKey(name)) {
            int dv = 1;
            if (defaults.CategoryMultipliers != null &&
                defaults.CategoryMultipliers.TryGetValue(name, out var dval))
              dv = dval;

            cfg.CategoryMultipliers[name] = dv;
            madeChanges = true;
            // log at verbose level; this is strictly about category bookkeeping
            // and can safely be hidden in normal operation.
            LoggingHelper.Msg($"CategoryMultiplier '{name}' added (default {dv})");
          }
        }

        var toRemove = cfg.CategoryMultipliers.Keys.Where(k => !present.Contains(k)).ToList();
        foreach (var k in toRemove) {
          cfg.CategoryMultipliers.Remove(k);
          madeChanges = true;
          LoggingHelper.Warning($"Dropping obsolete CategoryMultiplier '{k}' (not found in game data)");
        }

        if (madeChanges) {
          try {
            // sanitize one more time in case registration logic added any
            // blank/whitespace keys (shouldn't happen, but be defensive).
            PreferencesMapper.SanitizeCategoryKeys(cfg);
            PreferencesMapper.WriteToPreferences(cfg);
          }
          catch (Exception ex) {
            LoggingHelper.Warning($"Failed to persist CategoryMultipliers to preferences: {ex.Message}");
          }
        }

        PreferencesMapper.UpdateInMemoryCategoryMultipliers(cfg);
      }
      finally {
        _registering = false;
      }
    }

    private static HashSet<string> GetPresentCategoryNames() {
      try {
        var defs = S1API.Items.ItemManager.GetAllItemDefinitions();
        if (defs == null || defs.Count == 0)
          return new HashSet<string>();

        return defs.Select(d => ((EItemCategory)d.Category).ToString()).Distinct().ToHashSet();
      }
      catch {
        return new HashSet<string>();
      }
    }
  }
}
