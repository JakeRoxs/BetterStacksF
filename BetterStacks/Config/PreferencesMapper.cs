using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using Il2CppScheduleOne.ItemFramework;
using BetterStacks.Networking;
using Newtonsoft.Json;
using S1API.Lifecycle;

// services introduced during refactor

using BetterStacks.Utilities;

namespace BetterStacks.Config
{
    internal static class PreferencesMapper
    {
        private const string CategoryId = "BetterStacks";

        // In-memory mirror of CategoryMultipliers (replaces MelonPreferences_Entry registry).
        private static readonly Dictionary<string,int> _categoryMultiplierValues = new();

        // EnsureRegistered() idempotency flag.
        private static bool _registered = false;

        // replaced by MultiplayerHelper.IsClientInMultiplayer

        public static void EnsureRegistered()
        {
            if (_registered) return;

            try
            {
                // Mark registration-in-progress early so recursive calls during initialization
                // (e.g. from RegisterCategoryMultipliersFromGameDefs) don't re-enter.  The flag
                // is only left true once initialization succeeds; on any exception it will be
                // cleared in the catch block below, allowing subsequent retries.
                _registered = true;

                // initialize JSON store and take the initial snapshot
                JsonPreferencesStore.Initialize(() => ApplyPreferencesNow(persist: false));

                _lastAppliedPrefs = JsonPreferencesStore.Read();
#if DEBUG
                LoggingHelper.Msg($"Initial preference snapshot: {Newtonsoft.Json.JsonConvert.SerializeObject(_lastAppliedPrefs)}");
#endif

                // Mirror CategoryMultipliers into the in-memory registry for quick lookup.
                if (_lastAppliedPrefs?.CategoryMultipliers != null)
                {
                    _categoryMultiplierValues.Clear();
                    foreach (var kv in _lastAppliedPrefs.CategoryMultipliers)
                        _categoryMultiplierValues[kv.Key] = kv.Value;
                }

                // Prevent recursive re-entry: mark registered before calling the helper that may call EnsureRegistered().

                try { RegisterCategoryMultipliersFromGameDefs(); } catch { }

                _initialized = true;

            }
            catch (Exception ex)
            {
                // initialization did not complete, clear the flag so future callers may try again
                _registered = false;
                LoggingHelper.Warning($"EnsureRegistered failed: {ex.Message}");
            }
        }



        private static ModConfig? _lastAppliedPrefs = null;

        // Whether initial registration and snapshot have completed.
        private static bool _initialized = false;


        // Simplified: rely on MelonPreferences entry events; no FileSystemWatcher or on-disk hash tracking.

        // Return the set of category names that actually appear in the game's item definitions.
        // Unlike the old implementation, we do **not** fall back to the enum; an empty set means data
        // is not ready yet and callers should defer registration until the game has loaded defs.
        // moved to CategoryMultiplierRegistrar



        // Create (or prune) CategoryMultiplier keys in the JSON config based on game definitions.
        // Idempotent and safe to call multiple times.
        public static void RegisterCategoryMultipliersFromGameDefs()
        {
            CategoryMultiplierRegistrar.RegisterCategoryMultipliersFromGameDefs();
        }

        /// <summary>
        /// Helper used by other services to refresh the in-memory mirror after a
        /// config change or category registration.
        /// </summary>
        internal static void UpdateInMemoryCategoryMultipliers(ModConfig cfg)
        {
            _categoryMultiplierValues.Clear();
            if (cfg?.CategoryMultipliers != null)
            {
                foreach (var kv in cfg.CategoryMultipliers)
                    _categoryMultiplierValues[kv.Key] = kv.Value;
            }
        }

        // Apply current MelonPreferences immediately (called from event handlers or OnPreferencesSaved).
        public static void ApplyPreferencesNow(bool persist = true)
        {
            try
            {
                EnsureRegistered();

                // If ApplyPreferencesNow is invoked before initial registration/snapshot completes (for example,
                // MelonPreferences fires a Saved event while we are still registering entries), treat it as the
                // initial snapshot rather than a user-driven change — do not re-persist or broadcast.
                if (!_initialized)
                {
                    _lastAppliedPrefs = ReadFromPreferences();
                    LoggingHelper.Msg($"Initial preference snapshot (via ApplyPreferencesNow): {Newtonsoft.Json.JsonConvert.SerializeObject(_lastAppliedPrefs)}");
                    _initialized = true;
                    return;
                }

                var current = ReadFromPreferences();

                if (_lastAppliedPrefs != null && AreConfigsEqual(_lastAppliedPrefs, current))
                    return;

                LoggingHelper.Msg($"Preference change detected: previous={Newtonsoft.Json.JsonConvert.SerializeObject(_lastAppliedPrefs)}, new={Newtonsoft.Json.JsonConvert.SerializeObject(current)}");

                // If connected and not host, revert the change (do not persist) and log.
                if (MultiplayerHelper.IsClientInMultiplayer())
                {
                    if (_lastAppliedPrefs != null)
                    {
                        // Revert in-memory mirror to the last-applied (host) values.
                        _categoryMultiplierValues.Clear();
                        if (_lastAppliedPrefs.CategoryMultipliers != null)
                        {
                            foreach (var kv in _lastAppliedPrefs.CategoryMultipliers)
                                _categoryMultiplierValues[kv.Key] = kv.Value;
                        }

                        // Apply the host config locally so runtime values stay consistent.
                        BetterStacksMod.EnqueueConfigUpdate(_lastAppliedPrefs);
                    }

                    LoggingHelper.Msg("Preference changes from client reverted; only host may edit preferences while connected.");
                    return;
                }

                // Allowed change (host or single-player): apply, persist and broadcast if host.
                _lastAppliedPrefs = current;
                BetterStacksMod.EnqueueConfigUpdate(current);

                if (persist)
                {
                    try { WriteToPreferences(current); LoggingHelper.Msg("Saved JSON preferences after host edit."); } catch (Exception ex) { LoggingHelper.Warning($"Failed to save JSON preferences after host edit: {ex.Message}"); }
                }

                try
                {
                    var adapter = NetworkingManager.CurrentAdapter;
                    if (adapter != null && adapter.IsHost)
                        adapter.BroadcastHostConfig(new HostConfig { Config = current });
                }
                catch { }

                LoggingHelper.Msg("Preferences changed — applied live.");
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning($"ApplyPreferencesNow failed: {ex.Message}");
            }
        }

        // Entry-level MelonPreferences handlers removed — JSON config uses a FileSystemWatcher for hot-reload.

        public static bool AreConfigsEqual(ModConfig a, ModConfig b)
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
            try
            {
                return JsonPreferencesStore.Read();
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning($"Failed to read JSON config: {ex.Message}");
                return new ModConfig();
            }
        }

        public static void WriteToPreferences(ModConfig cfg)
        {
            // Disallow clients from programmatically writing prefs while connected to a host.
            if (MultiplayerHelper.IsClientInMultiplayer())
            {
                LoggingHelper.Msg("Client attempted to write preferences during multiplayer — operation blocked.");
                return;
            }

            EnsureRegistered();

            JsonPreferencesStore.Write(cfg);

            // update in-memory mirror
            _categoryMultiplierValues.Clear();
            if (cfg.CategoryMultipliers != null)
            {
                foreach (var kv in cfg.CategoryMultipliers)
                    _categoryMultiplierValues[kv.Key] = kv.Value;
            }

            // update last-applied snapshot
            _lastAppliedPrefs = cfg;

            // Apply and broadcast if host.
            var appliedCfg = ReadFromPreferences();
            BetterStacksMod.EnqueueConfigUpdate(appliedCfg);
            try
            {
                var adapter = NetworkingManager.CurrentAdapter;
                if (adapter != null && adapter.IsHost)
                    adapter.BroadcastHostConfig(new HostConfig { Config = appliedCfg });
            }
            catch { }
        }




        // File-path helpers have been moved into JsonPreferencesStore; no longer needed here.

        // Originals are tracked in a dedicated helper; we keep wrappers for backwards compatibility.
        public static int? GetSavedOriginalStackLimit(string defId) => OriginalStackTracker.GetSavedOriginalStackLimit(defId);
        public static void PersistOriginalStackLimit(string defId, int originalLimit) => OriginalStackTracker.PersistOriginalStackLimit(defId, originalLimit);
        public static bool HasSavedOriginals() => OriginalStackTracker.HasSavedOriginals();
    }
}
