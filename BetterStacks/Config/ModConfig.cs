using System.Collections.Generic;

namespace BetterStacks.Config
{
    public class ModConfig
    {
        // Primary dynamic category multipliers keyed by EItemCategory name.
        public Dictionary<string, int> CategoryMultipliers { get; set; } = new Dictionary<string, int>
        {
            ["Product"] = 3,
            ["Packaging"] = 3,
            ["Agriculture"] = 3,
            ["Ingredient"] = 3
        };

        // When true the host may assert authoritative control over this mod's config via HostConfig messages.
        public bool EnableServerAuthoritativeConfig { get; set; } = true;

        public int MixingStationCapacity { get; set; } = 1;
        public int MixingStationSpeed { get; set; } = 1;
        public int DryingRackCapacity { get; set; } = 1;
    }
}