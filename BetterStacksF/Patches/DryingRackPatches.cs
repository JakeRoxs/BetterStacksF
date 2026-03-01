using BetterStacksF;

namespace BetterStacksF.Patches {
  public static class DryingRackPatches {
    public static void PatchDryingRackCapacity(dynamic __instance, dynamic rack, bool open) {
      if (rack is not null) {
        int desired = BetterStacksFMod.CurrentConfig.DryingRackCapacity * 20;
        if (rack.ItemCapacity != desired)
          rack.ItemCapacity = desired;
      }
    }
  }
}
