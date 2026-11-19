# Zip-Strike Grapple Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** While grappling (now on F), pressing RMB overwrites your momentum with a straight-line 45 m/s flight to the anchor that sticks the landing — "target a platform, land on it quickly" — with a 3 s cooldown.

**Architecture:** All zip logic goes in the pure-C# `MovementCore` (same pattern as every movement feature: unit-testable tick function, thin `PlayerMotor` input glue, numbers in `MovementConfig`). While zipping, the core owns velocity completely: no gravity, no steering, no caps.

**Tech Stack:** Unity 6 (6000.5.1f1), URP, NUnit EditMode tests, new Input System.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-04-first-blood-design.md` (PR #3 section).
- Branch: `feature/zip-strike` (already exists, contains the spec commit).
- Git author the owner's git identity; NO Co-Authored-By lines; never push main.
- Repo root: `/mnt/c/Users/computer/My project` — **always quote the space**.
- Test command (Unity Editor must be CLOSED; warn the user first): `bash tools/run_tests.sh` from repo root; expect `result=Passed`.
- NEVER `git add -A` / `git add .`: `Assets/Space Graphics Toolkit*` (imported, must stay uncommitted until PR #6), `Assets/Scenes/SampleScene.unity` (pre-existing user dirt), `ProjectSettings/SceneTemplateSettings.json` (untracked, leave) must not be committed. Add explicit paths only.
- Every new file under `Assets/` needs its Unity-generated `.meta` committed with it (the `.meta` appears after the first headless run).
- Numbers (from spec, all land in `MovementSettings` + `MovementConfig.asset`): ZipSpeed 45, ZipArriveRadius 2, ZipLandSpeed 8, ZipCooldown 3.
- Known v1 behavior (do not "fix"): zipping into a wall grinds along it (surface clamp each hit, zip re-aims each tick) until arrival or cancel — playtest checkpoint, noted in spec.

---

### Task 1: Zip state machine in MovementCore (settings + core + tests)

**Files:**
- Modify: `Assets/AlienSkies/Scripts/MovementSettings.cs` (add 4 fields after `AimAssistRadius`, line ~33)
- Modify: `Assets/AlienSkies/Scripts/MovementCore.cs`
- Create: `Assets/AlienSkies/Tests/ZipTests.cs`

**Interfaces:**
- Consumes: existing `MovementCore` API (`StartGrapple(Vector3)`, `StopGrapple()`, `Reset()`, `Tick(in MoveInput, Vector3 position, bool grounded, float dt)`, `SetVelocity`, `Velocity`, `IsGrappling`).
- Produces (Tasks 3–4 rely on these exact members):
  - `public bool IsZipping { get; private set; }`
  - `public float ZipCooldownRemaining { get; }` (0 when ready)
  - `public bool StartZip()` — returns false unless grappling, not already zipping, and cooldown elapsed
  - `MovementSettings.ZipSpeed/ZipArriveRadius/ZipLandSpeed/ZipCooldown` (floats)

- [ ] **Step 1: Write the failing tests**

Create `Assets/AlienSkies/Tests/ZipTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Tests
{
    public class ZipTests
    {
        const float Dt = 0.016f;
        static MovementSettings S() => new MovementSettings();
        static MoveInput NoInput => new MoveInput { WishDir = Vector3.zero, JumpPressed = false };

        [Test]
        public void StartZip_RequiresActiveGrapple()
        {
            var core = new MovementCore(S());
            Assert.IsFalse(core.StartZip(), "zip must be an upgrade off the hook, not a free teleport");
            Assert.IsFalse(core.IsZipping);
        }

        [Test]
        public void Zip_OverwritesMomentum_TowardAnchor()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            core.SetVelocity(new Vector3(0f, 0f, 30f)); // flying sideways, fast
            Assert.IsTrue(core.StartZip());
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(s.ZipSpeed, core.Velocity.x, 0.01f, "zip overwrites momentum, not adds to it");
            Assert.AreEqual(0f, core.Velocity.z, 0.01f, "old sideways momentum must be gone");
        }

        [Test]
        public void Zip_SuppressesGravityAndSteering()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            var input = new MoveInput { WishDir = Vector3.forward, JumpPressed = false };
            core.Tick(input, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(0f, core.Velocity.y, 1e-4f, "gravity must be off during zip");
            Assert.AreEqual(0f, core.Velocity.z, 1e-4f, "WASD must not bend a zip");
        }

        [Test]
        public void Zip_Arrival_DetachesAndClampsLandingSpeed_NoReleaseBoost()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(1.5f, 0f, 0f)); // inside ZipArriveRadius of origin
            core.SetVelocity(new Vector3(45f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.IsFalse(core.IsZipping, "arrival ends the zip");
            Assert.IsFalse(core.IsGrappling, "arrival detaches the hook");
            Assert.LessOrEqual(core.Velocity.magnitude, s.ZipLandSpeed + 0.01f,
                "arrival clamps speed so you stick the landing (and no release boost fires)");
        }

        [Test]
        public void Zip_Cooldown_BlocksImmediateReuse()
        {
            var core = new MovementCore(S());
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            core.StopGrapple();
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsFalse(core.StartZip(), "cooldown must block back-to-back zips");
        }

        [Test]
        public void Zip_Cooldown_ExpiresAfterConfiguredTime()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            core.StopGrapple();
            int ticks = Mathf.CeilToInt(s.ZipCooldown / Dt) + 2;
            for (int i = 0; i < ticks; i++)
                core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(0f, core.ZipCooldownRemaining, 1e-4f);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip(), "cooldown must recover after ZipCooldown seconds");
        }

        [Test]
        public void Zip_CancelViaStopGrapple_KeepsVelocityWithReleaseBoost()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt); // velocity = 45 along +x
            core.StopGrapple();
            Assert.IsFalse(core.IsZipping);
            Assert.GreaterOrEqual(core.Velocity.x, s.ZipSpeed - 0.01f,
                "cancel keeps zip momentum (release boost applies on top)");
        }

        [Test]
        public void Reset_ClearsZipStateAndCooldown()
        {
            var core = new MovementCore(S());
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            core.StartZip();
            core.Reset();
            Assert.IsFalse(core.IsZipping);
            Assert.AreEqual(0f, core.ZipCooldownRemaining, 1e-4f, "respawn must not carry zip cooldown");
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip(), "zip must be available immediately after reset");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to compile** (that IS the failure mode here — `StartZip` doesn't exist)

Ask the user to close the Unity Editor if it's open, then:
Run: `cd "/mnt/c/Users/computer/My project" && bash tools/run_tests.sh`
Expected: compilation errors mentioning `StartZip` / `IsZipping` (script errors mean exit non-zero; that's the failing state).

- [ ] **Step 3: Add settings fields**

In `Assets/AlienSkies/Scripts/MovementSettings.cs`, after the `AimAssistRadius` line, insert:

```csharp
        [Header("Zip-strike")]
        public float ZipSpeed = 45f;         // straight-line flight speed to anchor
        public float ZipArriveRadius = 2f;   // detach + clamp within this distance
        public float ZipLandSpeed = 8f;      // arrival speed cap so you stick the landing
        public float ZipCooldown = 3f;       // seconds between zips
```

- [ ] **Step 4: Implement zip in MovementCore**

In `Assets/AlienSkies/Scripts/MovementCore.cs`:

4a. Add state (next to `bool _attachPending;`):

```csharp
        float _zipCooldown;

        public bool IsZipping { get; private set; }
        public float ZipCooldownRemaining => Mathf.Max(_zipCooldown, 0f);
```

4b. `Reset()` becomes:

```csharp
        public void Reset()
        {
            Velocity = Vector3.zero;
            IsGrappling = false;
            IsZipping = false;
            _attachPending = false;
            _zipCooldown = 0f;
        }
```

4c. Top of `Tick(...)`, before the `if (grounded && !IsGrappling)` line, insert:

```csharp
            if (_zipCooldown > 0f) _zipCooldown -= dt;

            if (IsZipping)
            {
                ZipTowardAnchor(position);
                return; // zip owns velocity: no steering, gravity, jump, or caps
            }
```

4d. New methods (place after `PullTowardAnchor`):

```csharp
        /// <summary>Zip-strike: overwrite velocity with a straight-line flight to the
        /// anchor. Re-aims every tick so it truly arrives; on arrival it detaches and
        /// clamps speed so you stick the landing instead of skidding off.</summary>
        void ZipTowardAnchor(Vector3 position)
        {
            var toAnchor = GrappleAnchor - position;
            if (toAnchor.magnitude <= _s.ZipArriveRadius)
            {
                IsZipping = false;
                IsGrappling = false; // arrival: no release boost — landing, not flinging
                if (Velocity.magnitude > _s.ZipLandSpeed)
                    Velocity = Velocity.normalized * _s.ZipLandSpeed;
                return;
            }
            Velocity = toAnchor.normalized * _s.ZipSpeed;
        }

        public bool StartZip()
        {
            if (!IsGrappling || IsZipping || _zipCooldown > 0f) return false;
            IsZipping = true;
            _attachPending = false; // zip supersedes the attach yank
            _zipCooldown = _s.ZipCooldown;
            return true;
        }
```

4e. `StopGrapple()` — add `IsZipping = false;` right after `IsGrappling = false;` (cancel path: keeps current velocity, existing release boost still applies).

- [ ] **Step 5: Run tests to verify all pass**

Run: `cd "/mnt/c/Users/computer/My project" && bash tools/run_tests.sh`
Expected: `result=Passed total=31 passed=31 failed=0` (23 existing + 8 new).

- [ ] **Step 6: Commit (include the new .meta Unity generated)**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/MovementCore.cs Assets/AlienSkies/Scripts/MovementSettings.cs Assets/AlienSkies/Tests/ZipTests.cs Assets/AlienSkies/Tests/ZipTests.cs.meta && git commit -m "feat: zip-strike core - overwrite-momentum flight to grapple anchor

RMB while grappling (wired in next commit) overwrites velocity with a
straight 45 m/s flight to the anchor; gravity/steering/caps suspended.
Arrival within 2 m detaches and clamps to 8 m/s so landings stick (no
release boost); cancel via grapple release keeps momentum + boost.
3 s cooldown, cleared on respawn. 8 new tests (31 total)."
```

---

### Task 2: Sync MovementConfig.asset

**Files:**
- Modify: `Assets/AlienSkies/Config/MovementConfig.asset`

**Interfaces:**
- Consumes: Task 1's field names — YAML keys must match exactly.
- Produces: the asset the scene's player already references (no scene change needed).

- [ ] **Step 1: Add the four YAML keys**

In `Assets/AlienSkies/Config/MovementConfig.asset`, after the `    AimAssistRadius: 3` line, insert (4-space indent, same as siblings):

```yaml
    ZipSpeed: 45
    ZipArriveRadius: 2
    ZipLandSpeed: 8
    ZipCooldown: 3
```

- [ ] **Step 2: Verify**

Run: `grep -n "Zip" "/mnt/c/Users/computer/My project/Assets/AlienSkies/Config/MovementConfig.asset"`
Expected: 4 lines, indented 4 spaces, between `AimAssistRadius` and `KillY`.

- [ ] **Step 3: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Config/MovementConfig.asset && git commit -m "config: zip-strike tuning defaults (speed 45, arrive 2, land 8, cd 3)"
```

---

### Task 3: Input rebind (grapple→F, zip→RMB) + HUD cooldown

**Files:**
- Modify: `Assets/AlienSkies/Scripts/PlayerMotor.cs:76-79`
- Modify: `Assets/AlienSkies/Scripts/MovementHUD.cs:28-29`

**Interfaces:**
- Consumes: `Core.StartZip()`, `Core.IsZipping`, `Core.ZipCooldownRemaining` from Task 1.
- Produces: final control map — F hold = grapple, RMB-while-grappling = zip, LMB reserved for fire (PR #5).

- [ ] **Step 1: Rebind in PlayerMotor**

In `Assets/AlienSkies/Scripts/PlayerMotor.cs`, replace:

```csharp
            if (cursorLocked && mouse.rightButton.wasPressedThisFrame && HasGrappleTarget)
                Core.StartGrapple(anchor);
            if (!cursorLocked || mouse.rightButton.wasReleasedThisFrame)
                Core.StopGrapple();
```

with:

```csharp
            if (cursorLocked && kb.fKey.wasPressedThisFrame && HasGrappleTarget)
                Core.StartGrapple(anchor);
            if (!cursorLocked || kb.fKey.wasReleasedThisFrame)
                Core.StopGrapple();
            // Zip-strike: RMB while hooked. LMB stays free — it becomes fire in PR #5.
            if (cursorLocked && mouse.rightButton.wasPressedThisFrame)
                Core.StartZip();
```

(`Core.StartZip()` self-guards when not grappling / on cooldown, so no outer condition.)

- [ ] **Step 2: HUD state + cooldown**

In `Assets/AlienSkies/Scripts/MovementHUD.cs`, replace:

```csharp
            if (Motor.Core.IsGrappling)
                GUI.Label(new Rect(24f, 56f, 400f, 32f), "GRAPPLING", _label);
```

with:

```csharp
            if (Motor.Core.IsZipping)
                GUI.Label(new Rect(24f, 56f, 400f, 32f), "ZIP!", _label);
            else if (Motor.Core.IsGrappling)
                GUI.Label(new Rect(24f, 56f, 400f, 32f), "GRAPPLING (RMB to zip)", _label);
            if (Motor.Core.ZipCooldownRemaining > 0f)
                GUI.Label(new Rect(24f, 88f, 400f, 32f),
                          $"zip ready in {Motor.Core.ZipCooldownRemaining:0.0}s", _label);
```

- [ ] **Step 3: Run the full suite (also proves the touched files compile)**

Run: `cd "/mnt/c/Users/computer/My project" && bash tools/run_tests.sh`
Expected: `result=Passed total=31 passed=31 failed=0`.

- [ ] **Step 4: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/PlayerMotor.cs Assets/AlienSkies/Scripts/MovementHUD.cs && git commit -m "feat: grapple moves to F, RMB-while-grappling zips; HUD shows zip state

LMB is now unbound on purpose - it becomes fire in the combat PR."
```

---

### Task 4: Docs, push, PR, verification

**Files:**
- Modify: `CLAUDE.md` (repo root — RESUME HERE line + diary)

**Interfaces:**
- Consumes: everything above, complete and committed.
- Produces: PR #3 open with all commits; repo brain current.

- [ ] **Step 1: Update CLAUDE.md**

Rewrite the `▶ RESUME HERE` blockquote (line 3) to:

```markdown
> **▶ RESUME HERE (2026-07-04):** First Blood milestone in flight (spec: `docs/superpowers/specs/2026-07-04-first-blood-design.md`). PR #3 zip-strike done: WASD move, mouse look, Space jump, **hold F grapple** (release to fling), **RMB while grappling = zip-strike** to anchor (3s cd), Esc frees cursor. LMB intentionally unbound until combat PR #5. Next: PR #4 Fish-Net foundation, then PR #5 combat, PR #6 SGT visuals (SGT imported, uncommitted).
```

Add to the top of the Diary section:

```markdown
- **2026-07-04** — First Blood spec + ADR-003 (Fish-Net day one). PR #3 zip-strike: overwrite-momentum flight to anchor (45 m/s, land-clamp 8, 3s cd), grapple rebound RMB→F, 8 new tests (31 total).
```

- [ ] **Step 2: Commit docs, push, open PR**

```bash
cd "/mnt/c/Users/computer/My project" && git add CLAUDE.md && git commit -m "docs: RESUME HERE + diary for zip-strike" && git push -u origin feature/zip-strike && gh pr create --title "Zip-strike grapple: RMB while hooked flies you to the anchor" --body "$(cat <<'EOF'
Implements PR #3 of the First Blood milestone (spec: docs/superpowers/specs/2026-07-04-first-blood-design.md).

- Grapple moves RMB -> **hold F** (LMB intentionally unbound; becomes fire in PR #5)
- **RMB while grappling = zip-strike**: momentum is overwritten with a straight 45 m/s flight to the anchor; gravity/steering suspended; arrival within 2 m detaches + clamps to 8 m/s so you stick platform landings; releasing F cancels and keeps momentum (+release boost)
- 3 s cooldown with HUD readout; cleared on respawn
- All numbers tunable in MovementConfig.asset
- Tests: 8 new, 31/31 green
- Known v1 quirk (per spec): zipping into a wall grinds along it until arrival/cancel - playtest checkpoint

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 3: Self-review checklist (mandatory before telling the user it's done)**

```bash
cd "/mnt/c/Users/computer/My project" && git status --short && git log origin/feature/zip-strike..HEAD --oneline && gh pr view 3 --json state,commits --jq '{state: .state, n: (.commits | length)}'
```

Expected: only the known dirt (`SampleScene.unity` modified, `SceneTemplateSettings.json` + SGT folders untracked); zero unpushed commits; PR state OPEN with all zip commits (spec 2 + tasks 4 = 6).

- [ ] **Step 4: Hand off to user for playtest**

Tell the user: reopen Unity, open `MovementPlayground`, Play. Checklist: hold F at a far platform's galaxy, then RMB — you should rocket straight there and land soft; try cancelling mid-zip with F release; confirm the 3 s cooldown readout; confirm WASD/jump/steering unchanged otherwise.
