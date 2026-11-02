using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Crony
{
    /// <summary>One read-only view of the saved map for offline collision, routing, and the map UI.</summary>
    public sealed class TownshipWorldMap
    {
        readonly Tilemap ground, water, roads, paths, bridges, walls;
        readonly List<Collider2D> props = new List<Collider2D>();
        readonly bool[] walkable;
        readonly int width, height;
        readonly int minX, minY;
        readonly Dictionary<int, int[]> routes = new Dictionary<int, int[]>();
        static readonly Vector2[] Clearance = { Vector2.zero, new Vector2(.18f, 0f), new Vector2(-.18f, 0f), new Vector2(0f, .18f), new Vector2(0f, -.18f) };
        public Rect Bounds { get; }
        public Texture2D Overview { get; }

        public TownshipWorldMap(GameObject root)
        {
            foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>())
            {
                if (map.name.StartsWith("Ground")) ground = map;
                else if (map.name.StartsWith("Central Lake")) water = map;
                else if (map.name.StartsWith("Tarmac")) roads = map;
                else if (map.name.StartsWith("Footpaths")) paths = map;
                else if (map.name.StartsWith("Narrow Wooden")) bridges = map;
                else if (map.name.StartsWith("Obstacle")) walls = map;
            }
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>())
                if (collider.enabled && !collider.isTrigger && collider.GetComponent<Tilemap>() == null)
                    props.Add(collider);
            BoundsInt cells = ground != null ? ground.cellBounds : new BoundsInt(-33, -28, 0, 67, 56, 1);
            minX = cells.xMin; minY = cells.yMin; width = cells.size.x; height = cells.size.y;
            Bounds = new Rect(minX, minY, width, height);
            walkable = new bool[width * height];
            Physics2D.SyncTransforms();
            for (int i = 0; i < walkable.Length; i++) walkable[i] = CanStand(Center(i));
            Overview = BuildOverview();
        }

        bool Has(Tilemap map, Vector2 point) => map != null && map.HasTile(map.WorldToCell(point));

        public bool CanStand(Vector2 point)
        {
            foreach (Vector2 offset in Clearance)
            {
                Vector2 sample = point + offset;
                if (!Has(ground, sample) || Has(walls, sample)) return false;
                // Bridges, jetties and paved paths are above water in the authored map.
                if (Has(water, sample) && !Has(roads, sample) && !Has(paths, sample) && !Has(bridges, sample)) return false;
                foreach (Collider2D prop in props)
                    if (prop != null && prop.enabled && prop.OverlapPoint(sample)) return false;
            }
            return true;
        }

        public bool ClearSegment(Vector2 from, Vector2 to)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / .2f));
            for (int i = 1; i <= steps; i++) if (!CanStand(Vector2.Lerp(from, to, i / (float)steps))) return false;
            return true;
        }

        int Cell(Vector2 world)
        {
            int x = Mathf.FloorToInt(world.x) - minX, y = Mathf.FloorToInt(world.y) - minY;
            return x < 0 || y < 0 || x >= width || y >= height ? -1 : x + y * width;
        }
        Vector2 Center(int cell) => new Vector2(minX + cell % width + .5f, minY + cell / width + .5f);

        int NearestCell(Vector2 world)
        {
            int cell = Cell(world);
            if (cell >= 0 && walkable[cell]) return cell;
            int nearest = -1; float distance = float.MaxValue;
            for (int i = 0; i < walkable.Length; i++)
            {
                if (!walkable[i]) continue;
                float d = (Center(i) - world).sqrMagnitude;
                if (d < distance) { distance = d; nearest = i; }
            }
            return nearest;
        }

        public Vector2 NearestWalkable(Vector2 world)
        {
            if (CanStand(world)) return world;
            int cell = NearestCell(world);
            return cell >= 0 ? Center(cell) : world;
        }

        public List<Vector2> FindPath(Vector2 from, Vector2 to)
        {
            var result = new List<Vector2>();
            int start = NearestCell(from), end = NearestCell(to);
            if (start < 0 || end < 0) return result;
            if (!routes.TryGetValue(end, out int[] next))
            {
                // A reverse breadth-first field is reused by every bot heading to the same station.
                next = new int[walkable.Length];
                for (int i = 0; i < next.Length; i++) next[i] = -1;
                var queue = new Queue<int>(); queue.Enqueue(end); next[end] = end;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue(); int x = current % width, y = current / width;
                    Visit(x - 1, y, current, next, queue);
                    Visit(x + 1, y, current, next, queue);
                    Visit(x, y - 1, current, next, queue);
                    Visit(x, y + 1, current, next, queue);
                }
                if (routes.Count > 64) routes.Clear();
                routes[end] = next;
            }
            if (next[start] < 0) return result;
            // Include the starting cell center to avoid cutting a corner while joining a route.
            int at = start;
            for (int guard = 0; guard < walkable.Length; guard++)
            {
                result.Add(Center(at));
                if (at == end) break;
                at = next[at];
            }
            return result;
        }

        void Visit(int x, int y, int current, int[] next, Queue<int> queue)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int candidate = x + y * width;
            if (next[candidate] >= 0 || !walkable[candidate] || !ClearSegment(Center(candidate), Center(current))) return;
            next[candidate] = current; queue.Enqueue(candidate);
        }

        Texture2D BuildOverview()
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Township live map", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                Vector2 p = Center(i);
                Color color = new Color32(25, 49, 49, 255);
                if (Has(ground, p)) color = new Color32(132, 165, 110, 255);
                if (Has(water, p)) color = new Color32(64, 129, 150, 255);
                if (Has(paths, p)) color = new Color32(220, 207, 165, 255);
                if (Has(roads, p)) color = new Color32(91, 104, 102, 255);
                if (Has(bridges, p)) color = new Color32(188, 146, 90, 255);
                if (Has(walls, p)) color = new Color32(54, 84, 62, 255);
                foreach (Collider2D prop in props)
                    if (prop != null && prop.OverlapPoint(p)) color = new Color32(216, 184, 143, 255);
                pixels[i] = color;
            }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }
    }
}
