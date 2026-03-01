using System.Collections.Generic;

namespace BetterStacksF.Config {
  public class ModConfig {
    // Primary dynamic category multipliers keyed by EItemCategory name.
    public Dictionary<string, int> CategoryMultipliers { get; set; } = new Dictionary<string, int> {
      ["Product"] = 3,
      ["Packaging"] = 3,
      ["Agriculture"] = 3,
      ["Ingredient"] = 3
    };

    // When true the host may assert authoritative control over this mod's config via HostConfig messages.
    public bool EnableServerAuthoritativeConfig { get; set; } = true;

    // Enable extra verbose logging (Debug-style messages) from the mod.  The
    // checkbox appears in the MelonPreferences UI and defaults to `true` in
    // DEBUG builds so developers get output without needing to flip the option.
    // Release builds default to `false`.
    public bool EnableVerboseLogging { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    public int MixingStationCapacity { get; set; } = 3;
    public int MixingStationSpeed { get; set; } = 1;
    public int DryingRackCapacity { get; set; } = 3;
    public int CauldronIngredientMultiplier { get; set; } = 1;
    public int CauldronCookSpeed { get; set; } = 1;
    public int ChemistryStationSpeed { get; set; } = 1;
    public int LabOvenSpeed { get; set; } = 1;
  }
}
