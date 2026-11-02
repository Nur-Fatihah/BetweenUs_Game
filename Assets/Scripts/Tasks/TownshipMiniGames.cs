using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Crony
{
    /// <summary>
    /// One skill game per checkpoint. A game calls Host.Win once per round; the host submits the station's next
    /// required action. Mistakes only cost time and never roll back progress already sent to the host.
    /// </summary>
    public abstract class TaskMiniGame
    {
        protected TownshipTaskMiniGameUI Host;
        protected RectTransform Area;
        protected float W, H;

        public abstract string Title { get; }
        public abstract string HowTo { get; }

        public static TaskMiniGame Create(TownshipTaskKind kind)
        {
            switch (kind)
            {
                case TownshipTaskKind.NasiLemak: return new HawkerRush();
                case TownshipTaskKind.IceCream: return new FreezerMemory();
                case TownshipTaskKind.ParkBench: return new HammerTime();
                case TownshipTaskKind.BadmintonNet: return new BadmintonRally();
                case TownshipTaskKind.CommunityPermits: return new PermitFrenzy();
                case TownshipTaskKind.BusSchedule: return new BusDepartures();
                case TownshipTaskKind.LakeCleanup: return new LakeBoat();
                case TownshipTaskKind.Router: return new SignalHunt();
                case TownshipTaskKind.SolarBatteries: return new SunChaser();
                default: return new FlyerCatch();
            }
        }

        public void Attach(TownshipTaskMiniGameUI host, RectTransform area)
        {
            Host = host;
            Area = area;
            W = area.sizeDelta.x;
            H = area.sizeDelta.y;
            Build();
            StartRound();
        }

        protected abstract void Build();
        /// <summary>Called on open and after every round won.</summary>
        public virtual void StartRound() { }
        public virtual void Tick(float dt) { }
        /// <summary>Space / Enter.</summary>
        public virtual void Primary() { }
        /// <summary>Number keys 1-5 (zero-based).</summary>
        public virtual void Number(int index) { }

        protected int Next => Host.Required(Host.Progress);
        protected string NextName => Host.ActionName(Next);

        protected GameObject Img(string name, Vector2 size, Vector2 position, Color color, bool clickable = false, Transform parent = null) =>
            Host.CreateImage(parent != null ? parent : Area, name, size, position, color, clickable);

        protected Image Picture(string name, Sprite sprite, Vector2 size, Vector2 position, Transform parent = null)
        {
            Image image = Img(name, size, position, Color.white, false, parent).GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            return image;
        }

        protected Text Label(string value, Vector2 position, Vector2 size, int fontSize, Color color, Transform parent = null) =>
            Host.CreateText(parent != null ? parent : Area, value, position, size, fontSize, color, FontStyle.Bold);

        protected Button Btn(string label, Vector2 position, Vector2 size, Color color, Action action, Transform parent = null) =>
            Host.CreateButton(parent != null ? parent : Area, label, position, size, color, action);

        protected static void Clickable(GameObject target, Action action, bool tapSound = true)
        {
            Button button = target.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => { EventSystem.current?.SetSelectedGameObject(null); if (tapSound) GameAudioManager.PlayGameTap(); action(); });
        }

        protected HoldButton Hold(string label, Vector2 position) =>
            Btn(label, position, new Vector2(90f, 58f), Hex("39515A"), null).gameObject.AddComponent<HoldButton>();

        /// <summary>-1..1 from A/D, arrow keys and optional on-screen hold buttons.</summary>
        protected static float Axis(HoldButton left = null, HoldButton right = null)
        {
            float axis = 0f;
            Keyboard keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) axis -= 1f;
                if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) axis += 1f;
            }
            if (left != null && left.Held) axis -= 1f;
            if (right != null && right.Held) axis += 1f;
            return Mathf.Clamp(axis, -1f, 1f);
        }

        protected static Color Hex(string value) => TownshipTaskMiniGameUI.Hex(value);
        protected static Sprite Circle => TownshipTaskMiniGameUI.GetCircleSprite();
        protected static float Rand(float min, float max) => UnityEngine.Random.Range(min, max);
        protected static int Rand(int maxExclusive) => UnityEngine.Random.Range(0, Mathf.Max(1, maxExclusive));
    }

    /// <summary>Pointer-held state for on-screen steering buttons.</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held { get; private set; }
        public void OnPointerDown(PointerEventData eventData) => Held = true;
        public void OnPointerUp(PointerEventData eventData) => Held = false;
        public void OnPointerExit(PointerEventData eventData) => Held = false;
        void OnDisable() => Held = false;
    }

    /// <summary>Hawker Stalls: scoop the ordered ingredient as it slides past on the conveyor.</summary>
    sealed class HawkerRush : TaskMiniGame
    {
        const float Zone = 70f;
        readonly List<(RectTransform rect, int kind)> items = new List<(RectTransform, int)>();
        Text ticket;
        float spawnTimer;

        public override string Title => "Hawker Rush";
        public override string HowTo => "Hawker rush! Press SPACE (or SCOOP) when the ingredient on the order ticket is on the banana leaf.";

        protected override void Build()
        {
            Img("Conveyor", new Vector2(W, 96f), new Vector2(0f, -10f), Hex("3B2F2A"));
            for (int i = 0; i < 9; i++) Img("Belt Stripe", new Vector2(4f, 96f), new Vector2((i - 4) * W / 9f, -10f), Hex("4D3E37"));
            Picture("Banana Leaf", TownshipTaskPixelArt.NasiLeaf, new Vector2(180f, 124f), new Vector2(0f, -10f)).preserveAspect = false;
            Img("Leaf Zone", new Vector2(Zone * 2f, 6f), new Vector2(0f, -66f), Hex("8FD39A"));
            ticket = Label("", new Vector2(0f, H * .36f), new Vector2(W * .9f, 40f), 22, Hex("FFE7A8"));
            Btn("SCOOP!  [Space]", new Vector2(0f, -H * .38f), new Vector2(260f, 54f), Hex("E07B4F"), Primary);
        }

        public override void StartRound()
        {
            ticket.text = "ORDER " + (Host.Progress + 1) + "/" + Host.Steps + "   ·   NEXT: " + NextName.ToUpperInvariant();
        }

        public override void Tick(float dt)
        {
            spawnTimer -= dt;
            if (spawnTimer <= 0f) { spawnTimer = Rand(.75f, 1.15f); Spawn(); }
            float speed = 170f + 45f * Host.Progress;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                RectTransform rect = items[i].rect;
                rect.anchoredPosition += Vector2.left * speed * dt;
                if (rect.anchoredPosition.x < -W * .5f - 60f) { Object.Destroy(rect.gameObject); items.RemoveAt(i); }
            }
        }

        void Spawn()
        {
            int kind = Rand(0f, 1f) < .4f ? Next : Rand(Host.ActionCount);
            Image image = Picture("Ingredient", TownshipTaskPixelArt.NasiIngredient(kind), new Vector2(84f, 84f), new Vector2(W * .5f + 50f, -10f));
            Label(Host.ActionName(kind), new Vector2(0f, -56f), new Vector2(140f, 22f), 13, Hex("FFF5DD"), image.transform);
            items.Add((image.rectTransform, kind));
        }

        public override void Primary()
        {
            int hit = items.FindIndex(item => Mathf.Abs(item.rect.anchoredPosition.x) <= Zone);
            if (hit < 0) { Host.Miss("Too early! Wait until it's on the leaf."); return; }
            int kind = items[hit].kind;
            Object.Destroy(items[hit].rect.gameObject);
            items.RemoveAt(hit);
            if (kind == Next) Host.Win(Host.ActionName(kind) + " scooped onto the leaf!");
            else Host.Miss("That's " + Host.ActionName(kind) + "! The order needs " + NextName + ".");
        }
    }

    /// <summary>Convenience Store: repeat the freezer's growing flavor sequence.</summary>
    internal sealed class FreezerMemory : TaskMiniGame
    {
        Image[] pops;
        Text status;
        bool watching;
        int shown, inputIndex, lit = -1;
        float timer, litTimer;

        public override string Title => "Freezer Memory";
        public override string HowTo => "Freezer memory! Watch the pops light up, then repeat the order (click them or press 1-4). Each round adds one more.";

        protected override void Build()
        {
            int count = Mathf.Clamp(Host.ActionCount, 1, 5);
            pops = new Image[count];
            float spacing = Mathf.Min(210f, W / count);
            status = Label("", new Vector2(0f, H * .36f), new Vector2(W * .9f, 36f), 22, Hex("FFE7A8"));
            for (int i = 0; i < count; i++)
            {
                int index = i;
                GameObject pop = Img("Pop " + i, new Vector2(126f, 126f), new Vector2((i - (count - 1) * .5f) * spacing, 10f), Color.white, true);
                pops[i] = pop.GetComponent<Image>();
                pops[i].sprite = Circle;
                Img("Stick", new Vector2(16f, 60f), new Vector2(0f, -86f), Hex("D9B987"), false, pop.transform).transform.SetAsFirstSibling();
                Label(Host.ActionName(i) + "\n[" + (i + 1) + "]", new Vector2(0f, -150f), new Vector2(170f, 48f), 15, Hex("FFF2D5"), pop.transform);
                // Each pop sings its own note, Simon-style, so the sequence can be heard as well as seen.
                Clickable(pop, () => Press(index), false);
                SetLit(i, false);
            }
        }

        public override void StartRound()
        {
            watching = true;
            shown = 0;
            inputIndex = 0;
            timer = .8f;
            status.text = "WATCH CAREFULLY…  (" + (Host.Progress + 1) + " pop" + (Host.Progress == 0 ? ")" : "s)");
        }

        public override void Tick(float dt)
        {
            if (lit >= 0 && (litTimer -= dt) <= 0f) { SetLit(lit, false); lit = -1; }
            if (!watching || (timer -= dt) > 0f) return;
            if (shown <= Host.Progress) { Light(Host.Required(shown)); shown++; timer = .65f; }
            else { watching = false; status.text = "YOUR TURN: repeat " + (Host.Progress + 1) + " pop" + (Host.Progress == 0 ? "" : "s"); }
        }

        public override void Number(int index) { if (index < pops.Length) Press(index); }

        void Press(int index)
        {
            if (watching || !Host.Playable) return;
            Light(index);
            if (index != Host.Required(inputIndex)) { Host.Miss("Oops, that was " + Host.ActionName(index) + ". Watch again."); StartRound(); return; }
            if (++inputIndex > Host.Progress) Host.Win("Perfect memory! The next round adds a pop.");
        }

        void Light(int index)
        {
            if (index < 0 || index >= pops.Length) return;
            if (lit >= 0) SetLit(lit, false);
            lit = index;
            litTimer = .4f;
            SetLit(index, true);
            GameAudioManager.PlayNote(index);
        }

        void SetLit(int index, bool on)
        {
            Color flavor = index == 0 ? Hex("79BC77") : index == 1 ? Hex("F3BC49") : index == 2 ? Hex("B95D63") : index == 3 ? Hex("EEE1B9") : Hex("9CC9E8");
            pops[index].color = on ? flavor : Color.Lerp(flavor, Color.black, .55f);
            pops[index].rectTransform.localScale = on ? Vector3.one * 1.15f : Vector3.one;
        }
    }

    /// <summary>Recreation Park: strike while the swinging needle is in a shrinking sweet spot.</summary>
    sealed class HammerTime : TaskMiniGame
    {
        RectTransform needle, zone;
        Image needleImage;
        Image[] nails;
        float barWidth, position, direction = 1f, zoneCenter, zoneWidth, cooldown;

        public override string Title => "Hammer Time";
        public override string HowTo => "Hammer time! Press SPACE (or HAMMER) while the swinging needle is in the green zone. It shrinks with every nail.";

        protected override void Build()
        {
            float plankWidth = W * .7f;
            Img("Bench Plank", new Vector2(plankWidth, 48f), new Vector2(0f, H * .24f), Hex("B9824C"));
            Img("Wood Grain", new Vector2(plankWidth * .85f, 3f), new Vector2(0f, H * .24f), Hex("D9AE73"));
            nails = new Image[Host.Steps];
            for (int i = 0; i < nails.Length; i++)
            {
                nails[i] = Img("Nail", new Vector2(26f, 26f), new Vector2((i - (nails.Length - 1) * .5f) * plankWidth / Mathf.Max(1, nails.Length), H * .24f), Hex("83959A")).GetComponent<Image>();
                nails[i].sprite = Circle;
            }
            barWidth = W * .7f;
            GameObject bar = Img("Power Bar", new Vector2(barWidth, 42f), new Vector2(0f, -H * .04f), Hex("2A3E44"));
            zone = Img("Sweet Spot", new Vector2(10f, 42f), Vector2.zero, Hex("4FC5A3"), false, bar.transform).GetComponent<RectTransform>();
            needleImage = Img("Needle", new Vector2(8f, 66f), Vector2.zero, Hex("F0C966"), false, bar.transform).GetComponent<Image>();
            needle = needleImage.rectTransform;
            Btn("HAMMER!  [Space]", new Vector2(0f, -H * .34f), new Vector2(260f, 54f), Hex("C9744A"), Primary);
        }

        public override void StartRound()
        {
            zoneWidth = Mathf.Max(.08f, .24f - .045f * Host.Progress);
            zoneCenter = Rand(zoneWidth, 1f - zoneWidth);
            zone.sizeDelta = new Vector2(zoneWidth * barWidth, 42f);
            zone.anchoredPosition = new Vector2((zoneCenter - .5f) * barWidth, 0f);
            for (int i = 0; i < nails.Length; i++) nails[i].color = i < Host.Progress ? Hex("F0C966") : i == Host.Progress ? Hex("E8E8E8") : Hex("83959A");
        }

        public override void Tick(float dt)
        {
            if (cooldown > 0f) { if ((cooldown -= dt) <= 0f) needleImage.color = Hex("F0C966"); return; }
            position += direction * (.8f + .3f * Host.Progress) * dt;
            if (position > 1f) { position = 1f; direction = -1f; }
            if (position < 0f) { position = 0f; direction = 1f; }
            needle.anchoredPosition = new Vector2((position - .5f) * barWidth, 0f);
        }

        public override void Primary()
        {
            if (cooldown > 0f) return;
            if (Mathf.Abs(position - zoneCenter) <= zoneWidth * .5f) { Host.Win(NextName + ": WHACK! Solid hit."); return; }
            Host.Miss("Bent nail! Hit inside the green zone.");
            cooldown = .5f;
            needleImage.color = Hex("E0645A");
        }
    }

    /// <summary>Community Hall: keep a badminton rally alive by swinging as the shuttle arrives.</summary>
    sealed class BadmintonRally : TaskMiniGame
    {
        const int Needed = 6;
        RectTransform shuttle, racket;
        Text counter;
        float t, speed, swing, leftX, rightX, groundY;
        bool incoming;
        int streak;

        public override string Title => "Badminton Rally";
        public override string HowTo => "Keep the rally going! Press SPACE (or SWING) as the shuttlecock reaches your racket. Return 6 in a row.";

        protected override void Build()
        {
            leftX = -W * .36f; rightX = W * .36f; groundY = -H * .26f;
            Img("Court", new Vector2(W * .9f, 10f), new Vector2(0f, groundY - 12f), Hex("4C9D83"));
            Img("Net", new Vector2(8f, H * .26f), new Vector2(0f, groundY + H * .12f), Hex("E7F0E8"));
            racket = Img("Your Racket", new Vector2(24f, 96f), new Vector2(leftX - 34f, groundY + 36f), Hex("F0C966")).GetComponent<RectTransform>();
            racket.pivot = new Vector2(.5f, 0f);
            Img("Rival Racket", new Vector2(24f, 96f), new Vector2(rightX + 34f, groundY + 36f), Hex("8FB7C9"));
            Image shuttleImage = Img("Shuttlecock", new Vector2(28f, 28f), Vector2.zero, Color.white).GetComponent<Image>();
            shuttleImage.sprite = Circle;
            shuttle = shuttleImage.rectTransform;
            counter = Label("", new Vector2(0f, H * .38f), new Vector2(W * .8f, 36f), 22, Hex("FFE7A8"));
            Btn("SWING!  [Space]", new Vector2(0f, -H * .42f), new Vector2(240f, 50f), Hex("4C9D83"), Primary);
        }

        public override void StartRound() { streak = 0; Serve(); }

        void Serve() { t = 0f; incoming = true; speed = .7f; Count(); }
        void Count() => counter.text = "RALLY  " + streak + " / " + Needed;

        public override void Tick(float dt)
        {
            swing = Mathf.Max(0f, swing - dt);
            racket.localRotation = Quaternion.Euler(0f, 0f, swing > 0f ? -45f : 0f);
            t += dt * speed;
            float x = incoming ? Mathf.Lerp(rightX, leftX, t) : Mathf.Lerp(leftX, rightX, t);
            shuttle.anchoredPosition = new Vector2(x, groundY + 40f + Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)) * H * .42f);
            if (!incoming && t >= 1f) { incoming = true; t = 0f; speed += .07f; }
            else if (incoming && t >= 1.06f) { Host.Miss("Missed! The rally starts over."); streak = 0; Serve(); }
        }

        public override void Primary()
        {
            if (swing > 0f) return;
            swing = .3f;
            if (!incoming || t < .78f) { Host.Miss("Too early! Wait for the shuttle."); return; }
            streak++;
            incoming = false;
            t = 0f;
            Count();
            if (streak >= Needed) Host.Win("What a rally! The net tension is spot on.");
        }
    }

    /// <summary>Administrative Office: file falling forms into the right binder before they land.</summary>
    sealed class PermitFrenzy : TaskMiniGame
    {
        const int Needed = 3;
        static readonly string[] Colors = { "A94C45", "3F78A5", "43845D" };
        RectTransform form;
        Image formImage;
        Text formLabel, status;
        int formType, streak, count;
        float formY;

        public override string Title => "Permit Frenzy";
        public override string HowTo => "Permit frenzy! File each falling form into its matching binder before it lands (click a binder or press 1-3). 3 in a row files the next permit.";

        protected override void Build()
        {
            count = Mathf.Clamp(Host.ActionCount, 1, 3);
            float binderWidth = Mathf.Min(260f, W / count * .85f);
            for (int i = 0; i < count; i++)
            {
                int index = i;
                Btn(Host.ActionName(i) + "  [" + (i + 1) + "]", new Vector2((i - (count - 1) * .5f) * (binderWidth + 18f), -H * .38f), new Vector2(binderWidth, 70f), Hex(Colors[i]), () => File(index));
            }
            formImage = Img("Falling Form", new Vector2(220f, 72f), Vector2.zero, Color.white).GetComponent<Image>();
            form = formImage.rectTransform;
            formLabel = Label("", Vector2.zero, new Vector2(200f, 60f), 16, Hex("1E2A2E"), form);
            status = Label("", new Vector2(0f, H * .42f), new Vector2(W * .9f, 32f), 20, Hex("FFE7A8"));
        }

        public override void StartRound() { streak = 0; NewForm(); }

        void NewForm()
        {
            formType = Rand(count);
            formY = H * .32f;
            form.anchoredPosition = new Vector2(Rand(-W * .25f, W * .25f), formY);
            formImage.color = Color.Lerp(Hex(Colors[formType]), Hex("FFF5DD"), .55f);
            formLabel.text = Host.ActionName(formType);
            status.text = "FILING " + NextName.ToUpperInvariant() + "   ·   streak " + streak + " / " + Needed;
        }

        public override void Tick(float dt)
        {
            formY -= (85f + 30f * Host.Progress + 12f * streak) * dt;
            form.anchoredPosition = new Vector2(form.anchoredPosition.x, formY);
            if (formY < -H * .24f) { Host.Miss("Dropped it! Keep the stack moving."); streak = 0; NewForm(); }
        }

        public override void Number(int index) { if (index < count) File(index); }

        void File(int binder)
        {
            if (!Host.Playable) return;
            if (binder != formType) { Host.Miss("Wrong binder! That form is a " + Host.ActionName(formType) + "."); streak = 0; NewForm(); return; }
            if (++streak >= Needed) { Host.Win(NextName + " filed in the right binder!"); return; }
            NewForm();
        }
    }

    /// <summary>Bus Stop: tap drifting departure tiles in chronological order before the bus leaves.</summary>
    sealed class BusDepartures : TaskMiniGame
    {
        const float BusTime = 20f;
        sealed class Tile { public RectTransform Rect; public Vector2 Velocity; public int Action; }
        readonly List<Tile> tiles = new List<Tile>();
        Text timerLabel, departed;
        float timer, halfWidth, halfHeight;

        public override string Title => "Bus Departures";
        public override string HowTo => "The bus is coming! Tap the departure times from earliest to latest while they drift around. Wrong taps cost 3 seconds.";

        protected override void Build()
        {
            float boardWidth = W * .86f, boardHeight = H * .66f;
            GameObject board = Img("Departure Board", new Vector2(boardWidth, boardHeight), new Vector2(0f, -H * .06f), Hex("11262C"));
            halfWidth = boardWidth * .5f - 80f;
            halfHeight = boardHeight * .5f - 30f;
            departed = Label("", new Vector2(-W * .1f, H * .40f), new Vector2(W * .7f, 30f), 16, Hex("8CE6BA"));
            timerLabel = Label("", new Vector2(W * .38f, H * .40f), new Vector2(160f, 30f), 20, Hex("FFE7A8"));
            for (int a = 0; a < Host.ActionCount; a++)
            {
                bool done = false;
                for (int s = 0; s < Host.Progress; s++) if (Host.Required(s) == a) done = true;
                if (done) continue;
                Tile tile = new Tile { Action = a };
                tile.Rect = Btn(Host.ActionName(a), Vector2.zero, new Vector2(150f, 52f), Hex(a % 2 == 0 ? "D6AE62" : "80BBA5"), () => Tap(tile), board.transform).GetComponent<RectTransform>();
                tiles.Add(tile);
            }
            Scatter();
            timer = BusTime;
        }

        void Scatter()
        {
            foreach (Tile tile in tiles)
            {
                tile.Rect.anchoredPosition = new Vector2(Rand(-halfWidth, halfWidth), Rand(-halfHeight, halfHeight));
                tile.Velocity = UnityEngine.Random.insideUnitCircle.normalized * Rand(50f, 80f);
            }
        }

        public override void StartRound()
        {
            List<string> gone = new List<string>();
            for (int s = 0; s < Host.Progress; s++) gone.Add(Host.ActionName(Host.Required(s)));
            departed.text = gone.Count == 0 ? "DEPARTED: none yet" : "DEPARTED: " + string.Join("  ›  ", gone);
        }

        public override void Tick(float dt)
        {
            timer -= dt;
            timerLabel.text = "BUS IN " + Mathf.Max(0, Mathf.CeilToInt(timer)) + "s";
            if (timer <= 0f) { Host.Miss("The bus left without you! Here comes the next one."); timer = BusTime; Scatter(); }
            float pace = 1f + .3f * Host.Progress;
            foreach (Tile tile in tiles)
            {
                Vector2 p = tile.Rect.anchoredPosition + tile.Velocity * pace * dt;
                if (Mathf.Abs(p.x) > halfWidth) { tile.Velocity.x = -tile.Velocity.x; p.x = Mathf.Clamp(p.x, -halfWidth, halfWidth); }
                if (Mathf.Abs(p.y) > halfHeight) { tile.Velocity.y = -tile.Velocity.y; p.y = Mathf.Clamp(p.y, -halfHeight, halfHeight); }
                tile.Rect.anchoredPosition = p;
            }
        }

        void Tap(Tile tile)
        {
            if (!Host.Playable || !tile.Rect.gameObject.activeSelf) return;
            if (tile.Action != Next) { Host.Miss("Not yet! Find the earliest departure left."); timer -= 3f; return; }
            tile.Rect.gameObject.SetActive(false);
            tiles.Remove(tile);
            Host.Win(Host.ActionName(tile.Action) + " departs on time!");
        }
    }

    /// <summary>Central Lake: steer the boat to net the requested trash and let the fish swim by.</summary>
    sealed class LakeBoat : TaskMiniGame
    {
        const int Needed = 2;
        readonly List<(RectTransform rect, int kind, float phase)> drifts = new List<(RectTransform, int, float)>();
        RectTransform boat;
        HoldButton left, right;
        Text status;
        float boatX, spawnTimer;
        int caught;

        public override string Title => "Lake Boat";
        public override string HowTo => "Steer the boat with A/D, the arrow keys or the << >> buttons. Net the item shown and let the fish swim past.";

        protected override void Build()
        {
            Image water = Picture("Lake", TownshipTaskPixelArt.Lake, new Vector2(W * .9f, H), Vector2.zero);
            water.preserveAspect = false;
            boat = Img("Boat", new Vector2(130f, 34f), new Vector2(0f, -H * .34f), Hex("C98B4E")).GetComponent<RectTransform>();
            Img("Net", new Vector2(116f, 10f), new Vector2(0f, 22f), Hex("E6E6E6"), false, boat);
            left = Hold("<<", new Vector2(-W * .40f, -H * .42f));
            right = Hold(">>", new Vector2(W * .40f, -H * .42f));
            status = Label("", new Vector2(0f, H * .42f), new Vector2(W * .8f, 32f), 20, Hex("FFF5DD"));
        }

        public override void StartRound() { caught = 0; Status(); }
        void Status() => status.text = "NET THE " + NextName.ToUpperInvariant() + "   ·   " + caught + " / " + Needed;

        public override void Tick(float dt)
        {
            boatX = Mathf.Clamp(boatX + Axis(left, right) * 380f * dt, -W * .42f + 65f, W * .42f - 65f);
            float boatY = boat.anchoredPosition.y;
            boat.anchoredPosition = new Vector2(boatX, boatY);
            if ((spawnTimer -= dt) <= 0f) { spawnTimer = Rand(.55f, .9f); Spawn(); }
            float speed = 110f + 25f * Host.Progress;
            for (int i = drifts.Count - 1; i >= 0; i--)
            {
                var drift = drifts[i];
                Vector2 p = drift.rect.anchoredPosition;
                p.y -= speed * dt;
                p.x += Mathf.Sin(Time.unscaledTime * 2f + drift.phase) * 30f * dt;
                drift.rect.anchoredPosition = p;
                bool inNet = p.y <= boatY + 34f && p.y >= boatY - 6f && Mathf.Abs(p.x - boatX) < 72f;
                if (!inNet && p.y > -H * .5f - 40f) continue;
                Object.Destroy(drift.rect.gameObject);
                drifts.RemoveAt(i);
                if (inNet && Catch(drift.kind)) return;
            }
        }

        void Spawn()
        {
            float roll = Rand(0f, 1f);
            int kind = roll < .25f ? -1 : roll < .65f ? Next : Rand(Host.ActionCount);
            Vector2 start = new Vector2(Rand(-W * .38f, W * .38f), H * .5f + 30f);
            RectTransform rect;
            if (kind < 0)
            {
                Image fish = Img("Fish", new Vector2(60f, 34f), start, Hex("F08A4B")).GetComponent<Image>();
                fish.sprite = Circle;
                Label("FISH", Vector2.zero, new Vector2(60f, 20f), 11, Hex("3A1E10"), fish.transform);
                rect = fish.rectTransform;
            }
            else
            {
                Image item = Picture("Trash", TownshipTaskPixelArt.LakeItem(kind), new Vector2(64f, 64f), start);
                Label(Host.ActionName(kind), new Vector2(0f, -42f), new Vector2(140f, 20f), 12, Hex("FFF5DD"), item.transform);
                rect = item.rectTransform;
            }
            drifts.Add((rect, kind, Rand(0f, 6f)));
        }

        /// <summary>Returns true when the round was won (the host may have closed the panel).</summary>
        bool Catch(int kind)
        {
            if (kind < 0) { caught = Mathf.Max(0, caught - 1); Host.Miss("Let the fish go! You lost one catch."); Status(); return false; }
            if (kind != Next) { Host.Miss("That's " + Host.ActionName(kind) + ". Net the " + NextName + " first."); return false; }
            if (++caught < Needed) { Status(); return false; }
            Host.Win(Host.ActionName(kind) + " cleared from the lake!");
            return true;
        }
    }

    /// <summary>Cellular Tower: hunt the hidden frequency for each breaker and hold the lock.</summary>
    sealed class SignalHunt : TaskMiniGame
    {
        const float LockNeeded = 1.1f, Window = 3.5f, MeterWidth = 300f;
        Slider dial;
        Image[] lights, bars;
        RectTransform lockFill;
        float target, lockTime;

        public override string Title => "Signal Hunt";
        public override string HowTo => "Hunt the signal! Slide the dial (or hold A/D) until the signal is strongest, then hold steady to lock each breaker.";

        protected override void Build()
        {
            int count = Mathf.Max(1, Host.ActionCount);
            lights = new Image[count];
            float spacing = Mathf.Min(120f, W / count);
            for (int i = 0; i < count; i++)
            {
                lights[i] = Img("Breaker Light", new Vector2(46f, 46f), new Vector2((i - (count - 1) * .5f) * spacing, H * .36f), Color.white).GetComponent<Image>();
                lights[i].sprite = Circle;
                Label("B" + (i + 1), new Vector2(0f, -36f), new Vector2(60f, 20f), 13, Hex("E7F0E8"), lights[i].transform);
            }
            bars = new Image[6];
            for (int i = 0; i < bars.Length; i++)
            {
                float height = 24f + i * 18f;
                bars[i] = Img("Signal Bar", new Vector2(28f, height), new Vector2((i - 2.5f) * 40f, H * .02f + height * .5f - 50f), Hex("2F474E")).GetComponent<Image>();
            }
            GameObject meter = Img("Lock Meter", new Vector2(MeterWidth, 16f), new Vector2(0f, -H * .14f), Hex("2A3E44"));
            lockFill = Img("Lock Fill", new Vector2(0f, 16f), new Vector2(-MeterWidth * .5f, 0f), Hex("F0C966"), false, meter.transform).GetComponent<RectTransform>();
            lockFill.pivot = new Vector2(0f, .5f);
            dial = Host.CreateSlider(Area, new Vector2(0f, -H * .32f), W * .7f, Hex("4FC5A3"));
            dial.minValue = 0f;
            dial.maxValue = 100f;
            dial.value = 50f;
        }

        public override void StartRound()
        {
            do target = Rand(8f, 92f); while (Mathf.Abs(target - dial.value) < 20f);
            lockTime = 0f;
            for (int i = 0; i < lights.Length; i++) lights[i].color = i < Host.Progress ? Hex("6BCB77") : i == Host.Progress ? Hex("F0C966") : Hex("42525A");
        }

        public override void Tick(float dt)
        {
            dial.value += Axis() * 40f * dt;
            // From the third breaker the frequency wanders a little, so you have to follow it.
            float live = target + (Host.Progress >= 2 ? Mathf.Sin(Time.unscaledTime * .8f) * (2f + Host.Progress) : 0f);
            float distance = Mathf.Abs(dial.value - live);
            int strength = Mathf.RoundToInt(Mathf.Clamp01(1f - distance / 40f) * bars.Length);
            for (int i = 0; i < bars.Length; i++) bars[i].color = i < strength ? Color.Lerp(Hex("F0C966"), Hex("6BCB77"), i / 5f) : Hex("2F474E");
            lockTime = distance <= Window ? lockTime + dt : Mathf.Max(0f, lockTime - dt * 2f);
            lockFill.sizeDelta = new Vector2(Mathf.Clamp01(lockTime / LockNeeded) * MeterWidth, 16f);
            if (lockTime >= LockNeeded) Host.Win(NextName + " locked on: breaker ON!");
        }
    }

    /// <summary>Walkway: keep the solar panel under the drifting sun to charge each cell.</summary>
    sealed class SunChaser : TaskMiniGame
    {
        const float MeterWidth = 360f;
        RectTransform sun, solarPanel, chargeFill;
        Image panelImage;
        Slider aim;
        Text status;
        float charge, sunX, sunVelocity, flipTimer, track, skyTop;

        public override string Title => "Sun Chaser";
        public override string HowTo => "Chase the sun! Keep the solar panel under the sun (slider or A/D) to charge the cell. Each full charge completes a step.";

        protected override void Build()
        {
            track = W * .8f;
            skyTop = H * .30f;
            Img("Sky", new Vector2(W * .9f, H * .58f), new Vector2(0f, H * .12f), Hex("2E5C73"));
            Image sunImage = Img("Sun", new Vector2(64f, 64f), new Vector2(0f, skyTop), Hex("FFD15C")).GetComponent<Image>();
            sunImage.sprite = Circle;
            sun = sunImage.rectTransform;
            panelImage = Img("Solar Panel", new Vector2(150f, 22f), new Vector2(0f, -H * .14f), Hex("3C6E8F")).GetComponent<Image>();
            solarPanel = panelImage.rectTransform;
            GameObject meter = Img("Charge Meter", new Vector2(MeterWidth, 22f), new Vector2(0f, -H * .25f), Hex("2A3E44"));
            chargeFill = Img("Charge Fill", new Vector2(0f, 22f), new Vector2(-MeterWidth * .5f, 0f), Hex("F0C966"), false, meter.transform).GetComponent<RectTransform>();
            chargeFill.pivot = new Vector2(0f, .5f);
            aim = Host.CreateSlider(Area, new Vector2(0f, -H * .40f), track, Hex("3C6E8F"));
            aim.value = .5f;
            status = Label("", new Vector2(0f, H * .44f), new Vector2(W * .8f, 30f), 18, Hex("FFE7A8"));
        }

        public override void StartRound()
        {
            charge = 0f;
            sunX = 0f;
            sunVelocity = (110f + 40f * Host.Progress) * (Rand(0f, 1f) < .5f ? -1f : 1f);
            flipTimer = Rand(1f, 2.4f);
            status.text = "CHARGING FOR: " + NextName.ToUpperInvariant();
        }

        public override void Tick(float dt)
        {
            aim.value = Mathf.Clamp01(aim.value + Axis() * .8f * dt);
            float panelX = (aim.value - .5f) * track;
            solarPanel.anchoredPosition = new Vector2(panelX, solarPanel.anchoredPosition.y);
            if ((flipTimer -= dt) <= 0f) { sunVelocity = -sunVelocity * Rand(.85f, 1.2f); flipTimer = Rand(.8f, 2.2f); }
            sunVelocity = Mathf.Clamp(sunVelocity, -320f, 320f);
            sunX += sunVelocity * dt;
            if (Mathf.Abs(sunX) > track * .5f) { sunX = Mathf.Sign(sunX) * track * .5f; sunVelocity = -sunVelocity; }
            sun.anchoredPosition = new Vector2(sunX, skyTop - Mathf.Abs(sunX) / track * 60f);
            bool aligned = Mathf.Abs(panelX - sunX) < 70f;
            panelImage.color = aligned ? Hex("7FC8F0") : Hex("3C6E8F");
            charge = Mathf.Clamp(charge + (aligned ? 24f : -14f) * dt, 0f, 100f);
            chargeFill.sizeDelta = new Vector2(charge / 100f * MeterWidth, 22f);
            if (charge >= 100f) Host.Win(NextName + " done: the cell is fully charged!");
        }
    }

    /// <summary>Notice Board: catch the named flyer while the wind blows them around.</summary>
    sealed class FlyerCatch : TaskMiniGame
    {
        sealed class Flyer { public RectTransform Rect; public Vector2 Velocity; public int Action; public bool Pinned; }
        readonly List<Flyer> flyers = new List<Flyer>();
        Text card;
        float halfWidth, halfHeight, gust;
        int pinnedCount;

        public override string Title => "Flyer Catch";
        public override string HowTo => "A gust is blowing the flyers around! Click the flyer named on the card to pin it to the board.";

        protected override void Build()
        {
            float boardWidth = W * .88f, boardHeight = H * .80f;
            Image board = Picture("Corkboard", TownshipTaskPixelArt.Corkboard, new Vector2(boardWidth, boardHeight), new Vector2(0f, -H * .06f));
            board.preserveAspect = false;
            halfWidth = boardWidth * .5f - 90f;
            halfHeight = boardHeight * .5f - 70f;
            card = Label("", new Vector2(0f, H * .42f), new Vector2(W * .8f, 32f), 22, Hex("FFE7A8"));
            for (int a = 0; a < Host.ActionCount; a++)
            {
                Flyer flyer = new Flyer { Action = a };
                Image image = Picture("Flyer", TownshipTaskPixelArt.Flyer(a), new Vector2(150f, 100f), new Vector2(Rand(-halfWidth, halfWidth), Rand(-halfHeight, halfHeight)), board.transform);
                image.raycastTarget = true;
                Label(Host.ActionName(a), new Vector2(0f, -62f), new Vector2(180f, 22f), 13, Hex("FFF0C4"), image.transform);
                Clickable(image.gameObject, () => Grab(flyer));
                flyer.Rect = image.rectTransform;
                flyer.Velocity = UnityEngine.Random.insideUnitCircle.normalized * Rand(110f, 150f);
                flyers.Add(flyer);
            }
            for (int s = 0; s < Host.Progress; s++)
            {
                Flyer done = flyers.Find(f => f.Action == Host.Required(s));
                if (done != null) Pin(done);
            }
        }

        public override void StartRound() => card.text = "CATCH:  " + NextName.ToUpperInvariant();

        public override void Tick(float dt)
        {
            gust = Mathf.Max(0f, gust - dt);
            float pace = 1f + .25f * Host.Progress + gust * 1.5f;
            foreach (Flyer flyer in flyers)
            {
                if (flyer.Pinned) continue;
                Vector2 p = flyer.Rect.anchoredPosition + flyer.Velocity * pace * dt;
                if (Mathf.Abs(p.x) > halfWidth) { flyer.Velocity.x = -flyer.Velocity.x; p.x = Mathf.Clamp(p.x, -halfWidth, halfWidth); }
                if (Mathf.Abs(p.y) > halfHeight) { flyer.Velocity.y = -flyer.Velocity.y; p.y = Mathf.Clamp(p.y, -halfHeight, halfHeight); }
                flyer.Rect.anchoredPosition = p;
                flyer.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 3f + flyer.Action) * 8f);
            }
        }

        void Grab(Flyer flyer)
        {
            if (!Host.Playable || flyer.Pinned) return;
            if (flyer.Action != Next) { Host.Miss("That's the " + Host.ActionName(flyer.Action) + " flyer! Look for " + NextName + "."); gust = .6f; return; }
            Pin(flyer);
            Host.Win(Host.ActionName(flyer.Action) + " pinned to the board!");
        }

        void Pin(Flyer flyer)
        {
            flyer.Pinned = true;
            flyer.Rect.localRotation = Quaternion.identity;
            flyer.Rect.anchoredPosition = new Vector2((pinnedCount - 1) * halfWidth * .6f, halfHeight * .55f);
            Picture("Pushpin", TownshipTaskPixelArt.Pushpin, new Vector2(28f, 28f), new Vector2(0f, 46f), flyer.Rect);
            pinnedCount++;
        }
    }
}
