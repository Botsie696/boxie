# BoxiCharacter (Unity drop-in)

Drag this whole `BoxiCharacter` folder into your Unity project's `Assets/`.
On first compile the editor script sets everything up automatically and creates:

- `Materials/` – M_Boxi_Fur (alpha-cutout), M_Boxi_Body, M_Boxi_Gloss (auto-picks Built-in / URP / HDRP shader)
- `Animations/Boxi.controller` – Animator Controller with all clips wired
- `Prefabs/BoxiCharacter.prefab` – drop this into a scene

If anything is missing, run **Tools ▸ Boxi Character ▸ Run Setup**.

## Contents
| Path | What |
|---|---|
| `Models/Boxi.fbx` | Skinned mesh (~18k tris, 3 materials), 37-bone Generic rig, 8 clips |
| `Textures/T_Boxi_Atlas.png` | 1024 atlas (fur strands, eye, skin, swatches) |
| `Scripts/Runtime/BoxiFurPhysics.cs` | Spring physics for fur, tuft, bow, glasses – works on any clip |
| `Scripts/Runtime/BoxiFace.cs` | Eye look target, lid open/close, brow angry/raise sliders |
| `Scripts/Runtime/BoxiBlinker.cs` | Random blinks via the additive Blink layer |
| `Scripts/Editor/BoxiCharacterSetup.cs` | Import settings + materials/controller/prefab builder |
| `Source~/` | Blender source + build script (ignored by Unity) |

## Clips (30 fps)
Run_InPlace_v2 (96f loop, main run) · Run_InPlace (48f bouncy loop) · Idle (72f loop) ·
Jump_Start (12f) · Jump_Air (24f loop) · Land (18f) · Smash (26f) · Blink (8f, lids only, additive)

## Animator parameters
- `Speed` (float) > 0.1 → Run, otherwise Idle
- `Jump` (trigger) → Jump_Start → Jump_Air, then set `Grounded` true → Land
- `Grounded` (bool) – set false when airborne, true on landing
- `Smash` (trigger) → ground pound
- `Blink` (trigger) – fired automatically by BoxiBlinker

## Runner game tip
The character runs in place. Feed the scroll speed to the fur so it streams back:
```csharp
GetComponent<Boxi.BoxiFurPhysics>().externalVelocity = transform.forward * runSpeed;
```
Add a Trail Renderer / speed-line particles behind her for extra speed.

## Rig
Root ▸ Body (squash/stretch, pivot on ground) ▸ Body_Upper (lean/turn) ▸
Eye_L/R, Lid_Upper_L/R, Lid_Lower_L/R, Brow_L/R, Glasses, Bow, Tuft_01-03, Fur_00..19.
