using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppScheduleOne.ItemFramework;
using S1API.Lifecycle;
using MelonLoader;
using BetterStacks.Utilities;

namespace BetterStacks.Config
{
    /// <summary>
    /// Helper responsible for synchronizing the CategoryMultipliers dictionary with
    /// the game definitions.  Adds missing categories and prunes obsolete ones.
    /// </summary>
    internal static class CategoryMultiplierRegistrar
    {
        private static bool _registrationScheduled = false;
        private static bool _registering = false;

        public static void RegisterCategoryMultipliersFromGameDefs()
        {
            var present = GetPresentCategoryNames();
            if (present.Count == 0)
            {
                if (!_registrationScheduled)
                {
                    _registrationScheduled = true;
                    GameLifecycle.OnPreLoad += () =>
                    {
                        try { RegisterCategoryMultipliersFromGameDefs(); } catch { }
                    };
#if DEBUG
                    LoggingHelper.Msg("Item definitions not ready yet, scheduled category registration for OnPreLoad.");
#endif
                }
                return;
            }

            if (_registering) return;
            _registering = true;
            try
            {
                PreferencesMapper.EnsureRegistered();

                var defaults = new ModConfig();
                var cfg = PreferencesMapper.ReadFromPreferences() ?? new ModConfig();
                if (cfg.CategoryMultipliers == null)
                    cfg.CategoryMultipliers = new Dictionary<string,int>();

                bool madeChanges = false;

                foreach (var name in present)
                {
                    if (!cfg.CategoryMultipliers.ContainsKey(name))
                    {
                        int dv = 1;
                        if (defaults.CategoryMultipliers != null &&
                            defaults.CategoryMultipliers.TryGetValue(name, out var dval))
                            dv = dval;

                        cfg.CategoryMultipliers[name] = dv;
                        madeChanges = true;
                        LoggingHelper.Msg($"Added CategoryMultiplier key for '{name}' with default={dv}");
                    }
                }

                var toRemove = cfg.CategoryMultipliers.Keys.Where(k => !present.Contains(k)).ToList();
                foreach (var k in toRemove)
                {
                    cfg.CategoryMultipliers.Remove(k);
                    madeChanges = true;
                    LoggingHelper.Warning($"Dropping obsolete CategoryMultiplier '{k}' (not found in game data)");
                }

                if (madeChanges)
                {
                    try { PreferencesMapper.WriteToPreferences(cfg); } catch (Exception ex) { LoggingHelper.Warning($"Failed to persist CategoryMultipliers JSON: {ex.Message}"); }
                }

                PreferencesMapper.UpdateInMemoryCategoryMultipliers(cfg);
            }
            finally
            {
                _registering = false;
            }
        }

        private static HashSet<string> GetPresentCategoryNames()
        {
            try
            {
                var defs = S1API.Items.ItemManager.GetAllItemDefinitions();
                if (defs == null || defs.Count == 0)
                    return new HashSet<string>();

                return defs.Select(d => ((EItemCategory)d.Category).ToString()).Distinct().ToHashSet();
            }
            catch
            {
                return new HashSet<string>();
            }
        }
    }
}