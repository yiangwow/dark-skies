# First Blood — networked 1v1 duel

- **Date:** 2026-07-04
- **Status:** Approved (user, in-session)
- **Vision:** Gurren Lagann × STRAFTAT × Quake — a momentum-movement arena duel in
  a cosmic sky. This spec turns the movement toy into the first playable *game*:
  guns, health, dying, respawning, and real 1v1 over the network.

## Decisions (locked with user)

1. **Fish-Net from day one.** Combat is built networked from the first commit —
   no offline combat that gets retrofitted later. `MovementCore`'s pure-C# tick
   architecture is exactly what Fish-Net client-side prediction re-simulates.
2. **Controls change:** grapple moves from RMB to **F** (hold). **LMB = fire,
   always — including while grappling.** **RMB while grappling = zip-strike** to
   the anchor (RMB does nothing when not grappling; reserved for alt-fire later).
   (Revised 2026-07-04 from an earlier LMB-zip idea so shooting mid-swing works.)
3. **Weapons v1:** rocket launcher, hitscan rifle, shotgun (rifle and shotgun
   share the hitscan path). Infinite ammo, carry all three, switch 1/2/3 + wheel.
4. **Space Graphics Toolkit** purchased and imported by user (2026-07-04). Repo
   is private, so committing SGT is license-compliant. It stays uncommitted
   until PR #6 (committed there; no single file >100 MB — exclude or LFS any
   offender).
5. Quake source code is not used (GPL, C, engine-specific). We take its ideas
   via Fish-Net + our own quake-style core.

## Build order — one branch + PR each

### PR #3 — Zip-strike grapple (`feature/zip-strike`)

Vibe → mechanics: "target a platform and land on it quickly."

- Zip is available **only while grappling** (F held, hook attached). RMB then
  **overwrites** velocity: straight-line flight toward the anchor at
  `ZipSpeed` (60 m/s — tuned up from 45 after playtest vs ~80 m/s grapple
  flings), gravity and air-steering suppressed during zip.
- Arrival within `ZipArriveRadius` (2 m) auto-detaches and clamps speed to
  `ZipLandSpeed` (8 m/s) so you stick the landing instead of skidding off.
- Releasing F mid-zip cancels: keep current velocity, normal release boost applies.
- `ZipCooldown` (3 s) starts on use; HUD shows cooldown state.
- All numbers in `MovementConfig` (tunable in inspector).
- Bindings this PR: grapple RMB→F, zip on RMB-while-grappling. LMB does nothing
  until PR #5 (fire).
- Tests: zip overwrites momentum; gravity suppressed during zip; arrival clamps
  speed and detaches; cooldown blocks re-use; cancel keeps velocity.

**Done when:** tests green; user playtests "grapple a far platform, LMB, land on it."

### PR #4 — Fish-Net foundation (`feature/fishnet-foundation`)

Riskiest chunk first. No gameplay added.

- Install Fish-Net (free) + dependencies; commit to repo.
- Minimal connect UI: Host / Join (IP field), Tugboat UDP transport. Localhost
  and LAN only in v1; internet later via port-forward or dedicated server on the
  home server (Linux headless build).
- Player becomes a networked object: `MovementCore` ticks inside Fish-Net's
  client-side prediction (server-authoritative, client predicts + reconciles).
  Remote players render smoothly (interpolation).
- Grapple/zip must predict too (they're part of the tick).
- Scene/spawn: both players spawn on the spawn platform.

**Done when:** two instances on one PC (editor + built player) fly, grapple, and
zip around the same arena and see each other do it without rubber-banding at
localhost latency.

### PR #5 — Combat core (`feature/combat-core`)

- **CombatCore (pure C#, unit-tested):** health 100, damage application, death at
  0, respawn timer 3 s, spawn-point selection, frag/death counters. Falling past
  `KillY` = suicide (−1 frag).
- **Weapons** (stats in a `CombatConfig` ScriptableObject, all tunable):
  | Weapon | Damage | Rate | Notes |
  |---|---|---|---|
  | Rocket | 80 direct, splash radius 6 m linear falloff | 0.8 s | Projectile 25 m/s; knockback impulse up to 12 m/s fed into `MovementCore` velocity; self-damage ×0.5 → rocket jumps |
  | Rifle | 20 | 0.35 s | Hitscan ray |
  | Shotgun | 10 pellets × 6 | 0.9 s | Hitscan, 7° spread cone |
- Networking: firing is client-initiated, server-validated; hits and damage are
  server-authoritative. No lag compensation (rewind) in v1 — acceptable at
  LAN/localhost pings; noted as future work for internet play.
- HUD: health, weapon, frag counter, zip/grapple cooldown; simple kill feed.
- Death: brief camera detach/fade, respawn after 3 s at farthest spawn point.
- Match loop v1: endless frag counting + Tab scoreboard. Win conditions later.

**Done when:** two players duel — shoot, rocket-jump, die, respawn, track frags.

**Assets flagged for this PR** (user-approved 2026-07-04; acquire/wire when we
get here, buy against the built weapons not before): 1. Hovl "Realistic
Triple-A Sci-fi effects 2" — already purchased+imported, stays uncommitted
until used (rockets, explosions, muzzle flashes, impacts). 2. An FPS weapon
viewmodel pack (rocket launcher/rifle/shotgun) — candidates to be researched
at PR start. 3. A sci-fi weapon SFX pack — same. Declined as not-needed:
predecessor Hovl pack, Shiny SSR 2 (matte platforms + perf budget).

### PR #6 — Visuals: SGT integration (`feature/sgt-visuals`)

- User imports SGT via Package Manager (My Assets → Import; also install Burst —
  required for SGT galaxy/nebula features). Claude wires it in.
- Replace/augment hand-rolled planets: SGT planets with atmospheres, a spiral
  **galaxy** landmark (replaces GalaxyCore sphere), nebula + starfield depth.
  Keep the approved CosmicSkybox unless SGT's is clearly better in screenshots.
- Keep collision/grapple targets identical — visuals must not change gameplay.
- Screenshot-verify loop as usual; user gives the vibe verdict.

**Done when:** screenshots pass Claude's check + user says the sky is no longer dull.

## Architecture invariants

- Simulation logic lives in pure C# cores (`MovementCore`, `CombatCore`) with
  EditMode tests; MonoBehaviours are thin glue. This is also what makes
  prediction work.
- All feel numbers live in ScriptableObject configs, editable in inspector.
- Scene stays code-generated (`PlaygroundBuilder`); spawn points added there.
- Headless verify loop unchanged: `tools/run_tests.sh`, scene rebuild,
  screenshots. Fish-Net multiplayer behavior is verified by playtest (editor +
  headless-built Windows player), not EditMode tests.

## Out of scope (v1)

Ammo/health pickups, more weapons, bots, matchmaking, Steam, lag compensation,
dedicated server hosting (planned later on the home server), win conditions,
audio.

## Risks

- **Fish-Net prediction learning curve** — mitigated by doing it first (PR #4)
  with zero gameplay stacked on top.
- **SGT scale/perf in a fast arena** — mitigated by screenshot+FPS check before
  replacing the hand-rolled sky wholesale.
- **Grapple-hand ergonomics (F hold + RMB zip + LMB fire simultaneously)** —
  playtest checkpoint in PR #5.
