---
name: portal-look-transition
description: Makes a portal in the cube-rail endless runner (Unity, URP) change the whole atmosphere - lighting, block materials, sky, fog, rocks, post-processing - into another scene's look, with a left-to-right wipe that starts as the runner enters the door, a camera move behind the runner, and a small "CHECKPOINT N" label, all in one scene with no load. Use when the user says things like "at portal 2 make it look like MainScene prop camera 3", "after the first portal transition into scene 1's vibe", "add a checkpoint look change", or names a .unity scene a portal should turn into.
---

# Portal look transition

At portal N the running scene changes into the look of a target scene, while the game keeps going. The end state must match the target exactly.

## Inputs (infer; ask only if missing)

| Input | Default |
|---|---|
| Working scene | `Assets/MainScene prop camera 3.2.unity` (already has the system) |
| Base look (before portal 1) | read from the working scene at runtime (3.2 = copy of 3.1) |
| Portal N | - |
| Target scene | - |
| Label | `CHECKPOINT {N}` |

If the working scene doesn't exist, duplicate the base scene (AssetDatabase.CopyAsset). Never edit the base or target scene.

## Hard rules

1. Source scenes and every asset they reference are read-only. Copies go in `Assets/PropCameraLooks/LookTransition/`. No git here: hash files before/after (SHA-256) to prove nothing else changed.
2. Shared scripts get additive hooks only (defaults = today's behaviour). Never edit the shared FlatFaceBlock / ChasmFog shaders; the system uses its own copies.
3. Only the look changes. Gameplay stays the working scene's, except the reported runway fix.
4. Pool everything, 0 B GC per frame, cached property IDs, UI on unscaled time, raycastTarget off.
5. Before portal 1 the working scene must render identically to the base scene.
6. User preferences: 3.2 defaults to camera mode E (D must work too); never zoom tighter than the authored ortho size; rocks/houses never pop in view; test runs stop before score 50.

## Step 1 - System installed?

Look for `Assets/PropCameraLooks/LookTransition/` and a `LookTransitionDirector` in the working scene. It exists for 3.2 (built 2026-10-02). If missing, read `references/system-setup.md` and rebuild it.

## Step 2 - Survey (report, then continue unless a role mapping is ambiguous)

`LookTransitionTools.Survey(beforeScenePath, targetScenePath, true)` (RunCommand) gives a markdown table: material roles, sky, ChasmFog, sun, ambient, camera bg, post overrides, path tint, exit portal, runner accents, hazards. Also flag:
- hazards losing contrast (pyramid, planks, coins, power-ups) - ask, never recolour silently;
- out-of-scope differences (CameraFeel, DifficultyProfile, obstacles, controls, houses/NeonWindowDecorator, renderer).

## Step 3 - Capture and register

Open the working scene, then `LookTransitionTools.Install(targetScenePath, N)`: captures base + target into `Looks/Look_<scene>.asset` (profiles copied to `Looks/Profiles/`), writes/updates the entry in `Sequence_MainScenePropCamera3_2.asset`, sets up the director and saves the working scene only. Entry options: `cameraCinematic`, `keepObstacle`, `takePlankColor`, `label`, timing override. Later portals chain (after portal 1 the target look is the "before").
Note: Install currently hard-codes 3.2 as the working scene and 3.1 as the base; adjust if the user names others.

## Step 4 - Runway timing (always compute and report)

v = runner speed at the exit leg (DifficultyProfile curve: v(31) ~ 17.8 u/s), L = exit runway cubes, T = L / v.
Timeline from the door (teleport frame): A follow-through 0.8, B hold 1.6, C rise back 1.0, D read time >= 1.5 (user wants >= 1.5), settle ends 7.0.
The wipe + label start AT THE DOOR (t = 0) and run A+B (user's choice); lights/ambient/fog settle after it; post fades 0 -> 7 s.
Fix order if T is short: (1) lengthen the exit runway (director sets `exitRunwaySecondsOverride`, framing capped to `portalRunwayCubes`), (2) phases down to minimums, (3) runner speed >= 85% during A-B. Portal 1 needed 85 cubes (3.1 had 30) - past the skill's 1.5x cap; the user approved it. If nothing fits, stop and ask.

## Step 5 - Verify (Unity MCP, Play mode, runs < score 50)

Use an `EditorApplication.update` harness from `Unity_RunCommand` (enter Play first with `Unity_ManageEditor`; never `File.Delete` in RunCommand - it is refused). Check:
- identity before the portal: pause, screenshot, swap runtime materials' wipe shaders to the originals, screenshot, compare pixels with post-processing disabled (only unscaled-time FX may differ);
- shots across the wipe; runner viewport stays inside ~0.2-0.8; rock pop-in/out/shrink = 0 (monitor `PathBackground` renderers in the frustum);
- end values = target exactly (`Shader.GetGlobalVectorArray("_LTA")` vs packed target materials, sun, ambient, ChasmFog, volume);
- director `LastReadTime` >= 1.5 s; taps dropped during the cinematic;
- kill (`HitObstacle`) + `Restart` before / mid-wipe / after: base look back, speed factor 1, inputs on, timeScale 1;
- `TransitionAllocatedBytes` = 0; modes D and E; several exit headings;
- final hash check: only new files, the working scene and the hook files changed.

## Step 6 - Report

Files added/changed (hook lines), survey table, v / L / T and runway fix, final timings, hazard warnings, anything not matched, anything not verified.
