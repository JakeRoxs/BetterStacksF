using System;
using MelonLoader;

namespace BetterStacks.Utilities
{
    internal static class LoggingHelper
    {
        // Controls whether the bulk of informational messages are written to the
        // log.  In DEBUG builds the flag is initialized to true, in RELEASE builds
        // it defaults to false, but callers may override it at runtime (e.g. via
        // a console command or in-game setting) to enable/disable verbose output
        // on the fly.  This replaces the previous compile‑time only `Verbose`
        // property.
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
        public static string SanitizeForLog(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            var builder = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
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
        public static void Msg(string msg)
        {
            if (EnableVerbose)
                MelonLogger.Msg(SanitizeForLog(msg));
        }

        /// <summary>
        /// Write a message that should always appear in the log, regardless of
        /// the build configuration.  This is intended for a small number of
        /// "basic initialisation" entries the user still wants to see in
        /// release output.
        /// </summary>
        public static void Init(string msg)
        {
            MelonLogger.Msg(SanitizeForLog(msg));
        }

        /// <summary>
        /// Write a warning to the log, sanitizing the text first.
        /// </summary>
        public static void Warning(string msg) => MelonLogger.Warning(SanitizeForLog(msg));

        /// <summary>
        /// Write an error to the log, sanitizing the text first.
        /// </summary>
        public static void Error(string msg) => MelonLogger.Error(SanitizeForLog(msg));
    }
}