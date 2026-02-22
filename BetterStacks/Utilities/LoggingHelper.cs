using System;
using MelonLoader;

namespace BetterStacks.Utilities
{
    internal static class LoggingHelper
    {
        // When building in DEBUG configuration we want all of the existing
        // informational messages to appear; in RELEASE they should be muted so
        // the log only contains warnings/errors and a handful of "initialisation"
        // entries.  The verbose flag is compiled into the assembly for a cheap
        // runtime check.
        private static bool Verbose
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

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
        /// Only emits output when <see cref="Verbose"/> is true (typically DEBUG
        /// builds).  In RELEASE builds the majority of informational messages are
        /// suppressed.
        /// </summary>
        public static void Msg(string msg)
        {
            if (Verbose)
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