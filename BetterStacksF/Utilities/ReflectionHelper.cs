using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;


namespace BetterStacksF.Utilities {
  internal static class ReflectionHelper {

    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Run <paramref name="action"/> inside a try/catch and log any exception
    /// along with the supplied <paramref name="context"/> message.  This
    /// consolidates repetitive error‑logging boilerplate used by many patches.
    /// </summary>
    public static void TryCatchLog(Action action, string context)
    {
        try { action(); }
        catch (Exception ex) { LoggingHelper.Error(context, ex); }
    }

    /// <summary>
    /// Generic helper that uses a <see cref="ConditionalWeakTable"/> to cache
    /// reflected <see cref="FieldInfo"/> lookups. Each object can have multiple
    /// field names cached safely in a per-object inner map.
    /// </summary>
    public static T? GetFieldValueCached<T>(object obj, string fieldName,
                                             ConditionalWeakTable<object, ConcurrentDictionary<string, FieldInfo?>> cache)
    {
        if (obj == null) return default;
        try {
            var map = cache.GetValue(obj, _ => new ConcurrentDictionary<string, FieldInfo?>());
            if (!map.TryGetValue(fieldName, out var fi)) {
                fi = obj.GetType().GetField(fieldName, InstanceFlags);
                map[fieldName] = fi;
            }
            if (fi == null) return default;
            var value = fi.GetValue(obj);
            return value is T t ? t : default;
        }
        catch (Exception ex) {
            LoggingHelper.Error($"GetFieldValueCached<{typeof(T).Name}> failed for {fieldName}", ex);
            return default;
        }
    }

    /// <summary>
    /// Compatibility overload for callers that supply a single-field cache; this
    /// is less robust than the dictionary overload and should only be used when
    /// fieldName is constant per cache.
    /// </summary>
    public static T? GetFieldValueCached<T>(object obj, string fieldName,
                                             ConditionalWeakTable<object, FieldInfo?> cache)
    {
        if (obj == null) return default;
        try {
            var fi = cache.GetValue(obj, o =>
                o.GetType().GetField(fieldName, InstanceFlags));
            if (fi == null) return default;
            var value = fi.GetValue(obj);
            return value is T t ? t : default;
        }
        catch (Exception ex) {
            LoggingHelper.Error($"GetFieldValueCached<{typeof(T).Name}> failed for {fieldName}", ex);
            return default;
        }
    }

    // track which types we've already logged
    private static readonly ConcurrentDictionary<Type, byte> _dumpedTypes
        = new ConcurrentDictionary<Type, byte>();

    public static void DumpObject(object obj, string? prefix = null) {
      if (obj == null) {
        LoggingHelper.Msg("DumpObject: null");
        return;
      }

      try {
        var type = obj.GetType();
        // TryAdd returns false if the type was already present
        if (!_dumpedTypes.TryAdd(type, 0)) return; // already logged

        prefix ??= string.Empty;
        LoggingHelper.Msg($"{prefix}DumpObject {type.FullName} fields/properties:");
        foreach (var f in type.GetFields(BindingFlags.Instance |
                                         BindingFlags.Public |
                                         BindingFlags.NonPublic)) {
          try { LoggingHelper.Msg($" {prefix}{f.Name} = {f.GetValue(obj)}"); } catch { }
        }
        foreach (var p in type.GetProperties(BindingFlags.Instance |
                                             BindingFlags.Public |
                                             BindingFlags.NonPublic)) {
          if (!p.CanRead) continue;
          try { LoggingHelper.Msg($" {prefix}{p.Name} = {p.GetValue(obj)}"); } catch { }
        }
      }
      catch (Exception ex) {
        LoggingHelper.Error("DumpObject failed", ex);
      }
    }

    /// <summary>
    /// Clear the internal cache of dumped types.  Useful when returning to the
    /// main menu or whenever you want subsequent <see cref="DumpObject"/>
    /// calls to re-log types.
    /// </summary>
    public static void ResetDumpCache() => _dumpedTypes.Clear();
  }
}
