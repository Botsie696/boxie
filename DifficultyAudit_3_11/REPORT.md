# Difficulty audit - MainScene prop camera 3.11 (levels 1-6 only, per request)
Measured 2026-10-14 in the Unity editor (Play mode, scene opened but not saved). Adaptive easing forced OFF at runtime for all measurements, restored afterwards.
Files: legs_seed1-3.csv (800 legs each), progression_every10_L1-6.csv, bot_analytic_L1-6.txt, bot_live_runs.csv.

## A. What 3.11 uses
| Item | Value |
|---|---|
| Profile class | `Scene310DifficultyProfile` (same class as 3.10) |
| Asset | `Assets/Difficulty/Scene3_11_CheckpointDifficulty.asset` - **its own copy, not shared** |
| vs 3.10 asset | levels much longer; portal legs differ; unused slow-mo/boost keys dropped (fields now on the scene generator) |
| Level lengths (legs) | 3.10: 37/41/29/45/45/33/41/31/49 -> 3.11: **42/60/85/70/70/75/70/80/100**; portal legs 42,102,187,257,327,402,472,552,652; endless repeat 60 |
| Scene diffs vs 3.10 | `difficultyProfile` ref, `pauseHitSize` 140->240, `rawTouchFallback`, `queueEarlyPressSeconds` 1.5, FaceForm + HudTheme objects, generator: specials/slow-mo sections, obstacle designs (pillars, march from level 3), obstacle coins, `noSpecialsInFirstLevel`, `coinsMatchTrackFade` |

## B. Config (serialized asset)
| Level | legs | speed start->end | durStart/End (s) | tHuman 1-roll | tHuman 2-roll | buildAvg/peak | costCap | decoy | runway legs |
|---|---|---|---|---|---|---|---|---|---|
| L1 Start | 42 | 17->23 (hold 3) | 1.0-1.3 / .85-1.15 | .75->.55 | .84->.64 | 1.6/3.2 | 5 | 0 | 21 |
| L2 Crimson | 60 | 20->24 | .74-.98 / .72-.96 | .52->.47 | .61->.56 | 2.2/3.8 | 5 | .30 | 15 |
| L3 Toon | 85 | 23->27 | 1.05-1.4 / 1.0-1.3 | .50->.47 | .59->.56 | 1.4/3.0 | 5 | .10 | 10 |
| L4 Pixel | 70 | 21.5->23.5 | .95-1.25 / .9-1.2 | .48->.44 | .57->.53 | 2.6/4.0 | 6 | .50 | 18 |
| L5 Storybook | 70 | 22->26 | .72-.98 / .68-.94 | .46->.42 | .55->.51 | 2.5/4.2 | 6 | .25 | 24 |
| L6 Blossom | 75 | 22.5->24.5 | .85-1.15 / .82-1.1 | .48->.46 | .57->.55 | 1.5/2.8 | 4 | 0 | 14 (+diamond from leg 10, coins x2) |

Globals: rollTime .10, rollCooldown .08, landingLock .03, cornerDeadline .03 (overhead 1 roll = .24 s, 2 rolls = .42 s, 0 = .06 s); globalFloor 1-roll .40 / 2-roll .47; speed ceiling 31 u/s; blend 4-6 legs between levels; boost window scale .9 (min .40 2-tap / .34 1-tap); rescue window .09 s / reach 3.5 cubes / +1 point; early-turn bonus 3.5 cubes; hit-stop on rescue; cubeSize 1.
Adaptive easing: ON in the asset. Triggers only after 3 deaths in one checkpoint (budget x0.88, +0.04 s tHuman) or 5 deaths (x0.80, +0.07 s). Stored in PlayerPrefs, reset when the exit door is crossed.

### Input model (PlayerController.cs)
| Question | Answer |
|---|---|
| Pre-input? | Taps are executed immediately (`ProcessInputQueue`, FixedUpdate, :704). A tap within `preCornerBuffer` **0.15 s** of the corner while already on the turn face is held (`nextLegBuffer`, :801) and replayed at landing. Earlier than that the tap rolls the runner at once (so pressing early for the *next* corner is not possible). |
| Queue | `inputQueue`, max **5** (`MaxQueuedInputs`, :166); overflow dropped. Roll cooldown .08 s between queued rolls; flips ignore cooldown. |
| Latest valid input | Corner decided at `cornerCommitOffset` -0.05 cube; a queued correct press held by the cooldown still counts (:1200); then rescue window **0.09 s** (one per leg). |
| fairCorners | tap just before landing is kept for the next leg; corner decided at exact point with sub-step overshoot; rescue enabled. |
| fairInput | fire on touch-down (not lift), 0.03 s landing lock, double-tap (0.22 s) = flip, multi-touch, swipe 45 px. |
| Rescue | missed corner -> glide up to 3.5 cubes; success if on the turn face, nothing queued, fewest rolls, last input within 0.09 s of the miss. Else fall. Planks: same rule. |
| Shield | `ShieldRecover` cancels one death and puts the runner back on route (power-up; not triggered in this audit). |
| Auto-correct | none beyond the rescue/early-bonus above; no snapping of wrong rolls. |

### Visibility (camera D)
`PathDepthShading.VisibleAheadCubes` = **92 cubes** (cubeSize 1). Seconds ahead = 92 / speed: 15 u/s 6.1 s, 20 u/s 4.6 s, 25 u/s 3.7 s, 28 u/s 3.3 s, 30 u/s 3.1 s, 32 u/s 2.9 s. This is the *drawn track* distance, i.e. the corner after next is always on screen; the real limit is human reaction not visibility. Fog/draw distance and behaviour during camera rotations, and whether decoys/runways are visible before their leg: **not measured** (needs screenshots at speed).

### Feature inventory (3.11, levels 1-6)
| Feature | Status | Cost |
|---|---|---|
| Decoy forks | ON per level (0-50%) | 2 |
| Runways (pillar obstacles) | ON, 1 per level, designs march from L3 | 2 |
| Boost zig-zags | ON (L2+ mostly), double-tap short legs 5-7 cubes | 3 |
| Glides (long legs) | ON, 1-3 per level | - |
| Camera rotations | ON (`cameraTurn`, restricted) | 3 |
| Planks/bridges, flips, opposite-face | in generator, appear in cost table; not seen as tags in L1-6 export | 3 / 1 |
| Slow-mo sections | ON generator-side from level 3 | - |
| Triangle gates / red blocks | not present in this scene's profile (not measured) | - |

## C. Generated track (3 seeds, 800 legs, L1-L6 only)
Per-level averages over 3 seeds (last-20 / last-30 in `legs_seed*.csv` summaries; ranges below). Slack = seconds - overhead - tHuman. **Boost zig-zag legs (0.15-0.25 s, flagged floorViolation by the generator's own log) show negative slack because they are judged as a rhythm sequence, not per leg - treat them separately.**

| Level | avg leg s | min leg s | 0/1/2-roll % | alternation % | longest short-leg run | avg slack | legs <0.05 s slack (of ~) | last-20 avg s / avg slack |
|---|---|---|---|---|---|---|---|---|
| L1 | 1.18 | 0.87 | 16/65/20 | 78 | 0 | .38 | 0-3 / 42 | 1.09 / .32 |
| L2 | 1.08 | 0.65 | 27/50/23 | 67 | 0 | .43 | 6-7 / 60 | 0.9 / .25 |
| L3 | 1.22 | 0.16 | 17/51/32 | 79 | 6-7 | .54 | 9-18 / 85 | 1.2 / .48 |
| L4 | 1.22 | 0.17 | 12/48/40 | 82 | 7-8 | .52 | 7-14 / 70 | 1.2 / .50 |
| L5 | 0.95 | 0.16 | 18/58/24 | 76 | 8 | .32 | 10-15 / 70 | 1.0 / .4 |
| L6 | 1.10 | 0.16 | 17/58/25 | 75 | 0-12 | .44 | 8-14 / 75 | 1.3 / .6 |

Hazards per 10 legs: 0-0.9 (decoy/runway/plank tags). Glides 1-3 per level. Seeds differ a lot in boost placement (e.g. L2 has 0 boost legs in seed 1, 13 in seed 2).

### Progression every 10 legs (3-seed mean; full table in progression_every10_L1-6.csv)
| legs | lvl | speed u/s | cubes | avg leg s | min s | % legs <0.6 s |
|---|---|---|---|---|---|---|
| 0-9 | 1 | 17.2 | 24.0 | 1.34 | .99 | 0 |
| 10-19 | 1 | 18.3 | 22.9 | 1.19 | .94 | 0 |
| 20-29 | 1 | 19.9 | 23.0 | 1.10 | .94 | 0 |
| 30-39 | 1 | 21.7 | 24.6 | 1.08 | .90 | 0 |
| 50-59 | 2 | 22.0 | 22.1 | .95 | .75 | 0 |
| 80-89 | 2 | 22.9 | 27.4 | 1.15 | .65 | 0 |
| 100-109 | 2/3 | 23.5 | 42.5 | 1.76 | .17 | 7 |
| 120-129 | 3 | 24.6 | 23.6 | .91 | .16 | 30 |
| 140-149 | 3 | 27.2 | 38.3 | 1.40 | .86 | 0 |
| 150-159 | 3 | 27.7 | 32.1 | 1.13 | .87 | 0 |
| 190-199 | 4 | 22.1 | 39.5 | 1.75 | 1.00 | 0 |
| 220-229 | 4 | 24.2 | 26.1 | 1.04 | .84 | 0 |
| 270-279 | 5 | 23.8 | 18.6 | .73 | .17 | 27 |
| 290-299 | 5 | 25.5 | 15.4 | .54 | .16 | 50 |
| 330-339 | 6 | 22.7 | 28.6 | 1.21 | .87 | 0 |
| 360-369 | 6 | 23.5 | 14.1 | .55 | .17 | 57 |
| 400-409 | 6 | 24.5 | 27.8 | 1.09 | .85 | 0 |

Shape: leg time falls from ~1.3 s to ~0.9 s inside L1-L2, then holds ~1.0-1.2 s except the boost bursts (0.16-0.25 s legs, 10-57% of a 10-leg window), and 8-12-leg runs of 0.2 s legs in L3/L5/L6. Speed resets down at each portal (L3 27.7 -> L4 22), so difficulty is a saw-tooth, not a ramp.

## D. Telemetry
Existing: `RunTelemetry` (Scripts/Difficulty, `telemetry: 0` in this asset = OFF), `RescuesThisRun`, `CornersMadeThisRun`, `LastMissTime`, `LastDeathCause`, `FirstTapAfterLanding`, `TapsThisLeg`, close-call streak, `AdaptiveEasing` death counters (PlayerPrefs `Key+checkpoint`), best score per checkpoint. **No per-corner input margin is recorded**, so your real margins cannot be collected without code. Minimal dev-only recorder (not built): per corner log time-corner-visible, tap times (`TapTaken` event exists), `LastExecutedTapTime - missTime`, rescue/shield flag, death cause; hook `TapTaken`, `Rescued`, `LandCorner`, `FailMiss`.

## E. Bot results
Tools: `AutoPlayer.humanBot` IS configurable (mean/sd/late chance/late extra) - used with your 3 profiles + 3% late (+0.12 s). It reacts from the moment the runner lands on a leg (no visibility delay). `HumanSimPlayer` analytic batch only has fixed Skilled .25/.04 and Average .42/.07 models (bracketing, not matching your profiles).

### E1 live bot (AutoPlayer.humanBot, real game, 3 runs per profile per checkpoint start, 5x speed) - small sample
| Level | Average .32/.07 | Above-avg .26/.06 | Developer .20/.04 |
|---|---|---|---|
| L1 | 2/3 clear | 3/3 | 2/3 |
| L2 | 2/3 | 1/3 | 2/3 |
| L3 | 0/3 | 1/3 | 1/3 |
| L4 | 0/3 | 1/3 | 0/3 |
| L5 | 1/3 | 2/3 | 2/3 |
| L6 | 0/3 | 0/3 | 1/3 |
Death causes (all profiles): **portal 10 of 31 deaths** (runner dies in the last 1-2 legs of the level at the door - at scores 41/101/186/326/401 for *every* profile incl. Developer: looks like a door-entry/bot-timing interaction, needs a human check), corner 17, decoy 1, offtrack 2. Rescues 0-20 per run (a run with 20 rescues at L6 survived on the 0.09 s window). Not meaningful: profile ordering (n=3). Unattended 30-run sweeps were not run (cost; your test-budget rule).

### E2 analytic batch (Skilled .25 / Average .42, 240 runs per level over 12 seeded layouts; adaptive off)
| Level | Skilled clear | Average clear | Where they die (Skilled) |
|---|---|---|---|
| L1 | 99% | 88% | cooldown phase (end) |
| L2 | 94% | 63% | build (11 of 15) |
| L3 | **8%** | **7%** | cooldown 139 / build 82; 79 of 221 deaths in last 30 legs |
| L4 | 48% | 32% | build 122 of 125 |
| L5 | 35% | 28% | build 78 / cooldown 73 |
| L6 | 46% | 28% | build 102 |
Caveat: this model treats each 0.16-0.25 s boost leg as an independent reaction, which is harsher than real play (rhythm); L3's 7-8% clear is probably overstated, but L4-L6 numbers agree directionally with the live bot (median ~half cleared).

## F. Performance / limits
Frame time, touch latency, tunnelling at 25-32 u/s: **not measured** (editor at 5x timescale is not representative; no device). Live runs at 5x with speeds up to 30 u/s showed no corner-trigger errors in the log. Highest speed with camera D showing the next corner >= 0.6 s: visibility is 92 cubes, i.e. 2.9 s at the 32 u/s ceiling - well above 0.6 s.

## G. Signals for a future adaptive system
| Signal | Today | Hook | Mobile-reliable |
|---|---|---|---|
| deaths per checkpoint | yes (AdaptiveEasing PlayerPrefs) | NoteDeath/NoteCleared | yes |
| rescues / close calls / streak | yes (`RescuesThisRun`, `AwardCloseCall`) | Rescued event | yes |
| death cause, leg | yes (`LastDeathCause`) | GameOver | yes |
| tap margin before corner | no (cheap: TapTaken + corner time) | PlayerController | yes (touch timestamps) |
| first tap after landing (reaction) | yes (`FirstTapAfterLanding`) | per leg | yes |
| dropped taps (queue full) | `LastDroppedTapTime` | Enqueue | yes |
| retries, session length | not stored | CheckpointRunController | yes |

## What would help the answer
- Not measured: fog/draw distance and camera-rotation visibility, runway/decoy pre-visibility, frame time/latency/tunnelling, endless cycles (L10+), adaptive easing effect, 30-run unattended sweep, shield saves, last-20/30 summaries for every level in a standalone table (computed, in terminal output only).
- Dev-only hooks needed: per-corner recorder (above), an `AutoPlayer` mode that anticipates boost sequences, a per-run summary dump from `RunTelemetry` (exists, set `telemetry`=1).
- Modification check: project is not a git repo; compared file mtimes against a baseline taken before the audit. Only `Assets/lilToon/Editor/CurrentRP.txt` changed (auto-regenerated by the lilToon plugin when the editor ran). Scene not saved; asset `adaptiveEasing` toggled in memory and restored.
