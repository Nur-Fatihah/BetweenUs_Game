using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>The single list of controls, drawn by both How to Play and Settings so the two never disagree.</summary>
    public static class ControlsGuide
    {
        sealed class Section
        {
            public readonly string Title;
            public readonly string[,] Bindings;
            public Section(string title, string[,] bindings) { Title = title; Bindings = bindings; }
        }

        static readonly Section[][] Columns =
        {
            new[]
            {
                new Section("MOVE & EXPLORE", new[,]
                {
                    { "W A S D", "Walk (arrow keys work too)" },
                    { "TAB", "Map: see and track your tasks" },
                    { "ESC", "Close a window or pause" },
                    { "C", "Dressing room (from the menu)" }
                }),
                new Section("EVERYONE", new[,]
                {
                    { "E", "Use a task, fix a sabotage, help someone up" },
                    { "R", "Report someone who is down" },
                    { "M", "Call your one emergency meeting" }
                }),
                new Section("TALK", new[,]
                {
                    { "T", "Open chat (Enter sends)" },
                    { "V", "Push to talk, when voice is on" }
                })
            },
            new[]
            {
                new Section("IMPOSTOR & CRONY", new[,]
                {
                    { "F", "Impostor: take down a nearby crewmate" },
                    { "G", "Impostor: recruit a crony (once, 45 s in)" },
                    { "E", "Impostor at a hollow stump: travel to the next" },
                    { "Q", "Sabotage. Online: N picks it, Q starts it" }
                }),
                new Section("TASK MINI-GAMES", new[,]
                {
                    { "SPACE", "Main action: scoop, hammer, swing" },
                    { "1 - 5", "Pick a choice: binders, freezer pops" },
                    { "A / D", "Steer or slide: boat, dial, solar panel" },
                    { "MOUSE", "Click buttons and moving targets" }
                }),
                new Section("ON-SCREEN BUTTONS", new[,]
                {
                    { "TOUCH", "Every key above also has a button" }
                })
            }
        };

        /// <summary>Fills a stretched parent with two columns of keycaps and descriptions.</summary>
        public static void Render(Transform parent, Font font, bool largeText)
        {
            const float unit = .068f;
            int body = largeText ? 15 : 14;
            for (int column = 0; column < Columns.Length; column++)
            {
                float x = .03f + column * .49f, y = .97f;
                foreach (Section section in Columns[column])
                {
                    SafetyUI.AddLabel(parent, section.Title, new Vector2(x, y - unit), new Vector2(x + .46f, y), body + 1, font, FontStyle.Bold, TextAnchor.MiddleLeft);
                    y -= unit;
                    for (int i = 0; i < section.Bindings.GetLength(0); i++)
                    {
                        GameObject cap = SafetyUI.Panel(parent, "Keycap " + section.Bindings[i, 0], PixelUiTheme.Chocolate);
                        RectTransform rect = cap.GetComponent<RectTransform>();
                        rect.anchorMin = new Vector2(x, y - unit + .008f);
                        rect.anchorMax = new Vector2(x + .1f, y - .008f);
                        rect.offsetMin = rect.offsetMax = Vector2.zero;
                        Image image = cap.GetComponent<Image>();
                        image.sprite = PixelUiTheme.GetRoundSprite();
                        image.type = Image.Type.Sliced;
                        Text key = SafetyUI.AddLabel(cap.transform, section.Bindings[i, 0], Vector2.zero, Vector2.one, body - 2, font, FontStyle.Bold, TextAnchor.MiddleCenter);
                        key.color = PixelUiTheme.Cream;
                        SafetyUI.AddLabel(parent, section.Bindings[i, 1], new Vector2(x + .115f, y - unit), new Vector2(x + .47f, y), body, font, FontStyle.Normal, TextAnchor.MiddleLeft);
                        y -= unit;
                    }
                    y -= unit * .35f;
                }
            }
        }
    }
}
