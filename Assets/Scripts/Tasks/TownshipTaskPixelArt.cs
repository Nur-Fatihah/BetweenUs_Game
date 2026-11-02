using System.Collections.Generic;
using UnityEngine;

namespace Crony
{
    /// <summary>Small point-filtered pixel sprites used by the interactive Township Park task panels.</summary>
    public static class TownshipTaskPixelArt
    {
        const int Size = 64;
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        static readonly Color32 Ink = new Color32(47, 43, 39, 255);

        public static Sprite NasiLeaf => Get("nasi-leaf");
        public static Sprite Lake => Get("lake");
        public static Sprite Corkboard => Get("corkboard");
        public static Sprite Pushpin => Get("pushpin");

        public static Sprite NasiIngredient(int index) => Get("nasi-" + Mathf.Clamp(index, 0, 3));
        public static Sprite LakeItem(int index) => Get("lake-item-" + Mathf.Clamp(index, 0, 2));
        public static Sprite Flyer(int index) => Get("flyer-" + Mathf.Clamp(index, 0, 2));

        static Sprite Get(string key)
        {
            if (Cache.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false)
            {
                name = "TownshipTaskPixel_" + key,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            Color32[] pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Clear;
            Draw(key, pixels);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), 64f, 0, SpriteMeshType.FullRect);
            sprite.name = "TownshipTaskPixel_" + key;
            Cache[key] = sprite;
            return sprite;
        }

        static void Draw(string key, Color32[] p)
        {
            if (key == "nasi-leaf") { DrawLeaf(p); return; }
            if (key == "lake") { DrawLake(p); return; }
            if (key == "corkboard") { DrawCorkboard(p); return; }
            if (key == "pushpin") { DrawPushpin(p); return; }
            if (key.StartsWith("nasi-")) { DrawNasi(p, int.Parse(key.Substring(5))); return; }
            if (key.StartsWith("lake-item-")) { DrawLakeItem(p, int.Parse(key.Substring(10))); return; }
            if (key.StartsWith("flyer-")) { DrawFlyer(p, int.Parse(key.Substring(6))); return; }
        }

        static void DrawLeaf(Color32[] p)
        {
            Color32 outline = C("173B2D"), green = C("347D4D"), light = C("72B466"), vein = C("C4D37A");
            Ellipse(p, 31, 31, 29, 24, outline);
            Ellipse(p, 31, 31, 27, 22, green);
            for (int x = 9; x <= 54; x++)
            {
                int y = 31 + (x - 31) / 4;
                Set(p, x, y, vein); Set(p, x, y + 1, light);
                if (x % 6 == 0)
                {
                    Line(p, x, y, Mathf.Max(7, x - 8), y + 6, light);
                    Line(p, x, y, Mathf.Min(56, x + 6), y - 7, light);
                }
            }
        }

        static void DrawLake(Color32[] p)
        {
            Rect(p, 0, 0, 63, 63, C("D8C18A"));
            Rect(p, 4, 5, 59, 58, C("347E91"));
            Rect(p, 7, 9, 56, 55, C("4BA0AA"));
            for (int y = 12; y < 54; y += 11)
            for (int x = 9 + ((y / 11) % 2) * 8; x < 55; x += 17)
            {
                Rect(p, x, y, x + 5, y + 1, C("9BD0C3"));
                Set(p, x + 6, y, C("9BD0C3"));
            }
            Rect(p, 4, 5, 59, 7, C("E8D79F"));
            Rect(p, 4, 56, 59, 58, C("E8D79F"));
        }

        static void DrawCorkboard(Color32[] p)
        {
            Rect(p, 2, 2, 61, 61, C("5B3826"));
            Rect(p, 6, 6, 57, 57, C("C58B58"));
            Rect(p, 8, 8, 55, 55, C("B7774A"));
            for (int y = 11; y < 54; y += 9)
                for (int x = 10 + ((y / 9) % 2) * 5; x < 55; x += 14)
                    Set(p, x, y, C("D79864"));
            Rect(p, 4, 4, 59, 6, C("E3BD7D"));
            Rect(p, 4, 58, 59, 60, C("8C5939"));
        }

        static void DrawNasi(Color32[] p, int index)
        {
            if (index == 0)
            {
                Ellipse(p, 31, 30, 22, 14, Ink); Ellipse(p, 31, 32, 18, 10, C("C64032"));
                Rect(p, 19, 29, 43, 31, C("EE7651"));
                for (int i = 0; i < 10; i++) Set(p, 19 + (i * 7) % 25, 25 + (i * 11) % 13, C("F4B05E"));
            }
            else if (index == 1)
            {
                Ellipse(p, 31, 31, 18, 24, Ink); Ellipse(p, 31, 32, 15, 21, C("FFF2D0"));
                Ellipse(p, 32, 33, 7, 8, C("EAB84D")); Set(p, 29, 37, C("FFF9E8"));
            }
            else if (index == 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    int y = 17 + i * 15;
                    Ellipse(p, 31, y, 20, 10, C("1F6341"));
                    Ellipse(p, 31, y, 16, 7, C("71B96E"));
                    Ellipse(p, 31, y, 5, 3, C("D2E69B"));
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    int x = 12 + i * 12;
                    Polygon(p, new[] { new Vector2Int(x - 5, 27 + i % 2 * 8), new Vector2Int(x + 4, 28 + i % 2 * 8), new Vector2Int(x + 9, 32 + i % 2 * 8), new Vector2Int(x + 2, 36 + i % 2 * 8), new Vector2Int(x - 6, 34 + i % 2 * 8) }, C("D9B782"));
                    Set(p, x - 6, 31 + i % 2 * 8, Ink);
                }
            }
        }

        static void DrawLakeItem(Color32[] p, int index)
        {
            if (index == 0)
            {
                Rect(p, 23, 15, 41, 52, C("D8F0E8"));
                Rect(p, 21, 17, 43, 49, C("6BBBC1"));
                Rect(p, 24, 13, 40, 17, C("D8F0E8"));
                Rect(p, 27, 10, 37, 14, C("D8F0E8"));
                Rect(p, 26, 21, 38, 24, C("B7E3DA"));
                Rect(p, 24, 48, 40, 52, C("487F89"));
                Rect(p, 27, 26, 29, 37, C("F0FFF5"));
            }
            else if (index == 1)
            {
                Rect(p, 18, 13, 45, 52, C("5C4033"));
                Rect(p, 16, 16, 47, 49, C("C45B4B"));
                Rect(p, 18, 19, 45, 46, C("E4A365"));
                Rect(p, 16, 18, 47, 22, C("F4D28B"));
                Rect(p, 16, 43, 47, 47, C("F4D28B"));
                for (int x = 21; x < 44; x += 7) { Set(p, x, 18, C("FFF0C4")); Set(p, x, 46, C("FFF0C4")); }
            }
            else
            {
                Ellipse(p, 31, 32, 7, 7, C("426B48"));
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    int x = 31 + Mathf.RoundToInt(Mathf.Cos(angle) * 17f);
                    int y = 31 + Mathf.RoundToInt(Mathf.Sin(angle) * 13f);
                    Ellipse(p, x, y, 12, 6, C(i % 2 == 0 ? "4C985F" : "70B86E"));
                    Line(p, 31, 31, x, y, C("B1D77C"));
                }
                Ellipse(p, 31, 31, 8, 7, C("E6D68E"));
                Ellipse(p, 31, 31, 4, 4, C("FFF1C3"));
            }
        }

        static void DrawFlyer(Color32[] p, int index)
        {
            Color32[] paper = { C("DDF0D9"), C("FFF0BB"), C("F3D6CE") };
            Color32[] accent = { C("4E8A70"), C("CE8A39"), C("B75D62") };
            Rect(p, 9, 5, 54, 59, C("67452F"));
            Rect(p, 7, 7, 52, 57, paper[index]);
            Rect(p, 11, 43, 48, 49, accent[index]);
            Rect(p, 12, 36, 42, 38, C("8C7761"));
            Rect(p, 12, 31, 46, 33, C("8C7761"));
            Rect(p, 12, 23, 46, 25, C("A69077"));
            Rect(p, 12, 17, 39, 19, C("A69077"));
            Rect(p, 13, 11, 46, 14, accent[index]);
            Ellipse(p, 48, 10, 4, 4, C("E1BA5D"));
        }

        static void DrawPushpin(Color32[] p)
        {
            Line(p, 31, 24, 31, 58, C("C8CFCC"));
            Line(p, 32, 26, 32, 56, C("87949A"));
            Ellipse(p, 31, 20, 15, 14, C("7D3937"));
            Ellipse(p, 29, 22, 11, 10, C("D95850"));
            Ellipse(p, 26, 26, 4, 4, C("F39A79"));
        }

        static Color32 C(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }

        static void Set(Color32[] p, int x, int y, Color32 color)
        {
            if (x >= 0 && x < Size && y >= 0 && y < Size) p[y * Size + x] = color;
        }

        static void Rect(Color32[] p, int x0, int y0, int x1, int y1, Color32 color)
        {
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(p, x, y, color);
        }

        static void Ellipse(Color32[] p, int cx, int cy, int rx, int ry, Color32 color)
        {
            for (int y = -ry; y <= ry; y++)
            for (int x = -rx; x <= rx; x++)
                if ((float)x * x / (rx * rx) + (float)y * y / (ry * ry) <= 1f) Set(p, cx + x, cy + y, color);
        }

        static void Line(Color32[] p, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                Set(p, x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int twice = error * 2;
                if (twice >= dy) { error += dy; x0 += sx; }
                if (twice <= dx) { error += dx; y0 += sy; }
            }
        }

        static void Polygon(Color32[] p, Vector2Int[] points, Color32 color)
        {
            int minY = Size, maxY = 0;
            foreach (Vector2Int point in points) { minY = Mathf.Min(minY, point.y); maxY = Mathf.Max(maxY, point.y); }
            for (int y = minY; y <= maxY; y++)
            {
                List<float> intersections = new List<float>();
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2Int a = points[i], b = points[(i + 1) % points.Length];
                    if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
                        intersections.Add(a.x + (y - a.y) * (float)(b.x - a.x) / (b.y - a.y));
                }
                intersections.Sort();
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                    for (int x = Mathf.CeilToInt(intersections[i]); x <= Mathf.FloorToInt(intersections[i + 1]); x++) Set(p, x, y, color);
            }
        }
    }
}
