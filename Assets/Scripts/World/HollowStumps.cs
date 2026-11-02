using System.Collections.Generic;
using UnityEngine;

namespace Crony
{
    /// <summary>
    /// Impostor fast travel through hollow, ivy-covered tree stumps. Five stumps form a ring; an impostor at one
    /// slips out of the next. Spots are derived from the scene's task stations, so every client (and the host that
    /// validates online use) computes the same positions without any network sync.
    /// </summary>
    public static class HollowStumps
    {
        public const float UseRange = 1.6f, Cooldown = 8f;
        static readonly TownshipTaskKind[] Anchors = { TownshipTaskKind.Router, TownshipTaskKind.LakeCleanup, TownshipTaskKind.NasiLemak, TownshipTaskKind.NoticeBoard, TownshipTaskKind.SolarBatteries };
        static readonly List<Vector2> spots = new List<Vector2>(), exits = new List<Vector2>();
        static Sprite sprite;

        public static int Count => spots.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Build()
        {
            spots.Clear();
            exits.Clear();
            GameObject map = GameObject.Find("Pixel Township Map");
            if (map == null) return;
            TownshipWorldMap world = new TownshipWorldMap(map);
            Transform root = new GameObject("Hollow Stumps").transform;
            foreach (TownshipTaskKind kind in Anchors)
            {
                TownshipTaskStation station = TownshipTaskStation.FindByKind(kind);
                if (station == null) continue;
                Vector2 spot = world.NearestWalkable((Vector2)station.transform.position + new Vector2(1.8f, -1.2f));
                spots.Add(spot);
                exits.Add(world.NearestWalkable(spot + new Vector2(0f, -.9f)));
                SpriteRenderer renderer = new GameObject("Hollow Stump " + kind).AddComponent<SpriteRenderer>();
                renderer.transform.SetParent(root, false);
                renderer.transform.position = spot;
                renderer.sprite = StumpSprite();
                SpriteRenderer reference = station.GetComponentInChildren<SpriteRenderer>();
                if (reference != null) { renderer.sortingLayerID = reference.sortingLayerID; renderer.sortingOrder = reference.sortingOrder; }
            }
        }

        /// <summary>Index of a stump within reach of a world position, or -1.</summary>
        public static int Nearest(Vector2 position)
        {
            for (int i = 0; i < spots.Count; i++)
                if ((spots[i] - position).sqrMagnitude <= UseRange * UseRange) return i;
            return -1;
        }

        public static Vector2 Spot(int index) => spots[index];
        /// <summary>Where a traveller entering stump <paramref name="index"/> comes out: just below the next stump.</summary>
        public static Vector2 ExitFrom(int index) => exits[(index + 1) % exits.Count];

        static Sprite StumpSprite()
        {
            if (sprite != null) return sprite;
            const int w = 22, h = 18;
            Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Hollow Stump" };
            Color clear = new Color(0f, 0f, 0f, 0f), bark = new Color32(122, 88, 70, 255), barkDark = new Color32(86, 62, 58, 255),
                rim = new Color32(196, 156, 104, 255), hole = new Color32(46, 38, 58, 255), ivy = new Color32(104, 140, 74, 255), ivyLight = new Color32(150, 178, 96, 255);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float nx = (x - 10.5f) / 10.5f;
                    Color c = clear;
                    bool trunk = y < 12 && Mathf.Abs(nx) < .92f - y * .01f;
                    float topY = (y - 12f) / 4.5f;
                    bool top = nx * nx + topY * topY < 1f;
                    if (trunk) c = (x % 4 == 0) ? barkDark : bark;
                    if (top) c = rim;
                    if (nx * nx * 1.6f + topY * topY * 1.8f < .55f) c = hole;
                    if ((trunk || top) && ((x * 5 + y * 3) % 13 < 3) && y < 10) c = (x + y) % 2 == 0 ? ivy : ivyLight;
                    if (y == 0 && Mathf.Abs(nx) < .95f) c = barkDark;
                    texture.SetPixel(x, y, c);
                }
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(.5f, .2f), 16f);
            return sprite;
        }
    }
}
