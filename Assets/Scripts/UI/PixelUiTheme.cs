using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>Shared crisp, pixel-framed UI palette and uGUI skin helpers.</summary>
    public static class PixelUiTheme
    {
        public static readonly Color Cream = Hex("F4E9D2");
        public static readonly Color Linen = Hex("EFE2C6");
        public static readonly Color Chocolate = Hex("3F4A2E");
        public static readonly Color Highlight = Hex("FFF8E6");
        public static readonly Color Almond = Hex("EADCBF");
        public static readonly Color Amber = Hex("D9A25A");
        public static readonly Color Gold = Hex("F2C14E");
        public static readonly Color Mint = Hex("A8CFA0");
        public static readonly Color Sky = Hex("93BCCB");
        public static readonly Color Coral = Hex("DE8F76");
        public static readonly Color Ink = Hex("38402A");
        public static readonly Color MutedInk = Hex("6D6A52");
        public static readonly Color Tan = Hex("D9C4A0");
        public static readonly Color Plum = Hex("A982B0");
        public static readonly Color Forest = Hex("7FA878");
        public static readonly Color Slate = Hex("4F6A80");

        static readonly HashSet<GameObject> warmSurfaceObjects = new HashSet<GameObject>();
        static Sprite windowSprite;
        static Sprite cardSprite;
        static Sprite roundSprite;
        static Sprite dotSprite;
        static Texture2D guiCream;
        static Texture2D guiChocolate;
        static Texture2D guiAlmond;
        static Texture2D guiAmber;
        static Texture2D guiCoral;

        /// <summary>Easy-to-read body face: Quicksand, anti-aliased at native resolution. Falls back to Unity's built-in font.</summary>
        public static Font BodyFont
        {
            get
            {
                if (bodyFont == null) bodyFont = Resources.Load<Font>("Fonts/Quicksand-SemiBold");
                return bodyFont != null ? bodyFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }
        static Font bodyFont;
        /// <summary>Soft indigo used for every drop shadow and contact shadow instead of flat black.</summary>
        public static readonly Color ShadowIndigo = new Color(.2f, .16f, .34f, .3f);

        public static void ConfigureCanvas(CanvasScaler scaler)
        {
            if (scaler == null) return;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
        }

        public static void ApplyWindow(GameObject root, bool dropShadow = true)
        {
            ApplyFrame(root, GetWindowSprite(), dropShadow);
        }

        public static void ApplyCard(GameObject root, bool dropShadow = false)
        {
            ApplyFrame(root, GetCardSprite(), dropShadow);
        }

        static void ApplyFrame(GameObject root, Sprite frame, bool dropShadow)
        {
            if (root == null) return;
            Image image = root.GetComponent<Image>();
            if (image == null) image = root.AddComponent<Image>();
            image.sprite = frame;
            // Sliced borders keep the inked edge crisp; the parchment fibres tile instead of stretching into blocks.
            image.type = Image.Type.Tiled;
            image.color = Color.white;
            image.raycastTarget = false;
            warmSurfaceObjects.Add(root);
            if (root.GetComponent<Canvas>() == null && root.GetComponent<UIPopIn>() == null) root.AddComponent<UIPopIn>();
            if (!dropShadow || root.transform.parent == null || root.transform.parent.Find(root.name + " Shadow") != null) return;

            GameObject shadow = new GameObject(root.name + " Shadow", typeof(RectTransform), typeof(Image));
            RectTransform source = root.GetComponent<RectTransform>();
            RectTransform target = shadow.GetComponent<RectTransform>();
            target.SetParent(root.transform.parent, false);
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta;
            target.anchoredPosition = source.anchoredPosition + new Vector2(6f, -7f);
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
            Image shadowImage = shadow.GetComponent<Image>();
            shadowImage.sprite = GetRoundSprite();
            shadowImage.type = Image.Type.Sliced;
            shadowImage.color = ShadowIndigo;
            shadow.AddComponent<UIPopIn>();
            shadowImage.raycastTarget = false;
            // Insert the shadow immediately behind the window. Keeping its local sibling
            // placement also preserves the ordering relative to the modal shade.
            shadow.transform.SetSiblingIndex(root.transform.GetSiblingIndex());
        }

        public static Image StyleButton(Button button, Color faceColor, bool selected = false)
        {
            if (button == null) return null;
            Image hitArea = button.GetComponent<Image>();
            if (hitArea == null) hitArea = button.gameObject.AddComponent<Image>();
            hitArea.sprite = GetRoundSprite();
            hitArea.type = Image.Type.Sliced;
            hitArea.color = new Color(1f, 1f, 1f, 0f);
            hitArea.raycastTarget = true;
            if (button.transform.Find("Button Halo") == null)
            {
                Image halo = Child(button.transform, "Button Halo", GetGlowSprite(), new Color(Gold.r, Gold.g, Gold.b, 0f), new Vector2(-14f, -14f), new Vector2(14f, 14f));
                halo.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            if (button.transform.Find("Pixel Button Frame") == null)
                Child(button.transform, "Pixel Button Frame", GetRoundSprite(), Chocolate, Vector2.zero, Vector2.zero).transform.SetSiblingIndex(1);

            Transform existing = button.transform.Find("Pixel Button Surface");
            Image surface;
            if (existing == null)
            {
                GameObject surfaceObject = new GameObject("Pixel Button Surface", typeof(RectTransform), typeof(Image), typeof(Outline));
                surfaceObject.transform.SetParent(button.transform, false);
                surface = surfaceObject.GetComponent<Image>();
                RectTransform surfaceRect = surfaceObject.GetComponent<RectTransform>();
                surfaceRect.anchorMin = Vector2.zero;
                surfaceRect.anchorMax = Vector2.one;
                surfaceRect.offsetMin = new Vector2(2f, 4f);
                surfaceRect.offsetMax = new Vector2(-2f, -2f);
                surfaceObject.transform.SetSiblingIndex(2);
                Outline outline = surfaceObject.GetComponent<Outline>();
                outline.effectColor = new Color(Highlight.r, Highlight.g, Highlight.b, .8f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
            else surface = existing.GetComponent<Image>();

            surface.sprite = GetRoundSprite();
            surface.type = Image.Type.Sliced;
            surface.color = faceColor;
            surface.raycastTarget = false;
            button.targetGraphic = surface;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
            colors.pressedColor = new Color(.83f, .88f, .85f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(.65f, .65f, .65f, .6f);
            button.colors = colors;
            UIButtonJuice juice = button.GetComponent<UIButtonJuice>();
            if (juice == null) juice = button.gameObject.AddComponent<UIButtonJuice>();
            juice.SetBaseScale(selected ? 1.015f : 1f);
            juice.SyncRestPosition();
            return surface;
        }

        public static void SetButtonFace(Button button, Color faceColor, bool selected = false)
        {
            if (button == null) return;
            Image surface = button.transform.Find("Pixel Button Surface")?.GetComponent<Image>();
            if (surface != null) surface.color = faceColor;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
            colors.pressedColor = new Color(.83f, .88f, .85f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            UIButtonJuice juice = button.GetComponent<UIButtonJuice>();
            if (juice != null)
            {
                juice.SetBaseScale(selected ? 1.015f : 1f);
                juice.SyncRestPosition();
            }
        }

        /// <summary>Cloth-tag tabs: the active tab grows 6% and turns sun-gold; inactive tabs recede, desaturated at 70% opacity.</summary>
        public static void SetTabState(Button tab, bool active)
        {
            if (tab == null) return;
            SetButtonFace(tab, active ? Gold : Color.Lerp(Tan, new Color(.78f, .76f, .72f), .45f), active);
            UIButtonJuice juice = tab.GetComponent<UIButtonJuice>();
            if (juice != null) juice.SetBaseScale(active ? 1.06f : 1f);
            if (!tab.TryGetComponent(out CanvasGroup group)) group = tab.gameObject.AddComponent<CanvasGroup>();
            group.alpha = active ? 1f : .7f;
            Transform surface = tab.transform.Find("Pixel Button Surface");
            if (surface != null && surface.Find("Stitching") == null)
                Child(surface, "Stitching", GetStitchSprite(), new Color(Chocolate.r, Chocolate.g, Chocolate.b, .55f), new Vector2(4f, 4f), new Vector2(-4f, -4f));
        }

        static Image Child(Transform parent, string name, Sprite sprite, Color color, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static bool HasWarmSurface(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
                if (warmSurfaceObjects.Contains(current.gameObject)) return true;
            return false;
        }

        public static Color ReadableTextColor(Transform parent, Color requested)
        {
            if (!HasWarmSurface(parent)) return requested;
            float luminance = requested.r * .2126f + requested.g * .7152f + requested.b * .0722f;
            return luminance > .48f ? Ink : requested;
        }

        public static Color ButtonTextColor(Color face)
        {
            float luminance = face.r * .2126f + face.g * .7152f + face.b * .0722f;
            return luminance > .43f ? Ink : Cream;
        }

        public static void MarkWarmSurface(GameObject root)
        {
            if (root != null) warmSurfaceObjects.Add(root);
        }

        public static Sprite GetWindowSprite()
        {
            if (windowSprite == null) windowSprite = BuildParchment(Cream, Chocolate, 11);
            return windowSprite;
        }

        public static Sprite GetCardSprite()
        {
            if (cardSprite == null) cardSprite = BuildParchment(Almond, Color.Lerp(Chocolate, Almond, .35f), 23);
            return cardSprite;
        }

        static Sprite scrimSprite;

        /// <summary>
        /// Horizontal scrim: nearly opaque on the left half, easing to clear on the right. UI alpha blends light in
        /// this linear-colour project, so a flat half-transparent shade barely darkens the park behind menus.
        /// </summary>
        public static Sprite GetScrimSprite()
        {
            if (scrimSprite != null) return scrimSprite;
            const int width = 64;
            Texture2D texture = new Texture2D(width, 1, TextureFormat.RGBA32, false) { name = "Menu Scrim", wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < width; x++)
            {
                float alpha = x < 30 ? .96f : Mathf.Lerp(.96f, 0f, Mathf.SmoothStep(0f, 1f, (x - 30) / 33f));
                texture.SetPixel(x, 0, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply(false, true);
            scrimSprite = Sprite.Create(texture, new Rect(0, 0, width, 1), new Vector2(.5f, .5f));
            return scrimSprite;
        }

        /// <summary>Smooth river-pebble shape: anti-aliased edge and a soft top-lit gradient, tinted by the Image colour.</summary>
        public static Sprite GetRoundSprite()
        {
            if (roundSprite != null) return roundSprite;
            const int size = 32; const float radius = 10f;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float coverage = RoundedCoverage(x, y, size, radius);
                    float shade = Mathf.Lerp(.9f, 1f, y / (size - 1f));
                    if (y == size - 4 && coverage > .99f) shade = 1f;
                    pixels[y * size + x] = new Color(shade, shade, shade, coverage);
                }
            roundSprite = MakeSprite(pixels, size, FilterMode.Bilinear, 11f, "Pebble UI Shape");
            return roundSprite;
        }

        static Sprite glowSprite, ringSprite, stitchSprite;

        public static Sprite GetGlowSprite()
        {
            if (glowSprite != null) return glowSprite;
            const int size = 64;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance to an inner rounded rect, so the halo hugs any button shape when sliced.
                    float dx = Mathf.Max(0f, Mathf.Abs(x - 31.5f) - 6f), dy = Mathf.Max(0f, Mathf.Abs(y - 31.5f) - 6f);
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 24f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            glowSprite = MakeSprite(pixels, size, FilterMode.Bilinear, 28f, "Soft Glow");
            return glowSprite;
        }

        public static Sprite GetRingSprite()
        {
            if (ringSprite != null) return ringSprite;
            const int size = 64;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Abs(new Vector2(x - 31.5f, y - 31.5f).magnitude - 28f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(1.6f - d * .6f));
                }
            ringSprite = MakeSprite(pixels, size, FilterMode.Bilinear, 0f, "Press Ring");
            return ringSprite;
        }

        public static Sprite GetStitchSprite()
        {
            if (stitchSprite != null) return stitchSprite;
            const int size = 32;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = InsideRounded(x, y, size, 0, 6) && !InsideRounded(x, y, size, 1, 5);
                    bool dash = ((x + y) / 2) % 2 == 0;
                    pixels[y * size + x] = edge && dash ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            stitchSprite = MakeSprite(pixels, size, FilterMode.Point, 8f, "Tab Stitching");
            return stitchSprite;
        }

        /// <summary>Warm paper: rounded corners, a hand-inked dark-olive border, a soft inner highlight and tileable fibres.</summary>
        static Sprite BuildParchment(Color fill, Color ink, int seed)
        {
            const int size = 64, corner = 9;
            System.Random random = new System.Random(seed);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color pixel = new Color(0f, 0f, 0f, 0f);
                    if (InsideRounded(x, y, size, 0, corner))
                    {
                        // A slight wobble in the ink weight makes the border look drawn rather than ruled.
                        bool heavy = (x * 7 + y * 13) % 11 < 6;
                        if (!InsideRounded(x, y, size, heavy ? 2 : 1, corner - 1)) pixel = ink;
                        else if (!InsideRounded(x, y, size, 3, corner - 2)) pixel = Color.Lerp(fill, Highlight, .7f);
                        else
                        {
                            float fibre = (float)random.NextDouble();
                            pixel = fill * (fibre < .08f ? .965f : fibre > .94f ? 1.02f : 1f);
                            pixel.a = 1f;
                        }
                    }
                    pixels[y * size + x] = pixel;
                }
            return MakeSprite(pixels, size, FilterMode.Point, 14f, "Parchment " + seed);
        }

        static float RoundedCoverage(int x, int y, int size, float radius)
        {
            float cx = Mathf.Clamp(x + .5f, radius, size - radius), cy = Mathf.Clamp(y + .5f, radius, size - radius);
            float d = new Vector2(x + .5f - cx, y + .5f - cy).magnitude;
            return Mathf.Clamp01(radius - d + .5f);
        }

        static Sprite MakeSprite(Color32[] pixels, int size, FilterMode filter, float border, string name)
        {
            Texture2D texture = NewTexture(size, name);
            texture.filterMode = filter;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            return sprite;
        }

        public static Sprite GetDotSprite()
        {
            if (dotSprite == null) dotSprite = BuildMaskSprite(20, 9, "Pixel Dot UI Mask");
            return dotSprite;
        }

        public static Texture2D GetGuiTexture(Color color)
        {
            if (Mathf.Approximately(color.r, Cream.r) && Mathf.Approximately(color.g, Cream.g)) return guiCream ?? (guiCream = OnePixel(color, "UI Cream"));
            if (Mathf.Approximately(color.r, Chocolate.r) && Mathf.Approximately(color.g, Chocolate.g)) return guiChocolate ?? (guiChocolate = OnePixel(color, "UI Chocolate"));
            if (Mathf.Approximately(color.r, Almond.r) && Mathf.Approximately(color.g, Almond.g)) return guiAlmond ?? (guiAlmond = OnePixel(color, "UI Almond"));
            if (Mathf.Approximately(color.r, Amber.r) && Mathf.Approximately(color.g, Amber.g)) return guiAmber ?? (guiAmber = OnePixel(color, "UI Amber"));
            if (Mathf.Approximately(color.r, Coral.r) && Mathf.Approximately(color.g, Coral.g)) return guiCoral ?? (guiCoral = OnePixel(color, "UI Coral"));
            return OnePixel(color, "UI Fill");
        }

        static Sprite BuildMaskSprite(int size, int radius, string name)
        {
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = InsideRounded(x, y, size, 0, radius) ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            Texture2D texture = NewTexture(size, name);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(Mathf.Min(radius, size / 2f), Mathf.Min(radius, size / 2f), Mathf.Min(radius, size / 2f), Mathf.Min(radius, size / 2f)));
            sprite.name = name;
            return sprite;
        }

        static bool InsideRounded(int x, int y, int size, int inset, int radius)
        {
            int left = inset;
            int right = size - 1 - inset;
            int bottom = inset;
            int top = size - 1 - inset;
            if (x < left || x > right || y < bottom || y > top) return false;
            int r = Mathf.Min(radius, (right - left + 1) / 2, (top - bottom + 1) / 2);
            int cx = x < left + r ? left + r : x > right - r ? right - r : x;
            int cy = y < bottom + r ? bottom + r : y > top - r ? top - r : y;
            int dx = x - cx;
            int dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        static Texture2D NewTexture(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        static Texture2D OnePixel(Color color, string name)
        {
            Texture2D texture = NewTexture(1, name);
            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);
            return texture;
        }

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }
    }
}
