using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BetterStacksF.Utilities {
  /// <summary>
  /// Lightweight runtime guard for optional S1API integration.
  ///
  /// This helper avoids hard dependencies on the S1API assembly by using
  /// reflection to locate and invoke S1API types/members only when the
  /// assembly is present.
  /// </summary>
  internal static class S1ApiCompat {
    private static readonly Lazy<bool> _isAvailable = new Lazy<bool>(CheckAvailability);

    public static bool IsAvailable => _isAvailable.Value;

    private static bool CheckAvailability() {
      // Use Type.GetType so we don't throw when the assembly is missing.
      return GetType("S1API.Lifecycle.GameLifecycle, S1API") != null;
    }

    private static Type? GetType(string typeName) {
      try {
        return Type.GetType(typeName, throwOnError: false);
      }
      catch {
        return null;
      }
    }

    public static bool TrySubscribeToGameLifecycleEvent(string eventName, Action handler) {
      if (!IsAvailable || handler == null) return false;

      var lifecycleType = GetType("S1API.Lifecycle.GameLifecycle, S1API");
      if (lifecycleType == null) return false;

      var ev = lifecycleType.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
      if (ev == null) return false;

      try {
        ev.AddEventHandler(null, handler);
        return true;
      }
      catch (Exception ex) {
        LoggingHelper.Error($"Failed to subscribe to S1API GameLifecycle event '{eventName}'", ex);
        return false;
      }
    }

    public static bool TryUnsubscribeFromGameLifecycleEvent(string eventName, Action handler) {
      if (!IsAvailable || handler == null) return false;

      var lifecycleType = GetType("S1API.Lifecycle.GameLifecycle, S1API");
      if (lifecycleType == null) return false;

      var ev = lifecycleType.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
      if (ev == null) return false;

      try {
        ev.RemoveEventHandler(null, handler);
        return true;
      }
      catch (Exception ex) {
        LoggingHelper.Error($"Failed to unsubscribe from S1API GameLifecycle event '{eventName}'", ex);
        return false;
      }
    }

    public static bool TryGetAllItemDefinitions(out IReadOnlyList<object>? definitions) {
      definitions = null;
      if (!IsAvailable) return false;

      var itemManagerType = GetType("S1API.Items.ItemManager, S1API");
      if (itemManagerType == null) return false;

      var method = itemManagerType.GetMethod("GetAllItemDefinitions", BindingFlags.Public | BindingFlags.Static);
      if (method == null) return false;

      try {
        var result = method.Invoke(null, null);
        if (result is IReadOnlyList<object> list) {
          definitions = list;
          return true;
        }

        // Some versions may return IList or IEnumerable
        if (result is IEnumerable enumerable) {
          definitions = enumerable.Cast<object>().ToList();
          return true;
        }

        return false;
      }
      catch (Exception ex) {
        LoggingHelper.Error("Failed to invoke S1API.Items.ItemManager.GetAllItemDefinitions", ex);
        return false;
      }
    }
  }
}
