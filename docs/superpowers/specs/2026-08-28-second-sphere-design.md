# Second Sphere — Levels Milestone

**Date:** 2026-08-28 · **Status:** approved (user: "I approve it so far, continue" — First Run milestone merged as PR #22) · **Builds on:** `2026-08-28-singleplayer-pivot-design.md`

## Vision

Turn *a run* into *a game with levels*. Two spheres with distinct movement
identities in the existing arena, selectable at the start line:

- **SPHERE 1 (exists):** *the crossing* — start wing → across the Gyroscope →
  amber gate on the far perch. Par 90s.
- **SPHERE 2 (new):** *the climb* — start at the opposite wing → up through
  the launch planets → gate floating above the high anchor worlds (y≈55–62).
  Grapple/launch-pad vertical route. Par 75s.

Plus the findability fix from the playtest checklist: every gate gets a tall
amber **beacon pillar** visible across the arena.

## Design

- **LevelDef** (serializable data, no new simulation): Name, Start,
  ParTime, STargetScore, Posts[], Gate (scene object: ring + beacon +
  trigger), BestKeyPrefix. Authored by the builder into `RunManager.Levels`.
- **Level select:** TAB at the start line ONLY (Ready state; keys 1/2/3 are
  weapon-switch — reviewed collision). Switching: reposition player to the
  level's start, toggle gate objects, repopulate enemies from the level's
  posts, rebuild RunCore with the level's par/S-target, load that level's
  PlayerPrefs bests (`{prefix}_best_score/time` — Sphere 1's existing keys
  stay valid).
- **EnemySpawner** learns `SetPosts()` + listens for level changes; its
  serialized Posts remain the no-levels fallback.
- **HUD:** level name by the score; a "TAB — next sphere" cue in Ready; the
  grade screen names the sphere.
- **All posts and the gate anchor on real colliders** (stage, cardinal launch
  planets, high anchor worlds — Phase 4 doctrine: verify against builder
  geometry, never author into void).

## Out of scope (deferred)

More than 2 spheres, a level-select UI beyond TAB, per-level music, movers
(BotBrain edge-sensing first), Steam anything.
