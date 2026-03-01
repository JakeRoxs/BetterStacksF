using System;

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
      try {
        int speed = BetterStacksFMod.CurrentConfig.CauldronCookSpeed;
        if (speed > 1) {
          // divide the incoming timer value and ensure we don’t hit zero;
          // the game expects a positive number here.
          remainingCookTime = Math.Max(1, remainingCookTime / speed);
        }
      }
      catch (Exception ex) {
        LoggingHelper.Error("CauldronPatches.Prefix_StartCookOperation failed", ex);
      }
    }

    // Currently we only modify the cooking *duration* via the prefix above.
    // The ingredient‑output multiplier that was contemplated in the config
    // remains unimplemented; when/if we revisit that feature the logic can
    // either live here or in a new patch.  For now we do not patch
    // FinishCookOperation at all, so this class contains only the active
    // prefix.
  }
}
