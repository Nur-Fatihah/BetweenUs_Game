using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>Runtime uGUI settings modal shared by the menu and in-game safety panel.</summary>
    public sealed class SettingsUI : MonoBehaviour
    {
        public enum Page { Audio, Display, Accessibility, Controls, Safety }
        static readonly string[] PageNames = { "AUDIO", "DISPLAY", "ACCESSIBILITY", "CONTROLS", "SAFETY" };

        Transform root;
        Transform pageContent;
        Font font;
        Action closeAction;
        Action<bool, bool, bool> accessibilityChanged;
        bool largeText;
        bool highContrast;
        bool reduceEffects;
        Page page;
        Button[] tabs;
        Text voiceStatus;

        public static SettingsUI Show(Transform parent, Action close, bool useLargeText, bool useHighContrast,
            bool useReducedEffects, Action<bool, bool, bool> onAccessibilityChanged = null, Page startPage = Page.Audio)
        {
            if (parent == null) return null;
            GameObject rootObject = new GameObject("Cozy Settings Modal", typeof(RectTransform));
            rootObject.transform.SetParent(parent, false);
            Stretch(rootObject.GetComponent<RectTransform>());
            SettingsUI view = rootObject.AddComponent<SettingsUI>();
            view.Initialize(useLargeText, useHighContrast, useReducedEffects, close, onAccessibilityChanged);
            if (startPage != Page.Audio) view.SelectPage(startPage);
            return view;
        }

        void Initialize(bool useLargeText, bool useHighContrast, bool useReducedEffects, Action close,
            Action<bool, bool, bool> onAccessibilityChanged)
        {
            root = transform;
            font = PixelUiTheme.BodyFont;
            closeAction = close;
            accessibilityChanged = onAccessibilityChanged;
            largeText = useLargeText;
            highContrast = useHighContrast;
            reduceEffects = useReducedEffects;
            UIButtonJuice.SetReducedMotion(reduceEffects);

            GameObject shade = Panel(root, "Modal Shade", new Color(.20f, .13f, .08f, .56f), true);
            Stretch(shade.GetComponent<RectTransform>());
            GameObject window = Panel(root, "Settings Window", PixelUiTheme.Cream, false);
            RectTransform windowRect = window.GetComponent<RectTransform>();
            windowRect.anchorMin = windowRect.anchorMax = new Vector2(.5f, .5f);
            windowRect.pivot = new Vector2(.5f, .5f);
            windowRect.sizeDelta = new Vector2(1000f, 840f);
            PixelUiTheme.ApplyWindow(window, true);

            Label(window.transform, "SETTINGS & SAFETY", new Vector2(.035f, .915f), new Vector2(.74f, .985f), 34, FontStyle.Bold, TextAnchor.MiddleLeft);
            Label(window.transform, "Changes save automatically.", new Vector2(.035f, .87f), new Vector2(.70f, .915f), 16, FontStyle.Normal, TextAnchor.MiddleLeft);
            MakeButton(window.transform, "Close settings", "X", new Vector2(.91f, .9f), new Vector2(.97f, .975f), PixelUiTheme.Coral, () => closeAction?.Invoke(), 22);

            tabs = new Button[PageNames.Length];
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                float minX = .035f + i * .188f;
                tabs[i] = MakeButton(window.transform, "Settings Tab " + PageNames[i], PageNames[i], new Vector2(minX, .795f),
                    new Vector2(minX + .178f, .855f), PixelUiTheme.Tan, () => SelectPage((Page)index), 15);
            }

            GameObject card = Panel(window.transform, "Settings Content Card", PixelUiTheme.Almond, false);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(.035f, .11f);
            cardRect.anchorMax = new Vector2(.965f, .78f);
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;
            PixelUiTheme.ApplyCard(card, false);
            pageContent = new GameObject("Page Content", typeof(RectTransform)).transform;
            pageContent.SetParent(card.transform, false);
            Stretch((RectTransform)pageContent);

            MakeButton(window.transform, "Reset Settings", "RESET TO DEFAULTS", new Vector2(.035f, .025f), new Vector2(.30f, .09f), PixelUiTheme.Linen, ResetToDefaults, 15);
            Label(window.transform, "Blocks, reports and your safety record are kept.", new Vector2(.31f, .025f), new Vector2(.75f, .09f), 13, FontStyle.Normal, TextAnchor.MiddleLeft);
            MakeButton(window.transform, "Done Settings", "DONE", new Vector2(.78f, .025f), new Vector2(.965f, .09f), PixelUiTheme.Amber, () => closeAction?.Invoke(), 17);
            SelectPage(Page.Audio);
        }

        void SelectPage(Page next)
        {
            page = next;
            for (int i = 0; i < tabs.Length; i++) PixelUiTheme.SetTabState(tabs[i], i == (int)page);
            for (int i = pageContent.childCount - 1; i >= 0; i--) Destroy(pageContent.GetChild(i).gameObject);
            voiceStatus = null;
            switch (page)
            {
                case Page.Audio: BuildAudioPage(); break;
                case Page.Display: BuildDisplayPage(); break;
                case Page.Accessibility: BuildAccessibilityPage(); break;
                case Page.Controls: ControlsGuide.Render(pageContent, font, largeText); break;
                case Page.Safety: BuildSafetyPage(); break;
            }
            Typography.Apply(root);
        }

        void ResetToDefaults()
        {
            GameSettings.ResetToDefaults();
            largeText = highContrast = reduceEffects = false;
            UIButtonJuice.SetReducedMotion(false);
            accessibilityChanged?.Invoke(false, false, false);
            SelectPage(page);
        }

        // ---- Audio ----

        void BuildAudioPage()
        {
            VolumeRow("Master volume", .86f, AudioListener.volume, value => { AudioListener.volume = value; PlayerPrefs.SetFloat(GameSettings.MasterVolumeKey, value); });
            VolumeRow("Music", .72f, GameAudioManager.MusicVolume, GameAudioManager.SetMusicVolume);
            VolumeRow("Sound effects", .58f, GameAudioManager.EffectsVolume, value => { GameAudioManager.SetEffectsVolume(value); GameAudioManager.PlayUiClick(); });
            AddToggleRow("Mute when in the background", "Silence the game while you are in another window.", .42f,
                PlayerPrefs.GetInt(GameAudioManager.MuteInBackgroundKey, 1) == 1, value => GameSettings.SetInt(GameAudioManager.MuteInBackgroundKey, value ? 1 : 0), .07f);
            var voice = TownshipVoiceManager.Instance;
            if (voice == null || !TownshipVoiceManager.Supported)
            {
                Label(pageContent, voice == null ? "Voice chat is not available in this scene. Text chat always works." : TownshipVoiceManager.UnsupportedMessage, new Vector2(.08f, .05f), new Vector2(.92f, .3f), 16, FontStyle.Normal, TextAnchor.MiddleLeft);
                return;
            }
            AddToggleRow("Voice chat", "Live audio in online games through Unity Vivox. Never recorded. Turning it on means you agree.", .26f, voice.EnabledByUser, value =>
            {
                if (value) { voice.SetConsent(true); voice.SetEnabled(true); }
                else voice.SetEnabled(false);
            }, .07f);
            AddToggleRow("Toggle to talk", "On: press V to start or stop talking. Off: hold V while you talk.", .11f, voice.ToggleToTalk, value => voice.ToggleToTalk = value, .07f);
            voiceStatus = Label(pageContent, "", new Vector2(.08f, .0f), new Vector2(.92f, .04f), 13, FontStyle.Bold, TextAnchor.MiddleLeft);
        }

        void VolumeRow(string title, float y, float value, Action<float> changed)
        {
            Text percent = Label(pageContent, Mathf.RoundToInt(value * 100f) + "%", new Vector2(.80f, y - .04f), new Vector2(.92f, y + .04f), 16, FontStyle.Bold, TextAnchor.MiddleRight);
            Label(pageContent, title, new Vector2(.08f, y - .04f), new Vector2(.28f, y + .04f), 17, FontStyle.Bold, TextAnchor.MiddleLeft);
            SliderRow(pageContent, title + " Slider", new Vector2(.28f, y), value, v => { percent.text = Mathf.RoundToInt(v * 100f) + "%"; changed(v); });
        }

        void Update()
        {
            if (voiceStatus == null || TownshipVoiceManager.Instance == null) return;
            voiceStatus.text = "Voice status: " + TownshipVoiceManager.Instance.Status;
        }

        void SliderRow(Transform parent, string name, Vector2 anchor, float value, Action<float> changed)
        {
            GameObject rootObject = new GameObject(name, typeof(RectTransform), typeof(Slider));
            rootObject.transform.SetParent(parent, false);
            RectTransform rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = anchor;
            rootRect.pivot = new Vector2(0f, .5f);
            rootRect.sizeDelta = new Vector2(450f, 36f);

            Image rootImage = rootObject.gameObject.AddComponent<Image>();
            rootImage.sprite = PixelUiTheme.GetRoundSprite();
            rootImage.color = PixelUiTheme.Chocolate;
            rootImage.raycastTarget = true;
            GameObject trackObject = GraphicChild(rootObject.transform, "Wood Slider Track", new Vector2(.5f, .5f), new Vector2(420f, 18f), PixelUiTheme.Chocolate);
            trackObject.GetComponent<Image>().sprite = PixelUiTheme.GetRoundSprite();
            GameObject fillObject = GraphicChild(trackObject.transform, "Golden Slider Fill", new Vector2(.5f, .5f), new Vector2(414f, 8f), PixelUiTheme.Amber);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, .5f);
            fillRect.anchorMax = new Vector2(0f, .5f);
            fillRect.pivot = new Vector2(0f, .5f);
            fillRect.sizeDelta = new Vector2(0f, 8f);
            fillRect.anchoredPosition = Vector2.zero;
            GameObject handleObject = GraphicChild(rootObject.transform, "Acorn Slider Handle", new Vector2(.5f, .5f), new Vector2(30f, 30f), PixelUiTheme.Gold);
            Image handle = handleObject.GetComponent<Image>();
            handle.sprite = PixelUiTheme.GetDotSprite();
            handle.raycastTarget = true;

            Slider slider = rootObject.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fillRect;
            slider.handleRect = handleObject.GetComponent<RectTransform>();
            slider.targetGraphic = handle;
            slider.value = Mathf.Clamp01(value);
            slider.onValueChanged.AddListener(v => changed?.Invoke(v));
            slider.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        }

        // ---- Display ----

        void BuildDisplayPage()
        {
            const float rowHalf = .075f;
            AddToggleRow("Fullscreen", "Fill the whole screen. Turn off to play in a window.", .87f, Screen.fullScreen, value =>
            {
                Screen.fullScreen = value;
                GameSettings.SetInt(GameSettings.FullscreenKey, value ? 1 : 0);
            }, rowHalf);
            AddToggleRow("V-Sync", "Match your monitor's refresh rate to stop screen tearing.", .69f, QualitySettings.vSyncCount > 0,
                value => { GameSettings.SetInt(GameSettings.VSyncKey, value ? 1 : 0); SelectPage(Page.Display); }, rowHalf);
            bool vSync = QualitySettings.vSyncCount > 0;
            InfoRow("Frame rate limit", vSync ? "V-Sync is on, so your monitor sets the frame rate. Turn V-Sync off to choose a limit." : "Lower limits save battery and keep laptops cooler.", .51f,
                vSync ? null : GameSettings.FrameCapLabel(GameSettings.FrameCap), CycleFrameCap, rowHalf);
            AddToggleRow("Full visual effects", "Glow, colour grading, god rays and drifting petals. Turn off if the game feels slow.", .33f, GameSettings.FullEffects,
                value => GameSettings.SetInt(GameSettings.FullEffectsKey, value ? 1 : 0), rowHalf);
            InfoRow("Problem log  ·  Crony " + Application.version, "Something went wrong? Open this folder and send crony-errors.log with your bug report. Nothing is uploaded.", .15f,
                "OPEN", () => Application.OpenURL("file://" + ErrorLog.Folder), rowHalf);
        }

        void CycleFrameCap()
        {
            int index = Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap);
            GameSettings.SetInt(GameSettings.FrameCapKey, GameSettings.FrameCaps[(index + 1) % GameSettings.FrameCaps.Length]);
            SelectPage(Page.Display);
        }

        // ---- Accessibility ----

        void BuildAccessibilityPage()
        {
            AddToggleRow("Large text", "Make menu labels, guides and task instructions bigger.", .84f, largeText, value =>
            {
                largeText = value;
                PlayerPrefs.SetInt(GameSettings.LargeTextKey, largeText ? 1 : 0);
                accessibilityChanged?.Invoke(largeText, highContrast, reduceEffects);
                SelectPage(page);
            });
            AddToggleRow("High contrast", "Stronger contrast for important interface details.", .63f, highContrast, value =>
            {
                highContrast = value;
                PlayerPrefs.SetInt(GameSettings.HighContrastKey, highContrast ? 1 : 0);
                accessibilityChanged?.Invoke(largeText, highContrast, reduceEffects);
                SelectPage(page);
            });
            AddToggleRow("Reduce motion", "Less camera shake and fewer bouncing button effects.", .42f, reduceEffects, value =>
            {
                reduceEffects = value;
                PlayerPrefs.SetInt(GameSettings.ReduceEffectsKey, reduceEffects ? 1 : 0);
                UIButtonJuice.SetReducedMotion(reduceEffects);
                accessibilityChanged?.Invoke(largeText, highContrast, reduceEffects);
            });
            InfoRow("Play your way", "Every task game works with the keyboard (Space, 1-5, A/D) and the mouse. Sounds always come with on-screen text.", .21f, null, null);
        }

        // ---- Safety ----

        void BuildSafetyPage()
        {
            InfoRow("Community rules", "What is not allowed, what happens at each level, and how reports and appeals work.", .87f,
                "READ RULES", () => SafetyUI.ShowRules(root, font, () => { Transform rules = root.Find("Community Rules Modal"); if (rules != null) Destroy(rules.gameObject); }), .065f);
            List<OwnIncident> record = CommunicationSafety.OwnIncidents;
            OwnIncident latest = record.Count > 0 ? record[record.Count - 1] : null;
            int pending = CommunicationSafety.PendingReportCount;
            InfoRow("Your safety record", (latest == null ? "No moderation decisions. Thanks for keeping it kind." :
                    record.Count + " decision(s). Latest: Level " + (int)latest.level + ", ref " + latest.reference + (latest.appealed ? " · appeal queued." : ".")) +
                    (pending > 0 ? "  " + pending + " report(s) you filed are waiting for review." : ""), .72f,
                latest != null && !latest.appealed ? "APPEAL" : null, () => AppealLatest(latest), .065f);
            int blocked = CommunicationSafety.BlockedCount;
            InfoRow("Blocked players", blocked == 0 ? "You have not blocked anyone. Click any player or message to block them." : blocked + " blocked. Their messages are hidden and their voice is muted.", .57f,
                blocked > 0 ? "UNBLOCK ALL" : null, () => { CommunicationSafety.ClearBlocks(); SelectPage(Page.Safety); }, .065f);
            AddToggleRow("Text chat", "Off hides all player messages and stops you sending them. Quick safety notices still appear.", .42f, GameSettings.TextChatEnabled,
                value => GameSettings.SetInt(GameSettings.TextChatKey, value ? 1 : 0), .065f);
            AddToggleRow("Streamer mode", "Hides the online room code on screen so viewers can't join. The code is still copied for you.", .27f, GameSettings.StreamerMode,
                value => GameSettings.SetInt(GameSettings.StreamerModeKey, value ? 1 : 0), .065f);
            InfoRow("Need support?", SupportResources.Guidance, .12f,
                "GET HELP", () => Application.OpenURL(SupportResources.HelplineUrl), .065f);
        }

        async void AppealLatest(OwnIncident incident)
        {
            if (incident == null || !CommunicationSafety.MarkAppealed(incident.reference)) { SelectPage(Page.Safety); return; }
            var appeal = new Appeal { reference = incident.reference, createdUtc = DateTime.UtcNow.ToString("O"), note = "Player requested review from Settings > Safety." };
            bool delivered = false;
            try { if (CommunicationSafety.Backend != null) delivered = await CommunicationSafety.Backend.SubmitAppealAsync(appeal); }
            catch (Exception e) { Debug.LogWarning("Appeal service unavailable: " + e.Message); }
            if (!delivered)
            {
                try { CommunicationSafety.SavePendingAppeal(appeal); }
                catch (Exception e) { Debug.LogWarning("Appeal could not be queued: " + e.Message); }
            }
            if (this != null) SelectPage(Page.Safety);
        }

        // ---- Shared row builders ----

        void InfoRow(string title, string detail, float y, string actionLabel, Action action, float halfHeight = .09f)
        {
            GameObject row = Panel(pageContent, title + " Info Row", PixelUiTheme.Cream, false);
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.04f, y - halfHeight);
            rect.anchorMax = new Vector2(.96f, y + halfHeight);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            PixelUiTheme.ApplyCard(row, false);
            Label(row.transform, title, new Vector2(.03f, .52f), new Vector2(.74f, .94f), 17, FontStyle.Bold, TextAnchor.MiddleLeft);
            Label(row.transform, detail, new Vector2(.03f, .06f), new Vector2(actionLabel != null ? .74f : .97f, .56f), 13, FontStyle.Normal, TextAnchor.MiddleLeft);
            if (actionLabel != null) MakeButton(row.transform, title + " Action", actionLabel, new Vector2(.77f, .2f), new Vector2(.97f, .8f), PixelUiTheme.Coral, action, 15);
        }

        void AddToggleRow(string title, string detail, float y, bool value, Action<bool> changed, float halfHeight = .09f)
        {
            GameObject row = Panel(pageContent, title + " Toggle Row", PixelUiTheme.Cream, false);
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.04f, y - halfHeight);
            rect.anchorMax = new Vector2(.96f, y + halfHeight);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            PixelUiTheme.ApplyCard(row, false);
            Label(row.transform, title, new Vector2(.03f, .50f), new Vector2(.74f, .94f), 17, FontStyle.Bold, TextAnchor.MiddleLeft);
            Label(row.transform, detail, new Vector2(.03f, .06f), new Vector2(.76f, .54f), 13, FontStyle.Normal, TextAnchor.MiddleLeft);
            GameObject trackObject = new GameObject("Pixel Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            trackObject.transform.SetParent(row.transform, false);
            RectTransform trackRect = trackObject.GetComponent<RectTransform>();
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(.87f, .5f);
            trackRect.sizeDelta = new Vector2(76f, 36f);
            Button button = trackObject.GetComponent<Button>();
            Image track = trackObject.GetComponent<Image>();
            track.sprite = PixelUiTheme.GetRoundSprite();
            track.raycastTarget = true;
            Image face = PixelUiTheme.StyleButton(button, value ? PixelUiTheme.Mint : PixelUiTheme.Coral);
            face.color = value ? PixelUiTheme.Mint : PixelUiTheme.Coral;
            GameObject knobObject = new GameObject("Toggle Knob", typeof(RectTransform), typeof(Image));
            knobObject.transform.SetParent(trackObject.transform, false);
            RectTransform knobRect = knobObject.GetComponent<RectTransform>();
            knobRect.anchorMin = knobRect.anchorMax = new Vector2(.5f, .5f);
            knobRect.sizeDelta = new Vector2(25f, 25f);
            Image knobImage = knobObject.GetComponent<Image>();
            knobImage.sprite = PixelUiTheme.GetDotSprite();
            knobImage.color = PixelUiTheme.Cream;
            knobImage.raycastTarget = false;
            PixelToggleSwitch toggle = trackObject.AddComponent<PixelToggleSwitch>();
            toggle.Bind(button, face, knobRect, value, changed);
        }

        Button MakeButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Color color, Action action, int size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Button button = go.GetComponent<Button>();
            PixelUiTheme.StyleButton(button, color);
            Label(go.transform, label, Vector2.zero, Vector2.one, size, FontStyle.Bold, TextAnchor.MiddleCenter).color = PixelUiTheme.ButtonTextColor(color);
            button.onClick.AddListener(() => action?.Invoke());
            return button;
        }

        Text Label(Transform parent, string text, Vector2 min, Vector2 max, int size, FontStyle style, TextAnchor alignment)
        {
            GameObject go = new GameObject(text, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Text label = go.GetComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = largeText ? Mathf.RoundToInt(size * 1.12f) : size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = PixelUiTheme.ReadableTextColor(parent, PixelUiTheme.Ink);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        GameObject Panel(Transform parent, string name, Color color, bool raycast)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return go;
        }

        GameObject GraphicChild(Transform parent, string name, Vector2 anchor, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.sprite = PixelUiTheme.GetRoundSprite();
            image.color = color;
            image.raycastTarget = false;
            return go;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
