using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Crony
{
    /// <summary>
    /// Internet rooms: private rooms with a shareable join code, or public rooms found by Quick Join. The session's
    /// relay network starts the NGO host or client itself, so the rest of the game (approval, roles, chat, voice)
    /// runs exactly as it does on LAN. Rooms use host migration, so a match continues when its host leaves.
    /// </summary>
    public static class OnlineRooms
    {
        static ISession session;
        public static bool Busy { get; private set; }
        public static string Code => session != null ? session.Code : null;
        /// <summary>The code as it may be shown on screen: hidden in streamer mode.</summary>
        public static string DisplayCode => Code == null ? null : GameSettings.StreamerMode ? "******" : Code;

        // Indexed room properties: Quick Join only matches rooms on the same game version that are still in the lobby.
        const string VersionKey = "version", StateKey = "state";
        // How long clients wait for a new host before giving up on a migration.
        const double MigrationTimeoutSeconds = 30;
        // Room events can arrive off Unity's main thread, where Unity's clock can't be read, so this uses DateTime.
        static DateTime? migratingSince;

        public static bool IsPublic => session != null && !session.IsPrivate;
        /// <summary>True while the host has left and the room is moving to a new host.</summary>
        public static bool Migrating => migratingSince is DateTime since && (DateTime.UtcNow - since).TotalSeconds < MigrationTimeoutSeconds;

        static SessionOptions RoomOptions(bool isPublic) => new SessionOptions
        {
            MaxPlayers = GameManager.MaxPlayers,
            IsPrivate = !isPublic,
            SessionProperties = new Dictionary<string, SessionProperty>
            {
                { VersionKey, new SessionProperty(Application.version, VisibilityPropertyOptions.Public, PropertyIndex.String1) },
                { StateKey, new SessionProperty("lobby", VisibilityPropertyOptions.Public, PropertyIndex.String2) }
            }
        }.WithRelayNetwork().WithNetworkOptions(SecureWebSockets()).WithHostMigration(new GameManager.MigrationData());

        /// <summary>
        /// Rooms run over secure WebSockets: browsers can't use anything else, and using it everywhere lets browser
        /// and desktop players share a room.
        /// </summary>
        static NetworkOptions SecureWebSockets() => new NetworkOptions { RelayProtocol = RelayProtocol.WSS };

        /// <summary>Opens a room. Private rooms are joined by code; public rooms can also be found by Quick Join.</summary>
        public static async Task<string> HostAsync(bool isPublic = false)
        {
            await RunAsync(async () => Track(await MultiplayerService.Instance.CreateSessionAsync(RoomOptions(isPublic))));
            return Code;
        }

        public static Task JoinAsync(string code) =>
            RunAsync(async () => Track(await JoinOrReconnectAsync(code.Trim().ToUpperInvariant())));

        /// <summary>
        /// Joins by code. A player whose game closed or whose connection dropped is still a member of the room, so
        /// joining again fails with a conflict; reconnecting to that membership puts them back in instead.
        /// </summary>
        static async Task<ISession> JoinOrReconnectAsync(string code)
        {
            // Host migration is switched on per player, so joiners need it too: any of them may become the next host.
            try { return await MultiplayerService.Instance.JoinSessionByCodeAsync(code, new JoinSessionOptions().WithNetworkOptions(SecureWebSockets()).WithHostMigration(new GameManager.MigrationData())); }
            catch (SessionException e) when (e.Error == SessionError.SessionConflict)
            {
                foreach (string id in await MultiplayerService.Instance.GetJoinedSessionIdsAsync())
                {
                    ISession again = await MultiplayerService.Instance.ReconnectToSessionAsync(id);
                    if (again.Code == code) return again;
                    // An older room this player never left: leave it so it stops holding them.
                    try { await again.LeaveAsync(); }
                    catch (Exception leaveError) { Debug.LogWarning("Leaving an old room: " + leaveError.Message); }
                }
                throw;
            }
        }

        /// <summary>Joins a public room on this version that hasn't started yet, or opens a new public room if there is none.</summary>
        public static Task QuickJoinAsync() => RunAsync(async () =>
        {
            var quickJoin = new QuickJoinOptions
            {
                CreateSession = true,
                Timeout = TimeSpan.FromSeconds(8),
                Filters = new List<FilterOption>
                {
                    new FilterOption(FilterField.StringIndex1, Application.version, FilterOperation.Equal),
                    new FilterOption(FilterField.StringIndex2, "lobby", FilterOperation.Equal),
                    new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater)
                }
            };
            Track(await MultiplayerService.Instance.MatchmakeSessionAsync(quickJoin, RoomOptions(true)));
        });

        /// <summary>After a dropped connection: rejoin the same room by its code. The host gives the player their seat back.</summary>
        public static Task RejoinAsync(string code) =>
            RunAsync(async () => Track(await JoinOrReconnectAsync(code)));

        /// <summary>Host: marks the room as playing or back in the lobby, so Quick Join skips matches in progress.</summary>
        public static async void SetMatchInProgress(bool playing)
        {
            if (session == null || !session.IsHost) return;
            try
            {
                IHostSession host = session.AsHost();
                host.SetProperty(StateKey, new SessionProperty(playing ? "playing" : "lobby", VisibilityPropertyOptions.Public, PropertyIndex.String2));
                await host.SavePropertiesAsync();
            }
            catch (Exception e) { Debug.LogWarning("Updating room state: " + e.Message); }
        }

        /// <summary>Host: takes a removed player out of the room too, so they can't come back with the code.</summary>
        public static async void RemovePlayer(string playerId)
        {
            if (session == null || !session.IsHost || string.IsNullOrEmpty(playerId)) return;
            try { await session.AsHost().RemovePlayerAsync(playerId); }
            catch (Exception e) { Debug.LogWarning("Removing player from room: " + e.Message); }
        }

        static void Track(ISession joined)
        {
            session = joined;
            migratingSince = null;
            if (joined == null) return;
            joined.SessionHostChanged += host =>
            {
                Debug.Log("[Crony] Room host changed (new host " + host + ", this player is host: " + joined.IsHost + ").");
                // Only the room this player is in (or was just in, mid-rejoin) counts.
                if (session == joined || session == null) migratingSince = DateTime.UtcNow;
            };
            joined.SessionMigrated += () => { Debug.Log("[Crony] Room moved to the new host."); migratingSince = null; };
            joined.RemovedFromSession += () => Debug.Log("[Crony] Removed from the room.");
            joined.StateChanged += state => Debug.Log("[Crony] Room state: " + state);
            if (joined.Network != null)
            {
                joined.Network.MigrationFailed += error => { Debug.LogWarning("[Crony] Moving to a new host failed: " + error); migratingSince = null; };
                joined.Network.StateChanged += state => Debug.Log("[Crony] Room network: " + state);
            }
        }

        /// <summary>Leaves the room, which also shuts down the relay connection. Safe to call when not in a room.</summary>
        public static async Task LeaveAsync()
        {
            ISession leaving = session;
            session = null;
            migratingSince = null;
            if (leaving == null) return;
            try { await leaving.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("Leaving online room: " + e.Message); }
        }

        /// <summary>Clears a room whose network already ended (host closed it, kicked, connection lost).</summary>
        public static void Forget() { if (!Busy && session != null) _ = LeaveAsync(); }

        // Without a limit, an unreachable service (offline, firewall) left players on "Creating…" forever.
        const int ConnectTimeoutMs = 20000;

        static async Task RunAsync(Func<Task> action)
        {
            if (Busy) throw new InvalidOperationException("Already connecting to an online room.");
            Busy = true;
            try
            {
                Task work = ConnectAsync(action);
                if (await Task.WhenAny(work, Wait(ConnectTimeoutMs / 1000f)) != work)
                {
                    _ = CloseIfLateAsync(work);
                    throw new TimeoutException("Online room connection timed out.");
                }
                await work;
            }
            finally { Busy = false; }
        }

        /// <summary>"-authProfile NAME" gives a test window its own sign-in, so several windows on one computer are different players.</summary>
        static InitializationOptions ServiceOptions()
        {
            InitializationOptions options = new InitializationOptions();
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-authProfile", StringComparison.OrdinalIgnoreCase)) options.SetProfile(args[i + 1]);
            return options;
        }

        /// <summary>A delay that runs on Unity's main loop, so it also works in browser builds (no worker threads).</summary>
        static async Task Wait(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) await Task.Yield();
        }

        static async Task ConnectAsync(Func<Task> action)
        {
            await LeaveAsync();
            // Netcode must be told to use WebSockets to match the room's secure WebSocket relay.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport) transport.UseWebSockets = true;
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync(ServiceOptions());
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            await action();
        }

        /// <summary>A connection that finishes after the player was told it failed is closed, not left half-open.</summary>
        static async Task CloseIfLateAsync(Task work)
        {
            try { await work; }
            catch (Exception) { return; }
            await LeaveAsync();
        }

        public static string Describe(Exception e)
        {
            string message = e.Message ?? string.Empty;
            if (message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0)
                return "No room with that code. Check the code and try again.";
            if (message.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return "That room is full (8/8).";
            if (e is TimeoutException) return "Couldn't reach the online room service. Check your internet connection or firewall" + (Application.platform == RuntimePlatform.WebGLPlayer ? "." : ", or play on the same Wi-Fi (LAN).");
            return "Online rooms are unavailable right now. Check your internet connection, or play on LAN.";
        }
    }
}
