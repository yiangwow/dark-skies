# Vertical Recovery Implementation Plan (GitHub PR #9)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Falling is no longer a death sentence — kick off any planet you touch (launch along its surface normal), plus jump buffering and coyote time so Space always feels responsive.

**Architecture:** All mechanics live in `MovementCore` (pure C#, TDD) so they run under prediction: buffer/coyote/contact are dt-decayed timers inside the core, snapshotted in `MovementCoreState` for reconciles. The motor feeds planet contacts in from `OnControllerColliderHit` (filtered by a `PlanetSurface` marker the builder puts on planet colliders).

**Tech Stack:** Unity 6 EditMode NUnit, existing NetworkPlayerMotor prediction flow — no new packages, no assets.

## Global Constraints

- Branch `feature/vertical-recovery` off `feature/juice-pass` (stacked; PR #9 bases on `feature/juice-pass` until PR #8 merges).
- Git author the owner's git identity; NO Co-Authored-By; never push main; NEVER `git add -A`; every new Assets/ file needs its .meta.
- Repo root `/mnt/c/Users/computer/My project` — always quote the space.
- Tests: `bash tools/run_tests.sh` (Unity Editor CLOSED). Suite is 53 green; this plan adds 9 (62 total).
- New tuning numbers (all in MovementSettings → MovementConfig.asset, inspector-tunable): `JumpBufferTime 0.15`, `CoyoteTime 0.12`, `PlanetLaunchSpeed 26`.
- Design rules (decided): planet launch is ADDITIVE (`Velocity += normal * PlanetLaunchSpeed`) so incoming momentum carries through; launch takes priority over ground jump when both apply (standing on a planet top = big launch, not 9 m/s hop); contact is consumed on use (no double-launch from one touch; re-touching re-arms — planet pogo is a feature); no ceiling filter (kicking off any surface angle is legal); platforms are NOT launch pads (only colliders with the `PlanetSurface` marker).
- v1 non-goals: launch VFX/SFX polish (a zip-style HUD cue only), launch cooldown beyond contact consumption, wall-jumps off platforms.

---

### Task 1: Core mechanics — buffer, coyote, planet launch (TDD)

**Files:**
- Modify: `Assets/AlienSkies/Scripts/MovementSettings.cs`
- Modify: `Assets/AlienSkies/Scripts/MovementCore.cs`
- Create: `Assets/AlienSkies/Tests/VerticalRecoveryTests.cs`
- Modify: `Assets/AlienSkies/Tests/CoreStateTests.cs` (roundtrip covers new fields)

**Interfaces:**
- Produces (Task 2 relies on): `MovementCore.RegisterSurfaceContact(Vector3 normal)`, `float MovementCore.SurfaceContactRemaining { get; }`; `MovementCoreState` gains `float JumpBuffer; float Coyote; float SurfaceContact; Vector3 SurfaceNormal;`; `MovementSettings` gains `float JumpBufferTime = 0.15f; float CoyoteTime = 0.12f; float PlanetLaunchSpeed = 26f;`.

- [ ] **Step 1: Write the failing tests** — `Assets/AlienSkies/Tests/VerticalRecoveryTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Tests
{
    public class VerticalRecoveryTests
    {
        static MovementSettings S() => new MovementSettings();
        static MoveInput Jump() => new MoveInput { JumpPressed = true };
        static MoveInput NoInput() => new MoveInput();
        const float Dt = 0.02f;

        [Test]
        public void JumpBuffer_PressBeforeLanding_JumpsOnLanding()
        {
            var c = new MovementCore(S());
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);   // press mid-air
            c.Tick(NoInput(), Vector3.zero, grounded: false, Dt);
            c.Tick(NoInput(), Vector3.zero, grounded: true, Dt); // land within window
            Assert.AreEqual(S().JumpSpeed, c.Velocity.y, 1e-3f, "buffered jump fires on landing");
        }

        [Test]
        public void JumpBuffer_ExpiresBeforeLanding_NoJump()
        {
            var s = S();
            var c = new MovementCore(s);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            for (int i = 0; i < 20; i++) // 0.4s airborne >> buffer window
                c.Tick(NoInput(), Vector3.zero, grounded: false, Dt);
            c.Tick(NoInput(), Vector3.zero, grounded: true, Dt);
            Assert.Less(c.Velocity.y, s.JumpSpeed * 0.5f, "stale buffer must not jump");
        }

        [Test]
        public void Coyote_JumpJustAfterLeavingGround_StillJumps()
        {
            var c = new MovementCore(S());
            c.Tick(NoInput(), Vector3.zero, grounded: true, Dt);  // arm coyote
            c.Tick(NoInput(), Vector3.zero, grounded: false, Dt); // walk off ledge
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);    // press within window
            Assert.AreEqual(S().JumpSpeed, c.Velocity.y, 1e-3f, "coyote jump fires");
        }

        [Test]
        public void Coyote_ExpiresInAir_NoJump()
        {
            var s = S();
            var c = new MovementCore(s);
            c.Tick(NoInput(), Vector3.zero, grounded: true, Dt);
            for (int i = 0; i < 20; i++) // 0.4s falling >> coyote window
                c.Tick(NoInput(), Vector3.zero, grounded: false, Dt);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            Assert.Less(c.Velocity.y, 0f, "no ghost jumps long after leaving ground");
        }

        [Test]
        public void PlanetLaunch_JumpAfterContact_LaunchesAlongNormal()
        {
            var s = S();
            var c = new MovementCore(s);
            var normal = new Vector3(1f, 1f, 0f).normalized;
            c.RegisterSurfaceContact(normal);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            float along = Vector3.Dot(c.Velocity, normal);
            Assert.Greater(along, s.PlanetLaunchSpeed * 0.8f, "launch adds speed along the normal");
        }

        [Test]
        public void PlanetLaunch_IsAdditive_KeepsIncomingMomentum()
        {
            var s = S();
            var c = new MovementCore(s);
            c.SetVelocity(new Vector3(30f, 0f, 0f)); // flying past the planet
            c.RegisterSurfaceContact(Vector3.up);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            Assert.Greater(c.Velocity.x, 25f, "horizontal momentum survives the launch");
            Assert.Greater(c.Velocity.y, s.PlanetLaunchSpeed * 0.8f, "and the kick is added on top");
        }

        [Test]
        public void PlanetLaunch_BeatsGroundJump_WhenGroundedOnPlanet()
        {
            var s = S();
            var c = new MovementCore(s);
            c.RegisterSurfaceContact(Vector3.up);
            c.Tick(Jump(), Vector3.zero, grounded: true, Dt);
            Assert.Greater(c.Velocity.y, s.JumpSpeed * 1.5f, "planet top gives the launch, not the hop");
        }

        [Test]
        public void PlanetLaunch_ContactConsumedOnUse()
        {
            var s = S();
            var c = new MovementCore(s);
            c.RegisterSurfaceContact(Vector3.up);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            float yAfterFirst = c.Velocity.y;
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt); // no new contact
            Assert.Less(c.Velocity.y, yAfterFirst, "second jump must not re-launch from one touch");
        }

        [Test]
        public void PlanetLaunch_ContactExpires()
        {
            var s = S();
            var c = new MovementCore(s);
            c.RegisterSurfaceContact(Vector3.up);
            for (int i = 0; i < 20; i++) // 0.4s >> contact window
                c.Tick(NoInput(), Vector3.zero, grounded: false, Dt);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            Assert.Less(c.Velocity.y, 0f, "stale contact must not launch");
        }
    }
}
```

- [ ] **Step 2: Extend the state roundtrip** — in `Assets/AlienSkies/Tests/CoreStateTests.cs`, find the existing capture/apply roundtrip test and its state-mutation setup; add before capture:

```csharp
            core.RegisterSurfaceContact(new Vector3(0f, 0f, 1f));
```

and after apply, assert the contact survived:

```csharp
            Assert.Greater(restored.SurfaceContactRemaining, 0f, "surface contact must survive reconcile");
```

(Read the file first; adapt variable names to the existing test. If the roundtrip test constructs a fresh core to apply into, that fresh core is where the assertion goes.)

- [ ] **Step 3: Run to verify failure.** Editor closed: `cd "/mnt/c/Users/computer/My project" && bash tools/run_tests.sh` → expect CS1061 (`RegisterSurfaceContact`/`PlanetLaunchSpeed` missing).

- [ ] **Step 4: Implement.**

4a. `MovementSettings.cs` — extend the Air header block:

```csharp
        [Header("Air")]
        public float AirAccel = 25f;
        public float AirControlMaxSpeed = 9f;
        public float AirSteerRate = 120f; // deg/s the held direction bends your flight path
        public float Gravity = 16f;
        public float JumpSpeed = 9f;
        public float JumpBufferTime = 0.15f; // press-early forgiveness
        public float CoyoteTime = 0.12f;     // press-late forgiveness (also the planet-contact window)

        [Header("Planet launch")]
        public float PlanetLaunchSpeed = 26f; // kick off a planet along its surface normal
```

4b. `MovementCore.cs` — extend `MovementCoreState`:

```csharp
    public struct MovementCoreState
    {
        public Vector3 Velocity;
        public bool IsGrappling;
        public Vector3 GrappleAnchor;
        public bool IsZipping;
        public bool AttachPending;
        public float ZipCooldown;
        public float JumpBuffer;
        public float Coyote;
        public float SurfaceContact;
        public Vector3 SurfaceNormal;
    }
```

4c. `MovementCore` fields + API (next to `_zipCooldown`):

```csharp
        float _jumpBuffer;
        float _coyote;
        float _surfaceContact;
        Vector3 _surfaceNormal;

        public float SurfaceContactRemaining => Mathf.Max(_surfaceContact, 0f);

        /// <summary>Motor calls this from OnControllerColliderHit for PLANET
        /// colliders only — arms a short window in which jump kicks off the
        /// surface along its normal (the vertical-recovery mechanic).</summary>
        public void RegisterSurfaceContact(Vector3 normal)
        {
            _surfaceNormal = normal;
            _surfaceContact = _s.CoyoteTime;
        }
```

4d. `CaptureState`/`ApplyState`/`Reset` — carry the new fields:

```csharp
        public MovementCoreState CaptureState() => new MovementCoreState
        {
            Velocity = Velocity,
            IsGrappling = IsGrappling,
            GrappleAnchor = GrappleAnchor,
            IsZipping = IsZipping,
            AttachPending = _attachPending,
            ZipCooldown = _zipCooldown,
            JumpBuffer = _jumpBuffer,
            Coyote = _coyote,
            SurfaceContact = _surfaceContact,
            SurfaceNormal = _surfaceNormal,
        };

        public void ApplyState(in MovementCoreState s)
        {
            Velocity = s.Velocity;
            IsGrappling = s.IsGrappling;
            GrappleAnchor = s.GrappleAnchor;
            IsZipping = s.IsZipping;
            _attachPending = s.AttachPending;
            _zipCooldown = s.ZipCooldown;
            _jumpBuffer = s.JumpBuffer;
            _coyote = s.Coyote;
            _surfaceContact = s.SurfaceContact;
            _surfaceNormal = s.SurfaceNormal;
        }

        public void Reset()
        {
            Velocity = Vector3.zero;
            IsGrappling = false;
            IsZipping = false;
            _attachPending = false;
            _zipCooldown = 0f;
            _jumpBuffer = 0f;
            _coyote = 0f;
            _surfaceContact = 0f;
        }
```

4e. `Tick` — replace the timer decrement line and the jump block. At the top of `Tick` (replacing `if (_zipCooldown > 0f) _zipCooldown -= dt;`):

```csharp
            if (_zipCooldown > 0f) _zipCooldown -= dt;
            _jumpBuffer = input.JumpPressed ? _s.JumpBufferTime : _jumpBuffer - dt;
            _coyote = grounded ? _s.CoyoteTime : _coyote - dt;
            _surfaceContact -= dt;
```

Replace the old jump block:

```csharp
            if (grounded && input.JumpPressed)
                Velocity = new Vector3(Velocity.x, _s.JumpSpeed, Velocity.z);
```

with:

```csharp
            if (_jumpBuffer > 0f)
            {
                if (_surfaceContact > 0f)
                {
                    // Planet kick: additive along the surface normal so incoming
                    // momentum carries through — and it beats the ground hop, so
                    // planet tops launch too.
                    Velocity += _surfaceNormal * _s.PlanetLaunchSpeed;
                    _jumpBuffer = 0f;
                    _surfaceContact = 0f;
                }
                else if (grounded || _coyote > 0f)
                {
                    Velocity = new Vector3(Velocity.x, _s.JumpSpeed, Velocity.z);
                    _jumpBuffer = 0f;
                    _coyote = 0f;
                }
            }
```

- [ ] **Step 5: Run suite** → `result=Passed total=62 passed=62 failed=0` (53 + 9; the CoreStateTests edit extends an existing test rather than adding one).

- [ ] **Step 6: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/MovementSettings.cs Assets/AlienSkies/Scripts/MovementCore.cs Assets/AlienSkies/Tests/VerticalRecoveryTests.cs Assets/AlienSkies/Tests/VerticalRecoveryTests.cs.meta Assets/AlienSkies/Tests/CoreStateTests.cs && git commit -m "feat: planet launch + jump buffer + coyote time in MovementCore (TDD)"
```

---

### Task 2: PlanetSurface marker, motor hook, HUD cue, builder wiring

**Files:**
- Create: `Assets/AlienSkies/Scripts/PlanetSurface.cs`
- Modify: `Assets/AlienSkies/Scripts/NetworkPlayerMotor.cs` (OnControllerColliderHit)
- Modify: `Assets/AlienSkies/Scripts/MovementHUD.cs` (KICK! cue)
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` (marker on planet colliders)

**Interfaces:**
- Consumes: `MovementCore.RegisterSurfaceContact(Vector3)`, `MovementCore.SurfaceContactRemaining` (Task 1).
- Produces: `AlienSkies.Movement.PlanetSurface : MonoBehaviour` (empty marker).

- [ ] **Step 1: Create `Assets/AlienSkies/Scripts/PlanetSurface.cs`:**

```csharp
using UnityEngine;

namespace AlienSkies.Movement
{
    /// <summary>Marker: this collider is a planet — touching it arms the jump-key
    /// launch (kick off along the surface normal). Builder adds it to every
    /// SgtPlanet's SphereCollider object; platforms deliberately don't get it.</summary>
    public sealed class PlanetSurface : MonoBehaviour { }
}
```

- [ ] **Step 2: Motor hook** — in `NetworkPlayerMotor.cs`, replace:

```csharp
        void OnControllerColliderHit(ControllerColliderHit hit) =>
            Core.ClampAgainstSurface(hit.normal);
```

with:

```csharp
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Core.ClampAgainstSurface(hit.normal);
            // Planets arm the jump-key kick (vertical recovery); the marker keeps
            // platforms out of it. Registration is part of the simulation — it
            // happens inside cc.Move during the replicate, so replays see it too.
            if (hit.collider.GetComponent<PlanetSurface>() != null)
                Core.RegisterSurfaceContact(hit.normal);
        }
```

- [ ] **Step 3: HUD cue** — in `MovementHUD.cs`, after the zip-cooldown label block (the `if (Motor.Core.ZipCooldownRemaining > 0f)` statement), add:

```csharp
            if (Motor.Core.SurfaceContactRemaining > 0f)
                GUI.Label(new Rect(24f, 120f, 400f, 32f), "PLANET — SPACE to kick off!", _label);
```

- [ ] **Step 4: Builder** — in `PlaygroundBuilder.cs` `BuildArena()`, after the `col.radius = 1f;` line inside the planet loop, add:

```csharp
                planet.gameObject.AddComponent<PlanetSurface>();
```

(`using AlienSkies.Movement;` is already imported.)

- [ ] **Step 5: Rebuild scene headlessly** (editor closed):

```bash
cd "/mnt/c/Users/computer/My project" && timeout -k 30 570 "/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\Users\computer\My project" -executeMethod AlienSkies.EditorTools.PlaygroundBuilder.Build -logFile - 2>&1 | grep -iE "error CS|PlaygroundBuilder: saved" | head -3
```

Expected: `PlaygroundBuilder: saved ...`.

- [ ] **Step 6: Run suite** → 62/62.

- [ ] **Step 7: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/PlanetSurface.cs Assets/AlienSkies/Scripts/PlanetSurface.cs.meta Assets/AlienSkies/Scripts/NetworkPlayerMotor.cs Assets/AlienSkies/Scripts/MovementHUD.cs Assets/AlienSkies/Editor/PlaygroundBuilder.cs Assets/AlienSkies/Scenes/MovementPlayground.unity Assets/AlienSkies/Prefabs/NetworkPlayer.prefab Assets/AlienSkies/Prefabs/Rocket.prefab Assets/DefaultPrefabObjects.asset && git commit -m "feat: planets are launch pads - marker, motor hook, HUD cue, arena wiring"
```

---

### Task 3: Docs, exe, push, PR

**Files:**
- Modify: `CLAUDE.md` (RESUME HERE + diary)

- [ ] **Step 1: Update CLAUDE.md.** RESUME HERE (replace the existing line):

```markdown
> **▶ RESUME HERE (2026-07-05):** VERTICAL RECOVERY IN (PR #9, stacked on PR #8): touch any planet → SPACE kicks you off along its surface (26 m/s, additive), jump buffering 0.15s + coyote 0.12s. PR #8 (juice: Hovl violet explosions, hitmarkers, screenshake) still awaiting playtest. Duel: Unity Play → HOST + `Builds/AlienSkies.exe` → JOIN 127.0.0.1. Next: match win condition (first to 10), then sky pass 2.
```

Diary entry (top of Diary section):

```markdown
- **2026-07-05** — PR #9 vertical recovery: planet launch pads (jump off any touched planet along its normal, additive 26 m/s, beats ground hop, contact consumed on use), jump buffering + coyote time — all in MovementCore under prediction (9 tests, 62 total; state snapshot extended for reconciles). PlanetSurface marker keeps platforms plain.
```

- [ ] **Step 2: Rebuild exe, commit docs, push, PR** (note `--base feature/juice-pass` — stacked on the unmerged PR #8):

```bash
cd "/mnt/c/Users/computer/My project" && rm -f Builds/AlienSkies.exe && bash tools/build_player.sh 2>&1 | tail -1 && git add CLAUDE.md docs/superpowers/plans/2026-07-05-vertical-recovery.md && git commit -m "docs: RESUME HERE + diary for vertical recovery" && git push -u origin feature/vertical-recovery && gh pr create --base feature/juice-pass --title "Vertical recovery: planets are launch pads + jump buffer/coyote" --body "$(cat <<'EOF'
Stacked on PR #8 (merge that first; this PR's diff shrinks to its own commits).

- Touch any planet -> SPACE kicks you off along the surface normal: +26 m/s ADDITIVE, so flying past a planet and kicking off mid-graze keeps your momentum. Planet tops launch too (beats the 9 m/s ground hop).
- Contact window = coyote window (0.12s), consumed on use; re-touch re-arms (planet pogo is legal).
- Jump buffering (0.15s) + coyote time (0.12s) for ordinary jumps - Space finally always does something sensible.
- All in MovementCore under prediction: 9 new tests (62 total), reconcile state extended.
- HUD shows "PLANET - SPACE to kick off!" while the window is live.

Playtest: fall off the spawn platform toward a planet, touch it, press Space - you should rocket back up along the surface. Try grazing a planet at 60 m/s and kicking off without stopping.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 3: Self-review checklist:** `git status --short` (only known dirt), zero unpushed commits, `gh pr view 9 --json state` OPEN, exe timestamp fresh.

- [ ] **Step 4: Hand off.** Playtest checkpoints: launch strength (26 too weak/strong?), window feel (0.12s contact — too strict?), does the HUD cue read, does planet pogo feel like a feature, buffered jumps on platform landings.
