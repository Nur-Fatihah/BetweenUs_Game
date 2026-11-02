# Crony visual pipeline: specification sheet

> **Cozy fantasy overhaul (current).** The sections below are updated for the warm, hand-painted direction: parchment UI, the organic world palette, atmosphere, the acoustic soundscape, the stump meeting table and hollow-stump vents. Rebuild steps: **Crony > Rendering > Repaint World Tiles**, then **Crony > Rendering > Set Up 2D Lighting Pipeline**.

## 0. Cozy fantasy overhaul at a glance

| Area | Implementation |
| --- | --- |
| World palette | `PixelWorldAssetGenerator`: grass in olive, moss and warm jade with forest-green blade clusters (4 variants scattered by a stable cell hash); paths in cream, beige and ochre; honey wood bridge; terracotta brick; violet-grey mortar and curbs instead of black |
| Water | `AnimatedWaterTile`: 4-frame teal-to-aquamarine watercolour depth with soft caustic light, played by the tilemap renderer itself (no CPU cost), with per-cell phase offsets so the lake never pulses in sync. Opaque on purpose: grass lies under the lake tiles |
| Shadows | Indigo/violet everywhere: UI drop shadows `ShadowIndigo`, avatar contact shadow, vision fog, vignette |
| Atmosphere | `Atmosphere` on the main camera: 3 slanted god rays (alpha 0.07 ± 0.04, slow drift), 34 golden dust motes, 12 petals and 8 leaves on a diagonal breeze; one pooled Update, ~60 sprites |
| Windows | Parchment sprite (tiled fibres, wobbly dark-olive ink border, inner highlight); `UIPopIn` opens every window with a 0.12 s spring (90% → 102% → 100%) plus alpha fade |
| Tabs | `PixelUiTheme.SetTabState`: stitched cloth tags; active grows 6% and turns gold, inactive desaturate to 70% opacity |
| Buttons | Pebble shape (anti-aliased, top-lit gradient) inside a dark-olive frame; hover 5% scale + golden halo + inner glow; press drops 2 units, desaturates 35% and emits an expanding ring |
| Type | Titles and announcements: Pixelify Sans Bold (OFL), hinted raster. Everything read for long: Quicksand SemiBold (OFL), anti-aliased. Font licences sit next to the fonts in `Assets/Resources/Fonts` |
| Music | `GameAudioManager` + `AcousticSynth`, rendered on worker threads: lobby F-major waltz (piano, picked guitar, flute counter-line, pizzicato), park C-major stroll (flute, guitar, piano), and a gentle A-minor tension piece (cello pizzicato ostinato, quiet woodwinds) for meetings and sabotage |
| Effects | Wooden tock clicks, parchment hover, water-drop chat ping, glockenspiel task chimes and a sparkling chime string on completion, hand bell for meetings, alternating grass rustle / path crunch / hollow bridge clop footsteps |
| Meetings | An ancient cross-cut stump table with drifting fireflies; each seat shows the player's avatar as a breathing portrait |
| Vents | `HollowStumps`: 5 ivy-covered hollow stumps near checkpoints; an impostor presses E to travel to the next one (8 s cooldown, host-validated online) |
| Performance | Bloom at quarter resolution with 4 passes. Settings > Display > **Full visual effects** turns off post-processing, god rays and petals for slower GPUs |


This sheet is the reference for lighting, post-processing, UI and motion in Crony. Every value marked **Implemented** is what the project ships with today; **Spec only** items are the agreed configuration for when the matching art or feature exists.

Re-create the rendering setup at any time with **Crony > Rendering > Set Up 2D Lighting Pipeline** (`Assets/Editor/CronyRenderingSetup.cs`). It is safe to re-run.

---

## 1. Render pipeline (Implemented)

The project previously pointed every quality level at a URP asset that no longer existed, so it silently fell back to the built-in renderer and every `Light2D` was inert. It now uses:

| Setting | Value | Why |
| --- | --- | --- |
| Pipeline asset | `Assets/Settings/Rendering/Crony_URP2D.asset` (all six quality levels + Graphics default) | One pipeline everywhere |
| Renderer | `Crony_2DRenderer.asset` (URP 2D Renderer) | 2D lights, shadows, normal maps |
| HDR | On | Light peaks above 1.0 feed bloom |
| MSAA | Off | Pixel art must stay hard-edged |
| Color grading | High Dynamic Range, LUT size 32 | Matches the generated LUT |
| Sprite material | `Sprite-Lit-Default` (131 scene/prefab renderers converted) | Sprites receive 2D light |
| Camera | Post-processing on, HDR on, AA none | |
| Pixel Perfect Camera | Ref 192×108 at 16 PPU in play, **Grid Snapping: None** | Sprites keep sub-pixel positions (section 6); the camera itself is still pixel-snapped by `CameraFollow2D` |
| Global light | Intensity 1.0, colour `#FFFAF2` offline; 0.72 online (0.12 in a lights-out sabotage) | At 1.0 lit sprites look exactly as painted; online darkness gives the personal vision light meaning |

All lights target **every sorting layer** (`GlowLight.TargetAllLayers`). Without this the `Avatar` layer is skipped and characters render black.

Default 2D renderer blend styles: 0 Multiply (ambient), **1 Additive (all glows)**, 2 Multiply with Mask, 3 Additive with Mask.

## 2. Normal maps on 2D sprites (Spec only)

No hand-painted normal maps exist yet; the lit material works without them. When adding them:

1. Author one normal map per sprite sheet at the **same pixel size** as the albedo. Use flat-shaded, pixel-quantised normals (no smooth gradients wider than 2 px), lit from the top-left to match the palette.
2. Import: Texture Type **Normal map**, Filter Mode **Point**, Compression **None**, Mipmaps off, same Pixels Per Unit as the albedo.
3. In the albedo's Sprite Editor > **Secondary Textures**, add name `_NormalMap` and assign the texture.
4. Lights: Beacon and Alarm glows `Normal Map Quality = Accurate`, `Normal Map Distance = 3` (world units the light sits above the sprites). Global light: normal maps ignored.
5. Keep normals subtle on characters (bevel 1 px) so faces stay readable at 192×108.

## 3. Ambient occlusion layers

| Layer | Status | Configuration |
| --- | --- | --- |
| Character contact shadow | **Implemented** (avatar layer 0, `Avatar Shadow`) | Soft ellipse under the feet, stays on the ground while the body bobs and tightens up to 5% as the body lifts (`AvatarSecondaryMotion`) |
| Tile AO | Spec only | A dedicated `AO` tilemap between Ground and Props: 1-tile soft edges along walls, buildings and water edges, black at 25% alpha, `Sprite-Unlit-Default` so lighting doesn't brighten it |

## 4. Light emission profiles (Implemented, `Assets/Scripts/World/GlowLight.cs`)

All glows are additive point lights (blend style 1), falloff 0.65, inner radius = 15% of outer, no shadows.

| Profile | Used for | Colour | Outer radius | Intensity animation |
| --- | --- | --- | --- | --- |
| Beacon | Open task checkpoint | `#FFC76B` | 1.3 | 0.5 ± 0.12, sine at 2.6 rad/s (slow breathing) |
| Alarm | Checkpoint that repairs the active sabotage | `#FF5240` | 2.2 | 0.25 → 1.2, sine at 3 Hz; peaks above 1.0 so bloom flares |
| Lamp | Street lamps (available for set dressing) | `#FFD18C` | 1.8 | 0.55 ± 0.04 Perlin flicker (never strobes) |
| Done | Completed checkpoint | `#73F2B3` | 0.9 | Steady 0.22 |

Online, each `TownshipTaskStation` switches profile from its `Completed` state and the active sabotage. Offline, the runtime does the same on its task markers.

## 5. Post-processing stack (Implemented, `Crony_PostProcessing.asset` on the global volume)

| Effect | Settings | Intent |
| --- | --- | --- |
| Bloom | Threshold 0.95, intensity 0.35, scatter 0.55, tint `#FFF2D9`, low-quality filtering | Only HDR peaks (glows, alarms) bloom, so pixel grids soften around lights without smearing the whole frame |
| Chromatic aberration | 0.04 | A hint of lens fringe at the screen edges only |
| Color adjustments | Contrast +6, saturation +4 | Slight punch |
| Vignette | 0.18, smoothness 0.5, colour `#0D141A` | Pulls focus to the centre |
| Color lookup | `Crony_GradeLUT.png`, contribution 0.65 | Shared grade (below) |
| Tonemapping | None | Keeps the hand-picked palette exact |

**Grading LUT** (generated by the setup tool): a 1024×32 strip (32³), linear, uncompressed, no mips, clamp, bilinear. Grade: shadows lifted towards teal (+0.012 G, +0.03 B, weighted by (1−L)²), highlights warmed (+0.03 R, +0.012 G, −0.02 B, weighted by L²), a 1.06 S-curve around mid-grey, then +6% saturation. Regenerate by re-running the tool.

## 6. Sub-pixel secondary motion (Implemented, `AvatarSecondaryMotion`)

Sprites are no longer snapped to the art grid, so these offsets render as smooth sub-pixel motion at screen resolution instead of popping one art pixel at a time. One art pixel = 1/32 unit.

| Motion | Rule |
| --- | --- |
| Walk bob | \|sin(phase)\| × 1.2 art px; phase speed 8–13 rad/s scaling with movement speed; eases back to rest when stopping |
| Idle breathing | Y scale ±1.2%, X counter-scaled ±0.6%, 2.4 rad/s, random phase per character so a crowd never breathes in sync |
| Squash & stretch | Spring (k 180, damping 16) driven by changes in speed; clamped ±8% |
| Trailing parts | Hair (70%), hat and accessories (100%) trail opposite to velocity, up to 1.5 art px, on a spring (k 260, damping 20) |
| Teleport guard | Velocity spikes above 30 units/s (meeting regroup, spawn) are ignored |

Never rotate pixel sprites for secondary motion: rotation re-samples the grid and causes shimmering. Use offsets and gentle scale only.

---

## 7. UI kit

### 7.1 Button (Implemented, `PixelUiTheme.StyleButton` + `UIButtonJuice`)

Anatomy ("juxtaposition" style): a hard **pixel-art frame** (Chocolate `#223E3B`, sliced) around a clean, anti-aliased **inner face** inset 3 px (bottom 5 px, giving the lip). The face has a 1 px highlight outline (`#DCE7DC`, 80%). Label in the button text colour picked for contrast. Icons inside buttons: one colour, 2 px strokes, readable at 16×16, no gradients.

| State | Scale | Face | Content | Glow | Extra |
| --- | --- | --- | --- | --- | --- |
| Default | 1.00 | Face colour | Rest | 0 | |
| Hover / keyboard focus | 1.04, eased (rate 18/s) | Face colour | Rest | Inner glow fades to 22% (`#FFFFEB`) | Hover tick sound |
| Pressed | 1.00 | Desaturated 35% towards luminance | Face + label drop 3 canvas units (one pixel step at the UI's pixel scale) | Off | |
| Click | | | | | Click sound + 2-unit, 0.12 s decaying window jolt |
| Disabled | 1.00 | 65% grey, 60% alpha | Rest | Off | No sound |

Reduce Motion (Settings > Accessibility) keeps the colour and glow cues and removes the scale, drop and jolt.

### 7.2 Dialogue box (Implemented, meeting screen)

Cream card (`ApplyCard`) with a 2.5%-width **speaker tab** in the speaker's outfit colour on the left edge. Speaker name: sans bold 13. Line: sans regular 14. The newest five lines are stacked top to bottom; ghosts are labelled "(ghost)".

### 7.3 Health bar (Spec only: Crony has no health)

Frame 2 px pixel border; inner track `#263D45`; fill a flat colour with a 1 px lighter top highlight; damage shows a trailing "ghost" segment (white 60%) that drains 0.4 s after the hit; numbers in the sans to the right, never inside the bar.

### 7.4 Inventory slot (Spec only: Crony has no inventory)

48×48 canvas units, 2 px pixel frame, inner face Almond; item icon centred at 32×32 (point-filtered, integer scale); stack count bottom-right in sans bold 13 with a 1 px dark outline; hover uses the button hover state; selected adds a 2 px Gold inner border.

---

## 8. HUD typography rules (Implemented, `Assets/Scripts/UI/Typography.cs`)

| Use | Font | Rules |
| --- | --- | --- |
| Titles, tabs, short labels, names | **Silkscreen** (pixel, SIL OFL; `Assets/Resources/Fonts`) | Imported as Hinted Raster. Size = original × 1.3 (labels) or × 1.1 (titles ≥ 28), snapped to a multiple of 8 px, minimum 16. If it doesn't fit its box at a crisp size, try one step smaller, then fall back to the sans; never best-fit to a blurry in-between size |
| Reading text, chat, instructions, long buttons | Clean sans (Unity LegacyRuntime) | Regular for body, bold for emphasis |
| Anything with numbers (timers, counts, %, codes) | Clean sans | Digits read faster in the sans; the pixel font also lacks symbols like ✓ and · |

Avoiding fatigue: never set paragraphs in the pixel font; keep at most one pixel-font size per panel region; pixel text is uppercase-shaped and widely tracked, so give it more room than sans text of the same size.

## 9. Juice and screen-shake feedback

| Trigger | Feedback |
| --- | --- |
| Hover a control | 4% scale ease, inner glow, soft tick |
| Press | 3-unit drop, 35% desaturation |
| Click | Click sound, 2-unit decaying window jolt over 0.12 s (sin 120 Hz horizontal, cos 95 Hz vertical at half strength) |
| Mini-game action | Tap sound; win: rising two-note chime; miss: low buzz |
| Meeting ejection | Banner eases down 30 units with a cubic ease-out during the first quarter of the stage |
| World | Glows pulse and flicker on their own rhythms; avatars breathe and bob |

Rules: shake only the window that owns the control, never the game world for menu actions; keep amplitude ≤ 2 canvas units; always respect Reduce Motion.
