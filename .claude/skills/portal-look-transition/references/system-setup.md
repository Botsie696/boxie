# LookTransition system (as built for MainScene prop camera 3.2)

Everything lives in `Assets/PropCameraLooks/LookTransition/`.

## Files
- `Scripts/AtmosphereLook.cs` - SO with every look value. `PackBlock` (16 float4 per role, linear colours) and `PackSky` (8 float4); `Capture(Sources)`; `Describe` for the survey. Roles: 0 block, 1 block fade, 2 player, 3 obstacle.
- `Scripts/CheckpointLookSequence.cs` - SO: entries per portal (look, label, cameraCinematic, keepObstacle, takePlankColor, timing). `Timing` holds A/B/C/D, settleEnd, speedFloor (>= 0.85), speedEaseIn, runwayMargin, wipeDelay (0 = at the door), wipeDuration (0 = to the end of B); `RunwaySecondsAtFullSpeed`.
- `Scripts/LookTransitionDirector.cs` - runtime, `[DefaultExecutionOrder(-1000)]`.
- `Editor/LookTransitionTools.cs` - Tools > Look Transition: Survey Scene, Capture Look From Scene, Install In Open Scene. Capture opens a scene additively, reads it, closes it unsaved and checks it was not dirtied and its file hash did not change; RenderSettings are parsed from the scene YAML.
- `Shaders/` - `FlatFaceBlockWipe` (copy of PropLooks/FlatFaceBlock; look params from `_LTA[128]`/`_LTB[128]` by `_LTRole`), `RadialSkyWipe` (`_LTSkyA/B[8]`), `ChasmFogLitWipe` + `LookChasmFogLitForwardPass.hlsl` (rocks), `ChasmFogBackdropWipe`, `LookChasmFog.hlsl` (copy of ChasmFog.hlsl, same include guard), `LookWipe.hlsl` (`_LTWipe` = x edge, softness, slant, active flag).
- `Looks/Look_<scene>.asset`, `Looks/Profiles/<scene>_Volume.asset`, `Sequence_MainScenePropCamera3_2.asset`.

## How it works
- **Materials**: at Awake the director makes runtime copies of the scene's look materials on the wipe shaders and swaps them into every renderer and field (generator obstacle, PathDepthShading fade, PortalZoneController block/sky, MonumentSkyBackdrop sky). Assets are never written. Copies made later (PathDepthShading lit/fade/ghost, sky quad) keep `_LTRole`, so they follow too.
- **Slots**: slot A = look in force, slot B = next look, picked per pixel by the wipe. At commit A := B and the wipe goes idle (no visible change).
- **Globals that can't split per pixel** (sun, ambient, ChasmFog colours, camera bg): held during the wipe while the shaders add "next minus current" (`_LTSunD`, `_LTAmbD`, `_LTChasm*D`) on the wiped side; afterwards the real values ease to the target while the difference shrinks to 0. Rocks get the sun/ambient difference through URP's diffuse terms.
- **Post**: a second global Volume (priority +1) fades 0 -> 1; at commit the base Volume takes the target profile.
- **Accents** (path tint gradient, travelled, trail/boost colours as PortalZoneController derives them, face highlight, arrows, planks, obstacle glow, exit door stone/glow) ease with the wipe.
- **Camera**: `CameraParentRotator.CameraOverride` (end of LateUpdate). Each frame it reads the live mode pose and blends with a chase pose (yaw = runway heading +/-35, tilt 26 >= rock guardPitch 24, ortho never below the authored size). The runner's SCREEN position is blended (live -> centred low), so it never leaves the frame. Weights: A smoother in, B hold, C smoother out. During it: `SuppressYawChanges`, `PortalZoneController.freezeDoorView`, `CubeRailRunner.inputSuspended`, `CinematicSpeedFactor` (only while the camera is owned).
- **Restart**: `PortalZoneController.OpeningRestored` -> base look instantly, everything released. A death releases the camera at once.
- **Label**: own overlay canvas (no raycaster), SafeArea, top-centre under the score, RectMask2D padding follows the wipe edge, then holds and fades on unscaled time.

## Additive hooks (defaults = old behaviour)
- `CameraParentRotator.cs`: `event CameraOverride`, `AuthoredOrthoSize`, `SuppressYawChanges`; `.Motion.cs`: `RailRotationBlocked` returns true while suppressed.
- `PlayerController.cs` (CubeRailRunner): `CinematicSpeedFactor` in MoveSpeed, `inputSuspended` checked in `Enqueue`.
- `PortalZoneController.cs`: `DoorCrossed(int)`, `OpeningRestored`, `ExitDoor`, `EntryDoor`, `freezeDoorView`.
- `DynamicPathGeneratorPortal.cs`: `exitRunwaySecondsOverride`, `exitRunwayFrameCubes`, `ExitRunwayCubes()` (used by PlanIsland, PlanClassicExit, FrameBothStructures); `DynamicPathGenerator.cs` exit lay calls `ExitRunwayCubes()`.

## Scene install (working scene only)
`LookTransition` GameObject with the director (sequence, shaders, base materials, scene refs); the scene's ChasmFog `fogLitShader` / `backdropShader` point at the wipe copies; `cameraMode: 4` (E) in 3.2.

## Gotchas
- New .cs files: `AssetDatabase.ImportAsset(path, ForceUpdate)`; don't save scripts while a Play-mode harness runs (recompile kills it).
- Identity tests are only valid before the first transition (after it, slot A holds the target while the copies' own props are still base).
- Film grain and unscaled-time FX (coins, arrows, diamond, trail) differ between any two captures; disable Volumes for pixel tests.
- The exit tower and trail colours fade with wipe progress rather than per pixel.
