using System;
using System.Reflection;
using System.Runtime.CompilerServices;

using BetterStacksF;
using BetterStacksF.Utilities;

namespace BetterStacksF.Patches {
  /// <summary>
  /// Speed up chemistry station cooking and keep the on-screen timer
  /// consistent.
  /// </summary>
  public static class ChemistryStationPatches {

    // formatting utility for the countdown clock
    private static string FormatInProgressTime(int minutes) {
      int h = minutes / 60;
      int m = minutes % 60;
      return h + ":" + m.ToString("00");
    }

    // we cache a scaled duration per-operation using a weak table.  the
    // original recipe object is shared across all uses so cloning it proved
    // fragile and didn’t actually affect the internal timer (it copies the
    // cook time into a private field when the operation is created).  by
    // remembering the scaled value we can update whatever internal field the
    // operation happens to use and drive the UI off our cached number as well.

    // weak table mapping operation object -> scaled cook minutes
    private class ScaledInfo { public int value; }
    private static readonly ConditionalWeakTable<object, ScaledInfo> _scaledTable =
        new ConditionalWeakTable<object, ScaledInfo>();
    // track recipes that have already been mutated so we don’t apply the
    // multiplier a second time when a different operation instance arrives.
    private static readonly ConditionalWeakTable<object, object> _scaledRecipes =
        new ConditionalWeakTable<object, object>();



    // apply multiplier to an operation; keeps the cook time >=1 and avoids
    // shrinking an already-scaled recipe.  we compute a new target duration and
    // remember it in `_scaledTable`; later hooks and the UI will use that value.
    private static void ScaleOperation(dynamic op) {
      try {
        if (op == null) return;

        object opKey = (object)op;
        // avoid re-scaling the same operation twice; SendCookOperation and
        // SetCookOperation may both fire during normal use.
        if (_scaledTable.TryGetValue(opKey, out _)) {
          LoggingHelper.Msg("ChemStation ScaleOperation skipped already-scaled operation");
          return;
        }

        int speed = BetterStacksFMod.CurrentConfig.ChemistryStationSpeed;
        if (speed <= 1) return;

        // check recipe-level cache before touching anything
        dynamic? recipe = null; // avoid CS8600 warning
        ReflectionHelper.TryCatchLog(() => { recipe = op.Recipe; },
                                "ChemStation ScaleOperation could not read recipe");
        if (recipe != null && _scaledRecipes.TryGetValue((object)recipe!, out _)) {
          LoggingHelper.Msg("ChemStation ScaleOperation skipped already-scaled recipe");
          // still add op entry so we don't recheck on the same op object
          ReflectionHelper.TryCatchLog(() => {
            int existing = (int)recipe!.CookTime_Mins;
            _scaledTable.Add(opKey, new ScaledInfo { value = existing });
          }, "ChemStation ScaleOperation failed to cache existing time");
          return;
        }

        int orig = 1;
        ReflectionHelper.TryCatchLog(() => { orig = (int)op.Recipe.CookTime_Mins; },
                                "ChemStation ScaleOperation could not read original cook time");
        if (orig <= 0) orig = 1;

        int scaled = Math.Max(1, orig / speed);

        // cache the scaled value per operation
        _scaledTable.Add(opKey, new ScaledInfo { value = scaled });

        LoggingHelper.Msg($"ChemStation ScaleOperation computed {orig} -> {scaled} (speed {speed})");

        // mutate the recipe object (as before) and also the live operation
        // instance so its internal timer starts at the scaled duration.  the
        // setter approach we attempted previously could not be patched, so we
        // brute‑force search for a field/property equal to the original value.
        ReflectionHelper.TryCatchLog(() => {
          var recipeInstance = op.Recipe;
          if (recipeInstance != null) {
            var rtype = recipeInstance.GetType();
            // pattern-match so analyzer knows the prop isn't null inside the block
            if (rtype.GetProperty("CookTime_Mins", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is PropertyInfo p && p.CanWrite) {
              p.SetValue(recipeInstance, scaled);
              LoggingHelper.Msg("ChemStation ScaleOperation mutated recipe property CookTime_Mins");
            }
            else {
              var field = rtype.GetField("CookTime_Mins", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
              if (field != null) {
                field.SetValue(recipeInstance, scaled);
                LoggingHelper.Msg("ChemStation ScaleOperation mutated recipe field CookTime_Mins");
              }
            }

            // remember that this recipe has been altered so we don't touch it again
            _scaledRecipes.Add((object)recipeInstance, 0);
          }
        }, "ChemStation ScaleOperation recipe mutation failed");

        // proactively adjust any integer field on the operation instance that
        // was initialized with the original duration.  this is the actual fix
        // that makes the timer behave; OnTimePass clamping is now redundant.
        SetOperationTime(op, scaled, orig);

        // dump structure once so we can inspect field names in logs
        if (LoggingHelper.EnableVerbose)
        {
          ReflectionHelper.DumpObject(opKey);
        }
      }
      catch (Exception ex) {
        LoggingHelper.Error("ScaleOperation failed", ex);
      }
    }


    // when we see an integer field whose value matches the original duration we
    // assume it's the one the station uses internally and rewrite it.  this
    // generic approach avoids hardcoding field names across game versions.
    private static void SetOperationTime(dynamic op, int scaled, int orig) {
      if (op == null) return;
      ReflectionHelper.TryCatchLog(() => {
          object obj = (object)op;
          var type = obj.GetType();
          foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            if (f.FieldType == typeof(int)) {
              var rawValue = f.GetValue(obj);
              if (rawValue is int val && val == orig) {
                f.SetValue(obj, scaled);
                LoggingHelper.Msg($"ChemStation SetOperationTime scaled field {f.Name} {orig}->{scaled}");
              }
            }
          }
          // also try CurrentTime property if for some reason it starts at orig
          var prop = type.GetProperty("CurrentTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
          if (prop != null && prop.CanWrite && prop.PropertyType == typeof(int)) {
            var rawValue = prop.GetValue(obj);
            if (rawValue is int val && val == orig) {
              prop.SetValue(obj, scaled);
              LoggingHelper.Msg($"ChemStation SetOperationTime scaled property CurrentTime {orig}->{scaled}");
            }
          }
      }, "ChemStation SetOperationTime failed");
    }

    // note: the prefix remains for logging, but we don't mutate the op
    public static bool Prefix_SendCookOperation(dynamic __instance, dynamic op) {
      LoggingHelper.Msg("ChemStation Prefix_SendCookOperation");
      ScaleOperation(op);
      return true;
    }

    public static bool Prefix_SetCookOperation(dynamic __instance, dynamic conn, dynamic operation) {
      LoggingHelper.Msg("ChemStation Prefix_SetCookOperation");
      ScaleOperation(operation);
      return true;
    }


    // when the station finalizes an operation we may be racing the timer; ensure
    // the op is at its scaled time before the work completes so it doesn't
    // finish immediately.
    public static void Prefix_FinalizeOperation(dynamic __instance) {
      try {
        int speed = BetterStacksFMod.CurrentConfig.ChemistryStationSpeed;
        if (speed <= 1) return;
        var op = __instance.CurrentCookOperation;
        if (op == null) return;
        int scaled = GetScaledTime(op);
        op.CurrentTime = scaled;
        // when operation finishes we can drop our cache entry
        _scaledTable.Remove((object)op);
        LoggingHelper.Msg($"ChemStation Prefix_FinalizeOperation set CurrentTime to {scaled}");
      }
      catch (Exception ex) {
        LoggingHelper.Error("ChemStation Prefix_FinalizeOperation failed", ex);
      }
    }
    // cached reflection for the chemistry window canvas field; resolved lazily
    private static System.Reflection.FieldInfo? _canvasField;

    // common helpers used by multiple hooks
    // return the duration that should be used for this operation; either the
    // cached scaled value or the recipe’s original cook time.
    private static int GetScaledTime(dynamic op) {
      if (op == null) return 0;
      object key = (object)op;
      if (_scaledTable.TryGetValue(key, out var info))
        return info.value;
      int recipeTime = 0;
      ReflectionHelper.TryCatchLog(() => { recipeTime = (int)op.Recipe.CookTime_Mins; },
                            "ChemStation GetScaledTime failed to read recipe time");
      return recipeTime;
    }

    private static int CalculateDisplayRemaining(dynamic op) {
      if (op == null) return 0;
      int recipeTime = GetScaledTime(op);
      int current = 0;
      ReflectionHelper.TryCatchLog(() => { current = (int)op.CurrentTime; },
                            "ChemStation CalculateDisplayRemaining failed to read CurrentTime");
      int rawRemaining = recipeTime - current;
      return rawRemaining < 0 ? 0 : rawRemaining;
    }

    private static object? GetCanvas(dynamic window) {
      if (_canvasField == null) {
        var type = window.GetType();
        _canvasField = type.GetField("canvas",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
      }

      return _canvasField?.GetValue(window);
    }

    private static void UpdateCanvasLabels(object canvas, int remaining) {
      if (canvas == null) return;
      ReflectionHelper.TryCatchLog(() => {
        dynamic dyn = canvas;
        dyn.InProgressLabel.text = FormatInProgressTime(remaining);
        if (dyn.InProgressRecipeEntry != null)
          dyn.InProgressRecipeEntry.CookingTimeLabel.text = FormatInProgressTime(remaining);
      }, "ChemStation UpdateCanvasLabels failed");
    }


    // UI hooks for timer
    public static void Postfix_UpdateUI(dynamic __instance) {
      ReflectionHelper.TryCatchLog(() => {
        int speed = BetterStacksFMod.CurrentConfig.ChemistryStationSpeed;
        if (speed <= 1) return;

        var station = __instance.ChemistryStation;
        if (station != null) {
          var remaining = CalculateDisplayRemaining(station.CurrentCookOperation);
          UpdateCanvasLabels(__instance, remaining);
        }
      }, "ChemistryStationPatches.Postfix_UpdateUI failed");
    }



  }
}
