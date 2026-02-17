using System;

namespace BetterStacks.Networking
{
    public interface INetworkAdapter : IDisposable
    {
        bool IsHost { get; }
        bool IsInitialized { get; }
        event Action<HostConfig>? OnHostConfigReceived;
        void Initialize();
        void ProcessIncomingMessages();
        void BroadcastHostConfig(HostConfig cfg);
    }
}