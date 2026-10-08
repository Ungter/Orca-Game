# Breakdown

How the hand-made visuals and systems in this project work, and the tricks behind them.
Each section names the files involved, so you can jump straight to the code.

- [1. Pixel font](#1-pixel-font) - `PixelFont.cs`
- [2. Pixel text on a Canvas](#2-pixel-text-on-a-canvas) - `PixelText.cs`
- [3. Inventory UI pixel art](#3-inventory-ui-pixel-art) - `InventoryPixelArt.cs`, `InventoryHUD.cs`
- [4. Pickup prompt](#4-pickup-prompt) - `PickupItem.cs`
- [5. Letter burn animation](#5-letter-burn-animation) - `LetterBurn.cs`, `ClueMinigame.cs`
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

Plays when the minigame is restarted (`E`), re-rolled (`R`) or lost (automatically, 1 s after
"OUT OF STEPS"). The letter burns away, the game resets while it's ash, and the letter
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

## 6. Night sky

**File:** `Assets/Materials/NightSkySkybox.shader`

Fully procedural skybox - no textures.

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
