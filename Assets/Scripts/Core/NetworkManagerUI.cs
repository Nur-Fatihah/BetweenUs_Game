using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>uGUI lobby, connection, match, voting, and chat controls for direct IP play.</summary>
    public sealed class NetworkManagerUI : MonoBehaviour
    {
        public static string RuntimePlayerNameOverride { get; private set; }

        [SerializeField] AvatarDatabaseSO avatarDatabase;
        static string sharedNotice = string.Empty;
        static float sharedNoticeTimer;
        const int DiscoveryPort = 47777;
        const string DiscoveryToken = "CRONY_ROOM_V1";

        string address = "127.0.0.1";
        string playerName = string.Empty;
        string localNotice = string.Empty;
        float noticeTimer;
        GameManager boundManager;
        NetworkManager boundNetwork;
        bool observedNetworkSession;
        bool autoJoinHandled;
        UdpClient discoveryBroadcaster;
        UdpClient discoveryListener;
        float nextDiscoveryBroadcastTime;
        string discoveredRoomStatus = "Searching local network for rooms...";

        Canvas canvas;
        RectTransform panelRect;
        Text connectionLabel;
        Text phaseLabel;
        Text noticeLabel;
        Text taskStatusLabel;
        Text lobbyHelpLabel;
        Text lobbyRosterLabel;
        Text lanHelperLabel;
        Text discoveryStatusLabel;
        Text playStatusLabel;
        Text titleLabel;
        readonly Dictionary<ulong, string> knownPlayers = new Dictionary<ulong, string>();
        bool rosterPrimed;
        RoundPhase announcedPhase = RoundPhase.Menu;
        float nextRosterCheck;
        InputField playerNameInput;
        InputField addressInput;
        InputField roomCodeInput;
        GameObject connectBlock;
        GameObject lobbyBlock;
        GameObject playBlock;
        GameObject resultsBlock;
        GameObject actionControls;
        // Impostor-team column beside the shared actions; knock-down and recruit are Impostor-only, a crony may sabotage.
        GameObject impostorControls, knockDownButton, recruitButton, sabotagePicker;
        Button startMatchButton;
        Button soloTestButton;
        Button spawnClientWindowButton;
        Button returnToLobbyButton;

        const float PanelWidth = 380f;
        static GameObject openModal;
        string pendingDisconnectReason, pendingDisconnectTitle = "DISCONNECTED";
        // Set while a same-Wi-Fi join is still connecting, so a failure can say "couldn't connect" instead of "lost".
        string lanJoinAddress;
        GameObject loadingModal;
        Text loadingText;
        string loadingMessage;
        float loadingStarted;
        bool leavingOnPurpose;
        GameObject menuBar;
        /// <summary>True while How to Play or Settings is open over an online match, so movement keys are ignored.</summary>
        public static bool ModalOpen => openModal != null;

        void Awake()
        {
            playerName = PlayerPrefs.GetString("Crony.PlayerName", string.Empty);
            ApplyCommandLineOverrides();
            BuildCanvas();
            BuildPanel();
            if (LanSupported) StartDiscoveryListener();
            if (GameManager.Instance != null) GameManager.Instance.InstallConnectionApproval();
        }

        public static void SetNotice(string message)
        {
            sharedNotice = message ?? string.Empty;
            sharedNoticeTimer = 4f;
        }

        public void SetAvatarDatabase(AvatarDatabaseSO database) => avatarDatabase = database;

        void Update()
        {
            if (sharedNoticeTimer > 0f) sharedNoticeTimer -= Time.deltaTime;
            if (noticeTimer > 0f) noticeTimer -= Time.deltaTime;

            NetworkManager network = NetworkManager.Singleton;
            BindNetworkCallbacks(network);
            if (network != null && !network.IsListening && GameManager.Instance != null)
                GameManager.Instance.InstallConnectionApproval();
            if (!autoJoinHandled && network != null && !network.IsListening && ShouldAutoJoinFromCommandLine())
            {
                autoJoinHandled = true;
                StartClientClicked();
            }
            // "-quickJoin" runs Quick Join on launch (used to test public rooms with extra windows).
            if (!autoJoinHandled && network != null && !network.IsListening && Array.Exists(Environment.GetCommandLineArgs(), arg => string.Equals(arg, "-quickJoin", StringComparison.OrdinalIgnoreCase)))
            {
                autoJoinHandled = true;
                QuickJoinClicked();
            }
            // "-joinCode ABC123" joins an online room on launch (used to test room codes with extra windows).
            if (!autoJoinHandled && network != null && !network.IsListening && roomCodeInput != null && CommandLineValue("-joinCode") is string joinCode)
            {
                autoJoinHandled = true;
                roomCodeInput.text = joinCode;
                JoinOnlineRoomClicked();
            }
            if (network != null && network.IsListening)
            {
                observedNetworkSession = true;
                if (GameManager.Instance != null && GameManager.Instance.IsSpawned) lastSeenPhase = GameManager.Instance.Phase.Value;
                if ((network.IsConnectedClient || network.IsHost) && continuityModal != null) RecoveredConnection();
            }
            else if (observedNetworkSession && network != null && KeepSessionAlive(network)) { }
            else if (observedNetworkSession)
            {
                observedNetworkSession = false;
                CloseContinuityModal();
                HideLoading();
                ResetContinuity();
                lastSeenPhase = RoundPhase.Menu;
                OnlineRooms.Forget();
                CronyRuntime.ResumeLocalMenu(pendingDisconnectReason, pendingDisconnectTitle);
                pendingDisconnectReason = null;
                pendingDisconnectTitle = "DISCONNECTED";
                leavingOnPurpose = false;
            }

            UpdateLanDiscovery(network);
            BindGameManager();
            RefreshView(network);
            UpdateLoading(network);
            if (canvas != null) canvas.enabled = (network != null && network.IsListening) || CronyRuntime.ShowNetworkSetup || loadingModal != null;
        }

        void OnDestroy()
        {
            UnbindGameManager();
            BindNetworkCallbacks(null);
            CloseDiscoverySockets();
        }

        void BindNetworkCallbacks(NetworkManager current)
        {
            if (boundNetwork == current) return;
            if (boundNetwork != null) boundNetwork.OnClientDisconnectCallback -= HandleClientDisconnected;
            boundNetwork = current;
            if (boundNetwork != null) boundNetwork.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        void HandleClientDisconnected(ulong clientId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.IsServer) return;
            string reason = manager.DisconnectReason;
            // Netcode's own messages ("Client-3 disconnected by server.", "[Disconnect Event]…") carry no real reason; the game's
            // deliberate ones (removed, suspended, wrong version, room full) are always full sentences of its own.
            if (!string.IsNullOrEmpty(reason) && (reason.EndsWith("disconnected by server.", StringComparison.Ordinal) ||
                reason.StartsWith("[Disconnect Event]", StringComparison.Ordinal))) reason = string.Empty;
            // An online room's host leaving hands the room to someone else (host migration), so that isn't final;
            // on LAN the room really is gone.
            if (!string.IsNullOrEmpty(reason) && reason.IndexOf("host shutting down", StringComparison.OrdinalIgnoreCase) >= 0)
                reason = OnlineRooms.Code != null ? string.Empty : "The host closed the room.";
            // A removal may disconnect before the private notice arrives; keep it appealable either way.
            var removal = !string.IsNullOrEmpty(reason) ? System.Text.RegularExpressions.Regex.Match(reason, @"Ref (CR-[0-9A-F]{8})") : null;
            if (removal != null && removal.Success)
                CommunicationSafety.RecordOwnIncident(removal.Groups[1].Value, reason.Contains("suspended") ? EnforcementLevel.Suspend : EnforcementLevel.Remove, reason);
            SetNotice(!string.IsNullOrWhiteSpace(reason) ? reason : "Disconnected from host (room may be full 8/8 or closed).");
            // The online panel closes with the session, so the reason is handed to the main menu to show properly.
            if (!leavingOnPurpose)
                pendingDisconnectReason = !string.IsNullOrWhiteSpace(reason) ? reason : "The connection to the host was lost, or the room was closed.";
            if (!leavingOnPurpose && lanJoinAddress != null && string.IsNullOrWhiteSpace(reason))
            {
                // Never got in: say what to check rather than "connection lost".
                pendingDisconnectTitle = "COULDN'T CONNECT";
                pendingDisconnectReason = "No game answered at " + lanJoinAddress + ". Check the host's IP address, that you're both on the same Wi-Fi, and that the host has pressed HOST LAN.";
            }
            else if (!leavingOnPurpose && lanJoinAddress != null) pendingDisconnectTitle = "COULDN'T JOIN";
            lanJoinAddress = null;
            HideLoading();
            // A drop with no reason from the host (Wi-Fi blip, host change) may be recoverable; a stated reason never is.
            unexplainedDrop = !leavingOnPurpose && string.IsNullOrWhiteSpace(reason);
            Debug.Log("[Crony] Disconnected" + (string.IsNullOrWhiteSpace(reason) ? " without a reason" : ": " + reason) + (unexplainedDrop ? "; will try to stay in the match." : "."));
            if (unexplainedDrop && dropDetectedAt < 0f) { dropDetectedAt = Time.realtimeSinceStartup; reconnectCode = OnlineRooms.Code; }
        }

        // ---- Staying in the match: host migration and reconnecting after a drop ----

        // A host leaving stops its network first; news of the new host arrives over the room service a few seconds later.
        const float MigrationGrace = 8f, ReconnectWindow = 45f, ReconnectRetry = 4f;
        RoundPhase lastSeenPhase = RoundPhase.Menu;
        bool unexplainedDrop, reconnectInFlight, continuityIsMigration;
        float dropDetectedAt = -1f, nextReconnectAt;
        int reconnectAttempts;
        GameObject continuityModal;
        Text continuityText;
        // The room code at the moment of the drop; rejoining leaves the old session, which clears OnlineRooms.Code.
        string reconnectCode;

        /// <summary>
        /// While disconnected: true keeps the session (and the match view) alive because the room is moving to a new
        /// host or this player is reconnecting. False ends the session as before.
        /// </summary>
        bool KeepSessionAlive(NetworkManager network)
        {
            if (leavingOnPurpose) return false;
            if (OnlineRooms.Migrating)
            {
                continuityIsMigration = true;
                ShowContinuity("THE HOST LEFT", "Moving the match to a new host. Everyone keeps their role.");
                return true;
            }
            if (!unexplainedDrop || dropDetectedAt < 0f) return false;
            float since = Time.realtimeSinceStartup - dropDetectedAt;
            // Online, wait a moment first (even in the lobby): news that the host changed can arrive just after the
            // connection drops.
            if (reconnectCode != null && since < MigrationGrace)
            {
                ShowContinuity("CONNECTION LOST", "Checking the room…");
                return true;
            }
            bool midMatch = lastSeenPhase == RoundPhase.Playing || lastSeenPhase == RoundPhase.Meeting;
            if (!midMatch) return false;
            if (since > ReconnectWindow)
            {
                Debug.LogWarning("[Crony] Gave up reconnecting after " + reconnectAttempts + " attempts.");
                pendingDisconnectReason = "Couldn't reconnect to the match. Check your connection and try again.";
                return false;
            }
            ShowContinuity("CONNECTION LOST", "Reconnecting to the match" + (reconnectAttempts > 0 ? " (attempt " + reconnectAttempts + ")" : "") +
                "… Your seat is held for " + (int)GameManager.SeatHoldSeconds + " seconds.");
            if (!reconnectInFlight && Time.realtimeSinceStartup >= nextReconnectAt) TryReconnect(network);
            return true;
        }

        async void TryReconnect(NetworkManager network)
        {
            if (OnlineRooms.Migrating) return;
            reconnectInFlight = true;
            reconnectAttempts++;
            Debug.Log("[Crony] Reconnect attempt " + reconnectAttempts + (reconnectCode != null ? " (room " + reconnectCode + ")" : " (LAN " + address + ")") +
                ", " + (Time.realtimeSinceStartup - dropDetectedAt).ToString("0.0") + "s after the drop.");
            try
            {
                if (reconnectCode != null) await OnlineRooms.RejoinAsync(reconnectCode);
                else if (!network.IsListening) { ConfigureAddress(network, address); network.StartClient(); }
            }
            catch (Exception e) { Debug.LogWarning("Reconnect attempt failed: " + e.Message); }
            reconnectInFlight = false;
            nextReconnectAt = Time.realtimeSinceStartup + ReconnectRetry;
        }

        void RecoveredConnection()
        {
            CloseContinuityModal();
            pendingDisconnectReason = null;
            SetNotice(continuityIsMigration ? "The match moved to a new host. Carry on!" : "Reconnected. Welcome back!");
            ResetContinuity();
        }

        void ResetContinuity()
        {
            unexplainedDrop = false;
            continuityIsMigration = false;
            reconnectCode = null;
            dropDetectedAt = -1f;
            reconnectAttempts = 0;
            nextReconnectAt = 0f;
        }

        void ShowContinuity(string title, string message)
        {
            if (continuityModal == null)
            {
                Font font = PixelUiTheme.BodyFont;
                continuityModal = SafetyUI.CreateModal(canvas.transform, "Connection", new Vector2(700f, 330f));
                Transform window = continuityModal.transform.Find("Safety Window");
                SafetyUI.AddLabel(window, title, new Vector2(.07f, .7f), new Vector2(.93f, .9f), 21, font, FontStyle.Bold, TextAnchor.MiddleLeft).name = "Continuity Title";
                continuityText = SafetyUI.AddLabel(window, message, new Vector2(.07f, .3f), new Vector2(.93f, .68f), 18, font, FontStyle.Normal, TextAnchor.UpperLeft);
                SafetyUI.AddButton(window, "Leave Match", "LEAVE MATCH", new Vector2(.6f, .08f), new Vector2(.93f, .25f), PixelUiTheme.Coral, LeaveDuringContinuity, font, 16);
                Typography.Apply(window);
            }
            continuityModal.transform.SetAsLastSibling();
            Transform heading = continuityModal.transform.Find("Safety Window/Continuity Title");
            if (heading != null && heading.TryGetComponent(out Text headingText)) headingText.text = title;
            if (continuityText != null) continuityText.text = message;
        }

        void CloseContinuityModal()
        {
            if (continuityModal != null) Destroy(continuityModal);
            continuityModal = null;
            continuityText = null;
        }

        void LeaveDuringContinuity()
        {
            pendingDisconnectReason = null;
            ResetContinuity();
            Disconnect();
        }

        void BindGameManager()
        {
            GameManager current = GameManager.Instance;
            if (boundManager == current) return;
            UnbindGameManager();
            boundManager = current;
            if (boundManager == null) return;
            boundManager.ModerationNoticeReceived += ShowLocalNotice;
        }

        void UnbindGameManager()
        {
            if (boundManager == null) return;
            boundManager.ModerationNoticeReceived -= ShowLocalNotice;
            boundManager = null;
        }

        void ShowLocalNotice(string note)
        {
            localNotice = note ?? string.Empty;
            noticeTimer = 5f;
        }

        void BuildCanvas()
        {
            GameObject canvasObject = new GameObject("Network Lobby uGUI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 35;
            PixelUiTheme.ConfigureCanvas(canvasObject.GetComponent<CanvasScaler>());
            GraphicRaycaster raycaster = canvasObject.GetComponent<GraphicRaycaster>();
            raycaster.enabled = true;
            EnsureEventSystem();
        }

        static void EnsureEventSystem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) eventSystem = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null)
            {
                GameObject root = new GameObject("Network UI EventSystem", typeof(EventSystem));
                eventSystem = root.GetComponent<EventSystem>();
            }
            InputSystemUIInputModule input = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (input == null)
            {
                BaseInputModule[] oldModules = eventSystem.GetComponents<BaseInputModule>();
                foreach (BaseInputModule oldModule in oldModules) Destroy(oldModule);
                input = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            input.AssignDefaultActions();
        }

        void BuildPanel()
        {
            GameObject panel = MakeImage(canvas.transform, "Network Lobby Panel", new Vector2(PanelWidth, 495f), new Vector2(-16f, -16f), PixelUiTheme.Cream);
            panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            // No drop shadow: the window moves between the centred setup view and the in-match corner.
            PixelUiTheme.ApplyWindow(panel, false);

            titleLabel = MakeLabel(panel.transform, "CRONY · TOWNSHIP PARK", new Vector2(18f, 14f), new Vector2(PanelWidth - 36f, 34f), 18, PixelUiTheme.Ink, FontStyle.Bold);
            connectionLabel = MakeLabel(panel.transform, "Connection: Offline", new Vector2(18f, 52f), new Vector2(PanelWidth - 36f, 26f), 14, PixelUiTheme.MutedInk, FontStyle.Bold);
            phaseLabel = MakeLabel(panel.transform, "Phase: Menu", new Vector2(18f, 78f), new Vector2(PanelWidth - 36f, 23f), 13, PixelUiTheme.MutedInk, FontStyle.Normal);
            noticeLabel = MakeLabel(panel.transform, string.Empty, new Vector2(18f, 101f), new Vector2(PanelWidth - 36f, 30f), 12, PixelUiTheme.Coral, FontStyle.Bold);

            connectBlock = MakeBlock(panel.transform, "Network Setup", 132f);
            MakeLabel(connectBlock.transform, LanSupported ? "Quick Join finds players for you, or play with friends by code or on the same Wi-Fi." : "Quick Join finds players for you, or play with friends using a room code.", new Vector2(0f, 0f), new Vector2(PanelWidth - 44f, 34f), 13, PixelUiTheme.Ink, FontStyle.Normal);
            CreateButton(connectBlock.transform, "Customize Avatar", "CUSTOMIZE AVATAR", new Vector2(0f, 36f), new Vector2(PanelWidth - 44f, 34f), PixelUiTheme.Mint, OpenCustomization);
            playerNameInput = CreateInputField(connectBlock.transform, "Player Name", playerName, new Vector2(0f, 76f), new Vector2(PanelWidth - 44f, 34f));
            CreateButton(connectBlock.transform, "Quick Join", "QUICK JOIN  ·  PLAY WITH ANYONE", new Vector2(0f, 118f), new Vector2(PanelWidth - 44f, 44f), PixelUiTheme.Amber, QuickJoinClicked);
            CreateButton(connectBlock.transform, "Create Online Room", "CREATE PRIVATE ROOM (CODE)", new Vector2(0f, 168f), new Vector2(PanelWidth - 44f, 36f), PixelUiTheme.Gold, CreateOnlineRoomClicked);
            roomCodeInput = CreateInputField(connectBlock.transform, "Room Code", string.Empty, new Vector2(-62f, 210f), new Vector2(PanelWidth - 168f, 36f));
            CreateButton(connectBlock.transform, "Join Room Code", "JOIN CODE", new Vector2(122f, 210f), new Vector2(112f, 36f), PixelUiTheme.Sky, JoinOnlineRoomClicked);
            Text lanTitle = MakeLabel(connectBlock.transform, "SAME WI-FI / LAN", new Vector2(0f, 256f), new Vector2(PanelWidth - 44f, 20f), 12, PixelUiTheme.MutedInk, FontStyle.Bold);
            addressInput = CreateInputField(connectBlock.transform, "Host IP", address, new Vector2(0f, 278f), new Vector2(PanelWidth - 44f, 34f));
            GameObject localhostButton = CreateButton(connectBlock.transform, "Use Localhost", "LOCALHOST (127.0.0.1)", new Vector2(-87f, 318f), new Vector2(164f, 30f), PixelUiTheme.Linen, UseLocalhostAddress);
            GameObject copyLanButton = CreateButton(connectBlock.transform, "Copy LAN IP", "COPY LAN IP", new Vector2(87f, 318f), new Vector2(164f, 30f), PixelUiTheme.Linen, CopyLocalLanAddress);
            localhostButton.GetComponent<RectTransform>().anchorMin = localhostButton.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, 1f);
            copyLanButton.GetComponent<RectTransform>().anchorMin = copyLanButton.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, 1f);
            GameObject host = CreateButton(connectBlock.transform, "Host Game", "HOST LAN", new Vector2(-87f, 354f), new Vector2(164f, 36f), PixelUiTheme.Linen, StartHostClicked);
            GameObject join = CreateButton(connectBlock.transform, "Join Game", "JOIN LAN", new Vector2(87f, 354f), new Vector2(164f, 36f), PixelUiTheme.Linen, StartClientClicked);
            host.GetComponent<RectTransform>().anchorMin = host.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, 1f);
            join.GetComponent<RectTransform>().anchorMin = join.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, 1f);
            discoveryStatusLabel = MakeLabel(connectBlock.transform, discoveredRoomStatus, new Vector2(0f, 394f), new Vector2(PanelWidth - 44f, 30f), 11, PixelUiTheme.Ink, FontStyle.Bold);
            lanHelperLabel = MakeLabel(connectBlock.transform, "Your LAN IP: " + GetLocalLanAddress() + " · UDP 7777 (Max 8 players)", new Vector2(0f, 424f), new Vector2(PanelWidth - 44f, 30f), 11, PixelUiTheme.MutedInk, FontStyle.Normal);
            GameObject backButton = CreateButton(connectBlock.transform, "Back To Menu", "BACK TO MENU", new Vector2(0f, 460f), new Vector2(PanelWidth - 44f, 34f), PixelUiTheme.Linen, BackToMenu);
            if (!LanSupported)
            {
                foreach (GameObject lanPart in new[] { lanTitle.gameObject, addressInput.gameObject, localhostButton, copyLanButton, host, join, discoveryStatusLabel.gameObject, lanHelperLabel.gameObject })
                    lanPart.SetActive(false);
                RectTransform backRect = backButton.GetComponent<RectTransform>();
                backRect.anchoredPosition = new Vector2(backRect.anchoredPosition.x, -256f);
                if (backButton.TryGetComponent(out UIButtonJuice backJuice)) backJuice.SyncRestPosition();
            }

            lobbyBlock = MakeBlock(panel.transform, "Lobby Controls", 132f);
            startMatchButton = CreateButton(lobbyBlock.transform, "Start Match", "START MATCH (2-8P)", new Vector2(0f, 0f), new Vector2(PanelWidth - 44f, 40f), PixelUiTheme.Amber, StartMatchClicked).GetComponent<Button>();
            soloTestButton = CreateButton(lobbyBlock.transform, "Solo Test Match", "SOLO TEST ROUND (1P)", new Vector2(0f, 45f), new Vector2(PanelWidth - 44f, 32f), PixelUiTheme.Gold, StartSoloTestClicked).GetComponent<Button>();
            spawnClientWindowButton = CreateButton(lobbyBlock.transform, "Spawn Local Client", "+ ADD LOCAL PLAYER (MAX 8)", new Vector2(0f, 82f), new Vector2(PanelWidth - 44f, 34f), PixelUiTheme.Sky, SpawnLocalClientWindowClicked).GetComponent<Button>();
            lobbyHelpLabel = MakeLabel(lobbyBlock.transform, "Each player receives a private role from the host.", new Vector2(0f, 121f), new Vector2(PanelWidth - 44f, 44f), 12, PixelUiTheme.Ink, FontStyle.Normal);
            lobbyRosterLabel = MakeLabel(lobbyBlock.transform, "Connected players (1/8):", new Vector2(0f, 168f), new Vector2(PanelWidth - 44f, 92f), 11, PixelUiTheme.MutedInk, FontStyle.Normal);
            CreateButton(lobbyBlock.transform, "Lobby Customize Avatar", "CUSTOMIZE AVATAR", new Vector2(0f, 265f), new Vector2(PanelWidth - 44f, 34f), PixelUiTheme.Mint, OpenCustomization);
            CreateButton(lobbyBlock.transform, "Lobby Room Settings", "ROOM SETTINGS", new Vector2(0f, 305f), new Vector2(PanelWidth - 44f, 34f), PixelUiTheme.Linen, OpenRoomSettings);
            CreateButton(lobbyBlock.transform, "Lobby Disconnect", "DISCONNECT", new Vector2(0f, 345f), new Vector2(PanelWidth - 44f, 34f), PixelUiTheme.Coral, Disconnect);

            playBlock = MakeBlock(panel.transform, "Match Controls", 132f);
            playStatusLabel = MakeLabel(playBlock.transform, string.Empty, new Vector2(0f, 0f), new Vector2(PanelWidth - 44f, 82f), 13, PixelUiTheme.Ink, FontStyle.Bold);
            taskStatusLabel = MakeLabel(playBlock.transform, string.Empty, new Vector2(0f, 82f), new Vector2(PanelWidth - 44f, 55f), 12, PixelUiTheme.MutedInk, FontStyle.Normal);
            CreateButton(playBlock.transform, "Match Disconnect", "DISCONNECT", new Vector2(0f, 148f), new Vector2(PanelWidth - 44f, 36f), PixelUiTheme.Coral, Disconnect);

            resultsBlock = MakeBlock(panel.transform, "Round Results", 132f);
            MakeLabel(resultsBlock.transform, "Round finished. Host can return everyone to the lobby or disconnect.", new Vector2(0f, 0f), new Vector2(PanelWidth - 44f, 52f), 13, PixelUiTheme.Ink, FontStyle.Normal);
            returnToLobbyButton = CreateButton(resultsBlock.transform, "Return To Lobby", "RETURN TO LOBBY (HOST)", new Vector2(0f, 58f), new Vector2(PanelWidth - 44f, 36f), PixelUiTheme.Amber, ReturnToLobbyClicked).GetComponent<Button>();
            CreateButton(resultsBlock.transform, "Results Disconnect", "DISCONNECT", new Vector2(0f, 102f), new Vector2(PanelWidth - 44f, 36f), PixelUiTheme.Coral, Disconnect);

            actionControls = new GameObject("Match Action Buttons · Bottom Right", typeof(RectTransform));
            actionControls.transform.SetParent(canvas.transform, false);
            RectTransform actionRect = actionControls.GetComponent<RectTransform>();
            actionRect.anchorMin = actionRect.anchorMax = new Vector2(1f, 0f);
            actionRect.pivot = new Vector2(1f, 0f);
            actionRect.sizeDelta = new Vector2(180f, 148f);
            actionRect.anchoredPosition = new Vector2(-16f, 16f);
            CreateActionButton(actionControls.transform, "Interact [E]", "INTERACT  [E]", 104f, PixelUiTheme.Amber, InteractNearby);
            CreateActionButton(actionControls.transform, "Report [R]", "REPORT  [R]", 52f, PixelUiTheme.Coral, ReportNearby);
            CreateActionButton(actionControls.transform, "Meeting [M]", "MEETING  [M]", 0f, PixelUiTheme.Gold, CallEmergencyMeeting);

            impostorControls = new GameObject("Impostor Action Buttons · Bottom Right", typeof(RectTransform));
            impostorControls.transform.SetParent(canvas.transform, false);
            RectTransform impostorRect = impostorControls.GetComponent<RectTransform>();
            impostorRect.anchorMin = impostorRect.anchorMax = impostorRect.pivot = new Vector2(1f, 0f);
            impostorRect.sizeDelta = new Vector2(180f, 148f);
            impostorRect.anchoredPosition = new Vector2(-208f, 16f);
            recruitButton = CreateActionButton(impostorControls.transform, "Recruit [G]", "RECRUIT CRONY  [G]", 104f, PixelUiTheme.Plum, RecruitNearby);
            CreateActionButton(impostorControls.transform, "Sabotage [Q]", "SABOTAGE  [Q]", 52f, PixelUiTheme.Coral, ToggleSabotagePicker);
            knockDownButton = CreateActionButton(impostorControls.transform, "Knock Down [F]", "KNOCK DOWN  [F]", 0f, PixelUiTheme.Coral, KnockDownNearby);
            BuildSabotagePicker();

            menuBar = new GameObject("Online Menu Bar · Top Left", typeof(RectTransform));
            menuBar.transform.SetParent(canvas.transform, false);
            RectTransform barRect = menuBar.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = barRect.pivot = new Vector2(0f, 1f);
            barRect.sizeDelta = new Vector2(320f, 40f);
            barRect.anchoredPosition = new Vector2(16f, -16f);
            GameObject help = CreateButton(menuBar.transform, "How To Play", "HOW TO PLAY", new Vector2(-82f, 0f), new Vector2(150f, 38f), PixelUiTheme.Linen, OpenHowToPlay);
            GameObject settings = CreateButton(menuBar.transform, "Settings", "SETTINGS  [ESC]", new Vector2(82f, 0f), new Vector2(150f, 38f), PixelUiTheme.Linen, OpenSettings);
            help.GetComponent<RectTransform>().anchorMin = help.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, 1f);
            settings.GetComponent<RectTransform>().anchorMin = settings.GetComponent<RectTransform>().anchorMax = new Vector2(.5f, 1f);

            Typography.Apply(canvas.transform);
            connectBlock.SetActive(true);
            lobbyBlock.SetActive(false);
            playBlock.SetActive(false);
            resultsBlock.SetActive(false);
            actionControls.SetActive(false);
        }

        GameObject CreateActionButton(Transform parent, string name, string label, float bottomOffset, Color color, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = CreateButton(parent, name, label, Vector2.zero, new Vector2(180f, 44f), color, action);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottomOffset);
            if (buttonObject.TryGetComponent(out UIButtonJuice juice)) juice.SyncRestPosition();
            return buttonObject;
        }

        /// <summary>Small card above the Sabotage button: one row per disruption, like the offline sabotage menu.</summary>
        void BuildSabotagePicker()
        {
            const float rowHeight = 48f;
            float height = 56f + SabotageInfo.Names.Length * rowHeight;
            sabotagePicker = MakeImage(canvas.transform, "Sabotage Picker", new Vector2(320f, height), Vector2.zero, PixelUiTheme.Cream);
            RectTransform rect = sabotagePicker.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-208f, 172f);
            PixelUiTheme.ApplyCard(sabotagePicker, false);
            MakeLabel(sabotagePicker.transform, "CHOOSE A DISRUPTION", new Vector2(0f, 10f), new Vector2(290f, 30f), 15, PixelUiTheme.Ink, FontStyle.Bold);
            for (int i = 0; i < SabotageInfo.Names.Length; i++)
            {
                SabotageType type = (SabotageType)(i + 1);
                CreateButton(sabotagePicker.transform, "Sabotage " + type, SabotageInfo.Names[i] + "  ·  " + SabotageInfo.Locations[i],
                    new Vector2(0f, 46f + i * rowHeight), new Vector2(296f, 42f), PixelUiTheme.Linen, () => StartSabotage(type));
            }
            sabotagePicker.SetActive(false);
        }

        GameObject MakeBlock(Transform parent, string name, float top)
        {
            GameObject block = new GameObject(name, typeof(RectTransform));
            block.transform.SetParent(parent, false);
            RectTransform rect = block.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = new Vector2(PanelWidth - 32f, 525f);
            rect.anchoredPosition = new Vector2(0f, -top);
            return block;
        }

        Text MakeLabel(Transform parent, string value, Vector2 topOffset, Vector2 size, int fontSize, Color color, FontStyle style)
        {
            GameObject labelObject = new GameObject(string.IsNullOrEmpty(value) ? "Status Label" : value, typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(parent, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(topOffset.x, -topOffset.y);
            Text text = labelObject.GetComponent<Text>();
            text.font = PixelUiTheme.BodyFont;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.text = value;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        GameObject CreateButton(Transform parent, string name, string label, Vector2 topOffset, Vector2 size, Color color, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = MakeImage(parent, name, size, Vector2.zero, color);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(topOffset.x, -topOffset.y);
            Button button = buttonObject.AddComponent<Button>();
            PixelUiTheme.StyleButton(button, color);
            button.onClick.AddListener(action);
            Text text = MakeLabel(buttonObject.transform, label, Vector2.zero, size - new Vector2(16f, 4f), 13, PixelUiTheme.ButtonTextColor(color), FontStyle.Bold);
            text.raycastTarget = false;
            return buttonObject;
        }

        InputField CreateInputField(Transform parent, string name, string initialText, Vector2 topOffset, Vector2 size)
        {
            GameObject inputObject = MakeImage(parent, name, size, Vector2.zero, PixelUiTheme.Almond);
            RectTransform rect = inputObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(topOffset.x, -topOffset.y);
            InputField input = inputObject.AddComponent<InputField>();
            GameObject textObject = new GameObject("Input Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(inputObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 5f); textRect.offsetMax = new Vector2(-10f, -5f);
            Text text = textObject.GetComponent<Text>();
            text.font = PixelUiTheme.BodyFont; text.fontSize = 14; text.color = PixelUiTheme.Ink;
            text.alignment = TextAnchor.MiddleLeft; text.supportRichText = false;
            GameObject placeholderObject = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            placeholderObject.transform.SetParent(inputObject.transform, false);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero; placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(10f, 5f); placeholderRect.offsetMax = new Vector2(-10f, -5f);
            Text placeholder = placeholderObject.GetComponent<Text>();
            placeholder.font = PixelUiTheme.BodyFont; placeholder.fontSize = 14; placeholder.color = PixelUiTheme.MutedInk;
            placeholder.text = name == "Host IP" ? "Host IP (e.g. 127.0.0.1)" : name == "Player Name" ? "Your Name (optional)" : name == "Room Code" ? "Room code" : "Type a short message";
            placeholder.alignment = TextAnchor.MiddleLeft;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.text = initialText;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = name == "Host IP" ? 64 : name == "Player Name" ? 20 : name == "Room Code" ? 12 : 180;
            input.caretColor = PixelUiTheme.Ink;
            input.selectionColor = new Color(1f, .67f, .29f, .35f);
            return input;
        }

        GameObject MakeImage(Transform parent, string name, Vector2 size, Vector2 position, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return imageObject;
        }

        void RefreshView(NetworkManager manager)
        {
            bool listening = manager != null && manager.IsListening;
            GameManager game = GameManager.Instance;
            PlayerController player = listening && manager.LocalClient != null && manager.LocalClient.PlayerObject != null
                ? manager.LocalClient.PlayerObject.GetComponent<PlayerController>() : null;
            RoundPhase phase = game != null && game.IsSpawned ? game.Phase.Value : RoundPhase.Menu;

            connectBlock.SetActive(!listening);
            lobbyBlock.SetActive(listening && phase == RoundPhase.Menu && player != null);
            playBlock.SetActive(listening && (phase == RoundPhase.Playing || phase == RoundPhase.Meeting || player == null));
            SyncMeetingScreen(game, phase == RoundPhase.Meeting && player != null);
            resultsBlock.SetActive(listening && phase == RoundPhase.Results && player != null);
            if (menuBar != null) menuBar.SetActive(listening && !ModalOpen);
            if (listening && openModal == null && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame &&
                !TownshipTextChatUI.HandlesEscape && !TownshipTaskMiniGameUI.IsOpen && !AvatarDressingRoomUI.IsOpen)
                OpenSettings();
            else if (openModal != null && UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            { Destroy(openModal); openModal = null; }
            if (actionControls != null)
            {
                actionControls.SetActive(listening && phase == RoundPhase.Playing && player != null &&
                    !TownshipTaskMiniGameUI.IsOpen && !AvatarDressingRoomUI.IsOpen);
                bool impostorTeam = actionControls.activeSelf && player.IsLocalImpostorTeam;
                impostorControls.SetActive(impostorTeam);
                knockDownButton.SetActive(impostorTeam && player.IsLocalImpostor);
                recruitButton.SetActive(impostorTeam && player.IsLocalImpostor);
                if (!impostorTeam || ModalOpen) sabotagePicker.SetActive(false);
            }

            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = listening ? Vector2.one : new Vector2(.5f, .5f);
            panelRect.anchoredPosition = listening ? new Vector2(-16f, -16f) : Vector2.zero;
            panelRect.localScale = listening ? Vector3.one : Vector3.one * 1.2f;
            titleLabel.text = !listening ? "PLAY ONLINE · HOST OR JOIN" : OnlineRooms.Code != null ? "ROOM CODE  ·  " + OnlineRooms.DisplayCode : "CRONY · TOWNSHIP PARK";
            float panelHeight = !listening ? (LanSupported ? 650f : 430f) : phase == RoundPhase.Meeting ? 720f : phase == RoundPhase.Playing ? 370f : phase == RoundPhase.Results ? 350f : 535f;
            panelRect.sizeDelta = new Vector2(PanelWidth, panelHeight);
            connectionLabel.text = manager == null ? "Connection: NetworkManager unavailable" : listening
                ? "Connection: " + (manager.IsHost ? "Host" : "Client") + " · " + manager.ConnectedClientsIds.Count + "/" + GameManager.MaxPlayers + " players"
                : "Connection: Offline";
            phaseLabel.text = "Phase: " + (game != null && game.IsSpawned ? phase.ToString() : "Menu / Connecting");

            string notice = noticeTimer > 0f ? localNotice : sharedNoticeTimer > 0f ? sharedNotice : string.Empty;
            noticeLabel.text = notice;
            noticeLabel.gameObject.SetActive(!string.IsNullOrEmpty(notice));
            address = addressInput != null && !string.IsNullOrWhiteSpace(addressInput.text) ? addressInput.text.Trim() : "127.0.0.1";
            if (discoveryStatusLabel != null) discoveryStatusLabel.text = discoveredRoomStatus;

            if (manager != null && manager.IsListening && game != null && game.IsSpawned)
            {
                taskStatusLabel.text = "Community tasks: " + game.CompletedTaskCount.Value + "/10" +
                    (game.ActiveSabotage.Value != (byte)SabotageType.None
                        ? "\nACTIVE DISRUPTION: " + SabotageInfo.NameOf((SabotageType)game.ActiveSabotage.Value) + " · " + Mathf.CeilToInt(game.SabotageTimeRemaining.Value) + "s"
                        : string.Empty);
                int count = manager.ConnectedClientsIds.Count;
                if (startMatchButton != null) startMatchButton.interactable = manager.IsServer && count >= 2 && count <= GameManager.MaxPlayers;
                if (soloTestButton != null) soloTestButton.gameObject.SetActive(manager.IsServer);
                if (spawnClientWindowButton != null)
                {
                    // Extra local windows join by IP, which a relay room does not accept.
                    spawnClientWindowButton.gameObject.SetActive(manager.IsServer && OnlineRooms.Code == null && LanSupported);
                    spawnClientWindowButton.interactable = count < GameManager.MaxPlayers;
                }
                if (returnToLobbyButton != null) returnToLobbyButton.gameObject.SetActive(manager.IsServer);
                if (lobbyHelpLabel != null) lobbyHelpLabel.text = OnlineRooms.Code != null
                    ? (OnlineRooms.IsPublic ? "PUBLIC ROOM · CODE " : "ROOM CODE: ") + OnlineRooms.DisplayCode +
                      (manager.IsServer ? (OnlineRooms.IsPublic ? " · Quick Join players land here; start when you're ready." : "  (copied) · share it with friends to join from anywhere.") : "  · waiting for the host to start.")
                    : manager.IsServer
                    ? "Host IP: " + GetLocalLanAddress() + " (or 127.0.0.1 on same PC). Walk around while waiting for up to 8 players!"
                    : "Connected! Walk around the park while waiting for the host to start.";
                if (lobbyRosterLabel != null) lobbyRosterLabel.text = BuildLobbyRosterText(manager, game);
                if (player != null)
                {
                    PlayerRole role = player.PrivateRole.Value;
                    playStatusLabel.text = "Role: " + (role == PlayerRole.Impostor ? "IMPOSTOR" : role == PlayerRole.Crony ? "CRONY of " + (GameManager.LocalCronyPartner ?? "the Impostor") : "CREWMATE") +
                        " · Status: " + player.Status.Value + "\nMove with WASD / arrows · E interact · R report · M meeting";
                    if (role == PlayerRole.Impostor) playStatusLabel.text += "\nF knock down · G recruit crony · N choose disruption · Q activate";
                    else if (role == PlayerRole.Crony) playStatusLabel.text += "\nYour tasks don't count · N choose disruption · Q activate";
                }
            }
            else
            {
                taskStatusLabel.text = string.Empty;
                playStatusLabel.text = "Waiting for the network session to finish connecting.";
                if (startMatchButton != null) startMatchButton.interactable = false;
                if (soloTestButton != null) soloTestButton.gameObject.SetActive(false);
                if (spawnClientWindowButton != null) spawnClientWindowButton.gameObject.SetActive(false);
                if (returnToLobbyButton != null) returnToLobbyButton.gameObject.SetActive(false);
            }
            AnnounceSessionChanges(manager, game, listening);
        }

        /// <summary>Posts joins, leaves and phase changes into chat as local system lines.</summary>
        void AnnounceSessionChanges(NetworkManager manager, GameManager game, bool listening)
        {
            TextChatManager chat = TextChatManager.Instance;
            if (!listening || game == null || !game.IsSpawned || chat == null)
            {
                knownPlayers.Clear();
                rosterPrimed = false;
                announcedPhase = RoundPhase.Menu;
                return;
            }
            if (Time.unscaledTime < nextRosterCheck) return;
            nextRosterCheck = Time.unscaledTime + .5f;

            HashSet<ulong> present = new HashSet<ulong>();
            foreach (ulong id in manager.ConnectedClientsIds)
            {
                PlayerController p = game.GetPlayer(id);
                string name = p != null ? p.DisplayName.Value.ToString() : string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;
                present.Add(id);
                if (!knownPlayers.ContainsKey(id) && rosterPrimed) chat.PostSystem(name + " joined the game.");
                knownPlayers[id] = name;
            }
            List<ulong> gone = null;
            foreach (KeyValuePair<ulong, string> pair in knownPlayers)
                if (!present.Contains(pair.Key)) (gone ??= new List<ulong>()).Add(pair.Key);
            if (gone != null)
                foreach (ulong id in gone) { chat.PostSystem(knownPlayers[id] + " left the game."); knownPlayers.Remove(id); }
            if (!rosterPrimed && present.Count > 0)
            {
                rosterPrimed = true;
                chat.PostSystem(!manager.IsHost ? "Connected. Say hi in chat!" : OnlineRooms.Code != null ? "Room open. Share the room code so friends can join." : "Room open. Share your IP so friends can join.");
            }

            RoundPhase phase = game.Phase.Value;
            if (phase == announcedPhase) return;
            announcedPhase = phase;
            if (phase == RoundPhase.Playing) chat.PostSystem("The round has started. Good luck!");
            else if (phase == RoundPhase.Meeting) chat.PostSystem("Emergency meeting! Discuss and vote.");
            else if (phase == RoundPhase.Results) chat.PostSystem("The round is over.");
            else if (phase == RoundPhase.Menu) chat.PostSystem("Back in the lobby.");
        }

        void BackToMenu()
        {
            GameAudioManager.PlayUiClick();
            CronyRuntime.ResumeLocalMenu();
        }

        void StartHostClicked()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) { SetNotice("NetworkManager is not present in this scene."); return; }
            SavePlayerNamePreference();
            ConfigureAddress(manager, "127.0.0.1");
            GameAudioManager.PlayUiClick();
            CronyRuntime.PrepareForNetworkGame();
            if (!manager.StartHost()) ConnectFailed("COULDN'T HOST", "The game couldn't open a room on this computer. Another copy of Crony may already be hosting (UDP port 7777). Close it and try again.");
        }

        void StartClientClicked()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) { SetNotice("NetworkManager is not present in this scene."); return; }
            SavePlayerNamePreference();
            ConfigureAddress(manager, address);
            GameAudioManager.PlayUiClick();
            CronyRuntime.PrepareForNetworkGame();
            lanJoinAddress = address;
            ShowLoading("JOINING GAME", "Connecting to the host at " + address);
            if (!manager.StartClient()) ConnectFailed("COULDN'T CONNECT", "The game couldn't start connecting. Check the host's IP address and try again.");
        }

        void StartMatchClicked()
        {
            GameManager game = GameManager.Instance;
            if (game == null || !game.StartMatchFromHost(false)) SetNotice("Connect 2 to 8 players to start (or use Solo Test Round).");
            else GameAudioManager.PlayUiClick();
        }

        void StartSoloTestClicked()
        {
            GameManager game = GameManager.Instance;
            if (game == null || !game.StartMatchFromHost(true)) SetNotice("Could not start solo test round.");
            else GameAudioManager.PlayUiClick();
        }

        void ReturnToLobbyClicked()
        {
            GameManager game = GameManager.Instance;
            if (game != null && game.ReturnToLobbyFromHost()) GameAudioManager.PlayUiClick();
        }

        void SpawnLocalClientWindowClicked()
        {
            NetworkManager manager = NetworkManager.Singleton;
            int currentCount = manager != null && manager.IsListening ? manager.ConnectedClientsIds.Count : 1;
            if (currentCount >= GameManager.MaxPlayers)
            {
                SetNotice("Maximum of 8 players reached (8/8).");
                return;
            }

            string exePath = ResolveExecutablePath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                SetNotice("Use top menu: Crony > Multiplayer Launcher > 1. Build Test Client (once).");
                return;
            }

            int nextPlayerNumber = Mathf.Clamp(currentCount + 1, 2, GameManager.MaxPlayers);
            try
            {
                LaunchTestClient(exePath, nextPlayerNumber);
                GameAudioManager.PlayUiClick();
                SetNotice("Launching Player " + nextPlayerNumber + " window (auto-joining 127.0.0.1)...");
            }
            catch (Exception ex)
            {
                SetNotice("Could not launch client window: " + ex.Message);
            }
        }

        static string ResolveExecutablePath()
        {
            if (!Application.isEditor)
            {
                try
                {
                    return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                }
                catch
                {
                    return string.Empty;
                }
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string candidate = Path.Combine(projectRoot, TestClientExecutable);
            return File.Exists(candidate) ? candidate : string.Empty;
        }

        /// <summary>Where the editor's Multiplayer Launcher builds the test client (relative to the project root).</summary>
        public static string TestClientBuildLocation =>
            Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer
                ? "Builds/LocalTestClient/Crony.app" : "Builds/LocalTestClient/Crony.exe";

        /// <summary>The file to run for a test client. On macOS that is the binary inside the .app bundle, so several copies can be started with arguments.</summary>
        public static string TestClientExecutable =>
            TestClientBuildLocation.EndsWith(".app", StringComparison.Ordinal) ? TestClientBuildLocation + "/Contents/MacOS/Crony" : TestClientBuildLocation;

        /// <summary>Starts one more game window that joins the host on this machine.</summary>
        public static void LaunchTestClient(string executable, int playerNumber)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                Arguments = "-autoJoinLocalhost 127.0.0.1 -playerName \"Player " + playerNumber + "\" -screen-fullscreen 0 -screen-width 960 -screen-height 540",
                UseShellExecute = false
            });
        }

        void UseLocalhostAddress()
        {
            address = "127.0.0.1";
            if (addressInput != null) addressInput.text = address;
            GameAudioManager.PlayUiClick();
            SetNotice("Host IP set to 127.0.0.1 (Localhost).");
        }

        void CopyLocalLanAddress()
        {
            string lanIp = GetLocalLanAddress();
            GUIUtility.systemCopyBuffer = lanIp;
            if (addressInput != null) addressInput.text = lanIp;
            address = lanIp;
            GameAudioManager.PlayUiClick();
            SetNotice("Copied LAN IP: " + lanIp + " (share this with up to 7 friends on Wi-Fi/LAN).");
        }

        void SavePlayerNamePreference()
        {
            if (playerNameInput == null) return;
            playerName = playerNameInput.text != null ? playerNameInput.text.Trim() : string.Empty;
            RuntimePlayerNameOverride = playerName;
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                PlayerPrefs.SetString("Crony.PlayerName", playerName);
                PlayerPrefs.Save();
            }
        }

        void ApplyCommandLineOverrides()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-playerName", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    playerName = args[i + 1].Trim();
                    RuntimePlayerNameOverride = playerName;
                }
                else if (string.Equals(args[i], "-autoJoinLocalhost", StringComparison.OrdinalIgnoreCase))
                {
                    address = i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal)
                        ? args[i + 1].Trim()
                        : "127.0.0.1";
                }
            }
        }

        static string CommandLineValue(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1].Trim();
            return null;
        }

        static bool ShouldAutoJoinFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], "-autoJoinLocalhost", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        void StartDiscoveryListener()
        {
            try
            {
                discoveryListener = new UdpClient();
                discoveryListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                discoveryListener.ExclusiveAddressUse = false;
                discoveryListener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                discoveryListener.EnableBroadcast = true;
            }
            catch
            {
                discoveryListener = null;
            }
        }

        void CloseDiscoverySockets()
        {
            try { discoveryBroadcaster?.Close(); } catch { }
            try { discoveryListener?.Close(); } catch { }
            discoveryBroadcaster = null;
            discoveryListener = null;
        }

        /// <summary>Browsers can't open UDP sockets, host, or discover LAN games, so the browser build plays online rooms only.</summary>
        static bool LanSupported => Application.platform != RuntimePlatform.WebGLPlayer;

        void UpdateLanDiscovery(NetworkManager manager)
        {
            if (!LanSupported) return;
            bool isHost = manager != null && manager.IsListening && manager.IsServer;
            if (isHost && Time.unscaledTime >= nextDiscoveryBroadcastTime)
            {
                nextDiscoveryBroadcastTime = Time.unscaledTime + 1.25f;
                try
                {
                    if (discoveryBroadcaster == null)
                    {
                        discoveryBroadcaster = new UdpClient();
                        discoveryBroadcaster.EnableBroadcast = true;
                    }
                    int count = manager.ConnectedClientsIds.Count;
                    string payload = DiscoveryToken + "|" + GetLocalLanAddress() + "|" + count + "|" + GameManager.MaxPlayers;
                    byte[] bytes = Encoding.UTF8.GetBytes(payload);
                    discoveryBroadcaster.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                }
                catch { }
            }

            if (discoveryListener == null) return;
            try
            {
                while (discoveryListener.Available > 0)
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = discoveryListener.Receive(ref remote);
                    string text = Encoding.UTF8.GetString(data);
                    string[] parts = text.Split('|');
                    if (parts.Length >= 4 && parts[0] == DiscoveryToken)
                    {
                        string hostIp = remote.Address != null && !IPAddress.IsLoopback(remote.Address)
                            ? remote.Address.ToString()
                            : parts[1];
                        discoveredRoomStatus = "Room found on LAN: " + hostIp + " (" + parts[2] + "/" + parts[3] + " players)";
                        if (addressInput != null && (string.IsNullOrWhiteSpace(addressInput.text) || addressInput.text == "127.0.0.1"))
                        {
                            // Keep 127.0.0.1 if on the same machine, or fill LAN IP if remote
                            if (hostIp != GetLocalLanAddress())
                            {
                                address = hostIp;
                                addressInput.text = hostIp;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        static string BuildLobbyRosterText(NetworkManager manager, GameManager game)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Lobby Roster (").Append(manager.ConnectedClientsIds.Count).Append('/').Append(GameManager.MaxPlayers).Append(" max):");
            foreach (ulong clientId in manager.ConnectedClientsIds)
            {
                PlayerController p = game.GetPlayer(clientId);
                string display = p != null && !string.IsNullOrWhiteSpace(p.DisplayName.Value.ToString())
                    ? p.DisplayName.Value.ToString()
                    : "Player " + (clientId + 1);
                builder.Append("\n• ").Append(display);
                if (clientId == NetworkManager.ServerClientId) builder.Append(" (Host)");
                if (manager.LocalClient != null && clientId == manager.LocalClientId) builder.Append(" [You]");
            }
            return builder.ToString();
        }

        static string GetLocalLanAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint endPoint)
                        return endPoint.Address.ToString();
                }
            }
            catch
            {
                // Fallback to DNS host lookup below when offline.
            }

            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch
            {
                // Ignore and return localhost fallback.
            }
            return "127.0.0.1";
        }

        void InteractNearby()
        {
            PlayerController player = GetLocalPlayer();
            if (player != null) player.InteractNearby();
        }

        void KnockDownNearby()
        {
            PlayerController player = GetLocalPlayer();
            if (player != null) player.RequestNearbyIncapacitation();
        }

        void RecruitNearby()
        {
            PlayerController player = GetLocalPlayer();
            if (player != null) player.RequestNearbyRecruit();
        }

        void ToggleSabotagePicker()
        {
            GameAudioManager.PlayUiClick();
            sabotagePicker.SetActive(!sabotagePicker.activeSelf);
            if (sabotagePicker.activeSelf) sabotagePicker.transform.SetAsLastSibling();
        }

        void StartSabotage(SabotageType type)
        {
            sabotagePicker.SetActive(false);
            PlayerController player = GetLocalPlayer();
            if (player != null) player.RequestSabotage(type);
        }

        void ReportNearby()
        {
            PlayerController player = GetLocalPlayer();
            if (player != null) player.ReportNearbyIncapacitation();
        }

        void CallEmergencyMeeting()
        {
            PlayerController player = GetLocalPlayer();
            if (player != null) player.RequestEmergencyMeeting();
        }

        static PlayerController GetLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.LocalClient != null && manager.LocalClient.PlayerObject != null
                ? manager.LocalClient.PlayerObject.GetComponent<PlayerController>() : null;
        }

        void OpenCustomization()
        {
            NetworkManager manager = NetworkManager.Singleton;
            PlayerAvatarLayerManager avatar = manager != null && manager.LocalClient != null && manager.LocalClient.PlayerObject != null
                ? manager.LocalClient.PlayerObject.GetComponent<PlayerAvatarLayerManager>() : null;
            if (avatar != null) AvatarDressingRoomUI.Open(avatar);
            else AvatarDressingRoomUI.Open(avatarDatabase);
            GameAudioManager.PlayUiClick();
        }

        MeetingScreen meetingScreen;
        readonly MeetingView meetingView = new MeetingView();

        void SyncMeetingScreen(GameManager game, bool inMeeting)
        {
            if (inMeeting && meetingScreen == null && game != null && game.Stage.Value != MeetingStage.None)
            {
                if (openModal != null) { Destroy(openModal); openModal = null; }
                meetingScreen = MeetingScreen.Show(canvas.transform, BuildMeetingView, OnMeetingSeat, () => Vote(ulong.MaxValue, true),
                    PlayerPrefs.GetInt(GameSettings.LargeTextKey, 0) == 1);
            }
            else if (!inMeeting && meetingScreen != null) { Destroy(meetingScreen.gameObject); meetingScreen = null; }
        }

        MeetingView BuildMeetingView()
        {
            MeetingView v = meetingView;
            GameManager game = GameManager.Instance;
            NetworkManager network = NetworkManager.Singleton;
            v.Seats.Clear(); v.Clues.Clear(); v.VotesByTarget.Clear();
            if (game == null || network == null || !game.IsSpawned) return v;
            ulong local = network.LocalClientId;
            v.Stage = game.Stage.Value;
            v.SecondsLeft = game.StageSecondsLeft;
            v.StageLength = game.StageLength.Value;
            v.BodyReport = game.ReportedPlayerId.Value != ulong.MaxValue;
            v.Headline = game.MeetingHeadline();
            foreach (ulong clientId in network.ConnectedClientsIds)
            {
                PlayerController player = game.GetPlayer(clientId);
                if (player == null || (player.Status.Value != PlayerLifeState.Alive && clientId != game.MeetingEjectedId)) continue;
                PlayerAvatarLayerManager avatar = player.GetComponent<PlayerAvatarLayerManager>();
                v.Seats.Add(new MeetingSeat
                {
                    Id = (long)clientId, Name = player.DisplayName.Value.ToString(), IsLocal = clientId == local,
                    Color = avatar != null ? (Color)avatar.CurrentState.ClothingTint : PixelUiTheme.Tan,
                    CanVote = player.Status.Value == PlayerLifeState.Alive || clientId == game.MeetingEjectedId,
                    HasVoted = game.VotedClients.Contains(clientId),
                    Portrait = avatar != null ? avatar.LayerRenderers : null
                });
            }
            PlayerController me = game.GetPlayer(local);
            v.LocalCanVote = me != null && me.Status.Value == PlayerLifeState.Alive;
            v.LocalVote = game.VotedClients.Contains(local) ? (localVote ?? MeetingView.Skip) : MeetingView.NoVote;
            if (game.InterferenceActive.Value) v.Clues.Add("Interference is blocking chat until the router is repaired.");
            if (v.Stage >= MeetingStage.Reveal)
                foreach (var pair in game.RevealedVotes)
                {
                    List<long> voters = new List<long>();
                    foreach (ulong voter in pair.Value) voters.Add((long)voter);
                    v.VotesByTarget[pair.Key == ulong.MaxValue ? MeetingView.Skip : (long)pair.Key] = voters;
                }
            v.Outcome = game.MeetingOutcome;
            v.EjectedRole = game.MeetingEjectedRole;
            v.EjectedId = game.MeetingEjectedId == ulong.MaxValue ? MeetingView.NoVote : (long)game.MeetingEjectedId;
            if (v.Stage == MeetingStage.Discussion) localVote = null;
            return v;
        }

        long? localVote;

        void OnMeetingSeat(long id)
        {
            GameManager game = GameManager.Instance;
            if (game == null) return;
            if (game.Stage.Value == MeetingStage.Voting) { Vote((ulong)id, false); return; }
            if (game.Stage.Value == MeetingStage.Discussion) TextChatManager.Instance?.Send("I suspect " + game.NameOf((ulong)id) + ".");
        }

        void Vote(ulong target, bool skip)
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.Stage.Value != MeetingStage.Voting || localVote.HasValue) return;
            localVote = skip ? MeetingView.Skip : (long)target;
            game.CastVoteRpc(target, skip);
            GameAudioManager.PlayVote();
        }

        void OpenHowToPlay()
        {
            if (openModal != null) return;
            GameAudioManager.PlayUiClick();
            openModal = HowToPlayUI.Show(canvas.transform, PixelUiTheme.BodyFont, PlayerPrefs.GetInt(GameSettings.LargeTextKey, 0) == 1, null).gameObject;
        }

        /// <summary>Lobby window: the host changes the match rules and can remove players; everyone else sees the rules.</summary>
        void OpenRoomSettings()
        {
            GameManager game = GameManager.Instance;
            NetworkManager manager = NetworkManager.Singleton;
            if (game == null || !game.IsSpawned || manager == null) return;
            if (openModal != null) Destroy(openModal);
            GameAudioManager.PlayUiClick();
            bool host = manager.IsServer;
            Font font = PixelUiTheme.BodyFont;
            openModal = SafetyUI.CreateModal(canvas.transform, "Room Settings", new Vector2(720f, 680f));
            Transform window = openModal.transform.Find("Safety Window");
            SafetyUI.AddLabel(window, "ROOM SETTINGS", new Vector2(.06f, .88f), new Vector2(.94f, .96f), 30, font, FontStyle.Bold, TextAnchor.MiddleLeft);
            SafetyUI.AddLabel(window, host ? "Tap a value to change it. Rules apply from the next match." : "Only the host can change these.", new Vector2(.06f, .83f), new Vector2(.94f, .88f), 15, font, FontStyle.Normal, TextAnchor.MiddleLeft);

            MatchRules rules = game.Rules.Value;
            RuleRow(window, 0, "Impostors (2 needs 5+ players)", MatchRules.ImpostorLabel(rules.Impostors), host, () => { rules.Impostors = Next(rules.Impostors, MatchRules.ImpostorOptions); ApplyRules(rules); });
            RuleRow(window, 1, "Discussion time", rules.DiscussSeconds + " s", host, () => { rules.DiscussSeconds = Next(rules.DiscussSeconds, MatchRules.DiscussOptions); ApplyRules(rules); });
            RuleRow(window, 2, "Voting time", rules.VoteSeconds + " s", host, () => { rules.VoteSeconds = Next(rules.VoteSeconds, MatchRules.VoteOptions); ApplyRules(rules); });
            RuleRow(window, 3, "Knock-down cooldown", rules.KnockCooldown + " s", host, () => { rules.KnockCooldown = Next(rules.KnockCooldown, MatchRules.CooldownOptions); ApplyRules(rules); });
            RuleRow(window, 4, "Round length", rules.RoundMinutes + " min", host, () => { rules.RoundMinutes = Next(rules.RoundMinutes, MatchRules.RoundOptions); ApplyRules(rules); });

            if (host)
            {
                SafetyUI.AddLabel(window, "PLAYERS", new Vector2(.06f, .36f), new Vector2(.94f, .41f), 16, font, FontStyle.Bold, TextAnchor.MiddleLeft);
                int row = 0;
                foreach (ulong clientId in manager.ConnectedClientsIds)
                {
                    if (clientId == NetworkManager.ServerClientId || row >= 7) continue;
                    PlayerController p = game.GetPlayer(clientId);
                    string name = p != null ? p.DisplayName.Value.ToString() : "Player " + (clientId + 1);
                    float column = row % 2 == 0 ? .06f : .51f, top = .35f - (row / 2) * .065f;
                    ulong id = clientId;
                    SafetyUI.AddLabel(window, name, new Vector2(column, top - .065f), new Vector2(column + .27f, top), 15, font, FontStyle.Normal, TextAnchor.MiddleLeft);
                    SafetyUI.AddButton(window, "Remove " + name, "REMOVE", new Vector2(column + .28f, top - .065f), new Vector2(column + .43f, top), PixelUiTheme.Coral,
                        () => { game.RemovePlayerFromHost(id); SetNotice(name + " was removed from the room."); CloseModal(); Invoke(nameof(OpenRoomSettings), .4f); }, font, 13);
                    row++;
                }
                if (row == 0) SafetyUI.AddLabel(window, "No one else has joined yet.", new Vector2(.06f, .29f), new Vector2(.94f, .35f), 15, font, FontStyle.Normal, TextAnchor.MiddleLeft);
            }
            SafetyUI.AddButton(window, "Close Room Settings", "DONE", new Vector2(.62f, .04f), new Vector2(.94f, .12f), PixelUiTheme.Amber, CloseModal, font, 17);
            Typography.Apply(window);
        }

        void RuleRow(Transform window, int index, string label, string value, bool editable, Action change)
        {
            Font font = PixelUiTheme.BodyFont;
            float top = .8f - index * .075f;
            SafetyUI.AddLabel(window, label, new Vector2(.06f, top - .065f), new Vector2(.6f, top), 17, font, FontStyle.Normal, TextAnchor.MiddleLeft);
            if (editable) SafetyUI.AddButton(window, "Rule " + label, value, new Vector2(.62f, top - .065f), new Vector2(.94f, top), PixelUiTheme.Linen, change, font, 16);
            else SafetyUI.AddLabel(window, value, new Vector2(.62f, top - .065f), new Vector2(.94f, top), 17, font, FontStyle.Bold, TextAnchor.MiddleRight);
        }

        static byte Next(byte value, byte[] options) => options[(Array.IndexOf(options, value) + 1) % options.Length];

        void ApplyRules(MatchRules rules)
        {
            GameManager game = GameManager.Instance;
            if (game != null && game.SetRulesFromHost(rules)) OpenRoomSettings();
        }

        void CloseModal()
        {
            if (openModal != null) Destroy(openModal);
            openModal = null;
        }

        void OpenSettings()
        {
            if (openModal != null) return;
            GameAudioManager.PlayUiClick();
            SettingsUI view = null;
            view = SettingsUI.Show(canvas.transform, () => { if (view != null) Destroy(view.gameObject); openModal = null; },
                PlayerPrefs.GetInt(GameSettings.LargeTextKey, 0) == 1, PlayerPrefs.GetInt(GameSettings.HighContrastKey, 0) == 1, PlayerPrefs.GetInt(GameSettings.ReduceEffectsKey, 0) == 1);
            openModal = view.gameObject;
        }

        async void Disconnect()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) return;
            GameAudioManager.PlayUiClick();
            leavingOnPurpose = true;
            // In an online room, leaving the room stops the network itself; shutting Netcode down directly as well
            // confuses the room (and the next room this player joins). LAN games shut down here.
            bool online = OnlineRooms.Code != null;
            if (online) ShowLoading("LEAVING", "Leaving the room");
            await OnlineRooms.LeaveAsync();
            HideLoading();
            if (manager.IsListening && !online) manager.Shutdown();
            SetNotice("Disconnected from the match.");
        }

        async void CreateOnlineRoomClicked()
        {
            if (!BeginOnlineConnect("Creating an online room…")) return;
            try
            {
                string code = await OnlineRooms.HostAsync();
                GUIUtility.systemCopyBuffer = code;
                SetNotice("Room " + OnlineRooms.DisplayCode + " is open. The code is copied: share it with friends.");
            }
            catch (Exception e) { Debug.LogWarning("Online room could not be created: " + e); ConnectFailed("COULDN'T CREATE ROOM", OnlineRooms.Describe(e)); }
        }

        async void QuickJoinClicked()
        {
            if (!BeginOnlineConnect("Looking for a public room…")) return;
            try
            {
                await OnlineRooms.QuickJoinAsync();
                SetNotice(NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost
                    ? "No open rooms right now, so you opened a public one. Players who Quick Join will land here."
                    : "Joined a public room. Say hi!");
            }
            catch (Exception e) { Debug.LogWarning("Quick Join failed: " + e); ConnectFailed("QUICK JOIN FAILED", OnlineRooms.Describe(e)); }
        }

        async void JoinOnlineRoomClicked()
        {
            string code = roomCodeInput != null ? roomCodeInput.text.Trim() : string.Empty;
            if (code.Length < 4) { SetNotice("Type the room code your friend shared."); return; }
            if (!BeginOnlineConnect("Joining room " + code.ToUpperInvariant() + "…")) return;
            try { await OnlineRooms.JoinAsync(code); }
            catch (Exception e) { Debug.LogWarning("Online room could not be joined: " + e); ConnectFailed("COULDN'T JOIN ROOM", OnlineRooms.Describe(e)); }
        }

        // ---- Loading window and connection errors ----

        /// <summary>Blocks input with a small animated window so a slow connection never looks like a frozen game.</summary>
        void ShowLoading(string title, string message)
        {
            HideLoading();
            Font font = PixelUiTheme.BodyFont;
            loadingModal = SafetyUI.CreateModal(canvas.transform, "Loading", new Vector2(640f, 260f));
            Transform window = loadingModal.transform.Find("Safety Window");
            SafetyUI.AddLabel(window, title, new Vector2(.08f, .62f), new Vector2(.92f, .88f), 21, font, FontStyle.Bold, TextAnchor.MiddleCenter);
            loadingText = SafetyUI.AddLabel(window, message, new Vector2(.06f, .12f), new Vector2(.94f, .58f), 18, font, FontStyle.Normal, TextAnchor.MiddleCenter);
            loadingMessage = message;
            loadingStarted = Time.unscaledTime;
            Typography.Apply(window);
            loadingModal.transform.SetAsLastSibling();
        }

        void HideLoading()
        {
            if (loadingModal != null) Destroy(loadingModal);
            loadingModal = null;
            loadingText = null;
        }

        void UpdateLoading(NetworkManager network)
        {
            if (loadingModal == null) return;
            // Done once this player is actually in: the host is listening, or the client has been accepted.
            if (network != null && (network.IsHost || network.IsConnectedClient) && !OnlineRooms.Busy) { HideLoading(); lanJoinAddress = null; return; }
            float elapsed = Time.unscaledTime - loadingStarted;
            string dots = new string('.', 1 + (int)(elapsed * 2.5f) % 3);
            loadingText.text = loadingMessage + dots + (elapsed > 8f ? "\nStill working. This can take up to 20 seconds." : "");
        }

        /// <summary>A connection attempt failed: close the loading window, return to the menu and say why in a pop-up.</summary>
        void ConnectFailed(string title, string message)
        {
            HideLoading();
            lanJoinAddress = null;
            SetNotice(message);
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening) manager.Shutdown();
            observedNetworkSession = false;
            CronyRuntime.ResumeLocalMenu(message, title);
        }

        bool BeginOnlineConnect(string notice)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null) { SetNotice("NetworkManager is not present in this scene."); return false; }
            if (OnlineRooms.Busy || manager.IsListening) return false;
            SavePlayerNamePreference();
            GameAudioManager.PlayUiClick();
            if (GameManager.Instance != null) GameManager.Instance.InstallConnectionApproval();
            CronyRuntime.PrepareForNetworkGame();
            SetNotice(notice);
            ShowLoading("CONNECTING", notice.TrimEnd('…', '.'));
            return true;
        }

        void ConfigureAddress(NetworkManager manager, string connectAddress)
        {
            UnityTransport transport = manager.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport == null) return;
            // Same-Wi-Fi games connect directly over UDP; only online rooms use WebSockets.
            transport.UseWebSockets = false;
            // A host on the same Wi-Fi answers within a second; give up after about 12 seconds instead of a minute.
            transport.MaxConnectAttempts = 12;
            transport.SetConnectionData(connectAddress, 7777, "0.0.0.0");
        }
    }
}
