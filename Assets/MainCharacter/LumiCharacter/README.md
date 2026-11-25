# LumiCharacter - Unity drop-in

**Lumi**: glowing amber fluffy cube girl (~1 x 1 x 1 m) - plush fur faces, thin glowing gold fur on all 12 edges and 8 corners,
big teal eyes set into the fur, glowing teal brows, subtle smug smirk, black hair cap + ponytail with a teal hair tie.
Rigged (40 bones), 9 animation clips, spring physics for fur + ponytail, face controls, per-instance colours.

## Install (2 steps)
1. Copy this whole `LumiCharacter` folder into your Unity project's `Assets/` folder.
2. Wait for compile. The editor script builds the materials, Animator Controller and prefab automatically.
   (If anything is missing: **Tools > Lumi Character > Run Setup**.)

Then drag **`Assets/LumiCharacter/Prefabs/LumiCharacter.prefab`** into a scene. Pivot = ground contact point.

## What gets created
| Path | What |
|---|---|
| `Models/Lumi.fbx` | Skinned mesh (~58k tris), Generic rig, 9 clips (import settings applied automatically) |
| `Textures/T_Lumi_Atlas.png` | 1024 atlas: grayscale fur strands / skin, coloured eye + lashes |
| `Materials/` (generated) | `M_Lumi_FurMain`, `M_Lumi_FurGlow` (emissive), `M_Lumi_FurAccent` (emissive brows), `M_Lumi_Skin`, `M_Lumi_Hair`, `M_Lumi_Bow` (hair tie), `M_Lumi_Lip`, `M_Lumi_Gloss` (eyes) |
| `Animations/Lumi.controller` (generated) | Animator Controller, all clips wired, additive Blink layer |
| `Prefabs/LumiCharacter.prefab` (generated) | Model + Animator + `LumiFurPhysics` + `LumiFace` + `LumiColors` + `LumiBlinker` |
| `Scripts/Runtime/*.cs` | Runtime components (below) |
| `Scripts/Editor/LumiCharacterSetup.cs` | Import settings + material / controller / prefab builder |
| `Source~/` | Blender source + build script (Unity ignores folders ending in `~`) |

Materials auto-pick Built-in / URP / HDRP shaders. Fur materials are alpha-cutout (cutoff 0.5).

## Animations (30 fps, authored for running along a track toward +Z, sitting back like on a rollercoaster)
| Clip | Frames | Loop | Notes |
|---|---|---|---|
| `Run_InPlace_v2` | 96 | yes | main run: grounded, wind from the front, fur / ponytail stream back |
| `Run_InPlace` | 48 | yes | rolling gallop with small contact bounces |
| `Idle` | 72 | yes | breathing, eye glances, blink |
| `Jump_Start` | 12 | | crouch + launch |
| `Jump_Air` | 24 | yes | falling, fur + ponytail lift |
| `Land` | 18 | | impact squash, forward momentum, settle |
| `Smash` | 26 | | ground pound |
| `Hang` | 72 | yes | hanging from a ledge by the top edge (upright, not sat back) |
| `Blink` | 8 | | lids only, plays on the additive layer |

## Animator parameters
`Speed` (float, >0.1 = run) - `Jump` (trigger) - `Grounded` (bool) - `Smash` (trigger) - `Hanging` (bool) - `Blink` (trigger, fired by `LumiBlinker`).

## Glow
`M_Lumi_FurGlow` and the brows are emissive (HDR). For the full halo enable **Bloom** (URP Volume / Post Processing, threshold ~1,
intensity 0.5-1.5); without Bloom the edges still read bright. Per instance: `LumiColors.glowColor` / `glowIntensity`, `browColor` / `browGlow`.
Best seen on dark levels with an orthographic camera slightly above eye level.

## Recolouring
Fur, brows, skin, hair, tie and lip textures are grayscale, so **the material Base Color is the colour**.
Per instance (no material copies): `LumiColors` (tick *Apply*) or `GetComponent<Lumi.LumiColors>().SetFur(Color.green);`

## Runtime components
- **`LumiFurPhysics`** - spring bones for fur + ponytail, ground collision, wind. For an in-place runner:
  `fur.externalVelocity = transform.forward * runSpeed;` and `fur.grounded = isGrounded;` (false while airborne / hanging).
- **`LumiFace`** - layered on top of any clip: eye look target or sliders, lid open / close, brow angry / raise, `smile` + `smirk`.
- **`LumiColors`** - per-instance colours incl. glow, hair, tie, lip.
- **`LumiBlinker`** - random blinks (occasional double blink).

## Runner hookup
```csharp
animator.SetFloat("Speed", runSpeed);
animator.SetBool("Grounded", isGrounded);
if (jumpPressed) animator.SetTrigger("Jump");
if (smashPressed) animator.SetTrigger("Smash");
animator.SetBool("Hanging", onLedge);   // place her so her top edge (pivot + 1 m) is at the ledge
```

## Rig
`Root > Body` (squash / stretch, pivot on ground) `> Body_Upper` (lean) `>` `Eye_L/R`, `Lid_Upper_L/R`, `Lid_Lower_L/R`, `Brow_L/R`,
`Face_Front` (look anchor), `Pony_01-04` (ponytail), `Smile_L/C/R`, `Fur_00..19` (outer fur patches).
