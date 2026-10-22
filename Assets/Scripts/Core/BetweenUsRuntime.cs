using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BetweenUs
{
    public enum RoundPhase { Menu, Playing, Meeting, Results }
    public enum PlayerRole { Crewmate, Impostor }

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
        public int nextTask;
        public bool botFinishedTask;
        public Vector2 position;
        public Color outfitColor;
        public GameObject root;
        public SpriteRenderer body;
        public SpriteRenderer hat;
        public TextMesh nameTag;
    }

    /// <summary>Runtime bootstraps and hosts a complete local practice round with text-only bot play.</summary>
    public sealed class BetweenUsRuntime : MonoBehaviour
    {
        const int PlayerLimit = 8;
        const float InteractRange = 1.8f;
        const float MatchLength = 480f;

        static BetweenUsRuntime instance;
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

        Camera gameCamera;
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
        AudioSource uiAudio;
        AudioClip clickTone;
        AudioClip taskTone;
        AudioClip alertTone;
        RoundPhase phase = RoundPhase.Menu;
        PlayerRole humanRole = PlayerRole.Crewmate;
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
        int currentSabotage = -1;
        int meetingBody = -1;
        int humanVote = -2;
        string meetingChat = "";
        string latestEvent = "";
        Color[] skinColors = { Hex("F0C8A7"), Hex("D9A27E"), Hex("A96D4D"), Hex("7F503D"), Hex("5A382F") };
        Color[] outfitColors = { Hex("31B8A2"), Hex("F16B5B"), Hex("F3C45B"), Hex("6687DD"), Hex("B980D0"), Hex("F4F1E8") };

        Text hudStatus;
        Text roleLabel;
        Text taskListLabel;
        Text interactLabel;
        Text toastLabel;
        Text meetingChatLabel;
        Text meetingTimerLabel;
        Text taskProgressLabel;
        Image taskProgressFill;
        readonly List<GameObject> temporaryUi = new List<GameObject>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<BetweenUsRuntime>() == null)
                new GameObject("Between Us Game").AddComponent<BetweenUsRuntime>();
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            squareSprite = MakeSquareSprite();
            circleSprite = MakeCircleSprite();
            SetupCamera();
            SetupWorld();
            SetupUI();
            SetupAudio();
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
            gameCamera.orthographic = true;
            gameCamera.orthographicSize = 8.2f;
            gameCamera.transform.position = new Vector3(0f, 0f, -10f);
            gameCamera.backgroundColor = Hex("94D4B1");
            gameCamera.clearFlags = CameraClearFlags.SolidColor;
            if (gameCamera.GetComponent<AudioListener>() == null) gameCamera.gameObject.AddComponent<AudioListener>();
        }

        void SetupAudio()
        {
            uiAudio = gameObject.AddComponent<AudioSource>();
            uiAudio.playOnAwake = false;
            uiAudio.spatialBlend = 0f;
            uiAudio.volume = .42f;
            clickTone = MakeTone(720f, .055f);
            taskTone = MakeChord(660f, 880f, .34f);
            alertTone = MakeTone(290f, .24f);
        }

        AudioClip MakeTone(float frequency, float duration)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Exp(-t * (duration > .15f ? 7f : 24f));
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * .12f;
            }
            AudioClip clip = AudioClip.Create("Between Us UI Tone", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        AudioClip MakeChord(float first, float second, float duration)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Clamp01((duration - t) * 5f) * Mathf.Clamp01(t * 35f);
                samples[i] = (Mathf.Sin(2f * Mathf.PI * first * t) * .5f + Mathf.Sin(2f * Mathf.PI * second * t) * .5f) * envelope * .14f;
            }
            AudioClip clip = AudioClip.Create("Community Task Complete", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void PlaySound(AudioClip clip)
        {
            if (uiAudio != null && clip != null) uiAudio.PlayOneShot(clip);
        }

        void SetupWorld()
        {
            GameObject root = new GameObject("Township Park World");
            worldRoot = root.transform;
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
            tasks.Add(new TaskStation("Pack Nasi Lemak", "Hawker Stalls", "Build the packet in the order shown. Select ingredients from left to right.", new Vector2(-8.9f, 5.6f), new[] { "Rice", "Sambal", "Egg", "Cucumber", "Peanuts" }, new[] { 0, 1, 2, 3 }));
            tasks.Add(new TaskStation("Restock Ice Cream", "Convenience Store", "Match each freezer slot to the flavor card.", new Vector2(6.8f, 5.5f), new[] { "Pandan", "Mango", "Red Bean", "Coconut" }, new[] { 2, 0, 3, 1 }));
            tasks.Add(new TaskStation("Fix Park Bench", "Recreation Park", "Align the slat, then tighten each loose bolt.", new Vector2(-9.2f, .1f), new[] { "Align slat", "Tighten bolt", "Check fit" }, new[] { 0, 1, 0, 1 }));
            tasks.Add(new TaskStation("Calibrate Badminton Net", "Community Hall", "Adjust the net height to the gauge. Follow the indicator.", new Vector2(-.3f, 5.4f), new[] { "Lower", "Raise", "Check gauge" }, new[] { 1, 2, 0 }));
            tasks.Add(new TaskStation("File Community Permits", "Administrative Office", "Put each permit into its matching binder.", new Vector2(6.5f, -.8f), new[] { "Green binder", "Blue binder", "Gold binder" }, new[] { 1, 0, 2, 0 }));
            tasks.Add(new TaskStation("Update Bus Schedule", "Bus Stop", "Set the departures in the order printed on the board.", new Vector2(8.9f, -6.2f), new[] { "07:30", "08:45", "10:15", "12:00" }, new[] { 0, 2, 1 }));
            tasks.Add(new TaskStation("Clear the Lake", "Central Lake", "Collect the floating items and sort them for recycling.", new Vector2(3.1f, -2.1f), new[] { "Plastic bottle", "Leaf bundle", "Paper cup", "Can" }, new[] { 2, 0, 3, 1 }));
            tasks.Add(new TaskStation("Reset Wi-Fi Router", "Cellular Tower", "Restore the signal by toggling switches in sequence.", new Vector2(-8.1f, -6.1f), new[] { "Switch A", "Switch B", "Switch C", "Switch D" }, new[] { 2, 0, 3, 1 }));
            tasks.Add(new TaskStation("Replace Solar Battery", "Garden Walkway", "Open the panel, replace the cell, and test the light.", new Vector2(2.2f, -7.7f), new[] { "Open panel", "Swap battery", "Close panel", "Test light" }, new[] { 0, 1, 2, 3 }));
            tasks.Add(new TaskStation("Pin Announcements", "Notice Board", "Place the flyers on the matching numbered pins.", new Vector2(6.7f, -4.0f), new[] { "Pin 1", "Pin 2", "Pin 3", "Pin 4" }, new[] { 3, 1, 0 }));

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
            go.transform.position = new Vector3(position.x, position.y, 0f);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = squareSprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            if (block) go.AddComponent<BoxCollider2D>();
            return go;
        }

        GameObject CreateEllipse(string name, Vector2 position, Vector2 size, Color color, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(worldRoot, false);
            go.transform.position = new Vector3(position.x, position.y, 0f);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
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
            go.transform.position = new Vector3(position.x, position.y, -.1f);
            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.text = value;
            mesh.font = uiFont;
            mesh.fontSize = 64;
            mesh.characterSize = size / 3.5f;
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
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440f, 900f);
            scaler.matchWidthOrHeight = .5f;
            overlayRoot = CreateUiRoot("Overlay Root", canvasObject.transform);
            hudRoot = CreateUiRoot("HUD Root", canvasObject.transform);
            CreateFog();
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            InputSystemUIInputModule inputModule = eventSystem.AddComponent<InputSystemUIInputModule>();
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
            phase = RoundPhase.Menu;
            StopPlayers();
            blackout = false;
            currentSabotage = -1;
            hudRoot.gameObject.SetActive(false);
            SetFogVisible(false);
            DestroyOverlay();
            GameObject backdrop = MakePanel(overlayRoot, "Menu Backdrop", Color.black * new Color(1f, 1f, 1f, .48f), Vector2.zero, Vector2.zero, true);
            GameObject card = MakePanel(overlayRoot, "Menu Card", highContrast ? Hex("071A24") : Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(570f, 760f));
            AddOutline(card, Hex("E6BD65"), 3f);
            MakeLabel(card.transform, "BETWEEN US", new Vector2(0f, 292f), new Vector2(510f, 90f), largeText ? 48 : 44, Hex("FFF4D7"), FontStyle.Bold);
            MakeLabel(card.transform, "WHO'S THE IMPOSTOR?", new Vector2(0f, 245f), new Vector2(500f, 30f), 16, Hex("E6BD65"), FontStyle.Bold);
            MakeLabel(card.transform, "A cozy social deduction game in a Malaysian town park.", new Vector2(0f, 205f), new Vector2(500f, 32f), 15, Hex("C4D5D3"), FontStyle.Normal);
            float y = 132f;
            AddMenuButton(card.transform, "Practice as Crewmate", new Vector2(0f, y), () => BeginMatch(PlayerRole.Crewmate), true); y -= 62f;
            AddMenuButton(card.transform, "Practice as Impostor", new Vector2(0f, y), () => BeginMatch(PlayerRole.Impostor), false); y -= 62f;
            AddMenuButton(card.transform, "Private Lobby · Local Bots", new Vector2(0f, y), ShowPrivateLobby, false); y -= 62f;
            AddMenuButton(card.transform, "Customize Avatar", new Vector2(0f, y), ShowCustomization, false); y -= 62f;
            AddMenuButton(card.transform, "Safety & Settings", new Vector2(0f, y), ShowSafetySettings, false); y -= 62f;
            GameObject online = AddMenuButton(card.transform, "Public Matchmaking · Service Needed", new Vector2(0f, y), null, false);
            online.GetComponent<Button>().interactable = false;
            MakeLabel(card.transform, "Text-only by default  ·  No account required  ·  8-minute rounds", new Vector2(0f, -291f), new Vector2(510f, 28f), 12, Hex("A9B9B8"), FontStyle.Normal);
            MakeLabel(card.transform, "FIRST RELEASE  •  TOWNSHIP PARK  •  OFFLINE PRACTICE", new Vector2(0f, -324f), new Vector2(510f, 24f), 10, Hex("8AA2A4"), FontStyle.Bold);
            if (backdrop != null) backdrop.transform.SetAsFirstSibling();
        }

        void ShowPrivateLobby()
        {
            DestroyOverlay();
            MakePanel(overlayRoot, "Lobby Backdrop", new Color(.02f, .08f, .11f, .78f), Vector2.zero, Vector2.zero, true);
            GameObject card = MakePanel(overlayRoot, "Private Lobby Card", Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(560f, 570f));
            AddOutline(card, Hex("E6BD65"), 3f);
            MakeLabel(card.transform, "PRIVATE LOBBY", new Vector2(0f, 205f), new Vector2(500f, 58f), 34, Hex("FFF4D7"), FontStyle.Bold);
            MakeLabel(card.transform, "Host a private practice room locally and fill it with bots.\nOnline friends and invite codes will connect when a match service is added.", new Vector2(0f, 127f), new Vector2(480f, 80f), 16, Hex("C4D5D3"), FontStyle.Normal);
            MakeLabel(card.transform, "LOCAL SESSION  ·  8 PLAYER SLOTS  ·  BOTS FILL THE ROOM", new Vector2(0f, 30f), new Vector2(480f, 34f), 13, Hex("E6BD65"), FontStyle.Bold);
            MakeLabel(card.transform, "This room stays on this device. It does not create an online invite.", new Vector2(0f, -16f), new Vector2(460f, 30f), 12, Hex("A9B9B8"), FontStyle.Normal);
            AddMenuButton(card.transform, "Start Private Bot Match", new Vector2(0f, -105f), () => BeginMatch(PlayerRole.Crewmate), true);
            AddMenuButton(card.transform, "Back", new Vector2(0f, -174f), ShowMainMenu, false);
        }

        void ShowCustomization()
        {
            DestroyOverlay();
            MakePanel(overlayRoot, "Customize Backdrop", new Color(.02f, .08f, .11f, .78f), Vector2.zero, Vector2.zero, true);
            GameObject card = MakePanel(overlayRoot, "Dressing Room", Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(610f, 650f));
            AddOutline(card, Hex("E6BD65"), 3f);
            MakeLabel(card.transform, "DRESSING ROOM", new Vector2(0f, 255f), new Vector2(520f, 56f), 34, Hex("FFF4D7"), FontStyle.Bold);
            GameObject avatar = new GameObject("Preview Avatar", typeof(RectTransform), typeof(Image));
            avatar.transform.SetParent(card.transform, false);
            RectTransform art = avatar.GetComponent<RectTransform>(); art.sizeDelta = new Vector2(90f, 90f); art.anchoredPosition = new Vector2(0f, 150f);
            avatar.GetComponent<Image>().sprite = circleSprite; avatar.GetComponent<Image>().color = skinColors[customSkin];
            MakeLabel(card.transform, "Skin tone", new Vector2(-155f, 72f), new Vector2(180f, 32f), 16, Color.white, FontStyle.Bold);
            for (int i = 0; i < skinColors.Length; i++)
            {
                int index = i;
                GameObject swatch = AddMenuButton(card.transform, "●", new Vector2(-155f + i * 72f, 28f), () => { customSkin = index; ShowCustomization(); }, false, new Vector2(54f, 48f));
                swatch.GetComponent<Image>().color = skinColors[i];
            }
            MakeLabel(card.transform, "Outfit", new Vector2(-155f, -43f), new Vector2(180f, 32f), 16, Color.white, FontStyle.Bold);
            for (int i = 0; i < outfitColors.Length; i++)
            {
                int index = i;
                GameObject swatch = AddMenuButton(card.transform, "✦", new Vector2(-180f + i * 72f, -91f), () => { customOutfit = index; ShowCustomization(); }, false, new Vector2(54f, 48f));
                swatch.GetComponent<Image>().color = outfitColors[i];
            }
            MakeLabel(card.transform, "Choose colors that feel like you. Cosmetic choices never change gameplay.", new Vector2(0f, -161f), new Vector2(520f, 38f), 13, Hex("C4D5D3"), FontStyle.Normal);
            AddMenuButton(card.transform, "Save & Back", new Vector2(0f, -226f), ShowMainMenu, true);
        }

        void ShowSafetySettings()
        {
            DestroyOverlay();
            MakePanel(overlayRoot, "Settings Backdrop", new Color(.02f, .08f, .11f, .78f), Vector2.zero, Vector2.zero, true);
            GameObject card = MakePanel(overlayRoot, "Safety Settings", Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(630f, 720f));
            AddOutline(card, Hex("E6BD65"), 3f);
            MakeLabel(card.transform, "SAFETY & SETTINGS", new Vector2(0f, 292f), new Vector2(560f, 54f), 32, Hex("FFF4D7"), FontStyle.Bold);
            MakeLabel(card.transform, "This first build is offline. It does not collect real names, age,\nlocation, voice, or account details. Text chat uses preset phrases.", new Vector2(0f, 224f), new Vector2(550f, 70f), 15, Hex("C4D5D3"), FontStyle.Normal);
            AddMenuButton(card.transform, "Large text: " + (largeText ? "ON" : "OFF"), new Vector2(0f, 132f), () => { largeText = !largeText; ShowSafetySettings(); }, false);
            AddMenuButton(card.transform, "High contrast: " + (highContrast ? "ON" : "OFF"), new Vector2(0f, 69f), () => { highContrast = !highContrast; ShowSafetySettings(); }, false);
            AddMenuButton(card.transform, "Reduce effects: " + (reducedEffects ? "ON" : "OFF"), new Vector2(0f, 6f), () => { reducedEffects = !reducedEffects; ShowSafetySettings(); }, false);
            MakeLabel(card.transform, "Quick chat phrases are filtered and pre-written. No voice chat is enabled.\nDuring a match, use Safety to mute, block, or report a bot.", new Vector2(0f, -91f), new Vector2(550f, 62f), 14, Hex("C4D5D3"), FontStyle.Normal);
            MakeLabel(card.transform, "Online moderation, appeals, and account controls require a secure service\nbefore any public release. Local reports are session-only in this build.", new Vector2(0f, -164f), new Vector2(550f, 62f), 13, Hex("E5C789"), FontStyle.Normal);
            AddMenuButton(card.transform, "Back", new Vector2(0f, -260f), ShowMainMenu, true);
        }

        GameObject AddMenuButton(Transform parent, string text, Vector2 position, Action action, bool primary, Vector2? size = null)
        {
            Color baseColor = primary ? Hex("E6BD65") : Hex("284653");
            GameObject go = MakeButton(parent, text, position, size ?? new Vector2(440f, 50f), baseColor, action);
            Text label = go.GetComponentInChildren<Text>();
            if (label != null) { label.color = primary ? Hex("1F3036") : Hex("F5F2E8"); label.fontSize = largeText ? 20 : 17; label.fontStyle = FontStyle.Bold; }
            return go;
        }

        void BeginMatch(PlayerRole role)
        {
            activeOverlay = "";
            DestroyOverlay();
            StopPlayers();
            humanRole = role;
            phase = RoundPhase.Playing;
            matchTimer = MatchLength;
            sabotageTimer = 0f;
            currentSabotage = -1;
            killCooldown = 0f;
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
            foreach (TaskStation task in tasks) { task.complete = false; task.step = 0; if (task.marker != null) task.marker.color = Hex("FFF3D9"); }
            SpawnPlayers();
            hudRoot.gameObject.SetActive(true);
            BuildHud();
            SetFogVisible(true);
            UpdateVisionMask();
            AddEvidence("Match started at Township Park.");
            ShowToast(role == PlayerRole.Crewmate ? "You are a Crewmate. Finish tasks and watch for clues." : "You are the Impostor. Blend in, sabotage, and make your move.", 4f);
        }

        void SpawnPlayers()
        {
            players.Clear();
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
                player.position = spawns[i];
                player.outfitColor = colors[i];
                GameObject avatar = new GameObject("Avatar - " + player.name);
                avatar.transform.SetParent(playerRoot, false);
                avatar.transform.position = new Vector3(spawns[i].x, spawns[i].y, 0f);
                GameObject body = new GameObject("Body");
                body.transform.SetParent(avatar.transform, false);
                body.transform.localScale = new Vector3(.84f, .96f, 1f);
                player.body = body.AddComponent<SpriteRenderer>();
                player.body.sprite = circleSprite;
                player.body.color = colors[i];
                player.body.sortingOrder = 15;
                GameObject face = new GameObject("Face");
                face.transform.SetParent(avatar.transform, false);
                face.transform.localPosition = new Vector3(0f, .15f, -.05f);
                face.transform.localScale = new Vector3(.42f, .42f, 1f);
                SpriteRenderer faceRenderer = face.AddComponent<SpriteRenderer>();
                faceRenderer.sprite = circleSprite;
                faceRenderer.color = i == 0 ? skinColors[Mathf.Clamp(customSkin, 0, skinColors.Length - 1)] : skinColors[(i + 1) % skinColors.Length];
                faceRenderer.sortingOrder = 16;
                GameObject hat = new GameObject("Headwear");
                hat.transform.SetParent(avatar.transform, false);
                hat.transform.localPosition = new Vector3(0f, .43f, 0f);
                hat.transform.localScale = new Vector3(.56f, .22f, 1f);
                player.hat = hat.AddComponent<SpriteRenderer>();
                player.hat.sprite = squareSprite;
                player.hat.color = colors[i];
                player.hat.sortingOrder = 17;
                GameObject leftEye = new GameObject("Left Eye");
                leftEye.transform.SetParent(avatar.transform, false);
                leftEye.transform.localPosition = new Vector3(-.08f, .17f, -.1f);
                leftEye.transform.localScale = new Vector3(.07f, .12f, 1f);
                SpriteRenderer leftEyeRenderer = leftEye.AddComponent<SpriteRenderer>(); leftEyeRenderer.sprite = circleSprite; leftEyeRenderer.color = Hex("263842"); leftEyeRenderer.sortingOrder = 18;
                GameObject rightEye = new GameObject("Right Eye");
                rightEye.transform.SetParent(avatar.transform, false);
                rightEye.transform.localPosition = new Vector3(.08f, .17f, -.1f);
                rightEye.transform.localScale = new Vector3(.07f, .12f, 1f);
                SpriteRenderer rightEyeRenderer = rightEye.AddComponent<SpriteRenderer>(); rightEyeRenderer.sprite = circleSprite; rightEyeRenderer.color = Hex("263842"); rightEyeRenderer.sortingOrder = 18;
                player.nameTag = CreateWorldLabel(player.name, spawns[i] + new Vector2(0f, .78f), .18f, Color.white, 20);
                player.nameTag.transform.SetParent(avatar.transform, true);
                player.nameTag.transform.localPosition = new Vector3(0f, .78f, -.1f);
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
            Color panel = highContrast ? Hex("071A24") : new Color(.07f, .14f, .18f, .92f);
            MakePanel(hudRoot, "Top HUD", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), false, new Vector2(1440f, 84f), new Vector2(0f, 0f));
            hudStatus = MakeLabel(hudRoot, "Township Park  •  08:00", new Vector2(-480f, -37f), new Vector2(400f, 38f), 22, Hex("FFF4D7"), FontStyle.Bold, new Vector2(0f, 1f), new Vector2(0f, 1f));
            roleLabel = MakeLabel(hudRoot, "CREWMATE", new Vector2(0f, -37f), new Vector2(260f, 38f), 20, Hex("A6E0CF"), FontStyle.Bold, new Vector2(.5f, 1f), new Vector2(.5f, 1f));
            taskListLabel = MakeLabel(hudRoot, "", new Vector2(18f, -110f), new Vector2(300f, 220f), 14, Hex("F4F1E8"), FontStyle.Normal, new Vector2(0f, 1f), new Vector2(0f, 1f));
            MakePanel(hudRoot, "Task List Backplate", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), false, new Vector2(326f, 230f), new Vector2(8f, -92f)).transform.SetAsFirstSibling();
            taskListLabel.transform.SetAsLastSibling();
            interactLabel = MakeLabel(hudRoot, "", new Vector2(0f, -92f), new Vector2(480f, 42f), 17, Hex("FFF4D7"), FontStyle.Bold, new Vector2(.5f, 1f), new Vector2(.5f, 1f));
            toastLabel = MakeLabel(hudRoot, "", new Vector2(0f, 122f), new Vector2(620f, 48f), 18, Hex("FFF4D7"), FontStyle.Bold);
            taskProgressLabel = MakeLabel(hudRoot, "", new Vector2(0f, 130f), new Vector2(260f, 30f), 14, Color.white, FontStyle.Bold, new Vector2(.5f, 0f), new Vector2(.5f, 0f));
            GameObject progress = MakePanel(hudRoot, "Task Bar Track", Hex("263D45"), new Vector2(.5f, 0f), new Vector2(.5f, 0f), false, new Vector2(290f, 14f), new Vector2(0f, 110f));
            GameObject fill = MakePanel(progress.transform, "Task Bar Fill", Hex("67D0AE"), new Vector2(0f, .5f), new Vector2(0f, .5f), false, new Vector2(1f, 10f));
            taskProgressFill = fill.GetComponent<Image>();
            GameObject interactButton = AddMenuButton(hudRoot, "Interact  [E]", Vector2.zero, Interact, true, new Vector2(180f, 52f));
            RectTransform interactRect = interactButton.GetComponent<RectTransform>();
            interactRect.anchorMin = new Vector2(.5f, 0f); interactRect.anchorMax = new Vector2(.5f, 0f); interactRect.pivot = new Vector2(.5f, 0f);
            interactRect.anchoredPosition = new Vector2(0f, 28f);
            AddHudButton("Report  [R]", new Vector2(-208f, 32f), ReportNearby, Hex("D96959"));
            AddHudButton("Meeting  [M]", new Vector2(-18f, 32f), CallEmergency, Hex("D6AD57"));
            AddHudButton("Safety", new Vector2(-208f, 84f), ShowInGameSafety, Hex("4D7E8E"));
            AddHudButton("Map", new Vector2(-18f, 84f), ShowMap, Hex("4D7E8E"));
            if (humanRole == PlayerRole.Impostor)
            {
                AddHudButton("Sabotage  [Q]", new Vector2(-113f, 140f), ShowSabotageMenu, Hex("AD5160"));
                AddHudButton("Incapacitate  [F]", new Vector2(-113f, 194f), TryHumanIncapacitate, Hex("AD5160"));
            }
            else AddHudButton("Quick Chat", new Vector2(-113f, 140f), ShowQuickChat, Hex("4D7E8E"));
            if (Application.isMobilePlatform && Touchscreen.current != null) BuildTouchPad();
        }

        void AddHudButton(string label, Vector2 position, Action action, Color color)
        {
            GameObject button = MakeButton(hudRoot, label, position, new Vector2(180f, 44f), color, action);
            Text text = button.GetComponentInChildren<Text>();
            if (text != null) { text.fontSize = largeText ? 18 : 15; text.fontStyle = FontStyle.Bold; text.color = Color.white; }
            RectTransform rt = button.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = position;
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
            Text text = button.GetComponentInChildren<Text>(); if (text != null) { text.color = Hex("20363C"); text.fontSize = 22; }
            TouchMoveButton move = button.AddComponent<TouchMoveButton>(); move.Initialize(this, direction);
        }

        void Update()
        {
            if (toastTimer > 0f) { toastTimer -= Time.deltaTime; if (toastLabel != null) toastLabel.text = toast; }
            else if (toastLabel != null && toastLabel.text.Length > 0) toastLabel.text = "";
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (phase == RoundPhase.Playing)
                {
                    if (keyboard.eKey.wasPressedThisFrame) Interact();
                    if (keyboard.rKey.wasPressedThisFrame) ReportNearby();
                    if (keyboard.mKey.wasPressedThisFrame) CallEmergency();
                    if (keyboard.qKey.wasPressedThisFrame && humanRole == PlayerRole.Impostor) ShowSabotageMenu();
                    if (keyboard.fKey.wasPressedThisFrame && humanRole == PlayerRole.Impostor) TryHumanIncapacitate();
                }
                if (keyboard.escapeKey.wasPressedThisFrame)
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
                    else if (phase == RoundPhase.Menu) { }
                    else ShowMainMenu();
                }
            }

            if (phase == RoundPhase.Playing)
            {
                UpdateHumanMovement();
                UpdateBots();
                UpdateMatchClock();
                UpdateCamera();
                UpdateNearbyPrompt();
                UpdateHud();
            }
            else if (phase == RoundPhase.Meeting) UpdateMeetingClock();
        }

        void UpdateHumanMovement()
        {
            if (human == null || !human.alive || human.downed || activeTask != null || activeOverlay.Length > 0) return;
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
        }

        public void SetTouchMove(Vector2 direction, bool pressed)
        {
            if (direction.x != 0f) touchMoveX = pressed ? direction.x : 0f;
            if (direction.y != 0f) touchMoveY = pressed ? direction.y : 0f;
        }

        void MovePlayer(MatchPlayer player, Vector2 delta)
        {
            Vector2 next = player.position;
            Vector2 xStep = next + new Vector2(delta.x, 0f);
            if (!IsBlocked(xStep)) next.x = xStep.x;
            Vector2 yStep = next + new Vector2(0f, delta.y);
            if (!IsBlocked(yStep)) next.y = yStep.y;
            next.x = Mathf.Clamp(next.x, -15f, 15f);
            next.y = Mathf.Clamp(next.y, -9.5f, 9.5f);
            player.position = next;
            if (player.root != null) player.root.transform.position = new Vector3(next.x, next.y, 0f);
        }

        bool IsBlocked(Vector2 position)
        {
            foreach (Rect blocker in mapBlockers) if (blocker.Contains(position)) return true;
            return false;
        }

        void UpdateBots()
        {
            botClock += Time.deltaTime;
            if (botClock < 0.22f) return;
            float dt = botClock; botClock = 0f;
            for (int i = 1; i < players.Count; i++)
            {
                MatchPlayer bot = players[i];
                if (!bot.alive) continue;
                if (bot.downed)
                {
                    bot.downedTimer += dt;
                    if (bot.downedTimer > 11f && bot.role == PlayerRole.Crewmate && UnityEngine.Random.value < .035f)
                    {
                        bot.downed = false; bot.downedTimer = 0f; bot.body.color = Hex("B5DFAE"); AddEvidence(bot.name + " was helped back up.");
                    }
                    continue;
                }
                if (bot.role == PlayerRole.Impostor)
                {
                    UpdateImpostorBot(bot, dt);
                    continue;
                }
                UpdateCrewBot(bot, dt);
            }
            if (phase == RoundPhase.Playing && HasUnreportedBody())
            {
                botMeetingClock += dt;
                if (botMeetingClock > 18f && UnityEngine.Random.value < .02f) ReportNearestBotBody();
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
                    MovePlayer(bot, (rescuePoint - bot.position).normalized * 2.2f * dt);
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
            if (bot.botFinishedTask) return;
            TaskStation target = FindNextOpenTask(bot);
            if (target == null) return;
            Vector2 destination = target.position + new Vector2(0f, -1.2f);
            Vector2 to = destination - bot.position;
            if (to.magnitude > .5f) MovePlayer(bot, to.normalized * 2.0f * dt);
            else
            {
                bot.taskTimer += dt;
                if (bot.taskTimer > 8f + bot.id * .7f)
                {
                    CompleteTask(target, bot.name);
                    bot.taskTimer = 0f;
                    bot.botFinishedTask = true;
                    bot.nextTask = (bot.nextTask + 2) % tasks.Count;
                }
            }
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
                if (candidate.id == bot.id || candidate.role == PlayerRole.Impostor || !candidate.alive || candidate.downed) continue;
                float distance = Vector2.Distance(bot.position, candidate.position);
                if (distance < nearest) { nearest = distance; target = candidate; }
            }
            if (target != null && nearest > 1.7f) MovePlayer(bot, (target.position - bot.position).normalized * 1.8f * dt);
            else if (nearest > 7f || target == null) Wander(bot, dt);
            if (killCooldown <= 0f && target != null && nearest <= 1.7f && UnityEngine.Random.value < .008f)
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
            if (delta.sqrMagnitude > .1f) MovePlayer(player, delta.normalized * 1.4f * dt);
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
            for (int i = 1; i < 12; i++) if (IsBlocked(Vector2.Lerp(a, b, i / 12f))) return false;
            return true;
        }

        void UpdateCamera()
        {
            if (human == null || gameCamera == null) return;
            Vector3 target = new Vector3(human.position.x, human.position.y, -10f);
            gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position, target, Time.deltaTime * 5f);
        }

        void UpdateNearbyPrompt()
        {
            if (interactLabel == null || human == null) return;
            MatchPlayer downed = FindNearbyDowned();
            if (downed != null) interactLabel.text = humanRole == PlayerRole.Crewmate ? "E  ·  Help " + downed.name + " up" : "E  ·  Move along";
            else if (currentSabotage >= 0 && Vector2.Distance(human.position, tasks[currentSabotage].position) < 2.5f) interactLabel.text = "E  ·  Restore " + SabotageNames[currentSabotage];
            else if (humanRole == PlayerRole.Crewmate)
            {
                TaskStation nearest = FindNearbyTask();
                interactLabel.text = nearest == null ? "WASD to move   •   E interact   •   R report   •   M meeting" : "E  ·  " + nearest.title;
            }
            else interactLabel.text = "WASD to move   •   F incapacitate   •   Q sabotage";
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

        void Interact()
        {
            if (phase != RoundPhase.Playing || human == null || human.downed || activeOverlay.Length > 0) return;
            MatchPlayer downed = FindNearbyDowned();
            if (downed != null)
            {
                if (humanRole == PlayerRole.Crewmate) { downed.downed = false; downed.downedTimer = 0f; downed.root.transform.localScale = Vector3.one; downed.body.color = downed.outfitColor; AddEvidence("You helped " + downed.name + " back up."); ShowToast("" + downed.name + " is safe again."); }
                return;
            }
            if (currentSabotage >= 0 && Vector2.Distance(human.position, tasks[currentSabotage].position) < 2.5f)
            {
                FixSabotage(); return;
            }
            if (humanRole == PlayerRole.Crewmate)
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
            BuildTaskOverlay();
        }

        void BuildTaskOverlay()
        {
            DestroyOverlay();
            MakePanel(overlayRoot, "Task Shade", new Color(.015f, .06f, .08f, .74f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Task Panel", highContrast ? Hex("071A24") : Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(700f, 560f));
            AddOutline(panel, Hex("E6BD65"), 3f);
            MakeLabel(panel.transform, activeTask.location.ToUpperInvariant(), new Vector2(0f, 225f), new Vector2(620f, 28f), 13, Hex("E6BD65"), FontStyle.Bold);
            MakeLabel(panel.transform, activeTask.title, new Vector2(0f, 177f), new Vector2(620f, 54f), largeText ? 31 : 28, Hex("FFF4D7"), FontStyle.Bold);
            MakeLabel(panel.transform, activeTask.instruction, new Vector2(0f, 125f), new Vector2(610f, 50f), largeText ? 18 : 16, Hex("C4D5D3"), FontStyle.Normal);
            MakeLabel(panel.transform, "STEP " + (activeTask.step + 1) + " OF " + activeTask.solution.Length, new Vector2(0f, 62f), new Vector2(500f, 28f), 15, Hex("A6E0CF"), FontStyle.Bold);
            GameObject bar = MakePanel(panel.transform, "Step Track", Hex("29414B"), new Vector2(0f, 10f), new Vector2(.5f, .5f), false, new Vector2(390f, 13f));
            float progress = activeTask.step / (float)activeTask.solution.Length;
            MakePanel(bar.transform, "Step Fill", Hex("67D0AE"), new Vector2(0f, .5f), new Vector2(0f, .5f), false, new Vector2(Mathf.Max(4f, 390f * progress), 9f));
            int count = activeTask.actions.Length;
            float start = -(count - 1) * 76f * .5f;
            for (int i = 0; i < count; i++)
            {
                int action = i;
                Color buttonColor = outfitColors[i % outfitColors.Length];
                GameObject choice = MakeButton(panel.transform, activeTask.actions[i], new Vector2(start + i * 76f, -75f), new Vector2(Mathf.Min(140f, 680f / count - 8f), 68f), buttonColor, () => TryTaskAction(action));
                Text text = choice.GetComponentInChildren<Text>(); if (text != null) { text.fontSize = largeText ? 16 : 14; text.color = Hex("1D333A"); text.fontStyle = FontStyle.Bold; }
            }
            AddMenuButton(panel.transform, "Close task", new Vector2(0f, -192f), CloseTask, false, new Vector2(240f, 46f));
            activeOverlay = "task";
        }

        void TryTaskAction(int action)
        {
            TaskStation task = activeTask;
            if (task == null) return;
            if (task.complete)
            {
                activeTask = null;
                activeOverlay = "";
                DestroyOverlay();
                ShowToast("Another player already completed this task.", 2f);
                return;
            }
            if (task.solution == null || task.solution.Length == 0 || task.actions == null || task.actions.Length == 0 ||
                task.step < 0 || task.step >= task.solution.Length || action < 0 || action >= task.actions.Length)
            {
                CloseTask();
                ShowToast("This task is no longer available. Reopen it to continue.", 2f);
                return;
            }
            int expectedAction = task.solution[task.step];
            if (expectedAction < 0 || expectedAction >= task.actions.Length)
            {
                CloseTask();
                ShowToast("This task has an invalid step and was closed.", 2f);
                return;
            }
            if (expectedAction != action)
            {
                task.step = 0;
                ShowToast("Not quite. Check the instruction and try the sequence again.", 2f);
                BuildTaskOverlay();
                return;
            }
            task.step++;
            if (task.step >= task.solution.Length)
            {
                activeTask = null; activeOverlay = "";
                CompleteTask(task, "You");
                DestroyOverlay();
                return;
            }
            BuildTaskOverlay();
        }

        void CloseTask()
        {
            activeTask = null; activeOverlay = ""; DestroyOverlay();
        }

        void CompleteTask(TaskStation task, string who)
        {
            if (task == null || task.complete) return;
            task.complete = true; task.step = task.solution.Length;
            if (task == activeTask)
            {
                activeTask = null;
                activeOverlay = "";
                DestroyOverlay();
                if (who != "You") ShowToast(who + " finished this task first.", 2f);
            }
            if (task.marker != null) task.marker.color = Hex("76D2AC");
            PlaySound(taskTone);
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
            if (remaining == 0) EndMatch(PlayerRole.Crewmate, "All community tasks are complete.");
            int livingCrew = 0; int livingImpostor = 0;
            foreach (MatchPlayer player in players) if (player.alive && !player.downed) { if (player.role == PlayerRole.Crewmate) livingCrew++; else livingImpostor++; }
            if (livingCrew <= livingImpostor) EndMatch(PlayerRole.Impostor, "The Impostor outnumbered the remaining crew.");
        }

        void Incapacitate(MatchPlayer target, string source)
        {
            if (target == null || target.downed || !target.alive || target.role == PlayerRole.Impostor) return;
            target.downed = true; target.downedTimer = 0f; target.body.color = Hex("9FB9B9");
            target.root.transform.localScale = new Vector3(.8f, .46f, 1f);
            if (humanRole == PlayerRole.Crewmate && human != null && Vector2.Distance(human.position, target.position) < 7.5f && HasLineOfSight(human.position, target.position))
            {
                target.suspicion += .7f; RecordSuspicion(source, 1f);
                AddEvidence("You saw " + source + " near " + target.name + " when they went down.");
            }
            else AddEvidence("A player was found needing help near " + NearestTaskName(target.position) + ".");
            ShowToast(target.name + " is down and needs help. Report them or revive them.", 3f);
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
                if (player.isHuman || !player.alive || player.downed || player.role == PlayerRole.Impostor) continue;
                float current = Vector2.Distance(human.position, player.position);
                if (current < distance && HasLineOfSight(human.position, player.position)) { target = player; distance = current; }
            }
            if (target == null) { ShowToast("Move closer to a Crewmate first."); return; }
            Incapacitate(target, "You"); killCooldown = 28f;
        }

        void ShowSabotageMenu()
        {
            if (phase != RoundPhase.Playing || humanRole != PlayerRole.Impostor) return;
            activeOverlay = "sabotage"; DestroyOverlay();
            MakePanel(overlayRoot, "Sabotage Shade", new Color(.015f, .06f, .08f, .74f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Sabotage Panel", Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(610f, 570f));
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

        static readonly string[] SabotageNames = { "Hawker Stall Gas Leak", "Main Power Blackout", "Water Supply Contamination", "Wi-Fi / PA Interference", "Trash Route Blockade" };
        static readonly string[] SabotageLocations = { "Hawker Stalls", "Cellular Tower", "Central Lake", "Cellular Tower", "South Walk" };
        static readonly int[] SabotageStations = { 0, 7, 6, 7, 8 };

        void TriggerSabotage(int index)
        {
            if (currentSabotage >= 0) { ShowToast("A disruption is already active."); return; }
            if (sabotageCooldown > 0f) { ShowToast("Sabotage controls are cooling down."); return; }
            currentSabotage = index; sabotageTimer = 32f; activeOverlay = ""; DestroyOverlay();
            sabotageCooldown = 45f;
            PlaySound(alertTone);
            AddEvidence("A disruption affected " + SabotageLocations[index] + ".");
            ShowToast(SabotageNames[index] + " · restore it within 32 seconds!", 3f);
            if (index == 1) blackout = true;
            UpdateVisionMask();
        }

        void FixSabotage()
        {
            string fixedName = SabotageNames[currentSabotage];
            if (currentSabotage == 1) blackout = false;
            currentSabotage = -1; sabotageTimer = 0f;
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
            StartMeeting(-1, "Emergency meeting called at the Notice Board.");
        }

        void ReportNearby()
        {
            if (phase != RoundPhase.Playing || humanRole == PlayerRole.Impostor || activeOverlay.Length > 0) return;
            MatchPlayer body = FindNearbyDowned();
            if (body == null) { ShowToast("No one nearby needs help."); return; }
            ReportPlayer(body);
        }

        void ReportPlayer(MatchPlayer body)
        {
            if (body == null) return;
            body.downed = false; body.alive = false; reportedPlayers.Add(body.id);
            if (body.root != null) body.root.SetActive(false);
            meetingBody = body.id;
            StartMeeting(body.id, body.name + " was reported near " + NearestTaskName(body.position) + ".");
        }

        bool HasUnreportedBody()
        {
            foreach (MatchPlayer player in players) if (!player.isHuman && player.alive && player.downed && !reportedPlayers.Contains(player.id)) return true;
            return false;
        }

        void ReportNearestBotBody()
        {
            foreach (MatchPlayer player in players) if (!player.isHuman && player.alive && player.downed && !reportedPlayers.Contains(player.id)) { ReportPlayer(player); return; }
        }

        void StartMeeting(int bodyId, string reason)
        {
            if (phase != RoundPhase.Playing) return;
            activeTask = null; activeOverlay = ""; DestroyOverlay();
            phase = RoundPhase.Meeting; meetingBody = bodyId; humanVote = -2; meetingChat = ""; meetingCooldown = 25f; meetingTimer = 0f;
            AddEvidence(reason);
            BuildMeetingOverlay();
            ShowToast("Meeting called · discuss clues and vote.", 3f);
        }

        void BuildMeetingOverlay()
        {
            DestroyOverlay();
            MakePanel(overlayRoot, "Meeting Shade", new Color(.015f, .06f, .08f, .78f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Meeting Panel", highContrast ? Hex("071A24") : Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(950f, 690f));
            AddOutline(panel, Hex("E6BD65"), 3f);
            MakeLabel(panel.transform, "COMMUNITY MEETING", new Vector2(0f, 300f), new Vector2(850f, 54f), largeText ? 34 : 31, Hex("FFF4D7"), FontStyle.Bold);
            MakeLabel(panel.transform, "Share what you saw. Evidence can guide a vote, but it never names the Impostor automatically.", new Vector2(0f, 258f), new Vector2(870f, 28f), 14, Hex("C4D5D3"), FontStyle.Normal);
            GameObject evidencePanel = MakePanel(panel.transform, "Evidence Board", Hex("223B45"), new Vector2(-202f, 107f), new Vector2(.5f, .5f), false, new Vector2(375f, 240f));
            MakeLabel(evidencePanel.transform, "RECENT CLUES", new Vector2(0f, 91f), new Vector2(340f, 28f), 14, Hex("E6BD65"), FontStyle.Bold);
            string clueText = "";
            int start = Mathf.Max(0, evidence.Count - 5);
            for (int i = start; i < evidence.Count; i++) clueText += "• " + evidence[i] + "\n";
            MakeLabel(evidencePanel.transform, clueText, new Vector2(0f, -5f), new Vector2(340f, 158f), 13, Hex("F2EBDC"), FontStyle.Normal);
            GameObject chatPanel = MakePanel(panel.transform, "Text Chat", Hex("223B45"), new Vector2(202f, 107f), new Vector2(.5f, .5f), false, new Vector2(375f, 240f));
            MakeLabel(chatPanel.transform, "TEXT-ONLY QUICK CHAT", new Vector2(0f, 91f), new Vector2(340f, 28f), 14, Hex("A6E0CF"), FontStyle.Bold);
            meetingChatLabel = MakeLabel(chatPanel.transform, meetingChat, new Vector2(0f, 30f), new Vector2(338f, 95f), 14, Hex("F2EBDC"), FontStyle.Normal);
            string[] phrases = { "I was doing a task.", "I saw someone near the lake.", "The lights went out.", "I need more evidence." };
            for (int i = 0; i < phrases.Length; i++)
            {
                int phraseIndex = i;
                AddMenuButton(chatPanel.transform, "Say: " + phrases[i], new Vector2(0f, -48f - i * 42f), () => SendQuickChat(phrases[phraseIndex]), false, new Vector2(340f, 34f));
            }
            MakeLabel(panel.transform, "VOTE TO EJECT", new Vector2(0f, -48f), new Vector2(470f, 30f), 15, Hex("E6BD65"), FontStyle.Bold);
            int aliveCount = 0; foreach (MatchPlayer player in players) if (player.alive) aliveCount++;
            int index = 0;
            foreach (MatchPlayer player in players)
            {
                if (!player.alive || player.blocked) continue;
                int voteId = player.id;
                int col = index % 4; int row = index / 4;
                Vector2 pos = new Vector2(-300f + col * 200f, -108f - row * 68f);
                AddMenuButton(panel.transform, player.isHuman ? "You" : player.name, pos, () => CastVote(voteId), player.id == humanVote, new Vector2(180f, 46f));
                index++;
            }
            AddMenuButton(panel.transform, "Skip vote", new Vector2(-128f, -243f), () => CastVote(-1), humanVote == -1, new Vector2(200f, 44f));
            AddMenuButton(panel.transform, "Confirm vote", new Vector2(128f, -243f), ResolveMeeting, true, new Vector2(220f, 44f));
            meetingTimerLabel = MakeLabel(panel.transform, "Voting closes in 40 seconds", new Vector2(0f, -292f), new Vector2(400f, 24f), 12, Hex("A9B9B8"), FontStyle.Normal);
        }

        void SendQuickChat(string phrase)
        {
            if (meetingChatLabel == null) return;
            meetingChat += "You: " + phrase + "\n";
            meetingChatLabel.text = meetingChat;
        }

        float meetingTimer;
        void UpdateMeetingClock()
        {
            meetingTimer += Time.deltaTime;
            if (meetingTimerLabel != null) meetingTimerLabel.text = "Voting closes in " + Mathf.Max(0, Mathf.CeilToInt(40f - meetingTimer)) + " seconds";
            if (meetingTimer > 40f) ResolveMeeting();
        }

        void CastVote(int targetId)
        {
            if (phase != RoundPhase.Meeting) return;
            humanVote = targetId;
            BuildMeetingOverlay();
        }

        void ResolveMeeting()
        {
            if (phase != RoundPhase.Meeting) return;
            Dictionary<int, int> votes = new Dictionary<int, int>();
            foreach (MatchPlayer voter in players)
            {
                if (!voter.alive || voter.blocked) continue;
                int choice;
                if (voter.isHuman) choice = humanVote == -2 ? -1 : humanVote;
                else if (voter.role == PlayerRole.Crewmate && voter.suspicion > .6f && human != null && humanRole == PlayerRole.Impostor) choice = 0;
                else if (voter.role == PlayerRole.Crewmate && latestEvent.IndexOf(voter.name, StringComparison.OrdinalIgnoreCase) >= 0) choice = voter.id;
                else choice = UnityEngine.Random.value < .38f ? -1 : players[UnityEngine.Random.Range(0, players.Count)].id;
                if (!votes.ContainsKey(choice)) votes[choice] = 0;
                votes[choice]++;
            }
            int chosen = -1; int highest = 0;
            foreach (KeyValuePair<int, int> vote in votes) if (vote.Value > highest) { chosen = vote.Key; highest = vote.Value; }
            int eligible = 0; foreach (MatchPlayer voter in players) if (voter.alive && !voter.blocked) eligible++;
            string outcome;
            if (chosen >= 0 && highest > eligible / 2)
            {
                MatchPlayer ejected = GetPlayer(chosen);
                if (ejected != null) { ejected.alive = false; ejected.downed = false; if (ejected.root != null) ejected.root.SetActive(false); outcome = ejected.name + " was sent home. " + (ejected.role == PlayerRole.Impostor ? "They were the Impostor." : "They were a Crewmate."); }
                else outcome = "No one was ejected.";
            }
            else outcome = "The vote ended without an ejection.";
            AddEvidence(outcome);
            phase = RoundPhase.Playing; meetingTimer = 0f; activeOverlay = ""; DestroyOverlay();
            ShowToast(outcome, 4f);
            CheckVictory();
        }

        MatchPlayer GetPlayer(int id) { foreach (MatchPlayer player in players) if (player.id == id) return player; return null; }

        void UpdateHud()
        {
            hudClock += Time.deltaTime;
            if (hudClock < .25f) return;
            hudClock = 0f;
            if (hudStatus != null) hudStatus.text = "Township Park  •  " + Mathf.FloorToInt(matchTimer / 60f).ToString("00") + ":" + Mathf.FloorToInt(matchTimer % 60f).ToString("00") + (currentSabotage >= 0 ? "  •  " + SabotageNames[currentSabotage] + " " + Mathf.CeilToInt(sabotageTimer) + "s" : "");
            if (roleLabel != null) { roleLabel.text = humanRole == PlayerRole.Crewmate ? "CREWMATE" : "IMPOSTOR"; roleLabel.color = humanRole == PlayerRole.Crewmate ? Hex("A6E0CF") : Hex("F29486"); }
            if (taskListLabel != null)
            {
                string list = "COMMUNITY TASKS\n";
                for (int i = 0; i < tasks.Count; i++) list += (tasks[i].complete ? "✓  " : "○  ") + tasks[i].title + "\n";
                taskListLabel.text = list;
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

        void ShowMap()
        {
            if (phase != RoundPhase.Playing) return;
            activeOverlay = "map"; DestroyOverlay();
            MakePanel(overlayRoot, "Map Shade", new Color(.015f, .06f, .08f, .8f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Map Card", Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(850f, 650f));
            AddOutline(panel, Hex("E6BD65"), 3f);
            MakeLabel(panel.transform, "TOWNSHIP PARK", new Vector2(0f, 282f), new Vector2(650f, 46f), 30, Hex("FFF4D7"), FontStyle.Bold);
            for (int i = 0; i < tasks.Count; i++)
            {
                TaskStation task = tasks[i];
                int col = i % 2; int row = i / 2;
                string state = task.complete ? "✓ DONE" : "OPEN";
                MakeLabel(panel.transform, (i + 1).ToString("00") + "  " + task.location + "  ·  " + state, new Vector2(-190f + col * 390f, 205f - row * 74f), new Vector2(370f, 52f), 17, task.complete ? Hex("A6E0CF") : Hex("F2EBDC"), FontStyle.Bold);
            }
            AddMenuButton(panel.transform, "Close map", new Vector2(0f, -283f), () => { activeOverlay = ""; DestroyOverlay(); }, true, new Vector2(250f, 46f));
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
            MakePanel(overlayRoot, "Safety Shade", new Color(.015f, .06f, .08f, .74f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Safety Panel", Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(720f, 650f));
            AddOutline(panel, Hex("E6BD65"), 3f);
            MakeLabel(panel.transform, "SAFETY CONTROLS", new Vector2(0f, 278f), new Vector2(640f, 50f), 30, Hex("FFF4D7"), FontStyle.Bold);
            MakeLabel(panel.transform, "Local practice bots only. Reports stay on this device for this session.", new Vector2(0f, 233f), new Vector2(640f, 28f), 13, Hex("C4D5D3"), FontStyle.Normal);
            int visible = 0;
            foreach (MatchPlayer player in players)
            {
                if (player.isHuman || !player.alive) continue;
                int id = player.id; float y = 174f - visible * 49f; visible++;
                MakeLabel(panel.transform, player.name, new Vector2(-195f, y), new Vector2(160f, 36f), 16, Color.white, FontStyle.Bold);
                AddMenuButton(panel.transform, player.muted ? "Unmute" : "Mute", new Vector2(-35f, y), () => { MatchPlayer p = GetPlayer(id); p.muted = !p.muted; ShowInGameSafety(); }, false, new Vector2(112f, 36f));
                AddMenuButton(panel.transform, player.blocked ? "Unblock" : "Block", new Vector2(90f, y), () => { MatchPlayer p = GetPlayer(id); p.blocked = !p.blocked; ShowInGameSafety(); }, false, new Vector2(112f, 36f));
                AddMenuButton(panel.transform, "Report", new Vector2(220f, y), () => ShowReportReasons(id), false, new Vector2(112f, 36f));
            }
            AddMenuButton(panel.transform, "Close", new Vector2(0f, -273f), () => { activeOverlay = ""; DestroyOverlay(); }, true, new Vector2(230f, 44f));
        }

        void ShowReportReasons(int playerId)
        {
            DestroyOverlay();
            MakePanel(overlayRoot, "Report Shade", new Color(.015f, .06f, .08f, .8f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Report Panel", Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(540f, 500f));
            AddOutline(panel, Hex("E6BD65"), 3f);
            MatchPlayer player = GetPlayer(playerId);
            MakeLabel(panel.transform, "REPORT " + (player == null ? "PLAYER" : player.name.ToUpperInvariant()), new Vector2(0f, 186f), new Vector2(490f, 44f), 26, Hex("FFF4D7"), FontStyle.Bold);
            MakeLabel(panel.transform, "Choose a reason. Reports do not trigger automatic punishment.", new Vector2(0f, 140f), new Vector2(460f, 32f), 14, Hex("C4D5D3"), FontStyle.Normal);
            string[] reasons = { "Bullying or targeted insults", "Identity-based abuse", "Unwanted sexual content", "Threat or personal information", "Cheating or disruption" };
            for (int i = 0; i < reasons.Length; i++)
            {
                string reason = reasons[i];
                AddMenuButton(panel.transform, reason, new Vector2(0f, 82f - i * 48f), () => SubmitLocalReport(playerId, reason), false, new Vector2(430f, 40f));
            }
            AddMenuButton(panel.transform, "Cancel", new Vector2(0f, -176f), ShowInGameSafety, false, new Vector2(190f, 40f));
        }

        void SubmitLocalReport(int playerId, string reason)
        {
            MatchPlayer player = GetPlayer(playerId);
            if (player != null) player.blocked = true;
            evidence.Add("Local report recorded for " + (player == null ? "player" : player.name) + ": " + reason + ".");
            activeOverlay = ""; DestroyOverlay(); ShowToast("Report saved for this practice session. Player blocked locally.", 3f);
        }

        void ShowPauseMenu()
        {
            activeOverlay = "pause"; DestroyOverlay();
            MakePanel(overlayRoot, "Pause Shade", new Color(.015f, .06f, .08f, .75f), Vector2.zero, Vector2.zero, true);
            GameObject panel = MakePanel(overlayRoot, "Pause Panel", Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(450f, 390f));
            AddOutline(panel, Hex("E6BD65"), 3f);
            MakeLabel(panel.transform, "PAUSED", new Vector2(0f, 135f), new Vector2(380f, 50f), 31, Hex("FFF4D7"), FontStyle.Bold);
            AddMenuButton(panel.transform, "Resume", new Vector2(0f, 55f), () => { activeOverlay = ""; DestroyOverlay(); }, true);
            AddMenuButton(panel.transform, "Safety & Settings", new Vector2(0f, -10f), ShowInGameSafety, false);
            AddMenuButton(panel.transform, "Return to Main Menu", new Vector2(0f, -75f), ShowMainMenu, false);
        }

        void EndMatch(PlayerRole winner, string reason)
        {
            if (phase == RoundPhase.Results || phase == RoundPhase.Menu) return;
            phase = RoundPhase.Results; activeTask = null; activeOverlay = "results"; DestroyOverlay();
            if (fogImage != null) fogImage.gameObject.SetActive(false);
            GameObject shade = MakePanel(overlayRoot, "Results Shade", new Color(.015f, .06f, .08f, .8f), Vector2.zero, Vector2.zero, true);
            GameObject card = MakePanel(overlayRoot, "Results Card", Hex("172A36"), Vector2.zero, Vector2.zero, false, new Vector2(620f, 540f));
            AddOutline(card, winner == PlayerRole.Crewmate ? Hex("78D5B3") : Hex("E88076"), 3f);
            MakeLabel(card.transform, winner == PlayerRole.Crewmate ? "CREWMATES WIN" : "IMPOSTOR WINS", new Vector2(0f, 177f), new Vector2(540f, 65f), 36, winner == PlayerRole.Crewmate ? Hex("A6E0CF") : Hex("F29486"), FontStyle.Bold);
            MakeLabel(card.transform, reason, new Vector2(0f, 112f), new Vector2(540f, 36f), 17, Hex("F2EBDC"), FontStyle.Normal);
            MakeLabel(card.transform, humanRole == PlayerRole.Crewmate ? "Your role: Crewmate" : "Your role: Impostor", new Vector2(0f, 47f), new Vector2(500f, 36f), 16, Hex("E6BD65"), FontStyle.Bold);
            string remaining = "Completed " + CountCompleted() + " of " + tasks.Count + " community tasks.";
            MakeLabel(card.transform, remaining + "\nThe match was played offline with local bots.", new Vector2(0f, -7f), new Vector2(520f, 60f), 14, Hex("C4D5D3"), FontStyle.Normal);
            AddMenuButton(card.transform, "Play Again", new Vector2(0f, -112f), () => BeginMatch(humanRole), true);
            AddMenuButton(card.transform, "Return to Menu", new Vector2(0f, -178f), ShowMainMenu, false);
        }

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
            float radius = blackout ? .13f : .50f;
            float maxAlpha = blackout ? .94f : .78f;
            for (int y = 0; y < 144; y++)
                for (int x = 0; x < 256; x++)
                {
                    float dx = (x + .5f - 128f) / 144f;
                    float dy = (y + .5f - 72f) / 144f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius - .035f, radius + .035f, d));
                    fogTexture.SetPixel(x, y, new Color(.025f, .055f, .07f, fade * maxAlpha));
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
            text.font = uiFont; text.text = value; text.fontSize = fontSize; text.color = color;
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
            Button button = go.GetComponent<Button>(); button.targetGraphic = image;
            ColorBlock colors = button.colors; colors.normalColor = color; colors.highlightedColor = Color.Lerp(color, Color.white, .16f); colors.pressedColor = Color.Lerp(color, Color.black, .12f); colors.disabledColor = new Color(color.r, color.g, color.b, .42f); button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => { PlaySound(clickTone); onClick(); });
            Text labelText = MakeLabel(go.transform, label, Vector2.zero, size - new Vector2(16f, 6f), largeText ? 18 : 15, Hex("20363C"), FontStyle.Bold);
            labelText.raycastTarget = false;
            return go;
        }

        void AddOutline(GameObject go, Color color, float distance)
        {
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
        BetweenUsRuntime game;
        Vector2 direction;
        public void Initialize(BetweenUsRuntime owner, Vector2 moveDirection) { game = owner; direction = moveDirection; }
        public void OnPointerDown(PointerEventData eventData) { if (game != null) game.SetTouchMove(direction, true); }
        public void OnPointerUp(PointerEventData eventData) { if (game != null) game.SetTouchMove(direction, false); }
        public void OnPointerExit(PointerEventData eventData) { if (game != null) game.SetTouchMove(direction, false); }
    }
}
