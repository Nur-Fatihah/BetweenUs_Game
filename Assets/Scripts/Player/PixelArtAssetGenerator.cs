using System.Collections.Generic;
using UnityEngine;

namespace Crony
{
    /// <summary>Runtime-safe 16x32 point-filtered pixel art generator for character layers and 24x24 zoomed item card icons.</summary>
    public static class PixelArtAssetGenerator
    {
        public const int Width = 16;
        public const int Height = 32;
        public const int IconSize = 24;
        public const float PixelsPerUnit = 32f;

        static readonly Color32 Outline = new Color32(42, 48, 59, 255);
        static readonly Color32 Fill = new Color32(255, 255, 255, 255);
        static readonly Color32 Shade = new Color32(214, 222, 232, 255);
        static readonly Color32 Highlight = new Color32(235, 242, 248, 255);
        static readonly Color32 GoldTrim = new Color32(242, 198, 92, 255);
        static readonly Dictionary<string, Sprite> runtimeSprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> cardIconSprites = new Dictionary<string, Sprite>();
        static Sprite shadowSprite;

        static int ResolveSeed(string itemID, int variantSeed)
        {
            if (variantSeed != 0 || string.IsNullOrEmpty(itemID)) return variantSeed;
            string id = itemID.ToLowerInvariant();
            if (id.Contains("blush")) return 2;
            if (id.Contains("wink")) return 1;
            if (id.Contains("flower") || id.Contains("long") || id.Contains("songkok") || id.Contains("baju") || id.Contains("traditional") || id.Contains("sandal") || id.Contains("glass")) return 1;
            if (id.Contains("curl") || id.Contains("tudung") || id.Contains("cheong") || id.Contains("skirt") || id.Contains("boot") || id.Contains("bag")) return 2;
            if (id.Contains("cap") || id.Contains("saree") || id.Contains("badge")) return 3;
            if (id.Contains("festive") || id.Contains("jacket")) return 4;
            return 0;
        }

        public static Texture2D CreateTexture(AvatarItemCategory category, string itemID, int variantSeed = 0)
        {
            int resolvedSeed = ResolveSeed(itemID, variantSeed);
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
            {
                name = "PixelArt_" + category + "_" + (string.IsNullOrEmpty(itemID) ? "Fallback" : itemID),
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                mipMapBias = 0f
            };
            Color32[] pixels = new Color32[Width * Height];
            DrawItem(pixels, category, itemID ?? string.Empty, resolvedSeed);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        public static Sprite CreateSprite(AvatarItemCategory category, string itemID, int variantSeed = 0)
        {
            Texture2D texture = CreateTexture(category, itemID, variantSeed);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, Width, Height), new Vector2(.5f, 0f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = "PixelArt_" + category + "_" + (string.IsNullOrEmpty(itemID) ? "Fallback" : itemID);
            return sprite;
        }

        /// <summary>Returns the distinct 16x32 runtime layer sprite for a character part.</summary>
        public static Sprite GetRuntimeLayerSprite(AvatarItemCategory category, string itemID)
        {
            string safeId = string.IsNullOrEmpty(itemID) ? "Default" : itemID;
            string key = "layer:" + category + ":" + safeId;
            if (runtimeSprites.TryGetValue(key, out Sprite existing) && existing != null) return existing;
            Sprite sprite = CreateSprite(category, safeId, ResolveSeed(safeId, 0));
            runtimeSprites[key] = sprite;
            return sprite;
        }

        public static Sprite GetFallbackSprite(AvatarItemCategory category, string itemID, Color color)
        {
            return GetRuntimeLayerSprite(category, itemID);
        }

        /// <summary>Returns a zoomed 24x24 preview icon sprite so each dressing room option card displays a distinct picture.</summary>
        public static Sprite GetCardPreviewSprite(AvatarItemCategory category, string itemID)
        {
            string safeId = string.IsNullOrEmpty(itemID) ? "Default" : itemID;
            string key = "card:" + category + ":" + safeId;
            if (cardIconSprites.TryGetValue(key, out Sprite existing) && existing != null) return existing;

            Texture2D tex = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false, false)
            {
                name = "CardIcon_" + category + "_" + safeId,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] iconPixels = new Color32[IconSize * IconSize];
            string lower = safeId.ToLowerInvariant();

            if (lower.Contains("none"))
            {
                DrawNoneBadge(iconPixels);
            }
            else
            {
                Color32[] layerPixels = new Color32[Width * Height];
                // For Face, Hair, Hat, Tattoo, and Glasses, draw a soft head silhouette behind the item so the option is easy to read
                if (category == AvatarItemCategory.Face || category == AvatarItemCategory.Hair || category == AvatarItemCategory.Hat)
                {
                    Ellipse(layerPixels, 8, 25, 5, 6, new Color32(180, 165, 150, 95));
                    Ellipse(layerPixels, 8, 25, 4, 5, new Color32(238, 218, 198, 120));
                }
                else if (category == AvatarItemCategory.Tattoo)
                {
                    DrawBody(layerPixels);
                    for (int i = 0; i < layerPixels.Length; i++)
                        if (layerPixels[i].a > 0) layerPixels[i] = new Color32(225, 205, 188, 110);
                }

                DrawItem(layerPixels, category, safeId, ResolveSeed(safeId, 0));

                // Crop window around the relevant body region for a clear icon preview
                int srcYMin = 4;
                int srcHeight = 28;
                switch (category)
                {
                    case AvatarItemCategory.Face:
                    case AvatarItemCategory.Hair:
                    case AvatarItemCategory.Hat:
                        srcYMin = 15;
                        srcHeight = 17;
                        break;
                    case AvatarItemCategory.Top:
                    case AvatarItemCategory.Accessory:
                    case AvatarItemCategory.Tattoo:
                        srcYMin = 10;
                        srcHeight = 18;
                        break;
                    case AvatarItemCategory.Bottom:
                        srcYMin = 4;
                        srcHeight = 15;
                        break;
                    case AvatarItemCategory.Shoes:
                        srcYMin = 1;
                        srcHeight = 11;
                        break;
                    case AvatarItemCategory.Skin:
                        srcYMin = 3;
                        srcHeight = 29;
                        break;
                }

                for (int iy = 0; iy < IconSize; iy++)
                {
                    for (int ix = 0; ix < IconSize; ix++)
                    {
                        int sx = Mathf.Clamp(Mathf.FloorToInt((ix / (float)IconSize) * Width), 0, Width - 1);
                        int sy = Mathf.Clamp(srcYMin + Mathf.FloorToInt((iy / (float)IconSize) * srcHeight), 0, Height - 1);
                        iconPixels[iy * IconSize + ix] = layerPixels[sy * Width + sx];
                    }
                }
            }

            tex.SetPixels32(iconPixels);
            tex.Apply(false, false);
            Sprite icon = Sprite.Create(tex, new Rect(0, 0, IconSize, IconSize), new Vector2(.5f, .5f), IconSize, 0, SpriteMeshType.FullRect);
            icon.name = tex.name;
            cardIconSprites[key] = icon;
            return icon;
        }

        static void DrawNoneBadge(Color32[] p)
        {
            Color32 ring = new Color32(115, 128, 124, 220);
            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    float dx = x - 11.5f;
                    float dy = y - 11.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist >= 6.2f && dist <= 8.2f)
                        p[y * IconSize + x] = ring;
                    else if ( dist < 7.5f && Mathf.Abs(dx + dy) <= 1.25f)
                        p[y * IconSize + x] = ring;
                }
            }
        }

        /// <summary>Normalizes a renderer to the front-facing (south) pixel-art view.</summary>
        public static void FaceSouth(SpriteRenderer renderer)
        {
            if (renderer == null) return;
            renderer.flipX = false;
            renderer.flipY = false;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
        }

        public static Sprite GetShadowSprite()
        {
            if (shadowSprite != null) return shadowSprite;
            Texture2D texture = CreateShadowTexture();
            shadowSprite = Sprite.Create(texture, new Rect(0, 0, Width, Height), new Vector2(.5f, 0f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            shadowSprite.name = "PixelArt_AvatarShadow";
            return shadowSprite;
        }

        public static Texture2D CreateShadowTexture()
        {
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
            {
                name = "PixelArt_AvatarShadow",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[Width * Height];
            for (int y = 0; y < 3; y++)
            {
                for (int x = 2; x <= 13; x++)
                {
                    float dx = (x - 7.5f) / 6f;
                    float dy = (y - 1f) / 2.1f;
                    if (dx * dx + dy * dy <= 1f) pixels[y * Width + x] = new Color32(27, 35, 43, 145);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static void DrawItem(Color32[] pixels, AvatarItemCategory category, string itemID, int seed)
        {
            string id = (itemID ?? string.Empty).ToLowerInvariant();
            if ((category == AvatarItemCategory.Tattoo || category == AvatarItemCategory.Hat || category == AvatarItemCategory.Accessory) && id.Contains("none")) return;

            switch (category)
            {
                case AvatarItemCategory.Skin: DrawBody(pixels); break;
                case AvatarItemCategory.Face: DrawFace(pixels, id, seed); break;
                case AvatarItemCategory.Tattoo: DrawTattoo(pixels, id, seed); break;
                case AvatarItemCategory.Hair: DrawHair(pixels, id, seed); break;
                case AvatarItemCategory.Hat: DrawHat(pixels, id, seed); break;
                case AvatarItemCategory.Top: DrawTop(pixels, id, seed); break;
                case AvatarItemCategory.Bottom: DrawBottom(pixels, id, seed); break;
                case AvatarItemCategory.Shoes: DrawShoes(pixels, id, seed); break;
                case AvatarItemCategory.Accessory: DrawAccessory(pixels, id, seed); break;
            }
        }

        static void DrawBody(Color32[] p)
        {
            Ellipse(p, 8, 25, 5, 6, Outline);
            Ellipse(p, 8, 25, 4, 5, Fill);
            Rect(p, 6, 18, 9, 21, Outline);
            Rect(p, 6, 18, 9, 20, Fill);
            Polygon(p, new[] { new Vector2Int(6, 20), new Vector2Int(3, 19), new Vector2Int(1, 14), new Vector2Int(3, 13), new Vector2Int(6, 16) }, Outline);
            Polygon(p, new[] { new Vector2Int(9, 20), new Vector2Int(12, 19), new Vector2Int(14, 14), new Vector2Int(12, 13), new Vector2Int(9, 16) }, Outline);
            Polygon(p, new[] { new Vector2Int(6, 19), new Vector2Int(4, 18), new Vector2Int(2, 14), new Vector2Int(4, 14), new Vector2Int(7, 17) }, Fill);
            Polygon(p, new[] { new Vector2Int(9, 19), new Vector2Int(11, 18), new Vector2Int(13, 14), new Vector2Int(11, 14), new Vector2Int(8, 17) }, Fill);
            Rect(p, 5, 5, 7, 15, Outline);
            Rect(p, 8, 5, 10, 15, Outline);
            Rect(p, 6, 5, 7, 14, Fill);
            Rect(p, 9, 5, 9, 14, Fill);
            Set(p, 5, 4, Outline); Set(p, 7, 4, Outline); Set(p, 8, 4, Outline); Set(p, 10, 4, Outline);
        }

        static void DrawFace(Color32[] p, string id, int seed)
        {
            Color32 eyeOutline = new Color32(45, 38, 37, 255);
            Color32 eye = new Color32(28, 30, 35, 255);
            Color32 shine = new Color32(255, 250, 230, 255);
            Color32 mouth = new Color32(142, 54, 65, 255);
            Color32 blush = new Color32(244, 136, 142, 255);

            if (id.Contains("wink") || seed == 1)
            {
                // Left eye winking (>), right eye open
                Set(p, 4, 26, eyeOutline); Set(p, 5, 25, eyeOutline); Set(p, 6, 25, eyeOutline); Set(p, 4, 24, eyeOutline);
                Rect(p, 9, 24, 11, 26, eyeOutline);
                Set(p, 10, 24, eye); Set(p, 10, 25, shine);
                Set(p, 4, 28, eyeOutline); Set(p, 5, 28, eyeOutline);
                Set(p, 9, 28, eyeOutline); Set(p, 10, 28, eyeOutline); Set(p, 11, 27, eyeOutline);
                // Playful side grin
                Set(p, 6, 22, mouth); Set(p, 7, 21, mouth); Set(p, 8, 21, mouth); Set(p, 9, 22, mouth); Set(p, 10, 23, mouth);
            }
            else if (id.Contains("blush") || seed == 2)
            {
                // Sparkling eyes + bright rosy cheeks + happy open smile
                Rect(p, 4, 24, 6, 26, eyeOutline); Rect(p, 9, 24, 11, 26, eyeOutline);
                Set(p, 5, 24, eye); Set(p, 10, 24, eye);
                Set(p, 5, 25, shine); Set(p, 10, 25, shine);
                Set(p, 4, 26, shine); Set(p, 9, 26, shine);
                Rect(p, 3, 22, 5, 23, blush);
                Rect(p, 10, 22, 12, 23, blush);
                Rect(p, 7, 20, 8, 22, mouth);
                Set(p, 6, 22, mouth); Set(p, 9, 22, mouth);
            }
            else
            {
                // Calm gentle expression
                Rect(p, 4, 24, 6, 26, eyeOutline); Rect(p, 9, 24, 11, 26, eyeOutline);
                Set(p, 5, 24, eye); Set(p, 10, 24, eye);
                Set(p, 5, 25, shine); Set(p, 10, 25, shine);
                Set(p, 4, 27, eyeOutline); Set(p, 5, 28, eyeOutline); Set(p, 6, 28, eyeOutline);
                Set(p, 9, 28, eyeOutline); Set(p, 10, 28, eyeOutline); Set(p, 11, 27, eyeOutline);
                Set(p, 7, 21, mouth); Set(p, 8, 21, mouth);
                Set(p, 6, 22, mouth); Set(p, 9, 22, mouth);
            }
        }

        static void DrawTattoo(Color32[] p, string id, int seed)
        {
            Color32 petal = new Color32(232, 92, 118, 255);
            Color32 center = new Color32(250, 214, 90, 255);
            Color32 leaf = new Color32(68, 158, 126, 255);
            // Cheek flower marking + arm botanical vine
            Set(p, 11, 22, petal); Set(p, 12, 23, petal); Set(p, 11, 23, center);
            Set(p, 2, 16, petal); Set(p, 3, 16, center); Set(p, 4, 16, petal);
            Set(p, 3, 17, petal); Set(p, 3, 15, petal);
            Set(p, 2, 14, leaf); Set(p, 12, 16, leaf); Set(p, 13, 17, petal); Set(p, 12, 17, center);
        }

        static void DrawHair(Color32[] p, string id, int seed)
        {
            if (id.Contains("long") || seed == 1)
            {
                // Long flowing waves past the shoulders
                Rect(p, 3, 27, 12, 31, Outline);
                Rect(p, 4, 28, 11, 30, Fill);
                Set(p, 5, 29, Highlight); Set(p, 6, 30, Highlight);
                Rect(p, 2, 13, 4, 27, Outline);
                Rect(p, 11, 13, 13, 27, Outline);
                Rect(p, 3, 14, 4, 26, Fill);
                Rect(p, 11, 14, 12, 26, Fill);
                Set(p, 2, 16, Fill); Set(p, 13, 16, Fill);
                Set(p, 3, 12, Outline); Set(p, 12, 12, Outline);
            }
            else if (id.Contains("curl") || seed == 2)
            {
                // Voluminous curly crown and side puffs
                Ellipse(p, 8, 29, 6, 3, Outline);
                Ellipse(p, 4, 26, 3, 3, Outline);
                Ellipse(p, 12, 26, 3, 3, Outline);
                Ellipse(p, 3, 22, 2, 3, Outline);
                Ellipse(p, 13, 22, 2, 3, Outline);
                Ellipse(p, 8, 29, 5, 2, Fill);
                Ellipse(p, 4, 26, 2, 2, Fill);
                Ellipse(p, 12, 26, 2, 2, Fill);
                Rect(p, 2, 21, 3, 24, Fill);
                Rect(p, 12, 21, 13, 24, Fill);
                Set(p, 6, 30, Highlight); Set(p, 9, 29, Highlight);
            }
            else
            {
                // Neat short crop with side part
                Rect(p, 3, 27, 12, 31, Outline);
                Rect(p, 4, 28, 11, 30, Fill);
                Rect(p, 3, 25, 4, 27, Outline);
                Rect(p, 11, 25, 12, 27, Outline);
                Set(p, 5, 27, Fill); Set(p, 6, 27, Fill); Set(p, 10, 27, Fill);
                Set(p, 8, 27, Outline);
                Set(p, 5, 29, Highlight); Set(p, 6, 29, Highlight);
            }
        }

        static void DrawHat(Color32[] p, string id, int seed)
        {
            if (id.Contains("tudung") || id.Contains("hijab") || seed == 2)
            {
                // Rounded tudung framing the face with draped shoulder shawl and brooch
                Ellipse(p, 8, 26, 6, 6, Outline);
                Ellipse(p, 8, 26, 5, 5, Fill);
                Rect(p, 3, 18, 12, 23, Outline);
                Rect(p, 4, 19, 11, 23, Fill);
                // Clear face window so eyes and smile remain visible
                for (int y = 21; y <= 26; y++)
                    for (int x = 5; x <= 10; x++) Set(p, x, y, new Color32(0, 0, 0, 0));
                Rect(p, 5, 27, 10, 27, Highlight);
                Set(p, 8, 19, GoldTrim); Set(p, 7, 19, Outline); Set(p, 9, 19, Outline);
            }
            else if (id.Contains("songkok") || seed == 1)
            {
                // Traditional flat-topped songkok with gold songket trim band
                Rect(p, 3, 27, 12, 31, Outline);
                Rect(p, 4, 28, 11, 30, Fill);
                Rect(p, 4, 28, 11, 28, GoldTrim);
                Set(p, 5, 30, Highlight); Set(p, 6, 30, Highlight);
            }
            else if (id.Contains("cap") || seed == 3)
            {
                // Rounded sporty cap with extended side visor brim and front badge
                Rect(p, 3, 28, 12, 31, Outline);
                Rect(p, 4, 28, 11, 30, Fill);
                Rect(p, 2, 27, 14, 28, Outline);
                Rect(p, 3, 27, 13, 27, Shade);
                Rect(p, 7, 29, 8, 30, GoldTrim);
            }
            else if (id.Contains("festive") || seed == 4)
            {
                // Three-pointed festive crown with jewels
                Rect(p, 3, 27, 12, 29, Outline);
                Rect(p, 4, 28, 11, 28, Fill);
                Set(p, 4, 30, Outline); Set(p, 4, 31, GoldTrim);
                Set(p, 7, 30, Outline); Set(p, 8, 30, Outline); Set(p, 7, 31, GoldTrim); Set(p, 8, 31, GoldTrim);
                Set(p, 11, 30, Outline); Set(p, 11, 31, GoldTrim);
                Set(p, 5, 29, Fill); Set(p, 7, 29, Fill); Set(p, 8, 29, Fill); Set(p, 10, 29, Fill);
                Set(p, 5, 28, new Color32(235, 75, 95, 255));
                Set(p, 8, 28, new Color32(72, 201, 152, 255));
                Set(p, 10, 28, new Color32(235, 75, 95, 255));
            }
            else
            {
                Rect(p, 3, 27, 12, 30, Outline);
                Rect(p, 4, 28, 11, 29, Fill);
            }
        }

        static void DrawTop(Color32[] p, string id, int seed)
        {
            if (id.Contains("baju") || seed == 1)
            {
                // Baju Melayu: Long-sleeved tunic with Cekak Musang collar, 3 gold buttons, and front pockets
                Rect(p, 4, 13, 11, 21, Outline);
                Rect(p, 5, 14, 10, 20, Fill);
                // Long sleeves
                Rect(p, 2, 14, 4, 20, Outline); Rect(p, 3, 15, 4, 19, Fill);
                Rect(p, 11, 14, 13, 20, Outline); Rect(p, 11, 15, 12, 19, Fill);
                // Collar + button placket
                Rect(p, 6, 20, 9, 21, Outline);
                Rect(p, 7, 16, 8, 20, Shade);
                Set(p, 7, 19, GoldTrim); Set(p, 7, 17, GoldTrim); Set(p, 7, 15, GoldTrim);
                Rect(p, 5, 14, 6, 15, Shade); Rect(p, 9, 14, 10, 15, Shade);
            }
            else if (id.Contains("cheong") || seed == 2)
            {
                // Cheongsam: High Mandarin collar, capped short sleeves, and diagonal gold frog-button lapel
                Rect(p, 4, 14, 11, 21, Outline);
                Rect(p, 5, 15, 10, 20, Fill);
                Rect(p, 3, 18, 4, 20, Outline); Set(p, 3, 19, Fill);
                Rect(p, 11, 18, 12, 20, Outline); Set(p, 12, 19, Fill);
                // Mandarin collar
                Rect(p, 6, 20, 9, 21, Outline);
                Set(p, 7, 20, GoldTrim); Set(p, 8, 20, GoldTrim);
                // Diagonal lapel + frog clasps
                Set(p, 7, 19, GoldTrim); Set(p, 8, 19, Outline);
                Set(p, 8, 18, GoldTrim); Set(p, 9, 18, Outline);
                Set(p, 9, 17, GoldTrim); Set(p, 10, 17, Outline);
            }
            else if (id.Contains("saree") || seed == 3)
            {
                // Saree blouse & diagonal draped pallu sash with gold zari border
                Rect(p, 4, 14, 11, 20, Outline);
                Rect(p, 5, 15, 10, 19, Shade);
                Rect(p, 3, 17, 4, 20, Outline); Set(p, 3, 18, Fill);
                Rect(p, 11, 17, 12, 20, Outline); Set(p, 12, 18, Fill);
                // Diagonal draped pallu from left shoulder to right hip
                for (int i = 0; i < 6; i++)
                {
                    int x = 5 + i;
                    int y = 20 - i;
                    Rect(p, Mathf.Max(5, x - 1), Mathf.Max(14, y - 1), Mathf.Min(10, x + 1), Mathf.Min(20, y), Fill);
                    Set(p, x, y, GoldTrim);
                }
            }
            else if (id.Contains("jacket") || seed == 4)
            {
                // Puffy Winter Jacket: High collar, puffy quilted ribs, center dark zipper, and warm cuffs
                Rect(p, 3, 13, 12, 21, Outline);
                Rect(p, 4, 14, 11, 20, Fill);
                Rect(p, 1, 14, 3, 20, Outline); Rect(p, 2, 15, 3, 19, Fill);
                Rect(p, 12, 14, 14, 20, Outline); Rect(p, 12, 15, 13, 19, Fill);
                // High puff collar + center zipper + quilt lines
                Rect(p, 5, 20, 10, 21, Highlight);
                Rect(p, 7, 14, 8, 21, Outline);
                Rect(p, 4, 16, 6, 16, Shade); Rect(p, 9, 16, 11, 16, Shade);
                Rect(p, 4, 18, 6, 18, Shade); Rect(p, 9, 18, 11, 18, Shade);
            }
            else
            {
                // Casual Tee: Short sleeves, crew collar, and bold horizontal chest stripe
                Rect(p, 4, 14, 11, 20, Outline);
                Rect(p, 5, 15, 10, 19, Fill);
                Rect(p, 2, 17, 4, 20, Outline); Rect(p, 3, 18, 4, 19, Fill);
                Rect(p, 11, 17, 13, 20, Outline); Rect(p, 11, 18, 12, 19, Fill);
                Rect(p, 5, 17, 10, 18, Highlight);
                Set(p, 7, 19, Outline); Set(p, 8, 19, Outline);
            }
        }

        static void DrawBottom(Color32[] p, string id, int seed)
        {
            if (id.Contains("skirt") || seed == 2)
            {
                // Flared A-line pleated skirt with hem trim
                Polygon(p, new[] { new Vector2Int(4, 15), new Vector2Int(11, 15), new Vector2Int(13, 7), new Vector2Int(2, 7) }, Outline);
                Polygon(p, new[] { new Vector2Int(5, 14), new Vector2Int(10, 14), new Vector2Int(12, 8), new Vector2Int(3, 8) }, Fill);
                Rect(p, 4, 9, 11, 9, Highlight);
                Set(p, 6, 10, Shade); Set(p, 8, 10, Shade); Set(p, 9, 10, Shade);
            }
            else if (id.Contains("traditional") || seed == 1)
            {
                // Traditional trousers with decorative woven songket sampin wrap
                Rect(p, 5, 7, 7, 15, Outline);
                Rect(p, 8, 7, 10, 15, Outline);
                Rect(p, 6, 8, 7, 14, Shade);
                Rect(p, 8, 8, 9, 14, Shade);
                // Songket sampin wrap from waist to knee
                Rect(p, 4, 10, 11, 15, Outline);
                Rect(p, 5, 11, 10, 14, Fill);
                Rect(p, 5, 12, 10, 12, GoldTrim);
                Set(p, 6, 13, GoldTrim); Set(p, 9, 13, GoldTrim);
            }
            else
            {
                // Everyday cuffed trousers with belt buckle
                Rect(p, 5, 14, 10, 15, Outline);
                Rect(p, 5, 7, 7, 14, Outline);
                Rect(p, 8, 7, 10, 14, Outline);
                Rect(p, 6, 8, 7, 13, Fill);
                Rect(p, 8, 8, 9, 13, Fill);
                Set(p, 7, 14, GoldTrim); Set(p, 8, 14, GoldTrim);
                Rect(p, 6, 8, 7, 8, Highlight); Rect(p, 8, 8, 9, 8, Highlight);
            }
        }

        static void DrawShoes(Color32[] p, string id, int seed)
        {
            if (id.Contains("boot") || seed == 2)
            {
                // Tall rain boots with top cuff and lug sole
                Rect(p, 4, 3, 7, 9, Outline); Rect(p, 8, 3, 11, 9, Outline);
                Rect(p, 5, 4, 6, 8, Fill); Rect(p, 9, 4, 10, 8, Fill);
                Rect(p, 5, 8, 6, 8, GoldTrim); Rect(p, 9, 8, 10, 8, GoldTrim);
                Rect(p, 4, 3, 7, 3, Outline); Rect(p, 8, 3, 11, 3, Outline);
            }
            else if (id.Contains("sandal") || seed == 1)
            {
                // Open-toe strappy sandals
                Rect(p, 4, 3, 7, 4, Outline); Rect(p, 8, 3, 11, 4, Outline);
                Rect(p, 5, 4, 6, 4, GoldTrim); Rect(p, 9, 4, 10, 4, GoldTrim);
                Rect(p, 5, 6, 6, 6, Outline); Rect(p, 9, 6, 10, 6, Outline);
            }
            else
            {
                // Low-top sneakers with white toe cap and laces
                Rect(p, 4, 3, 7, 6, Outline); Rect(p, 8, 3, 11, 6, Outline);
                Rect(p, 5, 4, 6, 5, Fill); Rect(p, 9, 4, 10, 5, Fill);
                Set(p, 4, 4, Highlight); Set(p, 11, 4, Highlight);
                Set(p, 6, 6, Highlight); Set(p, 9, 6, Highlight);
            }
        }

        static void DrawAccessory(Color32[] p, string id, int seed)
        {
            if (id.Contains("glass") || seed == 1)
            {
                // Distinct round eyeglasses with bridge and lens glint
                PixelRing(p, 5, 25, 2, 2);
                PixelRing(p, 10, 25, 2, 2);
                Rect(p, 7, 25, 8, 25, Outline);
                Set(p, 4, 26, Highlight); Set(p, 9, 26, Highlight);
            }
            else if (id.Contains("bag") || id.Contains("sling") || seed == 2)
            {
                // Cross-body sling strap + hip satchel bag with gold buckle
                for (int i = 0; i < 6; i++)
                {
                    Set(p, 5 + i, 20 - i, Outline);
                    Set(p, 6 + i, 20 - i, Fill);
                }
                Rect(p, 10, 11, 14, 16, Outline);
                Rect(p, 11, 12, 13, 15, Fill);
                Set(p, 12, 14, GoldTrim);
            }
            else
            {
                // Golden star community badge with dual ribbons on chest
                Rect(p, 9, 17, 11, 19, Outline);
                Rect(p, 9, 18, 11, 19, GoldTrim);
                Set(p, 10, 18, new Color32(235, 82, 96, 255));
                Set(p, 9, 16, new Color32(84, 160, 255, 255));
                Set(p, 11, 16, new Color32(84, 160, 255, 255));
            }
        }

        static void PixelRing(Color32[] p, int cx, int cy, int rx, int ry)
        {
            for (int y = -ry; y <= ry; y++)
            for (int x = -rx; x <= rx; x++)
                if (x == -rx || x == rx || y == -ry || y == ry) Set(p, cx + x, cy + y, Outline);
        }

        static void Set(Color32[] p, int x, int y, Color32 color)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return;
            p[y * Width + x] = color;
        }

        static void Rect(Color32[] p, int x0, int y0, int x1, int y1, Color32 color)
        {
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++) Set(p, x, y, color);
        }

        static void Ellipse(Color32[] p, int cx, int cy, int rx, int ry, Color32 color)
        {
            for (int y = -ry; y <= ry; y++)
            for (int x = -rx; x <= rx; x++)
                if ((float)(x * x) / (rx * rx) + (float)(y * y) / (ry * ry) <= 1f) Set(p, cx + x, cy + y, color);
        }

        static void Polygon(Color32[] p, Vector2Int[] points, Color32 color)
        {
            int minY = Height, maxY = 0;
            foreach (Vector2Int point in points) { minY = Mathf.Min(minY, point.y); maxY = Mathf.Max(maxY, point.y); }
            for (int y = minY; y <= maxY; y++)
            {
                List<float> intersections = new List<float>();
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2Int a = points[i];
                    Vector2Int b = points[(i + 1) % points.Length];
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
