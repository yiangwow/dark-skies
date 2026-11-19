# Alien Skies — Architecture & Field Manual

The maximum-detail reference. `CLAUDE.md` says *where we are*; this says *how
everything works and why*. If a rule here looks arbitrary, there is a diary
entry where we bled for it.

## 1. Vision & pillars

- **Fantasy:** Gurren Lagann cosmic scale — planets are jungle-gym equipment,
  not scenery. Movement is the hero; combat exists to give movement stakes.
- **Feel references:** Ultrakill (speed/aggression), Echo Point Nova
  (grapple flow), Quake (air control, rocket jumps).
- **Process:** gameplay-first, graybox-then-art, playtest verdicts gate
  merges. The user gives verdicts in vibe words; translate vibe → mechanics.
- **Division of labor:** Claude owns C#/tests/tools/headless verification and
  acts as art director (opinions + reasons, pushes back on incoherence).
  User drives the editor, playtests, and decides.

## 2. The layer cake

```
┌─ Pure C# cores ──────────────────────────────────────────────┐
│ MovementCore  CombatCore  WeaponCore  FeedbackCore           │
│ RunCore       BotBrain    MusicCore                          │
│ - no UnityEngine lifecycle, deterministic, unit-tested       │
└──────────────────────────────────────────────────────────────┘
┌─ MonoBehaviour glue ─────────────────────────────────────────┐
│ PlayerMotor (per-frame sim around MovementCore)              │
│ PlayerCombat (direct calls around CombatCore+WeaponCore)     │
│ CombatFeedback / MovementHUD / ViewmodelRig (local player)   │
│ RunManager (run loop) / ExitGate / EnemySpawner (pockets)    │
│ MachineVoice (the antagonist speaks) / MachineVoiceCore      │
│ BotDriver + BotSpawner / MusicManager / CombatFx / SlowSpin  │
└──────────────────────────────────────────────────────────────┘
```

Rule: **all feel math goes in a core with tests.** The glue may only gather
input, forward events, and render.

Single-player since ADR-004 (2026-08-28). FishNet, prediction, SyncVars, and
the prefab-trio/DefaultPrefabObjects machinery are GONE — if a diary entry or
old PR references them, that is history, not guidance.

## 3. Simulation

### 3.1 The player motor (PlayerMotor)
- Input is read and simulated in the SAME Update frame: `ReadDeviceInput()`
  accumulates one-time flags (jump/grapple/zip), `BuildInput()` drains them
  into an `InputFrame`, `Simulate()` advances `Core.Tick` +
  `CharacterController.Move` with `Time.deltaTime`.
- One code path for humans and bots: `ExternalInput`
  (`Func<PlayerMotor.InputFrame>`) replaces device reads when set;
  `IsHuman => ExternalInput == null` gates camera/HUD/cursor/body-hide setup,
  which runs lazily on the first Update (BotSpawner adds BotDriver right
  after Instantiate — after Awake, before first Update).
- **Yaw discipline:** the Graphical child owns look yaw during input; the sim
  root adopts `InputFrame.Yaw` in Simulate and the Graphical is re-aligned to
  the root EVERY frame. The Graphical is an attached child now — skipping the
  re-align makes its local yaw compound with the root each frame and the view
  spins forever (caught in plan review; the old code was immune only because
  the tick smoother detached the visual).
- Impulses (knockback) via `ApplyImpulse`; `RespawnAt` = teleport +
  `Core.Reset()`. Kill plane: `KillPlaneCrossed` fires once per fall, then a
  deterministic teleport to spawn.

### 3.2 Combat wiring
- Fire is a direct call: `FireCurrent` → `FireHitscan`/`FireRocket`;
  `FireGate` (0.9× interval tolerance) is the one rate check.
- `FireRocket` is public: the single validated fire path shared by the local
  player and BotDriver.
- What used to be SyncVar OnChange handlers are direct calls at the mutation
  site in `TakeDamage`: victim `OnDamaged` fires when health drops,
  `RespawnRemaining` starts when death lands.
- `NotifyHit` fires `OnHitConfirmed` ONLY for the local player (`IsLocal`) —
  the descendant of the old `Owner.IsValid` guard: bots must not drive
  CombatFeedback into their deactivated cameras (disabled-AudioSource warning
  spam, impact frames under a dead camera).
- Rockets are plain projectiles simulated in their own Update; splash applies
  damage + `MovementCore` knockback (rocket jumps unchanged).

### 3.3 The bot
- Stock Player prefab + `BotDriver` added right after Instantiate; input
  enters through `PlayerMotor.ExternalInput` inside the motor's Update —
  never a parallel mover.
- `BotBrain` is pure C# + seeded (orbit-strafe ring, jittered flips, hop
  timer, LOS/range/cadence-gated fire, aim-error cone).

### 3.4 The run loop (RunCore + RunManager)
- `RunCore` (pure, 18 tests): Ready → Running → Complete. Timer starts on the
  player's first HORIZONTAL movement (total velocity false-starts on gravity
  settle — review-caught). Kills score `KillScore × multiplier` where the
  multiplier is MusicCore heat mapped x1..x4 — **the music is the score**.
  Heat gain keys on movement state at kill-confirm time, never weapon
  identity or mere firing (guardrail 6):

  | Kill state | Heat gain |
  |---|---|
  | Grounded (incl. planet coyote window) | 0.33 |
  | Airborne | 0.60 |
  | Flow (grapple or zip) | 0.72 |

  Repeating the same state on consecutive kills discounts the gain by a
  0.65 staleness factor per repeat — a stationary kill loop goes stale and
  falls out of the top tiers; changing state restores the full gain. Firing
  pays nothing. Non-kill hits add a rate-limited trickle (`RegisterHit`,
  0.08 heat, 0.7 s cooldown) that caps every weapon — including the
  shotgun's 16-pellet burst — under the 0.12/s decay rate, so no weapon can
  hold a tier by plinking. Taking damage costs exactly one multiplier tier
  (`MusicCore.DropTier`, guardrail 7) instead of adding heat; self-splash
  from rocket-jumping is exempt, since the health cost is that verb's own
  price. Complete = score + under-par bonus → rank D/C/B/A/S vs
  `STargetScore`.
- `RunManager` (scene singleton): binds the one `IsLocal` PlayerCombat, feeds
  kills from `OnHitConfirmed`, completes via `ExitGate` (amber ring, far
  outer perch), persists bests in PlayerPrefs. Restarts: death or kill-plane
  → back to the start line, but ONLY while Running (the grade screen must
  survive sailing off the perch), and kill-plane restarts are DEFERRED one
  frame (`_restartPending`) because the motor's own fallback teleport runs
  after the event and would clobber the position. Every restart calls
  `MusicCore.ResetHeat()` — runs start cold, multipliers cannot leak.
- `PlayerCombat.InputsEnabled` gating now reads `RunManager.InputsFrozen`
  (set while the grade screen shows); `MusicCore.RoundOver` ducks the mix
  from the same flag.
- **Levels (`LevelDef`):** pure authored data (start, par, S-target, posts,
  gate object, bests-key prefix) built by the builder into
  `RunManager.Levels`. TAB at the start line (Ready state ONLY, cursor-lock
  gated — 1/2/3 are weapon keys) cycles spheres: rebuilds RunCore via
  `RunSettings.WithOverrides`, toggles gate objects, repositions the player,
  repopulates enemies (LevelChanged event + EnemySpawner's pull-once-after-
  subscribe, which makes Start order irrelevant), loads per-level PlayerPrefs
  bests. Three COURSES (built by `CourseBuilder` — parametric segment lanes with
  per-gap capability asserts; the generator refuses impossible geometry):
  **1 THE CROSSING** (outbound lane ~295 m from Wing_0, par 90),
  **2 THE CLIMB** (vertical ascent from Wing_1 to y≈115, par 75),
  **3 THE PLUNGE** (start (−60,120,−60), 100 m dive back west, par 60).
  Each course carries its own Posts/Orbiters/Turrets (recorded by the
  segments themselves) and fresh course1/2/3 bests keys. Every course also
  declares one authored cross-cut (guardrail 11): `BeginCrossCut`/
  `EndCrossCut` place a small grapple anchor astride an authored span,
  drawing nothing from the course's own rng, and validate an analytic
  climb-aware time saving above a 4 s floor — `AssertCrossCutDeclared`
  Exit(1)s the batchmode build exactly like `AssertBudget` if a course lacks
  a validated one. Gate centers are exempt from the
  1–2 m-above-collider rule (posts and Starts are not). Every gate root = ring + trigger + a 40m amber beacon pillar
  (GateBeacon unlit material, Amber×3) — the findability fix. Builder scope
  rule (recurred FIVE times in reviews): gate references thread from
  BuildArena to BuildPlayer BY PARAMETER — `FindFirstObjectByType` excludes
  inactive objects and Sphere 2's gate is saved inactive.
- **Beam turrets (`TurretCore` + `Turret`, one per course):** the second and
  FINAL enemy archetype (user doctrine: flavor over count — the roster is
  frozen). Pure-core state machine: acquire on LOS, track with an
  angular-speed-capped aim (140°/s — a fast player OUTRUNS the lock; momentum
  is the dodge), 0.9 s warning laser (cyan→magenta ramp), instant beam
  (25 dmg), 2.5 s cooldown. Damage plumbing generalized via `IDamageable`
  (PlayerCombat implements it; hitscan queries the interface; rocket splash
  is an OverlapSphere + dedup + the dist-guard that prevents negative-falloff
  pull). Turrets grant kill credit through the standard NotifyHit path,
  mirror PlayerCombat's damage contract (IsDead entry guard, died edge,
  per-hit confirms), and stop COLD on death (enabled=false — corpses never
  beam; the session's recurring death-frame lesson).
- **Enemies (`EnemySpawner`):** 6 stationary drones = the practice-bot
  pattern (Player prefab + BotDriver) at authored posts on REAL colliders
  (stage ×2, west launch planet, mid perch, gate perch ×2 — the midsection
  is deliberately collider-free, spinning geometry never gets colliders).
  40 HP via config swap (`pc.Config = enemyConfig; ServerResetForNewRound()`
  rebuilds the core — no PlayerCombat changes). Removal: death (0.6 s
  linger) or kill-plane (immediate — PlayerCombat's handler would otherwise
  full-heal and the motor would pile them at arena center); both paths sweep
  the drone's in-flight rockets (a destroyed shooter reads as attacker==null
  → false suicide). Full repopulate at RunStarted + RunReset: pre-clock
  kills and heat-pumping buy nothing. The B-key practice bot is disabled
  while a run is live (it was an infinite score fountain). Movers are OUT
  until BotBrain grows edge-sensing — orbit-strafe walking off ledges is a
  recorded playtest verdict, not a hunch.

### 3.5 The Voice of the Machine (MachineVoiceCore + MachineVoice)
- The Gyroscope is the antagonist: an ORIGINAL machine divinity in the
  SHODAN archetype (spec §1 Identity). Never System Shock names/lines —
  the archetype is the homage, the text is ours.
- `MachineVoiceCore` (pure, 10 tests, seeded): per-trigger line pools drawn
  shuffle-bag (no repeats until exhausted), 4 s chatter cooldown, priority
  triggers (deaths, ranks, NewBest, Chain) always speak. NewBest and Rank
  are EXCLUSIVE on completion (both are priority — the second would
  overwrite the first before one frame rendered).
- `MachineVoice` (MB): maps events → triggers (kill/chain/max-heat via
  OnHitConfirmed + MusicCore heat; death cause via
  `RunManager.LastResetCause` — nested enum PostGrade/Death/Void, set
  BEFORE `RunReset` fires; rank on the Ready→Complete transition;
  `LastRunWasBest` computed after `Core.CompleteRun()` and before the
  persist block). Boot guard: LevelSwitch taunts only after the first
  Update frame (RunManager.Start's SelectLevel(0) fires LevelChanged).
  Renders OnGUI with glitch-materialize (chars corrupt for ~0.5 s).
- Death glitch: `GlitchPass.shader` (procedural corruption overlay) on a
  camera-parented quad, spawned by the LOCAL CombatFeedback on RunReset
  when the cause is not PostGrade; reuses `ImpactFrameFade` (Life 0.35).

### 3.6 Stacked-PR merge procedure (learned twice)
`gh pr merge` on a stack merges into the PR's *base branch*, and deleting a
base auto-closes its children. Either (a) merge the TIP branch to main in one
PR, or (b) bottom-up: retarget each PR to main first — `gh api
repos/<owner>/<repo>/pulls/<n> -X PATCH -f base=main` (NOT `gh pr edit
--base`, which currently 500s on a projectCards deprecation) — then merge.

## 4. Scene generation (PlaygroundBuilder)

`MovementPlayground.unity` is 100% generated. **Never hand-edit it** — change
the builder and rebuild (menu `AlienSkies/...` or `-executeMethod
AlienSkies.EditorTools.PlaygroundBuilder.BuildNeonVector`).

- **One style:** `BuildNeonVector` is the only entry point (the StyleMode A/B
  enum and the packs-based Style A died in Phase 2 — NeonVector won).
- **Arena vocabulary** (helpers): `CreatePlatform` (box + 4 emissive trim
  strips), `CreateDisc` (cylinder; capsule collider REPLACED with a
  MeshCollider or the surface is a dome), `CreateSegmentRing` (light-dash
  ring; pass `colliders:false` for anything that spins), `CreateSpire`,
  `CreateBeacon`, `CreateWorld` (style-aware planet: NeonPlanet sphere or
  SgtPlanet + collider radius 1 + `PlanetSurface`).
- **Layout is authored** (the Gyroscope: stage y=24 + atrium column, cardinal
  launch planets, diagonal wings + colored compass beacons, helix, spire
  gardens, NE asteroid field, SW shattered world, disc outer ring, backdrop
  worlds >160m). Looks come from `lookRng(1337)`; misc authored-random uses
  its own streams. Spawns: wings + 2 mid-ring, never the stage.
- **The player is a scene instance**: the builder instantiates
  `Player.prefab` at the first spawn point at build time — no connect flow,
  no scene camera, the game starts in the arena.
- `SlowSpin` = decorative rotation. **NEVER put colliders under one** —
  the movement sim assumes static fight surfaces.
- Tirgames materials ship built-in-RP Standard shader (renders magenta in
  URP): `ConvertWeaponMaterialsToUrp` fixes them in place, idempotent —
  the pattern for every built-in-RP pack.

### 4.1 Prefab pair rule
`Player.prefab` references `Rocket.prefab` internals by fileID, and both are
regenerated by the builder in one pass (Rocket saves over the same path — no
DeleteAsset — so fileIDs survive). After every rebuild, commit whichever of
the two changed TOGETHER; never revert one in isolation. Symptom of
violation: fire sound plays, no rocket spawns (silent no-op). (The old
prefab-TRIO rule with DefaultPrefabObjects died with FishNet.)

## 5. Art direction

- **Palette** (`Palette.cs`) is the single color source: Magenta, Cyan,
  Violet, Green, Amber, Ink + `Planets[]`. Color language: cyan = structure/
  info, green = movement state, amber = opportunity/objective (stage trim,
  spire tips, halo), magenta = threat/kill, violet = movement tech (zip,
  helix).
- **Shaders** (all hand-written CG, unlit + HDR for bloom):
  - `NeonRim` — flat base + fresnel rim + sun shading; the whole vector look.
    Caveat: viewmodel-distance objects flood at glancing angles — rimPower
    3+ / lower intensity / lifted base for close objects.
  - `NeonPlanet` — procedural worlds: fbm continents, glowing coasts, polar
    caps, day/night terminator, HDR atmosphere rim, slow spin, `_Banding`
    dial for gas giants; seeded from world position. Born from the verdict
    "they just look like balls".
  - `GlowOrb` — banded storm ball (sun).
  - `CosmicSkybox` — the nebula sky.
- **FX (100% code, since Phase 2):** explosions = expanding violet HDR core
  sphere + magenta fresnel shockwave shell (`ExplosionShell` shader, additive,
  `_Progress` fade) + 10 radial cyan/amber debris streaks; hitscan impacts =
  small cyan spark; tracers unchanged. All spawned by `CombatFx`, no prefabs.
  **Shader-stripping rule (hard-won):** a shader acquired only via runtime
  `Shader.Find` is stripped from player builds and dies ONLY in the shipped
  exe — every runtime shader needs an asset-material reference chain from a
  shipped prefab (`PlayerCombat.ExplosionShellMaterial` → ExplosionShell.mat).
  The old `ApplyPaletteShift` recolor-any-authored-effect pattern lives in git
  history (pre-Phase-2 CombatFx.cs) if packs ever return.
- **Impact frames:** `ImpactFrame.shader` (hand-written, ZTest Always) on a
  camera-parented quad prefab, `ImpactFrameFade` drives `_Progress` over 0.4s
  — full-screen b/w anime pop on kill, owner-local. White is HDR-boosted
  (LDR 1.0 tonemaps to mid-gray).
- **Viewmodels:** `BuildProceduralWeapon` — primitive assemblies (nose +Z),
  NeonRim `NeonGun_{w}` materials, no colliders. Silhouettes + rim light, no
  models.
- **Screenshot-iterate** is the visual workflow; but SGT lit/volumetric
  content renders unfaithfully headless — user play-mode screenshots are
  ground truth for Style A planets.

## 6. Verification channels

| Channel | Command | Verifies |
|---|---|---|
| EditMode tests | `bash tools/run_tests.sh` | every core; hitscan raycast geometry vs real prefabs |
| Scene rebuild | `-executeMethod ...PlaygroundBuilder.BuildNeonVector` | builder + compile |
| Screenshots | `-executeMethod ...SceneScreenshot.Capture` | spawn/wide/up/arena_top views, weapons (viewmodel materials + orientation), fx (pre-warmed via `ParticleSystem.Simulate`), impactframe |
| Player build | `bash tools/build_player.sh` | end-to-end; auto-installs music masters |

Editor must be CLOSED for all headless work. Playtest = the only channel for
feel; everything above just proves "not broken".

## 7. Tuning

`Assets/AlienSkies/Config/*.asset` — inspector-editable, no recompile.
**Snapshot rule:** the `.asset` snapshots C# defaults at creation — when
changing a default, edit the C# struct AND the .asset AND the test together
(they have drifted apart before).

Current headline numbers: rocket 80 dmg / 150 m/s / 6 m splash / knockback 22
/ self×0.5; rifle 20 dmg / 0.35 s; shotgun 20×6 dmg / 7° / 0.9 s; HP 100,
respawn 3 s; zip 60 m/s / 3 s cd; grapple range 120; planet launch 26; KillY
−80. Run (RunConfig.asset): kill 100 × mult x1..x4, time bonus 10/s under
par, S target 2000 (A/B/C at 75/50/25%), grade dwell 2.5 s. Pars per level:
Sphere 1 = 90 s, Sphere 2 = 75 s (LevelDef, authored in the builder).
Enemies (EnemyCombatConfig.asset): 40 HP, everything else player defaults.

## 8. Assets policy & inventory

**ADR-004: zero store assets — all visuals are code.** Phase 2 (2026-08-28)
deleted Tirgames, Hovl, SGT/CW, and Vefects (~1.1 GB); their roles are filled
by hand-written shaders + code-built geometry. Do not add pack dependencies.

| Asset | Status |
|---|---|
| Laser Weapons Sound Pack | KEPT (audio, not visuals) — free tier, but no license file ships in the repo; **open user decision**: verify the license or replace with procedural/owned SFX |
| Music | NEVER committed — masters outside the repo, streamed at runtime |

## 9. Known gaps / parked

- Bot/enemy rocket launches: audible since 2026-08-29 (spatial one-shot in
  the shared FireRocket path); explosions were always world-audible.
- Bot: rockets only, no grapple/zip. MOVERS EXIST since 2026-08-29: BotBrain
  edge-sensing (EdgeAhead in Sense; latched retreat along the reverse of the
  previous wish — NOT toward the target, who is usually off-platform; hop
  suppressed; orbit sign flips once per encounter), one orbiter per sphere
  with platform-scaled OrbitRadius (7/7/4.5). Known steady state, accepted:
  with the player off-platform an orbiter guards the player-facing rim (any
  radius); rocket knockback can still shove one off — destroyed, no kill
  credit (arcade rule; S-attainability minus one drone = playtest item).
- Aim assist: the 3 m-thick spherecast tube IS the design (resolved
  2026-08-28 — the old First Blood spec's "~5° cone" wording was the drift).
- A/B style verdict + hybrid consolidation pending.
- Sky pass 2 (procedural black hole/star/belts — also the user's original
  "universe you can view" dream), music intensity mapping tune, Ovani-style
  stem crossfades (MusicCore.Level is already the driver).
- User fear to keep answering with shipped momentum: "will a game come out
  the other end?" — games die from un-fun loops, not mid art.
