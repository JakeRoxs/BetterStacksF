using System;

using MelonLoader;

using Newtonsoft.Json;

namespace BetterStacksF.Utilities {
  internal static class LoggingHelper {
    // Controls whether the bulk of informational messages are written to the
    // log.  A user-visible preference ("Verbose logging") is exposed via
    // MelonPreferences and the config system mirrors that value here; debug
    // builds default the preference to true while release builds default to
    // false.  The project file defines DEBUG only for the Debug configuration
    // so a proper Release compile will not enable verbose output unexpectedly.
    // Callers may also override the flag at runtime (e.g. via a console
    // command) to toggle verbose logging without rebuilding.  This replaces
    // the previous compile‑time only `Verbose` property.
    public static bool EnableVerbose { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Removes control characters (including null) from a string so it is safe for logging.
    /// Used by multiple classes to avoid duplicated implementations.
    /// </summary>
    public static string SanitizeForLog(string? s) {
      if (string.IsNullOrEmpty(s))
        return string.Empty;
      var builder = new System.Text.StringBuilder(s.Length);
      foreach (char c in s) {
        if (c >= 0x20) // drop control codes (including '\0')
          builder.Append(c);
      }
      return builder.ToString();
    }

    /// <summary>
    /// Write a standard info message to the log, sanitizing the text first.
    /// Messages are only emitted when <see cref="EnableVerbose"/> is true; the
    /// flag can be toggled at runtime to turn verbose logging on/off without a
    /// rebuild.
    /// </summary>
    public static void Msg(string msg) {
      if (EnableVerbose)
        MelonLogger.Msg(SanitizeForLog(msg));
    }

    /// <summary>
    /// Write a message that should always appear in the log, regardless of
    /// the build configuration.  This is intended for a small number of
    /// "basic initialisation" entries the user still wants to see in
    /// release output.
    /// </summary>
    public static void Init(string msg) {
      MelonLogger.Msg(SanitizeForLog(msg));
    }

    /// <summary>
    /// Write a verbose informational message.  If <paramref name="data"/>
    /// is supplied the object will be serialized to JSON and appended to the
    /// message.  The entry is only emitted when <see cref="EnableVerbose"/>
    /// is true.
    /// </summary>
    public static void Msg(string message, object? data, bool indented = true) {
      if (!EnableVerbose) return;
      var text = message;
      if (data != null)
        text += ": " + Serialize(data, indented);
      Msg(text);
    }

    /// <summary>
    /// Write an init-level log entry.  If <paramref name="data"/>
    /// is supplied the object will be serialized to JSON and appended to the
    /// message, using a newline to separate the description from the payload.
    /// </summary>
    public static void Init(string message, object? data, bool indented = true) {
      var text = message;
      if (data != null)
        text += ":\n" + Serialize(data, indented);
      Init(text);
    }

    /// <summary>
    /// Convert an object to a JSON string.  Uses indented formatting if
    /// requested; callers that simply need a string representation (for
    /// logging, network messages, etc.) can call this rather than referencing
    /// <c>JsonConvert</c> directly.
    /// </summary>
    public static string Serialize(object? obj, bool indented = false) {
      if (obj == null)
        return string.Empty;

      try {
        return JsonConvert.SerializeObject(obj, indented ? Formatting.Indented : Formatting.None);
      }
      catch (Exception ex) {
        // Problems during serialization shouldn't propagate to callers, and
        // if we attempt to log via our normal helpers we risk re‑entering
        // Serialize and blowing the stack.  The SafeLogError(msg, ex)
        // overload writes straight to MelonLogger and sanitizes its input so
        // the failure can be recorded without triggering another exception.
        SafeLogError("Serialize failed", ex);
        return string.Empty;
      }
    }

    /// <summary>
    /// Deserialize JSON text into the requested type.  Small convenience wrapper
    /// around <c>JsonConvert</c> for consistency with <see cref="Serialize"/>.
    /// </summary>
    public static T? Deserialize<T>(string json) {
      if (string.IsNullOrEmpty(json))
        return default;

      try {
        return JsonConvert.DeserializeObject<T>(json);
      }
      catch (Exception ex) {
        // A malformed or unexpected JSON string shouldn't crash the caller.
        SafeLogError("Deserialize failed", ex);
        return default;
      }
    }

    /// <summary>
    /// Write a warning to the log, sanitizing the text first.
    /// </summary>
    public static void Warning(string msg) => MelonLogger.Warning(SanitizeForLog(msg));

    /// <summary>
    /// Write a warning message.  If <paramref name="data"/> is supplied the
    /// object will be serialized to JSON and appended to the text.
    /// </summary>
    public static void Warning(string msg, object? data, bool indented = true) {
      if (data != null)
        msg += ": " + Serialize(data, indented);
      MelonLogger.Warning(SanitizeForLog(msg));
    }

    /// <summary>
    /// Write an error to the log, sanitizing the text first.
    /// </summary>
    public static void Error(string msg) => MelonLogger.Error(SanitizeForLog(msg));

    /// <summary>
    /// Write an error message.  If <paramref name="data"/> is supplied the
    /// object will be serialized to JSON and appended to the text.
    /// </summary>
    public static void Error(string msg, object? data, bool indented = true) {
      if (data != null)
        msg += ": " + Serialize(data, indented);
      MelonLogger.Error(SanitizeForLog(msg));
    }

    /// <summary>
    /// Write an error message along with an exception.  Both values are
    /// sanitized before being written to the log, and the exception text is
    /// emitted on a separate line so the stack trace is easier to read in the
    /// log file.  Inner exceptions and the full <see cref="Exception.ToString"/>
    /// output are preserved.
    /// </summary>
    /// <param name="msg">Primary error message describing what failed.</param>
    /// <param name="ex">Exception object to include; its <c>ToString()</c>
    /// output (stack trace, inner exceptions) will be sanitized and logged.
    /// </param>
    public static void Error(string msg, Exception ex)
        => MelonLogger.Error(SanitizeForLog(msg) + "\n" + SanitizeForLog(ex.ToString()));

    // Helper used internally by the serialization methods to avoid recursive
    // calls into Serialize/Deserialize.  This writes directly to MelonLogger
    // and sanitizes the message so that the act of logging cannot itself
    // trigger another exception.
    private static void SafeLogError(string msg) => MelonLogger.Error(SanitizeForLog(msg));

    // alternate overload with exception for convenience
    private static void SafeLogError(string msg, Exception ex)
        => MelonLogger.Error(SanitizeForLog(msg) + "\n" + SanitizeForLog(ex.ToString()));
  }
}
