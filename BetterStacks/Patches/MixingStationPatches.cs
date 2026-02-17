using System;
using MelonLoader;
using BetterStacks;

namespace BetterStacks.Patches
{
    public static class MixingStationPatches
    {
        public static bool PatchMixingStationCapacity(dynamic __instance)
        {
            __instance.MixTimePerItem /= BetterStacksMod.CurrentConfig.MixingStationSpeed;
            __instance.MixTimePerItem = Math.Max(1, __instance.MixTimePerItem);
            __instance.MaxMixQuantity = __instance.MaxMixQuantity * BetterStacksMod.CurrentConfig.MixingStationCapacity;
            return true;
        }
    }
}