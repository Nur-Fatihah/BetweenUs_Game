using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Crony
{
    /// <summary>
    /// Match continuity: a player who drops can rejoin the same match, and an online room survives its host leaving.
    /// Network client ids change on every connection, so seats are keyed by a stable player key (the Unity sign-in
    /// id online, a per-run id on LAN) that each client sends when it joins.
    /// </summary>
    public sealed partial class GameManager
    {
        /// <summary>How long a dropped player's seat is kept for them before it counts as an empty one.</summary>
        public const float SeatHoldSeconds = 60f;

        [Serializable]
        sealed class SeatSnapshot
        {
            public string key, name;
            public PlayerRole role;
            public PlayerLifeState status;
            public float x, y;
            public bool emergencyUsed;
            [NonSerialized] public float heldUntil;
        }

        [Serializable]
        sealed class MatchSnapshot
        {
            public string version;
            public RoundPhase phase;
            public MatchRules rules;
            public float matchTimeRemaining;
            public bool recruitUsed;
            public int matchStartClientCount;
            public List<int> completedTasks = new List<int>();
            public List<SeatSnapshot> seats = new List<SeatSnapshot>();
        }

        readonly Dictionary<ulong, string> playerKeys = new Dictionary<ulong, string>();
        readonly Dictionary<string, SeatSnapshot> heldSeats = new Dictionary<string, SeatSnapshot>();
        // Recent state of every seated player. Depending on how a connection ends, Netcode may remove the player
        // object before the disconnect callback, so the seat is held from this copy.
        readonly Dictionary<ulong, SeatSnapshot> seatCache = new Dictionary<ulong, SeatSnapshot>();
        float seatCacheClock;
        // Set on the player chosen as the new host, before the new network (and this GameManager) spawns. Kept as raw
        // bytes because it can arrive off Unity's main thread; it is parsed when the new match spawns.
        static volatile byte[] pendingMigration;
        static readonly string runKey = Guid.NewGuid().ToString("N");

        /// <summary>This player's stable key: the Unity sign-in id when signed in (online rooms), else an id for this run.</summary>
        public static string LocalPlayerKey
        {
            get
            {
                try
                {
                    if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn) return AuthenticationService.Instance.PlayerId;
                }
                catch (Exception)
                {
                    // Unity Services not initialised (LAN only): fall back to the per-run key.
                }
                return runKey;
            }
        }

        static byte[] ConnectionPayload() => Encoding.UTF8.GetBytes(Application.version + "\n" + LocalPlayerKey);

        static void ReadPayload(byte[] payload, out string version, out string key)
        {
            string text = payload != null && payload.Length > 0 ? Encoding.UTF8.GetString(payload) : string.Empty;
            int split = text.IndexOf('\n');
            version = split >= 0 ? text.Substring(0, split) : (text.Length > 0 ? text : "unknown");
            key = split >= 0 ? text.Substring(split + 1) : string.Empty;
        }

        bool HasHeldSeat(string key) => !string.IsNullOrEmpty(key) && heldSeats.ContainsKey(key);
        bool KeyConnected(string key) => !string.IsNullOrEmpty(key) && playerKeys.ContainsValue(key);

        void ReplaceStaleConnection(string key)
        {
            foreach (KeyValuePair<ulong, string> pair in playerKeys)
            {
                if (pair.Value != key || pair.Key == NetworkManager.ServerClientId) continue;
                ulong stale = pair.Key;
                NetworkManager.DisconnectClient(stale, "You joined again from another connection.");
                // In case the disconnect callback hasn't run yet, make sure the seat is held and the key is free.
                if (playerKeys.ContainsKey(stale)) { HoldSeat(stale); playerKeys.Remove(stale); }
                return;
            }
        }

        SeatSnapshot Capture(ulong clientId)
        {
            PlayerController player = GetPlayer(clientId);
            if (player == null || !playerKeys.TryGetValue(clientId, out string key) || !serverRoles.TryGetValue(clientId, out PlayerRole role)) return null;
            Vector3 at = player.transform.position;
            return new SeatSnapshot
            {
                key = key, name = player.DisplayName.Value.ToString(), role = role,
                // A knocked-down player would have recovered by the time they are back.
                status = player.Status.Value == PlayerLifeState.Ejected ? PlayerLifeState.Ejected : PlayerLifeState.Alive,
                x = at.x, y = at.y, emergencyUsed = emergencyUsed.Contains(clientId)
            };
        }

        /// <summary>Called as a player disconnects mid-match: keep their seat so they can come back.</summary>
        void HoldSeat(ulong clientId)
        {
            SeatSnapshot cached = seatCache.TryGetValue(clientId, out SeatSnapshot copy) ? copy : null;
            seatCache.Remove(clientId);
            if (Phase.Value != RoundPhase.Playing && Phase.Value != RoundPhase.Meeting) return;
            SeatSnapshot seat = Capture(clientId) ?? cached;
            if (seat == null) return;
            seat.heldUntil = Time.time + SeatHoldSeconds;
            heldSeats[seat.key] = seat;
            NetworkManagerUI.SetNotice(seat.name + " lost connection. Their seat is held for " + (int)SeatHoldSeconds + "s.");
            Debug.Log("[Crony] Holding " + seat.name + "'s seat (" + seat.role + ").");
        }

        /// <summary>Server, every frame: gives returning players their seat back and frees seats that timed out.</summary>
        void UpdateSeats()
        {
            seatCacheClock -= Time.deltaTime;
            if (seatCacheClock <= 0f)
            {
                seatCacheClock = .5f;
                seatCache.Clear();
                if (Phase.Value == RoundPhase.Playing || Phase.Value == RoundPhase.Meeting)
                    foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                        if (HasLiveSeat(clientId) && Capture(clientId) is SeatSnapshot seat) seatCache[clientId] = seat;
            }
            if (heldSeats.Count == 0) return;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                if (!playerKeys.TryGetValue(clientId, out string key) || !heldSeats.TryGetValue(key, out SeatSnapshot seat)) continue;
                PlayerController player = GetPlayer(clientId);
                if (player == null) continue; // player object not spawned yet; try next frame
                RestoreSeat(clientId, player, seat);
                heldSeats.Remove(key);
                break; // the collection changed; the rest are handled next frame
            }
            List<string> expired = null;
            foreach (KeyValuePair<string, SeatSnapshot> pair in heldSeats)
                if (Time.time > pair.Value.heldUntil) (expired ?? (expired = new List<string>())).Add(pair.Key);
            if (expired == null) return;
            foreach (string key in expired)
            {
                NetworkManagerUI.SetNotice(heldSeats[key].name + " didn't come back in time.");
                Debug.Log("[Crony] " + heldSeats[key].name + "'s held seat expired.");
                heldSeats.Remove(key);
            }
        }

        void RestoreSeat(ulong clientId, PlayerController player, SeatSnapshot seat)
        {
            serverRoles[clientId] = seat.role;
            player.AssignRoleServer(seat.role);
            if (seat.status == PlayerLifeState.Ejected) player.ServerEject();
            Vector2 at = new Vector2(seat.x, seat.y);
            player.transform.position = at;
            if (player.TryGetComponent(out Rigidbody2D body)) body.position = at;
            if (seat.emergencyUsed) emergencyUsed.Add(clientId);
            if (seat.role == PlayerRole.Crony)
            {
                foreach (KeyValuePair<ulong, PlayerRole> pair in serverRoles)
                    if (pair.Value == PlayerRole.Impostor && GetPlayer(pair.Key) is PlayerController impostor)
                        CronyRecruitedClientRpc(impostor.DisplayName.Value.ToString(), TargetChat(clientId));
            }
            NetworkManagerUI.SetNotice(seat.name + " is back in the match.");
            Debug.Log("[Crony] Restored " + seat.name + " as " + seat.role + ".");
        }

        /// <summary>
        /// Whether a connection counts toward the teams right now: not a stale connection being replaced (no key),
        /// and not a returning player whose held seat hasn't been restored yet (the held seat counts instead).
        /// </summary>
        bool HasLiveSeat(ulong clientId) => playerKeys.TryGetValue(clientId, out string key) && !heldSeats.ContainsKey(key);

        /// <summary>Held seats still count toward each team, so a brief drop can't hand the other side a win.</summary>
        void CountHeldSeats(ref int impostors, ref int team, ref int crew)
        {
            foreach (SeatSnapshot seat in heldSeats.Values)
            {
                if (seat.status != PlayerLifeState.Alive) continue;
                if (seat.role == PlayerRole.Impostor) impostors++;
                if (seat.role != PlayerRole.Crewmate) team++;
                else crew++;
            }
        }

        // ---- Host migration (online rooms) ----

        /// <summary>
        /// Serialises the match for Unity's host migration. The host's copy is uploaded every few seconds; if the
        /// host leaves, the player chosen as the new host receives it before their network starts.
        /// </summary>
        public sealed class MigrationData : IMigrationDataHandler
        {
            public byte[] Generate() => Instance != null && Instance.IsServer ? Instance.CaptureMatch() : Array.Empty<byte>();

            public void Apply(byte[] migrationData)
            {
                if (migrationData != null && migrationData.Length > 0) pendingMigration = migrationData;
            }
        }

        byte[] CaptureMatch()
        {
            MatchSnapshot snapshot = new MatchSnapshot
            {
                version = Application.version, phase = Phase.Value, rules = Rules.Value, matchTimeRemaining = matchTimeRemaining,
                recruitUsed = recruitUsed, matchStartClientCount = matchStartClientCount
            };
            foreach (TownshipTaskStation station in FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude))
                if (station.IsSpawned && station.Completed.Value) snapshot.completedTasks.Add((int)station.Kind);
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                if (Capture(clientId) is SeatSnapshot seat) snapshot.seats.Add(seat);
            snapshot.seats.AddRange(heldSeats.Values);
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(snapshot));
        }

        /// <summary>On the new host: rebuild the match from the last snapshot. Every seat is held until its player rejoins.</summary>
        void ApplyPendingMigration()
        {
            byte[] data = pendingMigration;
            pendingMigration = null;
            if (data == null) { if (OnlineRooms.Code != null) Debug.Log("[Crony] Network started without a host snapshot."); return; }
            MatchSnapshot snapshot = JsonUtility.FromJson<MatchSnapshot>(Encoding.UTF8.GetString(data));
            if (snapshot == null || snapshot.version != Application.version) return;
            Rules.Value = snapshot.rules.Sanitized();
            if (snapshot.phase == RoundPhase.Menu || snapshot.phase == RoundPhase.Results) return;

            matchTimeRemaining = snapshot.matchTimeRemaining;
            recruitUsed = snapshot.recruitUsed;
            matchStartClientCount = snapshot.matchStartClientCount;
            totalTasks = Mathf.Max(1, FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude).Length);
            completedTaskIds.Clear();
            foreach (TownshipTaskStation station in FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude))
            {
                station.ResetForNewMatchServer();
                if (snapshot.completedTasks.Contains((int)station.Kind)) { station.RestoreCompletedServer(); RegisterCompletedTask(station); }
            }
            serverRoles.Clear();
            heldSeats.Clear();
            foreach (SeatSnapshot seat in snapshot.seats)
            {
                seat.heldUntil = Time.time + SeatHoldSeconds;
                heldSeats[seat.key] = seat;
            }
            // A meeting can't be resumed mid-vote; everyone continues the round instead.
            Phase.Value = RoundPhase.Playing;
            NetworkManagerUI.SetNotice("The host left. You are the new host, and the match continues.");
            Debug.Log("[Crony] Rebuilt the match from the previous host's snapshot: " + snapshot.seats.Count + " seats, " + snapshot.completedTasks.Count + " tasks done, " + matchTimeRemaining.ToString("0") + "s left.");
        }
    }
}
