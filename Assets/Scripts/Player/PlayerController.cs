using Unity.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Crony
{
    /// <summary>Owner reads input; the server validates it and is the only writer of movement and life state.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody2D))]
    public sealed class PlayerController : NetworkBehaviour
    {
        [SerializeField] float moveSpeed = 4.4f;
        [SerializeField] Rigidbody2D body;
        [SerializeField] float interactRange = 2.1f;
        [SerializeField] SpriteRenderer[] visualRenderers;

        public readonly NetworkVariable<PlayerRole> PrivateRole = new NetworkVariable<PlayerRole>(PlayerRole.Crewmate, NetworkVariableReadPermission.Owner);
        public readonly NetworkVariable<PlayerLifeState> Status = new NetworkVariable<PlayerLifeState>(PlayerLifeState.Alive);
        public readonly NetworkVariable<FixedString64Bytes> DisplayName = new NetworkVariable<FixedString64Bytes>(new FixedString64Bytes("Player"));
        public readonly NetworkVariable<bool> RoleRevealed = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<PlayerRole> RevealedRole = new NetworkVariable<PlayerRole>(PlayerRole.Crewmate);

        Vector2 serverInput;
        float incapacitatedTime;
        float footstepTime;
        float requestRateLimit;
        SabotageType selectedSabotage = SabotageType.GasLeak;
        bool hasFootstepInput;
        Vector2 sentInput;
        float inputResendAt;
        readonly HashSet<TownshipTaskStation> nearbyStations = new HashSet<TownshipTaskStation>();

        public bool IsLocalImpostor => IsOwner && PrivateRole.Value == PlayerRole.Impostor;
        public bool IsLocalImpostorTeam => IsOwner && PrivateRole.Value != PlayerRole.Crewmate;
        public bool CanAct => Status.Value == PlayerLifeState.Alive && GameManager.Instance != null && GameManager.Instance.Phase.Value == RoundPhase.Playing;
        public bool CanMove => Status.Value == PlayerLifeState.Alive && (GameManager.Instance == null || GameManager.Instance.Phase.Value == RoundPhase.Playing || GameManager.Instance.Phase.Value == RoundPhase.Menu);

        static readonly Vector3[] LobbySpawnOffsets =
        {
            new Vector3(0f, 0f, 0f), new Vector3(-1.4f, .1f, 0f), new Vector3(1.4f, .1f, 0f),
            new Vector3(0f, 1.4f, 0f), new Vector3(-1.5f, -1.25f, 0f), new Vector3(1.5f, -1.25f, 0f),
            new Vector3(-2.5f, .3f, 0f), new Vector3(2.5f, .3f, 0f)
        };

        void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (visualRenderers == null || visualRenderers.Length == 0) visualRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            if (body != null)
            {
                body.gravityScale = 0f;
                body.freezeRotation = true;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (GetComponent<PlayerNameplate>() == null) gameObject.AddComponent<PlayerNameplate>().Bind(DisplayName.Value.ToString());
            Status.OnValueChanged += OnStatusChanged;
            RoleRevealed.OnValueChanged += OnRoleRevealed;
            ApplyStatusVisuals(Status.Value);
            if (IsServer)
            {
                Status.Value = PlayerLifeState.Alive;
                Vector3 spawnPos = LobbySpawnOffsets[OwnerClientId % (ulong)LobbySpawnOffsets.Length];
                transform.position = spawnPos;
                if (body != null) body.position = spawnPos;
            }
            if (IsOwner)
            {
                Camera mainCamera = Camera.main;
                CameraFollow2D cameraFollow = mainCamera != null ? mainCamera.GetComponent<CameraFollow2D>() : null;
                if (cameraFollow != null) cameraFollow.SetTarget(transform);
                string savedName = !string.IsNullOrWhiteSpace(NetworkManagerUI.RuntimePlayerNameOverride)
                    ? NetworkManagerUI.RuntimePlayerNameOverride
                    : PlayerPrefs.GetString("Crony.PlayerName", "Player");
                SubmitDisplayNameRpc(savedName);
            }
        }

        public override void OnNetworkDespawn()
        {
            Status.OnValueChanged -= OnStatusChanged;
            RoleRevealed.OnValueChanged -= OnRoleRevealed;
            if (IsOwner)
            {
                Camera mainCamera = Camera.main;
                CameraFollow2D cameraFollow = mainCamera != null ? mainCamera.GetComponent<CameraFollow2D>() : null;
                if (cameraFollow != null) cameraFollow.ClearTarget(transform);
            }
        }

        void Update()
        {
            if (IsServer && Status.Value == PlayerLifeState.Incapacitated)
            {
                incapacitatedTime -= Time.deltaTime;
                if (incapacitatedTime <= 0f) ServerRecover();
            }

            if (!IsOwner || !IsSpawned) return;
            if (requestRateLimit > 0f) requestRateLimit -= Time.deltaTime;
            if (TownshipTaskMiniGameUI.IsOpen || AvatarDressingRoomUI.IsOpen || TownshipTextChatUI.CapturesInput || NetworkManagerUI.ModalOpen)
            {
                SendMoveInput(Vector2.zero);
                hasFootstepInput = false;
                UpdateFootsteps(false);
                return;
            }
            if (!CanMove)
            {
                SendMoveInput(Vector2.zero);
                hasFootstepInput = false;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && CanAct)
            {
                if (keyboard.eKey.wasPressedThisFrame) InteractNearby();
                if (keyboard.fKey.wasPressedThisFrame && requestRateLimit <= 0f) RequestNearbyIncapacitation();
                if (keyboard.rKey.wasPressedThisFrame) ReportNearbyIncapacitation();
                if (keyboard.mKey.wasPressedThisFrame) RequestEmergencyMeeting();
                if (keyboard.nKey.wasPressedThisFrame) CycleSabotage();
                if (keyboard.qKey.wasPressedThisFrame && IsLocalImpostorTeam) RequestSabotage();
                if (keyboard.gKey.wasPressedThisFrame && IsLocalImpostor && requestRateLimit <= 0f) RequestNearbyRecruit();
            }

            Vector2 input = Vector2.zero;
            if (Keyboard.current != null)
            {
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
            }
            if (Gamepad.current != null) input += Gamepad.current.leftStick.ReadValue();
            input = Vector2.ClampMagnitude(input, 1f);
            SendMoveInput(input);
            hasFootstepInput = input.sqrMagnitude > 0.01f;
            UpdateFootsteps(hasFootstepInput);
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || body == null || !CanMove) return;
            float routeMultiplier = GameManager.Instance != null && GameManager.Instance.ActiveSabotage.Value == (byte)SabotageType.RouteBlockade ? .7f : 1f;
            body.MovePosition(body.position + serverInput * moveSpeed * routeMultiplier * Time.fixedDeltaTime);
        }

        void LateUpdate()
        {
            if (!IsOwner) return;
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            CameraFollow2D follow = mainCamera.GetComponent<CameraFollow2D>();
            if (follow != null && follow.Target == null) follow.SetTarget(transform);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            TownshipTaskStation station = other.GetComponent<TownshipTaskStation>();
            if (station != null) nearbyStations.Add(station);
        }

        void OnTriggerExit2D(Collider2D other)
        {
            TownshipTaskStation station = other.GetComponent<TownshipTaskStation>();
            if (station != null) nearbyStations.Remove(station);
        }

        void OnDisable() => nearbyStations.Clear();

        // Input goes to the host when it changes, plus a slow refresh while moving (the host clears input on
        // role changes and knock-downs). Sending it every frame flooded the host's receive queue on fast clients,
        // dropping packets and eventually the connection.
        void SendMoveInput(Vector2 input)
        {
            bool changed = (input - sentInput).sqrMagnitude >= .0025f;
            if (!changed && (input == Vector2.zero || Time.unscaledTime < inputResendAt)) return;
            sentInput = input;
            inputResendAt = Time.unscaledTime + .5f;
            SetMoveInputRpc(input);
        }

        [Rpc(SendTo.Server)]
        void SetMoveInputRpc(Vector2 input)
        {
            if (!IsServer || Status.Value != PlayerLifeState.Alive || input.sqrMagnitude > 1.05f) return;
            serverInput = Vector2.ClampMagnitude(input, 1f);
        }

        [Rpc(SendTo.Server)]
        void SubmitDisplayNameRpc(string requestedName, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            string clean = SanitizeDisplayName(requestedName);
            if (clean == "Player" || IsDuplicateNameOnServer(clean))
                clean = EnsureUniqueNameOnServer(clean == "Player" ? "Player " + (OwnerClientId + 1) : clean);
            DisplayName.Value = new FixedString64Bytes(clean);
        }

        bool IsDuplicateNameOnServer(string candidateName)
        {
            foreach (PlayerController other in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
            {
                if (other == this || !other.IsSpawned) continue;
                if (string.Equals(other.DisplayName.Value.ToString(), candidateName, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        string EnsureUniqueNameOnServer(string baseName)
        {
            if (!IsDuplicateNameOnServer(baseName)) return baseName;
            string prefix = baseName.Length > 12 ? baseName.Substring(0, 12).Trim() : baseName;
            for (int suffix = 2; suffix <= 99; suffix++)
            {
                string candidate = prefix + " " + suffix;
                if (!IsDuplicateNameOnServer(candidate)) return candidate;
            }
            return prefix;
        }

        static string SanitizeDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Player";
            char[] chars = value.Trim().ToCharArray();
            System.Text.StringBuilder builder = new System.Text.StringBuilder(16);
            foreach (char c in chars)
            {
                if (builder.Length >= 16) break;
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-') builder.Append(c);
            }
            string result = builder.ToString().Trim();
            return result.Length == 0 ? "Player" : result;
        }

        public void AssignRoleServer(PlayerRole role)
        {
            if (!IsServer) return;
            PrivateRole.Value = role;
            RoleRevealed.Value = false;
            Status.Value = PlayerLifeState.Alive;
            incapacitatedTime = 0f;
            serverInput = Vector2.zero;
        }

        /// <summary>Changes the secret role mid-match (crony recruitment) without resetting life state.</summary>
        public void SetPrivateRoleServer(PlayerRole role)
        {
            if (IsServer) PrivateRole.Value = role;
        }

        public void ServerIncapacitate(float seconds)
        {
            if (!IsServer || Status.Value != PlayerLifeState.Alive) return;
            incapacitatedTime = Mathf.Max(1f, seconds);
            Status.Value = PlayerLifeState.Incapacitated;
            serverInput = Vector2.zero;
        }

        public void ServerRecover()
        {
            if (!IsServer || Status.Value != PlayerLifeState.Incapacitated) return;
            incapacitatedTime = 0f;
            Status.Value = PlayerLifeState.Alive;
        }

        public void ServerEject()
        {
            if (!IsServer) return;
            Status.Value = PlayerLifeState.Ejected;
            serverInput = Vector2.zero;
            RoleRevealed.Value = true;
            RevealedRole.Value = PrivateRole.Value;
        }

        void OnStatusChanged(PlayerLifeState previous, PlayerLifeState current)
        {
            ApplyStatusVisuals(current);
            // Only the victim hears their own takedown, so the sound never gives away where it happened.
            if (current == PlayerLifeState.Incapacitated && IsOwner) GameAudioManager.PlayIncapacitate();
            if (current == PlayerLifeState.Ejected) GameAudioManager.PlayEject();
        }

        void ApplyStatusVisuals(PlayerLifeState state)
        {
            bool ejected = state == PlayerLifeState.Ejected || state == PlayerLifeState.Disconnected;
            float alpha = state == PlayerLifeState.Incapacitated ? .48f : 1f;
            foreach (SpriteRenderer renderer in visualRenderers)
            {
                if (renderer == null) continue;
                Color color = renderer.color;
                color.a = alpha;
                if (state == PlayerLifeState.Incapacitated) color = Color.Lerp(color, Color.gray, .5f);
                renderer.color = color;
                renderer.enabled = !ejected;
            }
            if (body != null) body.simulated = !ejected && state == PlayerLifeState.Alive;
        }

        void OnRoleRevealed(bool previous, bool current)
        {
            if (current && IsOwner) NetworkManagerUI.SetNotice("Role revealed: " + RevealedRole.Value + ".");
        }

        public void InteractNearby()
        {
            if (!IsOwner || !CanAct || TownshipTaskMiniGameUI.IsOpen) return;
            GameManager manager = GameManager.Instance;
            if (manager == null) return;
            GameAudioManager.PlayUiClick();
            if (IsLocalImpostor)
            {
                int stump = HollowStumps.Nearest(transform.position);
                if (stump >= 0) { manager.RequestStumpTravelRpc(stump); return; }
            }
            TownshipTaskStation station = FindNearbyStation();
            if (station == null) { NetworkManagerUI.SetNotice("Move closer to a task marker."); return; }
            if (IsLocalImpostor)
            {
                NetworkManagerUI.SetNotice("Impostors cannot complete community tasks.");
                return;
            }
            if (PrivateRole.Value == PlayerRole.Crony) NetworkManagerUI.SetNotice("Fake it to blend in: a crony's work doesn't count for the crew.");

            if (manager.ActiveSabotage.Value != (byte)SabotageType.None && GameManager.RepairTaskFor((SabotageType)manager.ActiveSabotage.Value) == station.Kind)
            {
                manager.RequestRepairSabotageRpc((byte)station.Kind);
                return;
            }

            station.OpenMiniGameUI();
        }

        TownshipTaskStation FindNearbyStation()
        {
            TownshipTaskStation nearest = null;
            float bestDistanceSquared = interactRange * interactRange;
            foreach (TownshipTaskStation candidate in nearbyStations)
            {
                if (candidate == null || !candidate.IsSpawned || candidate.Completed.Value) continue;
                float candidateDistanceSquared = (candidate.transform.position - transform.position).sqrMagnitude;
                if (candidateDistanceSquared <= bestDistanceSquared)
                {
                    nearest = candidate;
                    bestDistanceSquared = candidateDistanceSquared;
                }
            }
            if (nearest == null) nearest = TownshipTaskStation.FindNearest(transform.position, interactRange, out _);
            return nearest;
        }

        public void RequestNearbyIncapacitation()
        {
            if (!IsOwner || !CanAct || !IsLocalImpostor || requestRateLimit > 0f) return;
            PlayerController nearest = FindNearestPlayer(true, out float distance);
            if (nearest == null || distance > interactRange) { NetworkManagerUI.SetNotice("Move close to another player first."); return; }
            GameManager.Instance.RequestIncapacitateRpc(nearest.OwnerClientId);
            requestRateLimit = .6f;
        }

        public void ReportNearbyIncapacitation()
        {
            if (!IsOwner || !CanAct) return;
            PlayerController nearest = FindNearestPlayer(false, out float distance);
            if (nearest == null || nearest.Status.Value != PlayerLifeState.Incapacitated || distance > interactRange)
            {
                NetworkManagerUI.SetNotice("No one nearby needs help.");
                return;
            }
            GameManager.Instance.RequestReportRpc(nearest.OwnerClientId);
        }

        public void RequestEmergencyMeeting()
        {
            if (!IsOwner || !CanAct) return;
            if (GameManager.Instance != null) GameManager.Instance.RequestEmergencyMeetingRpc();
        }

        public void RequestNearbyRecruit()
        {
            if (!IsOwner || !CanAct || !IsLocalImpostor || requestRateLimit > 0f) return;
            PlayerController nearest = FindNearestPlayer(true, out float distance);
            if (nearest == null || distance > interactRange) { NetworkManagerUI.SetNotice("Stand next to a Crewmate to recruit them as your crony."); return; }
            GameManager.Instance.RequestRecruitRpc(nearest.OwnerClientId);
            requestRateLimit = .6f;
        }

        void CycleSabotage()
        {
            if (!IsLocalImpostorTeam) return;
            int next = (int)selectedSabotage + 1;
            if (next > (int)SabotageType.RouteBlockade) next = (int)SabotageType.GasLeak;
            selectedSabotage = (SabotageType)next;
            NetworkManagerUI.SetNotice("Selected disruption: " + SabotageInfo.NameOf(selectedSabotage) + ". Press Q to activate.");
        }

        void RequestSabotage() => RequestSabotage(selectedSabotage);

        /// <summary>Starts a disruption; used by the Q key (current choice) and the on-screen sabotage picker.</summary>
        public void RequestSabotage(SabotageType type)
        {
            if (!IsOwner || !CanAct || !IsLocalImpostorTeam || GameManager.Instance == null) return;
            selectedSabotage = type;
            GameManager.Instance.RequestSabotageRpc((byte)type);
        }

        PlayerController FindNearestPlayer(bool livingOnly, out float distance)
        {
            PlayerController nearest = null;
            distance = float.MaxValue;
            foreach (PlayerController candidate in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
            {
                if (candidate == this || !candidate.IsSpawned) continue;
                if (livingOnly && candidate.Status.Value != PlayerLifeState.Alive) continue;
                float current = Vector2.Distance(transform.position, candidate.transform.position);
                if (current < distance) { distance = current; nearest = candidate; }
            }
            return nearest;
        }

        void UpdateFootsteps(bool moving)
        {
            if (!moving)
            {
                footstepTime = 0f;
                return;
            }
            footstepTime -= Time.deltaTime;
            if (footstepTime > 0f) return;
            footstepTime = .34f;
            SurfaceType surface = SurfaceType.Grass;
            RaycastHit2D hit = Physics2D.Raycast(transform.position + Vector3.up * .2f, Vector2.down, 1.2f);
            if (hit.collider != null)
            {
                SurfaceAudioMarker marker = hit.collider.GetComponent<SurfaceAudioMarker>();
                if (marker != null) surface = marker.Surface;
            }
            GameAudioManager.PlayFootstep(surface);
        }
    }
}
