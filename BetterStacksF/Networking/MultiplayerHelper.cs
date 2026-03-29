using BetterStacksF.Utilities;

namespace BetterStacksF.Networking {
  internal static class MultiplayerHelper {
    /// <summary>
    /// Determines if the current process is a client connected to a host in a multiplayer session.
    /// </summary>
    public static bool IsClientInMultiplayer() {
      var adapter = NetworkingManager.CurrentAdapter;
      if (adapter == null || !adapter.IsInitialized) return false;
      if (adapter.IsHost) return false; // local or host session

      var adapterType = adapter.GetType();
      if (string.Equals(adapterType.FullName, "BetterStacksF.Networking.SteamNetworkAdapter", StringComparison.Ordinal)) {
        try {
          var inLobby = adapterType.GetProperty("IsInLobby")?.GetValue(adapter) as bool?;
          var memberCount = adapterType.GetProperty("LobbyMemberCount")?.GetValue(adapter) as int?;
          var isHost = adapterType.GetProperty("IsHost")?.GetValue(adapter) as bool?;

          if (inLobby == true && memberCount.GetValueOrDefault() > 1 && isHost == false)
            return true;
        }
        catch (System.Exception ex) {
          LoggingHelper.Warning($"Error checking Steam lobby state: {ex.Message}");
        }
        return false;
      }

      // conservative fallback: non-Steam adapters currently never report multiplayer
      return false;
    }
  }
}
