# Alien Skies: Single-Player Pivot — "First Run" Milestone

**Date:** 2026-08-28 · **Status:** approved (user, in chat) · **Supersedes:** the PvP direction of `2026-07-04-first-blood-design.md` (its movement/combat/juice content remains valid)

## Table of Contents

1. [Vision](#1-vision)
2. [What stays, what goes](#2-what-stays-what-goes)
3. [Game structure: runs and scoring](#3-game-structure-runs-and-scoring)
4. [Systems design](#4-systems-design)
5. [Art direction: procedural-only](#5-art-direction-procedural-only)
6. [Milestone scope and phases](#6-milestone-scope-and-phases)
7. [Testing and verification](#7-testing-and-verification)
8. [Risks and mitigations](#8-risks-and-mitigations)
9. [Out of scope (deferred list)](#9-out-of-scope-deferred-list)

## 1. Vision

**A single-player, mission-style breakcore movement-shooter.** Reference point:
CICADAMATA (freeform FPS-platformer levels replayed for score/speed) crossed with
Ultrakill's style-graded combat — which was Alien Skies' DNA from the start
(Gurren Lagann feel, Ultrakill/Echo Point Nova movement).

- Short handcrafted cosmic levels: platforms, planets, grapple lines in the void.
- Reach the exit; enemies populate the route; **combat is the score, time is the pressure**.
- **The music is the multiplier.** Kills and sustained aggression feed the existing
  MusicCore heat; heat drives both the breakcore mix (volume/low-pass, already built)
  and the score multiplier. Go quiet and the music — and your multiplier — decays.
- Online multiplayer is dead. Decision recorded as ADR-004: the networking tax
  (prediction, reconciliation, SyncVar glue, two-instance playtests) was the part
  that made the game feel impossible; single-player deletes it.

**Identity (user-directed, 2026-08-28 night):** Ultrakill × CICADAMATA:
*breakcore, speedrun, killing enemies, style, points, GLITCHY, space* — plus an
antagonist in the SHODAN archetype: **the Voice of the Machine**. The Gyroscope
itself is the AI — a contemptuous machine divinity that built the arena as a
test and taunts the runner in glitch-corrupted HUD text (deaths, ranks, kill
chains), shifting from amused contempt to unsettled respect at high skill.
ORIGINAL character and lines only — the archetype is the homage; no System
Shock names, lines, or likeness, ever. Glitch is a first-class visual language:
digital corruption on death/restart, character-corruption text materialization.

## 2. What stays, what goes

**Stays (unchanged pure-C# cores + shaders — the playtested feel):**
MovementCore (quake accel, zip-grapple, zip-strike, launch pads, jump buffer/coyote),
CombatCore + WeaponCore, FeedbackCore (trauma shake), MusicCore (heat/decay/duck),
BotBrain, all EditMode tests over them, NeonPlanet / NeonRim / CosmicSkybox shaders,
Palette, the builder's geometry vocabulary (rings, spires, discs, beacons, worlds,
megastructures), MusicManager streaming from StreamingAssets/Music,
tools/run_tests.sh + tools/build_player.sh + SceneScreenshot.

**Goes:**
- **FishNet** (vendored at Assets/FishNet, incl. the 5 LOCAL PATCH edits) and every
  NetworkBehaviour: NetworkPlayerMotor, networked PlayerCombat/Rocket/BotDriver
  glue, MatchManager, PlayerSpawner/Tugboat/ConnectHUD, DefaultPrefabObjects.asset,
  NetworkTickSmoother usage. The prefab-trio churn rule dies with it.
- **All store asset packs** (user: "I hate those prebought assets"):
  TirgamesAssets (committed subset AND the ~930 untracked editor-imported extras),
  Hovl (Assets/Hovl Studio), Space Graphics Toolkit + CW (Assets/Plugins/CW),
  Vefects impact frames. All are re-importable from the Asset Store if ever needed;
  nothing else references them after the de-pack phase (CombatFx already has graybox
  fallbacks; StylizedPacks style mode is deleted with them — NeonVector is the only style).
- **MatchCore** (first-to-N frags is a PvP concept) — replaced by RunCore.

## 3. Game structure: runs and scoring

A **level** is an authored scene section built by the existing builder: a route
through cosmic geometry from **spawn** to an **exit gate**, with 2–3 combat pockets
of enemies along it. v1 ships **one level** ("Sphere 1").

A **run**: timer starts on first input, ends on touching the exit gate.
Death (KillY or HP 0) = instant restart of the run (arcade rule, no lives).

**Scoring (KISS, all readable on one grade screen):**
- Each kill: `+base × multiplier`, where `multiplier = 1 + floor(heat × 3)`
  (heat ∈ [0,1] from MusicCore — so ×1..×4).
- Kills and speed feed heat (existing hooks); heat decays on quiet (existing).
- Final grade: score plus a par-time bonus (authored per level), mapped to a
  rank letter **D / C / B / A / S**. Locals-only best score+time per level
  (PlayerPrefs); no leaderboards, no unlocks in v1.

**HUD:** existing HP/zip bars stay; FRAGS counter becomes SCORE + multiplier readout
(multiplier doubles as the "music intensity" indicator); timer top-center;
VICTORY splash becomes the grade screen (time, kills, peak multiplier, rank, best).

## 4. Systems design

New/reworked units, each pure-C# and unit-tested where there's logic:

- **RunCore (new, pure C#):** run state machine (Ready → Running → Complete/Dead),
  timer, score accumulation, multiplier from heat, rank mapping from (score, time, par).
  ~Mirror of MatchCore's role and size. Fed by the same events that feed MusicCore.
- **RunManager (thin MonoBehaviour):** replaces MatchManager. Drives RunCore from
  scene events (first input, exit-gate trigger, player death), owns the grade
  screen + restart, persists bests to PlayerPrefs.
- **PlayerMotor (thin MonoBehaviour, resurrected):** Update-loop driver around
  MovementCore (the pre-FishNet pattern from movement toy v1; ExternalInput hook
  kept for bots). Camera stays on the Graphical child — keep the detached-visual
  pattern but with plain per-frame smoothing (no tick smoother).
- **PlayerCombat (de-networked):** local-authority fire/damage/respawn around
  CombatCore/WeaponCore; rockets become plain projectiles simulated locally.
- **EnemyDriver (thin MonoBehaviour):** BotBrain + CombatCore per enemy
  (enemies reuse player HP model, low HP values). v1 enemy set: the existing
  stationary rocket drone, plus the orbit-strafe variant where geometry allows
  (Stationary flag exists). Death = existing kill juice (impact frame, explosion,
  heat spike).
- **Exit gate:** trigger volume + emissive NeonRim ring; touching it completes the run.

Everything else (FeedbackCore/CombatFeedback, CombatFx, MusicManager, builder)
keeps its role with call-site changes only (RPC/SyncVar paths become direct calls
or C# events).

## 5. Art direction: procedural-only

**Policy (ADR-004): zero store assets.** Everything visual is code: hand-written
shaders, code-built meshes, palette-enforced coherence. This is the identity that
won every playtest A/B (NeonPlanet, NeonRim, procedural explosions) and it matches
the reference's minimalist-abstract look.

- **Viewmodels:** procedural neon light-forms — simple code-built geometric meshes
  (NeonRim-shaded), driven by the existing procedural kick/sway/momentum-lurch.
  Style follows the recorded caveat: rimPower 3+, lower intensity at viewmodel range.
- **Impact frames:** full-screen shader (posterize/invert flash, ~0.4s authored
  fade) replacing the Vefects billboard — same trigger path.
- **Explosions/FX:** CORRECTION (2026-08-28, Phase 2 recon): the "3 orthogonal
  planes" are Hovl instances — only the expanding HDR core-flash sphere is ours.
  Phase 2 upgrades the code-built explosion into the whole effect: core flash +
  fresnel shockwave shell (new hand-written shader) + radial debris streaks,
  palette-colored. Hitscan impacts get a small code-built spark; tracers stay.
- **Weapon audio:** existing procedural blips + the laser SFX already vendored
  stay if license-clean and free; anything from paid packs goes.

## 6. Milestone scope and phases

Branch from `feature/motion-pass` (PR #21 content rides along; PR the tip to main
per the stacked-merge lesson). One phase = one reviewable PR-sized chunk.

1. **Phase 1 — De-network.** Remove FishNet + all networked glue; PlayerMotor /
   PlayerCombat / RunManager-less interim (keep MatchManager stubbed out or
   removed with a temporary free-play scene); builder stops creating network
   objects; game boots straight into the arena with a B-key bot. Tests green
   (drop FishNet-dependent ones, keep all core tests). Exe builds.
2. **Phase 2 — De-pack.** Delete Tirgames/Hovl/SGT/CW/Vefects; procedural
   viewmodels; shader impact frame; StylizedPacks mode removed; screenshots
   re-verified (shot_weapons, shot_fx, shot_impactframe).
3. **Phase 3 — The run loop.** RunCore + RunManager + exit gate + HUD rework +
   grade screen + PlayerPrefs bests; Sphere 1 level authored in the builder
   (route + 3 combat pockets + par time); heat-multiplier wired.
4. **Phase 4 — Enemies pass.** EnemyDriver, enemy HP/death juice, placement in
   Sphere 1, difficulty sanity pass (playtest-gated).

Playtest gates: after Phase 1 (feel unchanged?), after Phase 3 (is the loop fun?),
after Phase 4 (grade chase working?).

## 7. Testing and verification

- Pure-core tests keep guarding feel; RunCore gets its own suite (state machine,
  multiplier math, rank mapping). Net-dependent tests are deleted with the code
  they tested.
- `bash tools/run_tests.sh` headless (editor closed) stays the gate for every phase.
- Screenshot verification set continues (arena top, weapons, fx, impact frame);
  user play-mode screenshots remain ground truth for anything volumetric/lit.
- Every phase ends with a rebuilt `Builds/AlienSkies.exe` for playtesting.

## 8. Risks and mitigations

- **Prefab surgery:** NetworkPlayer/Rocket prefabs are rebuilt as plain prefabs by
  the builder; work happens on a branch, uncommitted user editor churn is committed
  as WIP first (no destructive git).
- **Untracked pack files:** the ~930 user-imported Tirgames extras are deleted in
  Phase 2 — unrecoverable locally but re-importable from the Asset Store (user owns
  the licenses). Called out here so it's a decision, not an accident.
- **Feel drift:** removing tick smoothing changes the camera path. Phase 1's
  playtest gate is specifically "does movement still feel glass-smooth?";
  the pure cores are untouched, so any drift is glue-level and fixable.
- **Hidden FishNet reach:** grep for FishNet/SyncVar/ServerRpc across Assets +
  builder before declaring Phase 1 done; csproj files regenerate on Unity open.

## 9. Out of scope (deferred list)

More levels/spheres, level select, leaderboards, zone-time slow-mo, style-name
popups (ULTRAKILL-style), new enemy types beyond the two drone variants, weapon
pickups/loadouts, story framing, Steam anything, the UE5 visual R&D feedback loop.
