---
name: corner-props
description: Stand props (pillars, statues, any model) in the corner gaps of the cube-rail runner's sideways turns, per checkpoint, in MainScene prop camera 3.7 - import .glb/.fbx models with their textures, reduce them for mobile, order them in a CornerPropSet, wire them in. Use when the user says "add assets to the corners", "new pillars", "give checkpoint N its own corner props", "use these models in the turns", drops new models in Assets/BackgroundAssets, or asks how the corner props work.
---

# Corner props (MainScene prop camera 3.7)

Every leg of the path is laid with a diagonal riser: around each turn two of the 2 x 2 cells are empty. At every
**sideways** turn (never a climb or a drop) one of those cells gets a flat, upward-facing **tile** (a quad, not a cube)
level with the track top, and a **prop** standing on it.

## Files

| What | Where |
|---|---|
| Runtime (partial of DynamicPathGenerator) | `Assets/Scripts/DynamicPathGeneratorCornerProps.cs` |
| Hooks in DynamicPathGenerator.cs | `ResetCornerProps` / `UpdateCornerProps` / `DestroyCornerPropRuntime` / `PlaceCornerProp(behind, currentDir)` (beside PlaceCornerGem) / `ReleaseCornerPropsOn(s)` |
| Prop list per checkpoint | `Assets/Scripts/CornerPropSet.cs` (ScriptableObject: `props[]` in order, `legsPerProp` = 5) |
| Model -> prop builder | `Assets/BackgroundAssets/pillars/Editor/CornerPillarBuilder.cs` (Tools > Corner Props > Build Pillars / Wire Pillars Into 3.7) |
| Source models (pillars) | `Assets/BackgroundAssets/pillars/*.glb` (order = file name, ordinal) |
| Built output | `Assets/BackgroundAssets/pillars/Built/Pillar_NN{.prefab,.mat,_Mesh.asset,_BaseColor.png,_Normal.png,_MetalSmooth.png}` |
| The set | `Assets/BackgroundAssets/pillars/CornerProps_Pillars.asset` (props = pillars, specialProps = tree) |
| Fade with the legs | `Assets/Scripts/CornerPropFader.cs` (on every prop; opaque <-> transparent twin + per-slot alpha) |
| MV tree builder | `Assets/BackgroundAssets/CornerTree/Editor/CornerTreeBuilder.cs` (Tools > Corner Props > Build MV Tree) -> `CornerTree/CornerTree_MV.prefab`, `_Mesh.asset`, `Tree_*.mat` + `_Fade` twins, `Tree_LeafFlakes.mat` |

## Rules (user's)

- Sideways turns only. Up/down legs get nothing (but they still COUNT as legs).
- Props go in order, `legsPerProp` (5) legs each, counted from the start of each checkpoint; after the last prop the
  list repeats (until the user gives more assets).
- Each checkpoint will get its own design: `DynamicPathGenerator.cornerPropSets[checkpoint - 1]`; the last element
  carries on for later checkpoints; a null element = no props in that checkpoint. Checkpoint = `GateZoneIndex`
  (PortalsTaken + 1 at lay time).
- Prop stands on a tile = a plane facing up (not a cube). The tile wears the corner cube's own material + property
  block every frame, so it takes the checkpoint look, the fade and the look wipe.
- Keep the model's own colours: the textures are extracted from the glb, not replaced with a project shader.
- No colliders on props or tiles (the runner's forward probe raycasts the Default layer).
- Props FADE EXACTLY LIKE THE LEGS: each frame the prop + tile read the corner cube's drawn renderer (PathDepthShading's
  visual child): alpha = its MPB `_BaseColor.a` (far-ahead faintness, window edge fade, crossed-leg see-through),
  thickness = the child's localScale.x (prop scale and the track-top height follow it). CornerPropFader swaps to
  saved transparent twins (ZWrite on, like the path fade material) only while alpha < 1.
- Special prop (the MV tree): one sideways corner in every `specialEveryMin..Max` (18-22) gets `specialProps[n]`
  instead of the pillar, sized `min(specialHeight 2.4 / h, specialMaxWidth 2.2 / w)` cubes - a bit bigger than pillars.
- No visible popping: prop + tile only drawn while `PathDepthShading.ShownAmount(cornerCube)` > 0, pooled
  (2 instances per prefab warmed at first use).
- Only 3.7 has `spawnCornerProps` on; default false everywhere else. 3.7 has the corner gems off, so the cell is free.
- Do not play-test while another session is working in the editor; wait until the user says "test".

## Builder (what "apply the colours by extracting" means)

1. Reads the glb binary itself: JSON chunk -> material 0 -> texture -> image -> bufferView in the BIN chunk.
2. Saves base colour (sRGB), normal (NormalMap type) and metallic-roughness repacked to URP (R = metal from B,
   A = 1 - roughness from G), all shrunk to `textureSize` (1024).
3. URP Lit material with those maps (+ baseColorFactor, linear -> gamma).
4. Mesh from the glTFast import (the glb must be imported; its node rotation is baked in), positions welded,
   quadric edge-collapse to `targetTriangles` (6000) with UVs kept per triangle corner, re-split on UV seams, smooth
   normals, tangents; pivot = middle of the base.
5. Prefab `Pillar_NN` (+ CornerPropFader with the `_Fade` twin), set `CornerProps_Pillars` (keeps a hand-edited legsPerProp).

Build order: Build Pillars -> Build MV Tree -> Wire Pillars Into 3.7.



The source pillars are 1.5 M triangles and ~85-90 MB each - never put a raw generator model in the game.

## Tunables (DynamicPathGenerator inspector, header "Corner Props")

`cornerPropCell` Outside (tip of the V, straight on from the arriving leg) / Inside (between the legs) -
Size is in path cubes (a leg = 1 cube wide/tall), one uniform scale, fitted in this order: aim `cornerPropHeight` 1.8 ->
no wider than `cornerPropFootprint` 1.0 (the tile) -> no thinner than `cornerPropMinFootprint` 0.55 -> no taller than
`cornerPropMaxHeight` 2.4 (`CornerPropFit`). The 7 pillars land at 0.9-1.8 cubes tall, 0.6-1.0 wide. Props are square
to the track (same rotation as the tile) so square bases stay inside the tile -
`cornerPropTile` / `cornerPropTileSize` 1 - `cornerPropYaw` - `cornerPropShadows` off.

## Adding new props

- More pillars: drop .glb files into `pillars/`, re-run Build Pillars (order = file names; everything in Built is
  overwritten).
- A new checkpoint design: make a folder, generalise the builder's SourceDir / set path (or copy it), build a new
  CornerPropSet, put it in `cornerPropSets[checkpoint - 1]` of 3.7's generator.
- Non-glb models: make a prefab whose pivot is the middle of the base and add it to a CornerPropSet by hand.

## Change log

- 2026-10-10: first version (pillars, 7 glbs, 5 legs each, Outside cell, tile from the corner cube). Not yet
  built or play-tested - the user will say when to test.
- 2026-10-10: size made relative to the legs and consistent across models (user: "not too small or too big"):
  CornerPropFit (target 1.8, max 2.4 cubes tall; 0.55-1.0 cubes wide); props now square to the track, not on the
  diagonal. Still not built or play-tested.
- 2026-10-10: (user: "pillars must disappear and go transparent like the legs") CornerPropFader + per-frame alpha /
  thickness from the corner cube's drawn renderer (also fixed: the tile used to copy the cube's own, DISABLED renderer).
  Added the procedural MV tree (CornerTreeBuilder) as a special prop every 18-22 sideways corners, 2.4 cubes tall.
  Not built or play-tested.
- 2026-10-10: BUILT + WIRED into 3.7 (PathManager: spawnCornerProps 1, cornerPropSets = [CornerProps_Pillars]).
  Builder fixes found on the first real run: (1) reducer stopped at ~175 k tris - its stop check used the live
  triCount, which shrinks when UpdateMesh compacts, while deletedTris keeps the full count -> use startCount;
  (2) pillars lay on their side - glTFast puts the Z-up -> Y-up rotation on the ROOT, so bake mf.localToWorldMatrix
  with the root at the origin; (3) quadric "optimal" point only trusted within one edge length of the edge middle.
  Result: 7 pillars x 6000 tris (~15 k verts), upright, 0.35-0.87 wide x 0.8-1.12 tall model units; tree 2376 tris,
  1.49 x 2.91 x 1.01. Reducer logs "<name> reduce: ..." to the Editor.log. A full Build Pillars takes ~70 s.
  Not play-tested yet.
- 2026-10-10 (play-tested, user: "pillars wayy too small", "add lighting so it glows"): the 3.7 camera shows ~60
  cubes of track, so 1-2 cube props read as specks. Sizes now: pillars aim 4 cubes (max 4.5, width 1-2 cubes),
  tree specialHeight 5.5 / specialMaxWidth 3.5. The TILE now grows with the prop's FOOT (bottom 12 % width + 0.2) and
  spreads away from the track (inner corner pinned to the cell's inner corner, shift along from-nextDir for
  Outside). Glow: pillar materials emit their own base map x CornerPillarBuilder.glow (0.85) + a pooled warm point
  light per drawn prop ("Corner Prop Light" header: colour, intensity 6 x leg alpha, range 1.1 x height + 2, 1.8
  cubes toward the camera, half way up). Tree emission: leaves 0.8, trunk 0.35, cup 0.3.
  NOTE: 3.7's scene serializes the size fields - change them on PathManager, not just the code defaults.
  Test harness that works: EditorApplication.update watcher that sets timeScale 0 and ScreenCapture when an OPAQUE
  prop (material not *_Fade) is inside viewport 0.2-0.8; set specialEveryMin/Max = 2 for the test and restore 18/22.
  RunCommand refuses File.Delete ("user interactions") - write new file names instead.
- 2026-10-10 (user: "could be bigger; plane same size as the cube legs, not stretched, like another cube on a plane,
  not outside bounds"; not tested, token budget): tile is now ALWAYS exactly 1 cube (tileSize 1, no growth, no
  shifting). The prop's FOOT (bottom 12 %) is capped to cornerPropFootCubes (0.95) so it stays on the tile; the crown
  may be wider. Height targets raised: pillars 7 (max 8), tree 9 / width 6 - in practice the foot cap limits them
  (pillars ~3-4 cubes, tree ~4). Values live in 3.7's scene YAML and in CornerProps_Pillars.asset.
- 2026-10-11 (scene 3.8 ONLY; user: "corner pillars still too small, 2.75ish times bigger, sitting in the path, looks so dark,
  glow light should not hit the legs, only the pillars"; not play-tested): new fields `cornerPropSizeMul` (3.8 = 2.75; scales
  prop + tile, tile pinned at the corner point where the Outside cell touches the track and grows along `from` / back against
  nextDir so it never lies over the legs) and `cornerPropGlow` (3.8 = 2.5; CornerPropFader.glow multiplies the materials'
  `_EmissionColor` through the per-slot MPB - lights only the prop). 3.8's point light is OFF (`cornerPropLight: 0`) because
  it lit the legs. Defaults (1 / 1) leave 3.7 and every other scene unchanged.
- 2026-10-11: user said 2.75 was too big/out of bounds -> 3.8 cornerPropSizeMul now 1.6.
- 2026-10-11: user: plane must never exceed the cube -> 3.8 cornerPropSizeMul (1.6) now stretches the prop's height only; tile 1 cube, foot width unchanged. Not tested.
