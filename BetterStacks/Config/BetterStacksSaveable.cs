using S1API.Internal.Abstraction;
using S1API.Saveables;
using BetterStacks.Networking;
using MelonLoader;

namespace BetterStacks.Config
{
    /// <summary>
    /// Per-save persistence for BetterStacks config.
    /// Applies the saved config when a save is loaded.
    /// </summary>
    public class BetterStacksSaveable : Saveable
    {
        private static BetterStacksSaveable? _instance;

        [SaveableField("BetterStacks.Save.json")]
        private ModConfig _saveConfig = new ModConfig();

        public BetterStacksSaveable()
        {
            _instance = this;
        }

        public static ModConfig EffectiveSaveConfig => _instance?._saveConfig ?? new ModConfig();

        protected override void OnLoaded()
        {
            // Apply per-save overrides (host authoritative behavior should broadcast separately)
            BetterStacksMod.ApplyStackOverridesUsing(_saveConfig);
        }

        protected override void OnSaved()
        {
            // nothing special right now
        }

        public void UpdateAndPersist(ModConfig cfg)
        {
            // Reject null input early to satisfy nullable analysis.
            if (cfg == null) return;

            // If server-authoritative mode is active and this client is NOT the host,
            // prevent persisting host-enforced settings.
            if (BetterStacksMod.ServerAuthoritative && NetworkingManager.CurrentAdapter is not null && !NetworkingManager.CurrentAdapter.IsHost && cfg.EnableServerAuthoritativeConfig == true)
            {
                MelonLogger.Msg("[Better Stacks] Server-authoritative mode active — client save prevented.");
                return;
            }

            _saveConfig = cfg;
            Saveable.RequestGameSave();
        }
    }
}