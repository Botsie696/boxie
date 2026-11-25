---
name: within_level_legs_Design
description: Make each level (checkpoint) of the cube-rail runner feel like it progresses by changing ONLY the leg colours in thirds of the level (stage 0 = the look as authored, ~33 % stage 1, ~66 % stage 2), plus per-level obstacle-leg runway designs and non-yellow obstacle colours per stage. Scene "MainScene prop camera 3.10" only. Use when the user says "change the leg colours within a level", "make the level feel progressive", "add stage colours for checkpoint N", "colour the obstacles per checkpoint", or adds a new checkpoint that needs its 3 leg stages.
---

# Within-level leg design (3.10)

Background, rocks, lighting, fog, post and the runner NEVER change here - that is the checkpoint / look-transition
work (`add-checkpoint`, `portal-look-transition`). This skill only re-hues the legs (and the obstacle-leg blockers).

## How the progression should feel (design rules)
- **Three steps per level** (rule of three): base -> step 1 -> step 2, then the next portal changes the whole world.
  Each step must be visibly different in hue, but keep the level's theme (same family / temperature story).
- **Monotonic story**: each step moves one way (warmer, or lighter, or richer) so the player reads "further in", e.g.
  Start grey -> sandstone -> terracotta (warming toward the crimson level); Crimson red -> dusty pink -> amber gold.
- **Never seen changing**: a leg takes its stage when it is LAID (far ahead, out of sight) and keeps it. The road
  ahead arrives in the new colour, the runner crosses into it at a corner, and a pale threshold line circles the first
  cube of the new stage (`thresholdLine`). No global swap of on-screen legs (that reads as a glitch).
- **Contrast first**: legs must never read as sky/chasm/backdrop and must not match the runner (3.9/3.10 runner =
  yellow totem, so no lemon-yellow legs; the gold stage is pushed toward amber). Check against the level's sky top /
  mid / chasm colours (`AtmosphereLook.sky`, `chasmTop/Bottom`). Pixel level: colours must be IN the pixel palette
  (`PixelCheckpointController.DefaultPalette`) or the palette snap will move them.
- **Obstacles**: never yellow; a different colour per stage, contrasting that stage's legs (complementary-ish).
- **Obstacle legs** (portalRole 3) take the colour of the stage they are laid in (same face/pick as the path legs
  there) and keep only the level's runway diagonal stripes (`obstacleRunway.stripes`) so the section still reads.

## Files
- `Assets/LegStages310/Scripts/LegStagePalette.cs` - ScriptableObject; `FillDefaults()` holds the tuned colours
  (levels 0-5 = Start, Crimson, Toon, Pixel, Storybook, Flower). Asset: `Assets/LegStages310/LegStagePalette_3_10.asset`.
- `Assets/LegStages310/Scripts/LegStageColors.cs` - on object "LegStageColors" in 3.10. Scans the laid legs every
  0.12 s, works out level/stage from the leg serial (`DynamicPathGenerator.CheckpointLeg(k)` = portal k's runway out;
  stage = thirds of [portal k, portal k+1)), writes per-renderer property blocks. `StageOf(serial, ...)` is public.
- Shader: `Assets/PixelCheckpoint/Shaders/FlatFaceBlockPixel.shader` (the 3.9/3.10 leg shader) has a default-off
  block - per-cube `_LegStageFace` (face re-hue, keeps every face's luminance share of the top = same shading),
  `_LegStageSel` / `_LegStageSel2` (hue pick: move one hue band - Pixel moss, Storybook roofs - plus stripes and the
  threshold line). All zero in every other scene, so nothing changes there.
- Obstacle legs: `DynamicPathGeneratorObstacleLeg.cs` `obstacleLegTriangleChance` (0 = off, 3.9; 0.4 in 3.10) makes
  a station's two blockers triangle spikes (`ObstacleTriangle`, size `obstacleLegTriangleSize`) instead of pillars.

## Two ways to colour a stage
- `face` (flat looks: Start, Crimson, Toon): the top becomes `face`; sides keep their luminance ratio. Toon moss/ink
  are painted after, so they survive.
- `pickFrom` -> `pickTo` + `pickWidth` (textured looks: Pixel, Storybook): pixels near pickFrom's hue move to pickTo's
  hue, saturation x (to/from), value x (to/from). Neutral stone/plaster is untouched (saturation gate).
- Stage 0 keeps the look (face/pick alpha 0) - only its `obstacle` colour is used. Don't change existing look colours.

## Add a level (e.g. checkpoint 6) or retune
1. Read the level's look: block top/sides, sky, chasm (RunCommand over `CheckpointSet_3_10.asset` - 3.10 has its own set since the flower checkpoint, gamma hex).
2. Pick 2 new stage colours + 3 obstacle colours + a runway design following the rules above.
3. Add a `Level` in `LegStagePalette.FillDefaults()` (index = portals crossed), then re-run FillDefaults on the asset
   (RunCommand: load asset, `FillDefaults()`, SetDirty, SaveAssets) - or edit the asset in the inspector.
4. Test ONCE per critical case (user wants minimal testing): Play 3.10, runtime copy of the palette with
   `stageAt = (0.04, 0.1)` and the generator's `min/maxLegsBetweenObstacleLegs = 3/4` (runtime only, never saved),
   then `PixelTestHarness.Run(checkpointIndex, dir, 10f, 18f)`; read downscaled shots. Checkpoint index = level + 1.
   Reset `Time.timeScale = 1` before a test; stop Play after.

## Gotchas
- Path cubes use the director's block shader at runtime (`PixelCheckpoint/FlatFaceBlockPixel` in 3.9/3.10); a
  renderer is painted only if its material has `_LegStageFace` + `_LTRole`. Blockers are found by name
  (`ObstaclePole` / `ObstacleTriangle` ancestors); look-family blocker materials (role 3) get a face re-hue, others
  `_BaseColor`.
- PathDepthShading / ToonLegDesigns read the property block before writing, so all three coexist.
- 3.10 only: never add LegStageColors to another scene; the shared shader/generator changes are default-off.

## Obstacle trees (replace the totem pillars, 3.10)
- `obstacleLegTrees` on the generator (scene 3.10 = on, size 0.85 x 1.7 cubes): each pillar is a flat-shaded tree
  (`TreeTrunk` + `TreeCrown`, meshes in `ObstacleTreeMesh`, bottom of DynamicPathGeneratorObstacleLeg.cs). Triangle spikes stay.
- `LegStagePalette.TreeStyle(level, stage, ...)` = crown shape per level (0 stepped blocks, 1 ball, 2 cone, 3 diamond),
  trunk colour and 3 bright leaf colours (one per stage, never yellow, Pixel ones in the pixel palette). Material = the
  look's own role-3 shader, so each level's shading (ink, pixel snap, watercolour) comes for free. New level = add a row there.
