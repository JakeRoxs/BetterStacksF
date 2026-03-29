using System;

using BetterStacksF.Utilities;

using MelonLoader;

using Newtonsoft.Json;

using SteamNetworkLib;

namespace BetterStacksF.Networking {
  // Strongly-typed SteamNetworkLib adapter (compile-time dependency).
  // Replaces the previous reflection-based implementation and requires
  // SteamNetworkLib to be available at build time.
  public class SteamNetworkAdapter : INetworkAdapter {
    private SteamNetworkClient? _client;
    private string? _lastLobbyHostConfigValue;


    public bool IsHost => _client?.IsHost ?? false;
    public bool IsInitialized { get; private set; } = false;

    // Expose whether the underlying SteamNetworkClient is currently in a lobby/session.
    public bool IsInLobby => _client?.IsInLobby ?? false;

    // How many members are in the current lobby (0 if not in a lobby).
    public int LobbyMemberCount => _client?.CurrentLobby?.MemberCount ?? 0;


    public event Action<HostConfig>? OnHostConfigReceived;

    /// <summary>
    /// Create and initialize the underlying <see cref="SteamNetworkClient"/>.
    /// Callers must ensure SteamNetworkLib is ready (this typically means
    /// scheduling the call from a post‑load lifecycle event such as
    /// <c>GameLifecycle.OnLoadComplete</c>).  If initialization fails the
    /// adapter remains uninitialized and the caller may choose to fall back to
    /// another adapter.
    /// </summary>
    public void Initialize() {
      TryInitClient();
    }

    // internal so the mod initializer can invoke it directly; the public
    // <see cref="Initialize"/> method simply forwards here.
    internal void TryInitClient() {
      LoggingHelper.Msg($"[SteamNetworkAdapter] TryInitClient invoked (now={DateTime.UtcNow:O})");
      try {
        _client = new SteamNetworkClient();
        _client.Initialize();
        IsInitialized = true;
        LoggingHelper.Init("[SteamNetworkAdapter] typed adapter initialized");
      }
      catch (Exception initEx) {
        // failures during early initialization are expected until Steamworks
        // becomes fully ready.  log a verbose/informational message instead
        // of a warning so startup isn't cluttered when Steam is just slow.
        bool isSteamNotReady = initEx.Message.Contains("Steamworks is not initialized");
        if (isSteamNotReady) {
          LoggingHelper.Msg("[SteamNetworkAdapter] initialization deferred (Steamworks not ready, retrying shortly)");
        } else {
          LoggingHelper.Warning($"[SteamNetworkAdapter] Initialize failed: {initEx.Message}");
        }
        try { _client?.Dispose(); } catch { }
        _client = null;
        IsInitialized = false;
        // caller may choose to fall back if initialization didn't work
      }
    }

    public void Dispose() {
      try { _client?.Dispose(); } catch { }
      _client = null;
      IsInitialized = false;
    }

    public void ProcessIncomingMessages() {
      if (!IsInitialized || _client == null)
        return;

      var client = _client!; // non-null now

      try {
        client.ProcessIncomingMessages();

        // If we're not in a Steam lobby, skip lobby-data handling — the game often doesn't
        // initialize lobby networking until the player actively creates/joins a lobby.
        if (!client.IsInLobby) return;

        var value = client.GetLobbyData("BetterStacks_HostConfig");
        if (value != _lastLobbyHostConfigValue) {
          _lastLobbyHostConfigValue = value;
          if (!string.IsNullOrEmpty(value))
            ProcessHostConfig(value);
        }
      }
      catch (Exception ex) {
        // Mark the adapter as not initialized to prevent log spam and repeated failures.
        IsInitialized = false;
        LoggingHelper.Warning($"[SteamNetworkAdapter] ProcessIncomingMessages error: {ex.Message}");
      }

    }

    private void ProcessHostConfig(string value) {
      try {
        var hostCfg = JsonConvert.DeserializeObject<HostConfig>(value);
        if (hostCfg != null)
          OnHostConfigReceived?.Invoke(hostCfg);
      }
      catch (Exception ex) {
        LoggingHelper.Warning($"[SteamNetworkAdapter] HostConfig deserialize failed: {ex.Message}");
      }
    }

    public void BroadcastHostConfig(HostConfig cfg) {
      if (!IsHost) {
        LoggingHelper.Msg("[SteamNetworkAdapter] BroadcastHostConfig ignored — not host");
        return;
      }

      if (_client == null) {
        LoggingHelper.Warning("[SteamNetworkAdapter] client not initialized");
        return;
      }

      try {
        var json = JsonConvert.SerializeObject(cfg ?? new HostConfig());
        _client.SetLobbyData("BetterStacks_HostConfig", json);
        LoggingHelper.Msg("[SteamNetworkAdapter] HostConfig set to lobby data");
      }
      catch (Exception ex) {
        LoggingHelper.Warning($"[SteamNetworkAdapter] BroadcastHostConfig failed: {ex.Message}");
      }
    }
  }
}
