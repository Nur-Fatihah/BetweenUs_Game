using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>
    /// Runtime uGUI task panels shared by the networked stations and local bot-match map. Each checkpoint hosts a
    /// skill mini-game (TownshipMiniGames.cs); every round won submits the station's next required action, so the
    /// host keeps validating the exact same step sequence.
    /// </summary>
    public sealed class TownshipTaskMiniGameUI : MonoBehaviour
    {
        static TownshipTaskMiniGameUI instance;
        static Sprite circleSprite;

        Canvas canvas;
        RectTransform canvasRect;
        GameObject overlay;
        RectTransform panel;
        TownshipTaskStation networkStation;
        TaskStation localTask;
        TownshipTaskKind kind;
        string taskTitle;
        string taskLocation;
        string[] actionNames;
        int[] requiredActions;
        Action<int> localAction;
        Action localComplete;
        Action localClose;
        int progress;
        bool isOpen;
        bool finished;
        Text feedbackLabel;
        Text progressLabel;
        float panelWidth;
        float panelHeight;
        TaskMiniGame game;

        [SerializeField] TownshipTaskStation[] sceneStations = Array.Empty<TownshipTaskStation>();

        public static bool IsOpen => instance != null && instance.isOpen;

        public static void Open(TownshipTaskStation station)
        {
            if (station == null || !station.IsSpawned || station.Completed.Value) return;
            TownshipTaskMiniGameUI ui = EnsureInstance();
            ui.PrepareNetworkTask(station);
        }

        public static void OpenOffline(TaskStation task, Action<int> submitAction, Action complete, Action close)
        {
            if (task == null || task.complete) return;
            TownshipTaskMiniGameUI ui = EnsureInstance();
            ui.PrepareOfflineTask(task, submitAction, complete, close);
        }

        public static void Close()
        {
            if (instance != null) instance.ClosePanel(true);
        }

        static TownshipTaskMiniGameUI EnsureInstance()
        {
            if (instance != null) return instance;
            instance = FindAnyObjectByType<TownshipTaskMiniGameUI>(FindObjectsInactive.Include);
            if (instance != null) return instance;
            GameObject root = new GameObject("Township Task Mini Games");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<TownshipTaskMiniGameUI>();
            return instance;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            EnsureEventSystem();
            GameObject canvasObject = GetComponent<Canvas>() != null
                ? gameObject
                : new GameObject("Task Mini Game Canvas", typeof(RectTransform), typeof(Canvas));
            if (canvasObject != gameObject) canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.localScale = Vector3.one;
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            canvasRect.pivot = new Vector2(.5f, .5f);
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasObject.AddComponent<CanvasScaler>();
            PixelUiTheme.ConfigureCanvas(scaler);
            if (canvasObject.GetComponent<GraphicRaycaster>() == null) canvasObject.AddComponent<GraphicRaycaster>();
        }

        public void BindStations(TownshipTaskStation[] stations)
        {
            sceneStations = stations ?? Array.Empty<TownshipTaskStation>();
            foreach (TownshipTaskStation station in sceneStations)
                if (station != null) station.BindMiniGameUI(this);
        }

        public void OpenStation(TownshipTaskStation station)
        {
            if (station == null || !station.IsSpawned || station.Completed.Value) return;
            PrepareNetworkTask(station);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        static void EnsureEventSystem()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) eventSystem = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null)
            {
                GameObject eventRoot = new GameObject("Task UI EventSystem", typeof(EventSystem));
                eventSystem = eventRoot.GetComponent<EventSystem>();
                DontDestroyOnLoad(eventRoot);
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

        void PrepareNetworkTask(TownshipTaskStation station)
        {
            if (isOpen) ClosePanel(true);
            networkStation = station;
            localTask = null;
            localAction = null;
            localComplete = null;
            localClose = null;
            kind = station.Kind;
            taskTitle = station.TaskName;
            taskLocation = station.Location;
            actionNames = station.ActionNames;
            requiredActions = station.RequiredActions;
            progress = Mathf.Clamp(station.Progress.Value, 0, requiredActions != null ? requiredActions.Length : 0);
            BuildPanel();
        }

        void PrepareOfflineTask(TaskStation task, Action<int> submitAction, Action complete, Action close)
        {
            if (isOpen) ClosePanel(true);
            networkStation = null;
            localTask = task;
            localAction = submitAction;
            localComplete = complete;
            localClose = close;
            kind = GetOfflineKind(task);
            taskTitle = task.title;
            taskLocation = task.location;
            actionNames = task.actions;
            requiredActions = task.solution;
            progress = Mathf.Clamp(task.step, 0, requiredActions != null ? requiredActions.Length : 0);
            BuildPanel();
        }

        static TownshipTaskKind GetOfflineKind(TaskStation task)
        {
            switch (task.location)
            {
                case "Hawker Stalls": return TownshipTaskKind.NasiLemak;
                case "Convenience Store": return TownshipTaskKind.IceCream;
                case "Recreational Park":
                case "Recreation Park": return TownshipTaskKind.ParkBench;
                case "Community Hall": return TownshipTaskKind.BadmintonNet;
                case "Administrative Office": return TownshipTaskKind.CommunityPermits;
                case "Bus Stop": return TownshipTaskKind.BusSchedule;
                case "Central Lake": return TownshipTaskKind.LakeCleanup;
                case "Cellular Tower": return TownshipTaskKind.Router;
                case "Garden Walkway":
                case "Pedestrian Walkway": return TownshipTaskKind.SolarBatteries;
                case "Notice Board": return TownshipTaskKind.NoticeBoard;
                default: return TownshipTaskKind.CommunityPermits;
            }
        }

        void BuildPanel()
        {
            DestroyOverlay();
            EnsureEventSystem();
            isOpen = true;
            finished = false;

            float availableWidth = canvasRect.rect.width > 1f ? canvasRect.rect.width : Screen.width / Mathf.Max(.01f, canvas.scaleFactor);
            float availableHeight = canvasRect.rect.height > 1f ? canvasRect.rect.height : Screen.height / Mathf.Max(.01f, canvas.scaleFactor);
            panelWidth = Mathf.Min(1160f, availableWidth * .94f);
            panelHeight = Mathf.Min(760f, availableHeight * .94f);
            overlay = new GameObject("Task Mini Game Overlay", typeof(RectTransform));
            overlay.transform.SetParent(canvasRect, false);
            Stretch(overlay.GetComponent<RectTransform>());
            Image shade = overlay.AddComponent<Image>();
            shade.color = new Color(.20f, .13f, .08f, .58f);
            shade.raycastTarget = true;

            GameObject panelObject = CreateImage(overlay.transform, "Task Panel", new Vector2(panelWidth, panelHeight), Vector2.zero, Hex("172B34"), false);
            PixelUiTheme.ApplyWindow(panelObject, true);
            panel = panelObject.GetComponent<RectTransform>();
            Outline outline = panelObject.AddComponent<Outline>();
            outline.effectColor = Hex("E8C36A");
            outline.effectDistance = new Vector2(2f, -2f);

            game = TaskMiniGame.Create(kind);
            float headerY = panelHeight * .5f;
            CreateText(panel, taskLocation.ToUpperInvariant(), new Vector2(0f, headerY - 28f), new Vector2(panelWidth * .82f, 25f), 15, Hex("E9C875"), FontStyle.Bold);
            CreateText(panel, taskTitle, new Vector2(0f, headerY - 61f), new Vector2(panelWidth * .88f, 42f), 28, Hex("FFF5DD"), FontStyle.Bold);
            CreateText(panel, game.HowTo, new Vector2(0f, headerY - 104f), new Vector2(panelWidth * .86f, 42f), 15, Hex("C5D7D4"), FontStyle.Normal);

            float contentTop = headerY - 136f;
            float contentBottom = -panelHeight * .5f + 112f;
            RectTransform content = new GameObject("Mini Game Area", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(panel, false);
            content.sizeDelta = new Vector2(panelWidth * .92f, contentTop - contentBottom);
            content.anchoredPosition = new Vector2(0f, (contentTop + contentBottom) * .5f);
            // Moving pieces spawn just outside the play area; clip them so they slide in rather than pop in.
            content.gameObject.AddComponent<RectMask2D>();

            progressLabel = CreateText(panel, string.Empty, new Vector2(0f, -panelHeight * .5f + 82f), new Vector2(panelWidth * .68f, 25f), 15, Hex("A7E1CC"), FontStyle.Bold);
            feedbackLabel = CreateText(panel, "", new Vector2(0f, -panelHeight * .5f + 52f), new Vector2(panelWidth * .66f, 26f), 15, Hex("FFF0CB"), FontStyle.Normal);
            CreateButton(panel, "Close task", new Vector2(panelWidth * .5f - 100f, -panelHeight * .5f + 50f), new Vector2(150f, 42f), Hex("39515A"), () => ClosePanel(true));
            RefreshProgressLabel();
            game.Attach(this, content);
            Typography.Apply(panel);
        }

        // ---- API for mini-games ----

        public int Progress => progress;
        public int Steps => requiredActions != null ? requiredActions.Length : 0;
        public int Required(int step) => requiredActions != null && step >= 0 && step < requiredActions.Length ? requiredActions[step] : 0;
        public int ActionCount => actionNames != null ? actionNames.Length : 0;
        public bool Playable => CanInteract();

        public string ActionName(int action)
        {
            if (actionNames != null && action >= 0 && action < actionNames.Length) return actionNames[action];
            return "Step " + (action + 1);
        }

        /// <summary>The player won a round: submit the next required action and start the following round.</summary>
        public void Win(string message)
        {
            if (!CanInteract() || progress >= Steps) return;
            Feedback(message, false);
            GameAudioManager.PlayTaskInteraction();
            AdvanceTask(requiredActions[progress]);
            if (!finished) game.StartRound();
        }

        /// <summary>A mistake only costs time; progress already sent to the host is never rolled back.</summary>
        public void Miss(string message) => Feedback(message, true);

        void AdvanceTask(int action)
        {
            if (progress >= requiredActions.Length) return;
            SubmitAction(action);
            progress++;
            RefreshProgressLabel();
            if (progress >= requiredActions.Length) FinishTask();
        }

        bool CanInteract()
        {
            if (!isOpen || finished) return false;
            if (networkStation != null && (!networkStation.IsSpawned || networkStation.Completed.Value ||
                GameManager.Instance == null || GameManager.Instance.Phase.Value != RoundPhase.Playing)) return false;
            return true;
        }

        void SubmitAction(int action)
        {
            if (networkStation != null)
            {
                if (networkStation.IsSpawned) networkStation.RequestActionRpc(action);
            }
            else localAction?.Invoke(action);
        }

        void FinishTask()
        {
            if (finished) return;
            finished = true;
            Feedback("Task complete — well done!", false);
            GameAudioManager.PlayTaskComplete();
            if (networkStation != null && GameManager.Instance != null)
                GameManager.Instance.ReportTaskCompletion(networkStation);
            RefreshProgressLabel();
            if (networkStation == null)
            {
                Action callback = localComplete;
                ClosePanel(false);
                callback?.Invoke();
            }
            else StartCoroutine(CloseAfterSuccess());
        }

        IEnumerator CloseAfterSuccess()
        {
            yield return new WaitForSecondsRealtime(.8f);
            if (isOpen) ClosePanel(false);
        }

        void RefreshProgressLabel()
        {
            if (progressLabel == null) return;
            int total = Steps;
            progressLabel.text = "PARK TASK  ·  " + Mathf.Min(progress, total) + " / " + total + " steps";
        }

        void Feedback(string message, bool error)
        {
            if (feedbackLabel == null) return;
            feedbackLabel.text = message;
            feedbackLabel.color = error ? Hex("F0A294") : Hex("C7E8D8");
            if (error) GameAudioManager.PlayTaskMiss();
        }

        void ClosePanel(bool notifyClose)
        {
            if (!isOpen) return;
            isOpen = false;
            Action callback = notifyClose ? localClose : null;
            localClose = null;
            localComplete = null;
            localAction = null;
            networkStation = null;
            localTask = null;
            DestroyOverlay();
            callback?.Invoke();
        }

        void DestroyOverlay()
        {
            if (overlay != null)
            {
                overlay.SetActive(false);
                Destroy(overlay);
            }
            overlay = null;
            panel = null;
            feedbackLabel = null;
            progressLabel = null;
            game = null;
        }

        void Update()
        {
            if (!isOpen) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ClosePanel(true);
                return;
            }
            if (networkStation != null && (!networkStation.IsSpawned || networkStation.Completed.Value))
            {
                ClosePanel(false);
                return;
            }
            if (networkStation != null && (GameManager.Instance == null || GameManager.Instance.Phase.Value != RoundPhase.Playing))
            {
                ClosePanel(false);
                return;
            }
            if (game == null || !CanInteract()) return;
            game.Tick(Mathf.Min(Time.unscaledDeltaTime, .05f));
            if (keyboard == null) return;
            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame) { GameAudioManager.PlayGameTap(); game.Primary(); }
            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
            for (int i = 0; i < digits.Length; i++) if (keyboard[digits[i]].wasPressedThisFrame) { if (!(game is FreezerMemory)) GameAudioManager.PlayGameTap(); game.Number(i); }
        }

        // ---- UI builders shared with the mini-games ----

        public static Sprite GetCircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Runtime UI Circle";
            texture.hideFlags = HideFlags.HideAndDontSave;
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * .5f, (size - 1) * .5f);
            float radius = size * .48f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = Vector2.Distance(new Vector2(x, y), center) <= radius ? Color.white : Color.clear;
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f), size);
            circleSprite.name = "Runtime UI Circle Sprite";
            circleSprite.hideFlags = HideFlags.HideAndDontSave;
            return circleSprite;
        }

        public GameObject CreateImage(Transform parent, string name, Vector2 size, Vector2 position, Color color, bool raycastTarget)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return go;
        }

        public Text CreateText(Transform parent, string value, Vector2 position, Vector2 size, int fontSize, Color color, FontStyle style)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = go.GetComponent<Text>();
            text.font = PixelUiTheme.BodyFont;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = PixelUiTheme.ReadableTextColor(parent, color);
            text.raycastTarget = false;
            return text;
        }

        public Button CreateButton(Transform parent, string label, Vector2 position, Vector2 size, Color color, Action action)
        {
            GameObject go = CreateImage(parent, label + " Button", size, position, color, true);
            Button button = go.AddComponent<Button>();
            PixelUiTheme.StyleButton(button, color);
            // Keyboard focus would make Space both "press this button" and the game's action key.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            CreateText(go.transform, label, Vector2.zero, size - new Vector2(12f, 8f), 15, PixelUiTheme.ButtonTextColor(color), FontStyle.Bold);
            if (action != null) button.onClick.AddListener(() => { EventSystem.current?.SetSelectedGameObject(null); GameAudioManager.PlayGameTap(); action(); });
            return button;
        }

        public Slider CreateSlider(Transform parent, Vector2 position, float width, Color fillColor)
        {
            GameObject sliderRoot = CreateImage(parent, "Slider", new Vector2(width, 30f), position, Hex("344E56"), true);
            Slider slider = sliderRoot.AddComponent<Slider>();
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderRoot.transform, false);
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, .15f); fillAreaRect.anchorMax = new Vector2(1f, .85f);
            fillAreaRect.offsetMin = new Vector2(6f, 0f); fillAreaRect.offsetMax = new Vector2(-6f, 0f);
            GameObject fill = CreateImage(fillArea.transform, "Fill", Vector2.zero, Vector2.zero, fillColor, false);
            Stretch(fill.GetComponent<RectTransform>());
            slider.fillRect = fill.GetComponent<RectTransform>();
            GameObject handle = CreateImage(sliderRoot.transform, "Handle", new Vector2(28f, 48f), Vector2.zero, Hex("F0C966"), true);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = handleRect.anchorMax = new Vector2(0f, .5f);
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static Color Hex(string value)
        {
            if (ColorUtility.TryParseHtmlString("#" + value, out Color color)) return color;
            return Color.white;
        }
    }
}
