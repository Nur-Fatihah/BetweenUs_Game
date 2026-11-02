using System.Collections.Generic;
using UnityEngine;

namespace Crony
{
    /// <summary>Fades a roof or tree canopy while a player is standing underneath it.</summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class CanopyFadeZone2D : MonoBehaviour
    {
        [SerializeField] SpriteRenderer[] canopyRenderers;
        [SerializeField, Range(0f, 1f)] float coveredAlpha = .32f;
        [SerializeField, Min(.01f)] float fadeSpeed = 5f;

        readonly HashSet<Collider2D> occupants = new HashSet<Collider2D>();
        Color[] baseColors;
        Collider2D trigger;

        void Awake()
        {
            trigger = GetComponent<Collider2D>();
            trigger.isTrigger = true;
            if (canopyRenderers == null || canopyRenderers.Length == 0)
                canopyRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            baseColors = new Color[canopyRenderers.Length];
            for (int i = 0; i < canopyRenderers.Length; i++)
                baseColors[i] = canopyRenderers[i] != null ? canopyRenderers[i].color : Color.white;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<PlayerController>() != null) occupants.Add(other);
        }

        void OnTriggerExit2D(Collider2D other) => occupants.Remove(other);

        void Update()
        {
            float alphaScale = occupants.Count > 0 ? coveredAlpha : 1f;
            for (int i = 0; i < canopyRenderers.Length; i++)
            {
                SpriteRenderer renderer = canopyRenderers[i];
                if (renderer == null) continue;
                Color targetColor = baseColors[i];
                targetColor.a *= alphaScale;
                renderer.color = Color.Lerp(renderer.color, targetColor, Mathf.Clamp01(fadeSpeed * Time.deltaTime));
            }
        }

        void OnDisable()
        {
            occupants.Clear();
            if (baseColors == null) return;
            for (int i = 0; i < canopyRenderers.Length; i++)
                if (canopyRenderers[i] != null) canopyRenderers[i].color = baseColors[i];
        }
    }
}
