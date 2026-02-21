using System;
using MelonLoader;

namespace BetterStacks.Utilities
{
    internal static class LoggingHelper
    {
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
        /// </summary>
        public static void Msg(string msg) => MelonLogger.Msg(SanitizeForLog(msg));

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