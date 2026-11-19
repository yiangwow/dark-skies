# Phase 3: The Run Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the arena into a game: a timed run from spawn to an exit gate, kills scored through the music-heat multiplier, a D–S grade screen, instant retry — the First Run loop from the spec.

**Architecture:** One new pure-C# core (`RunCore`: state machine + scoring + rank mapping, fully unit-tested) driven by a thin `RunManager` that replaces `MatchManager`/`MatchCore` wholesale. The exit is a trigger (`ExitGate`) on the far outer ring, so a run crosses the whole Gyroscope. The HUD's frags line becomes SCORE × multiplier + timer; the VICTORY splash becomes the grade screen. MusicCore heat (already streaming the user's breakcore by combat intensity) IS the multiplier — no new simulation.

**Tech Stack:** Unity 6000.5.1f1, URP, EditMode NUnit. Gates: `bash tools/run_tests.sh` — NUnit counts every `[TestCase]` row as a test, so RunCoreTests = **18** cases (8 `[Test]` + 10 rows): expect **98** after Task 1 (85 − 5 MatchCore + 18), **99** from Task 2 on (+1 MusicCore ResetHeat test). Builder `-executeMethod`, screenshots, `tools/build_player.sh` (verify via "PlayerBuilder: Succeeded").

**Spec:** `docs/superpowers/specs/2026-08-28-singleplayer-pivot-design.md` (§3 runs and scoring, §4 systems)

## Global Constraints

- Unity Editor CLOSED for every headless command (`tasklist.exe | grep -i unity`).
- Repo root: `/mnt/c/Users/computer/My project` (quote the space). Branch: `feature/singleplayer-pivot`.
- Git author the owner's git identity; NO Co-Authored-By/Claude attribution. Commit `.meta` files with their assets; new C# files get Unity-generated `.meta` on the first headless run — commit those too.
- Never hand-edit `MovementPlayground.unity`; the builder regenerates it (Task 4).
- Existing pure cores untouched (MovementCore/CombatCore/WeaponCore/FeedbackCore/MusicCore/BotBrain). `MatchCore` is DELETED by this plan — that is sanctioned (spec §4: RunCore replaces it).
- Every task ends with `bash tools/run_tests.sh` green, then a commit. On failure read `TestResults/editmode.xml` + `TestResults/editmode.log` (grep `error CS`).
- Remember the Phase 2 landmines: any NEW runtime-only shader needs an asset-material chain (none planned here); materials referenced by saved prefabs must be asset-backed (the exit gate uses scene objects, not prefabs — no exposure).

---

### Task 1: RunCore + config + tests

**Files:**
- Create: `Assets/AlienSkies/Scripts/RunSettings.cs`
- Create: `Assets/AlienSkies/Scripts/RunConfig.cs`
- Create: `Assets/AlienSkies/Scripts/RunCore.cs`
- Create: `Assets/AlienSkies/Tests/RunCoreTests.cs`
- Delete: `Assets/AlienSkies/Scripts/MatchCore.cs` (+ `.meta`), `Assets/AlienSkies/Tests/MatchCoreTests.cs` (+ `.meta`)
- Modify: `Assets/AlienSkies/Scripts/MusicManager.cs` (lines ~86–87 — a MatchManager consumer, review-caught)
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` (delete the matchGO block ~236–238 ONLY — a MatchManager consumer in the Editor asmdef; Task 3 adds the RunManager scene object)
- Note: ALL MatchManager/MatchCore consumers retarget in THIS task's Step 4 (the gate-ordering lesson from Phases 1–2, caught a third time in review): PlayerCombat, MovementHUD, MusicManager, PlaygroundBuilder.

**Interfaces:**
- Produces (consumed by Tasks 2–3):
  - `AlienSkies.Run.RunSettings` (`[Serializable]`): `float ParTime = 90f; int KillScore = 100; int MaxMultiplier = 4; float TimeBonusPerSecond = 10f; int STargetScore = 2000; float RestartDelay = 2.5f;`
  - `AlienSkies.Run.RunConfig : ScriptableObject` with `public RunSettings Settings;` menu `AlienSkies/Run Config` (mirror MovementConfig.cs exactly)
  - `AlienSkies.Run.RunCore`:
    - `RunState State` (`enum RunState { Ready, Running, Complete }`)
    - `float Time`, `int Score`, `int Kills`, `int Multiplier` (current, from last heat), `int PeakMultiplier`
    - `void StartRun()`, `void Tick(float dt)`, `void RegisterKill(float heat)`, `void CompleteRun()`, `void Reset()`
    - After `CompleteRun()`: `int FinalScore` (Score + time bonus), `string Rank` ("D".."S")
    - `int MultiplierFor(float heat)` — public static-shaped helper so the HUD can show the live multiplier without mutating state

- [ ] **Step 1: Write RunSettings.cs** (mirror the layout of `MovementSettings.cs` — plain `[Serializable]` class, doc comments per field):

```csharp
namespace AlienSkies.Run
{
    /// <summary>Run-loop tuning. Par/target values are per-level knobs — for
    /// v1 there is one level (Sphere 1), so they live here.</summary>
    [System.Serializable]
    public class RunSettings
    {
        public float ParTime = 90f;             // seconds; under par earns the time bonus
        public int KillScore = 100;             // base points per kill, before multiplier
        public int MaxMultiplier = 4;           // heat maps 0..1 onto x1..xMax
        public float TimeBonusPerSecond = 10f;  // points per second under par
        public int STargetScore = 2000;         // final score for an S rank; A/B/C at 75/50/25%
        public float RestartDelay = 2.5f;       // grade screen dwell before auto-restart
    }
}
```

- [ ] **Step 2: Write RunConfig.cs** (copy MovementConfig.cs's shape):

```csharp
using UnityEngine;

namespace AlienSkies.Run
{
    [CreateAssetMenu(menuName = "AlienSkies/Run Config")]
    public sealed class RunConfig : ScriptableObject
    {
        public RunSettings Settings = new RunSettings();
    }
}
```

- [ ] **Step 3: Write RunCore.cs:**

```csharp
using UnityEngine;

namespace AlienSkies.Run
{
    public enum RunState { Ready, Running, Complete }

    /// <summary>The run loop: timer, score, music-heat multiplier, rank.
    /// Pure C# — the timer starts on the first input (RunManager decides what
    /// counts as input), kills score through the heat multiplier, the exit
    /// gate completes the run, death resets it. Combat is the score; the
    /// music is the multiplier (ADR-004).</summary>
    public sealed class RunCore
    {
        public RunState State { get; private set; } = RunState.Ready;
        public float Time { get; private set; }
        public int Score { get; private set; }
        public int Kills { get; private set; }
        public int Multiplier { get; private set; } = 1;
        public int PeakMultiplier { get; private set; } = 1;
        public int FinalScore { get; private set; }
        public string Rank { get; private set; } = "";

        readonly RunSettings _s;

        public RunCore(RunSettings settings) => _s = settings ?? new RunSettings();

        /// <summary>Heat 0..1 maps to x1..xMax in integer steps: x1 at zero
        /// heat, xMax only at (or very near) full heat.</summary>
        public int MultiplierFor(float heat)
        {
            float h = Mathf.Clamp01(heat);
            return 1 + Mathf.Min(_s.MaxMultiplier - 1, Mathf.FloorToInt(h * (_s.MaxMultiplier - 1) + 0.0001f));
        }

        public void StartRun()
        {
            if (State != RunState.Ready) return;
            State = RunState.Running;
        }

        public void Tick(float dt)
        {
            if (State == RunState.Running) Time += dt;
        }

        public void RegisterKill(float heat)
        {
            if (State != RunState.Running) return;
            Multiplier = MultiplierFor(heat);
            PeakMultiplier = Mathf.Max(PeakMultiplier, Multiplier);
            Score += _s.KillScore * Multiplier;
            Kills++;
        }

        public void CompleteRun()
        {
            if (State != RunState.Running) return;
            State = RunState.Complete;
            float underPar = Mathf.Max(0f, _s.ParTime - Time);
            FinalScore = Score + Mathf.RoundToInt(underPar * _s.TimeBonusPerSecond);
            float frac = _s.STargetScore > 0 ? (float)FinalScore / _s.STargetScore : 0f;
            Rank = frac >= 1f ? "S" : frac >= 0.75f ? "A" : frac >= 0.5f ? "B" : frac >= 0.25f ? "C" : "D";
        }

        public void Reset()
        {
            State = RunState.Ready;
            Time = 0f;
            Score = 0;
            Kills = 0;
            Multiplier = 1;
            PeakMultiplier = 1;
            FinalScore = 0;
            Rank = "";
        }
    }
}
```

- [ ] **Step 4: Retarget MatchCore's consumers in the same gate** (compile-order rule):
  1. Delete `Assets/AlienSkies/Scripts/MatchManager.cs` + `.meta` and `Assets/AlienSkies/Scripts/MatchCore.cs` + `.meta` and `Assets/AlienSkies/Tests/MatchCoreTests.cs` + `.meta` (`git rm`).
  2. `PlayerCombat.cs` Update (~line 100): replace
     `bool roundOver = MatchManager.Instance != null && MatchManager.Instance.IsRoundOver;`
     with
     `bool roundOver = AlienSkies.Run.RunManager.Instance != null && AlienSkies.Run.RunManager.Instance.InputsFrozen;`
     — Task 2 creates RunManager; to keep THIS task compiling, create the **stub** `Assets/AlienSkies/Scripts/RunManager.cs` now:

```csharp
using UnityEngine;

namespace AlienSkies.Run
{
    /// <summary>Scene singleton driving RunCore. Stub grown in the run-loop
    /// task: for now it only exposes the freeze flag PlayerCombat polls.</summary>
    public sealed class RunManager : MonoBehaviour
    {
        public static RunManager Instance { get; private set; }
        public bool InputsFrozen { get; private set; }
        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
```

  3. `MovementHUD.cs` (~117–128): delete the whole `MatchManager` VICTORY/DEFEATED block (Task 2 adds the grade screen).
  4. `MusicManager.cs` (~86–87): replace the MatchManager poll with
     `Core.RoundOver = AlienSkies.Run.RunManager.Instance != null && AlienSkies.Run.RunManager.Instance.InputsFrozen;`
     — preserves the round-end music duck (MusicCore.RoundOver drives Level ducking); the stub's `InputsFrozen` is always false until Task 2, so behavior is unchanged this task.
  5. `PlaygroundBuilder.cs` (~236–238): delete the matchGO block (`new GameObject("MatchManager")` + AddComponent + Config wiring) — nothing replaces it this task; Task 3 creates the RunManager scene object.
- [ ] **Step 5: Write RunCoreTests.cs** (10 tests — the contract):

```csharp
using NUnit.Framework;
using AlienSkies.Run;

public class RunCoreTests
{
    RunCore NewCore(RunSettings s = null) => new RunCore(s ?? new RunSettings());

    [Test]
    public void StartsReady_TickDoesNothingUntilStarted()
    {
        var c = NewCore();
        c.Tick(1f);
        Assert.AreEqual(RunState.Ready, c.State);
        Assert.AreEqual(0f, c.Time);
    }

    [Test]
    public void StartRun_ThenTick_AdvancesTime()
    {
        var c = NewCore();
        c.StartRun();
        c.Tick(0.5f);
        c.Tick(0.25f);
        Assert.AreEqual(RunState.Running, c.State);
        Assert.AreEqual(0.75f, c.Time, 1e-4f);
    }

    [TestCase(0f, 1)]
    [TestCase(0.32f, 1)]
    [TestCase(0.34f, 2)]
    [TestCase(0.67f, 3)]
    [TestCase(1f, 4)]
    public void MultiplierFor_MapsHeatToSteps(float heat, int expected)
    {
        var c = NewCore(); // MaxMultiplier 4 -> thirds
        Assert.AreEqual(expected, c.MultiplierFor(heat));
    }

    [Test]
    public void RegisterKill_ScoresThroughMultiplier_AndTracksPeak()
    {
        var c = NewCore();
        c.StartRun();
        c.RegisterKill(0f);   // x1 -> 100
        c.RegisterKill(1f);   // x4 -> 400
        c.RegisterKill(0.4f); // x2 -> 200
        Assert.AreEqual(700, c.Score);
        Assert.AreEqual(3, c.Kills);
        Assert.AreEqual(2, c.Multiplier);
        Assert.AreEqual(4, c.PeakMultiplier);
    }

    [Test]
    public void KillsIgnoredUnlessRunning()
    {
        var c = NewCore();
        c.RegisterKill(1f);
        Assert.AreEqual(0, c.Score);
        c.StartRun();
        c.CompleteRun();
        c.RegisterKill(1f);
        Assert.AreEqual(0, c.Score);
    }

    [Test]
    public void CompleteRun_UnderPar_AddsTimeBonus()
    {
        var s = new RunSettings { ParTime = 60f, TimeBonusPerSecond = 10f, STargetScore = 2000 };
        var c = NewCore(s);
        c.StartRun();
        c.Tick(30f);            // 30s under par -> +300
        c.RegisterKill(1f);     // +400
        c.CompleteRun();
        Assert.AreEqual(RunState.Complete, c.State);
        Assert.AreEqual(700, c.FinalScore);
    }

    [Test]
    public void CompleteRun_OverPar_NoBonusNoPenalty()
    {
        var s = new RunSettings { ParTime = 10f };
        var c = NewCore(s);
        c.StartRun();
        c.Tick(50f);
        c.RegisterKill(0f); // 100
        c.CompleteRun();
        Assert.AreEqual(100, c.FinalScore);
    }

    [TestCase(2000, "S")]
    [TestCase(1500, "A")]
    [TestCase(1000, "B")]
    [TestCase(500, "C")]
    [TestCase(100, "D")]
    public void Rank_FromFinalScoreFraction(int killPoints, string expected)
    {
        var s = new RunSettings { ParTime = 0f, KillScore = killPoints, STargetScore = 2000 };
        var c = NewCore(s);
        c.StartRun();
        c.RegisterKill(0f); // x1 -> exactly killPoints
        c.CompleteRun();
        Assert.AreEqual(expected, c.Rank);
    }

    [Test]
    public void Reset_ReturnsToReady_ClearsEverything()
    {
        var c = NewCore();
        c.StartRun();
        c.Tick(5f);
        c.RegisterKill(1f);
        c.CompleteRun();
        c.Reset();
        Assert.AreEqual(RunState.Ready, c.State);
        Assert.AreEqual(0f, c.Time);
        Assert.AreEqual(0, c.Score);
        Assert.AreEqual(1, c.PeakMultiplier);
        Assert.AreEqual("", c.Rank);
    }

    [Test]
    public void CompleteIgnoredUnlessRunning()
    {
        var c = NewCore();
        c.CompleteRun();
        Assert.AreEqual(RunState.Ready, c.State);
        Assert.AreEqual("", c.Rank);
    }
}
```

- [ ] **Step 6: Gate.** `bash tools/run_tests.sh` → PASS **98** (85 − 5 MatchCore + 18 RunCore; NUnit counts each `[TestCase]` row). `grep -rn "MatchCore\|MatchManager" Assets/AlienSkies/ --include="*.cs"` → zero hits.
- [ ] **Step 7: Commit** — `feat: RunCore replaces MatchCore - timer, heat multiplier, rank; RunManager stub`

---

### Task 2: RunManager + ExitGate + HUD

**Files:**
- Modify: `Assets/AlienSkies/Scripts/RunManager.cs` (grow the stub into the full manager below)
- Create: `Assets/AlienSkies/Scripts/ExitGate.cs`
- Modify: `Assets/AlienSkies/Scripts/MovementHUD.cs` (frags line → score/mult/timer; grade screen)
- Modify: `Assets/AlienSkies/Scripts/PlayerCombat.cs` (`ServerResetForNewRound` only — Step 1's consistency fix)
- Modify: `Assets/AlienSkies/Scripts/MusicCore.cs` (ONE added method: `public void ResetHeat() => _heat = 0f;` — sanctioned core extension so the score multiplier cannot leak across runs or be pumped before the clock starts; heat is a SCORING input now, and a run must start cold)
- Modify: `Assets/AlienSkies/Tests/MusicCoreTests.cs` (ONE added test: register kills to raise heat, `ResetHeat()`, assert `Intensity == 0` — total becomes **99**)

**Interfaces:**
- Consumes: `PlayerCombat` events (`OnHitConfirmed(damage, killed)` — local player only, per the Phase 2 gate), `PlayerMotor` (`Core.Velocity`, `KillPlaneCrossed`, `RespawnAt`), `MusicManager.Core.Intensity` (0..1 heat), `SpawnPointRegistry.Points[0]` (run start), `RunConfig`.
- Produces: `RunManager.Instance` with `RunCore Core`, `bool InputsFrozen`, `int BestScore`, `float BestTime`, `float RestartRemaining` — read by MovementHUD; `void CompleteRun()` — called by ExitGate; `ExitGate : MonoBehaviour` with `OnTriggerEnter`.

- [ ] **Step 1: Grow RunManager.cs** — replace the stub file's contents with:

```csharp
using UnityEngine;
using AlienSkies.Combat;
using AlienSkies.Movement;
using AlienSkies.Audio;

namespace AlienSkies.Run
{
    /// <summary>Scene singleton driving RunCore: starts the timer on the
    /// player's first movement, scores kills through the live music heat,
    /// freezes play on completion, restarts the run on death or kill-plane
    /// (arcade rule: a run is one unbroken attempt). Persists bests in
    /// PlayerPrefs (per-level keys; v1 = sphere1).</summary>
    public sealed class RunManager : MonoBehaviour
    {
        const string BestScoreKey = "sphere1_best_score";
        const string BestTimeKey = "sphere1_best_time";

        public static RunManager Instance { get; private set; }

        public RunConfig Config;

        public RunCore Core { get; private set; }
        public bool InputsFrozen { get; private set; }
        public int BestScore { get; private set; }
        public float BestTime { get; private set; }
        public float RestartRemaining { get; private set; }

        PlayerCombat _player;
        PlayerMotor _motor;
        MusicManager _music;
        Vector3 _runStart;

        RunSettings S => Config != null ? Config.Settings : _fallback;
        readonly RunSettings _fallback = new RunSettings();

        void Awake()
        {
            Instance = this;
            Core = new RunCore(S);
            BestScore = PlayerPrefs.GetInt(BestScoreKey, 0);
            BestTime = PlayerPrefs.GetFloat(BestTimeKey, 0f);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _music = FindFirstObjectByType<MusicManager>();
            BindPlayer();
        }

        // The human player: the one motor that reads devices. Bots have
        // ExternalInput and must never drive the run.
        void BindPlayer()
        {
            foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            {
                if (!pc.IsLocal) continue;
                _player = pc;
                _motor = pc.GetComponent<PlayerMotor>();
                _player.OnHitConfirmed += HandleHitConfirmed;
                _motor.KillPlaneCrossed += HandleKillPlane;
                _runStart = _motor.transform.position;
                return;
            }
        }

        void HandleHitConfirmed(float damage, bool killed)
        {
            if (killed) Core.RegisterKill(_music != null ? _music.Core.Intensity : 0f);
        }

        // Kill-plane fires MID-simulation and the motor teleports to its own
        // fallback point right AFTER the event — restarting synchronously here
        // would be overwritten. Defer to the top of the next Update instead
        // (review-caught position clobber). Only a Running run restarts; in
        // Complete the grade screen must survive falling off the perch.
        void HandleKillPlane()
        {
            if (Core.State == RunState.Running) _restartPending = true;
        }

        bool _restartPending;

        void Update()
        {
            float dt = Time.deltaTime;
            if (_player == null) { BindPlayer(); if (_player == null) return; }

            if (_restartPending) { _restartPending = false; RestartRun(); return; }

            switch (Core.State)
            {
                case RunState.Ready:
                    // First input starts the clock. HORIZONTAL velocity only:
                    // the player spawns above the platform and gravity-settles
                    // past any total-speed threshold with zero input
                    // (review-caught false start). Looking around also must
                    // not start a speedrun.
                    var v = _motor.Core.Velocity;
                    if (new Vector3(v.x, 0f, v.z).sqrMagnitude > 0.05f) Core.StartRun();
                    // Killed at the start line before moving (bot rocket):
                    // the run never started — just reclaim the start position.
                    if (_player.IsDead) RestartRun();
                    break;

                case RunState.Running:
                    Core.Tick(dt);
                    if (_player.IsDead) RestartRun();
                    break;

                case RunState.Complete:
                    // Deaths/falls here are cosmetic — the dwell finishes and
                    // restarts regardless; the motor's own kill-plane teleport
                    // parks the body meanwhile.
                    RestartRemaining -= dt;
                    if (RestartRemaining <= 0f) RestartRun();
                    break;
            }
        }

        /// <summary>Called by the ExitGate trigger.</summary>
        public void CompleteRun()
        {
            if (Core.State != RunState.Running) return;
            Core.CompleteRun();
            InputsFrozen = true;
            RestartRemaining = S.RestartDelay;
            if (Core.FinalScore > BestScore)
            {
                BestScore = Core.FinalScore;
                BestTime = Core.Time;
                PlayerPrefs.SetInt(BestScoreKey, BestScore);
                PlayerPrefs.SetFloat(BestTimeKey, BestTime);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Death, kill-plane, or post-grade: back to the start line.
        /// A run starts cold — the heat/multiplier must not leak in from the
        /// previous attempt (review-caught scoring exploit).</summary>
        void RestartRun()
        {
            Core.Reset();
            InputsFrozen = false;
            RestartRemaining = 0f;
            if (_music != null) _music.Core.ResetHeat();
            _player.ServerResetForNewRound(); // fresh CombatCore + revive (name is a de-net relic)
            _motor.RespawnAt(_runStart);
            // (No rotation write: the motor re-derives root rotation from the
            // Graphical every frame — keeping the old facing matches respawns.)
        }
    }
}
```

  **Consistency fix required by this file:** `RestartRun` teleports via `RespawnAt(_runStart)`, but `PlayerCombat.ServerResetForNewRound()` ALSO teleports (to `SpawnPointRegistry.Farthest`). Edit `PlayerCombat.ServerResetForNewRound` to STOP teleporting — it becomes core-reset-only:

```csharp
        /// <summary>Full combat reset: fresh CombatCore (zeroes frags/deaths/
        /// health), revive. Position is the caller's business (RunManager
        /// owns the start line; bot respawns keep using Respawn()).</summary>
        public void ServerResetForNewRound() => _core = new CombatCore(S);
```

  (Check the current body first; keep whatever event/field bookkeeping it does besides the `_motor.ServerTeleport`/`RespawnAt` line — delete only the teleport.)

- [ ] **Step 2: Write ExitGate.cs:**

```csharp
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Run
{
    /// <summary>Touch to finish the run. The builder gives this a trigger
    /// sphere inside a neon ring; only the human player completes runs.</summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class ExitGate : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            var motor = other.GetComponentInParent<PlayerMotor>();
            if (motor == null || !motor.IsHuman) return;
            if (RunManager.Instance != null) RunManager.Instance.CompleteRun();
        }
    }
}
```

- [ ] **Step 3: HUD rework in MovementHUD.cs:**
  1. Replace the FRAGS line (~94–96) with score + live multiplier + timer (the multiplier reads live heat so it breathes with the music even between kills):

```csharp
            var run = AlienSkies.Run.RunManager.Instance;
            if (run != null)
            {
                if (_music == null) _music = Object.FindFirstObjectByType<MusicManager>();
                int liveMult = run.Core.MultiplierFor(_music != null ? _music.Core.Intensity : 0f);
                ShadowLabel(new Rect(Screen.width / 2f - 160f, 24f, 320f, 32f),
                            $"SCORE {run.Core.Score}   x{liveMult}",
                            new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });
                if (run.Core.State == AlienSkies.Run.RunState.Running)
                    ShadowLabel(new Rect(Screen.width / 2f - 60f, 56f, 120f, 30f),
                                $"{run.Core.Time:0.0}s",
                                new GUIStyle(_cue) { alignment = TextAnchor.MiddleCenter });
            }
```

  (Add a `MusicManager _music;` field to MovementHUD for the lazy cache above; check MusicManager's actual namespace — likely `AlienSkies.Audio` — and add the using/qualifier.)
  2. Where the MatchManager block used to be (deleted in Task 1), add the grade screen:

```csharp
            if (run != null && run.Core.State == AlienSkies.Run.RunState.Complete)
            {
                var rank = new GUIStyle(_cross) { fontSize = 110 };
                rank.normal.textColor = run.Core.Rank == "S" ? AlienSkies.Palette.Amber
                                     : run.Core.Rank == "D" ? AlienSkies.Palette.Magenta
                                     : AlienSkies.Palette.Cyan;
                ShadowLabel(new Rect(Screen.width / 2f - 100f, Screen.height / 2f - 170f, 200f, 130f),
                            run.Core.Rank, rank);
                var line = new GUIStyle(_cross) { fontSize = 24 };
                ShadowLabel(new Rect(Screen.width / 2f - 250f, Screen.height / 2f - 20f, 500f, 32f),
                            $"SCORE {run.Core.FinalScore}   TIME {run.Core.Time:0.0}s   KILLS {run.Core.Kills}   PEAK x{run.Core.PeakMultiplier}",
                            line);
                ShadowLabel(new Rect(Screen.width / 2f - 250f, Screen.height / 2f + 16f, 500f, 32f),
                            $"BEST {run.BestScore}" + (run.BestTime > 0f ? $" ({run.BestTime:0.0}s)" : ""),
                            new GUIStyle(line) { fontSize = 20 });
                ShadowLabel(new Rect(Screen.width / 2f - 250f, Screen.height / 2f + 56f, 500f, 32f),
                            $"restart in {Mathf.Max(0f, run.RestartRemaining):0.0}s",
                            new GUIStyle(line) { fontSize = 18 });
            }
```

  3. Keep DEATHS out of the HUD entirely (death = restart now; the counter is meaningless). Remove any leftover Deaths display.
- [ ] **Step 3b: MusicCore.ResetHeat** — add to `MusicCore.cs`:

```csharp
        /// <summary>Runs start cold: RunManager zeroes the scoring heat at
        /// every run boundary so multipliers can't leak between attempts.</summary>
        public void ResetHeat() => _heat = 0f;
```

  (Match the real private field name — read the file; it may be `_heat` or similar.) And add one test to `MusicCoreTests.cs`:

```csharp
    [Test]
    public void ResetHeat_ZeroesIntensity()
    {
        var c = new MusicCore();
        c.RegisterKill();
        c.RegisterKill();
        Assert.Greater(c.Intensity, 0f);
        c.ResetHeat();
        Assert.AreEqual(0f, c.Intensity);
    }
```

- [ ] **Step 4: Gate.** `bash tools/run_tests.sh` → PASS **99**.
- [ ] **Step 5: Commit** — `feat: RunManager, ExitGate, score/timer/grade HUD - the run loop`

---

### Task 3: Builder — the gate, the start line, the run config

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs`
- Produce (generated in Task 4, committed then): `Assets/AlienSkies/Config/RunConfig.asset`

**Interfaces:**
- Consumes: `ExitGate`, `RunManager`, `RunConfig`, existing helpers (`CreateSegmentRing`, `NeonMaterial`, spawn registry).
- Produces: scene contains a `RunManager` GameObject (Config wired), an `ExitGate` at the far outer ring, and the player's spawn is the run start.

- [ ] **Step 1: RunConfig asset creation** — add `const string RunConfigPath = "Assets/AlienSkies/Config/RunConfig.asset";` next to the other path consts (~17–18), then in `Build()`, next to the Movement/Combat config loads, the same load-or-create pattern for `RunConfig`.
- [ ] **Step 2: Thread runConfig into BuildPlayer + create the RunManager scene object.** SCOPE TRAP (review-caught): `runConfig` is a `Build()` local, but the RunManager creation site (where matchGO used to be, deleted in Task 1) is inside `BuildPlayer(MovementConfig, CombatConfig)`. Extend the signature — mirroring how combatConfig already travels:
  - `static void BuildPlayer(MovementConfig config, CombatConfig combatConfig, AlienSkies.Run.RunConfig runConfig)`
  - update the call in `Build()` (~line 78)
  - at the old matchGO site:

```csharp
            var runGO = new GameObject("RunManager");
            var runMgr = runGO.AddComponent<AlienSkies.Run.RunManager>();
            runMgr.Config = runConfig;
```

  (RunManager finds the player/music at RUNTIME Start — no other builder wiring.)
- [ ] **Step 3: The exit gate** — review pinned the exact geometry: the outer-ring perch positions are loop-locals (~715–721) that nothing captures. Capture them (`var perchPositions = new Vector3[...]` filled inside that loop), then insert the gate code AFTER the `registry.Points = pts.ToArray();` line (~741, still inside `BuildArena` — `pts` and the amber ring material are in scope). Pick the perch farthest from `pts[0].position` (per review it is index 5, ≈(−134.6, 52, −10.2)) — compute it, don't hardcode:

```csharp
            // The finish line: an amber halo on the far perch. Amber = the
            // objective color in the palette language.
            Vector3 farPerch = perchPositions[0];
            foreach (var p in perchPositions)
                if ((p - pts[0].position).sqrMagnitude > (farPerch - pts[0].position).sqrMagnitude)
                    farPerch = p;
            var gateCenter = farPerch + Vector3.up * 3f;
            CreateSegmentRing(/* match the real signature — name/center/radius 5f/amber mat/colliders:false */);
            var gate = new GameObject("ExitGate");
            gate.transform.position = gateCenter;
            var trigger = gate.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 4f;
            gate.AddComponent<AlienSkies.Run.ExitGate>();
```

  (`CreateSegmentRing`'s real parameter list must be read from the file and matched; use the same amber material the halo/beacons use.)
- [ ] **Step 4: Sanity check** — `grep -n "MatchManager\|matchGO" Assets/AlienSkies/Editor/PlaygroundBuilder.cs` → zero hits (Task 1 already removed it; this is a no-op verification).
- [ ] **Step 5: Gate.** `bash tools/run_tests.sh` → PASS **99** (compile gate for the editor assembly).
- [ ] **Step 6: Commit** — `feat: builder places RunManager + amber exit gate on the far perch`

---

### Task 4: Regenerate + full verification

- [ ] **Step 1:** Rebuild scene: `-executeMethod AlienSkies.EditorTools.PlaygroundBuilder.BuildNeonVector` (log to `TestResults/rebuild_scene.log`; exit 0; zero `missing`/`error CS` lines).
- [ ] **Step 2:** `bash tools/run_tests.sh` → **99/99**.
- [ ] **Step 3:** Screenshots regenerate (7 PNGs, fresh mtimes). The arena-top shot should show the new amber gate ring on the far perch — confirm visually that SOMETHING amber exists there (orchestrator does the real art review).
- [ ] **Step 4:** `bash tools/build_player.sh` → "PlayerBuilder: Succeeded" in the newest full log + fresh `Builds/AlienSkies_Data/Managed` mtimes.
- [ ] **Step 5:** Commit regenerated scene + `Assets/AlienSkies/Config/RunConfig.asset` (+ metas) — `build: regenerate scene with run loop; RunConfig asset`.
- [ ] **Step 6:** STOP — orchestrator reviews `shot_arena_top.png` (gate visible? reads as objective-amber?) and the diff, then owns docs/push/PR.

---

### Task 5: Docs + push (orchestrator-owned)

ARCHITECTURE.md (§2 glue list: RunManager replaces MatchManager; new §3.x run-loop subsection; §7 tuning: RunSettings numbers), README (Play section: the run, the gate, the grade; controls unchanged), CLAUDE.md (RESUME → Phase 4 next + playtest gate: "is the loop fun?"; diary entry), memory. Push, verify diff, update PR #22 body checklist.
