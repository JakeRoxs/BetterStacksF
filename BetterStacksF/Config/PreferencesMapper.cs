using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using BetterStacksF.Networking;
using BetterStacksF.Utilities;

using Il2CppScheduleOne.ItemFramework;

using MelonLoader;


using S1API.Lifecycle;

namespace BetterStacksF.Config {
  internal static class PreferencesMapper {
    private const string CategoryId = "BetterStacks";

    private static MelonPreferences_Category _prefsCategory = null!;
    private static MelonPreferences_Entry<bool> _enableServerAuthoritative = null!;
    private static MelonPreferences_Entry<int> _mixingCapacity = null!;
    private static MelonPreferences_Entry<int> _mixingSpeed = null!;
    private static MelonPreferences_Entry<int> _dryingCapacity = null!;
    private static MelonPreferences_Entry<int> _cauldronMultiplier = null!;
    private static MelonPreferences_Entry<int> _cauldronCookSpeed = null!;
    private static MelonPreferences_Entry<int> _chemistrySpeed = null!;
    private static MelonPreferences_Entry<int> _labOvenSpeed = null!;
    private static MelonPreferences_Entry<bool> _verboseLogging = null!;
#if DEBUG
    private const bool VerboseLoggingDefault = true;
#else
    private const bool VerboseLoggingDefault = false;
#endif
    private static readonly Dictionary<string, MelonPreferences_Entry<int>> _categoryEntries = new();
    // cache of multiplier values for fast access by other subsystems.  It is
    // populated whenever the configuration is loaded from disk or written back
    // (see ApplyConfigToEntries/UpdateInMemoryCategoryMultipliers).  Until a
    // preferences file has actually been read or a save executed the cache will
    // be empty; that's fine because nothing uses it before a real config is
    // available.
    private static readonly Dictionary<string, int> _categoryMultiplierValues = new();

    private static ModConfig? _lastAppliedPrefs = null;

    /// <summary>
    /// Returns the most recently read preferences without touching disk.  May
    /// be <c>null</c> if no read has occurred yet.
    /// </summary>
    internal static ModConfig? GetCachedPreferences() => _lastAppliedPrefs;
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
            "CauldronCookSpeed",
            "ChemistryStationSpeed",
            "LabOvenSpeed",
            "VerboseLogging"
    };

    /// <summary>
    /// Remove any entries from <see cref="ModConfig.CategoryMultipliers"/> whose
    /// key is null, empty, or consists solely of whitespace.  Also trim keys and
    /// collapse duplicates (keeping the last value encountered).  This helps guard
    /// against malformed data introduced by the ModsApp UI or previous bugs.
    /// </summary>
    internal static void SanitizeCategoryKeys(ModConfig cfg) {
      if (cfg == null || cfg.CategoryMultipliers == null)
        return;

      var originals = cfg.CategoryMultipliers;
      var cleaned = new Dictionary<string, int>();
      foreach (var kv in originals) {
        var key = kv.Key?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(key)) {
          LoggingHelper.Warning($"Dropping invalid CategoryMultipliers key ('{kv.Key ?? "<null>"}')");
          continue;
        }

        // if duplicates arise after trimming, the later value wins
        cleaned[key] = kv.Value;
      }

      cfg.CategoryMultipliers = cleaned;
    }

    /// <summary>
    /// Remove any entries from the underlying MelonPreferences category whose
    /// identifiers are blank or whitespace.  These are invisible to the end user
    /// and are the root cause of the blank rows seen in the ModsApp editor.
    /// </summary>
    private static void RemoveInvalidCategoryEntries() {
      if (_prefsCategory == null) return;
      try {
        var prop = typeof(MelonPreferences_Category).GetProperty("Entries",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop == null) return;

        var dictObj = prop.GetValue(_prefsCategory);
        if (dictObj is System.Collections.IDictionary dict) {
          var toRemove = new List<object>();
          foreach (var key in dict.Keys) {
            if (key is string s && string.IsNullOrWhiteSpace(s))
              toRemove.Add(key);
          }

          foreach (var key in toRemove)
            dict.Remove(key);
        }
      }
      catch (Exception ex) {
        LoggingHelper.Warning($"RemoveInvalidCategoryEntries failed: {ex.Message}");
      }
    }

    private static readonly object _registrationLock = new object();

    private static string GetUserDataDirectory() {
      try {
        var t = Type.GetType("MelonLoader.MelonEnvironment, MelonLoader");
        if (t != null) {
          var prop = t.GetProperty("UserDataDirectory", BindingFlags.Public | BindingFlags.Static);
          if (prop != null) {
            var val = prop.GetValue(null) as string;
            if (!string.IsNullOrEmpty(val)) return val;
          }
        }
      }
      catch { }

      var gameDir = Environment.CurrentDirectory;
      return Path.Combine(gameDir, "UserData");
    }

    public static void EnsureRegistered() {
      if (_registered) return;
      lock (_registrationLock) {
        if (_registered) return; // double-checked lock
                                 // mark early to prevent the recursive call from RegisterCategoryMultipliers
                                 // re-entering this method.
        _registered = true;

        _prefsCategory = MelonPreferences.CreateCategory(CategoryId, "BetterStacks Preferences");
        // store preferences in a dedicated file to keep the main cfg clean
        try { _prefsCategory.SetFilePath(Path.Combine(GetUserDataDirectory(), "BetterStacksF.cfg")); } catch { }

        _suppressEntryEvents = true;
        try {
          InitializeCoreEntries();
          SubscribeCoreEntryEvents();
          LoadExistingCategoryEntries();
          MelonPreferences.OnPreferencesSaved.Subscribe(_ => ApplyPreferencesNow(true));
          _lastAppliedPrefs = ReadFromPreferences();
        }
        finally {
          _suppressEntryEvents = false;
        }

        // emit initial snapshot if verbose logging is enabled
        LoggingHelper.Msg("Initial preference snapshot", _lastAppliedPrefs);

        // remove any junk entries from the raw preferences so they don't linger
        RemoveInvalidCategoryEntries();

        try { RegisterCategoryMultipliersFromGameDefs(); }
        catch (Exception ex) {
          // registration is best-effort; log any failure for diagnostics
          LoggingHelper.Warning($"RegisterCategoryMultipliersFromGameDefs failed: {ex.Message}");
        }

        _initialized = true;
      }
    }

    // debounce helper used by OnEntryChanged.  many UI interactions (see
    // ModsApp) end up flipping several entries one after the other; without
    // aggregation we log a separate diff for each field and apply the
    // configuration repeatedly.  schedule a single "apply" at the end of the
    // current frame instead, which gives the UI enough time to finish its
    // batch and produces one cohesive diff.
    private static bool _applyScheduled;

    private static IEnumerator ApplyPreferencesLater() {
      // yield once so that any immediately following entry-change events can
      // fire before we read the preferences.  using MelonCoroutines keeps us
      // on the game thread and avoids needing a timer.
      yield return null;
      _applyScheduled = false;
      ApplyPreferencesNow(persist: false);
    }

    private static void OnEntryChanged<T>(T oldValue, T newValue) {
      if (_suppressEntryEvents)
        return;

      if (!_applyScheduled) {
        _applyScheduled = true;
        MelonCoroutines.Start(ApplyPreferencesLater());
      }
    }

    private static void InitializeCoreEntries() {
      // Many calls to MelonPreferences.CreateEntry follow.  In the event that
      // `_prefsCategory` is unexpectedly null (shouldn't happen) or the
      // underlying MelonPreferences implementation throws, we don't want an
      // unhandled exception bubbling out of `EnsureRegistered` and killing
      // initialization.  Wrap the whole thing so we can log the error and
      // continue gracefully using defaults.
      try {
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
            "(TODO)Multiplies the quantity able to be cooked (coca leaves) and the resulting output amount.");

        _cauldronCookSpeed = _prefsCategory.CreateEntry("CauldronCookSpeed", 1,
            "Cauldron cook speed multiplier",
            "Divides the cooking time when a cauldron starts a recipe, higher values make cooking faster.");

        _chemistrySpeed = _prefsCategory.CreateEntry("ChemistryStationSpeed", 1,
            "Chemistry station speed multiplier",
            "Divides the minute tick passed to chemistry stations; higher values make chemistry operations run faster.");

        _labOvenSpeed = _prefsCategory.CreateEntry("LabOvenSpeed", 1,
            "Lab oven speed multiplier",
            "Divides the minute tick passed to lab ovens; higher values make oven operations run faster.");

        _verboseLogging = _prefsCategory.CreateEntry("VerboseLogging", VerboseLoggingDefault,
            "Verbose logging",
            "When enabled the mod emits additional informational messages to the log.  Useful for debugging but noisy in normal use.");
      }
      catch (Exception ex) {
        LoggingHelper.Error("InitializeCoreEntries failed", ex);
      }
    }

    private static void SubscribeCoreEntryEvents() {
      // Entries might be null if InitializeCoreEntries failed partway through.
      // subscribe only to the ones that were successfully created to avoid
      // NullReferenceExceptions and log the missing ones for diagnostics.
      if (_enableServerAuthoritative != null)
        _enableServerAuthoritative.OnEntryValueChanged.Subscribe(OnEntryChanged);
      else
        LoggingHelper.Warning("PreferencesMapper: _enableServerAuthoritative entry not initialized");

      if (_mixingCapacity != null)
        _mixingCapacity.OnEntryValueChanged.Subscribe(OnEntryChanged);
      else
        LoggingHelper.Warning("PreferencesMapper: _mixingCapacity entry not initialized");

      if (_mixingSpeed != null)
        _mixingSpeed.OnEntryValueChanged.Subscribe(OnEntryChanged);
      else
        LoggingHelper.Warning("PreferencesMapper: _mixingSpeed entry not initialized");

      if (_dryingCapacity != null)
        _dryingCapacity.OnEntryValueChanged.Subscribe(OnEntryChanged);
      else
        LoggingHelper.Warning("PreferencesMapper: _dryingCapacity entry not initialized");

      if (_cauldronMultiplier != null)
        _cauldronMultiplier.OnEntryValueChanged.Subscribe(OnEntryChanged);
      else
        LoggingHelper.Warning("PreferencesMapper: _cauldronMultiplier entry not initialized");

      if (_cauldronCookSpeed != null)
        _cauldronCookSpeed.OnEntryValueChanged.Subscribe(OnEntryChanged);
      else
        LoggingHelper.Warning("PreferencesMapper: _cauldronCookSpeed entry not initialized");

      if (_chemistrySpeed != null)
        _chemistrySpeed.OnEntryValueChanged.Subscribe(OnEntryChanged);
      else
        LoggingHelper.Warning("PreferencesMapper: _chemistrySpeed entry not initialized");

      if (_labOvenSpeed != null)
        _labOvenSpeed.OnEntryValueChanged.Subscribe(OnEntryChanged);
      else
        LoggingHelper.Warning("PreferencesMapper: _labOvenSpeed entry not initialized");

      if (_verboseLogging != null)
        _verboseLogging.OnEntryValueChanged.Subscribe(OnEntryChanged);
      else
        LoggingHelper.Warning("PreferencesMapper: _verboseLogging entry not initialized");
    }

    private static bool AreCoreEntriesInitialized() {
      return _enableServerAuthoritative != null &&
             _mixingCapacity != null &&
             _mixingSpeed != null &&
             _dryingCapacity != null &&
             _cauldronMultiplier != null &&
             _cauldronCookSpeed != null &&
             _labOvenSpeed != null &&
             _verboseLogging != null;
    }

    private static void LoadExistingCategoryEntries() {
      try {
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

        foreach (var kv in dict) {
          var keyProp = kv.GetType().GetProperty("Key");
          var valueProp = kv.GetType().GetProperty("Value");
          if (keyProp == null || valueProp == null) continue;

          var key = keyProp.GetValue(kv) as string;
          var entryObj = valueProp.GetValue(kv);
          if (key == null || entryObj == null) continue;
          if (string.IsNullOrWhiteSpace(key)) {
            // drop any malformed entry identifier so we don't create
            // a blank category row later.
            LoggingHelper.Warning("PreferencesMapper: ignoring category entry with empty identifier");
            continue;
          }
          if (_reservedKeys.Contains(key)) continue;

          var entryType = entryObj.GetType();
          if (entryType.IsGenericType &&
              entryType.GetGenericTypeDefinition() == typeof(MelonPreferences_Entry<>) &&
              entryType.GetGenericArguments()[0] == typeof(int)) {
            var intEntry = (MelonPreferences_Entry<int>)entryObj;
            _categoryEntries[key] = intEntry;
            intEntry.OnEntryValueChanged.Subscribe((o, n) => {
              if (!_suppressEntryEvents)
                ApplyPreferencesNow(persist: false);
            });
          }
        }
      }
      catch (Exception ex) {
        LoggingHelper.Warning($"LoadExistingCategoryEntries reflection failed: {ex.Message}");
      }
    }

    public static void RegisterCategoryMultipliersFromGameDefs() {
      CategoryMultiplierRegistrar.RegisterCategoryMultipliersFromGameDefs();
    }

    internal static void UpdateInMemoryCategoryMultipliers(ModConfig cfg) {
      _categoryMultiplierValues.Clear();
      if (cfg?.CategoryMultipliers != null) {
        foreach (var kv in cfg.CategoryMultipliers)
          _categoryMultiplierValues[kv.Key] = kv.Value;
      }
    }

    public static ModConfig ReadFromPreferences() {
      EnsureRegistered();

      // if any of the core entries didn't get created, bail out early.
      if (!AreCoreEntriesInitialized()) {
        LoggingHelper.Warning("PreferencesMapper: core preference entries not initialized, returning empty config");
        return new ModConfig();
      }

      try {
        var cfg = new ModConfig {
          EnableServerAuthoritativeConfig = _enableServerAuthoritative.Value,
          EnableVerboseLogging = _verboseLogging.Value,
          MixingStationCapacity = _mixingCapacity.Value,
          MixingStationSpeed = _mixingSpeed.Value,
          DryingRackCapacity = _dryingCapacity.Value,
          CauldronIngredientMultiplier = _cauldronMultiplier.Value,
          CauldronCookSpeed = _cauldronCookSpeed.Value,
          ChemistryStationSpeed = _chemistrySpeed.Value,
          LabOvenSpeed = _labOvenSpeed.Value,
          // leave CategoryMultipliers untouched so the class initializer can
          // supply its built‑in defaults; we'll overlay any existing entries below.
        };

        foreach (var kv in _categoryEntries)
          cfg.CategoryMultipliers[kv.Key] = kv.Value.Value;

        // guard against any malformed entries (empty key, whitespace, etc.)
        SanitizeCategoryKeys(cfg);

        // clamp any zero or negative multipliers to 1 so they behave like
        // missing values.  this prevents diffs showing 0 -> N when a key is
        // added later (common during the first registration pass).
        var keys = cfg.CategoryMultipliers.Keys.ToList();
        foreach (var k in keys) {
          cfg.CategoryMultipliers[k] = NormalizeMultiplier(cfg.CategoryMultipliers[k]);
        }

        return cfg;
      }
      catch (Exception ex) {
        LoggingHelper.Warning($"Failed to read preferences: {ex.Message}");
        return new ModConfig();
      }
    }

    /// <summary>
    /// Update all preference entries (and in‑memory multiplier cache) to match the
    /// supplied config object. Entry change events are suppressed during the
    /// operation.
    /// </summary>
    private static void ApplyConfigToEntries(ModConfig cfg) {
      _suppressEntryEvents = true;
      try {
        if (AreCoreEntriesInitialized()) {
          _enableServerAuthoritative.Value = cfg.EnableServerAuthoritativeConfig;
          _verboseLogging.Value = cfg.EnableVerboseLogging;
          _mixingCapacity.Value = cfg.MixingStationCapacity;
          _mixingSpeed.Value = cfg.MixingStationSpeed;
          _dryingCapacity.Value = cfg.DryingRackCapacity;
          _cauldronMultiplier.Value = cfg.CauldronIngredientMultiplier;
          _cauldronCookSpeed.Value = cfg.CauldronCookSpeed;
          _chemistrySpeed.Value = cfg.ChemistryStationSpeed;
          _labOvenSpeed.Value = cfg.LabOvenSpeed;
        }

        _categoryMultiplierValues.Clear();
        if (cfg.CategoryMultipliers != null) {
          foreach (var kv in cfg.CategoryMultipliers) {
            CreateCategoryEntry(kv.Key, kv.Value).Value = kv.Value;
            _categoryMultiplierValues[kv.Key] = kv.Value;
          }
        }
      }
      finally {
        _suppressEntryEvents = false;
      }
    }

    public static void WriteToPreferences(ModConfig cfg) {
      if (MultiplayerHelper.IsClientInMultiplayer()) {
        LoggingHelper.Msg("Client attempted to write preferences during multiplayer — operation blocked.");
        return;
      }

      EnsureRegistered();

      // sanitize input first to prevent blank keys from being persisted
      SanitizeCategoryKeys(cfg);
      // remove stray entries from the underlying MelonPreferences category
      RemoveInvalidCategoryEntries();

      // sync UI entries + internal cache to the supplied config
      ApplyConfigToEntries(cfg);

      MelonPreferences.Save();

      _lastAppliedPrefs = cfg;

      var appliedCfg = ReadFromPreferences();
      ConfigManager.EnqueueConfigUpdate(appliedCfg);
      try {
        var adapter = NetworkingManager.CurrentAdapter;
        if (adapter != null && adapter.IsHost)
          adapter.BroadcastHostConfig(new HostConfig { Config = appliedCfg });
      }
      catch { }
    }

    private static MelonPreferences_Entry<int> CreateCategoryEntry(string name, int defaultValue) {
      // guard against an empty or whitespace key – these manifest as
      // blank rows in the UI and were the root of the recent bug.
      var key = name?.Trim();
      if (string.IsNullOrEmpty(key)) {
        // fall back to a single placeholder; multiple invocations here indicate
        // we received malformed input, and generating a GUID per call just
        // bloated the preferences file.  Log a stack trace to help diagnose
        // the caller.
        key = "<unknown>";
        LoggingHelper.Warning(
            $"CreateCategoryEntry called with empty name, using fallback key {key}\n" +
            Environment.StackTrace);
      }

      if (_categoryEntries.TryGetValue(key, out var existing))
        return existing;
      var entry = _prefsCategory.CreateEntry(key, defaultValue, key);
      entry.OnEntryValueChanged.Subscribe((o, n) => {
        if (!_suppressEntryEvents)
          ApplyPreferencesNow(persist: false);
      });

      _categoryEntries[key] = entry;
      return entry;
    }

    public static bool AreConfigsEqual(ModConfig? a, ModConfig? b) {
      // treat object identity and nulls first to avoid nullref exceptions
      if (ReferenceEquals(a, b))
        return true;
      if (a == null || b == null)
        return false;

      if (a.EnableServerAuthoritativeConfig != b.EnableServerAuthoritativeConfig) return false;
      if (a.MixingStationCapacity != b.MixingStationCapacity) return false;
      if (a.MixingStationSpeed != b.MixingStationSpeed) return false;
      if (a.DryingRackCapacity != b.DryingRackCapacity) return false;
      if (a.CauldronIngredientMultiplier != b.CauldronIngredientMultiplier) return false;
      if (a.CauldronCookSpeed != b.CauldronCookSpeed) return false;
      if (a.ChemistryStationSpeed != b.ChemistryStationSpeed) return false;
      if (a.LabOvenSpeed != b.LabOvenSpeed) return false;

      var da = a.CategoryMultipliers ?? new Dictionary<string, int>();
      var db = b.CategoryMultipliers ?? new Dictionary<string, int>();
      if (da.Count != db.Count) return false;
      foreach (var kv in da) {
        if (!db.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
      }
      return true;
    }

    // helpers for logging
    public static string DescribeConfig(ModConfig? cfg) {
      if (cfg == null)
        return "<null>";
      var sb = new System.Text.StringBuilder();
      sb.AppendLine($"EnableServerAuthoritativeConfig: {cfg.EnableServerAuthoritativeConfig}");
      sb.AppendLine($"MixingStationCapacity: {cfg.MixingStationCapacity}");
      sb.AppendLine($"MixingStationSpeed: {cfg.MixingStationSpeed}");
      sb.AppendLine($"DryingRackCapacity: {cfg.DryingRackCapacity}");
      sb.AppendLine($"CauldronIngredientMultiplier: {cfg.CauldronIngredientMultiplier}");
      sb.AppendLine($"CauldronCookSpeed: {cfg.CauldronCookSpeed}");
      sb.AppendLine($"ChemistryStationSpeed: {cfg.ChemistryStationSpeed}");
      sb.AppendLine($"LabOvenSpeed: {cfg.LabOvenSpeed}");
      if (cfg.CategoryMultipliers != null && cfg.CategoryMultipliers.Count > 0) {
        sb.AppendLine("CategoryMultipliers:");
        foreach (var kv in cfg.CategoryMultipliers) {
          sb.AppendLine($"  {kv.Key}: {kv.Value}");
        }
      }
      return sb.ToString();
    }

    public static string FormatConfigDiff(ModConfig? before, ModConfig? after) {
      if (ReferenceEquals(before, after))
        return "(no change)";
      if (before == null)
        return "initial configuration:\n" + DescribeConfig(after);
      if (after == null)
        return "configuration cleared";

      var sb = new System.Text.StringBuilder();
      void diff<T>(string name, T b, T a) {
        if (!EqualityComparer<T>.Default.Equals(b, a))
          sb.AppendLine($"  {name}: {b} -> {a}");
      }

      diff("EnableServerAuthoritativeConfig", before.EnableServerAuthoritativeConfig, after.EnableServerAuthoritativeConfig);
      diff("MixingStationCapacity", before.MixingStationCapacity, after.MixingStationCapacity);
      diff("MixingStationSpeed", before.MixingStationSpeed, after.MixingStationSpeed);
      diff("DryingRackCapacity", before.DryingRackCapacity, after.DryingRackCapacity);
      diff("CauldronIngredientMultiplier", before.CauldronIngredientMultiplier, after.CauldronIngredientMultiplier);
      diff("CauldronCookSpeed", before.CauldronCookSpeed, after.CauldronCookSpeed);
      diff("ChemistryStationSpeed", before.ChemistryStationSpeed, after.ChemistryStationSpeed);
      diff("LabOvenSpeed", before.LabOvenSpeed, after.LabOvenSpeed);

      // categories
      var ba = before.CategoryMultipliers ?? new Dictionary<string, int>();
      var aa = after.CategoryMultipliers ?? new Dictionary<string, int>();
      var allKeys = new HashSet<string>(ba.Keys);
      allKeys.UnionWith(aa.Keys);
      foreach (var k in allKeys) {
        bool hasB = ba.TryGetValue(k, out var bv);
        bool hasA = aa.TryGetValue(k, out var av);
        // treat an effectively-missing value the same way in both configs
        if (hasB && MultiplierIsMissing(bv)) hasB = false;
        if (hasA && MultiplierIsMissing(av)) hasA = false;

        if (!hasB && hasA) {
          sb.AppendLine($"  Category[{k}]: <added> {av}");
        } else if (hasB && !hasA) {
          sb.AppendLine($"  Category[{k}]: <removed> {bv}");
        } else if (bv != av) {
          sb.AppendLine($"  Category[{k}]: {bv} -> {av}");
        }
      }

      return sb.Length == 0 ? "(no sensible changes)" : sb.ToString();
    }

    public static void ApplyPreferencesNow(bool persist = true) {
      try {
        EnsureRegistered();

        if (!_initialized) {
          _lastAppliedPrefs = ReadFromPreferences();
          LoggingHelper.Msg("Initial preference snapshot (via ApplyPreferencesNow)", _lastAppliedPrefs);
          _initialized = true;
          return;
        }

        var current = ReadFromPreferences();

        if (_lastAppliedPrefs != null && AreConfigsEqual(_lastAppliedPrefs, current))
          return;

        // log a compact diff instead of full JSON dumps
        var diffText = FormatConfigDiff(_lastAppliedPrefs, current);
        LoggingHelper.Msg("Preference change detected:\n" + diffText);

        if (MultiplayerHelper.IsClientInMultiplayer()) {
          if (_lastAppliedPrefs != null) {
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

        try {
          var adapter = NetworkingManager.CurrentAdapter;
          if (adapter != null && adapter.IsHost)
            adapter.BroadcastHostConfig(new HostConfig { Config = current });
        }
        catch { }

        LoggingHelper.Msg("Preferences changed — applied live.");
      }
      catch (Exception ex) {
        LoggingHelper.Warning($"ApplyPreferencesNow failed: {ex.Message}");
      }
    }

    // multiplier helpers --------------------------------------------------
    // these encapsulate the business rule that any multiplier less than 1 is
    // considered equivalent to "absent" when reading or diffing.  centralizing
    // the logic here prevents the two call sites from drifting apart.

    private static int NormalizeMultiplier(int value) => value < 1 ? 1 : value;
    private static bool MultiplierIsMissing(int value) => value < 1;

    public static int? GetSavedOriginalStackLimit(string defId) => OriginalStackTracker.GetSavedOriginalStackLimit(defId);
    public static void PersistOriginalStackLimit(string defId, int originalLimit) => OriginalStackTracker.PersistOriginalStackLimit(defId, originalLimit);
    public static bool HasSavedOriginals() => OriginalStackTracker.HasSavedOriginals();
  }
}
