using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Crony
{
    /// <summary>Emission profiles from the visual spec. Each animates smoothly so lit areas feel alive, never static.</summary>
    public enum GlowProfile { Beacon, Alarm, Lamp, Done }

    /// <summary>Additive 2D point light with a profile-driven pulse or flicker. Bloom picks up its HDR peaks.</summary>
    [DisallowMultipleComponent]
    public sealed class GlowLight : MonoBehaviour
    {
        // Light2D blend style 1 is "Additive" in the default 2D renderer: glow adds on top of the ambient multiply pass.
        const int AdditiveBlendStyle = 1;
        Light2D glow;
        GlowProfile profile;
        float seed;

        public static GlowLight Attach(Transform parent, GlowProfile profile, Vector3 offset = default)
        {
            Transform existing = parent.Find("Glow Light");
            GlowLight light = existing != null ? existing.GetComponent<GlowLight>() : null;
            if (light == null)
            {
                GameObject go = new GameObject("Glow Light");
                go.transform.SetParent(parent, false);
                light = go.AddComponent<GlowLight>();
            }
            light.transform.localPosition = offset;
            light.SetProfile(profile);
            return light;
        }

        /// <summary>Sets the scene's global (ambient) 2D light. 1 = sprites look exactly as painted.</summary>
        public static void SetAmbient(float intensity)
        {
            foreach (Light2D light in FindObjectsByType<Light2D>(FindObjectsInactive.Exclude))
                if (light.lightType == Light2D.LightType.Global) { TargetAllLayers(light); light.intensity = intensity; }
        }

        /// <summary>2D lights only touch the sorting layers they list; avatars live on their own layer and would render black.</summary>
        public static void TargetAllLayers(Light2D light)
        {
            SortingLayer[] layers = SortingLayer.layers;
            int[] ids = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++) ids[i] = layers[i].id;
            light.targetSortingLayers = ids;
        }

        void Awake()
        {
            glow = gameObject.AddComponent<Light2D>();
            glow.lightType = Light2D.LightType.Point;
            glow.blendStyleIndex = AdditiveBlendStyle;
            glow.shadowsEnabled = false;
            glow.falloffIntensity = .65f;
            TargetAllLayers(glow);
            seed = Random.value * 10f;
        }

        public void SetProfile(GlowProfile next)
        {
            profile = next;
            if (glow == null) return;
            switch (profile)
            {
                case GlowProfile.Alarm: glow.color = new Color(1f, .32f, .25f); glow.pointLightOuterRadius = 2.2f; break;
                case GlowProfile.Lamp: glow.color = new Color(1f, .82f, .55f); glow.pointLightOuterRadius = 1.8f; break;
                case GlowProfile.Done: glow.color = new Color(.45f, .95f, .7f); glow.pointLightOuterRadius = .9f; break;
                default: glow.color = new Color(1f, .78f, .42f); glow.pointLightOuterRadius = 1.3f; break;
            }
            glow.pointLightInnerRadius = glow.pointLightOuterRadius * .15f;
        }

        void Update()
        {
            if (glow == null) return;
            float t = Time.time + seed;
            switch (profile)
            {
                // Slow breathing pulse invites players to the station.
                case GlowProfile.Beacon: glow.intensity = .5f + Mathf.Sin(t * 2.6f) * .12f; break;
                // Urgent 3 Hz pulse, peaking above 1 so bloom flares around the sabotage.
                case GlowProfile.Alarm: glow.intensity = .25f + (Mathf.Sin(t * 18.8f) * .5f + .5f) * .95f; break;
                // Lamps flicker with smooth noise, never on/off strobing.
                case GlowProfile.Lamp: glow.intensity = .55f + (Mathf.PerlinNoise(t * 3f, seed) - .5f) * .08f; break;
                default: glow.intensity = .22f; break;
            }
        }
    }
}
