using System;


namespace BetterStacksF.Utilities {
  internal static class ReflectionHelper {
    // caching avoids redundant GetProperty/GetField calls for every definition
    // encountered.  the value tuple also serves as a quick membership test in
    // FilterDefinitionsWithStackLimit.  We cache the *member* infos, not the
    // actual stack-limit values.
    //
    // Although the helpers are currently invoked only on the main thread, we
    // may end up calling them from other contexts (network callbacks or
    // future background tasks).  use a concurrent dictionary to avoid any
    // potential race conditions when populating the cache.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        Type,
        (System.Reflection.PropertyInfo? prop, System.Reflection.FieldInfo? field)>
        _stackLimitMemberCache =
            new System.Collections.Concurrent.ConcurrentDictionary<
                Type,
                (System.Reflection.PropertyInfo? prop, System.Reflection.FieldInfo? field)>();

    private static (System.Reflection.PropertyInfo? prop, System.Reflection.FieldInfo? field) GetOrCacheStackLimitMembers(Type targetType) {
      // use GetOrAdd to ensure the reflection work is done only once per type,
      // even if multiple threads race to query the cache simultaneously.
      return _stackLimitMemberCache.GetOrAdd(
          targetType,
          type => {
            var prop = type.GetProperty("StackLimit");
            var field = prop == null ? type.GetField("StackLimit") : null;
            return (prop, field);
          });
    }

    public static bool HasStackLimit(Type t) {
      var (prop, field) = GetOrCacheStackLimitMembers(t);
      return (prop != null && prop.CanRead) || field != null;
    }

    /// <summary>
    /// Attempts to get a <c>StackLimit</c> property or field value from an object.
    /// </summary>
    /// <returns><c>true</c> when a value was read successfully.</returns>
    public static bool TryGetStackLimit(object def, out int currentStack) {
      currentStack = 0;
      var defType = def.GetType();
      var (prop, field) = GetOrCacheStackLimitMembers(defType);
      if ((prop == null || !prop.CanRead) && field == null)
        return false;
      try {
        if (prop != null)
          currentStack = Convert.ToInt32(prop.GetValue(def));
        else if (field != null)
          currentStack = Convert.ToInt32(field.GetValue(def));
        return true;
      }
      catch (Exception ex) {
        // include the exception type to make post‑mortem diagnostics easier
        LoggingHelper.Warning($"TryGetStackLimit reflection failed ({ex.GetType().Name}): {ex.Message}");
        return false;
      }
    }

    public static bool TrySetStackLimit(object def, int value) {
      var defType = def.GetType();
      var (prop, field) = GetOrCacheStackLimitMembers(defType);
      try {
        if (prop != null && prop.CanWrite) {
          prop.SetValue(def, value);
          return true;
        }
        else if (field != null) {
          field.SetValue(def, value);
          return true;
        }
      }
      catch (Exception ex) {
        LoggingHelper.Warning($"TrySetStackLimit reflection failed ({ex.GetType().Name}): {ex.Message}");
      }
      return false;
    }
  }
}
