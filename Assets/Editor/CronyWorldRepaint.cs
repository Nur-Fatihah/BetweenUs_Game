using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Crony.Editor
{
    /// <summary>
    /// Re-bakes every world tile from PixelWorldAssetGenerator's warm palette in place (same files, same GUIDs), then
    /// scatters grass variants and swaps the lake to the animated caustic tile. Layout and walkability are unchanged:
    /// the same tilemaps keep a tile in the same cells.
    /// </summary>
    public static class CronyWorldRepaint
    {
        const string ArtFolder = "Assets/Art/World/Tiles", DataFolder = "Assets/Data/WorldTiles";

        [MenuItem("Crony/Rendering/Repaint World Tiles")]
        public static void Run()
        {
            Dictionary<PixelWorldAssetGenerator.TileKind, Tile> tiles = PixelMapTilemapBuilder.CreateTileAssets();
            Tile grass = tiles[PixelWorldAssetGenerator.TileKind.Grass];
            Tile water = tiles[PixelWorldAssetGenerator.TileKind.Water];
            TileBase[] grassVariants = { grass, BakeTile("GrassVariant1", PixelWorldAssetGenerator.TileKind.Grass, 1), BakeTile("GrassVariant2", PixelWorldAssetGenerator.TileKind.Grass, 2), BakeTile("GrassVariant3", PixelWorldAssetGenerator.TileKind.Grass, 3) };
            AnimatedWaterTile lake = BakeWater();

            int grassCells = 0, waterCells = 0;
            GameObject map = GameObject.Find("Pixel Township Map");
            if (map != null)
                foreach (Tilemap tilemap in map.GetComponentsInChildren<Tilemap>(true))
                {
                    BoundsInt bounds = tilemap.cellBounds;
                    foreach (Vector3Int cell in bounds.allPositionsWithin)
                    {
                        TileBase current = tilemap.GetTile(cell);
                        if (current == null) continue;
                        if (System.Array.IndexOf(grassVariants, current) >= 0)
                        {
                            // A stable hash keeps the same pattern every time the tool runs.
                            int pick = ((cell.x * 73856093) ^ (cell.y * 19349663)) & 0x7fffffff;
                            tilemap.SetTile(cell, grassVariants[pick % 7 < 4 ? 0 : 1 + pick % 3]);
                            grassCells++;
                        }
                        else if (current == water || current == lake) { tilemap.SetTile(cell, lake); waterCells++; }
                    }
                    EditorUtility.SetDirty(tilemap);
                }
            AssetDatabase.SaveAssets();
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("World repainted: " + grassCells + " grass cells varied, " + waterCells + " lake cells animated.");
        }

        static Tile BakeTile(string name, PixelWorldAssetGenerator.TileKind kind, int variant)
        {
            Sprite sprite = BakeSprite(name, kind, variant);
            string path = DataFolder + "/" + name + ".asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, path); }
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        static AnimatedWaterTile BakeWater()
        {
            Sprite[] frames = new Sprite[4];
            for (int i = 0; i < frames.Length; i++) frames[i] = BakeSprite("WaterFrame" + i, PixelWorldAssetGenerator.TileKind.Water, i);
            string path = DataFolder + "/WaterAnimated.asset";
            AnimatedWaterTile tile = AssetDatabase.LoadAssetAtPath<AnimatedWaterTile>(path);
            if (tile == null) { tile = ScriptableObject.CreateInstance<AnimatedWaterTile>(); AssetDatabase.CreateAsset(tile, path); }
            tile.frames = frames;
            tile.framesPerSecond = 2.5f;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        static Sprite BakeSprite(string name, PixelWorldAssetGenerator.TileKind kind, int variant)
        {
            string assetPath = ArtFolder + "/" + name + ".png";
            string diskPath = Path.Combine(Application.dataPath, "Art/World/Tiles/" + name + ".png");
            PixelMapTilemapBuilder.WriteTexture(assetPath, diskPath, PixelWorldAssetGenerator.CreateTileTexture(kind, 27 + (int)kind, variant), new Vector2(.5f, .5f));
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }
    }
}
