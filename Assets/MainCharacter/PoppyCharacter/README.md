# PoppyCharacter - Unity drop-in

**Poppy**: bright-pink fluffy cube girl (~1 x 1 x 1 m) with a soul: big honey-gold eyes with catchlights and a faint inner glow,
hopeful lilac brows, rosy blush cheeks, a sweet little smile with dimples, deep-plum space buns with glowing gold ties, glowing violet hair streaks and two spiral
ringlets. Pale-pink glow fur runs along all 12 edges and 8 corners.
Rigged (44 bones), 9 clips, spring physics for fur + buns + ringlets, face controls, per-instance colours.

## Install (2 steps)
1. Copy this whole `PoppyCharacter` folder into your Unity project's `Assets/` folder.
2. Wait for compile. The editor script builds the materials, Animator Controller and prefab automatically.
   (If anything is missing: **Tools > Poppy Character > Run Setup**.)

Then drag **`Assets/PoppyCharacter/Prefabs/PoppyCharacter.prefab`** into a scene. Pivot = ground contact point.

## What gets created
| Path | What |
|---|---|
| `Models/Poppy.fbx` | Skinned mesh (~59k tris), Generic rig, 9 clips (import settings applied automatically) |
| `Textures/T_Poppy_Atlas.png` | 1024 atlas: grayscale fur / skin, coloured eyes + lashes |
| `Materials/` (generated) | `M_Poppy_FurMain`, `FurGlow` (emissive), `FurAccent` (emissive brows), `FurBlush`, `Skin`, `Hair`, `Bow` (gold ties), `Lip`, `Gloss` (eyes, soft inner light) |
| `Animations/Poppy.controller` (generated) | Animator Controller, all clips wired, additive Blink layer |
| `Prefabs/PoppyCharacter.prefab` (generated) | Model + Animator + `PoppyFurPhysics` + `PoppyFace` + `PoppyColors` + `PoppyBlinker` |
| `Scripts/Runtime/*.cs` | Runtime components |
| `Scripts/Editor/PoppyCharacterSetup.cs` | Import settings + material / controller / prefab builder |
| `Source~/` | Blender source + build script (Unity ignores folders ending in `~`) |

## Animations (30 fps, running along a track toward +Z, sitting back like on a rollercoaster)
`Run_InPlace_v2` (96, loop, main run) - `Run_InPlace` (48, loop, gallop) - `Idle` (72, loop, **alive**: head tilt, glances left and right,
looks up at the viewer with lifted brows and a bigger smile, double blink) - `Jump_Start` (12) - `Jump_Air` (24, loop) - `Land` (18) -
`Smash` (26) - `Hang` (72, loop, hanging by the top edge) - `Blink` (8, lids only, additive).

## Animator parameters
`Speed` (float, >0.1 = run) - `Jump` (trigger) - `Grounded` (bool) - `Smash` (trigger) - `Hanging` (bool) - `Blink` (trigger, fired by `PoppyBlinker`).

## Glow
`M_Poppy_FurGlow`, the brows and (softly) the eyes are emissive. Enable **Bloom** (threshold ~1, intensity 0.5-1.5) for the halo; without
it the edges still read bright. Per instance: `PoppyColors.glowColor / glowIntensity`, `browColor / browGlow`. Best on dark levels with
an orthographic camera slightly above eye level.

## Recolouring
All fur, hair, tie, blush and lip textures are grayscale, so **the material Base Color is the colour**. Per instance (no material copies):
`PoppyColors` (tick *Apply*) or `GetComponent<Poppy.PoppyColors>().SetFur(Color.cyan);`

## Runtime components
- **`PoppyFurPhysics`** - spring bones for fur, buns (`Bun_`) and ringlets (`Curl_`), ground collision, wind. In-place runner:
  `fur.externalVelocity = transform.forward * runSpeed;` and `fur.grounded = isGrounded;` (false while airborne / hanging).
- **`PoppyFace`** - on top of any clip: eye look target or sliders, lid open / close, brow angry / raise, `smile` + `smirk`.
- **`PoppyColors`** - per-instance colours (fur, brows, skin, glow, hair, ties, blush, lip).
- **`PoppyBlinker`** - random blinks, occasional double blink.

## Runner hookup
```csharp
animator.SetFloat("Speed", runSpeed);
animator.SetBool("Grounded", isGrounded);
if (jumpPressed) animator.SetTrigger("Jump");
if (smashPressed) animator.SetTrigger("Smash");
animator.SetBool("Hanging", onLedge);   // top edge (pivot + 1 m) at the ledge
```

## Rig
`Root > Body` (squash / stretch, pivot on ground) `> Body_Upper` (lean) `>` `Eye_L/R`, `Lid_Upper_L/R`, `Lid_Lower_L/R`, `Brow_L/R`,
`Face_Front`, `Smile_L/C/R`, `Bun_L/R` (each with `Curl_<S>_01-03` ringlet chain), `Fur_00..19`.

## Hair glow
Fine violet hairline streaks (thin swirl lines on the buns, a few hair-thin stripes over the crown, narrow highlight locks in the ringlets) use `M_Poppy_HairGlow`
(emissive, tintable) and the gold ties use `M_Poppy_Bow` (emissive). Recolour per instance with `PoppyColors.streakColor / streakGlow`
(pink, cyan, mint... any "special hair" colour); Bloom gives the halo.

## Lighting / brightness
The pink is lit, not glowing: brighter strand grays + tint, tops catch more light than sides, and `M_Poppy_FurMain` / `M_Poppy_Skin` carry
a faint ambient lift (emission 0.14 / 0.25 - far below the Bloom threshold, so no halo) so shadows never turn muddy on dark levels.
Tune per instance with `PoppyColors.ambientLift`. A white-ish ambient / a soft front-top directional light keeps it vivid; strongly
coloured (green) ambient makes pink look grey.
