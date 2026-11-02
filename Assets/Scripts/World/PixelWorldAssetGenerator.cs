using System;
using UnityEngine;

namespace Crony
{
    /// <summary>Deterministic pixel-art fallback textures for the Township Park tilemap and props.</summary>
    public static class PixelWorldAssetGenerator
    {
        public const int PixelsPerUnit = 16;

        public enum TileKind
        {
            Grass, StonePath, TerraceSteps, BrickWalk, Sand, Water, Asphalt, DoubleYellowHorizontal,
            DoubleYellowVertical, CrosswalkHorizontal, CrosswalkVertical, CurbHorizontal,
            CurbVertical, WoodBridge, BrickWall, Hedge, Sidewalk, CourtMarking, LilyPad
        }

        public enum PropKind
        {
            HawkerStall, ConvenienceStore, AdministrativeOffice, CommunityHall, BusShelter, NoticeBoard, ParkBench,
            CellularTower, SolarStreetlight, TropicalTree, PalmTree, Hedge, BrickWall,
            RoadBarricade, StreetLamp, BadmintonCourt, LakeJetty, RouterCabinet,
            IceCreamFreezer, NasiLemakSteamer, FlowerBed, WaterHyacinths, FloatingBottle,
            FloatingCan, TaskMarker
        }

        struct Canvas
        {
            public readonly int width;
            public readonly int height;
            public readonly Color32[] pixels;

            public Canvas(int width, int height, Color32 clear)
            {
                this.width = width;
                this.height = height;
                pixels = new Color32[width * height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            }

            public void Pixel(int x, int y, Color32 color)
            {
                if ((uint)x >= width || (uint)y >= height) return;
                pixels[y * width + x] = color;
            }

            public void Rect(int x, int y, int w, int h, Color32 color)
            {
                int x0 = Mathf.Max(0, x), y0 = Mathf.Max(0, y);
                int x1 = Mathf.Min(width, x + w), y1 = Mathf.Min(height, y + h);
                for (int py = y0; py < y1; py++)
                    for (int px = x0; px < x1; px++) pixels[py * width + px] = color;
            }

            public void Border(int x, int y, int w, int h, Color32 color)
            {
                Rect(x, y, w, 1, color); Rect(x, y + h - 1, w, 1, color);
                Rect(x, y, 1, h, color); Rect(x + w - 1, y, 1, h, color);
            }

            public void HLine(int x, int y, int length, Color32 color) => Rect(x, y, length, 1, color);
            public void VLine(int x, int y, int length, Color32 color) => Rect(x, y, 1, length, color);
        }

        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);

        // Warm, hand-painted palette: no pure primaries, and shadows lean indigo or forest green instead of black.
        static readonly Color32 ForestShadow = C(52, 82, 60), VioletShadow = C(112, 98, 124);

        public static Texture2D CreateTileTexture(TileKind kind, int seed = 0) => CreateTileTexture(kind, seed, 0);

        /// <param name="variant">Grass: 0-3 alternate patches. Water: 0-3 caustic animation frames.</param>
        public static Texture2D CreateTileTexture(TileKind kind, int seed, int variant)
        {
            Canvas c = new Canvas(16, 16, C(0, 0, 0, 0));
            System.Random random = new System.Random(seed ^ ((int)kind * 7919) ^ (variant * 104729));
            switch (kind)
            {
                case TileKind.Grass: PaintGrass(ref c, random, variant); break;
                case TileKind.StonePath:
                case TileKind.BrickWalk:
                    c = new Canvas(16, 16, kind == TileKind.StonePath ? C(222, 205, 170) : C(214, 176, 140));
                    Color32 mortar = kind == TileKind.StonePath ? VioletShadow : C(150, 108, 102);
                    c.HLine(0, 0, 16, mortar); c.HLine(0, 8, 16, mortar);
                    c.VLine(0, 0, 8, mortar); c.VLine(8, 8, 8, mortar);
                    c.VLine(15, 0, 8, mortar); c.VLine(7, 8, 8, mortar);
                    c.HLine(1, 7, 6, C(196, 176, 150)); c.HLine(9, 15, 6, C(196, 176, 150));
                    c.Pixel(3, 3, C(240, 228, 198)); c.Pixel(12, 12, C(236, 214, 176)); c.Pixel(5, 11, C(204, 182, 146));
                    break;
                case TileKind.TerraceSteps:
                    c = new Canvas(16, 16, C(220, 204, 172));
                    for (int y = 3; y < 16; y += 5) { c.HLine(0, y, 16, VioletShadow); c.HLine(0, y + 1, 16, C(244, 232, 204)); }
                    c.VLine(1, 0, 3, C(196, 178, 148)); c.VLine(14, 4, 4, C(196, 178, 148));
                    break;
                case TileKind.Sand:
                    c = new Canvas(16, 16, C(230, 212, 172));
                    for (int i = 0; i < 22; i++)
                    {
                        int pick = random.Next(4);
                        c.Pixel(random.Next(16), random.Next(16), pick == 0 ? C(244, 230, 198) : pick == 1 ? C(212, 186, 140) : pick == 2 ? C(198, 162, 100) : C(222, 200, 158));
                    }
                    break;
                case TileKind.Water: PaintWater(ref c, variant); break;
                case TileKind.LilyPad:
                    PaintWater(ref c, variant);
                    c.Rect(4, 7, 8, 5, C(104, 150, 88)); c.Rect(6, 5, 4, 9, C(118, 164, 96));
                    c.Rect(5, 7, 6, 1, C(160, 196, 120)); c.Pixel(8, 5, C(86, 160, 158)); c.Pixel(10, 9, C(244, 196, 206));
                    break;
                case TileKind.Asphalt:
                    c = new Canvas(16, 16, C(122, 113, 104));
                    for (int i = 0; i < 16; i++) c.Pixel(random.Next(16), random.Next(16), random.Next(2) == 0 ? C(138, 128, 116) : C(104, 96, 92));
                    break;
                case TileKind.DoubleYellowHorizontal:
                    c = new Canvas(16, 16, C(122, 113, 104));
                    c.HLine(0, 6, 16, C(228, 186, 96)); c.HLine(0, 9, 16, C(228, 186, 96));
                    break;
                case TileKind.DoubleYellowVertical:
                    c = new Canvas(16, 16, C(122, 113, 104));
                    c.VLine(6, 0, 16, C(228, 186, 96)); c.VLine(9, 0, 16, C(228, 186, 96));
                    break;
                case TileKind.CrosswalkHorizontal:
                case TileKind.CrosswalkVertical:
                    c = new Canvas(16, 16, C(122, 113, 104));
                    for (int i = 0; i < 16; i += 4)
                    {
                        if (kind == TileKind.CrosswalkHorizontal) c.Rect(i, 2, 2, 12, C(242, 232, 208));
                        else c.Rect(2, i, 12, 2, C(242, 232, 208));
                    }
                    break;
                case TileKind.CurbHorizontal:
                case TileKind.CurbVertical:
                    c = new Canvas(16, 16, C(226, 212, 184));
                    if (kind == TileKind.CurbHorizontal) { c.HLine(0, 7, 16, VioletShadow); c.HLine(0, 8, 16, C(246, 236, 212)); }
                    else { c.VLine(7, 0, 16, VioletShadow); c.VLine(8, 0, 16, C(246, 236, 212)); }
                    break;
                case TileKind.WoodBridge:
                    c = new Canvas(16, 16, C(184, 132, 82));
                    for (int y = 0; y < 16; y += 4)
                    {
                        c.HLine(0, y, 16, C(112, 76, 60));
                        c.HLine(1, y + 1, 14, C(214, 166, 108));
                        c.Pixel(2, y + 2, C(96, 70, 64)); c.Pixel(13, y + 2, C(96, 70, 64));
                    }
                    c.VLine(0, 0, 16, C(228, 186, 128)); c.VLine(15, 0, 16, C(118, 82, 70));
                    break;
                case TileKind.BrickWall:
                    c = new Canvas(16, 16, C(180, 110, 86));
                    c.HLine(0, 7, 16, C(226, 204, 176)); c.HLine(0, 15, 16, C(226, 204, 176));
                    c.VLine(7, 0, 7, C(226, 204, 176)); c.VLine(3, 8, 7, C(226, 204, 176)); c.VLine(12, 8, 7, C(226, 204, 176));
                    c.HLine(0, 0, 16, C(204, 138, 104)); c.HLine(0, 6, 7, C(140, 92, 98)); c.HLine(8, 6, 8, C(140, 92, 98));
                    break;
                case TileKind.Hedge:
                    c = new Canvas(16, 16, ForestShadow);
                    c.Rect(1, 2, 5, 6, C(92, 122, 66)); c.Rect(8, 1, 6, 6, C(108, 138, 74));
                    c.Rect(4, 9, 7, 5, C(86, 116, 70)); c.Pixel(2, 12, C(150, 172, 96)); c.Pixel(12, 11, C(144, 176, 104));
                    c.Pixel(9, 3, C(164, 186, 112)); c.Pixel(6, 10, C(118, 150, 106));
                    break;
                case TileKind.Sidewalk:
                    c = new Canvas(16, 16, C(226, 214, 190));
                    c.HLine(0, 7, 16, C(190, 176, 160)); c.VLine(7, 0, 8, C(190, 176, 160)); c.VLine(12, 8, 8, C(190, 176, 160));
                    break;
                case TileKind.CourtMarking:
                    c = new Canvas(16, 16, C(104, 150, 112));
                    c.Rect(0, 0, 16, 1, C(242, 234, 212)); c.Rect(0, 15, 16, 1, C(242, 234, 212));
                    c.Rect(0, 0, 1, 16, C(242, 234, 212)); c.Rect(15, 0, 1, 16, C(242, 234, 212));
                    c.VLine(8, 0, 16, C(242, 234, 212));
                    break;
            }
            return ToTexture(c);
        }

        /// <summary>Olive, moss and warm jade with hand-drawn blade clusters; variants keep large lawns from tiling visibly.</summary>
        static void PaintGrass(ref Canvas c, System.Random random, int variant)
        {
            Color32[] bases = { C(112, 140, 72), C(104, 134, 70), C(116, 146, 82), C(108, 138, 76) };
            c = new Canvas(16, 16, bases[variant & 3]);
            Color32[] tones = { C(96, 124, 64), C(126, 152, 80), C(98, 146, 104), C(136, 160, 88) };
            for (int i = 0; i < 26; i++) c.Pixel(random.Next(16), random.Next(16), tones[random.Next(tones.Length)]);
            int clusters = 3 + random.Next(2);
            for (int i = 0; i < clusters; i++)
            {
                // A blade cluster: a forest-green base with two or three upright strokes catching warm light.
                int x = 1 + random.Next(13), y = 1 + random.Next(12);
                c.Pixel(x, y, ForestShadow); c.Pixel(x + 1, y, ForestShadow);
                c.Pixel(x - 1, y + 1, C(150, 172, 92)); c.Pixel(x - 1, y + 2, C(172, 186, 104));
                c.Pixel(x + 1, y + 1, C(140, 166, 88)); c.Pixel(x + 2, y + 2, C(164, 180, 100));
                c.Pixel(x, y + 1, C(122, 152, 84));
            }
            if (variant == 2) { c.Pixel(4 + random.Next(8), 4 + random.Next(8), C(246, 226, 200)); c.Pixel(3 + random.Next(10), 3 + random.Next(10), C(240, 190, 196)); }
        }

        /// <summary>Teal-to-aquamarine watercolour depth with soft caustic light; four frames loop seamlessly.</summary>
        static void PaintWater(ref Canvas c, int frame)
        {
            c = new Canvas(16, 16, C(0, 0, 0, 255));
            float phase = (frame & 3) * Mathf.PI * .5f;
            Color deep = new Color32(58, 134, 140, 255), shallow = new Color32(104, 182, 172, 255), glow = new Color32(184, 232, 214, 255);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    float u = x / 16f * Mathf.PI * 2f, v = y / 16f * Mathf.PI * 2f;
                    // Whole-cycle sines tile seamlessly across cells and loop seamlessly across frames.
                    float depth = .5f + .16f * Mathf.Sin(u + v * 2f) + .14f * Mathf.Sin(v - u + phase * .5f);
                    float caustic = Mathf.Abs(Mathf.Sin(u * 2f + phase) + Mathf.Sin(v * 2f - phase) + Mathf.Sin((u + v) * 1f + phase * 2f));
                    Color pixel = Color.Lerp(deep, shallow, Mathf.Clamp01(depth));
                    // Soft, sparse light patches rather than a hard lattice, so repeated tiles read as one lake.
                    if (caustic < .25f) pixel = Color.Lerp(pixel, glow, .32f);
                    else if (caustic < .55f) pixel = Color.Lerp(pixel, glow, .12f);
                    pixel = new Color(Mathf.Round(pixel.r * 32f) / 32f, Mathf.Round(pixel.g * 32f) / 32f, Mathf.Round(pixel.b * 32f) / 32f, 1f);
                    c.Pixel(x, y, pixel);
                }
        }

        public static Texture2D CreatePropTexture(PropKind kind)
        {
            int width = 32, height = 32;
            switch (kind)
            {
                case PropKind.HawkerStall: width = 64; height = 64; break;
                case PropKind.ConvenienceStore: width = 64; height = 64; break;
                case PropKind.AdministrativeOffice: width = 64; height = 64; break;
                case PropKind.CommunityHall: width = 96; height = 64; break;
                case PropKind.BusShelter: width = 64; height = 48; break;
                case PropKind.NoticeBoard: width = 32; height = 40; break;
                case PropKind.ParkBench: width = 32; height = 16; break;
                case PropKind.CellularTower: width = 32; height = 64; break;
                case PropKind.SolarStreetlight: width = 16; height = 64; break;
                case PropKind.TropicalTree: width = 48; height = 48; break;
                case PropKind.PalmTree: width = 48; height = 64; break;
                case PropKind.Hedge: width = 32; height = 16; break;
                case PropKind.BrickWall: width = 32; height = 16; break;
                case PropKind.RoadBarricade: width = 32; height = 16; break;
                case PropKind.StreetLamp: width = 16; height = 48; break;
                case PropKind.BadmintonCourt: width = 64; height = 48; break;
                case PropKind.LakeJetty: width = 64; height = 16; break;
                case PropKind.RouterCabinet: width = 16; height = 24; break;
                case PropKind.IceCreamFreezer: width = 32; height = 24; break;
                case PropKind.NasiLemakSteamer: width = 16; height = 16; break;
                case PropKind.FlowerBed: width = 32; height = 16; break;
                case PropKind.WaterHyacinths: width = 32; height = 24; break;
                case PropKind.FloatingBottle: width = 16; height = 16; break;
                case PropKind.FloatingCan: width = 16; height = 16; break;
                case PropKind.TaskMarker: width = 32; height = 32; break;
            }

            Canvas c = new Canvas(width, height, C(0, 0, 0, 0));
            Color32 outline = C(51, 46, 51), wood = C(132, 86, 55), lightWood = C(187, 131, 76);
            switch (kind)
            {
                case PropKind.HawkerStall:
                    c.Rect(5, 23, 54, 5, outline); c.Rect(7, 26, 50, 27, wood); c.Rect(5, 49, 54, 7, outline); c.Rect(8, 49, 48, 4, lightWood);
                    c.Rect(4, 13, 56, 13, outline);
                    for (int x = 5; x < 59; x += 8) c.Rect(x, 14, 4, 11, ((x / 8) & 1) == 0 ? C(205, 69, 64) : C(240, 222, 181));
                    c.Rect(8, 26, 48, 3, C(247, 228, 184));
                    c.Rect(10, 32, 14, 10, C(60, 100, 79)); c.Rect(34, 31, 18, 11, C(228, 186, 75));
                    c.Rect(13, 34, 8, 4, C(240, 221, 176)); c.Rect(38, 33, 10, 4, C(210, 62, 54));
                    c.Rect(14, 56, 4, 5, outline); c.Rect(46, 56, 4, 5, outline);
                    break;
                case PropKind.ConvenienceStore:
                    c.Rect(3, 2, 58, 59, outline); c.Rect(6, 5, 52, 53, C(222, 204, 162));
                    c.Rect(6, 42, 52, 16, C(120, 156, 137)); c.Rect(7, 45, 22, 13, C(71, 124, 144)); c.Rect(33, 45, 22, 13, C(61, 105, 128));
                    c.Rect(7, 48, 22, 1, C(177, 220, 210)); c.Rect(33, 48, 22, 1, C(177, 220, 210));
                    c.Rect(4, 9, 56, 11, C(53, 118, 107)); c.Rect(8, 11, 48, 6, C(241, 215, 151));
                    c.Rect(12, 23, 40, 15, C(159, 124, 84)); c.Rect(16, 25, 32, 10, C(227, 211, 174));
                    c.Rect(27, 42, 6, 16, outline); c.Rect(29, 43, 2, 15, C(190, 177, 147));
                    break;
                case PropKind.AdministrativeOffice:
                    c.Rect(3, 4, 58, 57, outline); c.Rect(6, 7, 52, 51, C(211, 193, 163));
                    c.Rect(5, 10, 54, 10, C(98, 115, 112)); c.Rect(8, 12, 48, 5, C(237, 221, 182));
                    c.Rect(11, 25, 17, 14, C(75, 127, 139)); c.Rect(36, 25, 17, 14, C(75, 127, 139));
                    c.Rect(12, 27, 15, 2, C(174, 216, 203)); c.Rect(37, 27, 15, 2, C(174, 216, 203));
                    c.Rect(26, 44, 12, 14, C(109, 73, 55)); c.Rect(28, 46, 8, 12, C(176, 130, 82));
                    c.Rect(13, 45, 9, 7, C(194, 71, 69)); c.Rect(43, 45, 9, 7, C(74, 132, 87));
                    c.Rect(14, 46, 7, 2, C(243, 226, 188)); c.Rect(44, 46, 7, 2, C(233, 237, 204));
                    break;
                case PropKind.CommunityHall:
                    c.Rect(4, 15, 88, 46, outline); c.Rect(8, 18, 80, 40, C(218, 204, 166));
                    c.Rect(1, 12, 94, 8, C(111, 62, 59)); c.Rect(7, 5, 82, 12, outline); c.Rect(10, 7, 76, 8, C(153, 77, 63));
                    c.Rect(15, 22, 66, 6, C(232, 219, 186)); c.Rect(20, 31, 18, 27, C(77, 117, 115)); c.Rect(58, 31, 18, 27, C(77, 117, 115));
                    c.Rect(42, 34, 12, 24, C(112, 75, 57)); c.Rect(44, 37, 8, 21, C(173, 123, 78));
                    c.Rect(42, 59, 12, 2, outline);
                    break;
                case PropKind.BusShelter:
                    c.Rect(4, 12, 56, 5, outline); c.Rect(6, 13, 52, 3, C(68, 132, 137));
                    c.Rect(7, 17, 3, 25, outline); c.Rect(54, 17, 3, 25, outline); c.Rect(9, 18, 2, 21, C(147, 190, 184)); c.Rect(53, 18, 2, 21, C(147, 190, 184));
                    c.Rect(13, 33, 38, 4, outline); c.Rect(15, 34, 34, 2, wood); c.Rect(17, 38, 3, 5, outline); c.Rect(45, 38, 3, 5, outline);
                    c.Rect(25, 20, 14, 9, C(221, 205, 161)); c.Rect(27, 22, 10, 5, C(79, 132, 151));
                    break;
                case PropKind.NoticeBoard:
                    c.Rect(2, 3, 28, 31, outline); c.Rect(4, 5, 24, 25, C(161, 99, 63)); c.Rect(7, 8, 18, 17, C(205, 161, 105));
                    c.Rect(8, 10, 7, 6, C(245, 236, 203)); c.Rect(16, 9, 7, 8, C(169, 207, 194)); c.Rect(9, 19, 13, 4, C(248, 227, 160));
                    c.Pixel(8, 10, C(202, 62, 58)); c.Pixel(19, 9, C(198, 58, 63)); c.Rect(13, 34, 2, 6, outline); c.Rect(22, 34, 2, 6, outline);
                    break;
                case PropKind.ParkBench:
                    c.Rect(3, 4, 26, 3, outline); c.Rect(4, 5, 24, 1, C(193, 139, 78));
                    c.Rect(4, 8, 24, 3, outline); c.Rect(5, 9, 22, 1, C(165, 111, 62)); c.Rect(6, 11, 3, 4, outline); c.Rect(23, 11, 3, 4, outline);
                    break;
                case PropKind.CellularTower:
                    c.VLine(14, 14, 44, outline); c.VLine(17, 14, 44, outline); c.HLine(10, 20, 12, outline); c.HLine(8, 34, 16, outline); c.HLine(5, 48, 22, outline); c.HLine(2, 59, 28, outline);
                    c.HLine(12, 18, 8, C(171, 182, 175)); c.HLine(9, 32, 14, C(171, 182, 175)); c.HLine(6, 46, 20, C(171, 182, 175));
                    c.VLine(15, 3, 11, C(87, 150, 185)); c.HLine(10, 5, 12, C(87, 150, 185)); c.HLine(12, 8, 8, C(87, 150, 185));
                    c.Rect(9, 3, 2, 2, C(239, 194, 74)); c.Rect(21, 3, 2, 2, C(239, 194, 74));
                    break;
                case PropKind.SolarStreetlight:
                    c.Rect(7, 9, 2, 53, outline); c.Rect(8, 10, 1, 51, C(152, 160, 145));
                    c.Rect(2, 5, 12, 4, outline); c.Rect(3, 6, 10, 2, C(47, 81, 104)); c.VLine(5, 6, 2, C(101, 157, 177)); c.VLine(9, 6, 2, C(101, 157, 177));
                    c.Rect(3, 10, 11, 2, C(232, 202, 104)); c.Rect(2, 61, 12, 3, outline);
                    break;
                case PropKind.TropicalTree:
                    c.Rect(21, 5, 7, 34, C(100, 66, 43)); c.Rect(22, 7, 4, 29, C(143, 90, 48));
                    c.Rect(5, 13, 20, 21, C(28, 103, 63)); c.Rect(12, 6, 26, 23, C(39, 125, 68)); c.Rect(20, 14, 22, 22, C(32, 111, 63));
                    c.Rect(11, 18, 18, 17, C(57, 147, 76)); c.Rect(17, 11, 12, 7, C(81, 165, 87)); c.Pixel(27, 19, C(123, 180, 91));
                    break;
                case PropKind.PalmTree:
                    c.Rect(22, 14, 5, 47, C(125, 81, 48)); c.Rect(23, 17, 2, 42, C(170, 119, 65));
                    c.Rect(8, 5, 18, 5, C(36, 126, 69)); c.Rect(25, 4, 18, 5, C(45, 142, 74));
                    c.Rect(2, 14, 24, 4, C(48, 137, 69)); c.Rect(24, 15, 22, 4, C(45, 134, 66));
                    c.Rect(8, 10, 17, 4, C(64, 155, 75)); c.Rect(26, 10, 14, 4, C(72, 164, 80));
                    c.Rect(10, 19, 16, 4, C(60, 151, 73)); c.Rect(25, 20, 14, 4, C(55, 143, 68));
                    c.Rect(18, 11, 6, 6, C(224, 174, 68));
                    break;
                case PropKind.Hedge:
                    c.Rect(1, 5, 30, 10, outline); c.Rect(2, 3, 9, 8, C(47, 119, 66)); c.Rect(10, 2, 10, 10, C(67, 145, 76)); c.Rect(20, 4, 10, 8, C(53, 132, 69)); c.Rect(3, 12, 26, 2, C(37, 94, 59));
                    break;
                case PropKind.BrickWall:
                    c.Rect(0, 2, 32, 14, C(119, 72, 64)); c.HLine(0, 8, 32, C(69, 55, 54)); c.VLine(8, 2, 6, C(69, 55, 54)); c.VLine(22, 9, 6, C(69, 55, 54)); c.VLine(15, 9, 6, C(69, 55, 54)); c.HLine(1, 3, 30, C(170, 108, 84));
                    break;
                case PropKind.RoadBarricade:
                    c.Rect(2, 5, 28, 9, outline); c.Rect(4, 7, 24, 5, C(223, 206, 159)); c.Rect(6, 7, 5, 5, C(210, 68, 56)); c.Rect(16, 7, 5, 5, C(210, 68, 56)); c.Rect(26, 7, 2, 5, C(210, 68, 56));
                    c.Rect(7, 14, 3, 2, outline); c.Rect(23, 14, 3, 2, outline);
                    break;
                case PropKind.StreetLamp:
                    c.Rect(7, 11, 2, 36, outline); c.Rect(8, 12, 1, 34, C(177, 180, 157)); c.Rect(4, 7, 9, 4, outline); c.Rect(5, 8, 7, 2, C(239, 211, 123)); c.Rect(6, 4, 7, 2, C(99, 133, 134));
                    break;
                case PropKind.BadmintonCourt:
                    c.Rect(2, 2, 60, 44, C(46, 119, 89)); c.Border(3, 3, 58, 42, C(244, 236, 207)); c.VLine(31, 3, 42, C(244, 236, 207)); c.HLine(3, 23, 58, C(244, 236, 207));
                    c.Rect(7, 22, 2, 5, outline); c.Rect(55, 22, 2, 5, outline); c.HLine(8, 24, 48, C(225, 219, 192));
                    break;
                case PropKind.LakeJetty:
                    c.Rect(0, 2, 64, 13, outline); for (int x = 2; x < 64; x += 8) c.VLine(x, 3, 10, C(194, 143, 83));
                    c.HLine(0, 3, 64, C(222, 173, 108)); c.HLine(0, 13, 64, C(84, 58, 43));
                    break;
                case PropKind.RouterCabinet:
                    c.Rect(2, 3, 12, 19, outline); c.Rect(4, 5, 8, 15, C(67, 107, 117)); c.Rect(6, 7, 4, 2, C(67, 188, 126));
                    c.Pixel(5, 11, C(232, 188, 75)); c.Pixel(8, 11, C(232, 188, 75)); c.Rect(5, 15, 6, 1, C(180, 205, 188));
                    break;
                case PropKind.IceCreamFreezer:
                    c.Rect(2, 2, 28, 21, outline); c.Rect(4, 4, 24, 14, C(119, 181, 191)); c.Rect(6, 6, 20, 10, C(223, 238, 217));
                    c.Rect(4, 17, 24, 4, C(86, 129, 145)); c.Rect(8, 6, 3, 8, C(241, 197, 89)); c.Rect(14, 6, 3, 8, C(208, 90, 94)); c.Rect(20, 6, 3, 8, C(132, 99, 173));
                    break;
                case PropKind.NasiLemakSteamer:
                    c.Rect(2, 8, 12, 6, outline); c.Rect(3, 7, 10, 5, C(193, 196, 185)); c.Rect(5, 4, 6, 3, C(116, 126, 124)); c.Rect(6, 2, 4, 2, C(219, 229, 213));
                    break;
                case PropKind.FlowerBed:
                    c.Rect(1, 5, 30, 10, C(111, 76, 52)); c.Rect(3, 7, 26, 5, C(48, 118, 64));
                    c.Pixel(6, 6, C(232, 104, 103)); c.Pixel(12, 8, C(244, 207, 92)); c.Pixel(18, 6, C(171, 123, 187)); c.Pixel(24, 9, C(240, 155, 113));
                    break;
                case PropKind.WaterHyacinths:
                    c.Rect(2, 12, 10, 7, C(52, 119, 75)); c.Rect(8, 8, 13, 10, C(69, 143, 83));
                    c.Rect(17, 11, 13, 8, C(53, 126, 74)); c.Rect(9, 6, 5, 4, C(178, 151, 192));
                    c.Rect(18, 5, 5, 5, C(204, 174, 210)); c.Rect(20, 6, 1, 1, C(250, 232, 195));
                    c.Pixel(5, 11, C(107, 173, 99)); c.Pixel(24, 13, C(106, 167, 90));
                    break;
                case PropKind.FloatingBottle:
                    c.Rect(6, 3, 4, 2, C(193, 224, 194)); c.Rect(5, 5, 6, 8, C(91, 181, 175));
                    c.Rect(6, 6, 2, 5, C(164, 221, 202)); c.Rect(5, 12, 6, 2, C(65, 143, 153));
                    break;
                case PropKind.FloatingCan:
                    c.Rect(4, 4, 8, 9, C(190, 198, 186)); c.HLine(4, 5, 8, C(226, 231, 208));
                    c.HLine(4, 11, 8, C(127, 142, 141)); c.Rect(5, 7, 6, 2, C(189, 82, 71));
                    break;
                case PropKind.TaskMarker:
                    for (int y = 3; y <= 29; y++)
                    for (int x = 4; x <= 27; x++)
                    {
                        int dx = x - 15, dy = y - 15;
                        int distance = dx * dx + dy * dy;
                        if (distance <= 11 * 11) c.Pixel(x, y, distance <= 7 * 7 ? C(255, 238, 190) : C(48, 54, 54));
                    }
                    for (int y = 2; y <= 15; y++)
                    for (int x = 12; x <= 19; x++)
                        if ((x - 16) * (x - 16) + (y - 14) * (y - 14) <= 4) c.Pixel(x, y, C(48, 54, 54));
                    break;
            }
            return ToTexture(c);
        }

        static Texture2D ToTexture(Canvas c)
        {
            Texture2D texture = new Texture2D(c.width, c.height, TextureFormat.RGBA32, false, true)
            {
                name = "Generated Township Pixel Art",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            texture.SetPixels32(c.pixels);
            texture.Apply(false, false);
            return texture;
        }

        public static Sprite CreateSprite(PropKind kind, Vector2 pivot)
        {
            Texture2D texture = CreatePropTexture(kind);
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
