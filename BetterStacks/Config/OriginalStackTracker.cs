using System.Collections.Generic;

namespace BetterStacks.Config
{
    /// <summary>
    /// Tracks original stack-limit values for the current session.
    /// </summary>
    internal static class OriginalStackTracker
    {
        private static readonly Dictionary<string,int> _savedOriginals = new();

        public static int? GetSavedOriginalStackLimit(string defId)
        {
            return _savedOriginals.TryGetValue(defId, out var v) ? (int?)v : null;
        }

        public static void PersistOriginalStackLimit(string defId, int originalLimit)
        {
            _savedOriginals[defId] = originalLimit;
        }

        public static bool HasSavedOriginals() => _savedOriginals.Count > 0;
    }
}