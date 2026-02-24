namespace BetterStacks.Networking {
  // Lightweight manager that owns the active adapter (defaults to LocalNetworkAdapter).
  public static class NetworkingManager {
    public static INetworkAdapter CurrentAdapter { get; private set; } = new LocalNetworkAdapter();

    public static void Initialize(INetworkAdapter adapter) {
      CurrentAdapter = adapter ?? new LocalNetworkAdapter();
      CurrentAdapter.Initialize();
      CurrentAdapter.OnHostConfigReceived += HostConfigEnforcer.ApplyHostConfig;
    }

    public static void Shutdown() {
      if (CurrentAdapter is not null) {
        CurrentAdapter.OnHostConfigReceived -= HostConfigEnforcer.ApplyHostConfig;
        CurrentAdapter.Dispose();
      }
      CurrentAdapter = new LocalNetworkAdapter();
    }

    public static void BroadcastHostConfig(HostConfig cfg) => CurrentAdapter.BroadcastHostConfig(cfg);
  }
}
