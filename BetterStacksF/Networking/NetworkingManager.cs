using BetterStacksF.Utilities;

namespace BetterStacksF.Networking {
  // Lightweight manager that owns the active adapter (defaults to LocalNetworkAdapter).
  public static class NetworkingManager {
    public static INetworkAdapter CurrentAdapter { get; private set; } = new LocalNetworkAdapter();

    public static void Initialize(INetworkAdapter adapter) {
      var targetAdapter = adapter ?? new LocalNetworkAdapter();

      // If reusing the same adapter and it is already initialized, avoid a second Initialize call.
      if (ReferenceEquals(CurrentAdapter, targetAdapter) && CurrentAdapter.IsInitialized) {
        return;
      }

      // Swap to a different adapter or uninitialized same adapter.
      if (!ReferenceEquals(CurrentAdapter, targetAdapter)) {
        if (CurrentAdapter != null) {
          CurrentAdapter.OnHostConfigReceived -= HostConfigEnforcer.ApplyHostConfig;
          CurrentAdapter.Dispose();
        }
        CurrentAdapter = targetAdapter;
      }

      if (!CurrentAdapter.IsInitialized) {
        CurrentAdapter.Initialize();
      } else {
        LoggingHelper.Msg("[NetworkingManager] Current adapter already initialized, skipping Initialize call");
      }

      CurrentAdapter.OnHostConfigReceived -= HostConfigEnforcer.ApplyHostConfig;
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
