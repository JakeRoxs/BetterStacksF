using BetterStacks;

namespace BetterStacks.Patches {
  public static class DryingRackPatches {
    public static void PatchDryingRackCapacity(dynamic __instance, dynamic rack, bool open) {
      if (rack is not null) {
        int desired = BetterStacksMod.CurrentConfig.DryingRackCapacity * 20;
        if (rack.ItemCapacity != desired)
          rack.ItemCapacity = desired;
      }
    }
  }
}
