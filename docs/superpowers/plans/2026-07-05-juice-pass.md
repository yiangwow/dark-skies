# Juice Pass Implementation Plan (GitHub PR #8)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make combat *feel* devastating — Hovl VFX for explosions/impacts, hitmarkers with hit/kill sounds, damage flash when hurt, trauma-based screenshake.

**Architecture:** Same pattern as movement/combat: a pure-C# `FeedbackCore` (trauma/timer math, unit-tested) driven by an owner-only `CombatFeedback` MonoBehaviour (camera shake, blip audio). Hit confirmation travels server → attacker via TargetRpc; the victim detects damage locally from the health SyncVar change (no extra RPC). `CombatFx` upgrades from graybox primitives to Hovl prefabs with graybox fallback.

**Tech Stack:** Hovl "Sci-fi effects 2" (URP Shader Graph, at `Assets/Hovl Studio/`), FishNet `[TargetRpc]`, `SyncVar<T>.OnChange`, procedural `AudioClip.Create` blips, `Mathf.PerlinNoise` shake.

## Global Constraints

- Branch `feature/juice-pass` off main (PR #7 merged at 19c13b5).
- Git author the owner's git identity; NO Co-Authored-By; never push main; NEVER `git add -A`; every Assets/ file needs its .meta.
- Repo root `/mnt/c/Users/computer/My project` — always quote the space.
- Tests: `bash tools/run_tests.sh` (Unity Editor CLOSED). Suite is 47 green; this plan adds 6 (53 total).
- Commit the whole `Assets/Hovl Studio` pack (69MB, private repo, license OK) plus `Packages/manifest.json` + `Packages/packages-lock.json` (import added `com.unity.shadergraph` 17.5.0 + `com.unity.mathematics` 1.4.0 — both required, previously uncommitted).
- Hovl prefab picks (playtest may swap): rocket explosion + death = `Chain explosions.prefab`, hitscan impact = `Hit sci-fi.prefab`. Hovl prefabs do NOT self-destroy — CombatFx destroys after a fixed lifetime.
- Trauma model: shake magnitude = trauma², trauma decays linearly. All feel numbers live as consts in FeedbackCore (v1; promote to config only if playtest demands).
- v1 non-goals: muzzle flashes (kick+sound covers it), directional damage indicator, remote-player weapon models, Rocket trail swap.

---

### Task 1: FeedbackCore (TDD)

**Files:**
- Create: `Assets/AlienSkies/Scripts/FeedbackCore.cs`
- Create: `Assets/AlienSkies/Tests/FeedbackCoreTests.cs`

**Interfaces:**
- Produces: `AlienSkies.Combat.FeedbackCore`: consts `TraumaDecay=1.4f, HitmarkerTime=0.18f, KillFlashTime=0.9f, DamageFlashDecay=1.2f`; reads `float Trauma, ShakeMagnitude, HitmarkerRemaining, KillFlashRemaining, DamageFlash`; methods `void AddTrauma(float)`, `void RegisterHit(bool killed)`, `void RegisterDamageTaken(float fraction)`, `void Tick(float dt)`.

- [ ] **Step 1: Write the failing tests** — `Assets/AlienSkies/Tests/FeedbackCoreTests.cs`:

```csharp
using NUnit.Framework;
using AlienSkies.Combat;

namespace AlienSkies.Tests
{
    public class FeedbackCoreTests
    {
        [Test]
        public void AddTrauma_ClampsToOne()
        {
            var f = new FeedbackCore();
            f.AddTrauma(0.7f);
            f.AddTrauma(0.7f);
            Assert.AreEqual(1f, f.Trauma, 1e-4f);
        }

        [Test]
        public void Tick_DecaysTraumaAndFloorsAtZero()
        {
            var f = new FeedbackCore();
            f.AddTrauma(0.5f);
            f.Tick(0.1f);
            Assert.AreEqual(0.5f - FeedbackCore.TraumaDecay * 0.1f, f.Trauma, 1e-4f);
            f.Tick(10f);
            Assert.AreEqual(0f, f.Trauma, 1e-4f);
        }

        [Test]
        public void ShakeMagnitude_IsTraumaSquared()
        {
            var f = new FeedbackCore();
            f.AddTrauma(0.5f);
            Assert.AreEqual(0.25f, f.ShakeMagnitude, 1e-4f);
        }

        [Test]
        public void RegisterHit_SetsHitmarker_OnlyKillSetsKillFlash()
        {
            var f = new FeedbackCore();
            f.RegisterHit(false);
            Assert.AreEqual(FeedbackCore.HitmarkerTime, f.HitmarkerRemaining, 1e-4f);
            Assert.AreEqual(0f, f.KillFlashRemaining, 1e-4f);
            f.RegisterHit(true);
            Assert.AreEqual(FeedbackCore.KillFlashTime, f.KillFlashRemaining, 1e-4f);
        }

        [Test]
        public void RegisterDamageTaken_RaisesFlashAndTrauma()
        {
            var f = new FeedbackCore();
            f.RegisterDamageTaken(0.3f);
            Assert.Greater(f.DamageFlash, 0f);
            Assert.Greater(f.Trauma, 0f);
            f.RegisterDamageTaken(5f);
            Assert.AreEqual(1f, f.DamageFlash, 1e-4f, "flash clamps to 1");
        }

        [Test]
        public void Tick_CountsDownTimers()
        {
            var f = new FeedbackCore();
            f.RegisterHit(true);
            f.RegisterDamageTaken(0.5f);
            f.Tick(FeedbackCore.HitmarkerTime + 0.01f);
            Assert.AreEqual(0f, f.HitmarkerRemaining, 1e-4f);
            Assert.Greater(f.KillFlashRemaining, 0f, "kill flash outlives hitmarker");
            f.Tick(10f);
            Assert.AreEqual(0f, f.KillFlashRemaining, 1e-4f);
            Assert.AreEqual(0f, f.DamageFlash, 1e-4f);
        }
    }
}
```

- [ ] **Step 2: Run to verify compile failure.** Editor closed, `cd "/mnt/c/Users/computer/My project" && bash tools/run_tests.sh` → expect CS0246 `FeedbackCore`.

- [ ] **Step 3: Implement** — `Assets/AlienSkies/Scripts/FeedbackCore.cs`:

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Pure feedback state: trauma-based screenshake (magnitude =
    /// trauma², so small hits whisper and big hits roar) plus hitmarker /
    /// kill-flash / damage-flash timers. CombatFeedback drives it per frame.</summary>
    public sealed class FeedbackCore
    {
        public const float TraumaDecay = 1.4f;      // trauma units per second
        public const float HitmarkerTime = 0.18f;   // seconds
        public const float KillFlashTime = 0.9f;    // seconds
        public const float DamageFlashDecay = 1.2f; // flash units per second

        public float Trauma { get; private set; }
        public float ShakeMagnitude => Trauma * Trauma;
        public float HitmarkerRemaining { get; private set; }
        public float KillFlashRemaining { get; private set; }
        public float DamageFlash { get; private set; }

        public void AddTrauma(float amount) => Trauma = Mathf.Clamp01(Trauma + amount);

        public void RegisterHit(bool killed)
        {
            HitmarkerRemaining = HitmarkerTime;
            if (killed) KillFlashRemaining = KillFlashTime;
        }

        /// <summary>fraction = damage / max health.</summary>
        public void RegisterDamageTaken(float fraction)
        {
            DamageFlash = Mathf.Clamp01(DamageFlash + fraction * 2f);
            AddTrauma(fraction * 1.2f);
        }

        public void Tick(float dt)
        {
            Trauma = Mathf.Max(0f, Trauma - TraumaDecay * dt);
            HitmarkerRemaining = Mathf.Max(0f, HitmarkerRemaining - dt);
            KillFlashRemaining = Mathf.Max(0f, KillFlashRemaining - dt);
            DamageFlash = Mathf.Max(0f, DamageFlash - DamageFlashDecay * dt);
        }
    }
}
```

- [ ] **Step 4: Run suite** → `result=Passed total=53 passed=53 failed=0`.

- [ ] **Step 5: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/FeedbackCore.cs Assets/AlienSkies/Scripts/FeedbackCore.cs.meta Assets/AlienSkies/Tests/FeedbackCoreTests.cs Assets/AlienSkies/Tests/FeedbackCoreTests.cs.meta && git commit -m "feat: FeedbackCore - trauma shake + feedback timers as pure tested C#"
```

---

### Task 2: CombatFx → Hovl prefabs (graybox fallback)

**Files:**
- Modify: `Assets/AlienSkies/Scripts/CombatFx.cs`
- Modify: `Assets/AlienSkies/Scripts/PlayerCombat.cs` (tracer RPC gains a hit flag; prefab fields; statics assignment)

**Interfaces:**
- Produces: `CombatFx` statics `public static GameObject ExplosionPrefab; public static GameObject ImpactPrefab; public static event System.Action<Vector3> ExplosionSpawned;` and `public static void Impact(Vector3 at)`. `Explosion(at, radius)` fires `ExplosionSpawned` ALWAYS (prefab or fallback). `PlayerCombat` gains fields `public GameObject ExplosionFxPrefab; public GameObject ImpactFxPrefab;` (builder-wired) and assigns the statics in `Awake`.

- [ ] **Step 1: Rework `CombatFx.cs`** — replace the two static spawners, keep the graybox Update animation for fallbacks:

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Combat visuals dispatcher. Prefers the Hovl prefabs (wired by
    /// PlayerCombat from the builder); falls back to graybox primitives so a
    /// missing pack never breaks a build. Local-only, spawned from Observer
    /// RPCs; Hovl prefabs don't self-destroy so we Destroy() on a timer.</summary>
    public sealed class CombatFx : MonoBehaviour
    {
        public static GameObject ExplosionPrefab;
        public static GameObject ImpactPrefab;
        /// <summary>Every client hears every explosion (proximity screenshake).</summary>
        public static event System.Action<Vector3> ExplosionSpawned;

        float _life, _maxLife;
        Material _mat;
        LineRenderer _line;
        float _maxScale;

        public static void Explosion(Vector3 at, float radius)
        {
            ExplosionSpawned?.Invoke(at);
            if (ExplosionPrefab != null)
            {
                var fx = Object.Instantiate(ExplosionPrefab, at, Quaternion.identity);
                fx.transform.localScale = Vector3.one * (radius / 4f); // authored ~4m
                Object.Destroy(fx, 3f);
                return;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            var fb = go.AddComponent<CombatFx>();
            fb._maxLife = 0.35f;
            fb._maxScale = radius * 2f;
            fb._mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fb._mat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.15f) * 3f);
            go.GetComponent<Renderer>().sharedMaterial = fb._mat;
        }

        public static void Impact(Vector3 at)
        {
            if (ImpactPrefab == null) return; // hitscan already has tracers
            var fx = Object.Instantiate(ImpactPrefab, at, Quaternion.identity);
            Object.Destroy(fx, 2f);
        }

        public static void Tracer(Vector3 from, Vector3 to)
        {
            var go = new GameObject("Tracer");
            var fx = go.AddComponent<CombatFx>();
            fx._maxLife = 0.09f;
            fx._line = go.AddComponent<LineRenderer>();
            fx._line.positionCount = 2;
            fx._line.SetPosition(0, from);
            fx._line.SetPosition(1, to);
            fx._line.startWidth = 0.05f;
            fx._line.endWidth = 0.02f;
            fx._mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fx._mat.SetColor("_BaseColor", new Color(0.55f, 0.9f, 1f) * 4f);
            fx._line.material = fx._mat;
        }

        void Update()
        {
            _life += Time.deltaTime;
            float t = _life / _maxLife;
            if (t >= 1f) { Destroy(_mat); Destroy(gameObject); return; }
            if (_line == null)
                transform.localScale = Vector3.one * Mathf.Lerp(1f, _maxScale, t);
        }
    }
}
```

- [ ] **Step 2: PlayerCombat edits.** (a) Add fields next to `public GameObject RocketPrefab;`:

```csharp
        public GameObject ExplosionFxPrefab; // Hovl; CombatFx falls back to graybox if null
        public GameObject ImpactFxPrefab;
```

(b) In `Awake()`, first lines:

```csharp
            if (ExplosionFxPrefab != null) CombatFx.ExplosionPrefab = ExplosionFxPrefab;
            if (ImpactFxPrefab != null) CombatFx.ImpactPrefab = ImpactFxPrefab;
```

(c) Hitscan impacts: in `CmdFireHitscan`, change the raycast block to remember the hit and pass it to the tracer RPC:

```csharp
                Vector3 pdir = ApplySpread(dir, stats.SpreadDegrees, p);
                Vector3 end;
                bool hitSomething = Physics.Raycast(origin, pdir, out var hit, 500f);
                if (hitSomething)
                {
                    end = hit.point;
                    var victim = hit.collider.GetComponentInParent<PlayerCombat>();
                    if (victim != null && victim != this)
                        victim.ServerTakeDamage(stats.Damage, this);
                }
                else end = origin + pdir * 500f;
                RpcTracer(origin + pdir * 1.0f, end, hitSomething);
```

(d) Tracer RPC gains the flag:

```csharp
        [ObserversRpc]
        void RpcTracer(Vector3 from, Vector3 to, bool hitSomething)
        {
            CombatFx.Tracer(from, to);
            if (hitSomething) CombatFx.Impact(to);
        }
```

- [ ] **Step 3: Run suite** (compile gate) → 53/53.

- [ ] **Step 4: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/CombatFx.cs Assets/AlienSkies/Scripts/PlayerCombat.cs && git commit -m "feat: CombatFx upgraded to Hovl prefabs with graybox fallback + hitscan impacts"
```

---

### Task 3: Hit confirmation + damage detection (network path)

**Files:**
- Modify: `Assets/AlienSkies/Scripts/PlayerCombat.cs`

**Interfaces:**
- Produces (Task 4 relies on): `PlayerCombat` events `public event System.Action<float, bool> OnHitConfirmed;` (damage, killed — fires on the ATTACKER's owner) and `public event System.Action<float> OnDamaged;` (damage as fraction of max health — fires on the VICTIM's owner).

- [ ] **Step 1: Apply the four edits.**

(a) Add `using FishNet.Connection;` to the usings.

(b) Add events next to `OnWeaponChanged`:

```csharp
        public event System.Action<float, bool> OnHitConfirmed; // damage, killed (attacker owner)
        public event System.Action<float> OnDamaged;            // damage fraction (victim owner)
```

(c) Health-change detection: in `Awake()` add `_health.OnChange += HandleHealthChanged;`, in `OnDestroy` add `_health.OnChange -= HandleHealthChanged;` (keep the existing `_dead` lines), and add:

```csharp
        void HandleHealthChanged(float prev, float next, bool asServer)
        {
            // Victim-side hurt feedback straight off the SyncVar — no extra RPC.
            if (!asServer && IsOwner && next < prev)
                OnDamaged?.Invoke((prev - next) / S.MaxHealth);
        }
```

(d) Attacker notification: in `ServerTakeDamage`, right after `_health.Value = _core.Health;` insert:

```csharp
            if (attacker != null && attacker != this)
                attacker.ServerNotifyHit(amount, died);
```

and add the pair of methods next to `ServerAddFrag`:

```csharp
        public void ServerNotifyHit(float damage, bool killed) =>
            TargetHitConfirmed(Owner, damage, killed);

        [TargetRpc]
        void TargetHitConfirmed(NetworkConnection conn, float damage, bool killed) =>
            OnHitConfirmed?.Invoke(damage, killed);
```

- [ ] **Step 2: Run suite** (compile gate — FishNet codegen must accept the TargetRpc) → 53/53.

- [ ] **Step 3: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/PlayerCombat.cs && git commit -m "feat: hit confirmation TargetRpc + victim damage detection off health SyncVar"
```

---

### Task 4: CombatFeedback (shake + blips) + HUD overlays

**Files:**
- Create: `Assets/AlienSkies/Scripts/CombatFeedback.cs`
- Modify: `Assets/AlienSkies/Scripts/MovementHUD.cs`

**Interfaces:**
- Consumes: `FeedbackCore` (Task 1), `PlayerCombat.OnFired/OnHitConfirmed/OnDamaged` + `CombatFx.ExplosionSpawned` (Tasks 2–3).
- Produces: `CombatFeedback` fields `public PlayerCombat Combat; public Transform ShakeTarget; public AudioSource FeedbackAudio;`, read `public FeedbackCore Core { get; }`. `MovementHUD` gains `public AlienSkies.Combat.CombatFeedback Feedback;`.

- [ ] **Step 1: Create `Assets/AlienSkies/Scripts/CombatFeedback.cs`:**

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Owner-only combat feel: Perlin camera shake from FeedbackCore
    /// trauma, procedural hit/kill blips (no asset needed), trauma sources
    /// (firing, being hit, nearby explosions). Shakes the CAMERA child, not the
    /// pivot — the pivot's rotation is owned by the motor's pitch control.</summary>
    public sealed class CombatFeedback : MonoBehaviour
    {
        public PlayerCombat Combat;
        public Transform ShakeTarget;   // MainCamera transform (zero local pose at rest)
        public AudioSource FeedbackAudio;

        public FeedbackCore Core { get; } = new FeedbackCore();

        AudioClip _hitBlip, _killBlip;
        float _noiseSeed;

        void Awake()
        {
            _noiseSeed = Random.value * 100f;
            _hitBlip = MakeBlip(1400f, 0.05f);
            _killBlip = MakeBlip(700f, 0.25f);
        }

        /// <summary>Sine blip with a squared decay envelope — reads as a crisp
        /// arcade hit tick without needing an audio asset.</summary>
        static AudioClip MakeBlip(float freq, float duration)
        {
            const int rate = 44100;
            int n = (int)(rate * duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = 1f - i / (float)n;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * i / rate) * env * env * 0.5f;
            }
            var clip = AudioClip.Create($"blip{(int)freq}", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnEnable()
        {
            CombatFx.ExplosionSpawned += HandleExplosion;
            if (Combat == null) return;
            Combat.OnFired += HandleFired;
            Combat.OnHitConfirmed += HandleHitConfirmed;
            Combat.OnDamaged += HandleDamaged;
        }

        void OnDisable()
        {
            CombatFx.ExplosionSpawned -= HandleExplosion;
            if (Combat == null) return;
            Combat.OnFired -= HandleFired;
            Combat.OnHitConfirmed -= HandleHitConfirmed;
            Combat.OnDamaged -= HandleDamaged;
        }

        void HandleFired(int weapon) =>
            Core.AddTrauma(weapon == 0 ? 0.25f : weapon == 2 ? 0.18f : 0.08f);

        void HandleHitConfirmed(float damage, bool killed)
        {
            Core.RegisterHit(killed);
            if (FeedbackAudio != null)
                FeedbackAudio.PlayOneShot(killed ? _killBlip : _hitBlip, 0.7f);
        }

        void HandleDamaged(float fraction) => Core.RegisterDamageTaken(fraction);

        void HandleExplosion(Vector3 at)
        {
            if (ShakeTarget == null) return;
            float dist = Vector3.Distance(ShakeTarget.position, at);
            Core.AddTrauma(Mathf.Clamp01(1f - dist / 30f) * 0.7f);
        }

        void Update()
        {
            Core.Tick(Time.deltaTime);
            if (ShakeTarget == null) return;
            float m = Core.ShakeMagnitude;
            if (m <= 0.0001f)
            {
                ShakeTarget.localPosition = Vector3.zero;
                ShakeTarget.localRotation = Quaternion.identity;
                return;
            }
            float t = Time.time * 18f;
            ShakeTarget.localPosition = new Vector3(
                Mathf.PerlinNoise(_noiseSeed, t) - 0.5f,
                Mathf.PerlinNoise(_noiseSeed + 17f, t) - 0.5f,
                0f) * (0.35f * m);
            ShakeTarget.localRotation = Quaternion.Euler(
                0f, 0f, (Mathf.PerlinNoise(_noiseSeed + 31f, t) - 0.5f) * 6f * m);
        }
    }
}
```

- [ ] **Step 2: Extend `MovementHUD.cs`.** Add field next to `Combat`:

```csharp
        public AlienSkies.Combat.CombatFeedback Feedback;
```

In `EnsureStyles()`, after `_cross` add:

```csharp
            _hitmark = new GUIStyle(_cross) { fontSize = 34 };
            _hitmark.normal.textColor = new Color(1f, 0.85f, 0.25f);
            _killLabel = new GUIStyle(_cross) { fontSize = 36 };
            _killLabel.normal.textColor = new Color(1f, 0.3f, 0.2f);
```

and the field declarations next to `_label, _cross`:

```csharp
        GUIStyle _hitmark, _killLabel;
```

At the very END of `OnGUI()` (after the Combat block) append:

```csharp
            if (Feedback == null) return;
            var fc = Feedback.Core;
            if (fc.DamageFlash > 0f)
            {
                GUI.color = new Color(1f, 0.08f, 0.08f, fc.DamageFlash * 0.35f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            if (fc.HitmarkerRemaining > 0f)
                GUI.Label(new Rect(Screen.width / 2f - 20f, Screen.height / 2f - 20f, 40f, 40f),
                          "×", _hitmark);
            if (fc.KillFlashRemaining > 0f)
                GUI.Label(new Rect(Screen.width / 2f - 150f, Screen.height / 2f - 110f, 300f, 44f),
                          "K I L L", _killLabel);
```

- [ ] **Step 3: Run suite** (compile gate) → 53/53.

- [ ] **Step 4: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/CombatFeedback.cs Assets/AlienSkies/Scripts/CombatFeedback.cs.meta Assets/AlienSkies/Scripts/MovementHUD.cs && git commit -m "feat: CombatFeedback - Perlin screenshake, hit/kill blips, damage flash HUD"
```

---

### Task 5: Builder wiring + vendor Hovl pack

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs`
- Commit: `Assets/Hovl Studio` (whole pack, 69MB), `Packages/manifest.json`, `Packages/packages-lock.json`, regenerated prefabs/scene

- [ ] **Step 1: In `BuildPlayer`, after `hud.Combat = combat;` add:**

```csharp
            combat.ExplosionFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Hovl Studio/Sci-fi effects 2/Prefabs/Chain explosions.prefab");
            combat.ImpactFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Hovl Studio/Sci-fi effects 2/Prefabs/Hit sci-fi.prefab");
            if (combat.ExplosionFxPrefab == null) Debug.LogError("missing Hovl explosion prefab");
            if (combat.ImpactFxPrefab == null) Debug.LogError("missing Hovl impact prefab");

            var feedback = player.AddComponent<CombatFeedback>();
            feedback.Combat = combat;
            feedback.ShakeTarget = camGO.transform;
            feedback.FeedbackAudio = fireAudio;
            hud.Feedback = feedback;
```

- [ ] **Step 2: Rebuild scene headlessly** (editor closed):

```bash
cd "/mnt/c/Users/computer/My project" && timeout -k 30 570 "/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\Users\computer\My project" -executeMethod AlienSkies.EditorTools.PlaygroundBuilder.Build -logFile - 2>&1 | grep -iE "error CS|missing Hovl|PlaygroundBuilder: saved" | head -5
```

Expected: `PlaygroundBuilder: saved ...`, no "missing Hovl".

- [ ] **Step 3: Run suite** → 53/53.

- [ ] **Step 4: Commit** (pack + manifest + builder + regenerated assets):

```bash
cd "/mnt/c/Users/computer/My project" && git add "Assets/Hovl Studio" "Assets/Hovl Studio.meta" Packages/manifest.json Packages/packages-lock.json Assets/AlienSkies/Editor/PlaygroundBuilder.cs Assets/AlienSkies/Prefabs/NetworkPlayer.prefab Assets/AlienSkies/Prefabs/Rocket.prefab Assets/AlienSkies/Prefabs/Rocket.prefab.meta Assets/AlienSkies/Scenes/MovementPlayground.unity Assets/DefaultPrefabObjects.asset && git commit -m "feat: juice wired into scene - Hovl FX prefabs, feedback rig, pack vendored

Vendors Hovl Sci-fi effects 2 (69MB, URP Shader Graph; private repo,
license OK) and commits the shadergraph/mathematics manifest deps the
imports added."
```

---

### Task 6: FX screenshot verification, docs, push, PR

**Files:**
- Modify: `Assets/AlienSkies/Editor/SceneScreenshot.cs`
- Modify: `CLAUDE.md` (RESUME HERE + diary)

- [ ] **Step 1: Add an FX shot to `SceneScreenshot.Capture`** after the weapons-shot block (particles need `Simulate()` — batchmode never plays them):

```csharp
            // Hovl FX verification: pre-warmed explosion in front of the camera.
            var fxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Hovl Studio/Sci-fi effects 2/Prefabs/Chain explosions.prefab");
            if (fxPrefab != null)
            {
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(fxPrefab);
                fx.transform.position = new Vector3(0f, 502f, 7f);
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
                    ps.Simulate(0.25f, true, true);
                Shot(new Vector3(0f, 501.6f, 0f), Quaternion.identity, "TestResults/shot_fx.png");
                Object.DestroyImmediate(fx);
            }
```

- [ ] **Step 2: Capture and view.** Run SceneScreenshot.Capture headlessly; Read `TestResults/shot_fx.png` — expect a visible explosion effect (not empty, not magenta). If magenta: the Hovl URP materials need the Shader Graph package compiled — check `Packages/manifest.json` committed and re-run. If empty: raise Simulate time to 0.4f.

- [ ] **Step 3: Update CLAUDE.md.** RESUME HERE:

```markdown
> **▶ RESUME HERE (2026-07-05):** JUICE IS IN (PR #8): Hovl explosions/impacts, hitmarkers + hit/kill blips, damage flash, trauma screenshake. Duel: Unity Play → HOST + `Builds/AlienSkies.exe` → JOIN 127.0.0.1 (rebuild exe: `bash tools/build_player.sh`). LMB fire, 1/2/3+scroll switch, F grapple, RMB-zip. Feel numbers: FeedbackCore consts + CombatConfig.asset. Next: vertical recovery (planets as launch pads leading candidate), match win condition, sky pass 2.
```

Diary entry (top of Diary section):

```markdown
- **2026-07-05** — PR #8 juice: FeedbackCore (trauma shake, 6 tests, 53 total), CombatFeedback (Perlin camera shake, procedural audio blips), hit-confirm TargetRpc + victim flash off health SyncVar, CombatFx → Hovl prefabs w/ graybox fallback, hitscan impact FX. Hovl Sci-fi effects 2 vendored whole (69MB).
```

- [ ] **Step 4: Rebuild exe, commit docs, push, PR:**

```bash
cd "/mnt/c/Users/computer/My project" && rm -f Builds/AlienSkies.exe && bash tools/build_player.sh 2>&1 | tail -1 && git add CLAUDE.md Assets/AlienSkies/Editor/SceneScreenshot.cs docs/superpowers/plans/2026-07-05-juice-pass.md && git commit -m "docs: RESUME HERE + diary for juice pass" && git push -u origin feature/juice-pass && gh pr create --title "Juice pass: Hovl VFX, hitmarkers, screenshake - combat that feels like TTGL" --body "$(cat <<'EOF'
- FeedbackCore: trauma-based screenshake (magnitude = trauma^2), pure C#, 6 tests (53 total)
- Hovl Sci-fi effects 2 explosions + hitscan impact FX (graybox fallback kept)
- Hitmarkers + procedural hit/kill audio blips (attacker knows they landed; kill gets a KILL flash)
- Damage flash + shake when YOU get hit (off the health SyncVar, no extra RPC)
- Proximity shake from every explosion on every client
- Vendors Hovl pack (69MB) + shadergraph/mathematics manifest deps

Playtest: rocket someone point-blank - screen should kick, explosion should look real, hitmarker should tick. Get rocketed - red flash + shake. Kill - KILL flash + low blip.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 5: Self-review checklist:** `git status --short` (only known dirt: SampleScene, SceneTemplateSettings, TirgamesAssets remainder), zero unpushed commits, `gh pr view 8 --json state` OPEN, exe fresh timestamp.

- [ ] **Step 6: Hand off.** Playtest checkpoints: explosion look/scale, shake amount (too much/too little), hitmarker readability, damage flash clarity, blip volume vs laser sounds.
