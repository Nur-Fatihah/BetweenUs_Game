using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>
    /// Button states from the UI kit spec. Default: at rest. Hover: 5% scale-up, a warm golden halo outside and a
    /// soft brightness lift inside. Pressed: the whole button drops 2 units (1-2 screen pixels) with a 35%
    /// desaturation and a small expanding ring. Click: the owning window gets a tiny jolt. Reduce Motion keeps the
    /// colour cues and drops the movement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIButtonJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, ISelectHandler, IDeselectHandler
    {
        const float PressDrop = 2f, HoverScale = 1.05f, PressDesaturate = .35f, GlowAlpha = .2f, HaloAlpha = .6f, RingTime = .28f;
        [SerializeField, Min(0.01f)] float transitionSpeed = 18f;
        RectTransform rectTransform;
        Button button;
        Image surface, glow, halo, ring;
        float ringAge = RingTime;
        Color restFace;
        Vector3 baseScale = Vector3.one;
        float scaleFactor = 1f, drop;
        bool pointerInside, pointerDown, selected, initialized, facePressed;
        readonly List<RectTransform> content = new List<RectTransform>();
        readonly List<Vector2> contentRest = new List<Vector2>();
        static bool reducedMotion;

        public static void SetReducedMotion(bool value) { reducedMotion = value; UIPopIn.SetReducedMotion(value); }

        void Awake()
        {
            rectTransform = transform as RectTransform;
            button = GetComponent<Button>();
            InitializeBase();
        }

        void OnEnable()
        {
            if (rectTransform == null) rectTransform = transform as RectTransform;
            if (button == null) button = GetComponent<Button>();
            InitializeBase();
        }

        void OnDisable()
        {
            pointerInside = pointerDown = selected = false;
            RestoreFace();
            drop = 0f;
            ApplyDrop();
        }

        void InitializeBase()
        {
            if (initialized || rectTransform == null) return;
            baseScale = rectTransform.localScale;
            initialized = true;
        }

        public void SetBaseScale(float multiplier) => scaleFactor = Mathf.Max(.5f, multiplier);
        public void SyncRestPosition() { content.Clear(); contentRest.Clear(); }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Interactable()) return;
            pointerInside = true;
            selected = false;
            GameAudioManager.PlayUiHover();
        }

        public void OnPointerExit(PointerEventData eventData) { pointerInside = false; pointerDown = false; RestoreFace(); }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactable()) return;
            pointerDown = true;
            PressFace();
            StartRing();
        }

        public void OnPointerUp(PointerEventData eventData) { pointerDown = false; RestoreFace(); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactable()) return;
            GameAudioManager.PlayUiClick();
            if (!reducedMotion) UIShake.Punch(transform);
        }

        public void OnSelect(BaseEventData eventData) => selected = true;
        public void OnDeselect(BaseEventData eventData) => selected = false;

        void CacheContent()
        {
            if (content.Count > 0 || rectTransform == null) return;
            if (surface == null) surface = transform.Find("Pixel Button Surface")?.GetComponent<Image>();
            if (halo == null) halo = transform.Find("Button Halo")?.GetComponent<Image>();
            for (int i = 0; i < rectTransform.childCount; i++)
            {
                RectTransform child = rectTransform.GetChild(i) as RectTransform;
                if (child == null || child == glow?.rectTransform || child == ring?.rectTransform) continue;
                content.Add(child);
                contentRest.Add(child.anchoredPosition);
            }
        }

        void EnsureGlow()
        {
            if (glow != null || surface == null) return;
            GameObject go = new GameObject("Inner Glow", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(surface.transform, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(3f, 3f);
            rect.offsetMax = new Vector2(-3f, -3f);
            glow = go.GetComponent<Image>();
            glow.sprite = PixelUiTheme.GetRoundSprite();
            glow.type = Image.Type.Sliced;
            glow.raycastTarget = false;
            glow.color = new Color(1f, 1f, .92f, 0f);
        }

        void StartRing()
        {
            if (reducedMotion || surface == null) return;
            if (ring == null)
            {
                GameObject go = new GameObject("Press Ring", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                RectTransform rect = go.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                ring = go.GetComponent<Image>();
                ring.sprite = PixelUiTheme.GetRingSprite();
                ring.raycastTarget = false;
                go.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            float side = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height);
            ring.rectTransform.sizeDelta = new Vector2(side, side);
            ringAge = 0f;
        }

        void PressFace()
        {
            CacheContent();
            if (surface == null || facePressed) return;
            restFace = surface.color;
            float grey = restFace.r * .299f + restFace.g * .587f + restFace.b * .114f;
            surface.color = Color.Lerp(restFace, new Color(grey, grey, grey, restFace.a), PressDesaturate);
            facePressed = true;
        }

        void RestoreFace()
        {
            if (!facePressed || surface == null) return;
            surface.color = restFace;
            facePressed = false;
        }

        void ApplyDrop()
        {
            for (int i = 0; i < content.Count; i++)
                if (content[i] != null) content[i].anchoredPosition = contentRest[i] + new Vector2(0f, -drop);
        }

        void Update()
        {
            if (rectTransform == null) return;
            CacheContent();
            EnsureGlow();
            bool hover = (pointerInside || selected) && Interactable();
            float t = 1f - Mathf.Exp(-transitionSpeed * Time.unscaledDeltaTime);
            float desiredScale = (reducedMotion ? 1f : hover && !pointerDown ? HoverScale : 1f) * scaleFactor;
            rectTransform.localScale = Vector3.Lerp(rectTransform.localScale, baseScale * desiredScale, t);
            float desiredDrop = pointerDown && !reducedMotion ? PressDrop : 0f;
            if (!Mathf.Approximately(drop, desiredDrop)) { drop = Mathf.MoveTowards(drop, desiredDrop, Time.unscaledDeltaTime * 60f); ApplyDrop(); }
            if (glow != null)
            {
                Color c = glow.color;
                c.a = Mathf.Lerp(c.a, hover ? GlowAlpha : 0f, t);
                glow.color = c;
            }
            if (halo != null)
            {
                Color c = halo.color;
                c.a = Mathf.Lerp(c.a, hover && !pointerDown ? HaloAlpha : 0f, t);
                halo.color = c;
            }
            if (ring != null && ringAge < RingTime)
            {
                ringAge += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(ringAge / RingTime);
                ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(.8f, 1.6f, 1f - (1f - k) * (1f - k));
                ring.color = new Color(1f, .93f, .72f, .5f * (1f - k));
            }
        }

        bool Interactable() => button == null || button.IsInteractable();
    }

    /// <summary>Menu "juice": a short, decaying jolt of the window that owns the pressed control.</summary>
    public sealed class UIShake : MonoBehaviour
    {
        const float Duration = .12f, Amplitude = 2f;
        RectTransform target;
        Vector2 rest;
        float time;

        public static void Punch(Transform control)
        {
            RectTransform window = null;
            for (Transform t = control.parent; t != null; t = t.parent)
                if (PixelUiTheme.HasWarmSurface(t) && t.GetComponent<Canvas>() == null) window = t as RectTransform;
                else if (window != null) break;
            if (window == null) return;
            if (!window.TryGetComponent(out UIShake shake)) shake = window.gameObject.AddComponent<UIShake>();
            if (shake.time <= 0f) { shake.target = window; shake.rest = window.anchoredPosition; }
            shake.time = Duration;
        }

        void Update()
        {
            if (time <= 0f || target == null) return;
            time -= Time.unscaledDeltaTime;
            float strength = Mathf.Max(0f, time / Duration) * Amplitude;
            target.anchoredPosition = time > 0f ? rest + new Vector2(Mathf.Sin(time * 120f) * strength, Mathf.Cos(time * 95f) * strength * .5f) : rest;
        }
    }
}
