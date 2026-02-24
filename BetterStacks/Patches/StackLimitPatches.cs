using BetterStacks.Utilities;

using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;

namespace BetterStacks.Patches {
  /// <summary>
  /// Harmony prefixes applied to <see cref="ItemInstance" /> quantity setters.
  /// These patches enforce the runtime stack limit (which may be modified by the
  /// mod) and log any attempts to drive the quantity negative.  They replace the
  /// original methods so the mod can clamp values without altering game code.
  /// </summary>
  internal static class StackLimitPatches {
    /// <summary>
    /// Prefix for <c>ItemInstance.SetQuantity</c>.
    /// Ensures <paramref name="quantity"/> is non-negative and does not exceed
    /// the instance's <see cref="ItemInstance.StackLimit"/>, then applies it.
    /// </summary>
    public static bool SetQuantityPrefix(ItemInstance __instance, int quantity) {
      if (quantity < 0) {
        LoggingHelper.Error("SetQuantity called with negative quantity");
        return false;
      }
      int limit = __instance.StackLimit;
      if (quantity > limit)
        quantity = limit;
      __instance.Quantity = quantity;
      __instance.InvokeDataChange();
      return false;
    }

    /// <summary>
    /// Prefix for <c>ItemInstance.ChangeQuantity</c>.
    /// Adjusts the change so the resulting quantity stays within [0, StackLimit]
    /// and logs if the computation would go negative.  The patched method never
    /// executes; we perform the update ourselves and cancel the original.
    /// </summary>
    public static bool ChangeQuantityPrefix(ItemInstance __instance, int change) {
      int num = __instance.Quantity + change;
      if (num < 0) {
        LoggingHelper.Error("ChangeQuantity resulted in negative quantity");
        return false;
      }

      if (change > 0) {
        int limit = __instance.StackLimit;
        if (num > limit)
          num = limit;
      }

      __instance.Quantity = num;
      __instance.InvokeDataChange();
      return false;
    }
  }
}
