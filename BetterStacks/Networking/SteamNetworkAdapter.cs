using System;
using MelonLoader;
using Newtonsoft.Json;
using BetterStacks.Utilities;
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

        // Deferred initialization helpers — Steam may not be ready at OnInitializeMelon time.
        private bool _deferredInit = false;
        private bool _deferredInitLogged = false;
        private DateTime _nextInitAttempt = DateTime.MinValue;
        private static readonly TimeSpan InitRetryInterval = TimeSpan.FromSeconds(5);

        public bool IsHost => _client?.IsHost ?? false;
        public bool IsInitialized { get; private set; } = false;

        // Expose whether the underlying SteamNetworkClient is currently in a lobby/session.
        public bool IsInLobby => _client?.IsInLobby ?? false;

        // How many members are in the current lobby (0 if not in a lobby).
        public int LobbyMemberCount => _client?.CurrentLobby?.MemberCount ?? 0;

        // True when initialization is deferred because Steam wasn't ready at first attempt.
        public bool InitializationDeferred => _deferredInit;

        public event Action<HostConfig>? OnHostConfigReceived;

        public void Initialize()
        {
            try
            {
                // Always defer actual SteamNetworkClient construction to the per-frame processing loop.
                // Calling into Steamworks synchronously during mod initialization can race and throw
                // "Steamworks is not initialized" even when SteamNetworkUtils reports available.
                // ProcessIncomingMessages will perform the real TryInitClient() with backoff when Steam is ready.
                _deferredInit = true;
                _nextInitAttempt = DateTime.UtcNow; // allow the loop to attempt init immediately when ready
                if (!_deferredInitLogged)
                {
                    LoggingHelper.Msg("[SteamNetworkAdapter] Initialization deferred — will attempt to initialize when Steam is ready (processed in the update loop).");
                    _deferredInitLogged = true;
                }
                return;
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning($"[SteamNetworkAdapter] Initialize failed: {ex.Message}");
                _client = null;
                IsInitialized = false;
            }
        }

        private void TryInitClient()
        {
            try
            {
                _client = new SteamNetworkClient();
                _client.Initialize();
                IsInitialized = true;
                _deferredInit = false;
                LoggingHelper.Msg("[SteamNetworkAdapter] typed adapter initialized");
            }
            catch (Exception initEx)
            {
                LoggingHelper.Warning($"[SteamNetworkAdapter] Initialize failed: {initEx.Message}");
                try { _client?.Dispose(); } catch { }
                _client = null;
                IsInitialized = false;
                // Mark as deferred so ProcessIncomingMessages will retry with backoff instead of failing permanently.
                _deferredInit = true;
                // Back off before next attempt to avoid spamming the log.
                _nextInitAttempt = DateTime.UtcNow + InitRetryInterval;
            }
        }

        public void Dispose()
        {
            try { _client?.Dispose(); } catch { }
            _client = null;
            IsInitialized = false;
            _deferredInit = false;
            _deferredInitLogged = false;
            _nextInitAttempt = DateTime.MinValue;
        }

        public void ProcessIncomingMessages()
        {
            // If initialization was deferred because Steam wasn't ready, attempt it again (with backoff).
            if (!IsInitialized)
            {
                if (_deferredInit && SteamNetworkLib.Utilities.SteamNetworkUtils.IsSteamInitialized() && DateTime.UtcNow >= _nextInitAttempt)
                    TryInitClient();

                // Nothing to process until the client is initialized.
                if (!IsInitialized || _client == null) return;
            }

            // Capture a local copy so flow analysis can prove non-nullability.
            var client = _client;
            if (client == null) // defensive but should never hit
                return;

            try
            {
                client.ProcessIncomingMessages();

                // If we're not in a Steam lobby, skip lobby-data handling — the game often doesn't
                // initialize lobby networking until the player actively creates/joins a lobby.
                if (!client.IsInLobby) return;

                var value = client.GetLobbyData("BetterStacks_HostConfig");
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
                            LoggingHelper.Warning($"[SteamNetworkAdapter] HostConfig deserialize failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Mark the adapter as not initialized to prevent log spam and repeated failures.
                IsInitialized = false;
                LoggingHelper.Warning($"[SteamNetworkAdapter] ProcessIncomingMessages error: {ex.Message}");
            }
        }

        public void BroadcastHostConfig(HostConfig cfg)
        {
            if (!IsHost)
            {
                LoggingHelper.Msg("[SteamNetworkAdapter] BroadcastHostConfig ignored — not host");
                return;
            }

            if (_client == null)
            {
                LoggingHelper.Warning("[SteamNetworkAdapter] client not initialized");
                return;
            }

            try
            {
                var json = JsonConvert.SerializeObject(cfg ?? new HostConfig());
                _client.SetLobbyData("BetterStacks_HostConfig", json);
                LoggingHelper.Msg("[SteamNetworkAdapter] HostConfig set to lobby data");
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning($"[SteamNetworkAdapter] BroadcastHostConfig failed: {ex.Message}");
            }
        }
    }
}