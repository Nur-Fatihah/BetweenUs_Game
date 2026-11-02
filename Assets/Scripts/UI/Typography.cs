using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>
    /// HUD type rules: a warm, rounded pixel display face (Pixelify Sans Bold) for titles, room headers and big
    /// announcements; everything people read for long (menus, chat, instructions, numbers) stays in Quicksand.
    /// </summary>
    public static class Typography
    {
        const int TitleSize = 22, PixelStep = 4;
        static Font pixelFont;

        public static Font PixelFont
        {
            get
            {
                if (pixelFont == null) pixelFont = Resources.Load<Font>("Fonts/PixelifySans-Bold");
                return pixelFont != null ? pixelFont : PixelUiTheme.BodyFont;
            }
        }

        /// <summary>Restyles every qualifying label under a freshly built UI root.</summary>
        public static void Apply(Transform root)
        {
            if (root == null) return;
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
                if (IsLabel(text)) Pixelate(text);
        }

        static bool IsLabel(Text text)
        {
            if (text.font == PixelFont || text.fontStyle != FontStyle.Bold || text.fontSize < 15) return false;
            string value = text.text;
            if (string.IsNullOrEmpty(value) || value.IndexOf('\n') >= 0) return false;
            foreach (char c in value)
                // Numbers read better in the sans; the pixel font also lacks symbols such as ✓ and ·.
                if (char.IsDigit(c) || c > 126) return false;
            return text.fontSize >= TitleSize && value.Length <= 32;
        }

        static void Pixelate(Text text)
        {
            Font font = text.font;
            FontStyle style = text.fontStyle;
            int size = text.fontSize;
            bool bestFit = text.resizeTextForBestFit;
            text.font = PixelFont;
            text.fontStyle = FontStyle.Normal;
            text.resizeTextForBestFit = false;
            // Even sizes keep the pixel strokes on whole pixels at the reference resolution.
            int start = Mathf.Max(PixelStep * 4, Mathf.RoundToInt(size / (float)PixelStep) * PixelStep);
            for (int candidate = start; candidate >= PixelStep * 4; candidate -= PixelStep)
            {
                text.fontSize = candidate;
                Rect box = text.rectTransform.rect;
                if (box.width <= 0f || (text.preferredWidth <= box.width && text.preferredHeight <= box.height * 1.25f)) return;
            }
            // Too long for its box at a crisp size: keep the readable sans instead of blurring or overflowing.
            text.font = font;
            text.fontStyle = style;
            text.fontSize = size;
            text.resizeTextForBestFit = bestFit;
        }
    }
}
