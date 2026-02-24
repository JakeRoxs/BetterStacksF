using System.Collections.Generic;
using BetterStacks.Networking;

namespace BetterStacks.Config
{
    /// <summary>
    /// Central manager for the mod configuration and incoming updates.
    /// Delays application of configurations to the main thread and forwards
    /// them to <see cref="StackOverrideManager"/>.
    /// </summary>
    internal static class ConfigManager
    {
        private static ModConfig _config = new ModConfig();
        private static readonly Queue<ModConfig> _pendingConfigs = new Queue<ModConfig>();
        // cache the most-recently enqueued config to avoid iterating the queue
        private static ModConfig? _lastPendingConfig;
        private static readonly object _lock = new object();

        public static ModConfig CurrentConfig => _config;

        public static ModConfig LoadConfig()
        {
            try
            {
                PreferencesMapper.EnsureRegistered();
                var cfg = PreferencesMapper.ReadFromPreferences();
                // ensure the helper in BetterStacksMod still runs (used by other code);
                // can be phased out once all callers are updated
                BetterStacksMod.EnsureCategoryMultipliers(cfg, addEnumKeys: true);
                return cfg;
            }
            catch
            {
                var cfg = new ModConfig();
                BetterStacksMod.EnsureCategoryMultipliers(cfg, addEnumKeys: true);
                return cfg;
            }
        }

        public static void EnqueueConfigUpdate(ModConfig cfg)
        {
            if (cfg == null) return;
            lock (_lock)
            {
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
        public static void ProcessPendingUpdates()
        {
            while (true)
            {
                ModConfig? next = null;
                lock (_lock)
                {
                    if (_pendingConfigs.Count > 0)
                        next = _pendingConfigs.Dequeue();
                }

                if (next == null)
                    break;

                _config = next;
                StackOverrideManager.ApplyStackOverrides(next);
            }
        }
    }
}