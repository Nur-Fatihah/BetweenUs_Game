using UnityEngine;

namespace Crony
{
    /// <summary>
    /// Ambient depth over the park: warm god rays slanting across the view, slow golden dust motes, and petals and
    /// leaves drifting on a breeze. A fixed pool of sprites that follows the camera and wraps around the view, so the
    /// cost is constant (~60 sprites, one Update). Positions are sub-pixel and ignore the art grid on purpose.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Atmosphere : MonoBehaviour
    {
        const int Dust = 34, Petals = 12, Leaves = 8, Rays = 3;
        sealed class Mote { public Transform T; public SpriteRenderer R; public Vector2 Offset, Velocity; public float Phase, Spin, Alpha; public int Kind; }
        Camera view;
        Mote[] motes;
        SpriteRenderer[] rays;
        float[] rayPhase;
        static Sprite dot, petal, leaf, ray;
        static Atmosphere current;

        /// <summary>Light mode hides the god rays, petals and leaves; dust motes stay.</summary>
        public static void SetFull(bool full)
        {
            if (current == null || current.motes == null) return;
            foreach (SpriteRenderer r in current.rays) r.gameObject.SetActive(full);
            foreach (Mote m in current.motes) if (m.Kind != 0) m.T.gameObject.SetActive(full);
        }

        void OnDestroy() { if (current == this) current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Camera camera = Camera.main;
            if (camera != null && camera.GetComponent<Atmosphere>() == null) camera.gameObject.AddComponent<Atmosphere>();
        }

        void Start()
        {
            view = GetComponent<Camera>();
            bool calm = PlayerPrefs.GetInt(GameSettings.ReduceEffectsKey, 0) == 1;
            Transform root = new GameObject("Atmosphere").transform;
            SortingLayer[] layers = SortingLayer.layers;
            string top = layers[layers.Length - 1].name;

            rays = new SpriteRenderer[Rays];
            rayPhase = new float[Rays];
            for (int i = 0; i < Rays; i++)
            {
                rays[i] = Renderer(root, "God Ray", RaySprite(), top, 31000);
                rays[i].transform.rotation = Quaternion.Euler(0f, 0f, -28f);
                rayPhase[i] = i * 2.1f;
            }

            int petals = calm ? 0 : Petals, leaves = calm ? 0 : Leaves;
            motes = new Mote[Dust + petals + leaves];
            for (int i = 0; i < motes.Length; i++)
            {
                int kind = i < Dust ? 0 : i < Dust + petals ? 1 : 2;
                Mote m = new Mote { Kind = kind, Phase = Random.value * 10f, Spin = Random.Range(-60f, 60f) };
                m.R = Renderer(root, kind == 0 ? "Dust Mote" : kind == 1 ? "Petal" : "Leaf", kind == 0 ? DotSprite() : kind == 1 ? PetalSprite() : LeafSprite(), top, 31500);
                m.T = m.R.transform;
                m.Alpha = kind == 0 ? Random.Range(.25f, .6f) : Random.Range(.7f, .95f);
                m.R.color = kind == 0 ? new Color(1f, .9f, .6f, m.Alpha) : kind == 1 ? new Color(1f, .82f, .86f, m.Alpha) : new Color(.72f, .82f, .5f, m.Alpha);
                float size = kind == 0 ? Random.Range(.5f, 1f) : 1f;
                m.T.localScale = Vector3.one * size;
                m.Velocity = kind == 0 ? new Vector2(Random.Range(-.05f, .05f), Random.Range(.04f, .12f)) * (calm ? .5f : 1f)
                    : new Vector2(Random.Range(.5f, .9f), Random.Range(-.35f, -.15f));
                motes[i] = m;
            }
            Scatter();
            current = this;
            SetFull(GameSettings.FullEffects);
        }

        static SpriteRenderer Renderer(Transform parent, string name, Sprite sprite, string layer, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            SpriteRenderer r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingLayerName = layer;
            r.sortingOrder = order;
            return r;
        }

        Vector2 HalfView => new Vector2(view.orthographicSize * view.aspect, view.orthographicSize);

        void Scatter()
        {
            Vector2 half = HalfView;
            foreach (Mote m in motes) m.Offset = new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
        }

        void LateUpdate()
        {
            if (view == null || motes == null) return;
            float dt = Time.deltaTime, time = Time.time;
            Vector2 center = view.transform.position, half = HalfView + new Vector2(.6f, .6f);
            foreach (Mote m in motes)
            {
                // Dust wanders on slow sines; petals and leaves ride a diagonal breeze with a gentle sway and spin.
                Vector2 sway = m.Kind == 0 ? new Vector2(Mathf.Sin(time * .7f + m.Phase), Mathf.Cos(time * .5f + m.Phase)) * .06f
                    : new Vector2(0f, Mathf.Sin(time * 2.2f + m.Phase) * .35f);
                m.Offset += (m.Velocity + sway) * dt;
                if (m.Offset.x > half.x) m.Offset.x = -half.x; else if (m.Offset.x < -half.x) m.Offset.x = half.x;
                if (m.Offset.y > half.y) m.Offset.y = -half.y; else if (m.Offset.y < -half.y) m.Offset.y = half.y;
                m.T.position = new Vector3(center.x + m.Offset.x, center.y + m.Offset.y, 0f);
                if (m.Kind != 0) m.T.rotation = Quaternion.Euler(0f, 0f, time * m.Spin + m.Phase * 40f);
                else { Color c = m.R.color; c.a = m.Alpha * (.65f + .35f * Mathf.Sin(time * 1.3f + m.Phase)); m.R.color = c; }
            }
            Vector2 view2 = HalfView;
            for (int i = 0; i < rays.Length; i++)
            {
                // Rays drift slowly and breathe in strength, so the light feels like late afternoon through trees.
                float x = Mathf.Repeat(i * .8f + time * .015f + rayPhase[i], 2.4f) - 1.2f;
                rays[i].transform.position = new Vector3(center.x + x * view2.x, center.y + view2.y * .15f, 0f);
                rays[i].transform.localScale = new Vector3(view2.x * .35f, view2.y * 1.6f, 1f);
                rays[i].color = new Color(1f, .9f, .62f, .07f + .04f * Mathf.Sin(time * .4f + rayPhase[i]));
            }
        }

        static Sprite DotSprite() => dot != null ? dot : dot = Make(6, (x, y) => Mathf.Clamp01(1f - new Vector2(x - 2.5f, y - 2.5f).magnitude / 3f), "Mote");
        static Sprite PetalSprite() => petal != null ? petal : petal = Make(5, (x, y) => (x + y >= 2 && x + y <= 6 && Mathf.Abs(x - y) <= 2) ? 1f : 0f, "Petal");
        static Sprite LeafSprite() => leaf != null ? leaf : leaf = Make(6, (x, y) => Mathf.Abs(x - y) <= 1 && x + y >= 1 && x + y <= 9 ? 1f : 0f, "Leaf");
        static Sprite RaySprite() => ray != null ? ray : ray = Make(16, (x, y) => Mathf.Sin(Mathf.PI * (x + .5f) / 16f) * Mathf.Sin(Mathf.PI * (y + .5f) / 16f), "Ray", 16f, FilterMode.Bilinear);

        static Sprite Make(int size, System.Func<int, int, float> alpha, string name, float ppu = 16f, FilterMode filter = FilterMode.Point)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = filter, wrapMode = TextureWrapMode.Clamp, name = name };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(x, y)));
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), ppu);
        }
    }
}
