using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Crony
{
    public enum PlayerLifeState : byte { Alive, Incapacitated, Ejected, Disconnected }
    public enum SabotageType : byte { None, GasLeak, PowerBlackout, WaterContamination, Interference, RouteBlockade }

    /// <summary>Match rules the host picks in the lobby. Synced to everyone so the lobby can show them.</summary>
    [Serializable]
    public struct MatchRules : INetworkSerializable, IEquatable<MatchRules>
    {
        /// <summary>0 means automatic: one Impostor per five players.</summary>
        public byte Impostors;
        public byte DiscussSeconds, VoteSeconds, KnockCooldown, RoundMinutes;

        public static readonly byte[] ImpostorOptions = { 0, 1, 2 }, DiscussOptions = { 15, 30, 45 }, VoteOptions = { 30, 45, 60 },
            CooldownOptions = { 20, 25, 35, 45 }, RoundOptions = { 6, 8, 10 };
        public static MatchRules Default => new MatchRules { Impostors = 0, DiscussSeconds = 15, VoteSeconds = 30, KnockCooldown = 25, RoundMinutes = 8 };

        /// <summary>Impostors for this many players, always leaving the crew a majority.</summary>
        public int ImpostorsFor(int players)
        {
            if (players <= 1) return 0;
            int wanted = Impostors == 0 ? Mathf.Max(1, players / 5) : Impostors;
            return Mathf.Clamp(wanted, 1, Mathf.Max(1, (players - 1) / 2));
        }

        /// <summary>Snaps every field to an allowed option, so a tampered request can't set odd values.</summary>
        public MatchRules Sanitized() => new MatchRules
        {
            Impostors = Snap(Impostors, ImpostorOptions), DiscussSeconds = Snap(DiscussSeconds, DiscussOptions), VoteSeconds = Snap(VoteSeconds, VoteOptions),
            KnockCooldown = Snap(KnockCooldown, CooldownOptions), RoundMinutes = Snap(RoundMinutes, RoundOptions)
        };

        static byte Snap(byte value, byte[] options)
        {
            byte best = options[0];
            foreach (byte option in options) if (Mathf.Abs(option - value) < Mathf.Abs(best - value)) best = option;
            return best;
        }

        public static string ImpostorLabel(byte value) => value == 0 ? "Auto" : value.ToString();
        public string Summary => "Impostors " + ImpostorLabel(Impostors) + " · talk " + DiscussSeconds + "s · vote " + VoteSeconds + "s · knock-down " + KnockCooldown + "s · " + RoundMinutes + " min";

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Impostors);
            serializer.SerializeValue(ref DiscussSeconds);
            serializer.SerializeValue(ref VoteSeconds);
            serializer.SerializeValue(ref KnockCooldown);
            serializer.SerializeValue(ref RoundMinutes);
        }

        public bool Equals(MatchRules other) => Impostors == other.Impostors && DiscussSeconds == other.DiscussSeconds && VoteSeconds == other.VoteSeconds &&
            KnockCooldown == other.KnockCooldown && RoundMinutes == other.RoundMinutes;
    }

    /// <summary>Player-facing names for each disruption, shared by offline and online play.</summary>
    public static class SabotageInfo
    {
        public static readonly string[] Names = { "Hawker Stall Gas Leak", "Main Power Blackout", "Water Supply Contamination", "Wi-Fi / PA Interference", "Trash Route Blockade" };
        public static readonly string[] Locations = { "Hawker Stalls", "Cellular Tower", "Central Lake", "Cellular Tower", "South Walk" };
        public static string NameOf(SabotageType type) => type == SabotageType.None ? "None" : Names[(int)type - 1];
    }

    /// <summary>Server-owned match state. Every gameplay-changing request is validated here.</summary>
    [DisallowMultipleComponent]
    public sealed partial class GameManager : NetworkBehaviour
    {
        public static GameManager Instance { get; private set; }
        public const int MaxPlayers = 8;

        const float IncapacitationDuration = 10f;
        const float SabotageCooldown = 38f;
        const float SabotageDuration = 55f;
        const float RevealSeconds = 5f, EjectSeconds = 4.5f;
        public readonly NetworkVariable<MatchRules> Rules = new NetworkVariable<MatchRules>(MatchRules.Default);
        // Accounts the host removed this session; they are turned away again if they rejoin with the room code.
        readonly HashSet<string> removedAccounts = new HashSet<string>();
        static readonly Vector3[] SpawnPositions =
        {
            new Vector3(0f, 0f, 0f), new Vector3(-1.4f, .1f, 0f), new Vector3(1.4f, .1f, 0f),
            new Vector3(0f, 1.4f, 0f), new Vector3(-1.5f, -1.25f, 0f), new Vector3(1.5f, -1.25f, 0f),
            new Vector3(-2.5f, .3f, 0f), new Vector3(2.5f, .3f, 0f)
        };
        const float InteractionRange = 2.1f;
        static readonly ulong SkipVote = ulong.MaxValue;

        public readonly NetworkVariable<RoundPhase> Phase = new NetworkVariable<RoundPhase>(RoundPhase.Menu);
        public readonly NetworkVariable<byte> ActiveSabotage = new NetworkVariable<byte>((byte)SabotageType.None);
        public readonly NetworkVariable<float> SabotageTimeRemaining = new NetworkVariable<float>(0f);
        public readonly NetworkVariable<bool> BlackoutActive = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> InterferenceActive = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<int> CompletedTaskCount = new NetworkVariable<int>(0);
        public readonly NetworkVariable<ulong> ReportedPlayerId = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<MeetingStage> Stage = new NetworkVariable<MeetingStage>(MeetingStage.None);
        public readonly NetworkVariable<double> StageEndsAt = new NetworkVariable<double>(0);
        public readonly NetworkVariable<float> StageLength = new NetworkVariable<float>(1f);
        public readonly NetworkVariable<ulong> MeetingCaller = new NetworkVariable<ulong>(ulong.MaxValue);
        /// <summary>Who has voted, never for whom: ballots stay on the host until the reveal.</summary>
        public readonly NetworkList<ulong> VotedClients = new NetworkList<ulong>();
        readonly Dictionary<ulong, ulong> serverVotes = new Dictionary<ulong, ulong>();
        ulong pendingEjection = ulong.MaxValue;
        // Client-side copy of the last reveal, for the meeting screen.
        public readonly Dictionary<ulong, List<ulong>> RevealedVotes = new Dictionary<ulong, List<ulong>>();
        public string MeetingOutcome { get; private set; } = "";
        public string MeetingEjectedRole { get; private set; } = "";
        public ulong MeetingEjectedId { get; private set; } = ulong.MaxValue;
        public float StageSecondsLeft => IsSpawned ? Mathf.Max(0f, (float)(StageEndsAt.Value - NetworkManager.ServerTime.Time)) : 0f;

        readonly Dictionary<ulong, PlayerRole> serverRoles = new Dictionary<ulong, PlayerRole>();
        readonly Dictionary<ulong, float> incapacitateCooldowns = new Dictionary<ulong, float>();
        readonly HashSet<ulong> emergencyUsed = new HashSet<ulong>();
        readonly HashSet<ulong> completedTaskIds = new HashSet<ulong>();
        float sabotageCooldownRemaining;
        float matchTimeRemaining;
        bool recruitUsed;
        const float RecruitUnlockSeconds = 45f;
        /// <summary>Client-side: the Impostor's name once the local player has been recruited as their crony.</summary>
        public static string LocalCronyPartner { get; private set; }
        int totalTasks;
        int matchStartClientCount;
        string currentChatLine = string.Empty;
        bool connectionApprovalInstalled;

        public event Action<string> ChatMessageReceived;
        public event Action<string> ModerationNoticeReceived;
        public string CurrentChatLine => currentChatLine;
        public bool IsRoundActive => Phase.Value == RoundPhase.Playing;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            // Despawning clears Instance, and Awake runs only once, so every new session (rejoin, new host) sets it again.
            Instance = this;
            if (IsServer)
            {
                InstallConnectionApproval();
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
                totalTasks = FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude).Length;
                ApplyPendingMigration();
            }
            Phase.OnValueChanged += OnPhaseChanged;
            BlackoutActive.OnValueChanged += OnBlackoutChanged;
            ActiveSabotage.OnValueChanged += OnSabotageSound;
            if (Phase.Value == RoundPhase.Meeting) GameAudioManager.PlayEmergencyKlaxon();
            GameAudioManager.SetMusic(MusicFor(Phase.Value));
        }

        public override void OnNetworkDespawn()
        {
            ActiveSabotage.OnValueChanged -= OnSabotageSound;
            Phase.OnValueChanged -= OnPhaseChanged;
            BlackoutActive.OnValueChanged -= OnBlackoutChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            if (Instance == this) Instance = null;
        }

        void OnPhaseChanged(RoundPhase previous, RoundPhase current)
        {
            if (IsServer && (current == RoundPhase.Menu) != (previous == RoundPhase.Menu)) OnlineRooms.SetMatchInProgress(current != RoundPhase.Menu);
            GameAudioManager.SetMusic(MusicFor(current));
            // Online rounds are a touch dimmer so each player's lantern defines what they can see.
            GlowLight.SetAmbient(current == RoundPhase.Playing ? (BlackoutActive.Value ? .12f : .72f) : 1f);
            if (current == RoundPhase.Meeting)
            {
                if (ReportedPlayerId.Value != ulong.MaxValue) GameAudioManager.PlayReport();
                else GameAudioManager.PlayEmergencyKlaxon();
            }
            else if (current == RoundPhase.Playing && previous == RoundPhase.Menu)
            {
                PlayerController local = NetworkManager.LocalClient != null && NetworkManager.LocalClient.PlayerObject != null ? NetworkManager.LocalClient.PlayerObject.GetComponent<PlayerController>() : null;
                GameAudioManager.PlayRoleReveal(local != null && local.PrivateRole.Value == PlayerRole.Impostor);
            }
        }

        static MusicMood MusicFor(RoundPhase phase) => phase == RoundPhase.Meeting ? MusicMood.Meeting : phase == RoundPhase.Playing ? MusicMood.Park : MusicMood.Menu;

        void OnSabotageSound(byte previous, byte current)
        {
            if (Phase.Value != RoundPhase.Playing) return;
            if (current != (byte)SabotageType.None) { GameAudioManager.PlaySabotage(); GameAudioManager.SetMusic(MusicMood.Meeting); }
            else if (previous != (byte)SabotageType.None) { GameAudioManager.PlayRepaired(); GameAudioManager.SetMusic(MusicMood.Park); }
        }

        void OnBlackoutChanged(bool previous, bool current)
        {
            foreach (VisionController vision in FindObjectsByType<VisionController>(FindObjectsInactive.Exclude))
                vision.RefreshVision();
            foreach (Light2D light in FindObjectsByType<Light2D>(FindObjectsInactive.Exclude))
            {
                if (light.lightType == Light2D.LightType.Global)
                    light.intensity = current ? 0.12f : 0.72f;
            }
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;
            UpdateSeats();
            float delta = Time.deltaTime;
            if (sabotageCooldownRemaining > 0f) sabotageCooldownRemaining = Mathf.Max(0f, sabotageCooldownRemaining - delta);

            if (Phase.Value == RoundPhase.Playing)
            {
                matchTimeRemaining -= delta;
                if (ActiveSabotage.Value != (byte)SabotageType.None)
                {
                    SabotageTimeRemaining.Value = Mathf.Max(0f, SabotageTimeRemaining.Value - delta);
                    if (SabotageTimeRemaining.Value <= 0f)
                    {
                        FinishRound(PlayerRole.Impostor, "The disruption was not repaired in time.");
                        return;
                    }
                }
                if (CompletedTaskCount.Value >= totalTasks && totalTasks > 0)
                {
                    FinishRound(PlayerRole.Crewmate, "The community finished every park task.");
                    return;
                }
                if (matchTimeRemaining <= 0f)
                {
                    FinishRound(PlayerRole.Crewmate, "The park stayed safe until closing time.");
                    return;
                }
                CheckParityVictory();
            }
            else if (Phase.Value == RoundPhase.Meeting)
            {
                if (Stage.Value == MeetingStage.Voting && AllLivingPlayersVoted()) StageEndsAt.Value = NetworkManager.ServerTime.Time;
                if (NetworkManager.ServerTime.Time >= StageEndsAt.Value) AdvanceMeetingServer();
            }
        }

        void OnClientDisconnected(ulong clientId)
        {
            HoldSeat(clientId);
            playerKeys.Remove(clientId);
            chatFilter.Forget(clientId); communicationAccounts.Remove(clientId); voiceAccounts.Remove(clientId);
            serverRoles.Remove(clientId);
            incapacitateCooldowns.Remove(clientId);
            emergencyUsed.Remove(clientId);
            serverVotes.Remove(clientId);
            if (IsServer) VotedClients.Remove(clientId);
            if (Phase.Value == RoundPhase.Playing) CheckParityVictory();
        }

        public void InstallConnectionApproval()
        {
            NetworkManager manager = NetworkManager.Singleton;
            // Every join carries this build's version (so the host can turn away a mismatched client before its
            // messages desync the match) and the player's stable key (so a dropped player gets their seat back).
            if (manager != null && !manager.IsListening) manager.NetworkConfig.ConnectionData = ConnectionPayload();
            if (!connectionApprovalInstalled && manager != null && !manager.IsListening && manager.NetworkConfig.ConnectionApproval)
            {
                manager.ConnectionApprovalCallback = ApproveConnection;
                connectionApprovalInstalled = true;
            }
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            NetworkManager manager = NetworkManager != null ? NetworkManager : NetworkManager.Singleton;
            bool isHostSelf = manager != null && request.ClientNetworkId == NetworkManager.ServerClientId && manager.ConnectedClientsIds.Count == 0;
            bool inLobby = !IsSpawned || Phase.Value == RoundPhase.Menu;
            int currentPlayers = manager != null ? manager.ConnectedClientsIds.Count : 0;
            bool hasRoom = isHostSelf || currentPlayers < MaxPlayers;
            ReadPayload(request.Payload, out string clientVersion, out string key);
            bool sameVersion = isHostSelf || clientVersion == Application.version;
            // The same player connecting again (their game crashed or their Wi-Fi dropped) replaces their old, stale
            // connection, which the host may not have timed out yet; that also holds their seat for them.
            if (!isHostSelf && sameVersion && KeyConnected(key)) ReplaceStaleConnection(key);
            bool duplicate = !isHostSelf && KeyConnected(key);
            // Mid-match, only players returning to a held seat (after a drop or a host change) may come in.
            bool returning = HasHeldSeat(key);
            bool approved = isHostSelf || (hasRoom && sameVersion && !duplicate && (inLobby || returning));
            if (approved && !string.IsNullOrEmpty(key)) playerKeys[request.ClientNetworkId] = key;
            response.Approved = approved;
            response.CreatePlayerObject = approved;
            response.Pending = false;
            response.Reason = !sameVersion
                ? "This room runs Crony " + Application.version + " but you have " + clientVersion + ". Everyone needs the same version from itch.io."
                : duplicate
                ? "You are already in this room."
                : !hasRoom
                ? "Township Park is full (8/8 players)."
                : !inLobby
                    ? "Match is already in progress. Wait for the lobby."
                    : string.Empty;
        }

        /// <summary>Called by the host UI after at least two network clients have joined (or 1 client in solo prototype mode).</summary>
        public bool StartMatchFromHost(bool allowSoloPrototype = false)
        {
            int connected = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
            int minPlayers = allowSoloPrototype ? 1 : 2;
            if (!IsServer || !IsSpawned || Phase.Value != RoundPhase.Menu || connected < minPlayers)
                return false;
            StartMatchServer();
            return true;
        }

        /// <summary>Host-only, lobby-only: changes the rules for the next match.</summary>
        public bool SetRulesFromHost(MatchRules rules)
        {
            if (!IsServer || !IsSpawned || Phase.Value != RoundPhase.Menu) return false;
            Rules.Value = rules.Sanitized();
            return true;
        }

        /// <summary>Host-only: removes a player from the room and keeps them out for the rest of this session.</summary>
        public bool RemovePlayerFromHost(ulong clientId)
        {
            if (!IsServer || clientId == NetworkManager.ServerClientId || !NetworkManager.ConnectedClients.ContainsKey(clientId)) return false;
            if (communicationAccounts.TryGetValue(clientId, out string account)) removedAccounts.Add(account);
            if (playerKeys.TryGetValue(clientId, out string key)) OnlineRooms.RemovePlayer(key);
            NetworkManager.DisconnectClient(clientId, "The host removed you from this room.");
            return true;
        }

        public bool ReturnToLobbyFromHost()
        {
            if (!IsServer || !IsSpawned || Phase.Value != RoundPhase.Results)
                return false;
            ActiveSabotage.Value = (byte)SabotageType.None;
            SabotageTimeRemaining.Value = 0f;
            BlackoutActive.Value = false;
            InterferenceActive.Value = false;
            ReportedPlayerId.Value = ulong.MaxValue;
            ClearVotesServer();
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                PlayerController player = GetPlayer(clientId);
                if (player != null) player.AssignRoleServer(PlayerRole.Crewmate);
            }
            serverRoles.Clear();
            heldSeats.Clear();
            Phase.Value = RoundPhase.Menu;
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartMatchRpc(RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
            StartMatchFromHost();
        }

        void StartMatchServer()
        {
            List<ulong> clients = new List<ulong>(NetworkManager.ConnectedClientsIds);
            matchStartClientCount = clients.Count;
            for (int i = clients.Count - 1; i > 0; i--)
            {
                int swap = UnityEngine.Random.Range(0, i + 1);
                (clients[i], clients[swap]) = (clients[swap], clients[i]);
            }

            serverRoles.Clear();
            heldSeats.Clear();
            int impostorCount = Rules.Value.ImpostorsFor(clients.Count);
            Vector3[] spawnPositions = SpawnPositions;
            for (int i = 0; i < clients.Count; i++)
            {
                ulong clientId = clients[i];
                PlayerRole role = i < impostorCount ? PlayerRole.Impostor : PlayerRole.Crewmate;
                serverRoles[clientId] = role;
                PlayerController player = GetPlayer(clientId);
                if (player != null)
                {
                    player.AssignRoleServer(role);
                    player.transform.position = spawnPositions[i % spawnPositions.Length];
                    Rigidbody2D body = player.GetComponent<Rigidbody2D>();
                    if (body != null) body.position = spawnPositions[i % spawnPositions.Length];
                }
            }

            emergencyUsed.Clear();
            incapacitateCooldowns.Clear();
            completedTaskIds.Clear();
            ClearVotesServer();
            CompletedTaskCount.Value = 0;
            totalTasks = Mathf.Max(1, FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude).Length);
            foreach (TownshipTaskStation station in FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude)) station.ResetForNewMatchServer();
            ActiveSabotage.Value = (byte)SabotageType.None;
            SabotageTimeRemaining.Value = 0f;
            BlackoutActive.Value = false;
            InterferenceActive.Value = false;
            sabotageCooldownRemaining = 0f;
            matchTimeRemaining = Rules.Value.RoundMinutes * 60f;
            recruitUsed = false;
            stumpReadyAt.Clear();
            ReportedPlayerId.Value = ulong.MaxValue;
            Phase.Value = RoundPhase.Playing;
            SafetySystem.Ensure(gameObject).ResetMatch();
            ClearMatchRestrictionsClientRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestIncapacitateRpc(ulong targetClientId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsServer || Phase.Value != RoundPhase.Playing || !IsImpostor(sender) || sender == targetClientId) return;
            PlayerController attacker = GetPlayer(sender);
            PlayerController target = GetPlayer(targetClientId);
            if (attacker == null || target == null || target.Status.Value != PlayerLifeState.Alive || IsImpostorTeam(targetClientId)) return;
            if (!IsWithinRange(attacker.transform.position, target.transform.position, InteractionRange)) return;
            if (incapacitateCooldowns.TryGetValue(sender, out float readyAt) && Time.time < readyAt) return;
            target.ServerIncapacitate(IncapacitationDuration);
            incapacitateCooldowns[sender] = Time.time + Rules.Value.KnockCooldown;
            GameAudioManager.PlayTaskInteraction();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestReportRpc(ulong targetClientId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsServer || Phase.Value != RoundPhase.Playing) return;
            PlayerController reporter = GetPlayer(sender);
            PlayerController target = GetPlayer(targetClientId);
            if (reporter == null || target == null || reporter.Status.Value != PlayerLifeState.Alive || target.Status.Value != PlayerLifeState.Incapacitated) return;
            if (!IsWithinRange(reporter.transform.position, target.transform.position, InteractionRange)) return;
            target.ServerRecover();
            ReportedPlayerId.Value = targetClientId;
            BeginMeetingServer(sender, targetClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestEmergencyMeetingRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            PlayerController caller = GetPlayer(sender);
            if (!IsServer || Phase.Value != RoundPhase.Playing || caller == null || caller.Status.Value != PlayerLifeState.Alive || emergencyUsed.Contains(sender)) return;
            emergencyUsed.Add(sender);
            ReportedPlayerId.Value = ulong.MaxValue;
            BeginMeetingServer(sender, ulong.MaxValue);
        }

        void BeginMeetingServer(ulong callerId, ulong reportedId)
        {
            Phase.Value = RoundPhase.Meeting;
            ReportedPlayerId.Value = reportedId;
            MeetingCaller.Value = callerId;
            ClearVotesServer();
            pendingEjection = ulong.MaxValue;
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
                if (player.Status.Value == PlayerLifeState.Incapacitated) player.ServerRecover();
            SetStageServer(MeetingStage.Discussion, Rules.Value.DiscussSeconds);
            MeetingStartedClientRpc();
        }

        void SetStageServer(MeetingStage stage, float seconds)
        {
            Stage.Value = stage;
            StageLength.Value = seconds;
            StageEndsAt.Value = NetworkManager.ServerTime.Time + seconds;
        }

        void ClearVotesServer()
        {
            serverVotes.Clear();
            if (IsServer) VotedClients.Clear();
        }

        [ClientRpc]
        void MeetingStartedClientRpc()
        {
            RevealedVotes.Clear();
            MeetingOutcome = MeetingEjectedRole = "";
            MeetingEjectedId = ulong.MaxValue;
            NetworkManagerUI.SetNotice(MeetingHeadline());
        }

        public string NameOf(ulong clientId)
        {
            PlayerController player = GetPlayer(clientId);
            return player != null ? player.DisplayName.Value.ToString() : "Someone";
        }

        public string MeetingHeadline() => ReportedPlayerId.Value == ulong.MaxValue
            ? NameOf(MeetingCaller.Value) + " called an emergency meeting."
            : NameOf(MeetingCaller.Value) + " found " + NameOf(ReportedPlayerId.Value) + " down and called everyone together.";

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void CastVoteRpc(ulong targetClientId, bool skip, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsServer || Phase.Value != RoundPhase.Meeting || Stage.Value != MeetingStage.Voting || serverVotes.ContainsKey(sender)) return;
            PlayerController voter = GetPlayer(sender);
            if (voter == null || voter.Status.Value != PlayerLifeState.Alive) return;
            if (!skip)
            {
                PlayerController target = GetPlayer(targetClientId);
                if (target == null || target.Status.Value != PlayerLifeState.Alive || sender == targetClientId) return;
            }
            // Votes are final once cast.
            serverVotes[sender] = skip ? SkipVote : targetClientId;
            VotedClients.Add(sender);
        }

        bool AllLivingPlayersVoted()
        {
            int eligible = 0;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                PlayerController player = GetPlayer(clientId);
                if (player != null && player.Status.Value == PlayerLifeState.Alive) eligible++;
            }
            return eligible > 0 && serverVotes.Count >= eligible;
        }

        void AdvanceMeetingServer()
        {
            switch (Stage.Value)
            {
                case MeetingStage.Discussion: SetStageServer(MeetingStage.Voting, Rules.Value.VoteSeconds); break;
                case MeetingStage.Voting: RevealVotesServer(); SetStageServer(MeetingStage.Reveal, RevealSeconds); break;
                case MeetingStage.Reveal: EjectServer(); SetStageServer(MeetingStage.Ejection, EjectSeconds); break;
                default: EndMeetingServer(); break;
            }
        }

        void RevealVotesServer()
        {
            Dictionary<ulong, int> tally = new Dictionary<ulong, int>();
            foreach (ulong target in serverVotes.Values) tally[target] = (tally.TryGetValue(target, out int n) ? n : 0) + 1;
            int skips = tally.TryGetValue(SkipVote, out int skipCount) ? skipCount : 0;
            ulong top = ulong.MaxValue; int topVotes = 0; bool tie = false;
            foreach (KeyValuePair<ulong, int> item in tally)
            {
                if (item.Key == SkipVote) continue;
                if (item.Value > topVotes) { top = item.Key; topVotes = item.Value; tie = false; }
                else if (item.Value == topVotes) tie = true;
            }
            // Most votes wins; a tie, or as many skips as the leader, sends no one home.
            pendingEjection = !tie && topVotes > skips ? top : ulong.MaxValue;
            string outcome, role;
            if (pendingEjection == ulong.MaxValue || !serverRoles.TryGetValue(pendingEjection, out PlayerRole ejectedRole))
            {
                pendingEjection = ulong.MaxValue;
                outcome = "No one was sent home.";
                role = topVotes == 0 && skips == 0 ? "Nobody voted." : tie ? "It was a tie." : "Most people skipped.";
            }
            else
            {
                string name = NameOf(pendingEjection);
                outcome = name + " was sent home.";
                role = ejectedRole == PlayerRole.Impostor ? name + " was the Impostor!" : ejectedRole == PlayerRole.Crony ? name + " was the Impostor's crony!" : name + " was not the Impostor.";
            }
            ulong[] voters = new ulong[serverVotes.Count], targets = new ulong[serverVotes.Count];
            int i = 0;
            foreach (KeyValuePair<ulong, ulong> vote in serverVotes) { voters[i] = vote.Key; targets[i] = vote.Value; i++; }
            MeetingRevealClientRpc(voters, targets, pendingEjection, outcome, role);
        }

        [ClientRpc]
        void MeetingRevealClientRpc(ulong[] voters, ulong[] targets, ulong ejectedId, string outcome, string role)
        {
            RevealedVotes.Clear();
            for (int i = 0; i < voters.Length; i++)
            {
                if (!RevealedVotes.TryGetValue(targets[i], out List<ulong> list)) RevealedVotes[targets[i]] = list = new List<ulong>();
                list.Add(voters[i]);
            }
            MeetingEjectedId = ejectedId;
            MeetingOutcome = outcome;
            MeetingEjectedRole = role;
        }

        void EjectServer()
        {
            PlayerController ejected = pendingEjection != ulong.MaxValue ? GetPlayer(pendingEjection) : null;
            if (ejected != null) ejected.ServerEject();
        }

        void EndMeetingServer()
        {
            Stage.Value = MeetingStage.None;
            Phase.Value = RoundPhase.Playing;
            ClearVotesServer();
            // Everyone regroups at the start area, and takedowns cool down so nobody is jumped the moment play resumes.
            int index = 0;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                PlayerController player = GetPlayer(clientId);
                if (player == null || player.Status.Value != PlayerLifeState.Alive) continue;
                Vector3 spot = SpawnPositions[index++ % SpawnPositions.Length];
                player.transform.position = spot;
                Rigidbody2D body = player.GetComponent<Rigidbody2D>();
                if (body != null) body.position = spot;
                if (IsImpostor(clientId)) incapacitateCooldowns[clientId] = Mathf.Max(incapacitateCooldowns.TryGetValue(clientId, out float t) ? t : 0f, Time.time + 15f);
            }
            MeetingResolvedClientRpc();
            CheckParityVictory();
        }

        [ClientRpc]
        void MeetingResolvedClientRpc() => NetworkManagerUI.SetNotice(MeetingOutcome + " " + MeetingEjectedRole);

        readonly Dictionary<ulong, float> stumpReadyAt = new Dictionary<ulong, float>();

        /// <summary>Impostor travel through a hollow stump. The host checks role, life, range and cooldown.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStumpTravelRpc(int stump, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsServer || Phase.Value != RoundPhase.Playing || !IsImpostor(sender) || stump < 0 || stump >= HollowStumps.Count) return;
            PlayerController player = GetPlayer(sender);
            if (player == null || player.Status.Value != PlayerLifeState.Alive) return;
            if (((Vector2)player.transform.position - HollowStumps.Spot(stump)).sqrMagnitude > (HollowStumps.UseRange + .4f) * (HollowStumps.UseRange + .4f)) return;
            if (stumpReadyAt.TryGetValue(sender, out float ready) && Time.time < ready) { PrivateNoticeClientRpc("The stump tunnel needs " + Mathf.CeilToInt(ready - Time.time) + "s to settle.", TargetChat(sender)); return; }
            Vector3 exit = HollowStumps.ExitFrom(stump);
            player.transform.position = exit;
            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            if (body != null) body.position = exit;
            stumpReadyAt[sender] = Time.time + HollowStumps.Cooldown;
            StumpTravelledClientRpc(TargetChat(sender));
        }

        [ClientRpc]
        void StumpTravelledClientRpc(ClientRpcParams targets = default) => GameAudioManager.PlayStumpTravel();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRecruitRpc(ulong targetClientId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsServer || Phase.Value != RoundPhase.Playing || !IsImpostor(sender)) return;
            PlayerController impostor = GetPlayer(sender);
            PlayerController target = GetPlayer(targetClientId);
            string refusal = recruitUsed ? "You already have a crony this match."
                : 480f - matchTimeRemaining < RecruitUnlockSeconds ? "Recruiting unlocks " + Mathf.CeilToInt(RecruitUnlockSeconds - (480f - matchTimeRemaining)) + "s into the match."
                : LivingCrewCount() < 3 ? "Too few crewmates left to recruit safely."
                : impostor == null || target == null || target.Status.Value != PlayerLifeState.Alive || !serverRoles.TryGetValue(targetClientId, out PlayerRole role) || role != PlayerRole.Crewmate
                    || !IsWithinRange(impostor.transform.position, target.transform.position, InteractionRange) ? "Stand next to a Crewmate to whisper the offer."
                : null;
            if (refusal != null) { PrivateNoticeClientRpc(refusal, TargetChat(sender)); return; }
            recruitUsed = true;
            serverRoles[targetClientId] = PlayerRole.Crony;
            target.SetPrivateRoleServer(PlayerRole.Crony);
            CronyRecruitedClientRpc(impostor.DisplayName.Value.ToString(), TargetChat(targetClientId));
            PrivateNoticeClientRpc(target.DisplayName.Value.ToString() + " is now your crony. They will cover for you.", TargetChat(sender));
        }

        int LivingCrewCount()
        {
            int count = 0;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                if (IsAlive(clientId) && serverRoles.TryGetValue(clientId, out PlayerRole role) && role == PlayerRole.Crewmate) count++;
            return count;
        }

        [ClientRpc]
        void CronyRecruitedClientRpc(string impostorName, ClientRpcParams targets = default)
        {
            LocalCronyPartner = impostorName;
            GameAudioManager.PlayRecruit();
            NetworkManagerUI.SetNotice(impostorName + " recruited you as their CRONY. Help them win: your tasks no longer count, but you can sabotage (N + Q).");
        }

        [ClientRpc]
        void PrivateNoticeClientRpc(string message, ClientRpcParams targets = default) => NetworkManagerUI.SetNotice(message);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSabotageRpc(byte sabotageValue, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            SabotageType sabotage = (SabotageType)sabotageValue;
            if (!IsServer || Phase.Value != RoundPhase.Playing || !IsImpostorTeam(sender) || sabotage == SabotageType.None || ActiveSabotage.Value != (byte)SabotageType.None || sabotageCooldownRemaining > 0f) return;
            ActiveSabotage.Value = sabotageValue;
            SabotageTimeRemaining.Value = SabotageDuration;
            sabotageCooldownRemaining = SabotageCooldown;
            BlackoutActive.Value = sabotage == SabotageType.PowerBlackout;
            InterferenceActive.Value = sabotage == SabotageType.Interference;
            SabotageActivatedClientRpc(sabotageValue);
        }

        [ClientRpc]
        void SabotageActivatedClientRpc(byte sabotageValue)
        {
            GameAudioManager.PlayTaskInteraction();
            NetworkManagerUI.SetNotice("Disruption active: " + SabotageInfo.NameOf((SabotageType)sabotageValue) + ". Find the repair station.");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRepairSabotageRpc(byte taskKindValue, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            TryRepairSabotageServer(sender, taskKindValue);
        }

        public bool TryRepairSabotageServer(ulong sender, byte taskKindValue)
        {
            PlayerController player = GetPlayer(sender);
            SabotageType active = (SabotageType)ActiveSabotage.Value;
            if (!IsServer || Phase.Value != RoundPhase.Playing || active == SabotageType.None || IsImpostor(sender) || player == null || player.Status.Value != PlayerLifeState.Alive) return false;
            TownshipTaskKind required = RepairTaskFor(active);
            if ((TownshipTaskKind)taskKindValue != required) return false;
            TownshipTaskStation station = FindTask(required);
            if (station == null || !IsWithinRange(player.transform.position, station.transform.position, InteractionRange + .4f)) return false;
            CompleteSabotageRepair();
            return true;
        }

        public static TownshipTaskKind RepairTaskFor(SabotageType sabotage)
        {
            switch (sabotage)
            {
                case SabotageType.GasLeak: return TownshipTaskKind.NasiLemak;
                case SabotageType.PowerBlackout: return TownshipTaskKind.Router;
                case SabotageType.WaterContamination: return TownshipTaskKind.LakeCleanup;
                case SabotageType.Interference: return TownshipTaskKind.Router;
                case SabotageType.RouteBlockade: return TownshipTaskKind.SolarBatteries;
                default: return TownshipTaskKind.NasiLemak;
            }
        }

        void CompleteSabotageRepair()
        {
            SabotageType repaired = (SabotageType)ActiveSabotage.Value;
            ActiveSabotage.Value = (byte)SabotageType.None;
            SabotageTimeRemaining.Value = 0f;
            BlackoutActive.Value = false;
            InterferenceActive.Value = false;
            SabotageRepairedClientRpc((byte)repaired);
        }

        [ClientRpc]
        void SabotageRepairedClientRpc(byte sabotageValue)
        {
            NetworkManagerUI.SetNotice("Repaired: " + SabotageInfo.NameOf((SabotageType)sabotageValue) + ".");
            GameAudioManager.PlayTaskInteraction();
        }

        readonly ChatSafetyFilter chatFilter = new ChatSafetyFilter();
        readonly Dictionary<ulong, string> communicationAccounts = new Dictionary<ulong, string>();
        readonly Dictionary<ulong, string> voiceAccounts = new Dictionary<ulong, string>();
        readonly string communicationSession = Guid.NewGuid().ToString("N");
        uint messageSequence;

        public void SetVerifiedCommunicationAccount(ulong clientId, string accountId)
        {
            if (!IsServer || string.IsNullOrEmpty(accountId) || accountId.Length > 64) return;
            communicationAccounts[clientId] = accountId;
            if (clientId != NetworkManager.ServerClientId && removedAccounts.Contains(accountId))
            {
                NetworkManager.DisconnectClient(clientId, "The host removed you from this room.");
                return;
            }
            // Level 4 suspensions only bind to verified accounts; session fallback IDs cannot be re-identified.
            if (clientId != NetworkManager.ServerClientId && SafetySystem.Ensure(gameObject).IsSuspended(accountId, out DateTime until))
                NetworkManager.DisconnectClient(clientId, "Your account is suspended from online play until " + until.ToLocalTime().ToString("d MMM HH:mm") + ". You can appeal in Settings > Safety.");
        }

        public string CommunicationAccount(ulong clientId)
        {
            if (communicationAccounts.TryGetValue(clientId, out var id)) return id;
            return communicationSession + ":" + clientId;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SendChatRpc(string rawText, RpcParams rpcParams = default)
        {
            if (!IsServer || InterferenceActive.Value) return;
            ulong sender = rpcParams.Receive.SenderClientId;
            var player = GetPlayer(sender);
            if (player == null || rawText == null || rawText.Length > ChatSafetyFilter.MaxLength) return;
            string account = CommunicationAccount(sender);
            SafetySystem safety = SafetySystem.Ensure(gameObject);
            if (safety.IsChatRestricted(account, out _))
            {
                ChatDecisionClientRpc((byte)ChatDecision.Restricted, TargetChat(sender));
                return;
            }
            var decision = chatFilter.Process(sender, rawText, Time.realtimeSinceStartupAsDouble, out var clean, out var category);
            if (decision == ChatDecision.Violation)
            {
                EnforceServer(sender, account, safety.Enforce(account, category));
                return;
            }
            if (decision != ChatDecision.Allowed && decision != ChatDecision.Masked)
            {
                ChatDecisionClientRpc((byte)decision, TargetChat(sender));
                return;
            }
            bool ghost = player.Status.Value != PlayerLifeState.Alive;
            ChatChannel channel = ghost ? ChatChannel.Ghost : Phase.Value == RoundPhase.Meeting ? ChatChannel.Meeting :
                Phase.Value == RoundPhase.Playing ? ChatChannel.Township : ChatChannel.Lobby;
            string id = communicationSession + ":" + (++messageSequence);
            foreach (ulong recipient in NetworkManager.ConnectedClientsIds)
            {
                var target = GetPlayer(recipient);
                if (target == null || (ghost && target.Status.Value == PlayerLifeState.Alive)) continue;
                ReceiveChatClientRpc(sender, CommunicationAccount(sender), player.DisplayName.Value.ToString(), clean, (byte)channel, id, TargetChat(recipient));
            }
            if (decision == ChatDecision.Masked) ChatDecisionClientRpc((byte)decision, TargetChat(sender));
        }

        void EnforceServer(ulong sender, string account, EnforcementResult result)
        {
            ModerationNoticeClientRpc(result.Consequence, result.Reference, (byte)result.Level, result.VoiceRestricted, (byte)result.Category, TargetChat(sender));
            if (result.VoiceRestricted) VoiceRestrictionClientRpc(account);
            // The host cannot remove itself; its chat stays locked for the match instead.
            if (result.Level >= EnforcementLevel.Remove && sender != NetworkManager.ServerClientId)
                NetworkManager.DisconnectClient(sender, result.Message);
        }

        [ClientRpc]
        void ModerationNoticeClientRpc(string consequence, string reference, byte level, bool voiceRestricted, byte category, ClientRpcParams targets = default)
        {
            if (voiceRestricted) CommunicationSafety.SetLocalVoiceRestricted(true);
            TextChatManager.Instance?.ShowModeration(consequence, reference, (EnforcementLevel)level, (SafetyCategory)category);
        }

        [ClientRpc]
        void VoiceRestrictionClientRpc(string account) => CommunicationSafety.SetVoiceRestricted(account, true);

        [ClientRpc]
        void ClearMatchRestrictionsClientRpc() => CommunicationSafety.ClearMatchRestrictions();

        static ClientRpcParams TargetChat(ulong id) => new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { id } } };

        [ClientRpc]
        void ReceiveChatClientRpc(ulong sender, string account, string name, string message, byte channel, string id, ClientRpcParams targets = default)
        {
            TextChatManager.Instance?.Receive(sender, account, name, message, (ChatChannel)channel, id);
            if (!CommunicationSafety.IsBlocked(account))
            {
                currentChatLine = name + ": " + message;
                ChatMessageReceived?.Invoke(currentChatLine);
            }
        }

        [ClientRpc]
        void ChatDecisionClientRpc(byte decision, ClientRpcParams targets = default) => TextChatManager.Instance?.ShowDecision((ChatDecision)decision);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RegisterVoiceIdentityRpc(string id, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsServer || GetPlayer(sender) == null || string.IsNullOrEmpty(id) || id.Length > 64) return;
            foreach (var pair in voiceAccounts)
                if (pair.Key != sender && pair.Value == id) return;
            voiceAccounts[sender] = id;
            foreach (var pair in voiceAccounts)
                VoiceIdentityClientRpc(pair.Key, pair.Value, CommunicationAccount(pair.Key), communicationSession);
        }

        [ClientRpc]
        void VoiceIdentityClientRpc(ulong client, string voiceId, string account, string session)
            => TownshipVoiceManager.Instance?.Register(client, voiceId, account, session);

        [ClientRpc]
        void ChatPublishedClientRpc(string line)
        {
            currentChatLine = line;
            ChatMessageReceived?.Invoke(line);
        }

        [ClientRpc]
        void ModerationNoticeClientRpc(string note, ClientRpcParams clientRpcParams = default)
        {
            ModerationNoticeReceived?.Invoke(note);
            NetworkManagerUI.SetNotice(note);
        }

        public void NotifyTaskCompleted(TownshipTaskStation station)
        {
            if (!IsServer || station == null || !station.Completed.Value) return;
            RegisterCompletedTask(station);
        }

        /// <summary>Reports a locally solved mini-game; the server accepts it only after its station is complete.</summary>
        public void ReportTaskCompletion(TownshipTaskStation station)
        {
            if (station == null || !station.IsSpawned || !IsSpawned) return;
            if (IsServer)
            {
                if (station.Completed.Value) RegisterCompletedTask(station);
                return;
            }
            ReportTaskCompletionRpc(station.NetworkObjectId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ReportTaskCompletionRpc(ulong stationNetworkObjectId, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsServer || Phase.Value != RoundPhase.Playing || IsImpostorTeam(sender)) return;
            PlayerController player = GetPlayer(sender);
            if (player == null || player.Status.Value != PlayerLifeState.Alive) return;

            TownshipTaskStation station = null;
            foreach (TownshipTaskStation candidate in FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude))
            {
                if (candidate.IsSpawned && candidate.NetworkObjectId == stationNetworkObjectId)
                {
                    station = candidate;
                    break;
                }
            }

            if (station == null || !station.Completed.Value || !IsWithinRange(player.transform.position, station.transform.position, InteractionRange)) return;
            RegisterCompletedTask(station);
        }

        void RegisterCompletedTask(TownshipTaskStation station)
        {
            if (station == null || !station.IsSpawned || !station.Completed.Value || !completedTaskIds.Add(station.NetworkObjectId)) return;
            CompletedTaskCount.Value = completedTaskIds.Count;
        }

        public bool IsImpostor(ulong clientId) => serverRoles.TryGetValue(clientId, out PlayerRole role) && role == PlayerRole.Impostor;
        /// <summary>The Impostor plus their recruited crony: they share a win condition and never help the crew.</summary>
        public bool IsImpostorTeam(ulong clientId) => serverRoles.TryGetValue(clientId, out PlayerRole role) && role != PlayerRole.Crewmate;

        public bool IsAlive(ulong clientId)
        {
            PlayerController player = GetPlayer(clientId);
            return player != null && player.Status.Value == PlayerLifeState.Alive;
        }

        public PlayerController GetPlayer(ulong clientId)
        {
            if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null) return null;
            return client.PlayerObject.GetComponent<PlayerController>();
        }

        static TownshipTaskStation FindTask(TownshipTaskKind kind)
        {
            foreach (TownshipTaskStation station in FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude))
                if (station.Kind == kind) return station;
            return null;
        }

        static bool IsWithinRange(Vector3 a, Vector3 b, float range) => (a - b).sqrMagnitude <= range * range;

        void CheckParityVictory()
        {
            if (matchStartClientCount <= 1) return;
            int impostors = 0;
            int team = 0;
            int crew = 0;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                PlayerController player = GetPlayer(clientId);
                if (player == null || player.Status.Value != PlayerLifeState.Alive || !HasLiveSeat(clientId)) continue;
                if (IsImpostor(clientId)) impostors++;
                if (IsImpostorTeam(clientId)) team++;
                else crew++;
            }
            CountHeldSeats(ref impostors, ref team, ref crew);
            if (impostors == 0) FinishRound(PlayerRole.Crewmate, "The Impostor left the park.");
            else if (matchStartClientCount == 2 ? crew == 0 : crew <= team)
                FinishRound(PlayerRole.Impostor, "The Impostor outnumbered the remaining crew.");
        }

        void FinishRound(PlayerRole winner, string reason)
        {
            if (!IsServer || Phase.Value == RoundPhase.Results) return;
            Debug.Log("[Crony] Round over at " + Time.time.ToString("0") + "s: " + winner + " team wins. " + reason);
            heldSeats.Clear();
            Phase.Value = RoundPhase.Results;
            ActiveSabotage.Value = (byte)SabotageType.None;
            SabotageTimeRemaining.Value = 0f;
            BlackoutActive.Value = false;
            InterferenceActive.Value = false;
            RoundFinishedClientRpc((byte)winner, reason);
        }

        [ClientRpc]
        void RoundFinishedClientRpc(byte winner, string reason)
        {
            PlayerController local = NetworkManager.LocalClient != null && NetworkManager.LocalClient.PlayerObject != null ? NetworkManager.LocalClient.PlayerObject.GetComponent<PlayerController>() : null;
            bool crewWon = (PlayerRole)winner == PlayerRole.Crewmate;
            string verdict = local == null ? string.Empty : (crewWon == (local.PrivateRole.Value == PlayerRole.Crewmate)) ? "  You win!" : "  You lose.";
            NetworkManagerUI.SetNotice((crewWon ? "Crewmates" : "Impostor team") + " win: " + reason + verdict);
            if (local != null) GameAudioManager.PlayMatchEnd(crewWon == (local.PrivateRole.Value == PlayerRole.Crewmate));
            LocalCronyPartner = null;
        }
    }
}
