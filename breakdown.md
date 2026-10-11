# Breakdown

How the hand-made visuals and systems in this project work, and the tricks behind them.
Each section names the files involved, so you can jump straight to the code.

- [1. Pixel font](#1-pixel-font) - `PixelFont.cs`
- [2. Pixel text on a Canvas](#2-pixel-text-on-a-canvas) - `PixelText.cs`
- [3. Inventory UI pixel art](#3-inventory-ui-pixel-art) - `InventoryPixelArt.cs`, `InventoryHUD.cs`
- [4. Pickup prompt](#4-pickup-prompt) - `PickupItem.cs`
- [5. Letter burn animation](#5-letter-burn-animation) - `LetterBurn.cs`, `ClueMinigame.cs`
- [5b. Minigame painted onto the letter](#5b-minigame-painted-onto-the-letter) - `ClueMinigame.cs`
- [5c. Cursive clue writing itself](#5c-cursive-clue-writing-itself) - `CursiveWriter.cs`
- [6. Night sky](#6-night-sky) - `Materials/NightSkySkybox.shader`
- [7. Lighting the sprite player](#7-lighting-the-sprite-player) - `SpriteLightTint.cs`
- [8. Fixing the map on import](#8-fixing-the-map-on-import) - `Editor/IslandLampLights.cs`
- [9. Fixing the FBX itself](#9-fixing-the-fbx-itself) - one-off scripts
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

**File:** `Assets/PickupItem.cs`

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
  how the lamp poles' shadows-only copies count.
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
- **Lamp poles - shadow from only part of a mesh:** Unity can only turn shadows on/off per
  renderer. So the pole's renderer stops casting, and a hidden child is added with a copy of
  the pole mesh **clipped** below a height (Sutherland-Hodgman clip of each triangle against
  a horizontal plane) with `ShadowCastingMode.ShadowsOnly`. The top 35% casts nothing (no
  disc of shade under the lamp); the rest still casts from every light. The clipped mesh is
  stored in the model with `context.AddObjectToAsset`.
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
