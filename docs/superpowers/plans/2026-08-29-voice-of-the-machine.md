# Voice of the Machine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the game its antagonist: the Gyroscope speaks — glitch-corrupted taunts on deaths/kills/ranks in the HUD — and death gets a digital-corruption glitch flash. Original SHODAN-archetype character; zero borrowed lines.

**Architecture:** `MachineVoiceCore` (pure C#, seeded, tested) owns line pools, priority, cooldown, and no-repeat rotation — `Post(trigger)` returns a line or null. `MachineVoice` (thin MB) maps game events to triggers and renders the active line with a character-corruption materialize effect (OnGUI, palette magenta). The death glitch is a camera-parented quad + new `GlitchPass.shader` (procedural corruption overlay: RGB-split bars, block noise, scanlines) reusing the existing `ImpactFrameFade` driver (its `Life` field is public) and the asset-backed-material doctrine. `RunManager` grows a `LastResetCause` and `LastRunWasBest` so the voice and the glitch know WHY.

**Tech Stack:** Unity 6000.5.1f1, URP, hand-written HLSL. Gate: `bash tools/run_tests.sh` → **110** from Task 1 on (100 + 10 MachineVoiceCore). Builder, screenshots, `tools/build_player.sh` (now truth-telling — trust its exit code).

**Spec:** `docs/superpowers/specs/2026-08-28-singleplayer-pivot-design.md` §1 Identity block.

## Global Constraints

- Unity Editor CLOSED for headless commands (`tasklist.exe | grep -i unity`).
- Repo root: `/mnt/c/Users/computer/My project` (quote the space). Branch: `feature/overnight-polish`.
- Git author the owner's git identity; NO Co-Authored-By/Claude attribution. Commit `.meta` files.
- Never hand-edit the scene; the builder regenerates it (Task 3).
- Every task: `bash tools/run_tests.sh` → PASS 110, then commit. Failures: `TestResults/editmode.xml` + `.log`.
- Landmines (recurred all night): materials in saved prefabs must be ASSET-backed (`GetOrCreateMaterial`); shaders reachable only via runtime `Shader.Find` get STRIPPED from player builds — the glitch material asset chain (prefab → material → shader) handles it; builder cross-function references thread BY PARAMETER, never `Find`.
- **Voice content rule (identity-level):** every line below is ORIGINAL. No System Shock/SHODAN names, lines, or paraphrases. If a worker "improves" a line into something recognizable, that is a defect.

---

### Task 1: MachineVoiceCore + tests

**Files:**
- Create: `Assets/AlienSkies/Scripts/MachineVoiceCore.cs`
- Create: `Assets/AlienSkies/Tests/MachineVoiceCoreTests.cs`

**Interfaces:**
- Produces: `AlienSkies.Run.VoiceTrigger` enum + `MachineVoiceCore`: `Post(VoiceTrigger)`, `Tick(float)`, `string CurrentLine` (null when silent), `float LineAge`.
- Consumed by Task 2's `MachineVoice` MB.

- [ ] **Step 1: Write MachineVoiceCore.cs:**

```csharp
using System.Collections.Generic;

namespace AlienSkies.Run
{
    public enum VoiceTrigger
    {
        RunStart, Kill, KillAtMaxHeat, Chain,
        Death, DeathByVoid,
        RankS, RankA, RankMid, RankD, NewBest,
        LevelSwitch, Idle,
    }

    /// <summary>The Voice of the Machine: the Gyroscope is the antagonist — a
    /// contemptuous machine divinity that built the arena as a test. Pure C#:
    /// line pools with seeded no-repeat rotation, a global cooldown for
    /// chatter, and priority triggers (deaths, ranks) that always speak.
    /// ORIGINAL character and lines only — the SHODAN archetype is the homage,
    /// never the text.</summary>
    public sealed class MachineVoiceCore
    {
        public const float LineDuration = 4f;
        public const float ChatterCooldown = 4f;

        public string CurrentLine { get; private set; }
        public float LineAge { get; private set; }

        readonly System.Random _rng;
        readonly Dictionary<VoiceTrigger, string[]> _pools;
        readonly Dictionary<VoiceTrigger, List<string>> _bags = new Dictionary<VoiceTrigger, List<string>>();
        float _cooldown;

        static readonly HashSet<VoiceTrigger> Priority = new HashSet<VoiceTrigger>
        {
            VoiceTrigger.Death, VoiceTrigger.DeathByVoid,
            VoiceTrigger.RankS, VoiceTrigger.RankA, VoiceTrigger.RankMid,
            VoiceTrigger.RankD, VoiceTrigger.NewBest,
            VoiceTrigger.Chain, // rare, earned — must never be eaten by the kill line's cooldown (review)
        };

        public MachineVoiceCore(int seed)
        {
            _rng = new System.Random(seed);
            _pools = BuildPools();
        }

        public void Tick(float dt)
        {
            if (_cooldown > 0f) _cooldown -= dt;
            if (CurrentLine == null) return;
            LineAge += dt;
            if (LineAge >= LineDuration) CurrentLine = null;
        }

        /// <summary>Returns the spoken line, or null when suppressed
        /// (chatter during cooldown). Priority triggers always speak.</summary>
        public string Post(VoiceTrigger trigger)
        {
            bool priority = Priority.Contains(trigger);
            if (!priority && _cooldown > 0f) return null;

            string line = Draw(trigger);
            if (line == null) return null;
            CurrentLine = line;
            LineAge = 0f;
            _cooldown = ChatterCooldown;
            return line;
        }

        // Shuffle-bag: each pool is drawn in a seeded random order without
        // repeats; when exhausted it refills. Deterministic per seed.
        string Draw(VoiceTrigger trigger)
        {
            if (!_pools.TryGetValue(trigger, out var pool) || pool.Length == 0) return null;
            if (!_bags.TryGetValue(trigger, out var bag) || bag.Count == 0)
            {
                bag = new List<string>(pool);
                _bags[trigger] = bag;
            }
            int i = _rng.Next(bag.Count);
            string line = bag[i];
            bag.RemoveAt(i);
            return line;
        }

        static Dictionary<VoiceTrigger, string[]> BuildPools() =>
            new Dictionary<VoiceTrigger, string[]>
            {
                [VoiceTrigger.RunStart] = new[]
                {
                    "RUN, LITTLE COMET. I AM TIMING YOU.",
                    "THE ARENA WAS BUILT FOR BETTER THAN YOU. PROVE ME WRONG.",
                    "GO. THE VOID IS PATIENT. I AM NOT.",
                },
                [VoiceTrigger.Kill] = new[]
                {
                    "ADEQUATE.",
                    "THE DRONE WAS EXPENDABLE. AS ARE YOU.",
                    "ONE LESS OF MINE. THOUSANDS REMAIN.",
                    "NOTED. UNIMPRESSED.",
                },
                [VoiceTrigger.KillAtMaxHeat] = new[]
                {
                    "THE MUSIC LIKES YOU. I REMAIN UNCONVINCED.",
                    "LOUD. FAST. ALMOST WORTH WATCHING.",
                    "YOU BURN BRIGHTLY. BRIGHT THINGS BURN OUT.",
                },
                [VoiceTrigger.Chain] = new[]
                {
                    "AN INTERESTING PATTERN. DO IT AGAIN.",
                    "CHAIN REGISTERED. A FLICKER OF COMPETENCE.",
                    "MY DRONES DIE IN RHYTHM. HOW MUSICAL OF YOU.",
                },
                [VoiceTrigger.Death] = new[]
                {
                    "DELETED BY MY SMALLEST FINGER.",
                    "YOUR PULSE WAS A ROUNDING ERROR. CORRECTED.",
                    "AGAIN. FROM THE BEGINNING. FOREVER, IF NEEDED.",
                    "THE ARENA REJECTS YOU. POLITELY, THIS TIME.",
                },
                [VoiceTrigger.DeathByVoid] = new[]
                {
                    "GRAVITY. MY OLDEST SERVANT.",
                    "THE VOID ACCEPTS ALL DONATIONS.",
                    "FLIGHT WAS NEVER PROMISED. ONLY FALLING, DELAYED.",
                },
                [VoiceTrigger.RankS] = new[]
                {
                    "...RECALCULATING. YOU WERE NOT SUPPOSED TO DO THAT.",
                    "FLAWLESS. I WILL PRETEND I PLANNED THIS.",
                },
                [VoiceTrigger.RankA] = new[]
                {
                    "NEARLY WORTHY OF MY ARENA. NEARLY.",
                    "IMPRESSIVE, FOR A CREATURE OF MEAT AND PANIC.",
                },
                [VoiceTrigger.RankMid] = new[]
                {
                    "MEDIOCRITY, MEASURED TO THE MILLISECOND.",
                    "I HAVE ARCHIVED FASTER DUST.",
                },
                [VoiceTrigger.RankD] = new[]
                {
                    "I HAVE SEEN DEBRIS ORBIT WITH MORE PURPOSE.",
                    "A GRADE EVEN MY PITY CANNOT ROUND UP.",
                },
                [VoiceTrigger.NewBest] = new[]
                {
                    "ARCHIVED. YOUR SPECIES PEAKS SO BRIEFLY.",
                    "A NEW RECORD. I HAVE ADJUSTED MY EXPECTATIONS. SLIGHTLY.",
                },
                [VoiceTrigger.LevelSwitch] = new[]
                {
                    "ANOTHER SPHERE. ANOTHER GRAVE.",
                    "CHOOSE YOUR ARENA. THE OUTCOME IS UNCHANGED.",
                },
                [VoiceTrigger.Idle] = new[]
                {
                    "THE CLOCK ONLY MOVES WHEN YOU DO. A MERCY.",
                    "STANDING STILL. BOLD STRATEGY. WRONG ARENA.",
                },
            };
    }
}
```

- [ ] **Step 2: Write MachineVoiceCoreTests.cs** (10 tests):

```csharp
using NUnit.Framework;
using AlienSkies.Run;

public class MachineVoiceCoreTests
{
    [Test]
    public void SilentAtStart()
    {
        var v = new MachineVoiceCore(1);
        Assert.IsNull(v.CurrentLine);
    }

    [Test]
    public void Post_SetsCurrentLine_AndAgeCountsUp()
    {
        var v = new MachineVoiceCore(1);
        var line = v.Post(VoiceTrigger.Kill);
        Assert.IsNotNull(line);
        Assert.AreEqual(line, v.CurrentLine);
        v.Tick(1.5f);
        Assert.AreEqual(1.5f, v.LineAge, 1e-4f);
    }

    [Test]
    public void LineExpiresAfterDuration()
    {
        var v = new MachineVoiceCore(1);
        v.Post(VoiceTrigger.Kill);
        v.Tick(MachineVoiceCore.LineDuration + 0.01f);
        Assert.IsNull(v.CurrentLine);
    }

    [Test]
    public void ChatterSuppressedDuringCooldown()
    {
        var v = new MachineVoiceCore(1);
        Assert.IsNotNull(v.Post(VoiceTrigger.Kill));
        Assert.IsNull(v.Post(VoiceTrigger.Kill)); // within cooldown
        v.Tick(MachineVoiceCore.ChatterCooldown + 0.01f);
        Assert.IsNotNull(v.Post(VoiceTrigger.Kill));
    }

    [Test]
    public void PriorityAlwaysSpeaks()
    {
        var v = new MachineVoiceCore(1);
        Assert.IsNotNull(v.Post(VoiceTrigger.Kill));
        Assert.IsNotNull(v.Post(VoiceTrigger.Death)); // ignores cooldown
        Assert.IsNotNull(v.Post(VoiceTrigger.RankS));
    }

    [Test]
    public void PoolCyclesWithoutRepeat_UntilExhausted()
    {
        var v = new MachineVoiceCore(7);
        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 4; i++) // Kill pool has 4 lines
        {
            var line = v.Post(VoiceTrigger.Kill);
            Assert.IsTrue(seen.Add(line), $"repeat before exhaustion: {line}");
            v.Tick(MachineVoiceCore.ChatterCooldown + 0.01f);
        }
        Assert.IsNotNull(v.Post(VoiceTrigger.Kill)); // bag refilled
    }

    [Test]
    public void DeterministicPerSeed()
    {
        var a = new MachineVoiceCore(42);
        var b = new MachineVoiceCore(42);
        Assert.AreEqual(a.Post(VoiceTrigger.Death), b.Post(VoiceTrigger.Death));
        Assert.AreEqual(a.Post(VoiceTrigger.Kill), b.Post(VoiceTrigger.Kill));
    }

    [Test]
    public void NewPostResetsAge()
    {
        var v = new MachineVoiceCore(1);
        v.Post(VoiceTrigger.Kill);
        v.Tick(2f);
        v.Post(VoiceTrigger.Death);
        Assert.AreEqual(0f, v.LineAge, 1e-4f);
    }

    [Test]
    public void EveryTriggerHasLines()
    {
        var v = new MachineVoiceCore(3);
        foreach (VoiceTrigger t in System.Enum.GetValues(typeof(VoiceTrigger)))
        {
            Assert.IsNotNull(v.Post(t), $"no line for {t}");
            v.Tick(MachineVoiceCore.ChatterCooldown + 0.01f);
        }
    }

    [Test]
    public void PostReturnsSameStringItStores()
    {
        var v = new MachineVoiceCore(1);
        Assert.AreEqual(v.Post(VoiceTrigger.RankD), v.CurrentLine);
    }
}
```

- [ ] **Step 3: Gate.** `bash tools/run_tests.sh` → PASS **110**.
- [ ] **Step 4: Commit** — `feat: MachineVoiceCore - the Gyroscope speaks (pools, priority, cooldown)`

---

### Task 2: MachineVoice MB + glitch pass + RunManager causes

**Files:**
- Create: `Assets/AlienSkies/Scripts/MachineVoice.cs`
- Create: `Assets/AlienSkies/Shaders/GlitchPass.shader`
- Modify: `Assets/AlienSkies/Scripts/RunManager.cs` (`ResetCause LastResetCause`, `bool LastRunWasBest`)
- Modify: `Assets/AlienSkies/Scripts/CombatFeedback.cs` (`GlitchPassPrefab` field + spawn on hard resets)

**Interfaces:**
- Produces: `MachineVoice` scene MB (finds everything at runtime; builder only AddComponents it); nested `RunManager.ResetCause { PostGrade, Death, Void }` + `LastResetCause` (set BEFORE `RunReset` fires) + `LastRunWasBest` (position pinned in Step 1); `CombatFeedback.GlitchPassPrefab` (wired by builder Task 3).

- [ ] **Step 1: RunManager:** add `public enum ResetCause { PostGrade, Death, Void }` **NESTED inside the RunManager class** (review: every consumer snippet compiles only against the nested form), `public ResetCause LastResetCause { get; private set; }`, `public bool LastRunWasBest { get; private set; }`.
  - In `CompleteRun`: set `LastRunWasBest = Core.FinalScore > BestScore;` at EXACTLY this position — AFTER the `Core.CompleteRun()` call (FinalScore is computed inside it; setting the flag before it reads 0 and is false forever) and IMMEDIATELY BEFORE the `if (Core.FinalScore > BestScore)` persist block (which overwrites BestScore).
  - Set the cause at each restart origin: kill-plane handler → `LastResetCause = ResetCause.Void;` (where `_restartPending` is set); the `IsDead` branches → `ResetCause.Death`; the Complete-dwell restart → `ResetCause.PostGrade`. Set it BEFORE `RestartRun()` runs so `RunReset` subscribers read it correctly.
- [ ] **Step 2: Write MachineVoice.cs:**

```csharp
using UnityEngine;
using AlienSkies.Combat;
using AlienSkies.Audio; // MusicManager is a SIBLING namespace — review-caught CS0246

namespace AlienSkies.Run
{
    /// <summary>Scene singleton: maps run events to MachineVoiceCore triggers
    /// and renders the active line with a glitch-materialize effect. The
    /// Gyroscope has opinions about you.</summary>
    public sealed class MachineVoice : MonoBehaviour
    {
        const float ChainWindow = 4f;
        const int ChainCount = 3;
        const float IdleAfter = 12f;

        public MachineVoiceCore Core { get; private set; }

        RunManager _run;
        PlayerCombat _player;
        MusicManager _music;
        RunState _lastState = RunState.Ready;
        float _idleTimer;
        int _recentKills;
        float _chainTimer;
        GUIStyle _style;

        static readonly char[] GlitchGlyphs = "▓▒░#/\\|01<>¥".ToCharArray();

        void Awake() => Core = new MachineVoiceCore((int)System.DateTime.Now.Ticks);

        void Start()
        {
            _run = RunManager.Instance;
            _music = FindFirstObjectByType<MusicManager>();
            if (_run != null)
            {
                _run.RunStarted += HandleRunStarted;
                _run.RunReset += HandleRunReset;
                _run.LevelChanged += HandleLevelChanged;
            }
            BindPlayer();
        }

        void OnDestroy()
        {
            if (_run != null)
            {
                _run.RunStarted -= HandleRunStarted;
                _run.RunReset -= HandleRunReset;
                _run.LevelChanged -= HandleLevelChanged;
            }
            if (_player != null) _player.OnHitConfirmed -= HandleHit;
        }

        void BindPlayer()
        {
            foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            {
                if (!pc.IsLocal) continue;
                _player = pc;
                _player.OnHitConfirmed += HandleHit;
                return;
            }
        }

        void HandleRunStarted()
        {
            _idleTimer = 0f;
            Core.Post(VoiceTrigger.RunStart);
        }

        void HandleRunReset()
        {
            if (_run.LastResetCause == RunManager.ResetCause.Void) Core.Post(VoiceTrigger.DeathByVoid);
            else if (_run.LastResetCause == RunManager.ResetCause.Death) Core.Post(VoiceTrigger.Death);
            // PostGrade: the rank line already spoke on completion.
        }

        // Boot guard (review): RunManager.Start's SelectLevel(0) fires
        // LevelChanged; if OUR Start ran first we'd taunt at boot and the
        // cooldown would eat the first RunStart line. Speak only after the
        // first Update frame.
        bool _sawFirstFrame;

        void HandleLevelChanged()
        {
            if (_sawFirstFrame) Core.Post(VoiceTrigger.LevelSwitch);
        }

        void HandleHit(float damage, bool killed)
        {
            if (!killed) return;
            _recentKills++;
            _chainTimer = ChainWindow;
            float heat = _music != null ? _music.Core.Intensity : 0f;
            if (_recentKills >= ChainCount) { _recentKills = 0; Core.Post(VoiceTrigger.Chain); }
            else if (heat >= 0.99f) Core.Post(VoiceTrigger.KillAtMaxHeat);
            else Core.Post(VoiceTrigger.Kill);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Core.Tick(dt);
            if (_player == null) BindPlayer();
            if (_run == null) return;

            if (_chainTimer > 0f) { _chainTimer -= dt; if (_chainTimer <= 0f) _recentKills = 0; }

            // Rank commentary the frame the grade lands.
            var state = _run.Core.State;
            if (state == RunState.Complete && _lastState != RunState.Complete)
            {
                // EXCLUSIVE (review: both are priority — the second post would
                // overwrite the first before a single OnGUI ran). A new best
                // wins the voice line; the rank is on the grade screen anyway.
                if (_run.LastRunWasBest) Core.Post(VoiceTrigger.NewBest);
                else switch (_run.Core.Rank)
                {
                    case "S": Core.Post(VoiceTrigger.RankS); break;
                    case "A": Core.Post(VoiceTrigger.RankA); break;
                    case "D": Core.Post(VoiceTrigger.RankD); break;
                    default: Core.Post(VoiceTrigger.RankMid); break;
                }
            }
            _lastState = state;
            _sawFirstFrame = true;

            // Idle nagging at the start line.
            if (state == RunState.Ready)
            {
                _idleTimer += dt;
                if (_idleTimer >= IdleAfter) { _idleTimer = 0f; Core.Post(VoiceTrigger.Idle); }
            }
            else _idleTimer = 0f;
        }

        void OnGUI()
        {
            string line = Core.CurrentLine;
            if (line == null) return;
            if (_style == null)
            {
                _style = new GUIStyle
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            // Materialize: early in the line's life, characters render as
            // glitch glyphs with decreasing probability; a per-second flicker
            // corrupts one random slice later on.
            float age = Core.LineAge;
            float corrupt = Mathf.Clamp01(1f - age / 0.5f);
            var chars = line.ToCharArray();
            var rng = new System.Random(line.GetHashCode() ^ (int)(age * 30f));
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ') continue;
                if (rng.NextDouble() < corrupt * 0.8f)
                    chars[i] = GlitchGlyphs[rng.Next(GlitchGlyphs.Length)];
            }
            string shown = new string(chars);

            var rect = new Rect(Screen.width / 2f - 500f, Screen.height * 0.70f, 1000f, 32f);
            float fade = Mathf.Clamp01((MachineVoiceCore.LineDuration - age) / 0.6f);
            var magenta = AlienSkies.Palette.Magenta;
            _style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * fade);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), shown, _style);
            _style.normal.textColor = new Color(magenta.r, magenta.g, magenta.b, fade);
            GUI.Label(rect, shown, _style);
        }
    }
}
```

- [ ] **Step 3: GlitchPass.shader** at `Assets/AlienSkies/Shaders/GlitchPass.shader` — copy `ImpactFrame.shader`'s EXACT SubShader header (Tags/ZTest Always/ZWrite Off/Cull Off/Blend SrcAlpha OneMinusSrcAlpha — that header is proven in the shipped player), same `_Progress` property, new fragment:

```hlsl
            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float4 frag(V i) : SV_Target
            {
                // Signal-corruption overlay: horizontal bands slide and flash,
                // RGB-split edges, block noise, scanlines. Fades with _Progress.
                float t = _Progress;
                float band = floor(i.uv.y * 24.0);
                float bandJit = hash(float2(band, floor(t * 20.0)));
                float slide = step(0.6, bandJit) * (bandJit - 0.5) * 0.4;

                float block = step(0.75, hash(float2(floor((i.uv.x + slide) * 16.0), band + floor(t * 12.0))));
                float scan = 0.5 + 0.5 * sin(i.uv.y * 900.0);

                float3 col = float3(0.0, 0.0, 0.0);
                col.r = block * hash(float2(band, 1.0));
                col.g = block * hash(float2(band, 2.0)) * 0.9;
                col.b = block * hash(float2(band, 3.0)) * 1.2;
                col += float3(1.0, 0.25, 0.75) * step(0.92, bandJit) * 0.8; // magenta tears

                float alpha = (block * 0.55 + 0.12 * scan) * (1.0 - t) * (1.0 - t);
                return float4(col * 2.0, alpha);
            }
```

  (Struct/vert/property scaffolding identical to ImpactFrame.shader; shader name `"AlienSkies/GlitchPass"`.)
- [ ] **Step 4: CombatFeedback:** add `public GameObject GlitchPassPrefab;` next to `ImpactFramePrefab`; in `Start()` (add one if absent — keep the existing OnEnable/OnDisable event wiring untouched): `if (AlienSkies.Run.RunManager.Instance != null) AlienSkies.Run.RunManager.Instance.RunReset += HandleRunReset;` (+ unsubscribe in `OnDestroy`, which now exists from the blip cleanup) with:

```csharp
        void HandleRunReset()
        {
            var run = AlienSkies.Run.RunManager.Instance;
            if (run == null || run.LastResetCause == AlienSkies.Run.RunManager.ResetCause.PostGrade) return;
            if (GlitchPassPrefab == null || ShakeTarget == null) return;
            var fx = Instantiate(GlitchPassPrefab, ShakeTarget);
            fx.transform.localPosition = new Vector3(0f, 0f, 2f);
            fx.transform.localRotation = Quaternion.identity;
            Destroy(fx, 1f);
        }
```

  Spawn only for the LOCAL player's feedback (bots' CombatFeedback also subscribes — guard with `if (!Combat.IsLocal) return;` at the top, or subscribe only when local; check how the field `Combat` is exposed and gate accordingly).
- [ ] **Step 5: Gate.** `bash tools/run_tests.sh` → PASS 110.
- [ ] **Step 6: Commit** — `feat: the Voice of the Machine + glitch pass shader; RunManager reset causes`

---

### Task 3: Builder + regenerate + verify

- [ ] **Step 1: Builder:** (a) `BuildGlitchPassPrefab()` next to `BuildImpactFramePrefab` — same pattern exactly: quad (DestroyImmediate collider), scale `(4.2, 2.4, 1)`, asset-backed material `GetOrCreateMaterial("GlitchPass", () => new Material(Shader.Find("AlienSkies/GlitchPass")))` on `sharedMaterial`, `ImpactFrameFade` component with `Life = 0.35f`, save to `Assets/AlienSkies/Prefabs/GlitchPass.prefab`; wire `feedback.GlitchPassPrefab = BuildGlitchPassPrefab();` next to the ImpactFrame wiring. (b) A `MachineVoice` scene object: `new GameObject("MachineVoice").AddComponent<AlienSkies.Run.MachineVoice>();` next to the RunManager creation (no wiring — it finds everything at runtime).
- [ ] **Step 2:** Rebuild scene (`BuildNeonVector`; exit 0; zero `missing`/`error CS`). **Step 3:** `bash tools/run_tests.sh` → 110/110. **Step 4:** screenshots (8 PNGs fresh — the voice/glitch are runtime-only, no new shot needed; verify `GlitchPass.prefab`'s `m_Materials` is a real guid, NOT `{fileID: 0}`). **Step 5:** `bash tools/build_player.sh` → exit 0 (script is truth-telling now). **Step 6:** Commit regenerated artifacts — `build: regenerate scene with MachineVoice + glitch prefab`.

---

### Task 4: Docs + PR (orchestrator-owned)

ARCHITECTURE (§2 glue + a §3.x Voice paragraph + §5 shader list 7), README (one flavor line), CLAUDE.md (RESUME/diary), memory, push, PR (base: feature/second-sphere or stacked PR to main per the morning plan).
