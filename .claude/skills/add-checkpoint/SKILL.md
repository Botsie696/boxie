---
name: add-checkpoint
description: Bring a placeholder checkpoint (5-10) of the cube-rail runner to life - give it its own look, map it to its portal / leg, make its MainUi band live (diorama, menu shot, darkness, unlock celebration) and verify it. Use when the user says "add checkpoint 5", "make checkpoint N look like <scene>", "give portal 4 its own look", "a new band in the menu", or asks how checkpoints, the checkpoint menu or start-from-checkpoint work.
---

# Add a checkpoint (MainScene prop camera 3.6 + MainUi)

Checkpoint 1 = run start. Checkpoint N (N >= 2) = the runner came out of **portal N - 1**. Checkpoints 1-4 are real
(default, Crimson, Raspberry, Jade Moon Garden); 5-10 are placeholders: their portal changes nothing, their menu band
shares one dim placeholder world. **Every checkpoint needs its own look** - never reuse another checkpoint's look.

Hard rules: `MainScene prop camera 3.5.unity` is the user's backup - never open-and-save it, never change an asset only it
uses. Only `MainScene prop camera 3.6.unity` and `MainUi.unity` are edited (`MainUi_backup.unity` stays untouched).
Pool everything, no per-frame allocations, unscaled time for UI, nothing invisible blocks touches, colours: yellow =
good/guidance, red = danger, white = neutral. Keep play-tests short (stop well before score 50 unless the user asks).

## Where everything lives

| What | Path / class |
|---|---|
| Checkpoint data (single source of truth) | `Assets/Checkpoints/Profiles/CP05_Placeholder.asset` ... `CP10_Placeholder.asset` - `CheckpointProfile` (`Assets/Checkpoints/Scripts/CheckpointProfile.cs`) |
| The ordered set + 3.6-only portal spacing | `Assets/Checkpoints/CheckpointSet_3_6.asset` - `CheckpointSet` (`legsBetweenPortals`, `BuildRuntimeSequence()`) |
| Looks (palette, sky, fog, sun, ambient, post) | `Assets/PropCameraLooks/LookTransition/Looks/Look_<scene>.asset` - `AtmosphereLook` |
| Portal look change at run time | `LookTransitionDirector.checkpoints` (3.6) builds its sequence from the set at Awake |
| Profile / set builder | `Assets/Checkpoints/Editor/CheckpointSetup.cs` - menu **Tools > Checkpoints > Build 3.6 Checkpoint Set** (+ `ApplyDefaultShots`) |
| Look capture | `Assets/PropCameraLooks/LookTransition/Editor/LookTransitionTools.cs` - `CaptureToAsset(scenePath)` (opens additively, closes unsaved) |
| Checkpoint start / unlock / snapshot / wallet | `Assets/Checkpoints/Scripts/CheckpointRunController.cs` (object "CheckpointRun" in 3.6), `DynamicPathGenerator.Checkpoint.cs`, `PortalZoneController.CrossFromCheckpoint`, `LookTransitionDirector.JumpToPortal / RequestRunwayFor` |
| Save | `SaveSystem` / `SaveData` / `CheckpointSnapshot` (`Assets/Checkpoints/Scripts/`), file `persistentDataPath/save.json` |
| Menu | `Assets/Checkpoints/Menu/MainMenuController.cs` (layout, darkness, taps, launch, celebration), `MenuDioramaRig.cs` (dioramas, per-camera look), `CheckpointBandGraphic.cs` + `Shaders/CheckpointBand.shader` |
| Menu builder | `Assets/Checkpoints/Editor/MainMenuBuilder.cs` - **Tools > Checkpoints > Build MainUi Menu** (re-runnable) |
| Ambient particles | `Assets/Checkpoints/Particles/Motes_CPnn.prefab` (made by the menu builder for every profile with a look) |
| Dev screenshots | `UICapture.Capture(w, h, safe, path)` (Play mode; `UICapture.IPhoneSafe/AndroidSafe`) |

## Steps

### 1. Make the look
1. The user names a source scene (or you build one with the `claudeEnvironmentCreation` skill - a new "MainScene prop camera N"
   scene; never edit existing look scenes).
2. Capture it: `LookTransitionTools.CaptureToAsset("Assets/<Scene>.unity")` -> `Look_<Scene>.asset` (+ its Volume profile copy
   in `Looks/Profiles`). This is the same data the portal wipe and the menu band both use. Check `hasRole`, `hasSky`, `hasChasm`.

### 2. Fill the profile (`CPnn_*.asset`)
- `index` (already set), `displayName` (one or two words, shown upper-case on the band), `look` = the new look.
- `legIndex` is nominal = (index - 1) x `CheckpointSet.legsBetweenPortals` (written by the setup tool; the exact leg of a real
  run is in the save's snapshot). Portal k is laid at serial k*L - 1, crossed at score k*L - 1, the runway out scores k*L.
- Portal transition: `label` (empty = "CHECKPOINT N"), `cameraCinematic` (true), `keepObstacle` (true keeps the pyramid's
  material through the door - turn off if the new look's obstacle reads well), `takePlankColor`, timing (leave default).
- `applySunRotation` (menu only), `skyboxMaterial` / `fog` (leave empty / off: these scenes fog in their shaders),
  `ambientParticlePrefab` (left empty, the menu builder makes `Motes_CPnn` from the look's door-glow colour).
- `lockedTeaseColor` = a hint of the look (the menu builder defaults it to the look's `doorGlowColor`).
- `menuCamera`: pick a shot NOT used by the bands next to it (motions: `HighOrbit`, `LowSideTrack`, `TopDownPushIn`,
  `TelephotoPan`, `Static`). Offsets are relative to the loop centre (loop is 12 x 10 units); `motionSpeed` is cycles per
  second (0.025-0.04 feels calm), `motionAmplitude` degrees (orbit/pan) or units (side track) or % (push-in). Proven values:
  HighOrbit (0,13,-15) fov 42 amp 35; LowSideTrack (0,3.5,-12.5) fov 46 amp 5; TopDownPushIn (0,21,-5) fov 40 amp 18;
  TelephotoPan (0,20,-48) fov 15 amp 10. Vary the yaw (angles.y) so neighbours never look alike.
  Rename the asset file from `CPnn_Placeholder` to `CPnn_<Name>` (keep the GUID: rename, don't recreate).

### 3. Bake the menu legs, then rebuild
- The menu bands show the first 5 legs a real run lays at each checkpoint, baked from 3.6 with the real generator
  (fixed seed per checkpoint): open `MainScene prop camera 3.6`, enter Play, run `MenuSliceBaker.Bake()` (Unity_RunCommand),
  wait for `[MenuSliceBaker] baked N slices`, stop Play. Output: `Assets/Checkpoints/Menu/MenuSlices.asset` (`MenuSliceData`:
  cubes, route, exit tower, the gameplay camera pose). Re-bake whenever legsBetweenPortals, leg rules or the camera feel change.
- A checkpoint unlocks at its portal OR when a run reaches its score X = `CheckpointSet.NominalLeg(N)` (CheckpointRunController);
  the menu reads the same X for "SCORE X" and the locked card, and unlocks anything the saved best already reached.
- **Tools > Checkpoints > Build MainUi Menu** (makes the mote prefab, wires everything; MainUi only).
- Nothing else is needed: `MenuDioramaRig.Build` makes a diorama for every profile with a look, `MainMenuController` picks the
  darkness from `darkness` (AnimationCurve: distance from the frontier -> brightness; last `silhouetteCount` locked bands are
  black silhouettes), and the unlock celebration plays from `SaveSystem.NewlyUnlocked` automatically.
- Gameplay picks the profile up at the next Play (the director builds its sequence from the set at Awake).

### 4. How the menu swaps the look per camera (for debugging)
`MenuDioramaRig.OnBeginCamera` (RenderPipelineManager.beginCameraRendering) pushes, for the band camera being drawn:
`_LTA/_LTB` (block roles), `_LTSkyA/_LTSkyB` (sky), `_LTWipe` idle, ChasmFog globals (`_ChasmFogTop/Bottom/Glow/GlowShape/
Height/Depth/Wind/Screen/View/ViewPos/House`), `_PropFogRefY`, sun colour / intensity (and rotation if `applySunRotation`),
ambient colours, and the Volume's profile; `OnEndCamera` puts the menu's own values back. Arrays are packed once (no
allocation). The fog backdrop is on layer 30 (`MenuDioramaRig.BackdropLayer`) so only band cameras draw it.

### 5. Performance budget (target 60 fps on a mid-range phone)
- Band RTs: 1080 x 700 x `renderScale` 0.5, no MSAA, no shadows, no depth/opaque texture; post on (turn `postProcessing` off
  on the rig if a device struggles).
- Render rates (MainMenuController.UpdateFrame): visible unlocked every 2nd frame, locked every 3rd, black bands (darkness 0,
  i.e. 4+ past the frontier) never; off-screen bands not at all; the shop open = none. `MenuDioramaRig.maxCamerasPerFrame` (4) caps
  cameras per frame. Measured in the editor: 16.7 ms/frame, 0 B GC, max 4 cameras.
- Menu per-frame code must stay at 0 B GC: `MainMenuController.DevAllocBytes / DevFrames` (editor, dev builds).

### 6. Verify (keep it short)
1. Compile: no console errors.
2. 3.6 normal run with a short portal spacing at runtime only (`gen.legsBetweenPortals = 3; ctl.RestartAt(1)` + AutoPlayer):
   the log line `[LookTransition] portal <N-1> -> '<Look>'` and a screenshot after the wipe.
3. Start from the checkpoint: `CheckpointRunController.RestartAt(N)` or the real path `GameLaunchRequest.Set(N);
   SceneManager.LoadScene(GameLaunchRequest.GameScene)`; at the crossing: score = (N-1)L - 1, speed = `SpeedForLeg` of it,
   then `CameraCinematicActive`, `inputSuspended`, speed factor 0.85, label "CHECKPOINT N". The ContextMenu
   **Dev/Test start from every checkpoint** on CheckpointRun writes `persistentDataPath/checkpoint_start_test.txt`; mismatches
   against a normal-run snapshot are logged as `[Checkpoint] mismatch ...`.
4. Menu screenshots with a test save (`SaveSystem.UseFile("save_menu_test.json")`, set `highestCheckpointUnlocked` /
   `newlyUnlockedCheckpoint`, reload MainUi): `UICapture.Capture(1080,1920,...)` and `(1170,2532, UICapture.IPhoneSafe(...))`
   - the band is lit when unlocked, darkens by distance when locked, the celebration plays once.
5. Prove 3.5 untouched: `sha256sum -c .baseline_3_5/hashes_2026-10-03.txt` from the project root - only files you meant to
   change may differ, never `MainScene prop camera 3.5.unity` or a look/sequence asset 3.5 uses.
6. Switch back to the real save (`SaveSystem.UseFile(null)`) and delete test saves/screenshots you made.

## Within-level leg stages (scene 3.10)
After a new checkpoint has its look, give it its 3 in-level leg colours + obstacle colours + obstacle-runway design with the `within_level_legs_Design` skill (LegStagePalette level index = portals crossed). Only legs change; never the environment.

## 3.10 checkpoints so far
Flower = 6 (`Assets/FlowerCheckpoint`), Voxel "Brick Meadow" = 7 (`Assets/VoxelCheckpoint`), Pencil "Sketchbook" = 8 (`Assets/PencilCheckpoint`, builder Tools > Pencil Checkpoint; Raspberry 9, Jade 10). The 3.10 set order is owned by `PencilCheckpointBuilder.RebuildSet310()` (Voxel's delegates to it); a new checkpoint should extend it the same way (default-off `_XxLook` hooks in FlatFaceBlockPixel / PixelRockLitForwardPass / PixelFaceTrail, controller with a later DefaultExecutionOrder).
