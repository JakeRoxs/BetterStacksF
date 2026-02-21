using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using Il2CppScheduleOne.ItemFramework;
using BetterStacks.Networking;
using Newtonsoft.Json;
using System.IO;
using System.Security.Cryptography;
using BetterStacks.Config;
using S1API.Lifecycle;

namespace BetterStacks.Config
{
    internal static class PreferencesMapper
    {
        private const string CategoryId = "BetterStacks";
        // JSON-based configuration file (migrated from MelonPreferences).
        private const string JsonFileName = "BetterStacks.json";

        // In-memory storage for original StackLimit values (runtime only).
        private static readonly Dictionary<string,int> _savedOriginalStackLimits = new();

        // In-memory mirror of CategoryMultipliers (replaces MelonPreferences_Entry registry).
        private static readonly Dictionary<string,int> _categoryMultiplierValues = new();

        // File watcher for JSON hot-reload (ModsApp edits JSON directly).
        private static FileSystemWatcher? _jsonWatcher = null;
        private static DateTime _lastWatcherEventUtc = DateTime.MinValue;
        // When true, the next watcher notification should be ignored because it originated
        // from our own programmatic write.
        private static bool _suppressWatcherEvent = false;

        // EnsureRegistered() idempotency flag.
        private static bool _registered = false;

        // Guard to prevent re-entrancy when registering category multipliers.
        private static bool _registeringCategoryMultipliers = false;

        // Ensure we only schedule a delayed registration once when defs are not ready.
        private static bool _registrationScheduled = false;

        private static bool IsClientInMultiplayer()
        {
            var adapter = NetworkingManager.CurrentAdapter;
            if (adapter == null || !adapter.IsInitialized) return false;
            if (adapter.IsHost) return false; // local or host session

            // For Steam adapter, only consider it a client when we're in a lobby that actually has
            // another member present. SteamNetworkClient can return IsInLobby=true even when alone
            // (e.g. single-player flow), so we guard by checking member count as well.
            if (adapter is SteamNetworkAdapter steam)
            {
                try
                {
                    if (steam.IsInLobby && steam.LobbyMemberCount > 1 && !steam.IsHost)
                        return true;
                }
                catch { }
                return false;
            }

            // Default conservative fallback: if not Steam, treat non-host as client only when
            // the adapter explicitly indicates multiplayer (none of our adapters currently do).
            return false;
        }

        public static void EnsureRegistered()
        {
            if (_registered) return;

            // Mark registration-in-progress immediately to prevent re-entry/recursion
            _registered = true;

            try
            {
                var userData = GetUserDataDirectory();
                Directory.CreateDirectory(userData);

                // preference file lives directly under the UserData root instead of a subfolder
                var jsonPath = Path.Combine(userData, JsonFileName);

                // If no JSON config exists yet, persist defaults so file is present for editors.
                if (!File.Exists(jsonPath))
                {
                    try { WriteToPreferences(new ModConfig()); MelonLogger.Msg("Created default JSON config: UserData/BetterStacks.json"); } catch { }
                }

                // Load current config into memory.
                _lastAppliedPrefs = ReadFromPreferences();
#if DEBUG
                MelonLogger.Msg($"Initial preference snapshot: {Newtonsoft.Json.JsonConvert.SerializeObject(_lastAppliedPrefs)}");
#endif

                // Mirror CategoryMultipliers into the in-memory registry for quick lookup.
                if (_lastAppliedPrefs?.CategoryMultipliers != null)
                {
                    _categoryMultiplierValues.Clear();
                    foreach (var kv in _lastAppliedPrefs.CategoryMultipliers)
                        _categoryMultiplierValues[kv.Key] = kv.Value;
                }

                // Prevent recursive re-entry: mark registered before calling the helper that may call EnsureRegistered().
                _registered = true;
                try { RegisterCategoryMultipliersFromGameDefs(); } catch { }

                // Watch the JSON file for external edits (ModsApp writes JSON directly).
                try
                {
                    // watch the root user data folder for changes to our single JSON file
                _jsonWatcher = new FileSystemWatcher(GetUserDataDirectory())
                    {
                        Filter = JsonFileName,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                        EnableRaisingEvents = true
                    };
                    _jsonWatcher.Changed += (s, e) =>
                    {
                        try
                        {
                            if (_suppressWatcherEvent) // our own write triggered this
                                return;

                            var now = DateTime.UtcNow;
                            if ((now - _lastWatcherEventUtc).TotalMilliseconds < 100) return;
                            _lastWatcherEventUtc = now;

                            MelonLogger.Msg($"Detected external JSON preference change: {e.Name}");
                            // The change originated outside of our code (e.g. ModsApp editor),
                            // so we should apply the new values without re-persisting them.
                            ApplyPreferencesNow(persist: false);
                        }
                        catch { }
                    };
                }
                catch { }

                _initialized = true;
                _registered = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"EnsureRegistered failed: {ex.Message}");
            }
        }



        private static ModConfig? _lastAppliedPrefs = null;

        // Whether initial registration and snapshot have completed.
        private static bool _initialized = false;

        // Timestamp used for debouncing programmatic writes / watcher events.
        private static DateTime _lastEntryValueSetUtc = DateTime.MinValue;

        // Simplified: rely on MelonPreferences entry events; no FileSystemWatcher or on-disk hash tracking.

        // Return the set of category names that actually appear in the game's item definitions.
        // Unlike the old implementation, we do **not** fall back to the enum; an empty set means data
        // is not ready yet and callers should defer registration until the game has loaded defs.
        private static HashSet<string> GetPresentCategoryNames()
        {
            try
            {
                var defs = S1API.Items.ItemManager.GetAllItemDefinitions();
                if (defs == null || defs.Count == 0)
                    return new HashSet<string>(); // not ready yet

                return defs.Select(d => ((EItemCategory)d.Category).ToString()).Distinct().ToHashSet();
            }
            catch
            {
                // ItemManager not available yet; signal absence rather than inventing categories.
                return new HashSet<string>();
            }
        }



        // Create (or prune) CategoryMultiplier keys in the JSON config based on game definitions.
        // Idempotent and safe to call multiple times.
        public static void RegisterCategoryMultipliersFromGameDefs()
        {
            // if the defs aren't loaded we schedule ourselves to run later instead of inventing values.
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
                    MelonLogger.Msg("Item definitions not ready yet, scheduled category registration for OnPreLoad.");
#endif
                }
                return;
            }

            if (_registeringCategoryMultipliers) return;
            _registeringCategoryMultipliers = true;
            try
            {
                EnsureRegistered();

                var defaults = new ModConfig();
                var cfg = ReadFromPreferences() ?? new ModConfig();
                if (cfg.CategoryMultipliers == null)
                    cfg.CategoryMultipliers = new Dictionary<string,int>();

                bool madeChanges = false;

                // Add missing present categories with sensible defaults.
                foreach (var name in present)
                {
                    if (!cfg.CategoryMultipliers.ContainsKey(name))
                    {
                        int dv = 1;
                        if (defaults.CategoryMultipliers != null && defaults.CategoryMultipliers.TryGetValue(name, out var dval))
                            dv = dval;

                        cfg.CategoryMultipliers[name] = dv;
                        madeChanges = true;
                        MelonLogger.Msg($"Added CategoryMultiplier key for '{name}' with default={dv}");
                    }
                }

                // Remove obsolete keys that no longer exist in the game defs (pruning pass).
                var toRemove = cfg.CategoryMultipliers.Keys.Where(k => !present.Contains(k)).ToList();
                foreach (var k in toRemove)
                {
                    cfg.CategoryMultipliers.Remove(k);
                    madeChanges = true;
                    MelonLogger.Warning($"Dropping obsolete CategoryMultiplier '{k}' (not found in game data)");
                }

                if (madeChanges)
                {
                    try { WriteToPreferences(cfg); } catch (Exception ex) { MelonLogger.Warning($"Failed to persist CategoryMultipliers JSON: {ex.Message}"); }
                }

                // Refresh in-memory mirror.
                _categoryMultiplierValues.Clear();
                foreach (var kv in cfg.CategoryMultipliers)
                    _categoryMultiplierValues[kv.Key] = kv.Value;
            }
            finally
            {
                _registeringCategoryMultipliers = false;
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
                    MelonLogger.Msg($"Initial preference snapshot (via ApplyPreferencesNow): {Newtonsoft.Json.JsonConvert.SerializeObject(_lastAppliedPrefs)}");
                    _initialized = true;
                    return;
                }

                var current = ReadFromPreferences();

                if (_lastAppliedPrefs != null && AreConfigsEqual(_lastAppliedPrefs, current))
                    return;

                MelonLogger.Msg($"Preference change detected: previous={Newtonsoft.Json.JsonConvert.SerializeObject(_lastAppliedPrefs)}, new={Newtonsoft.Json.JsonConvert.SerializeObject(current)}");

                // If connected and not host, revert the change (do not persist) and log.
                if (IsClientInMultiplayer())
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

                    MelonLogger.Msg("Preference changes from client reverted; only host may edit preferences while connected.");
                    return;
                }

                // Allowed change (host or single-player): apply, persist and broadcast if host.
                _lastAppliedPrefs = current;
                BetterStacksMod.EnqueueConfigUpdate(current);

                if (persist)
                {
                    try { WriteToPreferences(current); MelonLogger.Msg("Saved JSON preferences after host edit."); } catch (Exception ex) { MelonLogger.Warning($"Failed to save JSON preferences after host edit: {ex.Message}"); }
                }

                try
                {
                    var adapter = NetworkingManager.CurrentAdapter;
                    if (adapter != null && adapter.IsHost)
                        adapter.BroadcastHostConfig(new HostConfig { Config = current });
                }
                catch { }

                MelonLogger.Msg("Preferences changed — applied live.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ApplyPreferencesNow failed: {ex.Message}");
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
                var path = Path.Combine(GetUserDataDirectory(), JsonFileName);
                if (!File.Exists(path)) return new ModConfig();
                var json = File.ReadAllText(path);
                var cfg = JsonConvert.DeserializeObject<ModConfig>(json) ?? new ModConfig();
                return cfg;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Failed to read JSON config: {ex.Message}");
                return new ModConfig();
            }
        }

        public static void WriteToPreferences(ModConfig cfg)
        {
            // Disallow clients from programmatically writing prefs while connected to a host.
            if (IsClientInMultiplayer())
            {
                MelonLogger.Msg("Client attempted to write preferences during multiplayer — operation blocked.");
                return;
            }

            EnsureRegistered();
            // Registration of category multipliers is handled separately; do not call here to avoid re-entrant recursion.

            try
            {
                var path = Path.Combine(GetUserDataDirectory(), JsonFileName);
                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                // prevent watcher from reacting to this programmatic write
                _suppressWatcherEvent = true;
                try
                {
                    File.WriteAllText(path, json);
                }
                finally
                {
                    _suppressWatcherEvent = false;
                }
                _lastWatcherEventUtc = DateTime.UtcNow;

                // update in-memory mirror
                _categoryMultiplierValues.Clear();
                if (cfg.CategoryMultipliers != null)
                {
                    foreach (var kv in cfg.CategoryMultipliers)
                        _categoryMultiplierValues[kv.Key] = kv.Value;
                }

                // update last-applied snapshot
                _lastAppliedPrefs = cfg;

                MelonLogger.Msg("Wrote JSON preferences to UserData/BetterStacks.json");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Failed to write JSON preferences: {ex.Message}");
            }

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



        // Global MelonPreferences parsing removed — configuration now lives in a single JSON
        // file directly under the game's UserData folder (no BetterStacks subdirectory).
        // Write canonical files that ModsApp expects (ensures proper `[BetterStacks.Stacks]`
        // and `[BetterStacks.Tweaks]` sections). A single UserData file contains both sections
        // and existing callers continue to work since they simply forward to this helper.
        private static void WriteUserDataFile()
        {
            try
            {
                var userData = Path.Combine(GetUserDataDirectory());
                Directory.CreateDirectory(userData);
                var jsonPath = Path.Combine(userData, JsonFileName);

                // Build ModConfig from in-memory mirror
                var cfg = new ModConfig();
                cfg.CategoryMultipliers = new Dictionary<string,int>(_categoryMultiplierValues);

                // preserve other tweak defaults if present in last-applied
                if (_lastAppliedPrefs != null)
                {
                    cfg.EnableServerAuthoritativeConfig = _lastAppliedPrefs.EnableServerAuthoritativeConfig;
                    cfg.MixingStationCapacity = _lastAppliedPrefs.MixingStationCapacity;
                    cfg.MixingStationSpeed = _lastAppliedPrefs.MixingStationSpeed;
                    cfg.DryingRackCapacity = _lastAppliedPrefs.DryingRackCapacity;
                }

                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                _suppressWatcherEvent = true;
                try
                {
                    File.WriteAllText(jsonPath, json);
                }
                finally
                {
                    _suppressWatcherEvent = false;
                }
                _lastWatcherEventUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"WriteUserDataFile (JSON) failed: {ex.Message}");
            }
        }

        // Backwards-compatible wrappers (existing call-sites remain unchanged).
        private static void WriteStacksUserDataFile() => WriteUserDataFile();
        private static void WriteTweaksUserDataFile() => WriteUserDataFile();

        // Fallback-aware UserData directory accessor: prefer MelonLoader's MelonEnvironment.UserDataDirectory when available,
        // otherwise fall back to the game's UserData folder under Environment.CurrentDirectory.
        private static string GetUserDataDirectory()
        {
            try
            {
                var t = Type.GetType("MelonLoader.MelonEnvironment, MelonLoader");
                if (t != null)
                {
                    var prop = t.GetProperty("UserDataDirectory", BindingFlags.Public | BindingFlags.Static);
                    if (prop != null)
                    {
                        var val = prop.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(val)) return val;
                    }
                }
            }
            catch { }

            var gameDir = Environment.CurrentDirectory;
            return Path.Combine(gameDir, "UserData");
        }

        // Compute SHA256 hash of the canonical user-data file; used to ignore unchanged external saves.
        private static string? ComputeFileHash(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var sha = SHA256.Create();
                using var fs = File.OpenRead(path);
                var hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch { return null; }
        }

        // Originals are stored in-memory for the active save session (runtime-only).
        // Stored in the in-memory dictionary so originals are scoped to the active session rather than global MelonPreferences.
        public static int? GetSavedOriginalStackLimit(string defId)
        {
            return _savedOriginalStackLimits.TryGetValue(defId, out var v) ? (int?)v : null;
        }

        public static void PersistOriginalStackLimit(string defId, int originalLimit)
        {
            _savedOriginalStackLimits[defId] = originalLimit;
            //MelonLogger.Msg($"Recorded original StackLimit in-memory for '{defId}' = {originalLimit}");
        }

        public static bool HasSavedOriginals() => _savedOriginalStackLimits.Count > 0;
    }
}
