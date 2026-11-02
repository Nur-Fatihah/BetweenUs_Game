using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>
    /// Readable checkpoint names for both offline and online play. Each label is screen-space UI drawn at native
    /// resolution (crisp, above the vision fog and god rays): a parchment pill with an inked border, a number badge
    /// and the task name in Quicksand Bold. Labels appear when the local player is nearby and hide while a meeting,
    /// window or mini-game is open.
    /// </summary>
    public sealed class CheckpointLabels : MonoBehaviour
    {
        const float ShowDistance = 7f;
        sealed class Label { public TownshipTaskStation Station; public RectTransform Rect; public Image Plate, Badge; public Text Name; public CanvasGroup Group; }
        static CheckpointLabels instance;
        static int offlineFrame = -1;
        static Vector2 offlineViewer;
        static readonly HashSet<TownshipTaskKind> offlineDone = new HashSet<TownshipTaskKind>();
        static Font boldFont;
        readonly List<Label> labels = new List<Label>();
        RectTransform canvasRect;
        Camera view;

        public static Font BoldFont
        {
            get
            {
                if (boldFont == null) boldFont = Resources.Load<Font>("Fonts/Quicksand-Bold");
                return boldFont != null ? boldFont : PixelUiTheme.BodyFont;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (instance != null || FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Include).Length == 0) return;
            GameObject go = new GameObject("Checkpoint Labels", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the world fog (game canvas, 20) but under windows, meetings and mini-games, which hide labels anyway.
            canvas.sortingOrder = 21;
            PixelUiTheme.ConfigureCanvas(go.GetComponent<CanvasScaler>());
            instance = go.AddComponent<CheckpointLabels>();
        }

        /// <summary>Offline runtime: whether labels may show this frame, and where the player stands (world units).</summary>
        public static void SetOffline(bool active, Vector2 viewerWorld) { offlineFrame = active ? Time.frameCount : -1; offlineViewer = viewerWorld; }
        public static void SetOfflineComplete(TownshipTaskKind kind, bool done) { if (done) offlineDone.Add(kind); else offlineDone.Remove(kind); }
        public static void ClearOfflineProgress() => offlineDone.Clear();

        void Start()
        {
            canvasRect = (RectTransform)transform;
            List<TownshipTaskStation> stations = new List<TownshipTaskStation>(FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Include));
            stations.Sort((a, b) => a.Kind.CompareTo(b.Kind));
            foreach (TownshipTaskStation station in stations) labels.Add(Build(station));
        }

        void OnDestroy() { if (instance == this) instance = null; }

        Label Build(TownshipTaskStation station)
        {
            Label label = new Label { Station = station };
            GameObject root = new GameObject("Checkpoint " + station.TaskName, typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            label.Rect = (RectTransform)root.transform;
            label.Rect.sizeDelta = new Vector2(320f, 44f);
            label.Group = root.GetComponent<CanvasGroup>();
            label.Group.blocksRaycasts = false;
            label.Group.interactable = false;

            Image ink = Part(root.transform, "Ink Border", PixelUiTheme.Chocolate, Vector2.zero, Vector2.zero);
            label.Plate = Part(root.transform, "Parchment", new Color(.98f, .95f, .87f, .97f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            label.Badge = Part(label.Plate.transform, "Number Badge", PixelUiTheme.Amber, new Vector2(4f, 4f), Vector2.zero);
            RectTransform badge = label.Badge.rectTransform;
            badge.anchorMax = new Vector2(0f, 1f);
            badge.offsetMax = new Vector2(40f, -4f);
            Text number = Text(label.Badge.transform, ((int)station.Kind + 1).ToString(), 21, Color.white);
            number.color = PixelUiTheme.Ink;
            label.Name = Text(label.Plate.transform, station.TaskName, 21, PixelUiTheme.Ink);
            RectTransform nameRect = label.Name.rectTransform;
            nameRect.offsetMin = new Vector2(48f, 0f);
            nameRect.offsetMax = new Vector2(-10f, 0f);
            label.Name.alignment = TextAnchor.MiddleLeft;
            // Size the pill to the name so short and long titles both sit snugly.
            label.Rect.sizeDelta = new Vector2(Mathf.Clamp(label.Name.preferredWidth + 66f, 170f, 500f), 48f);
            ink.raycastTarget = label.Plate.raycastTarget = label.Badge.raycastTarget = false;
            root.SetActive(false);
            return label;
        }

        static Image Part(Transform parent, string name, Color color, Vector2 offsetMin, Vector2 offsetMax)
        {
            Image image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            image.sprite = PixelUiTheme.GetRoundSprite();
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        static Text Text(Transform parent, string value, int size, Color color)
        {
            Text text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            text.font = BoldFont;
            text.fontSize = size;
            text.color = color;
            text.text = value;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        void LateUpdate()
        {
            if (view == null) view = Camera.main;
            bool online = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            bool active;
            Vector2 viewer;
            if (online)
            {
                GameManager game = GameManager.Instance;
                NetworkObject me = NetworkManager.Singleton.LocalClient != null ? NetworkManager.Singleton.LocalClient.PlayerObject : null;
                active = game != null && game.IsSpawned && game.Phase.Value == RoundPhase.Playing && me != null &&
                         !NetworkManagerUI.ModalOpen && !TownshipTaskMiniGameUI.IsOpen;
                viewer = me != null ? (Vector2)me.transform.position : Vector2.zero;
            }
            // The offline runtime reports every frame; a missed report (round ended, runtime gone) hides the labels.
            else { active = offlineFrame >= Time.frameCount - 1 && !TownshipTaskMiniGameUI.IsOpen; viewer = offlineViewer; }

            foreach (Label label in labels)
            {
                if (label.Station == null) continue;
                Vector2 spot = label.Station.transform.position;
                bool show = active && view != null && (spot - viewer).sqrMagnitude < ShowDistance * ShowDistance;
                if (label.Rect.gameObject.activeSelf != show) label.Rect.gameObject.SetActive(show);
                if (!show) continue;
                // Viewport coordinates are resolution-independent, so Retina backing scales and pixel-perfect
                // cameras can't push labels off target.
                Vector3 viewport = view.WorldToViewportPoint(new Vector3(spot.x, spot.y + 1.3f, 0f));
                if (viewport.z < 0f) { label.Rect.gameObject.SetActive(false); continue; }
                Vector2 local = new Vector2((viewport.x - .5f) * canvasRect.rect.width, (viewport.y - .5f) * canvasRect.rect.height);
                // Stay fully on screen and below the top bar and toast banner (a checkpoint near the view's edge would otherwise push its name off it), and
                // snap to whole canvas units so the text never shimmers as the camera glides.
                Vector2 half = canvasRect.rect.size * .5f - label.Rect.sizeDelta * .5f - new Vector2(12f, 12f);
                local = new Vector2(Mathf.Clamp(local.x, -half.x, half.x), Mathf.Clamp(local.y, -half.y, half.y - 200f));
                label.Rect.anchoredPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
                bool done = online ? label.Station.IsSpawned && label.Station.Completed.Value : offlineDone.Contains(label.Station.Kind);
                label.Badge.color = done ? PixelUiTheme.Mint : PixelUiTheme.Amber;
                string title = done ? label.Station.TaskName + "  · done" : label.Station.TaskName;
                if (label.Name.text != title)
                {
                    label.Name.text = title;
                    label.Rect.sizeDelta = new Vector2(Mathf.Clamp(label.Name.preferredWidth + 66f, 170f, 520f), 48f);
                }
                // Fade a little with distance so the nearest checkpoint reads first.
                label.Group.alpha = Mathf.Lerp(1f, .7f, Mathf.InverseLerp(3f, ShowDistance, Vector2.Distance(spot, viewer)));
            }
        }
    }
}
