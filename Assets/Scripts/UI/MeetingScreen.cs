using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Crony
{
    /// <summary>Discussion → Voting → Reveal → Ejection. Shared by offline practice and online matches.</summary>
    public enum MeetingStage : byte { None, Discussion, Voting, Reveal, Ejection }

    public sealed class MeetingSeat
    {
        public long Id;
        public string Name;
        public Color Color;
        public bool CanVote;
        public bool HasVoted;
        public bool IsLocal;
        /// <summary>The player's avatar layers, drawn as a breathing portrait. Optional.</summary>
        public SpriteRenderer[] Portrait;
    }

    /// <summary>A snapshot the meeting screen draws. Ids are player ids; Skip is the "skip vote" target.</summary>
    public sealed class MeetingView
    {
        public const long NoVote = long.MinValue, Skip = -1;
        public MeetingStage Stage;
        public float SecondsLeft, StageLength = 1f;
        public bool BodyReport;
        public string Headline = "";
        public readonly List<MeetingSeat> Seats = new List<MeetingSeat>();
        public long LocalVote = NoVote;
        public bool LocalCanVote;
        public readonly List<string> Clues = new List<string>();
        /// <summary>Filled at Reveal: target id (or Skip) → voter ids.</summary>
        public readonly Dictionary<long, List<long>> VotesByTarget = new Dictionary<long, List<long>>();
        public string Outcome = "";
        public long EjectedId = NoVote;
        public string EjectedRole = "";
    }

    public sealed class MeetingScreen : MonoBehaviour
    {
        static readonly string[] Phrases = { "I was doing my tasks.", "Where was everyone?", "I need more evidence.", "Let's skip this one." };
        Func<MeetingView> read;
        Action<long> seatClicked;
        Action skip;
        Font font;
        bool largeText;
        Transform window;
        RectTransform seatsArea, feedArea, timerFill, banner;
        Text stageLabel, timerLabel, hintLabel, headline, cluesLabel;
        Button skipButton;
        string seatSignature = "", feedSignature = "";
        readonly List<RectTransform> portraits = new List<RectTransform>();
        readonly List<(RectTransform rect, Vector2 home, float phase)> fireflies = new List<(RectTransform, Vector2, float)>();
        static Sprite stumpSprite;
        int feedFromIndex;

        /// <param name="onSeat">Discussion: accuse. Voting: vote. The data source decides.</param>
        static MeetingScreen open;
        public static bool IsOpen => open != null;
        void OnDestroy() { if (open == this) open = null; }

        public static MeetingScreen Show(Transform parent, Func<MeetingView> source, Action<long> onSeat, Action onSkip, bool largeText)
        {
            GameObject modal = SafetyUI.CreateModal(parent, "Meeting", new Vector2(1240f, 820f));
            MeetingScreen screen = modal.AddComponent<MeetingScreen>();
            open = screen;
            screen.read = source;
            screen.seatClicked = onSeat;
            screen.skip = onSkip;
            screen.font = PixelUiTheme.BodyFont;
            screen.largeText = largeText;
            screen.feedFromIndex = TextChatManager.Instance != null ? TextChatManager.Instance.History.Count : 0;
            screen.Build(source());
            return screen;
        }

        int Size(int size) => largeText ? Mathf.RoundToInt(size * 1.12f) : size;

        void Build(MeetingView view)
        {
            window = transform.Find("Safety Window");
            SafetyUI.AddLabel(window, view.BodyReport ? "BODY REPORTED" : "EMERGENCY MEETING", new Vector2(.03f, .915f), new Vector2(.6f, .98f), Size(30), font, FontStyle.Bold, TextAnchor.MiddleLeft);
            headline = SafetyUI.AddLabel(window, view.Headline, new Vector2(.03f, .865f), new Vector2(.97f, .915f), Size(16), font, FontStyle.Normal, TextAnchor.MiddleLeft);

            GameObject pill = SafetyUI.Panel(window, "Stage Pill", PixelUiTheme.Chocolate);
            Anchor(pill.GetComponent<RectTransform>(), new Vector2(.62f, .925f), new Vector2(.97f, .975f));
            pill.GetComponent<Image>().sprite = PixelUiTheme.GetRoundSprite();
            pill.GetComponent<Image>().type = Image.Type.Sliced;
            stageLabel = SafetyUI.AddLabel(pill.transform, "", new Vector2(.04f, 0f), new Vector2(.7f, 1f), Size(16), font, FontStyle.Bold, TextAnchor.MiddleLeft);
            stageLabel.color = PixelUiTheme.Cream;
            timerLabel = SafetyUI.AddLabel(pill.transform, "", new Vector2(.7f, 0f), new Vector2(.96f, 1f), Size(18), font, FontStyle.Bold, TextAnchor.MiddleRight);
            timerLabel.color = PixelUiTheme.Gold;
            GameObject track = SafetyUI.Panel(window, "Timer Track", new Color(0f, 0f, 0f, .15f));
            Anchor(track.GetComponent<RectTransform>(), new Vector2(.03f, .845f), new Vector2(.97f, .855f));
            timerFill = SafetyUI.Panel(track.transform, "Timer Fill", PixelUiTheme.Amber).GetComponent<RectTransform>();
            Anchor(timerFill, Vector2.zero, Vector2.one);

            // The meeting table: an ancient cross-cut stump, with fireflies drifting over it between the seats.
            RectTransform stump = Area("Ancient Stump", new Vector2(.06f, .16f), new Vector2(.55f, .82f));
            Image stumpImage = stump.gameObject.AddComponent<Image>();
            stumpImage.sprite = StumpSprite();
            stumpImage.preserveAspect = true;
            stumpImage.raycastTarget = false;
            RectTransform glow = Area("Fireflies", new Vector2(.03f, .14f), new Vector2(.58f, .83f));
            for (int i = 0; i < 12; i++)
            {
                GameObject fly = SafetyUI.Panel(glow, "Firefly", new Color(1f, .93f, .55f, .8f));
                RectTransform rect = fly.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(Random.value, Random.value);
                rect.sizeDelta = new Vector2(16f, 16f);
                fly.GetComponent<Image>().sprite = PixelUiTheme.GetGlowSprite();
                fireflies.Add((rect, rect.anchorMin, Random.value * 6.28f));
            }
            seatsArea = Area("Seats", new Vector2(.03f, .14f), new Vector2(.58f, .83f));
            SafetyUI.AddLabel(window, "WHAT PEOPLE SAID", new Vector2(.61f, .79f), new Vector2(.97f, .83f), Size(15), font, FontStyle.Bold, TextAnchor.MiddleLeft);
            feedArea = Area("Dialogue Feed", new Vector2(.61f, .37f), new Vector2(.97f, .79f));
            SafetyUI.AddLabel(window, "CLUES", new Vector2(.61f, .32f), new Vector2(.97f, .36f), Size(15), font, FontStyle.Bold, TextAnchor.MiddleLeft);
            cluesLabel = SafetyUI.AddLabel(window, "", new Vector2(.61f, .2f), new Vector2(.97f, .32f), Size(13), font, FontStyle.Normal, TextAnchor.UpperLeft);
            for (int i = 0; i < Phrases.Length; i++)
            {
                string phrase = Phrases[i];
                float x = .61f + i % 2 * .18f, y = .135f - i / 2 * .055f;
                SafetyUI.AddButton(window, "Say " + i, phrase, new Vector2(x, y), new Vector2(x + .175f, y + .05f), PixelUiTheme.Linen, () => TextChatManager.Instance?.Send(phrase), font, Size(13));
            }
            SafetyUI.AddButton(window, "Open Chat", "TYPE IN CHAT  [T]", new Vector2(.61f, .025f), new Vector2(.97f, .07f), PixelUiTheme.Sky, () => TownshipTextChatUI.Instance?.Open(), font, Size(14));

            hintLabel = SafetyUI.AddLabel(window, "", new Vector2(.03f, .07f), new Vector2(.4f, .135f), Size(15), font, FontStyle.Bold, TextAnchor.MiddleLeft);
            SafetyUI.AddButton(window, "Skip Vote", "SKIP VOTE", new Vector2(.42f, .075f), new Vector2(.58f, .13f), PixelUiTheme.Tan, () => skip?.Invoke(), font, Size(15));
            skipButton = window.Find("Skip Vote").GetComponent<Button>();
            Refresh(view, true);
            Typography.Apply(window);
        }

        RectTransform Area(string name, Vector2 min, Vector2 max)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(window, false);
            Anchor(rect, min, max);
            return rect;
        }

        static void Anchor(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        void Update()
        {
            if (read != null) Refresh(read(), false);
            float t = Time.unscaledTime;
            // Portraits breathe gently, each on its own phase, so the circle feels alive while people talk.
            for (int i = 0; i < portraits.Count; i++)
                if (portraits[i] != null) portraits[i].localScale = new Vector3(1f - .012f * Mathf.Sin(t * 2.2f + i), 1f + .025f * Mathf.Sin(t * 2.2f + i), 1f);
            foreach (var fly in fireflies)
            {
                Vector2 drift = new Vector2(Mathf.Sin(t * .4f + fly.phase), Mathf.Cos(t * .33f + fly.phase * 1.7f)) * .05f;
                fly.rect.anchorMin = fly.rect.anchorMax = fly.home + drift;
                Image image = fly.rect.GetComponent<Image>();
                image.color = new Color(1f, .93f, .55f, .35f + .45f * (Mathf.Sin(t * 1.8f + fly.phase * 3f) * .5f + .5f));
            }
        }

        /// <summary>Cross-cut ancient stump: warm growth rings, a dark bark rim and a few moss patches.</summary>
        static Sprite StumpSprite()
        {
            if (stumpSprite != null) return stumpSprite;
            const int size = 96;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Meeting Stump" };
            Color bark = new Color32(92, 66, 58, 255), ringDark = new Color32(176, 132, 90, 255), ringLight = new Color32(212, 170, 116, 255), moss = new Color32(110, 140, 76, 255);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - 47.5f, dy = (y - 47.5f) * 1.08f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) + Mathf.Sin(Mathf.Atan2(dy, dx) * 5f) * 1.2f;
                    Color c = new Color(0f, 0f, 0f, 0f);
                    if (d < 46f) c = bark;
                    if (d < 42f) c = Mathf.FloorToInt(d / 3.2f) % 2 == 0 ? ringLight : ringDark;
                    if (d < 42f && d > 39f && (x * 7 + y * 3) % 23 < 6) c = moss;
                    if (d < 3f) c = ringDark;
                    texture.SetPixel(x, y, c);
                }
            texture.Apply(false, true);
            stumpSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
            return stumpSprite;
        }

        void Refresh(MeetingView view, bool force)
        {
            stageLabel.text = view.Stage == MeetingStage.Discussion ? "DISCUSS" : view.Stage == MeetingStage.Voting ? "VOTE" : view.Stage == MeetingStage.Reveal ? "VOTES ARE IN" : "RESULT";
            timerLabel.text = Mathf.CeilToInt(Mathf.Max(0f, view.SecondsLeft)) + "s";
            timerFill.anchorMax = new Vector2(Mathf.Clamp01(view.SecondsLeft / Mathf.Max(.01f, view.StageLength)), 1f);
            headline.text = view.Headline;
            bool canVoteNow = view.Stage == MeetingStage.Voting && view.LocalCanVote && view.LocalVote == MeetingView.NoVote;
            skipButton.interactable = canVoteNow;
            skipButton.gameObject.SetActive(view.Stage == MeetingStage.Voting);
            hintLabel.text = !view.LocalCanVote && view.Stage <= MeetingStage.Voting ? "You can't vote this meeting, but you can still watch and chat."
                : view.Stage == MeetingStage.Discussion ? "Talk it out. Click a player to accuse them. Voting opens soon."
                : view.Stage == MeetingStage.Voting ? (view.LocalVote == MeetingView.NoVote ? "Click a player to vote, or skip. Your vote is final." : "Vote locked in. Waiting for the others…")
                : view.Stage == MeetingStage.Reveal ? "Here is how everyone voted." : view.Outcome;
            cluesLabel.text = view.Clues.Count == 0 ? "No clues yet." : "• " + string.Join("\n• ", view.Clues);

            string signature = SeatSignature(view);
            if (force || signature != seatSignature) { seatSignature = signature; BuildSeats(view); Typography.Apply(seatsArea); }
            RefreshFeed(view, force);
            UpdateBanner(view);
        }

        static string SeatSignature(MeetingView view)
        {
            StringBuilder s = new StringBuilder().Append((int)view.Stage).Append('|').Append(view.LocalVote).Append('|').Append(view.LocalCanVote).Append('|');
            foreach (MeetingSeat seat in view.Seats) s.Append(seat.Id).Append(seat.CanVote ? 'a' : 'o').Append(seat.HasVoted ? 'v' : '-').Append(',');
            foreach (var pair in view.VotesByTarget) s.Append(pair.Key).Append(':').Append(pair.Value.Count).Append(';');
            return s.ToString();
        }

        void BuildSeats(MeetingView view)
        {
            portraits.Clear();
            for (int i = seatsArea.childCount - 1; i >= 0; i--) Destroy(seatsArea.GetChild(i).gameObject);
            bool reveal = view.Stage >= MeetingStage.Reveal;
            for (int i = 0; i < view.Seats.Count; i++)
            {
                MeetingSeat seat = view.Seats[i];
                float x = i % 2 * .5f, top = 1f - i / 2 * .228f;
                GameObject card = SafetyUI.Panel(seatsArea, "Seat " + seat.Name, PixelUiTheme.Cream);
                Anchor(card.GetComponent<RectTransform>(), new Vector2(x, top - .215f), new Vector2(x + .485f, top));
                PixelUiTheme.ApplyCard(card, false);
                Image cardImage = card.GetComponent<Image>();
                bool votedForThem = view.LocalVote == seat.Id;
                bool ejected = view.Stage == MeetingStage.Ejection && view.EjectedId == seat.Id;
                // Slightly translucent so the stump table reads through the circle of seats.
                cardImage.color = new Color(1f, 1f, 1f, .88f);
                if (!seat.CanVote) cardImage.color = new Color(.78f, .78f, .78f, .88f);
                if (votedForThem || ejected) cardImage.color = ejected ? new Color(1f, .78f, .74f, .95f) : new Color(1f, .92f, .7f, .95f);

                GameObject swatch = SafetyUI.Panel(card.transform, "Portrait Frame", seat.Color);
                Anchor(swatch.GetComponent<RectTransform>(), new Vector2(.03f, .1f), new Vector2(.18f, .9f));
                swatch.GetComponent<Image>().sprite = PixelUiTheme.GetDotSprite();
                swatch.GetComponent<Image>().preserveAspect = true;
                if (seat.Portrait != null) portraits.Add(Portrait(swatch.transform, seat.Portrait));
                SafetyUI.AddLabel(card.transform, seat.IsLocal && seat.Name == "You" ? "You" : seat.Name + (seat.IsLocal ? "  (you)" : ""), new Vector2(.2f, .5f), new Vector2(.97f, .92f), Size(17), font, FontStyle.Bold, TextAnchor.MiddleLeft);
                string status = !seat.CanVote ? "OUT · can't vote" : reveal ? "" : seat.HasVoted ? "✓ VOTED" : view.Stage == MeetingStage.Voting ? "deciding…" : "";
                SafetyUI.AddLabel(card.transform, status, new Vector2(.2f, .1f), new Vector2(.97f, .5f), Size(13), font, FontStyle.Normal, TextAnchor.MiddleLeft);

                if (reveal && view.VotesByTarget.TryGetValue(seat.Id, out List<long> voters))
                {
                    RectTransform chips = new GameObject("Chips", typeof(RectTransform)).GetComponent<RectTransform>();
                    chips.SetParent(card.transform, false);
                    Anchor(chips, new Vector2(.2f, .06f), new Vector2(.97f, .42f));
                    VoterChips(chips, view, voters);
                    SafetyUI.AddLabel(card.transform, voters.Count + (voters.Count == 1 ? " vote" : " votes"), new Vector2(.6f, .5f), new Vector2(.96f, .92f), Size(14), font, FontStyle.Bold, TextAnchor.MiddleRight);
                }
                if (seat.CanVote)
                {
                    long id = seat.Id;
                    Button button = card.AddComponent<Button>();
                    cardImage.raycastTarget = true;
                    button.transition = Selectable.Transition.ColorTint;
                    button.interactable = !seat.IsLocal && view.LocalCanVote &&
                        (view.Stage == MeetingStage.Discussion || (view.Stage == MeetingStage.Voting && view.LocalVote == MeetingView.NoVote));
                    button.onClick.AddListener(() => { GameAudioManager.PlayUiClick(); seatClicked?.Invoke(id); });
                    card.AddComponent<UIButtonJuice>();
                }
            }
            if (reveal && view.VotesByTarget.TryGetValue(MeetingView.Skip, out List<long> skipped))
            {
                GameObject row = SafetyUI.Panel(seatsArea, "Skipped Row", new Color(0f, 0f, 0f, 0f));
                Anchor(row.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, .075f));
                SafetyUI.AddLabel(row.transform, "SKIPPED (" + skipped.Count + ")", new Vector2(0f, 0f), new Vector2(.25f, 1f), Size(14), font, FontStyle.Bold, TextAnchor.MiddleLeft);
                RectTransform chips = new GameObject("Chips", typeof(RectTransform)).GetComponent<RectTransform>();
                chips.SetParent(row.transform, false);
                Anchor(chips, new Vector2(.25f, 0f), Vector2.one);
                VoterChips(chips, view, skipped);
            }
        }

        /// <summary>Stacks the avatar's sprite layers (skipping the ground shadow) into a small UI portrait.</summary>
        static RectTransform Portrait(Transform frame, SpriteRenderer[] layers)
        {
            RectTransform holder = new GameObject("Portrait", typeof(RectTransform)).GetComponent<RectTransform>();
            holder.SetParent(frame, false);
            holder.anchorMin = new Vector2(.08f, .02f);
            holder.anchorMax = new Vector2(.92f, 1.05f);
            holder.offsetMin = holder.offsetMax = Vector2.zero;
            holder.pivot = new Vector2(.5f, 0f);
            for (int i = 1; i < layers.Length; i++)
            {
                SpriteRenderer layer = layers[i];
                if (layer == null || layer.sprite == null || !layer.enabled) continue;
                Image image = new GameObject(layer.name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.transform.SetParent(holder, false);
                RectTransform rect = image.rectTransform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                image.sprite = layer.sprite;
                image.color = layer.color;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            return holder;
        }

        void VoterChips(Transform parent, MeetingView view, List<long> voters)
        {
            for (int v = 0; v < voters.Count; v++)
            {
                MeetingSeat voter = view.Seats.Find(s => s.Id == voters[v]);
                GameObject chip = SafetyUI.Panel(parent, "Voter Chip", voter != null ? voter.Color : Color.gray);
                RectTransform rect = chip.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
                rect.sizeDelta = new Vector2(22f, 22f);
                rect.anchoredPosition = new Vector2(12f + v * 25f, 0f);
                chip.GetComponent<Image>().sprite = PixelUiTheme.GetDotSprite();
            }
        }

        /// <summary>Meeting-channel chat drawn as dialogue boxes with a speaker tab in the speaker's colour.</summary>
        void RefreshFeed(MeetingView view, bool force)
        {
            var history = TextChatManager.Instance != null ? TextChatManager.Instance.History : null;
            List<ChatMessage> lines = new List<ChatMessage>();
            if (history != null)
                for (int i = Mathf.Min(feedFromIndex, history.Count); i < history.Count; i++)
                    if (history[i].Channel == ChatChannel.Meeting || history[i].Channel == ChatChannel.Ghost) lines.Add(history[i]);
            string signature = lines.Count == 0 ? "" : lines.Count + lines[lines.Count - 1].Id;
            if (!force && signature == feedSignature) return;
            feedSignature = signature;
            for (int i = feedArea.childCount - 1; i >= 0; i--) Destroy(feedArea.GetChild(i).gameObject);
            if (lines.Count == 0)
            {
                SafetyUI.AddLabel(feedArea, "No one has spoken yet. Share what you saw!", Vector2.zero, Vector2.one, Size(14), font, FontStyle.Normal, TextAnchor.MiddleCenter);
                return;
            }
            const int shown = 5;
            int first = Mathf.Max(0, lines.Count - shown);
            for (int i = first; i < lines.Count; i++)
            {
                ChatMessage message = lines[i];
                float top = 1f - (i - first) * .2f;
                MeetingSeat speaker = view.Seats.Find(s => s.Name == message.Name);
                Color tint = speaker != null ? speaker.Color : PixelUiTheme.Tan;
                GameObject box = SafetyUI.Panel(feedArea, "Dialogue Box", PixelUiTheme.Cream);
                Anchor(box.GetComponent<RectTransform>(), new Vector2(0f, top - .19f), new Vector2(1f, top));
                PixelUiTheme.ApplyCard(box, false);
                GameObject tab = SafetyUI.Panel(box.transform, "Speaker Tab", tint);
                Anchor(tab.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(.025f, 1f));
                SafetyUI.AddLabel(box.transform, message.Name + (message.Channel == ChatChannel.Ghost ? " (ghost)" : ""), new Vector2(.05f, .55f), new Vector2(.97f, .95f), Size(13), font, FontStyle.Bold, TextAnchor.MiddleLeft);
                SafetyUI.AddLabel(box.transform, message.Text, new Vector2(.05f, .05f), new Vector2(.97f, .58f), Size(14), font, FontStyle.Normal, TextAnchor.MiddleLeft);
            }
        }

        void UpdateBanner(MeetingView view)
        {
            bool show = view.Stage == MeetingStage.Ejection;
            if (show && banner == null)
            {
                banner = SafetyUI.Panel(window, "Ejection Banner", new Color(.09f, .17f, .2f, .94f)).GetComponent<RectTransform>();
                Anchor(banner, new Vector2(.06f, .36f), new Vector2(.94f, .62f));
                SafetyUI.AddLabel(banner, view.Outcome, new Vector2(.04f, .38f), new Vector2(.96f, .95f), Size(30), font, FontStyle.Bold, TextAnchor.MiddleCenter).color = PixelUiTheme.Cream;
                SafetyUI.AddLabel(banner, view.EjectedRole, new Vector2(.04f, .05f), new Vector2(.96f, .4f), Size(20), font, FontStyle.Normal, TextAnchor.MiddleCenter).color = PixelUiTheme.Gold;
            }
            if (banner != null)
            {
                banner.gameObject.SetActive(show);
                // Banner eases in from slightly above; sub-pixel motion keeps the slide smooth.
                float t = Mathf.Clamp01(1f - view.SecondsLeft / Mathf.Max(.01f, view.StageLength));
                banner.anchoredPosition = new Vector2(0f, Mathf.Lerp(30f, 0f, 1f - Mathf.Pow(1f - Mathf.Clamp01(t * 4f), 3f)));
            }
        }
    }
}
