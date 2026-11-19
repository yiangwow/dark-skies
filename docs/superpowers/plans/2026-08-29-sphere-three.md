# Sphere 3 — THE PLUNGE Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Checkbox steps.

**Goal:** A third level, pure builder data on the proven LevelDef system: start atop the higher anchor world, plunge and cross the whole Gyroscope, finish at a gate hovering over Sphere 1's start wing. Denser combat (7 posts), par 60 s, S at 2200.

**Architecture:** NO runtime code changes. One more `LevelDef` in the builder's `Levels` array, one more `BuildGate` call, one authored post set. `EnemySpawner`/`MachineVoice`/HUD/TAB all handle a third level automatically (TAB cycles `% Levels.Length`).

**Safety note (pre-reviewed):** Sphere 3's gate sits AT Sphere 1's spawn (38.9, 9, 38.9). This is safe by construction: inactive gates have no trigger; `SelectLevel` teleports the player (RespawnAt) in the same Update frame it activates the gate — physics steps after Update, so the player is never inside the freshly-activated trigger; and `CompleteRun` no-ops unless `State == Running` (a fresh `SelectLevel` leaves Ready). Verify these three facts against the real code rather than assuming.

**Gate:** `bash tools/run_tests.sh` → **110** (no new tests — data only). Truth-telling `tools/build_player.sh`.

## Global Constraints

Branch `feature/overnight-polish`; repo `/mnt/c/Users/computer/My project` (quote the space); Unity Editor closed for headless; author `yiangwow`, no attribution lines; commit `.meta`s; never hand-edit the scene; every position 1–2 m above a REAL collider within its footprint — verify against the actual builder geometry and report real numbers in deviations. Gate centers (gateSphere1/2/3) are exempt from this 1–2 m rule — matching the existing gate1/gate2 precedent, which sit 2.6 m and 6 m above their nearest collider respectively; the constraint applies to enemy posts and each LevelDef.Start only.

---

### Task 1: Builder — the third sphere

**Files:** Modify `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` only.

- [ ] **Step 1: The gate** — a third `BuildGate` call: `gateSphere3` with `gateCenter = pts[0].position` exactly — the ring/beacon/trigger root at `(38.891f, 9f, 38.891f)` (Sphere 1's start wing spawn point; reference the real `pts[0]` variable, don't hardcode). Gate centers are exempt from the Global Constraints' 1–2 m rule (see above). Thread it alongside gate1/gate2 (extend the BuildArena return tuple + BuildPlayer params — same pattern as the review-mandated Second Sphere bridge).
- [ ] **Step 2: The LevelDef** — append to `runMgr.Levels`:

```csharp
                new AlienSkies.Run.LevelDef
                {
                    Name = "SPHERE 3 — THE PLUNGE",
                    Start = new Vector3(16f, 68f, -8f), // higher anchor world top (r5, pole y=67, +1)
                    ParTime = 60f, STargetScore = 2200,
                    Posts = sphere3Posts,
                    Gate = gateSphere3, BestKeyPrefix = "sphere3",
                },
```

  with (verify every anchor in-file; wing platform extent must contain the two wing posts — read the wing construction and adjust to stay on the surface):

```csharp
            var sphere3Posts = new[]
            {
                new Vector3(-14f, 61f, 10f),   // lower anchor world top — the jump-off guard
                new Vector3(30f, 18f, 0f),     // east launch planet top
                new Vector3(-30f, 18f, 0f),    // west launch planet top
                sphere1Posts[0],               // stage
                sphere1Posts[1],               // stage
                new Vector3(35f, 7.5f, 35f),   // gate-wing approach guard
                new Vector3(43f, 7.5f, 43f),   // gate-wing approach guard
            };
```

  `gateSphere3.SetActive(false)` ONLY AFTER the Levels assignment (same rule as gate2).
- [ ] **Step 3: Gate.** `bash tools/run_tests.sh` → PASS 110. Commit — `feat: Sphere 3 THE PLUNGE - descent route, 7 posts, par 60`

### Task 2: Regenerate + verify + commit

- [ ] Rebuild scene (exit 0, zero missing/error); tests 110/110; screenshots (8 fresh — extend the shot_gate2 pattern? NO: keep scope tight, gate 3 is verified by the scene YAML: grep the saved scene for the `GateSphere3` GameObject and confirm `m_IsActive: 0` on that root); `bash tools/build_player.sh` exit 0; commit regenerated artifacts — `build: regenerate scene with three spheres`.
