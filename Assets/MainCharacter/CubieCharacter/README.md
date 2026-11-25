# CubieCharacter (Unity drop-in)

Cubie: fluffy coral CUBE girl (Leo's Fortune style), ~1x1x1 m with fur, teal brows, emerald eyes, mint flower clip.

Drag this whole `CubieCharacter` folder into your Unity project's `Assets/`.
On first compile the editor script sets everything up automatically and creates:

- `Materials/` – M_Cubie_FurMain, M_Cubie_FurAccent (alpha-cutout), M_Cubie_Skin, M_Cubie_Bow, M_Cubie_Gloss
  (auto-picks Built-in / URP / HDRP shader)
- `Animations/Cubie.controller` – Animator Controller with all clips wired
- `Prefabs/CubieCharacter.prefab` – drop this into a scene, pivot = contact point on the track

If anything is missing (or after updating from an older version), run **Tools ▸ Cubie Character ▸ Run Setup**.

## Recolouring
Fur, brows, skin and bow are painted grayscale – **the material Base Color is the character's colour**.
- Editor: change Base Color on `M_Cubie_FurMain` (fur), `M_Cubie_FurAccent` (brows), `M_Cubie_Skin` (lids – keep a bit darker than fur), `M_Cubie_Bow` (flower petals).
- Runtime / per instance: `CubieColors` component (tick *Apply*), or from code:
  ```csharp
  GetComponent<Cubie.CubieColors>().SetFur(Color.green);
  ```
Eyes and the flower centre (`M_Cubie_Gloss`) keep fixed colours.

## Contents
| Path | What |
|---|---|
| `Models/Cubie.fbx` | Skinned mesh (~25k tris, 5 materials), 37-bone Generic rig, 8 clips |
| `Textures/T_Cubie_Atlas.png` | 1024 atlas (grayscale fur strands + skin, coloured eye/lash/glasses) |
| `Scripts/Runtime/CubieFurPhysics.cs` | Spring physics for fur, tuft, flower + ground collision – works on any clip |
| `Scripts/Runtime/CubieFace.cs` | Eye look target, lid open/close, brow angry/raise, mouth open/wide/tremble sliders |
| `Scripts/Runtime/CubieColors.cs` | Runtime per-instance recolour |
| `Scripts/Runtime/CubieBlinker.cs` | Random blinks via the additive Blink layer |
| `Scripts/Editor/CubieCharacterSetup.cs` | Import settings + materials/controller/prefab builder |
| `Source~/` | Blender source + build script (ignored by Unity) |

## Clips (30 fps) – all authored for running along a track toward the character's forward (+Z)
Run_InPlace_v2 (96f loop, main run) · Run_InPlace (48f rolling gallop loop) · Idle (72f loop) ·
Jump_Start (12f) · Jump_Air (24f loop) · Land (18f) · Smash (26f) · Blink (8f, lids only, additive)

She sits on the track: the pivot is the ground contact, squash/stretch never lifts off or sinks in,
and bottom fur splays along the surface on contact/impact instead of going through it.

## Animator parameters
- `Speed` (float) > 0.1 → Run, otherwise Idle
- `Jump` (trigger) → Jump_Start → Jump_Air, then set `Grounded` true → Land
- `Grounded` (bool) – set false when airborne, true on landing
- `Smash` (trigger) → ground pound
- `Blink` (trigger) – fired automatically by CubieBlinker

## Runner game hookup
```csharp
var fur = GetComponent<Cubie.CubieFurPhysics>();
fur.externalVelocity = transform.forward * runSpeed; // in-place runner: world scrolls past
fur.grounded = isGrounded;                           // fur may hang below her while airborne
animator.SetFloat("Speed", runSpeed);
animator.SetBool("Grounded", isGrounded);
```
Add a Trail Renderer / speed-line particles behind her for extra speed.

## Rig
Root ▸ Body (squash/stretch, pivot on ground) ▸ Body_Upper (lean/turn) ▸
Eye_L/R, Lid_Upper_L/R, Lid_Lower_L/R, Brow_L/R, Face_Front (look anchor), Bow (flower clip), Tuft_01-03, Fur_00..19.
