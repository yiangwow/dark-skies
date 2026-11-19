# Phase 1: De-network Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove FishNet and all networking from Alien Skies so the game boots directly into the arena as a single-player game with a B-key practice bot, with all 85 tests green and a working exe.

**Architecture:** The prediction pipeline ([Replicate]/[Reconcile]/tick smoothing) exists to hide network latency; single-player has none, so input and simulation collapse into the same Update frame. All pure-C# cores are UNTOUCHED. The glue layer is rewritten: `NetworkPlayerMotor` → `PlayerMotor` (plain MonoBehaviour, per-frame sim), SyncVars → plain fields with direct calls where OnChange handlers fired, RPCs → method calls, `IsOwner` → "is the human player" (`ExternalInput == null`). The Graphical child stays (ViewmodelRig/rope/body hang off it) but never detaches — per-frame sim is inherently smooth.

**Tech Stack:** Unity 6000.5.1f1, URP, Input System. Headless verification from WSL: `bash tools/run_tests.sh` (compile + 85 EditMode tests), `Unity -executeMethod AlienSkies.EditorTools.PlaygroundBuilder.BuildNeonVector`, `bash tools/build_player.sh`.

**Spec:** `docs/superpowers/specs/2026-08-28-singleplayer-pivot-design.md` (Phase 1 section)

## Global Constraints

- Unity Editor must be CLOSED for every headless command. Check first: `tasklist.exe | grep -i unity` from WSL.
- Repo root: `/mnt/c/Users/computer/My project` (quote the space). Branch: `feature/singleplayer-pivot`.
- Git author the owner's git identity; NO Co-Authored-By or Claude attribution lines.
- Always commit `.meta` files with their assets; `git mv` C# files WITH their `.meta` to preserve GUIDs.
- Never hand-edit `Assets/AlienSkies/Scenes/MovementPlayground.unity` — it is generated; change PlaygroundBuilder and rebuild.
- Pure cores (`MovementCore`, `CombatCore`, `WeaponCore`, `FeedbackCore`, `MusicCore`, `BotBrain`, `MatchCore`), both config/settings classes, and the 13 pure test files must not change.
- Every task ends with `bash tools/run_tests.sh` passing (85 tests) — this is also the compile gate — then a commit.
- run_tests.sh writes `TestResults/editmode.xml` (results) and `TestResults/editmode.log` (compile log) — on failure read those two (grep the log for `error CS`).

---

### Task 1: De-network MatchManager (+ MovementHUD winner check)

**Files:**
- Modify: `Assets/AlienSkies/Scripts/MatchManager.cs` (full rewrite below)
- Modify: `Assets/AlienSkies/Scripts/MovementHUD.cs` (~line 120 winner check)

**Interfaces:**
- Consumes: `MatchCore` (unchanged): `ReportScore(int frags)`, `TickReset(float dt)`, `StartNewRound()`, `IsOver`, `ResetRemaining`.
- Produces: `MatchManager.Instance`, `bool IsRoundOver`, `PlayerCombat Winner` (replaces `int WinnerClientId`), `float ResetRemaining` — consumed by MovementHUD and PlayerCombat (which only reads `IsRoundOver`, unchanged).

- [ ] **Step 1: Rewrite MatchManager.cs** — replace the entire file with:

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Scene singleton: watches frag counts, ends the round at the
    /// target, freezes play via IsRoundOver (players poll it), and resets every
    /// PlayerCombat after the delay. Interim for Phase 1 — replaced by
    /// RunManager in Phase 3 (see the pivot spec).</summary>
    public sealed class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        public CombatConfig Config;

        public bool IsRoundOver { get; private set; }
        public PlayerCombat Winner { get; private set; }
        public float ResetRemaining { get; private set; }

        MatchCore _core;

        CombatSettings S => Config != null ? Config.Settings : _fallback;
        readonly CombatSettings _fallback = new CombatSettings();

        void Awake()
        {
            Instance = this;
            _core = new MatchCore(S);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (ResetRemaining > 0f) ResetRemaining -= dt;

            if (!_core.IsOver)
            {
                foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
                {
                    if (!_core.ReportScore(pc.Frags)) continue;
                    Winner = pc;
                    IsRoundOver = true;
                    ResetRemaining = S.RoundResetDelay;
                    break;
                }
            }
            else if (_core.TickReset(dt))
            {
                foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
                    pc.ServerResetForNewRound();
                _core.StartNewRound();
                IsRoundOver = false;
                Winner = null;
            }
        }
    }
}
```

- [ ] **Step 2: Fix the HUD winner check** — in `MovementHUD.cs`, find the round-over block (~line 120):

```csharp
bool won = match.WinnerClientId == Combat.OwnerId;
```

replace with:

```csharp
bool won = match.Winner == Combat;
```

- [ ] **Step 3: Compile + tests.** Run: `bash tools/run_tests.sh`. Expected: PASS, 85 tests (MatchCoreTests still cover round logic; PlayerCombat still compiles because it only reads `MatchManager.Instance.IsRoundOver`).

- [ ] **Step 4: Commit**

```bash
git add Assets/AlienSkies/Scripts/MatchManager.cs Assets/AlienSkies/Scripts/MovementHUD.cs
git commit -m "refactor: de-network MatchManager - SyncVars to plain fields, Winner replaces WinnerClientId"
```

---

### Task 2: De-network PlayerCombat and Rocket

**Files:**
- Modify: `Assets/AlienSkies/Scripts/PlayerCombat.cs` (full rewrite below)
- Modify: `Assets/AlienSkies/Scripts/Rocket.cs` (full rewrite below)
- Modify: `Assets/AlienSkies/Scripts/BotDriver.cs` (one call site, Step 3)
- Modify: `Assets/AlienSkies/Scripts/MusicManager.cs` (one line, Step 3 — `IsOwner` dies with NetworkBehaviour in THIS task, so its caller changes in THIS task)

**Interfaces:**
- Consumes (unchanged, still on the networked motor this task): `NetworkPlayerMotor` members `CameraPivot`, `InputsEnabled`, `KillPlaneCrossed`, `ApplyImpulse(Vector3)`, `ServerTeleport(Vector3)`. Task 3 renames the type to `PlayerMotor` and `ServerTeleport`→`RespawnAt`; this task keeps the old names so each commit compiles.
- Produces (consumed by BotDriver in Task 3, MusicManager, CombatFeedback, MovementHUD, Rocket, MatchManager):
  - `public bool IsLocal` — true for the human player (motor has no ExternalInput)
  - `public void FireRocket(Vector3 origin, Vector3 dir)` — replaces `ServerFireRocket`
  - `public void TakeDamage(float amount, PlayerCombat attacker)` — replaces `ServerTakeDamage`
  - `public void NotifyHit(float damage, bool killed)` — replaces `ServerNotifyHit`
  - `public void AddFrag()` — replaces `ServerAddFrag`
  - `ServerResetForNewRound()` keeps its name this task (MatchManager already calls it); renamed `ResetForNewRound` in Task 3 only if free — otherwise keep permanently, it's cosmetic.
  - Unchanged: `Health`, `Frags`, `Deaths`, `IsDead`, `RespawnRemaining`, `CurrentWeapon`, `CurrentWeaponName`, `FragTarget`, all four events, serialized fields.
- `Rocket.Init(PlayerCombat, Vector3, WeaponStats)` unchanged.

- [ ] **Step 1: Rewrite PlayerCombat.cs** — replace the entire file with:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;
using AlienSkies.Movement;

namespace AlienSkies.Combat
{
    /// <summary>Combat glue: local input fires directly; CombatCore owns
    /// health/death/frags. What used to be SyncVar OnChange handlers are now
    /// direct calls at the mutation sites; what used to be RPCs are methods.</summary>
    [RequireComponent(typeof(NetworkPlayerMotor))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        public CombatConfig Config;
        public GameObject RocketPrefab;
        public GameObject ExplosionFxPrefab; // CombatFx falls back to graybox if null
        public GameObject ImpactFxPrefab;
        public AudioSource FireAudio;
        public AudioClip RocketClip;
        public AudioClip RifleClip;
        public AudioClip ShotgunClip;

        public event System.Action<int> OnFired;         // weapon index (local player)
        public event System.Action<int> OnWeaponChanged; // weapon index (local player)
        public event System.Action<float, bool> OnHitConfirmed; // damage, killed (attacker)
        public event System.Action<float> OnDamaged;            // damage fraction (victim)

        public float Health => _core.Health;
        public int Frags => _core.Frags;
        public int Deaths => _core.Deaths;
        public bool IsDead => _core.IsDead;
        public float RespawnRemaining { get; private set; }
        public int CurrentWeapon => _weapons.CurrentIndex;
        public string CurrentWeaponName => S.Weapons[_weapons.CurrentIndex].Name;
        public int FragTarget => S.FragTarget;

        /// <summary>True for the human player: their motor reads devices.
        /// Bots get an ExternalInput before their first frame.</summary>
        public bool IsLocal => _motor.ExternalInput == null;

        CombatCore _core;
        WeaponCore _weapons;
        NetworkPlayerMotor _motor;
        readonly float[] _lastFire = new float[8];

        CombatSettings S => Config != null ? Config.Settings : _fallback;
        readonly CombatSettings _fallback = new CombatSettings();

        void Awake()
        {
            if (ExplosionFxPrefab != null) CombatFx.ExplosionPrefab = ExplosionFxPrefab;
            if (ImpactFxPrefab != null) CombatFx.ImpactPrefab = ImpactFxPrefab;
            _motor = GetComponent<NetworkPlayerMotor>();
            _core = new CombatCore(S);
            _weapons = new WeaponCore(S);
            _motor.KillPlaneCrossed += HandleKillPlane;
        }

        void OnDestroy() => _motor.KillPlaneCrossed -= HandleKillPlane;

        void HandleKillPlane()
        {
            // A dead body falling into space must NOT reset the core: doing so
            // clears _core.IsDead and the respawn timer branch never fires
            // (permanent death). The motor has already parked the corpse at the
            // spawn point; let the timer finish.
            if (_core.IsDead) return;

            // Arcade rule: falling into space while alive = instant
            // self-respawn, -1 frag.
            _core.AddSuicide();
            _core.Respawn();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _weapons.Tick(dt);
            bool roundOver = MatchManager.Instance != null && MatchManager.Instance.IsRoundOver;
            _motor.InputsEnabled = !_core.IsDead && !roundOver;

            if (_core.IsDead && _core.TickRespawn(dt))
                Respawn();

            if (!IsLocal) return;
            if (RespawnRemaining > 0f) RespawnRemaining -= dt;
            if (_core.IsDead || roundOver || Cursor.lockState != CursorLockMode.Locked) return;

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
                FireRocket(origin + dir * 1.2f, dir);
            else
                FireHitscan(w, origin, dir);
        }

        void PlayFireSound(int w)
        {
            if (FireAudio == null) return;
            AudioClip clip = w == 0 ? RocketClip : w == 1 ? RifleClip : ShotgunClip;
            if (clip == null) return;
            FireAudio.pitch = w == 1 ? 1.35f : 1f;
            FireAudio.PlayOneShot(clip, 0.8f);
        }

        bool FireGate(int w, WeaponStats stats)
        {
            if (Time.time - _lastFire[w] < stats.FireInterval * 0.9f) return false;
            _lastFire[w] = Time.time;
            return true;
        }

        void FireHitscan(int weaponIndex, Vector3 origin, Vector3 dir)
        {
            if (_core.IsDead) return;
            if (weaponIndex < 0 || weaponIndex >= S.Weapons.Length) return;
            var stats = S.Weapons[weaponIndex];
            if (stats.ProjectileSpeed > 0f) return; // rockets use FireRocket
            if (!FireGate(weaponIndex, stats)) return;

            for (int p = 0; p < stats.Pellets; p++)
            {
                Vector3 pdir = ApplySpread(dir, stats.SpreadDegrees, p);
                Vector3 end;
                Vector3 surfaceNormal = Vector3.up;
                bool hitSomething = RaycastIgnoringSelf(origin, pdir, out var hit);
                if (hitSomething)
                {
                    // Overlapping-at-start spherecast hits report point (0,0,0).
                    end = hit.distance > 0f ? hit.point : origin;
                    surfaceNormal = hit.normal;
                    var victim = hit.collider.GetComponentInParent<PlayerCombat>();
                    if (victim != null && victim != this)
                        victim.TakeDamage(stats.Damage, this);
                }
                else end = origin + pdir * 500f;
                CombatFx.Tracer(origin + pdir * 1.0f, end);
                if (hitSomething) CombatFx.Impact(end, surfaceNormal);
            }
        }

        // Spherecast past our own colliders: every shot is HitscanRadius thick,
        // fattening targets for bullets without touching anyone's movement capsule.
        bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = default;
            float bestDist = float.MaxValue;
            foreach (var h in Physics.SphereCastAll(origin, S.HitscanRadius, dir, 500f))
            {
                if (h.collider.GetComponentInParent<PlayerCombat>() == this) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; }
            }
            return bestDist < float.MaxValue;
        }

        static Vector3 ApplySpread(Vector3 dir, float degrees, int pellet)
        {
            if (pellet == 0 || degrees <= 0f) return dir; // first pellet flies true
            Vector3 axis = Vector3.Cross(dir, Random.onUnitSphere).normalized;
            return Quaternion.AngleAxis(Random.Range(0f, degrees), axis) * dir;
        }

        /// <summary>The one validated rocket-fire path — shared by the local
        /// player and AI drivers (BotDriver).</summary>
        public void FireRocket(Vector3 origin, Vector3 dir)
        {
            if (_core.IsDead || RocketPrefab == null) return;
            var stats = S.Weapons[0];
            if (!FireGate(0, stats)) return;

            var go = Instantiate(RocketPrefab, origin, Quaternion.LookRotation(dir));
            go.GetComponent<Rocket>().Init(this, dir * stats.ProjectileSpeed, stats);
        }

        public void TakeDamage(float amount, PlayerCombat attacker)
        {
            if (_core.IsDead) return;
            float prev = _core.Health;
            bool died = _core.ApplyDamage(amount);
            // Victim hurt feedback — was the health SyncVar's OnChange.
            if (_core.Health < prev)
                OnDamaged?.Invoke((prev - _core.Health) / S.MaxHealth);
            if (attacker != null && attacker != this)
                attacker.NotifyHit(amount, died);
            if (!died) return;

            RespawnRemaining = S.RespawnDelay; // was the dead SyncVar's OnChange
            if (attacker != null && attacker != this)
                attacker.AddFrag();
            else
                _core.AddSuicide(); // rocket-jumped yourself to death
            CombatFx.Explosion(transform.position + Vector3.up * 0.9f, 2.5f);
        }

        // Only the human gets attacker-side hit feedback — was the
        // Owner.IsValid gate ("AI players have no client"). Without it, bot
        // hit-confirms drive CombatFeedback into the bot's deactivated camera:
        // PlayOneShot warnings every hit, impact frames on every bot kill.
        public void NotifyHit(float damage, bool killed)
        {
            if (IsLocal) OnHitConfirmed?.Invoke(damage, killed);
        }

        public void AddFrag() => _core.AddFrag();

        /// <summary>Full reset for a new round: fresh CombatCore (zeroes
        /// frags/deaths/health), revive, respawn spread across the map.</summary>
        public void ServerResetForNewRound()
        {
            _core = new CombatCore(S);
            _motor.ServerTeleport(SpawnPointRegistry.Farthest(transform.position));
        }

        void Respawn()
        {
            _core.Respawn();
            _motor.ServerTeleport(SpawnPointRegistry.Farthest(transform.position));
        }
    }
}
```

Note the deliberate simplifications: `Health/Frags/Deaths/IsDead` now read `_core` directly (the SyncVar mirrors existed only to replicate `_core` to clients), and the tracer/impact/death FX spawn inline where the ObserversRpcs fired.

- [ ] **Step 2: Rewrite Rocket.cs** — replace the entire file with:

```csharp
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Combat
{
    /// <summary>Projectile: spherecast-stepped flight, splash damage with
    /// linear falloff, knockback into MovementCore velocity (rocket jumps
    /// included via SelfDamageScale + self-knockback).</summary>
    public sealed class Rocket : MonoBehaviour
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
            float dt = Time.deltaTime;
            _life += dt;
            if (_life > 10f) { Explode(transform.position, Vector3.up); return; }

            Vector3 step = _velocity * dt;
            // 0.45 radius: generous direct-hit proximity — rockets should feel
            // like they clip people they nearly touch.
            if (Physics.SphereCast(transform.position, 0.45f, _velocity.normalized,
                    out var hit, step.magnitude + 0.05f))
            {
                // Don't detonate on the shooter's own capsule in the first moments.
                bool isShooter = hit.collider.GetComponentInParent<PlayerCombat>() == _shooter;
                if (!(isShooter && _life < 0.2f)) { Explode(hit.point, hit.normal); return; }
            }
            transform.position += step;
        }

        void Explode(Vector3 at, Vector3 surfaceNormal)
        {
            foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            {
                Vector3 to = pc.transform.position + Vector3.up * 0.9f - at;
                float dist = to.magnitude;
                if (dist > _stats.SplashRadius) continue;
                float falloff = 1f - dist / _stats.SplashRadius;
                float dmg = _stats.Damage * falloff * (pc == _shooter ? _stats.SelfDamageScale : 1f);
                pc.TakeDamage(dmg, _shooter);
                pc.GetComponent<NetworkPlayerMotor>()
                  .ApplyImpulse(to.normalized * (_stats.Knockback * falloff));
            }
            CombatFx.Explosion(at, surfaceNormal, SplashOrDefault());
            if (ExplosionClip != null) AudioSource.PlayClipAtPoint(ExplosionClip, at, 0.9f);
            Destroy(gameObject);
        }

        float SplashOrDefault() => _stats != null ? _stats.SplashRadius : 6f;
    }
}
```

- [ ] **Step 3: Fix the two callers that used FishNet-inherited members** (they break the compile THIS task, not next):
  - `Assets/AlienSkies/Scripts/BotDriver.cs` line 57: `_combat.ServerFireRocket(` → `_combat.FireRocket(`
  - `Assets/AlienSkies/Scripts/MusicManager.cs` line 105: `pc.IsOwner` → `pc.IsLocal`

- [ ] **Step 3b: Compile + tests.** Run: `bash tools/run_tests.sh`. Expected: PASS, 85 tests.

- [ ] **Step 4: Commit**

```bash
git add Assets/AlienSkies/Scripts/PlayerCombat.cs Assets/AlienSkies/Scripts/Rocket.cs Assets/AlienSkies/Scripts/BotDriver.cs Assets/AlienSkies/Scripts/MusicManager.cs
git commit -m "refactor: de-network PlayerCombat and Rocket - direct calls replace RPCs and SyncVars"
```

---

### Task 3: NetworkPlayerMotor → PlayerMotor (the core surgery)

**Files:**
- Rename: `git mv Assets/AlienSkies/Scripts/NetworkPlayerMotor.cs Assets/AlienSkies/Scripts/PlayerMotor.cs` (and `git mv` its `.meta` alongside — preserves the script GUID)
- Modify: `Assets/AlienSkies/Scripts/PlayerMotor.cs` (full rewrite below)
- Modify: `Assets/AlienSkies/Scripts/BotDriver.cs` (full rewrite below)
- Modify: `Assets/AlienSkies/Scripts/BotSpawner.cs` (full rewrite below)
- Modify: `Assets/AlienSkies/Scripts/ViewmodelRig.cs` (type rename only)
- Modify: `Assets/AlienSkies/Scripts/MovementHUD.cs` (type rename only)
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` (type rename ONLY — line 210 `AddComponent<NetworkPlayerMotor>()` → `AddComponent<PlayerMotor>()`; it is in the Editor asmdef which references Runtime, so the rename breaks its compile THIS task. All other builder changes stay in Task 4.)
- Modify: `Assets/AlienSkies/Scripts/PlayerCombat.cs` (type rename + `ServerTeleport` → `RespawnAt`)
- Modify: `Assets/AlienSkies/Scripts/Rocket.cs` (type rename)
- Modify: `Assets/AlienSkies/Tests/HitscanGeometryTests.cs` (type rename; prefab path stays `NetworkPlayer.prefab` until Task 4 renames the prefab)

**Interfaces:**
- Produces (consumed by everything above):
  - `public sealed class PlayerMotor : MonoBehaviour` in `AlienSkies.Movement`
  - `public struct PlayerMotor.OneTimeInput` — fields unchanged: `JumpPressed`, `GrappleStarted`, `GrappleAnchor`, `GrappleStopped`, `ZipPressed`, `ResetState()`
  - `public struct PlayerMotor.InputFrame` — replaces `ReplicateData`: `Vector3 WishDir; float Yaw; OneTimeInput OneTime;` ctor `InputFrame(Vector3 wishDir, float yaw, OneTimeInput oneTime)`
  - `public System.Func<InputFrame> ExternalInput` — bot input hook, called once per frame inside the motor's Update
  - `public void RespawnAt(Vector3 position)` — replaces `ServerTeleport` (teleport + `Core.Reset()`)
  - Unchanged: `Config`, `CameraPivot`, `Graphical`, `BodyRenderer`, `InputsEnabled`, `KillPlaneCrossed`, `Core`, `HasGrappleTarget`, `ApplyImpulse(Vector3)`
- Consumes: `MovementCore` unchanged; `GrappleAimer.TryFindAnchor` unchanged; `PlanetSurface` marker unchanged.

- [ ] **Step 1: git mv the file and its meta**

```bash
cd "/mnt/c/Users/computer/My project"
git mv Assets/AlienSkies/Scripts/NetworkPlayerMotor.cs Assets/AlienSkies/Scripts/PlayerMotor.cs
git mv Assets/AlienSkies/Scripts/NetworkPlayerMotor.cs.meta Assets/AlienSkies/Scripts/PlayerMotor.cs.meta
```

- [ ] **Step 2: Rewrite PlayerMotor.cs** — replace the entire file with:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlienSkies.Movement
{
    /// <summary>The player motor. Input is read and simulated in the same
    /// Update frame with MovementCore — one code path for the human (device
    /// input) and bots (ExternalInput). De-networked descendant of the
    /// prediction-based NetworkPlayerMotor; the Graphical child remains (rig,
    /// rope, body hang off it) but no longer detaches — per-frame simulation
    /// needs no tick smoothing.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        public struct OneTimeInput
        {
            public bool JumpPressed;
            public bool GrappleStarted;
            public Vector3 GrappleAnchor;
            public bool GrappleStopped;
            public bool ZipPressed;

            public void ResetState()
            {
                JumpPressed = false;
                GrappleStarted = false;
                GrappleAnchor = default;
                GrappleStopped = false;
                ZipPressed = false;
            }
        }

        public struct InputFrame
        {
            public Vector3 WishDir; // world-space
            public float Yaw;       // body yaw in degrees
            public OneTimeInput OneTime;

            public InputFrame(Vector3 wishDir, float yaw, OneTimeInput oneTime)
            {
                WishDir = wishDir;
                Yaw = yaw;
                OneTime = oneTime;
            }
        }

        static readonly Vector3 SpawnPoint = new Vector3(0f, 3f, 0f);

        public MovementConfig Config;
        public Transform CameraPivot;
        public Transform Graphical;     // visual child; owns visual yaw
        public Renderer BodyRenderer;
        public bool InputsEnabled = true;   // PlayerCombat gates this while dead
        public event System.Action KillPlaneCrossed; // once per fall
        [SerializeField] float _mouseSensitivity = 0.08f;

        public MovementCore Core { get; private set; }
        public bool HasGrappleTarget { get; private set; }

        /// <summary>Input source for AI-driven players (practice bot). When
        /// set, the frame's input comes from here instead of devices — and this
        /// motor is not the human player (see IsHuman).</summary>
        public System.Func<InputFrame> ExternalInput;

        public bool IsHuman => ExternalInput == null;

        CharacterController _cc;
        LineRenderer _rope;
        float _pitch;
        OneTimeInput _oneTime;
        bool _initialized;

        MovementSettings S => Config != null ? Config.Settings : _fallback;
        readonly MovementSettings _fallback = new MovementSettings();

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            Core = new MovementCore(S);
            BuildRope();
        }

        void BuildRope()
        {
            _rope = gameObject.AddComponent<LineRenderer>();
            _rope.positionCount = 2;
            _rope.startWidth = 0.08f;
            _rope.endWidth = 0.03f;
            // HDR unlit so bloom makes the rope read as an energy line
            var ropeMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ropeMat.SetColor("_BaseColor", new Color(0.29f, 1f, 0.69f) * 2.6f);
            _rope.material = ropeMat;
            _rope.enabled = false;
        }

        // Human-vs-bot is decided by ExternalInput, which BotDriver assigns in
        // its Awake — after this component's Awake (AddComponent order in
        // BotSpawner) but before the first Update. So one-time setup runs
        // lazily on the first frame, not in Start (same-frame race).
        void Initialize()
        {
            _initialized = true;
            CameraPivot.gameObject.SetActive(IsHuman);
            if (IsHuman)
            {
                // Exactly one AudioListener: ours.
                var mine = CameraPivot.GetComponentInChildren<AudioListener>(true);
                foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (l != mine) l.enabled = false;
                Cursor.lockState = CursorLockMode.Locked;
                // The human shouldn't see the inside of their own body capsule.
                if (BodyRenderer != null) BodyRenderer.enabled = false;
            }
            var hud = GetComponent<MovementHUD>();
            if (hud != null) hud.enabled = IsHuman;
        }

        void Update()
        {
            if (!_initialized) Initialize();

            ReadDeviceInput();
            Simulate(BuildInput(), Time.deltaTime);

            // Rope renders from simulation state; anchor at the visual body.
            _rope.enabled = Core.IsGrappling;
            if (Core.IsGrappling)
            {
                var visualPos = Graphical != null ? Graphical.position : transform.position;
                _rope.SetPosition(0, visualPos + Vector3.down * 0.3f);
                _rope.SetPosition(1, Core.GrappleAnchor);
            }
        }

        void ReadDeviceInput()
        {
            if (!IsHuman) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;

            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;

            if (cursorLocked)
            {
                // Yaw rotates the Graphical; the sim root follows in Simulate
                // (kept split so bots can drive yaw through InputFrame too).
                Vector2 look = mouse.delta.ReadValue() * _mouseSensitivity;
                Graphical.Rotate(0f, look.x, 0f);
                _pitch = Mathf.Clamp(_pitch - look.y, -89f, 89f);
                CameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }

            // One-time inputs, consumed in BuildInput. Gated while dead —
            // corpses can look around but not act.
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
        }

        InputFrame BuildInput()
        {
            if (ExternalInput != null)
                return InputsEnabled ? ExternalInput() : default;
            if (!InputsEnabled) return new InputFrame(Vector3.zero, Graphical.eulerAngles.y, default);

            var kb = Keyboard.current;
            Vector3 wish = Vector3.zero;
            if (kb != null && Cursor.lockState == CursorLockMode.Locked)
            {
                float f = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                float r = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                wish = Graphical.forward * f + Graphical.right * r; // aim frame = visual frame
                wish.y = 0f;
                if (wish.sqrMagnitude > 1e-4f) wish.Normalize();
            }

            var frame = new InputFrame(wish, Graphical.eulerAngles.y, _oneTime);
            _oneTime.ResetState();
            return frame;
        }

        void Simulate(InputFrame frame, float delta)
        {
            // Sim root follows the input yaw (human: from Graphical; bot: from
            // BotBrain intent). The Graphical is then re-aligned to the root
            // EVERY frame, for humans too: it is an attached child now (the
            // old code detached it via the tick smoother), so leaving its
            // accumulated local yaw in place would compound with the root's
            // new world yaw next frame — a positive feedback loop that spins
            // the camera forever after one mouse movement. Re-aligning zeroes
            // the local yaw each frame; pitch is untouched (it lives on
            // CameraPivot).
            transform.rotation = Quaternion.Euler(0f, frame.Yaw, 0f);
            if (Graphical != null)
                Graphical.rotation = transform.rotation;

            if (frame.OneTime.GrappleStarted) Core.StartGrapple(frame.OneTime.GrappleAnchor);
            if (frame.OneTime.GrappleStopped) Core.StopGrapple();
            if (frame.OneTime.ZipPressed) Core.StartZip();

            var input = new MoveInput { WishDir = frame.WishDir, JumpPressed = frame.OneTime.JumpPressed };
            Core.Tick(input, transform.position, _cc.isGrounded, delta);
            _cc.Move(Core.Velocity * delta);

            if (transform.position.y < S.KillY)
            {
                KillPlaneCrossed?.Invoke();
                Teleport(SpawnPoint);
            }
        }

        /// <summary>Inject external velocity (rocket knockback).</summary>
        public void ApplyImpulse(Vector3 impulse) => Core.SetVelocity(Core.Velocity + impulse);

        /// <summary>Respawn teleport: move and reset all movement state.</summary>
        public void RespawnAt(Vector3 position)
        {
            Teleport(position);
            Core.Reset();
        }

        void Teleport(Vector3 position)
        {
            // CharacterController must be disabled around direct position writes or
            // its internal physics state stays at the old position until next simulate.
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
            if (position == SpawnPoint) Core.Reset();
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Core.ClampAgainstSurface(hit.normal);
            // Planets arm the jump-key kick (vertical recovery); the marker keeps
            // platforms out of it.
            if (hit.collider.GetComponent<PlanetSurface>() != null)
                Core.RegisterSurfaceContact(hit.normal);
        }
    }
}
```

- [ ] **Step 3: Rewrite BotDriver.cs** — replace the entire file with:

```csharp
using UnityEngine;
using AlienSkies.Movement;
using AlienSkies.Combat;

namespace AlienSkies.Bots
{
    /// <summary>Bot driver on a spawned player prefab instance. A plain
    /// MonoBehaviour added by BotSpawner right after Instantiate; input is
    /// produced inside the motor's Update via ExternalInput, so bots and the
    /// human run the exact same simulation path.</summary>
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class BotDriver : MonoBehaviour
    {
        PlayerMotor _motor;
        PlayerCombat _combat;
        CharacterController _cc;
        BotBrain _brain;
        PlayerCombat _target;
        float _retargetTimer;
        static int _nextSeed = 1337; // distinct behavior per spawned bot

        void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _combat = GetComponent<PlayerCombat>();
            _cc = GetComponent<CharacterController>();
            _brain = new BotBrain(_nextSeed++) { Stationary = true };
            _motor.ExternalInput = BuildInput;
        }

        PlayerMotor.InputFrame BuildInput()
        {
            float dt = Time.deltaTime;
            RefreshTarget(dt);

            var sense = new BotBrain.Sense
            {
                SelfPos = _motor.CameraPivot.position,
                HasTarget = _target != null,
                Grounded = _cc.isGrounded,
            };
            if (_target != null)
            {
                var targetMotor = _target.GetComponent<PlayerMotor>();
                sense.TargetPos = targetMotor.CameraPivot.position;
                // Eye-to-eye: rays starting inside our own capsule don't hit it.
                sense.TargetVisible =
                    !Physics.Linecast(sense.SelfPos, sense.TargetPos, out var hit)
                    || hit.collider.GetComponentInParent<PlayerCombat>() == _target;
            }

            var intent = _brain.Tick(in sense, dt);

            if (intent.Fire && !_combat.IsDead)
                _combat.FireRocket(sense.SelfPos + intent.AimDir * 1.2f, intent.AimDir);

            var oneTime = new PlayerMotor.OneTimeInput { JumpPressed = intent.Jump };
            return new PlayerMotor.InputFrame(intent.WishDir, intent.Yaw, oneTime);
        }

        void RefreshTarget(float dt)
        {
            _retargetTimer -= dt;
            if (_retargetTimer > 0f && _target != null && !_target.IsDead) return;
            _retargetTimer = 1f;
            _target = null;
            float best = float.MaxValue;
            foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            {
                if (pc == _combat || pc.IsDead) continue;
                if (pc.GetComponent<BotDriver>() != null) continue; // humans only
                float d = (pc.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; _target = pc; }
            }
        }
    }
}
```

- [ ] **Step 4: Rewrite BotSpawner.cs** — replace the entire file with:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlienSkies.Bots
{
    /// <summary>Practice bot toggle: press B to spawn (or despawn) one bot.
    /// Uses the regular player prefab with a BotDriver added right after
    /// Instantiate — before the motor's first Update decides human-vs-bot.</summary>
    public sealed class BotSpawner : MonoBehaviour
    {
        public GameObject PlayerPrefab;

        GameObject _bot;

        void Update()
        {
            if (PlayerPrefab == null) return;
            var kb = Keyboard.current;
            if (kb == null || !kb.bKey.wasPressedThisFrame) return;

            if (_bot != null)
            {
                Destroy(_bot);
                _bot = null;
                return;
            }

            _bot = Instantiate(PlayerPrefab,
                AlienSkies.Combat.SpawnPointRegistry.Farthest(Vector3.zero),
                Quaternion.identity);
            _bot.AddComponent<BotDriver>();
        }
    }
}
```

- [ ] **Step 5: Type-rename sweep.** In each of these files, replace every `NetworkPlayerMotor` token with `PlayerMotor`:
  - `Assets/AlienSkies/Scripts/ViewmodelRig.cs` (the `public NetworkPlayerMotor Motor;` field)
  - `Assets/AlienSkies/Scripts/MovementHUD.cs` (the `public NetworkPlayerMotor Motor;` field)
  - `Assets/AlienSkies/Scripts/PlayerCombat.cs` (the `[RequireComponent]` attribute, the `_motor` field type, the `GetComponent` call) — AND change both `_motor.ServerTeleport(` call sites to `_motor.RespawnAt(`
  - `Assets/AlienSkies/Scripts/Rocket.cs` (the `GetComponent<NetworkPlayerMotor>()` in `Explode`)
  - `Assets/AlienSkies/Tests/HitscanGeometryTests.cs` (the `GetComponent<NetworkPlayerMotor>()` calls; leave the prefab PATH alone this task)
  - `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` (line 210: `AddComponent<NetworkPlayerMotor>()` → `AddComponent<PlayerMotor>()` — nothing else in the builder this task)

  Verify no stragglers: `grep -rn "NetworkPlayerMotor\|ServerTeleport\|ReplicateData\|ServerFireRocket" Assets/AlienSkies/` must return nothing.

- [ ] **Step 6: Compile + tests.** Run: `bash tools/run_tests.sh`. Expected: PASS, 85 tests. (HitscanGeometryTests still loads `NetworkPlayer.prefab`, whose serialized script GUID survived the `git mv` — the prefab finds `PlayerMotor` because the GUID is the identity, not the class name.)

- [ ] **Step 7: Commit**

```bash
git add -A Assets/AlienSkies/Scripts Assets/AlienSkies/Tests Assets/AlienSkies/Editor/PlaygroundBuilder.cs
git commit -m "refactor: NetworkPlayerMotor becomes PlayerMotor - per-frame sim replaces prediction"
```

---

### Task 4: Builder rewrite + delete FishNet, DefaultPrefabObjects, ConnectHUD

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` (region edits below — READ the file first; it is 1078 lines and mostly art code that must not change)
- Modify: `Assets/AlienSkies/Scripts/AlienSkies.Runtime.asmdef` (drop `"FishNet.Runtime"` from `references`)
- Modify: `Assets/AlienSkies/Editor/AlienSkies.Editor.asmdef` (drop `"FishNet.Runtime"` from `references`)
- Modify: `Assets/AlienSkies/Tests/HitscanGeometryTests.cs` (prefab path `NetworkPlayer.prefab` → `Player.prefab`)
- Delete: `Assets/FishNet/` (+ `.meta`), `Assets/DefaultPrefabObjects.asset` (+ `.meta`), `Assets/AlienSkies/Scripts/ConnectHUD.cs` (+ `.meta`), `Assets/AlienSkies/Prefabs/NetworkPlayer.prefab` (+ `.meta`)
- Note: `Assets/AlienSkies/Prefabs/Player.prefab` and the regenerated `Rocket.prefab` + `MovementPlayground.unity` are PRODUCED by the headless rebuild in Task 5, not hand-written here.

**Interfaces:**
- Consumes: `PlayerMotor`, `BotSpawner`, `MatchManager` (all plain MonoBehaviours from Tasks 1–3).
- Produces: a builder whose output scene contains — the arena (unchanged), a `Game` GameObject (`BotSpawner` + `MusicManager` as before), a `MatchManager` GameObject (plain), and an instantiated `Player.prefab` instance at a spawn point. No NetworkManager, no Tugboat, no PlayerSpawner, no ConnectHUD, no SceneCamera, no NetworkObject/NetworkTransform/NetworkTickSmoother anywhere.

- [ ] **Step 1: Read `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` in full.** The FishNet touchpoints (line numbers from the scout, verify while reading): usings at 8–11; scene-NetworkObject reserialize block ~99–109; `player.AddComponent<NetworkObject>()` ~209; NetworkTickSmoother block ~305–319; NetworkManager/Tugboat/PlayerSpawner bootstrap ~323–330; MatchManager `AddComponent<NetworkObject>()` ~333; `BuildRocketPrefab` NetworkObject+NetworkTransform ~409+. Also find: the `ConnectHUD` and `SceneCamera` creation, and every occurrence of the string `NetworkPlayer.prefab`.

- [ ] **Step 2: Apply the builder edits:**
  1. Delete the four FishNet `using` lines.
  2. Delete the scene-NetworkObject reserialize block (the `CreateSceneId`/`ReserializeEditorSetValues` loop) and any comment referencing the FishNet local patches.
  3. In `BuildPlayer`: delete `player.AddComponent<NetworkObject>()`; delete the entire NetworkTickSmoother configuration block (the `SerializedObject` property-path writes); keep the Graphical/Body/CameraPivot/MainCamera/WeaponRig hierarchy exactly as is.
  4. Change the player prefab save path from `Assets/AlienSkies/Prefabs/NetworkPlayer.prefab` to `Assets/AlienSkies/Prefabs/Player.prefab` (every occurrence in the file).
  5. Delete the NetworkManager GameObject bootstrap (NetworkManager + Tugboat + PlayerSpawner + ConnectHUD components and their configuration). Keep BotSpawner and MusicManager: create a plain `Game` GameObject carrying `BotSpawner` (wire `PlayerPrefab` to the loaded player prefab asset as before) and whatever the builder previously attached alongside (MusicManager stays wherever it is currently created).
  6. Delete the `SceneCamera` creation and the ConnectHUD wiring to it. In its place, after the prefab is saved, instantiate the player into the scene:

```csharp
// The player exists from t=0 — no connect flow, no scene camera.
// NOTE ON SCOPE: this lands inside BuildPlayer(MovementConfig, CombatConfig),
// which has NO `scene` local (that lives in Build()) — use the single-argument
// InstantiatePrefab overload: it lands in the active scene, and the builder's
// freshly created scene is the only one open (NewSceneMode.Single).
var playerPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AlienSkies/Prefabs/Player.prefab");
var playerInstance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefabAsset);
playerInstance.name = "Player";
// First wing spawn — the registry pattern already used elsewhere in the file:
var pts = Object.FindFirstObjectByType<SpawnPointRegistry>().Points;
playerInstance.transform.SetPositionAndRotation(pts[0].position, pts[0].rotation);
```

  (SpawnPointRegistry must already be populated at this point — the builder assigns `SpawnPointRegistry.Points` during arena construction. If BuildPlayer runs before the spawn registry exists in the build order, move this block to after the spawn points are created in Build() instead; the prefab asset is loadable from anywhere once saved.)

  7. In `BuildRocketPrefab`: delete `AddComponent<NetworkObject>()` and `AddComponent<FishNet.Component.Transforming.NetworkTransform>()`. Keep the save-over-same-path behavior (harmless, still nice for stable GUIDs).
  8. On the MatchManager GameObject creation: delete its `AddComponent<NetworkObject>()`, keep the `MatchManager` component + Config wiring.

- [ ] **Step 3: asmdef edits.** In `Assets/AlienSkies/Scripts/AlienSkies.Runtime.asmdef` and `Assets/AlienSkies/Editor/AlienSkies.Editor.asmdef`, remove the `"FishNet.Runtime"` entry from the `references` array (JSON — mind trailing commas).

- [ ] **Step 4: Test path update.** In `Assets/AlienSkies/Tests/HitscanGeometryTests.cs`, change the asset path `Assets/AlienSkies/Prefabs/NetworkPlayer.prefab` → `Assets/AlienSkies/Prefabs/Player.prefab`. Also grep the whole repo for other references: `grep -rn "NetworkPlayer" Assets/AlienSkies tools/ --include="*.cs" --include="*.sh"` — update any straggler (SceneScreenshot.cs is the likely candidate for prefab-loading paths).

- [ ] **Step 5: Deletions.**

```bash
cd "/mnt/c/Users/computer/My project"
git rm -r -q Assets/FishNet Assets/FishNet.meta
git rm -q Assets/DefaultPrefabObjects.asset Assets/DefaultPrefabObjects.asset.meta
git rm -q Assets/AlienSkies/Scripts/ConnectHUD.cs Assets/AlienSkies/Scripts/ConnectHUD.cs.meta
git rm -q "Assets/AlienSkies/Prefabs/NetworkPlayer.prefab" "Assets/AlienSkies/Prefabs/NetworkPlayer.prefab.meta"
```

  (The old prefab is deleted rather than migrated: the builder regenerates `Player.prefab` from code in Task 5, and nothing references the old GUID once DefaultPrefabObjects is gone. The WIP commit `30b3af1` preserves the user's last editor churn of it in history.)

- [ ] **Step 6: Compile + tests.** Run: `bash tools/run_tests.sh`. Expected: **HitscanGeometryTests FAILS or errors** (Player.prefab does not exist yet — it is generated in Task 5); the other 82 tests PASS and, critically, there are ZERO compile errors. If compile errors mention FishNet, grep for the missed reference and fix.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: remove FishNet, DefaultPrefabObjects, ConnectHUD; builder emits offline scene"
```

---

### Task 5: Headless rebuild + full verification

**Files:**
- Produce (generated): `Assets/AlienSkies/Prefabs/Player.prefab` (+meta), `Assets/AlienSkies/Prefabs/Rocket.prefab` (regenerated), `Assets/AlienSkies/Scenes/MovementPlayground.unity` (regenerated), `TestResults/shot_*.png`, `Builds/AlienSkies.exe`

**Interfaces:**
- Consumes: the Task 4 builder. Produces: the playable single-player build.

- [ ] **Step 1: Rebuild the scene + prefabs headless.** Find the Unity editor binary path used by `tools/run_tests.sh` (read the script), then run the builder the same way, e.g.:

```bash
cd "/mnt/c/Users/computer/My project"
"<UNITY_PATH>" -batchmode -quit -projectPath "$(pwd -W 2>/dev/null || wslpath -w .)" \
  -executeMethod AlienSkies.EditorTools.PlaygroundBuilder.BuildNeonVector \
  -logFile Logs/build_scene.log
```

  (Match the invocation style of the existing tools scripts exactly — they already solve the WSL→Windows path problem. If a `tools/rebuild_scene.sh` exists, use it.) Expected: exit 0; `Assets/AlienSkies/Prefabs/Player.prefab` exists; `git status` shows the regenerated scene + prefabs. Read the tail of the log on any error.

- [ ] **Step 2: Full test suite.** Run: `bash tools/run_tests.sh`. Expected: PASS, **85/85** including HitscanGeometryTests against the new Player.prefab.

- [ ] **Step 3: Screenshots.** Run the SceneScreenshot capture the same way as Step 1 (`-executeMethod AlienSkies.EditorTools.SceneScreenshot.Capture` — verify the exact method name in `Assets/AlienSkies/Editor/SceneScreenshot.cs`). Expected: `TestResults/shot_spawn.png`, `shot_arena_top.png`, `shot_weapons.png`, `shot_fx.png`, `shot_impactframe.png` regenerate. View `shot_weapons.png` and `shot_arena_top.png` — weapons must not be magenta; arena intact.

- [ ] **Step 4: Player build.** Run: `bash tools/build_player.sh`. Expected: exit 0, `Builds/AlienSkies.exe` refreshed, music masters copied into `Builds/AlienSkies_Data/StreamingAssets/Music/`.

- [ ] **Step 5: Commit the regenerated artifacts.**

```bash
git add Assets/AlienSkies/Prefabs Assets/AlienSkies/Scenes
git commit -m "build: regenerate scene and prefabs for single-player - Player.prefab replaces NetworkPlayer"
```

---

### Task 6: Documentation + PR

**Files:**
- Modify: `docs/ARCHITECTURE.md` (§2 layer cake: drop the FishNet layer; §3 "Networking" replaced by a short "Simulation" section describing the per-frame PlayerMotor loop + ExternalInput; §3.5 patches and §4.1 prefab-trio rule deleted — note their obsolescence in the diary instead; §4 builder: no scene NetworkObjects, player instantiated at build time; §8 asset table: FishNet row removed)
- Modify: `README.md` (controls/quick start: no HOST/JOIN — launch exe, B for bot)
- Modify: `CLAUDE.md` (RESUME HERE: Phase 1 done, next = Phase 2 de-pack; diary entry for the surgery; ADR-003 status → superseded by ADR-004)

- [ ] **Step 1: Apply the doc edits above.** Keep ARCHITECTURE.md's hard-won gotchas that still apply (spherecast self-skip, snapshot rule, SlowSpin collider rule, KillY corpse rule); delete only what FishNet's removal obsoletes.

- [ ] **Step 2: Verify clean tree + full gate one last time.** `git status` (only intended changes), `bash tools/run_tests.sh` (85/85).

- [ ] **Step 3: Commit + push + PR.**

```bash
git add docs/ARCHITECTURE.md README.md CLAUDE.md
git commit -m "docs: single-player architecture - simulation section replaces networking"
git push -u origin feature/singleplayer-pivot
gh pr create --base main --title "Single-player pivot Phase 1: de-network" \
  --body "ADR-004. Removes FishNet + all networked glue; per-frame PlayerMotor replaces prediction; game boots straight into the arena with a B-key bot. Spec: docs/superpowers/specs/2026-08-28-singleplayer-pivot-design.md. Includes the motion-pass stack (PR #21 content). 85/85 tests, exe rebuilt."
```

  (Tip branch to main per the stacked-merge lesson — this PR supersedes/contains PR #21; close #21 with a comment pointing here after this merges.)

- [ ] **Step 4: Verify the pushed diff** — `gh pr diff --name-only` and check: FishNet gone, no orphaned files, prefabs + scene + docs present.
