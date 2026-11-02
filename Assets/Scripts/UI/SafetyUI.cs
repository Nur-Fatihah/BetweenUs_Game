using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>Warm, readable player safety and report panels for local and network sessions.</summary>
    public static class SafetyUI
    {
        public static GameObject ShowRoster(Transform parent, IList<MatchPlayer> players, Font legacyFont,
            Action<int> toggleMute, Action<int> toggleBlock, Action<int> report, Action settings, Action close)
        {
            GameObject modal = CreateModal(parent, "Safety & Moderation", new Vector2(930f, 790f));
            Transform panel = modal.transform.Find("Safety Window");
            AddLabel(panel, "SAFETY CONTROLS", new Vector2(.04f, .885f), new Vector2(.62f, .97f), 31, legacyFont, FontStyle.Bold, TextAnchor.MiddleLeft);
            AddLabel(panel, "Manage a player or file a report. Local practice reports stay on this device.",
                new Vector2(.04f, .835f), new Vector2(.93f, .89f), 15, legacyFont, FontStyle.Normal, TextAnchor.MiddleLeft);
            AddButton(panel, "Open Settings", "SETTINGS", new Vector2(.66f, .90f), new Vector2(.83f, .97f), PixelUiTheme.Sky, settings, legacyFont, 13);
            AddButton(panel, "Close Safety", "CLOSE", new Vector2(.85f, .90f), new Vector2(.96f, .97f), PixelUiTheme.Coral, close, legacyFont, 13);

            int row = 0;
            if (players != null)
            {
                foreach (MatchPlayer player in players)
                {
                    if (player == null || player.isHuman || !player.alive) continue;
                    int id = player.id;
                    float yTop = .79f - row * .092f;
                    float yBottom = yTop - .078f;
                    GameObject card = Panel(panel, "Safety Player Card " + player.name, PixelUiTheme.Cream);
                    RectTransform cardRect = card.GetComponent<RectTransform>();
                    cardRect.anchorMin = new Vector2(.04f, yBottom);
                    cardRect.anchorMax = new Vector2(.96f, yTop);
                    cardRect.offsetMin = Vector2.zero;
                    cardRect.offsetMax = Vector2.zero;
                    PixelUiTheme.ApplyCard(card, false);
                    AddLabel(card.transform, player.name, new Vector2(.035f, .1f), new Vector2(.34f, .9f), 17, legacyFont, FontStyle.Bold, TextAnchor.MiddleLeft);
                    AddLabel(card.transform, player.muted ? "MUTED" : player.blocked ? "BLOCKED" : "IN TOWN",
                        new Vector2(.36f, .1f), new Vector2(.55f, .9f), 12, legacyFont, FontStyle.Bold, TextAnchor.MiddleCenter);
                    AddButton(card.transform, "Mute Player", player.muted ? "UNMUTE" : "MUTE", new Vector2(.58f, .13f), new Vector2(.71f, .87f), PixelUiTheme.Tan, () => toggleMute?.Invoke(id), legacyFont, 12);
                    AddButton(card.transform, "Block Player", player.blocked ? "UNBLOCK" : "BLOCK", new Vector2(.73f, .13f), new Vector2(.85f, .87f), PixelUiTheme.Sky, () => toggleBlock?.Invoke(id), legacyFont, 12);
                    AddButton(card.transform, "Report Player", "REPORT", new Vector2(.85f, .13f), new Vector2(.96f, .87f), PixelUiTheme.Coral, () => report?.Invoke(id), legacyFont, 12);
                    row++;
                }
            }

            if (row == 0)
                AddLabel(panel, "No other players are available to manage right now.", new Vector2(.1f, .40f), new Vector2(.9f, .58f), 18, legacyFont, FontStyle.Normal, TextAnchor.MiddleCenter);
            return modal;
        }

        public const string DistressReason = "They may be in distress";
        static readonly string[] ReportReasons = { "Bullying or targeted insults", "Identity-based abuse", "Unwanted sexual content", "Threats or personal information",
            "Extremism or praising violence", "Encouraging self-harm", "Cheating, spam or impersonation", DistressReason };

        public static GameObject ShowRules(Transform parent, Font legacyFont, Action close)
        {
            GameObject modal = CreateModal(parent, "Community Rules", new Vector2(900f, 800f));
            Transform panel = modal.transform.Find("Safety Window");
            AddLabel(panel, "COMMUNITY RULES", new Vector2(.05f, .9f), new Vector2(.75f, .97f), 30, legacyFont, FontStyle.Bold, TextAnchor.MiddleLeft);
            AddButton(panel, "Close Rules", "CLOSE", new Vector2(.80f, .905f), new Vector2(.95f, .965f), PixelUiTheme.Coral, close, legacyFont, 14);
            string[] sections =
            {
                "NOT ALLOWED\nTargeted insults or bullying · abuse about race, gender, religion or any identity · sexual comments or requests · real-world threats, stalking or sharing someone's personal details · extremist recruitment or praising violence · encouraging anyone to self-harm · spam, impersonation, cheating or dodging the filter.\nSwearing is not sent: a short note explains why, and it never counts as a strike. Game talk like \"I'll get you next round\" is fine.",
                "WHAT HAPPENS\n1  Message blocked, rule explained, warning recorded.\n2  Chat paused and voice restricted for the match. You can keep playing.\n3  Removed from the match.\n4  Suspension for repeated or severe misconduct. Threats, doxxing, extremism and encouraging self-harm skip the warnings. Permanent bans always need a human review.",
                "IF YOU ARE STRUGGLING\nSaying you want to hurt yourself is never punished. The message stays private and you will see support options.",
                "REPORTS & APPEALS\nReport a player, a message, a voice incident or a whole match from Chat > Players. Your name is never shown to the person you report, and a pile of reports alone never punishes anyone. Appeal any decision in Settings > Safety."
            };
            float[] tops = { .88f, .62f, .36f, .25f };
            float[] bottoms = { .63f, .37f, .26f, .05f };
            for (int i = 0; i < sections.Length; i++)
                AddLabel(panel, sections[i], new Vector2(.05f, bottoms[i]), new Vector2(.95f, tops[i]), 16, legacyFont, FontStyle.Normal, TextAnchor.UpperLeft);
            return modal;
        }

        public static GameObject ShowReport(Transform parent, string playerName, Font legacyFont, Action<string> chooseReason, Action cancel)
        {
            GameObject modal = CreateModal(parent, "Report Player", new Vector2(680f, 760f));
            Transform panel = modal.transform.Find("Safety Window");
            AddLabel(panel, "REPORT " + (string.IsNullOrEmpty(playerName) ? "PLAYER" : playerName.ToUpperInvariant()),
                new Vector2(.05f, .895f), new Vector2(.95f, .97f), 27, legacyFont, FontStyle.Bold, TextAnchor.MiddleCenter);
            AddLabel(panel, "Choose the closest reason. A report does not trigger automatic punishment.",
                new Vector2(.08f, .84f), new Vector2(.92f, .895f), 14, legacyFont, FontStyle.Normal, TextAnchor.MiddleCenter);
            for (int i = 0; i < ReportReasons.Length; i++)
            {
                string reason = ReportReasons[i];
                float top = .775f - i * .077f;
                AddButton(panel, "Report Reason " + (i + 1), reason, new Vector2(.10f, top - .064f), new Vector2(.90f, top),
                    reason == DistressReason ? PixelUiTheme.Mint : PixelUiTheme.Tan, () => chooseReason?.Invoke(reason), legacyFont, 15);
            }
            AddButton(panel, "Cancel Report", "CANCEL", new Vector2(.34f, .03f), new Vector2(.66f, .10f), PixelUiTheme.Coral, cancel, legacyFont, 15);
            return modal;
        }

        internal static GameObject CreateModal(Transform parent, string title, Vector2 size)
        {
            GameObject modal = new GameObject(title + " Modal", typeof(RectTransform), typeof(Image));
            modal.transform.SetParent(parent, false);
            RectTransform modalRect = modal.GetComponent<RectTransform>();
            modalRect.anchorMin = Vector2.zero;
            modalRect.anchorMax = Vector2.one;
            modalRect.offsetMin = Vector2.zero;
            modalRect.offsetMax = Vector2.zero;
            Image shade = modal.GetComponent<Image>();
            shade.color = new Color(.20f, .13f, .08f, .56f);
            shade.raycastTarget = true;
            GameObject window = Panel(modal.transform, "Safety Window", PixelUiTheme.Cream);
            RectTransform rect = window.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            PixelUiTheme.ApplyWindow(window, true);
            return modal;
        }

        internal static GameObject Panel(Transform parent, string name, Color fill)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = fill;
            image.raycastTarget = false;
            return go;
        }

        internal static Text AddLabel(Transform parent, string value, Vector2 min, Vector2 max, int size, Font font, FontStyle style, TextAnchor alignment)
        {
            GameObject go = new GameObject(value, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Text label = go.GetComponent<Text>();
            label.font = font != null ? font : PixelUiTheme.BodyFont;
            label.text = value;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = PixelUiTheme.ReadableTextColor(parent, PixelUiTheme.Ink);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        internal static void AddButton(Transform parent, string name, string label, Vector2 min, Vector2 max, Color color, Action action, Font font, int fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Button button = go.GetComponent<Button>();
            PixelUiTheme.StyleButton(button, color);
            Text labelText = AddLabel(go.transform, label, Vector2.zero, Vector2.one, fontSize, font, FontStyle.Bold, TextAnchor.MiddleCenter);
            labelText.color = PixelUiTheme.ButtonTextColor(color);
            button.onClick.AddListener(() => action?.Invoke());
        }
    }
}
