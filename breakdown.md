# Breakdown

How the hand-made visuals and systems in this project work, and the tricks behind them.
Each section names the files involved, so you can jump straight to the code.

- [1. Pixel font](#1-pixel-font) - `PixelFont.cs`
- [2. Pixel text on a Canvas](#2-pixel-text-on-a-canvas) - `PixelText.cs`
- [3. Inventory UI pixel art](#3-inventory-ui-pixel-art) - `InventoryPixelArt.cs`, `InventoryHUD.cs`
- [4. Pickup prompt](#4-pickup-prompt) - `PickupItem.cs`, `InteractPrompt.cs`
- [5. Letter burn animation](#5-letter-burn-animation) - `LetterBurn.cs`, `ClueMinigame.cs`
- [5b. The potion hunt on the island](#5b-the-potion-hunt-on-the-island) - `ClueMinigame.cs`, `ClueMap.cs`, `ClueMapArt.cs`
- [5c. Cursive clue writing itself](#5c-cursive-clue-writing-itself) - `CursiveWriter.cs`
- [6. Night sky](#6-night-sky) - `Materials/NightSkySkybox.shader`
- [7. Lighting the sprite player](#7-lighting-the-sprite-player) - `SpriteLightTint.cs`
- [8. Fixing the map on import](#8-fixing-the-map-on-import) - `Editor/IslandLampLights.cs`
- [8c. Glowing windows](#8c-glowing-windows) - `WindowLamp.cs`, `WindowGlow.cs`, `Materials/WindowLightRays.shader`
- [9. Fixing the FBX itself](#9-fixing-the-fbx-itself) - one-off scripts
- [10. Title screen](#10-title-screen) - `TitleScreen.cs`, `PixelButton.cs`, `OpeningVoyage.cs`
- [11. Talking to the gravestone](#11-talking-to-the-gravestone) - `TalkInteractable.cs`, `InteractPrompt.cs`
- [12. Dig site skill checks](#12-dig-site-skill-checks) - `DigSite.cs`, `DigSparkles.cs`, `Interactables.cs`
- [15. Dolphin statue letter puzzle](#15-dolphin-statue-letter-puzzle) - `DolphinWordPuzzle.cs`
- [16. The magician's lantern](#16-the-magicians-lantern) - `PlayerLantern.cs`, `MagicianEncounter.cs`
- [17. The alchemist's letter](#17-the-alchemists-letter) - `AlchemistEncounter.cs`
- [18. Al Cachud comes out of his door](#18-al-cachud-comes-out-of-his-door) - `CachudCallout.cs`, `CachudDoorway.cs`
- [19. Door beacons](#19-door-beacons) - `DoorBeacon.cs`
- [20. Night mood: hard light for a murder mystery](#20-night-mood-hard-light-for-a-murder-mystery) - `LampGlow.cs`, `SampleSceneProfile.asset`
- [21. Island terrain, rock shore and foliage](#21-island-terrain-rock-shore-and-foliage) - `Editor/IslandTerrain.cs`, `Editor/IslandScenery.cs`
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

Plays on the held clue letter when the potion hunt runs out of steps or is restarted with `R`
(two random fires near the bottom, no headline; see 5b). The letter burns away, the hunt is
cleared while it's ash, and the letter re-forms showing the cleared map.

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
3. Run `midpoint` - `ClueMinigame` clears the hunt (potions, marks, trail) and, for `R`,
   starts a new one where the player stands, while the letter is invisible.
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

## 5b. The potion hunt on the island

**Files:** `Assets/ClueMinigame.cs` (the hunt and the held letter), `Assets/ClueMap.cs`
(survey, routes, potion placement), `Assets/ClueMapArt.cs` (map art, potions, meshes),
`Assets/Resources/ClueMap/ClueGridLight.shader` (light walls and beams)

The clue letter used to open a 20x20 colour maze in the inspection view. Now the board is the
island: the letter holds a map of it, and the hunt is walked.

- **Holding the letter:** its inventory number (or a click on its slot, which gets the gold
  rim like the lantern) toggles `IsHeld`; `InventoryInspection` never opens it. The letter
  (`Letter_screen.png` with the map on it) covers about a fifth of the screen in the
  bottom-right corner and slides up from below it (ease-out cubic, 0.35 s). It ducks away
  while a conversation, the inspection view, the title or the pause menu is up, and comes back.
- **Surveying the island** (`ClueMap`): the ground block `IslandDemo:pCube1` is cut into
  `CellsAcross` (24) cells along its longer side, and each cell into 8 x 8 spots. Per spot: is
  there ground under it (`Collider.Raycast` on the ground only) - if not it's sea, or a pier
  when something flat sits just above sea level; what a ray from the sky meets first (trees,
  buildings and the sea by their names/parents, low wide surfaces are paths and slabs, the
  rest is "structure"); the height the player stands at; whether the player's capsule fits
  there; and whether the sky is open above it. Then, per pair of neighbouring spots, whether
  they're walkable between: height difference under 1 m and nothing hit by rays at 0.5 m and
  1.3 m (with `queriesHitBackfaces` on, so single-sided walls block too). The player's own
  collider is ignored. It runs 1500 spots a frame after the title screen closes (by then the
  opening's boat is moored and solid), about 30 frames in all.
- **Steps are cells:** a 0-1 breadth-first search over the spots, free inside a cell and one
  step across a border, gives the fewest cells crossed between any two spots, walking round
  fences and houses.
- **Placing potions** (`PlanHunt`): candidates are spots reachable from the player, open to
  the sky, with a clear 2.2 x 1.7 x 2.2 m box above them and all eight neighbours walkable -
  so nothing spawns inside, under or against a structure. Five are picked farthest-first: each
  scores its distance (in cells) to the start and the potions so far, and one is drawn at
  random from those scoring at least 65% of the best, in a cell of its own and at least 4 cells
  from the rest (relaxed if the island runs out of room). They spread over the whole island.
- **Budget:** the shortest route from the start through all five in the best order (all 120
  orders tried), plus 25% (at least 4). Walking isn't stepping, so it's roomier than the paper
  board's 10%.
- **Playing:** `R` with the letter up starts a hunt where the player stands (refused off
  the ground, on the pier or in the sea). A cell only counts as left 0.6 m past its border, so
  walking along a line doesn't eat steps; crossing with no steps left burns the letter (see 5,
  no "OUT OF STEPS" headline) and the hunt is cleared, to start again anywhere. `R` mid-hunt
  burns and starts a new one. A potion is collected by walking within 1.3 m of it. The player
  walks 60% faster while a hunt is on (`PlayerController.SpeedMultiplier`).
- **On the island:** each potion is a billboard sprite of the flask art with a point light in
  its colour and a 9 m beam (two crossed additive cards). Light walls stand on every cell
  border over walkable land: one mesh of quads following the surveyed ground, brightest at
  the foot and fading upwards, faint (intensity 0.23). Only the borders round the player
  light: the shader works out from each fragment's position (and UV1, which way its border
  runs) which two cells it parts, and glows 100% on the four sides of the player's cell, 40% on
  the rest of the 3 x 3 cells round it, nothing beyond; a move to another cell cross-fades over
  0.25 s. They fade in with a hunt and out when it ends.
- **The map** (`ClueMapArt.DrawMap`, drawn once from the survey): up is world +x and right is
  world -z, as the island looks from above in the editor (pier at the bottom). Washes of sea
  (with wave dashes), grass, paths, the pier's planks, tree canopies, shingled roofs; ink on
  the coast and around houses and trees; the cells as a checkerboard with pen lines; the
  fountain and every lamp post as landmarks; an inked frame. Washes are slightly see-through
  so the paper shows. On top: potions and crosses for collected ones, a ring at the start, a
  crayon trail (rough dabs every half letter pixel, gaps where the paper's tooth misses) and a
  blinking "you are here" ring. Status line: steps left and potions found.
- **Finishing:** with all five, the letter comes up with a pulsing glow over `building3`.
  Talking to the alchemist then (`AlchemistEncounter`) plays placeholder lines and calls
  `Deliver()`: "Fell" is revealed to the word bank and, once the room closes, the letter comes
  up and writes it in cursive on a cleared patch (see 5c). Before that the door just says to
  keep looking.
- **Script reloads:** the survey, potions and pixel buffers are `[NonSerialized]` and
  rebuilt; a reload in Play mode surveys again.
- **Other NPCs wait** (`ClueMinigame.HuntUnderway`, `Assets/BusyNotice.cs`): from starting the hunt
  until the potions are handed to the alchemist (`Playing` and `Found`), the magician and Al
  Cachud won't start a conversation. E at them still works but only puts their `BusyLine`
  ("...you are busy. Come back later...") in the bottom speech panel for 3 s. There's no camera
  move, no door opening and no input lock, and being sent away doesn't count as the magician's
  visit (his windows stay dark). The twenty-one game hangs off Cachud's conversation, so it waits
  too. The alchemist, the gravestone, the dolphin statue and dig sites are unaffected. Once the
  hunt is delivered, or runs out of steps and goes back to `Idle`, everyone talks again.

---

## 5c. Cursive clue writing itself

**Files:** `Assets/CursiveWriter.cs`, `Assets/ClueMinigame.cs` (`ShowClue`), `Assets/ShellMinigame.cs`

When the alchemist finishes the potion hunt, "Fell" is written onto the clue letter in
cursive (the magician's letter writes its keyword the same way), stroke by stroke in real
writing order, with drops of ink flicking off the nib.

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
  when the letter comes up after the alchemist's room closes; rebuilding the letter later
  shows it finished (`Complete()`). The writer is `[NonSerialized]` and rebuilt on demand.
- **Adding letters:** only the glyphs the clues need exist (F J T e f h i k l r ,). Add
  more in `BuildGlyphs()`; missing characters log a warning and leave a gap.

---

## 6. Night sky

**Files:** `Assets/Materials/NightSkySkybox.shader`, `Assets/Materials/NightSky.hlsl`

Fully procedural skybox - no textures. The stars and aurora live in `NightSky.hlsl`
(`NightSkyLights(dir, t)`).

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

**Replacement pixel-art ocean** (`Assets/Materials/PixelWater.shader`,
`Assets/Materials/OceanWaterMat.mat`, `Assets/Materials/Water.png`): the scene's existing ocean
material now uses the supplied art directly, not the old procedural water shader. The water
is flat and unlit to preserve the image's palette, with no generated waves, reflections,
specular highlights or foam drawn over it. World-space X/Z tiling gives one 256-pixel tile
per 16 metres, independent of the imported mesh's UVs and scale. Point filtering keeps
pixels crisp up close, mipmaps stop distant shimmer, and repeat wrapping joins tiles.
`_TextureScroll` gently drifts the tile; scene fog still softens the distant horizon.

**An ocean without edges** (`Assets/FarOcean.cs`, `Assets/OceanHorizon.cs`, the horizon fade in
`PixelWater.shader`): the map's ocean is a square, and from the title's orbit its edges showed
against the sky.
- **Far ocean:** `Far Ocean` in SampleScene is Unity's built-in plane, scaled to 8 x 8 km, with
  the ocean's material. Its `FarOcean` component (`[ExecuteAlways]`, so it shows in the Scene
  view too) keeps it centred under the `ocean` object, 2 cm below it, and sized, and gives it the
  ocean's material. The water is tiled from world positions, so it carries straight on past the
  old edges. Where the two overlap the ocean itself wins on depth, and both draw the same water
  anyway. It has no collider and casts no shadows. On every scene load `OceanHorizon` refreshes
  it, or lays one (and logs it) in a scene that has an ocean but no far ocean. A first version
  only built it at runtime, and it didn't show up on the title screen.
- **Melting into the sky:** the water shader blends distant water into the night sky's own
  colour for that direction, between 45% and 85% of the camera's far plane (never nearer than
  150 m, so the water round the island stays water). The sky colour comes from the skybox's
  gradient (horizon to zenith, sinking to its ground colour below the horizon), which
  `OceanHorizon` passes in as `_Sky*` globals. Past 85% the water is exactly the colour the
  skybox draws beyond the far plane, so there's no line where the geometry stops. The far plane
  grows with the title's orbit, so the same shares work there and in play.

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
- **Lamp lights:** a `LampGlow` light (a wide spot aimed down, see 14) is added inside every `lampN/pCube1`, placed at
  the mesh's bounds centre. `LampGlow` owns the Light's settings (sodium orange, intensity 280,
  range 32, hard shadows - see 20. Night mood) and turns off shadows on the lamp head,
  otherwise the head would block its own light.
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
- **Inverted normals (sidewalk not showing shadows, then black patches):** the sidewalk slab
  (`IslandDemo:pCube2`, renamed `IslandDemo:sideWalk` in SampleScene) came out of Maya with a mix
  of faces: most with normals facing opposite their triangle winding, some (the ramp along the
  south edge and other sections) correct. Faces still draw (culling uses winding), but lighting
  uses normals, so lights see the back of a flipped face: at night it renders pure black. The
  old fix voted per mesh and flipped all normals, which repaired the majority and broke the
  rest. `FixInvertedNormals` now works per vertex: it sums the area-weighted face directions
  (`Cross(b-a, c-a)`) of the triangles using each vertex and flips that vertex's normal and
  tangent when they disagree. Vertices that already agree stay as they are.
- **Transparent map material (dock's far sides showing through its top):** the FBX has one
  Maya material, `openPBR_shader1` (opacity 1, no transmission), on 96 objects including
  `IslandDemo:Dock` and its posts. Unity imports it as URP Transparent (premultiplied, queue 3000,
  no depth write), so triangles draw in mesh order, and on the L-shaped dock the far walls drew
  over the top from some angles. `MakeImportedMaterialsOpaque` runs first in the import, while
  every material on the map is still one the import made. It sets any Transparent material whose
  base colour alpha is 1 back to Opaque, the same way URP's inspector does (surface, blend,
  depth write, keywords, `RenderType` tag, Geometry queue, depth and shadow passes).
- **Inside-out slab (player stuck in `pCube86`):** `pCube86` is mirrored by a negative Y scale
  and its faces are stored pointing inward. Unity flips culling for mirrored objects and PhysX
  flips their collider triangles, so the stored direction wins: the slab rendered and collided
  inside out, the player walked in through faces pointing away and the inner faces boxed them in.
  `FixInsideOutMeshes` welds each mesh by position, and if it is closed with a negative enclosed
  volume it reverses the winding, flips the normals and the tangent sign. Only `pCube86` trips it.
- **Gravel sidewalk:** the slab gets a matte copy of `Assets/Materials/SidewalkGravel.mat`
  (URP Lit, smoothness 0, specular highlights and environment reflections off - the Arnold
  import was glossy) with a hand-drawn 32x32 gravel tile from
  `Assets/Editor/SidewalkGravelArt.cs` as its base map. `ExtraSidewalkNames` (`pCube86`) get the
  same material and metre UVs (normals via the inverse transpose, so squashed slabs keep their
  walls) but no spill.
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
- **Grass ground:** `Assets/Materials/Grass Texture.png` is a repeating, point-filtered
  256x256 tile on `GroundMat.mat` (one tile per 8 m, with mipmaps for distance).
  `IslandLampLights` gives the ground block (`IslandDemo:pCube1`) metre-based box UVs at
  import time, so the texture covers the island at a consistent scale. The scene overrides
  that mesh's material with `GroundMat`; the grass stays matte and uses its original colours.
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
- **Paved scene slabs** (`Assets/Editor/ScenePaving.cs`): the `sideWalkAlch` group in SampleScene
  is plain Unity cubes, not part of the model, so the importer never sees them. An editor
  script does the same job in the scene: every child gets the material of `IslandDemo:sideWalk`,
  a copy of its mesh with metre box UVs (`SidewalkSpill.MetreUVs`, shared with the importer,
  measured in the group's frame) and a `Sidewalk Spill (<slab>)` sibling with a `MeshCollider`,
  sloping onto `IslandDemo:pCube1`, the map's paved slabs or another slab in the group.
  - **Baked, not built at runtime:** the map's meshes aren't Read/Write, so a build couldn't
    read the ground. The meshes are saved to `Assets/Generated/sideWalkAlch/` and rebuilt in
    place (`EditorUtility.CopySerialized`), so the scene's references survive; meshes and spills
    of removed slabs are deleted.
  - **Rebuilt when slabs change:** a poll every 0.5 s in edit mode hashes the group (slab names,
    poses, sizes, active state) and repaves when it differs from the last paving this session,
    waiting for a scene-view drag to finish. `Tools/Orca/Repave Scene Sidewalks` forces it. Add
    another group by name to `GroupNames`.
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
- **Pixel art:** 7x7 hand-drawn cells at 16 px/m (droplets cropped to their used 5x5 middle). Droplets: 4 variants (rows, picked at
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

## 8c. Glowing windows

**Files:** `Assets/WindowLamp.cs` (every lit window), `Assets/WindowGlow.cs` (the older rays
effect, now switched off), `Assets/Materials/WindowLightRays.shader` and `.mat` (the rays),
`Assets/Materials/WindowGlow.mat` (emissive glass template)

### Lamp-lit windows (`WindowLamp`)

- **Which windows:** every renderer named `magHW...`, `alHW...` or `chudHW...` (the magician's,
  the alchemist's and Al Cachud's houses, named in the scene) plus `glowWindow1` and
  `glowWindow2`. They are found on every scene load (`sceneLoaded`, plus `AfterSceneLoad` for the
  first), so a renamed window needs nothing else. It runs before the windows' `Start`, so the
  `WindowGlow` on `glowWindow1/2` is switched off before it builds and all windows look alike.
- **Glass:** a copy of the lamp heads' material (`LampHeadGlow`, which the map import puts on
  `maplamp:pCube1`, borrowed from whichever renderer in the scene wears it) in a warmer, softer
  colour: base (1, 0.58, 0.24), emission (1.1, 0.48, 0.13) against the lamps' (2.8, 1.85, 0.84),
  matte (smoothness 0.15). Paler and brighter, the night grade's ACES and desaturation washed the
  panes out to near white, and a whole pane at the lamps' brightness felt blinding.
  Only the glass glows, the way the lamp heads keep their black stripes: a slot glows when its
  colour is at least 60% as light as the window's lightest slot (and not near-black), so the
  dark bars, stripes and supports keep their own material; so does any slot the rest of the
  house also uses (frames, walls). A window of one material glows whole; if nothing qualifies,
  its lightest slot does. Glass painted with a texture gets a copy of the lamp material with
  that texture as base and emission map, so painted stripes stay dark too. Without a lamp head
  loaded, a stand-in with the same colours. The originals come back when it's disabled.
- **Light:** a shadowless spot (amber (1, 0.68, 0.3), intensity 10, range 10 m, 130 degree cone, tipped down
  0.2 m per m) just outside the glass, on its own root object. Out is the thinnest axis of the
  window's box, on the side away from the rest of the house, as below. The PC renderer is
  Forward+, so the extra lights aren't limited per object.
- **Glow on the wall round it** (`Assets/Resources/WindowSpill/WindowSpill.shader`,
  `Orca/Window Spill`): the spot points away from the wall, so the wall stayed pitch black around
  a bright pane, and the window looked painted on rather than lit. A real point light out in
  front didn't fix it. The walls hardly showed it, and it threw a hot specular spot off the
  glossy bars. Instead an additive card lies on the window's outside face, lifted 2% of the
  pane's smaller side. It covers the window plus `SpillMargin` (0.4) x that side all round and
  adds the light's amber at `SpillIntensity` 0.12. Nothing is added over the window itself
  (UV1 carries the window's share of the card); from its edge the glow fades out to the card's
  border (`pow(1 - d, 2)`). Being added light rather than lighting, it shows on dark walls too
  and has no highlight. Fog fades it to black. One shared material, one small mesh per window.
  It sits under the spot's object, so it shows, hides and is removed with it. It's in
  `Resources` so builds include the shader.
- **Round windows (glass drums, `alHWRound...`):** these are closed cylinders, not panes. Their
  thinnest box side was their height, so "out" came out as straight up or down: the spot shone
  down from the bottom face and the spill card lay flat on it. A window counts as a drum when its
  two level sides are within 75% of each other (`DrumRoundness`) and it is at least a quarter as
  tall as it is wide (`DrumHeightShare`). That rules out panes in walls and flat skylights;
  round panes set in a wall still count as panes. A drum gets a point light in its middle,
  shadowless so the glass doesn't stop it, shining out all round. Its intensity is the pane
  spot's x ((radius + 3) / 3)^2, so 3 m past the glass is lit like 3 m in front of a pane, with
  range radius + 10 m. The spill becomes an open tube 2% outside the glass, `SpillMargin` x the
  drum's height taller above and below it. The same shader reads it as a card that is all
  window across, so the glow only rises and falls from the drum's top and bottom edges, the same
  all round.

### The older rays effect (`WindowGlow`)

Still on `glowWindow1` and `glowWindow2` (the FBX's `world/building1/pCube79` and `pCube56`,
renamed in the scene) but switched off by `WindowLamp`. How it worked:

- **Which way is out:** each window is a thin box. Its thinnest axis is the glass normal, and of
  its two faces the one away from the centre of the rest of the house is the outside, so the
  same component works on any wall.
- **Glass:** slot 0 (`glassSlot`) gets a copy of `WindowGlow.mat` in `color`, emission
  `color x 2.6` (HDR, so bloom spreads it); slot 1, the window bars, stays the house material.
  The original materials come back when the component is disabled or destroyed.
- **Rays:** a mesh of additive cards from the glass outwards. Nine shafts (`shafts`) start at
  spread-out spots on the glass, each two crossed cards (one flat, one upright) 12-30% of the
  window wide and 55-100% of `rayLength` (2.6 m) long. They widen by `raySpread` (45%) and lean
  down by `rayDroop` (0.25 m per m). Two upright and two flat haze cards fill the beam between them.
- **`Orca/Window Light Rays` shader:** brightest at the glass, fading out along the card
  (`(1 - y)^1.8` after a quick rise off the glass), soft sides, and faded when a card is seen
  edge-on so no shaft shows as a hard line. Each shaft brightens and dims on its own phase
  (UV z), about one slow pulse every 3 s. Vertex colour carries the light colour and each
  shaft's brightness.
- **Light:** a shadowless spot (intensity 22, range 14 m, 120 degree cone) on the glass,
  tipped down like the rays, with a slow Perlin waver of up to 8%. It sits outside the window's
  collider so `SpriteLightTint` doesn't count the window as blocking it.
- **Switching on:** `litBy` points at the house's door (`MagicianEntrance`'s `MagicianEncounter`).
  The windows stay dark until `HasVisited` (the first E at the door) and the player is back
  outside (`!InRoom`: idle, the fade back out, or the outdoor trick), so the player sees them come
  on. Then everything (light intensity, ray intensity, glass emission, and the glass colour from
  its unlit colour) fades in over `fadeInTime` (1.5 s, smoothstepped), `glowWindow1` half a
  second (`switchOnDelay`) after `glowWindow2`. Once on they stay on. Left empty, a window is
  lit from the start. Each window keeps its own copy of the ray material, so the fades don't mix.
- The rays and the light live on a root object (`glowWindowN Light Rays`): not under the window,
  whose uneven scale would stretch them, nor under the house, whose bounds frame the hat trick.
  Built in `Start`, so they only show in Play mode.

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

Shown once when play starts; no extra scene. PLAY flies the camera out to the player's boat
for the opening (below).

- **Seeing the island at night:** from the orbit the scene's fog (density 0.009) and the dim
  moon hid the island entirely. While the title is up, through the flight and the crossing,
  the fog is thinned to `TitleFogDensity`: `0.45 / distance from the camera to the island`,
  never thicker than the scene's, which keeps the island about 80% clear (exp2 fog,
  `e^-(0.45^2)`). Closing in during the crossing, it thickens back towards the scene's. The
  moon (`RenderSettings.sun`) is 1.8 x brighter. Both ease back to the scene's own values over
  the 1.2 s hand-off (or the flight, without a crossing). They are restored exactly when the
  title finishes or is destroyed.

### Opening: arriving by boat

**File:** `Assets/OpeningVoyage.cs` (TitleScreen owns the timing, the shot and the input lock)

- **Where from and to:** the dock is `IslandDemo:Dock` in the map, an L whose leg runs out to
  sea along x at the island's -x edge (the bottom of the editor's top view). The boat comes in
  along x from the side away from the middle of the island block (`IslandDemo:pCube1`), so it
  never crosses land, and moors 0.5 m off the leg's end, lined up with where the jump lands:
  1.6 m in from the end, 1 m to the right of the walkway's middle (found scanning across z for
  the dock's own upward surface), kept 0.6 m in from its edge. If the follow camera would be
  blocked there (`CameraFollow.HasClearView`, the same sphere cast it uses; a post such as
  `IslandDemo:pCylinder2` pulled it in for an awkward close-up), the landing moves further right
  in 0.2 m steps until the view is clear or the walkway ends. It sets off on that
  line 20 m inside the edge of the `ocean` object's bounds (kept between 60 and 400 m out), and
  the crossing takes distance / 9 m/s, held to 10-20 s, easing out so it slows as it comes in.
- **The boat:** a placeholder cube (2.4 x 0.8 x 4.2 m, wood-brown copy of the default material)
  riding 0.35 m above the water, bobbing and rocking a little. The player is placed on its deck
  each frame with the CharacterController off; the cube's collider stays off until it moors.
- **The shot:** right behind the player, 5.5 m back, 1.4 m to their right and 2.6 m above their
  feet, looking 8 m ahead the way the boat goes; it follows them through the jump (along the
  jump's line, not its arc, so it doesn't bounce). The player stands with their back to the
  camera (`PlayerController.FaceAway`, then the idle frame). The letterbox stays up;
  "SOMEONE'S BEEN MURDERED!" shows 1.2 s after it sets off, for 2.8 s.
- **Getting off:** once it's in, "[SPACE]" fades in over the player. SPACE jumps them in a
  0.75 s arc (1.4 m above the straight line, back-walk frames away from the camera) onto the
  landing spot, its height read from the dock's own collider (not a post or lamp on it).
  Landing ends the opening: the follow camera faces up the dock towards the island
  and the view blends into it over 1.2 s while the letterbox opens. The boat stays moored, solid.
- **Interrupted** (scene reload mid-crossing): the boat moors and the player is put on the dock,
  their controller back as it was. No dock in the map: PLAY just flies down to the player.

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
- A buried `MagicWand` keeps its leaking magic (`WandMagic`'s glints and light) dark until it has risen
  (`PickupItem.IsBuried`), so before the gravestone talk nothing lights the site.

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

## 14. Running on weaker GPUs

Goal: smooth on integrated graphics (e.g. Intel Arc in a Core Ultra 7 155H) without
removing anything. Where the time went: the map is 5058 separate meshes, ~3.2M triangles
(~2.8M of them leaf spheres in shrubs, hedges and trees), and every shadowed point light
renders it six more times - with 15 lamps that was ~90 extra passes, plus four moon
cascades, plus a full re-render for SSAO's depth-normals prepass.

- **Lamp shadows by distance** (`LampGlow`): in play mode only the 2 lamps nearest the
  camera keep shadows (was 6, which kept all four round the fountain plaza). The rest
  fade `shadowStrength` to 0 over 0.6 s and switch shadows off; coming closer fades them back
  in, so nothing pops. Every lamp keeps its light. Ranked 4x a second; in the editor every
  lamp keeps its shadow. With fewer lamps sharing the 4096 shadow atlas, each one also gets
  a sharper shadow map than before. `SpriteLightTint` follows automatically (it only casts
  rays for lights with shadows on).
- **Mesh LOD on the map** (`generateMeshLods` in the FBX import settings): Unity builds
  simplified versions of each mesh and picks one by screen size, in the main view and in
  shadow maps. Close up nothing changes.
- **Hidden things aren't drawn:** GPU occlusion culling is on in the GPU Resident Drawer
  (already enabled), so objects behind buildings and terrain are skipped. Depth priming
  (Auto) reuses the depth prepass so overlapping leaf spheres are shaded once per pixel.
- **Cheaper, same effects:** SSAO at half resolution from depth only (no depth-normals
  prepass), Gaussian blur; moon shadows 2 cascades instead of 4 (same 50 m distance);
  soft shadows off altogether since the night-mood pass (hard shadows are also the cheapest).
  The opaque-texture copy is off - nothing samples
  `_CameraOpaqueTexture`.
- **Aurora early-out** (`NightSky.hlsl`): visibility (patches x breathing x horizon fade) is
  worked out first and the 16-layer loop is skipped where it's
  zero. Identical output.
- **Adaptive resolution** (`AdaptiveResolution`, installs itself): reads GPU frame time from
  `FrameTimingManager` (Frame Timing Stats is on in Player Settings). Over 14.5 ms it lowers
  the pipeline's render scale in 0.05 steps (never below 720 rows); under 11 ms it raises it
  back. UI stays at full resolution; the 3D image is upscaled with FSR. A GPU that keeps up
  never changes. Without GPU timings it only lowers, on frames clearly missing vSync. The
  asset's original render scale is restored when play stops.
- **Frozen backdrop** (`FrozenBackdrop`, installs itself): while the pause menu or the
  inspection view is open, the world is rendered once (`RenderPipeline.SubmitRenderRequest`)
  into a texture shown on a canvas beneath all other UI, and the camera draws nothing
  (culling mask 0, no shadows, post-processing or skybox) until it closes.
- **Small things:** vSync on (laptops share power between CPU and GPU; uncapped frames only
  cost heat); the fountain stops launching droplets beyond the distance where an art pixel
  is under 0.1 screen pixel (farther than the title view); `CameraFollow` uses a
  non-allocating sphere cast.
- **Lamps are downward spot lights** (`LampGlow`): looking at the fountain plaza cost ~40%
  of the frame at any distance. Measured, the fountain itself was noise; the four plaza
  lamps' shadows were the cost: as point lights each rendered the map six times (cube
  faces), 24 extra shadow views, +3.2M triangles and +3,850 shadow casters a frame, and only
  while they're in view. Now each lamp is a 150-degree spot (soft edge from 110) aimed
  straight down: one shadow view per lamp. Nothing above about 15 degrees under the lamp
  heads is lit.
- **Fountain spray:** lit per vertex in `Orca/Fountain Spray` (the pixel shader is a texture
  read, clip and multiply), droplet quads cropped from 7x7 to 5x5 cells, launch rate falls
  with screen size below one screen pixel per art pixel (down to 25%), and the pool's
  property block is only set when its frame changes. Small savings; the spray was never the
  bottleneck.
- **Foliage merged per plant** (`IslandLampLights.MergeFoliage`, at import): the frame was
  main-thread bound on draw submission (~5300 meshes, mostly leaf spheres, each culled and
  drawn separately in the main view, depth prepass, moon cascades and lamp shadows). Each
  tree, shrub and hedge's leaf and bark parts are combined into one mesh per shade material
  in the plant's space (full-detail triangles; mirrored parts get their winding and tangent
  sign turned), with Mesh LODs generated for the result. Same triangles and materials, so it
  looks the same. The parts keep their GameObjects and colliders, and a `MergedRenderer` on
  each points at the renderer that draws it now; `SpriteLightTint` follows it so foliage
  still shades the player.
- **Depth priming stays:** on desktop, URP's Auto depth priming always runs the depth prepass
  (~2 ms of main thread here), whatever SSAO needs. Turning it off would trade that CPU time
  for leaf overdraw on the GPU, which hurts the integrated GPUs this section targets.
- **FPS counter** (`FpsCounter`, installs itself): `]` toggles a pixel-font readout in the
  top-right corner, averaged over half a second on unscaled time so it keeps counting while
  paused. `GUI.depth` -2000 draws it over the pause menu and title screen. The label string is
  only rebuilt when the number changes.

---

## 15. Dolphin statue letter puzzle

**Files:** `Assets/DolphinWordPuzzle.cs`, `Assets/Scenes/SampleScene.unity`

- Walk within 2.5 m of the `DolphinStatue` and press E to open a pixel-art menu.
  Its prompt sits near the closest side of the base, not above the 13 m-tall model.
  While it is open, player movement and other interactions are locked; Esc closes it.
- The clue line shows **Fell, Jerk, Thief** all at once for now. Later progression can
  gate these words. The letter bank uses exactly these letters, without spaces.
- The thirteen movable letter tiles start in clue order. Click a tile, then an empty slot or
  another tile to move or swap it. The answer slots have gaps after JEFF and THE.
  When they exactly spell **JEFF THE KILLER**, the menu says **YOU WIN** and keeps
  the completed arrangement on reopening.

---

## 16. The magician's lantern

**Files:** `Assets/PlayerLantern.cs` (on `Player`), `Assets/PlayerController.cs` (sprites),
`Assets/InventoryInspection.cs` and `Assets/InventoryHUD.cs` (equipping), art in
`Assets/Sprites/lanternPlayer/`

- **In the bag from the start:** `PlayerLantern.Start` puts the lantern (item `"Lantern"`,
  `itemSprite` = `Lantern.png`, set on the Player in SampleScene) in the bag when the game
  starts, and again on every reload since the bag starts empty. It's not held yet, and it's never
  added twice. `startWithLantern` switches it off. The magician no longer hands one over: his
  `LanternGift`, `LanternNoRoom` and `HasGivenLantern` are gone, so without the wand he asks for
  it and sends the player out, and with it the thanks leads straight to the trick.
- **Equipping:** pressing the lantern's slot number (1-5) toggles holding it instead of opening
  the inspection view, and clicking its slot in the inventory strip does the same. While held, its
  slot gets a gold rim and a gold name. Losing the item puts it away.
- **Sprites:** `lanternFrontIdle`, `lanternFrontWalk`, `lanternLeftWalk`, `lanternRightWalk` on
  `PlayerController` (512 px frames like the rest, two-frame sheets sliced top then bottom). There
  is no back view with the lantern: from behind the body hides it, so the normal back sprites stay.
- **Light:** a warm shadowless point light (intensity 4, range 9 m, Perlin flicker up to 15%) on a
  child of the player, moved to the hand holding it: the sprite's left from the front and walking
  left, its right walking right, and in front of the player when they face away.
- **Lighting the holder:** `SpriteLightTint` would otherwise see a light a few centimetres from the
  sprite and burn it white. Lights inside the player count as at least 1 m away and at 40%
  (`heldLightShare`), so the lantern brightens the player without washing them out.

---

## 17. The alchemist's letter

**Files:** `Assets/AlchemistEncounter.cs`, `Assets/PickupItem.cs` (`Collect`)

- **Same cinematic as the magician:** E at `alchDoor` ("Knock") fades into the room
  (`Magician/Room`), the portrait bobs while each line types, Enter finishes typing then
  continues, Esc leaves, and it fades back out. The portrait is the magician's art and the lines
  (`Introduction`, `LetterGift`, `Farewell`) are placeholders until the alchemist's own exist.
- **Sets itself up:** nothing is placed in the scene. On every scene load (`sceneLoaded`, plus
  `AfterSceneLoad` for the first) it finds the object named `alchDoor` and puts an
  `AlchemistEntrance` at the bottom centre of what it renders (FBX nodes keep their pivot at the
  model's origin), so renaming any door to `alchDoor` moves the encounter there.
- **The letter changes hands:** the world's `Clue Letter` pickup is switched off when the entrance
  is set up, and after the introduction `PickupItem.Collect()` puts it in the bag with its own
  sprite, exactly as picking it up would, so its number holds up the hunt's map (see 5b). A full
  bag gets `LetterNoRoom`; the next visit skips the introduction and offers it again.
- **Later visits:** while potions are still out there, one line (`KeepLooking`); with all five
  found, `PotionsFound` then `ClueGift`, which has `ClueMinigame.Deliver()` write "Fell" on the
  letter; after that `Farewell`. Each visit plays a short script of lines and an action after
  its last (`Play`).
- **Blocking:** `AlchemistEncounter.IsOpen` joins the others in `Interactables.Busy` and
  `BlocksMovement`, hides the inventory strip, stops Esc from pausing and holds the camera.

---

## 18. Al Cachud comes out of his door

**Files:** `Assets/CachudCallout.cs` (the meeting and conversation), `Assets/CachudDoorway.cs`
(the door and the walk out; it was the old opening's walk-out)

- **Waiting indoors:** on `Start` he finds his door (`CachudDoorway.FindDoor`: the object named
  `chudDoor` in `IslanddemoV3`, as SampleScene renames it, else the model's
  `pasted__building2/pasted__pCube54`) and, until he has come out, hides his sprite and turns his colliders off, so he
  isn't an invisible obstacle where he was placed. The door is the interactable: "[E] KNOCK"
  over it within 3 m. There is no longer a "HEY, YOU!" call-out by distance.
- **Knocking:** the camera pans to a shot from out in front of the door (1.25 s, as before),
  and the player is set down 4.2 m out and 1.4 m to the side, back to the camera, if that spot is
  clear. Then the door swings 105 degrees about its hinge edge (it has a centre pivot, so it is
  rotated about a world-space hinge without touching the model), a dark unlit quad covers the
  solid facade behind it, he walks from 0.9 m inside to 2 m outside with a small lift on each
  step (he has no walking frames), and the door shuts behind him (3.8 s). Which face is the
  outside comes from where the player knocked.
- **Then the same conversation:** greeting, missing dice, the one question, the fountain clue,
  the dice hand-in and the twenty-one invitation, in the shot the entrance ended on. Escape or
  a cancel mid-entrance finishes it at once: door shut, him outside.
- **Afterwards:** he stays where he stopped outside the door and E talks to him again, with the
  usual side-on shot. Without the door in the scene (tests), knocking just starts the
  conversation where he stands.

---

## 19. Door beacons

**Files:** `Assets/DoorBeacon.cs`, `Assets/Resources/DoorBeacon/DoorBeacon.shader`

- **Which doors:** after every scene load (same `BeforeSceneLoad` + `AfterSceneLoad` bootstrap as
  the alchemist) it looks up `alchDoor`, `magDoor` and `chudDoor` by name and stands a
  `Door Beacon (<door>)` object at the bottom centre of what each door renders. Renaming another
  object to one of those names moves its beacon there.
- **The beam:** one quad, `1.2 x` the door's width and `4 x` its height, sized from the door's
  bounds so it fits any map scale. The shader (`Orca/Door Beacon`, additive, no depth write)
  turns it to the camera about the vertical axis, so it never shows edge-on. It is brightest at
  the ground and fades out upwards (`pow(1 - v, _FadePower)`), has a bright core with a soft halo
  across its width, a slow pulse and faint bands drifting up. It is pulled `0.75 x` the door
  width towards the camera so the wall behind the door doesn't cut it in half. No shadows, no
  real light. It lives in `Resources` so builds include it.
- **When it shows:** it fades in and out over 0.6 s. It's hidden while `Interactables.Busy` (any
  conversation, cinematic or the magician's outdoor trick) or while its door is inactive.
  Al Cachud's goes out for good once he has come out, since from then on E talks to him, not the door.
- **Fades as you walk up:** on top of that, its brightness follows the player's ground distance
  from its foot: full beyond 14 m, eased (`SmoothStep`) down to nothing at 4 m, just before the
  door's own "[E]" prompt shows at 3 m. Walking away brings it back.
- **Renamed doors:** the scene renames the model's doors for this (`chudDoor` was
  `pasted__pCube54`, `magDoor` was `building1/pCylinder1`), so `CachudDoorway.FindDoor` and
  `MagicianEncounter.FindDoor` look for the new name first and fall back to the model's.

---

## 20. Night mood: hard light for a murder mystery

**Files:** `Assets/Scenes/SampleScene.unity` (moon, ambient, fog), `Assets/LampGlow.cs`,
`Assets/Settings/SampleSceneProfile.asset` (post-processing), `Assets/Settings/PC_RPAsset.asset`,
`Assets/Settings/PC_Renderer.asset` (SSAO), `Assets/Materials/NightSkySkybox.mat`,
`Assets/Materials/LampHeadGlow.mat`

The old look was soft: no moon, every lamp reaching 70 m, so the island was a single warm wash
with soft-edged shadows. The new look is a noir night: cold moonlight with crisp shadows,
tight sodium-orange pools under the lamps with real darkness between them, murk in the
distance, and a desaturated, high-contrast grade.

- **Moon (the scene's `Directional Light`):** re-enabled. It is Realtime (it was Baked with
  no bake, so it lit nothing), cold blue-white `(0.58, 0.68, 1)`, intensity 0.35, hard
  shadows at full strength. It's lowered to 32 degrees (was 50) so houses, fences and trees
  throw long shadows. Its Custom Shadow Layers still include Lamp Pole Shadows. It is also set
  as `RenderSettings.sun`, which the player's shadow quad follows.
- **Lamps (`LampGlow`):** sodium orange `(1, 0.66, 0.3)`, intensity 280, range 32 (was 70), hard
  shadows. The pool under each lamp stays about as bright, but it ends within about 25 m, so
  the ground between lamps is moonlit only. The lamp heads' glow (`LampHeadGlow.mat`) is
  shifted to the same orange. The importer's version was bumped so the map's lamps pick up
  the new defaults, and the four lamps placed in the scene were updated to match.
- **Shadows:** soft shadows are off in `PC_RPAsset`, so every shadow has a hard edge. SSAO is
  stronger (0.6, was 0.4) for darker contact shadows where walls meet the ground.
- **Ambient:** Trilight, cold and low - sky `(0.022, 0.03, 0.06)`, equator
  `(0.012, 0.015, 0.026)`, ground `(0.004, 0.004, 0.006)`. Shadowed areas stay dark blue rather
  than black, so the player can still read the ground.
- **Fog:** exponential squared, density 0.009, colour `(0.03, 0.05, 0.1)`, close to the sky's
  horizon so distant shapes sink into it. About 80% of the scene is still visible at 50 m and
  16% at 150 m, so the island comes out of the murk during the opening crossing.
- **Sky:** aurora down to 0.12 (was 0.33). The bright green curtains read whimsical, not tense.
- **Post-processing:** ACES tonemapping (filmic, more contrast than Neutral). Colour
  Adjustments: post exposure +0.3 (to make up for ACES), contrast +22, saturation -28 and a
  slightly cold filter. Split Toning: teal shadows `(0.38, 0.47, 0.62)`, warm highlights
  `(0.62, 0.53, 0.42)`, balance -20. Vignette 0.38 (smoothness 0.4). Film Grain Medium 1 at
  0.2. Bloom 0.3 with tighter scatter (0.4) so the lamp haloes don't smear.
- **Tuning:** moon brightness and angle are on the Directional Light. The size of the lamp
  pools is `range` in `LampGlow` (also serialised on the four scene lamps). The grade is in
  `SampleSceneProfile`. How far you can see is the scene's fog density (Lighting window >
  Environment).

---

## 21. Island terrain, rock shore and foliage

**Files:** `Assets/Editor/IslandTerrain.cs` (levels, ground, moving what stands on it),
`Assets/Editor/IslandScenery.cs` (trees, undergrowth, boulders, shore),
`Assets/Generated/IslandTerrain/` and `Assets/Generated/IslandScenery/` (the baked meshes),
`Assets/Materials/CliffMat.mat`, `SceneryMat.mat`, `Scenery Palette.png`

The island came out of Maya flat: a 122-vertex slab with everything standing on it at one
height, and forty trees that were all the same two trees. Two editor tools reshape and dress it.
Nothing is hand-modelled; every mesh is built in code and baked into the project, so the
result is the same on every run and the model itself is never edited.

- `Tools/Orca/Raise Island Terrain` runs once on the flat island. It refuses to run twice.
- `Tools/Orca/Regrow Island Scenery` can be run any time; it deletes what it grew and regrows
  it from the same seed. Run it after the terrain.

### Heights: a border drawn on the map, then blurred

The island sits at four levels: the harbour and start house at sea level, the lower town (the
square and the well) at +5 m, the upper town (both big houses, the north road, the magician's
dell) at +9.5 m, and wooded heights round the dell and on the south-west lobe at +15 m. East of
the east road the town falls away to the beach.

- **A level is a yes/no map.** Each of the three `Levels` is a rise in metres and a test for
  "is this spot inside" (an outline polygon for the lower town, plain x/z comparisons for the
  others). It is rasterised at one sample per metre: inside is the rise, outside is 0.
- **The slope comes from blurring that map**, box-blurred twice by `Blend` metres either side.
  Two box blurs make a tent, so each slope is a smooth S: level at the top and bottom, steepest
  halfway. Three things fall out of the one number: the slope is about `4 x Blend` long, the
  ground is exactly level again `2 x Blend` from the border, and the steepest point is
  `Rise / (2 x Blend + 1)`. That gives 18 degrees into the lower town, 22 up to the upper town
  and 23 on the wooded banks.
- **The three maps are added**, and the sum is the rise at any spot (`HeightField.At`, read
  with bilinear filtering). Everything else in the tool asks only that one question.
- **Where the borders go is the design.** Because the ground is exactly level `2 x Blend` from
  a border, a building that far inside one keeps a level floor. Borders run through lawns and
  cross the paths at right angles, so a path climbs head-on and never leans sideways. The
  lower-town edge is bent for this: it meets the west diagonal road and the path to the east
  gate square on, and passes south of the well.

### Re-cutting the ground

- **The slab is too coarse to bend,** so its triangles are split: every edge longer than 3 m is
  halved, again and again, until none is left (122 vertices become about 39,600). An edge is
  judged by its two end points alone, so both triangles sharing it make the same choice and the
  surface stays closed. Midpoints interpolate the UVs, so the grass keeps its metre scale.
- **Only the top rises.** A vertex's share of the rise goes from 1 at the top surface to 0 at
  the slab's underwater rim, so the sides stretch into cliffs and the sea bed stays put.
- **Rock on the steep faces.** After reshaping, each triangle goes into one of two submeshes by
  its own normal: grass, or `CliffMat` where it is steeper than about 52 degrees. The cliff
  texture is drawn in code like the gravel: banded strata in the grass texture's chunky pixels.
- **A crack in the model:** the slab had a hairline wedge open across its top, east from the
  square (two corners at the shore 27 cm apart that should be one). Flat, it never showed;
  raised, the lawn would split along it. Corners closer than 0.4 m are welded before cutting.
  `RepairJunctions` also cuts any edge that has another corner sitting partway along it, the
  other way a flat mesh hides a crack.
- The model is untouched: the new mesh is saved as `Ground.asset` and the scene's ground object
  points at it, keeping its name, so `ClueMap`, the paving and the opening still find it.

### Moving everything that stood on it

Every object is visited once, parents before children, and given one of four treatments.

- **Lifted whole** by the rise under its middle: anything under 8 m across, each shrub, and
  anything named as a tree, lamp, building, house, statue or grave. A building's parts all get
  the same rise because the borders keep clear of it.
- **Trees go by the lowest ground under the trunk,** not the middle. A trunk is cut off flat,
  so on a slope its downhill side would hang in the air.
- **Hedges are lifted and leant.** A hedge is one 17 m row; lifted level on a slope, its ends
  would be metres out. It is tipped to the slope measured between its two ends, turning about
  its middle.
- **Bent:** a single long mesh that crosses a slope (the road slab, its gravel spill, fence
  rails, the alchemist's path) gets a copy with every vertex raised by the rise under it.
  Vertical lines stay vertical, so nothing tips over. Normals are corrected by the slope
  instead of being recalculated, which would put seams in smooth shading. Coarse meshes are
  split first (2.5 m edges) so they follow the curve.
- **Positions are absolute.** Each object's new place is worked out from where it stood before
  anything moved, so a child never also rides its parent's lift.
- `ScenePaving` is told to re-lay the alchemist's path last. Its old meshes are deleted first:
  it normally rebuilds them inside their files, and the editor keeps drawing the old shape of a
  mesh rebuilt that way until it restarts.

### Modelling the plants

No two trees are alike: each is grown from its own random numbers, not copied from a few
models.

- **Broadleaf:** an 8-sided trunk of five rings, wide at the foot and buried 0.7 m (so a slope
  never shows under it), with a slight bend. It forks into 3-5 boughs rising at 22-48 degrees.
  Each bough ends in a leaf mass, with a crown on top and one filler mass.
- **A leaf mass** is a ball of triangles (an icosahedron split twice, 162 points) with every
  point pushed in or out by Perlin noise, and the underside flattened to 60%. That lumpiness is
  what separates them from the model's perfect spheres.
- **Poplar:** tall and narrow. Four or five leaf masses stacked up the trunk, each taller than
  wide and smaller than the one below.
- **Pine:** 4-6 tiers of boughs. Each tier is a cone whose rim alternates long and short points
  (9-11 of them) and droops, with an underside so it looks right from below, where the player
  is. Pines are flat-shaded; the leaf masses are smooth.
- **Bushes** are 3-5 small leaf masses. **Ferns** are 6-10 arching ribbons from one root.
  **Grass tufts** are 5-9 tapering blades. **Flowers** are a stem and a five-sided double cone.
  Ribbons and blades are drawn on both sides and lit as if they faced up, so they shade like the
  ground they grow from.
- **Boulders** are a coarse ball (42 points) with every corner pushed in or out by up to 26%,
  squashed, turned at random and flat-shaded.
- **One material for everything.** `Scenery Palette.png` is 4 x 16 pixels: sixteen rows, each
  four shades of one thing (three leaf greens, pine, two barks, grass, fern, four petal
  colours, rock, wet rock, cut wood and one spare). A vertex's V picks the row and its U says how light it
  is, lighter where it faces the sky. The texture is point-sampled, so the shading steps in
  four flat bands. That keeps the cartoon look and means bark, leaves, petals and rock all
  draw with `SceneryMat`.
- **The model's own trees** that still face the way the model left them (36 of the 40) are each
  turned (25-335 degrees), resized (big ones 80-108%, medium 82-122%) and leant up to 3.5
  degrees about the foot of the trunk. The numbers come from a hash of the tree's name, so they
  never change, and a tree already turned is left alone. Fifteen stood a little above the
  ground; each is lowered until its trunk is 0.3 m in the earth all the way round.

### Placing them

- **Zones set the density.** Eleven rectangles (`Zones`) give trees, bushes, ferns and flowers
  per 100 square metres and the split between broadleaf, poplar and pine: 1.5-1.6 trees in the
  three woods, 1.0 in the west grove, 0.1-0.7 on the lawns, pine-heavy on the north head and
  south-west.
- **A jittered grid, not random points.** `Scatter` walks the island in cells (3 m for trees),
  picks one random spot in each and plants there with probability density x cell area. Plants
  can't pile up, and a fixed seed gives the same island every time.
- **Woods grow in clumps.** In a wood the density is multiplied by a slow Perlin noise, 0 to
  1.7, which leaves clearings and thickets.
- **Every spot is checked before anything grows:** ground under it and above the tide line;
  not too steep (trees stay under about 30 degrees); nothing within reach from just above the
  grass to overhead (one capsule query that catches paths, walls, fences, trunks and posts);
  clear of building footprints by the tree's own spread; 7 m from any lamp, so the pools of
  light stay; outside a ring round everything the player has to find or use (the player's
  start, both doors, the letter, dice, wand, dig site, gravestone, statue and the dock); and
  not crowding another new tree.
- **Undergrowth follows the same rules with smaller numbers.** Ferns come in stands of one to
  three, flowers in patches of five to ten of one colour, grass tufts everywhere there is
  grass. Boulders are sparse on the flat and thick where the ground slopes over about 17
  degrees, so the banks between levels look like banks.

### The rock shore

The slab's sides are sheer and showed as a dark gap above the water. A bank of rock now runs
from the island's edge down into the sea, all the way round (about 1 km).

- **Finding the coast:** take the ground's upward faces, and the edges only one of them uses
  are the outline. Chained end to end they make one loop, resampled every 2 m, each sample with
  the direction out to sea (averaged over its neighbours so corners don't fan open).
- **The bank** is eight rows of points at each sample: one tucked under the turf, a raised lip
  at the edge, then six stepping out and down to 1.7 m under water. It reaches further out the
  higher the cliff (2.2 m plus half a metre per metre of height). Each row is jittered in and
  out and up and down, in blocks two samples wide, which gives ledges instead of a smooth ramp.
  Faces are flat-shaded, and anything near the waterline takes the darker wet-rock row.
- **Boulders** sit at the waterline on about four samples in ten, with more partway up cliffs
  over 3.5 m.
- **The dock's foot is left smooth:** within 3 m of it the lip and the jitter go away and no
  boulders are placed.

### Baking it

- **One mesh per patch.** Plants are written straight into a builder for the 48 m patch they
  stand in, in three layers: trees and boulders (cast shadows), ground cover (no shadows) and
  shore (also the collision mesh). About 2,500 plants and boulders and the whole shore come to
  97 meshes and one material, the same idea as the model's merged foliage.
- **Packed small.** Normals are stored as bytes and UVs as 16-bit, 20 bytes a vertex, because
  these meshes (about 325,000 vertices) are saved in the project.
- **Colliders are separate and simple:** a capsule for each trunk, a sphere for each crown
  (kept 2.4 m clear of the ground so the player walks under it), a sphere for each bush and
  larger boulder. Each carries a `MergedRenderer` pointing at the mesh that draws it, like the
  model's plants, so `SpriteLightTint` shades the player under them.
- **The clue map** reads the new trees and bushes as foliage by name. `ClueMap` treats anything
  named `rock` as in the way, and only an unnamed flat surface over the sea counts as a pier,
  so the shore doesn't draw as dock.

### Changing it

- **Heights and slopes:** the `Levels` table at the top of `IslandTerrain.cs`. To run it again,
  first put back the flat island: both the scene and `Assets/Generated/sideWalkAlch` from the
  commit before the terrain, since the paving's meshes are bent too.
- **How much grows where:** the `Zones` table in `IslandScenery.cs`; `Seed` for a different
  arrangement; `KeptClear` for the rings nothing grows in.
- **Colours:** the `Ramps` strings, four hex colours per row; the palette is rewritten on the
  next regrow.

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
