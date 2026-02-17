<!-- Converted from PDF (text-only) -->

# Combined conversion — doc/tdoc.pdf + doc/toc.pdf

## tdoc.pdf

Introduction
What is SteamNetworkLib?
SteamNetworkLib is a powerful C# wrapper library built on top of Steamworks.NET that
dramatically simpliﬁes Steam networking functionality for Unity games and applications. It
provides a clean, intuitive API for common networking tasks that would otherwise require
extensive boilerplate code and deep knowledge of the Steamworks API.
Why SteamNetworkLib?
Working directly with Steamworks.NET can be challenging and error-prone. Especially for
Schedule 1 mods, where you need to manage both Mono and IL2CPP branches of
Steamworks.NET. SteamNetworkLib addresses these pain points by providing:
🚀  Simpliﬁed API
One-line operations for complex networking tasks
Async/await support for modern C# development
Intuitive method names that clearly express intent
Comprehensive error handling with meaningful exceptions
🎯  Focused Functionality
Lobby Management — Create, join, and manage Steam lobbies eﬀortlessly
Data Synchronization — Simple key-value data sharing between players
P2P Communication — Reliable peer-to-peer messaging system
Member Management — Track players and their data automatically
🔧  Developer-Friendly
Extensive documentation with practical examples
Full XML documentation for IntelliSense support
MelonLoader optimized for modding scenarios
Key Features (at a glance)
Lobby: CreateLobbyAsync, JoinLobbyAsync, GetLobbyMembers()
Data: SetLobbyData, SetMyData, GetPlayerData
P2P: RegisterMessageHandler<T>, SendMessageToPlayerAsync, BroadcastMessageAsync
See the dedicated guides for details:
Lobby Management
Data Synchronization

P2P Messaging
Events and Error Handling
Recipes
Architecture Overview
SteamNetworkLib is designed with modularity and ease of use in mind:
SteamNetworkClient — Your main interface to all functionality
SteamLobbyManager — Handles lobby creation, joining, and member management
SteamLobbyData — Manages lobby-wide key-value data storage
SteamMemberData — Manages per-player key-value data storage
SteamP2PManager — Handles reliable message passing between players
Getting Started
Ready to dive in? Head over to the Getting Started guide to learn how to integrate
SteamNetworkLib into your project in just a few minutes!
SteamNetworkClient (Main Entry Point)
├── SteamLobbyManager (Lobby operations)
├── SteamLobbyData (Lobby-wide data)
├── SteamMemberData (Player-specific data)
└── SteamP2PManager (Peer-to-peer communication)

Getting Started
This guide walks you through setting up SteamNetworkLib in a Unity game mod using
MelonLoader and implementing the minimal loop.
Installation
Prerequisites
1. MelonLoader installed on the target Unity game
2. Steam client running
3. Unity game with Steam integration
4. Basic C# and MelonLoader modding knowledge
5. Visual Studio or VS Code
Add SteamNetworkLib to Your Mod Project
Target .NET Standard 2.1 (works for Mono and Il2Cpp) and reference SteamNetworkLib:
Add references to:
MelonLoader.dll
UnityEngine.dll
Assembly-CSharp.dll
SteamNetworkLib.dll
Minimal mod setup
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyTitle>YourAwesomeMod</AssemblyTitle>
    <AssemblyVersion>1.0.0</AssemblyVersion>
  </PropertyGroup>
</Project>
using MelonLoader;
using SteamNetworkLib;
public class YourAwesomeModMain : MelonMod
{
    private SteamNetworkClient client;
    public override void OnInitializeMelon()

Best Practices
Use unique preﬁxes for your mod's data keys to avoid collisions with other mods. See
Data Synchronization for details.
From here, pick the guide you need next:
Lobby Management: create/join/leave/invite
Data Synchronization: lobby and member data
P2P Messaging: typed messages, ﬁles, channels
Events and Errors: event model and exceptions
Recipes: copy-paste snippets for common tasks
    {
        // Optional: configure network rules (relay, session policy, channels)
        var rules = new SteamNetworkLib.Core.NetworkRules
        {
            EnableRelay = true,
            AcceptOnlyFriends = false
        };
        client = new SteamNetworkClient(rules);
        if (client.Initialize())
        {
            // Optional: subscribe to events
            client.OnLobbyCreated += (s, e) => MelonLogger.Msg($"Lobby: 
{e.Lobby.LobbyId}");
        }
    }
    public override void OnUpdate()
    {
        client?.ProcessIncomingMessages();
    }
    public override void OnDeinitializeMelon()
    {
        client?.Dispose();
    }
}

Lobby Management
This page covers creating, joining, leaving, and inviting players to Steam lobbies using
SteamNetworkClient.
Quick start
Events
Subscribe to lobby lifecycle and membership changes:
SteamNetworkClient client = new SteamNetworkClient();
client.Initialize();
// Create a friends-only lobby with up to 8 members
LobbyInfo lobby = await client.CreateLobbyAsync(ELobbyType.k_ELobbyTypeFriendsOnly, 
8);
// Join an existing lobby
await client.JoinLobbyAsync(lobbyId);
// Leave the current lobby
client.LeaveLobby();
// Read current members
List<MemberInfo> members = client.GetLobbyMembers();
// Invite a friend or open Steam overlay invite
client.InviteFriend(friendId);
client.OpenInviteDialog();
client.OnLobbyCreated += (s, e) =>
{
    MelonLogger.Msg($"Lobby created: {e.Lobby.LobbyId}");
};
client.OnLobbyJoined += (s, e) =>
{
    MelonLogger.Msg($"Joined lobby: {e.Lobby.LobbyId}");
};
client.OnLobbyLeft += (s, e) =>
{
    MelonLogger.Msg($"Left lobby {e.LobbyId}: {e.Reason}");

Event args reference:
LobbyCreatedEventArgs
LobbyJoinedEventArgs
LobbyLeftEventArgs
MemberJoinedEventArgs
MemberLeftEventArgs
Tips
Only the lobby owner can change lobby-wide data; see Data Synchronization.
Check client.IsInLobby and client.IsHost for state-aware UI and commands.
Use Steam overlay invites for the smoothest UX: client.OpenInviteDialog().
};
client.OnMemberJoined += (s, e) =>
{
    MelonLogger.Msg($"Member joined: {e.Member.DisplayName}");
};
client.OnMemberLeft += (s, e) =>
{
    MelonLogger.Msg($"Member left: {e.Member.DisplayName}");
};

Data Synchronization
Recommendation: For most use cases, use Synchronized Variables (SyncVars)
instead. SyncVars provide a cleaner API with automatic type safety, validation, rate
limiting, and built-in preﬁx handling via NetworkSyncOptions.KeyPrefix.
Use lobby-wide and per-player key-value data for lightweight shared state like versions,
ﬂags, and small strings.
Raw API vs SyncVars
SyncVars are recommended over the raw lobby/member data API for most use cases:
Feature Raw API SyncVars
Preﬁx handlingManual Automatic via NetworkSyncOptions.KeyPrefix
Type safety String-only Type-safe generic T
Validation Manual Built-in validators
Rate limiting Manual Built-in MaxSyncsPerSecond
Events Raw change eventsTyped OnValueChanged events
Batch updatesSetMyDataBatch() Auto-sync batching
Use SyncVars for game state, player data, and most mod data. See Synchronized
Variables (SyncVars) for details.
Important: Use Unique Preﬁxes
Always use custom preﬁxes for your mod's data keys to avoid collisions with
other mods.
// Good: Use a unique prefix for your mod
const string PREFIX = "MyMod_";
client.SetLobbyData($"{PREFIX}version", "1.0.0");
client.SetMyData($"{PREFIX}loadout", "1911");
// Bad: Generic keys may collide with other mods

Tip: SyncVars handle preﬁxes automatically. Just set KeyPrefix in NetworkSyncOptions:
Lobby-wide data (host-only)
Per-player data
Change events
client.SetLobbyData("version", "1.0.0");  // May conflict!
client.SetMyData("loadout", "1911");      // May conflict!
var options = new NetworkSyncOptions { KeyPrefix = "MyMod_" };
var score = client.CreateHostSyncVar("Score", 0, options);
// Actual Steam key: "MyMod_Score" - no manual prefix needed!
// Set by the lobby owner
client.SetLobbyData("mod_version", "1.0.0");
// Read by anyone
string modVersion = client.GetLobbyData("mod_version");
// Local player sets their visible data
client.SetMyData("name", "bob");
// Read for self
string myClass = client.GetMyData("name");
// Read for any specific player
string otherClass = client.GetPlayerData(playerId, "name");
// Read the same key for everyone
Dictionary<CSteamID, string> allClasses = client.GetDataForAllPlayers("name");
client.OnLobbyDataChanged += (s, e) =>
{
    MelonLogger.Msg($"Lobby data: {e.Key} -> {e.NewValue}");
};
client.OnMemberDataChanged += (s, e) =>
{

Version compatibility helper
Version checks are enabled by default. The client stores its library version under a reserved
key and triggers OnVersionMismatch when players diﬀer.
Batch updates
When to use P2P instead
Use data keys for small strings/ﬂags.
For large payloads (ﬁles, images, audio), use the P2P Messaging API.
    MelonLogger.Msg($"Member {e.MemberId}: {e.Key} -> {e.NewValue}");
};
client.OnVersionMismatch += (s, e) =>
{
    MelonLogger.Warning($"Version mismatch. Local: {e.LocalVersion}");
};
// Optional: toggle
client.VersionCheckEnabled = true;
// Manual check
bool ok = client.CheckLibraryVersionCompatibility();
client.SetMyDataBatch(new Dictionary<string, string>
{
    ["loadout"] = "1911",
    ["ready"] = "true",
});

Synchronized Variables (SyncVars)
SteamNetworkLib provides a high-level API for synchronized variables that automatically
keep values in sync across all lobby members with minimal boilerplate.
Quick Start
HostSyncVar - Host-Authoritative Data
Use HostSyncVar<T> when you need a single shared value that only the lobby host can
modify:
Key Points
Only the lobby host can set the value
Non-host writes are silently ignored (no exceptions)
Enable WarnOnIgnoredWrites in options for debugging
Uses Steam lobby data under the hood
ClientSyncVar - Per-Client Data
// Host-authoritative: only host can modify, all can read
var roundNumber = client.CreateHostSyncVar("Round", 1);
// Client-owned: each client owns their value, all can read everyone's
var isReady = client.CreateClientSyncVar("Ready", false);
// Create
var gameSettings = client.CreateHostSyncVar("Settings", new GameSettings());
var maxScore = client.CreateHostSyncVar("MaxScore", 100);
// Subscribe to changes
gameSettings.OnValueChanged += (oldVal, newVal) =>
{
    MelonLogger.Msg($"Settings changed!");
};
// Modify (only works for host - silently ignored otherwise)
maxScore.Value = 200;
// Read (works for everyone)
int current = maxScore.Value;

Use ClientSyncVar<T> when each client needs their own synced value:
Key Points
Each client can only modify their own value
All clients can read all other clients' values
Missing values return the default value
Uses Steam lobby member data under the hood
Supported Types
The default JSON serializer supports:
// Create
var isReady = client.CreateClientSyncVar("Ready", false);
var playerLoadout = client.CreateClientSyncVar("Loadout", "default");
// Subscribe to any client's changes
isReady.OnValueChanged += (playerId, oldVal, newVal) =>
{
    MelonLogger.Msg($"Player {playerId} ready: {newVal}");
};
// Subscribe to only my changes
isReady.OnMyValueChanged += (oldVal, newVal) =>
{
    MelonLogger.Msg($"I am now ready: {newVal}");
};
// Set my own value
isReady.Value = true;
// Read my value
bool myReady = isReady.Value;
// Read another client's value
bool player2Ready = isReady.GetValue(player2Id);
// Get all clients' values
Dictionary<CSteamID, bool> allReady = isReady.GetAllValues();
bool everyoneReady = allReady.Values.All(r => r);

Category Types
Primitives int, long, float, double, bool, string, byte, short, uint, ulong, decimal
Enums Any enum type (serialized as integer)
Collections List<T>, T[], Dictionary<string, T>
Custom Types Classes/structs with parameterless constructor and public properties
Custom Type Example
Requirements for custom types:
1. Must have a public parameterless constructor
2. Properties must be public with both getter and setter
3. Property types must themselves be serializable
4. No circular references
Conﬁguration Options
Customize behavior with NetworkSyncOptions:
public class GameSettings
{
    public int MaxPlayers { get; set; } = 4;
    public string MapName { get; set; } = "default";
    public bool FriendlyFire { get; set; } = false;
    public List<string> EnabledMods { get; set; } = new();
}
// Usage
var settings = client.CreateHostSyncVar("Settings", new GameSettings());
var options = new NetworkSyncOptions
{
    // Log warnings when non-host tries to write (debugging)
    WarnOnIgnoredWrites = true,
    
    // Add prefix to avoid key collisions with other mods (IMPORTANT!)
    KeyPrefix = "MyMod_",
    
    // Disable auto-sync for manual batching (see below)
    AutoSync = false,

KeyPreﬁx - Avoid Collisions
Always use a unique preﬁx for published mods to prevent key collisions with
other mods.
When using raw lobby/member data, apply preﬁxes manually:
Value Validation
Add validation constraints to ensure values meet requirements before syncing:
    
    // Rate limit syncs (e.g., 10 per second for position updates)
    MaxSyncsPerSecond = 10,
    
    // Throw exceptions on validation errors (default: false)
    ThrowOnValidationError = false,
    
    // Use custom serializer (optional)
    Serializer = new MyCustomSerializer()
};
var score = client.CreateHostSyncVar("Score", 0, options);
// Good: Use your mod name as prefix
var options = new NetworkSyncOptions { KeyPrefix = "MyMod_" };
var score = client.CreateHostSyncVar("Score", 0, options);
// Actual Steam key: "MyMod_Score"
var teamName = client.CreateClientSyncVar("TeamName", "Alpha", options);
// Actual Steam key: "MyMod_TeamName"
const string PREFIX = "MyMod_";
client.SetLobbyData($"{PREFIX}version", "1.0.0");
client.SetMyData($"{PREFIX}loadout", "1911");
// Range validation (built-in)
var scoreValidator = new RangeValidator<int>(0, 9999);
var score = client.CreateHostSyncVar("Score", 0, null, scoreValidator);
// Predicate validation (simple custom logic)
var teamNameValidator = new PredicateValidator<string>(
    value => value.Length >= 3 && value.Length <= 15,

Validation behavior:
Invalid values are rejected before syncing
By default, validation errors are logged and trigger OnSyncError
Set ThrowOnValidationError = true to throw exceptions instead
Failed writes do not change the current value
Rate Limiting
Limit how frequently a SyncVar can sync to prevent network spam:
    "Team name must be 3-15 characters"
);
var teamName = client.CreateClientSyncVar("Team", "Alpha", null, teamNameValidator);
// Composite validation (combine multiple rules)
var usernameValidator = new CompositeValidator<string>(
    new PredicateValidator<string>(
        v => v.Length >= 3 && v.Length <= 20,
        "Username must be 3-20 characters"
    ),
    new PredicateValidator<string>(
        v => char.IsLetter(v[0]),
        "Username must start with a letter"
    )
);
var positionOptions = new NetworkSyncOptions
{
    MaxSyncsPerSecond = 10  // Max 10 position updates per second
};
var positionX = client.CreateClientSyncVar("PosX", 0f, positionOptions);
// Rapid updates - automatically throttled to 10/sec
for (int i = 0; i < 100; i++)
{
    positionX.Value = i * 0.1f;  // Only ~10 of these will actually sync
}
// Check if there's a pending value waiting to sync
if (positionX.IsDirty)
{
    // Force immediate sync, bypassing rate limit

Batch Syncing / Manual Sync
Disable AutoSync to make multiple changes before syncing:
Use cases for manual syncing:
Atomic multi-variable updates
Reducing network traﬃc when changing multiple values
Deferring sync until a speciﬁc game event
Advanced Example
For a comprehensive example demonstrating validation, rate limiting, and batch syncing,
see:
Examples/AdvancedSyncVarExample.cs
This example includes:
Range validation with error handling
Rate-limited position updates
Batch syncing for state transitions
Custom validators with complex rules
Interactive hotkeys to test each feature
    positionX.FlushPending();
}
var batchOptions = new NetworkSyncOptions { AutoSync = false };
var gamePhase = client.CreateHostSyncVar("Phase", "Lobby", batchOptions);
var roundNumber = client.CreateHostSyncVar("Round", 0, batchOptions);
// Make multiple changes locally - nothing syncs yet
gamePhase.Value = "InGame";
roundNumber.Value = 1;
// Check which vars have unsaved changes
if (gamePhase.IsDirty || roundNumber.IsDirty)
{
    // Sync all changes at once
    gamePhase.FlushPending();
    roundNumber.FlushPending();
}

Custom Serialization
Implement ISyncSerializer for custom serialization:
Complete Example
For a comprehensive, production-ready example demonstrating all SyncVar features, see:
Examples/SyncVarExample.cs
This example includes:
Host-authoritative game state (round tracking, settings, timer)
Client-owned state (ready system, teams, loadouts)
Custom serializable types (classes and enums)
Event handling and error management
Interactive test hotkeys (F1-F7)
Ready-check system with real game logic
Run the example to see SyncVars in action with live synchronization across multiple clients.
Lifecycle Management
public class MySerializer : ISyncSerializer
{
    public string Serialize<T>(T value)
    {
        // Your serialization logic
    }
    
    public T Deserialize<T>(string data)
    {
        // Your deserialization logic
    }
    
    public bool CanSerialize(Type type)
    {
        // Return true if type is supported
    }
}
// Usage
var options = new NetworkSyncOptions { Serializer = new MySerializer() };
var data = client.CreateHostSyncVar("Data", myValue, options);

SyncVars are automatically cleaned up when:
You leave a lobby (OnLobbyLeft)
The SteamNetworkClient is disposed
No manual disposal required! Just create them and forget about cleanup.
Error Handling
When to Use Which
Use Case SyncVar Type
Game settings HostSyncVar
Round/match state HostSyncVar
Shared timer HostSyncVar
// Create sync vars
var score = client.CreateHostSyncVar("Score", 0);
var ready = client.CreateClientSyncVar("Ready", false);
// Use them...
score.Value = 100;
ready.Value = true;
// When you leave the lobby or dispose the client,
// all sync vars are automatically disposed - no cleanup code needed!
var score = client.CreateHostSyncVar("Score", 0);
// Subscribe to sync errors
score.OnSyncError += (exception) =>
{
    MelonLogger.Error($"Sync error: {exception.Message}");
};
// For debugging non-host writes
score.OnWriteIgnored += (attemptedValue) =>
{
    MelonLogger.Warning($"Write ignored: {attemptedValue}");
};

Use Case SyncVar Type
Player ready status ClientSyncVar
Player loadout/class ClientSyncVar
Player preferences ClientSyncVar
Per-player scores ClientSyncVar

P2P Messaging
Use the P2P layer to send reliable, typed messages and raw packets between players. Ideal
for chat, gameplay events, ﬁle chunks, and streaming.
Basics
Sending custom messages
Create a type by inheriting P2PMessage and implement MessageType, Serialize, Deserialize.
Step 1: Deﬁne your custom message class
// Register handlers via the high-level client API
client.RegisterMessageHandler<TextMessage>((msg, sender) =>
{
    MelonLogger.Msg($"Message from {sender}: {msg.Content}");
});
// Send to one player
await client.SendMessageToPlayerAsync(targetId, new TextMessage { Content = 
"Hello!" });
// Broadcast to everyone
await client.BroadcastMessageAsync(new TextMessage { Content = "Welcome!" });
// Pump incoming packets every frame (e.g., in Update)
client.ProcessIncomingMessages();
using System.Text;
using SteamNetworkLib.Models;
public class TransactionMessage : P2PMessage
{
    public override string MessageType => "TRANSACTION";
    public string TransactionId { get; set; } = string.Empty;
    public string FromPlayer { get; set; } = string.Empty;
    public string ToPlayer { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public override byte[] Serialize()
    {

Step 2: Register a handler for your custom message type
Step 3: Send and receive custom messages
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return Encoding.UTF8.GetBytes(json);
    }
    public override void Deserialize(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        var deserialized = 
System.Text.Json.JsonSerializer.Deserialize<TransactionMessage>(json);
        if (deserialized != null)
        {
            TransactionId = deserialized.TransactionId;
            FromPlayer = deserialized.FromPlayer;
            ToPlayer = deserialized.ToPlayer;
            Amount = deserialized.Amount;
            Currency = deserialized.Currency;
            SenderId = deserialized.SenderId;
            Timestamp = deserialized.Timestamp;
        }
    }
}
public override void OnInitializeMelon()
{
    client = new SteamNetworkClient();
    if (client.Initialize())
    {
        // Register handler - this automatically registers the custom type
        client.RegisterMessageHandler<TransactionMessage>(OnTransactionReceived);
    }
}
private void OnTransactionReceived(TransactionMessage message, CSteamID sender)
{
    MelonLogger.Msg($"Transaction {message.TransactionId}: 
{message.Amount} {message.Currency}");
}
// Send a custom message
var transaction = new TransactionMessage

How it works
The library receives message type identiﬁers as strings and needs a mapping to C# classes
for deserialization. When you call RegisterMessageHandler<T>(), the library automatically
registers your custom type. Built-in types (TEXT, DATA_SYNC, FILE_TRANSFER, STREAM,
HEARTBEAT, EVENT) are pre-registered.
Sending custom messages
Create a type by inheriting P2PMessage and implement MessageType, Serialize, Deserialize.
{
    TransactionId = "txn-12345",
    FromPlayer = "Player1",
    ToPlayer = "Player2",
    Amount = 100.00m,
    Currency = "USD"
};
await client.SendMessageToPlayerAsync(targetId, transaction);
// Or broadcast to all players
client.BroadcastMessage(transaction);
using System.Text;
public class CustomMessage : P2PMessage
{
    public override string MessageType => "CUSTOM";
    public string Payload { get; set; } = string.Empty;
    public override byte[] Serialize()
    {
        var json = $"{{{CreateJsonBase(\"\\\"Payload\\\":\\\"{Payload}\\\"\")}}}";
        return Encoding.UTF8.GetBytes(json);
    }
    public override void Deserialize(byte[] data)
    {
        var json = Encoding.UTF8.GetString(data);
        ParseJsonBase(json);
        Payload = ExtractJsonValue(json, "Payload");
    }
}

File transfer (chunked)
For ﬁles, send FileTransferMessage in chunks up to client.P2PManager.MaxPacketSize.
Channels and reliability
Default channel is 0; you can use multiple channels (e.g., 0 control, 1 ﬁles, 2 audio).
Use EP2PSend.k_EP2PSendReliable for reliability; for streams, prefer the message-
recommended send type.
Selecting channels and reliability automatically
Conﬁgure a policy once via NetworkRules.MessagePolicy and apply it at runtime:
client.RegisterMessageHandler<CustomMessage>((m, sender) => { /* ... */ });
await client.SendMessageToPlayerAsync(targetId, new CustomMessage { Payload = 
"Hi" });
var bytes = File.ReadAllBytes(path);
int chunkSize = client.P2PManager.MaxPacketSize; // use client wrappers for sending
int total = (int)Math.Ceiling((double)bytes.Length / chunkSize);
for (int i = 0; i < total; i++)
{
    var slice = bytes.Skip(i * chunkSize).Take(chunkSize).ToArray();
    var file = new FileTransferMessage
    {
        FileName = Path.GetFileName(path),
        FileSize = bytes.Length,
        ChunkIndex = i,
        TotalChunks = total,
        IsFileData = true,
        ChunkData = slice
    };
    await client.SendMessageToPlayerAsync(targetId, file);
}
// Streams on channel 1 using the message's recommended send type;
// everything else reliable on channel 0
client.NetworkRules.MessagePolicy = msg =>
{
    if (msg is StreamMessage s) return (1, s.RecommendedSendType);

Events and sessions
client.OnP2PMessageReceived ﬁres for any deserialized message.
P2P sessions are managed automatically by the client.
    return (0, client.NetworkRules.DefaultSendType);
};
client.UpdateNetworkRules(client.NetworkRules);

Network Rules
SteamNetworkLib exposes a lightweight Network Rules system to control Steamworks
behavior without touching low-level APIs.
Quick Start
Message Policy
Choose channel and send type per message (e.g., unreliable for streams):
Runtime Updates
Rules can be swapped at runtime; global settings like relay are applied immediately:
IL2CPP Channel Range
For IL2CPP builds, incoming packet polling scans a channel range:
MinReceiveChannel (default 0)
MaxReceiveChannel (default 3)
using SteamNetworkLib;
using SteamNetworkLib.Core;
var rules = new NetworkRules
{
    EnableRelay = true,          // Use Steam relay for NAT traversal
    AcceptOnlyFriends = false,   // Accept sessions from anyone
};
var client = new SteamNetworkClient(rules);
client.Initialize();
rules.MessagePolicy = msg =>
{
    if (msg is StreamMessage s)
        return (channel: 1, sendType: s.RecommendedSendType);
    return (channel: 0, sendType: rules.DefaultSendType);
};
rules.EnableRelay = false;
client.UpdateNetworkRules(rules);

Tune these if you segment traﬃc across channels.

Events and Error Handling
Understand the event model and exceptions to build resilient networking code.
Client events
Advanced P2P events exist on SteamP2PManager (packet-level and session events) if you
need low-level control.
Exceptions
SteamNetworkException: Base exception for library errors.
LobbyException: Lobby-speciﬁc failures (creation, join, invalid IDs).
P2PException: P2P send/receive/session issues (target, channel, session error).
client.OnLobbyCreated += (s, e) => { /* e.Lobby */ };
client.OnLobbyJoined  += (s, e) => { /* e.Lobby */ };
client.OnLobbyLeft    += (s, e) => { /* e.LobbyId, e.Reason */ };
client.OnMemberJoined += (s, e) => { /* e.Member */ };
client.OnMemberLeft   += (s, e) => { /* e.Member, e.Reason */ };
client.OnLobbyDataChanged  += (s, e) => { /* e.Key, e.OldValue, e.NewValue, 
e.ChangedBy */ };
client.OnMemberDataChanged += (s, e) => { /* e.MemberId, e.Key, e.OldValue, 
e.NewValue */ };
client.OnP2PMessageReceived += (s, e) =>
{
    // e.Message (P2PMessage), e.SenderId, e.Channel
};
client.OnVersionMismatch += (s, e) =>
{
    // e.LocalVersion, e.PlayerVersions, e.IncompatiblePlayers
};
try
{
    var lobby = await client.CreateLobbyAsync();
}
catch (LobbyException ex)
{
    MelonLogger.Error($"Lobby error: {ex.Message}");
}

IL2CPP speciﬁcs
client.ProcessIncomingMessages() internally calls SteamAPI.RunCallbacks() on IL2CPP, which
is required to drive Steam callbacks. Ensure you call it every frame (e.g., OnUpdate).
catch (SteamNetworkException ex)
{
    MelonLogger.Error($"Steam error: {ex.Message}");
}

Advanced API
For experienced users, SteamNetworkLib exposes its core components for direct control.
Use these when you need ﬁne‑grained behavior beyond the high‑level SteamNetworkClient
helpers.
Components
SteamLobbyManager — Create/join/leave lobbies, invites, member tracking.
SteamLobbyData — Lobby‑wide key/value store.
SteamMemberData — Per‑player key/value store.
SteamP2PManager — P2P packets/messages, sessions, channels, reliability.
NetworkRules — Runtime conﬁguration for relay, channel polling, session policy.
Access via:
P2P Manager
Sending
Receiving
Handlers
var client = new SteamNetworkClient(rules);
client.Initialize();
var lobby = client.LobbyManager;
var p2p   = client.P2PManager;
// Typed message
await p2p.SendMessageAsync(targetId, new TextMessage { Content = "Hi" }, channel: 0, 
sendType: EP2PSend.k_EP2PSendReliable);
// Raw packet
p2p.BroadcastPacket(bytes, channel: 1, sendType: EP2PSend.k_EP2PSendUnreliable);
// Call regularly (or use client.ProcessIncomingMessages())
p2p.ProcessIncomingPackets();
p2p.RegisterMessageHandler<TextMessage>((msg, sender) => { /* ... */ });

Sessions
Limits
Lobby Manager
Data APIs
Rules (Advanced)
var active = p2p.GetActiveSessions();
var state  = p2p.GetSessionState(someId);
p2p.CloseSession(someId);
int max = p2p.MaxPacketSize; // keep chunks <= max
// Create / Join
var lobbyInfo = await lobby.CreateLobbyAsync(ELobbyType.k_ELobbyTypeFriendsOnly, 
maxMembers: 4);
// await lobby.JoinLobbyAsync(lobbyId);
// Members & invites
var members = lobby.GetLobbyMembers();
lobby.InviteFriend(friendId);
// Leave
lobby.LeaveLobby();
// Lobby‐wide
var map = client.LobbyData.GetData("map");
client.LobbyData.SetData("map", "arena");
// Per‐player
client.MemberData.SetMemberData("class", "mage");
string? cls = client.MemberData.GetMemberData(playerId, "class");
// Update at runtime
var rules = client.NetworkRules;
rules.AcceptOnlyFriends = true;    // gate P2P session requests
rules.MinReceiveChannel = 0;       // IL2CPP polling range

Notes
Setting EnableRelay applies SteamNetworking.AllowP2PPacketRelay(...).
In IL2CPP, polling respects the channel range from NetworkRules.
High‑level handlers still ﬁre (client.OnP2PMessageReceived) when you use p2p directly.
rules.MaxReceiveChannel = 3;
client.UpdateNetworkRules(rules);

Recipes
Short, focused examples for common tasks.
Host-authoritative sync var
Per-client sync var
Broadcast a mod conﬁguration to everyone
RPC-like event to a single player
var roundNumber = client.CreateHostSyncVar("Round", 1);
// Subscribe to changes
roundNumber.OnValueChanged += (oldVal, newVal) =>
{
    MelonLogger.Msg($"Round {oldVal} -> {newVal}");
};
// Host sets new value (clients only read)
roundNumber.Value = 2;
var isReady = client.CreateClientSyncVar("Ready", false);
// Subscribe to any player's changes
isReady.OnValueChanged += (playerId, oldVal, newVal) =>
{
    MelonLogger.Msg($"Player {playerId}: ready={newVal}");
};
// Set my own ready status
isReady.Value = true;
// Check if everyone is ready
var allReady = isReady.GetAllValues().Values.All(r => r);
var cfg = new DataSyncMessage { Key = "mod_config", Value = 
JsonConvert.SerializeObject(config) };
await client.BroadcastMessageAsync(cfg);

Send a screenshot ﬁle to the host
Invite friends via Steam overlay
Check mod version compatibility
var evt = new EventMessage
{
    EventType = "give_item",
    Payload = "soil")
};
await client.SendMessageToPlayerAsync(targetId, evt);
var bytes = File.ReadAllBytes("screenshot.png");
int chunk = client.P2PManager.MaxPacketSize;
int total = (int)Math.Ceiling((double)bytes.Length / chunk);
for (int i = 0; i < total; i++)
{
    var slice = bytes.Skip(i * chunk).Take(chunk).ToArray();
    var msg = new FileTransferMessage
    {
        FileName = "screenshot.png",
        FileSize = bytes.Length,
        ChunkIndex = i,
        TotalChunks = total,
        IsFileData = true,
        ChunkData = slice
    };
    await client.SendMessageToPlayerAsync(hostId, msg, channel: 1);
}
client.OpenInviteDialog();
client.SetMyData("mod_version", MyMod.Version);
client.SyncModDataWithAllPlayers("mod_version", MyMod.Version);
if (!client.IsModDataCompatible("mod_version"))
{
    MelonLogger.Warning("Players have mismatched mod versions");
}

---

## toc.pdf

Classes
SteamNetworkClient
Main entry point for SteamNetworkLib - provides simpliﬁed access to all Steam
networking features. Perfect for use in MelonLoader mods that need Steam lobby and P2P
functionality.
Namespace SteamNetworkLib

Namespace:SteamNetworkLib
Assembly:SteamNetworkLib.dll
Main entry point for SteamNetworkLib - provides simpliﬁed access to all Steam networking
features. Perfect for use in MelonLoader mods that need Steam lobby and P2P functionality.
Inheritance
object  SteamNetworkClient
Implements
IDisposable
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the SteamNetworkClient class. Call Initialize() before using any
other methods.
Initializes a new instance of the SteamNetworkClient class with custom NetworkRules. Call
Initialize() before using any other methods.
Class SteamNetworkClient
public class SteamNetworkClient : IDisposable

SteamNetworkClient()
public SteamNetworkClient()
SteamNetworkClient(NetworkRules)
public SteamNetworkClient(NetworkRules rules)

Parameters
rules NetworkRules
Properties
Gets information about the current lobby, or null if not in a lobby.
Property Value
LobbyInfo
Gets whether the local player is the host of the current lobby.
Property Value
bool
Gets whether the local player is currently in a lobby.
Property Value
bool
CurrentLobby
public LobbyInfo? CurrentLobby { get; }
IsHost
public bool IsHost { get; }
IsInLobby
public bool IsInLobby { get; }

Gets the current version of SteamNetworkLib.
Property Value
string
Gets the lobby data manager for handling lobby-wide data.
Property Value
SteamLobbyData
Gets the lobby manager for handling Steam lobby operations.
Property Value
SteamLobbyManager
Gets the Steam ID of the local player.
LibraryVersion
public static string LibraryVersion { get; }
LobbyData
public SteamLobbyData? LobbyData { get; }
LobbyManager
public SteamLobbyManager? LobbyManager { get; }
LocalPlayerId
public CSteamID LocalPlayerId { get; }

Property Value
CSteamID
Gets the member data manager for handling player-speciﬁc data.
Property Value
SteamMemberData
Current network rules applied to P2P behavior.
Property Value
NetworkRules
Gets the P2P manager for handling peer-to-peer communication.
Property Value
SteamP2PManager
MemberData
public SteamMemberData? MemberData { get; }
NetworkRules
public NetworkRules NetworkRules { get; }
P2PManager
public SteamP2PManager? P2PManager { get; }
VersionCheckEnabled

Gets or sets whether automatic version checking is enabled. When enabled, the library will
automatically check for version compatibility between players.
Property Value
bool
Remarks
IMPORTANT: Version checking is crucial for ensuring proper data transfer and
synchronization between players.
Disabling this feature or ignoring version mismatches may result in:
Data serialization/deserialization failures
Message format incompatibilities
Synchronization errors
Unexpected networking behavior or crashes
It is strongly recommended to keep this enabled and ensure all players use the same
SteamNetworkLib version.
Methods
Sends a message to all players in the lobby. This is a non-async wrapper around
BroadcastMessageAsync.
Parameters
message P2PMessage
The message to broadcast.
Remarks
public bool VersionCheckEnabled { get; set; }
BroadcastMessage(P2PMessage)
public void BroadcastMessage(P2PMessage message)

This method is provided for backward compatibility. For new code, use
BroadcastMessageAsync instead.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
P2PException
Thrown when the message cannot be sent.
Sends a message to all players in the lobby.
Parameters
message P2PMessage
The message to broadcast.
Returns
Task
Remarks
This method sends the message to each player individually.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
P2PException
Thrown when the message cannot be sent.
BroadcastMessageAsync(P2PMessage)
public Task BroadcastMessageAsync(P2PMessage message)

Broadcasts a simple text message to all players. This is a non-async wrapper around
BroadcastTextMessageAsync.
Parameters
text string
The text message to broadcast.
Remarks
This method is provided for backward compatibility. For new code, use
BroadcastTextMessageAsync instead.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
P2PException
Thrown when the message cannot be sent.
Broadcasts a simple text message to all players.
Parameters
text string
The text message to broadcast.
Returns
BroadcastTextMessage(string)
public void BroadcastTextMessage(string text)
BroadcastTextMessageAsync(string)
public Task BroadcastTextMessageAsync(string text)

Task
A task that represents the asynchronous operation.
Remarks
This is a convenience method that creates a TextMessage internally.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
P2PException
Thrown when the message cannot be sent.
Performs a comprehensive version check and ﬁres the OnVersionMismatch event if
incompatibilities are found.
Returns
bool
True if all versions are compatible, false if mismatches were detected.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby.
CheckLibraryVersionCompatibility()
public bool CheckLibraryVersionCompatibility()

Creates a client-owned synchronized variable where each client can set their own value.
Parameters
key string
A unique key for this sync variable.
defaultValue T
The default value for clients who haven't set a value.
options NetworkSyncOptions
Optional conﬁguration options.
validator ISyncValidator<T>
Optional validator for value constraints.
Returns
ClientSyncVar<T>
A new ClientSyncVar<T> instance.
Type Parameters
T
The type of value to synchronize.
Remarks
Authority: Each client can only modify their own value. All clients can read all other
clients' values.
Storage: Uses Steam lobby member data, automatically synced by Steam.
CreateClientSyncVar<T>(string, T,
NetworkSyncOptions?, ISyncValidator<T>?)
public ClientSyncVar<T> CreateClientSyncVar<T>(string key, T defaultValue, 
NetworkSyncOptions? options = null, ISyncValidator<T>? validator = null)

Use Cases: Ready status, player loadouts, per-client preferences.
Validation: Optional validator can enforce constraints on values.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
ArgumentException
Thrown when key is null or empty.
SyncSerializationException
Thrown when the type T cannot be serialized.
Creates a host-authoritative synchronized variable.
// Create a client-owned sync var
var isReady = client.CreateClientSyncVar("Ready", false);
// With validation
var teamValidator = new RangeValidator<int>(1, 4);
var team = client.CreateClientSyncVar("Team", 1, null, teamValidator);
// Subscribe to any client's changes
isReady.OnValueChanged += (playerId, oldVal, newVal) => 
    MelonLogger.Msg($"Player {playerId} ready: {newVal}");
// Set my own value
isReady.Value = true;
// Read another player's value
bool player2Ready = isReady.GetValue(player2Id);
// Get all players' values
var allReady = isReady.GetAllValues();
bool everyoneReady = allReady.Values.All(r => r);
CreateHostSyncVar<T>(string, T,
NetworkSyncOptions?, ISyncValidator<T>?)

Parameters
key string
A unique key for this sync variable.
defaultValue T
The default value when no synced value exists.
options NetworkSyncOptions
Optional conﬁguration options.
validator ISyncValidator<T>
Optional validator for value constraints.
Returns
HostSyncVar<T>
A new HostSyncVar<T> instance.
Type Parameters
T
The type of value to synchronize.
Remarks
Authority: Only the lobby host can modify this value. Non-host writes are silently ignored
(or logged if WarnOnIgnoredWrites is enabled).
Storage: Uses Steam lobby data, automatically synced by Steam to all lobby members.
Use Cases: Game settings, round numbers, match state, or any host-controlled state.
Validation: Optional validator can enforce constraints on values (e.g., ranges, formats).
public HostSyncVar<T> CreateHostSyncVar<T>(string key, T defaultValue, 
NetworkSyncOptions? options = null, ISyncValidator<T>? validator = null)

Exceptions
InvalidOperationException
Thrown when the client is not initialized.
ArgumentException
Thrown when key is null or empty.
SyncSerializationException
Thrown when the type T cannot be serialized.
Creates a new lobby with the speciﬁed settings.
Parameters
lobbyType ELobbyType
The type of lobby to create.
maxMembers int
// Create a host-authoritative sync var
var roundNumber = client.CreateHostSyncVar("Round", 1);
// With validation
var scoreValidator = new RangeValidator<int>(0, 1000);
var score = client.CreateHostSyncVar("Score", 0, null, scoreValidator);
// Subscribe to changes
roundNumber.OnValueChanged += (oldVal, newVal) => 
    MelonLogger.Msg($"Round: {oldVal} -> {newVal}");
// Only host can modify - silently ignored for non-hosts
roundNumber.Value = 2;
CreateLobbyAsync(ELobbyType, int)
public Task<LobbyInfo> CreateLobbyAsync(ELobbyType lobbyType = 1, int maxMembers 
= 4)

The maximum number of members allowed in the lobby.
Returns
Task<LobbyInfo>
A task that represents the asynchronous operation. The task result contains the lobby
information.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when lobby creation fails.
Releases all resources used by the SteamNetworkClient.
Remarks
This method disposes all component managers and releases any unmanaged resources.
Gets the same data key for all players in the lobby.
Parameters
key string
The data key.
Dispose()
public void Dispose()
GetDataForAllPlayers(string)
public Dictionary<CSteamID, string> GetDataForAllPlayers(string key)

Returns
Dictionary<CSteamID, string>
A dictionary mapping player Steam IDs to their data values.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby.
Gets lobby-wide data.
Parameters
key string
The data key.
Returns
string
The data value, or null if not found.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby.
GetLobbyData(string)
public string? GetLobbyData(string key)

Gets all members in the current lobby.
Returns
List<MemberInfo>
A list of MemberInfo objects for all players in the lobby.
Remarks
Returns an empty list if not currently in a lobby.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
Gets data for the local player.
Parameters
key string
The data key.
Returns
string
The data value, or null if not found.
GetLobbyMembers()
public List<MemberInfo> GetLobbyMembers()
GetMyData(string)
public string? GetMyData(string key)

Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby.
Gets data for a speciﬁc player.
Parameters
playerId CSteamID
The Steam ID of the player.
key string
The data key.
Returns
string
The data value, or null if not found.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby or the player is not in the lobby.
GetPlayerData(CSteamID, string)
public string? GetPlayerData(CSteamID playerId, string key)

Gets the SteamNetworkLib versions of all players in the lobby.
Returns
Dictionary<CSteamID, string>
A dictionary mapping player Steam IDs to their SteamNetworkLib versions. Players
without version data are excluded.
Remarks
Use this method to identify which players have diﬀerent library versions that could cause
data transfer issues.
Players with missing version data may be using older versions of SteamNetworkLib that
don't support version checking,
which could result in unpredictable networking behavior and synchronization failures.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby.
Initializes the Steam networking client. Must be called before using any other methods.
Returns
GetPlayerLibraryVersions()
public Dictionary<CSteamID, string> GetPlayerLibraryVersions()
Initialize()
public bool Initialize()

bool
True if initialization was successful.
Exceptions
SteamNetworkException
Thrown when Steam is not available or when initialization fails.
Invites a friend to the current lobby.
Parameters
friendId CSteamID
The Steam ID of the friend to invite.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby or invitation fails.
Checks if all players have compatible mod data for a given key.
Parameters
InviteFriend(CSteamID)
public void InviteFriend(CSteamID friendId)
IsModDataCompatible(string)
public bool IsModDataCompatible(string dataKey)

dataKey string
The data key to check for compatibility.
Returns
bool
True if all players have the same data value, false otherwise.
Remarks
This method is useful for verifying that all players are using the same mod version.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby.
Joins an existing lobby by ID.
Parameters
lobbyId CSteamID
The Steam ID of the lobby to join.
Returns
Task<LobbyInfo>
A task that represents the asynchronous operation. The task result contains the lobby
information.
JoinLobbyAsync(CSteamID)
public Task<LobbyInfo> JoinLobbyAsync(CSteamID lobbyId)

Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when joining the lobby fails.
Leaves the current lobby.
Remarks
This method has no eﬀect if the local player is not currently in a lobby.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
Opens the Steam overlay invite dialog.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby or the overlay fails to open.
LeaveLobby()
public void LeaveLobby()
OpenInviteDialog()
public void OpenInviteDialog()

Processes incoming P2P packets. Call this regularly (e.g., in Update()).
Remarks
This method should be called frequently to ensure timely processing of incoming messages.
If not called regularly, messages may be delayed or dropped.
Registers a handler for a speciﬁc message type.
Parameters
handler Action<T, CSteamID>
The handler function that will be called when messages of this type are received.
Type Parameters
T
The type of message to handle.
Remarks
The handler will be called with the message and the sender's Steam ID.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
ProcessIncomingMessages()
public void ProcessIncomingMessages()
RegisterMessageHandler<T>(Action<T, CSteamID>)
public void RegisterMessageHandler<T>(Action<T, CSteamID> handler) where T : 
P2PMessage, new()

Sends a data synchronization message to a player.
Parameters
playerId CSteamID
The Steam ID of the target player.
key string
The data key.
value string
The data value.
dataType string
The data type identiﬁer.
Returns
Task<bool>
A task that represents the asynchronous operation. The task result indicates whether the
message was sent successfully.
Remarks
This is a convenience method that creates a DataSyncMessage internally.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
P2PException
SendDataSyncAsync(CSteamID, string, string, string)
public Task<bool> SendDataSyncAsync(CSteamID playerId, string key, string value, 
string dataType = "string")

Thrown when the message cannot be sent.
Sends a message to a speciﬁc player.
Parameters
playerId CSteamID
The Steam ID of the target player.
message P2PMessage
The message to send.
Returns
Task<bool>
A task that represents the asynchronous operation. The task result indicates whether the
message was sent successfully.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
P2PException
Thrown when the message cannot be sent.
Sends a simple text message to a player.
SendMessageToPlayerAsync(CSteamID, P2PMessage)
public Task<bool> SendMessageToPlayerAsync(CSteamID playerId, P2PMessage message)
SendTextMessageAsync(CSteamID, string)
public Task<bool> SendTextMessageAsync(CSteamID playerId, string text)

Parameters
playerId CSteamID
The Steam ID of the target player.
text string
The text message to send.
Returns
Task<bool>
A task that represents the asynchronous operation. The task result indicates whether the
message was sent successfully.
Remarks
This is a convenience method that creates a TextMessage internally.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
P2PException
Thrown when the message cannot be sent.
Sets lobby-wide data that is accessible to all players.
Parameters
key string
The data key.
SetLobbyData(string, string)
public void SetLobbyData(string key, string value)

value string
The data value.
Remarks
Only the lobby owner can set lobby data. This method will fail silently if called by a non-
owner.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby or not the lobby owner.
Sets data for the local player that is visible to all players.
Parameters
key string
The data key.
value string
The data value.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
SetMyData(string, string)
public void SetMyData(string key, string value)

Thrown when not in a lobby.
Sets multiple data values at once for the local player.
Parameters
data Dictionary<string, string>
A dictionary containing key-value pairs to set.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby.
Synchronizes data with all players in the lobby. This is a non-async wrapper around
SyncModDataWithAllPlayersAsync.
Parameters
dataKey string
The data key to synchronize.
dataValue string
SetMyDataBatch(Dictionary<string, string>)
public void SetMyDataBatch(Dictionary<string, string> data)
SyncModDataWithAllPlayers(string, string, string)
public void SyncModDataWithAllPlayers(string dataKey, string dataValue, string 
dataType = "string")

The data value to synchronize.
dataType string
The data type identiﬁer.
Remarks
This method is provided for backward compatibility. For new code, use
SyncModDataWithAllPlayersAsync instead.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby.
Synchronizes data with all players in the lobby. Useful for mod compatibility checks and
data synchronization.
Parameters
dataKey string
The data key to synchronize.
dataValue string
The data value to synchronize.
dataType string
The data type identiﬁer.
SyncModDataWithAllPlayersAsync(string, string, string)
public Task SyncModDataWithAllPlayersAsync(string dataKey, string dataValue, string 
dataType = "string")

Returns
Task
A task that represents the asynchronous operation.
Remarks
This method both sets the local player's data and broadcasts it to all other players.
Exceptions
InvalidOperationException
Thrown when the client is not initialized.
LobbyException
Thrown when not in a lobby.
Updates network rules at runtime and propagates to managers.
Parameters
rules NetworkRules
Events
Occurs when a new lobby is created.
Event Type
UpdateNetworkRules(NetworkRules)
public void UpdateNetworkRules(NetworkRules rules)
OnLobbyCreated
public event EventHandler<LobbyCreatedEventArgs>? OnLobbyCreated

EventHandler<LobbyCreatedEventArgs>
Occurs when lobby data is changed.
Event Type
EventHandler<LobbyDataChangedEventArgs>
Occurs when the local player joins a lobby.
Event Type
EventHandler<LobbyJoinedEventArgs>
Occurs when the local player leaves a lobby.
Event Type
EventHandler<LobbyLeftEventArgs>
Occurs when member data is changed.
OnLobbyDataChanged
public event EventHandler<LobbyDataChangedEventArgs>? OnLobbyDataChanged
OnLobbyJoined
public event EventHandler<LobbyJoinedEventArgs>? OnLobbyJoined
OnLobbyLeft
public event EventHandler<LobbyLeftEventArgs>? OnLobbyLeft
OnMemberDataChanged

Event Type
EventHandler<MemberDataChangedEventArgs>
Occurs when a new member joins the current lobby.
Event Type
EventHandler<MemberJoinedEventArgs>
Occurs when a member leaves the current lobby.
Event Type
EventHandler<MemberLeftEventArgs>
Occurs when a P2P message is received from another player.
Event Type
EventHandler<P2PMessageReceivedEventArgs>
public event EventHandler<MemberDataChangedEventArgs>? OnMemberDataChanged
OnMemberJoined
public event EventHandler<MemberJoinedEventArgs>? OnMemberJoined
OnMemberLeft
public event EventHandler<MemberLeftEventArgs>? OnMemberLeft
OnP2PMessageReceived
public event EventHandler<P2PMessageReceivedEventArgs>? OnP2PMessageReceived

Occurs when a version mismatch is detected between players in the lobby.
Event Type
EventHandler<VersionMismatchEventArgs>
Remarks
CRITICAL: This event indicates that players are using incompatible versions of
SteamNetworkLib.
Version mismatches can cause serious issues including data corruption, synchronization
failures, and networking errors.
OnVersionMismatch
public event EventHandler<VersionMismatchEventArgs>? OnVersionMismatch

Classes
NetworkRules
Conﬁgurable network rules that inﬂuence how SteamNetworkLib behaves.
SteamLobbyData
Manages Steam lobby data (global key-value storage for the lobby). Provides functionality
for getting lobby-wide data that is accessible to all players. Only the lobby host can set
and manage the lobby data.
SteamLobbyManager
Manages Steam lobby operations including creation, joining, leaving, and member
management. Provides core functionality for handling Steam lobbies and their associated
events.
SteamMemberData
Manages Steam lobby member data (per-player key-value storage). Provides functionality
for setting, getting, and managing player-speciﬁc data that is visible to all lobby
members.
SteamP2PManager
Manages Steam P2P networking for direct player-to-player communication. Provides
functionality for sending messages, managing sessions, and handling P2P events.
Namespace SteamNetworkLib.Core

Namespace:SteamNetworkLib.Core
Assembly:SteamNetworkLib.dll
Conﬁgurable network rules that inﬂuence how SteamNetworkLib behaves.
Inheritance
object  NetworkRules
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Properties
If true, only accept P2P sessions from friends (auto-ﬁlter in callback).
Property Value
bool
Class NetworkRules
public class NetworkRules

NetworkRules()
public NetworkRules()
AcceptOnlyFriends
public bool AcceptOnlyFriends { get; set; }

Default send type when a policy does not supply one.
Property Value
EP2PSend
Enables Steam relay usage for NAT traversal. Applied via
SteamNetworking.AllowP2PPacketRelay().
Property Value
bool
Maximum channel index to poll for incoming packets (IL2CPP).
Property Value
int
Optional message policy to choose channel and send type per message. If null,
DefaultSendType and caller-provided channel are used.
DefaultSendType
public EP2PSend DefaultSendType { get; set; }
EnableRelay
public bool EnableRelay { get; set; }
MaxReceiveChannel
public int MaxReceiveChannel { get; set; }
MessagePolicy

Property Value
Func<P2PMessage, (int channel, EP2PSend sendType)>
Minimum channel index to poll for incoming packets (IL2CPP).
Property Value
int
public Func<P2PMessage, (int channel, EP2PSend sendType)> MessagePolicy { get; 
set; }
MinReceiveChannel
public int MinReceiveChannel { get; set; }

Namespace:SteamNetworkLib.Core
Assembly:SteamNetworkLib.dll
Manages Steam lobby data (global key-value storage for the lobby). Provides functionality
for getting lobby-wide data that is accessible to all players. Only the lobby host can set and
manage the lobby data.
Inheritance
object  SteamLobbyData
Implements
IDisposable
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the SteamLobbyData class.
Parameters
lobbyManager SteamLobbyManager
The lobby manager instance to use for lobby operations.
Exceptions
Class SteamLobbyData
public class SteamLobbyData : IDisposable

SteamLobbyData(SteamLobbyManager?)
public SteamLobbyData(SteamLobbyManager? lobbyManager)

ArgumentNullException
Thrown when lobbyManager is null.
Methods
Clears all lobby data. Only the lobby host can perform this operation.
Exceptions
LobbyException
Thrown when not in a lobby or not the lobby host.
Releases all resources used by the SteamLobbyData.
Gets all lobby data as a dictionary.
Returns
Dictionary<string, string>
A dictionary containing all key-value pairs of lobby data, or an empty dictionary if not in a
lobby.
ClearAllData()
public void ClearAllData()
Dispose()
public void Dispose()
GetAllData()
public Dictionary<string, string> GetAllData()

Gets lobby-wide data by key.
Parameters
key string
The data key to retrieve.
Returns
string
The data value if found, or null if not found or not in a lobby.
Exceptions
ArgumentException
Thrown when the key is invalid.
Gets the number of data entries in the lobby.
Returns
int
The count of lobby data entries, or 0 if not in a lobby.
Gets a list of all data keys in the lobby.
GetData(string)
public string? GetData(string key)
GetDataCount()
public int GetDataCount()
GetDataKeys()

Returns
List<string>
A list of all data keys, or an empty list if not in a lobby.
Checks whether lobby data exists for the speciﬁed key.
Parameters
key string
The data key to check.
Returns
bool
True if the key exists and has a non-empty value, false otherwise.
Refreshes the lobby data cache by reloading all data from Steam servers.
Removes lobby data for the speciﬁed key.
public List<string> GetDataKeys()
HasData(string)
public bool HasData(string key)
RefreshData()
public void RefreshData()
RemoveData(string)

Parameters
key string
The data key to remove.
Exceptions
ArgumentException
Thrown when the key is invalid.
LobbyException
Thrown when not in a lobby or the operation fails.
Sets lobby-wide data that is accessible to all players in the lobby.
Parameters
key string
The data key. Cannot be null, empty, or exceed 255 characters.
value string
The data value to set.
Exceptions
ArgumentException
Thrown when the key is invalid.
LobbyException
public void RemoveData(string key)
SetData(string, string)
public void SetData(string key, string value)

Thrown when not in a lobby or the operation fails.
Sets multiple lobby data values in a batch operation.
Parameters
data Dictionary<string, string>
A dictionary containing key-value pairs to set. Null or empty dictionaries are ignored.
Exceptions
LobbyException
Thrown when not in a lobby or any individual set operation fails.
Events
Occurs when lobby data is changed by any player in the lobby.
Event Type
EventHandler<LobbyDataChangedEventArgs>
SetDataBatch(Dictionary<string, string>)
public void SetDataBatch(Dictionary<string, string> data)
OnLobbyDataChanged
public event EventHandler<LobbyDataChangedEventArgs>? OnLobbyDataChanged

Namespace:SteamNetworkLib.Core
Assembly:SteamNetworkLib.dll
Manages Steam lobby operations including creation, joining, leaving, and member
management. Provides core functionality for handling Steam lobbies and their associated
events.
Inheritance
object  SteamLobbyManager
Implements
IDisposable
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the SteamLobbyManager class.
Exceptions
SteamNetworkException
Thrown when Steam is not initialized.
Properties
Class SteamLobbyManager
public class SteamLobbyManager : IDisposable

SteamLobbyManager()
public SteamLobbyManager()

Gets information about the current lobby, or null if not in a lobby.
Property Value
LobbyInfo
Gets a value indicating whether the local player is the host of the current lobby.
Property Value
bool
Gets a value indicating whether the local player is currently in a lobby.
Property Value
bool
Gets the Steam ID of the local player.
CurrentLobby
public LobbyInfo? CurrentLobby { get; }
IsHost
public bool IsHost { get; }
IsInLobby
public bool IsInLobby { get; }
LocalPlayerID
public CSteamID? LocalPlayerID { get; }

Property Value
CSteamID
Methods
Creates a new Steam lobby with the speciﬁed settings.
Parameters
lobbyType ELobbyType
The type of lobby to create (public, friends only, etc.).
maxMembers int
The maximum number of members allowed in the lobby.
Returns
Task<LobbyInfo>
A task that represents the asynchronous operation. The task result contains the created
lobby information.
Exceptions
LobbyException
Thrown when lobby creation fails or is already in progress.
Releases all resources used by the SteamLobbyManager.
CreateLobbyAsync(ELobbyType, int)
public Task<LobbyInfo> CreateLobbyAsync(ELobbyType lobbyType = 1, int maxMembers 
= 4)
Dispose()

Gets a list of all members currently in the lobby.
Returns
List<MemberInfo>
A list of member information for all players in the lobby, or an empty list if not in a lobby.
Invites a friend to the current lobby.
Parameters
friendId CSteamID
The Steam ID of the friend to invite.
Exceptions
LobbyException
Thrown when not in a lobby or the friend ID is invalid.
Joins an existing Steam lobby by its ID.
public void Dispose()
GetLobbyMembers()
public List<MemberInfo> GetLobbyMembers()
InviteFriend(CSteamID?)
public void InviteFriend(CSteamID? friendId)
JoinLobbyAsync(CSteamID)

Parameters
lobbyId CSteamID
The Steam ID of the lobby to join.
Returns
Task<LobbyInfo>
A task that represents the asynchronous operation. The task result contains the joined
lobby information.
Exceptions
LobbyException
Thrown when the lobby ID is invalid, join fails, or is already in progress.
Leaves the current lobby if the local player is in one.
Opens the Steam overlay invite dialog for inviting friends to the current lobby.
Exceptions
LobbyException
Thrown when not in a lobby.
public Task<LobbyInfo> JoinLobbyAsync(CSteamID lobbyId)
LeaveLobby()
public void LeaveLobby()
OpenInviteDialog()
public void OpenInviteDialog()

Events
Occurs when a new lobby is successfully created.
Event Type
EventHandler<LobbyCreatedEventArgs>
Occurs when the local player joins a lobby.
Event Type
EventHandler<LobbyJoinedEventArgs>
Occurs when the local player leaves a lobby.
Event Type
EventHandler<LobbyLeftEventArgs>
Occurs when a new member joins the current lobby.
OnLobbyCreated
public event EventHandler<LobbyCreatedEventArgs>? OnLobbyCreated
OnLobbyJoined
public event EventHandler<LobbyJoinedEventArgs>? OnLobbyJoined
OnLobbyLeft
public event EventHandler<LobbyLeftEventArgs>? OnLobbyLeft
OnMemberJoined

Event Type
EventHandler<MemberJoinedEventArgs>
Occurs when a member leaves the current lobby.
Event Type
EventHandler<MemberLeftEventArgs>
public event EventHandler<MemberJoinedEventArgs>? OnMemberJoined
OnMemberLeft
public event EventHandler<MemberLeftEventArgs>? OnMemberLeft

Namespace:SteamNetworkLib.Core
Assembly:SteamNetworkLib.dll
Manages Steam lobby member data (per-player key-value storage). Provides functionality
for setting, getting, and managing player-speciﬁc data that is visible to all lobby members.
Inheritance
object  SteamMemberData
Implements
IDisposable
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the SteamMemberData class.
Parameters
lobbyManager SteamLobbyManager
The lobby manager instance to use for lobby operations.
Exceptions
ArgumentNullException
Class SteamMemberData
public class SteamMemberData : IDisposable

SteamMemberData(SteamLobbyManager?)
public SteamMemberData(SteamLobbyManager? lobbyManager)

Thrown when lobbyManager is null.
Methods
Releases all resources used by the SteamMemberData.
Gets all data for a speciﬁc player.
Parameters
playerId CSteamID
The Steam ID of the player whose data to retrieve.
Returns
Dictionary<string, string>
A dictionary containing all key-value pairs for the speciﬁed player, or an empty dictionary
if not in a lobby.
Gets a summary of all member data across all players in the lobby.
Returns
Dispose()
public void Dispose()
GetAllMemberData(CSteamID)
public Dictionary<string, string> GetAllMemberData(CSteamID playerId)
GetAllMemberDataSummary()
public Dictionary<CSteamID, Dictionary<string, string>> GetAllMemberDataSummary()

Dictionary<CSteamID, Dictionary<string, string>>
A dictionary mapping player Steam IDs to their complete data dictionaries. Useful for
debugging and overview purposes.
Gets data for a speciﬁc player.
Parameters
playerId CSteamID
The Steam ID of the player whose data to retrieve.
key string
The data key to retrieve.
Returns
string
The data value if found, or null if not found or not in a lobby.
Exceptions
ArgumentException
Thrown when the key is invalid.
Gets data for the local player.
GetMemberData(CSteamID, string)
public string? GetMemberData(CSteamID playerId, string key)
GetMemberData(string)
public string? GetMemberData(string key)

Parameters
key string
The data key to retrieve.
Returns
string
The data value if found, or null if not found or not in a lobby.
Gets the same data key for all players in the lobby.
Parameters
key string
The data key to retrieve for all players.
Returns
Dictionary<CSteamID, string>
A dictionary mapping player Steam IDs to their data values for the speciﬁed key.
Exceptions
ArgumentException
Thrown when the key is invalid.
Gets a list of players who have data for the speciﬁed key.
GetMemberDataForAllPlayers(string)
public Dictionary<CSteamID, string> GetMemberDataForAllPlayers(string key)
GetPlayersWithData(string)

Parameters
key string
The data key to check.
Returns
List<CSteamID>
A list of Steam IDs for players who have data for the speciﬁed key.
Exceptions
ArgumentException
Thrown when the key is invalid.
Checks whether a speciﬁc player has data for the speciﬁed key.
Parameters
playerId CSteamID
The Steam ID of the player to check.
key string
The data key to check.
Returns
bool
True if the key exists and has a non-empty value, false otherwise.
public List<CSteamID> GetPlayersWithData(string key)
HasMemberData(CSteamID, string)
public bool HasMemberData(CSteamID playerId, string key)

Checks whether the local player has data for the speciﬁed key.
Parameters
key string
The data key to check.
Returns
bool
True if the key exists and has a non-empty value, false otherwise.
Refreshes the member data cache for all players by reloading from Steam servers.
Refreshes the member data cache for a speciﬁc player by reloading from Steam servers.
Parameters
playerId CSteamID
The Steam ID of the player whose data to refresh.
HasMemberData(string)
public bool HasMemberData(string key)
RefreshAllMemberData()
public void RefreshAllMemberData()
RefreshMemberData(CSteamID)
public void RefreshMemberData(CSteamID playerId)
RemoveMemberData(string)

Removes data for the local player.
Parameters
key string
The data key to remove.
Exceptions
ArgumentException
Thrown when the key is invalid.
LobbyException
Thrown when not in a lobby.
Sets data for a speciﬁc player. Only the local player can set their own data.
Parameters
playerId CSteamID
The Steam ID of the player. Must be the local player.
key string
The data key. Cannot be null, empty, or exceed 255 characters.
value string
The data value to set.
Exceptions
public void RemoveMemberData(string key)
SetMemberData(CSteamID, string, string)
public void SetMemberData(CSteamID playerId, string key, string value)

ArgumentException
Thrown when the key is invalid.
LobbyException
Thrown when not in a lobby or trying to set data for another player.
Sets data for the local player.
Parameters
key string
The data key. Cannot be null, empty, or exceed 255 characters.
value string
The data value to set.
Exceptions
ArgumentException
Thrown when the key is invalid.
LobbyException
Thrown when not in a lobby.
Sets multiple data values for the local player in a batch operation.
SetMemberData(string, string)
public void SetMemberData(string key, string value)
SetMemberDataBatch(Dictionary<string, string>)
public void SetMemberDataBatch(Dictionary<string, string> data)

Parameters
data Dictionary<string, string>
A dictionary containing key-value pairs to set. Null or empty dictionaries are ignored.
Exceptions
LobbyException
Thrown when not in a lobby or any individual set operation fails.
Events
Occurs when member data is changed for any player in the lobby.
Event Type
EventHandler<MemberDataChangedEventArgs>
OnMemberDataChanged
public event EventHandler<MemberDataChangedEventArgs>? OnMemberDataChanged

Namespace:SteamNetworkLib.Core
Assembly:SteamNetworkLib.dll
Manages Steam P2P networking for direct player-to-player communication. Provides
functionality for sending messages, managing sessions, and handling P2P events.
Inheritance
object  SteamP2PManager
Implements
IDisposable
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the SteamP2PManager class.
Parameters
lobbyManager SteamLobbyManager
The lobby manager instance to use for lobby operations.
Exceptions
ArgumentNullException
Class SteamP2PManager
public class SteamP2PManager : IDisposable

SteamP2PManager(SteamLobbyManager)
public SteamP2PManager(SteamLobbyManager lobbyManager)

Thrown when lobbyManager is null.
SteamNetworkException
Thrown when Steam is not initialized.
Initializes a new instance of the SteamP2PManager class with conﬁgurable network rules.
Parameters
lobbyManager SteamLobbyManager
The lobby manager instance to use for lobby operations.
rules NetworkRules
Network rules that inﬂuence relay usage, receive channels, and session ﬁltering.
Properties
Gets the number of currently active P2P sessions.
Property Value
int
Gets a value indicating whether the P2P manager is active and ready to send/receive data.
SteamP2PManager(SteamLobbyManager?,
NetworkRules)
public SteamP2PManager(SteamLobbyManager? lobbyManager, NetworkRules rules)
ActiveSessionCount
public int ActiveSessionCount { get; }
IsActive

Property Value
bool
Gets the maximum packet size in bytes that can be sent via P2P communication.
Property Value
int
Methods
Accepts a P2P session request from another player.
Parameters
playerId CSteamID
The Steam ID of the player to accept a session with.
Returns
bool
True if the session was accepted successfully, false otherwise.
public bool IsActive { get; }
MaxPacketSize
public int MaxPacketSize { get; }
AcceptSession(CSteamID)
public bool AcceptSession(CSteamID playerId)

Broadcasts a message to all players in the current lobby.
Parameters
message P2PMessage
The message to broadcast.
channel int
The communication channel to use (default: 0).
sendType EP2PSend
The send type for reliability and ordering (default: reliable).
Exceptions
P2PException
Thrown when not in a lobby.
Broadcasts raw packet data to all players in the current lobby.
Parameters
data byte[]
The raw data to broadcast.
channel int
The communication channel to use (default: 0).
BroadcastMessage(P2PMessage, int, EP2PSend)
public void BroadcastMessage(P2PMessage message, int channel = 0, EP2PSend sendType 
= 2)
BroadcastPacket(byte[], int, EP2PSend)
public void BroadcastPacket(byte[] data, int channel = 0, EP2PSend sendType = 2)

sendType EP2PSend
The send type for reliability and ordering (default: reliable).
Exceptions
P2PException
Thrown when not in a lobby.
Closes P2P sessions that have been inactive for longer than the speciﬁed threshold.
Parameters
inactiveThreshold TimeSpan
The time threshold for considering a session inactive.
Closes a P2P session with a speciﬁc player.
Parameters
playerId CSteamID
The Steam ID of the player to close the session with.
Releases all resources used by the SteamP2PManager.
CleanupInactiveSessions(TimeSpan)
public void CleanupInactiveSessions(TimeSpan inactiveThreshold)
CloseSession(CSteamID)
public void CloseSession(CSteamID playerId)
Dispose()

Gets a list of all currently active P2P sessions.
Returns
List<CSteamID>
A list of Steam IDs representing active P2P sessions.
Gets the current state of a P2P session with a speciﬁc player.
Parameters
playerId CSteamID
The Steam ID of the player to get session state for.
Returns
P2PSessionState_t
The current P2P session state information.
Processes all incoming P2P packets. Call this regularly (e.g., in Update()) to handle received
data.
public void Dispose()
GetActiveSessions()
public List<CSteamID> GetActiveSessions()
GetSessionState(CSteamID)
public P2PSessionState_t GetSessionState(CSteamID playerId)
ProcessIncomingPackets()

Registers a custom message type for dynamic deserialization. This is automatically called
when you use RegisterMessageHandler, so you typically don't need to call this directly. Only
use this if you need to receive a message type without registering a handler for it. Built-in
types (TEXT, DATA_SYNC, FILE_TRANSFER, STREAM, HEARTBEAT, EVENT) are registered
automatically.
Type Parameters
T
The custom message type to register.
Registers a handler for a speciﬁc message type. Automatically registers custom message
types for deserialization.
Parameters
handler Action<T, CSteamID>
The handler function that will be called when messages of this type are received.
Type Parameters
T
The type of message to handle.
public void ProcessIncomingPackets()
RegisterCustomMessageType<T>()
public void RegisterCustomMessageType<T>() where T : P2PMessage, new()
RegisterMessageHandler<T>(Action<T, CSteamID>)
public void RegisterMessageHandler<T>(Action<T, CSteamID> handler) where T : 
P2PMessage, new()

Sends a message to a speciﬁc player via P2P communication.
Parameters
targetId CSteamID
The Steam ID of the target player.
message P2PMessage
The message to send.
channel int
The communication channel to use (default: 0).
sendType EP2PSend
The send type for reliability and ordering (default: reliable).
Returns
Task<bool>
A task that represents the asynchronous operation. The task result indicates whether the
message was sent successfully.
Exceptions
P2PException
Thrown when the P2P manager is not active, target ID is invalid, or sending fails.
Sends raw packet data to a speciﬁc player via P2P communication.
SendMessageAsync(CSteamID, P2PMessage, int,
EP2PSend)
public Task<bool> SendMessageAsync(CSteamID targetId, P2PMessage message, int 
channel = 0, EP2PSend sendType = 2)
SendPacketAsync(CSteamID, byte[], int, EP2PSend)

Parameters
targetId CSteamID
The Steam ID of the target player.
data byte[]
The raw data to send.
channel int
The communication channel to use (default: 0).
sendType EP2PSend
The send type for reliability and ordering (default: reliable).
Returns
Task<bool>
A task that represents the asynchronous operation. The task result indicates whether the
packet was sent successfully.
Exceptions
P2PException
Thrown when the P2P manager is not active, target ID is invalid, packet is too large, or
sending fails.
Unregisters a custom message type.
public Task<bool> SendPacketAsync(CSteamID targetId, byte[] data, int channel = 0, 
EP2PSend sendType = 2)
UnregisterCustomMessageType<T>()
public void UnregisterCustomMessageType<T>() where T : P2PMessage, new()

Type Parameters
T
The custom message type to unregister.
Unregisters all handlers for a speciﬁc message type.
Type Parameters
T
The type of message to unregister handlers for.
Updates network rules at runtime and applies global settings where possible.
Parameters
rules NetworkRules
Events
Occurs when a SteamNetworkLib message is received and deserialized.
Event Type
UnregisterMessageHandler<T>()
public void UnregisterMessageHandler<T>() where T : P2PMessage, new()
UpdateRules(NetworkRules)
public void UpdateRules(NetworkRules rules)
OnMessageReceived
public event EventHandler<P2PMessageReceivedEventArgs>? OnMessageReceived

EventHandler<P2PMessageReceivedEventArgs>
Occurs when a raw P2P packet is received from another player.
Event Type
EventHandler<P2PPacketReceivedEventArgs>
Occurs when a P2P packet is sent (provides send result information).
Event Type
EventHandler<P2PPacketSentEventArgs>
Occurs when a P2P session connection fails.
Event Type
EventHandler<P2PSessionConnectFailEventArgs>
Occurs when another player requests a P2P session with the local player.
OnPacketReceived
public event EventHandler<P2PPacketReceivedEventArgs>? OnPacketReceived
OnPacketSent
public event EventHandler<P2PPacketSentEventArgs>? OnPacketSent
OnSessionConnectFail
public event EventHandler<P2PSessionConnectFailEventArgs>? OnSessionConnectFail
OnSessionRequested

Event Type
EventHandler<P2PSessionRequestEventArgs>
public event EventHandler<P2PSessionRequestEventArgs>? OnSessionRequested

Classes
LobbyCreatedEventArgs
Provides data for the lobby created event. Contains information about the newly created
lobby and the result of the creation operation.
LobbyDataChangedEventArgs
Provides data for the lobby data changed event. Contains information about what lobby
data was changed, including old and new values.
LobbyJoinedEventArgs
Provides data for the lobby joined event. Contains information about the lobby that was
joined and the result of the join operation.
LobbyLeftEventArgs
Provides data for the lobby left event. Contains information about the lobby that was left
and the reason for leaving.
MemberDataChangedEventArgs
Provides data for the member data changed event. Contains information about what
member data was changed, including old and new values.
MemberJoinedEventArgs
Provides data for the member joined event. Contains information about the member who
joined the lobby.
MemberLeftEventArgs
Provides data for the member left event. Contains information about the member who left
the lobby and the reason for leaving.
P2PMessageReceivedEventArgs
Provides data for the P2P message received event. Contains deserialized message data
and metadata about the received message.
P2PPacketReceivedEventArgs
Provides data for the P2P packet received event. Contains raw packet data and metadata
about the received packet.
P2PPacketSentEventArgs
Provides data for the P2P packet sent event. Contains information about the result of
sending a P2P packet.
Namespace SteamNetworkLib.Events

P2PSessionConnectFailEventArgs
Provides data for the P2P session connect fail event. Contains information about a failed
P2P connection attempt.
P2PSessionRequestEventArgs
Provides data for the P2P session request event. Contains information about an incoming
P2P session request and allows controlling the response.
VersionMismatchEventArgs
Event arguments for SteamNetworkLib version mismatches between players.

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the lobby created event. Contains information about the newly created
lobby and the result of the creation operation.
Inheritance
object  EventArgs  LobbyCreatedEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the LobbyCreatedEventArgs class.
Parameters
lobby LobbyInfo
The lobby information for the created lobby.
result EResult
The result of the lobby creation operation.
Properties
Class LobbyCreatedEventArgs
public class LobbyCreatedEventArgs : EventArgs


LobbyCreatedEventArgs(LobbyInfo, EResult)
public LobbyCreatedEventArgs(LobbyInfo lobby, EResult result = 1)

Gets the lobby information for the created lobby.
Property Value
LobbyInfo
Gets the result of the lobby creation operation.
Property Value
EResult
Lobby
public LobbyInfo Lobby { get; }
Result
public EResult Result { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the lobby data changed event. Contains information about what lobby
data was changed, including old and new values.
Inheritance
object  EventArgs  LobbyDataChangedEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the LobbyDataChangedEventArgs class.
Parameters
key string
The key of the lobby data that was changed.
oldValue string
The previous value of the lobby data.
newValue string
Class LobbyDataChangedEventArgs
public class LobbyDataChangedEventArgs : EventArgs


LobbyDataChangedEventArgs(string, string?, string?,
CSteamID?)
public LobbyDataChangedEventArgs(string key, string? oldValue, string? newValue, 
CSteamID? changedBy)

The new value of the lobby data.
changedBy CSteamID
The Steam ID of the player who made the change.
Properties
Gets the Steam ID of the player who made the change.
Property Value
CSteamID
Gets the key of the lobby data that was changed.
Property Value
string
Gets the new value of the lobby data, or null if it was removed.
Property Value
string
ChangedBy
public CSteamID? ChangedBy { get; }
Key
public string Key { get; }
NewValue
public string? NewValue { get; }

Gets the previous value of the lobby data, or null if it was newly set.
Property Value
string
OldValue
public string? OldValue { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the lobby joined event. Contains information about the lobby that was
joined and the result of the join operation.
Inheritance
object  EventArgs  LobbyJoinedEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the LobbyJoinedEventArgs class.
Parameters
lobby LobbyInfo
The lobby information for the joined lobby.
result EResult
The result of the lobby join operation.
Properties
Class LobbyJoinedEventArgs
public class LobbyJoinedEventArgs : EventArgs


LobbyJoinedEventArgs(LobbyInfo, EResult)
public LobbyJoinedEventArgs(LobbyInfo lobby, EResult result = 1)

Gets the lobby information for the joined lobby.
Property Value
LobbyInfo
Gets the result of the lobby join operation.
Property Value
EResult
Lobby
public LobbyInfo Lobby { get; }
Result
public EResult Result { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the lobby left event. Contains information about the lobby that was left
and the reason for leaving.
Inheritance
object  EventArgs  LobbyLeftEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the LobbyLeftEventArgs class.
Parameters
lobbyId CSteamID
The Steam ID of the lobby that was left.
reason string
The reason for leaving the lobby.
Properties
Class LobbyLeftEventArgs
public class LobbyLeftEventArgs : EventArgs


LobbyLeftEventArgs(CSteamID, string)
public LobbyLeftEventArgs(CSteamID lobbyId, string reason = "")

Gets the Steam ID of the lobby that was left.
Property Value
CSteamID
Gets the reason for leaving the lobby, or an empty string if no speciﬁc reason was provided.
Property Value
string
LobbyId
public CSteamID LobbyId { get; }
Reason
public string Reason { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the member data changed event. Contains information about what
member data was changed, including old and new values.
Inheritance
object  EventArgs  MemberDataChangedEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the MemberDataChangedEventArgs class.
Parameters
memberId CSteamID
The Steam ID of the member whose data was changed.
key string
The key of the member data that was changed.
oldValue string
Class MemberDataChangedEventArgs
public class MemberDataChangedEventArgs : EventArgs


MemberDataChangedEventArgs(CSteamID?, string,
string?, string?)
public MemberDataChangedEventArgs(CSteamID? memberId, string key, string? oldValue, 
string? newValue)

The previous value of the member data.
newValue string
The new value of the member data.
Properties
Gets the key of the member data that was changed.
Property Value
string
Gets the Steam ID of the member whose data was changed.
Property Value
CSteamID
Gets the new value of the member data, or null if it was removed.
Property Value
string
Key
public string Key { get; }
MemberId
public CSteamID? MemberId { get; }
NewValue
public string? NewValue { get; }

Gets the previous value of the member data, or null if it was newly set.
Property Value
string
OldValue
public string? OldValue { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the member joined event. Contains information about the member who
joined the lobby.
Inheritance
object  EventArgs  MemberJoinedEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the MemberJoinedEventArgs class.
Parameters
member MemberInfo
The information about the member who joined the lobby.
Properties
Gets the information about the member who joined the lobby.
Class MemberJoinedEventArgs
public class MemberJoinedEventArgs : EventArgs


MemberJoinedEventArgs(MemberInfo)
public MemberJoinedEventArgs(MemberInfo member)
Member

Property Value
MemberInfo
public MemberInfo Member { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the member left event. Contains information about the member who left
the lobby and the reason for leaving.
Inheritance
object  EventArgs  MemberLeftEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the MemberLeftEventArgs class.
Parameters
member MemberInfo
The information about the member who left the lobby.
reason string
The reason for leaving the lobby.
Properties
Class MemberLeftEventArgs
public class MemberLeftEventArgs : EventArgs


MemberLeftEventArgs(MemberInfo, string)
public MemberLeftEventArgs(MemberInfo member, string reason = "")

Gets the information about the member who left the lobby.
Property Value
MemberInfo
Gets the reason for leaving the lobby, or an empty string if no speciﬁc reason was provided.
Property Value
string
Member
public MemberInfo Member { get; }
Reason
public string Reason { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the P2P message received event. Contains deserialized message data and
metadata about the received message.
Inheritance
object  EventArgs  P2PMessageReceivedEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the P2PMessageReceivedEventArgs class.
Parameters
message P2PMessage
The deserialized message that was received.
senderId CSteamID
The Steam ID of the player who sent the message.
channel int
Class P2PMessageReceivedEventArgs
public class P2PMessageReceivedEventArgs : EventArgs


P2PMessageReceivedEventArgs(P2PMessage,
CSteamID, int)
public P2PMessageReceivedEventArgs(P2PMessage message, CSteamID senderId, 
int channel)

The communication channel on which the message was received.
Properties
Gets the communication channel on which the message was received.
Property Value
int
Gets the deserialized message that was received.
Property Value
P2PMessage
Gets the timestamp when the message was received.
Property Value
DateTime
Channel
public int Channel { get; }
Message
public P2PMessage Message { get; }
ReceivedAt
public DateTime ReceivedAt { get; }
SenderId

Gets the Steam ID of the player who sent the message.
Property Value
CSteamID
public CSteamID SenderId { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the P2P packet received event. Contains raw packet data and metadata
about the received packet.
Inheritance
object  EventArgs  P2PPacketReceivedEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the P2PPacketReceivedEventArgs class.
Parameters
senderId CSteamID
The Steam ID of the player who sent the packet.
data byte[]
The raw packet data that was received.
channel int
Class P2PPacketReceivedEventArgs
public class P2PPacketReceivedEventArgs : EventArgs


P2PPacketReceivedEventArgs(CSteamID, byte[], int,
uint)
public P2PPacketReceivedEventArgs(CSteamID senderId, byte[] data, int channel, 
uint packetSize)

The communication channel on which the packet was received.
packetSize uint
The size of the received packet in bytes.
Properties
Gets the communication channel on which the packet was received.
Property Value
int
Gets the raw packet data that was received.
Property Value
byte[]
Gets the size of the received packet in bytes.
Property Value
uint
Channel
public int Channel { get; }
Data
public byte[] Data { get; }
PacketSize
public uint PacketSize { get; }

Gets the timestamp when the packet was received.
Property Value
DateTime
Gets the Steam ID of the player who sent the packet.
Property Value
CSteamID
ReceivedAt
public DateTime ReceivedAt { get; }
SenderId
public CSteamID SenderId { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the P2P packet sent event. Contains information about the result of
sending a P2P packet.
Inheritance
object  EventArgs  P2PPacketSentEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the P2PPacketSentEventArgs class.
Parameters
targetId CSteamID
The Steam ID of the target player the packet was sent to.
success bool
Whether the packet was sent successfully.
dataSize int
Class P2PPacketSentEventArgs
public class P2PPacketSentEventArgs : EventArgs


P2PPacketSentEventArgs(CSteamID, bool, int, int,
EP2PSend)
public P2PPacketSentEventArgs(CSteamID targetId, bool success, int dataSize, int 
channel, EP2PSend sendType)

The size of the data that was sent in bytes.
channel int
The communication channel on which the packet was sent.
sendType EP2PSend
The send type used for the packet transmission.
Properties
Gets the communication channel on which the packet was sent.
Property Value
int
Gets the size of the data that was sent in bytes.
Property Value
int
Gets the send type used for the packet transmission.
Channel
public int Channel { get; }
DataSize
public int DataSize { get; }
SendType
public EP2PSend SendType { get; }

Property Value
EP2PSend
Gets whether the packet was sent successfully.
Property Value
bool
Gets the Steam ID of the target player the packet was sent to.
Property Value
CSteamID
Success
public bool Success { get; }
TargetId
public CSteamID TargetId { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the P2P session connect fail event. Contains information about a failed
P2P connection attempt.
Inheritance
object  EventArgs  P2PSessionConnectFailEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the P2PSessionConnectFailEventArgs class.
Parameters
targetId CSteamID
The Steam ID of the target player that could not be connected to.
error EP2PSessionError
The speciﬁc error that occurred during the connection attempt.
Class P2PSessionConnectFailEventArgs
public class P2PSessionConnectFailEventArgs : EventArgs


P2PSessionConnectFailEventArgs(CSteamID,
EP2PSessionError)
public P2PSessionConnectFailEventArgs(CSteamID targetId, EP2PSessionError error)

Properties
Gets the speciﬁc error that occurred during the connection attempt.
Property Value
EP2PSessionError
Gets the Steam ID of the target player that could not be connected to.
Property Value
CSteamID
Error
public EP2PSessionError Error { get; }
TargetId
public CSteamID TargetId { get; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Provides data for the P2P session request event. Contains information about an incoming
P2P session request and allows controlling the response.
Inheritance
object  EventArgs  P2PSessionRequestEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the P2PSessionRequestEventArgs class.
Parameters
requesterId CSteamID
The Steam ID of the player requesting the P2P session.
requesterName string
The display name of the player requesting the P2P session.
Properties
Class P2PSessionRequestEventArgs
public class P2PSessionRequestEventArgs : EventArgs


P2PSessionRequestEventArgs(CSteamID, string)
public P2PSessionRequestEventArgs(CSteamID requesterId, string requesterName)

Gets the Steam ID of the player requesting the P2P session.
Property Value
CSteamID
Gets the display name of the player requesting the P2P session.
Property Value
string
Gets or sets whether the P2P session request should be accepted. Defaults to true.
Property Value
bool
RequesterId
public CSteamID RequesterId { get; }
RequesterName
public string RequesterName { get; }
ShouldAccept
public bool ShouldAccept { get; set; }

Namespace:SteamNetworkLib.Events
Assembly:SteamNetworkLib.dll
Event arguments for SteamNetworkLib version mismatches between players.
Inheritance
object  EventArgs  VersionMismatchEventArgs
Inherited Members
EventArgs.Empty , object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the VersionMismatchEventArgs class.
Parameters
localVersion string
The local player's SteamNetworkLib version.
playerVersions Dictionary<CSteamID, string>
Dictionary mapping player Steam IDs to their versions.
incompatiblePlayers List<CSteamID>
Class VersionMismatchEventArgs
public class VersionMismatchEventArgs : EventArgs


VersionMismatchEventArgs(string,
Dictionary<CSteamID, string>, List<CSteamID>)
public VersionMismatchEventArgs(string localVersion, Dictionary<CSteamID, string> 
playerVersions, List<CSteamID> incompatiblePlayers)

List of players with incompatible versions.
Properties
Gets a list of players with incompatible versions.
Property Value
List<CSteamID>
Gets the local player's SteamNetworkLib version.
Property Value
string
Gets a dictionary mapping player Steam IDs to their SteamNetworkLib versions.
Property Value
Dictionary<CSteamID, string>
IncompatiblePlayers
public List<CSteamID> IncompatiblePlayers { get; }
LocalVersion
public string LocalVersion { get; }
PlayerVersions
public Dictionary<CSteamID, string> PlayerVersions { get; }

Classes
LobbyException
Exception thrown when lobby-speciﬁc operations fail. Provides additional context about
Steam lobby operations including result codes and lobby IDs.
P2PException
Exception thrown when peer-to-peer (P2P) communication operations fail. Provides
additional context about Steam P2P operations including target IDs, session errors, and
channels.
SteamNetworkException
Base exception for all Steam networking operations in SteamNetworkLib. This serves as
the parent class for more speciﬁc networking exceptions.
Namespace SteamNetworkLib.Exceptions

Namespace:SteamNetworkLib.Exceptions
Assembly:SteamNetworkLib.dll
Exception thrown when lobby-speciﬁc operations fail. Provides additional context about
Steam lobby operations including result codes and lobby IDs.
Inheritance
object  Exception  SteamNetworkException LobbyException
Implements
ISerializable
Inherited Members
Exception.GetBaseException() , Exception.ToString() , Exception.GetType() ,
Exception.TargetSite , Exception.Message , Exception.Data ,
Exception.InnerException , Exception.HelpLink , Exception.Source ,
Exception.HResult , Exception.StackTrace , Exception.SerializeObjectState ,
object.MemberwiseClone() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the LobbyException class.
Initializes a new instance of the LobbyException class with a speciﬁed error message.
Class LobbyException
public class LobbyException : SteamNetworkException, ISerializable



LobbyException()
public LobbyException()
LobbyException(string)

Parameters
message string
The message that describes the error.
Initializes a new instance of the LobbyException class with a speciﬁed error message and
lobby ID.
Parameters
message string
The message that describes the error.
lobbyId CSteamID
The Steam ID of the lobby associated with the operation.
Initializes a new instance of the LobbyException class with a speciﬁed error message and
Steam result code.
Parameters
message string
The message that describes the error.
public LobbyException(string message)
LobbyException(string, CSteamID)
public LobbyException(string message, CSteamID lobbyId)
LobbyException(string, EResult)
public LobbyException(string message, EResult steamResult)

steamResult EResult
The Steam result code that indicates the speciﬁc failure reason.
Initializes a new instance of the LobbyException class with a speciﬁed error message,
Steam result code, and lobby ID.
Parameters
message string
The message that describes the error.
steamResult EResult
The Steam result code that indicates the speciﬁc failure reason.
lobbyId CSteamID
The Steam ID of the lobby associated with the operation.
Initializes a new instance of the LobbyException class with a speciﬁed error message and a
reference to the inner exception that is the cause of this exception.
Parameters
message string
The error message that explains the reason for the exception.
inner Exception
LobbyException(string, EResult, CSteamID)
public LobbyException(string message, EResult steamResult, CSteamID lobbyId)
LobbyException(string, Exception)
public LobbyException(string message, Exception inner)

The exception that is the cause of the current exception, or a null reference if no inner
exception is speciﬁed.
Properties
Gets the Steam ID of the lobby associated with the operation, if available.
Property Value
CSteamID?
Gets the Steam result code associated with the lobby operation, if available.
Property Value
EResult?
LobbyId
public CSteamID? LobbyId { get; }
SteamResult
public EResult? SteamResult { get; }

Namespace:SteamNetworkLib.Exceptions
Assembly:SteamNetworkLib.dll
Exception thrown when peer-to-peer (P2P) communication operations fail. Provides
additional context about Steam P2P operations including target IDs, session errors, and
channels.
Inheritance
object  Exception  SteamNetworkException P2PException
Implements
ISerializable
Inherited Members
Exception.GetBaseException() , Exception.ToString() , Exception.GetType() ,
Exception.TargetSite , Exception.Message , Exception.Data ,
Exception.InnerException , Exception.HelpLink , Exception.Source ,
Exception.HResult , Exception.StackTrace , Exception.SerializeObjectState ,
object.MemberwiseClone() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the P2PException class.
Initializes a new instance of the P2PException class with a speciﬁed error message.
Class P2PException
public class P2PException : SteamNetworkException, ISerializable



P2PException()
public P2PException()
P2PException(string)

Parameters
message string
The message that describes the error.
Initializes a new instance of the P2PException class with a speciﬁed error message and
target peer ID.
Parameters
message string
The message that describes the error.
targetId CSteamID
The Steam ID of the target peer associated with the operation.
Initializes a new instance of the P2PException class with a speciﬁed error message, target
peer ID, and session error.
Parameters
message string
The message that describes the error.
public P2PException(string message)
P2PException(string, CSteamID)
public P2PException(string message, CSteamID targetId)
P2PException(string, CSteamID, EP2PSessionError)
public P2PException(string message, CSteamID targetId, EP2PSessionError 
sessionError)

targetId CSteamID
The Steam ID of the target peer associated with the operation.
sessionError EP2PSessionError
The P2P session error that occurred during the operation.
Initializes a new instance of the P2PException class with a speciﬁed error message, target
peer ID, and channel.
Parameters
message string
The message that describes the error.
targetId CSteamID
The Steam ID of the target peer associated with the operation.
channel int
The communication channel associated with the P2P operation.
Initializes a new instance of the P2PException class with a speciﬁed error message and
session error.
Parameters
message string
P2PException(string, CSteamID, int)
public P2PException(string message, CSteamID targetId, int channel)
P2PException(string, EP2PSessionError)
public P2PException(string message, EP2PSessionError sessionError)

The message that describes the error.
sessionError EP2PSessionError
The P2P session error that occurred during the operation.
Initializes a new instance of the P2PException class with a speciﬁed error message and a
reference to the inner exception that is the cause of this exception.
Parameters
message string
The error message that explains the reason for the exception.
inner Exception
The exception that is the cause of the current exception, or a null reference if no inner
exception is speciﬁed.
Properties
Gets the communication channel associated with the P2P operation.
Property Value
int
P2PException(string, Exception)
public P2PException(string message, Exception inner)
Channel
public int Channel { get; }
SessionError

Gets the P2P session error that occurred during the operation, if available.
Property Value
EP2PSessionError?
Gets the Steam ID of the target peer associated with the P2P operation, if available.
Property Value
CSteamID?
public EP2PSessionError? SessionError { get; }
TargetId
public CSteamID? TargetId { get; }

Namespace:SteamNetworkLib.Exceptions
Assembly:SteamNetworkLib.dll
Base exception for all Steam networking operations in SteamNetworkLib. This serves as the
parent class for more speciﬁc networking exceptions.
Inheritance
object  Exception  SteamNetworkException
Implements
ISerializable
Derived
LobbyException, P2PException
Inherited Members
Exception.GetBaseException() , Exception.ToString() , Exception.GetType() ,
Exception.TargetSite , Exception.Message , Exception.Data ,
Exception.InnerException , Exception.HelpLink , Exception.Source ,
Exception.HResult , Exception.StackTrace , Exception.SerializeObjectState ,
object.MemberwiseClone() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the SteamNetworkException class.
Class SteamNetworkException
public class SteamNetworkException : Exception, ISerializable


SteamNetworkException()
public SteamNetworkException()
SteamNetworkException(string)

Initializes a new instance of the SteamNetworkException class with a speciﬁed error
message.
Parameters
message string
The message that describes the error.
Initializes a new instance of the SteamNetworkException class with a speciﬁed error
message and a reference to the inner exception that is the cause of this exception.
Parameters
message string
The error message that explains the reason for the exception.
inner Exception
The exception that is the cause of the current exception, or a null reference if no inner
exception is speciﬁed.
public SteamNetworkException(string message)
SteamNetworkException(string, Exception)
public SteamNetworkException(string message, Exception inner)

Classes
DataSyncMessage
Represents a data synchronization message for sharing key-value data between players.
Used for synchronizing game state, conﬁguration data, or any structured information
across the lobby.
EventMessage
Represents an event message for broadcasting system events, game events, and
notiﬁcations between players. Supports advanced features like priority levels, targeting,
acknowledgments, and expiration.
FileTransferMessage
Represents a ﬁle transfer message for sharing ﬁles between players in chunks. Supports
chunked ﬁle transfer to handle large ﬁles that exceed network packet size limits.
HeartbeatMessage
Represents a heartbeat message for connection monitoring, latency measurement, and
keepalive functionality. Used to monitor P2P network performance.
LobbyInfo
Represents information about a Steam lobby including its metadata and current state.
Contains all essential details needed to identify and manage a lobby session.
MemberInfo
Represents information about a lobby member including their identity and status.
Contains all essential details needed to identify and manage a player in a lobby.
P2PMessage
Base class for all P2P messages in SteamNetworkLib. Provides common functionality for
serialization, deserialization, and message metadata.
StreamMessage
Represents a universal real-time streaming message for audio, video, or continuous data
streams. Supports compression, quality settings, and proper sequencing for low-latency
communication. Optimized for streaming applications that require minimal delay and high
throughput.
TextMessage
Represents a simple text message for P2P communication between players. This is the
most basic message type for sending plain text content.
Namespace SteamNetworkLib.Models

Namespace:SteamNetworkLib.Models
Assembly:SteamNetworkLib.dll
Represents a data synchronization message for sharing key-value data between players.
Used for synchronizing game state, conﬁguration data, or any structured information across
the lobby.
Inheritance
object  P2PMessage DataSyncMessage
Inherited Members
P2PMessage.CreateJsonBase(string) , P2PMessage.ParseJsonBase(string) ,
P2PMessage.ExtractJsonValue(string, string) , P2PMessage.SenderId ,
P2PMessage.Timestamp , object.GetType() , object.MemberwiseClone() ,
object.ToString() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
For simple, small data synchronization (typically under 1KB), consider using lobby data or
member data instead:
Use SteamLobbyData for lobby-wide data that all players need to see
Use SteamMemberData for player-speciﬁc data that should be visible to all players
Constructors
Properties
Class DataSyncMessage
public class DataSyncMessage : P2PMessage


DataSyncMessage()
public DataSyncMessage()

Gets or sets the data type identiﬁer that describes the format of the value. Common values
include "string", "json", "xml", "binary", etc.
Property Value
string
Gets or sets the data key identiﬁer. This key is used to identify what type of data is being
synchronized.
Property Value
string
Gets the message type identiﬁer for data sync messages.
Property Value
string
Gets or sets the data value to be synchronized. Can contain any string data including JSON,
XML, or plain text.
DataType
public string DataType { get; set; }
Key
public string Key { get; set; }
MessageType
public override string MessageType { get; }
Value

Property Value
string
Methods
Deserializes the data sync message from a byte array received over the network.
Parameters
data byte[]
The byte array containing the serialized message data.
Serializes the data sync message to a byte array for network transmission.
Returns
byte[]
A byte array containing the serialized message data in JSON format.
public string Value { get; set; }
Deserialize(byte[])
public override void Deserialize(byte[] data)
Serialize()
public override byte[] Serialize()

Namespace:SteamNetworkLib.Models
Assembly:SteamNetworkLib.dll
Represents an event message for broadcasting system events, game events, and
notiﬁcations between players. Supports advanced features like priority levels, targeting,
acknowledgments, and expiration.
Inheritance
object  P2PMessage EventMessage
Inherited Members
P2PMessage.CreateJsonBase(string) , P2PMessage.ParseJsonBase(string) ,
P2PMessage.ExtractJsonValue(string, string) , P2PMessage.SenderId ,
P2PMessage.Timestamp , object.GetType() , object.MemberwiseClone() ,
object.ToString() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Properties
Gets or sets the event payload data as a JSON string. Contains the actual data or
parameters associated with the event.
Class EventMessage
public class EventMessage : P2PMessage


EventMessage()
public EventMessage()
EventData
public string EventData { get; set; }

Property Value
string
Gets or sets the unique event identiﬁer for tracking and acknowledgment purposes.
Automatically generated if not speciﬁed.
Property Value
string
Gets or sets the speciﬁc event name or identiﬁer. This should be a unique identiﬁer for the
speciﬁc event being triggered.
Property Value
string
Gets or sets the type or category of the event. Common values include "system", "game",
"user", "notiﬁcation", etc.
Property Value
string
EventId
public string EventId { get; set; }
EventName
public string EventName { get; set; }
EventType
public string EventType { get; set; }

Gets or sets when the event expires for time-sensitive events. Events past their expiration
time should be ignored by recipients.
Property Value
DateTime?
Gets the message type identiﬁer for event messages.
Property Value
string
Gets or sets the priority level of the event. Values: 0 = low, 1 = normal, 2 = high, 3 =
critical. Higher priority events should be processed ﬁrst.
Property Value
int
Gets or sets a value indicating whether the event requires acknowledgment from
recipients. When true, recipients should send back an acknowledgment message.
ExpiresAt
public DateTime? ExpiresAt { get; set; }
MessageType
public override string MessageType { get; }
Priority
public int Priority { get; set; }
RequiresAck

Property Value
bool
Gets or sets a value indicating whether this event should be persisted or logged. Useful for
important events that need to be recorded for later analysis.
Property Value
bool
Gets or sets additional tags for event categorization and ﬁltering. Can be used to
implement custom event ﬁltering logic.
Property Value
string
Gets or sets the target audience for the event. Common values include "all", "friends",
"speciﬁc_players", "host_only", etc.
public bool RequiresAck { get; set; }
ShouldPersist
public bool ShouldPersist { get; set; }
Tags
public string Tags { get; set; }
TargetAudience
public string TargetAudience { get; set; }

Property Value
string
Gets or sets a comma-separated list of target player Steam IDs. Only used when Target
Audience is set to "speciﬁc_players".
Property Value
string
Methods
Deserializes the event message from a byte array received over the network.
Parameters
data byte[]
The byte array containing the serialized message data.
Serializes the event message to a byte array for network transmission.
Returns
TargetPlayerIds
public string TargetPlayerIds { get; set; }
Deserialize(byte[])
public override void Deserialize(byte[] data)
Serialize()
public override byte[] Serialize()

byte[]
A byte array containing the serialized message data in JSON format.

Namespace:SteamNetworkLib.Models
Assembly:SteamNetworkLib.dll
Represents a ﬁle transfer message for sharing ﬁles between players in chunks. Supports
chunked ﬁle transfer to handle large ﬁles that exceed network packet size limits.
Inheritance
object  P2PMessage FileTransferMessage
Inherited Members
P2PMessage.CreateJsonBase(string) , P2PMessage.ParseJsonBase(string) ,
P2PMessage.ExtractJsonValue(string, string) , P2PMessage.SenderId ,
P2PMessage.Timestamp , object.GetType() , object.MemberwiseClone() ,
object.ToString() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Properties
Gets or sets the raw ﬁle data for this chunk. Contains the actual bytes of the ﬁle segment
being transferred.
Class FileTransferMessage
public class FileTransferMessage : P2PMessage


FileTransferMessage()
public FileTransferMessage()
ChunkData
public byte[] ChunkData { get; set; }

Property Value
byte[]
Gets or sets the zero-based index of this chunk in the ﬁle transfer sequence. Used to
reassemble the ﬁle chunks in the correct order.
Property Value
int
Gets or sets the name of the ﬁle being transferred. Should include the ﬁle extension for
proper identiﬁcation.
Property Value
string
Gets or sets the total size of the ﬁle in bytes. Used by the recipient to validate the complete
transfer and allocate storage.
Property Value
int
ChunkIndex
public int ChunkIndex { get; set; }
FileName
public string FileName { get; set; }
FileSize
public int FileSize { get; set; }

Gets or sets a value indicating whether this message contains actual ﬁle data. When false,
this message may be a ﬁle transfer control message (start, end, error, etc.).
Property Value
bool
Gets the message type identiﬁer for ﬁle transfer messages.
Property Value
string
Gets or sets the total number of chunks that make up the complete ﬁle. Used by the
recipient to determine when the ﬁle transfer is complete.
Property Value
int
Methods
IsFileData
public bool IsFileData { get; set; }
MessageType
public override string MessageType { get; }
TotalChunks
public int TotalChunks { get; set; }
Deserialize(byte[])

Deserializes the ﬁle transfer message from a byte array received over the network. Parses
the JSON header and extracts the binary chunk data.
Parameters
data byte[]
The byte array containing the serialized message data.
Serializes the ﬁle transfer message to a byte array for network transmission. Uses a hybrid
format with JSON header followed by raw binary data.
Returns
byte[]
A byte array containing the serialized message with header and chunk data.
public override void Deserialize(byte[] data)
Serialize()
public override byte[] Serialize()

Namespace:SteamNetworkLib.Models
Assembly:SteamNetworkLib.dll
Represents a heartbeat message for connection monitoring, latency measurement, and
keepalive functionality. Used to monitor P2P network performance.
Inheritance
object  P2PMessage HeartbeatMessage
Inherited Members
P2PMessage.CreateJsonBase(string) , P2PMessage.ParseJsonBase(string) ,
P2PMessage.ExtractJsonValue(string, string) , P2PMessage.SenderId ,
P2PMessage.Timestamp , object.GetType() , object.MemberwiseClone() ,
object.ToString() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Properties
Gets or sets the average latency in milliseconds. Calculated from recent ping/pong round-
trip times.
Class HeartbeatMessage
public class HeartbeatMessage : P2PMessage


HeartbeatMessage()
public HeartbeatMessage()
AverageLatencyMs
public float AverageLatencyMs { get; set; }

Property Value
ﬂoat
Gets or sets the current bandwidth usage in bytes per second. Includes both incoming and
outgoing data transfer rates.
Property Value
int
Gets or sets additional metadata about the player's connection. Can include information
like connection type, NAT status, or other network details.
Property Value
string
Gets or sets the unique identiﬁer for this heartbeat. Used for matching ping and pong
messages to calculate round-trip time.
Property Value
string
BandwidthUsage
public int BandwidthUsage { get; set; }
ConnectionInfo
public string ConnectionInfo { get; set; }
HeartbeatId
public string HeartbeatId { get; set; }

Gets or sets the high-precision timestamp when this heartbeat was sent. Used for accurate
latency calculations using system ticks or similar high-resolution timing.
Property Value
long
Gets or sets a value indicating whether this is a response to a heartbeat (pong). When
false, this is an initial heartbeat (ping). When true, this is a response (pong).
Property Value
bool
Gets the message type identiﬁer for heartbeat messages.
Property Value
string
Gets or sets the connection quality information as packet loss percentage. Value between
0.0 and 100.0 indicating the percentage of packets lost.
HighPrecisionTimestamp
public long HighPrecisionTimestamp { get; set; }
IsResponse
public bool IsResponse { get; set; }
MessageType
public override string MessageType { get; }
PacketLossPercent

Property Value
ﬂoat
Gets or sets the current player status or state. Common values include "online", "away",
"busy", "playing", etc.
Property Value
string
Gets or sets the sequence number for tracking heartbeat order. Helps detect lost
heartbeats and measure packet ordering.
Property Value
uint
Methods
Deserializes the heartbeat message from a byte array received over the network.
public float PacketLossPercent { get; set; }
PlayerStatus
public string PlayerStatus { get; set; }
SequenceNumber
public uint SequenceNumber { get; set; }
Deserialize(byte[])
public override void Deserialize(byte[] data)

Parameters
data byte[]
The byte array containing the serialized message data.
Serializes the heartbeat message to a byte array for network transmission.
Returns
byte[]
A byte array containing the serialized message data in JSON format.
Serialize()
public override byte[] Serialize()

Namespace:SteamNetworkLib.Models
Assembly:SteamNetworkLib.dll
Represents information about a Steam lobby including its metadata and current state.
Contains all essential details needed to identify and manage a lobby session.
Inheritance
object  LobbyInfo
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the LobbyInfo class. Sets the creation time to the current local
time.
Properties
Gets or sets when the lobby was created or when it was joined locally. Used for tracking
session duration and ordering lobby lists.
Class LobbyInfo
public class LobbyInfo

LobbyInfo()
public LobbyInfo()
CreatedAt
public DateTime CreatedAt { get; set; }

Property Value
DateTime
Gets or sets the unique Steam ID of the lobby. This is the primary identiﬁer used for all
lobby operations.
Property Value
CSteamID
Gets or sets the maximum number of members allowed in the lobby. This limit is set when
the lobby is created and determines capacity.
Property Value
int
Gets or sets the current number of members in the lobby. This count includes all connected
players including the host.
Property Value
int
LobbyId
public CSteamID? LobbyId { get; set; }
MaxMembers
public int MaxMembers { get; set; }
MemberCount
public int MemberCount { get; set; }

Gets or sets the display name or title of the lobby. This is an optional human-readable
identiﬁer for the lobby.
Property Value
string
Gets or sets the Steam ID of the lobby owner (host). The owner has special privileges like
changing lobby settings and kicking members.
Property Value
CSteamID
Name
public string? Name { get; set; }
OwnerId
public CSteamID? OwnerId { get; set; }

Namespace:SteamNetworkLib.Models
Assembly:SteamNetworkLib.dll
Represents information about a lobby member including their identity and status. Contains
all essential details needed to identify and manage a player in a lobby.
Inheritance
object  MemberInfo
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the MemberInfo class. Sets the join time to the current local
time.
Properties
Gets or sets the display name of the member as shown in Steam. This is the human-
readable name that other players will see.
Class MemberInfo
public class MemberInfo

MemberInfo()
public MemberInfo()
DisplayName
public string DisplayName { get; set; }

Property Value
string
Gets or sets a value indicating whether this member represents the local player. This helps
distinguish the local player from other lobby members in the UI.
Property Value
bool
Gets or sets a value indicating whether this member is the lobby owner (host). The owner
has special privileges like changing lobby settings and managing members.
Property Value
bool
Gets or sets when this member joined the lobby. Used for tracking session duration and
determining join order.
Property Value
DateTime
IsLocalPlayer
public bool IsLocalPlayer { get; set; }
IsOwner
public bool IsOwner { get; set; }
JoinedAt
public DateTime JoinedAt { get; set; }

Gets or sets the unique Steam ID of the member. This is the primary identiﬁer used for all
player-speciﬁc operations.
Property Value
CSteamID
SteamId
public CSteamID SteamId { get; set; }

Namespace:SteamNetworkLib.Models
Assembly:SteamNetworkLib.dll
Base class for all P2P messages in SteamNetworkLib. Provides common functionality for
serialization, deserialization, and message metadata.
Inheritance
object  P2PMessage
Derived
DataSyncMessage, EventMessage, FileTransferMessage, HeartbeatMessage,
StreamMessage, TextMessage
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the P2PMessage class. Sets the timestamp to the current UTC
time.
Properties
Gets the unique identiﬁer for this message type. Must be implemented by derived classes
to specify the message type.
Class P2PMessage
public abstract class P2PMessage

P2PMessage()
protected P2PMessage()
MessageType

Property Value
string
Gets or sets the Steam ID of the player who sent this message. This is automatically set
when sending messages through the P2P manager.
Property Value
CSteamID
Gets or sets the timestamp when this message was created. Defaults to UTC time when the
message instance is created.
Property Value
DateTime
Methods
Helper method to create a JSON representation of basic message properties. Can be
extended by derived classes to include their speciﬁc properties.
public abstract string MessageType { get; }
SenderId
public CSteamID SenderId { get; set; }
Timestamp
public DateTime Timestamp { get; set; }
CreateJsonBase(string)

Parameters
additionalData string
Additional JSON data to include in the base JSON string.
Returns
string
A JSON string containing the base message properties and any additional data.
Deserializes the message from a byte array received over the network. Must be
implemented by derived classes to reconstruct their speciﬁc data.
Parameters
data byte[]
The byte array containing the serialized message data.
Simple JSON value extractor that avoids dependencies on external JSON libraries. Handles
both quoted strings and unquoted values (numbers, booleans).
Parameters
json string
protected string CreateJsonBase(string additionalData = "")
Deserialize(byte[])
public abstract void Deserialize(byte[] data)
ExtractJsonValue(string, string)
protected string ExtractJsonValue(string json, string key)

The JSON string to extract the value from.
key string
The key of the value to extract.
Returns
string
The extracted value as a string, or an empty string if not found.
Helper method to parse basic message properties from JSON. Should be called by derived
classes to populate the base properties.
Parameters
json string
The JSON string to parse.
Serializes the message to a byte array for network transmission. Must be implemented by
derived classes to handle their speciﬁc data.
Returns
byte[]
A byte array containing the serialized message data.
ParseJsonBase(string)
protected void ParseJsonBase(string json)
Serialize()
public abstract byte[] Serialize()

Namespace:SteamNetworkLib.Models
Assembly:SteamNetworkLib.dll
Represents a universal real-time streaming message for audio, video, or continuous data
streams. Supports compression, quality settings, and proper sequencing for low-latency
communication. Optimized for streaming applications that require minimal delay and high
throughput.
Inheritance
object  P2PMessage StreamMessage
Inherited Members
P2PMessage.CreateJsonBase(string) , P2PMessage.ParseJsonBase(string) ,
P2PMessage.ExtractJsonValue(string, string) , P2PMessage.SenderId ,
P2PMessage.Timestamp , object.GetType() , object.MemberwiseClone() ,
object.ToString() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Properties
Gets or sets the sequence number this message is acknowledging. Used for reliability
feedback and ﬂow control mechanisms.
Class StreamMessage
public class StreamMessage : P2PMessage


StreamMessage()
public StreamMessage()
AckForSequence
public uint? AckForSequence { get; set; }

Property Value
uint?
Gets or sets the bits per sample for audio quality. Common values include 16, 24, 32 bits
per sample.
Property Value
int
Gets or sets the timestamp of when this data was captured or generated. Used for
synchronization between audio and video streams or maintaining timing.
Property Value
long
Gets or sets the number of audio channels. 1 = mono, 2 = stereo, 6 = 5.1 surround, etc.
Property Value
int
BitsPerSample
public int BitsPerSample { get; set; }
CaptureTimestamp
public long CaptureTimestamp { get; set; }
Channels
public int Channels { get; set; }

Gets or sets the compression codec used for the stream data. Common values include
"none", "opus", "mp3", "h264", "vp8", etc.
Property Value
string
Gets or sets the expected duration of this frame in milliseconds. Used for timing and
buﬀering calculations on the receiving end.
Property Value
int
Gets or sets the number of samples per frame. For example, 960 samples for 20ms at
48kHz sampling rate.
Property Value
int
Codec
public string Codec { get; set; }
FrameDurationMs
public int FrameDurationMs { get; set; }
FrameSamples
public int FrameSamples { get; set; }
IsFecFrame

Gets or sets a value indicating whether this frame contains forward error correction data.
FEC data can help recover from packet loss without retransmission.
Property Value
bool
Gets or sets a value indicating whether this is a retransmitted frame. Used for reliability
mechanisms when important frames need to be resent.
Property Value
bool
Gets or sets a value indicating whether this is the end of the stream. Signals to the receiver
that no more data will be sent for this stream.
Property Value
bool
Gets or sets a value indicating whether this is the start of a new stream. Used to initialize
stream decoders and reset state on the receiving end.
public bool IsFecFrame { get; set; }
IsRetransmit
public bool IsRetransmit { get; set; }
IsStreamEnd
public bool IsStreamEnd { get; set; }
IsStreamStart

Property Value
bool
Gets the message type identiﬁer for stream messages.
Property Value
string
Gets or sets additional metadata as a JSON string. Can contain codec-speciﬁc parameters,
custom headers, or other stream information.
Property Value
string
Gets or sets the payload type for extensibility. More speciﬁc than StreamType, can include
"pcm_audio", "h264_video", "metadata", etc.
Property Value
public bool IsStreamStart { get; set; }
MessageType
public override string MessageType { get; }
Metadata
public string Metadata { get; set; }
PayloadType
public string PayloadType { get; set; }

string
Gets or sets the priority level for this frame. Value between 0-255, where higher values
indicate more important frames (e.g., keyframes).
Property Value
byte
Gets or sets the quality level for compression. Value between 0-100, where higher values
mean better quality but larger size.
Property Value
int
Gets or sets the recommended send type for this stream. Defaults to UnreliableNoDelay for
real-time audio/video to minimize latency.
Property Value
EP2PSend
Priority
public byte Priority { get; set; }
Quality
public int Quality { get; set; }
RecommendedSendType
public EP2PSend RecommendedSendType { get; set; }

Gets or sets the sample rate for audio streams in Hz. Common values include 44100,
48000, 22050, etc.
Property Value
int
Gets or sets the sequence number for packet ordering and loss detection. Essential for
maintaining proper stream continuity and detecting missing packets.
Property Value
uint
Gets or sets the actual stream data payload. Contains the raw or compressed
audio/video/data bytes.
Property Value
byte[]
SampleRate
public int SampleRate { get; set; }
SequenceNumber
public uint SequenceNumber { get; set; }
StreamData
public byte[] StreamData { get; set; }
StreamId

Gets or sets the unique stream identiﬁer to handle multiple concurrent streams. Allows
multiple independent streams between the same players.
Property Value
string
Gets or sets the type of stream data being transmitted. Common values include "audio",
"video", "data", "mixed", etc.
Property Value
string
Methods
Deserializes the stream message from a byte array received over the network. Parses the
JSON header and extracts the binary stream data.
Parameters
data byte[]
The byte array containing the serialized message data.
public string StreamId { get; set; }
StreamType
public string StreamType { get; set; }
Deserialize(byte[])
public override void Deserialize(byte[] data)
Serialize()

Serializes the stream message to a byte array for network transmission. Uses a hybrid
format with JSON header followed by raw binary stream data.
Returns
byte[]
A byte array containing the serialized message with header and stream data.
public override byte[] Serialize()

Namespace:SteamNetworkLib.Models
Assembly:SteamNetworkLib.dll
Represents a simple text message for P2P communication between players. This is the
most basic message type for sending plain text content.
Inheritance
object  P2PMessage TextMessage
Inherited Members
P2PMessage.CreateJsonBase(string) , P2PMessage.ParseJsonBase(string) ,
P2PMessage.ExtractJsonValue(string, string) , P2PMessage.SenderId ,
P2PMessage.Timestamp , object.GetType() , object.MemberwiseClone() ,
object.ToString() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Properties
Gets or sets the text content of the message.
Property Value
Class TextMessage
public class TextMessage : P2PMessage


TextMessage()
public TextMessage()
Content
public string Content { get; set; }

string
Gets the message type identiﬁer for text messages.
Property Value
string
Methods
Deserializes the text message from a byte array received over the network.
Parameters
data byte[]
The byte array containing the serialized message data.
Serializes the text message to a byte array for network transmission.
Returns
byte[]
A byte array containing the serialized message data in JSON format.
MessageType
public override string MessageType { get; }
Deserialize(byte[])
public override void Deserialize(byte[] data)
Serialize()
public override byte[] Serialize()

Classes
StreamChannel<T>
Generic streaming channel that handles real-time data streaming with jitter buﬀering,
packet reordering, and loss detection. Codec-agnostic - subclasses handle speciﬁc
encoding/decoding. Provides robust streaming capabilities for audio, video, or any
continuous data streams.
StreamFrame<T>
Represents a buﬀered stream frame with metadata for timing and sequencing. Contains
the decoded frame data along with timing information for proper playback.
StreamSender<T>
Generic stream sender that handles encoding and sending real-time data streams with
proper sequencing, timing, and metadata. Provides the foundation for streaming audio,
video, or any continuous data over Steam P2P networks.
Namespace SteamNetworkLib.Streaming

Namespace:SteamNetworkLib.Streaming
Assembly:SteamNetworkLib.dll
Generic streaming channel that handles real-time data streaming with jitter buﬀering,
packet reordering, and loss detection. Codec-agnostic - subclasses handle speciﬁc
encoding/decoding. Provides robust streaming capabilities for audio, video, or any
continuous data streams.
Type Parameters
T
The type of decoded frame data (e.g., ﬂoat[] for audio, byte[] for video)
Inheritance
object  StreamChannel<T>
Implements
IDisposable
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the StreamChannel<T> class.
Parameters
Class StreamChannel<T>
public abstract class StreamChannel<T> : IDisposable where T : class

StreamChannel(string)
protected StreamChannel(string streamId)

streamId string
The unique identiﬁer for this stream channel.
Exceptions
ArgumentNullException
Thrown when streamId is null.
Fields
Lock object for thread-safe access to the jitter buﬀer and related state.
Field Value
object
The next expected sequence number for ordered frame delivery.
Field Value
uint
The highest sequence number received so far, used for tracking stream progress.
_buﬀerLock
protected readonly object _bufferLock
_expectedSequence
protected uint _expectedSequence
_highestSequenceReceived
protected uint _highestSequenceReceived

Field Value
uint
Indicates whether this stream channel has been disposed and should no longer process
messages.
Field Value
bool
Indicates whether the stream is currently active and receiving data.
Field Value
bool
Jitter buﬀer that stores incoming frames with their sequence numbers for ordered playback.
Field Value
SortedDictionary<uint, StreamFrame<T>>
_isDisposed
protected bool _isDisposed
_isStreaming
protected bool _isStreaming
_jitterBuﬀer
protected readonly SortedDictionary<uint, StreamFrame<T>> _jitterBuffer

Timestamp of the last frame received, used for calculating stream timing and detecting
gaps.
Field Value
DateTime
Properties
Gets the average jitter measurement for received frames.
Property Value
double
Gets or sets the target buﬀer size in milliseconds for jitter compensation. Higher values
provide better resistance to network jitter but increase latency.
Property Value
int
_lastFrameTime
protected DateTime _lastFrameTime
AverageJitter
public double AverageJitter { get; }
BuﬀerMs
public int BufferMs { get; set; }
BuﬀeredFrameCount

Gets the current number of frames in the jitter buﬀer.
Property Value
int
Gets or sets a value indicating whether jitter buﬀering is enabled. When disabled, frames
are played immediately upon arrival (lower latency but less stability).
Property Value
bool
Gets or sets a value indicating whether packet loss detection and recovery is enabled.
When enabled, attempts to handle missing frames with packet loss concealment.
Property Value
bool
Gets or sets the maximum buﬀer size in milliseconds before dropping old frames. Prevents
memory buildup when the network is severely congested.
public int BufferedFrameCount { get; }
EnableJitterBuﬀer
public bool EnableJitterBuffer { get; set; }
EnablePacketLossDetection
public bool EnablePacketLossDetection { get; set; }
MaxBuﬀerMs
public int MaxBufferMs { get; set; }

Property Value
int
Gets the unique identiﬁer for this stream channel.
Property Value
string
Gets the total number of frames dropped due to various reasons (buﬀer overﬂow, late
arrival, etc.).
Property Value
uint
Gets the total number of frames that arrived later than expected.
Property Value
uint
StreamId
public string StreamId { get; }
TotalFramesDropped
public uint TotalFramesDropped { get; }
TotalFramesLate
public uint TotalFramesLate { get; }

Gets the total number of frames received since the stream started.
Property Value
uint
Methods
Deserializes codec-speciﬁc data into frame data. Must be implemented by derived classes
to handle their speciﬁc data format.
Parameters
data byte[]
The raw data bytes to deserialize.
message StreamMessage
The complete stream message for context.
Returns
T
The deserialized frame data, or null if deserialization failed.
Releases all resources used by the StreamChannel<T>.
TotalFramesReceived
public uint TotalFramesReceived { get; }
DeserializeFrame(byte[], StreamMessage)
protected abstract T? DeserializeFrame(byte[] data, StreamMessage message)
Dispose()

Handles missing frames by generating replacement data or applying packet loss
concealment. Can be overridden by subclasses to implement speciﬁc loss concealment
strategies.
Parameters
sequenceNumber uint
The sequence number of the missing frame.
Returns
T
Generated replacement frame data, or null to skip the frame.
Processes a received stream message by adding it to the jitter buﬀer if enabled, or playing
it immediately if buﬀering is disabled.
Parameters
message StreamMessage
The stream message to process.
senderId CSteamID
The Steam ID of the player who sent this message.
public virtual void Dispose()
HandleMissingFrame(uint)
protected virtual T? HandleMissingFrame(uint sequenceNumber)
ProcessStreamMessage(StreamMessage, CSteamID)
public virtual void ProcessStreamMessage(StreamMessage message, CSteamID senderId)

Resets the stream channel to its initial state, clearing all buﬀers and statistics.
Updates the stream channel by processing buﬀered frames and performing maintenance
tasks. Must be called regularly (e.g., in Update loop) to process buﬀered frames and
maintain stream health.
Events
Occurs when a frame is dropped due to buﬀer overﬂow or being too old.
Event Type
Action<uint>
Occurs when a frame arrives later than expected, indicating timing issues.
Event Type
Action<uint, TimeSpan>
Reset()
public virtual void Reset()
Update()
public virtual void Update()
OnFrameDropped
public event Action<uint>? OnFrameDropped
OnFrameLate
public event Action<uint, TimeSpan>? OnFrameLate

Occurs when a decoded frame is ready for playback or processing.
Event Type
Action<T>
Occurs when the stream ends or stops receiving data.
Event Type
Action<StreamChannel<T>>
Occurs when the stream starts receiving data.
Event Type
Action<StreamChannel<T>>
OnFrameReady
public event Action<T>? OnFrameReady
OnStreamEnded
public event Action<StreamChannel<T>>? OnStreamEnded
OnStreamStarted
public event Action<StreamChannel<T>>? OnStreamStarted

Namespace:SteamNetworkLib.Streaming
Assembly:SteamNetworkLib.dll
Represents a buﬀered stream frame with metadata for timing and sequencing. Contains the
decoded frame data along with timing information for proper playback.
Type Parameters
T
Type of the frame data (e.g., ﬂoat[] for audio, byte[] for video)
Inheritance
object  StreamFrame<T>
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Properties
Gets or sets the timestamp when this frame was originally captured. Used for
synchronization and latency measurements.
Class StreamFrame<T>
public class StreamFrame<T> where T : class

StreamFrame()
public StreamFrame()
CaptureTimestamp

Property Value
long
Gets or sets the decoded frame data ready for playback or processing.
Property Value
T
Gets or sets the expected duration of this frame in milliseconds. Used for timing
calculations and buﬀer sizing.
Property Value
int
Gets or sets a value indicating whether this frame is a retransmission. Helps with quality
metrics and debugging network issues.
Property Value
public long CaptureTimestamp { get; set; }
Data
public T Data { get; set; }
FrameDurationMs
public int FrameDurationMs { get; set; }
IsRetransmit
public bool IsRetransmit { get; set; }

bool
Gets or sets the timestamp when this frame was received over the network. Used for jitter
calculations and buﬀer management.
Property Value
long
Gets or sets the sequence number of this frame in the stream. Used for ordering and
detecting missing frames.
Property Value
uint
ReceivedTimestamp
public long ReceivedTimestamp { get; set; }
SequenceNumber
public uint SequenceNumber { get; set; }

Namespace:SteamNetworkLib.Streaming
Assembly:SteamNetworkLib.dll
Generic stream sender that handles encoding and sending real-time data streams with
proper sequencing, timing, and metadata. Provides the foundation for streaming audio,
video, or any continuous data over Steam P2P networks.
Type Parameters
T
Type of data being sent (e.g., ﬂoat[] for audio, byte[] for video)
Inheritance
object  StreamSender<T>
Implements
IDisposable
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Constructors
Initializes a new instance of the StreamSender<T> class.
Parameters
Class StreamSender<T>
public abstract class StreamSender<T> : IDisposable where T : class

StreamSender(string, SteamNetworkClient?)
protected StreamSender(string streamId, SteamNetworkClient? networkClient)

streamId string
The unique identiﬁer for this stream.
networkClient SteamNetworkClient
The network client used for sending stream data.
Exceptions
ArgumentNullException
Thrown when streamId is null.
Fields
The current sequence number for outgoing frames, incremented with each frame sent.
Field Value
uint
Indicates whether this stream sender has been disposed and should no longer send data.
Field Value
bool
_currentSequence
protected uint _currentSequence
_isDisposed
protected bool _isDisposed
_networkClient

Reference to the network client used for sending stream data to connected peers.
Field Value
SteamNetworkClient
Properties
Gets or sets the duration of each frame in milliseconds. This aﬀects timing and
synchronization of the stream.
Property Value
int
Gets a value indicating whether the stream is currently active and sending data.
Property Value
bool
Gets or sets the speciﬁc payload type within the stream type (e.g., "pcm_audio",
"h264_video").
protected SteamNetworkClient? _networkClient
FrameDurationMs
public int FrameDurationMs { get; protected set; }
IsStreaming
public bool IsStreaming { get; }
PayloadType

Property Value
string
Gets the unique identiﬁer for this stream.
Property Value
string
Gets or sets the type of stream being sent (e.g., "audio", "video", "data").
Property Value
string
Gets the total number of bytes sent since the stream started.
Property Value
uint
public string PayloadType { get; protected set; }
StreamId
public string StreamId { get; }
StreamType
public string StreamType { get; protected set; }
TotalBytesSent
public uint TotalBytesSent { get; }

Gets the total number of frames sent since the stream started.
Property Value
uint
Methods
Creates a properly formatted StreamMessage with all required metadata. Automatically
increments the sequence number and sets timing information.
Parameters
data byte[]
The encoded frame data to include in the message.
isStart bool
Whether this message marks the start of the stream.
isEnd bool
Whether this message marks the end of the stream.
Returns
StreamMessage
A properly formatted stream message ready for transmission.
TotalFramesSent
public uint TotalFramesSent { get; }
CreateStreamMessage(byte[], bool, bool)
protected virtual StreamMessage CreateStreamMessage(byte[] data, bool isStart, 
bool isEnd)

Releases all resources used by the StreamSender<T>. Automatically stops the stream if it's
currently active.
Gets the recommended P2P send type for this stream type. Can be overridden by derived
classes to specify diﬀerent reliability levels.
Returns
EP2PSend
The recommended Steamworks.EP2PSend type for this stream.
Resets the stream sender to its initial state, clearing all statistics. Stops the stream if it's
currently active.
Sends a frame of data to all connected peers. Should be called by derived classes after
encoding their speciﬁc data type.
Parameters
Dispose()
public virtual void Dispose()
GetRecommendedSendType()
protected virtual EP2PSend GetRecommendedSendType()
Reset()
public virtual void Reset()
SendFrame(T)
protected virtual void SendFrame(T frameData)

frameData T
The frame data to encode and send.
Sends a frame to a speciﬁc target player instead of broadcasting to all peers. Useful for
targeted streaming or private communication.
Parameters
frameData T
The frame data to encode and send.
targetId CSteamID
The Steam ID of the target player.
Returns
Task<bool>
A task that represents the asynchronous send operation. The task result indicates
success.
Sends the stream message via the network client to all connected peers. Handles error
logging and performance monitoring.
Parameters
message StreamMessage
The stream message to send.
SendFrameToTarget(T, CSteamID)
protected virtual Task<bool> SendFrameToTarget(T frameData, CSteamID targetId)
SendStreamMessage(StreamMessage)
protected virtual void SendStreamMessage(StreamMessage message)

Serializes the frame data into bytes for network transmission. Must be implemented by
derived classes to handle their speciﬁc data format.
Parameters
frameData T
The frame data to serialize.
Returns
byte[]
The serialized frame data as bytes, or null if serialization failed.
Starts the stream by sending a stream start message to all connected peers. Initializes the
sequence counter and notiﬁes the network that streaming has begun.
Stops the stream by sending a stream end message to all connected peers. Signals to
receivers that no more data will be sent for this stream.
Events
SerializeFrame(T)
protected abstract byte[]? SerializeFrame(T frameData)
StartStream()
public virtual void StartStream()
StopStream()
public virtual void StopStream()
OnFrameSent

Occurs when a frame has been successfully sent over the network.
Event Type
Action<uint, int>
Occurs when the stream stops sending data.
Event Type
Action<StreamSender<T>>
Occurs when the stream starts sending data.
Event Type
Action<StreamSender<T>>
public event Action<uint, int>? OnFrameSent
OnStreamEnded
public event Action<StreamSender<T>>? OnStreamEnded
OnStreamStarted
public event Action<StreamSender<T>>? OnStreamStarted

Classes
ClientSyncVar<T>
A client-owned synchronized variable where each client can set their own value, and all
values are visible to all lobby members via Steam member data.
CompositeValidator<T>
Validator that combines multiple validators with AND logic.
HostSyncVar<T>
A host-authoritative synchronized variable that automatically keeps its value in sync
across all lobby members using Steam lobby data.
JsonSyncSerializer
Default JSON serializer for HostSyncVar<T> and ClientSyncVar<T>. Provides IL2CPP-
compatible JSON serialization without external dependencies.
NetworkSyncOptions
Conﬁguration options for HostSyncVar<T> and ClientSyncVar<T> behavior. All properties
have sensible defaults for the simplest developer experience.
PredicateValidator<T>
Base class for simple validators using a predicate function.
RangeValidator<T>
Validator for numeric ranges with inclusive or exclusive bounds.
SyncException
Exception thrown when a sync operation fails due to network or state issues.
SyncSerializationException
Exception thrown when serialization or deserialization fails in sync operations.
SyncValidationException
Exception thrown when value validation fails before sync.
Interfaces
ISyncSerializer
Interface for custom value serialization in HostSyncVar<T> and ClientSyncVar<T>.
ISyncValidator<T>
Interface for validating sync var values before they are synchronized.
Namespace SteamNetworkLib.Sync

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
A client-owned synchronized variable where each client can set their own value, and all
values are visible to all lobby members via Steam member data.
Type Parameters
T
The type of value to synchronize.
Inheritance
object  ClientSyncVar<T>
Implements
IDisposable
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
Authority Model: Each client owns and can modify their own value. All clients can read all
other clients' values.
Storage: Uses Steam lobby member data for storage, which is automatically synchronized
to all lobby members by Steam.
Use Cases:
Ready status for each player
Player loadouts or preferences
Per-player scores or statistics
Player customization options visible to others
Class ClientSyncVar<T>
public class ClientSyncVar<T> : IDisposable


Properties
Gets the full key including any preﬁx.
Property Value
string
Gets whether there is a pending value waiting to be synced.
Property Value
bool
// Create a client-owned sync var
var isReady = client.CreateClientSyncVar("Ready", false);
// Subscribe to any client's changes
isReady.OnValueChanged += (playerId, oldVal, newVal) => 
    MelonLogger.Msg($"Player {playerId} ready: {newVal}");
// Set my own value
isReady.Value = true;
// Read another player's value
bool player2Ready = isReady.GetValue(player2Id);
// Get all players' values
var allReadyStates = isReady.GetAllValues();
FullKey
public string FullKey { get; }
IsDirty
public bool IsDirty { get; }

Remarks
This is true when AutoSync is disabled or when rate limiting has deferred a sync.
Gets the sync key used for this variable.
Property Value
string
Gets or sets the local player's value.
Property Value
T
Remarks
This is a shorthand for getting/setting the value for the local player.
Use GetValue(CSteamID) to get other players' values.
Methods
Releases all resources used by the ClientSyncVar<T>.
Key
public string Key { get; }
Value
public T Value { get; set; }
Dispose()
public void Dispose()

Forces immediate sync of any pending value, bypassing rate limit.
Remarks
Use this when AutoSync is disabled to manually trigger sync operations, or to force a rate-
limited pending value to sync immediately.
Gets the values for all players in the lobby.
Returns
Dictionary<CSteamID, T>
A dictionary mapping player Steam IDs to their values.
Gets the value for a speciﬁc player.
Parameters
playerId CSteamID
The Steam ID of the player.
Returns
T
FlushPending()
public void FlushPending()
GetAllValues()
public Dictionary<CSteamID, T> GetAllValues()
GetValue(CSteamID)
public T GetValue(CSteamID playerId)

The player's value, or the default value if not set.
Forces a refresh of all values from Steam member data.
Events
Occurs when the local player's value changes.
Event Type
Action<T, T>
Remarks
This is a convenience event that ﬁlters OnValueChanged to only ﬁre for the local player.
Parameters: (oldValue, newValue)
Occurs when a sync operation fails (serialization error, etc.).
Event Type
Action<Exception>
Refresh()
public void Refresh()
OnMyValueChanged
public event Action<T, T>? OnMyValueChanged
OnSyncError
public event Action<Exception>? OnSyncError

Occurs when any player's value changes.
Event Type
Action<CSteamID, T, T>
Remarks
Parameters: (playerId, oldValue, newValue)
OnValueChanged
public event Action<CSteamID, T, T> OnValueChanged

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Validator that combines multiple validators with AND logic.
Type Parameters
T
The type of value to validate.
Inheritance
object  CompositeValidator<T>
Implements
ISyncValidator<T>
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
Validation Logic: All validators must pass for the value to be considered valid. Validators
are checked in order, and the ﬁrst failure determines the error message.
Use Cases:
Combining range validation with custom business rules
Validating multiple independent constraints
Reusing existing validators in complex validation scenarios
Performance: Validators are evaluated sequentially. Order validators from fastest to
slowest for optimal performance (fail fast on cheap checks).
Class CompositeValidator<T>
public class CompositeValidator<T> : ISyncValidator<T>


Constructors
Initializes a new instance of the CompositeValidator<T> class.
Parameters
validators ISyncValidator<T>[]
Array of validators to check. All must pass for validation to succeed.
Remarks
// Combine range and custom validation
var healthValidator = new CompositeValidator<int>(
    new RangeValidator<int>(0, 100),
    new PredicateValidator<int>(
        health => health % 10 == 0,
        "Health must be a multiple of 10"
    )
);
// Multiple string constraints
var usernameValidator = new CompositeValidator<string>(
    new PredicateValidator<string>(
        s => !string.IsNullOrWhiteSpace(s),
        "Username cannot be empty"
    ),
    new PredicateValidator<string>(
        s => s.Length >= 3 && s.Length <= 20,
        "Username must be 3-20 characters"
    ),
    new PredicateValidator<string>(
        s => s.All(char.IsLetterOrDigit),
        "Username must be alphanumeric"
    )
);
CompositeValidator(params ISyncValidator<T>[])
public CompositeValidator(params ISyncValidator<T>[] validators)

Validators are evaluated in the order provided.
The error message from the ﬁrst failing validator is returned.
If no validators are provided, all values pass validation.
Exceptions
ArgumentNullException
Thrown when validators array is null.
Methods
Gets a human-readable error message describing why validation failed.
Parameters
value T
The invalid value that failed validation.
Returns
string
An error message describing why the value is invalid, or null if no speciﬁc message is
available.
Remarks
This message is used in SyncValidationException and logged when validation fails.
Include the invalid value and expected constraints in the message for easier debugging.
GetErrorMessage(T)
public string? GetErrorMessage(T value)
IsValid(T)

Validates a value before it is synchronized.
Parameters
value T
The value to validate.
Returns
bool
True if the value is valid and should be synchronized; false otherwise.
Remarks
This method is called synchronously before each sync operation.
Keep validation logic fast and non-blocking as it runs on the main thread.
public bool IsValid(T value)

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
A host-authoritative synchronized variable that automatically keeps its value in sync across
all lobby members using Steam lobby data.
Type Parameters
T
The type of value to synchronize.
Inheritance
object  HostSyncVar<T>
Implements
IDisposable
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
Authority Model: Only the lobby host can modify this value. Non-host clients can read the
value and observe changes, but writes are silently ignored.
Storage: Uses Steam lobby data for storage, which is automatically synchronized to all
lobby members by Steam.
Supported Types:
Primitives: int, ﬂoat, double, bool, string, long, etc.
Enums: Serialized as underlying integer values
Collections: List<T>, Dictionary<string, T>, arrays
Custom types: Classes with parameterless constructor and public properties
Class HostSyncVar<T>
public class HostSyncVar<T> : IDisposable


Properties
Gets whether the local player can modify this value (i.e., is host).
Property Value
bool
Gets the full key including any preﬁx.
Property Value
string
Gets whether there is a pending value waiting to be synced.
// Create a host-authoritative sync var
var roundNumber = client.CreateHostSyncVar("Round", 1);
// Subscribe to changes
roundNumber.OnValueChanged += (oldVal, newVal) => 
    MelonLogger.Msg($"Round changed: {oldVal} -> {newVal}");
// Only host can modify - silently ignored for non-hosts
roundNumber.Value = 2;
CanWrite
public bool CanWrite { get; }
FullKey
public string FullKey { get; }
IsDirty

Property Value
bool
Remarks
This is true when AutoSync is disabled or when rate limiting has deferred a sync.
Gets the sync key used for this variable.
Property Value
string
Gets or sets the synchronized value.
Property Value
T
Remarks
Reading: Always returns the current synchronized value.
Writing: Only takes eﬀect when called by the lobby host. Non-host writes are silently
ignored (or logged if WarnOnIgnoredWrites is enabled).
public bool IsDirty { get; }
Key
public string Key { get; }
Value
public T Value { get; set; }

Methods
Releases all resources used by the HostSyncVar<T>.
Forces immediate sync of any pending value, bypassing rate limit.
Remarks
Use this when AutoSync is disabled to manually trigger sync operations, or to force a rate-
limited pending value to sync immediately.
Forces a refresh of the value from the lobby data.
Remarks
Normally not needed as changes are automatically detected, but useful after joining a
lobby or when debugging synchronization issues.
Events
Occurs when a sync operation fails (serialization error, etc.).
Dispose()
public void Dispose()
FlushPending()
public void FlushPending()
Refresh()
public void Refresh()
OnSyncError

Event Type
Action<Exception>
Occurs when the value changes from any source (local or remote).
Event Type
Action<T, T>
Remarks
Parameters: (oldValue, newValue)
Occurs when a non-host attempts to write a value.
Event Type
Action<T>
Remarks
This event is primarily for debugging purposes.
Parameter: the attempted value that was ignored
public event Action<Exception>? OnSyncError
OnValueChanged
public event Action<T, T>? OnValueChanged
OnWriteIgnored
public event Action<T>? OnWriteIgnored

Namespace: SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Interface for custom value serialization in HostSyncVar<T> and ClientSyncVar<T>.
Remarks
Implement this interface to provide custom serialization logic for complex types or when
the default JsonSyncSerializer doesn't meet your needs.
Methods
Checks if a type can be serialized by this serializer.
Interface ISyncSerializer
public interface ISyncSerializer
public class MyCustomSerializer : ISyncSerializer
{
    public string Serialize<T>(T value)
    {
        // Custom serialization logic
        return MySerializationLibrary.ToJson(value);
    }
    public T Deserialize<T>(string data)
    {
        // Custom deserialization logic
        return MySerializationLibrary.FromJson<T>(data);
    }
    public bool CanSerialize(Type type)
    {
        return MySerializationLibrary.IsSupported(type);
    }
}
CanSerialize(Type)

Parameters
type Type
The type to check.
Returns
bool
True if the type can be serialized; otherwise, false.
Remarks
This method is called during HostSyncVar<T> and ClientSyncVar<T> creation to validate
that the type parameter is serializable.
Deserializes a string back to the original value type.
Parameters
data string
The serialized string data.
Returns
T
The deserialized value.
Type Parameters
T
bool CanSerialize(Type type)
Deserialize<T>(string)
T Deserialize<T>(string data)

The type to deserialize to.
Exceptions
SyncSerializationException
Thrown when deserialization fails.
Serializes a value to a string for network transmission.
Parameters
value T
The value to serialize.
Returns
string
A string representation of the value.
Type Parameters
T
The type of value to serialize.
Exceptions
SyncSerializationException
Thrown when serialization fails.
Serialize<T>(T)
string Serialize<T>(T value)

Namespace: SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Interface for validating sync var values before they are synchronized.
Type Parameters
T
The type of value to validate.
Remarks
Validation Flow: Validators are invoked before a value is synchronized. If validation fails,
the sync operation is blocked and an error is reported.
Error Handling: Validation errors can either throw SyncValidationException or be reported
via the OnSyncError/OnSyncError event, depending on the ThrowOnValidationError setting.
Use Cases:
Enforcing numeric ranges (e.g., health must be 0-100)
Validating string formats (e.g., usernames must be alphanumeric)
Checking enum values are within valid ranges
Complex business logic validation
Built-in Validators:
PredicateValidator<T> - Custom validation using a lambda
RangeValidator<T> - Numeric range validation
CompositeValidator<T> - Combine multiple validators
Interface ISyncValidator<T>
public interface ISyncValidator<T>
// Create a validator that ensures health stays within 0-100
var healthValidator = new RangeValidator<int>(0, 100);
// Create a custom validator using predicate
var nameValidator = new PredicateValidator<string>(
    name => !string.IsNullOrEmpty(name) && name.Length <= 20,

Methods
Gets a human-readable error message describing why validation failed.
Parameters
value T
The invalid value that failed validation.
Returns
string
An error message describing why the value is invalid, or null if no speciﬁc message is
available.
Remarks
This message is used in SyncValidationException and logged when validation fails.
Include the invalid value and expected constraints in the message for easier debugging.
Validates a value before it is synchronized.
    "Name must be 1-20 characters"
);
// Use with a sync var
var health = client.CreateHostSyncVar("Health", 100, validator: healthValidator);
GetErrorMessage(T)
string? GetErrorMessage(T value)
IsValid(T)
bool IsValid(T value)

Parameters
value T
The value to validate.
Returns
bool
True if the value is valid and should be synchronized; false otherwise.
Remarks
This method is called synchronously before each sync operation.
Keep validation logic fast and non-blocking as it runs on the main thread.

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Default JSON serializer for HostSyncVar<T> and ClientSyncVar<T>. Provides IL2CPP-
compatible JSON serialization without external dependencies.
Inheritance
object  JsonSyncSerializer
Implements
ISyncSerializer
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
Supported Types:
Primitives: int, long, ﬂoat, double, bool, string, byte, short, uint, ulong
Enums: Any enum type (serialized as underlying integer value)
Collections: List<T>, arrays (T[]), Dictionary<string, T>
Custom types: Classes and structs with parameterless constructor and public
properties
Requirements for Custom Types:
1. Must have a public parameterless constructor
2. Properties must be public with both getter and setter
3. Property types must themselves be serializable
4. Circular references are not supported
Class JsonSyncSerializer
public class JsonSyncSerializer : ISyncSerializer

// Valid custom type
public class GameSettings
{

Constructors
Methods
Checks if a type can be serialized by this serializer.
Parameters
type Type
The type to check.
Returns
bool
True if the type can be serialized; otherwise, false.
    public int MaxPlayers { get; set; } = 4;
    public string MapName { get; set; } = "default";
    public bool FriendlyFire { get; set; } = false;
    public List<string> EnabledMods { get; set; } = new();
}
// Usage
var settings = client.CreateHostSyncVar("Settings", new GameSettings());
JsonSyncSerializer()
public JsonSyncSerializer()
CanSerialize(Type)
public bool CanSerialize(Type type)
Deserialize<T>(string)

Deserializes a JSON string to the speciﬁed type.
Parameters
data string
The JSON string to deserialize.
Returns
T
The deserialized value.
Type Parameters
T
The type to deserialize to.
Exceptions
SyncSerializationException
Thrown when deserialization fails.
Serializes a value to a JSON string.
Parameters
value T
The value to serialize.
public T Deserialize<T>(string data)
Serialize<T>(T)
public string Serialize<T>(T value)

Returns
string
A JSON string representation of the value.
Type Parameters
T
The type of value to serialize.
Exceptions
SyncSerializationException
Thrown when serialization fails.

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Conﬁguration options for HostSyncVar<T> and ClientSyncVar<T> behavior. All properties
have sensible defaults for the simplest developer experience.
Inheritance
object  NetworkSyncOptions
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
This class follows the same pattern as NetworkRules - providing high-level abstractions with
sensible defaults while allowing advanced conﬁguration.
Constructors
Creates a new instance with default options.
Class NetworkSyncOptions
public class NetworkSyncOptions

// Simple usage with defaults
var score = client.CreateHostSyncVar("Score", 0);
// Advanced usage with custom options
var options = new NetworkSyncOptions
{
    KeyPrefix = "MyMod_",
    WarnOnIgnoredWrites = true
};
var settings = client.CreateHostSyncVar("Settings", new GameSettings(), options);
NetworkSyncOptions()

Properties
If true, automatically syncs value changes when the value is set. Default: true
Property Value
bool
Remarks
When enabled, setting Value immediately propagates the change to all connected clients.
Disable this if you need to batch multiple changes before syncing manually with Flush().
Key preﬁx to avoid collisions with other mods using SteamNetworkLib. Default: null (no
preﬁx)
Property Value
string
Remarks
Recommended for published mods to prevent key collisions.
The ﬁnal key will be: {KeyPrefix}{key}
public NetworkSyncOptions()
AutoSync
public bool AutoSync { get; set; }
KeyPreﬁx
public string? KeyPrefix { get; set; }
var options = new NetworkSyncOptions { KeyPrefix = "MyMod_" };
var score = client.CreateHostSyncVar("Score", 0, options);

Maximum number of syncs per second. If 0, no rate limiting is applied. Default: 0
(unlimited)
Property Value
int
Remarks
When set to a value greater than 0, rapid value changes will be throttled to this maximum
rate. Useful for high-frequency updates like player positions.
The latest value is always sent when the rate limit expires.
Custom serializer for the value type. If null, uses the default JsonSyncSerializer.
Property Value
ISyncSerializer
Remarks
Implement ISyncSerializer for custom serialization logic.
The default JSON serializer supports:
Primitives: int, ﬂoat, double, bool, string, long
Collections: List<T>, Dictionary<string, T>, arrays
Custom types with parameterless constructor and public properties
// Actual Steam lobby data key: "MyMod_Score"
MaxSyncsPerSecond
public int MaxSyncsPerSecond { get; set; }
Serializer
public ISyncSerializer? Serializer { get; set; }

If true, syncs current value to newly joined players. Default: true
Property Value
bool
Remarks
This ensures late-joining players receive the current state. For HostSyncVar<T>, the host
re-broadcasts the value. For ClientSyncVar<T>, each player's value is already available via
Steam member data.
If true, validation errors throw exceptions. If false, they are logged and invoke OnSyncError.
Default: false
Property Value
bool
Remarks
When false (default), validation errors are handled gracefully and don't interrupt execution.
When true, validation errors will throw SyncValidationException.
If true, logs a warning when a non-host attempts to write to a HostSyncVar<T>. Default:
false
SyncOnPlayerJoin
public bool SyncOnPlayerJoin { get; set; }
ThrowOnValidationError
public bool ThrowOnValidationError { get; set; }
WarnOnIgnoredWrites

Property Value
bool
Remarks
Useful for debugging during development to catch accidental writes from non-host clients.
The write is always silently ignored regardless of this setting - this only controls logging.
public bool WarnOnIgnoredWrites { get; set; }

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Base class for simple validators using a predicate function.
Type Parameters
T
The type of value to validate.
Inheritance
object  PredicateValidator<T>
Implements
ISyncValidator<T>
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
Best For: One-oﬀ custom validation logic without creating a dedicated validator class.
Performance: The predicate is invoked directly without additional overhead.
Class PredicateValidator<T>
public class PredicateValidator<T> : ISyncValidator<T>

// Validate that a string is not empty and has valid length
var usernameValidator = new PredicateValidator<string>(
    username => !string.IsNullOrWhiteSpace(username) && username.Length >= 3 && 
username.Length <= 20,
    "Username must be 3-20 characters and not whitespace"
);
// Validate enum is defined
var gameModeValidator = new PredicateValidator<GameMode>(
    mode => Enum.IsDefined(typeof(GameMode), mode),

Constructors
Creates a new predicate-based validator.
Parameters
predicate Func<T, bool>
Function that returns true if the value is valid.
errorMessage string
Error message to return when validation fails.
Remarks
The predicate should be pure (no side eﬀects) as it may be called multiple times.
The error message should clearly explain what validation failed for debugging purposes.
Exceptions
ArgumentNullException
Thrown when predicate is null.
Methods
Gets a human-readable error message describing why validation failed.
    "Invalid game mode selected"
);
PredicateValidator(Func<T, bool>, string)
public PredicateValidator(Func<T, bool> predicate, string errorMessage)
GetErrorMessage(T)

Parameters
value T
The invalid value that failed validation.
Returns
string
An error message describing why the value is invalid, or null if no speciﬁc message is
available.
Remarks
This message is used in SyncValidationException and logged when validation fails.
Include the invalid value and expected constraints in the message for easier debugging.
Validates a value before it is synchronized.
Parameters
value T
The value to validate.
Returns
bool
True if the value is valid and should be synchronized; false otherwise.
Remarks
public string? GetErrorMessage(T value)
IsValid(T)
public bool IsValid(T value)

This method is called synchronously before each sync operation.
Keep validation logic fast and non-blocking as it runs on the main thread.

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Validator for numeric ranges with inclusive or exclusive bounds.
Type Parameters
T
The numeric type to validate. Must implement IComparable<T>.
Inheritance
object  RangeValidator<T>
Implements
ISyncValidator<T>
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
Supported Types: Any type implementing IComparable<T> including int, ﬂoat,
double, decimal, long, etc.
Bounds: By default, the range is inclusive (min and max are valid values). Use inclusive:
false for exclusive bounds.
Use Cases:
Health/energy values (0-100)
Position coordinates within map bounds
Percentage values (0.0-1.0)
Count limits (0-maxItems)
Class RangeValidator<T>
public class RangeValidator<T> : ISyncValidator<T> where T : IComparable<T>


Constructors
Creates a new range validator.
Parameters
min T
Minimum allowed value.
max T
Maximum allowed value.
inclusive bool
If true (default), min and max are valid values. If false, values must be strictly between
min and max.
Remarks
When inclusive is true, the valid range is [min, max].
When inclusive is false, the valid range is (min, max).
Exceptions
ArgumentException
// Health must be between 0 and 100 (inclusive)
var healthValidator = new RangeValidator<int>(0, 100);
// Percentage must be 0.0 to 1.0 (inclusive)
var percentValidator = new RangeValidator<float>(0f, 1f);
// Exclusive range - value must be strictly between min and max
var exclusiveValidator = new RangeValidator<int>(0, 100, inclusive: false);
RangeValidator(T, T, bool)
public RangeValidator(T min, T max, bool inclusive = true)

Thrown when min is greater than max.
Methods
Gets a human-readable error message describing why validation failed.
Parameters
value T
The invalid value that failed validation.
Returns
string
An error message describing why the value is invalid, or null if no speciﬁc message is
available.
Remarks
This message is used in SyncValidationException and logged when validation fails.
Include the invalid value and expected constraints in the message for easier debugging.
Validates a value before it is synchronized.
Parameters
value T
GetErrorMessage(T)
public string? GetErrorMessage(T value)
IsValid(T)
public bool IsValid(T value)

The value to validate.
Returns
bool
True if the value is valid and should be synchronized; false otherwise.
Remarks
This method is called synchronously before each sync operation.
Keep validation logic fast and non-blocking as it runs on the main thread.

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Exception thrown when a sync operation fails due to network or state issues.
Inheritance
object  Exception  SyncException
Implements
ISerializable
Inherited Members
Exception.GetBaseException() , Exception.ToString() , Exception.GetType() ,
Exception.TargetSite , Exception.Message , Exception.Data ,
Exception.InnerException , Exception.HelpLink , Exception.Source ,
Exception.HResult , Exception.StackTrace , Exception.SerializeObjectState ,
object.MemberwiseClone() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
This exception is thrown when:
Network connectivity issues prevent sync operations
Steam network state is invalid or disconnected
Sync operations fail due to internal Steamworks API errors
Operation timeouts occur during sync
This exception provides information about the sync key when available and wraps any
underlying Steamworks API exceptions.
Constructors
Initializes a new instance of the SyncException class.
Class SyncException
public class SyncException : Exception, ISerializable


SyncException(string)

Parameters
message string
The error message.
Initializes a new instance of the SyncException class.
Parameters
message string
The error message.
innerException Exception
The inner exception that caused this exception.
Initializes a new instance of the SyncException class with key information.
Parameters
message string
The error message.
syncKey string
The sync key associated with the operation.
public SyncException(string message)
SyncException(string, Exception)
public SyncException(string message, Exception innerException)
SyncException(string, string)
public SyncException(string message, string syncKey)

Initializes a new instance of the SyncException class with full details.
Parameters
message string
The error message.
syncKey string
The sync key associated with the operation.
innerException Exception
The inner exception that caused this exception.
Properties
Gets the key associated with the sync operation, if available.
Property Value
string
SyncException(string, string, Exception)
public SyncException(string message, string syncKey, Exception innerException)
SyncKey
public string? SyncKey { get; }

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Exception thrown when serialization or deserialization fails in sync operations.
Inheritance
object  Exception  SyncSerializationException
Implements
ISerializable
Inherited Members
Exception.GetBaseException() , Exception.ToString() , Exception.GetType() ,
Exception.TargetSite , Exception.Message , Exception.Data ,
Exception.InnerException , Exception.HelpLink , Exception.Source ,
Exception.HResult , Exception.StackTrace , Exception.SerializeObjectState ,
object.MemberwiseClone() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
This exception is thrown when:
A type cannot be serialized (unsupported type)
Serialization fails due to circular references or other issues
Deserialization fails due to malformed data or type mismatch
This exception provides detailed information about the type that failed to
serialize/deserialize and the associated sync key when available.
Constructors
Initializes a new instance of the SyncSerializationException class.
Class SyncSerializationException
public class SyncSerializationException : Exception, ISerializable


SyncSerializationException(string)

Parameters
message string
The error message.
Initializes a new instance of the SyncSerializationException class.
Parameters
message string
The error message.
innerException Exception
The inner exception that caused this exception.
Initializes a new instance of the SyncSerializationException class with type information.
Parameters
message string
The error message.
targetType Type
The type that failed to serialize/deserialize.
public SyncSerializationException(string message)
SyncSerializationException(string, Exception)
public SyncSerializationException(string message, Exception innerException)
SyncSerializationException(string, Type)
public SyncSerializationException(string message, Type targetType)

Initializes a new instance of the SyncSerializationException class with type and key
information.
Parameters
message string
The error message.
targetType Type
The type that failed to serialize/deserialize.
syncKey string
The sync key associated with the operation.
Initializes a new instance of the SyncSerializationException class with full details.
Parameters
message string
The error message.
targetType Type
The type that failed to serialize/deserialize.
syncKey string
The sync key associated with the operation.
SyncSerializationException(string, Type, string)
public SyncSerializationException(string message, Type targetType, string syncKey)
SyncSerializationException(string, Type, string,
Exception)
public SyncSerializationException(string message, Type targetType, string syncKey, 
Exception innerException)

innerException Exception
The inner exception that caused this exception.
Properties
Gets the key associated with the sync operation, if available.
Property Value
string
Gets the type that failed to serialize/deserialize, if available.
Property Value
Type
SyncKey
public string? SyncKey { get; }
TargetType
public Type? TargetType { get; }

Namespace:SteamNetworkLib.Sync
Assembly:SteamNetworkLib.dll
Exception thrown when value validation fails before sync.
Inheritance
object  Exception  SyncValidationException
Implements
ISerializable
Inherited Members
Exception.GetBaseException() , Exception.ToString() , Exception.GetType() ,
Exception.TargetSite , Exception.Message , Exception.Data ,
Exception.InnerException , Exception.HelpLink , Exception.Source ,
Exception.HResult , Exception.StackTrace , Exception.SerializeObjectState ,
object.MemberwiseClone() , object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Remarks
This exception is thrown when:
A value fails validation rules before being synced
The value is null when a non-null value is required
The value type does not match the expected type for the sync key
This exception provides information about the invalid value and the associated sync key
when available.
Constructors
Initializes a new instance of the SyncValidationException class.
Class SyncValidationException
public class SyncValidationException : Exception, ISerializable


SyncValidationException(string)

Parameters
message string
The error message.
Initializes a new instance of the SyncValidationException class.
Parameters
message string
The error message.
syncKey string
The sync key associated with the operation.
invalidValue object
The value that failed validation.
Properties
Gets the invalid value, if available.
Property Value
public SyncValidationException(string message)
SyncValidationException(string, string, object?)
public SyncValidationException(string message, string syncKey, object? invalidValue)
InvalidValue
public object? InvalidValue { get; }

object
Gets the key associated with the sync operation, if available.
Property Value
string
SyncKey
public string? SyncKey { get; }

Classes
AudioStreamingCompatibility
Provides runtime compatibility detection for audio streaming features. Audio streaming
requires Mono runtime due to OpusSharp limitations with IL2CPP.
MessageSerializer
Utility methods for message serialization and deserialization.
SteamNetworkUtils
Utility methods for Steam networking operations.
Namespace SteamNetworkLib.Utilities

Namespace:SteamNetworkLib.Utilities
Assembly:SteamNetworkLib.dll
Provides runtime compatibility detection for audio streaming features. Audio streaming
requires Mono runtime due to OpusSharp limitations with IL2CPP.
Inheritance
object  AudioStreamingCompatibility
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Properties
Gets whether audio streaming is supported in the current runtime environment.
Property Value
bool
Remarks
Audio streaming is only supported on Mono runtime due to OpusSharp library limitations.
IL2CPP has memory marshalling issues that cause corruption when passing audio data
between managed and native code through OpusSharp.
Class AudioStreamingCompatibility
public static class AudioStreamingCompatibility

IsSupported
public static bool IsSupported { get; }
RuntimeType

Gets the current runtime type being used.
Property Value
string
Methods
Gets a detailed explanation of why audio streaming may not be supported.
Returns
string
Logs a warning message about audio streaming compatibility to the console.
Throws an exception if audio streaming is not supported in the current environment.
Exceptions
PlatformNotSupportedException
public static string RuntimeType { get; }
GetCompatibilityMessage()
public static string GetCompatibilityMessage()
LogCompatibilityWarning()
public static void LogCompatibilityWarning()
ThrowIfNotSupported()
public static void ThrowIfNotSupported()

Thrown when audio streaming is not supported.

Namespace:SteamNetworkLib.Utilities
Assembly:SteamNetworkLib.dll
Utility methods for message serialization and deserialization.
Inheritance
object  MessageSerializer
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Fields
Header identiﬁer for SteamNetworkLib messages to validate message authenticity.
Field Value
string
Methods
Creates a P2P message instance from serialized data.
Class MessageSerializer
public static class MessageSerializer

MESSAGE_HEADER
public const string MESSAGE_HEADER = "SNLM"
CreateMessage<T>(byte[])
public static T CreateMessage<T>(byte[] data) where T : P2PMessage, new()

Parameters
data byte[]
The serialized data.
Returns
T
Deserialized message instance.
Type Parameters
T
The message type to create.
Exceptions
P2PException
Thrown when creation fails or type mismatch occurs.
Deserializes a byte array to extract message type and data.
Parameters
data byte[]
The serialized data.
Returns
(string MessageType, byte[] MessageData)
Tuple containing message type and message data.
DeserializeMessage(byte[])
public static (string MessageType, byte[] MessageData) DeserializeMessage(byte[] 
data)

Exceptions
P2PException
Thrown when deserialization fails or data is invalid.
Gets the message type from serialized data without full deserialization.
Parameters
data byte[]
The serialized data.
Returns
string
Message type string, or null if invalid.
Validates that data contains a valid SteamNetworkLib message.
Parameters
data byte[]
The data to validate.
Returns
bool
GetMessageType(byte[])
public static string? GetMessageType(byte[] data)
IsValidMessage(byte[])
public static bool IsValidMessage(byte[] data)

True if the data contains a valid message.
Serializes a P2P message to a byte array with header validation.
Parameters
message P2PMessage
The message to serialize.
Returns
byte[]
Serialized message data.
Exceptions
P2PException
Thrown when serialization fails or message is too large.
SerializeMessage(P2PMessage)
public static byte[] SerializeMessage(P2PMessage message)

Namespace:SteamNetworkLib.Utilities
Assembly:SteamNetworkLib.dll
Utility methods for Steam networking operations.
Inheritance
object  SteamNetworkUtils
Inherited Members
object.GetType() , object.MemberwiseClone() , object.ToString() ,
object.Equals(object) , object.Equals(object, object) ,
object.ReferenceEquals(object, object) , object.GetHashCode()
Methods
Formats a lobby type for display.
Parameters
lobbyType ELobbyType
The lobby type.
Returns
string
Human-readable lobby type string.
Class SteamNetworkUtils
public static class SteamNetworkUtils

FormatLobbyType(ELobbyType)
public static string FormatLobbyType(ELobbyType lobbyType)

Formats a Steam result for display.
Parameters
result EResult
The Steam result.
Returns
string
Human-readable result string.
Gets the local Steam user's display name.
Returns
string
Display name of the local user.
Gets the display name for a speciﬁc Steam user.
Parameters
FormatSteamResult(EResult)
public static string FormatSteamResult(EResult result)
GetLocalPlayerName()
public static string GetLocalPlayerName()
GetPlayerName(CSteamID)
public static string GetPlayerName(CSteamID steamId)

steamId CSteamID
Steam ID of the user.
Returns
string
Display name of the user.
Checks if a speciﬁc user is a friend of the local player.
Parameters
steamId CSteamID
Steam ID to check.
Returns
bool
True if the user is a friend.
Checks if Steam is initialized and ready for networking.
Returns
bool
True if Steam is initialized.
IsFriend(CSteamID)
public static bool IsFriend(CSteamID steamId)
IsSteamInitialized()
public static bool IsSteamInitialized()

Validates that a Steam ID is valid and not nil. Compatible with both real Steam and
Goldberg Steam Emu.
Parameters
steamId CSteamID
Steam ID to validate.
Returns
bool
True if the Steam ID is valid.
Converts a string to a Steam ID safely.
Parameters
steamIdString string
String representation of Steam ID.
Returns
CSteamID?
Steam ID if valid, null otherwise.
IsValidSteamID(CSteamID)
public static bool IsValidSteamID(CSteamID steamId)
ParseSteamID(string)
public static CSteamID? ParseSteamID(string steamIdString)
