using System.Collections.Generic;

namespace BetterStacks.Config
{
    public class ModConfig
    {
        // Primary dynamic category multipliers keyed by EItemCategory name.
        public Dictionary<string, int> CategoryMultipliers { get; set; } = new Dictionary<string, int>();

        // When true the host may assert authoritative control over this mod's config via HostConfig messages.
        public bool EnableServerAuthoritativeConfig { get; set; } = false;

        public int MixingStationCapacity { get; set; } = 1;
        public int MixingStationSpeed { get; set; } = 3;

        public int DryingRackCapacity { get; set; } = 1;
    }
}