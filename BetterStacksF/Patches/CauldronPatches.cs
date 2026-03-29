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

      // prefix_Start: log invocation and snapshot slots for later diff
      try {
        if (LoggingHelper.EnableVerbose) {
          var msg = "Prefix_StartCookOperation invoked";
          msg += $" connType={(conn==null?"null":conn.GetType().Name)}";
          msg += $" qualityType={(quality==null?"null":quality.GetType().Name)}";
          msg += $" remainingCookTime={remainingCookTime}";
          LoggingHelper.Msg(msg);
        }

        // take a quick snapshot *only if one hasn’t already been recorded*.
        // SendCookOperation will usually fire first and populate the table;
        // we don’t want to overwrite that snapshot or erase the pre-deduction
        // state before the postfix runs.
        try {
            if (!_preCookSnapshot.TryGetValue((object)__instance, out _)) {
                var slots = GetSlotsArray((object)__instance);
                if (slots != null) {
                    var arr = new int[slots.Length];
                    for (int i = 0; i < slots.Length; i++)
                        arr[i] = GetSlotCount(slots.GetValue(i));
                    _preCookSnapshot.Add((object)__instance, arr);
                }
            }
        } catch { }
      }
      catch { /* ignore logging errors */ }

      // if the cauldron isn't cooking yet, clear any stale consumed flag so
      // the later postfix will run, but preserve an ID added by a preceding
      // SendCookOperation for the same cook.  only remove the entry when it’s
      // genuinely from an earlier, completed cook.
      try {
          if (!((bool)__instance.isCooking)) {
              string id = GetCauldronId((object)__instance);
              if (!string.IsNullOrEmpty(id) && !_consumedIds.Contains(id)) {
                  ClearConsumed((object)__instance);
                  LoggingHelper.Msg("StartCookOperation: cauldron not cooking, cleared consumed flag");
              }
          }
      } catch { }

      // attempt immediate removal and record what we consumed.  we still
      // keep an entry in _pendingCauldrons so that a later visual update can
      // correct the slot if our initial guess was wrong.
      try {
          if (TryMarkConsumed((object)__instance)) {
              var tuple = ApplyFlexibleConsumptionToCauldron(__instance);
              var info = new PendingInfo { ConsumedSlot = tuple.primarySlot, ExtraAmount = tuple.extraConsumed };
              _pendingCauldrons.Remove((object)__instance);
              _pendingCauldrons.Add((object)__instance, info);
              LoggingHelper.Msg($"Immediate Start consumption slot={info.ConsumedSlot} extra={info.ExtraAmount}");
          } else {
              LoggingHelper.Msg("Skipping duplicate consumption in StartCookOperation (pointer match)");
          }
      } catch { }

      int localTime = remainingCookTime;
      // dump the entire cauldron instance so we can see what fields are available
      if (LoggingHelper.EnableVerbose) {
        ReflectionHelper.TryCatchLog(() => ReflectionHelper.DumpObject(__instance, "cauldron instance: "),
                                  "Dumping cauldron instance failed");
      }
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

    // postfix invoked after the vanilla method has run; at this point the
    // base 20‑leaf cost has already been deducted, so we can now safely apply
    // flexible consumption using the pre/post snapshots recorded earlier.
    public static void Postfix_StartCookOperation(dynamic __instance) {
      try {
        if (_pendingCauldrons.TryGetValue((object)__instance, out _)) {
            LoggingHelper.Msg("Deferring StartCookOperation consumption until visual update");
            return;
        }
        if (TryMarkConsumed((object)__instance))
            ApplyFlexibleConsumptionToCauldron(__instance);
        else
            LoggingHelper.Msg("Skipping duplicate consumption in StartCookOperation (pointer match)");
      } catch { }
    }

    // ---------------------------------------------------------------------
    // Flexible consumption patch.  The behaviour is defined in
    // docs/cauldron-consumption.md; the cauldron may draw from all four
    // Coca Leaf slots and consume anywhere from 20 up to 20×multiplier units per
    // cook.  This implementation is entirely reflection‑driven so that it
    // continues to work even if the underlying game classes change slightly.
    //
    // Two ConditionalWeakTable caches remember the relevant FieldInfo
    // instances for the lifetime of the objects, avoiding repeated calls to
    // Type.GetField.  A lightweight DumpObject helper can be enabled during
    // development via <see cref="LoggingHelper.EnableVerbose"/> to inspect
    // the shape of the dynamic objects we receive from the game.

    private static readonly ConditionalWeakTable<object, Dictionary<string, FieldInfo?>> _cauldronFieldCache
        = new ConditionalWeakTable<object, Dictionary<string, FieldInfo?>>();
    private static readonly ConditionalWeakTable<object, Dictionary<string, FieldInfo?>> _cauldronSlotsCache
        = new ConditionalWeakTable<object, Dictionary<string, FieldInfo?>>();
    // cache the two most recent slot-count snapshots produced by
    // UpdateIngredientVisuals.  We need both the current and the previous
    // values because the UI update can fire *after* the vanilla 20-leaf cost is
    // removed; the earlier snapshot is the only reliable pre-deduction state.
    // Using a 2‑element array keeps things simple while still allowing GC.
    private static readonly ConditionalWeakTable<object, int[][]> _visualSnapshotCache
        = new ConditionalWeakTable<object, int[][]>();

    // track the last primary slot we chose for a particular cauldron.  use a
    // tiny reference wrapper so it fits in a ConditionalWeakTable.
    private class IntHolder { public int Value; public IntHolder(int v) { Value = v; } }
    private static readonly ConditionalWeakTable<object, IntHolder> _lastPrimarySlot
        = new ConditionalWeakTable<object, IntHolder>();
    // remember which cauldrons have been adjusted this cycle.  rather than
    // rely on fragile pointer or object identity, we compute a stable string
    // identifier for the cauldron (GUID if available, else pointer, else
    // runtime hashcode) and track that.
    private static readonly HashSet<string> _consumedIds = new HashSet<string>();
    // information about cauldrons where we’ve already removed the extra
    // leaves (or plan to) and are waiting to see the vanilla deduction
    // so we can correct the slot if we guessed wrong.  storing a tiny object
    // rather than a bare boolean lets us remember which slot and how many
    // leaves were consumed.
    private class PendingInfo {
        public int ConsumedSlot = -1;   // slot index we drained from, -1=none yet
        public int ExtraAmount = 0;     // number of leaves removed
    }
    private static readonly ConditionalWeakTable<object, PendingInfo> _pendingCauldrons
        = new ConditionalWeakTable<object, PendingInfo>();

    // helper used by correction logic – adjust a stack quantity by delta.
    private static void ChangeStackQuantity(object stack, int delta) {
        try {
            dynamic dyn = stack;
            if (delta < 0) dyn.ChangeQuantity(delta, true);
            else dyn.ChangeQuantity(delta, false);
        } catch {
            try {
                dynamic dyn = stack;
                dyn.Quantity += delta;
            } catch {
                try {
                    dynamic dyn = stack;
                    dyn.count += delta;
                } catch { }
            }
        }
    }

    private static void CorrectConsumedSlot(dynamic cauldron, int wrongSlot, int rightSlot, int amount) {
        if (cauldron == null || wrongSlot == rightSlot || amount <= 0) return;
        var slots = GetSlotsArray((object)cauldron);
        if (slots == null) return;
        // add back to wrong slot
        var wrong = slots.GetValue(wrongSlot);
        if (wrong != null) ChangeStackQuantity(wrong, amount);
        // drain from right slot
        var right = slots.GetValue(rightSlot);
        if (right != null) ChangeStackQuantity(right, -amount);
        LoggingHelper.Msg($"Corrected deferred consumption: moved {amount} from slot{wrongSlot} to slot{rightSlot}");
    }

    private static long GetInstancePointer(object? obj) {
        if (obj == null) return 0;
        var t = obj.GetType();
        // try a small set of well-known field/property names individually so
        // that a failure on one doesn't abort the entire lookup.
        foreach (var name in new[] { "m_CachedPtr", "Pointer", "pooledPtr" }) {
            try {
                var f = t.GetField(name,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) {
                    var v = f.GetValue(obj);
                    if (v != null) {
                        long l;
                        if (v is IntPtr ip) l = ip.ToInt64();
                        else l = Convert.ToInt64(v);
                        if (l != 0) {
                            LoggingHelper.Msg($"GetInstancePointer found {l} from field {name}");
                            return l;
                        }
                    }
                }
                var p = t.GetProperty(name,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanRead) {
                    var val = p.GetValue(obj);
                    if (val != null) {
                        long l;
                        if (val is IntPtr ip2) l = ip2.ToInt64();
                        else l = Convert.ToInt64(val);
                        if (l != 0) {
                            LoggingHelper.Msg($"GetInstancePointer found {l} from property {name}");
                            return l;
                        }
                    }
                }
            } catch (Exception ex) {
                // field existed but conversion failed; record and continue
                LoggingHelper.Msg($"GetInstancePointer check {name} threw: {ex.Message}");
            }
        }

        // fallback: scan all numeric fields and pick the largest value seen.  This
        // is more resilient than returning on the first non-zero; the native
        // pointer will usually be the biggest number.
        long best = 0;
        string bestName = null;
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            try {
                if (f.FieldType == typeof(long) || f.FieldType == typeof(ulong) ||
                    f.FieldType == typeof(int) || f.FieldType == typeof(uint) ||
                    f.FieldType == typeof(IntPtr)) {
                    var val = f.GetValue(obj);
                    if (val != null) {
                        long l = Convert.ToInt64(val);
                        if (l != 0 && l > best) {
                            best = l;
                            bestName = f.Name;
                        }
                    }
                }
            } catch { /* ignore individual field errors */ }
        }
        if (best != 0) {
            LoggingHelper.Msg($"GetInstancePointer chose {best} from fallback field {bestName}");
            return best;
        }
        return 0;
    }

    private static string GetCauldronId(object cauldron) {
        if (cauldron == null) return "";
        // prefer stable native pointer; GUID is sometimes a transient network
        // object id that changes between Send/Start, so only use it as a
        // fallback when pointer lookup fails.
        long ptr = GetInstancePointer(cauldron);
        if (ptr != 0) return ptr.ToString();
        try {
            var g = (string)((dynamic)cauldron).GUID;
            if (!string.IsNullOrEmpty(g)) return g;
        } catch { }
        return RuntimeHelpers.GetHashCode(cauldron).ToString();
    }

    private static bool TryMarkConsumed(object cauldron) {
        long ptr = GetInstancePointer(cauldron);
        string id = GetCauldronId(cauldron);
        LoggingHelper.Msg($"TryMarkConsumed ptr={ptr} id={id}");
        if (string.IsNullOrEmpty(id)) return true; // shouldn't happen
        if (_consumedIds.Contains(id)) return false;
        _consumedIds.Add(id);
        return true;
    }

    private static void ClearConsumed(object cauldron) {
        string id = GetCauldronId(cauldron);
        LoggingHelper.Msg($"ClearConsumed id={id}");
        if (!string.IsNullOrEmpty(id)) _consumedIds.Remove(id);
    }

    private static Array? GetSlotsArray(object cauldron) {
      // try legacy private field first (used in very old builds)
      var slots = ReflectionHelper.GetFieldValueCached<Array>(cauldron, "m_slots", _cauldronSlotsCache);
      if (slots != null) {
        if (LoggingHelper.EnableVerbose) LoggingHelper.Msg("GetSlotsArray: found m_slots array");
        return slots;
      }

      // look for the new public property; the value might be an Il2CppReferenceArray
      // which does *not* always satisfy "is Array" so fall back to IEnumerable.
      try {
        var prop = cauldron.GetType().GetProperty("IngredientSlots",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanRead) {
          var val = prop.GetValue(cauldron);
          if (LoggingHelper.EnableVerbose) {
            LoggingHelper.Msg($"GetSlotsArray: IngredientSlots property returned {val?.GetType().FullName ?? "<null>"}");
          }
          if (val is Array arr) return arr;
          if (val is System.Collections.IEnumerable ie) {
            return ie.Cast<object>().ToArray();
          }
        }
      } catch (Exception ex) {
        if (LoggingHelper.EnableVerbose) LoggingHelper.Error("GetSlotsArray property access failed", ex);
      }

      // maybe a field exists with the same name (unlikely but harmless)
      // ReflectionHelper.GetFieldValueCached now keys by object + field name, so
      // previous m_slots lookups do not poison the IngredientSlots lookup.
      try {
        var fallback = ReflectionHelper.GetFieldValueCached<object>(cauldron, "IngredientSlots", _cauldronSlotsCache);
        if (fallback != null) {
          if (fallback is Array arr) return arr;
          if (fallback is System.Collections.IEnumerable ie) {
            return ie.Cast<object>().ToArray();
          }
        }
      } catch { }

      // check for list-backed slots (include backing-field style names)
      foreach (var name in new[] { "InputSlots", "ItemSlots", "_InputSlots_k__BackingField", "_ItemSlots_k__BackingField" }) {
        try {
          var f = cauldron.GetType().GetField(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
          if (f != null) {
            var val = f.GetValue(cauldron);
            if (LoggingHelper.EnableVerbose)
              LoggingHelper.Msg($"GetSlotsArray: field '{name}' returned {val?.GetType().FullName ?? "<null>"}");
            if (val is System.Collections.IEnumerable ie) {
              return ie.Cast<object>().ToArray();
            }
          }
        } catch (Exception ex) {
          if (LoggingHelper.EnableVerbose)
            LoggingHelper.Error($"GetSlotsArray field {name} access failed", ex);
        }
        try {
          var p = cauldron.GetType().GetProperty(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
          if (p != null && p.CanRead) {
            var val = p.GetValue(cauldron);
            if (LoggingHelper.EnableVerbose)
              LoggingHelper.Msg($"GetSlotsArray: prop '{name}' returned {val?.GetType().FullName ?? "<null>"}");
            if (val is System.Collections.IEnumerable ie) {
              return ie.Cast<object>().ToArray();
            }
          }
        } catch (Exception ex) {
          if (LoggingHelper.EnableVerbose)
            LoggingHelper.Error($"GetSlotsArray prop {name} access failed", ex);
        }
      }

      if (LoggingHelper.EnableVerbose) LoggingHelper.Msg("GetSlotsArray: no slots found");
      return null;
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

    // New helper that applies the flexible consumption algorithm directly to
    // a Cauldron instance rather than an operation object.  This is useful
    // because some game paths (like automated tasks) no longer pass the
    // operation object to the patched methods; we still have the cauldron
    // instance itself, so we can modify slot counts immediately.
    // returns (primarySlot, actualExtraConsumed); primarySlot may be -1 if no
    // consumption occurred.
    private static (int primarySlot, int extraConsumed) ApplyFlexibleConsumptionToCauldron(dynamic cauldron, int? forcedMultiplier = null)
    {
        try {
            if (cauldron == null) return (-1, 0);
            int multiplier = forcedMultiplier ?? Math.Max(1, BetterStacksFMod.CurrentConfig.CauldronIngredientMultiplier);

            var slots = GetSlotsArray((object)cauldron);
            if (slots == null) return (-1, 0);

            if (LoggingHelper.EnableVerbose) ReflectionHelper.DumpObject(cauldron, "cauldron instance pre-consume: ");

            var slotsNonNull = slots;
            int totalInput = slotsNonNull.Cast<object?>()
                                  .Select(GetSlotCount)
                                  .Sum();

            int usable = CalculateUsable(totalInput, multiplier);
            LoggingHelper.Msg($"[cauldron] totalInput={totalInput}, multiplier={multiplier}, usable={usable}");

            if (usable < 20) return (-1, 0); // nothing even the vanilla code will cook

            // vanilla StartCookOperation is about to deduct 20 leaves itself.
            // we only want to remove the *extra* amount beyond the base cost.
            int extra = usable - 20;
            LoggingHelper.Msg($"[cauldron] extra to consume={extra} (usable minus base 20)");
            if (extra <= 0) return (-1, 0);

            // decide which slot the game already removed 20 from. try the
            // snapshot produced by UpdateIngredientVisuals; fall back to the
            // previous heuristic (lowest positive count) if no snapshot exists.
            int primary = DeterminePrimarySlot((object)cauldron, slotsNonNull);

            int remaining = extra;
            int idx = primary;
            int cyclesWithoutTake = 0;
            while (remaining > 0 && cyclesWithoutTake < slotsNonNull.Length) {
                if (idx >= slotsNonNull.Length) idx = 0;
                object? maybeStack = slotsNonNull.GetValue(idx);
                if (maybeStack == null) {
                    idx++;
                    cyclesWithoutTake++;
                    continue;
                }
                int cur = GetSlotCount(maybeStack);
                int take = Math.Min(cur, remaining);
                if (take > 0) {
                    // try to subtract using available API
                    try {
                        dynamic stack = maybeStack;
                        // prefer ChangeQuantity method if present
                        stack.ChangeQuantity(-take, true);
                    } catch {
                        // fallback to property if available
                        try {
                            dynamic stack = maybeStack;
                            stack.Quantity = cur - take;
                        } catch {
                            // last resort: try setting 'count' directly for old builds
                            try {
                                dynamic stack = maybeStack;
                                stack.count -= take;
                            } catch {
                                // give up
                            }
                        }
                    }
                    remaining -= take;
                    cyclesWithoutTake = 0;
                } else {
                    cyclesWithoutTake++;
                }
                idx++;
            }
            int drained = extra - remaining;
            return (primary, drained);
        } catch {
            return (-1, 0);
        }
    }

    // early UI hook -----------------------------------------------------------
    // when the cauldron inventory UI opens the game calls
    // CauldronCanvas.SetIsOpen(cauldron, open, removeUI).  the slots are
    // guaranteed to be populated by that point, making this the earliest
    // reliable opportunity to inspect/modify them.  automated tasks trigger
    // the same call before pressing Start, which is why our previous
    // attempts in SendCookOperation sometimes saw empty slots.
    public static void Prefix_CauldronCanvas_SetIsOpen(dynamic __instance,
                                                        dynamic cauldron,
                                                        bool open,
                                                        bool removeUI) {
      // log parameters so we can reason about the call-site
      LoggingHelper.Msg($"Prefix_CauldronCanvas_SetIsOpen invoked open={open} removeUI={removeUI}");
      if (!open && cauldron != null) {
          // UI closing; if a cook was cancelled we should forget the
          // consumed flag so a later cook on the same object can proceed.
          ClearConsumed((object)cauldron);
          _lastPrimarySlot.Remove((object)cauldron);
          _pendingCauldrons.Remove((object)cauldron);
          LoggingHelper.Msg("Cleared consumed flag and pending state on UI close");
      }

      // when the UI opens we can grab the current slot counts even if there
      // has been no ingredient movement yet.  this is important for cook
      // sessions resumed from a save file where the cauldron already contains
      // leaves; without this we had no snapshot to compare and would fall back
      // to the wrong slot.  Only record a snapshot if one isn't already stored.
      if (open && cauldron != null) {
          try {
              var slots = GetSlotsArray((object)cauldron);
              if (slots != null) {
                  var counts = new int[slots.Length];
                  for (int i = 0; i < slots.Length; i++)
                      counts[i] = GetSlotCount(slots.GetValue(i));
                  if (!_visualSnapshotCache.TryGetValue(cauldron, out int[][] pair) ||
                      pair == null || pair[1] == null) {
                      _visualSnapshotCache.Remove(cauldron);
                      _visualSnapshotCache.Add(cauldron, new int[][] { null, counts });
                      LoggingHelper.Msg("Captured initial visual slot snapshot on UI open");
                  }
              }
          } catch { }
      }
      // do not consume here; the only reliable moment to modify the cauldron
      // contents is when the cook actually starts.  UpdateIngredientVisuals
      // already logs slot counts for debugging, and StartCookOperation handles
      // the consumption.  leaving this hook enabled for diagnostics might still
      // be useful but it must not change state.
      // if we discover a case StartCookOperation doesn't catch, revisit this.
    }

    // helpers ----------------------------------------------------------------
    // read the `count` value from a slot object using dynamic/reflection.
    // returns 0 on any failure; logs once per unique exception to avoid spam.
    private static readonly HashSet<string> _slotCountErrors = new HashSet<string>();
    private static int GetSlotCount(object? slot)
    {
        if (slot == null) return 0;
        // prefer Quantity property which is what ItemSlot actually exposes
        try {
            // dynamic will throw if property missing
            return (int)((dynamic)slot).Quantity;
        } catch { }
        try {
            return (int)((dynamic)slot).quantity; // lowercase variant
        } catch { }

        try {
            return (int)((dynamic)slot).count; // legacy fallback
        }
        catch (Exception ex) {
            // attempt reflection fallback
            try {
                var t = slot.GetType();
                foreach (var name in new[] { "Quantity", "quantity", "count", "m_quantity", "m_count" }) {
                    var f = t.GetField(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null) return Convert.ToInt32(f.GetValue(slot));
                    var p = t.GetProperty(name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && p.CanRead)
                        return Convert.ToInt32(p.GetValue(slot));
                }
            }
            catch { /* ignore */ }

            var key = ex.GetType().Name + ":" + ex.Message;
            if (_slotCountErrors.Add(key)) {
                LoggingHelper.Warning($"GetSlotCount failed on type {slot.GetType().Name}: {ex.Message}");
            }
            return 0;
        }
    }

    // determine which slot the vanilla code drained 20 units from.  we
    // prefer a pair of snapshots taken immediately before and immediately
    // after the vanilla operation; prefixes record preCounts, postfixes clear
    // them.  if no pre-snapshot is available we fall back to the old visual
    // cache (kept by UpdateIngredientVisuals) and finally to the simple
    // lowest-positive rule.
    private static readonly ConditionalWeakTable<object,int[]> _preCookSnapshot
        = new ConditionalWeakTable<object,int[]>();

    private static int DeterminePrimarySlot(object cauldron, Array slots) {
        if (_preCookSnapshot.TryGetValue(cauldron, out var preCounts)) {
            if (preCounts.Length != slots.Length) {
                LoggingHelper.Msg($"preCook snapshot length mismatch {preCounts.Length} vs {slots.Length}");
            } else {
                LoggingHelper.Msg("preCook snapshot found, comparing");
                bool anyDiff = false;
                for (int i = 0; i < slots.Length; i++) {
                    int cur = GetSlotCount(slots.GetValue(i));
                    int diff = preCounts[i] - cur;
                    if (diff > 0) {
                        anyDiff = true;
                        if (LoggingHelper.EnableVerbose)
                            LoggingHelper.Msg($"  primary slot determined via pre/post snapshot index={i} (pre={preCounts[i]}, cur={cur}, diff={diff})");
                        _preCookSnapshot.Remove(cauldron);
                        return i;
                    }
                }
                _preCookSnapshot.Remove(cauldron);
                if (!anyDiff) {
                    LoggingHelper.Msg("preCook snapshot present but no slot change detected");
                    // try to salvage by using the last visual snapshot, if any
                    if (_visualSnapshotCache.TryGetValue(cauldron, out var pair) &&
                        pair != null && pair[1] != null && pair[1].Length == slots.Length) {
                        for (int i = 0; i < slots.Length; i++) {
                            int cur = GetSlotCount(slots.GetValue(i));
                            int diff2 = pair[1][i] - cur;
                            if (diff2 > 0) {
                                if (LoggingHelper.EnableVerbose)
                                    LoggingHelper.Msg($"  primary slot determined via solo visual snapshot index={i} (vis={pair[1][i]}, cur={cur}, diff={diff2})");
                                return i;
                            }
                        }
                        LoggingHelper.Msg("solo visual snapshot present but no change detected");
                    }
                }
            }
        } else {
            LoggingHelper.Msg("no preCook snapshot available");
        }

        if (_visualSnapshotCache.TryGetValue(cauldron, out var visPair) &&
            visPair.Length == 2 && visPair[0] != null && visPair[1] != null &&
            visPair[0].Length == slots.Length && visPair[1].Length == slots.Length) {
            var prev = visPair[0];
            var curCounts = visPair[1];
            for (int i = 0; i < slots.Length; i++) {
                int diff = prev[i] - curCounts[i];
                if (diff > 0) {
                    if (LoggingHelper.EnableVerbose)
                        LoggingHelper.Msg($"  primary slot determined via visual snapshot pair index={i} (prev={prev[i]}, cur={curCounts[i]}, diff={diff})");
                    _visualSnapshotCache.Remove(cauldron);
                    _lastPrimarySlot.Remove(cauldron);
                    _lastPrimarySlot.Add(cauldron, new IntHolder(i));
                    return i;
                }
            }
            // if we reach here the stored snapshots show no change; clear them
            _visualSnapshotCache.Remove(cauldron);
            LoggingHelper.Msg("visual snapshot pair present but no difference detected");
        } else {
            LoggingHelper.Msg("no usable visual snapshot pair available");
        }

        // before giving up, see if we remembered a previous primary slot and
        // that slot is still non-empty; this handles back-to-back cooks where
        // the deduction slot hasn't changed.
        if (_lastPrimarySlot.TryGetValue(cauldron, out var holder)) {
            int last = holder.Value;
            if (last >= 0 && last < slots.Length) {
                int cnt = GetSlotCount(slots.GetValue(last));
                if (cnt > 0) {
                    LoggingHelper.Msg($"  using cached lastPrimary={last} (count={cnt})");
                    return last;
                }
            }
        }

        // fallback: model vanilla behaviour and pick the first non-empty slot
        int primary = 0;
        for (int i = 0; i < slots.Length; i++) {
            if (GetSlotCount(slots.GetValue(i)) > 0) {
                primary = i;
                break;
            }
        }
        if (LoggingHelper.EnableVerbose) {
            int cnt = GetSlotCount(slots.GetValue(primary));
            LoggingHelper.Msg($"  primary slot fallback index={primary} (count={cnt})");
        }
        return primary;
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

        // log values so we can verify
        LoggingHelper.Msg($"Cauldron flexible consumption: totalInput={totalInput}, multiplier={multiplier}, usable={usable}");

        if (usable < 20) return;

        int extra = usable - 20;
        LoggingHelper.Msg($"  extra to consume={extra}");
        if (extra <= 0) return;

        int primary = DeterminePrimarySlot(cauldron, slotsNonNull);
        int remaining = extra;
        int idx = primary;
        int cyclesWithoutTake = 0;
        while (remaining > 0 && cyclesWithoutTake < slotsNonNull.Length) {
          if (idx >= slotsNonNull.Length) idx = 0;
          object? maybeStack = slotsNonNull.GetValue(idx);
          if (maybeStack == null) {
              idx++;
              cyclesWithoutTake++;
              continue;
          }
          int cur = GetSlotCount(maybeStack);
          int take = Math.Min(cur, remaining);
          if (take > 0) {
              try {
                dynamic stack = maybeStack;
                stack.ChangeQuantity(-take, true);
              } catch {
                try {
                  dynamic stack = maybeStack;
                  stack.Quantity = cur - take;
                } catch {
                  try {
                    dynamic stack = maybeStack;
                    stack.count -= take;
                  } catch { }
                }
              }
              remaining -= take;
              cyclesWithoutTake = 0;
          } else {
              cyclesWithoutTake++;
          }
          idx++;
        }

        operation.m_ingredientCount = usable;
        operation.m_resultCount = usable / 2;
      }
      catch (Exception ex) {
        LoggingHelper.Error("ApplyFlexibleConsumption failed", ex);
      }
    }

    // the vanilla FinishCookOperation signature has changed over time; older
    // builds passed a single `operation` object, newer ones take no parameters
    // at all.  Harmony will complain if our prefix declares a named argument
    // that the target method doesn’t have, so we use the generic `__args`
    // injection instead.  Any operation value we find is processed in the same
    // way as before.
    public static void Prefix_FinishCookOperation(dynamic __instance,
                                                  params object[] __args) {
      LoggingHelper.Msg($"Prefix_FinishCookOperation invoked (args={__args.Length})");

      // if we still have a pending entry for this cauldron it means no UI
      // update ever showed the vanilla deduction; the safe thing is to
      // perform the extra‑leaf removal now rather than leave the leaves
      // sitting around forever.  the post-deduction counts may already be
      // available, so we can run the regular helper here.
      if (_pendingCauldrons.TryGetValue((object)__instance, out var info)) {
          _pendingCauldrons.Remove((object)__instance);
          LoggingHelper.Msg("Performing deferred consumption on finish");
          if (info.ConsumedSlot == -1) {
              if (TryMarkConsumed((object)__instance))
                  ApplyFlexibleConsumptionToCauldron(__instance);
          } else {
              // determine actual deduction slot now that final counts should
              // reflect the vanilla removal
              var slots = GetSlotsArray((object)__instance);
              if (slots != null) {
                  int actual = DeterminePrimarySlot((object)__instance, slots);
                  if (info.ConsumedSlot != actual && info.ExtraAmount > 0)
                      CorrectConsumedSlot(__instance, info.ConsumedSlot, actual, info.ExtraAmount);
              }
          }
      }

      // the flexible‑consumption helper works two ways: it can modify the
      // cauldron instance itself (slots) or it can operate on the cook
      // operation object.  older game builds passed the operation here; newer
      // versions dropped the parameter entirely.  we handle both cases so the
      // ingredient *and* result counts are corrected.
      ApplyFlexibleConsumptionToCauldron(__instance);
      foreach (var arg in __args) {
        if (arg == null) continue;
        ApplyFlexibleConsumption(arg);
      }
      // when the cook really ends we can forget the consumed flag so the
      // cauldron can be reused in a future operation.
      ClearConsumed((object)__instance);

      // log output count if we were able to adjust an operation
      if (__args.Length > 0 && __args[0] != null) {
        try {
          dynamic op = __args[0];
          if (LoggingHelper.EnableVerbose) {
            int ingr = op.m_ingredientCount;
            int res = op.m_resultCount;
            LoggingHelper.Msg($"FinishCookOperation op counts after patch: ingredient={ingr}, result={res}");
          }
        } catch {
          // ignore if fields missing
        }
      }
    }

    // patch of the task combine‑ingredients step; this runs when all materials
    // have been placed and before the start button is pressed.  it's ideal for
    // automated tasks because slots are populated at this point.
    public static void Prefix_TaskCombineIngredients(dynamic __instance) {
      try {
        LoggingHelper.Msg("Prefix_TaskCombineIngredients invoked");
        // the task keeps its own reference to the Cauldron object
        var cauldron = (dynamic)__instance.Cauldron;
        if (cauldron != null) {
          ApplyFlexibleConsumptionToCauldron(cauldron);
        }
        // also log the raw items the task thinks it has
        if (LoggingHelper.EnableVerbose) {
          var leaves = __instance.CocaLeaves as System.Array;
          if (leaves != null) {
            for (int i = 0; i < leaves.Length; i++) {
              LoggingHelper.Msg($"  task CocaLeaves[{i}] = {leaves.GetValue(i)}");
            }
          }
          var gas = __instance.Gasoline;
          LoggingHelper.Msg($"  task Gasoline = {gas}");
        }
      } catch (Exception ex) {
        LoggingHelper.Error("Prefix_TaskCombineIngredients failed", ex);
      }
    }

    // called when the cauldron's UI visuals are updated – this typically
    // happens immediately after ingredients are transferred into the
    // container, making it a reliable place to run the consumption logic.
    public static void Prefix_UpdateIngredientVisuals(dynamic __instance) {
      // used for debugging and for snapshotting counts. we avoid consuming
      // here; actual removal happens when the cook starts. the last snapshot
      // before SendCookOperation/StartCookOperation will be compared to the
      // values read at the start event to determine the primary slot.
      LoggingHelper.Msg("Prefix_UpdateIngredientVisuals invoked");
      try {
        var slots = GetSlotsArray((object)__instance);
        if (slots != null) {
          var counts = new int[slots.Length];
          for (int i = 0; i < slots.Length; i++) {
            var slot = slots.GetValue(i);
            int count = GetSlotCount(slot);
            counts[i] = count;
            if (LoggingHelper.EnableVerbose)
              LoggingHelper.Msg($"  visual slot[{i}] count={count}");
          }
          // stash the snapshot for later consumption logic. preserve the
          // previous entry so we can compare two consecutive updates.
          int[][] pair = new int[2][];
          if (_visualSnapshotCache.TryGetValue(__instance, out int[][] existing)) {
              pair[0] = existing[1]; // shift current snapshot to previous
          }
          pair[1] = counts;
          _visualSnapshotCache.Remove(__instance);
          _visualSnapshotCache.Add(__instance, pair);
        } else {
          if (LoggingHelper.EnableVerbose)
            LoggingHelper.Msg("  visual slots array is null");
        }

        // if we have deferred consumption waiting for a post-deduction update,
        // see if this snapshot shows the vanilla 20‑leaf removal.  we look for
        // any slot whose current count is lower than the stored preCook
        // snapshot; if no change is visible we leave the request in the table
        // and try again on the next UI update.
        try {
            if (_pendingCauldrons.TryGetValue((object)__instance, out var info)) {
                bool sawDiff = false;
                int actualSlot = -1;
                if (_preCookSnapshot.TryGetValue((object)__instance, out var pre)) {
                    var slots2 = GetSlotsArray((object)__instance);
                    if (slots2 != null) {
                        int[] cur = new int[slots2.Length];
                        for (int i = 0; i < cur.Length; i++)
                            cur[i] = GetSlotCount(slots2.GetValue(i));
                        for (int i = 0; i < cur.Length; i++) {
                            if (pre[i] > cur[i]) { sawDiff = true; actualSlot = i; break; }
                        }
                    }
                }
                if (sawDiff) {
                    _pendingCauldrons.Remove((object)__instance);
                    LoggingHelper.Msg("Performing deferred consumption on visual update");
                    if (info.ConsumedSlot == -1) {
                        // never consumed yet – normal path
                        if (TryMarkConsumed((object)__instance)) {
                            var tuple = ApplyFlexibleConsumptionToCauldron(__instance);
                            int slot = tuple.primarySlot;
                            int extra = tuple.extraConsumed;
                            // nothing else needed; we already removed from table
                        }
                    } else if (info.ConsumedSlot != actualSlot && info.ExtraAmount > 0) {
                        CorrectConsumedSlot(__instance, info.ConsumedSlot, actualSlot, info.ExtraAmount);
                    }
                }
            }
        } catch { }
      } catch (Exception ex) {
        LoggingHelper.Error("Visuals slot enumeration failed", ex);
      }
    }

    // network helpers also eventually call through to FinishCookOperation but
    // we intercept here as well just in case.  the connection argument is
    // no longer guaranteed to exist so it's omitted.
    // the networking helpers still usually have an operation argument but
    // there’s been at least one game update where the connection parameter was
    // removed entirely.  use the flexible-args pattern here too to avoid
    // harmony compile errors and make future updates easier.
    public static void Prefix_SendCookOperation(dynamic __instance,
                                                params object[] __args) {
      LoggingHelper.Msg($"Prefix_SendCookOperation invoked (args={__args.Length})");
      // take a snapshot before vanilla does its 20-leaf deduction.  the
      // timing of SendCookOperation is inconsistent – the game may already have
      // removed the base 20 leaves when this prefix runs.  We prefer to seed
      // from the most recent visual snapshot, but only if that snapshot isn’t
      // stale (i.e. the current inventory hasn’t grown since it was taken).
      try {
          // first compute the current counts
          int[] currCounts = null;
          var slots = GetSlotsArray((object)__instance);
          if (slots != null) {
              currCounts = new int[slots.Length];
              for (int i = 0; i < slots.Length; i++)
                  currCounts[i] = GetSlotCount(slots.GetValue(i));
          }

          bool usedVisual = false;
          if (currCounts != null &&
              _visualSnapshotCache.TryGetValue((object)__instance, out var visPair) &&
              visPair != null && visPair.Length > 1 && visPair[1] != null) {
              var snap = visPair[1];
              bool snapshotIsOld = false;
              for (int i = 0; i < currCounts.Length; i++) {
                  if (currCounts[i] > snap[i]) { // items added since snapshot
                      snapshotIsOld = true;
                      break;
                  }
              }
              if (!snapshotIsOld) {
                  _preCookSnapshot.Remove((object)__instance);
                  _preCookSnapshot.Add((object)__instance, (int[])snap.Clone());
                  usedVisual = true;
                  if (LoggingHelper.EnableVerbose)
                      LoggingHelper.Msg("Prefix_SendCookOperation used visual snapshot for preCook");
              }
          }

          if (!usedVisual && currCounts != null) {
              _preCookSnapshot.Remove((object)__instance);
              _preCookSnapshot.Add((object)__instance, currCounts);
          }
      } catch { }

      // the args are just remainingCookTime and quality; no operation object
      // send may fire before start; if this is a new cook and the
      // cauldron isn't cooking yet, clear stale flag so we can consume later.
      try {
          if (!((bool)__instance.isCooking)) {
              ClearConsumed((object)__instance);
              LoggingHelper.Msg("SendCookOperation: cauldron not cooking, cleared consumed flag");
          }
      } catch { }

      // attempt to consume immediately; we will still keep a pending entry so
      // we can correct the slot later if needed.
      try {
          if (TryMarkConsumed((object)__instance)) {
              var tuple = ApplyFlexibleConsumptionToCauldron(__instance);
              int slot = tuple.primarySlot;
              int extra = tuple.extraConsumed;
              var info = new PendingInfo { ConsumedSlot = slot, ExtraAmount = extra };
              _pendingCauldrons.Remove((object)__instance);
              _pendingCauldrons.Add((object)__instance, info);
              LoggingHelper.Msg($"Immediate Send consumption slot={slot} extra={extra}");
          } else {
              LoggingHelper.Msg("Skipping duplicate consumption in SendCookOperation (pointer match)");
          }
      } catch { }
    }

    // postfix for send operation
    public static void Postfix_SendCookOperation(dynamic __instance) {
      try {
        // if we already have a pending info entry the prefix already did the
        // work (or scheduled a future correction), so there's nothing to do.
        if (_pendingCauldrons.TryGetValue((object)__instance, out _)) {
            LoggingHelper.Msg("Deferring SendCookOperation consumption until visual update");
            return;
        }

        if (TryMarkConsumed((object)__instance))
            ApplyFlexibleConsumptionToCauldron(__instance);
        else
            LoggingHelper.Msg("Skipping duplicate consumption in SendCookOperation (pointer match)");
      } catch { }
    }

    // similar to SendCookOperation, omit connection parameter which may
    // have been removed by the game update
    public static void Prefix_SetCookOperation(dynamic __instance,
                                               params object[] __args) {
      LoggingHelper.Msg($"Prefix_SetCookOperation invoked (args={__args.Length})");
      // snapshot the slots like the other prefixes
      try {
          var slots = GetSlotsArray((object)__instance);
          if (slots != null) {
              var arr = new int[slots.Length];
              for (int i = 0; i < slots.Length; i++)
                  arr[i] = GetSlotCount(slots.GetValue(i));
              _preCookSnapshot.Remove((object)__instance);
              _preCookSnapshot.Add((object)__instance, arr);
          }
      } catch { }
    }

    public static void Postfix_SetCookOperation(dynamic __instance) {
      try {
        if (TryMarkConsumed((object)__instance))
            ApplyFlexibleConsumptionToCauldron(__instance);
        else
            LoggingHelper.Msg("Skipping duplicate consumption in SetCookOperation (pointer match)");
      } catch { }
    }
  }
}

