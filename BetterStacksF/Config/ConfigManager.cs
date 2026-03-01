using System.Collections.Generic;

using BetterStacksF.Utilities;


namespace BetterStacksF.Config {
  /// <summary>
  /// Central manager for the mod configuration and incoming updates.
  /// Delays application of configurations to the main thread and forwards
  /// them to <see cref="StackOverrideManager"/>.
  /// </summary>
  internal static class ConfigManager {
    private static ModConfig _config = new ModConfig();
    private static bool _loggedInitialLoad = false; // prevent duplicate logging
    private static readonly Queue<ModConfig> _pendingConfigs = new Queue<ModConfig>();
    // cache the most-recently enqueued config to avoid iterating the queue
    private static ModConfig? _lastPendingConfig;
    private static readonly object _lock = new object();

    public static ModConfig CurrentConfig => _config; // kept for consumers; _config updated on load/update

    public static ModConfig LoadConfig() {
      // reads the preferences file, sanitizes the result and updates the
      // manager's cache.  callers are responsible for any additional
      // validation/initialisation (e.g. category sync).
      //
      // This method can be invoked from arbitrary threads (network callbacks,
      // UI events, etc.), so it takes the same lock used by the queue helpers
      // to avoid races against EnqueueConfigUpdate/ProcessPendingUpdates.
      PreferencesMapper.EnsureRegistered();
      // after registration we may already have a cached copy from the
      // initial ReadFromPreferences call; use it to avoid duplicating work.
      var cfg = PreferencesMapper.GetCachedPreferences() ?? PreferencesMapper.ReadFromPreferences();
      // sanitize any strange keys that may have made it into prefs; this
      // also handles the case where the ModsApp UI has gone wrong.
      PreferencesMapper.SanitizeCategoryKeys(cfg);

      // push the values we just loaded back into the preference entries.
      // this guarantees that manual edits (parsed in ReadFromPreferences) are
      // reflected in MelonPreferences and therefore won't be lost by later
      // registration logic which creates its own defaults.
      PreferencesMapper.ApplyConfigToEntries(cfg);

      lock (_lock) {
        try {
          // update our working copy immediately so callers looking at
          // CurrentConfig see the right data.
          _config = cfg;

          // mirror verbose logging flag to the helper so it takes effect
          LoggingHelper.EnableVerbose = cfg.EnableVerboseLogging;

          // log once only; subsequent calls may return the same object
          if (!_loggedInitialLoad) {
            LoggingHelper.Init("Configuration loaded:\n" + PreferencesMapper.DescribeConfig(cfg));
            _loggedInitialLoad = true;
          }

          return cfg;
        }
        catch (Exception ex) {
          var fallbackConfig = new ModConfig();
          _config = fallbackConfig;
          LoggingHelper.EnableVerbose = fallbackConfig.EnableVerboseLogging;
          // include exception text so failures are diagnosable
          LoggingHelper.Warning($"Configuration load failed, using defaults: {ex}", fallbackConfig);
          return fallbackConfig;
        }
      }
    }

    public static void EnqueueConfigUpdate(ModConfig cfg) {
      if (cfg == null) return;
      lock (_lock) {
        if (PreferencesMapper.AreConfigsEqual(_config, cfg))
          return;

        // compare with last queued config if present
        if (_lastPendingConfig != null && PreferencesMapper.AreConfigsEqual(_lastPendingConfig, cfg))
          return;

        _pendingConfigs.Enqueue(cfg);
        _lastPendingConfig = cfg;
      }
    }

    /// <summary>
    /// Must be called on the main thread (e.g. from <c>MelonMod.OnUpdate</c>).
    /// Applies any queued configurations by updating the active config and
    /// invoking the override manager.
    /// </summary>
    public static void ProcessPendingUpdates() {
      while (true) {
        ModConfig? next = null;
        lock (_lock) {
          if (_pendingConfigs.Count > 0)
            next = _pendingConfigs.Dequeue();
        }

        if (next == null)
          break;

        _config = next;
        // preserve verbose logging setting
        LoggingHelper.EnableVerbose = next.EnableVerboseLogging;

        StackOverrideManager.ApplyStackOverrides(next);
      }
    }
  }
}
