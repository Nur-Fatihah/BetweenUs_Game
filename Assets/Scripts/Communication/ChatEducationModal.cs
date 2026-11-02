using UnityEngine;
using UnityEngine.UI;

namespace Crony
{
    /// <summary>
    /// Shown instead of posting a blocked chat message. It never just says "error": it explains the real-world
    /// effect of that kind of speech, names the manipulation behind it, and offers a kinder way to say what the
    /// player meant. Opens with the standard window spring; Rewrite reopens chat so the player can try again.
    /// </summary>
    public static class ChatEducationModal
    {
        struct Lesson { public string Title, RealWorld, Manipulation, Instead; }

        static GameObject open;

        static Lesson For(SafetyCategory category)
        {
            switch (category)
            {
                case SafetyCategory.Profanity: return new Lesson { Title = "Let's keep the park friendly",
                    RealWorld = "Swearing at people tends to escalate an argument: the other person hears an attack instead of your point, and the conversation shuts down.",
                    Manipulation = "Frustration is part of a deduction game, and it's exactly the tension the Impostor wants. Venting at others hands them the chaos.",
                    Instead = "Say what you actually feel: \"That vote surprised me. Can we go over the clues again?\"" };
                case SafetyCategory.TargetedInsult: return new Lesson { Title = "That one hits a person, not a problem",
                    RealWorld = "Repeated insults online are linked to anxiety, isolation and people leaving communities they loved. Every message adds to a pile the target carries alone.",
                    Manipulation = "Piling on feels like teamwork, but it's how a group gets turned against one person without evidence, which is exactly how an Impostor escapes.",
                    Instead = "Challenge the idea, not the person: \"I don't trust that alibi. Where were you when the lights went out?\"" };
                case SafetyCategory.IdentityAbuse: return new Lesson { Title = "Nobody here is a stereotype",
                    RealWorld = "Hate speech tells whole groups they don't belong. It feeds harassment and discrimination offline and pushes people out of shared spaces.",
                    Manipulation = "Blaming a group is one of the oldest manipulation tricks: it hands you a ready-made enemy so you stop asking who actually did it.",
                    Instead = "Judge actions, not identities. Point to what someone did this round: where they were, what they said, how they voted." };
                case SafetyCategory.SelfHarmEncouragement: return new Lesson { Title = "Words like that can reach someone who is struggling",
                    RealWorld = "Telling someone to hurt themselves can land on a person who is already having a hard time. Messages like this have contributed to real tragedies.",
                    Manipulation = "\"It's just a joke\" is how cruelty hides. You can't see who is reading, or what kind of day they're having.",
                    Instead = "If you're angry, take a breath or mute them. If someone seems low, check in: \"Hey, are you okay?\" Help lines are in Settings > Safety." };
                case SafetyCategory.Extremism: return new Lesson { Title = "Violence isn't a punchline",
                    RealWorld = "Extremist movements recruit through \"edgy\" jokes and memes that praise violence, testing who laughs along. Repetition slowly makes the unthinkable feel normal.",
                    Manipulation = "Recruiters look for people who feel alone or unheard, then offer belonging plus an enemy to blame. Feeling isolated makes anyone more open to that pitch, and that's not a personal failing.",
                    Instead = "Real belonging is built, not borrowed from hate. Find your people in groups that make things together: teams, clubs, even this township crew." };
                case SafetyCategory.ThreatOrDoxxing: return new Lesson { Title = "Keep it inside the game",
                    RealWorld = "Real-world threats and sharing personal details can lead to stalking and harm. In many places it's a crime, and it can follow someone for years.",
                    Manipulation = "Making someone feel unsafe to win an argument means the argument was already lost.",
                    Instead = "Game talk is fine: \"I'm voting you out next round!\" If someone threatens you, report them from the chat." };
                case SafetyCategory.SexualHarassment: return new Lesson { Title = "That's not what this chat is for",
                    RealWorld = "Unwanted sexual comments make people feel unsafe and are one of the top reasons players quit online games.",
                    Manipulation = "Pressure dressed up as flirting still takes away someone's choice to just play.",
                    Instead = "Keep chat about the game. Friendship grows from playing well together." };
                case SafetyCategory.Impersonation: return new Lesson { Title = "Be yourself, not the system",
                    RealWorld = "Pretending to be staff or the game is a classic scam setup: it borrows trust that isn't yours.",
                    Manipulation = "Fake authority works because people are taught to obey official-looking messages.",
                    Instead = "Make your case as you: \"I think it's Dev, and here's why.\"" };
                default: return new Lesson { Title = "Dodging the filter still counts",
                    RealWorld = "Spacing out letters or swapping symbols carries the same message to the reader; only the filter is fooled.",
                    Manipulation = "Finding loopholes can feel like winning, but the person on the other end still reads it.",
                    Instead = "Say what you mean in a way you'd be happy for everyone to read." };
            }
        }

        /// <param name="consequence">What happened in Crony (warning, chat pause, removal…). Empty for an unpunished interception.</param>
        public static void Show(SafetyCategory category, string consequence)
        {
            TownshipTextChatUI chat = TownshipTextChatUI.Instance;
            if (chat == null) return;
            if (open != null) Object.Destroy(open);
            Lesson lesson = For(category);
            Font font = PixelUiTheme.BodyFont;
            open = SafetyUI.CreateModal(chat.transform, "Chat Pause", new Vector2(900f, 680f));
            open.transform.SetAsLastSibling();
            Transform window = open.transform.Find("Safety Window");

            GameObject band = SafetyUI.Panel(window, "Alert Band", PixelUiTheme.Coral);
            Anchor(band.GetComponent<RectTransform>(), new Vector2(.04f, .86f), new Vector2(.96f, .955f));
            band.GetComponent<Image>().sprite = PixelUiTheme.GetRoundSprite();
            band.GetComponent<Image>().type = Image.Type.Sliced;
            SafetyUI.AddLabel(band.transform, "MESSAGE NOT SENT  ·  " + lesson.Title, new Vector2(.03f, 0f), new Vector2(.97f, 1f), 22, font, FontStyle.Bold, TextAnchor.MiddleLeft).color = PixelUiTheme.Ink;

            Section(window, "WHY IT MATTERS", lesson.RealWorld, .59f, PixelUiTheme.Coral, font);
            Section(window, "WHAT'S REALLY GOING ON", lesson.Manipulation, .37f, PixelUiTheme.Tan, font);
            Section(window, "TRY THIS INSTEAD", lesson.Instead, .15f, PixelUiTheme.Mint, font);
            if (!string.IsNullOrEmpty(consequence))
                SafetyUI.AddLabel(window, "In Crony: " + consequence, new Vector2(.05f, .1f), new Vector2(.95f, .15f), 14, font, FontStyle.Normal, TextAnchor.MiddleLeft);

            GameObject modal = open;
            SafetyUI.AddButton(window, "Rewrite Message", "REWRITE MY MESSAGE", new Vector2(.05f, .025f), new Vector2(.48f, .09f), PixelUiTheme.Mint,
                () => { Object.Destroy(modal); TownshipTextChatUI.Instance?.Open(); }, font, 16);
            SafetyUI.AddButton(window, "Understood", "I UNDERSTAND", new Vector2(.52f, .025f), new Vector2(.95f, .09f), PixelUiTheme.Tan,
                () => Object.Destroy(modal), font, 16);
            Typography.Apply(window);
        }

        static void Section(Transform window, string heading, string body, float bottom, Color accent, Font font)
        {
            GameObject card = SafetyUI.Panel(window, heading, PixelUiTheme.Cream);
            Anchor(card.GetComponent<RectTransform>(), new Vector2(.04f, bottom), new Vector2(.96f, bottom + .2f));
            PixelUiTheme.ApplyCard(card, false);
            GameObject stripe = SafetyUI.Panel(card.transform, "Accent", accent);
            Anchor(stripe.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(.012f, 1f));
            SafetyUI.AddLabel(card.transform, heading, new Vector2(.04f, .64f), new Vector2(.97f, .95f), 15, font, FontStyle.Bold, TextAnchor.MiddleLeft);
            SafetyUI.AddLabel(card.transform, body, new Vector2(.04f, .05f), new Vector2(.97f, .66f), 15, font, FontStyle.Normal, TextAnchor.UpperLeft);
        }

        static void Anchor(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
