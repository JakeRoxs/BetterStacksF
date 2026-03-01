using System;

namespace BetterStacksF.Networking {
  // Single-player/no-network fallback — acts as host so behavior matches single-player.
  public class LocalNetworkAdapter : INetworkAdapter {
    public bool IsHost => true;
    public bool IsInitialized => true;
    public event Action<HostConfig>? OnHostConfigReceived;
    public void Initialize() { }
    public void Dispose() { }
    public void ProcessIncomingMessages() { }
    public void BroadcastHostConfig(HostConfig cfg) => OnHostConfigReceived?.Invoke(cfg);
  }
}
