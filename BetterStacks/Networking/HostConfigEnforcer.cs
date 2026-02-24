using MelonLoader;
using BetterStacks.Config;
using BetterStacks.Utilities;

namespace BetterStacks.Networking
{
    // Applies HostConfig on clients and exposes helper for persistence decisions.
    public static class HostConfigEnforcer
    {
        public static void ApplyHostConfig(HostConfig hostConfig)
        {
            if (hostConfig?.Config == null)
                return;

            // Update internal config and re-apply authoritative overrides.
            ConfigManager.EnqueueConfigUpdate(hostConfig.Config);

            // Toggle runtime flag so save/persistence logic knows host-authoritative state.
            BetterStacksMod.ServerAuthoritative = hostConfig.Config.EnableServerAuthoritativeConfig;

            LoggingHelper.Msg($"HostConfig applied; ServerAuthoritative={BetterStacksMod.ServerAuthoritative}");
        }

        public static bool ShouldAllowPersist(ModConfig attempted)
        {
            // if host isn't enforcing authority, the client may save whatever it likes
            if (!BetterStacksMod.ServerAuthoritative)
                return true;

            // while authoritative the host’s setting is the only valid one – clients
            // shouldn't be able to persist a different value at all.
            return attempted?.EnableServerAuthoritativeConfig ==
                   BetterStacksMod.ServerAuthoritativeEnabled;
        }
    }
}