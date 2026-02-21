using BetterStacks.Utilities;

namespace BetterStacks.Networking
{
    internal static class MultiplayerHelper
    {
        /// <summary>
        /// Determines if the current process is a client connected to a host in a multiplayer session.
        /// </summary>
        public static bool IsClientInMultiplayer()
        {
            var adapter = NetworkingManager.CurrentAdapter;
            if (adapter == null || !adapter.IsInitialized) return false;
            if (adapter.IsHost) return false; // local or host session

            if (adapter is SteamNetworkAdapter steam)
            {
                try
                {
                    if (steam.IsInLobby && steam.LobbyMemberCount > 1 && !steam.IsHost)
                        return true;
                }
                catch (System.Exception ex)
                {
                    LoggingHelper.Warning($"Error checking Steam lobby state: {ex.Message}");
                }
                return false;
            }

            // conservative fallback: non-Steam adapters currently never report multiplayer
            return false;
        }
    }
}