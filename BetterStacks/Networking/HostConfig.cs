using BetterStacks;

namespace BetterStacks.Networking
{
    // DTO sent from host -> clients containing authoritative mod values.
    public class HostConfig
    {
        public ModConfig Config { get; set; } = new ModConfig();
    }
}