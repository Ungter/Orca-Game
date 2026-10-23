# Breakdown

How the hand-made visuals and systems in this project work, and the tricks behind them.
Each section names the files involved, so you can jump straight to the code.

- [1. Pixel font](#1-pixel-font) - `PixelFont.cs`
- [2. Pixel text on a Canvas](#2-pixel-text-on-a-canvas) - `PixelText.cs`
- [3. Inventory UI pixel art](#3-inventory-ui-pixel-art) - `InventoryPixelArt.cs`, `InventoryHUD.cs`
- [4. Pickup prompt](#4-pickup-prompt) - `PickupItem.cs`, `InteractPrompt.cs`
- [5. Letter burn animation](#5-letter-burn-animation) - `LetterBurn.cs`, `ClueMinigame.cs`
- [5b. Minigame painted onto the letter](#5b-minigame-painted-onto-the-letter) - `ClueMinigame.cs`
- [5c. Cursive clue writing itself](#5c-cursive-clue-writing-itself) - `CursiveWriter.cs`
- [6. Night sky](#6-night-sky) - `Materials/NightSkySkybox.shader`
- [7. Lighting the sprite player](#7-lighting-the-sprite-player) - `SpriteLightTint.cs`
- [8. Fixing the map on import](#8-fixing-the-map-on-import) - `Editor/IslandLampLights.cs`
- [9. Fixing the FBX itself](#9-fixing-the-fbx-itself) - one-off scripts
- [10. Title screen](#10-title-screen) - `TitleScreen.cs`, `PixelButton.cs`
- [11. Talking to the gravestone](#11-talking-to-the-gravestone) - `TalkInteractable.cs`, `InteractPrompt.cs`
- [12. Dig site skill checks](#12-dig-site-skill-checks) - `DigSite.cs`, `DigSparkles.cs`, `Interactables.cs`
- [Shared rules for crisp pixel art](#shared-rules-for-crisp-pixel-art)

---

## 1. Pixel font

**File:** `Assets/PixelFont.cs`

A bitmap font: every character is drawn by hand as a tiny grid, all characters are
packed into one texture, and text is drawn as one small textured rectangle per letter.

### Glyphs are strings in code

```csharp
{ 'A', new[] { ".#.", "#.#", "###", "#.#", "#.#" } },
{ 'g', new[] { "...", ".##", "#.#", "#.#", ".##", "..#", "##." } },
```

- One string per pixel row, top to bottom. `#` is ink, `.` is empty.
- **Height:** the grid is 7 rows. Capitals and digits use the top 5 (`CapHeight = 5`);
  descenders (g j p q y , ;) drop into rows 6-7. Shorter glyphs are padded with blank rows.
- **Width:** the string length is the glyph width, so the font is proportional
  (`i` is 1 wide, `M` is 5, most letters 3).
- **Spacing:** 1 pixel between letters (`Spacing`), 8 pixels between lines (`LineHeight`).
- Any missing character draws as `?`.

Why strings: no image file to manage, every pixel is visible and editable in a diff,
and adding a character is one line.

### Packing the atlas (`EnsureAtlas`)

On first use, all glyphs are written side by side into one texture 7 pixels tall:

1. Walk every glyph, writing a white, opaque pixel for each `#`.
2. Leave a 1-pixel gap between glyphs so neighbours never bleed into each other.
3. Remember each character's slice (`RectInt` x, width) in a lookup table.
4. **Flip rows** while writing: textures count rows from the bottom, the glyph strings
   are written top-down.
5. **Point filtering**, so scaling never blurs a pixel.
6. Ink is white so it can be tinted to any colour when drawn (`GUI.color` / vertex colour).
7. `HideAndDontSave` and `Apply(false, true)` - no mipmaps, CPU copy released.

### Drawing

- Move a pen left to right; for each character draw a rectangle the glyph's size using
  only its atlas slice (`GUI.DrawTextureWithTexCoords`), advance by width + 1.
- `\n` returns the pen to the line start and moves down `LineHeight`.
- **Shadow:** draw the same text 1 pixel down-right in the shadow colour first.
- **Two APIs:** `Draw(x, y, ...)` works in GUI units under whatever `GUI.matrix` is set
  (used inside the inventory grid); `Draw(Vector2 screenPos, text, pixelSize, ...)`
  sets up a whole-number scale matrix itself for plain screen-space use.
- Only draws on `EventType.Repaint`, so IMGUI's layout passes cost nothing.

### Layout helpers

- `Measure(text)` - widest line in font pixels (sum of widths + gaps, minus the trailing gap).
- `MeasureHeight(text)` - `(lines - 1) * LineHeight + Height`.
- `Wrap(text, maxWidth)` - greedy word wrap at spaces and existing newlines.
- `GetGlyph(c)` / `GetRows(c)` - atlas slice or raw rows, for code that draws glyphs itself
  (`PixelText`, `LetterBurn`).

---

## 2. Pixel text on a Canvas

**File:** `Assets/PixelText.cs`

A drop-in replacement for `UnityEngine.UI.Text` that draws `PixelFont` on any uGUI Canvas.

- **It's a `MaskableGraphic` that builds its own mesh.** `OnPopulateMesh` emits one quad
  (two triangles) per glyph, plus a second set for the shadow, all using `PixelFont.Atlas`
  as `mainTexture`. One draw call for the whole label.
- **Font size is cap height** in canvas units, not points.
- **Pixel-perfect size:** the size of one font pixel is
  `round(fontSize / CapHeight * canvas.scaleFactor) / canvas.scaleFactor` - always a whole
  number of *screen* pixels, so glyphs never get uneven columns.
- **Pixel-perfect position:** each line's origin goes through `Snap()`, which converts the
  point to the root canvas, rounds it to the screen-pixel grid, and converts back.
- **Rebuild on resize:** `Update` watches `canvas.scaleFactor` and dirties the mesh when it
  changes, because window resizes don't always trigger a graphic rebuild.
- **Key highlighting:** with `highlightBrackets` on, characters between `[` and `]` use
  `keyColor`, so `[ESC] CLOSE` gets a gold key and a plain label with one string.
- Alignment is read from `TextAnchor` (all nine positions), lines are centred/left/right
  per line.

---

## 3. Inventory UI pixel art

**Files:** `Assets/InventoryPixelArt.cs`, `Assets/InventoryHUD.cs`

### Art as palette-indexed text

The same idea as the font, but with colour: a palette maps characters to colours, and each
piece of art is an array of strings.

```csharp
{ 'K', new Color32(26, 15, 10, 255) },    // outline
{ 'W', new Color32(92, 58, 33, 255) },    // wood
{ 's', new Color32(168, 172, 180, 255) }, // iron highlight
{ '.', new Color32(0, 0, 0, 0) },         // transparent
```

```
".KKKKKKKKKKKKKKKKKK.",
"KssiKHHHHHHHHHHKssiK",   iron stud, light wood edge, iron stud
"KHLWKBbbbbbbbbbKLWDK",   wood side, dark well, wood side
```

- **Pieces:** `Slot` (wood frame, iron studs), `SlotFilled` (gold studs, warmer well),
  `Banner` (parchment scroll).
- **Shading is in the letters:** top/left edges use lighter wood (`H`, `L`), bottom/right use
  darker (`W`, `D`), so everything reads as lit from the top-left. Corners use
  three-tone studs (`s`/`i`/`I` or `y`/`g`/`G`) for a bevel.
- **Rounded look for free:** the frame's corners are `.` (transparent), which knocks off one
  pixel and softens the silhouette.
- **Typos are loud:** a character missing from the palette becomes bright magenta.
- `Build(rows, name)` turns a map into a point-filtered, sRGB texture (flipping rows), lazily,
  once.

### 9-slice

Each texture comes with a `RectOffset` border (e.g. `SlotBorder = (6, 5, 6, 5)`). Drawn
through a `GUIStyle` with that border, Unity keeps the corners exactly as drawn, repeats
the edges and stretches the centre. That's how one 20x20 slot drawing becomes a 32x26 slot,
and how the banner widens to fit any title without distorting its rolled ends.

### Drawing on an art-pixel grid

`InventoryHUD.OnGUI` sets

```csharp
GUI.matrix = Matrix4x4.TRS(panelTopLeft, Quaternion.identity, new Vector3(p, p, 1));
```

where `p` is a **whole number** (`pixelSize * UIScale()`, rounded; 3 at 720p). After that,
one GUI unit is one art pixel, so every layout number is in art pixels (slot 32x26, gap 2,
banner 14 tall) and every art pixel lands on an exact `p x p` block of screen pixels.
The panel origin is rounded to whole screen pixels first.

Per frame, in order: banner + title (`PixelFont`), slot frames, the item's own sprite fitted
into the well (`DrawTextureWithTexCoords` with the sprite's texture rect), gold slot number,
item name wrapped to two lines under the slot. Fading is just `GUI.color.a`.

---

## 4. Pickup prompt

**Files:** `Assets/PickupItem.cs`, drawing shared with talkable objects in `Assets/InteractPrompt.cs`

- **The drifting bug:** `WorldToScreenPoint` measures y from the *bottom* of the screen,
  IMGUI from the *top*. Mixing them made the prompt climb as you walked closer. Fix:
  `Screen.height - screenPos.y`.
- **Stable anchor:** the prompt projects a fixed point - the top of the sprite's bounds
  measured once at rest (`anchorHeight`) above `restPosition` - not the live transform, so
  it doesn't follow the bob.
- **Constant gap:** the offset above the item is in screen pixels (scaled by UI scale), so it
  looks the same at any distance.
- **Two runs, one string feel:** `[E]` (gold) and ` PICK UP` (parchment) are drawn as two
  `PixelFont.Draw` calls; the second is offset by `Measure("[E]") + Spacing` so they join
  exactly.
- **Fade:** `promptAlpha` moves toward 0/1 each frame and is shaped with smoothstep
  (`a*a*(3-2a)`).
- **Bob:** `restPosition + up * sin(t * speed * 2pi + randomPhase) * height`. The random phase
  keeps several items from bobbing in sync.

---

## 5. Letter burn animation

**Files:** `Assets/LetterBurn.cs`, `Assets/ClueMinigame.cs`

Plays when the minigame is restarted (`E`), re-rolled (`R`) or lost (immediately on the step
that runs out, with the fire starting at the board's centre and a flame headline). The letter burns away, the game resets while it's ash, and the letter
re-forms showing the new game.

### Flatten the UI into one bitmap (`Capture`)

The letter is many UI objects: paper sprite, board texture, 84 border tiles, swatches,
`PixelText` labels. To burn them as one sheet, they're flattened into a single `Color32[]`
at the paper sprite's own resolution (384x240):

- **Walk every active `Graphic`** under the surface in hierarchy order (parents before
  children = back to front), and composite each into the buffer:
  - `RawImage` / `Image` with a sprite - read the texture back (below) and blend it in.
  - `Image` without a sprite - fill its rect with its colour (the colour swatches).
  - `PixelText` - re-rasterise the text with `PixelFont.GetRows`, using the same layout maths
    as `PixelText` itself (cap-height sizing, alignment, bracket colours, shadow), at the
    bitmap's resolution.
- **Where things go:** `GetWorldCorners` -> surface local space -> bitmap pixels. Generic, so
  any new element added to the letter is captured automatically.
- **Reading any texture back without Read/Write:** `Graphics.Blit` the texture (with the
  sprite's UV rect as scale/offset) into a temporary sRGB `RenderTexture` at the target
  size, then `ReadPixels` + `GetPixels32`. This works on textures that aren't CPU-readable
  and downsamples them in the same step. Results are cached per capture by
  `(texture, uv, width, height)`, so the 84 border tiles cost only a handful of readbacks.

### Swap the real UI for the bitmap

- A `RawImage` overlay ("Burn") stretched over the surface shows a point-filtered texture of
  the buffer.
- The real children are hidden with a `CanvasGroup` at alpha 0 - **not** `SetActive(false)`.
  That matters: the game's own `Redraw()` toggles objects active/inactive, and the second
  capture reads `activeInHierarchy` to know what the new state shows. Alpha leaves all of
  that untouched.

### Timelines instead of simulation

Everything is driven by a per-cell **"when"** value in 0..1, computed once:

- **Cells:** the bitmap is animated in 2x2-pixel cells (`Cell = 2`) for a chunky, hand-made
  look while the letter art underneath stays full resolution.
- **Burn order:** distance to the nearer of two random ignition points near the bottom,
  plus noise, normalised to 0..1. The noise is what makes the flame front ragged instead of
  two perfect circles.
- **Rebuild order:** distance to the nearest edge (so edges re-form first and it closes in on
  the middle), plus a little noise.
- **Noise:** two octaves of smooth value noise from an integer hash - no textures, no
  `Random` state, cheap per cell.

Global progress runs 0 -> `1 + Band`. A cell's local age is
`(progress - when) / Band`, so every cell lives through the same short sequence, just
offset in time, and the whole sheet is guaranteed done at the end.

### Burn look (per cell, by age)

| age | look |
|---|---|
| slightly below 0 | paper pulled toward scorch brown (squared, so it browns late and fast) |
| 0 - 1 | fire ramp: pale yellow, orange, flame orange, red, ember, char |
| above 1 | transparent (burnt away) |

- A random **flicker** (+0-12% age) per cell per frame makes the flames shimmer.
- Cells only burn where the original had paper (alpha > 16), so transparent margins
  never catch.
- **Embers:** cells at age 0.25-0.6 have a small chance to spawn an ember: rises 18-45 px/s,
  jitters sideways, lives 0.5-1.3 s, cools down the fire ramp and fades. Drawn on the same
  2x2 grid. Capped at 500.

### Rebuild look

- **Ash specks:** ~380 specks, each assigned a random paper cell of the *new* letter.
  Each starts 15-55 px away, waits, then eases in (smoothstep) to land exactly when its cell
  re-forms (`arrive = when * duration / (1 + Band)`). Colour warms from ash grey toward the
  seam colour as it flies. Visually, ash gathers where the paper is about to appear.
- **Glowing seam:** a re-forming cell is the new letter tinted toward warm orange, fading as
  its age goes 0 -> 1, so a soft glow line sweeps inward.

### Losing: fixed ignition and the flame headline

- `Play(midpoint, origin, headline)`. With an `origin` (0..1 on the letter) the burn order
  is the distance from that single point plus noise, so the fire fans out from the board's
  centre; `E`/`R` pass none and get two random fires near the bottom.
- **Flame text** ("OUT OF STEPS"): built from `PixelFont` capitals, each font pixel a 2x2
  cell block, centred on the origin and drawn on top of the burn.
  - **Three frames:** for every column of every letter, each frame gets 0-2 flame-tongue
    pixels above the top ink pixel; frames loop at 9 fps. Colours run white-hot at the
    bottom to red at the tongues, shifted per pixel per frame by a hash so it flickers.
  - **Random order, growing in:** letters are shuffled and their start times spread over
    the burn (0.1-1.0 s). Each letter has a random seed point inside its box; a pixel
    appears when a growing radius (0.35 s, smoothstep) passes its distance from the seed
    plus a little hash jitter for a ragged front. Newly grown pixels flash white.
  - Dark 1-cell outline drawn in a first pass so the text reads over fire and paper.
  - The pause after burning is 1.2 s when there's a headline (time to read it); during the
    rebuild the text crumbles away pixel by pixel (hash threshold over 0.4 s).

### Sequence and integration

1. `Play(midpoint)` captures the current letter, hides the real UI, starts **burn** (1.1 s).
2. **Pause** (0.3 s): only embers.
3. Run `midpoint` - `ClueMinigame` resets or regenerates the board and calls `Redraw()`,
   which updates the board texture and labels while they're invisible.
4. Capture again (now the new game), start **rebuild** (1 s).
5. Hide the overlay, restore `CanvasGroup` alpha.

- Input is ignored while `IsPlaying`; time uses `unscaledDeltaTime` (clamped to 50 ms so a
  hitch doesn't skip the animation).
- Closing the letter mid-animation calls `Cancel()`, which still runs the midpoint so the
  reset is never lost.
- Cost: ~23k cells and 92k pixels rewritten per frame on the CPU, plus one texture upload.
  The two captures do a few GPU readbacks each - a small one-off stall, invisible behind
  the animation.

---

## 5b. Minigame painted onto the letter

**File:** `Assets/ClueMinigame.cs` (`Redraw` and the drawing helpers below it)

The board used to be a flat slate-blue panel with saturated rainbow squares, so it looked
pasted on. It's now painted into the same 770x770 pixel buffer as before, but styled as
pigment and pen ink on the letter's paper.

- **No background at all:** the buffer starts fully transparent, so the board's background
  *is* the letter's paper. `Blend` is a proper alpha-over that keeps the canvas's own
  transparency (shadows and ink stay translucent over the paper), and `Fill` goes through it.
- **Grid running out onto the paper** (`BuildPaperLines`): instead of a picture border, a
  second texture over the whole letter (4 texels per letter pixel, under the board)
  continues the grid past the play area. Per texel: distance to the play area -> squared
  fade over `PaperLineFade` tiles; anti-aliased distance to the nearest grid line; ink
  density noise along each line. Lines are masked to *plain paper*: the letter sprite is read
  back through the GPU, the paper colour is averaged under the board, and texels whose letter
  pixel differs from it (edges, decoration) get no ink. Built once per letter sprite.
- **Paper grain** (`Grain`): one static field built once - large soft blotches, finer
  mottling, horizontally stretched noise for fibres, rare dark flecks. Paint granulation,
  shadows and ink density all sample it, which ties the layers together.
- **Muted pigments:** vermilion, orange ochre, saffron, verdigris, lapis, murex purple,
  chalk. Desaturated enough to look like period paint, still clearly distinct for play.
- **Colour hints reuse the board painter** (`EnsureSwatches`): each colour is painted with
  `PaintTile` into the board buffer and copied out into its own small texture, so the
  "Next / then" stamps match the board tiles exactly. Built lazily at the start of
  `Redraw` (the buffer is repainted right after) and rebuilt if missing.
- **Surviving play-mode script reloads:** Unity restores private runtime arrays as *empty*
  arrays, not null. Runtime caches (`pixels`, `grain`, `swatchTextures`) are
  `[NonSerialized]` and every lazy build checks length/nulls; `EnsureCanvas` recreates the
  buffer and board texture if they went missing.
- **Painted tiles** (`PaintTile`): soft cast shadow down-right, *ragged edges* (pixels near
  the edge randomly skipped by a hash), granulation from the grain, light from the top-left
  (gradient across the tile plus bright upper/left rim and dark lower/right rim), and an
  uneven inked outline.
- **Pen grid** (`InkLine`): drawn *over* the paint. Each line drifts sideways by a pixel
  along slow noise (hand wobble) and its density follows grain + noise (ink running
  thin). Outer lines are thicker.
- **Player as a wax seal:** AA disc in the current colour with drop shadow, darker pressed
  rim, a stamped inner ring that's dark top-left / light bottom-right (reads as debossed),
  top-left shading and a small glint.
- **Static lighting** (`ApplyLighting`, last pass every redraw): brighter and warmer
  top-left falling off to the bottom-right and darker toward the edges. It only scales
  colour, never alpha, so empty paper stays untouched.
- **No end screen:** running out of steps draws the final position and burns straight
  away; "OUT OF STEPS" is part of the burn (see 5)..

---

## 5c. Cursive clue writing itself

**Files:** `Assets/CursiveWriter.cs`, `Assets/ClueMinigame.cs` (`EnsureClueWriter`)

When the puzzle is solved, "Fell, Jerk, Thief" is written onto the letter in cursive,
stroke by stroke in real writing order, with drops of ink flicking off the nib.

- **Glyphs are pen paths, not bitmaps.** Each glyph is a list of strokes (pen down ... pen
  up); each stroke is a list of *pieces*, each a Catmull-Rom spline through hand-placed
  points. A new piece starts a sharp corner (the bottom of an ascender loop in `k`/`h`)
  without lifting the pen. Units: baseline 0, x-height 1, ascenders ~1.95, descenders ~-0.85.
- **Joined words:** lowercase letters enter near (0, 0.18) and exit low on their right, so
  `JoinIn`/`JoinOut` letters are appended to one continuous stroke - a whole word is one
  pen movement. Capitals like F and T end their stroke; J joins on.
- **Correct stroke order:** i-dots are `Later` strokes, collected and written after the
  word, the way people actually dot their i's. F and T are bar, stem, (crossbar).
- **Broad-nib pen:** every ~0.35 texel along the path, a thin capsule is stamped along a
  fixed nib angle (35 deg). Moving along the nib gives hairlines, across it gives full
  width - the thick/thin contrast of calligraphy for free. Starts and ends taper
  (smoothstep over 1.5 nib widths) like pressure.
- **Ink buffer only grows:** coverage is max-blended into a byte buffer, so overlapping
  stamps never darken past solid ink; the frame is rebuilt from it each tick.
- **Animation:** constant pen speed chosen so the whole text takes ~4 s, a 0.14 s pause on
  each pen lift. Ink drops spawn at the tip (~220/s, 3-6 texel squares, some double size), shoot backwards/sideways from the
  motion, slow down, fall slightly and fade in 0.2-0.55 s; drawn as small square pixels.
- **Showing it:** a `RawImage` on the letter, sized from the layout's bounds. Writing plays
  on the winning move; reopening a solved letter (or closing mid-write) shows it finished
  (`Complete()`). The writer is `[NonSerialized]` and rebuilt on demand.
- **Adding letters:** only the glyphs this clue needs exist (F J T e f h i k l r ,). Add more
  in `BuildGlyphs()`; missing characters log a warning and leave a gap.

---

## 6. Night sky

**Files:** `Assets/Materials/NightSkySkybox.shader`, `Assets/Materials/NightSky.hlsl`

Fully procedural skybox - no textures. The stars and aurora live in `NightSky.hlsl`
(`NightSkyLights(dir, t)`) so the ocean can reuse them for its reflection (see below).

- **Gradient:** horizon -> zenith with `pow(up, 1 / sharpness)`, a darker colour below the
  horizon.
- **Stars without a texture:** the view direction is scaled into a 3D grid
  (`dir * density`); every cell hashes to "has a star or not", a jittered position, a
  brightness and a tint. Working in 3D around the viewer avoids the stretching you'd get
  at the poles with a 2D sky map.
- **Bright stars are rare:** brightness is `pow(random, 6)`, so most stars are faint and a
  few are bright - like a real sky.
- **No shimmer when turning:** star radius is clamped to at least ~1 screen pixel via
  `fwidth(p)`, with energy conserved (`size^2 / radius^2`), so stars stay steady instead of
  sparkling from aliasing.
- **Twinkle:** product of two sines at different speeds per star - irregular, never in sync.
- **Two star layers** at different densities add depth.
- **Aurora:** 16 samples up a thin vertical slab; each projects the view ray onto a plane
  at that height and reads ridged, domain-warped noise (`1 - |2n - 1|` raised to a high
  power gives thin curtains). Colour goes green at the bottom to violet at the top.
- **Fading in and out:** a slow large-scale noise mask makes patches come and go, times a
  "breathing" envelope (`_AuroraFadePeriod`) that never drops below `_AuroraMinVisibility`.
- Bright aurora slightly dims stars behind it, like real sky glow.

**Ocean reflecting the sky** (`Assets/Materials/OceanWater.shader`): the water evaluates
`NightSkyLights` along its reflected view ray, so the same stars and aurora show up mirrored
and moving on the waves. The normal used for that lookup blends the smooth wave normal with
the rippled one (`_SkyReflectionDistortion`), so the reflection wobbles instead of turning
into noise. It's scaled by fresnel (`_SkyReflection`) and uses 8 aurora samples instead of 16
since the waves blur it anyway. The ocean material carries its own copy of the star/aurora
settings - keep them equal to `NightSkySkybox.mat` so the reflected stars line up with the
real ones.

**Ocean waves that don't tile:** four user waves (A-D) plus four weaker "secondary" copies
turned by odd angles and stretched by irrational factors (0.618, 1.371, ...), so the sum never
lines up into a repeating square. Each wave is also faded by its own slowly drifting
large-scale noise (`_WaveVariation`, `_WaveVariationScale`), giving calm and rough patches
that wander. The fade only ever weakens a wave, so crests can't loop over. The ripple/foam
noise hash was swapped for a float-safe one - the old `frac(p * 123.34)` repeated every 50
cells.

---

## 7. Lighting the sprite player

**File:** `Assets/SpriteLightTint.cs`

The player sprite is unlit, so it can't react to light by itself. This component estimates
the light reaching it and tints the sprite.

- **Samples:** three points on the body (feet, middle, head) from the `CharacterController`.
- **Same falloff as URP:** point/spot lights use inverse-square times URP's smooth window
  `(1 - (d^2/r^2)^2)^2`, so the estimate matches what the 3D scene shows. Spots add the cone
  factor.
- **Shadows:** for lights that cast shadows, a ray from each sample to the light. Only
  colliders whose renderers actually cast shadows count (cached per collider), so lamp heads,
  invisible walls and triggers don't make fake shade. Children are checked too, which is
  how the lamp poles' shadows-only copies count. Like URP, a renderer only blocks a light
  when its rendering layers overlap the light's `renderingLayerMask` (URP keeps that equal to
  the light's shadow layers), so lamp lights ignore the poles and the moon doesn't.
- **Soft edges:** each light contributes the *fraction* of samples that see it, so stepping
  half into a shadow gives half the light.
- **Exposure curve:** `1 - exp(-energy * exposure)` maps any amount of light into a brightness
  between `minBrightness` and `maxBrightness` without hard clipping.
- **Colour:** the sprite takes on a fraction (`lightColorTint`) of the incoming light's hue.
- **Smoothing:** exponential ease (`1 - exp(-dt / smoothTime * 3)`), frame-rate independent.
- Light list refreshes once a second instead of every frame.

---

## 8. Fixing the map on import

**File:** `Assets/Editor/IslandLampLights.cs` (an `AssetPostprocessor` on `IslanddemoV3.fbx`)

Changes made at import time live inside the imported model, so they survive re-exports from
Maya and follow the map into any scene.

- **Version bump to reimport:** `GetVersion()` is part of the import's identity; raising it
  makes Unity reimport the model with the new rules. Every change below bumped it.
- **`DependsOnSourceAsset(FoliageMatte.mat)`:** editing the material reimports the map, so
  its colour carries into all generated shades.
- **Lamp lights:** a `LampGlow` point light is added inside every `lampN/pCube1`, placed at
  the mesh's bounds centre. `LampGlow` owns the Light's settings (intensity 300, range 70,
  soft shadows) and turns off shadows on the lamp head, otherwise the head would block its
  own light.
- **Glowing lamp heads:** the light sits inside the head, so the head's outer faces never
  catch it and stayed dark. Each head's own material slots (the panels; slots sharing a
  material with the pole/caps are the frame and stay) get `Materials/LampHeadGlow.mat`, a
  URP Lit material with HDR emission (warm, ~10x in linear) that the scene's bloom
  (threshold 1) turns into a halo. It's a project asset assigned directly, so tune the glow
  there with no reimport. The hand-placed `lamp5 (1)` copy in `SampleScene` points at it too.
- **Lamp poles - no shadow from their own lamp:** the light sits just above the pole's top,
  so the pole threw a disc of shade round its foot. Unity can't turn one caster off for one
  light, but URP filters shadow casters per light by **rendering layer**. So the pole's
  renderer stops casting and a hidden `ShadowsOnly` child with the same mesh casts instead,
  on rendering layer 8, "Lamp Pole Shadows" (`LampGlow.PoleShadowLayer`). `LampGlow` turns on
  Custom Shadow Layers for every lamp light with that layer left out; the scene's
  Directional Light (moon) has Custom Shadow Layers = Default + Lamp Pole Shadows, so poles
  still cast full-length moon shadows. The visible pole stays on Default so every light
  lights it. A light added later that should get pole shadows needs that layer in its shadow
  layers too.
- **Foliage materials:** every `pSphere` in `bigtrees`, `mediumtrees`, `shrubs`, `hedges`
  gets a leaf shade; every `pCylinder` in the tree groups gets a bark shade.
  - **Shades:** 8 copies of the matte template per colour, each shifted in HSV (hue,
    saturation, value jitter). A few shared materials instead of one per object keeps
    batching working.
  - **Stable choice:** FNV-1a hash of the name picks the shade - stable across sessions,
    unlike `string.GetHashCode`. Trees hash the tree's name (one tone per tree); shrubs and
    hedges hash name + index of each sphere (speckled bushes), because sphere names repeat.
- **Inverted normals (sidewalk not showing shadows):** the sidewalk slab
  (`IslandDemo:pCube2`) came out of Maya with every normal facing opposite its triangle
  winding. It still drew (culling uses winding), but lighting uses normals, so lights saw its
  back: only ambient lit it and shadows had nothing to darken. `FixInvertedNormals` does an
  area-weighted vote per mesh (triangle `Cross(b-a, c-a)` vs. its vertex normals) and flips
  normals + tangents when disagreement outweighs agreement 3:1. Only that slab trips it.
- **Gravel sidewalk:** the slab gets a matte copy of `Assets/Materials/SidewalkGravel.mat`
  (URP Lit, smoothness 0, specular highlights and environment reflections off - the Arnold
  import was glossy) with a hand-drawn 32x32 gravel tile from
  `Assets/Editor/SidewalkGravelArt.cs` as its base map.
  - **Bitmap as text:** same idea as `InventoryPixelArt` - one string per pixel row, one
    palette letter per pixel. Styled on Minecraft gravel: grey stones packed edge to edge
    (mid, pale, dark and a rare faintly warm grey), each lit from the top-left (light rim
    top/left, dark rim bottom/right) with dark gaps where stones meet. The tile wraps on both
    axes. Point filtering plus mipmaps: crisp up close, no crawling far away.
  - **UVs in metres:** the importer box-projects new UVs per vertex (tops use X/Z, walls the
    vertical plane they face), in model units x `MapSceneScale` (209.3, the map's scale in the
    scene). So the template's tiling is tiles per metre: 0.5 = one tile per 2 m, 16 px/m,
    about a 30 px tall player.
  - **Reimport on edit:** `GetVersion()` mixes in `SidewalkGravelArt.ContentHash()`, so
    editing the art reimports the map; `DependsOnSourceAsset` does the same for the material.
  - **Depth without geometry:** a height field is derived from the same bitmap - each
    stone is a dome rising with distance from the nearest gap pixel (pale stones a bit proud,
    dark ones sunk, gaps lowest). From it the importer builds a tangent-space normal map
    (central differences, `GravelNormalStrength`) and a height map for URP's parallax
    (`_Parallax` on the template). Both point-filtered, so the bumps stay pixel-crisp; tangents
    are recalculated after the new UVs so the normal map lines up.
- **Gravel spilling over the edges** (`Assets/Editor/SidewalkSpill.cs`): instead of the slab's
  90 degree walls, a ragged gravel slope runs from every top edge down to the ground.
  - **Finding the edges:** weld the slab's vertices by position, keep upward-facing
    triangles, and the edges only one of them uses are the outline. Outward is the side away
    from the triangle's third corner; corners use the averaged direction of both edges, so
    neighbouring strips meet without gaps.
  - **Finding the ground:** the ground block's upward triangles (`IslandDemo:pCube1`) are
    queried directly - barycentric point-in-triangle in X/Z, highest surface below the edge.
    No physics needed at import time.
  - **Shape:** samples every 0.5 m. Width = 0.35 m + 1.2 x drop (roughly gravel's angle of
    repose), +-45% value noise so the foot is ragged, capped at 2.5 m; a bend row at 45% gives
    a slightly convex heap with a little bump noise. Drops over 3 m are skipped.
  - **Loose heaps:** small seven-sided cones scattered just past the foot, positions and sizes
    from a position hash so reimports give the same result.
  - Same metre UVs as the top, so pebbles carry on over the edge. Built as a `Sidewalk Spill`
    object in the model with a `MeshCollider`, so the player walks up the slope.
- **Fountain water** (`IslandDemo:pCylinder3`, a single mesh: basin, tapered column, spout
  pipe): measured from its own surface, the same barycentric query as the spill. Walking in
  from the outer edge, the top stays at rim height across the rim, drops at the inner wall
  (lowest point inside = floor), and climbs again at the column. Water line = 75% of the way
  from floor to rim. The column sits a little off the basin's centre, so the column radius
  is the widest of eight directions and the spout tip is the average of the pipe's top
  vertices. A `Fountain Water` object with `FountainWater` goes on the tip; see section 8b.

---

## 8b. Fountain water

**Files:** `Assets/FountainWater.cs` (spray, pool), `Assets/FountainWaterArt.cs` (the art),
`Assets/Materials/FountainSpray.mat` and `FountainPool.mat` (templates)

- **A spray of single droplets, not streams:** a firework-like burst of ~3500 pixel-art
  droplets a second (`dropletsPerSecond`, ~10k in the air), as a world-space particle
  system. Every droplet is a real particle with a depth-tested, alpha-clipped material, so
  the column hides the ones behind it from every side. (The first version drew streams on
  camera-facing sheets; a flat sheet can't go round the column, so they cut through it.)
- **Firework shape from drag:** linear drag (1.8/s) makes droplets burst out fast, slow
  into a dome over the spout (tops out ~3.5 m above it), then drift down at `g / drag`
  (~5.5 m/s) in a curtain round the column. The long hang time is what keeps so many in
  the air.
- **Drag in a particle job, not the Limit Velocity module:** Unity's built-in drag (with
  "multiply by velocity" off) takes a fixed amount off every particle's speed, whatever
  its speed, so fast droplets carried on 9-21 m and the spray covered the whole plaza. With
  "multiply by velocity" on it's quadratic instead. Neither matches the launch maths, so
  `OnParticleUpdateJobScheduled` schedules an `IJobParticleSystemParallelFor` that scales
  every velocity by `e^(-drag * dt)` after each step. Simulated with Unity's step order, landing
  is within 0.07 m of the aim at 60 fps and 0.15 m at 30 fps. The callback only reaches
  components on the particle system's own object, so the spray lives on `Fountain Water`.
- **Aimed to land in the basin:** droplets are emitted from `Update` with chosen velocities
  (`Emit` with `EmitParams`). Each picks an upward speed (-2 to 13 m/s) and where to come
  down (column + 0.25 m to wall - 0.4 m, packed towards the column so it's covered). With
  linear drag, height is `y(t) = (v + g/k)(1 - e^-kt)/k - g t/k`: Newton's method gives the
  flight time to the water, and sideways distance levels off at `speed / k`, so
  `speed = k * landing / (1 - e^-kT)`. Sideways travel happens early and the fall is near
  vertical, so a droplet that lands clear of the column's foot clears its narrower top too.
- **Pixel art:** 7x7 hand-drawn cells at 16 px/m. Droplets: 4 variants (rows, picked at
  random) x 4 stages over the flight (sparkle bursting from the spout, round drop, small drop,
  falling streak). A plane at the water line kills droplets and 15% spawn a splash (4 frames,
  drawn in the top half of the cell, since it's centred on the landing point and the bottom
  half is under the water). Sheets have no mipmaps: neighbouring cells would bleed and
  alpha-clipped droplets would fade out in the distance.
- **Lit like the world:** both materials are URP Lit, so the plaza lamps light the droplets
  and the pool. The particle system is scaled `Shape` only, so sizes and speeds stay in metres
  under the map's 209x scale.
- **Pool:** a flat ring at the water line, from well inside the column to just inside the
  wall, with a texture laid flat over the basin at 16 px/m (UVs map the basin's square to
  0-1). Deeper colour against the walls, broken ripple rings running out from where most
  droplets land, churned water and foam clumps there, and blinking glints on a jittered
  grid. 12 frames at 10 fps swapped through a `MaterialPropertyBlock`; every cycle divides 12
  so the loop has no seam.
- Built in `Start`, so it only shows in Play mode.

---

## 9. Fixing the FBX itself

One-off Python scripts (not in the repo) read and rewrote the binary FBX directly.

- **Reading binary FBX:** a node is `end offset, property count, property length, name`,
  then typed properties; arrays (`f d l i b`) may be zlib-compressed. Writing back recomputes
  every end offset and keeps the footer's padding rules. Verified by round-tripping the
  file byte-for-byte before changing anything.
- **Walk-through heights:** the map was baked in Unity's scene units (scale 346, offset y)
  and probed with a height-field: rays down at a grid of points, flood-filled from spawn
  using the `CharacterController` limits (0.3 m step, 45 deg slope, 1.82 m height). That
  found the ledges and fences that trapped the player.
- **Sloping a slab's edges:** the buried bottom corners were pushed outward along the side
  faces' normals (least-squares for corners shared by two faces) so walls became ~34 deg
  ramps.
- **Inside-out meshes:** Unity's mesh colliders only block from the front, and
  `CharacterController` ignores back faces. Two checks:
  - *winding vs stored normals* - catches meshes whose triangles disagree with their own
    normals;
  - *signed volume of closed meshes* - catches meshes where both are flipped consistently
    (the one that actually mattered here).
  The fix reverses each polygon's corner order and permutes every per-corner layer
  (normals, UV indices) to match, negates normals, and rebuilds the edge list.
- Originals are kept in `Backups/`.

---

## 10. Title screen

**Files:** `Assets/TitleScreen.cs`, `Assets/PixelButton.cs`, art maps in `Assets/InventoryPixelArt.cs`,
orca sheet in `Assets/Resources/TitleScreen/Orca Sprite.png`

Shown once when play starts; no extra scene. PLAY flies the camera down to the player.

### Bootstrapping into the gameplay scene

- `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` creates it in any scene with a `CameraFollow`,
  the same trick the pause menu uses - nothing to place by hand.
- **Freezing gameplay:** it disables every enabled `CameraFollow`, `PlayerController`, `InventoryHUD`,
  `InventoryInspection` and `PickupItem`, remembers them, and re-enables exactly those afterwards.
  `TitleScreen.IsOpen` stops Esc from opening the pause menu.

### Camera

- **Framing:** bounds of every renderer under `IslanddemoV3` except the `ocean` child. Distance is
  `radius / tan(fov / 2)`, so the land fills the view whatever the map's scale.
- **Orbit:** `Quaternion.Euler(pitch, yaw, 0)`; position is the island centre pushed back along that
  rotation. Pitch 35, yaw creeps 3 deg/s, starting from the gameplay camera's heading.
- **Fly-in:** PLAY re-enables `CameraFollow` first. `TitleScreen` runs at execution order 100, so in
  `LateUpdate` the follow camera has already placed itself; the title lerps/slerps from the frozen title
  pose to that live pose with a smoothstep. The target keeps moving correctly because it is re-read
  every frame. Player control and the HUD come back when the flight lands.

### Layout and art

- Same rules as the pause menu: IMGUI under a `GUI.matrix` of whole-number scale, one GUI unit = one
  art pixel, positions in art pixels from the top-centre of the screen.
- **Title:** the pause menu's parchment banner and `PixelFont`, drawn under a second whole-number
  scale (up to 3x) so it stays crisp. The scale is the largest that fits 80% of the screen width.
- **Block centring:** orca + scroll + divider + button + hint are centred as one block; the scroll lands
  mid-screen.
- **PLAY / EXIT buttons:** `PixelButton`, shared with the pause menu's KEYBINDS / HOME / BACK buttons. EXIT quits.
- **Selection:** both menus keep a selected button (arrows/W/S or a moving mouse change it; Enter presses it).
  `PixelButton.DrawSelection` flanks it with gold `>` `<` glyphs whose gap steps 0,0,1,2,3,3,2,1 px every
  0.12 s - whole-pixel moves, no sub-pixel easing. No on-screen key hints.
- **Back to the title:** the pause menu's HOME reloads the active scene. `TitleScreen` subscribes to
  `SceneManager.sceneLoaded` (registered `BeforeSceneLoad`), so it opens on every load, not just the first.
- **Orca:** the sheet is two 64x64 frames (each texel block is 4x4, so one frame = 64 art pixels). Drawn
  1:1 in art pixels with `DrawTextureWithTexCoords`, alternating halves at 3 fps, bobbing one pixel.
  Drawn before the scroll so the tail tucks behind it. Imported with point filter, no mips, no compression.
- **Hand-made pieces** (string maps in `InventoryPixelArt`, same palette plus four fire colours R/O/Y/w):
  - `Corner` - gilded corner bracket, drawn once and mirrored into all four corners by negative UV width/height.
  - `ChainLink` - tiles vertically from the screen top down behind the scroll's rolled ends.
    The scroll (and the orca with it) sways +-2 art px every 5 s on a half-sine, half-triangle wave,
    snapped to whole screen pixels rather than art pixels: art-pixel snapping left only five positions
    and the sine parked at each end for over a second. Texels stay p x p, just shifted. Each link is
    offset by `sway * depth`, depth 0 at the screen top to 1 at the scroll, so the chains lean like a
    pendulum. Translation only - rotating would smear the pixels.
  - `Torch` + three `TorchFlame` frames - wall torches either side, 8 fps flicker, offset per torch.
  - Torch glow - a procedural 25x25 texture of three stepped alpha rings (no soft gradient), alpha
    flickered with Perlin noise. Embers are single art pixels rising and cooling yellow -> orange -> red.
  - `Gem` on a gold rule between scroll and button.
- **Shade:** a light dim plus four stepped dark bands along the top and bottom edges (a pixel vignette).
  A black veil fades out over the first 1.2 s to hide the scene popping in.

---

## 11. Talking to the gravestone

**Files:** `Assets/TalkInteractable.cs` (on `graveStone1` in the scene), `Assets/InteractPrompt.cs`,
bubble/tail/Enter-key maps in `Assets/InventoryPixelArt.cs`, portrait `Assets/Resources/ShellGame/panther.png`

Walk up to the gravestone, press E, and the panther slides into the bottom-right corner and says
"Something is in the forest". Enter or a click sends it away.

### Prompt and who answers E

- `[E] TALK` uses the same `InteractPrompt.Draw` as pickups (same radius 2.5, gap, fade).
- `graveStone1` is a group whose meshes live on children, so the anchor is the top-centre of the
  combined child renderer bounds, measured once in `Awake`.
- Pickups, talkables and dig sites all register with `Interactables`; only the one nearest the
  player shows its prompt and answers E, so one press never does two things.

### Conversation phases

`Entering` (0.45 s ease-out slide) -> `Popping` (bubble fades in and rises 3 px) -> `Typing`
(40 chars/s) -> `Waiting` (Enter keycap hint fades in and bobs) -> `Leaving` (0.4 s ease-in slide out
while the bubble fades in place) -> `Idle`.

- Presses are ignored while the panther enters. While typing, the first press finishes the line; the
  next one closes it.
- `TalkInteractable.IsTalking` is true from the press until the panther is gone. It feeds
  `Interactables.Busy`: `PlayerController` stands still, `PickupItem` and `InventoryInspection` ignore
  input while it is set.
- Runs at execution order -50, before the player and pickups, so the opening E press moves nothing.
- Pausing freezes it; the click that resumes is swallowed so it cannot dismiss the bubble.
- `Tick(deltaTime, advance)` holds all the logic, so `Editor/TalkInteractableTests.cs` drives it
  without a scene.

### Layout and art

- IMGUI under a whole-number `GUI.matrix` scale (3 at 720p): one GUI unit = one art pixel.
- The panther sheet is 256x512: two square frames stacked, drawn 4 texture px per art pixel at
  64x64 art px. The frames alternate every 0.5 s as an idle. It is mirrored (negative UV width) so it
  faces into the screen.
- The bubble is a 9-sliced 12x12 parchment map (3 px borders, bright top lip). A separate 8x6 tail
  overlaps the bubble's bottom outline by one row, which opens the outline, and its tip lands on
  `speechPoint` (the panther's head). A 35% black copy offset by 1 px is the drop shadow.
- The Enter hint is a 13x9 wooden keycap with a gold return arrow, bottom-right inside the bubble.

---

## 12. Dig site skill checks

**Files:** `Assets/DigSite.cs` (on `digSiteMinigame2`, reward `ClueLetter2`), `Assets/Interactables.cs`,
bar frame map `SkillBar` in `Assets/InventoryPixelArt.cs`, burying in `Assets/PickupItem.cs`

Walk up to the dig site (radius 1.75, 30% under the other interactables), press E, and hit three
Dead by Daylight style skill checks with Space. Clearing them raises `ClueLetter2` out of the ground.

### Checks

- A needle rests at the left of the bar for 0.35 s, then crosses it in 1.3 s. The gold zone is open
  0.5 s, then 0.3 s, then 0.2 s (`windows`), so its width is `window / 1.3` of the track.
- Timing is judged in needle seconds, not pixels: a press counts when
  `ZoneStart <= needleTime <= ZoneStart + window`. The zone opens somewhere between 30% of the sweep
  and 0.08 s before the end.
- Space early, Space late or letting the needle run past the zone all fail.
- Phases: `Appearing` -> `Sweep` -> `Hit` (zone flashes, pip turns gold) -> next `Sweep` ...
  -> `Complete` -> `Dug`. A fail goes `Failed` (red tint, 1 px shake) -> `Shatter` -> `Idle`, and the
  next dig starts from the first check.
- Space is also jump. While `Interactables.Busy`, `PlayerController` sets `jumpLocked`, and only a release
  clears it, so the press that ends a check never jumps.
- `DigSite.IsDigging` (part of `Interactables.Busy`) locks the player from the E press until a check
  succeeds or fails. The player is free while the shards fall, but the site only shows its prompt
  again once they are gone.

### Shattering

- On a fail the bar is cut into columns 9-16 px wide, each split at a random height into a top and a
  bottom shard, so the break is jagged.
- Each shard flies outward from the bar's middle (top shards jump higher) and falls under gravity.
  Positions come from `v*t + g*t*t/2` and are rounded to whole art pixels; alpha fades in quarter steps.
- A shard is drawn by clipping the whole bar with `GUI.BeginGroup(shardRect)` and drawing the bar offset
  by the shard's source position. No textures are cut up.

### The reward

- `DigSite.Start` calls `reward.Bury()` (after every `Awake`, so the letter has recorded its rest
  position): its renderers and glow are switched off and it is not interactable. Place the letter in the
  scene where it should rise.
- `Unearth` drops it below its resting bottom edge and eases it up over 1.2 s with a small shudder; the
  ground hides the part still underground. Its glow brightens with it. The bob phase is reset so the
  bob starts from zero. After that it is a normal pickup with its own `[E] PICK UP` prompt.

### Sparkles after the gravestone

**File:** `Assets/DigSparkles.cs`

- `TalkInteractable.HasSpoken` turns true when a conversation finishes (the panther has left). The dig
  site's `revealedBy` points at the gravestone; left empty, `Start` uses the scene's only talkable object.
  Until then the site looks as it always did.
- Seven glints, each a `SpriteRenderer` with a 1x1 dot, a 3x3 plus or a 5x5 star built from
  `InventoryPixelArt.Build` (32 px per unit). Each one plays dot -> plus -> star -> star -> plus -> dot
  over about a second while it rises 0.35 units, waits a random moment, then reappears at a random
  spot within half the dig radius. They are turned to the camera's rotation every frame.
- One shadowless point light (intensity 1.5, range 3) breathes slowly with a little shimmer, so the
  ground around the site picks up the glow.
- Everything fades in over 1.2 s and fades out the same way once the last check is cleared.
- The sparkles live on their own root object, not under the site, whose uneven scale would stretch them.

---

## 13. Debug fly / noclip

**File:** `Assets/PlayerController.cs` (`SetDebugFly`, `UpdateDebugFly`)

- The backquote key toggles it. Toggling is skipped while paused, while the inventory is open,
  or while `Interactables.Busy` is set.
- On: the `CharacterController` is disabled, so nothing collides, and the transform moves
  directly at 5x `moveSpeed`. WASD moves relative to the camera, Shift flies up and Ctrl flies down.
- Off: the controller comes back with zero vertical velocity, so normal gravity drops the player
  to the ground. `jumpLocked` is set, so Space has to be released before the player can jump.

---

## Shared rules for crisp pixel art

These apply to the font, inventory, prompt and burn:

1. **Point filtering, no mipmaps** on anything that should look pixelated.
2. **Whole-number scale only.** One art pixel = an exact `n x n` block of screen pixels;
   non-integer scales make some columns wider than others.
3. **Snap origins to screen pixels** (round the position after scaling).
4. **sRGB textures** (`GraphicsFormat.R8G8B8A8_SRGB`) so hand-picked colours look as typed
   in a Linear-colour-space project.
5. **Draw in art-pixel units** (`GUI.matrix` scale, or cap-height font sizes) so layout
   numbers match the art.
6. **Shade with the palette, not effects:** light top-left edges, dark bottom-right edges,
   transparent corners, a 1-pixel hard drop shadow.
7. **Generate textures at runtime from data** (`HideAndDontSave`, `Apply(false, true)`):
   art lives in code, diffs show exactly what changed, nothing to import.
