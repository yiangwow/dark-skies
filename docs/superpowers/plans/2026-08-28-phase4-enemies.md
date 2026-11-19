# Phase 4: Enemies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Populate the run with killable enemies — 6 drones in 3 pockets along the route — completing the First Run milestone: every system (movement, combat, scoring, music, grade) now has something to chew on.

**Architecture:** Maximum reuse: an enemy IS the proven practice-bot pattern (Player prefab + `BotDriver`), spawned by a new `EnemySpawner` scene singleton. Low HP via config swap (no PlayerCombat changes: assign `Config` then `ServerResetForNewRound()` — it rebuilds CombatCore from the new settings). Magenta body rim = threat. **All 6 enemies are STATIONARY posts on real colliders** (review: the orbit mode is documented-broken — that is why `Stationary` exists — and the route's midsection is deliberately collider-free, so positions are anchored to the stage / launch planet / perch discs). Enemies are removed on death (0.6s linger) AND on kill-plane (immediate — otherwise `PlayerCombat.HandleKillPlane` full-heals the drone and the motor teleports it to arena center: a review-caught free-heal-and-pile bug); their in-flight rockets are swept so a destroyed shooter can't charge the player a false suicide. The set repopulates at run START and RESTART (closes pre-clock sniping + heat pumping), and the B-key practice bot is disabled while a run is live (review: it was an infinite score fountain that would corrupt saved bests).

**Tech Stack:** Unity 6000.5.1f1. Gates: `bash tools/run_tests.sh` (**99** throughout — no new pure-core logic), builder, screenshots, `tools/build_player.sh` ("PlayerBuilder: Succeeded").

**Spec:** `docs/superpowers/specs/2026-08-28-singleplayer-pivot-design.md` (§4 EnemyDriver, Phase 4 in §6)

## Global Constraints

- Unity Editor CLOSED for every headless command (`tasklist.exe | grep -i unity`).
- Repo root: `/mnt/c/Users/computer/My project` (quote the space). Branch: `feature/singleplayer-pivot`.
- Git author the owner's git identity; NO Co-Authored-By/Claude attribution. Commit `.meta` files.
- Never hand-edit `MovementPlayground.unity`; the builder regenerates it (Task 3).
- Pure cores untouched (incl. BotBrain — variety comes from its existing `Stationary` flag).
- Every task ends with `bash tools/run_tests.sh` → PASS 99, then a commit. On failure read `TestResults/editmode.xml` + `TestResults/editmode.log`.
- Phase-2 landmines: no new runtime-only shaders here; the enemy body material must be an ASSET wired through the scene (`EnemySpawner.EnemyBodyMaterial`) — never runtime-only.

---

### Task 1: EnemySpawner + the small hooks

**Files:**
- Create: `Assets/AlienSkies/Scripts/EnemySpawner.cs`
- Modify: `Assets/AlienSkies/Scripts/RunManager.cs` (two events + heat reset at start)
- Modify: `Assets/AlienSkies/Scripts/BotDriver.cs` (one method: `SetStationary`)
- Modify: `Assets/AlienSkies/Scripts/Rocket.cs` (one accessor: `public PlayerCombat Shooter => _shooter;` — lets the spawner sweep orphaned rockets)
- Modify: `Assets/AlienSkies/Scripts/BotSpawner.cs` (B disabled while a run is live; live bot despawned at run start)
- Modify: `Assets/AlienSkies/Scripts/CombatFeedback.cs` (leak fix: `OnDestroy` also destroys the two procedural blip AudioClips — review: every enemy spawn creates them and Destroy(go) does not collect them)
- Modify: `Assets/AlienSkies/Scripts/PlayerMotor.cs` (leak fix: `OnDestroy` destroys the rope's runtime material)

**Interfaces:**
- Consumes: `RunManager` events (added below), `BotDriver`, `PlayerCombat` (`Config`, `ServerResetForNewRound`, `IsDead`), `PlayerMotor.BodyRenderer`.
- Produces: `EnemySpawner` with serialized `PlayerPrefab`, `EnemyConfig` (CombatConfig), `EnemyBodyMaterial`, `Posts` (Vector3[] — all stationary; movers are out of scope until BotBrain grows edge-sensing) — all wired by the builder in Task 2.

- [ ] **Step 1: RunManager hooks.** Add two events and fire them (also reset heat at START — review-derived exploit: firing before the clock pumps the multiplier):

```csharp
        /// <summary>Fired when the clock starts (Ready -> Running).</summary>
        public event System.Action RunStarted;
        /// <summary>Fired on every restart (death, kill-plane, post-grade).</summary>
        public event System.Action RunReset;
```

  In the Ready branch, replace `if (... > 0.05f) Core.StartRun();` with:

```csharp
                    if (new Vector3(v.x, 0f, v.z).sqrMagnitude > 0.05f)
                    {
                        Core.StartRun();
                        if (_music != null) _music.Core.ResetHeat(); // runs start cold — no pre-clock pumping
                        RunStarted?.Invoke();
                    }
```

  At the end of `RestartRun()` add `RunReset?.Invoke();`.
- [ ] **Step 2: BotDriver.SetStationary.** The brain is built in `Awake` (which runs during `AddComponent`), so a field can't arrive in time — expose a setter:

```csharp
        /// <summary>Spawners call this right after AddComponent: Awake has
        /// already built the brain, so mutate it directly.</summary>
        public void SetStationary(bool stationary) => _brain.Stationary = stationary;
```

- [ ] **Step 3: Write EnemySpawner.cs:**

```csharp
using UnityEngine;
using System.Collections.Generic;
using AlienSkies.Combat;
using AlienSkies.Movement;
using AlienSkies.Bots;

namespace AlienSkies.Run
{
    /// <summary>Populates the run with enemy drones: the practice-bot pattern
    /// (Player prefab + BotDriver, always stationary) at authored posts on
    /// real geometry, low HP via config swap, magenta threat-rim body.
    /// Removal paths: death (short linger so the explosion reads) and
    /// kill-plane (immediate — PlayerCombat's handler would otherwise
    /// full-heal the drone and the motor would pile it at arena center).
    /// Both paths sweep the drone's in-flight rockets so a destroyed shooter
    /// can't charge the player a false suicide. The full set repopulates
    /// when a run starts or restarts — pre-clock kills buy nothing.</summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        public GameObject PlayerPrefab;
        public CombatConfig EnemyConfig;      // low MaxHealth variant
        public Material EnemyBodyMaterial;    // magenta NeonRim — threat color
        public Vector3[] Posts = new Vector3[0];

        const float CorpseLinger = 0.6f; // let the death explosion read

        sealed class Drone
        {
            public GameObject Go;
            public PlayerCombat Pc;
            public PlayerMotor Motor;
            public System.Action OnFell;
            public float DeadAt = -1f;
            public bool Fell;
        }

        readonly List<Drone> _live = new List<Drone>();

        void Start()
        {
            if (RunManager.Instance != null)
            {
                RunManager.Instance.RunStarted += Repopulate;
                RunManager.Instance.RunReset += Repopulate;
            }
            Repopulate();
        }

        void OnDestroy()
        {
            if (RunManager.Instance != null)
            {
                RunManager.Instance.RunStarted -= Repopulate;
                RunManager.Instance.RunReset -= Repopulate;
            }
        }

        void Update()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var d = _live[i];
                if (d.Go == null) { _live.RemoveAt(i); continue; }
                if (d.Fell)
                {
                    Remove(d); // fell into the void: no linger, no heal, gone
                    _live.RemoveAt(i);
                    continue;
                }
                if (d.Pc.IsDead && d.DeadAt < 0f)
                    d.DeadAt = Time.time;
                else if (d.DeadAt >= 0f && Time.time - d.DeadAt > CorpseLinger)
                {
                    Remove(d);
                    _live.RemoveAt(i);
                }
            }
        }

        void Repopulate()
        {
            foreach (var d in _live)
                if (d.Go != null) Remove(d);
            _live.Clear();
            foreach (var p in Posts) Spawn(p);
        }

        void Spawn(Vector3 at)
        {
            if (PlayerPrefab == null) return;
            var go = Instantiate(PlayerPrefab, at, Quaternion.identity);
            go.name = "Enemy_Post";

            var driver = go.AddComponent<BotDriver>();
            driver.SetStationary(true);

            var pc = go.GetComponent<PlayerCombat>();
            if (EnemyConfig != null)
            {
                // Awake already built CombatCore from the player config;
                // swapping Config then resetting rebuilds it with enemy HP.
                pc.Config = EnemyConfig;
                pc.ServerResetForNewRound();
            }

            var motor = go.GetComponent<PlayerMotor>();
            if (EnemyBodyMaterial != null && motor.BodyRenderer != null)
                motor.BodyRenderer.sharedMaterial = EnemyBodyMaterial;

            var drone = new Drone { Go = go, Pc = pc, Motor = motor };
            drone.OnFell = () => drone.Fell = true; // handled next Update
            motor.KillPlaneCrossed += drone.OnFell;
            _live.Add(drone);
        }

        void Remove(Drone d)
        {
            if (d.Motor != null && d.OnFell != null)
                d.Motor.KillPlaneCrossed -= d.OnFell;
            // Sweep this drone's in-flight rockets: a detonation from a
            // destroyed shooter reads as attacker==null and charges the
            // victim a suicide (review-caught misattribution).
            foreach (var r in FindObjectsByType<Rocket>(FindObjectsSortMode.None))
                if (r.Shooter == d.Pc) Destroy(r.gameObject);
            Destroy(d.Go);
        }
    }
}
```
- [ ] **Step 4: Rocket accessor** — in `Rocket.cs`, add next to `Init`:

```csharp
        /// <summary>Who fired this — spawners sweep rockets whose shooter died.</summary>
        public PlayerCombat Shooter => _shooter;
```

- [ ] **Step 5: BotSpawner run-gating** — the practice bot was an unlimited score/heat fountain mid-run (review). In `BotSpawner`: (a) in `Update`, before the B-key check, add
  `if (AlienSkies.Run.RunManager.Instance != null && AlienSkies.Run.RunManager.Instance.Core.State == AlienSkies.Run.RunState.Running) return; // practice only at the start line`
  (b) in `Start`, subscribe `RunManager.Instance.RunStarted` to despawn the live bot:

```csharp
        void Start()
        {
            if (AlienSkies.Run.RunManager.Instance != null)
                AlienSkies.Run.RunManager.Instance.RunStarted += DespawnBot;
        }

        void OnDestroy()
        {
            if (AlienSkies.Run.RunManager.Instance != null)
                AlienSkies.Run.RunManager.Instance.RunStarted -= DespawnBot;
        }

        void DespawnBot()
        {
            if (_bot != null) { Destroy(_bot); _bot = null; }
        }
```

  (Also route the existing B-toggle despawn through `DespawnBot()` to keep one path.)
- [ ] **Step 6: Leak fixes** (review: every enemy spawn creates runtime objects `Destroy(go)` does not collect, and repopulate-per-restart is the game's core loop):
  - `CombatFeedback.cs`: in `OnDestroy` (it already unsubscribes events), add `if (_hitBlip != null) Destroy(_hitBlip); if (_killBlip != null) Destroy(_killBlip);` (match the real field names).
  - `PlayerMotor.cs`: add `void OnDestroy() { if (_rope != null && _rope.material != null) Destroy(_rope.material); }` — adapt: the rope material was created in `BuildRope`; store it in a field if needed to destroy the exact instance.
- [ ] **Step 7: Gate.** `bash tools/run_tests.sh` → PASS 99.
- [ ] **Step 8: Commit** — `feat: EnemySpawner - stationary drones, config-swap HP, fall/rocket sweeps, run-gated practice bot`

---

### Task 2: Builder — pockets, enemy config, threat material

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs`
- Produce (generated in Task 3, committed then): `Assets/AlienSkies/Config/EnemyCombatConfig.asset`

**Interfaces:**
- Consumes: `EnemySpawner`, existing arena geometry variables (read the file for real anchors), `NeonMaterial` helper, the loaded player prefab reference, `CombatConfig`.
- Produces: a scene `EnemySpawner` object fully wired; `EnemyCombatConfig.asset` (MaxHealth 40, everything else = player defaults); a `NeonEnemyBody` magenta material asset.

- [ ] **Step 1: Enemy combat config** — load-or-create `Assets/AlienSkies/Config/EnemyCombatConfig.asset` next to the other configs; ALWAYS set `enemyCombat.Settings.MaxHealth = 40f;` + `EditorUtility.SetDirty` (builder owns the value; the snapshot rule from §7 of the field manual).
- [ ] **Step 2: Threat material** — the exact call (review-verified against the NeonBody recipe at ~169):
  `var enemyBodyMat = NeonMaterial("NeonEnemyBody", AlienSkies.Palette.Magenta, 3.2f, 0.25f);`
- [ ] **Step 3: EnemySpawner scene object** — next to the RunManager creation in `BuildPlayer` (the prefab and configs are in scope there):

```csharp
            var enemyGO = new GameObject("EnemySpawner");
            var enemies = enemyGO.AddComponent<AlienSkies.Run.EnemySpawner>();
            enemies.PlayerPrefab = prefabAsset; // the loaded Player.prefab — match the real variable
            enemies.EnemyConfig = enemyCombatConfig;
            enemies.EnemyBodyMaterial = enemyBodyMat;
            // Three pockets along the route, every post ON REAL COLLIDERS
            // (review: the midsection is deliberately collider-free — spinning
            // geometry never gets colliders — so posts anchor to the stage,
            // the west launch planet, and the perch discs):
            enemies.Posts = new[]
            {
                new Vector3(6f, 25.5f, 6f),        // pocket 1: on the stage (y=24 + surface)
                new Vector3(-8f, 25.5f, -6f),      // pocket 1: stage far edge
                new Vector3(-30f, 18f, 0f),        // pocket 2: atop the west launch planet (center (-30,10,0), r7)
                new Vector3(-87.7f, 29.5f, -68f),  // pocket 2: the mid perch disc (surface y=28)
                new Vector3(-132f, 53.5f, -8f),    // pocket 3: gate perch guard (perch (-134.6, 52, -10.2))
                new Vector3(-137f, 53.5f, -12.5f), // pocket 3: gate perch guard
            };
```

  (Thread `enemyCombatConfig`/`enemyBodyMat` into `BuildPlayer` the same way `runConfig` traveled in Phase 3, or create them inside `BuildPlayer` — whichever matches the file's current structure. VERIFY EACH POST against the real geometry code while in the file: the west-planet center/radius (~417–423), both perch disc centers and radii (~724–732), stage extent (~411–412) — every post must sit 1–2m above a collider surface and within its footprint; adjust coordinates if the real numbers differ, and say so in deviations.)
- [ ] **Step 4: Gate.** `bash tools/run_tests.sh` → PASS 99.
- [ ] **Step 5: Commit** — `feat: builder places enemy pockets along the run route`

---

### Task 3: Regenerate + full verification

- [ ] **Step 1:** Rebuild scene (`BuildNeonVector`, log to `TestResults/rebuild_scene.log`, exit 0, zero `missing`/`error CS`).
- [ ] **Step 2:** `bash tools/run_tests.sh` → **99/99**.
- [ ] **Step 3:** Screenshots (7 PNGs, fresh). Enemies spawn at RUNTIME so they will NOT appear — expected; do not chase it.
- [ ] **Step 4:** `bash tools/build_player.sh` + verify "PlayerBuilder: Succeeded" in a full log.
- [ ] **Step 5:** Commit regenerated scene + `EnemyCombatConfig.asset` (+ any prefab churn, same class as prior regen commits) — `build: regenerate scene with enemy pockets`.
- [ ] **Step 6:** STOP — orchestrator reviews the diff and owns docs/push/PR.

---

### Task 4: Docs + push (orchestrator-owned)

ARCHITECTURE.md (§2 glue: EnemySpawner; §3.4 run loop: enemies paragraph; §7: enemy HP 40), README (mention the drones), CLAUDE.md (RESUME: First Run milestone COMPLETE, all playtest gates listed; diary), memory, push, PR body final section.
