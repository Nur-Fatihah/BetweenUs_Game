using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>
    /// The CRONY wordmark drawn from hand-made block glyphs (2-pixel strokes, 3-pixel gaps so a sliver of background shows between outlined letters) and
    /// rendered with point filtering, so every edge stays sharp at any size. The C is open on its whole right side,
    /// so it can't be read as an O or a G.
    /// </summary>
    public static class PixelLogo
    {
        const int GlyphWidth = 6, GlyphHeight = 7, LetterGap = 3, Pad = 2;

        static readonly string[][] Glyphs =
        {
            new[] { ".#####", "##....", "##....", "##....", "##....", "##....", ".#####" }, // C
            new[] { "#####.", "##..##", "##..##", "#####.", "##.##.", "##..##", "##..##" }, // R
            new[] { ".####.", "##..##", "##..##", "##..##", "##..##", "##..##", ".####." }, // O
            new[] { "##..##", "###.##", "######", "##.###", "##..##", "##..##", "##..##" }, // N
            new[] { "##..##", "##..##", ".####.", "..##..", "..##..", "..##..", "..##.." }, // Y
        };

        static Sprite sprite;

        /// <summary>Adds the wordmark to <paramref name="parent"/>, <paramref name="pixelScale"/> UI units per logo pixel.</summary>
        public static RectTransform Create(Transform parent, int pixelScale, Vector2 position)
        {
            Sprite logo = GetSprite();
            GameObject go = new GameObject("CRONY Pixel Logo", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(logo.rect.width, logo.rect.height) * pixelScale;
            rect.anchoredPosition = position;
            Image image = go.GetComponent<Image>();
            image.sprite = logo;
            image.raycastTarget = false;
            return rect;
        }

        static Sprite GetSprite()
        {
            if (sprite != null) return sprite;
            int textWidth = Glyphs.Length * GlyphWidth + (Glyphs.Length - 1) * LetterGap;
            // Room for a 1-pixel ink outline all round and a 1-pixel drop shadow below-right.
            int width = textWidth + Pad * 2 + 1, height = GlyphHeight + Pad * 2 + 1;
            bool[,] fill = new bool[width, height];
            for (int g = 0; g < Glyphs.Length; g++)
                for (int row = 0; row < GlyphHeight; row++)
                    for (int col = 0; col < GlyphWidth; col++)
                        if (Glyphs[g][row][col] == '#')
                            fill[Pad + g * (GlyphWidth + LetterGap) + col, Pad + 1 + (GlyphHeight - 1 - row)] = true;

            Color clear = new Color(0f, 0f, 0f, 0f);
            Color ink = PixelUiTheme.Chocolate;
            Color shadow = new Color(.17f, .2f, .27f, .55f);
            Color top = PixelUiTheme.Highlight, bottom = PixelUiTheme.Amber;
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (fill[x, y])
                    {
                        // Two-tone face: a cream upper half over amber, the hi-bit way of shading a block letter.
                        int row = y - Pad - 1;
                        pixels[x + y * width] = row >= GlyphHeight / 2 ? top : bottom;
                    }
                    else if (Touches(fill, x, y, width, height)) pixels[x + y * width] = ink;
                }
            // Shadow last, only where nothing else was drawn.
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (pixels[x + y * width].a == 0f && x > 0 && y < height - 1 && pixels[(x - 1) + (y + 1) * width] == ink)
                        pixels[x + y * width] = shadow;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "CRONY Logo",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 16f);
            sprite.name = "CRONY Logo";
            return sprite;
        }

        /// <summary>
        /// Square app icon: the wordmark's C on a forest-green tile, drawn on a 24-pixel grid and scaled up with hard
        /// edges. Readable, so the editor can save it as the build icon.
        /// </summary>
        public static Texture2D RenderIcon(int size)
        {
            const int grid = 24;
            string[] c = Glyphs[0];
            Color tile = PixelUiTheme.Forest, ink = PixelUiTheme.Chocolate;
            Color[,] cells = new Color[grid, grid];
            bool[,] fill = new bool[grid, grid];
            // A 2x-scaled 6x7 glyph is 12x14; centre it on the tile with room for its outline.
            int left = (grid - GlyphWidth * 2) / 2, bottom = (grid - GlyphHeight * 2) / 2;
            for (int row = 0; row < GlyphHeight; row++)
                for (int col = 0; col < GlyphWidth; col++)
                    if (c[row][col] == '#')
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                                fill[left + col * 2 + dx, bottom + (GlyphHeight - 1 - row) * 2 + dy] = true;
            for (int y = 0; y < grid; y++)
                for (int x = 0; x < grid; x++)
                {
                    bool edge = x == 0 || y == 0 || x == grid - 1 || y == grid - 1;
                    cells[x, y] = fill[x, y] ? (y >= bottom + GlyphHeight ? PixelUiTheme.Highlight : PixelUiTheme.Amber)
                        : Touches(fill, x, y, grid, grid) || edge ? ink : tile;
                }
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Crony Icon", filterMode = FilterMode.Point };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[x + y * size] = cells[x * grid / size, y * grid / size];
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static bool Touches(bool[,] fill, int x, int y, int width, int height)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < width && ny < height && fill[nx, ny]) return true;
                }
            return false;
        }
    }
}
