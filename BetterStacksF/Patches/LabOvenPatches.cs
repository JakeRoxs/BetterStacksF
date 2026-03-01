using System;
using System.Collections.Generic;
using System.Reflection;

using BetterStacksF;
using BetterStacksF.Utilities;

namespace BetterStacksF.Patches {
  /// <summary>
  /// Speed modifier for lab oven operations.  Rather than altering the
  /// minute tick we scale the operation's recipe duration when it is
  /// created (locally or via network) so the timer and UI naturally reflect
  /// the faster/slower rate.
  /// </summary>
  public static class LabOvenPatches {
    // tracking scaled operations (avoid shrinking same object multiple times)
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> _scaledOps
        = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();

    // when running in DEBUG we dump field/property lists so we can discover
    // where internal timers live.  doing this every CheckProgress invocation
    // was extremely expensive; mirror the chemistry station patch and keep a
    // set of already‑logged types so we only produce output once per type.
    private static readonly System.Collections.Generic.HashSet<Type> _loggedTypes
        = new System.Collections.Generic.HashSet<Type>();

    // debug helper: print all fields/properties of an operation type once so
    // we can identify the relevant duration field without flooding the log.
    // The method is always compiled but callers should check
    // <see cref="LoggingHelper.EnableVerbose"/> to avoid unnecessary work.
    private static void DumpOperation(object op) {
      if (op == null) return;
      var type = op.GetType();
      if (_loggedTypes.Contains(type)) return;
      _loggedTypes.Add(type);

      LoggingHelper.Msg($"LabOven operation type {type.FullName} fields/properties:");
      foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
        try { LoggingHelper.Msg($" field {f.Name} ({f.FieldType.Name}) = {f.GetValue(op)}"); } catch { }
      }
      foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
        if (!p.CanRead) continue;
        try { LoggingHelper.Msg($" prop {p.Name} ({p.PropertyType.Name}) = {p.GetValue(op)}"); } catch { }
      }
    }

    // apply multiplier when an oven operation is created (local or via RPC)
    private static void ScaleOperation(dynamic op) {
      try {
        if (op == null) return;
        if (_scaledOps.TryGetValue((object)op, out _))
          return; // already scaled

        int speed = BetterStacksFMod.CurrentConfig.LabOvenSpeed;
        if (speed <= 1) return;

        // the oven operation stores a simple duration field rather than a
        // Recipe object like the chemistry station.  use the `cookDuration`
        // property (an int) and adjust it directly.
        // dump fields/properties of the operation so we can see where the
        // true duration lives.  this will log a bunch of data on first call.
        if (LoggingHelper.EnableVerbose) {
          try {
            DumpOperation((object)op);
          }
          catch (Exception e) {
            LoggingHelper.Error("LabOvenPatches introspect failed", e);
          }
        }

        int time = (int)op.cookDuration;
        if (time <= 0) {
          // the operation hasn't had its duration initialized yet; ask the
          // object to compute it.  this avoids clobbering a valid value when
          // cookDuration is set to -1 as a sentinel.
          try {
            time = (int)op.GetCookDuration();
            LoggingHelper.Msg($"LabOven ScaleOperation fetched duration via GetCookDuration: {time}");
          }
          catch (Exception e) {
            LoggingHelper.Error("LabOven ScaleOperation failed to call GetCookDuration", e);
          }
        }
        if (time <= 0) // still nonsense, ensure positive to avoid infinite loops
        {
          time = 1;
        }

        // log the raw values so the user can see what's happening; many oven
        // recipes have a very short base duration (often 1), meaning a high
        // multiplier cannot make them any smaller.  this message will show
        // the incoming time, the configured speed, and the computed result.
        LoggingHelper.Msg($"LabOven ScaleOperation invoked: originalTime={time}, speed={speed}");

        // calculate a new cook time by dividing by the speed multiplier;
        // the previous implementation skipped scaling when the original
        // duration was shorter than the configured speed, which meant that
        // high values (e.g. 80) had no effect on short recipes.  we always
        // perform the division and then clamp to a minimum of one minute.
        // divide by the multiplier but keep the result in the valid
        // range; it should never exceed the original value, but clamping
        // makes the function resilient to unexpected config values (for
        // example if speed were somehow set to 0 or negative).  we also force
        // a minimum of 1 so the game’s timer never hits zero.
        int scaled = time / speed;
        if (scaled < 1) scaled = 1;
        if (scaled > time) scaled = time;

        LoggingHelper.Msg($"LabOven ScaleOperation computed scaled={scaled}");

        if (scaled != time) {
          op.cookDuration = scaled;
          _scaledOps.Add((object)op, new object());
          LoggingHelper.Msg($"LabOven operation scaled from {time} to {scaled} (speed {speed})");
        }
      }
      catch (Exception ex) {
        LoggingHelper.Error("LabOvenPatches.ScaleOperation failed", ex);
      }
    }

    // operation hooks
    public static bool Prefix_SendCookOperation(dynamic __instance, dynamic operation) {
      ScaleOperation(operation);
      return true;
    }

    public static bool Prefix_SetCookOperation(dynamic __instance, dynamic conn, dynamic operation) {
      ScaleOperation(operation);
      return true;
    }

    // LabOven operations are scaled by ScaleOperation, which adjusts the
    // operation's cookDuration based on the configured speed multiplier.
    // ScaleOperation is invoked from the Harmony prefixes
    // Prefix_SendCookOperation (when the local oven sends a cook operation)
    // and Prefix_SetCookOperation (when an operation is received/applied
    // over the network), so scaling always occurs at operation creation time.
  }
}
