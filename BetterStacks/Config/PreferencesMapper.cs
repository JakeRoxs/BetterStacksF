using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using Il2CppScheduleOne.ItemFramework;
using BetterStacks.Networking;
using Newtonsoft.Json;
using S1API.Lifecycle;
using BetterStacks.Utilities;
using System.IO;

namespace BetterStacks.Config
{
    internal static class PreferencesMapper
    {
        private const string CategoryId = "BetterStacks";

        private static MelonPreferences_Category _prefsCategory = null!;
        private static MelonPreferences_Entry<bool> _enableServerAuthoritative = null!;
        private static MelonPreferences_Entry<int> _mixingCapacity = null!;
        private static MelonPreferences_Entry<int> _mixingSpeed = null!;
        private static MelonPreferences_Entry<int> _dryingCapacity = null!;
        private static MelonPreferences_Entry<int> _cauldronMultiplier = null!;
        private static MelonPreferences_Entry<int> _cauldronCookSpeed = null!;

        private static readonly Dictionary<string, MelonPreferences_Entry<int>> _categoryEntries = new();
        private static readonly Dictionary<string, int> _categoryMultiplierValues = new();

        private static ModConfig? _lastAppliedPrefs = null;
        private static bool _registered = false;
        private static bool _initialized = false;
        private static bool _suppressEntryEvents = false;

        private static readonly HashSet<string> _reservedKeys = new HashSet<string>
        {
            "EnableServerAuthoritativeConfig",
            "MixingStationCapacity",
            "MixingStationSpeed",
            "DryingRackCapacity",
            "CauldronIngredientMultiplier",
            "CauldronCookSpeed"
        };

        private static readonly object _registrationLock = new object();

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

        public static void EnsureRegistered()
        {
            if (_registered) return;
            lock (_registrationLock)
            {
                if (_registered) return; // double-checked lock
                // mark early to prevent the recursive call from RegisterCategoryMultipliers
                // re-entering this method.
                _registered = true;

                _prefsCategory = MelonPreferences.CreateCategory(CategoryId, "BetterStacks Preferences");
                // store preferences in a dedicated file to keep the main cfg clean
                try { _prefsCategory.SetFilePath(Path.Combine(GetUserDataDirectory(), "BetterStacksF.cfg")); } catch { }

                _suppressEntryEvents = true;
                try
                {
                    _enableServerAuthoritative = _prefsCategory.CreateEntry("EnableServerAuthoritativeConfig", true,
                        "Enable server-authoritative config",
                        "When enabled the host may override player settings in multiplayer.");

                    _mixingCapacity = _prefsCategory.CreateEntry("MixingStationCapacity", 1,
                        "Mixing station capacity multiplier",
                        "Multiplies the amount of items a mixing station can hold.");

                    _mixingSpeed = _prefsCategory.CreateEntry("MixingStationSpeed", 1,
                        "Mixing station speed multiplier",
                        "Divides the time of mixing stations, higher values make mixing faster.");

                    _dryingCapacity = _prefsCategory.CreateEntry("DryingRackCapacity", 1,
                        "Drying rack capacity multiplier",
                        "Multiplies the capacity of drying racks.");

                    _cauldronMultiplier = _prefsCategory.CreateEntry("CauldronIngredientMultiplier", 1,
                        "Cauldron ingredient multiplier",
                        "Multiplies the quantity able to be cooked (coca leaves) and the resulting output amount.");

                    _cauldronCookSpeed = _prefsCategory.CreateEntry("CauldronCookSpeed", 1,
                        "Cauldron cook speed multiplier",
                        "Divides the cooking time when a cauldron starts a recipe, higher values make cooking faster.");

                    _enableServerAuthoritative.OnEntryValueChanged.Subscribe(OnEntryChanged);
                    _mixingCapacity.OnEntryValueChanged.Subscribe(OnEntryChanged);
                    _mixingSpeed.OnEntryValueChanged.Subscribe(OnEntryChanged);
                    _dryingCapacity.OnEntryValueChanged.Subscribe(OnEntryChanged);
                    _cauldronMultiplier.OnEntryValueChanged.Subscribe(OnEntryChanged);
                    _cauldronCookSpeed.OnEntryValueChanged.Subscribe(OnEntryChanged);

                    LoadExistingCategoryEntries();

                    MelonPreferences.OnPreferencesSaved.Subscribe(_ => ApplyPreferencesNow(true));

                    _lastAppliedPrefs = ReadFromPreferences();
                }
                finally
                {
                    _suppressEntryEvents = false;
                }
            } // end lock
#if DEBUG
            LoggingHelper.Msg($"Initial preference snapshot: {JsonConvert.SerializeObject(_lastAppliedPrefs)}");
#endif

            if (_lastAppliedPrefs?.CategoryMultipliers != null)
            {
                _categoryMultiplierValues.Clear();
                foreach (var kv in _lastAppliedPrefs.CategoryMultipliers)
                    _categoryMultiplierValues[kv.Key] = kv.Value;
            }

            try { RegisterCategoryMultipliersFromGameDefs(); }
            catch (Exception ex)
            {
                // registration is best-effort; log any failure for diagnostics
                LoggingHelper.Warning($"RegisterCategoryMultipliersFromGameDefs failed: {ex.Message}");
            }


            _initialized = true;
        } 

        private static void OnEntryChanged<T>(T oldValue, T newValue)
        {
            if (!_suppressEntryEvents)
                ApplyPreferencesNow(persist: false);
        }

        private static void LoadExistingCategoryEntries()
        {
            try
            {
                // MelonPreferences does not expose a public list of entries, so we
                // reflect into its private 'Entries' dictionary. This mirrors the
                // internal structure of the currently used MelonLoader version and
                // may break if that implementation changes. Any failure here is
                // non‑fatal; it simply means we won't pick up existing user-defined
                // category entries.
                var prop = typeof(MelonPreferences_Category).GetProperty("Entries",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null) return;

                var dict = prop.GetValue(_prefsCategory) as System.Collections.IEnumerable;
                if (dict == null) return;

                foreach (var kv in dict)
                {
                    var keyProp = kv.GetType().GetProperty("Key");
                    var valueProp = kv.GetType().GetProperty("Value");
                    if (keyProp == null || valueProp == null) continue;

                    var key = keyProp.GetValue(kv) as string;
                    var entryObj = valueProp.GetValue(kv);
                    if (key == null || entryObj == null) continue;
                    if (_reservedKeys.Contains(key)) continue;

                    var entryType = entryObj.GetType();
                    if (entryType.IsGenericType &&
                        entryType.GetGenericTypeDefinition() == typeof(MelonPreferences_Entry<>) &&
                        entryType.GetGenericArguments()[0] == typeof(int))
                    {
                        var intEntry = (MelonPreferences_Entry<int>)entryObj;
                        _categoryEntries[key] = intEntry;
                        intEntry.OnEntryValueChanged.Subscribe((o, n) =>
                        {
                            if (!_suppressEntryEvents)
                                ApplyPreferencesNow(persist: false);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning($"LoadExistingCategoryEntries reflection failed: {ex.Message}");
            }
        }

        public static void RegisterCategoryMultipliersFromGameDefs()
        {
            CategoryMultiplierRegistrar.RegisterCategoryMultipliersFromGameDefs();
        }

        internal static void UpdateInMemoryCategoryMultipliers(ModConfig cfg)
        {
            _categoryMultiplierValues.Clear();
            if (cfg?.CategoryMultipliers != null)
            {
                foreach (var kv in cfg.CategoryMultipliers)
                    _categoryMultiplierValues[kv.Key] = kv.Value;
            }
        }

        public static ModConfig ReadFromPreferences()
        {
            EnsureRegistered();
            try
            {
                var cfg = new ModConfig
                {
                    EnableServerAuthoritativeConfig = _enableServerAuthoritative.Value,
                    MixingStationCapacity = _mixingCapacity.Value,
                    MixingStationSpeed = _mixingSpeed.Value,
                    DryingRackCapacity = _dryingCapacity.Value,
                    CauldronIngredientMultiplier = _cauldronMultiplier.Value,
                    CauldronCookSpeed = _cauldronCookSpeed.Value,
                    CategoryMultipliers = new Dictionary<string, int>()
                };

                foreach (var kv in _categoryEntries)
                    cfg.CategoryMultipliers[kv.Key] = kv.Value.Value;

                return cfg;
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning($"Failed to read preferences: {ex.Message}");
                return new ModConfig();
            }
        }

        /// <summary>
        /// Update all preference entries (and in‑memory multiplier cache) to match the
        /// supplied config object. Entry change events are suppressed during the
        /// operation.
        /// </summary>
        private static void ApplyConfigToEntries(ModConfig cfg)
        {
            _suppressEntryEvents = true;
            try
            {
                _enableServerAuthoritative.Value = cfg.EnableServerAuthoritativeConfig;
                _mixingCapacity.Value = cfg.MixingStationCapacity;
                _mixingSpeed.Value = cfg.MixingStationSpeed;
                _dryingCapacity.Value = cfg.DryingRackCapacity;
                _cauldronMultiplier.Value = cfg.CauldronIngredientMultiplier;
                _cauldronCookSpeed.Value = cfg.CauldronCookSpeed;

                _categoryMultiplierValues.Clear();
                if (cfg.CategoryMultipliers != null)
                {
                    foreach (var kv in cfg.CategoryMultipliers)
                    {
                        CreateCategoryEntry(kv.Key, kv.Value).Value = kv.Value;
                        _categoryMultiplierValues[kv.Key] = kv.Value;
                    }
                }
            }
            finally
            {
                _suppressEntryEvents = false;
            }
        }

        public static void WriteToPreferences(ModConfig cfg)
        {
            if (MultiplayerHelper.IsClientInMultiplayer())
            {
                LoggingHelper.Msg("Client attempted to write preferences during multiplayer — operation blocked.");
                return;
            }

            EnsureRegistered();

            // sync UI entries + internal cache to the supplied config
            ApplyConfigToEntries(cfg);

            MelonPreferences.Save();

            _lastAppliedPrefs = cfg;

            var appliedCfg = ReadFromPreferences();
            ConfigManager.EnqueueConfigUpdate(appliedCfg);
            try
            {
                var adapter = NetworkingManager.CurrentAdapter;
                if (adapter != null && adapter.IsHost)
                    adapter.BroadcastHostConfig(new HostConfig { Config = appliedCfg });
            }
            catch { }
        }

        private static MelonPreferences_Entry<int> CreateCategoryEntry(string name, int defaultValue)
        {
            if (_categoryEntries.TryGetValue(name, out var existing))
                return existing;
            // these entries don’t need a tooltip‑style description, it just
            // bloats the ModsApp UI so we leave it empty
            var entry = _prefsCategory.CreateEntry(name, defaultValue, "");
            entry.OnEntryValueChanged.Subscribe((o, n) =>
            {
                if (!_suppressEntryEvents)
                    ApplyPreferencesNow(persist: false);
            });

            _categoryEntries[name] = entry;
            return entry;
        }

        public static bool AreConfigsEqual(ModConfig a, ModConfig b)
        {
            if (a.EnableServerAuthoritativeConfig != b.EnableServerAuthoritativeConfig) return false;
            if (a.MixingStationCapacity != b.MixingStationCapacity) return false;
            if (a.MixingStationSpeed != b.MixingStationSpeed) return false;
            if (a.DryingRackCapacity != b.DryingRackCapacity) return false;
            if (a.CauldronIngredientMultiplier != b.CauldronIngredientMultiplier) return false;
            if (a.CauldronCookSpeed != b.CauldronCookSpeed) return false;

            var da = a.CategoryMultipliers ?? new Dictionary<string, int>();
            var db = b.CategoryMultipliers ?? new Dictionary<string, int>();
            if (da.Count != db.Count) return false;
            foreach (var kv in da)
            {
                if (!db.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            }
            return true;
        }

        public static void ApplyPreferencesNow(bool persist = true)
        {
            try
            {
                EnsureRegistered();

                if (!_initialized)
                {
                    _lastAppliedPrefs = ReadFromPreferences();
                    LoggingHelper.Msg($"Initial preference snapshot (via ApplyPreferencesNow): {JsonConvert.SerializeObject(_lastAppliedPrefs)}");
                    _initialized = true;
                    return;
                }

                var current = ReadFromPreferences();

                if (_lastAppliedPrefs != null && AreConfigsEqual(_lastAppliedPrefs, current))
                    return;

                LoggingHelper.Msg($"Preference change detected: previous={JsonConvert.SerializeObject(_lastAppliedPrefs)}, new={JsonConvert.SerializeObject(current)}");

                if (MultiplayerHelper.IsClientInMultiplayer())
                {
                    if (_lastAppliedPrefs != null)
                    {
                        // When a client makes a change, revert everything to the last host snapshot.
                        // ApplyConfigToEntries handles both the entry values and the in-memory cache.
                        ApplyConfigToEntries(_lastAppliedPrefs);

                        ConfigManager.EnqueueConfigUpdate(_lastAppliedPrefs);
                    }

                    LoggingHelper.Msg("Preference changes from client reverted; only host may edit preferences while connected.");
                    return;
                }

                _lastAppliedPrefs = current;
                ConfigManager.EnqueueConfigUpdate(current);

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

        public static int? GetSavedOriginalStackLimit(string defId) => OriginalStackTracker.GetSavedOriginalStackLimit(defId);
        public static void PersistOriginalStackLimit(string defId, int originalLimit) => OriginalStackTracker.PersistOriginalStackLimit(defId, originalLimit);
        public static bool HasSavedOriginals() => OriginalStackTracker.HasSavedOriginals();
    }
}
