using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Crony
{
    /// <summary>Responsive uGUI customization menu with a layered preview and locally saved, network-equipped looks.</summary>
    public sealed class AvatarDressingRoomUI : MonoBehaviour
    {
        enum Tab : byte { Skin, Eyes, Hair, Clothes, Hats, Accessories }

        static readonly Color32[] SkinPalette =
        {
            new Color32(255, 224, 189, 255), new Color32(240, 200, 167, 255), new Color32(217, 162, 126, 255),
            new Color32(191, 130, 93, 255), new Color32(169, 109, 77, 255), new Color32(127, 80, 61, 255),
            new Color32(90, 56, 47, 255), new Color32(226, 178, 142, 255)
        };
        static readonly Color32[] HairPalette =
        {
            new Color32(45, 37, 35, 255), new Color32(80, 54, 42, 255), new Color32(126, 77, 48, 255),
            new Color32(183, 127, 69, 255), new Color32(221, 188, 123, 255), new Color32(42, 64, 86, 255),
            new Color32(120, 65, 102, 255), new Color32(232, 232, 221, 255)
        };
        static readonly Color32[] ClothingPalette =
        {
            new Color32(239, 94, 96, 255), new Color32(242, 173, 75, 255), new Color32(238, 217, 120, 255),
            new Color32(89, 171, 131, 255), new Color32(86, 160, 196, 255), new Color32(100, 116, 194, 255),
            new Color32(165, 111, 181, 255), new Color32(239, 230, 210, 255)
        };

        [SerializeField] AvatarDatabaseSO database;

        Canvas canvas;
        RectTransform canvasRect;
        GameObject overlay;
        RectTransform panelRect;
        RectTransform gridContent;
        RectTransform subcategoryRoot;
        UnityEngine.UI.Slider hueSlider;
        UnityEngine.UI.ScrollRect itemScroll;
        UnityEngine.UI.GridLayoutGroup itemGrid;
        float lastGridWidth = -1f;
        readonly List<UnityEngine.UI.Button> itemButtons = new List<UnityEngine.UI.Button>();
        readonly List<int> itemIndices = new List<int>();
        UnityEngine.UI.Text statusLabel;
        UnityEngine.UI.Image[] previewLayers = Array.Empty<UnityEngine.UI.Image>();
        readonly List<UnityEngine.UI.Button> tabButtons = new List<UnityEngine.UI.Button>();
        readonly List<UnityEngine.UI.Button> subcategoryButtons = new List<UnityEngine.UI.Button>();
        readonly List<Vector2> tabPositions = new List<Vector2>();

        PlayerAvatarLayerManager target;
        AvatarLookState look;
        Tab activeTab;
        AvatarItemCategory activeCategory = AvatarItemCategory.Skin;
        bool isOpen;
        int oldSortingOrder;
        bool oldOverrideSorting;

        public static bool IsOpen { get; private set; }

        public static void Open(PlayerAvatarLayerManager avatar)
        {
            if (avatar == null) return;
            FindOrCreate().OpenFor(avatar.Database, avatar);
        }

        public static void Open(AvatarDatabaseSO sourceDatabase) => FindOrCreate().OpenFor(sourceDatabase, null);

        public static void Open() => FindOrCreate().OpenFor(null, null);

        static AvatarDressingRoomUI FindOrCreate()
        {
            AvatarDressingRoomUI ui = UnityEngine.Object.FindAnyObjectByType<AvatarDressingRoomUI>(FindObjectsInactive.Include);
            if (ui != null) return ui;
            GameObject root = new GameObject("Avatar Dressing Room Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            Canvas standaloneCanvas = root.GetComponent<Canvas>();
            standaloneCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            standaloneCanvas.sortingOrder = 120;
            return root.AddComponent<AvatarDressingRoomUI>();
        }

        public void SetDatabase(AvatarDatabaseSO value) => database = value;

        void Awake()
        {
            EnsureCanvas();
            EnsureEventSystem();
            BuildMenu();
        }

        void OnDestroy()
        {
            if (isOpen) Close();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (!TownshipTextChatUI.CapturesInput && keyboard != null && keyboard.cKey.wasPressedThisFrame)
            {
                if (isOpen) Close();
                else OpenFromLobby();
            }

            if (!isOpen) return;
            ReflowItems();
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
            if (target != null && (!target.isActiveAndEnabled || (target.IsSpawned && !target.IsOwner))) Close();
            Unity.Netcode.NetworkManager network = Unity.Netcode.NetworkManager.Singleton;
            if (network != null && network.IsListening && GameManager.Instance != null && GameManager.Instance.IsSpawned &&
                GameManager.Instance.Phase.Value != RoundPhase.Menu)
            {
                Close();
                return;
            }
            if ((network == null || !network.IsListening) && !CronyRuntime.CanOpenAvatarCustomization)
                Close();
        }

        void OpenFromLobby()
        {
            PlayerAvatarLayerManager avatar = null;
            Unity.Netcode.NetworkManager network = Unity.Netcode.NetworkManager.Singleton;
            if (network != null && network.IsListening)
            {
                GameManager game = GameManager.Instance;
                if (game != null && game.IsSpawned && game.Phase.Value != RoundPhase.Menu) return;
                if (network.LocalClient != null && network.LocalClient.PlayerObject != null)
                    avatar = network.LocalClient.PlayerObject.GetComponent<PlayerAvatarLayerManager>();
            }
            else if (!CronyRuntime.CanOpenAvatarCustomization)
            {
                return;
            }

            OpenFor(avatar != null ? avatar.Database : database, avatar);
        }

        void EnsureCanvas()
        {
            canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.localScale = Vector3.one;
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            canvasRect.pivot = new Vector2(.5f, .5f);
            UnityEngine.UI.CanvasScaler scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            PixelUiTheme.ConfigureCanvas(scaler);
            UnityEngine.UI.GraphicRaycaster raycaster = GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster == null) raycaster = gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            raycaster.enabled = true;
        }

        static void EnsureEventSystem()
        {
            UnityEngine.EventSystems.EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null)
            {
                GameObject root = new GameObject("Avatar UI EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(InputSystemUIInputModule));
                eventSystem = root.GetComponent<UnityEngine.EventSystems.EventSystem>();
                root.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            else if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                InputSystemUIInputModule module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }
        }

        void BuildMenu()
        {
            if (overlay != null) return;
            overlay = new GameObject("Avatar Dressing Room Overlay", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            overlay.transform.SetParent(canvasRect, false);
            RectTransform overlayRect = overlay.GetComponent<RectTransform>();
            Stretch(overlayRect);
            UnityEngine.UI.Image shade = overlay.GetComponent<UnityEngine.UI.Image>();
            shade.color = new Color(.025f, .09f, .08f, .78f);
            shade.raycastTarget = true;

            GameObject panel = CreateImage(overlay.transform, "Dressing Room Panel", new Vector2(1510f, 930f), Vector2.zero, PixelUiTheme.Cream, false);
            panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(.035f, .045f);
            panelRect.anchorMax = new Vector2(.965f, .955f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            PixelUiTheme.ApplyWindow(panel, true);

            CreateAnchoredText(panel.transform, "AVATAR DRESSING ROOM", new Vector2(.04f, .925f), new Vector2(.70f, .99f), 25, PixelUiTheme.Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
            AddButton(panel.transform, "Close dressing room", "CLOSE  ×", new Vector2(.88f, .925f), new Vector2(.97f, .99f), PixelUiTheme.Almond, Close);

            GameObject stage = CreateAnchoredImage(panel.transform, "Preview Backdrop", new Vector2(.025f, .14f), new Vector2(.335f, .89f), PixelUiTheme.Linen, false);
            PixelUiTheme.ApplyCard(stage, false);
            GameObject spotlight = CreateImage(stage.transform, "Preview Spotlight", new Vector2(360f, 520f), new Vector2(0f, -12f), new Color(1f, .81f, .35f, .16f), false);
            UnityEngine.UI.Image spotlightImage = spotlight.GetComponent<UnityEngine.UI.Image>();
            spotlightImage.sprite = PixelUiTheme.GetDotSprite();
            spotlightImage.color = new Color(.25f, .45f, .35f, .13f);
            RectTransform spotlightRect = spotlight.GetComponent<RectTransform>();
            spotlightRect.anchorMin = new Vector2(.12f, .16f);
            spotlightRect.anchorMax = new Vector2(.88f, .28f);
            spotlightRect.offsetMin = spotlightRect.offsetMax = Vector2.zero;
            CreateAnchoredText(panel.transform, "LIVE AVATAR STAGE", new Vector2(.045f, .82f), new Vector2(.315f, .875f), 14, PixelUiTheme.MutedInk, FontStyle.Bold, TextAnchor.MiddleCenter);
            CreateAnchoredText(panel.transform, "Your character faces front while you mix colors and accessories.", new Vector2(.045f, .16f), new Vector2(.315f, .22f), 13, PixelUiTheme.MutedInk, FontStyle.Normal, TextAnchor.MiddleCenter);
            BuildPreview(panel.transform);

            BuildTabs(panel.transform);
            subcategoryRoot = CreateRect(panel.transform, "Subcategory Tabs", new Vector2(.36f, .765f), new Vector2(.975f, .815f));
            BuildGrid(panel.transform);
            BuildPalette(panel.transform);

            statusLabel = CreateAnchoredText(panel.transform, "Choose an item, then save and equip.", new Vector2(.36f, .045f), new Vector2(.71f, .12f), 18, PixelUiTheme.MutedInk, FontStyle.Normal, TextAnchor.MiddleLeft);
            AddButton(panel.transform, "Randomize Avatar", "RANDOMIZE", new Vector2(.73f, .045f), new Vector2(.845f, .12f), PixelUiTheme.Mint, RandomizeAvatar);
            AddButton(panel.transform, "Save and equip avatar", "SAVE & EQUIP", new Vector2(.855f, .045f), new Vector2(.975f, .12f), PixelUiTheme.Amber, SaveAndEquip);
            overlay.SetActive(false);
        }

        void BuildPreview(Transform parent)
        {
            GameObject previewRoot = new GameObject("Layered Avatar Preview", typeof(RectTransform));
            previewRoot.transform.SetParent(parent, false);
            RectTransform rootRect = previewRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(.065f, .26f);
            rootRect.anchorMax = new Vector2(.295f, .80f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            previewLayers = new UnityEngine.UI.Image[PlayerAvatarLayerManager.LayerCount];
            for (int i = 0; i < previewLayers.Length; i++)
            {
                GameObject imageObject = new GameObject("Preview Layer " + i, typeof(RectTransform), typeof(UnityEngine.UI.Image));
                imageObject.transform.SetParent(previewRoot.transform, false);
                RectTransform imageRect = imageObject.GetComponent<RectTransform>();
                imageRect.anchorMin = imageRect.anchorMax = new Vector2(.5f, .5f);
                imageRect.pivot = new Vector2(.5f, .5f);
                imageRect.sizeDelta = new Vector2(160f, 320f);
                UnityEngine.UI.Image image = imageObject.GetComponent<UnityEngine.UI.Image>();
                image.preserveAspect = false;
                image.raycastTarget = false;
                previewLayers[i] = image;
            }
        }

        void BuildTabs(Transform parent)
        {
            string[] names = { "01  SKIN", "02  FACE", "03  HAIR", "04  OUTFIT", "05  HATS", "06  EXTRAS" };
            for (int i = 0; i < names.Length; i++)
            {
                int tabIndex = i;
                float left = .36f + i * .103f;
                Vector2 position = new Vector2(left, .83f);
                UnityEngine.UI.Button button = AddButton(parent, "Avatar Tab " + names[i], names[i], position, new Vector2(left + .098f, .89f), PixelUiTheme.Tan, () => SelectTab((Tab)tabIndex));
                tabButtons.Add(button);
                tabPositions.Add(button.GetComponent<RectTransform>().anchoredPosition);
            }
        }

        void BuildGrid(Transform parent)
        {
            GameObject scrollRoot = CreateAnchoredImage(parent, "Avatar Item Scroll View", new Vector2(.36f, .285f), new Vector2(.975f, .75f), PixelUiTheme.Almond, true);
            PixelUiTheme.ApplyCard(scrollRoot, false);
            UnityEngine.UI.ScrollRect scroll = scrollRoot.AddComponent<UnityEngine.UI.ScrollRect>();
            itemScroll = scroll;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Mask));
            viewport.transform.SetParent(scrollRoot.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(8f, 8f);
            viewportRect.offsetMax = new Vector2(-24f, -8f);
            viewport.GetComponent<UnityEngine.UI.Image>().color = PixelUiTheme.Linen;
            viewport.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            viewport.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;

            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(UnityEngine.UI.GridLayoutGroup), typeof(UnityEngine.UI.ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            gridContent = content.GetComponent<RectTransform>();
            gridContent.anchorMin = new Vector2(0f, 1f);
            gridContent.anchorMax = new Vector2(1f, 1f);
            gridContent.pivot = new Vector2(.5f, 1f);
            gridContent.anchoredPosition = Vector2.zero;
            gridContent.sizeDelta = Vector2.zero;
            UnityEngine.UI.GridLayoutGroup grid = content.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            itemGrid = grid;
            grid.cellSize = new Vector2(132f, 140f);
            grid.spacing = new Vector2(12f, 12f);
            grid.padding = new RectOffset(16, 16, 16, 16);
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 7;
            grid.startAxis = UnityEngine.UI.GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            UnityEngine.UI.ContentSizeFitter fitter = content.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewportRect;
            scroll.content = gridContent;
            GameObject rail = CreateAnchoredImage(scrollRoot.transform, "Item Scrollbar", new Vector2(1f, 0f), Vector2.one, PixelUiTheme.Linen, true);
            RectTransform railRect = rail.GetComponent<RectTransform>();
            railRect.offsetMin = new Vector2(-18f, 12f);
            railRect.offsetMax = new Vector2(-6f, -12f);
            GameObject handle = CreateAnchoredImage(rail.transform, "Handle", Vector2.zero, Vector2.one, PixelUiTheme.MutedInk, true);
            var scrollbar = rail.AddComponent<UnityEngine.UI.Scrollbar>();
            scrollbar.handleRect = handle.GetComponent<RectTransform>();
            scrollbar.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
            scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;

        }

        void BuildPalette(Transform parent)
        {
            CreateAnchoredText(parent, "COLOR PALETTE", new Vector2(.36f, .22f), new Vector2(.49f, .27f), 12, PixelUiTheme.MutedInk, FontStyle.Bold, TextAnchor.MiddleLeft);
            RectTransform palette = CreateRect(parent, "Color Swatches", new Vector2(.49f, .205f), new Vector2(.975f, .275f));
            for (int i = 0; i < ClothingPalette.Length; i++)
            {
                int colorIndex = i;
                float left = .01f + i * .12f;
                GameObject swatch = CreateImage(palette, "Color Swatch " + i, new Vector2(40f, 40f), Vector2.zero, Color.white, true);
                RectTransform rect = swatch.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(left, .5f);
                rect.anchorMax = new Vector2(left, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                UnityEngine.UI.Image image = swatch.GetComponent<UnityEngine.UI.Image>();
                image.sprite = PixelUiTheme.GetDotSprite();
                image.raycastTarget = true;
                UnityEngine.UI.Button button = swatch.AddComponent<UnityEngine.UI.Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => SelectPaletteColor(colorIndex));
                UnityEngine.UI.Outline outline = swatch.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = PixelUiTheme.Chocolate;
                outline.effectDistance = new Vector2(2f, -2f);
                swatch.AddComponent<UIButtonJuice>();
            }

            GameObject hueRoot = new GameObject("Hue Picker Slider", typeof(RectTransform), typeof(UnityEngine.UI.Slider), typeof(UnityEngine.UI.Image));
            hueRoot.transform.SetParent(parent, false);
            RectTransform hueRect = hueRoot.GetComponent<RectTransform>();
            hueRect.anchorMin = new Vector2(.49f, .165f);
            hueRect.anchorMax = new Vector2(.975f, .185f);
            hueRect.offsetMin = Vector2.zero;
            hueRect.offsetMax = Vector2.zero;
            UnityEngine.UI.Image sliderBackground = hueRoot.GetComponent<UnityEngine.UI.Image>();
            sliderBackground.color = Color.clear;
            sliderBackground.raycastTarget = false;
            GameObject trackObject = CreateAnchoredImage(hueRoot.transform, "Warm Tan Slider Track", Vector2.zero, Vector2.one, PixelUiTheme.Tan, true);
            UnityEngine.UI.Image hueTrack = trackObject.GetComponent<UnityEngine.UI.Image>();
            hueTrack.sprite = PixelUiTheme.GetRoundSprite();
            Texture2D spectrum = new Texture2D(128, 1, TextureFormat.RGBA32, false);
            spectrum.name = "Avatar hue spectrum";
            spectrum.wrapMode = TextureWrapMode.Clamp;
            for (int x = 0; x < 128; x++) spectrum.SetPixel(x, 0, Color.HSVToRGB(x / 127f, .65f, .9f));
            spectrum.Apply();
            hueTrack.sprite = Sprite.Create(spectrum, new Rect(0, 0, 128, 1), new Vector2(.5f, .5f));
            hueTrack.type = UnityEngine.UI.Image.Type.Simple;
            hueTrack.color = Color.white;
            hueTrack.raycastTarget = true;
            GameObject hueFillObject = CreateAnchoredImage(hueRoot.transform, "Hue Fill", new Vector2(0f, .35f), new Vector2(0f, .65f), PixelUiTheme.Amber, false);
            hueFillObject.GetComponent<UnityEngine.UI.Image>().enabled = false;
            RectTransform hueFill = hueFillObject.GetComponent<RectTransform>();
            hueFill.anchorMin = new Vector2(0f, .5f);
            hueFill.anchorMax = new Vector2(0f, .5f);
            hueFill.pivot = new Vector2(0f, .5f);
            hueFill.sizeDelta = new Vector2(0f, 12f);
            GameObject hueHandle = CreateImage(hueRoot.transform, "Hue Picker Handle", new Vector2(28f, 28f), Vector2.zero, PixelUiTheme.Cream, true);
            UnityEngine.UI.Image hueHandleImage = hueHandle.GetComponent<UnityEngine.UI.Image>();
            hueHandleImage.sprite = PixelUiTheme.GetRoundSprite();
            hueHandle.AddComponent<UnityEngine.UI.Outline>().effectColor = PixelUiTheme.Chocolate;
            hueHandle.GetComponent<UnityEngine.UI.Outline>().effectDistance = new Vector2(2f, -2f);
            UnityEngine.UI.Slider slider = hueRoot.GetComponent<UnityEngine.UI.Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.fillRect = hueFill;
            slider.handleRect = hueHandle.GetComponent<RectTransform>();
            slider.targetGraphic = hueHandleImage;
            slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Automatic };
            slider.onValueChanged.AddListener(ApplyHue);
            hueSlider = slider;
        }

        public void OpenFor(PlayerAvatarLayerManager avatar)
        {
            if (avatar == null) return;
            OpenFor(avatar.Database, avatar);
        }

        void OpenFor(AvatarDatabaseSO sourceDatabase, PlayerAvatarLayerManager avatar)
        {
            EnsureCanvas();
            EnsureEventSystem();
            BuildMenu();
            target = avatar;
            database = ResolveDatabase(sourceDatabase, avatar);
            look = database == null ? AvatarLookState.CreateDefault() : database.Sanitize(avatar != null ? avatar.CurrentState : PlayerAvatarLayerManager.LoadPreferences(database));
            if (canvas != null)
            {
                oldSortingOrder = canvas.sortingOrder;
                oldOverrideSorting = canvas.overrideSorting;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 120;
            }
            isOpen = IsOpen = true;
            overlay.SetActive(true);
            Canvas.ForceUpdateCanvases();
            lastGridWidth = -1f;
            ReflowItems();
            SelectTab(Tab.Skin);
            RefreshPreview();
            if (statusLabel != null) statusLabel.text = "Choose an item, then save and equip.";
        }

        AvatarDatabaseSO ResolveDatabase(AvatarDatabaseSO sourceDatabase, PlayerAvatarLayerManager avatar)
        {
            if (sourceDatabase != null) return sourceDatabase;
            if (avatar != null && avatar.Database != null) return avatar.Database;
            if (database != null) return database;
            Unity.Netcode.NetworkManager net = Unity.Netcode.NetworkManager.Singleton;
            if (net == null) net = UnityEngine.Object.FindAnyObjectByType<Unity.Netcode.NetworkManager>(FindObjectsInactive.Include);
            if (net != null && net.NetworkConfig != null && net.NetworkConfig.PlayerPrefab != null)
            {
                PlayerAvatarLayerManager prefabAvatar = net.NetworkConfig.PlayerPrefab.GetComponent<PlayerAvatarLayerManager>();
                if (prefabAvatar != null && prefabAvatar.Database != null) return prefabAvatar.Database;
            }
            AvatarDatabaseSO[] loaded = Resources.FindObjectsOfTypeAll<AvatarDatabaseSO>();
            return loaded != null && loaded.Length > 0 ? loaded[0] : null;
        }

        public void Close()
        {
            if (!isOpen) return;
            isOpen = IsOpen = false;
            target = null;
            if (overlay != null) overlay.SetActive(false);
            if (canvas != null)
            {
                canvas.sortingOrder = oldSortingOrder;
                canvas.overrideSorting = oldOverrideSorting;
            }
        }

        void SelectTab(Tab tab)
        {
            activeTab = tab;
            if (tab == Tab.Skin) activeCategory = AvatarItemCategory.Skin;
            else if (tab == Tab.Eyes) activeCategory = AvatarItemCategory.Face;
            else if (tab == Tab.Hair) activeCategory = AvatarItemCategory.Hair;
            else if (tab == Tab.Clothes) activeCategory = AvatarItemCategory.Top;
            else if (tab == Tab.Hats) activeCategory = AvatarItemCategory.Hat;
            else activeCategory = AvatarItemCategory.Accessory;
            for (int i = 0; i < tabButtons.Count; i++)
            {
                RectTransform tabRect = tabButtons[i].GetComponent<RectTransform>();
                tabRect.anchoredPosition = tabPositions[i];
                PixelUiTheme.SetButtonFace(tabButtons[i], i == (int)tab ? PixelUiTheme.Gold : PixelUiTheme.Tan, i == (int)tab);
            }
            BuildSubcategories();
            PopulateItems();
            UpdatePaletteVisibility();
            RefreshPreview();
        }

        void BuildSubcategories()
        {
            foreach (UnityEngine.UI.Button button in subcategoryButtons)
                if (button != null) Destroy(button.gameObject);
            subcategoryButtons.Clear();
            if (subcategoryRoot == null) return;
            if (activeTab == Tab.Clothes)
            {
                AddSubcategory("TOPS", AvatarItemCategory.Top, 0);
                AddSubcategory("BOTTOMS", AvatarItemCategory.Bottom, 1);
                AddSubcategory("SHOES", AvatarItemCategory.Shoes, 2);
            }
            else if (activeTab == Tab.Eyes)
            {
                AddSubcategory("EXPRESSIONS", AvatarItemCategory.Face, 0);
                AddSubcategory("TATTOOS", AvatarItemCategory.Tattoo, 1);
            }
            else if (activeTab == Tab.Accessories)
            {
                AddSubcategory("ACCESSORIES", AvatarItemCategory.Accessory, 0);
                AddSubcategory("TATTOOS", AvatarItemCategory.Tattoo, 1);
            }
        }

        void AddSubcategory(string label, AvatarItemCategory category, int index)
        {
            float left = .01f + index * .19f;
            UnityEngine.UI.Button button = AddButton(subcategoryRoot, "Avatar Subcategory " + label, label,
                new Vector2(left, .05f), new Vector2(left + .17f, .95f), PixelUiTheme.Tan, null);
            subcategoryButtons.Add(button);
            button.onClick.AddListener(() =>
            {
                activeCategory = category;
                for (int i = 0; i < subcategoryButtons.Count; i++) PixelUiTheme.SetButtonFace(subcategoryButtons[i], subcategoryButtons[i] == button ? PixelUiTheme.Gold : PixelUiTheme.Tan, subcategoryButtons[i] == button);
                PopulateItems();
                UpdatePaletteVisibility();
                RefreshPreview();
            });
            if (index == 0) PixelUiTheme.SetButtonFace(button, PixelUiTheme.Gold, true);
        }

        void PopulateItems()
        {
            if (gridContent == null) return;
            itemButtons.Clear();
            itemIndices.Clear();
            for (int i = gridContent.childCount - 1; i >= 0; i--)
            {
                gridContent.GetChild(i).gameObject.SetActive(false);
                Destroy(gridContent.GetChild(i).gameObject);
            }
            IReadOnlyList<AvatarItemSO> items = database != null ? database.GetItems(activeCategory) : Array.Empty<AvatarItemSO>();
            for (int i = 0; i < items.Count; i++)
            {
                AvatarItemSO item = items[i];
                if (item == null) continue;
                AvatarItemSO captured = item;
                UnityEngine.UI.Button card = AddButton(gridContent, "Avatar Item " + item.ItemID, item.DisplayName,
                    new Vector2(.5f, .5f), new Vector2(.5f, .5f), PixelUiTheme.Almond, () => ChooseItem(captured));
                itemButtons.Add(card);
                itemIndices.Add(i);
                card.GetComponent<RectTransform>().sizeDelta = new Vector2(132f, 140f);
                UnityEngine.UI.Text itemLabel = card.GetComponentInChildren<UnityEngine.UI.Text>();
                if (itemLabel != null)
                {
                    itemLabel.fontSize = 16;
                    itemLabel.color = PixelUiTheme.Ink;
                    itemLabel.alignment = TextAnchor.MiddleCenter;
                    RectTransform labelRect = itemLabel.rectTransform;
                    labelRect.anchorMin = new Vector2(.04f, .015f);
                    labelRect.anchorMax = new Vector2(.96f, .255f);
                    labelRect.offsetMin = Vector2.zero;
                    labelRect.offsetMax = Vector2.zero;
                }
                int selectedIndex = database != null ? database.GetIndex(item.Category, item.ItemID) : -1;
                bool isSelected = selectedIndex == look.GetIndex(item.Category) && item.Category == activeCategory;
                GameObject preview = CreateImage(card.transform, "64x64 Pixel Item Icon", new Vector2(84f, 84f), new Vector2(0f, 18f), item.DefaultColor, false);
                UnityEngine.UI.Image previewImage = preview.GetComponent<UnityEngine.UI.Image>();
                previewImage.sprite = item.PreviewIconSprite;
                if (previewImage.sprite != null && previewImage.sprite.texture != null)
                    previewImage.sprite.texture.filterMode = FilterMode.Point;
                previewImage.preserveAspect = true;
                bool isNoneOption = item.ItemID != null && item.ItemID.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0;
                // Show each option's own distinct default color on the card so options don't look identical;
                // if the option is currently selected (and not Skin, where each card represents a preset skin tone), reflect active tint.
                Color cardColor = isNoneOption ? Color.white :
                    (isSelected && item.Category != AvatarItemCategory.Skin) ? PlayerAvatarLayerManager.ResolveTint(item, look) : item.DefaultColor;
                previewImage.color = cardColor;
                PixelUiTheme.SetButtonFace(card, isSelected ? PixelUiTheme.Gold : PixelUiTheme.Almond, isSelected);
            }
            ReflowItems();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(gridContent);
            if (itemScroll != null) { itemScroll.StopMovement(); itemScroll.verticalNormalizedPosition = 1f; }
        }

        void ReflowItems()
        {
            if (itemScroll == null || itemGrid == null || itemScroll.viewport == null) return;
            float width = itemScroll.viewport.rect.width;
            if (width < 1f || Mathf.Abs(width - lastGridWidth) < 1f) return;
            lastGridWidth = width;
            int columns = Mathf.Max(2, Mathf.FloorToInt((width - 32f + 12f) / 144f));
            itemGrid.constraintCount = columns;
            float cellWidth = (width - 32f - 12f * (columns - 1)) / columns;
            itemGrid.cellSize = new Vector2(cellWidth, 140f);
            UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(gridContent);
            if (previewLayers.Length > 0 && previewLayers[0] != null)
            {
                RectTransform area = previewLayers[0].transform.parent as RectTransform;
                float height = Mathf.Min(area.rect.height * .88f, area.rect.width * 2f * .8f);
                foreach (var layer in previewLayers) layer.rectTransform.sizeDelta = new Vector2(height * .5f, height);
            }
        }

        void ChooseItem(AvatarItemSO item)
        {
            if (item == null || database == null) return;
            int index = database.GetIndex(item.Category, item.ItemID);
            if (index < 0) return;
            activeCategory = item.Category;
            look.SetIndex(item.Category, index);
            if (item.SupportColorTint) look.SetTint(item.Category, item.DefaultColor);
            RefreshPreview();
            UpdatePaletteVisibility();
            for (int i = 0; i < itemButtons.Count; i++)
                PixelUiTheme.SetButtonFace(itemButtons[i], itemIndices[i] == index ? PixelUiTheme.Gold : PixelUiTheme.Almond, itemIndices[i] == index);
            if (statusLabel != null) statusLabel.text = item.DisplayName + " selected. Save to keep this look.";
        }

        void SelectPaletteColor(int index)
        {
            Color32[] palette = PaletteFor(activeCategory);
            if (index < 0 || index >= palette.Length) return;
            AvatarItemSO item = database != null ? database.GetItem(activeCategory, look.GetIndex(activeCategory)) : null;
            if (item == null || !item.SupportColorTint) return;
            look.SetTint(activeCategory, palette[index]);
            UpdatePaletteVisibility();
            RefreshPreview();
            if (statusLabel != null) statusLabel.text = "Color updated. Save & Equip to apply.";
        }

        void UpdatePaletteVisibility()
        {
            if (subcategoryRoot == null) return;
            bool tintable = database != null && database.GetItem(activeCategory, look.GetIndex(activeCategory)) is AvatarItemSO item && item.SupportColorTint;
            Transform palette = subcategoryRoot.parent.Find("Color Swatches");
            if (palette != null) palette.gameObject.SetActive(tintable);
            Transform hue = subcategoryRoot.parent.Find("Hue Picker Slider");
            if (hue != null) hue.gameObject.SetActive(tintable);
            Transform paletteLabel = subcategoryRoot.parent.Find("COLOR PALETTE");
            if (paletteLabel != null) paletteLabel.gameObject.SetActive(tintable);
            if (tintable)
            {
                UpdatePaletteColors();
                UnityEngine.UI.Slider slider = hueSlider;
                if (slider != null)
                {
                    Color tint = look.GetTint(activeCategory);
                    Color.RGBToHSV(tint, out float hueValue, out float saturation, out float value);
                    slider.SetValueWithoutNotify(hueValue);
                    UnityEngine.UI.Image hueFill = slider.transform.Find("Hue Fill")?.GetComponent<UnityEngine.UI.Image>();
                    if (hueFill != null) hueFill.color = Color.HSVToRGB(hueValue, Mathf.Max(.7f, saturation), Mathf.Max(.8f, value));
                }
            }
        }

        void ApplyHue(float hue)
        {
            AvatarItemSO item = database != null ? database.GetItem(activeCategory, look.GetIndex(activeCategory)) : null;
            if (item == null || !item.SupportColorTint) return;
            Color current = look.GetTint(activeCategory);
            Color.RGBToHSV(current, out _, out float saturation, out float value);
            Color tint = Color.HSVToRGB(hue, Mathf.Max(.72f, saturation), Mathf.Max(.82f, value));
            look.SetTint(activeCategory, tint);
            if (hueSlider != null)
            {
                UnityEngine.UI.Image hueFill = hueSlider.transform.Find("Hue Fill")?.GetComponent<UnityEngine.UI.Image>();
                if (hueFill != null) hueFill.color = Color.HSVToRGB(hue, .82f, 1f);
            }
            RefreshPreview();
            if (statusLabel != null) statusLabel.text = "Color updated. Save & Equip to apply.";
        }

        void UpdatePaletteColors()
        {
            Transform palette = subcategoryRoot.parent.Find("Color Swatches");
            if (palette == null) return;
            Color32[] colors = PaletteFor(activeCategory);
            for (int i = 0; i < palette.childCount && i < colors.Length; i++)
                palette.GetChild(i).GetComponent<UnityEngine.UI.Image>().color = colors[i];
        }

        static Color32[] PaletteFor(AvatarItemCategory category)
        {
            if (category == AvatarItemCategory.Skin) return SkinPalette;
            if (category == AvatarItemCategory.Hair || category == AvatarItemCategory.Hat) return HairPalette;
            return ClothingPalette;
        }

        void RandomizeAvatar()
        {
            if (database == null) return;
            for (int i = 0; i < AvatarLookState.CategoryCount; i++)
            {
                AvatarItemCategory category = (AvatarItemCategory)i;
                IReadOnlyList<AvatarItemSO> items = database.GetItems(category);
                if (items.Count == 0) continue;
                int index = UnityEngine.Random.Range(0, items.Count);
                AvatarItemSO item = items[index];
                look.SetIndex(category, index);
                if (item != null && item.SupportColorTint)
                {
                    Color32[] colors = PaletteFor(category);
                    look.SetTint(category, colors[UnityEngine.Random.Range(0, colors.Length)]);
                }
            }
            SelectTab(activeTab);
            if (statusLabel != null) statusLabel.text = "Random look generated. Save & Equip to apply.";
        }

        void SaveAndEquip()
        {
            if (database == null) return;
            look = database.Sanitize(look);
            if (target == null)
            {
                PlayerAvatarLayerManager.SavePreferences(database, look);
                if (statusLabel != null) statusLabel.text = "Saved locally. Your look will sync when your player joins.";
            }
            else if (target.RequestEquip(look))
            {
                if (statusLabel != null) statusLabel.text = "Saved locally and equipped. Your look is syncing to the session.";
            }
            else if (statusLabel != null) statusLabel.text = "Only your own avatar can be equipped.";
        }

        void RefreshPreview()
        {
            if (previewLayers == null || previewLayers.Length != PlayerAvatarLayerManager.LayerCount) return;
            Transform previewRoot = previewLayers[0] != null ? previewLayers[0].transform.parent : null;
            if (previewRoot != null)
            {
                previewRoot.localRotation = Quaternion.identity;
                previewRoot.localScale = Vector3.one;
            }
            for (int i = 0; i < previewLayers.Length; i++)
            {
                if (previewLayers[i] == null) continue;
                RectTransform layerTransform = previewLayers[i].rectTransform;
                layerTransform.localRotation = Quaternion.identity;
                layerTransform.localScale = Vector3.one;
                layerTransform.anchoredPosition = Vector2.zero;
                previewLayers[i].preserveAspect = false;
            }
            previewLayers[0].sprite = PixelArtAssetGenerator.GetShadowSprite();
            previewLayers[0].color = Color.white;
            for (int i = 1; i < previewLayers.Length; i++)
            {
                AvatarItemCategory category = LayerCategory(i);
                AvatarItemSO item = database != null ? database.GetItem(category, look.GetIndex(category)) : null;
                UnityEngine.UI.Image image = previewLayers[i];
                if (item == null)
                {
                    image.sprite = PixelArtAssetGenerator.GetFallbackSprite(category, "Default", Color.white);
                    image.color = category == AvatarItemCategory.Skin ? look.SkinTint : Color.white;
                    image.enabled = category == AvatarItemCategory.Skin || category == AvatarItemCategory.Face;
                }
                else
                {
                    image.sprite = item.Sprite != null ? item.Sprite : PixelArtAssetGenerator.GetFallbackSprite(category, item.ItemID, item.DefaultColor);
                    if (image.sprite != null && image.sprite.texture != null) image.sprite.texture.filterMode = FilterMode.Point;
                    image.color = PlayerAvatarLayerManager.ResolveTint(item, look);
                    image.enabled = true;
                }
            }
        }

        static Color HexColor(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color result);
            return result;
        }

        static AvatarItemCategory LayerCategory(int layer)
        {
            switch (layer)
            {
                case 1: return AvatarItemCategory.Skin;
                case 2: return AvatarItemCategory.Tattoo;
                case 3: return AvatarItemCategory.Face;
                case 4: return AvatarItemCategory.Bottom;
                case 5: return AvatarItemCategory.Shoes;
                case 6: return AvatarItemCategory.Top;
                case 7: return AvatarItemCategory.Hair;
                case 8: return AvatarItemCategory.Hat;
                default: return AvatarItemCategory.Accessory;
            }
        }

        UnityEngine.UI.Button AddButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Color color, Action callback)
        {
            GameObject buttonObject = CreateAnchoredImage(parent, name, anchorMin, anchorMax, color, true);
            UnityEngine.UI.Button button = buttonObject.AddComponent<UnityEngine.UI.Button>();
            PixelUiTheme.StyleButton(button, color);
            CreateAnchoredText(buttonObject.transform, label, Vector2.zero, Vector2.one, 17, PixelUiTheme.Ink, FontStyle.Bold, TextAnchor.MiddleCenter).raycastTarget = false;
            button.onClick.AddListener(() => callback?.Invoke());
            return button;
        }

        GameObject CreateAnchoredImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color, bool raycast)
        {
            GameObject result = CreateImage(parent, name, Vector2.zero, Vector2.zero, color, raycast);
            RectTransform rect = result.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
            return result;
        }

        UnityEngine.UI.Text CreateAnchoredText(Transform parent, string content, Vector2 anchorMin, Vector2 anchorMax,
            int size, Color color, FontStyle style, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(content, typeof(RectTransform), typeof(UnityEngine.UI.Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            UnityEngine.UI.Text text = textObject.GetComponent<UnityEngine.UI.Text>();
            text.text = content;
            text.font = PixelUiTheme.BodyFont;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = PixelUiTheme.ReadableTextColor(parent, color);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        GameObject CreateImage(Transform parent, string name, Vector2 size, Vector2 position, Color color, bool raycast)
        {
            GameObject result = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            result.transform.SetParent(parent, false);
            RectTransform rect = result.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            UnityEngine.UI.Image image = result.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return result;
        }

        static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject result = new GameObject(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            RectTransform rect = result.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
        }

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}
