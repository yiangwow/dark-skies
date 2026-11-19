# Heat Redesign — guardrails 6 + 7 Implementation Plan (REVISION 2)

> REVISION 2 folds an 8-finding adversarial review (5 blockers): real grounded
> signal (SurfaceContactRemaining is PLANET-only — platforms never arm it),
> hit-trickle cooldown (shotgun pellets = 16 RegisterHits in one frame),
> full existing-test enumeration, runner-truth gate count.

> **For agentic workers:** execute task-by-task; each task ends at the 134-test
> gate (count changes where stated). Spec = `docs/DESIGN-GUARDRAILS.md`
> guardrails 6 (heat-moves-or-dies) and 7 (hit-drops-one-tier).

**Goal:** heat gain keys on movement state at kill time (never mere firing,
never being hit); taking damage drops exactly one multiplier tier; no
stationary loop sustains x3+.

**Architecture:** all heat math stays pure in `MusicCore` (new kill-context
API + tier-drop); `MusicManager.Bind` is the only wiring site that changes.
Scoring (`RunCore`) and the mix ride are untouched.

**Kill-method note:** guardrail 6 says movement-state × kill-method; with the
rocket-only lean there is one method, so the method axis is deferred until a
second kill method exists (recorded, not dropped).

## Global constraints
- Pure C# first: every behavior change lands in `MusicCore` with tests.
- MusicCore stays config-free (public fields are the contract, pinned by tests).
- No changes to: RunCore scoring, mix ride (Level/volume/low-pass), builder.
- Existing consumers of removed APIs must be retargeted in the same task.

---

### Task 1: MusicCore — kill context, staleness, tier drop

**Files:** Modify `Assets/AlienSkies/Scripts/MusicCore.cs`,
`Assets/AlienSkies/Tests/MusicCoreTests.cs` (read both fully first).

New public surface (replaces RegisterFire/RegisterDamage/RegisterKill):

```csharp
public enum KillMoveState { Grounded, Airborne, Flow } // Flow = grapple or zip

public float HitHeat = 0.08f;        // attacker-side non-kill hit: trickle only
public float HitTrickleCooldown = 0.7f; // min seconds between hit-trickle adds
public float KillHeatGrounded = 0.33f;
public float KillHeatAirborne = 0.60f;
public float KillHeatFlow = 0.72f;
public float StalenessFactor = 0.65f; // consecutive same-state kill multiplier

// removed: FireHeat, DamageHeat, RegisterFire(), RegisterDamage(),
// and the ZERO-ARG RegisterKill() — no overload survives (see test
// enumeration below; every existing caller is retargeted in this task).

KillMoveState _lastKillState; bool _hasKilled; int _sameStateStreak;
float _hitCooldown;

/// <summary>Non-kill hit trickle, RATE-LIMITED: one add per
/// HitTrickleCooldown regardless of weapon — the shotgun's pellet loop
/// raises up to 16 hit-confirms in ONE frame (16 x 0.08 = instant x4
/// from a dead stop), and the rifle's 0.207s gate would out-run decay
/// (0.386/s vs 0.12/s). Capped: max 0.08/0.7 = 0.114/s &lt; decay — no
/// weapon can hold a tier by plinking. Cooldown ticks in Tick().</summary>
public void RegisterHit()
{
    if (_hitCooldown > 0f) return;
    _hitCooldown = HitTrickleCooldown;
    AddHeat(HitHeat);
}

public void RegisterKill(KillMoveState state)
{
    _sameStateStreak = _hasKilled && state == _lastKillState ? _sameStateStreak + 1 : 0;
    _hasKilled = true; _lastKillState = state;
    float baseHeat = state == KillMoveState.Flow ? KillHeatFlow
                   : state == KillMoveState.Airborne ? KillHeatAirborne
                   : KillHeatGrounded;
    AddHeat(baseHeat * Mathf.Pow(StalenessFactor, _sameStateStreak));
}

/// <summary>Guardrail 7: damage costs exactly one multiplier tier — heat
/// lands just under the current tier's floor (top of the tier below). At
/// tier 1 there is nothing below: heat halves instead (still recoverable).
/// maxMultiplier mirrors RunCore.MultiplierFor's mapping.</summary>
public void DropTier(int maxMultiplier)
{
    int bands = Mathf.Max(1, maxMultiplier - 1);
    int tier = 1 + Mathf.Min(bands, Mathf.FloorToInt(Intensity * bands + 0.0001f));
    _heat = tier > 1 ? Mathf.Max(0f, (float)(tier - 1) / bands - 0.02f)
                     : _heat * 0.5f;
}
```

`Tick(dt)` additionally decrements `_hitCooldown` (floor 0).
`ResetHeat()` also resets `_hasKilled`/`_sameStateStreak`/`_hitCooldown`
(runs start fresh — a stale streak must not leak into the next run).

**Also in Task 1 — real grounded signal (blocker fix):**
`MovementCore.SurfaceContactRemaining` is armed ONLY by PlanetSurface
colliders (PlayerMotor.OnControllerColliderHit gates on
`GetComponent<PlanetSurface>()`); every CreatePlatform surface — StartPad,
PlatformRun, CombatPocket, the stage — never arms it, so it is NOT a grounded
proxy (a platform camper would read Airborne: 0.60/kill, x4 in 3 stationary
kills — the exact exploit this plan exists to kill). Fix in
`Assets/AlienSkies/Scripts/MovementCore.cs`: the `grounded` bool is already a
parameter of `Tick(...)` — store it: `public bool IsGrounded { get; private
set; }` assigned at the top of Tick. Read MovementCore fully first and follow
its existing property style; do not touch the PlanetSurface/coyote logic.
Add one test in MovementCoreTests' existing style: Tick with grounded=true →
IsGrounded true; next Tick with grounded=false → false.

**Existing-test enumeration (read MusicCoreTests.cs first; all 7 accounted
for — none of this is optional, and loosening a tolerance is forbidden):**
- `Events_HeatInOrder` — DELETED. Its premise (fire < damage < kill heat) has
  no post-redesign equivalent; the kill-vs-hit ordering lives in new test 1.
- `Intensity_ClampsAtOne` — retarget: 10 kills CYCLING Grounded→Airborne→Flow
  (state changes every kill → streak stays 0; undiscounted sum ~5.5 → clamp
  still exercised). Keep the 1e-4 assertion.
- `HotStreak_HoldsFullIntensityBriefly` — retarget: kills Flow, Airborne,
  Flow (0.72+0.60+0.72 = 2.04 → capped at MaxHeat 1.5; after Tick(1s):
  1.38 → Intensity still 1). Keep the assertion.
- `Heat_DecaysToCold`, `RoundOver_DucksTheLevel`, `ResetHeat_ZeroesIntensity`
  — signature-only retarget to `RegisterKill(KillMoveState.Grounded)`
  (assertions unchanged; recompute any exact expected values if one asserts a
  specific sub-1 intensity — state the recomputation in the task report).

**New tests** (same file/style):
1. `KillHeat_ScalesWithMoveState` — one kill per state on fresh cores:
   Intensity Flow > Airborne > Grounded, exact values 0.72/0.60/0.33; also
   assert a lone RegisterHit (0.08) < a lone Grounded kill (kill-vs-hit
   ordering, replacing Events_HeatInOrder's surviving intent).
2. `SameStateKills_GoStale_StateChangeRestores` — 3 grounded kills back to
   back gain 0.33, 0.2145, ~0.1394 (assert cumulative with 1e-3 tolerance);
   then one Airborne kill gains full 0.60.
3. `DropTier_LandsExactlyOneTierDown` — for maxMultiplier 4: seed heat to the
   middle of each tier 2..4 (0.5, 0.8, 1.0), DropTier, assert
   `RunCore.MultiplierFor`-equivalent mapping (recompute inline:
   `1 + min(3, floor(i*3 + 0.0001))`) reads exactly one tier lower; at tier 1
   (heat 0.2) assert heat halves.
4. `CampTest_GroundedLoopFallsOutOfTopTiers` — simulate: kill(Grounded) then
   Tick(4s of 0.05 steps), repeated 5x; assert Intensity never exceeds 0.34
   after the first kill's decay begins and the tier mapping never reaches 3.
5. `ResetHeat_ClearsStaleness` — 2 grounded kills, ResetHeat, 1 grounded
   kill: gain is full 0.33 (streak cleared).
6. `HitTrickle_RateLimited` — 16 RegisterHit calls with no Tick between
   (the shotgun pellet frame): Intensity == 0.08 exactly; after Tick(0.7)
   plus decay accounting, one more RegisterHit adds again. Also assert the
   sustained max rate stays under decay: 10 cycles of [RegisterHit +
   Tick(0.7)] never exceed Intensity 0.1.

**Gate:** run_tests. Runner-truth arithmetic (the NUnit runner reported
total=134 on the last green run — trust the runner, not a static grep):
134 − 1 (Events_HeatInOrder deleted) + 6 (new MusicCore tests) + 1 (new
MovementCore IsGrounded test) = **expect exactly 140 passed**. Any other
total means an unenumerated breakage — stop and report, do not "fix" tests
to make the number.

### Task 2: wiring — MusicManager + movement state read

**Files:** Modify `Assets/AlienSkies/Scripts/MusicManager.cs` (Bind only).

```csharp
foreach (var pc in FindObjectsByType<AlienSkies.Combat.PlayerCombat>(FindObjectsSortMode.None))
{
    if (!pc.IsLocal) continue;
    _bound = pc;
    var motor = pc.GetComponent<AlienSkies.Movement.PlayerMotor>();
    // Guardrail 7: being hit COSTS a tier (was RegisterDamage — the audit's
    // "getting hit raises your multiplier" inversion). SELF-SPLASH IS EXEMPT:
    // rocket jumps are the first-class movement verb (guardrail 12) — the
    // health cost is their price, the multiplier must not double-charge.
    // LastDamageSource is set by TakeDamage before OnDamaged fires.
    // MaxMultiplier comes from the live run settings so the band math
    // matches scoring exactly.
    pc.OnDamaged += _ =>
    {
        if (pc.LastDamageSource == AlienSkies.Combat.DamageSourceKind.SelfSplash) return;
        Core.DropTier(AlienSkies.Run.RunManager.Instance != null
            ? AlienSkies.Run.RunManager.Instance.MaxMultiplier : 4);
    };
    // Guardrail 6: kills pay by movement state; non-kill hits are a
    // rate-limited trickle (MusicCore.RegisterHit's own cooldown caps ALL
    // weapons at ~0.114/s, under the 0.12/s decay — no weapon can hold a
    // tier by plinking, shotgun pellet bursts collapse to one add).
    // Bare firing pays nothing.
    pc.OnHitConfirmed += (dmg, killed) =>
    {
        if (!killed) { Core.RegisterHit(); return; }
        var st = MusicCore.KillMoveState.Grounded;
        if (motor != null)
        {
            var mc = motor.Core;
            // IsGrounded is the real CharacterController signal (Task 1);
            // SurfaceContactRemaining ORs in planet-coyote contact —
            // platforms never arm it (PlanetSurface-gated), planets do.
            st = (mc.IsGrappling || mc.IsZipping) ? MusicCore.KillMoveState.Flow
               : (mc.IsGrounded || mc.SurfaceContactRemaining > 0f)
                   ? MusicCore.KillMoveState.Grounded
                   : MusicCore.KillMoveState.Airborne;
        }
        Core.RegisterKill(st);
    };
    break;
}
```

The `pc.OnFired += ...` line is DELETED (guardrail 6: never heat from firing).
Notes: the state is read at kill-CONFIRM time (rocket impact), matching the
guardrail's "movement state at kill time". Grounded includes the planet
coyote window — conservative: the guardrail punishes ground camping and must
not reward bunny-trigger timing.

**Files:** Modify `Assets/AlienSkies/Scripts/RunManager.cs` — add
`public int MaxMultiplier => S.MaxMultiplier;` next to the other S-forwarding
properties (read the file; if RunSettings' field is named differently, follow
the actual name).

**Gate:** full test run passes at the Task 1 total; grep the repo for
`RegisterFire|RegisterDamage` — zero remaining references outside git history.

### Task 3: docs

`docs/ARCHITECTURE.md` §3 music/heat paragraph rewritten to the new contract
(kill-state table, tier-drop, trickle hits, no fire heat) and
`docs/DESIGN-GUARDRAILS.md` scorecard rows 6 and 7 updated:
6 → PARTIAL (movement-keyed + camp-safe + no victim pump; milk/pace halves
still need playtest), 7 → MEETS (pending playtest confirmation).
Same-commit rule applies.

**Gate:** none (docs), rides the final commit.
