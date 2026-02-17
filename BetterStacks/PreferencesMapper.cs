using System;
using System.Collections.Generic;
using MelonLoader;
using Il2CppScheduleOne.ItemFramework;
using BetterStacks.Networking;

namespace BetterStacks
{
    internal static class PreferencesMapper
    {
        private const string CategoryId = "BetterStacks";
        private static MelonPreferences_Category? _category;
        private static bool _suppressPreferenceEvents = false;

        private static MelonPreferences_Entry<bool>? _enableServerAuthoritative;
        private static MelonPreferences_Entry<int>? _mixingStationCapacity;
        private static MelonPreferences_Entry<int>? _mixingStationSpeed;
        private static MelonPreferences_Entry<int>? _dryingRackCapacity;
        private static readonly Dictionary<string, MelonPreferences_Entry<int>> _categoryMultiplierEntries = new();

        private static bool IsClientInMultiplayer()
        {
            var adapter = NetworkingManager.CurrentAdapter;
            return adapter is not null && adapter.IsInitialized && !adapter.IsHost;
        }

        public static void EnsureRegistered()
        {
            if (_category != null) return;
            _suppressPreferenceEvents = true; // prevent handlers firing during registration
            _category = MelonPreferences.CreateCategory(CategoryId);

            var defaults = new ModConfig();

            _enableServerAuthoritative = _category.CreateEntry<bool>("EnableServerAuthoritativeConfig", defaults.EnableServerAuthoritativeConfig);
            _mixingStationCapacity = _category.CreateEntry<int>("MixingStation.Capacity", defaults.MixingStationCapacity);
            _mixingStationSpeed = _category.CreateEntry<int>("MixingStation.Speed", defaults.MixingStationSpeed);
            _dryingRackCapacity = _category.CreateEntry<int>("DryingRack.Capacity", defaults.DryingRackCapacity);

            foreach (var name in Enum.GetNames(typeof(EItemCategory)))
            {
                int dv = 1;
                if (defaults.CategoryMultipliers != null && defaults.CategoryMultipliers.TryGetValue(name, out var dval))
                    dv = dval;
                var entry = _category.CreateEntry<int>($"CategoryMultiplier_{name}", dv);
                _categoryMultiplierEntries.Add(name, entry);
            }

            // Use polling (checked from OnUpdate) instead of direct MelonPreferences event subscription.
            // Initialize last-applied snapshot so we can detect and revert unauthorized client edits.
            _lastAppliedPrefs = ReadFromPreferences();
            _suppressPreferenceEvents = false;
        }

        // No-op handler placeholder kept for compatibility with older Melon APIs; polling is used instead.
        private static void HandlePreferenceChange<T>(MelonPreferences_Entry<T> entry, T oldValue, T newValue, string logicalName) { }

        private static ModConfig? _lastAppliedPrefs = null;

        // Called from BetterStacksMod.OnUpdate to detect preference edits (e.g. from modsapp) and apply/revert them immediately.
        public static void PollAndApplyChanges()
        {
            try
            {
                EnsureRegistered();
                var current = ReadFromPreferences();

                if (_lastAppliedPrefs is null)
                {
                    _lastAppliedPrefs = current;
                    return;
                }

                if (AreConfigsEqual(_lastAppliedPrefs, current))
                    return;

                // If connected and not host, revert the change and log.
                if (IsClientInMultiplayer())
                {
                    // Revert preference values to last applied snapshot (do not call WriteToPreferences since clients are blocked from writing).
                    _suppressPreferenceEvents = true;
                    try
                    {
                        // restore scalar entries
                        _enableServerAuthoritative!.Value = _lastAppliedPrefs.EnableServerAuthoritativeConfig;
                        _mixingStationCapacity!.Value = _lastAppliedPrefs.MixingStationCapacity;
                        _mixingStationSpeed!.Value = _lastAppliedPrefs.MixingStationSpeed;
                        _dryingRackCapacity!.Value = _lastAppliedPrefs.DryingRackCapacity;

                        // restore category multipliers
                        if (_lastAppliedPrefs.CategoryMultipliers != null)
                        {
                            foreach (var kv in _lastAppliedPrefs.CategoryMultipliers)
                            {
                                if (_categoryMultiplierEntries.TryGetValue(kv.Key, out var entry))
                                    entry.Value = kv.Value;
                            }
                        }
                    }
                    finally
                    {
                        _suppressPreferenceEvents = false;
                    }
                    MelonLogger.Msg("[Better Stacks] Preference changes from client reverted; only host may edit preferences while connected.");
                    return;
                }

                // Allowed change (host or single-player): apply and broadcast if host.
                _lastAppliedPrefs = current;
                BetterStacksMod.UpdateConfigFromHost(current);
                if (NetworkingManager.CurrentAdapter is not null && NetworkingManager.CurrentAdapter.IsHost)
                    NetworkingManager.BroadcastHostConfig(new HostConfig { Config = current });
                MelonLogger.Msg("[Better Stacks] Preferences changed — applied live.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Better Stacks] PollAndApplyChanges failed: {ex.Message}");
            }
        }

        private static bool AreConfigsEqual(ModConfig a, ModConfig b)
        {
            if (a.EnableServerAuthoritativeConfig != b.EnableServerAuthoritativeConfig) return false;
            if (a.MixingStationCapacity != b.MixingStationCapacity) return false;
            if (a.MixingStationSpeed != b.MixingStationSpeed) return false;
            if (a.DryingRackCapacity != b.DryingRackCapacity) return false;

            var da = a.CategoryMultipliers ?? new Dictionary<string,int>();
            var db = b.CategoryMultipliers ?? new Dictionary<string,int>();
            if (da.Count != db.Count) return false;
            foreach (var kv in da)
            {
                if (!db.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            }
            return true;
        }

        public static ModConfig ReadFromPreferences()
        {
            EnsureRegistered();
            var cfg = new ModConfig();

            cfg.EnableServerAuthoritativeConfig = _enableServerAuthoritative!.Value;
            cfg.MixingStationCapacity = _mixingStationCapacity!.Value;
            cfg.MixingStationSpeed = _mixingStationSpeed!.Value;
            cfg.DryingRackCapacity = _dryingRackCapacity!.Value;

            cfg.CategoryMultipliers = new Dictionary<string, int>();
            foreach (var kv in _categoryMultiplierEntries)
                cfg.CategoryMultipliers[kv.Key] = kv.Value.Value;

            return cfg;
        }

        public static void WriteToPreferences(ModConfig cfg)
        {
            // Disallow clients from programmatically writing prefs while connected to a host.
            if (IsClientInMultiplayer())
            {
                MelonLogger.Msg("[Better Stacks] Client attempted to write preferences during multiplayer — operation blocked.");
                return;
            }

            EnsureRegistered();
            _suppressPreferenceEvents = true;
            try
            {
                _enableServerAuthoritative!.Value = cfg.EnableServerAuthoritativeConfig;
                _mixingStationCapacity!.Value = cfg.MixingStationCapacity;
                _mixingStationSpeed!.Value = cfg.MixingStationSpeed;
                _dryingRackCapacity!.Value = cfg.DryingRackCapacity;

                if (cfg.CategoryMultipliers != null)
                {
                    foreach (var kv in cfg.CategoryMultipliers)
                    {
                        if (_categoryMultiplierEntries.TryGetValue(kv.Key, out var entry))
                            entry.Value = kv.Value;
                    }
                }
            }
            finally
            {
                _suppressPreferenceEvents = false;
            }

            MelonPreferences.Save();

            // Apply and broadcast if host.
            var appliedCfg = ReadFromPreferences();
            BetterStacksMod.UpdateConfigFromHost(appliedCfg);
            if (NetworkingManager.CurrentAdapter is not null && NetworkingManager.CurrentAdapter.IsHost)
                NetworkingManager.BroadcastHostConfig(new HostConfig { Config = appliedCfg });
        }
    }
}
