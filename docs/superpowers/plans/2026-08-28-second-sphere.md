# Second Sphere Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two selectable levels (TAB at the start line): Sphere 1 = the existing crossing; Sphere 2 = a vertical climb to a gate above the high anchor worlds. Every gate gets a beacon pillar.

**Architecture:** `LevelDef` is pure data (start, par, S-target, posts, gate object, bests-key prefix) authored by the builder into `RunManager.Levels`. Selecting a level rebuilds `RunCore` with overridden settings (`RunSettings.WithOverrides` — pure, tested), toggles gate GameObjects, repositions the player, and repopulates enemies via `EnemySpawner.SetPosts`. No new simulation.

**Tech Stack:** Unity 6000.5.1f1. Gates: `bash tools/run_tests.sh` — **100** from Task 1 on (99 + 1 WithOverrides test); builder; screenshots; `tools/build_player.sh` ("PlayerBuilder: Succeeded").

**Spec:** `docs/superpowers/specs/2026-08-28-second-sphere-design.md`

## Global Constraints

- Unity Editor CLOSED for every headless command (`tasklist.exe | grep -i unity`).
- Repo root: `/mnt/c/Users/computer/My project` (quote the space). Branch: `feature/second-sphere` (off post-merge main).
- Git author the owner's git identity; NO Co-Authored-By/Claude attribution. Commit `.meta` files (new C# files get theirs on the first headless run — commit those too).
- Never hand-edit `MovementPlayground.unity`; the builder regenerates it (Task 3).
- Pure cores untouched except the ONE sanctioned addition named below (`RunSettings.WithOverrides`).
- Every task ends with `bash tools/run_tests.sh` → PASS 100, then a commit. On failure read `TestResults/editmode.xml` + `TestResults/editmode.log` (grep `error CS`).
- Phase-4 doctrine: every authored position must sit 1–2m above a real collider within its footprint — verify against the builder's actual geometry code and report the real numbers in deviations.
- Keys 1/2/3 are weapon-switch — level select is TAB only, and ONLY in Ready state.

---

### Task 1: LevelDef + RunManager levels + EnemySpawner.SetPosts + HUD

**Files:**
- Create: `Assets/AlienSkies/Scripts/LevelDef.cs`
- Modify: `Assets/AlienSkies/Scripts/RunSettings.cs` (add `WithOverrides`)
- Modify: `Assets/AlienSkies/Tests/RunCoreTests.cs` (add ONE test for it → 100 total)
- Modify: `Assets/AlienSkies/Scripts/RunManager.cs` (levels, TAB select, per-level bests/start)
- Modify: `Assets/AlienSkies/Scripts/EnemySpawner.cs` (`SetPosts` + level-change subscription)
- Modify: `Assets/AlienSkies/Scripts/MovementHUD.cs` (level name + TAB cue + grade-screen name)

**Interfaces:**
- Produces: `AlienSkies.Run.LevelDef` (below); `RunSettings WithOverrides(float parTime, int sTargetScore)`; `RunManager.Levels`, `CurrentLevel`, `CurrentLevelDef`, `event System.Action LevelChanged`, `void SelectLevel(int index)`; `EnemySpawner.SetPosts(Vector3[] posts)`.
- Consumes: everything already on RunManager/EnemySpawner from Phases 3–4.

- [ ] **Step 1: LevelDef.cs:**

```csharp
using UnityEngine;

namespace AlienSkies.Run
{
    /// <summary>One sphere: pure authored data, built by PlaygroundBuilder
    /// into RunManager.Levels. The Gate object (ring + beacon + trigger) is a
    /// scene child the RunManager toggles per selection.</summary>
    [System.Serializable]
    public class LevelDef
    {
        public string Name = "SPHERE 1";
        public Vector3 Start;
        public float ParTime = 90f;
        public int STargetScore = 2000;
        public Vector3[] Posts = new Vector3[0];
        public GameObject Gate;
        public string BestKeyPrefix = "sphere1"; // sphere1 keeps its old bests
    }
}
```

- [ ] **Step 2: RunSettings.WithOverrides** (sanctioned pure addition — a level swaps par/target without touching the shared asset):

```csharp
        /// <summary>Copy with per-level overrides — levels swap par/target
        /// without mutating the shared config asset.</summary>
        public RunSettings WithOverrides(float parTime, int sTargetScore) =>
            new RunSettings
            {
                ParTime = parTime,
                KillScore = KillScore,
                MaxMultiplier = MaxMultiplier,
                TimeBonusPerSecond = TimeBonusPerSecond,
                STargetScore = sTargetScore,
                RestartDelay = RestartDelay,
            };
```

  And ONE test in RunCoreTests.cs:

```csharp
    [Test]
    public void WithOverrides_SwapsParAndTarget_KeepsTheRest()
    {
        var s = new RunSettings { ParTime = 90f, STargetScore = 2000, KillScore = 123 };
        var o = s.WithOverrides(75f, 1500);
        Assert.AreEqual(75f, o.ParTime);
        Assert.AreEqual(1500, o.STargetScore);
        Assert.AreEqual(123, o.KillScore);
        Assert.AreEqual(90f, s.ParTime); // original untouched
    }
```

- [ ] **Step 3: RunManager changes** (read the current file; apply as a coherent edit):
  1. Fields: `public LevelDef[] Levels = new LevelDef[0];` `public int CurrentLevel { get; private set; }` `public LevelDef CurrentLevelDef => Levels != null && Levels.Length > 0 ? Levels[Mathf.Clamp(CurrentLevel, 0, Levels.Length - 1)] : null;` `public event System.Action LevelChanged;`
  2. Bests keys become per-level: replace the two consts with `string BestScoreKey => (CurrentLevelDef?.BestKeyPrefix ?? "sphere1") + "_best_score";` (same for time). Load bests in `Awake` AND on every `SelectLevel`.
  3. `Start()`: after `BindPlayer()`, if `Levels.Length > 0` call `SelectLevel(0)` (idempotent first selection: positions the player at the level start, arms the right gate — the builder also places the player at Sphere 1's start, so this is a no-op teleport on boot).
  4. `_runStart` becomes `CurrentLevelDef != null ? CurrentLevelDef.Start : <the captured spawn position>` — keep the BindPlayer capture as the no-levels fallback.
  5. TAB handling in the Ready branch (BEFORE the movement check so switching never starts the clock):

```csharp
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    if (kb != null && kb.tabKey.wasPressedThisFrame && Levels.Length > 1
                        && Cursor.lockState == CursorLockMode.Locked) // project convention: all gameplay keys are lock-gated
                    {
                        SelectLevel((CurrentLevel + 1) % Levels.Length);
                        break; // reposition this frame; movement check resumes next
                    }
```

  6. `SelectLevel`:

```csharp
        /// <summary>Switch sphere — start line only (Ready). Rebuilds RunCore
        /// with the level's par/target, toggles gates, moves the player,
        /// repopulates enemies (via LevelChanged), loads that level's bests.</summary>
        public void SelectLevel(int index)
        {
            if (Core.State != RunState.Ready) return;
            if (Levels == null || Levels.Length == 0) return;
            CurrentLevel = Mathf.Clamp(index, 0, Levels.Length - 1);
            var lvl = Levels[CurrentLevel];

            Core = new RunCore(S.WithOverrides(lvl.ParTime, lvl.STargetScore));
            for (int i = 0; i < Levels.Length; i++)
                if (Levels[i].Gate != null) Levels[i].Gate.SetActive(i == CurrentLevel);

            BestScore = PlayerPrefs.GetInt(BestScoreKey, 0);
            BestTime = PlayerPrefs.GetFloat(BestTimeKey, 0f);

            if (_motor != null) _motor.RespawnAt(lvl.Start);
            if (_music != null) _music.Core.ResetHeat();
            LevelChanged?.Invoke();
        }
```

  (Adapt property/const names to the real file. The initial `Core = new RunCore(S)` in `Awake` stays — `SelectLevel(0)` in Start replaces it when levels exist. `CompleteRun`'s best-persist writes must use the property-based keys.)
- [ ] **Step 4: EnemySpawner:** add

```csharp
        /// <summary>Levels hand their posts in here; the serialized Posts
        /// array remains the no-levels fallback.</summary>
        public void SetPosts(Vector3[] posts)
        {
            Posts = posts ?? new Vector3[0];
            Repopulate();
        }
```

  and in `Start()`, after the existing subscriptions: `if (RunManager.Instance != null) { RunManager.Instance.LevelChanged += HandleLevelChanged; HandleLevelChanged(); }` with `void HandleLevelChanged() { var lvl = RunManager.Instance != null ? RunManager.Instance.CurrentLevelDef : null; if (lvl != null) SetPosts(lvl.Posts); }` (+ matching unsubscribe in `OnDestroy`). **The immediate `HandleLevelChanged()` call after subscribing is load-bearing (review):** Start order between RunManager and EnemySpawner is undefined — if RunManager.Start's `SelectLevel(0)` fired LevelChanged before this subscription existed, the pull-once makes it order-proof.
- [ ] **Step 5: HUD:** next to the SCORE line add the level name (`run.CurrentLevelDef?.Name`); in Ready state (`run.Core.State == RunState.Ready` and `Levels.Length > 1`) draw a cue bottom-center: `"TAB — next sphere"` (amber `_cue` style); on the grade screen include the name in the stats line. Adapt to the file's ShadowLabel/rect idioms.
- [ ] **Step 6: Gate.** `bash tools/run_tests.sh` → PASS **100**.
- [ ] **Step 7: Commit** — `feat: LevelDef + level select (TAB at the start line) + per-level bests`

---

### Task 2: Builder — gates with beacons, two authored spheres

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs`

**Interfaces:**
- Consumes: `LevelDef`, `RunManager.Levels`, existing gate code (ring + trigger from Phase 3), `NeonMaterial`, arena geometry.
- Produces: `GameObject BuildGate(string name, Vector3 center, ...)` returning a root with ring + **beacon pillar** + ExitGate trigger; two gates; `RunManager.Levels` authored with both spheres; the player still spawns at Sphere 1's start.

- [ ] **Step 1: Refactor the existing gate code into `BuildGate`** — the Phase 3 gate (ring + trigger, in `BuildArena`, ~794–806) becomes a helper: root GameObject `name`, children = the amber segment-ring, the trigger sphere + `ExitGate`, and NEW: the beacon — a thin vertical cylinder (`CreatePrimitive(PrimitiveType.Cylinder)`, collider DestroyImmediate'd, `localScale = (0.6f, 20f, 0.6f)` — Unity's cylinder mesh is 2 units tall, so this is a 40m pillar; position it `center + Vector3.up * 20f` so its BASE sits at the gate). **Beacon material (review-pinned — the ring is URP/Unlit, NOT NeonRim, and NeonRim's dark base would undercut findability):** a NEW unlit material via the existing beacon recipe — `GetOrCreateMaterial("GateBeacon", () => new Material(Shader.Find("Universal Render Pipeline/Unlit")))` with `_BaseColor = AlienSkies.Palette.Amber * 3f` (the CreateBeacon precedent at ~875–878). Do NOT reuse/rename any existing Neon material. Return the root. Sphere 1's gate keeps its current position (the far perch).
- [ ] **Step 2: Sphere 2 geometry** — review resolved the real numbers (verify in-file, report deviations if the code differs):
  - **Start:** `pts[2].position` — expected ≈ (−38.9, 9.0, −38.9). The registry holds 4 wings THEN 2 perch spawns; a naive farthest-from-`pts[0]` search would pick a PERCH spawn (pts[4]) — restrict to wing indices 0–3, or just use index 2 with the expected value as the check. Do NOT use `SpawnPointRegistry.Farthest`.
  - **Gate:** the high anchor worlds pass `10f` to `CreateWorld` — that is a DIAMETER (radius 5; tops y=60 and y=67). Gate above the higher world: `gateCenter = (16f, 73f, -8f)` (center + radius + 6).
  - **Posts (6), all on verified colliders:** the 2 stage posts (shared literal, Step 3), 2 launch-planet tops — confirmed r=7, top y=17: `(-30f, 18f, 0f)` and `(30f, 18f, 0f)` (verify the east planet's real center) — and the 2 high-anchor tops 1m above the poles: `(-14f, 61f, 10f)` and `(16f, 68f, -8f)` (verify both world centers in-file).
- [ ] **Step 3: Author `RunManager.Levels`.** **SCOPE BRIDGE (review blocker — the recurring Build()/BuildArena/BuildPlayer trap):** the gates are `BuildArena` locals; the RunManager lives in `BuildPlayer`; `FindFirstObjectByType` is FORBIDDEN here (it excludes inactive objects — Sphere 2's gate would arrive null and the level would be silently unfinishable). Thread references explicitly:
  - `static (GameObject gate1, GameObject gate2) BuildArena()` — return both gate roots from the refactored BuildGate calls;
  - in `Build()`: `var (gate1, gate2) = BuildArena(); BuildPlayer(config, combatConfig, runConfig, enemyCombatConfig, gate1, gate2);` (extend BuildPlayer's parameter list);
  - single-source the Sphere 1 posts (review: the plan previously contradicted itself): `var sphere1Posts = new[] { ...the existing 6-post literal... };` then `enemies.Posts = sphere1Posts;` (fallback stays wired) AND `Levels[0].Posts = sphere1Posts;`
  - `gate2.SetActive(false)` ONLY AFTER `runMgr.Levels` stores the reference.

  The authoring literal:

```csharp
            runMgr.Levels = new[]
            {
                new AlienSkies.Run.LevelDef
                {
                    Name = "SPHERE 1 — THE CROSSING",
                    Start = /* pts[0].position */,
                    ParTime = 90f, STargetScore = 2000,
                    Posts = /* the existing 6 posts array — move the literal here */,
                    Gate = gateSphere1, BestKeyPrefix = "sphere1",
                },
                new AlienSkies.Run.LevelDef
                {
                    Name = "SPHERE 2 — THE CLIMB",
                    Start = /* far wing position */,
                    ParTime = 75f, STargetScore = 2000,
                    Posts = /* the 6 climb posts */,
                    Gate = gateSphere2, BestKeyPrefix = "sphere2",
                },
            };
```

  Scene-object references (gates) into a serialized scene component are fine — both live in the same scene. (Posts single-sourcing and the SetActive ordering are specified in the SCOPE BRIDGE block above — follow that, not older phrasing.)
- [ ] **Step 4: Gate.** `bash tools/run_tests.sh` → PASS 100.
- [ ] **Step 5: Commit** — `feat: builder authors two spheres - beacon gates, climb route`

---

### Task 3: Regenerate + full verification

- [ ] **Step 1:** Rebuild scene (`BuildNeonVector`; exit 0; zero `missing`/`error CS` in the log).
- [ ] **Step 2:** `bash tools/run_tests.sh` → **100/100**.
- [ ] **Step 3:** Screenshots — 7 fresh PNGs; `shot_arena_top.png`/`shot_wide.png` show Sphere 1's beacon. **Sphere 2's gate is saved INACTIVE and invisible to the normal shots (review)** — add one dedicated shot to `SceneScreenshot.Capture`: locate the Sphere-2 gate root (`FindObjectsByType` with `FindObjectsInactive.Include`, or by name), `SetActive(true)`, shoot `shot_gate2.png` framing the high anchors (camera ≈ (60, 85, −60) looking at (16, 73, −8)), restore inactive before the remaining shots.
- [ ] **Step 4:** `bash tools/build_player.sh` + "PlayerBuilder: Succeeded" in a full log.
- [ ] **Step 5:** Commit regenerated scene/prefab churn — `build: regenerate scene with two spheres`.
- [ ] **Step 6:** STOP — orchestrator reviews the beacon in the shots, then owns docs/push/PR.

---

### Task 4: Docs + push + PR (orchestrator-owned)

ARCHITECTURE.md (§3.4: levels paragraph; §7: per-level pars), README (two spheres + TAB), CLAUDE.md (RESUME/diary), memory, push, `gh pr create` (new PR off main).
