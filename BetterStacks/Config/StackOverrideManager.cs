using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppScheduleOne.ItemFramework;
using MelonLoader;
using BetterStacks.Utilities;

namespace BetterStacks.Config
{
    /// <summary>
    /// Performs the core logic of applying stack limit overrides based on a config.
    /// Maintains session‑scoped data such as original limits and category modifiers.
    /// </summary>
    internal static class StackOverrideManager
    {
        private static Dictionary<string, int> _originalStackLimits = new Dictionary<string, int>();
        private static bool _originalsCaptured = false;
        private static Dictionary<EItemCategory, int> _lastCategoryModifiers = new Dictionary<EItemCategory, int>();

        /// <summary>
        /// Hooked at <see cref="S1API.Lifecycle.GameLifecycle.OnPreLoad"/> by the mod initializer.
        /// </summary>
        public static void ApplyStackOverrides(ModConfig cfg)
        {
            try
            {
                cfg ??= new ModConfig();

                var defsCheck = S1API.Items.ItemManager.GetAllItemDefinitions();
                if (defsCheck == null || defsCheck.Count == 0)
                {
                    LoggingHelper.Msg("Item definitions not ready yet — deferring ApplyStackOverrides.");
                    return;
                }

                var allDefs = defsCheck.ToList();

#if DEBUG
                var categoryCounts = allDefs.GroupBy(d => (EItemCategory)d.Category).ToDictionary(g => g.Key, g => g.Count());
                LoggingHelper.Msg($"ItemManager total definitions={allDefs.Count}, categories present={string.Join(", ", categoryCounts.Select(kv => kv.Key + "=" + kv.Value))}");
                var presentNames = categoryCounts.Keys.Select(k => k.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var bakedDefaults = new[] { "Product", "Packaging", "Agriculture", "Ingredient" };
                var missingDefaults = bakedDefaults.Where(d => !presentNames.Contains(d)).ToList();
                if (missingDefaults.Count > 0)
                {
                    LoggingHelper.Warning($"Default CategoryMultiplier keys not found in game item categories: {string.Join(", ", missingDefaults)}");
                }

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
                        LoggingHelper.Msg($"Diagnostic: no definitions found with category {cat}");
                    else
                    {
                        var types = matches.Select(m => m.GetType().Name).Distinct();
                        LoggingHelper.Msg($"Diagnostic: {matches.Count} defs for {cat}, types=[{string.Join(',', types)}], examples=[{string.Join(',', matches.Take(8).Select(m => m.Name + "(" + m.GetType().Name + ")"))}]");
                    }
                }
#endif

                var defs = allDefs.Where(d =>
                    d.GetType().GetProperty("StackLimit") != null ||
                    d.GetType().GetField("StackLimit") != null).ToList();
                LoggingHelper.Msg($"Found {defs.Count} item definitions with StackLimit at ApplyStackOverridesUsing");

                var processedByCategory = new Dictionary<EItemCategory, int>();
                var changedByCategory = new Dictionary<EItemCategory, int>();
                var changedNamesByCategory = new Dictionary<EItemCategory, List<string>>();

                bool capturedThisRun = false;
                var currentModifiers = new Dictionary<EItemCategory, int>();

                foreach (var def in defs)
                {
                    var category = (EItemCategory)def.Category;
                    processedByCategory.TryGetValue(category, out var pCount);
                    processedByCategory[category] = pCount + 1;

                    var defType = def.GetType();

                    if ((def.Name != null && def.Name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0)
                        || defType.Name.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
#if DEBUG
                        LoggingHelper.Msg($"Skipping non-stackable effect definition: {LoggingHelper.SanitizeForLog(def.Name)} ({defType.Name})");
#endif
                        continue;
                    }

                    if (!ReflectionHelper.TryGetStackLimit(def, out int currentStack))
                    {
                        LoggingHelper.Warning($"Skipping {LoggingHelper.SanitizeForLog(def.Name)} ({defType.Name}) — unable to read StackLimit");
                        continue;
                    }

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
                                LoggingHelper.Msg($"Adjusted newly-created {LoggingHelper.SanitizeForLog(def.Name)} ({category}) stack limit from {currentStack} to {adjusted} " +
                                                $"(oldMod={prevMod}, newMod={currentMod})");
#endif
                                ReflectionHelper.TrySetStackLimit(def, adjusted);
                                currentStack = adjusted;
                            }

                            int orig = Math.Max(1, currentStack / currentMod);
                            _originalStackLimits[def.ID] = orig;
                            PreferencesMapper.PersistOriginalStackLimit(def.ID, orig);
                            capturedThisRun = true;
#if DEBUG
                            LoggingHelper.Msg($"Recorded original StackLimit for new definition '{LoggingHelper.SanitizeForLog(def.Name)}' (ID={def.ID}) = {orig}");
#endif
                        }
                    }

                    int originalLimit = _originalStackLimits[def.ID];

                    int modifier = BetterStacksMod.GetModifierForCategory(cfg, category);
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
                            list.Add($"{LoggingHelper.SanitizeForLog(def.Name)}(current {currentStack}->{newLimit}, orig {originalLimit})");

#if DEBUG
                        LoggingHelper.Msg($"Set {LoggingHelper.SanitizeForLog(def.Name)} ({def.Category}) stack limit from {currentStack} to {newLimit}");
#endif

                        if (!ReflectionHelper.TrySetStackLimit(def, newLimit))
                            LoggingHelper.Warning($"Cannot set StackLimit on {LoggingHelper.SanitizeForLog(def.Name)} ({defType.Name}) — member is read-only.");
                    }
                }

                int totalChanged = changedByCategory.Values.Sum();
                if (totalChanged > 0)
                {
                    LoggingHelper.Msg("ApplyStackOverrides summary:");
                    foreach (var kv in processedByCategory.OrderBy(k => k.Key.ToString()))
                    {
                        changedByCategory.TryGetValue(kv.Key, out var changed);
                        changedNamesByCategory.TryGetValue(kv.Key, out var samples);
                        string sampleStr = samples is null || samples.Count == 0 ? "" : string.Join(", ", samples);
                        LoggingHelper.Msg($"  {kv.Key}: processed={kv.Value}, changed={changed}, examples=[{sampleStr}]");
                    }
                }
                else
                {
                    LoggingHelper.Msg("ApplyStackOverrides: no stack limits needed (config already applied)");
                }

                _lastCategoryModifiers = currentModifiers.ToDictionary(kv => kv.Key, kv => kv.Value);
                if (capturedThisRun || PreferencesMapper.HasSavedOriginals())
                    _originalsCaptured = true;
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"ApplyStackOverridesUsing failed: {ex}");
            }
        }

    }
}