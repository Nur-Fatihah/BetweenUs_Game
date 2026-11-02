using System;
using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>Tabbed guide: goals per role, controls, every checkpoint mini-game, and meeting rules.</summary>
    public sealed class HowToPlayUI : MonoBehaviour
    {
        static readonly string[] TabNames = { "THE GOAL", "CONTROLS", "TASK GAMES", "MEETINGS & TIPS" };
        static readonly (TownshipTaskKind kind, string place)[] Checkpoints =
        {
            (TownshipTaskKind.NasiLemak, "Hawker Stalls"), (TownshipTaskKind.IceCream, "Convenience Store"),
            (TownshipTaskKind.ParkBench, "Recreation Park"), (TownshipTaskKind.BadmintonNet, "Community Hall"),
            (TownshipTaskKind.CommunityPermits, "Administrative Office"), (TownshipTaskKind.BusSchedule, "Bus Stop"),
            (TownshipTaskKind.LakeCleanup, "Central Lake"), (TownshipTaskKind.Router, "Cellular Tower"),
            (TownshipTaskKind.SolarBatteries, "Garden Walkway"), (TownshipTaskKind.NoticeBoard, "Notice Board")
        };

        Font font;
        bool largeText;
        Transform window;
        RectTransform page;
        Button[] tabs;

        public static HowToPlayUI Show(Transform parent, Font font, bool largeText, Action close)
        {
            GameObject modal = SafetyUI.CreateModal(parent, "How To Play", new Vector2(1060f, 780f));
            HowToPlayUI view = modal.AddComponent<HowToPlayUI>();
            view.font = font;
            view.largeText = largeText;
            view.window = modal.transform.Find("Safety Window");
            SafetyUI.AddLabel(view.window, "HOW TO PLAY", new Vector2(.04f, .91f), new Vector2(.6f, .98f), 30, font, FontStyle.Bold, TextAnchor.MiddleLeft);
            SafetyUI.AddButton(view.window, "Close How To Play", "DONE", new Vector2(.82f, .915f), new Vector2(.96f, .975f), PixelUiTheme.Amber,
                () => { Destroy(modal); close?.Invoke(); }, font, 16);
            view.tabs = new Button[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                float x = .04f + i * .232f;
                SafetyUI.AddButton(view.window, "How To Tab " + i, TabNames[i], new Vector2(x, .835f), new Vector2(x + .222f, .895f), PixelUiTheme.Tan, () => view.Select(index), font, 15);
                view.tabs[i] = view.window.Find("How To Tab " + i).GetComponent<Button>();
            }
            view.Select(0);
            return view;
        }

        void Select(int index)
        {
            for (int i = 0; i < tabs.Length; i++) PixelUiTheme.SetTabState(tabs[i], i == index);
            if (page != null) Destroy(page.gameObject);
            page = new GameObject("How To Page", typeof(RectTransform)).GetComponent<RectTransform>();
            page.SetParent(window, false);
            page.anchorMin = new Vector2(.04f, .04f);
            page.anchorMax = new Vector2(.96f, .815f);
            page.offsetMin = page.offsetMax = Vector2.zero;
            switch (index)
            {
                case 0: BuildGoal(); break;
                case 1: ControlsGuide.Render(page, font, largeText); break;
                case 2: BuildTasks(); break;
                default: BuildMeetings(); break;
            }
            Typography.Apply(window);
        }

        int Size(int size) => largeText ? Mathf.RoundToInt(size * 1.12f) : size;

        void BuildGoal()
        {
            (string title, Color color, string body)[] roles =
            {
                ("CREWMATE", PixelUiTheme.Mint, "Most players.\n\nFinish the community tasks, help fallen neighbours up (E), and vote out the Impostor.\n\nYOU WIN WHEN\n• every task is done, or\n• the Impostor is voted out, or\n• the park stays safe until closing time."),
                ("IMPOSTOR", PixelUiTheme.Coral, "One secret player.\n\nBlend in, take down crewmates (F), sabotage the park (Q), recruit one crony (G), and slip through hollow tree stumps (E) to escape.\n\nYOU WIN WHEN\n• your team equals the crew left standing, or\n• a sabotage isn't fixed in time."),
                ("CRONY", Hex("B48AD0"), "A crewmate the Impostor recruited mid-match. You learn who the Impostor is.\n\nCover for them, sabotage (Q), and fake tasks: your work no longer counts.\n\nYOU WIN WHEN\n• the Impostor wins. If the Impostor is voted out, you lose too.")
            };
            for (int i = 0; i < roles.Length; i++)
            {
                float x = i * .34f;
                GameObject card = SafetyUI.Panel(page, roles[i].title + " Card", PixelUiTheme.Cream);
                RectTransform rect = card.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(x, .15f);
                rect.anchorMax = new Vector2(x + .32f, 1f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                PixelUiTheme.ApplyCard(card, false);
                GameObject band = SafetyUI.Panel(card.transform, "Role Band", roles[i].color);
                RectTransform bandRect = band.GetComponent<RectTransform>();
                bandRect.anchorMin = new Vector2(0f, .87f);
                bandRect.anchorMax = Vector2.one;
                bandRect.offsetMin = bandRect.offsetMax = Vector2.zero;
                Text title = SafetyUI.AddLabel(band.transform, roles[i].title, Vector2.zero, Vector2.one, Size(22), font, FontStyle.Bold, TextAnchor.MiddleCenter);
                title.color = PixelUiTheme.ButtonTextColor(roles[i].color);
                SafetyUI.AddLabel(card.transform, roles[i].body, new Vector2(.07f, .03f), new Vector2(.93f, .84f), Size(17), font, FontStyle.Normal, TextAnchor.UpperLeft);
            }
            SafetyUI.AddLabel(page, "Rounds last 8 minutes. You only see a small circle around you, and a lights-out sabotage makes it smaller, so stay alert.",
                new Vector2(0f, 0f), new Vector2(1f, .12f), Size(15), font, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        void BuildTasks()
        {
            SafetyUI.AddLabel(page, "Walk to a checkpoint and press E. Win a round to finish each step. Mistakes only cost time.",
                new Vector2(0f, .93f), new Vector2(1f, 1f), Size(15), font, FontStyle.Bold, TextAnchor.MiddleLeft);
            for (int i = 0; i < Checkpoints.Length; i++)
            {
                TaskMiniGame game = TaskMiniGame.Create(Checkpoints[i].kind);
                float x = i % 2 * .5f, top = .91f - i / 2 * .18f;
                GameObject card = SafetyUI.Panel(page, game.Title + " Card", PixelUiTheme.Cream);
                RectTransform rect = card.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(x, top - .17f);
                rect.anchorMax = new Vector2(x + .49f, top);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                PixelUiTheme.ApplyCard(card, false);
                SafetyUI.AddLabel(card.transform, Checkpoints[i].place.ToUpperInvariant() + "  ·  " + game.Title, new Vector2(.03f, .62f), new Vector2(.97f, .96f), Size(14), font, FontStyle.Bold, TextAnchor.MiddleLeft);
                SafetyUI.AddLabel(card.transform, game.HowTo, new Vector2(.03f, .04f), new Vector2(.97f, .64f), Size(12), font, FontStyle.Normal, TextAnchor.UpperLeft);
            }
        }

        void BuildMeetings()
        {
            (string title, string body)[] cards =
            {
                ("MEETINGS", "• Found someone down? Press R to report. Everyone gathers to talk and vote.\n• Each player can call one emergency meeting with M.\n• Vote for a suspect or skip. A player is sent home only with more than half the votes.\n• The game then reveals if they were the Impostor, the crony, or innocent."),
                ("SABOTAGE", "• An alarm sounds and a timer appears at the top of the screen.\n• Go to the marked checkpoint and press E to repair it before time runs out.\n• Lights-out shrinks everyone's vision until it is fixed."),
                ("CHAT & VOICE", "• T opens chat. Quick phrases help you speak fast in meetings.\n• Voice is optional: turn it on in Settings > Audio, then hold V to talk.\n• Click any message or player to mute, block, or report them.\n• Bullying, hate and threats are blocked. Read the rules in Settings > Safety."),
                ("TIPS", "• Watch who walks away from a body.\n• A crewmate who never finishes a task might be the crony.\n• Stick together: the Impostor hunts people who wander alone.\n• Listen: every action has its own sound.")
            };
            for (int i = 0; i < cards.Length; i++)
            {
                float x = i % 2 * .51f, top = 1f - i / 2 * .51f;
                GameObject card = SafetyUI.Panel(page, cards[i].title + " Card", PixelUiTheme.Cream);
                RectTransform rect = card.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(x, top - .49f);
                rect.anchorMax = new Vector2(x + .49f, top);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                PixelUiTheme.ApplyCard(card, false);
                SafetyUI.AddLabel(card.transform, cards[i].title, new Vector2(.05f, .8f), new Vector2(.95f, .96f), Size(18), font, FontStyle.Bold, TextAnchor.MiddleLeft);
                SafetyUI.AddLabel(card.transform, cards[i].body, new Vector2(.05f, .04f), new Vector2(.95f, .8f), Size(15), font, FontStyle.Normal, TextAnchor.UpperLeft);
            }
        }

        static Color Hex(string value) => ColorUtility.TryParseHtmlString("#" + value, out Color color) ? color : Color.white;
    }
}
