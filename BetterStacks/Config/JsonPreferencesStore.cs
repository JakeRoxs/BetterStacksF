using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using MelonLoader;
using Newtonsoft.Json;
using BetterStacks.Utilities;

namespace BetterStacks.Config
{
    /// <summary>
    /// Encapsulates JSON-based preferences storage and optional hot-reload via FileSystemWatcher.
    /// </summary>
    internal static class JsonPreferencesStore
    {
        private const string JsonFileName = "BetterStacks.json";
        // debounce interval to prevent duplicate watcher events flooding the callback
        private const int WatcherDebounceMilliseconds = 100;

        private static FileSystemWatcher? _jsonWatcher;
        private static DateTime _lastWatcherEventUtc = DateTime.MinValue;
        private static bool _suppressWatcherEvent = false;

        public static void Initialize(Action onExternalChange)
        {
            var userData = GetUserDataDirectory();
            Directory.CreateDirectory(userData);
            var path = Path.Combine(userData, JsonFileName);

            if (!File.Exists(path))
            {
                try { Write(new ModConfig()); LoggingHelper.Msg("Created default JSON config: UserData/BetterStacks.json"); } catch { }
            }

            // Setup watcher once
            if (_jsonWatcher == null)
            {
                try
                {
                    _jsonWatcher = new FileSystemWatcher(userData)
                    {
                        Filter = JsonFileName,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                        EnableRaisingEvents = true
                    };
                    _jsonWatcher.Changed += (s, e) =>
                    {
                        try
                        {
                            if (_suppressWatcherEvent) return;
                            var now = DateTime.UtcNow;
                            if ((now - _lastWatcherEventUtc).TotalMilliseconds < WatcherDebounceMilliseconds) return;
                            _lastWatcherEventUtc = now;
                            LoggingHelper.Msg($"Detected external JSON preference change: {e.Name}");
                            onExternalChange?.Invoke();
                        }
                        catch { }
                    };
                }
                catch { }
            }
        }

        public static ModConfig Read()
        {
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
                LoggingHelper.Warning($"Failed to read JSON config: {ex.Message}");
                return new ModConfig();
            }
        }

        public static void Write(ModConfig cfg)
        {
            // clients should not be allowed to write; caller must guard
            try
            {
                var path = Path.Combine(GetUserDataDirectory(), JsonFileName);
                var json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                _suppressWatcherEvent = true;
                try { File.WriteAllText(path, json); }
                finally { _suppressWatcherEvent = false; }
                _lastWatcherEventUtc = DateTime.UtcNow;
                LoggingHelper.Msg("Wrote JSON preferences to UserData/BetterStacks.json");
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning($"Failed to write JSON preferences: {ex.Message}");
            }
        }


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
    }
}