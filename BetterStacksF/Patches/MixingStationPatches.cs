using System;

using BetterStacksF;

namespace BetterStacksF.Patches {
  public static class MixingStationPatches {
    public static bool PatchMixingStationCapacity(dynamic __instance) {
      __instance.MixTimePerItem /= BetterStacksFMod.CurrentConfig.MixingStationSpeed;
      __instance.MixTimePerItem = Math.Max(1, __instance.MixTimePerItem);
      __instance.MaxMixQuantity = __instance.MaxMixQuantity * BetterStacksFMod.CurrentConfig.MixingStationCapacity;
      return true;
    }
  }
}
