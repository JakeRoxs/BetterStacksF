using System;

using BetterStacksF;
using BetterStacksF.Utilities;

namespace BetterStacksF.Patches {
  public static class MixingStationPatches {
    public static bool PatchMixingStationCapacity(dynamic __instance) {
      ReflectionHelper.TryCatchLog(() => {
        __instance.MixTimePerItem /= BetterStacksFMod.CurrentConfig.MixingStationSpeed;
        __instance.MixTimePerItem = Math.Max(1, __instance.MixTimePerItem);
        __instance.MaxMixQuantity = __instance.MaxMixQuantity * BetterStacksFMod.CurrentConfig.MixingStationCapacity;
        if (LoggingHelper.EnableVerbose) {
          LoggingHelper.Msg("MixingStation capacity patch applied");
        }
      }, "MixingStationPatches.PatchMixingStationCapacity failed");

      return true;
    }
  }
}
