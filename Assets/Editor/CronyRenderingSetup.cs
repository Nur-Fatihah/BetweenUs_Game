using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Crony.Editor
{
    /// <summary>
    /// Builds the URP 2D lighting pipeline described in Documentation/VisualPipeline.md: pipeline + 2D renderer,
    /// lit sprite materials, the post-processing stack with a generated grading LUT, and the camera/volume wiring.
    /// Safe to re-run; existing assets are updated in place.
    /// </summary>
    public static class CronyRenderingSetup
    {
        const string Folder = "Assets/Settings/Rendering";
        const string PipelinePath = Folder + "/Crony_URP2D.asset";
        const string RendererPath = Folder + "/Crony_2DRenderer.asset";
        const string ProfilePath = Folder + "/Crony_PostProcessing.asset";
        const string LutPath = Folder + "/Crony_GradeLUT.png";
        const string LitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat";
        const int LutSize = 32;

        [MenuItem("Crony/Rendering/Set Up 2D Lighting Pipeline")]
        public static void Run()
        {
            Directory.CreateDirectory(Folder);
            UniversalRenderPipelineAsset pipeline = EnsurePipeline();
            AssignPipeline(pipeline);
            Texture2D lut = EnsureLut();
            VolumeProfile profile = EnsureProfile(lut);
            int converted = ConvertSpriteMaterials();
            WireScene(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("Crony 2D lighting pipeline ready: " + converted + " sprite/tilemap renderers now use the lit material.");
        }

        static UniversalRenderPipelineAsset EnsurePipeline()
        {
            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                // URP only exposes 2D renderer creation through its internal editor menu helper.
                Type menus = Type.GetType("UnityEditor.Rendering.Universal.Renderer2DMenus, Unity.RenderPipelines.Universal.Editor");
                var create = menus.GetMethod("CreateRendererAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var renderer = (ScriptableRendererData)create.Invoke(null, new object[] { RendererPath, RendererType._2DRenderer, false, "Renderer" });
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 1;
            pipeline.colorGradingMode = ColorGradingMode.HighDynamicRange;
            pipeline.colorGradingLutSize = LutSize;
            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        static void AssignPipeline(UniversalRenderPipelineAsset pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>32³ strip LUT: teal-lifted shadows, warm highlights, a gentle S-curve and +6% saturation.</summary>
        static Texture2D EnsureLut()
        {
            Texture2D strip = new Texture2D(LutSize * LutSize, LutSize, TextureFormat.RGB24, false, true);
            for (int b = 0; b < LutSize; b++)
                for (int g = 0; g < LutSize; g++)
                    for (int r = 0; r < LutSize; r++)
                        strip.SetPixel(b * LutSize + r, g, Grade(new Color(r / (LutSize - 1f), g / (LutSize - 1f), b / (LutSize - 1f))));
            File.WriteAllBytes(LutPath, strip.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(strip);
            AssetDatabase.ImportAsset(LutPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(LutPath);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(LutPath);
        }

        static Color Grade(Color c)
        {
            float lum = c.r * .2126f + c.g * .7152f + c.b * .0722f;
            float shadow = (1f - lum) * (1f - lum), highlight = lum * lum;
            Vector3 v = new Vector3(c.r, c.g, c.b) + new Vector3(-.01f, .012f, .03f) * shadow + new Vector3(.03f, .012f, -.02f) * highlight;
            for (int i = 0; i < 3; i++) v[i] = Mathf.Clamp01(.5f + (v[i] - .5f) * 1.06f);
            float graded = v.x * .2126f + v.y * .7152f + v.z * .0722f;
            v = Vector3.LerpUnclamped(new Vector3(graded, graded, graded), v, 1.06f);
            return new Color(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y), Mathf.Clamp01(v.z));
        }

        static VolumeProfile EnsureProfile(Texture2D lut)
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            Bloom bloom = Get<Bloom>(profile);
            bloom.threshold.Override(.95f);
            bloom.intensity.Override(.35f);
            bloom.scatter.Override(.55f);
            bloom.tint.Override(new Color(1f, .95f, .85f));
            bloom.highQualityFiltering.Override(false);
            // Quarter-resolution bloom with fewer passes: the soft glow looks the same and costs a fraction of the GPU time.
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            bloom.maxIterations.Override(4);
            Get<ChromaticAberration>(profile).intensity.Override(.04f);
            ColorAdjustments adjustments = Get<ColorAdjustments>(profile);
            adjustments.contrast.Override(6f);
            adjustments.saturation.Override(4f);
            Vignette vignette = Get<Vignette>(profile);
            vignette.intensity.Override(.18f);
            vignette.smoothness.Override(.5f);
            vignette.color.Override(new Color(.12f, .09f, .22f));
            ColorLookup lookup = Get<ColorLookup>(profile);
            lookup.texture.Override(lut);
            lookup.contribution.Override(.65f);
            // No tonemapping: it would shift the hand-picked pixel palette.
            Get<Tonemapping>(profile).mode.Override(TonemappingMode.None);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component)) return component;
            component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static int ConvertSpriteMaterials()
        {
            Material lit = AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath);
            int converted = 0;
            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects()) converted += ConvertUnder(root, lit);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = PrefabUtility.LoadPrefabContents(path);
                int count = ConvertUnder(prefab, lit);
                if (count > 0) PrefabUtility.SaveAsPrefabAsset(prefab, path);
                PrefabUtility.UnloadPrefabContents(prefab);
                converted += count;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return converted;
        }

        static int ConvertUnder(GameObject root, Material lit)
        {
            int count = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SpriteRenderer) && !(renderer is TilemapRenderer)) continue;
                Material current = renderer.sharedMaterial;
                if (current != null && current.name != "Sprites-Default") continue;
                renderer.sharedMaterial = lit;
                count++;
            }
            return count;
        }

        static void WireScene(VolumeProfile profile)
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject volumeObject = GameObject.Find("Global Post Processing") ?? new GameObject("Global Post Processing");
            if (!volumeObject.TryGetComponent(out Volume volume)) volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.allowHDR = true;
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.None;
                PixelPerfectCamera pixelPerfect = camera.GetComponent<PixelPerfectCamera>();
                // Sprites keep sub-pixel positions so secondary motion glides instead of popping pixel to pixel.
                if (pixelPerfect != null) pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.None;
            }
            foreach (Light2D light in UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include))
            {
                GlowLight.TargetAllLayers(light);
                if (light.lightType == Light2D.LightType.Global) { light.intensity = 1f; light.color = new Color(1f, .98f, .95f); }
                EditorUtility.SetDirty(light);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
