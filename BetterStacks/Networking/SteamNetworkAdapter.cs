using System;
using MelonLoader;
using Newtonsoft.Json;
using SteamNetworkLib;

namespace BetterStacks.Networking
{
    // Strongly-typed SteamNetworkLib adapter (compile-time dependency).
    // Replaces the previous reflection-based implementation and requires
    // SteamNetworkLib to be available at build time.
    public class SteamNetworkAdapter : INetworkAdapter
    {
        private SteamNetworkClient? _client;
        private string? _lastLobbyHostConfigValue;

        public bool IsHost => _client?.IsHost ?? false;
        public bool IsInitialized { get; private set; } = false;

        public event Action<HostConfig>? OnHostConfigReceived;

        public void Initialize()
        {
            try
            {
                _client = new SteamNetworkClient();
                try
                {
                    _client.Initialize();
                    IsInitialized = true;
                    MelonLogger.Msg("[Better Stacks][SteamNetworkAdapter] typed adapter initialized");
                }
                catch (Exception initEx)
                {
                    MelonLogger.Warning($"[Better Stacks][SteamNetworkAdapter] Initialize failed: {initEx.Message}");
                    try { _client.Dispose(); } catch { }
                    _client = null;
                    IsInitialized = false;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Better Stacks][SteamNetworkAdapter] Initialize failed: {ex.Message}");
                _client = null;
                IsInitialized = false;
            }
        }

        public void Dispose()
        {
            try { _client?.Dispose(); } catch { }
            _client = null;
            IsInitialized = false;
        }

        public void ProcessIncomingMessages()
        {
            // Avoid calling into SteamNetworkClient unless it was initialized successfully.
            if (!IsInitialized || _client == null) return;

            try
            {
                _client.ProcessIncomingMessages();

                var value = _client.GetLobbyData("BetterStacks_HostConfig");
                if (value != _lastLobbyHostConfigValue)
                {
                    _lastLobbyHostConfigValue = value;
                    if (!string.IsNullOrEmpty(value))
                    {
                        try
                        {
                            var hostCfg = JsonConvert.DeserializeObject<HostConfig>(value);
                            if (hostCfg != null) OnHostConfigReceived?.Invoke(hostCfg);
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning($"[Better Stacks][SteamNetworkAdapter] HostConfig deserialize failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Mark the adapter as not initialized to prevent log spam and repeated failures.
                IsInitialized = false;
                MelonLogger.Warning($"[Better Stacks][SteamNetworkAdapter] ProcessIncomingMessages error: {ex.Message}");
            }
        }

        public void BroadcastHostConfig(HostConfig cfg)
        {
            if (!IsHost)
            {
                MelonLogger.Msg("[Better Stacks][SteamNetworkAdapter] BroadcastHostConfig ignored — not host");
                return;
            }

            if (_client == null)
            {
                MelonLogger.Warning("[Better Stacks][SteamNetworkAdapter] client not initialized");
                return;
            }

            try
            {
                var json = JsonConvert.SerializeObject(cfg ?? new HostConfig());
                _client.SetLobbyData("BetterStacks_HostConfig", json);
                MelonLogger.Msg("[Better Stacks][SteamNetworkAdapter] HostConfig set to lobby data");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Better Stacks][SteamNetworkAdapter] BroadcastHostConfig failed: {ex.Message}");
            }
        }
    }
}