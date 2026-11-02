using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Reflection;
using UnityEngine.Rendering.Universal;

namespace Crony
{
    public enum RoundPhase { Menu, Playing, Meeting, Results }
    /// <summary>A Crony is a crewmate the Impostor recruited mid-match: they win or lose with the Impostor.</summary>
    public enum PlayerRole { Crewmate, Impostor, Crony }

    [Serializable]
    public sealed class TaskStation
    {
        public string title;
        public string location;
        public string instruction;
        public Vector2 position;
        public string[] actions;
        public int[] solution;
        public int step;
        public bool complete;
        public SpriteRenderer marker;

        public TaskStation(string title, string location, string instruction, Vector2 position, string[] actions, int[] solution)
        {
            this.title = title;
            this.location = location;
            this.instruction = instruction;
            this.position = position;
            this.actions = actions;
            this.solution = solution;
        }
    }

    public sealed class MatchPlayer
    {
        public int id;
        public string name;
        public bool isHuman;
        public PlayerRole role;
        public bool alive = true;
        public bool downed;
        public bool blocked;
        public bool muted;
        public float downedTimer;
        public float taskTimer;
        public float suspicion;
        /// <summary>Index of the checkpoint this player last finished (or faked), for meeting alibis; -1 before any.</summary>
        public int lastTask = -1;
        public int nextTask;
        public bool botFinishedTask;
        public List<Vector2> route;
        public int routeStep;
        public Vector2 routeGoal;
        public float routeRetry;
        public Vector2 position;
        public Color outfitColor;
        public GameObject root;
        public PlayerAvatarLayerManager avatarLayers;
        public SpriteRenderer body;
        public SpriteRenderer hat;
        public TextMesh nameTag;
    }

    /// <summary>Runtime bootstraps and hosts a complete local practice round with text-only bot play.</summary>
    public sealed class CronyRuntime : MonoBehaviour
    {
        const int PlayerLimit = 8;
        const float InteractRange = 1.8f;
        const float MatchLength = 480f;
        const float MapScaleX = 1.5f;
        const float MapScaleY = 1.35f;

        static CronyRuntime instance;
        public static RoundPhase CommunicationPhase => instance != null ? instance.phase : RoundPhase.Menu;
        public static IReadOnlyList<MatchPlayer> CommunicationPlayers => instance != null ? instance.players : null;
        public static bool CommunicationAlive => instance == null || instance.human == null || (instance.human.alive && !instance.human.downed);

        public static bool ShowNetworkSetup => instance == null || instance.activeOverlay == "network";
        bool IsLocalPaused => activeOverlay == "pause" || activeOverlay == "safety" || activeOverlay == "settings";
        /// <summary>True on the offline title menus (main menu, practice lobby, online setup), before any round or session.</summary>
        public static bool InTitleMenus => instance != null && instance.isActiveAndEnabled && instance.phase == RoundPhase.Menu;
        public static bool CanOpenAvatarCustomization => instance == null ||
            (instance.isActiveAndEnabled && instance.phase == RoundPhase.Menu);
        readonly List<TaskStation> tasks = new List<TaskStation>();
        readonly List<MatchPlayer> players = new List<MatchPlayer>();
        readonly List<string> evidence = new List<string>();
        readonly HashSet<int> reportedPlayers = new HashSet<int>();
        readonly Rect[] mapBlockers =
        {
            new Rect(-13.7f, 5.45f, 4.0f, 2.5f),
            new Rect(6.85f, 5.45f, 4.5f, 2.5f),
            new Rect(7.3f, -2.0f, 3.8f, 2.2f),
            new Rect(-12.25f, -8.5f, 3.5f, 2.4f),
            new Rect(-2.1f, 6.4f, 3.8f, 1.6f),
            new Rect(3.6f, -7.85f, 2.4f, 1.3f)
        };

        TownshipWorldMap worldMap;
        int trackedTask = -1;
        int mapOpenedFrame = -1;
        RectTransform mapPlayerMarker;
        Text trackedTaskLabel;
        CameraFollow2D sceneCameraFollow;
        bool sceneCameraFollowWasEnabled;
        Camera gameCamera;
        Vector3 cameraVelocity;
        Canvas canvas;
        RectTransform overlayRoot;
        RectTransform hudRoot;
        Image fogImage;
        Image fogMask;
        Texture2D fogTexture;
        Sprite fogSprite;
        Transform worldRoot;
        Transform playerRoot;
        Sprite squareSprite;
        Sprite circleSprite;
        Font uiFont;
        RoundPhase phase = RoundPhase.Menu;
        PlayerRole humanRole = PlayerRole.Crewmate;
        PlayerRole chosenRole = PlayerRole.Crewmate;
        // One recruitment per match, unlocked after a short opening so the first minute stays a normal round.
        const float RecruitUnlockSeconds = 45f;
        bool recruitUsed;
        float stumpCooldown, footstepTimer;
        MatchPlayer cronyPartner;
        GameObject recruitButton;
        Text sabotageButtonLabel, knockDownButtonLabel;
        MatchPlayer human;
        TaskStation activeTask;
        string activeOverlay = "";
        string toast = "";
        float toastTimer;
        float matchTimer;
        float sabotageTimer;
        float sabotageCooldown;
        float killCooldown;
        float meetingCooldown;
        float botClock;
        float hudClock;
        float botMeetingClock;
        float touchMoveX;
        float touchMoveY;
        bool emergencyUsed;
        bool blackout;
        bool largeText;
        bool highContrast;
        bool reducedEffects = true;
        int customSkin;
        int customOutfit;
        int customAccessory;
        int currentSabotage = -1;
        int meetingBody = -1;
        int humanVote = -2;
        string latestEvent = "";
        Color[] skinColors = { Hex("F0C8A7"), Hex("D9A27E"), Hex("A96D4D"), Hex("7F503D"), Hex("5A382F") };
        Color[] outfitColors = { Hex("67CBB4"), Hex("F18AA0"), Hex("79BCE3"), Hex("F0C966"), Hex("B28CE2"), Hex("F1DED0") };
        readonly string[] outfitNames = { "Daisy Cardigan", "Berry Overalls", "Cloud Hoodie", "Star Sweater", "Garden Apron", "Sunny Tee" };
        readonly string[] accessoryNames = { "None", "Flower Crown", "Big Bow", "Cat Ears", "Party Hat", "Beret" };

        Text hudStatus;
        Text roleLabel;
        Text taskListLabel;
        Text interactLabel;
        Text toastLabel;
        GameObject toastBackplate;
        const float DiscussSeconds = 15f, VoteSeconds = 30f, RevealSeconds = 5f, EjectSeconds = 4.5f;
        MeetingStage meetingStage;
        float stageTimer, stageLength;
        bool meetingBodyReport;
        string meetingHeadline = "", meetingOutcome = "", meetingEjectedRole = "";
        int meetingEjected = -1;
        readonly Dictionary<int, float> accusations = new Dictionary<int, float>();
        readonly Dictionary<int, int> accusedBy = new Dictionary<int, int>();
        readonly HashSet<int> humanAccused = new HashSet<int>();
        readonly Dictionary<int, int> meetingVotes = new Dictionary<int, int>();
        readonly Dictionary<int, float> botVoteTimes = new Dictionary<int, float>();
        readonly List<BotLine> botLines = new List<BotLine>();
        readonly BotMemoryManager botMemory = new BotMemoryManager();
        readonly BotDiscussionEngine discussion = new BotDiscussionEngine();
        readonly DiscussionContext meetingContext = new DiscussionContext();
        // Bots answer each accusation at most once a meeting, so arguments don't loop.
        readonly HashSet<int> reactedThisMeeting = new HashSet<int>();
        readonly List<Vector2Int> pendingReactions = new List<Vector2Int>();
        int meetingReporter = -1;
        float evidenceClock;
        string lastRepairPlace;
        static string[] stationTitles;
        float RoundClock => MatchLength - matchTimer;
        readonly MeetingView meetingView = new MeetingView();
        Vector2[] spawnPoints;
        sealed class BotLine { public float At; public MatchPlayer Who; public string Text; public int Accuse = -1; public int Defends = -1; public float Weight; public bool Said; }
        Text taskProgressLabel;
        Image taskProgressFill;
        readonly List<GameObject> temporaryUi = new List<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<CronyRuntime>() == null)
                new GameObject("Crony Game").AddComponent<CronyRuntime>();
        }

        /// <summary>Leaves the procedural township map in place and hands player control to the NGO session.</summary>
        public static void PrepareForNetworkGame()
        {
            if (instance == null) return;
            instance.StopPlayers();
            if (TownshipTaskMiniGameUI.IsOpen) TownshipTaskMiniGameUI.Close();
            instance.activeTask = null;
            instance.activeOverlay = string.Empty;
            instance.phase = RoundPhase.Menu;
            instance.DestroyOverlay();
            if (instance.canvas != null) instance.canvas.gameObject.SetActive(false);
            if (instance.hudRoot != null) instance.hudRoot.gameObject.SetActive(false);
            instance.SetFogVisible(false);
            if (instance.worldRoot != null) instance.worldRoot.gameObject.SetActive(false);
            if (instance.sceneCameraFollow != null) instance.sceneCameraFollow.enabled = instance.sceneCameraFollowWasEnabled;
            instance.enabled = false;
        }

        /// <summary>Why the last online session ended, shown once on the main menu (e.g. a version mismatch).</summary>
        public static void ResumeLocalMenu(string reason = null, string title = "DISCONNECTED")
        {
            if (instance == null) return;
            instance.enabled = true;
            if (instance.worldRoot != null) instance.worldRoot.gameObject.SetActive(true);
            if (instance.sceneCameraFollow != null) instance.sceneCameraFollow.enabled = false;
            if (instance.canvas != null) instance.canvas.gameObject.SetActive(true);
            if (instance.hudRoot != null) instance.hudRoot.gameObject.SetActive(true);
            instance.SetFogVisible(false);
            instance.ShowMainMenu();
            if (!string.IsNullOrWhiteSpace(reason)) instance.ShowMenuMessage(title, reason);
        }

        void ShowMenuMessage(string title, string message)
        {
            GameObject modal = SafetyUI.CreateModal(overlayRoot, title, new Vector2(720f, 360f));
            Transform window = modal.transform.Find("Safety Window");
            Font font = PixelUiTheme.BodyFont;
            SafetyUI.AddLabel(window, title, new Vector2(.07f, .72f), new Vector2(.93f, .9f), 21, font, FontStyle.Bold, TextAnchor.MiddleLeft);
            SafetyUI.AddLabel(window, message, new Vector2(.07f, .3f), new Vector2(.93f, .7f), 18, font, FontStyle.Normal, TextAnchor.UpperLeft);
            SafetyUI.AddButton(window, "Close Message", "OK", new Vector2(.62f, .08f), new Vector2(.93f, .24f), PixelUiTheme.Amber, () => Destroy(modal), font, 17);
            Typography.Apply(window);
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            uiFont = PixelUiTheme.BodyFont;
            largeText = PlayerPrefs.GetInt("Crony.LargeText", 0) == 1;
            highContrast = PlayerPrefs.GetInt("Crony.HighContrast", 0) == 1;
            reducedEffects = PlayerPrefs.GetInt("Crony.ReduceEffects", 1) == 1;
            UIButtonJuice.SetReducedMotion(reducedEffects);
            squareSprite = MakeSquareSprite();
            circleSprite = MakeCircleSprite();
            SetupCamera();
            SetupWorld();
            SetupUI();
            ShowMainMenu();
        }

        void SetupCamera()
        {
            gameCamera = Camera.main;
            if (gameCamera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                gameCamera = cameraObject.AddComponent<Camera>();
            }
            sceneCameraFollow = gameCamera.GetComponent<CameraFollow2D>();
            if (sceneCameraFollow != null)
            {
                sceneCameraFollowWasEnabled = sceneCameraFollow.enabled;
                sceneCameraFollow.enabled = false;
            }
            CameraFollow2D.ConfigurePixelCamera(gameCamera);
            gameCamera.orthographic = true;
            CameraFollow2D.ApplyGameplayZoom(gameCamera);
            gameCamera.transform.position = new Vector3(0f, 0f, -10f);
            gameCamera.backgroundColor = Hex("94D4B1");
            gameCamera.clearFlags = CameraClearFlags.SolidColor;
            if (gameCamera.GetComponent<AudioListener>() == null) gameCamera.gameObject.AddComponent<AudioListener>();
        }

        static Vector2 MapToWorld(Vector2 point)
        {
            return new Vector2(point.x * MapScaleX, point.y * MapScaleY);
        }

        static Vector2 WorldToMap(Vector2 point) => new Vector2(point.x / MapScaleX, point.y / MapScaleY);

        static Vector2 MapToWorldSize(Vector2 size)
        {
            return new Vector2(size.x * MapScaleX, size.y * MapScaleY);
        }

        void SetupWorld()
        {
            GameObject root = new GameObject("Township Park World");
            worldRoot = root.transform;
            GameObject authoredMap = GameObject.Find("Pixel Township Map");
            if (authoredMap != null)
            {
                worldMap = new TownshipWorldMap(authoredMap);
                CreateTasks();
                playerRoot = new GameObject("Players").transform;
                playerRoot.SetParent(worldRoot, false);
                return;
            }
            CreateRect("Park Grass", Vector2.zero, new Vector2(32f, 22f), Hex("91C995"), -100);
            CreateRect("North Walk", new Vector2(0f, 1.7f), new Vector2(31f, 1.55f), Hex("E9D8B3"), -80);
            CreateRect("South Walk", new Vector2(0f, -5.1f), new Vector2(31f, 1.1f), Hex("E9D8B3"), -80);
            CreateRect("Central Walk", new Vector2(0f, -1.4f), new Vector2(1.2f, 14f), Hex("E9D8B3"), -80);
            CreateRect("Market Walk", new Vector2(-6.1f, 5.2f), new Vector2(14f, .8f), Hex("E9D8B3"), -80);
            CreateEllipse("Community Lake", new Vector2(0f, -2.4f), new Vector2(7.1f, 3.1f), Hex("63B9CC"), -60);
            CreateEllipse("Lake Rim", new Vector2(0f, -2.4f), new Vector2(7.5f, 3.5f), Hex("D6C99D"), -61);
            CreateEllipse("Island", new Vector2(.25f, -2.1f), new Vector2(1.25f, .55f), Hex("77B67B"), -50);
            CreateRect("Park Boundary North", new Vector2(0f, 10.2f), new Vector2(32f, .35f), Hex("426A55"), -30, true);
            CreateRect("Park Boundary South", new Vector2(0f, -10.2f), new Vector2(32f, .35f), Hex("426A55"), -30, true);
            CreateRect("Park Boundary East", new Vector2(15.8f, 0f), new Vector2(.35f, 20.5f), Hex("426A55"), -30, true);
            CreateRect("Park Boundary West", new Vector2(-15.8f, 0f), new Vector2(.35f, 20.5f), Hex("426A55"), -30, true);
            CreateDecorations();
            CreateTasks();
            playerRoot = new GameObject("Players").transform;
            playerRoot.SetParent(worldRoot, false);
            BoxCollider2D ground = worldRoot.gameObject.AddComponent<BoxCollider2D>();
            ground.size = new Vector2(32f * MapScaleX, 22f * MapScaleY);
            ground.isTrigger = true;
            SurfaceAudioMarker groundSurface = worldRoot.gameObject.AddComponent<SurfaceAudioMarker>();
            groundSurface.SetSurface(SurfaceType.Grass);
        }

        void CreateDecorations()
        {
            CreateBuilding("Hawker Stalls", new Vector2(-11.7f, 6.7f), new Vector2(4.0f, 2.5f), Hex("EE9A62"), "NASI LEMAK");
            CreateBuilding("Convenience Store", new Vector2(9.1f, 6.7f), new Vector2(4.5f, 2.5f), Hex("76B7D4"), "KEDAI");
            CreateBuilding("Community Hall", new Vector2(-.2f, 7.2f), new Vector2(3.8f, 1.6f), Hex("DBA765"), "DEWAN");
            CreateBuilding("Administrative Office", new Vector2(9.2f, -.9f), new Vector2(3.8f, 2.2f), Hex("D8A7B2"), "COMMUNITY");
            CreateBuilding("Cellular Tower", new Vector2(-10.5f, -7.3f), new Vector2(3.5f, 2.4f), Hex("9297C8"), "TOWER");
            CreateBuilding("Bus Stop", new Vector2(11f, -7.3f), new Vector2(3.4f, 2.2f), Hex("E0BF64"), "BUS STOP");
            CreateRect("Playground Mat", new Vector2(-11.7f, -.4f), new Vector2(3.4f, 2.2f), Hex("E7C772"), -42);
            CreateRect("Badminton Court", new Vector2(-.2f, 8.3f), new Vector2(4.2f, .45f), Hex("AAD7CE"), -40);
            CreateRect("Community Garden", new Vector2(4.8f, -7.3f), new Vector2(2.2f, 1.7f), Hex("A8CA8A"), -41);
            CreateEllipse("Flower Bed 1", new Vector2(-6.4f, 3.3f), new Vector2(1.4f, .7f), Hex("E98490"), -30);
            CreateEllipse("Flower Bed 2", new Vector2(6.3f, 2.8f), new Vector2(1.6f, .8f), Hex("E8BB6D"), -30);
            CreateEllipse("Flower Bed 3", new Vector2(-8.7f, -3.7f), new Vector2(1.3f, .7f), Hex("B38ED0"), -30);
            CreateRect("Notice Board Back", new Vector2(6.6f, -5.0f), new Vector2(1.4f, .22f), Hex("9E6645"), -10);
            for (int i = 0; i < 11; i++)
            {
                float x = -13.3f + i * 2.65f;
                CreateEllipse("Tree Crown " + i, new Vector2(x, i % 2 == 0 ? 9.4f : -9.4f), new Vector2(1.0f, .8f), i % 2 == 0 ? Hex("558A63") : Hex("5A9066"), -25);
            }
        }

        void CreateBuilding(string name, Vector2 position, Vector2 size, Color color, string sign)
        {
            CreateRect(name + " Shadow", position + new Vector2(.15f, -.2f), size, Hex("557B68"), -48);
            CreateRect(name, position, size, color, -45, true);
            CreateRect(name + " Roof", position + new Vector2(0f, size.y * .34f), new Vector2(size.x * 1.03f, .35f), Color.Lerp(color, Color.white, .35f), -44);
            CreateWorldLabel(sign, position + new Vector2(0f, .18f), .28f, Hex("293C45"), -5);
            CreateRect(name + " Door", position + new Vector2(0f, -size.y * .31f), new Vector2(.6f, .65f), Hex("805E4A"), -43);
        }

        void CreateTasks()
        {
            tasks.Clear();
            tasks.Add(new TaskStation("Sort & Pack Nasi Lemak", "Hawker Stalls", "Drag sambal, boiled egg, cucumber, and anchovies onto the banana leaf in order.", new Vector2(-8.9f, 5.6f), new[] { "Sambal", "Boiled egg", "Cucumber", "Anchovies" }, new[] { 0, 1, 2, 3 }));
            tasks.Add(new TaskStation("Restock Ice Cream", "Convenience Store", "Match each freezer slot to the flavor card.", new Vector2(6.8f, 5.5f), new[] { "Pandan", "Mango", "Red Bean", "Coconut" }, new[] { 2, 0, 3, 1 }));
            tasks.Add(new TaskStation("Fix Park Bench", "Recreation Park", "Rotate the wrench to align both slats and tighten each bolt before time runs out.", new Vector2(-9.2f, .1f), new[] { "Align first slat", "Tighten first bolt", "Align second slat", "Tighten second bolt" }, new[] { 0, 1, 2, 3 }));
            tasks.Add(new TaskStation("Calibrate Badminton Net", "Community Hall", "Slide the tension control until its marker meets the height gauge line.", new Vector2(-.3f, 5.4f), new[] { "Calibrate tension" }, new[] { 0 }));
            tasks.Add(new TaskStation("File Community Permits", "Administrative Office", "File event, facility, and commercial permits in matching binders.", new Vector2(6.5f, -.8f), new[] { "Event Permit", "Facility Permit", "Commercial Permit" }, new[] { 0, 1, 2 }));
            tasks.Add(new TaskStation("Update Bus Schedule", "Bus Stop", "Sort the time tiles into chronological order.", new Vector2(8.9f, -6.2f), new[] { "08:30 AM", "10:15 AM", "07:45 AM", "09:30 AM" }, new[] { 2, 0, 3, 1 }));
            tasks.Add(new TaskStation("Clear the Lake", "Central Lake", "Sort bottles, cans, and water hyacinths into their bins.", new Vector2(3.1f, -2.1f), new[] { "Plastic Bottle", "Aluminum Can", "Water Hyacinths" }, new[] { 0, 1, 2 }));
            tasks.Add(new TaskStation("Reset Wi-Fi Router", "Cellular Tower", "Toggle circuit breakers in ascending order.", new Vector2(-8.1f, -6.1f), new[] { "Circuit Breaker 1", "Circuit Breaker 2", "Circuit Breaker 3", "Circuit Breaker 4", "Circuit Breaker 5" }, new[] { 0, 1, 2, 3, 4 }));
            tasks.Add(new TaskStation("Replace Solar Battery", "Garden Walkway", "Unscrew the panel, remove the drained cell, and fit a charged battery.", new Vector2(2.2f, -7.7f), new[] { "Unscrew compartment", "Remove drained battery", "Install charged battery" }, new[] { 0, 1, 2 }));
            tasks.Add(new TaskStation("Pin Announcements", "Notice Board", "Align and pin three community flyers.", new Vector2(6.7f, -4.0f), new[] { "Park Cleanup Day", "Badminton Signup", "Lake Safety Notice" }, new[] { 0, 1, 2 }));

            if (worldMap != null)
            {
                foreach (TownshipTaskStation station in FindObjectsByType<TownshipTaskStation>())
                {
                    int index = (int)station.Kind;
                    if (index < 0 || index >= tasks.Count) continue;
                    tasks[index].position = WorldToMap(station.transform.position);
                    Transform marker = station.transform.Find("Pixel Task Marker");
                    if (marker != null) tasks[index].marker = marker.GetComponent<SpriteRenderer>();
                }
                return;
            }

            Color[] taskColors = { Hex("E5A15E"), Hex("77B8D7"), Hex("B58D6B"), Hex("62A8A1"), Hex("DB8A99"), Hex("E0BD60"), Hex("4B9FA8"), Hex("7D88BB"), Hex("83AF78"), Hex("B8896B") };
            string[] emblems = { "N", "I", "B", "H", "F", "B", "L", "W", "S", "A" };
            for (int i = 0; i < tasks.Count; i++)
            {
                TaskStation task = tasks[i];
                GameObject marker = CreateEllipse("Task " + (i + 1), task.position, new Vector2(1.05f, 1.05f), Hex("FFF3D9"), -7);
                task.marker = marker.GetComponent<SpriteRenderer>();
                task.marker.color = Hex("FFF3D9");
                CreateEllipse("Task Emblem " + i, task.position, new Vector2(.72f, .72f), taskColors[i], -6);
                CreateWorldLabel(emblems[i], task.position, .42f, Color.white, 1);
                CreateWorldLabel(task.location, task.position + new Vector2(0f, -.86f), .19f, Hex("293B42"), 4);
                task.marker.gameObject.GetComponent<SpriteRenderer>().sortingOrder = -7;
            }
        }

        GameObject CreateRect(string name, Vector2 position, Vector2 size, Color color, int order, bool block = false)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(worldRoot, false);
            Vector2 worldPosition = MapToWorld(position);
            Vector2 worldSize = MapToWorldSize(size);
            go.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
            go.transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = squareSprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            if (block)
            {
                go.AddComponent<BoxCollider2D>();
                AddRuntimeShadowCaster(go);
            }
            if (name.Contains("Walk") || name.Contains("Path"))
            {
                BoxCollider2D surfaceCollider = go.AddComponent<BoxCollider2D>();
                surfaceCollider.isTrigger = true;
                go.AddComponent<SurfaceAudioMarker>().SetSurface(SurfaceType.Concrete);
            }
            return go;
        }

        static void AddRuntimeShadowCaster(GameObject go)
        {
            // Runtime-created map geometry has no serialized shadow path, so give URP's caster an explicit rectangle.
            bool wasActive = go.activeSelf;
            go.SetActive(false);
            ShadowCaster2D caster = go.AddComponent<ShadowCaster2D>();
            Vector3[] path =
            {
                new Vector3(-.5f, -.5f, 0f), new Vector3(-.5f, .5f, 0f),
                new Vector3(.5f, .5f, 0f), new Vector3(.5f, -.5f, 0f)
            };
            typeof(ShadowCaster2D).GetField("m_ShapePath", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(caster, path);
            typeof(ShadowCaster2D).GetField("m_ShapePathHash", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(caster, path.GetHashCode());
            FieldInfo sourceField = typeof(ShadowCaster2D).GetField("m_ShadowCastingSource", BindingFlags.Instance | BindingFlags.NonPublic);
            if (sourceField != null) sourceField.SetValue(caster, System.Enum.ToObject(sourceField.FieldType, 1));
            go.SetActive(wasActive);
        }

        GameObject CreateEllipse(string name, Vector2 position, Vector2 size, Color color, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(worldRoot, false);
            Vector2 worldPosition = MapToWorld(position);
            Vector2 worldSize = MapToWorldSize(size);
            go.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
            go.transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = circleSprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return go;
        }

        TextMesh CreateWorldLabel(string value, Vector2 position, float size, Color color, int order)
        {
            GameObject go = new GameObject("Label - " + value);
            go.transform.SetParent(worldRoot, false);
            Vector2 worldPosition = MapToWorld(position);
            go.transform.position = new Vector3(worldPosition.x, worldPosition.y, -.1f);
            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.text = value;
            mesh.font = uiFont;
            mesh.fontSize = 64;
            mesh.characterSize = size * ((MapScaleX + MapScaleY) * .5f) / 3.5f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            mesh.richText = false;
            mesh.GetComponent<MeshRenderer>().sortingOrder = order;
            return mesh;
        }

        void SetupUI()
        {
            GameObject canvasObject = new GameObject("Game UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            PixelUiTheme.ConfigureCanvas(scaler);
            hudRoot = CreateUiRoot("HUD Root", canvasObject.transform);
            // Modals must render above the HUD so their buttons and text stay unobstructed.
            overlayRoot = CreateUiRoot("Overlay Root", canvasObject.transform);
            CreateFog();
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) eventSystem = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null) eventSystem = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                BaseInputModule[] oldModules = eventSystem.GetComponents<BaseInputModule>();
                foreach (BaseInputModule oldModule in oldModules) Destroy(oldModule);
                inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            inputModule.AssignDefaultActions();
        }

        RectTransform CreateUiRoot(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        void CreateFog()
        {
            fogTexture = new Texture2D(256, 144, TextureFormat.RGBA32, false);
            fogTexture.wrapMode = TextureWrapMode.Clamp;
            fogTexture.filterMode = FilterMode.Bilinear;
            fogSprite = Sprite.Create(fogTexture, new Rect(0, 0, 256, 144), new Vector2(.5f, .5f));
            GameObject fog = new GameObject("Vision Falloff", typeof(RectTransform), typeof(Image));
            fog.transform.SetParent(canvas.transform, false);
            fog.transform.SetAsFirstSibling();
            RectTransform rect = fog.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            fogImage = fog.GetComponent<Image>();
            fogImage.sprite = fogSprite;
            fogImage.raycastTarget = false;
            fogMask = fogImage;
            UpdateVisionMask();
        }

        static Sprite MakeSquareSprite()
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;
            return Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 2f);
        }

        static Sprite MakeCircleSprite()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f - size * .5f) / (size * .5f);
                    float dy = (y + .5f - size * .5f) / (size * .5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * size * .5f)));
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
        }

        void ShowMainMenu()
        {
            activeOverlay = "";
            phase = RoundPhase.Menu;
            if (gameCamera != null) gameCamera.transform.position = new Vector3(0f, 0f, -10f);
            StopPlayers();
            blackout = false;
            currentSabotage = -1;
            hudRoot.gameObject.SetActive(false);
            SetFogVisible(false);
            DestroyOverlay();
            GameAudioManager.SetMusic(MusicMood.Menu);
            Vector2 middle = new Vector2(.5f, .5f);
            // The park stays visible behind a light shade; a scrim fading in from the left keeps the logo and copy legible.
            GameObject backdrop = MakePanel(overlayRoot, "Menu Backdrop", new Color(.03f, .08f, .09f, .34f), Vector2.zero, Vector2.zero, true);
            GameObject band = MakePanel(overlayRoot, "Menu Title Band", new Color(.08f, .11f, .16f, 1f), middle, middle, false, new Vector2(1100f, 1080f), new Vector2(-410f, 0f));
            band.GetComponent<Image>().sprite = PixelUiTheme.GetScrimSprite();
            band.GetComponent<Image>().raycastTarget = false;

            // Left: identity.
            Transform left = band.transform;
            MakeLabel(left, "T O W N S H I P   P A R K", new Vector2(0f, 250f), new Vector2(640f, 32f), 18, PixelUiTheme.Gold, FontStyle.Bold);
            PixelLogo.Create(left, 14, new Vector2(0f, 135f));
            Lift(MakeLabel(left, "A peaceful park. A hidden impostor.\nTrust carefully.", new Vector2(0f, -25f), new Vector2(660f, 84f), largeText ? 32 : 30, PixelUiTheme.Cream, FontStyle.Bold));
            Lift(MakeLabel(left, "Explore the township, finish community tasks and talk it out in meetings. Someone in the park is not who they seem.",
                new Vector2(0f, -115f), new Vector2(620f, 80f), largeText ? 21 : 19, PixelUiTheme.Linen));
            string[] chips = { "2-8 PLAYERS", "10 MINI-GAMES", "TEXT + VOICE" };
            for (int i = 0; i < chips.Length; i++)
            {
                GameObject chip = MakePanel(left, "Feature Chip", PixelUiTheme.Highlight, middle, middle, false, new Vector2(170f, 40f), new Vector2(-190f + i * 190f, -205f));
                chip.GetComponent<Image>().sprite = PixelUiTheme.GetRoundSprite();
                chip.GetComponent<Image>().type = Image.Type.Sliced;
                MakeLabel(chip.transform, chips[i], Vector2.zero, new Vector2(166f, 36f), 14, PixelUiTheme.Ink, FontStyle.Bold);
            }
            string savedName = PlayerPrefs.GetString("Crony.PlayerName", string.Empty);
            MakeLabel(left, (string.IsNullOrWhiteSpace(savedName) ? "Set your player name in Play Online" : "Playing as " + savedName) + "   ·   8-minute rounds",
                new Vector2(0f, -470f), new Vector2(660f, 28f), 16, PixelUiTheme.Linen);

            // Right: one window with the action stack, and the accessory settings tucked into its bottom corner.
            GameObject card = MakePanel(overlayRoot, "Menu Window", PixelUiTheme.Cream, middle, middle, false, new Vector2(560f, 900f), new Vector2(560f, 0f));
            PixelUiTheme.ApplyWindow(card, true);
            Transform right = card.transform;
            SectionLabel(right, "PLAY", 400f);
            AddMenuButton(right, "PLAY ONLINE", new Vector2(0f, 340f), ShowOnlinePlay, true, new Vector2(480f, 76f));
            Caption(right, Application.platform == RuntimePlatform.WebGLPlayer ? "Quick Join or play with friends using a room code" : "Join friends with a room code or on the same Wi-Fi", 288f);
            MenuButton(right, "PRACTICE WITH BOTS", 226f, ShowPrivateLobby, PixelUiTheme.Forest, new Vector2(480f, 68f));
            Caption(right, "Pick a role and learn the park against 7 bots", 179f);
            SectionLabel(right, "YOU", 120f);
            MenuButton(right, "CUSTOMIZE AVATAR  [C]", 66f, ShowCustomization, PixelUiTheme.Slate, new Vector2(480f, 58f));
            MenuButton(right, "HOW TO PLAY & CONTROLS", 0f, ShowHowToPlay, PixelUiTheme.Linen, new Vector2(480f, 58f));

            GameObject utilities = MakePanel(right, "Settings Audio and Chat", PixelUiTheme.Almond, middle, middle, false, new Vector2(500f, 230f), new Vector2(0f, -215f));
            PixelUiTheme.ApplyCard(utilities, false);
            Transform util = utilities.transform;
            MakeLabel(util, "SETTINGS, AUDIO & CHAT", new Vector2(0f, 88f), new Vector2(460f, 30f), 15, PixelUiTheme.MutedInk, FontStyle.Bold).alignment = TextAnchor.MiddleLeft;
            MenuButton(util, "SETTINGS", 34f, () => ShowSafetySettings(SettingsUI.Page.Display), PixelUiTheme.Linen, new Vector2(460f, 48f));
            GameObject audio = MenuButton(util, "AUDIO & VOICE", -24f, () => ShowSafetySettings(SettingsUI.Page.Audio), PixelUiTheme.Linen, new Vector2(226f, 48f));
            audio.GetComponent<RectTransform>().anchoredPosition = new Vector2(-117f, -24f);
            GameObject chat = MenuButton(util, "CHAT & SAFETY", -24f, () => ShowSafetySettings(SettingsUI.Page.Safety), PixelUiTheme.Linen, new Vector2(226f, 48f));
            chat.GetComponent<RectTransform>().anchoredPosition = new Vector2(117f, -24f);
            foreach (GameObject moved in new[] { audio, chat }) if (moved.TryGetComponent(out UIButtonJuice juice)) juice.SyncRestPosition();
            TownshipVoiceManager voice = TownshipVoiceManager.Instance;
            MakeLabel(util, "Text chat " + (GameSettings.TextChatEnabled ? "on" : "off") + "   ·   Voice " + (voice != null && voice.EnabledByUser ? "on" : "off") + "   ·   Chat [T]  Voice [V] in a match",
                new Vector2(0f, -80f), new Vector2(460f, 26f), 14, PixelUiTheme.MutedInk);

            GameObject credits = MenuButton(right, "CREDITS", -385f, ShowCredits, PixelUiTheme.Linen, new Vector2(200f, 46f));
            credits.GetComponent<RectTransform>().anchoredPosition = new Vector2(-140f, -385f);
            if (credits.TryGetComponent(out UIButtonJuice creditsJuice)) creditsJuice.SyncRestPosition();
            GameObject quit = MenuButton(right, "QUIT GAME", -385f, QuitGame, PixelUiTheme.Coral, new Vector2(200f, 46f));
            quit.GetComponent<RectTransform>().anchoredPosition = new Vector2(140f, -385f);
            if (quit.TryGetComponent(out UIButtonJuice quitJuice)) quitJuice.SyncRestPosition();

            if (backdrop != null) backdrop.transform.SetAsFirstSibling();
            Typography.Apply(band.transform);
            Typography.Apply(card.transform);
        }

        // A crisp 2-unit drop shadow so light copy stays readable over the park.
        static void Lift(Text label) => label.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

        /// <summary>Credits and third-party licences; the fonts' OFL licence asks for this notice.</summary>
        void ShowCredits()
        {
            activeOverlay = "credits";
            DestroyOverlay();
            GameObject modal = SafetyUI.CreateModal(overlayRoot, "Credits", new Vector2(860f, 700f));
            Transform window = modal.transform.Find("Safety Window");
            Font font = PixelUiTheme.BodyFont;
            SafetyUI.AddLabel(window, "CREDITS", new Vector2(.06f, .87f), new Vector2(.94f, .96f), 32, font, FontStyle.Bold, TextAnchor.MiddleLeft);
            SafetyUI.AddLabel(window,
                "CRONY · TOWNSHIP PARK   v" + Application.version + "\nMade by the Crony team with Unity.\n\n" +
                "FONTS (SIL Open Font License 1.1, included with the game)\n" +
                "Pixelify Sans · Copyright 2021 The Pixelify Sans Project Authors\n" +
                "Quicksand · Copyright 2011 The Quicksand Project Authors\n\n" +
                "SERVICES\nOnline rooms by Unity Multiplayer Services · Voice chat by Unity Vivox\n" +
                "Crisis helpline directory by " + SupportResources.HelplineName + " (findahelpline.com)\n\n" +
                "All music and sound effects are generated by the game itself.",
                new Vector2(.06f, .16f), new Vector2(.94f, .85f), 17, font, FontStyle.Normal, TextAnchor.UpperLeft);
            SafetyUI.AddButton(window, "Close Credits", "BACK", new Vector2(.66f, .04f), new Vector2(.94f, .12f), PixelUiTheme.Amber, ShowMainMenu, font, 17);
            Typography.Apply(window);
        }

        void SectionLabel(Transform parent, string text, float y) =>
            MakeLabel(parent, text, new Vector2(0f, y), new Vector2(480f, 30f), 16, PixelUiTheme.MutedInk, FontStyle.Bold).alignment = TextAnchor.MiddleLeft;

        void Caption(Transform parent, string text, float y) =>
            MakeLabel(parent, text, new Vector2(0f, y), new Vector2(480f, 26f), 15, PixelUiTheme.MutedInk);

        GameObject MenuButton(Transform parent, string text, float y, Action action, Color face, Vector2 size)
        {
            GameObject button = AddMenuButton(parent, text, new Vector2(0f, y), action, false, size);
            PixelUiTheme.SetButtonFace(button.GetComponent<Button>(), face);
            button.GetComponentInChildren<Text>().color = PixelUiTheme.ButtonTextColor(face);
            return button;
        }

        void ShowHowToPlay() => OpenHowToPlay(ShowMainMenu);

        void OpenHowToPlay(Action back)
        {
            activeOverlay = "help"; DestroyOverlay();
            HowToPlayUI.Show(overlayRoot, uiFont, largeText, back);
        }

        void ShowPrivateLobby()
        {
            activeOverlay = "lobby";
            DestroyOverlay();
            MakePanel(overlayRoot, "Lobby Backdrop", new Color(.02f, .08f, .11f, .78f), Vector2.zero, Vector2.zero, true);
            GameObject card = MakePanel(overlayRoot, "Private Lobby Card", PixelUiTheme.Cream, new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(640f, 600f));
            MakeLabel(card.transform, "PRACTICE WITH BOTS", new Vector2(0f, 240f), new Vector2(560f, 54f), 34, PixelUiTheme.Ink, FontStyle.Bold);
            MakeLabel(card.transform, "A full 8-player round on this device. Seven bots fill the park.", new Vector2(0f, 190f), new Vector2(560f, 30f), 17, PixelUiTheme.MutedInk);
            MakeLabel(card.transform, "CHOOSE YOUR ROLE", new Vector2(0f, 135f), new Vector2(560f, 28f), 15, PixelUiTheme.MutedInk, FontStyle.Bold);
            AddMenuButton(card.transform, "CREWMATE", new Vector2(0f, 80f), () => BeginMatch(PlayerRole.Crewmate), true, new Vector2(460f, 58f));
            MakeLabel(card.transform, "Finish tasks, report bodies and vote out the impostor", new Vector2(0f, 38f), new Vector2(560f, 24f), 14, PixelUiTheme.MutedInk);
            GameObject impostor = AddMenuButton(card.transform, "IMPOSTOR", new Vector2(0f, -22f), () => BeginMatch(PlayerRole.Impostor), false, new Vector2(460f, 58f));
            PixelUiTheme.SetButtonFace(impostor.GetComponent<Button>(), PixelUiTheme.Coral);
            impostor.GetComponentInChildren<Text>().color = PixelUiTheme.ButtonTextColor(PixelUiTheme.Coral);
            MakeLabel(card.transform, "Sabotage the park and avoid being voted out", new Vector2(0f, -64f), new Vector2(560f, 24f), 14, PixelUiTheme.MutedInk);
            AddMenuButton(card.transform, "RANDOM ROLE", new Vector2(0f, -124f), () => BeginMatch(UnityEngine.Random.value < 1f / PlayerLimit ? PlayerRole.Impostor : PlayerRole.Crewmate), false, new Vector2(460f, 52f));
            AddMenuButton(card.transform, "Back", new Vector2(0f, -230f), ShowMainMenu, false, new Vector2(220f, 46f));
        }

        void ShowOnlinePlay()
        {
            // NetworkManagerUI draws the host/join window while this overlay is active.
            activeOverlay = "network";
            DestroyOverlay();
            MakePanel(overlayRoot, "Network Shade", new Color(.02f, .08f, .07f, .78f), Vector2.zero, Vector2.zero, true);
        }

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void ShowCustomization()
        {
            AvatarDressingRoomUI.Open();
        }

        GameObject AddAvatarPart(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size, Color color, int sortingOrder, bool ui, float rotation = 0f)
        {
            if (ui)
            {
                GameObject part = new GameObject(name, typeof(RectTransform), typeof(Image));
                part.transform.SetParent(parent, false);
                RectTransform rect = part.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = size * 100f; rect.anchoredPosition = position * 100f;
                part.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
                Image image = part.GetComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false;
                return part;
            }

            GameObject worldPart = new GameObject(name);
            worldPart.transform.SetParent(parent, false);
            worldPart.transform.localPosition = new Vector3(position.x, position.y, 0f);
            worldPart.transform.localScale = new Vector3(size.x, size.y, 1f);
            worldPart.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            SpriteRenderer renderer = worldPart.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = sortingOrder;
            return worldPart;
        }

        void AddOutfitDetails(Transform parent, int outfitIndex, bool ui)
        {
            Color soft = Hex("FFF0D8");
            Color mint = Hex("83D7BC");
            Color rose = Hex("F5A4B7");
            switch (outfitIndex)
            {
                case 0: // Daisy cardigan
                    AddAvatarPart(parent, "Cardigan Trim", squareSprite, new Vector2(0f, -.19f), new Vector2(.46f, .045f), soft, 16, ui);
                    for (int i = 0; i < 3; i++) AddAvatarPart(parent, "Cardigan Button", circleSprite, new Vector2(.12f, .08f - i * .12f), new Vector2(.045f, .045f), Hex("E6BD65"), 16, ui);
                    break;
                case 1: // Berry overalls
                    AddAvatarPart(parent, "Overall Bib", squareSprite, new Vector2(0f, -.16f), new Vector2(.34f, .30f), Hex("6599C5"), 16, ui);
                    AddAvatarPart(parent, "Overall Strap Left", squareSprite, new Vector2(-.14f, .09f), new Vector2(.10f, .34f), Hex("6599C5"), 16, ui, -10f);
                    AddAvatarPart(parent, "Overall Strap Right", squareSprite, new Vector2(.14f, .09f), new Vector2(.10f, .34f), Hex("6599C5"), 16, ui, 10f);
                    AddAvatarPart(parent, "Overall Buckle Left", circleSprite, new Vector2(-.14f, .02f), new Vector2(.06f, .06f), Hex("FFE6A8"), 17, ui);
                    AddAvatarPart(parent, "Overall Buckle Right", circleSprite, new Vector2(.14f, .02f), new Vector2(.06f, .06f), Hex("FFE6A8"), 17, ui);
                    break;
                case 2: // Cloud hoodie
                    AddAvatarPart(parent, "Hoodie Pocket", squareSprite, new Vector2(0f, -.28f), new Vector2(.34f, .15f), Hex("D9F1F4"), 16, ui);
                    AddAvatarPart(parent, "Hoodie Drawstring Left", squareSprite, new Vector2(-.06f, -.02f), new Vector2(.025f, .18f), Hex("D9F1F4"), 16, ui);
                    AddAvatarPart(parent, "Hoodie Drawstring Right", squareSprite, new Vector2(.06f, -.02f), new Vector2(.025f, .18f), Hex("D9F1F4"), 16, ui);
                    break;
                case 3: // Star sweater
                    AddAvatarPart(parent, "Sweater Stripe Top", squareSprite, new Vector2(0f, -.14f), new Vector2(.48f, .05f), soft, 16, ui);
                    AddAvatarPart(parent, "Sweater Stripe Bottom", squareSprite, new Vector2(0f, -.25f), new Vector2(.42f, .04f), mint, 16, ui);
                    AddAvatarPart(parent, "Star Sweater Badge", circleSprite, new Vector2(.20f, -.04f), new Vector2(.12f, .12f), Hex("FFE19A"), 17, ui);
                    break;
                case 4: // Garden apron
                    AddAvatarPart(parent, "Garden Apron", squareSprite, new Vector2(0f, -.17f), new Vector2(.38f, .32f), Hex("FFF0D8"), 16, ui);
                    AddAvatarPart(parent, "Apron Flower", circleSprite, new Vector2(.12f, -.12f), new Vector2(.09f, .09f), rose, 17, ui);
                    AddAvatarPart(parent, "Apron Leaf", circleSprite, new Vector2(.18f, -.17f), new Vector2(.05f, .05f), mint, 17, ui);
                    break;
                default: // Sunny tee
                    AddAvatarPart(parent, "Sunny Tee Stripe", squareSprite, new Vector2(0f, -.18f), new Vector2(.46f, .05f), Hex("F6A77B"), 16, ui);
                    AddAvatarPart(parent, "Sunny Tee Collar", squareSprite, new Vector2(0f, .10f), new Vector2(.24f, .045f), Hex("FFF0D8"), 16, ui);
                    break;
            }
        }

        void AddAccessoryVisual(Transform parent, int accessoryIndex, bool ui)
        {
            Color pink = Hex("F59CB7");
            Color blush = Hex("FFD2D9");
            Color gold = Hex("F2C861");
            Color lavender = Hex("B99AE8");
            switch (accessoryIndex)
            {
                case 1: // Flower crown
                    for (int i = 0; i < 5; i++)
                    {
                        float x = -.24f + i * .12f;
                        float y = .46f + (i == 2 ? .045f : 0f);
                        Color petal = i % 2 == 0 ? blush : Hex("FFF0D8");
                        AddAvatarPart(parent, "Flower Crown Petal", circleSprite, new Vector2(x, y), new Vector2(.15f, .15f), petal, 19, ui);
                        AddAvatarPart(parent, "Flower Crown Center", circleSprite, new Vector2(x, y), new Vector2(.06f, .06f), gold, 20, ui);
                    }
                    break;
                case 2: // Bow
                    AddAvatarPart(parent, "Bow Left Loop", squareSprite, new Vector2(-.12f, .49f), new Vector2(.19f, .15f), pink, 19, ui, -28f);
                    AddAvatarPart(parent, "Bow Right Loop", squareSprite, new Vector2(.12f, .49f), new Vector2(.19f, .15f), pink, 19, ui, 28f);
                    AddAvatarPart(parent, "Bow Knot", circleSprite, new Vector2(0f, .49f), new Vector2(.10f, .10f), gold, 20, ui);
                    break;
                case 3: // Cat ears
                    AddAvatarPart(parent, "Cat Ear Left", squareSprite, new Vector2(-.19f, .49f), new Vector2(.19f, .23f), lavender, 19, ui, 45f);
                    AddAvatarPart(parent, "Cat Ear Right", squareSprite, new Vector2(.19f, .49f), new Vector2(.19f, .23f), lavender, 19, ui, 45f);
                    AddAvatarPart(parent, "Cat Ear Inner Left", squareSprite, new Vector2(-.19f, .48f), new Vector2(.09f, .14f), blush, 20, ui, 45f);
                    AddAvatarPart(parent, "Cat Ear Inner Right", squareSprite, new Vector2(.19f, .48f), new Vector2(.09f, .14f), blush, 20, ui, 45f);
                    break;
                case 4: // Party crown
                    AddAvatarPart(parent, "Crown Band", squareSprite, new Vector2(0f, .43f), new Vector2(.48f, .09f), gold, 19, ui);
                    for (int i = 0; i < 3; i++)
                    {
                        float x = -.16f + i * .16f;
                        AddAvatarPart(parent, "Crown Point", squareSprite, new Vector2(x, .53f), new Vector2(.13f, .15f), gold, 19, ui, 45f);
                    }
                    AddAvatarPart(parent, "Crown Jewel", circleSprite, new Vector2(0f, .44f), new Vector2(.09f, .09f), pink, 20, ui);
                    break;
                case 5: // Beret
                    AddAvatarPart(parent, "Beret", circleSprite, new Vector2(0f, .48f), new Vector2(.50f, .22f), lavender, 19, ui, -10f);
                    AddAvatarPart(parent, "Beret Pom", circleSprite, new Vector2(.19f, .57f), new Vector2(.10f, .10f), pink, 20, ui);
                    break;
            }
        }

        void SetButtonColor(GameObject buttonObject, Color color)
        {
            Image image = buttonObject.GetComponent<Image>();
            if (image != null) image.color = color;
            Button button = buttonObject.GetComponent<Button>();
            if (button == null) return;
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, .18f);
            colors.pressedColor = Color.Lerp(color, Color.black, .12f);
            colors.disabledColor = new Color(color.r, color.g, color.b, .45f);
            button.colors = colors;
        }

        void ShowSafetySettings() => ShowSafetySettings(SettingsUI.Page.Audio);

        void ShowSafetySettings(SettingsUI.Page page)
        {
            activeOverlay = "settings";
            DestroyOverlay();
            MakePanel(overlayRoot, "Settings Backdrop", new Color(.025f, .09f, .08f, .78f), Vector2.zero, Vector2.zero, true);
            SettingsUI.Show(overlayRoot, ShowMainMenu, largeText, highContrast, reducedEffects, StoreAccessibilitySettings, page);
        }

        GameObject AddMenuButton(Transform parent, string text, Vector2 position, Action action, bool primary, Vector2? size = null)
        {
            Color baseColor = primary ? PixelUiTheme.Amber : PixelUiTheme.Almond;
            GameObject go = MakeButton(parent, text, position, size ?? new Vector2(440f, 50f), baseColor, action);
            Text label = go.GetComponentInChildren<Text>();
            if (label != null) { label.color = PixelUiTheme.ButtonTextColor(baseColor); label.fontSize = largeText ? 20 : 17; label.fontStyle = FontStyle.Bold; }
            return go;
        }

        void BeginMatch(PlayerRole role)
        {
            activeOverlay = "";
            trackedTask = -1;
            DestroyOverlay();
            StopPlayers();
            humanRole = chosenRole = role;
            recruitUsed = false;
            stumpCooldown = 0f;
            botMemory.Clear(); discussion.Clear(); lastRepairPlace = null; evidenceClock = 0f;
            CheckpointLabels.ClearOfflineProgress();
            cronyPartner = null;
            phase = RoundPhase.Playing;
            matchTimer = MatchLength;
            sabotageTimer = 0f;
            currentSabotage = -1;
            killCooldown = 12f;
            sabotageCooldown = 0f;
            meetingCooldown = 0f;
            emergencyUsed = false;
            botClock = 0f;
            hudClock = 0f;
            botMeetingClock = 0f;
            meetingBody = -1;
            humanVote = -2;
            evidence.Clear();
            reportedPlayers.Clear();
            SafetySystem.Ensure(gameObject).ResetMatch();
            CommunicationSafety.ClearMatchRestrictions();
            foreach (TaskStation task in tasks)
            {
                task.complete = false; task.step = 0;
                if (task.marker == null) continue;
                task.marker.color = Hex("FFF3D9");
                GlowLight.Attach(task.marker.transform, GlowProfile.Beacon);
            }
            // Offline vision comes from the screen-space fog, so the world stays fully lit underneath it.
            GlowLight.SetAmbient(1f);
            SpawnPlayers();
            hudRoot.gameObject.SetActive(true);
            BuildHud();
            SetFogVisible(true);
            UpdateVisionMask();
            AddEvidence("Match started at Township Park.");
            GameAudioManager.SetMusic(MusicMood.Park);
            GameAudioManager.PlayRoleReveal(role == PlayerRole.Impostor);
            ShowToast(role == PlayerRole.Crewmate ? "You are a Crewmate. Finish tasks and watch for clues." : "You are the Impostor. Blend in, sabotage, and make your move.", 4f);
        }

        void SpawnPlayers()
        {
            players.Clear();
            Unity.Netcode.NetworkManager networkManager = Unity.Netcode.NetworkManager.Singleton;
            if (networkManager == null)
                networkManager = FindAnyObjectByType<Unity.Netcode.NetworkManager>();
            GameObject playerPrefab = networkManager != null ? networkManager.NetworkConfig.PlayerPrefab : null;
            if (playerPrefab == null || playerPrefab.GetComponent<PlayerAvatarLayerManager>() == null)
            {
                Debug.LogError("Township Park practice players require NetworkManager.PlayerPrefab to reference the layered NetworkPlayer prefab.");
                return;
            }

            string[] names = { "Player", "Aina", "Benny", "Chloe", "Dev", "Farah", "Gopal", "Hana" };
            Color[] colors = { outfitColors[Mathf.Clamp(customOutfit, 0, outfitColors.Length - 1)], Hex("F07668"), Hex("6DA9D8"), Hex("E5BB58"), Hex("9E77C7"), Hex("6AB59A"), Hex("DB89AC"), Hex("D1A37A") };
            Vector2[] spawns = { new Vector2(0f, 0f), new Vector2(-2f, 0f), new Vector2(2f, 0f), new Vector2(0f, 2.5f), new Vector2(-2.5f, 2.2f), new Vector2(2.5f, -1f), new Vector2(-2f, -2.5f), new Vector2(2f, 2.5f) };
            int impostorId = humanRole == PlayerRole.Impostor ? 0 : UnityEngine.Random.Range(1, PlayerLimit);
            for (int i = 0; i < PlayerLimit; i++)
            {
                MatchPlayer player = new MatchPlayer();
                player.id = i;
                player.name = names[i];
                player.isHuman = i == 0;
                player.role = i == impostorId ? PlayerRole.Impostor : PlayerRole.Crewmate;
                if (worldMap != null) spawns[i] = WorldToMap(worldMap.NearestWalkable(MapToWorld(spawns[i])));
                player.position = spawns[i];
                spawnPoints = spawns;
                GameObject avatar = Instantiate(playerPrefab, playerRoot, false);
                avatar.name = "Avatar - " + player.name;
                Vector2 spawnWorldPosition = MapToWorld(spawns[i]);
                avatar.transform.position = new Vector3(spawnWorldPosition.x, spawnWorldPosition.y, 0f);
                player.avatarLayers = avatar.GetComponent<PlayerAvatarLayerManager>();
                if (player.avatarLayers == null)
                {
                    Destroy(avatar);
                    Debug.LogError("The configured NetworkPlayer prefab lost its PlayerAvatarLayerManager component.");
                    return;
                }

                AvatarDatabaseSO database = player.avatarLayers.Database;
                if (database != null)
                {
                    AvatarLookState look = i == 0
                        ? PlayerAvatarLayerManager.LoadPreferences(database)
                        : database.CreateDefaultState();
                    if (i > 0)
                    {
                        AvatarItemCategory[] categories =
                        {
                            AvatarItemCategory.Skin, AvatarItemCategory.Face, AvatarItemCategory.Tattoo,
                            AvatarItemCategory.Hair, AvatarItemCategory.Hat, AvatarItemCategory.Bottom,
                            AvatarItemCategory.Shoes, AvatarItemCategory.Top, AvatarItemCategory.Accessory
                        };
                        foreach (AvatarItemCategory category in categories)
                        {
                            int count = database.GetItems(category).Count;
                            if (count > 0) look.SetIndex(category, (i * 2 + (int)category) % count);
                        }
                        look.SetTint(AvatarItemCategory.Skin, skinColors[i % skinColors.Length]);
                        look.SetTint(AvatarItemCategory.Hair, new Color32(47, 39, 40, 255));
                        look.SetTint(AvatarItemCategory.Top, colors[i]);
                    }
                    player.avatarLayers.RequestEquip(look, false);
                }
                player.avatarLayers.FaceSouth();
                if (player.avatarLayers.LayerRenderers != null && player.avatarLayers.LayerRenderers.Length > 1)
                    player.body = player.avatarLayers.LayerRenderers[1];
                player.outfitColor = player.body != null ? player.body.color : Color.white;
                player.nameTag = CreateWorldLabel(player.name, spawns[i] + new Vector2(0f, .78f), .075f, Color.white, 20);
                player.nameTag.transform.SetParent(avatar.transform, true);
                player.nameTag.transform.localPosition = new Vector3(0f, .78f, -.1f);
                avatar.AddComponent<PlayerNameplate>().Bind(player.name, player.nameTag);
                player.root = avatar;
                if (i == 0) human = player;
                players.Add(player);
            }
            for (int i = 0; i < players.Count; i++)
                if (players[i].role == PlayerRole.Impostor && i != 0) latestEvent = players[i].name + " is acting strangely";
        }

        void StopPlayers()
        {
            for (int i = 0; i < players.Count; i++) if (players[i].root != null) Destroy(players[i].root);
            players.Clear(); human = null;
        }

        void BuildHud()
        {
            for (int i = hudRoot.childCount - 1; i >= 0; i--) Destroy(hudRoot.GetChild(i).gameObject);
            Color panel = PixelUiTheme.Cream;
            GameObject statusBanner = MakePanel(hudRoot, "Top HUD", panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(1920f, 48f), new Vector2(0f, 500f));
            hudStatus = MakeLabel(hudRoot, "Township Park  •  08:00", new Vector2(32f, -30f), new Vector2(720f, 38f), 22, PixelUiTheme.Ink, FontStyle.Bold, new Vector2(0f, 1f), new Vector2(0f, 1f));
            // One line even while a sabotage name and countdown are appended.
            hudStatus.horizontalOverflow = HorizontalWrapMode.Overflow;
            hudStatus.alignment = TextAnchor.MiddleLeft;
            roleLabel = MakeLabel(hudRoot, "CREWMATE", new Vector2(0f, -30f), new Vector2(260f, 38f), 20, Hex("A6E0CF"), FontStyle.Bold, new Vector2(.5f, 1f), new Vector2(.5f, 1f));
            taskListLabel = MakeLabel(hudRoot, "", new Vector2(18f, -110f), new Vector2(300f, 268f), 18, PixelUiTheme.Ink, FontStyle.Normal, new Vector2(0f, 1f), new Vector2(0f, 1f));
            taskListLabel.alignment = TextAnchor.UpperLeft;
            GameObject checklist = MakePanel(hudRoot, "Task List Backplate", panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(326f, 230f), new Vector2(-800f, 350f));
            checklist.transform.SetAsFirstSibling();
            taskListLabel.transform.SetAsLastSibling();
            interactLabel = MakeLabel(hudRoot, "", new Vector2(0f, -114f), new Vector2(700f, 42f), 20, Hex("FFF4D7"), FontStyle.Bold, new Vector2(.5f, 1f), new Vector2(.5f, 1f));
            toastBackplate = MakePanel(hudRoot, "Toast Backplate", new Color(.06f, .14f, .13f, .93f), new Vector2(.5f, 1f), new Vector2(.5f, 1f), false, new Vector2(700f, 66f), new Vector2(0f, -153f));
            toastBackplate.GetComponent<Image>().raycastTarget = false;
            toastLabel = MakeLabel(toastBackplate.transform, "", Vector2.zero, new Vector2(670f, 56f), 20, PixelUiTheme.Cream, FontStyle.Bold);
            taskProgressLabel = MakeLabel(hudRoot, "", new Vector2(-32f, -25f), new Vector2(300f, 30f), 16, PixelUiTheme.Ink, FontStyle.Bold, new Vector2(1f, 1f), new Vector2(1f, 1f));
            GameObject progress = MakePanel(hudRoot, "Task Bar Track", Hex("263D45"), new Vector2(1f, 1f), new Vector2(1f, 1f), false, new Vector2(290f, 10f), new Vector2(-32f, -62f));
            GameObject fill = MakePanel(progress.transform, "Task Bar Fill", Hex("67D0AE"), new Vector2(0f, .5f), new Vector2(0f, .5f), false, new Vector2(1f, 10f));
            taskProgressFill = fill.GetComponent<Image>();
            GameObject interactButton = AddHudButton("Interact  [E]", new Vector2(-16f, 16f), Interact, Hex("FFAB4C"));
            GameObject reportButton = AddHudButton("Report  [R]", new Vector2(-16f, 68f), ReportNearby, Hex("D96959"));
            GameObject meetingButton = AddHudButton("Meeting  [M]", new Vector2(-16f, 120f), CallEmergency, Hex("D6AD57"));
            AddHudButton("Safety", new Vector2(-208f, 16f), ShowInGameSafety, Hex("4D7E8E"));
            AddHudButton("Map  [Tab]", new Vector2(-208f, 68f), ShowMap, Hex("4D7E8E"));
            if (humanRole == PlayerRole.Impostor)
            {
                sabotageButtonLabel = AddHudButton("Sabotage  [Q]", new Vector2(-208f, 120f), ShowSabotageMenu, Hex("AD5160")).GetComponentInChildren<Text>();
                knockDownButtonLabel = AddHudButton("Knock Down  [F]", new Vector2(-208f, 172f), TryHumanIncapacitate, Hex("AD5160")).GetComponentInChildren<Text>();
                recruitButton = AddHudButton("Recruit Crony  [G]", new Vector2(-208f, 224f), TryHumanRecruit, Hex("8A4E9C"));
            }
            GameObject tracker = MakePanel(hudRoot, "Task Tracker Panel", panel, new Vector2(0f, 0f), new Vector2(0f, 0f), false, new Vector2(365f, 118f), new Vector2(16f, 16f));
            trackedTaskLabel = MakeLabel(tracker.transform, "", Vector2.zero, new Vector2(333f, 92f), 17, PixelUiTheme.Ink);
            trackedTaskLabel.alignment = TextAnchor.MiddleLeft;
            trackedTaskLabel.lineSpacing = 1.2f;
            if (Application.isMobilePlatform && Touchscreen.current != null) BuildTouchPad();

            TownshipHUDUI layout = hudRoot.GetComponent<TownshipHUDUI>();
            if (layout == null) layout = hudRoot.gameObject.AddComponent<TownshipHUDUI>();
            layout.Bind(statusBanner.GetComponent<RectTransform>(), checklist.GetComponent<RectTransform>(), taskListLabel.rectTransform,
                interactButton.GetComponent<RectTransform>(), reportButton.GetComponent<RectTransform>(), meetingButton.GetComponent<RectTransform>());
        }

        GameObject AddHudButton(string label, Vector2 position, Action action, Color color)
        {
            GameObject button = MakeButton(hudRoot, label, position, new Vector2(180f, 44f), color, action);
            Text text = button.GetComponentInChildren<Text>();
            if (text != null) { text.fontSize = largeText ? 18 : 15; text.fontStyle = FontStyle.Bold; text.color = PixelUiTheme.ButtonTextColor(color); }
            RectTransform rt = button.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = position;
            if (button.TryGetComponent(out UIButtonJuice juice1)) juice1.SyncRestPosition();
            return button;
        }

        void BuildTouchPad()
        {
            GameObject pad = MakePanel(hudRoot, "Touch Controls", new Color(.04f, .11f, .14f, .38f), new Vector2(0f, 0f), new Vector2(0f, 0f), false, new Vector2(190f, 190f), new Vector2(125f, 120f));
            AddTouchButton(pad.transform, "↑", new Vector2(0f, 48f), Vector2.up);
            AddTouchButton(pad.transform, "↓", new Vector2(0f, -48f), Vector2.down);
            AddTouchButton(pad.transform, "←", new Vector2(-48f, 0f), Vector2.left);
            AddTouchButton(pad.transform, "→", new Vector2(48f, 0f), Vector2.right);
        }

        void AddTouchButton(Transform parent, string label, Vector2 position, Vector2 direction)
        {
            GameObject button = MakeButton(parent, label, position, new Vector2(52f, 52f), new Color(.8f, .9f, .83f, .82f), null);
            Text text = button.GetComponentInChildren<Text>(); if (text != null) { text.color = PixelUiTheme.Ink; text.fontSize = 22; }
            TouchMoveButton move = button.AddComponent<TouchMoveButton>(); move.Initialize(this, direction);
        }

        void Update()
        {
            if (toastTimer > 0f) { toastTimer -= Time.deltaTime; if (toastLabel != null) toastLabel.text = toast; }
            else if (toastLabel != null && toastLabel.text.Length > 0) toastLabel.text = "";
            if (toastBackplate != null) toastBackplate.SetActive(toastTimer > 0f);
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && !TownshipTextChatUI.CapturesInput)
            {
                if (phase == RoundPhase.Playing && activeOverlay.Length == 0)
                {
                    if (keyboard.tabKey.wasPressedThisFrame) ShowMap();
                    if (keyboard.eKey.wasPressedThisFrame) Interact();
                    if (keyboard.rKey.wasPressedThisFrame) ReportNearby();
                    if (keyboard.mKey.wasPressedThisFrame) CallEmergency();
                    if (keyboard.qKey.wasPressedThisFrame && humanRole != PlayerRole.Crewmate) ShowSabotageMenu();
                    if (keyboard.fKey.wasPressedThisFrame && humanRole == PlayerRole.Impostor) TryHumanIncapacitate();
                    if (keyboard.gKey.wasPressedThisFrame && humanRole == PlayerRole.Impostor) TryHumanRecruit();
                }
                if (keyboard.tabKey.wasPressedThisFrame && activeOverlay == "map" && mapOpenedFrame != Time.frameCount)
                { activeOverlay = ""; DestroyOverlay(); }
                if (keyboard.escapeKey.wasPressedThisFrame && !TownshipTextChatUI.HandlesEscape && !AvatarDressingRoomUI.IsOpen)
                {
                    if (phase == RoundPhase.Playing)
                    {
                        if (activeOverlay.Length > 0)
                        {
                            if (activeTask != null) CloseTask();
                            else { activeOverlay = ""; DestroyOverlay(); }
                        }
                        else ShowPauseMenu();
                    }
                    else if (phase == RoundPhase.Menu && activeOverlay.Length > 0) ShowMainMenu();
                    else if (phase == RoundPhase.Results) ShowMainMenu();
                    // Meetings resolve through voting; Escape must not abandon the round.
                }
            }

            // Checkpoint names show while walking the park, never over a window, meeting or menu.
            CheckpointLabels.SetOffline(phase == RoundPhase.Playing && activeOverlay.Length == 0 && human != null, human != null ? MapToWorld(human.position) : Vector2.zero);
            if (phase == RoundPhase.Playing)
            {
                if (IsLocalPaused) return;
                UpdateHumanMovement();
                UpdateBots();
                if (phase == RoundPhase.Playing) CheckVictory();
                if (phase != RoundPhase.Playing) return;
                UpdateMatchClock();
                if (phase != RoundPhase.Playing) return;
                UpdateCamera();
                if (mapPlayerMarker != null && activeOverlay == "map") mapPlayerMarker.anchoredPosition = MapPanelPoint(human.position);
                UpdateNearbyPrompt();
                UpdateHud();
            }
            else if (phase == RoundPhase.Meeting) UpdateMeeting();
        }

        void UpdateHumanMovement()
        {
            if (TownshipTextChatUI.CapturesInput || human == null || !human.alive || human.downed || activeTask != null || activeOverlay.Length > 0) return;
            Vector2 movement = new Vector2(touchMoveX, touchMoveY);
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                movement.x += (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
                movement.y += (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            }
            Gamepad pad = Gamepad.current;
            if (pad != null) movement += pad.leftStick.ReadValue();
            if (movement.sqrMagnitude > 1f) movement.Normalize();
            float speed = human.role == PlayerRole.Impostor ? 4.6f : 4.1f;
            MovePlayer(human, movement * speed * Time.deltaTime);
            StepSounds(movement.sqrMagnitude > .01f);
        }

        /// <summary>Alternating footsteps matched to the ground under the player (grass, path, bridge, water edge).</summary>
        void StepSounds(bool moving)
        {
            if (!moving) { footstepTimer = 0f; return; }
            if ((footstepTimer -= Time.deltaTime) > 0f) return;
            footstepTimer = .34f;
            SurfaceType surface = SurfaceType.Grass;
            Vector2 feet = MapToWorld(human.position);
            RaycastHit2D hit = Physics2D.Raycast(feet + Vector2.up * .2f, Vector2.down, 1.2f);
            SurfaceAudioMarker marker = hit.collider != null ? hit.collider.GetComponent<SurfaceAudioMarker>() : null;
            if (marker != null) surface = marker.Surface;
            GameAudioManager.PlayFootstep(surface);
        }

        public void SetTouchMove(Vector2 direction, bool pressed)
        {
            if (direction.x != 0f) touchMoveX = pressed ? direction.x : 0f;
            if (direction.y != 0f) touchMoveY = pressed ? direction.y : 0f;
        }

        void MovePlayer(MatchPlayer player, Vector2 delta)
        {
            int slices = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .12f));
            if (slices > 1)
            {
                for (int i = 0; i < slices; i++) MovePlayer(player, delta / slices);
                return;
            }
            Vector2 next = player.position;
            Vector2 xStep = next + new Vector2(delta.x, 0f);
            if (!IsBlocked(xStep)) next.x = xStep.x;
            Vector2 yStep = next + new Vector2(0f, delta.y);
            if (!IsBlocked(yStep)) next.y = yStep.y;
            if (worldMap == null)
            {
                next.x = Mathf.Clamp(next.x, -15f, 15f);
                next.y = Mathf.Clamp(next.y, -9.5f, 9.5f);
            }
            player.position = next;
            if (player.root != null)
            {
                Vector2 worldPosition = MapToWorld(next);
                player.root.transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);
            }
        }

        bool IsBlocked(Vector2 position)
        {
            if (worldMap != null) return !worldMap.CanStand(MapToWorld(position));
            foreach (Rect blocker in mapBlockers) if (blocker.Contains(position)) return true;
            return false;
        }

        void UpdateBots()
        {
            botClock += Time.deltaTime;
            if (botClock <= 0f) return;
            float dt = botClock; botClock = 0f;
            for (int i = 1; i < players.Count; i++)
            {
                MatchPlayer bot = players[i];
                if (!bot.alive) continue;
                if (bot.downed)
                {
                    bot.downedTimer += dt;
                    if (bot.downedTimer > 11f && bot.role == PlayerRole.Crewmate && UnityEngine.Random.value < 1f - Mathf.Pow(1f - .035f, dt / .22f))
                    {
                        bot.downed = false; bot.downedTimer = 0f; bot.body.color = Hex("B5DFAE"); AddEvidence(bot.name + " was helped back up.");
                    }
                    continue;
                }
                if (bot.role == PlayerRole.Impostor)
                {
                    UpdateImpostorBot(bot, dt);
                    TryBotRecruit(bot, dt);
                    continue;
                }
                UpdateCrewBot(bot, dt);
            }
            if (phase == RoundPhase.Playing)
            {
                evidenceClock += dt;
                if (evidenceClock >= .5f) { evidenceClock = 0f; ObserveEvidence(); }
            }
            if (phase == RoundPhase.Playing && HasUnreportedBody())
            {
                botMeetingClock += dt;
                if (botMeetingClock > 18f && UnityEngine.Random.value < 1f - Mathf.Pow(1f - .02f, dt / .22f)) ReportNearestBotBody();
            }
            else botMeetingClock = 0f;
        }

        void UpdateCrewBot(MatchPlayer bot, float dt)
        {
            MatchPlayer downedPlayer = FindNearestDowned(bot);
            if (downedPlayer != null && Vector2.Distance(bot.position, downedPlayer.position) < 6f)
            {
                Vector2 rescuePoint = downedPlayer.position + new Vector2(.45f, .35f);
                if (Vector2.Distance(bot.position, downedPlayer.position) > 1.25f)
                    MoveBotTowards(bot, rescuePoint, 2.2f, dt);
                else
                {
                    bot.taskTimer += dt;
                    if (bot.taskTimer > 1.6f)
                    {
                        downedPlayer.downed = false;
                        downedPlayer.downedTimer = 0f;
                        downedPlayer.root.transform.localScale = Vector3.one;
                        downedPlayer.body.color = downedPlayer.outfitColor;
                        bot.taskTimer = 0f;
                        AddEvidence(bot.name + " helped " + downedPlayer.name + " back up.");
                    }
                }
                return;
            }
            if (bot.botFinishedTask)
            {
                bot.taskTimer += dt;
                if (bot.taskTimer < 6f) { WanderAfterTask(bot, dt); return; }
                bot.botFinishedTask = false;
                bot.taskTimer = 0f;
            }
            TaskStation target = FindNextOpenTask(bot);
            if (target == null) return;
            Vector2 destination = worldMap != null ? WorldToMap(worldMap.NearestWalkable(MapToWorld(target.position))) : target.position + new Vector2(0f, -1.2f);
            Vector2 to = destination - bot.position;
            if (to.magnitude > .65f) MoveBotTowards(bot, destination, 2.0f, dt);
            else
            {
                bot.taskTimer += dt;
                if (bot.taskTimer > 18f + bot.id * 1.3f)
                {
                    if (bot.role != PlayerRole.Crony) CompleteTask(target, bot.name);
                    else NoteTaskFinished(bot, tasks.IndexOf(target));
                    bot.taskTimer = 0f;
                    bot.botFinishedTask = true;
                    bot.nextTask = (bot.nextTask + 2) % tasks.Count;
                }
            }
        }

        void WanderAfterTask(MatchPlayer bot, float dt)
        {
            Vector2 destination = tasks[bot.nextTask % tasks.Count].position;
            Vector2 delta = destination - bot.position;
            if (delta.magnitude > 1f) MoveBotTowards(bot, destination, 1.8f, dt);
        }

        MatchPlayer FindNearestDowned(MatchPlayer from)
        {
            MatchPlayer nearest = null;
            float distance = 6f;
            foreach (MatchPlayer player in players)
            {
                if (!player.alive || !player.downed || reportedPlayers.Contains(player.id)) continue;
                float current = Vector2.Distance(from.position, player.position);
                if (current < distance) { nearest = player; distance = current; }
            }
            return nearest;
        }

        void UpdateImpostorBot(MatchPlayer bot, float dt)
        {
            if (human != null && humanRole == PlayerRole.Crewmate && human.alive && !human.downed && Vector2.Distance(bot.position, human.position) < 1.55f && killCooldown <= 0f && HasLineOfSight(bot.position, human.position))
            {
                Incapacitate(human, bot.name);
                killCooldown = 30f;
                return;
            }
            MatchPlayer target = null;
            float nearest = 7f;
            foreach (MatchPlayer candidate in players)
            {
                if (candidate.id == bot.id || candidate.role != PlayerRole.Crewmate || !candidate.alive || candidate.downed) continue;
                float distance = Vector2.Distance(bot.position, candidate.position);
                if (distance < nearest) { nearest = distance; target = candidate; }
            }
            if (target != null && nearest > 1.7f) MoveBotTowards(bot, target.position, 1.8f, dt);
            else if (nearest > 7f || target == null) Wander(bot, dt);
            if (killCooldown <= 0f && target != null && nearest <= 1.7f && HasLineOfSight(bot.position, target.position) && UnityEngine.Random.value < 1f - Mathf.Pow(1f - .008f, dt / .22f))
            {
                Incapacitate(target, bot.name); killCooldown = 30f;
            }
        }

        void Wander(MatchPlayer player, float dt)
        {
            player.taskTimer -= dt;
            if (player.taskTimer <= 0f)
            {
                player.nextTask = UnityEngine.Random.Range(0, tasks.Count);
                player.taskTimer = UnityEngine.Random.Range(2f, 5f);
            }
            Vector2 destination = tasks[player.nextTask].position + new Vector2(.8f, -.8f);
            Vector2 delta = destination - player.position;
            if (delta.sqrMagnitude > .1f) MoveBotTowards(player, destination, 1.4f, dt);
        }

        void MoveBotTowards(MatchPlayer bot, Vector2 destination, float speed, float dt)
        {
            if (worldMap == null) { MovePlayer(bot, (destination - bot.position).normalized * speed * dt); return; }
            Vector2 worldFrom = MapToWorld(bot.position);
            Vector2 worldGoal = worldMap.NearestWalkable(MapToWorld(destination));
            if ((worldFrom - worldGoal).sqrMagnitude < 9f && worldMap.ClearSegment(worldFrom, worldGoal))
            {
                Vector2 target = WorldToMap(worldGoal);
                MovePlayer(bot, Vector2.ClampMagnitude(target - bot.position, speed * dt));
                return;
            }
            bot.routeRetry -= dt;
            if (bot.route == null || (bot.routeGoal - worldGoal).sqrMagnitude > 1f || bot.routeRetry <= 0f)
            {
                bot.route = worldMap.FindPath(worldFrom, worldGoal);
                bot.routeStep = 0; bot.routeGoal = worldGoal; bot.routeRetry = 10f;
            }
            while (bot.routeStep < bot.route.Count && Vector2.Distance(worldFrom, bot.route[bot.routeStep]) < .15f) bot.routeStep++;
            if (bot.routeStep >= bot.route.Count) return;
            Vector2 waypoint = WorldToMap(bot.route[bot.routeStep]);
            MovePlayer(bot, Vector2.ClampMagnitude(waypoint - bot.position, speed * dt));
        }

        TaskStation FindNextOpenTask(MatchPlayer bot)
        {
            for (int n = 0; n < tasks.Count; n++)
            {
                int index = (bot.nextTask + n + bot.id) % tasks.Count;
                if (!tasks[index].complete) return tasks[index];
            }
            return null;
        }

        bool HasLineOfSight(Vector2 a, Vector2 b)
        {
            if (worldMap != null) return worldMap.ClearSegment(MapToWorld(a), MapToWorld(b));
            for (int i = 1; i < 12; i++) if (IsBlocked(Vector2.Lerp(a, b, i / 12f))) return false;
            return true;
        }

        void UpdateCamera()
        {
            if (human == null || gameCamera == null) return;

            Vector2 playerWorld = MapToWorld(human.position);
            float halfHeight = gameCamera.orthographicSize;
            float halfWidth = halfHeight * gameCamera.aspect;
            float worldHalfWidth = worldMap != null ? worldMap.Bounds.width * .5f : 16f * MapScaleX;
            float worldHalfHeight = worldMap != null ? worldMap.Bounds.height * .5f : 11f * MapScaleY;
            float minX = -worldHalfWidth + halfWidth;
            float maxX = worldHalfWidth - halfWidth;
            float minY = -worldHalfHeight + halfHeight;
            float maxY = worldHalfHeight - halfHeight;
            Vector3 target = new Vector3(
                minX <= maxX ? Mathf.Clamp(playerWorld.x, minX, maxX) : 0f,
                minY <= maxY ? Mathf.Clamp(playerWorld.y, minY, maxY) : 0f,
                -10f);

            gameCamera.transform.position = Vector3.SmoothDamp(gameCamera.transform.position, target, ref cameraVelocity, .12f);
            // Keep the vision circle on the player even when the camera stops at a map edge.
            if (fogImage != null)
            {
                Vector3 viewport = gameCamera.WorldToViewportPoint(playerWorld + new Vector2(0f, .5f));
                RectTransform fogRect = fogImage.rectTransform;
                fogRect.anchorMin = new Vector2(viewport.x - 1f, viewport.y - 1f);
                fogRect.anchorMax = new Vector2(viewport.x + 1f, viewport.y + 1f);
                fogRect.offsetMin = fogRect.offsetMax = Vector2.zero;
            }
        }

        void UpdateNearbyPrompt()
        {
            if (interactLabel == null || human == null) return;
            MatchPlayer downed = FindNearbyDowned();
            if (downed != null) interactLabel.text = humanRole != PlayerRole.Impostor ? "E  ·  Help " + downed.name + " up" : "E  ·  Move along";
            else if (currentSabotage >= 0 && Vector2.Distance(human.position, tasks[currentSabotage].position) < 2.5f) interactLabel.text = "E  ·  Restore " + SabotageNames[currentSabotage];
            else if (humanRole != PlayerRole.Impostor)
            {
                TaskStation nearest = FindNearbyTask();
                interactLabel.text = nearest == null ? "WASD to move   •   E interact   •   R report   •   M meeting" : "E  ·  " + nearest.title;
            }
            else if (HollowStumps.Nearest(MapToWorld(human.position)) >= 0)
                interactLabel.text = stumpCooldown > 0f ? "Hollow stump settling · " + Mathf.CeilToInt(stumpCooldown) + "s" : "E  ·  Slip into the hollow stump";
            else interactLabel.text = "WASD to move   •   F knock down   •   Q sabotage";
        }

        TaskStation FindNearbyTask()
        {
            if (human == null) return null;
            TaskStation closest = null; float best = InteractRange;
            foreach (TaskStation task in tasks)
            {
                if (task.complete) continue;
                float distance = Vector2.Distance(human.position, task.position);
                if (distance < best) { closest = task; best = distance; }
            }
            return closest;
        }

        MatchPlayer FindNearbyDowned()
        {
            if (human == null) return null;
            foreach (MatchPlayer player in players)
                if (!player.isHuman && player.alive && player.downed && !reportedPlayers.Contains(player.id) && Vector2.Distance(human.position, player.position) < InteractRange) return player;
            return null;
        }

        bool TryStumpTravel()
        {
            int stump = HollowStumps.Nearest(MapToWorld(human.position));
            if (stump < 0) return false;
            if (stumpCooldown > 0f) { ShowToast("The stump tunnel needs " + Mathf.CeilToInt(stumpCooldown) + "s to settle."); return true; }
            foreach (MatchPlayer observer in Observers(human)) Remember(observer, human, null, EvidenceType.NearStump, human.position, NearestTaskName(human.position));
            human.position = WorldToMap(HollowStumps.ExitFrom(stump));
            foreach (MatchPlayer observer in Observers(human)) Remember(observer, human, null, EvidenceType.NearStump, human.position, NearestTaskName(human.position));
            if (human.root != null) { Vector2 w = MapToWorld(human.position); human.root.transform.position = new Vector3(w.x, w.y, 0f); }
            stumpCooldown = HollowStumps.Cooldown;
            GameAudioManager.PlayStumpTravel();
            return true;
        }

        void Interact()
        {
            if (phase != RoundPhase.Playing || human == null || human.downed || activeOverlay.Length > 0) return;
            if (humanRole == PlayerRole.Impostor && TryStumpTravel()) return;
            MatchPlayer downed = FindNearbyDowned();
            if (downed != null)
            {
                if (humanRole != PlayerRole.Impostor) { downed.downed = false; downed.downedTimer = 0f; downed.root.transform.localScale = Vector3.one; downed.body.color = downed.outfitColor; AddEvidence("You helped " + downed.name + " back up."); GameAudioManager.PlayRepaired(); ShowToast("" + downed.name + " is safe again."); }
                return;
            }
            if (currentSabotage >= 0 && Vector2.Distance(human.position, tasks[currentSabotage].position) < 2.5f)
            {
                FixSabotage(); return;
            }
            if (humanRole != PlayerRole.Impostor)
            {
                TaskStation task = FindNearbyTask();
                if (task != null) OpenTask(task);
                else ShowToast("Move closer to a task station.");
            }
            else ShowToast("Blend in and use F near a player to incapacitate.");
        }

        void OpenTask(TaskStation task)
        {
            activeTask = task;
            activeOverlay = "task";
            TownshipTaskMiniGameUI.OpenOffline(task, SubmitOfflineMiniGameAction,
                () => { if (humanRole == PlayerRole.Crony) FakeTask(task); else CompleteTask(task, "You"); },
                () => { if (activeTask == task) { activeTask = null; activeOverlay = string.Empty; } });
        }

        void FakeTask(TaskStation task)
        {
            if (activeTask == task) { activeTask = null; activeOverlay = string.Empty; }
            ShowToast("Faked it. You looked busy, but the crew's progress didn't move.", 3f);
        }

        void SubmitOfflineMiniGameAction(int action)
        {
            TaskStation task = activeTask;
            // A crony's work is for show; it never moves the shared station.
            if (humanRole == PlayerRole.Crony) return;
            if (task == null || task.complete || task.solution == null || task.solution.Length == 0 ||
                task.step < 0 || task.step >= task.solution.Length || task.actions == null || action < 0 || action >= task.actions.Length)
                return;

            if (task.solution[task.step] == action) task.step++;
            else task.step = 0;
        }

        void CloseTask()
        {
            if (TownshipTaskMiniGameUI.IsOpen) TownshipTaskMiniGameUI.Close();
            activeTask = null; activeOverlay = ""; DestroyOverlay();
        }

        void CompleteTask(TaskStation task, string who)
        {
            if (task == null || task.complete) return;
            task.complete = true; task.step = task.solution.Length;
            CheckpointLabels.SetOfflineComplete((TownshipTaskKind)tasks.IndexOf(task), true);
            if (task == activeTask)
            {
                if (TownshipTaskMiniGameUI.IsOpen) TownshipTaskMiniGameUI.Close();
                activeTask = null;
                activeOverlay = "";
                DestroyOverlay();
                if (who != "You") ShowToast(who + " finished this task first.", 2f);
            }
            if (task.marker != null) { task.marker.color = Hex("76D2AC"); GlowLight.Attach(task.marker.transform, GlowProfile.Done); }
            NoteTaskFinished(who == "You" ? human : players.Find(p => p.name == who), tasks.IndexOf(task));
            AddEvidence(who + " completed " + task.title + ".");
            if (who == "You") ShowToast("Task complete · " + task.title);
            CheckVictory();
            UpdateHud();
        }

        void UpdateMatchClock()
        {
            matchTimer -= Time.deltaTime;
            if (killCooldown > 0f) killCooldown -= Time.deltaTime;
            if (meetingCooldown > 0f) meetingCooldown -= Time.deltaTime;
            if (sabotageCooldown > 0f) sabotageCooldown -= Time.deltaTime;
            if (stumpCooldown > 0f) stumpCooldown -= Time.deltaTime;
            if (currentSabotage >= 0)
            {
                sabotageTimer -= Time.deltaTime;
                if (sabotageTimer <= 0f) EndMatch(PlayerRole.Impostor, "The sabotage timer ran out.");
            }
            if (matchTimer <= 0f && phase == RoundPhase.Playing) EndMatch(PlayerRole.Crewmate, "The park stayed safe until closing time.");
        }

        void CheckVictory()
        {
            if (phase != RoundPhase.Playing) return;
            int remaining = 0; foreach (TaskStation task in tasks) if (!task.complete) remaining++;
            if (tasks.Count > 0 && remaining == 0)
            {
                EndMatch(PlayerRole.Crewmate, "All community tasks are complete.");
                return;
            }
            int livingCrew = 0; int livingImpostorTeam = 0; int livingImpostor = 0;
            foreach (MatchPlayer player in players)
            {
                if (!player.alive || player.downed) continue;
                if (player.role == PlayerRole.Crewmate) livingCrew++;
                else livingImpostorTeam++;
                if (player.role == PlayerRole.Impostor) livingImpostor++;
            }
            if (livingImpostor == 0)
            {
                EndMatch(PlayerRole.Crewmate, "The Impostor was identified and sent home.");
                return;
            }
            if (livingCrew <= livingImpostorTeam)
            {
                EndMatch(PlayerRole.Impostor, cronyPartner != null ? "The Impostor and their crony outnumbered the crew." : "The Impostor outnumbered the remaining crew.");
                return;
            }
        }

        void Incapacitate(MatchPlayer target, string source)
        {
            if (target == null || target.downed || !target.alive || target.role != PlayerRole.Crewmate) return;
            target.downed = true; target.downedTimer = 0f; target.body.color = Hex("9FB9B9");
            GameAudioManager.PlayIncapacitate();
            MatchPlayer attacker = source == "You" ? human : players.Find(p => p.name == source);
            if (attacker != null)
                foreach (MatchPlayer bot in players)
                    if (!bot.isHuman && bot.alive && !bot.downed && bot.role == PlayerRole.Crewmate && bot != target &&
                        Vector2.Distance(bot.position, target.position) < 6f && HasLineOfSight(bot.position, target.position))
                    Remember(bot, attacker, target, EvidenceType.WitnessedTakedown, target.position, NearestTaskName(target.position));
            target.root.transform.localScale = new Vector3(.8f, .46f, 1f);
            if (humanRole != PlayerRole.Impostor && human != null && Vector2.Distance(human.position, target.position) < 7.5f && HasLineOfSight(human.position, target.position))
            {
                target.suspicion += .7f; RecordSuspicion(source, 1f);
                AddEvidence("You saw " + source + " near " + target.name + " when they went down.");
            }
            else AddEvidence("A player was found needing help near " + NearestTaskName(target.position) + ".");
            ShowToast(attacker == human ? "You took " + target.name + " down. Slip away before anyone sees." : target.name + " is down and needs help. Report them or revive them.", 3f);
            CheckVictory();
        }

        string NearestTaskName(Vector2 position)
        {
            TaskStation closest = tasks[0]; float best = float.MaxValue;
            foreach (TaskStation task in tasks) { float d = Vector2.Distance(position, task.position); if (d < best) { best = d; closest = task; } }
            return closest.location;
        }

        void TryHumanIncapacitate()
        {
            if (phase != RoundPhase.Playing || humanRole != PlayerRole.Impostor || human == null || activeOverlay.Length > 0) return;
            if (killCooldown > 0f) { ShowToast("Ability ready in " + Mathf.CeilToInt(killCooldown) + "s."); return; }
            MatchPlayer target = null; float distance = 1.9f;
            foreach (MatchPlayer player in players)
            {
                if (player.isHuman || !player.alive || player.downed || player.role != PlayerRole.Crewmate) continue;
                float current = Vector2.Distance(human.position, player.position);
                if (current < distance && HasLineOfSight(human.position, player.position)) { target = player; distance = current; }
            }
            if (target == null) { ShowToast("Move closer to a Crewmate first."); return; }
            Incapacitate(target, "You"); killCooldown = 28f;
        }

        int LivingCrewCount() { int count = 0; foreach (MatchPlayer p in players) if (p.alive && !p.downed && p.role == PlayerRole.Crewmate) count++; return count; }

        // Recruiting must never end the round on the spot, so at least three crewmates must be standing.
        bool CanRecruitNow() => !recruitUsed && MatchLength - matchTimer >= RecruitUnlockSeconds && LivingCrewCount() >= 3;

        void TryHumanRecruit()
        {
            if (phase != RoundPhase.Playing || humanRole != PlayerRole.Impostor || human == null || activeOverlay.Length > 0) return;
            if (recruitUsed) { ShowToast("You already have a crony this match."); return; }
            float wait = RecruitUnlockSeconds - (MatchLength - matchTimer);
            if (wait > 0f) { ShowToast("Recruiting unlocks in " + Mathf.CeilToInt(wait) + "s. Earn some trust first."); return; }
            if (!CanRecruitNow()) { ShowToast("Too few crewmates left to recruit safely."); return; }
            MatchPlayer target = null; float distance = 1.9f;
            foreach (MatchPlayer player in players)
            {
                if (player.isHuman || !player.alive || player.downed || player.role != PlayerRole.Crewmate) continue;
                float current = Vector2.Distance(human.position, player.position);
                if (current < distance && HasLineOfSight(human.position, player.position)) { target = player; distance = current; }
            }
            if (target == null) { ShowToast("Stand next to a Crewmate to whisper the offer."); return; }
            Recruit(human, target);
        }

        void TryBotRecruit(MatchPlayer impostor, float dt)
        {
            if (!CanRecruitNow() || UnityEngine.Random.value > 1f - Mathf.Pow(1f - .01f, dt / .22f)) return;
            foreach (MatchPlayer candidate in players)
            {
                if (candidate.id == impostor.id || candidate.role != PlayerRole.Crewmate || !candidate.alive || candidate.downed) continue;
                if (Vector2.Distance(impostor.position, candidate.position) < 1.8f && HasLineOfSight(impostor.position, candidate.position)) { Recruit(impostor, candidate); return; }
            }
        }

        void Recruit(MatchPlayer impostor, MatchPlayer target)
        {
            recruitUsed = true;
            target.role = PlayerRole.Crony;
            cronyPartner = target;
            if (target.isHuman || impostor.isHuman) GameAudioManager.PlayRecruit();
            if (target.isHuman)
            {
                humanRole = PlayerRole.Crony;
                // Loyal sidekicks can stir trouble too.
                sabotageButtonLabel = AddHudButton("Sabotage  [Q]", new Vector2(-208f, 120f), ShowSabotageMenu, Hex("AD5160")).GetComponentInChildren<Text>();
                ShowToast(impostor.name + " pulled you aside: you are now their CRONY. Help them win. Your tasks no longer count, but you can sabotage [Q].", 6f);
            }
            else if (impostor.isHuman) ShowToast(target.name + " is now your crony. They will cover for you and stop helping the crew.", 4f);
            UpdateHud();
        }

        void ShowSabotageMenu()
        {
            if (phase != RoundPhase.Playing || humanRole == PlayerRole.Crewmate) return;
            activeOverlay = "sabotage"; DestroyOverlay();
            MakePanel(overlayRoot, "Sabotage Shade", new Color(.015f, .06f, .08f, .74f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Sabotage Panel", Hex("172A36"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(610f, 570f));
            AddOutline(panel, Hex("DE7A73"), 3f);
            MakeLabel(panel.transform, "SABOTAGE", new Vector2(0f, 215f), new Vector2(500f, 50f), 32, Hex("FFF4D7"), FontStyle.Bold);
            MakeLabel(panel.transform, "Choose a fictional park disruption. Crew can restore it at the marked station.", new Vector2(0f, 162f), new Vector2(520f, 44f), 14, Hex("C4D5D3"), FontStyle.Normal);
            for (int i = 0; i < SabotageNames.Length; i++)
            {
                int sabotage = i;
                string label = SabotageNames[i] + "  ·  " + SabotageLocations[i];
                AddMenuButton(panel.transform, label, new Vector2(0f, 92f - i * 58f), () => TriggerSabotage(sabotage), false, new Vector2(490f, 46f));
            }
            AddMenuButton(panel.transform, "Close", new Vector2(0f, -226f), () => { activeOverlay = ""; DestroyOverlay(); }, false, new Vector2(200f, 44f));
        }

        static readonly string[] SabotageNames = SabotageInfo.Names;
        static readonly string[] SabotageLocations = SabotageInfo.Locations;
        static readonly int[] SabotageStations = { 0, 7, 6, 7, 8 };

        void TriggerSabotage(int index)
        {
            if (currentSabotage >= 0) { ShowToast("A disruption is already active."); return; }
            if (sabotageCooldown > 0f) { ShowToast("Sabotage controls are cooling down."); return; }
            currentSabotage = index; sabotageTimer = 32f; activeOverlay = ""; DestroyOverlay();
            TaskStation repairStation = tasks[SabotageStations[index]];
            if (repairStation.marker != null) GlowLight.Attach(repairStation.marker.transform, GlowProfile.Alarm);
            sabotageCooldown = 45f;
            GameAudioManager.PlaySabotage();
            GameAudioManager.SetMusic(MusicMood.Meeting);
            AddEvidence("A disruption affected " + SabotageLocations[index] + ".");
            ShowToast(SabotageNames[index] + " · restore it within 32 seconds!", 3f);
            if (index == 1) blackout = true;
            UpdateVisionMask();
        }

        void FixSabotage()
        {
            string fixedName = SabotageNames[currentSabotage];
            if (currentSabotage == 1) blackout = false;
            TaskStation repaired = tasks[SabotageStations[currentSabotage]];
            lastRepairPlace = SabotageLocations[currentSabotage];
            foreach (MatchPlayer observer in Observers(human)) Remember(observer, human, null, EvidenceType.DisruptionRepair, human.position, lastRepairPlace);
            if (repaired.marker != null) GlowLight.Attach(repaired.marker.transform, repaired.complete ? GlowProfile.Done : GlowProfile.Beacon);
            currentSabotage = -1; sabotageTimer = 0f;
            GameAudioManager.PlayRepaired();
            GameAudioManager.SetMusic(MusicMood.Park);
            AddEvidence("The crew restored " + fixedName + ".");
            ShowToast("Disruption repaired.");
            UpdateVisionMask(); UpdateHud();
        }

        void CallEmergency()
        {
            if (phase != RoundPhase.Playing || activeOverlay.Length > 0) return;
            if (emergencyUsed) { ShowToast("Your emergency meeting has been used."); return; }
            if (meetingCooldown > 0f) { ShowToast("Meeting available in " + Mathf.CeilToInt(meetingCooldown) + "s."); return; }
            emergencyUsed = true;
            meetingReporter = -1;
            StartMeeting(-1, "You called an emergency meeting.");
        }

        void ReportNearby()
        {
            if (phase != RoundPhase.Playing || humanRole == PlayerRole.Impostor || activeOverlay.Length > 0) return;
            MatchPlayer body = FindNearbyDowned();
            if (body == null) { ShowToast("No one nearby needs help."); return; }
            ReportPlayer(body, human);
        }

        void ReportPlayer(MatchPlayer body, MatchPlayer reporter)
        {
            if (body == null) return;
            meetingReporter = reporter != null ? reporter.id : -1;
            foreach (MatchPlayer nearby in players)
                if (nearby != body && nearby.alive && !nearby.downed && Vector2.Distance(nearby.position, body.position) <= BotDiscussionEngine.DiscoveryRadius)
                    foreach (MatchPlayer observer in Observers(nearby))
                        Remember(observer, nearby, body, EvidenceType.BodyDiscovery, nearby.position, NearestTaskName(body.position));
            body.downed = false; body.alive = false; reportedPlayers.Add(body.id);
            if (body.root != null) body.root.SetActive(false);
            meetingBody = body.id;
            string who = reporter == null ? "Someone" : reporter.isHuman ? "You" : reporter.name;
            StartMeeting(body.id, who + " found " + body.name + " down near " + NearestTaskName(body.position) + ".");
        }

        bool HasUnreportedBody()
        {
            foreach (MatchPlayer player in players) if (!player.isHuman && player.alive && player.downed && !reportedPlayers.Contains(player.id)) return true;
            return false;
        }

        void ReportNearestBotBody()
        {
            foreach (MatchPlayer player in players)
            {
                if (player.isHuman || !player.alive || !player.downed || reportedPlayers.Contains(player.id)) continue;
                MatchPlayer finder = null; float best = float.MaxValue;
                foreach (MatchPlayer bot in players)
                {
                    if (bot.isHuman || !bot.alive || bot.downed || bot == player) continue;
                    float d = Vector2.Distance(bot.position, player.position);
                    if (d < best) { best = d; finder = bot; }
                }
                ReportPlayer(player, finder);
                return;
            }
        }

        void StartMeeting(int bodyId, string reason)
        {
            if (phase != RoundPhase.Playing) return;
            if (TownshipTaskMiniGameUI.IsOpen) TownshipTaskMiniGameUI.Close();
            activeTask = null; DestroyOverlay(); activeOverlay = "meeting";
            phase = RoundPhase.Meeting; meetingBody = bodyId; humanVote = -2; meetingCooldown = 25f;
            if (bodyId >= 0) GameAudioManager.PlayReport(); else GameAudioManager.PlayEmergencyKlaxon();
            GameAudioManager.SetMusic(MusicMood.Meeting);
            AddEvidence(reason);
            meetingBodyReport = bodyId >= 0;
            meetingHeadline = reason;
            meetingOutcome = meetingEjectedRole = "";
            meetingEjected = -1;
            accusations.Clear(); accusedBy.Clear(); humanAccused.Clear(); meetingVotes.Clear(); botVoteTimes.Clear(); botLines.Clear(); pendingReactions.Clear();
            SetStage(MeetingStage.Discussion, DiscussSeconds);
            PlanBotStatements();
            MeetingScreen.Show(overlayRoot, BuildMeetingView, OnMeetingSeat, () => CastMeetingVote(-1), largeText);
        }

        void SetStage(MeetingStage stage, float seconds) { meetingStage = stage; stageTimer = stageLength = seconds; }

        bool CanMeetingVote(MatchPlayer p) => p != null && p.alive && !p.downed;
        float Accused(int id) => accusations.TryGetValue(id, out float value) ? value : 0f;
        string SeatName(MatchPlayer p) => p.isHuman ? PlayerPrefs.GetString("Crony.PlayerName", "You") : p.name;

        void UpdateMeeting()
        {
            stageTimer -= Time.deltaTime;
            float elapsed = stageLength - stageTimer;
            if (meetingStage == MeetingStage.Discussion)
                foreach (BotLine line in botLines)
                {
                    if (line.Said || line.At > elapsed || !CanMeetingVote(line.Who)) continue;
                    line.Said = true;
                    TextChatManager.Instance?.Receive((ulong)(1000 + line.Who.id), "practice:" + line.Who.id, line.Who.name, line.Text, ChatChannel.Meeting, Guid.NewGuid().ToString("N"));
                    if (line.Defends >= 0) accusations[line.Defends] = Mathf.Max(0f, Accused(line.Defends) - line.Weight);
                    if (line.Accuse >= 0)
                    {
                        accusations[line.Accuse] = Accused(line.Accuse) + line.Weight;
                        accusedBy[line.Accuse] = line.Who.id;
                        pendingReactions.Add(new Vector2Int(line.Accuse, line.Who.id));
                    }
                }
            if (meetingStage == MeetingStage.Discussion && pendingReactions.Count > 0)
            {
                // Queued outside the loop above, which is still walking botLines.
                foreach (Vector2Int accusation in pendingReactions) QueueReactions(accusation.x, accusation.y, elapsed);
                pendingReactions.Clear();
            }
            else if (meetingStage == MeetingStage.Voting)
            {
                foreach (KeyValuePair<int, float> plan in botVoteTimes)
                    if (!meetingVotes.ContainsKey(plan.Key) && plan.Value <= elapsed && CanMeetingVote(GetPlayer(plan.Key)))
                    { meetingVotes[plan.Key] = ChooseBotVote(GetPlayer(plan.Key)); GameAudioManager.PlayVote(); }
                bool everyone = true;
                foreach (MatchPlayer p in players) if (CanMeetingVote(p) && !meetingVotes.ContainsKey(p.id)) everyone = false;
                if (everyone) stageTimer = Mathf.Min(stageTimer, 0f);
            }
            if (stageTimer > 0f) return;
            switch (meetingStage)
            {
                case MeetingStage.Discussion:
                    SetStage(MeetingStage.Voting, VoteSeconds);
                    foreach (MatchPlayer p in players) if (!p.isHuman && CanMeetingVote(p)) botVoteTimes[p.id] = UnityEngine.Random.Range(2f, 14f);
                    break;
                case MeetingStage.Voting: TallyMeeting(); SetStage(MeetingStage.Reveal, RevealSeconds); break;
                case MeetingStage.Reveal: ApplyEjection(); SetStage(MeetingStage.Ejection, EjectSeconds); break;
                default: EndMeeting(); break;
            }
        }

        // ---- Bot evidence: every half second each bot notes what it can see (BotMemoryManager) ----

        List<MatchPlayer> Observers(MatchPlayer actor)
        {
            List<MatchPlayer> seen = new List<MatchPlayer>();
            if (actor == null) return seen;
            foreach (MatchPlayer bot in players)
                if (!bot.isHuman && bot != actor && bot.alive && !bot.downed &&
                    Vector2.Distance(bot.position, actor.position) < 6f && HasLineOfSight(bot.position, actor.position))
                    seen.Add(bot);
            return seen;
        }

        void Remember(MatchPlayer observer, MatchPlayer actor, MatchPlayer target, EvidenceType type, Vector2 at, string place) =>
            botMemory.LogSpatialEvent(observer.id, new MemoryEntry
            {
                timestamp = RoundClock, actorID = actor.id, targetID = target != null ? target.id : -1, position = at, type = type, place = place
            });

        void ObserveEvidence()
        {
            TaskStation repairStation = currentSabotage >= 0 ? tasks[SabotageStations[currentSabotage]] : null;
            foreach (MatchPlayer actor in players)
            {
                if (!actor.alive || actor.downed) continue;
                MatchPlayer nearBody = null;
                foreach (MatchPlayer other in players)
                    if (other != actor && other.alive && other.downed && !reportedPlayers.Contains(other.id) && Vector2.Distance(actor.position, other.position) < 2.5f)
                    { nearBody = other; break; }
                // Stumps sit beside checkpoints, so standing by one only counts when the player isn't at a task.
                bool atStump = HollowStumps.Nearest(MapToWorld(actor.position)) >= 0 && !NearAnyTask(actor.position);
                bool ignoringDisruption = repairStation != null && Vector2.Distance(actor.position, repairStation.position) < 2.5f;
                if (nearBody == null && !atStump && !ignoringDisruption) continue;
                foreach (MatchPlayer observer in Observers(actor))
                {
                    if (nearBody != null && observer != nearBody) Remember(observer, actor, nearBody, EvidenceType.ProximityToTarget, actor.position, NearestTaskName(actor.position));
                    if (atStump) Remember(observer, actor, null, EvidenceType.NearStump, actor.position, NearestTaskName(actor.position));
                    if (ignoringDisruption) Remember(observer, actor, null, EvidenceType.DisruptionIgnored, actor.position, SabotageLocations[currentSabotage]);
                }
            }
        }

        bool NearAnyTask(Vector2 position)
        {
            foreach (TaskStation task in tasks) if (Vector2.Distance(position, task.position) < 2.5f) return true;
            return false;
        }

        /// <summary>A checkpoint was finished (or faked by a crony): that player's alibi, and an alibi for anyone who saw it.</summary>
        void NoteTaskFinished(MatchPlayer actor, int taskIndex)
        {
            if (actor == null || taskIndex < 0) return;
            actor.lastTask = taskIndex;
            foreach (MatchPlayer observer in Observers(actor)) Remember(observer, actor, null, EvidenceType.TaskAlibi, actor.position, StationTitle(taskIndex));
        }

        string StationTitle(int taskIndex)
        {
            if (stationTitles == null)
            {
                stationTitles = new string[tasks.Count];
                for (int i = 0; i < stationTitles.Length; i++) stationTitles[i] = TaskMiniGame.Create((TownshipTaskKind)i).Title + " station";
            }
            return stationTitles[Mathf.Clamp(taskIndex, 0, stationTitles.Length - 1)];
        }

        // Before a first finished task, each player claims the station they were heading to (spread by id so bots differ).
        string AlibiOf(MatchPlayer p) => StationTitle(p.lastTask >= 0 ? p.lastTask : (p.nextTask + p.id) % tasks.Count);

        /// <summary>How bots address someone out loud: the human by their chosen name (never "You").</summary>
        string SpokenName(MatchPlayer p)
        {
            if (!p.isHuman) return p.name;
            string saved = PlayerPrefs.GetString("Crony.PlayerName", string.Empty);
            return string.IsNullOrWhiteSpace(saved) ? p.name : saved;
        }

        /// <summary>
        /// Opens the Discussion stage: every bot rebuilds its suspicion row from memory, then says one opening line
        /// chosen by its role (BotDiscussionEngine). Accusers speak early; replies are queued as accusations land.
        /// </summary>
        void PlanBotStatements()
        {
            botMemory.Prune(RoundClock);
            reactedThisMeeting.Clear();
            meetingContext.Present.Clear();
            foreach (MatchPlayer p in players) if (CanMeetingVote(p)) meetingContext.Present.Add(p.id);
            meetingContext.NameOf = id => SpokenName(GetPlayer(id));
            meetingContext.RoleOf = id => GetPlayer(id).role;
            meetingContext.AlibiOf = id => AlibiOf(GetPlayer(id));
            meetingContext.BodyId = meetingBodyReport ? meetingBody : -1;
            meetingContext.DiscoveryTime = meetingBodyReport ? RoundClock : float.NegativeInfinity;
            meetingContext.ReporterId = meetingBodyReport ? meetingReporter : -1;
            meetingContext.LastRepairPlace = lastRepairPlace;

            List<MatchPlayer> bots = players.FindAll(p => !p.isHuman && CanMeetingVote(p));
            // All rows first: the Impostor's opening reads how much the crew already doubts each player.
            foreach (MatchPlayer bot in bots) discussion.Recompute(bot.id, botMemory.Recall(bot.id), meetingContext);
            foreach (MatchPlayer bot in bots)
            {
                DialogueLine line = discussion.GenerateDialogueLine(bot.id, meetingContext);
                float at = line.accuse >= 0 && line.weight >= 2f ? UnityEngine.Random.Range(1f, 5f) : UnityEngine.Random.Range(2.5f, DiscussSeconds - 2f);
                botLines.Add(new BotLine { Who = bot, At = at, Text = line.text, Accuse = line.accuse, Defends = line.defends, Weight = line.weight });
            }
        }

        // An opening line plus one reply keeps a 15-second discussion readable.
        const int MaxLinesPerBot = 2;
        int LinesPlanned(MatchPlayer bot) { int n = 0; foreach (BotLine l in botLines) if (l.Who == bot) n++; return n; }

        /// <summary>Queues up to two replies to an accusation: the accused first, then the crony, then anyone with evidence.</summary>
        void QueueReactions(int accused, int accuser, float elapsed)
        {
            if (elapsed > DiscussSeconds - 1.5f) return;
            List<MatchPlayer> candidates = players.FindAll(p => !p.isHuman && CanMeetingVote(p) && p.id != accuser);
            candidates.Sort((a, b) => Priority(a).CompareTo(Priority(b)));
            int Priority(MatchPlayer p) => p.id == accused ? 0 : p.role == PlayerRole.Crony ? 1 : 2;
            float delay = 0f; int replies = 0;
            foreach (MatchPlayer bot in candidates)
            {
                int key = bot.id * 1000 + accused;
                if (reactedThisMeeting.Contains(key) || LinesPlanned(bot) >= MaxLinesPerBot) continue;
                if (!discussion.TryReact(bot.id, accused, accuser, meetingContext, out DialogueLine line)) continue;
                // Someone who already vouched for this player doesn't need to say it twice.
                if (line.defends >= 0 && botLines.Exists(l => l.Who == bot && l.Defends == line.defends)) continue;
                reactedThisMeeting.Add(key);
                delay += UnityEngine.Random.Range(.9f, 1.8f);
                botLines.Add(new BotLine { Who = bot, At = elapsed + delay, Text = line.text, Accuse = line.accuse, Defends = line.defends, Weight = line.weight });
                if (++replies >= 2) break;
            }
        }

        void OnMeetingSeat(long id)
        {
            MatchPlayer target = GetPlayer((int)id);
            if (target == null || target.isHuman || !CanMeetingVote(human)) return;
            if (meetingStage == MeetingStage.Voting) { CastMeetingVote((int)id); return; }
            if (meetingStage != MeetingStage.Discussion || !humanAccused.Add(target.id)) return;
            TextChatManager.Instance?.Send("I suspect " + SeatName(target) + ".");
            accusations[target.id] = Accused(target.id) + 1.5f;
            accusedBy[target.id] = human.id;
            // The accused (and possibly the crony or a witness) answers a moment later.
            QueueReactions(target.id, human.id, stageLength - stageTimer);
        }

        void CastMeetingVote(int target)
        {
            if (meetingStage != MeetingStage.Voting || humanVote != -2 || !CanMeetingVote(human)) return;
            humanVote = target;
            meetingVotes[human.id] = target;
            GameAudioManager.PlayVote();
        }

        int ChooseBotVote(MatchPlayer bot)
        {
            MatchPlayer impostor = players.Find(p => p.role == PlayerRole.Impostor && CanMeetingVote(p));
            if (bot.role == PlayerRole.Crony) return impostor != null ? ImpostorPick(impostor) : -1;
            if (bot.role == PlayerRole.Impostor) return ImpostorPick(bot);
            int best = -1; float bestScore = 1.2f;
            foreach (MatchPlayer c in players)
            {
                if (c == bot || !CanMeetingVote(c)) continue;
                // The bot's own evidence counts most; what the room argued and older general suspicion add to it.
                float score = discussion.Suspicion(bot.id, c.id) * 3f + Accused(c.id) + c.suspicion * .5f + UnityEngine.Random.Range(0f, .5f);
                if (score > bestScore) { bestScore = score; best = c.id; }
            }
            return best;
        }

        int ImpostorPick(MatchPlayer impostor)
        {
            if (accusedBy.TryGetValue(impostor.id, out int accuser))
            {
                MatchPlayer a = GetPlayer(accuser);
                if (CanMeetingVote(a) && a.role == PlayerRole.Crewmate) return accuser;
            }
            int best = -1; float bestScore = .6f;
            foreach (MatchPlayer c in players)
            {
                if (!CanMeetingVote(c) || c.role != PlayerRole.Crewmate) continue;
                float score = c.suspicion * .8f + Accused(c.id) + UnityEngine.Random.Range(0f, .4f);
                if (score > bestScore) { bestScore = score; best = c.id; }
            }
            return best;
        }

        void TallyMeeting()
        {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            foreach (int target in meetingVotes.Values) counts[target] = (counts.TryGetValue(target, out int n) ? n : 0) + 1;
            int skips = counts.TryGetValue(-1, out int skipCount) ? skipCount : 0;
            int top = -1, topVotes = 0; bool tie = false;
            foreach (KeyValuePair<int, int> pair in counts)
            {
                if (pair.Key < 0) continue;
                if (pair.Value > topVotes) { top = pair.Key; topVotes = pair.Value; tie = false; }
                else if (pair.Value == topVotes) tie = true;
            }
            // Most votes wins; a tie, or as many skips as the leader, sends no one home.
            MatchPlayer ejected = !tie && topVotes > skips ? GetPlayer(top) : null;
            meetingEjected = ejected != null ? ejected.id : -1;
            if (ejected == null)
            {
                meetingOutcome = "No one was sent home.";
                meetingEjectedRole = topVotes == 0 && skips == 0 ? "Nobody voted." : tie ? "It was a tie." : "Most people skipped.";
                return;
            }
            meetingOutcome = SeatName(ejected) + " was sent home.";
            meetingEjectedRole = ejected.role == PlayerRole.Impostor ? SeatName(ejected) + " was the Impostor!" : ejected.role == PlayerRole.Crony ? SeatName(ejected) + " was the Impostor's crony!" : SeatName(ejected) + " was not the Impostor.";
        }

        void ApplyEjection()
        {
            AddEvidence(meetingOutcome + " " + meetingEjectedRole);
            MatchPlayer ejected = GetPlayer(meetingEjected);
            if (ejected == null) return;
            GameAudioManager.PlayEject();
            ejected.alive = false; ejected.downed = false; ejected.suspicion = 0f;
            if (ejected.root != null) ejected.root.SetActive(false);
        }

        void EndMeeting()
        {
            phase = RoundPhase.Playing; activeOverlay = ""; DestroyOverlay();
            GameAudioManager.SetMusic(MusicMood.Park);
            // Everyone regroups at the start area and the Impostor's ability cools down, like after any town hall.
            for (int i = 0; i < players.Count; i++)
            {
                MatchPlayer p = players[i];
                if (!p.alive || p.downed || spawnPoints == null) continue;
                p.position = spawnPoints[i % spawnPoints.Length];
                p.route = null;
                if (p.root != null) { Vector2 w = MapToWorld(p.position); p.root.transform.position = new Vector3(w.x, w.y, 0f); }
            }
            killCooldown = Mathf.Max(killCooldown, 15f);
            ShowToast(meetingOutcome + " " + meetingEjectedRole, 4f);
            CheckVictory();
        }

        MeetingView BuildMeetingView()
        {
            MeetingView v = meetingView;
            v.Seats.Clear(); v.Clues.Clear(); v.VotesByTarget.Clear();
            v.Stage = meetingStage; v.SecondsLeft = stageTimer; v.StageLength = stageLength;
            v.BodyReport = meetingBodyReport; v.Headline = meetingHeadline;
            foreach (MatchPlayer p in players)
                if (p.alive || p.id == meetingEjected)
                    v.Seats.Add(new MeetingSeat { Id = p.id, Name = SeatName(p), Color = p.avatarLayers != null ? (Color)p.avatarLayers.CurrentState.ClothingTint : p.outfitColor, CanVote = CanMeetingVote(p) || p.id == meetingEjected, HasVoted = meetingVotes.ContainsKey(p.id), IsLocal = p.isHuman, Portrait = p.avatarLayers != null ? p.avatarLayers.LayerRenderers : null });
            v.LocalVote = humanVote == -2 ? MeetingView.NoVote : humanVote;
            v.LocalCanVote = CanMeetingVote(human);
            for (int i = Mathf.Max(0, evidence.Count - 3); i < evidence.Count; i++) v.Clues.Add(evidence[i]);
            if (meetingStage >= MeetingStage.Reveal)
                foreach (KeyValuePair<int, int> vote in meetingVotes)
                {
                    long key = vote.Value < 0 ? MeetingView.Skip : vote.Value;
                    if (!v.VotesByTarget.TryGetValue(key, out List<long> voters)) v.VotesByTarget[key] = voters = new List<long>();
                    voters.Add(vote.Key);
                }
            v.Outcome = meetingOutcome; v.EjectedId = meetingEjected < 0 ? MeetingView.NoVote : meetingEjected; v.EjectedRole = meetingEjectedRole;
            return v;
        }

        MatchPlayer GetPlayer(int id) { foreach (MatchPlayer player in players) if (player.id == id) return player; return null; }

        void UpdateHud()
        {
            hudClock += Time.deltaTime;
            if (hudClock < .25f) return;
            hudClock = 0f;
            UpdateTaskTracker();
            if (hudStatus != null) hudStatus.text = "Township Park  •  " + Mathf.FloorToInt(matchTimer / 60f).ToString("00") + ":" + Mathf.FloorToInt(matchTimer % 60f).ToString("00") + (currentSabotage >= 0 ? "  •  " + SabotageNames[currentSabotage] + " " + Mathf.CeilToInt(sabotageTimer) + "s" : "");
            // The ability buttons count down their cooldowns so a press that does nothing yet is never a mystery.
            if (knockDownButtonLabel != null) knockDownButtonLabel.text = killCooldown > 0f ? "Knock Down  " + Mathf.CeilToInt(killCooldown) + "s" : "Knock Down  [F]";
            if (sabotageButtonLabel != null) sabotageButtonLabel.text = currentSabotage >= 0 ? "Sabotage  · active" : sabotageCooldown > 0f ? "Sabotage  " + Mathf.CeilToInt(sabotageCooldown) + "s" : "Sabotage  [Q]";
            // Recruiting is once per match, so the button leaves once it is spent.
            if (recruitButton != null && recruitButton.activeSelf == recruitUsed) recruitButton.SetActive(!recruitUsed);
            if (roleLabel != null) { roleLabel.text = RoleTitle(humanRole); roleLabel.color = humanRole == PlayerRole.Crewmate ? Hex("287866") : humanRole == PlayerRole.Crony ? Hex("7A3F8E") : Hex("A83F4B"); }
            if (taskListLabel != null)
            {
                string list = "COMMUNITY TASKS\n";
                for (int i = 0; i < tasks.Count; i++) list += (tasks[i].complete ? "✓  " : "○  ") + tasks[i].title + "\n";
                taskListLabel.text = list;
                taskListLabel.lineSpacing = 1.05f;
            }
            int count = 0; foreach (TaskStation task in tasks) if (task.complete) count++;
            if (taskProgressLabel != null) taskProgressLabel.text = "COMMUNITY PROGRESS  " + count + "/" + tasks.Count;
            if (taskProgressFill != null)
            {
                RectTransform rt = taskProgressFill.rectTransform;
                float ratio = count / (float)tasks.Count;
                rt.anchorMin = new Vector2(0f, .5f); rt.anchorMax = new Vector2(0f, .5f); rt.pivot = new Vector2(0f, .5f);
                rt.sizeDelta = new Vector2(Mathf.Max(2f, 288f * ratio), 10f);
            }
            UpdateNearbyPrompt();
        }

        Vector2 MapPanelPoint(Vector2 point)
        {
            if (worldMap != null)
            {
                Vector2 world = MapToWorld(point);
                return new Vector2((world.x - worldMap.Bounds.center.x) / worldMap.Bounds.width * 620f,
                    (world.y - worldMap.Bounds.center.y) / worldMap.Bounds.height * 518f);
            }
            return new Vector2(point.x * 20.5f, point.y * 16.5f);
        }

        Vector2 MapPanelSize(Vector2 size)
        {
            return new Vector2(size.x * 20.5f, size.y * 16.5f);
        }

        GameObject AddMapTile(Transform parent, string name, Vector2 point, Vector2 size, Color color, string label = null)
        {
            GameObject tile = MakePanel(parent, name, color, new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, MapPanelSize(size), MapPanelPoint(point));
            if (!string.IsNullOrEmpty(label))
            {
                Text tileLabel = MakeLabel(tile.transform, label, Vector2.zero, MapPanelSize(size) - new Vector2(8f, 4f), 10, Hex("263B43"), FontStyle.Bold);
                tileLabel.resizeTextForBestFit = true;
                tileLabel.raycastTarget = false;
            }
            return tile;
        }

        void ShowAuthoredMap()
        {
            MakePanel(overlayRoot, "Map Shade", new Color(.02f, .07f, .07f, .82f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Map Card", PixelUiTheme.Cream, new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(1120f, 760f));
            MakeLabel(panel.transform, "TOWNSHIP PARK", new Vector2(-180f, 324f), new Vector2(650f, 48f), 32, PixelUiTheme.Ink, FontStyle.Bold);
            MakeLabel(panel.transform, "Choose a task to track • Your position updates live", new Vector2(-180f, 280f), new Vector2(650f, 32f), 17, PixelUiTheme.MutedInk);
            GameObject map = new GameObject("Authored Park Overview", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
            map.transform.SetParent(panel.transform, false);
            RectTransform mapRect = map.GetComponent<RectTransform>();
            mapRect.anchorMin = mapRect.anchorMax = new Vector2(.5f, .5f);
            mapRect.sizeDelta = new Vector2(620f, 518f);
            mapRect.anchoredPosition = new Vector2(-210f, -10f);
            map.GetComponent<UnityEngine.UI.RawImage>().texture = worldMap.Overview;
            map.GetComponent<UnityEngine.UI.RawImage>().raycastTarget = false;
            MakeLabel(panel.transform, "COMMUNITY TASKS", new Vector2(325f, 260f), new Vector2(390f, 34f), 19, PixelUiTheme.Ink, FontStyle.Bold);
            for (int i = 0; i < tasks.Count; i++)
            {
                int index = i; TaskStation task = tasks[i];
                Color color = task.complete ? PixelUiTheme.Almond : trackedTask == i ? PixelUiTheme.Gold : PixelUiTheme.Linen;
                string label = (task.complete ? "DONE  " : (i + 1).ToString("00") + "  ") + task.title;
                GameObject row = MakeButton(panel.transform, label, new Vector2(325f, 211f - i * 46f), new Vector2(390f, 40f), color, () => TrackTask(index));
                row.GetComponent<Button>().interactable = !task.complete;
                GameObject pin = MakeButton(map.transform, (i + 1).ToString(), MapPanelPoint(task.position), new Vector2(28f, 28f), task.complete ? PixelUiTheme.Mint : PixelUiTheme.Gold, () => TrackTask(index));
                pin.GetComponent<Button>().interactable = !task.complete;
            }
            GameObject playerPin = MakePanel(map.transform, "You Are Here", PixelUiTheme.Cream, new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(16f, 16f), MapPanelPoint(human.position));
            playerPin.GetComponent<Image>().sprite = PixelUiTheme.GetDotSprite();
            playerPin.GetComponent<Image>().raycastTarget = false;
            mapPlayerMarker = playerPin.GetComponent<RectTransform>();
            MakeLabel(playerPin.transform, "YOU", new Vector2(0f, 22f), new Vector2(64f, 24f), 14, Color.white, FontStyle.Bold);
            MakeLabel(panel.transform, "WHITE: YOU    GOLD: OPEN TASK    GREEN: COMPLETE", new Vector2(-210f, -299f), new Vector2(650f, 30f), 14, PixelUiTheme.MutedInk, FontStyle.Bold);
            AddMenuButton(panel.transform, "Back to park  [Tab / Esc]", new Vector2(300f, -315f), () => { activeOverlay = ""; DestroyOverlay(); }, true, new Vector2(410f, 48f));
        }

        void TrackTask(int index)
        {
            if (index < 0 || index >= tasks.Count || tasks[index].complete) return;
            trackedTask = index;
            activeOverlay = ""; DestroyOverlay();
            ShowToast("Tracking " + tasks[index].title + ". Follow the direction at the bottom left.");
        }

        void UpdateTaskTracker()
        {
            if (trackedTaskLabel == null || human == null) return;
            if (humanRole == PlayerRole.Impostor)
            {
                trackedTaskLabel.text = "IMPOSTOR\nBlend in. Create a distraction.\nF: act   Q: sabotage   G: " + (recruitUsed ? "crony: " + cronyPartner?.name : "recruit a crony");
                return;
            }
            if (humanRole == PlayerRole.Crony)
            {
                MatchPlayer boss = null; foreach (MatchPlayer p in players) if (p.role == PlayerRole.Impostor) boss = p;
                trackedTaskLabel.text = "CRONY of " + (boss != null ? boss.name : "the Impostor") + "\nCover for them. Fake tasks to blend in.\nQ: sabotage   Tab: map";
                return;
            }
            if (trackedTask < 0 || trackedTask >= tasks.Count || tasks[trackedTask].complete)
            {
                trackedTask = -1; float best = float.MaxValue;
                for (int i = 0; i < tasks.Count; i++)
                {
                    if (tasks[i].complete) continue;
                    float distance = (tasks[i].position - human.position).sqrMagnitude;
                    if (distance < best) { best = distance; trackedTask = i; }
                }
            }
            if (trackedTask < 0) { trackedTaskLabel.text = "ALL TASKS COMPLETE\nStay together and watch for clues."; return; }
            TaskStation task = tasks[trackedTask];
            Vector2 direction = MapToWorld(task.position) - MapToWorld(human.position);
            string bearing = (direction.y > 2f ? "NORTH" : direction.y < -2f ? "SOUTH" : "") + (direction.x > 2f ? " EAST" : direction.x < -2f ? " WEST" : "");
            string hint = direction.magnitude < InteractRange * MapScaleY ? "NEARBY · Press E to interact" : bearing.Trim() + " · " + Mathf.CeilToInt(direction.magnitude) + " m · Tab for map";
            trackedTaskLabel.text = "NEXT · " + task.location.ToUpperInvariant() + "\n" + task.title + "\n" + hint;
        }

        void ShowMap()
        {
            if (phase != RoundPhase.Playing) return;
            if (activeOverlay.Length > 0) return;
            activeOverlay = "map"; mapOpenedFrame = Time.frameCount; DestroyOverlay();
            if (worldMap != null) { ShowAuthoredMap(); return; }
            MakePanel(overlayRoot, "Map Shade", new Color(.015f, .06f, .08f, .82f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Map Card", Hex("172A36"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(850f, 650f));
            AddOutline(panel, Hex("E6BD65"), 3f);
            MakeLabel(panel.transform, "TOWNSHIP PARK", new Vector2(0f, 282f), new Vector2(650f, 46f), 30, Hex("FFF4D7"), FontStyle.Bold);
            MakeLabel(panel.transform, "Explore the park • numbered markers show task stations", new Vector2(0f, 247f), new Vector2(680f, 26f), 13, Hex("C4D5D3"), FontStyle.Normal);

            GameObject map = MakePanel(panel.transform, "Park Overview", Hex("A8D19B"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(740f, 420f), new Vector2(0f, -5f));
            AddOutline(map, Hex("537A61"), 2f);

            // Paths and the lake define the broad park layout.
            AddMapTile(map.transform, "North Path", new Vector2(0f, 1.7f), new Vector2(31f, 1.55f), Hex("E9D8B3"));
            AddMapTile(map.transform, "South Path", new Vector2(0f, -5.1f), new Vector2(31f, 1.1f), Hex("E9D8B3"));
            AddMapTile(map.transform, "Central Path", new Vector2(0f, -1.4f), new Vector2(1.2f, 14f), Hex("E9D8B3"));
            AddMapTile(map.transform, "Market Path", new Vector2(-6.1f, 5.2f), new Vector2(14f, .8f), Hex("E9D8B3"));
            GameObject lakeRim = AddMapTile(map.transform, "Lake Rim", new Vector2(0f, -2.4f), new Vector2(7.5f, 3.5f), Hex("D6C99D"));
            lakeRim.GetComponent<Image>().sprite = circleSprite;
            GameObject lake = AddMapTile(map.transform, "Community Lake", new Vector2(0f, -2.4f), new Vector2(7.1f, 3.1f), Hex("63B9CC"));
            lake.GetComponent<Image>().sprite = circleSprite;
            GameObject island = AddMapTile(map.transform, "Lake Island", new Vector2(.25f, -2.1f), new Vector2(1.25f, .55f), Hex("77B67B"));
            island.GetComponent<Image>().sprite = circleSprite;

            AddMapTile(map.transform, "Hawker Stalls", new Vector2(-11.7f, 6.7f), new Vector2(4f, 2.5f), Hex("EE9A62"), "NASI LEMAK");
            AddMapTile(map.transform, "Convenience Store", new Vector2(9.1f, 6.7f), new Vector2(4.5f, 2.5f), Hex("76B7D4"), "KEDAI");
            AddMapTile(map.transform, "Community Hall", new Vector2(-.2f, 7.2f), new Vector2(3.8f, 1.6f), Hex("DBA765"), "DEWAN");
            AddMapTile(map.transform, "Administrative Office", new Vector2(9.2f, -.9f), new Vector2(3.8f, 2.2f), Hex("D8A7B2"), "OFFICE");
            AddMapTile(map.transform, "Cellular Tower", new Vector2(-10.5f, -7.3f), new Vector2(3.5f, 2.4f), Hex("9297C8"), "TOWER");
            AddMapTile(map.transform, "Bus Stop", new Vector2(11f, -7.3f), new Vector2(3.4f, 2.2f), Hex("E0BF64"), "BUS");
            AddMapTile(map.transform, "Playground", new Vector2(-11.7f, -.4f), new Vector2(3.4f, 2.2f), Hex("E7C772"), "PLAY");
            AddMapTile(map.transform, "Community Garden", new Vector2(4.8f, -7.3f), new Vector2(2.2f, 1.7f), Hex("A8CA8A"), "GARDEN");

            for (int i = 0; i < tasks.Count; i++)
            {
                TaskStation task = tasks[i];
                Color markerColor = task.complete ? Hex("64C9A5") : Hex("F3C85E");
                GameObject marker = AddMapTile(map.transform, "Task Marker " + (i + 1), task.position, new Vector2(.84f, .84f), markerColor, (i + 1).ToString());
                marker.GetComponent<Image>().sprite = circleSprite;
                AddOutline(marker, Hex("17313A"), 1.5f);
            }

            if (human != null)
            {
                GameObject you = AddMapTile(map.transform, "You Marker", human.position, new Vector2(.78f, .78f), Hex("45E0C0"));
                you.GetComponent<Image>().sprite = circleSprite;
                AddOutline(you, Color.white, 2f);
                MakeLabel(map.transform, "YOU", MapPanelPoint(human.position + new Vector2(0f, .72f)), new Vector2(70f, 22f), 11, Hex("17313A"), FontStyle.Bold);
            }

            MakeLabel(panel.transform, "YOU", new Vector2(-230f, -244f), new Vector2(70f, 24f), 12, Hex("45E0C0"), FontStyle.Bold);
            MakeLabel(panel.transform, "OPEN TASK", new Vector2(-40f, -244f), new Vector2(110f, 24f), 12, Hex("F3C85E"), FontStyle.Bold);
            MakeLabel(panel.transform, "DONE", new Vector2(150f, -244f), new Vector2(80f, 24f), 12, Hex("64C9A5"), FontStyle.Bold);
            AddMenuButton(panel.transform, "Close map", new Vector2(0f, -284f), () => { activeOverlay = ""; DestroyOverlay(); }, true, new Vector2(250f, 44f));
        }

        void ShowQuickChat()
        {
            if (phase != RoundPhase.Playing) return;
            activeOverlay = "quick"; DestroyOverlay();
            GameObject panel = MakePanel(overlayRoot, "Quick Chat", Hex("172A36"), new Vector2(1f, 0f), new Vector2(1f, 0f), false, new Vector2(420f, 390f), new Vector2(-230f, 250f));
            AddOutline(panel, Hex("E6BD65"), 2f);
            MakeLabel(panel.transform, "QUICK CHAT", new Vector2(0f, 150f), new Vector2(360f, 38f), 24, Hex("FFF4D7"), FontStyle.Bold);
            string[] phrases = { "I'm doing a task.", "I saw someone near the lake.", "The lights went out.", "Let's share evidence.", "Please explain your route." };
            for (int i = 0; i < phrases.Length; i++)
            {
                string phrase = phrases[i];
                AddMenuButton(panel.transform, phrase, new Vector2(0f, 88f - i * 52f), () => { AddEvidence("You said: " + phrase); activeOverlay = ""; DestroyOverlay(); }, false, new Vector2(360f, 42f));
            }
            AddMenuButton(panel.transform, "Close", new Vector2(0f, -177f), () => { activeOverlay = ""; DestroyOverlay(); }, false, new Vector2(180f, 38f));
        }

        void ShowInGameSafety()
        {
            if (phase != RoundPhase.Playing) return;
            activeOverlay = "safety"; DestroyOverlay();
            SafetyUI.ShowRoster(overlayRoot, players, uiFont,
                id => { MatchPlayer p = GetPlayer(id); if (p != null) p.muted = !p.muted; ShowInGameSafety(); },
                id => { MatchPlayer p = GetPlayer(id); if (p != null) p.blocked = !p.blocked; ShowInGameSafety(); },
                ShowReportReasons,
                () =>
                {
                    activeOverlay = "settings";
                    DestroyOverlay();
                    SettingsUI.Show(overlayRoot, ShowInGameSafety, largeText, highContrast, reducedEffects, StoreAccessibilitySettings);
                },
                () => { activeOverlay = ""; DestroyOverlay(); });
        }

        void StoreAccessibilitySettings(bool large, bool contrast, bool reduce)
        {
            largeText = large;
            highContrast = contrast;
            reducedEffects = reduce;
            UIButtonJuice.SetReducedMotion(reducedEffects);
            PlayerPrefs.SetInt("Crony.LargeText", largeText ? 1 : 0);
            PlayerPrefs.SetInt("Crony.HighContrast", highContrast ? 1 : 0);
            PlayerPrefs.SetInt("Crony.ReduceEffects", reducedEffects ? 1 : 0);
        }

        void ShowReportReasons(int playerId)
        {
            DestroyOverlay();
            MatchPlayer player = GetPlayer(playerId);
            SafetyUI.ShowReport(overlayRoot, player == null ? "PLAYER" : player.name, uiFont,
                reason => SubmitLocalReport(playerId, reason), ShowInGameSafety);
        }

        void SubmitLocalReport(int playerId, string reason)
        {
            MatchPlayer player = GetPlayer(playerId);
            activeOverlay = ""; DestroyOverlay();
            // A concern report is care, not a complaint: never block or flag the player for it.
            if (reason == SafetyUI.DistressReason) { ShowToast("Thank you for looking out for them. If someone may be in danger now, contact local emergency services.", 4f); return; }
            if (player != null) player.blocked = true;
            evidence.Add("Local report recorded for " + (player == null ? "player" : player.name) + ": " + reason + ".");
            ShowToast("Report saved for this practice session. Player blocked locally.", 3f);
        }

        void ShowPauseMenu()
        {
            activeOverlay = "pause"; DestroyOverlay();
            MakePanel(overlayRoot, "Pause Shade", new Color(.015f, .06f, .08f, .75f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Pause Panel", Hex("172A36"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(450f, 450f));
            AddOutline(panel, Hex("E6BD65"), 3f);
            MakeLabel(panel.transform, "PAUSED", new Vector2(0f, 165f), new Vector2(380f, 50f), 31, Hex("FFF4D7"), FontStyle.Bold);
            AddMenuButton(panel.transform, "Resume", new Vector2(0f, 85f), () => { activeOverlay = ""; DestroyOverlay(); }, true);
            AddMenuButton(panel.transform, "How to play & controls", new Vector2(0f, 20f), () => OpenHowToPlay(ShowPauseMenu), false);
            AddMenuButton(panel.transform, "Safety & Settings", new Vector2(0f, -45f), ShowInGameSafety, false);
            AddMenuButton(panel.transform, "Return to Main Menu", new Vector2(0f, -110f), ShowMainMenu, false);
            Typography.Apply(panel.transform);
        }

        void EndMatch(PlayerRole winner, string reason)
        {
            if (phase == RoundPhase.Results || phase == RoundPhase.Menu) return;
            phase = RoundPhase.Results; activeTask = null; activeOverlay = "results"; DestroyOverlay();
            if (fogImage != null) fogImage.gameObject.SetActive(false);
            hudRoot.gameObject.SetActive(false);
            MakePanel(overlayRoot, "Results Shade", new Color(.015f, .06f, .08f, .8f), Vector2.zero, Vector2.zero, true);
            GameObject card = MakePanel(overlayRoot, "Results Card", Hex("172A36"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), false, new Vector2(620f, 540f));
            AddOutline(card, winner == PlayerRole.Crewmate ? Hex("78D5B3") : Hex("E88076"), 3f);
            MakeLabel(card.transform, winner == PlayerRole.Crewmate ? "CREWMATES WIN" : "IMPOSTOR WINS", new Vector2(0f, 177f), new Vector2(540f, 65f), 36, winner == PlayerRole.Crewmate ? Hex("A6E0CF") : Hex("F29486"), FontStyle.Bold);
            MakeLabel(card.transform, reason, new Vector2(0f, 112f), new Vector2(540f, 36f), 17, Hex("F2EBDC"), FontStyle.Normal);
            bool humanWon = (winner == PlayerRole.Crewmate) == (humanRole == PlayerRole.Crewmate);
            GameAudioManager.PlayMatchEnd(humanWon);
            GameAudioManager.SetMusic(MusicMood.Menu);
            MakeLabel(card.transform, "Your role: " + RoleTitle(humanRole) + (humanWon ? "  ·  YOU WIN" : "  ·  you lose"), new Vector2(0f, 47f), new Vector2(500f, 36f), 16, Hex("E6BD65"), FontStyle.Bold);
            string remaining = "Completed " + CountCompleted() + " of " + tasks.Count + " community tasks." + (cronyPartner != null ? "  The crony was " + cronyPartner.name + "." : "");
            MakeLabel(card.transform, remaining + "\nThe match was played offline with local bots.", new Vector2(0f, -7f), new Vector2(520f, 60f), 14, Hex("C4D5D3"), FontStyle.Normal);
            AddMenuButton(card.transform, "Play Again", new Vector2(0f, -112f), () => BeginMatch(chosenRole), true);
            AddMenuButton(card.transform, "Return to Menu", new Vector2(0f, -178f), ShowMainMenu, false);
            Typography.Apply(card.transform);
        }

        static string RoleTitle(PlayerRole role) => role == PlayerRole.Crewmate ? "CREWMATE" : role == PlayerRole.Crony ? "CRONY" : "IMPOSTOR";

        int CountCompleted() { int count = 0; foreach (TaskStation task in tasks) if (task.complete) count++; return count; }

        void ShowToast(string message, float duration = 2.5f)
        {
            toast = message; toastTimer = duration;
            if (toastLabel != null) toastLabel.text = message;
        }

        void AddEvidence(string item)
        {
            string stamped = Mathf.FloorToInt((MatchLength - matchTimer) / 60f).ToString("00") + ":" + Mathf.FloorToInt((MatchLength - matchTimer) % 60f).ToString("00") + "  " + item;
            evidence.Add(stamped);
            if (evidence.Count > 12) evidence.RemoveAt(0);
            latestEvent = item;
        }

        void RecordSuspicion(string playerName, float amount)
        {
            foreach (MatchPlayer player in players) if (player.name == playerName) player.suspicion += amount;
        }

        void UpdateVisionMask()
        {
            if (fogTexture == null) return;
            // The fog image spans twice the screen, centred on the player, so radii are in half screen heights.
            float radius = (blackout ? .13f : .30f) * .5f;
            float maxAlpha = blackout ? .94f : .78f;
            for (int y = 0; y < 144; y++)
                for (int x = 0; x < 256; x++)
                {
                    float dx = (x + .5f - 128f) / 144f;
                    float dy = (y + .5f - 72f) / 144f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius - .0175f, radius + .0175f, d));
                    fogTexture.SetPixel(x, y, new Color(.09f, .07f, .2f, fade * maxAlpha));
                }
            fogTexture.Apply(false, false);
        }

        void SetFogVisible(bool visible) { if (fogImage != null) fogImage.gameObject.SetActive(visible); }

        void DestroyOverlay()
        {
            if (overlayRoot == null) return;
            for (int i = overlayRoot.childCount - 1; i >= 0; i--) Destroy(overlayRoot.GetChild(i).gameObject);
        }

        GameObject MakePanel(Transform parent, string name, Color color, Vector2 anchor, Vector2 pivot, bool stretch, Vector2 size = default(Vector2), Vector2 position = default(Vector2))
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = stretch ? Vector2.zero : anchor;
            rt.anchorMax = stretch ? Vector2.one : anchor;
            rt.pivot = pivot;
            if (stretch) { rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
            else { rt.sizeDelta = size; rt.anchoredPosition = position; }
            Image image = go.GetComponent<Image>(); image.color = color; image.sprite = squareSprite;
            bool window = name.EndsWith("Card", StringComparison.Ordinal) || name.EndsWith("Panel", StringComparison.Ordinal) ||
                name == "Top HUD" || name == "Task List Backplate" || name == "Evidence Board" ||
                name == "Text Chat" || name == "Quick Chat" || name == "Park Overview";
            if (window) PixelUiTheme.ApplyWindow(go, name.EndsWith("Card", StringComparison.Ordinal) || name.EndsWith("Panel", StringComparison.Ordinal));
            return go;
        }

        Text MakeLabel(Transform parent, string value, Vector2 position, Vector2 size, int fontSize, Color color, FontStyle style = FontStyle.Normal, Vector2? anchor = null, Vector2? pivot = null)
        {
            GameObject go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            Vector2 a = anchor ?? new Vector2(.5f, .5f);
            rt.anchorMin = a; rt.anchorMax = a; rt.pivot = pivot ?? new Vector2(.5f, .5f);
            rt.sizeDelta = size; rt.anchoredPosition = position;
            Text text = go.GetComponent<Text>();
            text.font = uiFont != null ? uiFont : PixelUiTheme.BodyFont; text.text = value; text.fontSize = fontSize; text.color = PixelUiTheme.ReadableTextColor(parent, color);
            text.fontStyle = style; text.alignment = TextAnchor.MiddleCenter; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow; text.lineSpacing = 1.05f; text.raycastTarget = false;
            if (highContrast) text.supportRichText = false;
            return text;
        }

        GameObject MakeButton(Transform parent, string label, Vector2 position, Vector2 size, Color color, Action onClick)
        {
            GameObject go = new GameObject("Button - " + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(.5f, .5f); rt.anchorMax = new Vector2(.5f, .5f); rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = size; rt.anchoredPosition = position;
            Image image = go.GetComponent<Image>(); image.color = color; image.sprite = squareSprite;
            Button button = go.GetComponent<Button>();
            PixelUiTheme.StyleButton(button, color);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            Text labelText = MakeLabel(go.transform, label, Vector2.zero, size - new Vector2(16f, 6f), largeText ? 18 : 15, PixelUiTheme.ButtonTextColor(color), FontStyle.Bold);
            labelText.raycastTarget = false;
            return go;
        }

        void AddOutline(GameObject go, Color color, float distance)
        {
            if (go == null || PixelUiTheme.HasWarmSurface(go.transform)) return;
            Outline outline = go.AddComponent<Outline>(); outline.effectColor = color; outline.effectDistance = new Vector2(distance, -distance);
        }

        static Color Hex(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return Color.white;
            Color c;
            if (ColorUtility.TryParseHtmlString(html, out c)) return c;
            if (html[0] != '#' && ColorUtility.TryParseHtmlString("#" + html, out c)) return c;
            return Color.white;
        }
    }

    public sealed class TouchMoveButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        CronyRuntime game;
        Vector2 direction;
        public void Initialize(CronyRuntime owner, Vector2 moveDirection) { game = owner; direction = moveDirection; }
        public void OnPointerDown(PointerEventData eventData) { if (game != null) game.SetTouchMove(direction, true); }
        public void OnPointerUp(PointerEventData eventData) { if (game != null) game.SetTouchMove(direction, false); }
        public void OnPointerExit(PointerEventData eventData) { if (game != null) game.SetTouchMove(direction, false); }
    }
}
