using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Crony.Editor
{
    /// <summary>Idempotent project authoring pass for NGO prefabs, ten task assets, and scene wiring.</summary>
    public static class CronyProjectSetup
    {
        const string ScenePath = "Assets/Scenes/TownshipPark_Map.unity";
        const string PlayerPrefabPath = "Assets/Prefabs/Player/NetworkPlayer.prefab";
        const string CatalogPath = "Assets/Data/Avatar/AvatarCosmeticCatalog.asset";
        const string AvatarDatabasePath = "Assets/Data/Avatar/AvatarDatabase.asset";
        const int AvatarSortingLayerId = 0x41564154;

        sealed class TaskSpec
        {
            public TownshipTaskKind kind;
            public string fileName;
            public string componentType;
            public string title;
            public string location;
            public string instructions;
            public string[] actions;
            public int[] sequence;
            public Vector2 mapPosition;

            public TaskSpec(TownshipTaskKind kind, string fileName, string componentType, string title, string location,
                string instructions, string[] actions, int[] sequence, Vector2 mapPosition)
            {
                this.kind = kind; this.fileName = fileName; this.componentType = componentType; this.title = title;
                this.location = location; this.instructions = instructions; this.actions = actions;
                this.sequence = sequence; this.mapPosition = mapPosition;
            }
        }

        sealed class AvatarItemSpec
        {
            public readonly string id;
            public readonly string title;
            public readonly AvatarItemCategory category;
            public readonly Color color;
            public readonly bool tintable;
            public readonly int variant;

            public AvatarItemSpec(string id, string title, AvatarItemCategory category, string hexColor, bool tintable, int variant = 0)
            {
                this.id = id; this.title = title; this.category = category; color = Hex(hexColor); this.tintable = tintable; this.variant = variant;
            }
        }

        static readonly AvatarItemSpec[] AvatarItems =
        {
            new AvatarItemSpec("00-skin-light", "Porcelain", AvatarItemCategory.Skin, "F0C8A7", true),
            new AvatarItemSpec("01-skin-warm", "Warm sand", AvatarItemCategory.Skin, "D9A27E", true),
            new AvatarItemSpec("02-skin-amber", "Amber", AvatarItemCategory.Skin, "A96D4D", true),
            new AvatarItemSpec("03-skin-deep", "Deep umber", AvatarItemCategory.Skin, "7F503D", true),
            new AvatarItemSpec("04-skin-mahogany", "Mahogany", AvatarItemCategory.Skin, "5A382F", true),
            new AvatarItemSpec("00-face-calm", "Calm eyes", AvatarItemCategory.Face, "FFFFFF", false, 0),
            new AvatarItemSpec("01-face-blush", "Blushing", AvatarItemCategory.Face, "FFFFFF", false, 2),
            new AvatarItemSpec("02-face-wink", "Wink", AvatarItemCategory.Face, "FFFFFF", false, 1),
            new AvatarItemSpec("00-tattoo-none", "No markings", AvatarItemCategory.Tattoo, "FFFFFF", false, 0),
            new AvatarItemSpec("01-tattoo-flower", "Arm flower", AvatarItemCategory.Tattoo, "FFFFFF", false, 1),
            new AvatarItemSpec("00-hair-short", "Short crop", AvatarItemCategory.Hair, "3A2B27", true, 0),
            new AvatarItemSpec("01-hair-long", "Long waves", AvatarItemCategory.Hair, "49372F", true, 1),
            new AvatarItemSpec("02-hair-curly", "Curly", AvatarItemCategory.Hair, "6A4935", true, 2),
            new AvatarItemSpec("00-hat-none", "No headwear", AvatarItemCategory.Hat, "FFFFFF", false, 0),
            new AvatarItemSpec("01-hat-songkok", "Songkok", AvatarItemCategory.Hat, "242A31", true, 1),
            new AvatarItemSpec("02-hat-tudung", "Tudung", AvatarItemCategory.Hat, "4E7C79", true, 2),
            new AvatarItemSpec("03-hat-cap", "Township cap", AvatarItemCategory.Hat, "498AA2", true, 3),
            new AvatarItemSpec("04-hat-festive", "Festive crown", AvatarItemCategory.Hat, "D4A64C", true, 4),
            new AvatarItemSpec("00-top-casual", "Casual tee", AvatarItemCategory.Top, "E0A64E", true, 0),
            new AvatarItemSpec("01-top-baju-melayu", "Baju Melayu", AvatarItemCategory.Top, "317EB4", true, 1),
            new AvatarItemSpec("02-top-cheongsam", "Cheongsam", AvatarItemCategory.Top, "C9484D", true, 2),
            new AvatarItemSpec("03-top-saree", "Saree blouse", AvatarItemCategory.Top, "764B9B", true, 3),
            new AvatarItemSpec("04-top-winter-jacket", "Winter jacket", AvatarItemCategory.Top, "53778D", true, 4),
            new AvatarItemSpec("00-bottom-trousers", "Everyday trousers", AvatarItemCategory.Bottom, "50658B", true, 0),
            new AvatarItemSpec("01-bottom-traditional", "Traditional trousers", AvatarItemCategory.Bottom, "8D5C45", true, 1),
            new AvatarItemSpec("02-bottom-skirt", "Pleated skirt", AvatarItemCategory.Bottom, "AD5B85", true, 2),
            new AvatarItemSpec("00-shoes-sneakers", "Sneakers", AvatarItemCategory.Shoes, "E5E5DE", true, 0),
            new AvatarItemSpec("01-shoes-sandals", "Sandals", AvatarItemCategory.Shoes, "9C6745", true, 1),
            new AvatarItemSpec("02-shoes-boots", "Rain boots", AvatarItemCategory.Shoes, "65836E", true, 2),
            new AvatarItemSpec("00-accessory-none", "No accessory", AvatarItemCategory.Accessory, "FFFFFF", false, 0),
            new AvatarItemSpec("01-accessory-glasses", "Round glasses", AvatarItemCategory.Accessory, "30363D", false, 1),
            new AvatarItemSpec("02-accessory-sling-bag", "Sling bag", AvatarItemCategory.Accessory, "C88745", false, 2),
            new AvatarItemSpec("03-accessory-badge", "Community badge", AvatarItemCategory.Accessory, "EFC15B", false, 3)
        };

        static readonly TaskSpec[] TaskSpecs =
        {
            new TaskSpec(TownshipTaskKind.NasiLemak, "HawkerStalls_NasiLemak", "NasiLemakTask", "Sort & Pack Nasi Lemak", "Hawker Stalls",
                "Drag sambal, boiled egg, cucumber, and anchovies onto the banana leaf in the printed order.",
                new[] { "Sambal", "Boiled egg", "Cucumber", "Anchovies" }, new[] { 0, 1, 2, 3 }, new Vector2(24f, 6.1f)),
            new TaskSpec(TownshipTaskKind.IceCream, "ConvenienceStore_IceCream", "IceCreamTask", "Restock Ice Cream Cabinet", "Convenience Store",
                "Drag each local ice cream pop into the freezer slot with its matching flavor silhouette.", new[] { "Pandan", "Mango", "Red bean", "Coconut" }, new[] { 2, 0, 3, 1 }, new Vector2(24f, -.5f)),
            new TaskSpec(TownshipTaskKind.ParkBench, "RecreationalPark_Bench", "ParkBenchTask", "Fix Park Bench", "Recreational Park",
                "Rotate the wrench to align both wooden slats and tighten each bolt within 45 seconds.", new[] { "Align first slat", "Tighten first bolt", "Align second slat", "Tighten second bolt" }, new[] { 0, 1, 2, 3 }, new Vector2(-7f, -2.7f)),
            new TaskSpec(TownshipTaskKind.BadmintonNet, "CommunityHall_BadmintonNet", "BadmintonTask", "Calibrate Badminton Net", "Community Hall",
                "Slide the tension control until its marker meets the height gauge line, then calibrate.", new[] { "Calibrate tension" }, new[] { 0 }, new Vector2(7f, 17.8f)),
            new TaskSpec(TownshipTaskKind.CommunityPermits, "AdministrativeOffice_Permits", "CommunityPermitsTask", "File Community Permits", "Administrative Office",
                "File the event, facility, and commercial permits into their matching color binders.", new[] { "Event Permit", "Facility Permit", "Commercial Permit" }, new[] { 0, 1, 2 }, new Vector2(-7f, 17.8f)),
            new TaskSpec(TownshipTaskKind.BusSchedule, "BusStop_ScheduleBoard", "BusScheduleTask", "Update Bus Schedule Board", "Bus Stop",
                "Drag the departure tiles into chronological order to match the route display.", new[] { "08:30 AM", "10:15 AM", "07:45 AM", "09:30 AM" }, new[] { 2, 0, 3, 1 }, new Vector2(-7f, -21.5f)),
            new TaskSpec(TownshipTaskKind.LakeCleanup, "CentralLake_Cleanup", "LakeCleanupTask", "Clear Water Hyacinths & Trash", "Central Lake",
                "Drag plastic bottles, cans, and overgrown water hyacinths into their sorting bins.", new[] { "Plastic Bottle", "Aluminum Can", "Water Hyacinths" }, new[] { 0, 1, 2 }, new Vector2(0f, 6.7f)),
            new TaskSpec(TownshipTaskKind.Router, "CellularTower_Router", "RouterResetTask", "Reset Wi-Fi Router & PA System", "Cellular Tower",
                "Toggle the five numbered breakers in ascending order; a wrong switch resets the sequence.", new[] { "Circuit Breaker 1", "Circuit Breaker 2", "Circuit Breaker 3", "Circuit Breaker 4", "Circuit Breaker 5" }, new[] { 0, 1, 2, 3, 4 }, new Vector2(-23f, 6.5f)),
            new TaskSpec(TownshipTaskKind.SolarBatteries, "Walkway_SolarBatteries", "SolarBatteryTask", "Replace Solar Streetlight Batteries", "Pedestrian Walkway",
                "Hold and rotate to unscrew the compartment, remove the drained cell, then fit a charged solar battery.", new[] { "Unscrew compartment", "Remove drained battery", "Install charged battery" }, new[] { 0, 1, 2 }, new Vector2(-23f, -7.7f)),
            new TaskSpec(TownshipTaskKind.NoticeBoard, "NoticeBoard_Announcements", "NoticeBoardTask", "Pin Community Announcements", "Notice Board",
                "Align three community flyers on their crosshairs and press each pushpin to secure them.", new[] { "Park Cleanup Day", "Badminton Signup", "Lake Safety Notice" }, new[] { 0, 1, 2 }, new Vector2(7f, -21.2f))
        };

        [MenuItem("Crony/Configure Township Park Systems")]
        public static void ConfigureProject()
        {
            EnsureFolder("Assets", "Data");
            EnsureFolder("Assets/Data", "Tasks");
            EnsureFolder("Assets/Data", "Avatar");
            EnsureFolder("Assets", "Art");
            EnsureFolder("Assets/Art", "AvatarParts");
            EnsureFolder("Assets/Art", "Generated");
            EnsureFolder("Assets/Prefabs/Player", "");

            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Sprite bodySprite = CreateSpriteAsset("AvatarBody", AvatarSprite.Body);
            AvatarCosmeticCatalog catalog = ConfigureCosmeticCatalog();
            AvatarDatabaseSO avatarDatabase = ConfigureAvatarDatabase();
            EnsureAvatarSortingLayer();
            GameObject playerPrefab = ConfigurePlayerPrefab(bodySprite, avatarDatabase);
            ConfigureScene(playerPrefab, catalog, avatarDatabase);
            PixelMapTilemapBuilder.BuildActiveScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("Crony: Township Park NGO, player, tasks, safety, audio, avatar, lighting, and scene setup complete.");
        }

        enum AvatarSprite { Body, Face, ShortHair, LongHair, Hijab, Songkok, BajuMelayu, Cheongsam, Saree, EverydayBaju, None, Brooch, FlowerPin, Glasses }

        static AvatarCosmeticCatalog ConfigureCosmeticCatalog()
        {
            AvatarCosmeticCatalog catalog = AssetDatabase.LoadAssetAtPath<AvatarCosmeticCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AvatarCosmeticCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.headwear = new[]
            {
                Option("Short hair", CreateSpriteAsset("Headwear_ShortHair", AvatarSprite.ShortHair), Color.white),
                Option("Long hair", CreateSpriteAsset("Headwear_LongHair", AvatarSprite.LongHair), Color.white),
                Option("Hijab", CreateSpriteAsset("Headwear_Hijab", AvatarSprite.Hijab), Color.white),
                Option("Songkok", CreateSpriteAsset("Headwear_Songkok", AvatarSprite.Songkok), Color.white)
            };
            catalog.outfits = new[]
            {
                Option("Baju Melayu", CreateSpriteAsset("Outfit_BajuMelayu", AvatarSprite.BajuMelayu), Color.white),
                Option("Cheongsam", CreateSpriteAsset("Outfit_Cheongsam", AvatarSprite.Cheongsam), Color.white),
                Option("Saree", CreateSpriteAsset("Outfit_Saree", AvatarSprite.Saree), Color.white),
                Option("Everyday baju", CreateSpriteAsset("Outfit_EverydayBaju", AvatarSprite.EverydayBaju), Color.white)
            };
            catalog.accessories = new[]
            {
                Option("None", CreateSpriteAsset("Accessory_None", AvatarSprite.None), Color.white),
                Option("Songket brooch", CreateSpriteAsset("Accessory_Brooch", AvatarSprite.Brooch), Color.white),
                Option("Flower pin", CreateSpriteAsset("Accessory_FlowerPin", AvatarSprite.FlowerPin), Color.white),
                Option("Glasses", CreateSpriteAsset("Accessory_Glasses", AvatarSprite.Glasses), Color.white)
            };
            catalog.skinTones = new[] { Hex("F0C8A7"), Hex("D9A27E"), Hex("A96D4D"), Hex("7F503D"), Hex("5A382F") };
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        static AvatarCosmeticOption Option(string name, Sprite sprite, Color tint) => new AvatarCosmeticOption { displayName = name, sprite = sprite, tint = tint };

        static AvatarDatabaseSO ConfigureAvatarDatabase()
        {
            EnsureFolder("Assets/Data/Avatar", "Items");
            EnsureFolder("Assets/Art/Generated", "AvatarItems");
            EnsurePixelShadowSprite();
            List<AvatarItemSO> catalogItems = new List<AvatarItemSO>(AvatarItems.Length);
            foreach (AvatarItemSpec spec in AvatarItems)
            {
                Sprite sprite = CreatePixelAvatarSprite(spec);
                string assetPath = "Assets/Data/Avatar/Items/" + spec.id + ".asset";
                AvatarItemSO item = AssetDatabase.LoadAssetAtPath<AvatarItemSO>(assetPath);
                if (item == null)
                {
                    item = ScriptableObject.CreateInstance<AvatarItemSO>();
                    AssetDatabase.CreateAsset(item, assetPath);
                }
                item.Configure(spec.id, spec.title, spec.category, sprite, spec.tintable, spec.color);
                EditorUtility.SetDirty(item);
                catalogItems.Add(item);
            }

            AvatarDatabaseSO database = AssetDatabase.LoadAssetAtPath<AvatarDatabaseSO>(AvatarDatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<AvatarDatabaseSO>();
                AssetDatabase.CreateAsset(database, AvatarDatabasePath);
            }
            database.Configure(catalogItems);
            EditorUtility.SetDirty(database);
            return database;
        }

        static void EnsurePixelShadowSprite()
        {
            const string assetPath = "Assets/Art/Generated/AvatarShadow.png";
            Texture2D texture = PixelArtAssetGenerator.CreateShadowTexture();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "Art/Generated/AvatarShadow.png"), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(.5f, 0f);
            importer.SetTextureSettings(settings);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelArtAssetGenerator.PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        static Sprite CreatePixelAvatarSprite(AvatarItemSpec spec)
        {
            string assetPath = "Assets/Art/Generated/AvatarItems/" + spec.id + ".png";
            string diskPath = Path.Combine(Application.dataPath, "Art/Generated/AvatarItems/" + spec.id + ".png");
            Texture2D texture = PixelArtAssetGenerator.CreateTexture(spec.category, spec.id, spec.variant);
            File.WriteAllBytes(diskPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                TextureImporterSettings spriteSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(spriteSettings);
                spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
                spriteSettings.spriteAlignment = (int)SpriteAlignment.Custom;
                spriteSettings.spritePivot = new Vector2(.5f, 0f);
                importer.SetTextureSettings(spriteSettings);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = PixelArtAssetGenerator.PixelsPerUnit;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        static void EnsureAvatarSortingLayer()
        {
            UnityEngine.Object[] settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (settingsAssets == null || settingsAssets.Length == 0) return;
            SerializedObject tagManager = new SerializedObject(settingsAssets[0]);
            SerializedProperty sortingLayers = tagManager.FindProperty("m_SortingLayers");
            if (sortingLayers == null) return;
            for (int i = 0; i < sortingLayers.arraySize; i++)
                if (sortingLayers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == "Avatar") return;
            int insertIndex = sortingLayers.arraySize > 0 ? 1 : 0;
            sortingLayers.InsertArrayElementAtIndex(insertIndex);
            SerializedProperty layer = sortingLayers.GetArrayElementAtIndex(insertIndex);
            layer.FindPropertyRelative("name").stringValue = "Avatar";
            layer.FindPropertyRelative("uniqueID").intValue = AvatarSortingLayerId;
            layer.FindPropertyRelative("locked").intValue = 0;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The avatar is drawn upward from its feet (sprite pivot at the bottom), so the body collider is a small
        /// pill at the feet, the same footprint the offline map tests. A centred collider poked half a tile below
        /// the feet and snagged on water beside boardwalks and roads.
        /// </summary>
        public static void ApplyFootCollider(CapsuleCollider2D collider)
        {
            collider.direction = CapsuleDirection2D.Horizontal;
            collider.size = new Vector2(.44f, .28f);
            collider.offset = new Vector2(0f, .1f);
        }

        static GameObject ConfigurePlayerPrefab(Sprite bodySprite, AvatarDatabaseSO database)
        {
            GameObject root = new GameObject("NetworkPlayer");
            root.AddComponent<NetworkObject>();
            Rigidbody2D rigidbody = root.AddComponent<Rigidbody2D>();
            rigidbody.gravityScale = 0f;
            rigidbody.constraints = RigidbodyConstraints2D.FreezeRotation;
            rigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;
            rigidbody.bodyType = RigidbodyType2D.Dynamic;
            CapsuleCollider2D collider = root.AddComponent<CapsuleCollider2D>();
            ApplyFootCollider(collider);

            SpriteRenderer body = root.AddComponent<SpriteRenderer>();
            body.sprite = bodySprite;
            body.sortingOrder = 14;
            body.color = Hex("68BCA7");
            body.enabled = false;

            string[] layerNames =
            {
                "Avatar Shadow", "Avatar Skin", "Avatar Tattoos", "Avatar Face", "Avatar Bottoms",
                "Avatar Footwear", "Avatar Top", "Avatar Hair", "Avatar Headwear", "Avatar Accessories"
            };
            SpriteRenderer[] avatarLayers = new SpriteRenderer[PlayerAvatarLayerManager.LayerCount];
            for (int i = 0; i < avatarLayers.Length; i++)
            {
                avatarLayers[i] = CreateSpriteChild(root.transform, layerNames[i], null, Vector2.zero, Vector2.one, i);
                avatarLayers[i].sortingLayerName = "Avatar";
            }

            GameObject visionObject = new GameObject("Local Point Vision");
            visionObject.transform.SetParent(root.transform, false);
            Light2D playerLight = visionObject.AddComponent<Light2D>();
            playerLight.lightType = Light2D.LightType.Point;
            playerLight.pointLightOuterRadius = 6.2f;
            playerLight.pointLightInnerRadius = .7f;
            playerLight.intensity = 1.15f;
            playerLight.shadowsEnabled = true;
            playerLight.enabled = false;

            PlayerController controller = root.AddComponent<PlayerController>();
            SerializedObject controllerData = new SerializedObject(controller);
            controllerData.FindProperty("body").objectReferenceValue = rigidbody;
            SerializedProperty rendererArray = controllerData.FindProperty("visualRenderers");
            rendererArray.arraySize = avatarLayers.Length;
            for (int i = 0; i < avatarLayers.Length; i++) rendererArray.GetArrayElementAtIndex(i).objectReferenceValue = avatarLayers[i];
            controllerData.ApplyModifiedPropertiesWithoutUndo();

            NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            PlayerAvatarLayerManager layerManager = root.AddComponent<PlayerAvatarLayerManager>();
            SerializedObject avatarData = new SerializedObject(layerManager);
            avatarData.FindProperty("database").objectReferenceValue = database;
            SerializedProperty layerArray = avatarData.FindProperty("layerRenderers");
            layerArray.arraySize = avatarLayers.Length;
            for (int i = 0; i < avatarLayers.Length; i++) layerArray.GetArrayElementAtIndex(i).objectReferenceValue = avatarLayers[i];
            Sprite shadowSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Generated/AvatarShadow.png");
            avatarData.FindProperty("shadowSprite").objectReferenceValue = shadowSprite;
            avatarData.ApplyModifiedPropertiesWithoutUndo();
            avatarLayers[0].sprite = shadowSprite;
            avatarLayers[0].color = Color.white;
            avatarLayers[0].enabled = shadowSprite != null;
            EditorUtility.SetDirty(avatarLayers[0]);
            AvatarLookState defaultLook = database != null ? database.CreateDefaultState() : AvatarLookState.CreateDefault();
            AvatarItemCategory[] categoriesByLayer =
            {
                AvatarItemCategory.Skin, AvatarItemCategory.Skin, AvatarItemCategory.Tattoo, AvatarItemCategory.Face,
                AvatarItemCategory.Bottom, AvatarItemCategory.Shoes, AvatarItemCategory.Top, AvatarItemCategory.Hair,
                AvatarItemCategory.Hat, AvatarItemCategory.Accessory
            };
            for (int i = 1; i < avatarLayers.Length; i++)
            {
                AvatarItemCategory category = categoriesByLayer[i];
                AvatarItemSO item = database != null ? database.GetItem(category, defaultLook.GetIndex(category)) : null;
                avatarLayers[i].sprite = item != null ? item.Sprite : PixelArtAssetGenerator.CreateSprite(category, "PrefabFallback");
                avatarLayers[i].color = item != null ? PlayerAvatarLayerManager.ResolveTint(item, defaultLook) : Color.white;
                avatarLayers[i].enabled = avatarLayers[i].sprite != null;
                EditorUtility.SetDirty(avatarLayers[i]);
            }
            VisionController vision = root.AddComponent<VisionController>();
            SerializedObject visionData = new SerializedObject(vision);
            visionData.FindProperty("playerLight").objectReferenceValue = playerLight;
            visionData.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        static SpriteRenderer CreateSpriteChild(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 scale, int sortingOrder)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = position;
            child.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        static void ConfigureScene(GameObject playerPrefab, AvatarCosmeticCatalog catalog, AvatarDatabaseSO avatarDatabase)
        {
            GameObject oldPlayer = GameObject.Find("PlayerPrefab");
            if (oldPlayer != null) Undo.DestroyObjectImmediate(oldPlayer);

            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.backgroundColor = Hex("94D4B1");
            }

            Light2D globalLight = null;
            foreach (Light2D light in UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsInactive.Exclude))
                if (light.lightType == Light2D.LightType.Global) { globalLight = light; break; }
            if (globalLight == null)
            {
                GameObject lightRoot = new GameObject("Township Global 2D Light");
                globalLight = lightRoot.AddComponent<Light2D>();
            }
            globalLight.lightType = Light2D.LightType.Global;
            globalLight.intensity = .72f;
            globalLight.shadowsEnabled = false;

            NetworkManager manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
            if (manager == null)
            {
                GameObject networkRoot = new GameObject("NGO Network Manager");
                manager = networkRoot.AddComponent<NetworkManager>();
                UnityTransport transport = networkRoot.AddComponent<UnityTransport>();
                transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
                manager.NetworkConfig.NetworkTransport = transport;
                networkRoot.AddComponent<NetworkManagerUI>();
            }
            else
            {
                UnityTransport transport = manager.GetComponent<UnityTransport>();
                if (transport == null) transport = manager.gameObject.AddComponent<UnityTransport>();
                transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
                manager.NetworkConfig.NetworkTransport = transport;
                if (manager.GetComponent<NetworkManagerUI>() == null) manager.gameObject.AddComponent<NetworkManagerUI>();
            }
            manager.NetworkConfig.PlayerPrefab = playerPrefab;
            manager.NetworkConfig.TickRate = 30;
            manager.NetworkConfig.EnableSceneManagement = true;
            manager.NetworkConfig.ConnectionApproval = true;
            EditorUtility.SetDirty(manager);
            NetworkManagerUI networkUI = manager.GetComponent<NetworkManagerUI>();
            if (networkUI != null)
            {
                SerializedObject networkUIData = new SerializedObject(networkUI);
                SerializedProperty avatarDatabaseProperty = networkUIData.FindProperty("avatarDatabase");
                if (avatarDatabaseProperty != null) avatarDatabaseProperty.objectReferenceValue = avatarDatabase;
                networkUIData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(networkUI);
            }

            GameObject gameManagerObject = GameObject.Find("Network Game Manager");
            if (gameManagerObject == null) gameManagerObject = new GameObject("Network Game Manager");
            if (gameManagerObject.GetComponent<NetworkObject>() == null) gameManagerObject.AddComponent<NetworkObject>();
            if (gameManagerObject.GetComponent<GameManager>() == null) gameManagerObject.AddComponent<GameManager>();
            if (gameManagerObject.GetComponent<SafetySystem>() == null) gameManagerObject.AddComponent<SafetySystem>();

            ConfigureTaskAssetsAndStations();
            ConfigureTaskMiniGameUI(avatarDatabase);
            ConfigureAudio();
            ConfigureSabotagePresentation();
            ConfigureBuildScene();
        }

        static void ConfigureTaskAssetsAndStations()
        {
            foreach (TaskSpec spec in TaskSpecs)
            {
                string assetPath = "Assets/Data/Tasks/" + spec.fileName + ".asset";
                TownshipTaskDefinition definition = AssetDatabase.LoadAssetAtPath<TownshipTaskDefinition>(assetPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<TownshipTaskDefinition>();
                    AssetDatabase.CreateAsset(definition, assetPath);
                }
                SerializedObject data = new SerializedObject(definition);
                data.FindProperty("kind").enumValueIndex = (int)spec.kind;
                data.FindProperty("taskName").stringValue = spec.title;
                data.FindProperty("location").stringValue = spec.location;
                data.FindProperty("instructions").stringValue = spec.instructions;
                SerializedProperty actions = data.FindProperty("actionNames");
                actions.arraySize = spec.actions.Length;
                for (int i = 0; i < spec.actions.Length; i++) actions.GetArrayElementAtIndex(i).stringValue = spec.actions[i];
                SerializedProperty sequence = data.FindProperty("requiredActions");
                sequence.arraySize = spec.sequence.Length;
                for (int i = 0; i < spec.sequence.Length; i++) sequence.GetArrayElementAtIndex(i).intValue = spec.sequence[i];
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definition);

                string stationName = "Task Station - " + spec.title;
                GameObject station = GameObject.Find(stationName);
                if (station == null) station = new GameObject(stationName);
                station.transform.position = new Vector3(spec.mapPosition.x, spec.mapPosition.y, 0f);
                if (station.GetComponent<NetworkObject>() == null) station.AddComponent<NetworkObject>();
                BoxCollider2D trigger = station.GetComponent<BoxCollider2D>();
                if (trigger == null) trigger = station.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                trigger.size = new Vector2(4.2f, 4.2f);
                trigger.offset = Vector2.zero;
                Type componentType = typeof(TownshipTaskStation).Assembly.GetType("Crony." + spec.componentType);
                TownshipTaskStation task = station.GetComponent<TownshipTaskStation>();
                if (task != null && task.GetType() != componentType) Undo.DestroyObjectImmediate(task);
                if (station.GetComponent(componentType) == null) station.AddComponent(componentType);
                task = station.GetComponent<TownshipTaskStation>();
                SerializedObject taskData = new SerializedObject(task);
                taskData.FindProperty("definition").objectReferenceValue = definition;
                taskData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(station);
            }
        }

        static void ConfigureTaskMiniGameUI(AvatarDatabaseSO avatarDatabase)
        {
            GameObject canvasRoot = GameObject.Find("Township Task UI Canvas");
            if (canvasRoot == null)
                canvasRoot = new GameObject("Township Task UI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            if (canvas == null) canvas = canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            RectTransform canvasRect = canvasRoot.GetComponent<RectTransform>();
            if (canvasRect == null) canvasRect = canvasRoot.AddComponent<RectTransform>();
            Undo.RecordObject(canvasRect, "Configure Township Task Canvas");
            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one;
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            canvasRect.pivot = new Vector2(.5f, .5f);
            SerializedObject canvasRectData = new SerializedObject(canvasRect);
            canvasRectData.FindProperty("m_LocalScale").vector3Value = Vector3.one;
            canvasRectData.FindProperty("m_AnchorMin").vector2Value = Vector2.zero;
            canvasRectData.FindProperty("m_AnchorMax").vector2Value = Vector2.one;
            canvasRectData.FindProperty("m_AnchoredPosition").vector2Value = Vector2.zero;
            canvasRectData.FindProperty("m_SizeDelta").vector2Value = Vector2.zero;
            canvasRectData.FindProperty("m_Pivot").vector2Value = new Vector2(.5f, .5f);
            canvasRectData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(canvasRect);

            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;

            GraphicRaycaster raycaster = canvasRoot.GetComponent<GraphicRaycaster>();
            if (raycaster == null) raycaster = canvasRoot.AddComponent<GraphicRaycaster>();
            raycaster.enabled = true;

            TownshipTaskMiniGameUI ui = canvasRoot.GetComponent<TownshipTaskMiniGameUI>();
            if (ui == null) ui = canvasRoot.AddComponent<TownshipTaskMiniGameUI>();
            ui.enabled = true;
            AvatarDressingRoomUI dressingRoom = canvasRoot.GetComponent<AvatarDressingRoomUI>();
            if (dressingRoom == null) dressingRoom = canvasRoot.AddComponent<AvatarDressingRoomUI>();
            SerializedObject dressingData = new SerializedObject(dressingRoom);
            dressingData.FindProperty("database").objectReferenceValue = avatarDatabase;
            dressingData.ApplyModifiedPropertiesWithoutUndo();

            TownshipTaskStation[] stations = new TownshipTaskStation[TaskSpecs.Length];
            for (int i = 0; i < TaskSpecs.Length; i++)
            {
                stations[i] = GameObject.Find("Task Station - " + TaskSpecs[i].title)?.GetComponent<TownshipTaskStation>();
                if (stations[i] == null) continue;
                SerializedObject stationData = new SerializedObject(stations[i]);
                SerializedProperty uiReference = stationData.FindProperty("miniGameUI");
                if (uiReference != null) uiReference.objectReferenceValue = ui;
                stationData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(stations[i]);
            }

            SerializedObject uiData = new SerializedObject(ui);
            SerializedProperty stationArray = uiData.FindProperty("sceneStations");
            stationArray.arraySize = stations.Length;
            for (int i = 0; i < stations.Length; i++)
                stationArray.GetArrayElementAtIndex(i).objectReferenceValue = stations[i];
            uiData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ui);

            EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null)
            {
                GameObject eventRoot = new GameObject("EventSystem", typeof(EventSystem));
                eventSystem = eventRoot.GetComponent<EventSystem>();
            }
            eventSystem.gameObject.SetActive(true);
            eventSystem.sendNavigationEvents = true;
            InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            foreach (BaseInputModule oldModule in eventSystem.GetComponents<BaseInputModule>())
                if (!(oldModule is InputSystemUIInputModule)) UnityEngine.Object.DestroyImmediate(oldModule);
            if (inputModule == null) inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
            EditorUtility.SetDirty(canvasRoot);
            EditorUtility.SetDirty(eventSystem.gameObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        static void ConfigureAudio()
        {
            GameObject audioRoot = GameObject.Find("Game Audio Manager");
            if (audioRoot == null) audioRoot = new GameObject("Game Audio Manager");
            if (audioRoot.GetComponent<AudioSource>() == null) audioRoot.AddComponent<AudioSource>();
            GameAudioManager audioManager = audioRoot.GetComponent<GameAudioManager>();
            if (audioManager == null) audioManager = audioRoot.AddComponent<GameAudioManager>();

            AudioClip click = CreateAudioAsset("Assets/Audio/UI/Click.wav", new[] { 880f }, .06f, .12f, .12f, false);
            AudioClip task = CreateAudioAsset("Assets/Audio/Tasks/Interaction.wav", new[] { 520f, 690f }, .12f, .12f, .08f, false);
            AudioClip complete = CreateAudioAsset("Assets/Audio/Tasks/Complete.wav", new[] { 659f, 784f, 988f }, .34f, .12f, 0f, false);
            AudioClip klaxon = CreateAudioAsset("Assets/Audio/UI/EmergencyKlaxon.wav", new[] { 370f, 570f }, .82f, .16f, 0f, true);
            string[] names = { "Grass", "Concrete", "Wood", "Water", "Metal", "Tile" };
            float[] frequencies = { 125f, 210f, 95f, 315f, 700f, 250f };
            AudioClip[] footstepClips = new AudioClip[names.Length];
            for (int i = 0; i < names.Length; i++)
                footstepClips[i] = CreateAudioAsset("Assets/Audio/DynamicFootsteps/" + names[i] + ".wav", new[] { frequencies[i] }, .085f, .08f, .42f, false);

            SerializedObject data = new SerializedObject(audioManager);
            data.FindProperty("uiClick").objectReferenceValue = click;
            data.FindProperty("taskInteraction").objectReferenceValue = task;
            data.FindProperty("taskComplete").objectReferenceValue = complete;
            data.FindProperty("emergencyKlaxon").objectReferenceValue = klaxon;
            SerializedProperty footsteps = data.FindProperty("footsteps");
            footsteps.arraySize = footstepClips.Length;
            for (int i = 0; i < footstepClips.Length; i++) footsteps.GetArrayElementAtIndex(i).objectReferenceValue = footstepClips[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(audioManager);
        }

        static AudioClip CreateAudioAsset(string assetPath, float[] frequencies, float duration, float amplitude, float noiseAmount, bool alternating)
        {
            AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (existing != null) return existing;
            string fullPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            int sampleRate = 22050;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            byte[] wav;
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(36 + sampleCount * 2);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(sampleCount * 2);
                System.Random random = new System.Random(assetPath.GetHashCode());
                for (int i = 0; i < sampleCount; i++)
                {
                    float time = i / (float)sampleRate;
                    float envelope = Mathf.Clamp01((duration - time) * 10f) * Mathf.Exp(-time * (noiseAmount > 0f ? 24f : 5f));
                    float signal = 0f;
                    if (alternating && frequencies.Length > 1)
                    {
                        float tone = frequencies[Mathf.FloorToInt(time / (duration / 4f)) % frequencies.Length];
                        float sweep = Mathf.Lerp(tone, tone + 80f, Mathf.PingPong(time * 3f, 1f));
                        signal = Mathf.Sin(2f * Mathf.PI * sweep * time);
                    }
                    else
                    {
                        for (int f = 0; f < frequencies.Length; f++)
                            signal += Mathf.Sin(2f * Mathf.PI * frequencies[f] * time) / frequencies.Length;
                    }
                    if (noiseAmount > 0f) signal = Mathf.Lerp(signal, (float)(random.NextDouble() * 2.0 - 1.0), noiseAmount);
                    short sample = (short)Mathf.Clamp(signal * envelope * amplitude * short.MaxValue, short.MinValue, short.MaxValue);
                    writer.Write(sample);
                }
                wav = stream.ToArray();
            }
            File.WriteAllBytes(fullPath, wav);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AudioImporter importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer != null)
            {
                importer.forceToMono = true;
                importer.loadInBackground = false;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        }

        static void ConfigureSabotagePresentation()
        {
            GameObject root = GameObject.Find("Sabotage Presentation");
            if (root == null) root = new GameObject("Sabotage Presentation");
            SabotagePresentation presentation = root.GetComponent<SabotagePresentation>();
            if (presentation == null) presentation = root.AddComponent<SabotagePresentation>();

            GameObject gas = CreateMarker("Gas Leak Marker", new Vector2(-16f, 7.5f), Hex("E28B4C"), .9f);
            GameObject water = CreateMarker("Water Contamination Marker", new Vector2(4.65f, -2.8f), Hex("6F7DCC"), 1.6f);
            GameObject interference = CreateMarker("Interference Marker", new Vector2(-12f, -8.1f), Hex("D5C15B"), .9f);
            GameObject gate = FindSceneObjectIncludingInactive("Route Blockade Gate");
            DestroyDuplicateSceneObjects("Route Blockade Gate", gate);
            if (gate == null)
            {
                gate = new GameObject("Route Blockade Gate");
                gate.transform.position = new Vector3(0f, -6.85f, 0f);
                gate.transform.localScale = new Vector3(3.2f, .45f, 1f);
                SpriteRenderer renderer = gate.AddComponent<SpriteRenderer>();
                renderer.sprite = CreateSpriteAsset("AvatarBody", AvatarSprite.Body);
                renderer.color = Hex("D26C54");
                renderer.sortingOrder = 8;
                gate.AddComponent<BoxCollider2D>();
                SurfaceAudioMarker surface = gate.AddComponent<SurfaceAudioMarker>();
                surface.SetSurface(SurfaceType.Wood);
            }
            gate.SetActive(false);

            SerializedObject data = new SerializedObject(presentation);
            data.FindProperty("gasLeakMarker").objectReferenceValue = gas;
            data.FindProperty("waterContaminationMarker").objectReferenceValue = water;
            data.FindProperty("interferenceMarker").objectReferenceValue = interference;
            data.FindProperty("routeBlockadeGate").objectReferenceValue = gate;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject CreateMarker(string name, Vector2 position, Color color, float size)
        {
            GameObject marker = FindSceneObjectIncludingInactive(name);
            DestroyDuplicateSceneObjects(name, marker);
            if (marker == null) marker = new GameObject(name);
            marker.transform.position = new Vector3(position.x, position.y, 0f);
            marker.transform.localScale = Vector3.one * size;
            SpriteRenderer renderer = marker.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = marker.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateSpriteAsset("AvatarBody", AvatarSprite.Body);
            renderer.color = color;
            renderer.sortingOrder = 10;
            marker.SetActive(false);
            return marker;
        }

        static GameObject FindSceneObjectIncludingInactive(string objectName)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
                if (candidate.scene.IsValid() && candidate.scene == activeScene && candidate.name == objectName) return candidate;
            return null;
        }

        static void DestroyDuplicateSceneObjects(string objectName, GameObject keep)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
                if (candidate != keep && candidate.scene.IsValid() && candidate.scene == activeScene && candidate.name == objectName)
                    Undo.DestroyObjectImmediate(candidate);
        }

        // Release identity. The version is what the online version check compares, so bump it with every release.
        const string CompanyName = "Crony Games", ApplicationId = "com.cronygames.crony", GameVersion = "1.0.1";
        const string IconPath = "Assets/Art/Icon/CronyIcon.png";

        static void ApplyAppIcon()
        {
            if (!File.Exists(IconPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
                Texture2D icon = PixelLogo.RenderIcon(512);
                File.WriteAllBytes(IconPath, icon.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(icon);
                AssetDatabase.ImportAsset(IconPath);
                if (AssetImporter.GetAtPath(IconPath) is TextureImporter importer)
                {
                    importer.filterMode = FilterMode.Point;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
            }
            Texture2D asset = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (asset != null) PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { asset }, IconKind.Any);
        }

        static void ConfigureBuildScene()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            bool found = false;
            foreach (EditorBuildSettingsScene scene in scenes)
                if (scene.path == ScenePath) { scene.enabled = true; found = true; }
            if (!found)
            {
                Array.Resize(ref scenes, scenes.Length + 1);
                scenes[scenes.Length - 1] = new EditorBuildSettingsScene(ScenePath, true);
            }
            EditorBuildSettings.scenes = scenes;
            PlayerSettings.productName = "Crony";
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, ApplicationId);
            PlayerSettings.bundleVersion = GameVersion;
            ApplyAppIcon();
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
        }

        [MenuItem("Crony/Multiplayer Launcher (Up to 8 Players)/1. Build Test Client (Builds/LocalTestClient)")]
        public static void BuildLocalTestClient()
        {
            ConfigureBuildScene();
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string fullExePath = Path.Combine(projectRoot, NetworkManagerUI.TestClientBuildLocation);
            string buildFolder = Path.GetDirectoryName(fullExePath);
            if (!string.IsNullOrEmpty(buildFolder) && !Directory.Exists(buildFolder))
                Directory.CreateDirectory(buildFolder);

#if UNITY_EDITOR_OSX
            // Universal binary, so the client runs on Intel and Apple Silicon Macs alike.
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
#endif
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = fullExePath,
                target = EditorUserBuildSettings.activeBuildTarget,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                Debug.Log("Crony: Local test client built at " + fullExePath + ". You can now launch up to 7 client windows from Unity to play with 8 players total!");
            else
                Debug.LogError("Crony: Build failed (" + report.summary.result + "). Check the Console for details.");
        }

        /// <summary>
        /// Builds the itch.io downloads: Builds/Release/Windows/Crony.exe and Builds/Release/Mac/Crony.app. Zip each
        /// folder for upload (on a Mac, zip the .app with "ditto -c -k --keepParent" so it stays runnable).
        /// </summary>
        [MenuItem("Crony/Build Release (Windows + Mac)")]
        public static void BuildRelease()
        {
            ConfigureBuildScene();
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "Release");
            BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;
            bool ok = BuildFor(BuildTarget.StandaloneWindows64, Path.Combine(root, "Windows", "Crony.exe"));
#if UNITY_EDITOR_OSX
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
#endif
            ok &= BuildFor(BuildTarget.StandaloneOSX, Path.Combine(root, "Mac", "Crony.app"));
            if (EditorUserBuildSettings.activeBuildTarget != previous) EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(previous), previous);
            if (ok) Debug.Log("Crony: Release builds for version " + PlayerSettings.bundleVersion + " are in " + root);
        }

        /// <summary>
        /// Renders the three music tracks to Assets/Resources/Music for the browser build, which can't render them on
        /// worker threads at runtime.
        /// </summary>
        [MenuItem("Crony/Bake Browser Music")]
        public static void BakeBrowserMusic()
        {
            string folder = "Assets/Resources/" + GameAudioManager.BakedMusicFolder;
            Directory.CreateDirectory(folder);
            foreach (MusicMood mood in new[] { MusicMood.Menu, MusicMood.Park, MusicMood.Meeting })
            {
                string path = folder + "/" + mood + ".wav";
                WriteWav(path, GameAudioManager.RenderMusicForBake(mood), AcousticSynth.Rate);
                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is AudioImporter importer)
                {
                    AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = .6f;
                    settings.loadType = AudioClipLoadType.CompressedInMemory;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }
            }
            Debug.Log("Crony: Browser music baked to " + folder);
        }

        static void WriteWav(string path, float[] samples, int rate)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                int dataBytes = samples.Length * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataBytes);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(dataBytes);
                foreach (float sample in samples) writer.Write((short)Mathf.Clamp(sample * 32767f, -32768f, 32767f));
            }
        }

        /// <summary>
        /// Builds the browser version for itch.io (HTML5) into Builds/Release/WebGL and zips it for upload. Online
        /// rooms work in the browser; same-Wi-Fi play is desktop-only.
        /// </summary>
        [MenuItem("Crony/Build Browser Version (WebGL for itch.io)")]
        public static void BuildBrowser()
        {
            ConfigureBuildScene();
            BakeBrowserMusic();
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            // itch.io serves the compressed files without a Content-Encoding header, so the loader must unpack them itself.
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            // Assets/WebGLTemplates/Crony: the canvas fills the itch.io frame, with no Unity footer to crop.
            PlayerSettings.WebGL.template = "PROJECT:Crony";
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.runInBackground = true;
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "Release");
            BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;
            // BuildFor clears the parent of the output path, so the build goes one level down: only WebGL/ is replaced.
            string output = Path.Combine(root, "WebGL", "Crony");
            bool ok = BuildFor(BuildTarget.WebGL, output);
            if (EditorUserBuildSettings.activeBuildTarget != previous) EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(previous), previous);
            if (ok) Debug.Log("Crony: Browser build for version " + PlayerSettings.bundleVersion + " is in " + output);
        }

        static bool BuildFor(BuildTarget target, string path)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
            {
                Debug.LogError("Crony: " + target + " build support is not installed (Unity Hub > Installs > Add modules).");
                return false;
            }
            string folder = Path.GetDirectoryName(path);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = target, options = BuildOptions.None });
            // Unity can report success even when a platform's post-processing failed, so check the output exists.
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded && (File.Exists(path) || Directory.Exists(path));
            if (!ok) Debug.LogError("Crony: " + target + " build failed (" + report.summary.result + ", " + report.summary.totalErrors + " errors).");
            return ok;
        }

        [MenuItem("Crony/Multiplayer Launcher (Up to 8 Players)/2. Launch +1 Auto-Join Client Window")]
        public static void LaunchOneLocalClient() => LaunchAutoJoinClients(1);

        [MenuItem("Crony/Multiplayer Launcher (Up to 8 Players)/3. Launch 3 Auto-Join Client Windows (4 Players Total with Editor Host)")]
        public static void LaunchThreeLocalClients() => LaunchAutoJoinClients(3);

        [MenuItem("Crony/Multiplayer Launcher (Up to 8 Players)/4. Launch 7 Auto-Join Client Windows (Max 8 Players Total with Editor Host)")]
        public static void LaunchSevenLocalClients() => LaunchAutoJoinClients(7);

        [MenuItem("Crony/Multiplayer Launcher (Up to 8 Players)/5. Close All Launched Client Windows")]
        public static void CloseAllLocalClients()
        {
            int closed = 0;
            foreach (System.Diagnostics.Process proc in System.Diagnostics.Process.GetProcessesByName("Crony"))
            {
                try
                {
                    proc.Kill();
                    closed++;
                }
                catch { }
            }
            Debug.Log("Crony: Closed " + closed + " local client window(s).");
        }

        static void LaunchAutoJoinClients(int countToLaunch)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string fullExePath = Path.Combine(projectRoot, NetworkManagerUI.TestClientExecutable);
            if (!File.Exists(fullExePath))
            {
                bool buildNow = EditorUtility.DisplayDialog(
                    "Build Local Test Client First?",
                    "No test client executable was found at:\n" + NetworkManagerUI.TestClientExecutable + "\n\nWould you like Unity to build it now so you can launch up to 8 players directly from Unity?",
                    "Build & Launch",
                    "Cancel");
                if (!buildNow) return;
                BuildLocalTestClient();
                if (!File.Exists(fullExePath)) return;
            }

            int existingProcesses = System.Diagnostics.Process.GetProcessesByName("Crony").Length;
            int maxAdditional = Mathf.Max(0, GameManager.MaxPlayers - 1 - existingProcesses);
            int toSpawn = Mathf.Min(countToLaunch, maxAdditional);
            if (toSpawn <= 0)
            {
                Debug.LogWarning("Crony: Already at the 8-player maximum (1 Editor Host + 7 Client Windows).");
                return;
            }

            for (int i = 0; i < toSpawn; i++)
            {
                int playerNumber = existingProcesses + i + 2;
                NetworkManagerUI.LaunchTestClient(fullExePath, playerNumber);
            }
            Debug.Log("Crony: Launched " + toSpawn + " client window(s) auto-joining 127.0.0.1 (up to 8 players max).");
        }

        static Sprite CreateSpriteAsset(string assetName, AvatarSprite style)
        {
            string assetPath = "Assets/Art/AvatarParts/" + assetName + ".png";
            Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[64 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 0);
            DrawAvatarPart(pixels, style);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "Art/AvatarParts/" + assetName + ".png"), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                TextureImporterSettings spriteSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(spriteSettings);
                spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
                spriteSettings.spriteAlignment = (int)SpriteAlignment.Center;
                importer.SetTextureSettings(spriteSettings);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 64f;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        static void DrawAvatarPart(Color32[] pixels, AvatarSprite style)
        {
            Color32 clear = new Color32(0, 0, 0, 0);
            switch (style)
            {
                case AvatarSprite.Body: Ellipse(pixels, 32, 32, 25, 30, Color.white); break;
                case AvatarSprite.Face: Ellipse(pixels, 32, 32, 22, 22, Color.white); break;
                case AvatarSprite.ShortHair:
                    Ellipse(pixels, 32, 49, 23, 13, Hex("29333A"));
                    Polygon(pixels, new[] { new Vector2Int(9, 49), new Vector2Int(16, 42), new Vector2Int(22, 49) }, Hex("29333A"));
                    Polygon(pixels, new[] { new Vector2Int(42, 49), new Vector2Int(49, 42), new Vector2Int(55, 49) }, Hex("29333A"));
                    break;
                case AvatarSprite.LongHair:
                    Ellipse(pixels, 32, 37, 27, 28, Hex("49372F"));
                    Ellipse(pixels, 32, 33, 17, 20, Color.clear);
                    Polygon(pixels, new[] { new Vector2Int(7, 40), new Vector2Int(17, 33), new Vector2Int(19, 8), new Vector2Int(8, 9) }, Hex("49372F"));
                    Polygon(pixels, new[] { new Vector2Int(45, 33), new Vector2Int(57, 40), new Vector2Int(56, 9), new Vector2Int(45, 8) }, Hex("49372F"));
                    break;
                case AvatarSprite.Hijab:
                    Ellipse(pixels, 32, 35, 29, 30, Hex("4E7C79"));
                    Ellipse(pixels, 32, 37, 17, 20, Color.clear);
                    Polygon(pixels, new[] { new Vector2Int(7, 23), new Vector2Int(18, 16), new Vector2Int(27, 5), new Vector2Int(38, 5), new Vector2Int(48, 16), new Vector2Int(57, 23), new Vector2Int(50, 5), new Vector2Int(13, 5) }, Hex("4E7C79"));
                    break;
                case AvatarSprite.Songkok:
                    Rect(pixels, 13, 39, 51, 61, Hex("242A31"));
                    Rect(pixels, 11, 37, 53, 42, Hex("171C22"));
                    break;
                case AvatarSprite.BajuMelayu:
                    Polygon(pixels, new[] { new Vector2Int(12, 50), new Vector2Int(24, 57), new Vector2Int(40, 57), new Vector2Int(52, 50), new Vector2Int(59, 37), new Vector2Int(50, 32), new Vector2Int(46, 42), new Vector2Int(46, 7), new Vector2Int(18, 7), new Vector2Int(18, 42), new Vector2Int(14, 32), new Vector2Int(5, 37) }, Hex("317EB4"));
                    Rect(pixels, 18, 20, 46, 32, Hex("D69B42"));
                    for (int x = 20; x < 46; x += 5) Rect(pixels, x, 20, x + 1, 32, Hex("F1D28A"));
                    Rect(pixels, 30, 34, 34, 50, Hex("E8DDBB"));
                    break;
                case AvatarSprite.Cheongsam:
                    Polygon(pixels, new[] { new Vector2Int(14, 55), new Vector2Int(21, 58), new Vector2Int(42, 58), new Vector2Int(50, 54), new Vector2Int(46, 7), new Vector2Int(18, 7) }, Hex("C9484D"));
                    Polygon(pixels, new[] { new Vector2Int(25, 55), new Vector2Int(31, 47), new Vector2Int(38, 55), new Vector2Int(33, 58) }, Hex("F2D79B"));
                    for (int i = 0; i < 4; i++) Ellipse(pixels, 34 + i, 42 - i * 7, 2, 2, Hex("F3D48E"));
                    break;
                case AvatarSprite.Saree:
                    Polygon(pixels, new[] { new Vector2Int(14, 55), new Vector2Int(23, 58), new Vector2Int(43, 57), new Vector2Int(50, 53), new Vector2Int(46, 7), new Vector2Int(18, 7) }, Hex("764B9B"));
                    Polygon(pixels, new[] { new Vector2Int(40, 58), new Vector2Int(57, 47), new Vector2Int(50, 40), new Vector2Int(43, 48), new Vector2Int(23, 29), new Vector2Int(18, 35) }, Hex("D49C46"));
                    for (int x = 21; x < 45; x += 5) Rect(pixels, x, 8, x + 1, 23, Hex("D5B36B"));
                    break;
                case AvatarSprite.EverydayBaju:
                    Polygon(pixels, new[] { new Vector2Int(11, 50), new Vector2Int(24, 57), new Vector2Int(40, 57), new Vector2Int(53, 50), new Vector2Int(59, 37), new Vector2Int(50, 32), new Vector2Int(46, 42), new Vector2Int(46, 7), new Vector2Int(18, 7), new Vector2Int(18, 42), new Vector2Int(14, 32), new Vector2Int(5, 37) }, Hex("E0A64E"));
                    Rect(pixels, 20, 30, 44, 33, Hex("F6D286"));
                    break;
                case AvatarSprite.Brooch:
                    Ellipse(pixels, 42, 24, 9, 9, Hex("D8A64C"));
                    Ellipse(pixels, 42, 24, 4, 4, Hex("F4E5B5"));
                    break;
                case AvatarSprite.FlowerPin:
                    Ellipse(pixels, 42, 24, 5, 5, Hex("E985A1"));
                    Ellipse(pixels, 48, 24, 5, 5, Hex("F5C76B"));
                    Ellipse(pixels, 45, 29, 5, 5, Hex("D77B9B"));
                    Ellipse(pixels, 45, 24, 3, 3, Hex("F0D171"));
                    break;
                case AvatarSprite.Glasses:
                    Ring(pixels, 21, 34, 10, 8, 3, Hex("30363D"));
                    Ring(pixels, 43, 34, 10, 8, 3, Hex("30363D"));
                    Rect(pixels, 30, 33, 34, 36, Hex("30363D"));
                    break;
            }
        }

        static void Set(Color32[] pixels, int x, int y, Color color)
        {
            if (x < 0 || x >= 64 || y < 0 || y >= 64) return;
            Color32 c = color;
            pixels[y * 64 + x] = c;
        }

        static void Rect(Color32[] pixels, int x0, int y0, int x1, int y1, Color color)
        {
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(pixels, x, y, color);
        }

        static void Ellipse(Color32[] pixels, int cx, int cy, int rx, int ry, Color color)
        {
            for (int y = Mathf.Max(0, cy - ry); y <= Mathf.Min(63, cy + ry); y++)
                for (int x = Mathf.Max(0, cx - rx); x <= Mathf.Min(63, cx + rx); x++)
                {
                    float dx = (x - cx) / (float)rx;
                    float dy = (y - cy) / (float)ry;
                    if (dx * dx + dy * dy <= 1f) Set(pixels, x, y, color);
                }
        }

        static void Ring(Color32[] pixels, int cx, int cy, int rx, int ry, int thickness, Color color)
        {
            for (int y = cy - ry; y <= cy + ry; y++) for (int x = cx - rx; x <= cx + rx; x++)
            {
                float dx = (x - cx) / (float)rx, dy = (y - cy) / (float)ry;
                float distance = dx * dx + dy * dy;
                float innerX = Mathf.Max(1f, rx - thickness) / (float)rx;
                float innerY = Mathf.Max(1f, ry - thickness) / (float)ry;
                if (distance <= 1f && (dx * dx / (innerX * innerX) + dy * dy / (innerY * innerY) >= 1f)) Set(pixels, x, y, color);
            }
        }

        static void Polygon(Color32[] pixels, Vector2Int[] points, Color color)
        {
            int minX = 63, maxX = 0, minY = 63, maxY = 0;
            foreach (Vector2Int point in points) { minX = Mathf.Min(minX, point.x); maxX = Mathf.Max(maxX, point.x); minY = Mathf.Min(minY, point.y); maxY = Mathf.Max(maxY, point.y); }
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                bool inside = false;
                for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                {
                    Vector2Int a = points[i], b = points[j];
                    if (((a.y > y) != (b.y > y)) && (x < (b.x - a.x) * (y - a.y) / (float)(b.y - a.y + (b.y == a.y ? 1 : 0)) + a.x)) inside = !inside;
                }
                if (inside) Set(pixels, x, y, color);
            }
        }

        static void EnsureFolder(string parent, string name)
        {
            if (string.IsNullOrEmpty(name) || AssetDatabase.IsValidFolder(parent + "/" + name)) return;
            AssetDatabase.CreateFolder(parent, name);
        }

        static Color Hex(string value)
        {
            Color color;
            if (!value.StartsWith("#", StringComparison.Ordinal)) value = "#" + value;
            return ColorUtility.TryParseHtmlString(value, out color) ? color : Color.white;
        }
    }
}
