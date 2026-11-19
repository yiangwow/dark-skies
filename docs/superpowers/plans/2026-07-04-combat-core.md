# Combat Core Implementation Plan (GitHub PR #7)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two players duel — rocket launcher / rifle / shotgun, 100 HP, dying, 3 s respawns, frag counting — fully server-authoritative over Fish-Net.

**Architecture:** Same pattern as movement: pure-C# cores (`CombatCore` for health/death/frags, `WeaponCore` for cooldown/switching) unit-tested headlessly, with a `PlayerCombat : NetworkBehaviour` glue layer (input, ServerRpc fire, SyncVars, FX dispatch). Rockets are server-simulated `NetworkObject`s. Knockback feeds straight into `MovementCore` velocity so rocket jumps work through prediction (server writes it; reconcile carries it to the owner). Viewmodels are Tirgames models on an owner-only camera rig with procedural kick/sway/momentum-lurch.

**Tech Stack:** Fish-Net 4.7.2 ([ServerRpc]/[ObserversRpc], `SyncVar<T>.Value`, `Spawn()`/`Despawn()`), Unity 6, NUnit EditMode, Tirgames SciFiWorld weapon prefabs, Laser Weapons Sound Pack (Free).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-04-first-blood-design.md` — "PR #5 — Combat core" section (GitHub numbering drifted; this ships as PR #7).
- Branch: `feature/combat-core` (created off merged main).
- Git author the owner's git identity; NO Co-Authored-By; never push main.
- Repo root `/mnt/c/Users/computer/My project` — always quote the space.
- Tests: `bash tools/run_tests.sh` (Unity Editor CLOSED; warn user first). Suite is currently 35 green; this plan adds 12 (47 total).
- NEVER `git add -A`. Untracked-by-design: `Assets/TirgamesAssets*` and `Assets/Laser Weapons Sound Pack*` stay untracked EXCEPT the specific weapon prefabs/models/materials/sounds actually referenced — commit exactly those files (+.meta) in Task 5/6; `Assets/ImportedModels*`, `Assets/Scenes/SampleScene.unity`, `ProjectSettings/SceneTemplateSettings.json` stay untouched.
- Weapon stats (spec values, all tunable via `CombatConfig.asset`): Rocket 80 dmg direct, splash 6 m linear falloff, knockback 12 m/s, projectile 25 m/s, 0.8 s interval, self-damage ×0.5. Rifle 20 dmg hitscan 0.35 s. Shotgun 10 pellets × 6 dmg, 7° spread, 0.9 s. Health 100, respawn 3 s.
- Weapon order everywhere: index 0 = Rocket, 1 = Rifle, 2 = Shotgun (keys 1/2/3).
- KillY rule (decided): falling into space = instant self-respawn with −1 frag and full health — arcade style, no 3 s wait. Kill-by-damage = 3 s dead state, then respawn at the spawn point farthest from where you died.
- v1 non-goals (do NOT build): lag compensation, ammo, pickups, win conditions, remote-player weapon models, Hovl VFX (not yet purchased — graybox explosion/tracer FX instead). Spec's "simple kill feed" and Tab scoreboard are deliberately deferred too: in a 1v1 the FRAGS/DEATHS header is the scoreboard; revisit when there are more than two players.

---

### Task 1: CombatSettings + CombatConfig + CombatCore (TDD)

**Files:**
- Create: `Assets/AlienSkies/Scripts/CombatSettings.cs`
- Create: `Assets/AlienSkies/Scripts/CombatConfig.cs`
- Create: `Assets/AlienSkies/Scripts/CombatCore.cs`
- Create: `Assets/AlienSkies/Tests/CombatCoreTests.cs`

**Interfaces:**
- Produces (later tasks rely on exactly these):
  - `AlienSkies.Combat.WeaponStats` fields: `string Name; float Damage; float FireInterval; int Pellets; float SpreadDegrees; float ProjectileSpeed; float SplashRadius; float Knockback; float SelfDamageScale;`
  - `AlienSkies.Combat.CombatSettings` fields: `float MaxHealth; float RespawnDelay; float SwitchDelay; WeaponStats[] Weapons;` (defaults = the three spec weapons in order)
  - `AlienSkies.Combat.CombatConfig : ScriptableObject` with `public CombatSettings Settings = new CombatSettings();`
  - `AlienSkies.Combat.CombatCore`: ctor `(CombatSettings)`, `float Health`, `bool IsDead`, `int Frags`, `int Deaths`, `float RespawnRemaining`, `bool ApplyDamage(float)` (returns true only on the killing hit), `void AddFrag()`, `void AddSuicide()`, `bool TickRespawn(float dt)` (true when respawn due), `void Respawn()`.

- [ ] **Step 1: Write the failing tests** — `Assets/AlienSkies/Tests/CombatCoreTests.cs`:

```csharp
using NUnit.Framework;
using AlienSkies.Combat;

namespace AlienSkies.Tests
{
    public class CombatCoreTests
    {
        static CombatSettings S() => new CombatSettings();

        [Test]
        public void ApplyDamage_ReducesHealth()
        {
            var c = new CombatCore(S());
            c.ApplyDamage(30f);
            Assert.AreEqual(70f, c.Health, 1e-4f);
            Assert.IsFalse(c.IsDead);
        }

        [Test]
        public void ApplyDamage_KillingHit_ReturnsTrueOnceAndClampsToZero()
        {
            var c = new CombatCore(S());
            Assert.IsFalse(c.ApplyDamage(99f));
            Assert.IsTrue(c.ApplyDamage(50f), "the killing hit must report the kill");
            Assert.AreEqual(0f, c.Health, 1e-4f);
            Assert.IsTrue(c.IsDead);
            Assert.AreEqual(1, c.Deaths);
            Assert.IsFalse(c.ApplyDamage(10f), "corpses take no damage and report no kills");
            Assert.AreEqual(1, c.Deaths, "death must count once");
        }

        [Test]
        public void TickRespawn_CountsDownOnlyWhileDead()
        {
            var s = S();
            var c = new CombatCore(s);
            Assert.IsFalse(c.TickRespawn(10f), "alive players never signal respawn");
            c.ApplyDamage(999f);
            Assert.IsFalse(c.TickRespawn(s.RespawnDelay * 0.5f));
            Assert.IsTrue(c.TickRespawn(s.RespawnDelay * 0.6f), "timer elapsed -> respawn due");
        }

        [Test]
        public void Respawn_RestoresFullHealthAndLife()
        {
            var s = S();
            var c = new CombatCore(s);
            c.ApplyDamage(999f);
            c.Respawn();
            Assert.AreEqual(s.MaxHealth, c.Health, 1e-4f);
            Assert.IsFalse(c.IsDead);
        }

        [Test]
        public void FragAndSuicideCounters()
        {
            var c = new CombatCore(S());
            c.AddFrag();
            c.AddFrag();
            c.AddSuicide();
            Assert.AreEqual(1, c.Frags, "suicide costs a frag");
        }

        [Test]
        public void DefaultWeapons_MatchSpecOrderAndValues()
        {
            var s = S();
            Assert.AreEqual(3, s.Weapons.Length);
            Assert.AreEqual(25f, s.Weapons[0].ProjectileSpeed, "index 0 is the rocket");
            Assert.AreEqual(0f, s.Weapons[1].ProjectileSpeed, "index 1 is hitscan rifle");
            Assert.AreEqual(10, s.Weapons[2].Pellets, "index 2 is the shotgun");
        }
    }
}
```

- [ ] **Step 2: Run to verify compile failure.** Warn the user to close the Unity Editor, then: `cd "/mnt/c/Users/computer/My project" && bash tools/run_tests.sh` → expect CS0246 `CombatCore`/`CombatSettings` not found.

- [ ] **Step 3: Implement.**

`Assets/AlienSkies/Scripts/CombatSettings.cs`:

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Per-weapon tuning. Damage is per hit (per pellet for the shotgun);
    /// ProjectileSpeed 0 means hitscan.</summary>
    [System.Serializable]
    public class WeaponStats
    {
        public string Name = "Weapon";
        public float Damage = 20f;
        public float FireInterval = 0.35f;
        public int Pellets = 1;
        public float SpreadDegrees = 0f;
        public float ProjectileSpeed = 0f;
        public float SplashRadius = 0f;
        public float Knockback = 0f;        // impulse m/s at splash epicenter
        public float SelfDamageScale = 0.5f;
    }

    /// <summary>All combat tuning numbers. Plain class so tests construct it
    /// directly; CombatConfig (ScriptableObject) wraps it for the editor.</summary>
    [System.Serializable]
    public class CombatSettings
    {
        public float MaxHealth = 100f;
        public float RespawnDelay = 3f;
        public float SwitchDelay = 0.25f;

        public WeaponStats[] Weapons = new[]
        {
            new WeaponStats { Name = "Rocket",  Damage = 80f, FireInterval = 0.8f,  Pellets = 1,  SpreadDegrees = 0f, ProjectileSpeed = 25f, SplashRadius = 6f, Knockback = 12f, SelfDamageScale = 0.5f },
            new WeaponStats { Name = "Rifle",   Damage = 20f, FireInterval = 0.35f, Pellets = 1,  SpreadDegrees = 0f },
            new WeaponStats { Name = "Shotgun", Damage = 6f,  FireInterval = 0.9f,  Pellets = 10, SpreadDegrees = 7f },
        };
    }
}
```

`Assets/AlienSkies/Scripts/CombatConfig.cs`:

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    [CreateAssetMenu(menuName = "AlienSkies/Combat Config")]
    public class CombatConfig : ScriptableObject
    {
        public CombatSettings Settings = new CombatSettings();
    }
}
```

`Assets/AlienSkies/Scripts/CombatCore.cs`:

```csharp
namespace AlienSkies.Combat
{
    /// <summary>Pure health/death/respawn/frag state machine. No UnityEngine
    /// dependencies beyond nothing — fully unit-testable; PlayerCombat drives it
    /// on the server only.</summary>
    public sealed class CombatCore
    {
        readonly CombatSettings _s;

        public float Health { get; private set; }
        public bool IsDead { get; private set; }
        public int Frags { get; private set; }
        public int Deaths { get; private set; }
        public float RespawnRemaining { get; private set; }

        public CombatCore(CombatSettings settings)
        {
            _s = settings;
            Health = _s.MaxHealth;
        }

        /// <summary>Returns true only for the killing hit.</summary>
        public bool ApplyDamage(float amount)
        {
            if (IsDead || amount <= 0f) return false;
            Health -= amount;
            if (Health > 0f) return false;
            Health = 0f;
            IsDead = true;
            Deaths++;
            RespawnRemaining = _s.RespawnDelay;
            return true;
        }

        public void AddFrag() => Frags++;
        public void AddSuicide() => Frags--;

        /// <summary>Returns true when the respawn timer has elapsed.</summary>
        public bool TickRespawn(float dt)
        {
            if (!IsDead) return false;
            RespawnRemaining -= dt;
            return RespawnRemaining <= 0f;
        }

        public void Respawn()
        {
            Health = _s.MaxHealth;
            IsDead = false;
            RespawnRemaining = 0f;
        }
    }
}
```

- [ ] **Step 4: Run suite** → `result=Passed total=41 passed=41 failed=0` (35 + 6).

- [ ] **Step 5: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/CombatSettings.cs Assets/AlienSkies/Scripts/CombatSettings.cs.meta Assets/AlienSkies/Scripts/CombatConfig.cs Assets/AlienSkies/Scripts/CombatConfig.cs.meta Assets/AlienSkies/Scripts/CombatCore.cs Assets/AlienSkies/Scripts/CombatCore.cs.meta Assets/AlienSkies/Tests/CombatCoreTests.cs Assets/AlienSkies/Tests/CombatCoreTests.cs.meta && git commit -m "feat: CombatCore - health/death/respawn/frags as pure tested C#"
```

---

### Task 2: WeaponCore (TDD)

**Files:**
- Create: `Assets/AlienSkies/Scripts/WeaponCore.cs`
- Create: `Assets/AlienSkies/Tests/WeaponCoreTests.cs`

**Interfaces:**
- Consumes: `CombatSettings`/`WeaponStats` from Task 1.
- Produces: `AlienSkies.Combat.WeaponCore`: ctor `(CombatSettings)`, `int CurrentIndex`, `WeaponStats Current`, `float Cooldown`, `void Tick(float dt)`, `bool TryFire()`, `bool TrySwitch(int index)`, `void SwitchNext(int direction)`.

- [ ] **Step 1: Write the failing tests** — `Assets/AlienSkies/Tests/WeaponCoreTests.cs`:

```csharp
using NUnit.Framework;
using AlienSkies.Combat;

namespace AlienSkies.Tests
{
    public class WeaponCoreTests
    {
        static CombatSettings S() => new CombatSettings();

        [Test]
        public void StartsOnRocket_AndFireSetsCooldown()
        {
            var w = new WeaponCore(S());
            Assert.AreEqual(0, w.CurrentIndex);
            Assert.IsTrue(w.TryFire());
            Assert.IsFalse(w.TryFire(), "cooldown must block immediate refire");
        }

        [Test]
        public void Tick_ExpiresCooldown()
        {
            var s = S();
            var w = new WeaponCore(s);
            w.TryFire();
            w.Tick(s.Weapons[0].FireInterval + 0.01f);
            Assert.IsTrue(w.TryFire(), "cooldown elapsed -> can fire");
        }

        [Test]
        public void TrySwitch_Bounds_AndSameIndexRejected()
        {
            var w = new WeaponCore(S());
            Assert.IsFalse(w.TrySwitch(0), "already held");
            Assert.IsFalse(w.TrySwitch(-1));
            Assert.IsFalse(w.TrySwitch(3));
            Assert.IsTrue(w.TrySwitch(2));
            Assert.AreEqual(2, w.CurrentIndex);
        }

        [Test]
        public void Switch_AppliesSwapDelay()
        {
            var s = S();
            var w = new WeaponCore(s);
            w.TrySwitch(1);
            Assert.IsFalse(w.TryFire(), "swap delay must block instant fire");
            w.Tick(s.SwitchDelay + 0.01f);
            Assert.IsTrue(w.TryFire());
        }

        [Test]
        public void Switch_NeverShortensAnActiveCooldown()
        {
            var s = S();
            var w = new WeaponCore(s);
            w.TryFire(); // rocket: 0.8s cooldown
            w.TrySwitch(1);
            w.Tick(s.SwitchDelay + 0.01f);
            Assert.IsFalse(w.TryFire(), "switching must not bypass the rocket's cooldown");
        }

        [Test]
        public void SwitchNext_WrapsBothDirections()
        {
            var w = new WeaponCore(S());
            w.SwitchNext(-1);
            Assert.AreEqual(2, w.CurrentIndex);
            w.SwitchNext(1);
            Assert.AreEqual(0, w.CurrentIndex);
        }
    }
}
```

- [ ] **Step 2: Run to verify compile failure** (`WeaponCore` not found).

- [ ] **Step 3: Implement** — `Assets/AlienSkies/Scripts/WeaponCore.cs`:

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Weapon selection + fire-rate gate. One shared cooldown timer:
    /// firing sets it to the weapon's interval, switching raises it to at least
    /// the swap delay (never lowers it, so switching can't bypass a cooldown).</summary>
    public sealed class WeaponCore
    {
        readonly CombatSettings _s;

        public int CurrentIndex { get; private set; }
        public WeaponStats Current => _s.Weapons[CurrentIndex];
        public float Cooldown { get; private set; }

        public WeaponCore(CombatSettings settings) => _s = settings;

        public void Tick(float dt)
        {
            if (Cooldown > 0f) Cooldown -= dt;
        }

        public bool TryFire()
        {
            if (Cooldown > 0f) return false;
            Cooldown = Current.FireInterval;
            return true;
        }

        public bool TrySwitch(int index)
        {
            if (index < 0 || index >= _s.Weapons.Length || index == CurrentIndex) return false;
            CurrentIndex = index;
            Cooldown = Mathf.Max(Cooldown, _s.SwitchDelay);
            return true;
        }

        public void SwitchNext(int direction) =>
            TrySwitch((CurrentIndex + direction + _s.Weapons.Length) % _s.Weapons.Length);
    }
}
```

- [ ] **Step 4: Run suite** → `result=Passed total=47 passed=47 failed=0`.

- [ ] **Step 5: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/WeaponCore.cs Assets/AlienSkies/Scripts/WeaponCore.cs.meta Assets/AlienSkies/Tests/WeaponCoreTests.cs Assets/AlienSkies/Tests/WeaponCoreTests.cs.meta && git commit -m "feat: WeaponCore - cooldown + switching as pure tested C#"
```

---

### Task 3: Motor combat hooks

**Files:**
- Modify: `Assets/AlienSkies/Scripts/NetworkPlayerMotor.cs`
- Modify: `Assets/AlienSkies/Tests/ZipTests.cs` (no changes needed — listed to confirm suite still passes)

**Interfaces:**
- Produces (Task 4 relies on these):
  - `public bool InputsEnabled = true;` — when false the owner accumulates no movement inputs (look still works so corpses can watch)
  - `public event System.Action KillPlaneCrossed;` — fires SERVER-SIDE ONLY, once per fall (server ticks have no replays)
  - `public void ApplyImpulse(Vector3 impulse)` — adds to `Core` velocity (call on server; reconcile carries it to the owner — this is how rocket knockback works through prediction)
  - `public void ServerTeleport(Vector3 position)` — CharacterController-safe teleport + `Core.Reset()`

- [ ] **Step 1: Apply the four edits.**

1a. Add fields (next to `public Renderer BodyRenderer;`):

```csharp
        public bool InputsEnabled = true;   // PlayerCombat gates this while dead
        public event System.Action KillPlaneCrossed; // server-side, once per fall
```

1b. In `Update()`, gate movement-input accumulation (look stays live). Replace the line `// Accumulate one-time inputs between ticks (consumed in BuildData).` and the block after it with:

```csharp
            // Accumulate one-time inputs between ticks (consumed in BuildData).
            // Gated while dead — corpses can look around but not act.
            if (!InputsEnabled) return;
            Vector3 anchor = default;
            HasGrappleTarget = cursorLocked && GrappleAimer.TryFindAnchor(
                CameraPivot.position, CameraPivot.forward, S, transform, out anchor);
            if (cursorLocked && kb.fKey.wasPressedThisFrame && HasGrappleTarget)
            {
                _oneTime.GrappleStarted = true;
                _oneTime.GrappleAnchor = anchor;
            }
            if (!cursorLocked || kb.fKey.wasReleasedThisFrame)
                _oneTime.GrappleStopped = true;
            if (cursorLocked && mouse.rightButton.wasPressedThisFrame)
                _oneTime.ZipPressed = true;
            if (cursorLocked && kb.spaceKey.wasPressedThisFrame)
                _oneTime.JumpPressed = true;
```

1c. In `BuildData()`, change the guard line `if (!IsOwner) return default;` to:

```csharp
            if (!IsOwner || !InputsEnabled) return default;
```

1d. In `PerformReplicate`, replace the KillY line:

```csharp
            if (transform.position.y < S.KillY)
                Teleport(SpawnPoint); // deterministic: replays reach the same y the same tick
```

with:

```csharp
            if (transform.position.y < S.KillY)
            {
                if (IsServerStarted) KillPlaneCrossed?.Invoke(); // server ticks once, no replays
                Teleport(SpawnPoint); // deterministic: replays reach the same y the same tick
            }
```

1e. Add the two public methods (after `CreateReconcile`):

```csharp
        /// <summary>Server-side: inject external velocity (rocket knockback). The
        /// reconcile carries it to the owner, so it works through prediction.</summary>
        public void ApplyImpulse(Vector3 impulse) => Core.SetVelocity(Core.Velocity + impulse);

        /// <summary>Server-side respawn teleport.</summary>
        public void ServerTeleport(Vector3 position)
        {
            Teleport(position);
            Core.Reset();
        }
```

- [ ] **Step 2: Run suite** (compile gate; movement tests untouched) → `result=Passed total=47 passed=47 failed=0`.

- [ ] **Step 3: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/NetworkPlayerMotor.cs && git commit -m "feat: motor combat hooks - impulse injection, input gate, kill-plane event"
```

---

### Task 4: PlayerCombat + Rocket + graybox FX (the networked layer)

**Files:**
- Create: `Assets/AlienSkies/Scripts/PlayerCombat.cs`
- Create: `Assets/AlienSkies/Scripts/Rocket.cs`
- Create: `Assets/AlienSkies/Scripts/CombatFx.cs`
- Create: `Assets/AlienSkies/Scripts/SpawnPointRegistry.cs`

**Interfaces:**
- Consumes: Tasks 1–3 exactly as specified; FishNet `NetworkBehaviour`, `[ServerRpc]`, `[ObserversRpc]`, `SyncVar<T>.Value`, `Spawn(GameObject)`, `Despawn()`.
- Produces (Tasks 5–6 rely on): `PlayerCombat` public fields `CombatConfig Config; GameObject RocketPrefab; AudioSource FireAudio; AudioClip RocketClip; AudioClip RifleClip; AudioClip ShotgunClip;` public reads `float Health; int Frags; int Deaths; bool IsDead; float RespawnRemaining; string CurrentWeaponName; int CurrentWeapon;` public events `event System.Action<int> OnFired; event System.Action<int> OnWeaponChanged;`; `SpawnPointRegistry` MonoBehaviour with `public Transform[] Points;` and static `Vector3 Farthest(Vector3 from)`.

- [ ] **Step 1: Create `Assets/AlienSkies/Scripts/SpawnPointRegistry.cs`:**

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Scene-placed respawn points (wired by PlaygroundBuilder). Static
    /// access because respawns are decided by whichever PlayerCombat is serving.</summary>
    public sealed class SpawnPointRegistry : MonoBehaviour
    {
        public Transform[] Points = new Transform[0];

        static SpawnPointRegistry _instance;

        void Awake() => _instance = this;

        public static Vector3 Farthest(Vector3 from)
        {
            if (_instance == null || _instance.Points.Length == 0)
                return new Vector3(0f, 3f, 0f);
            Transform best = _instance.Points[0];
            float bestSqr = -1f;
            foreach (var t in _instance.Points)
            {
                float d = (t.position - from).sqrMagnitude;
                if (d > bestSqr) { bestSqr = d; best = t; }
            }
            return best.position;
        }
    }
}
```

- [ ] **Step 2: Create `Assets/AlienSkies/Scripts/CombatFx.cs`** (graybox tracer + explosion visuals until the Hovl pack is bought):

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Graybox combat visuals: expanding explosion sphere and fading
    /// hitscan tracer. Self-destroying, local-only (spawned from Observer RPCs).</summary>
    public sealed class CombatFx : MonoBehaviour
    {
        float _life, _maxLife;
        Material _mat;
        LineRenderer _line;
        float _maxScale;

        public static void Explosion(Vector3 at, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            var fx = go.AddComponent<CombatFx>();
            fx._maxLife = 0.35f;
            fx._maxScale = radius * 2f;
            fx._mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fx._mat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.15f) * 3f);
            go.GetComponent<Renderer>().sharedMaterial = fx._mat;
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

- [ ] **Step 3: Create `Assets/AlienSkies/Scripts/Rocket.cs`:**

```csharp
using FishNet.Object;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Combat
{
    /// <summary>Server-simulated projectile: spherecast-stepped flight, splash
    /// damage with linear falloff, knockback into MovementCore velocity (rocket
    /// jumps included via SelfDamageScale + self-knockback). Position syncs to
    /// clients via the NetworkTransform on the prefab.</summary>
    public sealed class Rocket : NetworkBehaviour
    {
        public AudioClip ExplosionClip;

        PlayerCombat _shooter;
        Vector3 _velocity;
        WeaponStats _stats;
        float _life;

        public void Init(PlayerCombat shooter, Vector3 velocity, WeaponStats stats)
        {
            _shooter = shooter;
            _velocity = velocity;
            _stats = stats;
        }

        void Update()
        {
            if (!IsServerStarted) return;
            float dt = Time.deltaTime;
            _life += dt;
            if (_life > 10f) { Explode(transform.position); return; }

            Vector3 step = _velocity * dt;
            if (Physics.SphereCast(transform.position, 0.25f, _velocity.normalized,
                    out var hit, step.magnitude + 0.05f))
            {
                // Don't detonate on the shooter's own capsule in the first moments.
                bool isShooter = hit.collider.GetComponentInParent<PlayerCombat>() == _shooter;
                if (!(isShooter && _life < 0.2f)) { Explode(hit.point); return; }
            }
            transform.position += step;
        }

        void Explode(Vector3 at)
        {
            foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            {
                Vector3 to = pc.transform.position + Vector3.up * 0.9f - at;
                float dist = to.magnitude;
                if (dist > _stats.SplashRadius) continue;
                float falloff = 1f - dist / _stats.SplashRadius;
                float dmg = _stats.Damage * falloff * (pc == _shooter ? _stats.SelfDamageScale : 1f);
                pc.ServerTakeDamage(dmg, _shooter);
                pc.GetComponent<NetworkPlayerMotor>()
                  .ApplyImpulse(to.normalized * (_stats.Knockback * falloff));
            }
            RpcExplosionFx(at);
            Despawn();
        }

        [ObserversRpc]
        void RpcExplosionFx(Vector3 at)
        {
            CombatFx.Explosion(at, _statsSplashOrDefault());
            if (ExplosionClip != null) AudioSource.PlayClipAtPoint(ExplosionClip, at, 0.9f);
        }

        // _stats is server-only state; observers use a sane visual default.
        float _statsSplashOrDefault() => _stats != null ? _stats.SplashRadius : 6f;
    }
}
```

- [ ] **Step 4: Create `Assets/AlienSkies/Scripts/PlayerCombat.cs`:**

```csharp
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;
using AlienSkies.Movement;

namespace AlienSkies.Combat
{
    /// <summary>Networked combat glue: owner input -> ServerRpc fire; server owns
    /// CombatCore (health/death/frags) and validates fire rate; SyncVars carry
    /// state to HUDs; Observer RPCs carry tracers/deaths to every client.</summary>
    [RequireComponent(typeof(NetworkPlayerMotor))]
    public sealed class PlayerCombat : NetworkBehaviour
    {
        public CombatConfig Config;
        public GameObject RocketPrefab;
        public AudioSource FireAudio;
        public AudioClip RocketClip;
        public AudioClip RifleClip;
        public AudioClip ShotgunClip;

        public event System.Action<int> OnFired;         // weapon index (owner only)
        public event System.Action<int> OnWeaponChanged; // weapon index (owner only)

        readonly SyncVar<float> _health = new SyncVar<float>();
        readonly SyncVar<int> _frags = new SyncVar<int>();
        readonly SyncVar<int> _deaths = new SyncVar<int>();
        readonly SyncVar<bool> _dead = new SyncVar<bool>();

        public float Health => _health.Value;
        public int Frags => _frags.Value;
        public int Deaths => _deaths.Value;
        public bool IsDead => _dead.Value;
        public float RespawnRemaining { get; private set; } // owner-side HUD estimate
        public int CurrentWeapon => _weapons.CurrentIndex;
        public string CurrentWeaponName => S.Weapons[_weapons.CurrentIndex].Name;

        CombatCore _core;     // server-authoritative
        WeaponCore _weapons;  // owner-side gate; server re-validates rate
        NetworkPlayerMotor _motor;
        readonly float[] _serverLastFire = new float[8];

        CombatSettings S => Config != null ? Config.Settings : _fallback;
        readonly CombatSettings _fallback = new CombatSettings();

        void Awake()
        {
            _motor = GetComponent<NetworkPlayerMotor>();
            _core = new CombatCore(S);
            _weapons = new WeaponCore(S);
            _dead.OnChange += HandleDeadChanged;
        }

        void OnDestroy() => _dead.OnChange -= HandleDeadChanged;

        void HandleDeadChanged(bool prev, bool next, bool asServer)
        {
            if (next && !asServer) RespawnRemaining = S.RespawnDelay;
        }

        public override void OnStartServer()
        {
            _health.Value = S.MaxHealth;
            _motor.KillPlaneCrossed += ServerHandleKillPlane;
        }

        public override void OnStopServer() => _motor.KillPlaneCrossed -= ServerHandleKillPlane;

        void ServerHandleKillPlane()
        {
            // Arcade rule: falling into space = instant self-respawn, -1 frag.
            _core.AddSuicide();
            _core.Respawn();
            _frags.Value = _core.Frags;
            _health.Value = _core.Health;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _weapons.Tick(dt);
            _motor.InputsEnabled = !_dead.Value;

            if (IsServerStarted && _core.IsDead && _core.TickRespawn(dt))
                ServerRespawn();

            if (!IsOwner) return;
            if (RespawnRemaining > 0f) RespawnRemaining -= dt;
            if (_dead.Value || Cursor.lockState != CursorLockMode.Locked) return;

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || kb == null) return;

            int before = _weapons.CurrentIndex;
            if (kb.digit1Key.wasPressedThisFrame) _weapons.TrySwitch(0);
            if (kb.digit2Key.wasPressedThisFrame) _weapons.TrySwitch(1);
            if (kb.digit3Key.wasPressedThisFrame) _weapons.TrySwitch(2);
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0.01f) _weapons.SwitchNext(1);
            else if (scroll < -0.01f) _weapons.SwitchNext(-1);
            if (_weapons.CurrentIndex != before) OnWeaponChanged?.Invoke(_weapons.CurrentIndex);

            if (mouse.leftButton.isPressed && _weapons.TryFire())
                FireCurrent();
        }

        void FireCurrent()
        {
            int w = _weapons.CurrentIndex;
            Vector3 origin = _motor.CameraPivot.position;
            Vector3 dir = _motor.CameraPivot.forward;

            OnFired?.Invoke(w);
            PlayFireSound(w);

            if (S.Weapons[w].ProjectileSpeed > 0f)
                CmdFireRocket(origin + dir * 1.2f, dir);
            else
                CmdFireHitscan(w, origin, dir);
        }

        void PlayFireSound(int w)
        {
            if (FireAudio == null) return;
            AudioClip clip = w == 0 ? RocketClip : w == 1 ? RifleClip : ShotgunClip;
            if (clip == null) return;
            FireAudio.pitch = w == 1 ? 1.35f : 1f;
            FireAudio.PlayOneShot(clip, 0.8f);
        }

        bool ServerFireGate(int w, WeaponStats stats)
        {
            if (Time.time - _serverLastFire[w] < stats.FireInterval * 0.9f) return false;
            _serverLastFire[w] = Time.time;
            return true;
        }

        [ServerRpc]
        void CmdFireHitscan(int weaponIndex, Vector3 origin, Vector3 dir)
        {
            if (_core.IsDead) return;
            if (weaponIndex < 0 || weaponIndex >= S.Weapons.Length) return;
            var stats = S.Weapons[weaponIndex];
            if (stats.ProjectileSpeed > 0f) return; // wrong RPC for rockets
            if (!ServerFireGate(weaponIndex, stats)) return;

            for (int p = 0; p < stats.Pellets; p++)
            {
                Vector3 pdir = ApplySpread(dir, stats.SpreadDegrees, p);
                Vector3 end;
                if (Physics.Raycast(origin, pdir, out var hit, 500f))
                {
                    end = hit.point;
                    var victim = hit.collider.GetComponentInParent<PlayerCombat>();
                    if (victim != null && victim != this)
                        victim.ServerTakeDamage(stats.Damage, this);
                }
                else end = origin + pdir * 500f;
                RpcTracer(origin + pdir * 1.0f, end);
            }
        }

        static Vector3 ApplySpread(Vector3 dir, float degrees, int pellet)
        {
            if (pellet == 0 || degrees <= 0f) return dir; // first pellet flies true
            Vector3 axis = Vector3.Cross(dir, Random.onUnitSphere).normalized;
            return Quaternion.AngleAxis(Random.Range(0f, degrees), axis) * dir;
        }

        [ServerRpc]
        void CmdFireRocket(Vector3 origin, Vector3 dir)
        {
            if (_core.IsDead || RocketPrefab == null) return;
            var stats = S.Weapons[0];
            if (!ServerFireGate(0, stats)) return;

            var go = Instantiate(RocketPrefab, origin, Quaternion.LookRotation(dir));
            go.GetComponent<Rocket>().Init(this, dir * stats.ProjectileSpeed, stats);
            Spawn(go);
        }

        [ObserversRpc]
        void RpcTracer(Vector3 from, Vector3 to) => CombatFx.Tracer(from, to);

        [ObserversRpc]
        void RpcDeathFx(Vector3 at) => CombatFx.Explosion(at, 2.5f);

        public void ServerTakeDamage(float amount, PlayerCombat attacker)
        {
            if (!IsServerStarted || _core.IsDead) return;
            bool died = _core.ApplyDamage(amount);
            _health.Value = _core.Health;
            if (!died) return;

            _dead.Value = true;
            _deaths.Value = _core.Deaths;
            if (attacker != null && attacker != this)
            {
                attacker.ServerAddFrag();
            }
            else
            {
                _core.AddSuicide(); // rocket-jumped yourself to death
                _frags.Value = _core.Frags;
            }
            RpcDeathFx(transform.position + Vector3.up * 0.9f);
        }

        public void ServerAddFrag()
        {
            _core.AddFrag();
            _frags.Value = _core.Frags;
        }

        void ServerRespawn()
        {
            _core.Respawn();
            _health.Value = _core.Health;
            _dead.Value = false;
            _motor.ServerTeleport(SpawnPointRegistry.Farthest(transform.position));
        }
    }
}
```

- [ ] **Step 5: Run suite** (compile gate — FishNet codegen must accept the RPCs) → `result=Passed total=47 passed=47 failed=0`.

- [ ] **Step 6: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/PlayerCombat.cs Assets/AlienSkies/Scripts/PlayerCombat.cs.meta Assets/AlienSkies/Scripts/Rocket.cs Assets/AlienSkies/Scripts/Rocket.cs.meta Assets/AlienSkies/Scripts/CombatFx.cs Assets/AlienSkies/Scripts/CombatFx.cs.meta Assets/AlienSkies/Scripts/SpawnPointRegistry.cs Assets/AlienSkies/Scripts/SpawnPointRegistry.cs.meta && git commit -m "feat: PlayerCombat + Rocket - server-authoritative weapons over Fish-Net

Owner fires via ServerRpc (server re-validates rate); hitscan raycasts
and rocket splash resolve on the server; knockback injects into
MovementCore velocity so rocket jumps ride prediction; SyncVars feed
HUDs; graybox tracer/explosion FX until the Hovl pack lands."
```

---

### Task 5: Viewmodels with procedural motion + HUD

**Files:**
- Create: `Assets/AlienSkies/Scripts/ViewmodelRig.cs`
- Modify: `Assets/AlienSkies/Scripts/MovementHUD.cs`

**Interfaces:**
- Consumes: `PlayerCombat` events `OnFired(int)`/`OnWeaponChanged(int)`, reads `Health/Frags/Deaths/IsDead/RespawnRemaining/CurrentWeaponName`; `NetworkPlayerMotor.Core.Velocity`.
- Produces: `ViewmodelRig` with `public NetworkPlayerMotor Motor; public PlayerCombat Combat; public GameObject[] WeaponModels;` (index-aligned with weapon order). `MovementHUD` gains `public PlayerCombat Combat;`.

- [ ] **Step 1: Create `Assets/AlienSkies/Scripts/ViewmodelRig.cs`:**

```csharp
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Combat
{
    /// <summary>Owner-only first-person weapon rig. Static models get procedural
    /// life: fire kick, speed bob, and momentum lurch driven by MovementCore
    /// velocity — the gun physically reacts to zips and grapple flings.</summary>
    public sealed class ViewmodelRig : MonoBehaviour
    {
        public NetworkPlayerMotor Motor;
        public PlayerCombat Combat;
        public GameObject[] WeaponModels = new GameObject[0];

        Vector3 _basePos;
        Vector3 _kick;
        float _bobT;

        void Awake() => _basePos = transform.localPosition;

        void OnEnable()
        {
            if (Combat == null) return;
            Combat.OnFired += HandleFired;
            Combat.OnWeaponChanged += ShowWeapon;
            ShowWeapon(Combat.CurrentWeapon);
        }

        void OnDisable()
        {
            if (Combat == null) return;
            Combat.OnFired -= HandleFired;
            Combat.OnWeaponChanged -= ShowWeapon;
        }

        void HandleFired(int weapon) =>
            _kick += new Vector3(0f, 0.02f, -(weapon == 0 ? 0.22f : weapon == 2 ? 0.16f : 0.07f));

        void ShowWeapon(int index)
        {
            for (int i = 0; i < WeaponModels.Length; i++)
                if (WeaponModels[i] != null) WeaponModels[i].SetActive(i == index);
        }

        void LateUpdate()
        {
            var v = Motor != null ? Motor.Core.Velocity : Vector3.zero;
            float speed = new Vector3(v.x, 0f, v.z).magnitude;

            _bobT += Time.deltaTime * Mathf.Lerp(2f, 15f, Mathf.Clamp01(speed / 30f));
            Vector3 bob = new Vector3(Mathf.Sin(_bobT) * 0.012f,
                                      Mathf.Abs(Mathf.Cos(_bobT)) * 0.009f, 0f)
                          * Mathf.Clamp01(speed / 8f);

            // Momentum lurch: gun drags opposite to velocity (in camera space).
            Vector3 lurch = transform.parent.InverseTransformDirection(-v) * 0.0035f;
            lurch = Vector3.ClampMagnitude(lurch, 0.1f);

            _kick = Vector3.Lerp(_kick, Vector3.zero, Time.deltaTime * 9f);
            transform.localPosition = _basePos + bob + lurch + _kick;
        }
    }
}
```

- [ ] **Step 2: Extend `MovementHUD.cs`.** Add field `public AlienSkies.Combat.PlayerCombat Combat;` next to `Motor`. At the END of `OnGUI()` (after the crosshair block) append:

```csharp
            if (Combat == null) return;
            GUI.Label(new Rect(24f, Screen.height - 60f, 300f, 40f),
                      $"HP {Combat.Health:0}", new GUIStyle(_label) { fontSize = 30 });
            GUI.Label(new Rect(Screen.width - 220f, Screen.height - 56f, 200f, 32f),
                      $"[{Combat.CurrentWeapon + 1}] {Combat.CurrentWeaponName}", _label);
            GUI.Label(new Rect(Screen.width / 2f - 100f, 24f, 200f, 32f),
                      $"FRAGS {Combat.Frags}   DEATHS {Combat.Deaths}",
                      new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });
            if (Combat.IsDead)
                GUI.Label(new Rect(Screen.width / 2f - 200f, Screen.height / 2f + 40f, 400f, 40f),
                          $"DEAD — respawn in {Mathf.Max(0f, Combat.RespawnRemaining):0.0}s",
                          new GUIStyle(_cross) { fontSize = 30 });
```

- [ ] **Step 3: Run suite** (compile gate) → 47/47.

- [ ] **Step 4: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/ViewmodelRig.cs Assets/AlienSkies/Scripts/ViewmodelRig.cs.meta Assets/AlienSkies/Scripts/MovementHUD.cs && git commit -m "feat: procedural viewmodel rig + combat HUD (health/frags/weapon/death)"
```

---

### Task 6: Builder wiring — prefabs, spawn points, config, assets

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs`
- Generated: `Assets/AlienSkies/Config/CombatConfig.asset`, `Assets/AlienSkies/Prefabs/Rocket.prefab`, rebuilt scene + player prefab

**Interfaces:** consumes everything above. Asset paths used (commit these Tirgames/sound files + their .meta in this task's commit):
- Viewmodels: `Assets/TirgamesAssets/SciFiWorld/Weapons/Prefabs/SciFiRocketLauncher01_1.prefab`, `SciFiRifle01_1.prefab`, `SciFiShotGun01_1.prefab` (+ the Models/Textures/Materials those prefabs reference — `git add Assets/TirgamesAssets` is acceptable here IF total size is reasonable; otherwise add the referenced subfolders)
- Rocket visual: `Assets/TirgamesAssets/SciFiWorld/Weapons/Prefabs/SciFiRocket01.prefab`
- Sounds: `Assets/Laser Weapons Sound Pack/Free/heavy_blast_001.wav` (rocket), `heavy_blast_004.wav` (shotgun), `continuous_beam_1.wav` (rifle), `heavy_blast_007.wav` (explosion)

- [ ] **Step 1: Add to `PlaygroundBuilder.cs`** — `using AlienSkies.Combat;` at top, `const string CombatConfigPath = "Assets/AlienSkies/Config/CombatConfig.asset";` next to ConfigPath, and in `Build()` after the MovementConfig load block:

```csharp
            var combatConfig = AssetDatabase.LoadAssetAtPath<CombatConfig>(CombatConfigPath);
            if (combatConfig == null)
            {
                combatConfig = ScriptableObject.CreateInstance<CombatConfig>();
                AssetDatabase.CreateAsset(combatConfig, CombatConfigPath);
            }
```

Pass it into BuildPlayer: change the call to `BuildPlayer(config, combatConfig);` and the signature to `static void BuildPlayer(MovementConfig config, CombatConfig combatConfig)`.

- [ ] **Step 2: Rocket prefab.** Add a method and call it inside `BuildPlayer` before the player prefab section:

```csharp
        static GameObject BuildRocketPrefab()
        {
            string path = $"{PrefabDir}/Rocket.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var rocket = new GameObject("Rocket");
            rocket.AddComponent<NetworkObject>();
            rocket.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
            var r = rocket.AddComponent<Rocket>();
            r.ExplosionClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/Laser Weapons Sound Pack/Free/heavy_blast_007.wav");

            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/TirgamesAssets/SciFiWorld/Weapons/Prefabs/SciFiRocket01.prefab");
            GameObject visual = visualPrefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            if (visualPrefab == null) Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.name = "Visual";
            visual.transform.SetParent(rocket.transform, false);
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // nose forward

            var trail = rocket.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.startWidth = 0.25f;
            trail.endWidth = 0.02f;
            var trailMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            trailMat.SetColor("_BaseColor", new Color(1f, 0.6f, 0.2f) * 3f);
            trail.material = trailMat;

            var prefab = PrefabUtility.SaveAsPrefabAsset(rocket, path);
            Object.DestroyImmediate(rocket);
            return prefab;
        }
```

- [ ] **Step 3: Player prefab additions** (inside `BuildPlayer`, after the MovementHUD wiring, before `SaveAsPrefabAsset`):

```csharp
            // --- Combat: audio, weapons rig (owner-only, under the camera), PlayerCombat
            var fireAudio = camGO.AddComponent<AudioSource>();
            fireAudio.playOnAwake = false;
            fireAudio.spatialBlend = 0f;

            var rigGO = new GameObject("WeaponRig");
            rigGO.transform.SetParent(pivot, false);
            rigGO.transform.localPosition = new Vector3(0.3f, -0.34f, 0.55f);
            rigGO.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // Tirgames guns model +Z toward player

            string[] weaponPrefabPaths =
            {
                "Assets/TirgamesAssets/SciFiWorld/Weapons/Prefabs/SciFiRocketLauncher01_1.prefab",
                "Assets/TirgamesAssets/SciFiWorld/Weapons/Prefabs/SciFiRifle01_1.prefab",
                "Assets/TirgamesAssets/SciFiWorld/Weapons/Prefabs/SciFiShotGun01_1.prefab",
            };
            var weaponModels = new GameObject[weaponPrefabPaths.Length];
            for (int w = 0; w < weaponPrefabPaths.Length; w++)
            {
                var wp = AssetDatabase.LoadAssetAtPath<GameObject>(weaponPrefabPaths[w]);
                if (wp == null) { Debug.LogError($"missing weapon prefab {weaponPrefabPaths[w]}"); continue; }
                weaponModels[w] = (GameObject)PrefabUtility.InstantiatePrefab(wp);
                weaponModels[w].transform.SetParent(rigGO.transform, false);
                weaponModels[w].transform.localScale = Vector3.one * 0.5f;
                foreach (var c in weaponModels[w].GetComponentsInChildren<Collider>())
                    Object.DestroyImmediate(c); // viewmodels must not block raycasts
                weaponModels[w].SetActive(w == 0);
            }

            var combat = player.AddComponent<PlayerCombat>();
            combat.Config = combatConfig;
            combat.RocketPrefab = BuildRocketPrefab();
            combat.FireAudio = fireAudio;
            combat.RocketClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Laser Weapons Sound Pack/Free/heavy_blast_001.wav");
            combat.RifleClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Laser Weapons Sound Pack/Free/continuous_beam_1.wav");
            combat.ShotgunClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Laser Weapons Sound Pack/Free/heavy_blast_004.wav");

            var rig = rigGO.AddComponent<ViewmodelRig>();
            rig.Motor = motor;
            rig.Combat = combat;
            rig.WeaponModels = weaponModels;

            hud.Combat = combat;
```

(Note: `BuildRocketPrefab()` must run BEFORE `SaveAsPrefabAsset` of the player — call order as written. The rocket prefab is created first because the player prefab references it.)

- [ ] **Step 4: Spawn points** (in `BuildArena`, after the rest-platform loop):

```csharp
            // Respawn points: spawn platform + the three highest rest platforms.
            var registry = new GameObject("SpawnPoints").AddComponent<SpawnPointRegistry>();
            var pts = new System.Collections.Generic.List<Transform>();
            void AddSpawn(Vector3 p)
            {
                var t = new GameObject($"Spawn_{pts.Count}").transform;
                t.SetParent(registry.transform, false);
                t.position = p;
                pts.Add(t);
            }
            AddSpawn(new Vector3(0f, 3f, 0f));
            for (int i = 4; i <= 6; i++)
            {
                float yaw = i * 1.9f;
                var p = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * (40f + i * 25f)
                        + Vector3.up * (i * 14f + 2.5f);
                AddSpawn(p);
            }
            registry.Points = pts.ToArray();
```

Also update `PlayerSpawner`: in the network bootstrap replace `spawner.Spawns = new[] { spawnPoint };` (and delete the single spawnPoint block) with:

```csharp
            spawner.Spawns = Object.FindFirstObjectByType<SpawnPointRegistry>().Points;
```

(`BuildArena()` runs before `BuildPlayer(...)` in `Build()` — verify the call order in `Build()` and move `BuildArena();` above `BuildPlayer(...);` if needed.)

- [ ] **Step 5: Rebuild scene headlessly** (editor closed):

```bash
cd "/mnt/c/Users/computer/My project" && timeout -k 30 570 "/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\Users\computer\My project" -executeMethod AlienSkies.EditorTools.PlaygroundBuilder.Build -logFile - 2>&1 | grep -iE "error CS|missing weapon|PlaygroundBuilder: saved" | head -5
```

Expected: `PlaygroundBuilder: saved ...`, no "missing weapon".

- [ ] **Step 6: Run suite** → 47/47. Screenshot pass: run SceneScreenshot.Capture and view shots (arena unchanged; player prefab isn't in scene so viewmodels verify in playtest).

- [ ] **Step 7: Commit** (including the used Tirgames + sound assets):

```bash
cd "/mnt/c/Users/computer/My project" && du -sh Assets/TirgamesAssets && find Assets/TirgamesAssets -size +90M -type f
# If under ~300MB and no >90MB files: add the whole pack (it's a dependency now, private repo):
git add Assets/TirgamesAssets Assets/TirgamesAssets.meta "Assets/Laser Weapons Sound Pack" "Assets/Laser Weapons Sound Pack.meta" && git add Assets/AlienSkies/Editor/PlaygroundBuilder.cs Assets/AlienSkies/Config/CombatConfig.asset Assets/AlienSkies/Config/CombatConfig.asset.meta Assets/AlienSkies/Prefabs Assets/AlienSkies/Scenes/MovementPlayground.unity Assets/DefaultPrefabObjects.asset && git commit -m "feat: combat wired into scene - viewmodels, rocket prefab, spawn points, config

Vendors the Tirgames SciFiWorld weapons + free laser sound pack (used
by viewmodels/rocket/audio; private repo, license OK)."
```

---

### Task 7: Docs, push, PR, duel playtest handoff

**Files:**
- Modify: `CLAUDE.md` (RESUME HERE + diary)

- [ ] **Step 1: Update CLAUDE.md.** RESUME HERE:

```markdown
> **▶ RESUME HERE (2026-07-04):** COMBAT IS IN (PR #7): LMB fire, 1/2/3 + scroll switch (Rocket/Rifle/Shotgun), 100 HP, 3s respawn at farthest point, frags on HUD, rocket jumps work. Duel: Unity Play → HOST + `Builds/AlienSkies.exe` → JOIN 127.0.0.1 (rebuild exe: `bash tools/build_player.sh`). F grapple, RMB-while-grappling zip. Next: duel verification, then sky pass 2 (black hole/star/belts) or Hovl VFX upgrade.
```

Diary entry:

```markdown
- **2026-07-04** — PR #7 combat: CombatCore + WeaponCore (pure C#, 14 tests, 49 total), PlayerCombat ServerRpc/SyncVar layer, server-simulated Rocket with splash+knockback into MovementCore (rocket jumps via prediction), Tirgames viewmodels w/ procedural kick/bob/momentum-lurch, laser SFX, graybox tracers/explosions, spawn point registry. Arcade KillY rule: instant respawn, −1 frag.
```

- [ ] **Step 2: Commit docs, push, open PR:**

```bash
cd "/mnt/c/Users/computer/My project" && git add CLAUDE.md docs/superpowers/plans/2026-07-04-combat-core.md && git commit -m "docs: RESUME HERE + diary for combat core" && git push -u origin feature/combat-core && gh pr create --title "Combat core: rockets, rifle, shotgun, dying, frags - it's a game now" --body "$(cat <<'EOF'
GitHub PR #7 = the spec's combat milestone (docs/superpowers/specs/2026-07-04-first-blood-design.md).

- CombatCore + WeaponCore: pure C#, 14 new tests (49 total)
- PlayerCombat: owner input -> ServerRpc, server-authoritative damage/death/respawn/frags, SyncVars to HUD
- Rocket: server-simulated projectile, 6m splash w/ falloff, knockback injected into MovementCore velocity - rocket jumps work through prediction (self-damage x0.5)
- Rifle hitscan 20dmg/0.35s; Shotgun 10x6dmg 7-degree cone (first pellet flies true)
- Tirgames sci-fi viewmodels with procedural fire kick, speed bob, momentum lurch (gun reacts to zips/flings)
- Free laser-pack fire/explosion sounds; graybox tracer + explosion FX (Hovl upgrade later)
- 4 spawn points, respawn at farthest from death; KillY = instant respawn at -1 frag (arcade rule)
- LMB fire, 1/2/3 + wheel switch; controls doc updated

Playtest: HOST + exe JOIN, shoot each other. Checkpoints: rocket-jump feel, shotgun spread fairness, can you shoot while grappling (yes - LMB is free), F+RMB+LMB hand ergonomics (spec checkpoint).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 3: Self-review checklist:** `git status --short` (only known dirt), zero unpushed commits, `gh pr view 7 --json state` OPEN, rebuild exe (`bash tools/build_player.sh`) so the duel binary includes combat.

- [ ] **Step 4: Hand off.** Tell the user: Unity Play → HOST, exe → JOIN. Verify: shoot each other with all three weapons, die, respawn across the map, frag counter moves, rocket-jump off the spawn platform, gun lurches during a zip. Report the spec's ergonomics checkpoint (F grapple + RMB zip + LMB fire simultaneously).
