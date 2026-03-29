using System;
using System.Collections.Generic;
using System.Linq;

using BetterStacksF.Utilities;

using MelonLoader;


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
          S1ApiCompat.TrySubscribeToGameLifecycleEvent("OnPreLoad", () => {
            try { RegisterCategoryMultipliersFromGameDefs(); } catch { }
          });
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
      if (!S1ApiCompat.IsAvailable) {
        return new HashSet<string>();
      }

      try {
        var defs = S1API.Items.ItemManager.GetAllItemDefinitions();
        if (defs == null || defs.Count == 0)
          return new HashSet<string>();

        bool verbose = LoggingHelper.EnableVerbose;

        // instrument each definition with numeric category ID + string for debugging.
        if (verbose) {
          foreach (var def in defs) {
            try {
              var gameRawValue = GetRawCategoryName(def);
              var numericCategory = GetRawCategoryNumericValue(def);

              var s1apiValue = "<none>";
              try {
                s1apiValue = def.Category.ToString();
              }
              catch {
                s1apiValue = "<s1api unavailable>";
              }
              // Show game-provided raw category via reflection/backing field and
              // compare it to S1API layer output.
              LoggingHelper.Msg($"Item category debug: {def.Name} (game-category='{gameRawValue}', s1api-category='{s1apiValue}', id={numericCategory}, resolved='{BetterStacksFMod.NormalizeCategoryKey(gameRawValue)}')");
            }
            catch (Exception ex) {
              LoggingHelper.Warning($"Failed to log category for item {def.Name}: {ex.Message}");
            }
          }
        }

        var rawCategories = defs
          .Select(d => GetRawCategoryName(d))
          .Where(s => !string.IsNullOrWhiteSpace(s))
          .Distinct()
          .ToList();

        if (verbose)
          LoggingHelper.Msg($"Raw item categories from game defs: {string.Join(", ", rawCategories)}");

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var categoryName in rawCategories) {
          var normalizedName = BetterStacksFMod.NormalizeCategoryKey(categoryName);
          if (!string.IsNullOrWhiteSpace(normalizedName)) {
            present.Add(normalizedName);

            if (verbose && !string.Equals(normalizedName, categoryName, StringComparison.OrdinalIgnoreCase))
              LoggingHelper.Msg($"Mapped unknown raw category '{categoryName}' to '{normalizedName}'.");

            continue;
          }

          if (Enum.TryParse<S1API.Items.ItemCategory>(categoryName, true, out var catValue)) {
            if (Enum.IsDefined(typeof(S1API.Items.ItemCategory), catValue)) {
              present.Add(categoryName);
            } else if (verbose) {
              LoggingHelper.Msg($"Skipping unknown numeric item category from game defs: '{categoryName}'");
            }
          } else if (verbose) {
            LoggingHelper.Msg($"Skipping unknown item category from game defs: '{categoryName}'");
          }
        }

        if (verbose)
          LoggingHelper.Msg($"Valid item categories after enum filtering: {string.Join(", ", present)}");

        return present;
      }
      catch {
        return new HashSet<string>();
      }
    }

    private static string GetRawCategoryName(S1API.Items.ItemDefinition def) {
      if (def == null) return string.Empty;

      try {
        var categoryValue = def.Category; // S1API wrapper property
        var s = categoryValue.ToString();
        if (!string.IsNullOrWhiteSpace(s))
          return s;
      }
      catch {
        // ignore, fallback to reflection
      }

      try {
        var field = def.GetType().GetField("category", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var value = field?.GetValue(def);
        if (value != null)
          return value.ToString() ?? string.Empty;
      }
      catch {
      }

      try {
        var prop = def.GetType().GetProperty("Category", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var value = prop?.GetValue(def);
        if (value != null)
          return value.ToString() ?? string.Empty;
      }
      catch {
      }

      return string.Empty;
    }

    private static int GetRawCategoryNumericValue(S1API.Items.ItemDefinition def) {
      if (def == null) return -1;
      try {
        return Convert.ToInt32(def.Category);
      }
      catch {
      }

      try {
        var field = def.GetType().GetField("category", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var value = field?.GetValue(def);
        if (value != null && int.TryParse(value.ToString(), out var parsed))
          return parsed;
      }
      catch {
      }

      try {
        var prop = def.GetType().GetProperty("Category", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        var value = prop?.GetValue(def);
        if (value != null && int.TryParse(value.ToString(), out var parsed))
          return parsed;
      }
      catch {
      }

      return -1;
    }
  }
}
