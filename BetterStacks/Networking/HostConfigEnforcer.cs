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
            BetterStacksMod.EnqueueConfigUpdate(hostConfig.Config);

            // Toggle runtime flag so save/persistence logic knows host-authoritative state.
            BetterStacksMod.ServerAuthoritative = hostConfig.Config.EnableServerAuthoritativeConfig;

            LoggingHelper.Msg($"HostConfig applied; ServerAuthoritative={BetterStacksMod.ServerAuthoritative}");
        }

        public static bool ShouldAllowPersist(ModConfig attempted)
        {
            if (!BetterStacksMod.ServerAuthoritative)
                return true;

            // Block client-side persistence when the host declared server-authoritative.
            return !(attempted?.EnableServerAuthoritativeConfig == true);
        }
    }
}