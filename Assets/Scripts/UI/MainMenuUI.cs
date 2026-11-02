using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>Builder for the cozy wooden announcement-board identity used by the main menu.</summary>
    public sealed class MainMenuUI : MonoBehaviour
    {
        public static void BuildAnnouncementBanner(Transform parent, Font legacyFont, int titleSize)
        {
            if (parent == null) return;
            GameObject banner = new GameObject("Township Park Pixel Banner", typeof(RectTransform), typeof(Image));
            banner.transform.SetParent(parent, false);
            RectTransform rect = banner.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(410f, 170f);
            rect.anchoredPosition = new Vector2(-255f, 170f);
            banner.GetComponent<Image>().color = Color.clear;
            banner.GetComponent<Image>().raycastTarget = false;
            AddLabel(banner.transform, "CRONY", new Vector2(0f, 0f), new Vector2(410f, 130f), titleSize, legacyFont, FontStyle.Bold);
            AddLabel(banner.transform, "T O W N S H I P   P A R K", new Vector2(0f, 100f), new Vector2(410f, 28f), 16, legacyFont, FontStyle.Bold);
        }

        static void AddLabel(Transform parent, string value, Vector2 position, Vector2 size, int fontSize, Font font, FontStyle style)
        {
            GameObject labelObject = new GameObject(value, typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(parent, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text label = labelObject.GetComponent<Text>();
            label.font = font != null ? font : PixelUiTheme.BodyFont;
            label.text = value;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = PixelUiTheme.Ink;
            label.raycastTarget = false;

        }
    }
}
