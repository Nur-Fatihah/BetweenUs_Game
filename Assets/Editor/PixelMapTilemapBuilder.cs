using System;
using System.Collections.Generic;
using System.IO;
using Crony;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace Crony.Editor
{
    /// <summary>Creates the saved Township Park pixel world, its paths, blockers, shadows, and task bindings.</summary>
    public static class PixelMapTilemapBuilder
    {
        const string ScenePath = "Assets/Scenes/TownshipPark_Map.unity";
        const string TileArtFolder = "Assets/Art/World/Tiles";
        const string PropArtFolder = "Assets/Art/World/Props";
        const string TileDataFolder = "Assets/Data/WorldTiles";

        static readonly string[] StationTitles =
        {
            "Sort & Pack Nasi Lemak", "Restock Ice Cream Cabinet", "Fix Park Bench",
            "Calibrate Badminton Net", "File Community Permits", "Update Bus Schedule Board",
            "Clear Water Hyacinths & Trash", "Reset Wi-Fi Router & PA System",
            "Replace Solar Streetlight Batteries", "Pin Community Announcements"
        };

        static readonly Vector2[] StationPositions =
        {
            new Vector2(24f, 6.1f), new Vector2(24f, -.5f), new Vector2(-7f, -2.7f),
            new Vector2(7f, 17.8f), new Vector2(-7f, 17.8f), new Vector2(-7f, -21.5f),
            new Vector2(0f, 6.7f), new Vector2(-23f, 6.5f), new Vector2(-23f, -7.7f),
            new Vector2(7f, -21.2f)
        };

        static readonly Color[] MarkerColors =
        {
            Hex("E78358"), Hex("70BCCD"), Hex("C68D5E"), Hex("52A99A"), Hex("CF7181"),
            Hex("E4BF57"), Hex("559BAA"), Hex("777EB5"), Hex("8AAE70"), Hex("BD8A6A")
        };

        [MenuItem("Crony/Build Pixel Township Map")]
        public static void BuildAndSave()
        {
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BuildActiveScene();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("Township Park pixel world built and saved with H-layout roads, districts, colliders, shadow casters, canopy fades, and ten task bindings.");
        }

        public static void BuildActiveScene()
        {
            EnsureFolder("Assets", "Art");
            EnsureFolder("Assets/Art", "World");
            EnsureFolder("Assets/Art/World", "Tiles");
            EnsureFolder("Assets/Art/World", "Props");
            EnsureFolder("Assets", "Data");
            EnsureFolder("Assets/Data", "WorldTiles");

            Dictionary<PixelWorldAssetGenerator.TileKind, Tile> tileAssets = CreateTileAssets();
            Dictionary<PixelWorldAssetGenerator.PropKind, Sprite> propSprites = CreatePropAssets();

            GameObject oldMap = GameObject.Find("Pixel Township Map");
            if (oldMap != null) Undo.DestroyObjectImmediate(oldMap);
            GameObject mapRoot = new GameObject("Pixel Township Map");
            Undo.RegisterCreatedObjectUndo(mapRoot, "Build Pixel Township Map");
            Grid grid = mapRoot.AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            grid.cellGap = Vector3.zero;
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;

            Tilemap ground = MakeTilemap(mapRoot.transform, "Ground · 16px grass and terraces", -90);
            Tilemap water = MakeTilemap(mapRoot.transform, "Central Lake · water", -80);
            Tilemap roads = MakeTilemap(mapRoot.transform, "Tarmac Roads", -70);
            Tilemap walkways = MakeTilemap(mapRoot.transform, "Footpaths and Terraces", -69);
            Tilemap bridges = MakeTilemap(mapRoot.transform, "Narrow Wooden Bridges", -68);
            Tilemap markings = MakeTilemap(mapRoot.transform, "Lane Lines and Crosswalks", -60);
            Tilemap obstacles = MakeTilemap(mapRoot.transform, "Obstacle and Perimeter Walls", -40);
            Tilemap canopyTilemap = MakeTilemap(mapRoot.transform, "Overhead Canopy Layer", 40);
            canopyTilemap.gameObject.name = "Overhead Canopy Layer · sprite groups below";

            HashSet<Vector2Int> bridgeCells = BuildBridgeCells();
            HashSet<Vector2Int> roadCells = new HashSet<Vector2Int>();
            HashSet<Vector2Int> walkwayCells = new HashSet<Vector2Int>();
            HashSet<Vector2Int> lakeCells = new HashSet<Vector2Int>();
            Dictionary<Vector2Int, PixelWorldAssetGenerator.TileKind> wallCells = BuildWallCells();

            FillTerrain(ground, water, roads, walkways, bridges, markings, obstacles,
                tileAssets, roadCells, walkwayCells, lakeCells, bridgeCells, wallCells);

            ConfigureSurfaceTilemap(roads, SurfaceType.Concrete);
            ConfigureSurfaceTilemap(walkways, SurfaceType.Concrete);
            ConfigureSurfaceTilemap(bridges, SurfaceType.Wood);
            ConfigureWaterTilemap(water);
            ClearCoveredWaterCollision(water, roads, walkways, bridges);
            ConfigureWallTilemap(obstacles);

            GameObject worldProps = new GameObject("District Buildings and Street Props");
            worldProps.transform.SetParent(mapRoot.transform, false);
            GameObject canopyProps = new GameObject("Fading Tree and Roof Canopies");
            canopyProps.transform.SetParent(canopyTilemap.transform, false);
            PlaceDistrictProps(worldProps.transform, canopyProps.transform, propSprites);
            CreateWallShadowCasters(mapRoot.transform, wallCells, tileAssets);
            PlaceTaskStations(propSprites[PixelWorldAssetGenerator.PropKind.TaskMarker]);
            ConfigureCameraAndLighting();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        internal static Dictionary<PixelWorldAssetGenerator.TileKind, Tile> CreateTileAssets()
        {
            Dictionary<PixelWorldAssetGenerator.TileKind, Tile> assets = new Dictionary<PixelWorldAssetGenerator.TileKind, Tile>();
            foreach (PixelWorldAssetGenerator.TileKind kind in Enum.GetValues(typeof(PixelWorldAssetGenerator.TileKind)))
            {
                string key = kind.ToString();
                string pngPath = TileArtFolder + "/" + key + ".png";
                string diskPath = Path.Combine(Application.dataPath, "Art/World/Tiles/" + key + ".png");
                WriteTexture(pngPath, diskPath, PixelWorldAssetGenerator.CreateTileTexture(kind, 27 + (int)kind), new Vector2(.5f, .5f));
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
                string tilePath = TileDataFolder + "/" + key + ".asset";
                Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                tile.sprite = sprite;
                tile.colliderType = kind == PixelWorldAssetGenerator.TileKind.Water ||
                                    kind == PixelWorldAssetGenerator.TileKind.BrickWall ||
                                    kind == PixelWorldAssetGenerator.TileKind.Hedge
                    ? Tile.ColliderType.Grid
                    : Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
                assets[kind] = tile;
            }
            return assets;
        }

        static Dictionary<PixelWorldAssetGenerator.PropKind, Sprite> CreatePropAssets()
        {
            Dictionary<PixelWorldAssetGenerator.PropKind, Sprite> assets = new Dictionary<PixelWorldAssetGenerator.PropKind, Sprite>();
            foreach (PixelWorldAssetGenerator.PropKind kind in Enum.GetValues(typeof(PixelWorldAssetGenerator.PropKind)))
            {
                string key = kind.ToString();
                string pngPath = PropArtFolder + "/" + key + ".png";
                string diskPath = Path.Combine(Application.dataPath, "Art/World/Props/" + key + ".png");
                WriteTexture(pngPath, diskPath, PixelWorldAssetGenerator.CreatePropTexture(kind), new Vector2(.5f, 0f));
                assets[kind] = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
            }
            return assets;
        }

        internal static void WriteTexture(string assetPath, string diskPath, Texture2D texture, Vector2 pivot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(diskPath));
            File.WriteAllBytes(diskPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            importer.SetTextureSettings(settings);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelWorldAssetGenerator.PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        static Tilemap MakeTilemap(Transform parent, string name, int sortingOrder)
        {
            GameObject layer = new GameObject(name, typeof(GridLayout), typeof(Tilemap), typeof(TilemapRenderer));
            layer.transform.SetParent(parent, false);
            TilemapRenderer renderer = layer.GetComponent<TilemapRenderer>();
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = sortingOrder;
            renderer.mode = TilemapRenderer.Mode.Chunk;
            return layer.GetComponent<Tilemap>();
        }

        static void FillTerrain(Tilemap ground, Tilemap water, Tilemap roads, Tilemap walkways, Tilemap bridges,
            Tilemap markings, Tilemap obstacles, Dictionary<PixelWorldAssetGenerator.TileKind, Tile> tiles,
            HashSet<Vector2Int> roadCells, HashSet<Vector2Int> walkwayCells, HashSet<Vector2Int> lakeCells,
            HashSet<Vector2Int> bridgeCells, Dictionary<Vector2Int, PixelWorldAssetGenerator.TileKind> wallCells)
        {
            Tile grass = tiles[PixelWorldAssetGenerator.TileKind.Grass];
            Tile sand = tiles[PixelWorldAssetGenerator.TileKind.Sand];
            Tile waterTile = tiles[PixelWorldAssetGenerator.TileKind.Water];
            Tile asphalt = tiles[PixelWorldAssetGenerator.TileKind.Asphalt];
            Tile woodBridge = tiles[PixelWorldAssetGenerator.TileKind.WoodBridge];
            for (int y = -28; y <= 27; y++)
            for (int x = -33; x <= 33; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (!IsLand(x, y)) continue;
                bool insideLake = InLake(x, y);
                bool shore = InLake((x - .5f) * .83f, (y - .5f) * .83f) && !insideLake;
                ground.SetTile(new Vector3Int(x, y, 0), shore ? sand : grass);

                if (insideLake && !bridgeCells.Contains(cell) && !IsIslandCell(x, y))
                {
                    water.SetTile(new Vector3Int(x, y, 0), waterTile);
                    lakeCells.Add(cell);
                }

                if (IsRoadCell(x, y))
                {
                    roadCells.Add(cell);
                    roads.SetTile(new Vector3Int(x, y, 0), bridgeCells.Contains(cell) ? null : asphalt);
                }
                if (bridgeCells.Contains(cell)) bridges.SetTile(new Vector3Int(x, y, 0), woodBridge);

                if (IsWalkwayCell(x, y) && !IsRoadCell(x, y) && !bridgeCells.Contains(cell))
                {
                    walkwayCells.Add(cell);
                    PixelWorldAssetGenerator.TileKind tileKind = IsTerraceStairCell(x, y)
                        ? PixelWorldAssetGenerator.TileKind.TerraceSteps
                        : IsTerraceCell(x, y)
                            ? PixelWorldAssetGenerator.TileKind.StonePath
                            : PixelWorldAssetGenerator.TileKind.Sidewalk;
                    walkways.SetTile(new Vector3Int(x, y, 0), tiles[tileKind]);
                }
                else if (IsTerraceCell(x, y) && !IsRoadCell(x, y) && !insideLake && !IsIslandCell(x, y))
                {
                    ground.SetTile(new Vector3Int(x, y, 0), tiles[PixelWorldAssetGenerator.TileKind.StonePath]);
                }
            }

            PaintRoadMarkings(roads, markings, tiles, bridgeCells);

            foreach (KeyValuePair<Vector2Int, PixelWorldAssetGenerator.TileKind> entry in wallCells)
                obstacles.SetTile(new Vector3Int(entry.Key.x, entry.Key.y, 0), tiles[entry.Value]);
            for (int y = -28; y <= 27; y++)
            for (int x = -33; x <= 33; x++)
            {
                if (!IsLand(x, y)) continue;
                if (x == 0 && y <= -24 && y >= -26) continue; // Main gate opening.
                if (HasOutsideNeighbor(x, y))
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!wallCells.ContainsKey(cell))
                    {
                        wallCells[cell] = PixelWorldAssetGenerator.TileKind.BrickWall;
                        obstacles.SetTile(new Vector3Int(x, y, 0), tiles[PixelWorldAssetGenerator.TileKind.BrickWall]);
                    }
                }
            }

            // A small stone island interrupts the lake and creates a central meeting point.
            for (int y = -1; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
                if (IsIslandCell(x, y)) ground.SetTile(new Vector3Int(x, y, 0), tiles[PixelWorldAssetGenerator.TileKind.Grass]);

            // The narrow footbridge leads from the south gate to the central island.
            for (int y = -15; y <= -5; y++)
            {
                bridges.SetTile(new Vector3Int(0, y, 0), woodBridge);
                bridgeCells.Add(new Vector2Int(0, y));
            }
        }

        static void PaintRoadMarkings(Tilemap roads, Tilemap markings,
            Dictionary<PixelWorldAssetGenerator.TileKind, Tile> tiles, HashSet<Vector2Int> bridgeCells)
        {
            Tile horizontalLine = tiles[PixelWorldAssetGenerator.TileKind.DoubleYellowHorizontal];
            Tile verticalLine = tiles[PixelWorldAssetGenerator.TileKind.DoubleYellowVertical];
            Tile horizontalCrosswalk = tiles[PixelWorldAssetGenerator.TileKind.CrosswalkHorizontal];
            Tile verticalCrosswalk = tiles[PixelWorldAssetGenerator.TileKind.CrosswalkVertical];
            Tile curbH = tiles[PixelWorldAssetGenerator.TileKind.CurbHorizontal];
            Tile curbV = tiles[PixelWorldAssetGenerator.TileKind.CurbVertical];

            for (int x = -29; x <= 29; x++)
            for (int y = -25; y <= 25; y++)
            {
                if (IsRoadCell(x, y) && !bridgeCells.Contains(new Vector2Int(x, y)))
                {
                    bool crosswalkAtMajor = (Mathf.Abs(x + 21) <= 2 || Mathf.Abs(x - 21) <= 2) &&
                                            (Mathf.Abs(y - 17) <= 1 || Mathf.Abs(y + 18) <= 1);
                    bool crosswalkAtMiddle = (Mathf.Abs(x + 21) <= 1 || Mathf.Abs(x - 21) <= 1) && Mathf.Abs(y - 1) <= 2;
                    if (crosswalkAtMajor) markings.SetTile(new Vector3Int(x, y, 0), horizontalCrosswalk);
                    else if (crosswalkAtMiddle) markings.SetTile(new Vector3Int(x, y, 0), verticalCrosswalk);
                    else if (y == 17 || y == -18 || y == 1) markings.SetTile(new Vector3Int(x, y, 0), horizontalLine);
                    else if (x == -21 || x == 21) markings.SetTile(new Vector3Int(x, y, 0), verticalLine);
                }

                if (IsLand(x, y) && !IsRoadCell(x, y) && !InLake(x, y))
                {
                    if ((y == 15 || y == 19) && Mathf.Abs(x) <= 28 && IsRoadCell(x, y - (y == 15 ? -1 : 1)))
                        markings.SetTile(new Vector3Int(x, y, 0), curbH);
                    if ((x == -23 || x == -19 || x == 19 || x == 23) && Mathf.Abs(y) <= 23)
                        if (IsRoadCell(x - (x < 0 ? -1 : 1), y)) markings.SetTile(new Vector3Int(x, y, 0), curbV);
                }
            }
        }

        static void ConfigureSurfaceTilemap(Tilemap tilemap, SurfaceType surface)
        {
            AddComposite(tilemap.gameObject, true);
            SurfaceAudioMarker marker = tilemap.GetComponent<SurfaceAudioMarker>();
            if (marker == null) marker = tilemap.gameObject.AddComponent<SurfaceAudioMarker>();
            marker.SetSurface(surface);
        }

        static void ConfigureWaterTilemap(Tilemap tilemap)
        {
            AddComposite(tilemap.gameObject, false);
            ShadowCaster2D caster = tilemap.gameObject.GetComponent<ShadowCaster2D>();
            if (caster == null) caster = tilemap.gameObject.AddComponent<ShadowCaster2D>();
            caster.castsShadows = false;
        }

        static void ConfigureWallTilemap(Tilemap tilemap) => AddComposite(tilemap.gameObject, false);

        /// <summary>
        /// Water drawn under a road, path or bridge is walkable, the same rule the offline map uses
        /// (TownshipWorldMap.CanStand). Those cells get a twin of the water tile with the same art and no collider.
        /// </summary>
        public static int ClearCoveredWaterCollision(Tilemap water, params Tilemap[] covers)
        {
            Dictionary<TileBase, TileBase> walkable = new Dictionary<TileBase, TileBase>();
            int cleared = 0;
            foreach (Vector3Int cell in water.cellBounds.allPositionsWithin)
            {
                TileBase tile = water.GetTile(cell);
                if (tile == null || !Array.Exists(covers, cover => cover.HasTile(cell))) continue;
                if (!walkable.TryGetValue(tile, out TileBase twin)) walkable[tile] = twin = WalkableTwin(tile);
                if (twin == null || twin == tile) continue;
                water.SetTile(cell, twin);
                cleared++;
            }
            return cleared;
        }

        static TileBase WalkableTwin(TileBase tile)
        {
            string path = AssetDatabase.GetAssetPath(tile);
            if (string.IsNullOrEmpty(path)) return null;
            if (tile is AnimatedWaterTile animated)
            {
                if (animated.colliderType == Tile.ColliderType.None) return tile;
                return LoadOrCreateTwin(path, animated, twin => twin.colliderType = Tile.ColliderType.None);
            }
            if (tile is Tile plain)
            {
                if (plain.colliderType == Tile.ColliderType.None) return tile;
                return LoadOrCreateTwin(path, plain, twin => twin.colliderType = Tile.ColliderType.None);
            }
            return null;
        }

        static T LoadOrCreateTwin<T>(string sourcePath, T source, Action<T> configure) where T : TileBase
        {
            string twinPath = sourcePath.Substring(0, sourcePath.Length - ".asset".Length) + " Covered.asset";
            T twin = AssetDatabase.LoadAssetAtPath<T>(twinPath);
            if (twin != null) return twin;
            twin = UnityEngine.Object.Instantiate(source);
            configure(twin);
            AssetDatabase.CreateAsset(twin, twinPath);
            return twin;
        }

        static void AddComposite(GameObject target, bool trigger)
        {
            Rigidbody2D body = target.GetComponent<Rigidbody2D>();
            if (body == null) body = target.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            body.simulated = true;
            TilemapCollider2D tiles = target.GetComponent<TilemapCollider2D>();
            if (tiles == null) tiles = target.AddComponent<TilemapCollider2D>();
            tiles.compositeOperation = Collider2D.CompositeOperation.Merge;
            CompositeCollider2D composite = target.GetComponent<CompositeCollider2D>();
            if (composite == null) composite = target.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
            composite.isTrigger = trigger;
        }

        static void PlaceDistrictProps(Transform propRoot, Transform canopyRoot,
            Dictionary<PixelWorldAssetGenerator.PropKind, Sprite> sprites)
        {
            PlaceProp(propRoot, "Administrative Office", PixelWorldAssetGenerator.PropKind.AdministrativeOffice,
                new Vector2(-7f, 20.1f), sprites, new Vector2(4.2f, .9f), false, false, 28);
            PlaceProp(propRoot, "Dewan Orang Ramai · Community Hall", PixelWorldAssetGenerator.PropKind.CommunityHall,
                new Vector2(7f, 20.1f), sprites, new Vector2(5.6f, .9f), false, false, 28);
            PlaceProp(propRoot, "Badminton Court", PixelWorldAssetGenerator.PropKind.BadmintonCourt,
                new Vector2(7f, 14.4f), sprites, Vector2.zero, false, false, 16);
            PlaceProp(propRoot, "Hawker Stalls · Warung", PixelWorldAssetGenerator.PropKind.HawkerStall,
                new Vector2(24f, 8.3f), sprites, new Vector2(3.5f, .8f), true, true, 26);
            PlaceProp(propRoot, "Kedai Runcit · Convenience Store", PixelWorldAssetGenerator.PropKind.ConvenienceStore,
                new Vector2(24f, 1.8f), sprites, new Vector2(3.5f, .8f), true, true, 26);
            PlaceProp(propRoot, "Cellular Tower", PixelWorldAssetGenerator.PropKind.CellularTower,
                new Vector2(-24f, 8f), sprites, new Vector2(1.4f, .7f), false, false, 24);
            PlaceProp(propRoot, "Bus Stop Shelter", PixelWorldAssetGenerator.PropKind.BusShelter,
                new Vector2(-7f, -18.5f), sprites, new Vector2(3.6f, .7f), true, true, 24);
            PlaceProp(propRoot, "Central Lake Jetty", PixelWorldAssetGenerator.PropKind.LakeJetty,
                new Vector2(0f, 5.6f), sprites, Vector2.zero, false, false, 25);
            PlaceProp(propRoot, "Lake Water Hyacinths", PixelWorldAssetGenerator.PropKind.WaterHyacinths,
                new Vector2(-5.2f, 3.1f), sprites, Vector2.zero, false, false, -55);
            PlaceProp(propRoot, "Floating Bottle", PixelWorldAssetGenerator.PropKind.FloatingBottle,
                new Vector2(4.7f, 2.1f), sprites, Vector2.zero, false, false, -54);
            PlaceProp(propRoot, "Floating Can", PixelWorldAssetGenerator.PropKind.FloatingCan,
                new Vector2(5.4f, -2.1f), sprites, Vector2.zero, false, false, -54);
            PlaceProp(propRoot, "Notice Board", PixelWorldAssetGenerator.PropKind.NoticeBoard,
                new Vector2(7f, -18.4f), sprites, new Vector2(1.4f, .6f), false, false, 24);
            PlaceProp(propRoot, "Park Bench", PixelWorldAssetGenerator.PropKind.ParkBench,
                new Vector2(-7f, -1.1f), sprites, new Vector2(1.9f, .45f), false, false, 22);
            PlaceProp(propRoot, "Wi-Fi Router Cabinet", PixelWorldAssetGenerator.PropKind.RouterCabinet,
                new Vector2(-21.8f, 6.3f), sprites, new Vector2(.8f, .5f), false, false, 24);
            PlaceProp(propRoot, "Solar Streetlight", PixelWorldAssetGenerator.PropKind.SolarStreetlight,
                new Vector2(-24f, -5.2f), sprites, new Vector2(.7f, .5f), false, false, 24);
            PlaceProp(propRoot, "Nasi Lemak Steamer", PixelWorldAssetGenerator.PropKind.NasiLemakSteamer,
                new Vector2(25.6f, 8.3f), sprites, Vector2.zero, false, false, 27);
            PlaceProp(propRoot, "Ice Cream Freezer Chest", PixelWorldAssetGenerator.PropKind.IceCreamFreezer,
                new Vector2(25.6f, 1.8f), sprites, new Vector2(1.2f, .5f), false, false, 27);
            PlaceProp(propRoot, "Main Gate Solar Lamp", PixelWorldAssetGenerator.PropKind.StreetLamp,
                new Vector2(2.2f, -24.7f), sprites, new Vector2(.55f, .4f), false, true, 27);
            PlaceProp(propRoot, "Notice Board Solar Lamp", PixelWorldAssetGenerator.PropKind.StreetLamp,
                new Vector2(9.3f, -18.8f), sprites, new Vector2(.55f, .4f), false, true, 25);

            Vector2[] treePositions =
            {
                new Vector2(-28f, 13f), new Vector2(-26f, 7f), new Vector2(-28f, -3f), new Vector2(-26f, -12f),
                new Vector2(-17f, 13f), new Vector2(-17f, -12f), new Vector2(-12f, 9f), new Vector2(-12f, -9f),
                new Vector2(12f, 10f), new Vector2(16f, 8f), new Vector2(28f, 11f), new Vector2(28f, -7f),
                new Vector2(16f, -12f), new Vector2(27f, -12f), new Vector2(-11f, 18f), new Vector2(11f, 18f),
                new Vector2(-11f, -18f), new Vector2(12f, -18f), new Vector2(-5f, -10f), new Vector2(5f, -10f)
            };
            for (int i = 0; i < treePositions.Length; i++)
            {
                PixelWorldAssetGenerator.PropKind kind = i % 4 == 0 ? PixelWorldAssetGenerator.PropKind.PalmTree : PixelWorldAssetGenerator.PropKind.TropicalTree;
                CreateTree(canopyRoot, "Tropical Tree Canopy " + (i + 1), kind, treePositions[i], sprites[kind], i);
            }

            Vector2[] flowerBeds = { new Vector2(-13f, 2f), new Vector2(13f, -2f), new Vector2(-4f, 10f), new Vector2(5f, -10f) };
            for (int i = 0; i < flowerBeds.Length; i++)
                PlaceProp(propRoot, "Tropical Flower Bed " + (i + 1), PixelWorldAssetGenerator.PropKind.FlowerBed,
                    flowerBeds[i], sprites, Vector2.zero, false, false, 17);
        }

        static void PlaceProp(Transform parent, string name, PixelWorldAssetGenerator.PropKind kind, Vector2 position,
            Dictionary<PixelWorldAssetGenerator.PropKind, Sprite> sprites, Vector2 colliderSize,
            bool canopy, bool hasStreetLight, int sortingOrder)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(position.x, position.y, 0f);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprites[kind];
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = sortingOrder;
            if (colliderSize.sqrMagnitude > 0f)
            {
                BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
                collider.size = colliderSize;
                collider.offset = new Vector2(0f, Mathf.Max(.25f, colliderSize.y * .5f));
            }
            AddShadowCaster(go);
            if (hasStreetLight) AddWarmStreetLight(go.transform);
            if (canopy) CreateCanopyZone(go.transform, renderer, new Vector2(4.4f, 3.5f));
        }

        static void CreateTree(Transform parent, string name, PixelWorldAssetGenerator.PropKind kind,
            Vector2 position, Sprite sprite, int index)
        {
            GameObject tree = new GameObject(name);
            tree.transform.SetParent(parent, false);
            tree.transform.position = new Vector3(position.x, position.y, 0f);
            SpriteRenderer renderer = tree.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = 31 + (index % 5);
            if (kind == PixelWorldAssetGenerator.PropKind.PalmTree) renderer.color = Hex("D8F0D0");
            BoxCollider2D trunk = tree.AddComponent<BoxCollider2D>();
            trunk.size = new Vector2(.55f, .65f);
            trunk.offset = new Vector2(0f, .4f);
            AddShadowCaster(tree);
            CreateCanopyZone(tree.transform, renderer, kind == PixelWorldAssetGenerator.PropKind.PalmTree
                ? new Vector2(3.6f, 3.4f) : new Vector2(3.2f, 3.2f));
        }

        static void CreateCanopyZone(Transform parent, SpriteRenderer canopy, Vector2 size)
        {
            GameObject zone = new GameObject("Canopy Fade Trigger");
            zone.transform.SetParent(parent, false);
            zone.transform.localPosition = new Vector3(0f, .75f, 0f);
            BoxCollider2D trigger = zone.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = size;
            CanopyFadeZone2D fade = zone.AddComponent<CanopyFadeZone2D>();
            SerializedObject data = new SerializedObject(fade);
            SerializedProperty renderers = data.FindProperty("canopyRenderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = canopy;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddShadowCaster(GameObject target)
        {
            ShadowCaster2D caster = target.GetComponent<ShadowCaster2D>();
            if (caster == null) caster = target.AddComponent<ShadowCaster2D>();
            caster.castsShadows = true;
            caster.selfShadows = false;
        }

        static void CreateWallShadowCasters(Transform parent,
            Dictionary<Vector2Int, PixelWorldAssetGenerator.TileKind> wallCells,
            Dictionary<PixelWorldAssetGenerator.TileKind, Tile> tiles)
        {
            GameObject group = new GameObject("Wall and Hedge Shadow Casters");
            group.transform.SetParent(parent, false);
            HashSet<Vector2Int> remaining = new HashSet<Vector2Int>(wallCells.Keys);
            foreach (Vector2Int start in wallCells.Keys)
            {
                if (!remaining.Contains(start)) continue;
                PixelWorldAssetGenerator.TileKind kind = wallCells[start];
                Vector2Int direction = remaining.Contains(start + Vector2Int.right) ? Vector2Int.right : Vector2Int.up;
                if (!remaining.Contains(start + direction)) direction = Vector2Int.zero;
                List<Vector2Int> run = new List<Vector2Int> { start };
                remaining.Remove(start);
                if (direction != Vector2Int.zero)
                {
                    Vector2Int next = start + direction;
                    while (remaining.Contains(next) && wallCells[next] == kind)
                    {
                        run.Add(next);
                        remaining.Remove(next);
                        next += direction;
                    }
                }
                GameObject casterObject = new GameObject("Shadow · " + kind + " · " + run.Count);
                casterObject.transform.SetParent(group.transform, false);
                float centerX = 0f, centerY = 0f;
                for (int i = 0; i < run.Count; i++) { centerX += run[i].x; centerY += run[i].y; }
                casterObject.transform.position = new Vector3(centerX / run.Count, centerY / run.Count, 0f);
                casterObject.transform.localScale = direction == Vector2Int.up
                    ? new Vector3(1f, run.Count, 1f)
                    : new Vector3(run.Count, 1f, 1f);
                SpriteRenderer sprite = casterObject.AddComponent<SpriteRenderer>();
                sprite.sprite = tiles[kind].sprite;
                sprite.sortingLayerName = "Default";
                sprite.sortingOrder = -39;
                sprite.enabled = false;
                AddShadowCaster(casterObject);
            }
        }

        static void PlaceTaskStations(Sprite taskMarker)
        {
            TownshipTaskMiniGameUI ui = UnityEngine.Object.FindAnyObjectByType<TownshipTaskMiniGameUI>(FindObjectsInactive.Include);
            if (ui == null)
            {
                GameObject canvas = GameObject.Find("Township Task UI Canvas");
                if (canvas == null) canvas = new GameObject("Township Task UI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Canvas canvasComponent = canvas.GetComponent<Canvas>();
                canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster == null) raycaster = canvas.AddComponent<GraphicRaycaster>();
                raycaster.enabled = true;
                ui = canvas.GetComponent<TownshipTaskMiniGameUI>();
                if (ui == null) ui = canvas.AddComponent<TownshipTaskMiniGameUI>();
            }

            GameObject[] stations = new GameObject[StationTitles.Length];
            for (int i = 0; i < StationTitles.Length; i++)
            {
                GameObject stationObject = GameObject.Find("Task Station - " + StationTitles[i]);
                if (stationObject == null)
                {
                    Debug.LogError("Missing task station: " + StationTitles[i]);
                    continue;
                }
                stations[i] = stationObject;
                stationObject.transform.position = StationPositions[i];
                TownshipTaskStation station = stationObject.GetComponent<TownshipTaskStation>();
                if (station == null) station = stationObject.AddComponent<TownshipTaskStation>();
                BoxCollider2D trigger = stationObject.GetComponent<BoxCollider2D>();
                if (trigger == null) trigger = stationObject.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                trigger.enabled = true;
                trigger.offset = Vector2.zero;
                trigger.size = new Vector2(3.4f, 3.4f);
                station.BindMiniGameUI(ui);
                SerializedObject stationData = new SerializedObject(station);
                SerializedProperty uiReference = stationData.FindProperty("miniGameUI");
                if (uiReference != null) uiReference.objectReferenceValue = ui;
                stationData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(stationObject);
                PlaceStationMarker(stationObject.transform, i, taskMarker);
            }

            SerializedObject uiData = new SerializedObject(ui);
            SerializedProperty stationArray = uiData.FindProperty("sceneStations");
            if (stationArray != null)
            {
                stationArray.arraySize = stations.Length;
                for (int i = 0; i < stations.Length; i++)
                    stationArray.GetArrayElementAtIndex(i).objectReferenceValue = stations[i] != null ? stations[i].GetComponent<TownshipTaskStation>() : null;
                uiData.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(ui);

            EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null) eventSystem = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            eventSystem.gameObject.SetActive(true);
            if (eventSystem.GetComponent<BaseInputModule>() == null)
                eventSystem.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
        }

        static void PlaceStationMarker(Transform station, int index, Sprite taskMarker)
        {
            Transform existing = station.Find("Pixel Task Marker");
            GameObject markerObject = existing != null ? existing.gameObject : new GameObject("Pixel Task Marker");
            markerObject.transform.SetParent(station, false);
            markerObject.transform.localPosition = new Vector3(0f, .48f, 0f);
            markerObject.transform.localScale = new Vector3(.48f, .48f, 1f);
            SpriteRenderer renderer = markerObject.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = markerObject.AddComponent<SpriteRenderer>();
            renderer.sprite = taskMarker;
            renderer.color = MarkerColors[index];
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = 50;
        }

        static void ConfigureCameraAndLighting()
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.orthographic = true;
                camera.orthographicSize = CameraFollow2D.GameplayOrthographicSize;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.backgroundColor = Hex("172B30");
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.allowDynamicResolution = false;
                CameraFollow2D follow = camera.GetComponent<CameraFollow2D>();
                if (follow == null) follow = camera.gameObject.AddComponent<CameraFollow2D>();
                SerializedObject followData = new SerializedObject(follow);
                followData.FindProperty("assetsPixelsPerUnit").intValue = PixelWorldAssetGenerator.PixelsPerUnit;
                followData.ApplyModifiedPropertiesWithoutUndo();
                PixelPerfectCamera pixelPerfect = camera.GetComponent<PixelPerfectCamera>();
                if (pixelPerfect == null) pixelPerfect = camera.gameObject.AddComponent<PixelPerfectCamera>();
                pixelPerfect.assetsPPU = PixelWorldAssetGenerator.PixelsPerUnit;
                pixelPerfect.refResolutionX = CameraFollow2D.ViewWidthPixels;
                pixelPerfect.refResolutionY = CameraFollow2D.ViewHeightPixels;
                pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.None;
                pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
                EditorUtility.SetDirty(camera.gameObject);
            }

            QualitySettings.antiAliasing = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            Light2D global = null;
            foreach (Light2D light in UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsInactive.Include))
                if (light.lightType == Light2D.LightType.Global) { global = light; break; }
            if (global == null) global = new GameObject("Township Global 2D Light").AddComponent<Light2D>();
            global.gameObject.name = "Township Ambient · Shadow Friendly";
            global.lightType = Light2D.LightType.Global;
            global.intensity = .13f;
            global.shadowsEnabled = false;
            EditorUtility.SetDirty(global);
        }

        static void AddWarmStreetLight(Transform parent)
        {
            GameObject lightObject = new GameObject("Warm pixel streetlight pool");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = new Vector3(0f, 1f, 0f);
            Light2D light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = Hex("FFD995");
            light.intensity = .42f;
            light.pointLightInnerRadius = .15f;
            light.pointLightOuterRadius = 2.6f;
            light.shadowsEnabled = true;
        }

        static Dictionary<Vector2Int, PixelWorldAssetGenerator.TileKind> BuildWallCells()
        {
            Dictionary<Vector2Int, PixelWorldAssetGenerator.TileKind> cells = new Dictionary<Vector2Int, PixelWorldAssetGenerator.TileKind>();
            AddHorizontal(cells, -28, -16, 10, PixelWorldAssetGenerator.TileKind.Hedge, -21, 2);
            AddHorizontal(cells, 16, 28, 10, PixelWorldAssetGenerator.TileKind.Hedge, 21, 2);
            AddHorizontal(cells, -28, -16, -9, PixelWorldAssetGenerator.TileKind.Hedge, -21, 2);
            AddHorizontal(cells, 16, 28, -9, PixelWorldAssetGenerator.TileKind.Hedge, 21, 2);
            AddVertical(cells, -11, -6, 6, PixelWorldAssetGenerator.TileKind.Hedge, -3, 1);
            AddVertical(cells, 11, -6, 6, PixelWorldAssetGenerator.TileKind.Hedge, 3, 1);
            AddHorizontal(cells, -9, -3, 7, PixelWorldAssetGenerator.TileKind.Hedge, -6, 1);
            AddHorizontal(cells, 3, 9, -7, PixelWorldAssetGenerator.TileKind.Hedge, 6, 1);
            AddVertical(cells, 0, 16, 23, PixelWorldAssetGenerator.TileKind.BrickWall, 20, 1);
            AddVertical(cells, 0, -23, -15, PixelWorldAssetGenerator.TileKind.Hedge, -18, 1);

            Vector2Int[] exclusions =
            {
                new Vector2Int(24, 6), new Vector2Int(24, 0), new Vector2Int(-7, -3), new Vector2Int(7, 18),
                new Vector2Int(-7, 18), new Vector2Int(-7, -22), new Vector2Int(0, 7), new Vector2Int(-23, 7),
                new Vector2Int(-23, -8), new Vector2Int(7, -21)
            };
            for (int i = 0; i < exclusions.Length; i++)
            {
                Vector2Int center = exclusions[i];
                for (int y = center.y - 2; y <= center.y + 2; y++)
                    for (int x = center.x - 2; x <= center.x + 2; x++) cells.Remove(new Vector2Int(x, y));
            }
            return cells;
        }

        static void AddHorizontal(Dictionary<Vector2Int, PixelWorldAssetGenerator.TileKind> cells,
            int minX, int maxX, int y, PixelWorldAssetGenerator.TileKind kind, int gapX = int.MaxValue, int gapWidth = 0)
        {
            for (int x = minX; x <= maxX; x++)
                if (Mathf.Abs(x - gapX) > gapWidth && IsLand(x, y) && !IsRoadCell(x, y) && !IsBridgeCell(x, y))
                    cells[new Vector2Int(x, y)] = kind;
        }

        static void AddVertical(Dictionary<Vector2Int, PixelWorldAssetGenerator.TileKind> cells,
            int x, int minY, int maxY, PixelWorldAssetGenerator.TileKind kind, int gapY = int.MaxValue, int gapWidth = 0)
        {
            for (int y = minY; y <= maxY; y++)
                if (Mathf.Abs(y - gapY) > gapWidth && IsLand(x, y) && !IsRoadCell(x, y) && !IsBridgeCell(x, y))
                    cells[new Vector2Int(x, y)] = kind;
        }

        static Vector2Int[] MakeHorizontal(int minX, int maxX, int y, int gapX, int gapWidth)
        {
            List<Vector2Int> points = new List<Vector2Int>();
            for (int x = minX; x <= maxX; x++) if (Mathf.Abs(x - gapX) > gapWidth) points.Add(new Vector2Int(x, y));
            return points.ToArray();
        }

        static HashSet<Vector2Int> BuildBridgeCells()
        {
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            for (int x = -8; x <= 8; x++)
            {
                cells.Add(new Vector2Int(x, 0));
                cells.Add(new Vector2Int(x, 1));
            }
            for (int y = -4; y <= 6; y++) cells.Add(new Vector2Int(0, y));
            return cells;
        }

        static bool IsLand(int x, int y)
        {
            bool northWing = y >= 15 && y <= 24 && Mathf.Abs(x) <= (y == 24 ? 25 : 29);
            bool southWing = y >= -24 && y <= -15 && Mathf.Abs(x) <= (y == -24 ? 24 : 29);
            bool westWing = x >= -29 && x <= -15 && y >= -17 && y <= 16;
            bool eastWing = x >= 15 && x <= 29 && y >= -17 && y <= 16;
            bool centralPark = Mathf.Abs(x) <= (Mathf.Abs(y - 1) > 9 ? 10 : 14) && y >= -9 && y <= 13;
            bool northConnector = Mathf.Abs(x) <= 8 && y > 13 && y < 16;
            bool southConnector = Mathf.Abs(x) <= 5 && y > -15 && y < -9;
            bool gateApron = Mathf.Abs(x) <= 4 && y >= -27 && y < -24;
            return northWing || southWing || westWing || eastWing || centralPark || northConnector || southConnector || gateApron;
        }

        static bool IsRoadCell(int x, int y)
        {
            bool north = y >= 16 && y <= 18 && Mathf.Abs(x) <= 28;
            bool south = y >= -19 && y <= -17 && Mathf.Abs(x) <= 28;
            bool west = x >= -22 && x <= -20 && y >= -22 && y <= 19;
            bool east = x >= 20 && x <= 22 && y >= -22 && y <= 19;
            bool centralCrossbar = y >= 0 && y <= 2 && Mathf.Abs(x) <= 22;
            return IsLand(x, y) && (north || south || west || east || centralCrossbar);
        }

        static bool IsWalkwayCell(int x, int y)
        {
            bool innerNorth = y >= 12 && y <= 15 && Mathf.Abs(x) <= 10;
            bool innerSouth = y >= -15 && y <= -12 && Mathf.Abs(x) <= 6;
            bool lakeNorthJetty = x >= -1 && x <= 1 && y >= 3 && y <= 9;
            bool westAlley = x >= -15 && x <= -12 && y >= -8 && y <= 13;
            bool eastAlley = x >= 12 && x <= 15 && y >= -8 && y <= 13;
            bool marketConnector = x >= 22 && x <= 26 && y >= 4 && y <= 9;
            bool eastShopConnector = x >= 22 && x <= 26 && y >= -2 && y <= 2;
            bool westConnector = x >= -26 && x <= -22 && y >= 4 && y <= 9;
            bool solarConnector = x >= -26 && x <= -22 && y >= -10 && y <= -5;
            bool southernGate = Mathf.Abs(x) <= 1 && y >= -27 && y <= -19;
            bool busConnector = x >= -9 && x <= -5 && y >= -22 && y <= -18;
            bool noticeConnector = x >= 5 && x <= 9 && y >= -22 && y <= -18;
            bool lakeRing = InLake((x - .5f) * .89f, (y - .5f) * .89f) && !InLake(x, y) && Mathf.Abs(x) <= 11;
            return IsLand(x, y) && (innerNorth || innerSouth || lakeNorthJetty || westAlley || eastAlley || marketConnector || eastShopConnector || westConnector || solarConnector || busConnector || noticeConnector || southernGate || lakeRing);
        }

        static bool IsTerraceCell(int x, int y)
        {
            bool northTerrace = y >= 19 && y <= 22 && Mathf.Abs(x) <= 12;
            bool southPlaza = y >= -23 && y <= -21 && Mathf.Abs(x) <= 12;
            bool hallCourt = y >= 13 && y <= 15 && x >= 3 && x <= 11;
            return northTerrace || southPlaza || hallCourt;
        }

        static bool IsTerraceStairCell(int x, int y) =>
            (Mathf.Abs(x + 7) <= 1 || Mathf.Abs(x - 7) <= 1) && y >= 12 && y <= 15;

        static bool InLake(float x, float y)
        {
            float dx = x / 7.1f, dy = (y - 1f) / 5.2f;
            return dx * dx + dy * dy <= 1f;
        }

        static bool IsIslandCell(int x, int y) => Mathf.Abs(x) <= 2 && y >= -1 && y <= 2;

        static bool IsBridgeCell(int x, int y)
        {
            bool horizontal = Mathf.Abs(y) <= 1 && Mathf.Abs(x) <= 8;
            bool vertical = x == 0 && y >= -4 && y <= 6;
            return horizontal || vertical;
        }

        static bool HasOutsideNeighbor(int x, int y) =>
            !IsLand(x + 1, y) || !IsLand(x - 1, y) || !IsLand(x, y + 1) || !IsLand(x, y - 1);

        static void EnsureFolder(string parent, string child)
        {
            if (string.IsNullOrEmpty(child)) return;
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}
