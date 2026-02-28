using System;
using System.Collections.Generic;
using System.Reflection;

using BetterStacks;
using BetterStacks.Utilities;

namespace BetterStacks.Patches {
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

#if DEBUG
    // debug helper: print all fields/properties of an operation type once so
    // we can identify the relevant duration field without flooding the log.
    private static void DumpOperation(object op)
    {
      if (op == null) return;
      var type = op.GetType();
      if (_loggedTypes.Contains(type)) return;
      _loggedTypes.Add(type);

      LoggingHelper.Msg($"LabOven operation type {type.FullName} fields/properties:");
      foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
      {
        try { LoggingHelper.Msg($" field {f.Name} ({f.FieldType.Name}) = {f.GetValue(op)}"); } catch { }
      }
      foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
      {
        if (!p.CanRead) continue;
        try { LoggingHelper.Msg($" prop {p.Name} ({p.PropertyType.Name}) = {p.GetValue(op)}"); } catch { }
      }
    }
#endif

    // apply multiplier when an oven operation is created (local or via RPC)
    private static void ScaleOperation(dynamic op)
    {
      try
      {
        if (op == null) return;
        if (_scaledOps.TryGetValue((object)op, out _))
          return; // already scaled

        int speed = BetterStacksMod.CurrentConfig.LabOvenSpeed;
        if (speed <= 1) return;

        // the oven operation stores a simple duration field rather than a
        // Recipe object like the chemistry station.  use the `cookDuration`
        // property (an int) and adjust it directly.
        // dump fields/properties of the operation so we can see where the
        // true duration lives.  this will log a bunch of data on first call.
#if DEBUG
        try {
          DumpOperation((object)op);
        } catch (Exception e) {
          LoggingHelper.Error("LabOvenPatches introspect failed", e);
        }
#endif

        int time = (int)op.cookDuration;
        if (time <= 0)
        {
          // the operation hasn't had its duration initialized yet; ask the
          // object to compute it.  this avoids clobbering a valid value when
          // cookDuration is set to -1 as a sentinel.
          try {
            time = (int)op.GetCookDuration();
            LoggingHelper.Msg($"LabOven ScaleOperation fetched duration via GetCookDuration: {time}");
          } catch (Exception e) {
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
#if DEBUG
        LoggingHelper.Msg($"LabOven ScaleOperation invoked: originalTime={time}, speed={speed}");
#endif

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

#if DEBUG
        LoggingHelper.Msg($"LabOven ScaleOperation computed scaled={scaled}");
#endif

        if (scaled != time)
        {
          op.cookDuration = scaled;
          _scaledOps.Add((object)op, new object());
          LoggingHelper.Msg($"LabOven operation scaled from {time} to {scaled} (speed {speed})");
        }
      }
      catch (Exception ex)
      {
        LoggingHelper.Error("LabOvenPatches.ScaleOperation failed", ex);
      }
    }

    // operation hooks
    public static bool Prefix_SendCookOperation(dynamic __instance, dynamic operation)
    {
      ScaleOperation(operation);
      return true;
    }

    public static bool Prefix_SetCookOperation(dynamic __instance, dynamic conn, dynamic operation)
    {
      ScaleOperation(operation);
      return true;
    }

    // speed up the task’s own progress method; this is the code path that
    // actually decrements the oven timer during a lab‑oven task, so short
    // recipes that skip the object’s Update will still be accelerated.
    public static void Prefix_StartLabOvenTask_CheckProgress(dynamic __instance)
    {
      try
      {
#if DEBUG
        LoggingHelper.Msg("StartLabOvenTask.CheckProgress prefix");
#endif
        // dump fields/properties of the task instance to discover hidden timers
        // but only once per concrete task type – the previous implementation
        // logged on every call which killed performance under DEBUG.
#if DEBUG
        if (__instance != null)
        {
          var instType = ((object)__instance).GetType();
          if (!_loggedTypes.Contains(instType))
          {
            _loggedTypes.Add(instType);
            LoggingHelper.Msg($"StartLabOvenTask type {instType.FullName} fields/properties:");
            foreach (var f in instType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
              try { LoggingHelper.Msg($" StartLabOvenTask field {f.Name} = {f.GetValue(__instance)}"); } catch { }
            }
            foreach (var p in instType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
              if (!p.CanRead) continue;
              try { LoggingHelper.Msg($" StartLabOvenTask prop  {p.Name} = {p.GetValue(__instance)}"); } catch { }
            }
          }
        }
#endif

        int speed = BetterStacksMod.CurrentConfig.LabOvenSpeed;
        if (speed <= 1) return;

        var oven = __instance.Oven;
        if (oven == null) return;
        var op = oven.CurrentOperation;
        if (op == null) return;

        int progress = (int)op.CookProgress;
        int dur = (int)op.cookDuration;
        if (progress >= dur) return;

        int incr = speed - 1;
        int newProg = progress + incr;
        if (newProg > dur) newProg = dur;
        op.CookProgress = newProg;
#if DEBUG
        LoggingHelper.Msg($"LabOven task CheckProgress add progress {incr}: {progress}->{newProg} (dur {dur}, speed {speed})");
#endif
      }
      catch (Exception ex)
      {
        LoggingHelper.Error("LabOvenPatches.Prefix_StartLabOvenTask_CheckProgress failed", ex);
      }
    }

    // also hook the task’s ProgressStep just in case the other method isn’t
    // invoked for some reason; the code is identical.
    public static void Prefix_StartLabOvenTask_ProgressStep(dynamic __instance)
    {
      Prefix_StartLabOvenTask_CheckProgress(__instance);
    }

    // artificial progress boost on each frame update; the oven’s Update method
    // normally marches the timer forward by a fixed amount.  by adding extra
    // progress we ensure even 1‑tick recipes complete faster when speed>1.
    public static void Prefix_Update(dynamic __instance)
    {
      try
      {
        // diagnostic – always log entry and the current operation state
#if DEBUG
        LoggingHelper.Msg("LabOven Update prefix called");
        var op = __instance.CurrentOperation;
        LoggingHelper.Msg(op == null ? "  current operation is null" : "  current operation exists");
#else
        var op = __instance.CurrentOperation;
#endif

        int speed = BetterStacksMod.CurrentConfig.LabOvenSpeed;
        if (speed <= 1) return;

        if (op == null) return;

        int progress = (int)op.CookProgress;
        int dur = (int)op.cookDuration;
#if DEBUG
        LoggingHelper.Msg($"  progress={progress}, dur={dur}");
#endif
        if (progress >= dur) return;

        int incr = speed - 1;
        int newProg = progress + incr;
        if (newProg > dur) newProg = dur;
        op.CookProgress = newProg;
#if DEBUG
        LoggingHelper.Msg($"LabOven Update add progress {incr}: {progress}->{newProg} (dur {dur}, speed {speed})");
#endif
      }
      catch (Exception ex)
      {
        LoggingHelper.Error("LabOvenPatches.Prefix_Update failed", ex);
      }
    }

    // scale whatever value GetCookDuration returns so the oven internally
    // thinks every recipe is shorter when the speed multiplier is greater than
    // one.  this catches cases where `cookDuration` itself was left at -1
    // until the getter computes the real length.
    public static void Postfix_GetCookDuration(dynamic __instance, ref int __result)
    {
      try
      {
        int speed = BetterStacksMod.CurrentConfig.LabOvenSpeed;
        if (speed <= 1 || __result <= 0)
          return;
        int orig = __result;
        int scaled = orig / speed;
        if (scaled < 1) scaled = 1;
        if (scaled > orig) scaled = orig;
        if (scaled != orig)
        {
          __result = scaled;
#if DEBUG
          LoggingHelper.Msg($"LabOven GetCookDuration scaled {orig} -> {scaled} (speed {speed})");
#endif
        }
      }
      catch (Exception ex)
      {
        LoggingHelper.Error("LabOvenPatches.Postfix_GetCookDuration failed", ex);
      }
    }

    // adjust the passage of in‑game minutes to the oven itself; the laboratory
    // object listens for TimeManager ticks via OnTimePass and reduces its
    // internal countdown by the amount passed.  higher speed values divide the
    // incoming minutes, effectively making the oven think time is moving more
    // slowly and thus finishing sooner.  this is analogous to the cauldron
    // prefix that divides the remaining cook time on start.
    public static bool Prefix_OnTimePass(dynamic __instance, ref int minutes)
    {
      try
      {
        int speed = BetterStacksMod.CurrentConfig.LabOvenSpeed;
        if (speed > 1)
        {
          int orig = minutes;
          // treat each passed minute as `speed` minutes; this accelerates the
          // countdown in the oven’s internal logic.  we don’t clamp upward
          // because a large value merely skips ahead which is harmless.
          minutes = orig * speed;
#if DEBUG
          LoggingHelper.Msg($"LabOven OnTimePass adjusted from {orig} to {minutes} (speed {speed})");
#endif
        }
      }
      catch (Exception ex)
      {
        LoggingHelper.Error("LabOvenPatches.Prefix_OnTimePass failed", ex);
      }
      return true;
    }
  }
}