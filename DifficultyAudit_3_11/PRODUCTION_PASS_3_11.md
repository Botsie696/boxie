# Scene 3.11 production pass — report (2026-10-14)

Scope kept: only 3.11 and its own assets changed in behaviour. Shared scripts got inert hooks only (null / default = old behaviour).
**3.10 regression: the seeded track (3 seeds × 600 legs) and the speed / T_human / duration curve (legs 0-700) are byte-identical to the pre-work baseline** (`regression/310_baseA_*` vs `310_final_*`).
Testing was cut short on request: live checks = door test (doors 1-3) + one boot of 3.11. Phase 6 bot tables are **not run**; the simulator is built (Tools below) for you to run when you want.

## Phase 0 — production fixes
| Item | Result |
|---|---|
| Door deaths: cause 1 (bot) | AutoPlayer's corner steering aimed at the corner *beyond* the door and rolled the runner off the door face every ~0.1 s (traced). Fix: `AutoPlayer.PortalOwnsSteering` (static, off by default, on in 3.11). |
| Door deaths: cause 2 (game, real) | The crossing is checked once per **frame** (`PortalZoneController.Update`) but the runner moves/dies per **physics step**; a long frame (hitch at speed, or a sped-up test) carried it off the end of the door runway before the check → "portal" death even on the right face. Fix: `PortalZoneController.crossEveryPhysicsStep` (off by default, **on in 3.11**). |
| Proof | Before: 4/20 door deaths at door 1. After: doors 1, 2, 3 = **60 arrivals, 60 crossings, 0 door deaths** (`door_test.csv`). Doors 4-9 not run (stopped on request); the fix is not door-specific. |
| Stale names | L8 "Raspberry" → **Sketchbook**, L9 "Jade Moon" → **Rogue Planet**. |
| RunTelemetry | Added by `Scene311Systems` in editor / development builds only. |
| Per-corner recorder | `CornerRecorder311` (compiled out of release): per corner visible time, every input, earliest/latest valid input, rescue-latest, margin, result (made/rescued/shield/death), cause → `persistentDataPath/CornerRecorder311/corners_*.csv`. |

Other real bugs found and fixed (3.11 only):
- **Retries had no specials**: the per-level specials plan was never reset on restart, so a retry at the same checkpoint reused the spent plan (no boosts/runways/slow-mo). Reset in `Reset311()`.
- Slow-motion tiny legs ran **20% under** the fairness floor by design (`slowMoSpeedBoost 1.2`) → 1.0 in 3.11; the floor validator now judges slow-mo legs in real time (they were false positives).
- Boosts are sized for the fastest speed / strictest floor inside the whole boost, and always end before the exhale legs (a late door could put a speed reset inside a boost → legs 7% under floor).
- The corner onto a door's runway now prefers a turn the leg has time for.
- Remaining floor violations: 10 / 4,809 legs (0.2%), all from the generator's dead-end fallback (same rate as 3.10).

## Phase 1 — base (Average) profile — built into the asset (`Tools > Scene 3.11 > Build Difficulty Asset`, table: `base_table.md`)
| L | name | legs (door) | speed | T_human 1r (2r = +0.07) | build/peak, cap | decoy | rescue | finale | signature |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Start | 45 (45) | 17→23 | .72→.55 | 1.6/3.2, 5 | 0 | .09 / 3.5 | 20 | first 2-roll pairs, no specials |
| 2 | Crimson | 60 (105) | 20→24.5 | .52→.46 | 2.3/3.9, 5 | .30 | .08 / 3.0 | 20 | staircase, short legs ×0.85 |
| 3 | Toon | 70 (175) | 23→27.5 | .50→.45 | 1.8/3.4, 5 | .10 | .07 / 3.0 | 20 | long boost → 2-roll exit |
| 4 | Pixel | 75 (250) | 23→25.5 | .47→.42 | 2.8/4.1, 6 | .55 | .06 / 2.5 | 25 | decoy gauntlet, each decoy → 2-roll |
| 5 | Storybook | 80 (330) | 23.5→27 | .45→.41 | 2.7/4.3, 6 | .30 | .05 / 2.0 | 28 | runway (Fence+Drifter) → slow-mo → burst |
| 6 | Blossom | 65 (395) | 24→26.5 | .47→.43 | 1.7/3.2, 5 | .10 | .06 / 2.5 | 25 | mild; coins ×2; power-up guaranteed |
| 7 | Brick Meadow | 85 (480) | 24.5→28 | .44→.40 | 2.8/4.3, 6 | .20 | .04 / 1.5 | 28 | CornerSlider runway → burst |
| 8 | Sketchbook | 90 (570) | 25.5→29.5 | .44→.40 | 2.2/3.8, 5 | .20 | .04 / 1.5 | 30 | long boost ×1.5 → decoys |
| 9 | Rogue Planet | 100 (670) | 26→29.5 | .43→.40 | 3.0/4.6, 6 | .40 | .03 / 1.5 | 30 | everything mixed + double spike |
| FF | Freefall | 60/cycle | 26.5→30.5, +0.3/cycle, cap 31 | .42→.40, −.005/cycle, floor .38 | identity +0.1/cycle, peak cap 4.8 | identity | .03 | 20 | identity (L2,3,4,5,7,8,9 rotate); adaptive OFF |

Shape per level: 3 release → waves (2 for L1-3, 3 for L4-9: build → peak → 1-2 glides, breather glide every ~11 legs, each wave peak +0.2) → finale (peak +0.3, T_human held at END) → 2 exhale legs (cost ≤2, no specials) → door. Speed: ease-in curve ×1.4 within a level, 5-leg smoothstep down at each portal. After a cap leg the next costs ≤3 (L1-6) / ≤4 (L7+), except the L9 spike. Power-ups ×0.7 from L4, ×0.5 from L7. Floors: base never under .40/.47, hardest tier never under .38/.45. Checkpoint starts lay each level from its own plan (deterministic per level, tier applied at run start).

## Phase 2 — boosts, slow-mo, runways (in the asset)
| L | boosts (waves + finale) | modes (newest 40%) | runways × stations | designs | gap | sliders | slow-mo |
|---|---|---|---|---|---|---|---|
| 2 | 1 | B1 | 1 × 5 | Totem, Pair | ×1.20 | – | – |
| 3 | 1 + finale long/exit | B1, B2 | 1 × 6 | + Fence | ×1.15 | – | 1 |
| 4 | 1 | B1, B2 | 2 × 6 | + Corner, 1 Drifter | ×1.15 | 22-32% | 1 |
| 5 | 2 | + B3 broken | 1 + finale × 7 | Fence, Corner, Drifter | ×1.10 | 25-35% | finale |
| 6 | 2 | B1-B3 | 1 × 6 | + Twin | ×1.15 | 25-32% | 1 |
| 7 | 2 | + B4 exit | 2 + finale × 7 | + CornerSlider | ×1.10 | 30-42% | 1 |
| 8 | 2 + finale long | B1-B4 | 2 × 8 | full mix | ×1.05 | 30-42% | 1 |
| 9 | 3 | B1-B4 | 2 + finale × 8-9 | full mix | ×1.05 | 30-45% | finale |
| FF | 2 | identity | 2 × identity +1/2 cycles (max 10) | identity | identity | identity | 1 |
Boost length 5-6 legs at L2, ×1.15/level, cap 14. Spacing: boosts ≥6 legs apart, ≥4 from runway/slow-mo, runways ≥8 apart, glide after every runway. If a level is too short, boosts shrink first, then slow-mo, then extra boosts are dropped.

## Phase 3 — skill model (`SkillProfile311` + `SkillModel311`, tuning `Scene311/Data/SkillTuning311.asset`)
Elo-style: leg rating = 700 + 60×cost + 900×max(0, 0.35 − slack); expected = 1/(1+10^((leg−player)/400)); K 40 → 6 (halves per 300 legs). Overall + 15 feature ratings (corner 1/2-roll, decoy, B1-B4, slow-mo, 7 runway designs). Result per leg: 1 clean, 0.6 rescue, 0 miss/shield. Tier = (rating−1500)/150 clamped −2..+2, interpolated through the brief's table. Weak feature (>1 tier below overall): share −30% + Below values. Applied only at run start / restart and when a door's runway is laid (next level). First 3 runs: Average. Pacing: quick first clear (< low target) → next checkpoint +0.5 tier; mercy −1 tier per 25% over the high target (max 2, floor Struggling, speed ×0.94 only at mercy ≥1), resets on clear. Freefall ignores it. Saved: `skill311.json` (versioned). Dev overlay: F2 / triple-tap top-left.

## Phase 4 — Hub, daily, missions, awards (`MetaSystems311` / `MetaUI311`, tuning `MetaTuning311.asset`)
7-day calendar 50/75/100/150/200/250/500, 1 grace day a week; shown once per day at scene start (game time held while shown). 3 daily missions 50/100/150 + 1 free reroll, pool 17 (scaled by highest checkpoint and tier); 3 weekly 300/500/800 (pool 6, Monday reset); 30 one-time awards. Clock: latest UTC kept; behind it = nothing granted. Coins → **shop wallet** `SaveSystem.AddCoins` (save.json). Claims only on the game-over panel or in the Hub (paused / between runs); mid-run only toasts (no raycasts). Hub buttons on the pause screen and the game-over panel (game-over tap-to-restart blocked over them). Save `meta311.json` v2 with migration, atomic write + .bak, on change and pause/quit.

## Phase 5 — notifications (`com.unity.mobile.notifications` 2.5.0, local only)
Never at launch (iOS "request on launch" turned off). Soft prompt after the day-2 daily claim or the first L3 clear; only YES opens the OS dialog; NOT NOW → once more after 7 days. Pause toggle REMINDERS ON/OFF. On pause/quit: cancel all, then day+1 daily-ready at the learned play hour (10-21) — or "streak at risk" 19:00 if streak ≥3 — plus re-engagement day 3 and day 7; max 1/day; channel "daily"; text names the reward.

## Phase 6 — testing status
| Test | Status |
|---|---|
| 3.10 regression | **done: identical** |
| Door fix | **done: doors 1-3, 60/60** |
| Boot 3.11 (all systems, no errors) | **done** |
| Bot clear rates / adaptive / multi-week | **not run (on request)**. Tool ready: `Sim311` (Editor). In Play mode run: `var b = Sim311.BuildCorpora(6, 770, new[]{-2f,-1.5f,-1f,-0.5f,0f,0.5f,1f,1.5f,2f}, true); Sim311.Write("A.md", Sim311.BaseClearRates(b,60)); Sim311.Write("B.md", Sim311.AdaptiveAttempts(b,15)); Sim311.Write("C.md", Sim311.MultiWeek(b,15,45));` → `phase6/`. Expect to retune durations / T_human afterwards; the brief's base numbers were entered as given. |
| Meta systems | not exercised beyond boot; use the dev menu |
| Device / performance | not done (no device) |

## New files
`Assets/Scene311/Scripts/`: Level311, Scene311DifficultyProfile, DynamicPathGenerator.Level311 (partial), Scene311Systems, CornerRecorder311 (dev), Skill/{SkillTuning311, SkillModel311, SkillProfile311}, Meta/{MetaTuning311, MetaSave311 (+MetaClock311), MetaSystems311, MetaUI311, Notifications311, DevMenu311 (dev)}. `Assets/Scene311/Editor/`: Scene311DifficultyBuilder, Sim311. ScriptableObjects: `Difficulty/Scene3_11_CheckpointDifficulty.asset` (now Scene311DifficultyProfile, same GUID), `Scene311/Data/SkillTuning311.asset`, `Scene311/Data/MetaTuning311.asset`.
Shared scripts touched (inert hooks only): DifficultyProfile, Scene310DifficultyProfile (private → protected), DynamicPathGenerator (+Waves, HazardBudget, TimeBudget, Specials, Boost, ObstacleLeg, ObstacleLegDesigns, SlowMo, Power, PowerUps, Analysis, Portal), PlayerController (per-leg rescue lookup, ShieldSaved event), PortalZoneController (crossEveryPhysicsStep), AutoPlayer (PortalOwnsSteering), TimeScaleMixer (Meta source, dev KeepPhysicsStep), HumanSimPlayer (optional late input, PlayLeg public). Packages/manifest.json: notifications package. 3.11 scene: Scene311Systems object, shield ON, crossEveryPhysicsStep ON.

## Dev menu (F3 or triple-tap top-right; dev builds only)
clock +1 h / +1 day / +1 week, clock back 1 day, clear offset · complete a mission · daily card · Hub · soft prompt · trigger prompt condition · reset meta save · reset skill · force tier (−2..+2) + mercy (0-2) · fire test notification (10 s) · log notification plan. Skill overlay: F2 / triple-tap top-left.

## Phone test steps
1. Build a **Development Build** with 3.11 (MainUi still routes to 3.10: open 3.11 directly or set `MainMenuBuilder.GameScene`).
2. First launch: daily card appears (Day 1, 50) → CLAIM → coins in the shop wallet.
3. Dev menu → +1 day → relaunch: Day 2 card → claim → the soft prompt appears at the next game over → YES → OS dialog (Android 13+ / iOS). Try NO / deny paths too.
4. Pause → REMINDERS toggle off/on. Dev menu → "log plan" (check times), "fire test", leave the app → notification after 10 s → tap it → game opens normally.
5. Dev menu → clock back 1 day → daily / claims give nothing until the offset is cleared.
6. Play to a door at speed several times (watch: no door deaths). Pause → REWARDS → Hub tabs; game over → CLAIM.
7. Kill the app mid-run → reopen: coins, missions, streak kept.

## Needs real-human playtesting
Base difficulty vs the clear-rate targets (bots not run); the Elo constants (leg rating weights, K); mercy/push pacing feel; slow-mo and B3 broken-rhythm readability; mission targets and reward economy; one-handed reach of Hub buttons on real phone sizes; notification timing and wording.
