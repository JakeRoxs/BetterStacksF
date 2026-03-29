using System;
using BetterStacksF.Utilities;
using HarmonyLib;

namespace BetterStacksF.Patches {
  internal static class CashPatches {
    private static float GetCashStackLimit() {
      int cashModifier = BetterStacksFMod.GetModifierForCategory(BetterStacksFMod.CurrentConfig, S1API.Items.ItemCategory.Cash);
      // The base cash chunk in gameplay is 1000; multiply this by the category modifier.
      return Math.Max(1, cashModifier) * 1000f;
    }

    public static bool Prefix_AddCash(Il2CppScheduleOne.NPCs.NPCInventory __instance, float amountToAdd) {
      if (amountToAdd <= 0f)
        return false; // nothing to do

      int cashModifier = BetterStacksFMod.GetModifierForCategory(BetterStacksFMod.CurrentConfig, S1API.Items.ItemCategory.Cash);
      if (cashModifier <= 1)
        return true; // preserve default behavior when unchanged

      float allowedStack = GetCashStackLimit();
      float remaining = amountToAdd;
      bool insertedAny = false;

      while (remaining > 0f) {
        float chunk = Math.Min(remaining, allowedStack);

        var cashItem = Il2CppScheduleOne.Money.MoneyManager.Instance?.GetCashInstance(chunk);
        if (cashItem == null) {
          LoggingHelper.Warning("Failed to create cash instance in AddCash patch; aborting remaining cash insertion.");
          break;
        }

        // Force cash instance balance to conform to the modified limit.
        try {
          cashItem.SetBalance(Math.Min(chunk, allowedStack), true);
        }
        catch {
          // fallback: if method isn't available, continue with the created chunk
        }

        if (!__instance.CanItemFit(cashItem)) {
          break;
        }

        __instance.InsertItem(cashItem, true);
        insertedAny = true;
        remaining -= chunk;
      }

      // Only skip the original AddCash when we've consumed the full amount.
      if (insertedAny && remaining <= 0f)
        return false;

      // Allow the original method to handle any leftover amount or overflow.
      return true;
    }

    public static bool Prefix_CashInstance_SetBalance(Il2CppScheduleOne.ItemFramework.CashInstance __instance, ref float newBalance, bool blockClear) {
      float limit = GetCashStackLimit();
      if (newBalance > limit)
        newBalance = limit;
      if (newBalance < 0f)
        newBalance = 0f;
      return true;
    }

    public static bool Prefix_CashInstance_CanStackWith(Il2CppScheduleOne.ItemFramework.CashInstance __instance,
                                                          Il2CppScheduleOne.ItemFramework.ItemInstance other,
                                                          bool checkQuantities,
                                                          ref bool __result) {
      if (!checkQuantities)
        return true;

      if (other == null)
        return false;

      var otherCash = other as Il2CppScheduleOne.ItemFramework.CashInstance;
      if (otherCash == null)
        return true;

      float limit = GetCashStackLimit();
      float total = __instance.GetTotalAmount() + otherCash.GetTotalAmount();
      __result = total <= limit;
      return false;
    }

    public static bool Prefix_CashInstance_CanStackWithBase(Il2CppScheduleOne.ItemFramework.CashInstance __instance,
                                                              Il2CppScheduleOne.Core.Items.Framework.BaseItemInstance other,
                                                              bool checkQuantities,
                                                              ref bool __result) {
      if (!checkQuantities)
        return true;

      if (other == null)
        return false;

      var otherCash = other as Il2CppScheduleOne.ItemFramework.CashInstance;
      if (otherCash == null)
        return true;

      float limit = GetCashStackLimit();
      float total = __instance.GetTotalAmount() + otherCash.GetTotalAmount();
      __result = total <= limit;
      return false;
    }

    public static void Postfix_GetCashInstance(Il2CppScheduleOne.ItemFramework.CashInstance __result, float amount) {
      if (__result == null)
        return;

      float limit = GetCashStackLimit();
      if (__result.GetTotalAmount() > limit) {
        __result.SetBalance(limit, true);
      }
    }
  }
}
