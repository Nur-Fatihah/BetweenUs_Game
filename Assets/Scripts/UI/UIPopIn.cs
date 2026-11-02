using UnityEngine;

namespace Crony
{
    /// <summary>
    /// Windows never flash on: a 0.12 s spring from 90% through 102% to 100% scale with a matching alpha fade.
    /// Runs every time the window is enabled. Reduce Motion keeps the fade and skips the scale.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIPopIn : MonoBehaviour
    {
        const float Duration = .12f, Overshoot = .7f;
        CanvasGroup group;
        Vector3 restScale;
        float time = Duration, restAlpha = 1f;
        static bool reducedMotion;

        public static void SetReducedMotion(bool value) => reducedMotion = value;

        void OnEnable()
        {
            if (!TryGetComponent(out group)) group = gameObject.AddComponent<CanvasGroup>();
            restScale = transform.localScale;
            restAlpha = group.alpha > 0f ? group.alpha : 1f;
            time = 0f;
            Apply(0f);
        }

        void OnDisable()
        {
            if (time < Duration) { transform.localScale = restScale; if (group != null) group.alpha = restAlpha; }
            time = Duration;
        }

        void Update()
        {
            if (time >= Duration) return;
            time += Time.unscaledDeltaTime;
            Apply(Mathf.Clamp01(time / Duration));
        }

        void Apply(float t)
        {
            group.alpha = Mathf.Lerp(0f, restAlpha, Mathf.Clamp01(t * 1.6f));
            if (reducedMotion) { transform.localScale = restScale; return; }
            float scale = t < Overshoot
                ? Mathf.Lerp(.9f, 1.02f, 1f - (1f - t / Overshoot) * (1f - t / Overshoot))
                : Mathf.Lerp(1.02f, 1f, (t - Overshoot) / (1f - Overshoot));
            transform.localScale = restScale * scale;
        }
    }
}
