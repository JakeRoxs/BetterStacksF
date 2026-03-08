using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using BetterStacksF;
using BetterStacksF.Utilities;

namespace BetterStacksF.Patches {
  /// <summary>
  /// Harmony patches for cauldron behaviour.  We need to intercept both the
  /// vanilla methods and the network RPC helpers; the game doesn’t simply call
  /// the base method when doing an RPC so a single patch would miss the remote
  /// code path.
  ///
  /// * StartCookOperation(...) is called when the user begins a recipe.  The
  ///   first parameter contains the remaining cook time; we divide that by the
  ///   configured <see cref="ModConfig.CauldronCookSpeed"/> so that the game’s
  ///   built‑in timer runs faster/slower.  Only this path is currently
  ///   intercepted – the previously planned output‑multiplier in
  ///   <c>FinishCookOperation</c> was never implemented and the patch has been
  ///   removed.
  ///
  /// Patching order/triggering: the prefix runs before the original method; we
  /// don’t cancel the call so the normal behaviour proceeds with the possibly
  /// modified timer.
  /// </summary>
  public static class CauldronPatches {
    public static void Prefix_StartCookOperation(dynamic __instance,
                                                dynamic conn,
                                                ref int remainingCookTime,
                                                dynamic quality) {
      int localTime = remainingCookTime;
      ReflectionHelper.TryCatchLog(() => {
        int speed = BetterStacksFMod.CurrentConfig.CauldronCookSpeed;
        if (speed > 1) {
          // divide the incoming timer value and ensure we don’t hit zero;
          // the game expects a positive number here.
          localTime = Math.Max(1, localTime / speed);
        }
      }, "CauldronPatches.Prefix_StartCookOperation failed");
      remainingCookTime = localTime;
    }

    // ---------------------------------------------------------------------
    // Flexible consumption patch.  The behaviour is defined in
    // docs/cauldron-consumption.md; the cauldron may draw from all four
    // coca slots and consume anywhere from 20 up to 20×multiplier units per
    // cook.  This implementation is entirely reflection‑driven so that it
    // continues to work even if the underlying game classes change slightly.
    //
    // Two ConditionalWeakTable caches remember the relevant FieldInfo
    // instances for the lifetime of the objects, avoiding repeated calls to
    // Type.GetField.  A lightweight DumpObject helper can be enabled during
    // development via <see cref="LoggingHelper.EnableVerbose"/> to inspect
    // the shape of the dynamic objects we receive from the game.

    private static readonly ConditionalWeakTable<object, FieldInfo?> _cauldronFieldCache
        = new ConditionalWeakTable<object, FieldInfo?>();
    private static readonly ConditionalWeakTable<object, FieldInfo?> _cauldronSlotsCache
        = new ConditionalWeakTable<object, FieldInfo?>();

    private static Array? GetSlotsArray(object cauldron) {
      // reuse generic helper – the caller already handles null cases.
      return ReflectionHelper.GetFieldValueCached<Array>(cauldron, "m_slots", _cauldronSlotsCache);
    }

    private static object? GetCauldronFromOp(object op) {
      // we don't know the type ahead of time so use object return
      return ReflectionHelper.GetFieldValueCached<object>(op, "m_cauldron", _cauldronFieldCache);
    }

    private static int CalculateUsable(int totalInput, int multiplier)
    {
        int pairable = totalInput & ~1; // force even
        int maxInput = 20 * multiplier;
        return Math.Min(pairable, maxInput);
    }

    // helpers ----------------------------------------------------------------
    // read the `count` value from a slot object using dynamic/reflection.
    // returns 0 on any failure; logs once per unique exception to avoid spam.
    private static readonly HashSet<string> _slotCountErrors = new HashSet<string>();
    private static int GetSlotCount(object? slot)
    {
        if (slot == null) return 0;
        try {
            return (int)((dynamic)slot).count;
        }
        catch (Exception ex) {
            // attempt reflection fallback
            try {
                var t = slot.GetType();
                var f = t.GetField("count",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return Convert.ToInt32(f.GetValue(slot));
                var p = t.GetProperty("count",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead)
                    return Convert.ToInt32(p.GetValue(slot));
            }
            catch { /* ignore */ }

            var key = ex.GetType().Name + ":" + ex.Message;
            if (_slotCountErrors.Add(key)) {
                LoggingHelper.Warning($"GetSlotCount failed on type {slot.GetType().Name}: {ex.Message}");
            }
            return 0;
        }
    }

    private static void ApplyFlexibleConsumption(dynamic operation) {
      try {
        if (operation == null) return;

        int multiplier = Math.Max(1, BetterStacksFMod.CurrentConfig.CauldronIngredientMultiplier);

        var cauldron = GetCauldronFromOp((object)operation);
        if (cauldron == null) return;

        var slots = GetSlotsArray(cauldron);
        if (slots == null) return;

        if (LoggingHelper.EnableVerbose) ReflectionHelper.DumpObject(cauldron, "cauldron: ");

        var slotsNonNull = slots;
        int totalInput = slotsNonNull.Cast<object?>()
                              .Select(GetSlotCount)
                              .Sum();

        int usable = CalculateUsable(totalInput, multiplier);
        if (usable < 20) return;

        int remaining = usable;
        for (int idx = slotsNonNull.Length - 1; idx >= 0 && remaining > 0; idx--) {
          object? maybeStack = slotsNonNull.GetValue(idx);
          if (maybeStack == null) continue;
          dynamic stack = maybeStack;
          int take = Math.Min((int)stack.count, remaining);
          stack.count -= take;
          remaining -= take;
        }

        operation.m_ingredientCount = usable;
        operation.m_resultCount = usable / 2;
      }
      catch (Exception ex) {
        LoggingHelper.Error("ApplyFlexibleConsumption failed", ex);
      }
    }

    public static void Prefix_FinishCookOperation(dynamic __instance,
                                                  dynamic conn,
                                                  ref dynamic operation) {
      dynamic op = operation;
      ReflectionHelper.TryCatchLog(() => ApplyFlexibleConsumption(op),
           "CauldronPatches.Prefix_FinishCookOperation failed");
      operation = op;
    }

    public static void Prefix_SendCookOperation(dynamic __instance,
                                                dynamic conn,
                                                ref dynamic operation) {
      dynamic op = operation;
      ReflectionHelper.TryCatchLog(() => ApplyFlexibleConsumption(op),
           "CauldronPatches.Prefix_SendCookOperation failed");
      operation = op;
    }

    public static void Prefix_SetCookOperation(dynamic __instance,
                                               dynamic conn,
                                               ref dynamic operation) {
      dynamic op = operation;
      ReflectionHelper.TryCatchLog(() => ApplyFlexibleConsumption(op),
           "CauldronPatches.Prefix_SetCookOperation failed");
      operation = op;
    }
  }
}

