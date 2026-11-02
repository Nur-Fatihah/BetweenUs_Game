using UnityEngine;

namespace Crony
{
    /// <summary>
    /// Gentle procedural motion for the layered avatar: a sub-pixel walk bob and an idle breathing lift. Every body
    /// layer moves by the same offset so hair, hats and faces always stay attached to the body, and there are no
    /// springs or per-layer scales, so the motion is stable at any frame rate. Works offline and online because it
    /// only reads how far the root moved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AvatarSecondaryMotion : MonoBehaviour
    {
        const int ShadowLayer = 0;
        const float ArtPixel = 1f / 32f, MaxDelta = .1f;
        PlayerAvatarLayerManager avatar;
        Vector3 lastPosition;
        float speed, walkPhase, seed;

        void Awake()
        {
            avatar = GetComponent<PlayerAvatarLayerManager>();
            lastPosition = transform.position;
            seed = Random.value * 6.28f;
        }

        void LateUpdate()
        {
            if (avatar == null || avatar.LayerRenderers == null) return;
            // Clamping the step keeps a frame hitch from turning into a jump.
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, MaxDelta);
            Vector3 position = transform.position;
            float measured = ((Vector2)(position - lastPosition)).magnitude / dt;
            lastPosition = position;
            // Teleports (meeting regroup, spawns, stumps) are not walking.
            if (measured > 30f || float.IsNaN(measured)) measured = 0f;
            speed = Mathf.Lerp(speed, measured, 1f - Mathf.Exp(-12f * dt));

            bool walking = speed > .2f;
            walkPhase = walking
                ? walkPhase + dt * Mathf.Lerp(8f, 13f, Mathf.Clamp01(speed / 5f))
                : Mathf.MoveTowards(walkPhase, Mathf.Round(walkPhase / Mathf.PI) * Mathf.PI, dt * 6f);
            walkPhase = Mathf.Repeat(walkPhase, Mathf.PI * 64f);
            float bob = Mathf.Abs(Mathf.Sin(walkPhase)) * 1.2f * ArtPixel;
            float breath = walking ? 0f : (Mathf.Sin(Time.time * 2.4f + seed) * .5f + .5f) * .5f * ArtPixel;
            Vector3 lift = new Vector3(0f, bob + breath, 0f);

            SpriteRenderer[] layers = avatar.LayerRenderers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null) continue;
                Transform layer = layers[i].transform;
                if (i == ShadowLayer)
                {
                    // Contact shadow in soft indigo; it stays on the ground and tightens slightly as the body lifts.
                    layers[i].color = new Color(.2f, .16f, .36f, .5f);
                    layer.localPosition = Vector3.zero;
                    layer.localScale = Vector3.one * (1f - (bob + breath) * 3f);
                    continue;
                }
                layer.localPosition = lift;
                layer.localScale = Vector3.one;
            }
        }
    }
}
