using BetterStacksF;
using BetterStacksF.Utilities;

namespace BetterStacksF.Patches {
  public static class DryingRackPatches {
    public static void PatchDryingRackCapacity(dynamic __instance, dynamic rack, bool open) {
      if (rack is null) return;
      ReflectionHelper.TryCatchLog(() => {
        int desired = BetterStacksFMod.CurrentConfig.DryingRackCapacity * 20;
        if (rack.ItemCapacity != desired) {
          LoggingHelper.Msg($"DryingRack capacity adjusting from {rack.ItemCapacity} to {desired}");
          rack.ItemCapacity = desired;
        }
      }, "DryingRackPatches.PatchDryingRackCapacity failed");
    }
  }
}
