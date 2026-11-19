# Cross-Cut Invariant — guardrail 11 Implementation Plan (REVISION 2)

> REVISION 2 folds an 11-finding adversarial review (6 blockers): climb-aware
> time model + floor 4s (the 8s floor was triangle-inequality-unreachable on
> every span — verifier proved saving <= D/20 - 1.5s), real helper signatures
> (CreateWorld needs a lookRng — solved with a fresh cosmetic-only
> Random(worldIndex), course jitter streams stay byte-identical),
> Validated bool over the Vector3.zero sentinel, CLIMB anchor on -x.

> **For agentic workers:** execute task-by-task. Spec = `docs/DESIGN-GUARDRAILS.md`
> guardrail 11. Facts base: CourseBuilder cursor contract (c.P = far edge of
> last footing), AssertBudget logs + `Exit(1)` in batchmode only, per-course
> `System.Random` streams are consumed IN CALL ORDER (inserting an rng draw
> shifts all later jitter), capability constants are hand-derived and
> hardcoded (not read from MovementSettings).

**Goal:** every course declares one authored cross-cut (granted verbs only,
analytic time saving above a floor), and the generator REFUSES a course
without a valid one — same enforcement class as AssertBudget.

**Key decisions (why):**
- **Authored, not emergent.** Planet_24 is backdrop-RNG geometry referenced
  only in comments; a validated cut must be an object the course itself
  places. THE CROSSING gets its own authored anchor; the Planet_24 prose
  stays as flavor.
- **No RNG in cross-cut placement.** Begin/End draw nothing from the course
  rng — authored offsets only — so existing jitter streams (and every
  budget check that depends on them) are byte-identical after this change.
- **Analytic time model, climb-aware** (precedent: the capability table).
  mainSeconds = (dist(From,To) + 2*max(0, To.y - From.y)) / MainPathSpeed —
  the main path pays double for elevation because launch arcs are mostly
  vertical time; descents pay nothing (a dive course is already fast, which
  is WHY its cut saves less). MainPathSpeed 12 m/s, CutSpeed 40 m/s (real
  grapple flings run ~80 per the ZipSpeed tuning comment; 40 is half),
  CutOverhead 1.5 s. Floor: MinCrossCutSeconds 4 — a 4-12 s save on 60-90 s
  pars is real medal-delta scale. Review-proven bound: with a single
  intermediate anchor, saving <= D/20 - 1.5 under the old constants — the
  old 8 s floor was unreachable on every real span.
- **Scoring honesty (recorded, not fixed here):** the guardrail's literal
  "changes a grade tier" is unreachable at current constants — tier width =
  0.25 × STargetScore = 500 pts = 50 s at TimeBonusPerSecond 10. A 4 s
  floor stands in; the mismatch goes to the Wave-2 grades design chat
  (either time bonus is under-weighted or tier width over-wide).

## Global constraints
- No changes to segment geometry, rng call order, worldIndexStart values of
  existing calls, or the Turrets index lookups.
- No new unit tests: CourseBuilder validation's test IS the batchmode regen
  gate (AssertBudget precedent — the suite stays at 140).
- Cross-cut anchors use the existing internal helpers (CreateWorld +
  CreateSegmentRing pip) — zero new visual language (that is art-session
  territory; these are ordinary grapple worlds in the established style).

---

### Task 1: CourseBuilder — CrossCutDef + Begin/End + AssertCrossCut

**Files:** Modify `Assets/AlienSkies/Editor/CourseBuilder.cs` (read fully first).

Add to CourseResult:
```csharp
public CrossCutDef CrossCut; // exactly one per course (guardrail 11)
```

New types/consts/methods (file-level, near the other asserts):
```csharp
public sealed class CrossCutDef
{
    public string Name;
    public Vector3 From;      // main-path footing where the cut is taken
    public Vector3 Anchor;    // the authored grapple world's center
    public Vector3 To;        // main-path footing where the cut rejoins
    public string Verbs;      // e.g. "grapple+launch" — granted on this course
    public bool Validated;    // set by EndCrossCut after its asserts ran
}

// Analytic-time constants — hand-derived like the capability table, NOT read
// from MovementSettings (that file's numbers aren't visible to the editor
// assembly by design; see ShrinkForRise's comment). Conservative on purpose.
const float MainPathSpeed = 12f;      // blended run/launch traversal m/s
const float CutSpeed = 40f;           // grapple-fling blend (real ~80; halved)
const float CutOverheadSeconds = 1.5f;// aim + attach + release
const float MinCrossCutSeconds = 4f;  // floor: medal-delta scale on 60-90s pars
const float ClimbCostFactor = 2f;     // main path pays double for elevation
const float MaxAnchorGrapple = 100f;  // < GrappleRange 120: attach must be easy
const float MaxAnchorRise = 60f;      // < the 72m swing budget: reachable swing

/// <summary>Places the authored cross-cut anchor (a small grapple world +
/// violet ring pip, the GrappleGap visual grammar) and records the take-off
/// point. Draws NOTHING from any rng — placement is authored — so existing
/// jitter streams are untouched. Call BEFORE the span the cut bypasses.</summary>
public static void BeginCrossCut(string course, ref Cursor c, CourseResult result,
    Vector3 anchorOffset, string verbs, int worldIndex)
{
    Vector3 anchor = c.P + c.H * anchorOffset.z + Vector3.up * anchorOffset.y
                   + c.Right * anchorOffset.x;
    // Fresh cosmetic-only rng: CreateWorld's lookRng picks hue/banding/spin
    // (4 draws). Using the course stream here would shift every later
    // segment's jitter — the exact landmine this file warns about — so the
    // anchor gets its own deterministic look stream instead.
    var lookRng = new System.Random(worldIndex);
    PlaygroundBuilder.CreateWorld(anchor, 8f, worldIndex, lookRng);
    // Same violet-pip grammar as GrappleGap's anchors (copy that call shape).
    PlaygroundBuilder.CreateSegmentRing($"{course}_CrossCut_pip", anchor,
        5f, Quaternion.Euler(90f, 0f, 0f), 12, new Vector3(0.4f, 0.2f, 1.0f),
        VioletRingMat(), false);
    result.CrossCut = new CrossCutDef
        { Name = $"{course}_CrossCut", From = c.P, Anchor = anchor, Verbs = verbs };
}

/// <summary>Records the rejoin point and VALIDATES the cut. Call AFTER the
/// bypassed span. Uses AssertBudget so a bad cut fails the batchmode build
/// exactly like a bad gap (guardrail 11: validated by the generator, not
/// hoped for).</summary>
public static void EndCrossCut(string course, ref Cursor c, CourseResult result)
{
    if (result.CrossCut == null)
    {
        Debug.LogError($"CourseBuilder[{course}]: EndCrossCut without BeginCrossCut");
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(1);
        return;
    }
    var cut = result.CrossCut;
    cut.To = c.P;

    float climb = Mathf.Max(0f, cut.To.y - cut.From.y);
    float mainSeconds = (Vector3.Distance(cut.From, cut.To)
                       + ClimbCostFactor * climb) / MainPathSpeed;
    float cutSeconds = (Vector3.Distance(cut.From, cut.Anchor)
                      + Vector3.Distance(cut.Anchor, cut.To)) / CutSpeed
                      + CutOverheadSeconds;
    float saving = mainSeconds - cutSeconds;

    // All three via the existing primitive: value <= budget or Exit(1).
    AssertBudget(course, "cross-cut attach distance",
        Vector3.Distance(cut.From, cut.Anchor), MaxAnchorGrapple);
    AssertBudget(course, "cross-cut anchor rise",
        cut.Anchor.y - cut.From.y, MaxAnchorRise);
    AssertBudget(course, "cross-cut MIN saving shortfall (must be <= 0)",
        MinCrossCutSeconds - saving, 0f);
    cut.Validated = true;
    Debug.Log($"CourseBuilder[{course}]: cross-cut '{cut.Name}' saves ~{saving:0.0}s "
            + $"(main ~{mainSeconds:0.0}s vs cut ~{cutSeconds:0.0}s, verbs {cut.Verbs})");
}

/// <summary>Course-completeness half of guardrail 11: a course with no
/// declared cross-cut is an invalid layout. Call at every course's end,
/// beside AssertCourseBounds.</summary>
public static void AssertCrossCutDeclared(string course, CourseResult result)
{
    if (result.CrossCut != null && result.CrossCut.Validated) return;
    Debug.LogError($"CourseBuilder[{course}]: no validated cross-cut declared");
    if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(1);
}
```
Adjust to the file's real material-helper name for the violet ring (read the
existing GrappleGap pip call and copy its exact form; if CreateWorld's real
signature differs — e.g. it takes a diameter or extra args — follow the real
signature; the scout notes anchors in GrappleGap are d8 worlds).

**Gate:** compiles (checked at Task 3's regen); no test-count change.

### Task 2: wire the three courses (PlaygroundBuilder.cs course region)

Read the course region (~lines 371–524) first. Insert Begin/End pairs —
byte-zero impact on rng draws — and the declared-check beside each
AssertCourseBounds. worldIndex values: 120 (CROSSING), 250 (CLIMB), 330
(PLUNGE) — clear of every existing hand-picked index.

- **THE CROSSING** (heading +X): Begin BEFORE `LaunchWeave(4, ...)` with
  `anchorOffset = new Vector3(-14f, 34f, 22f)`, verbs "grapple+launch"; End
  AFTER `PlatformRun(2)` (the last segment before GateFinish — the cut
  bypasses weave + pocket + run: swing over the weave, kick past the pocket,
  trading its kill points for time, the guardrail's intended tension).
  Review-computed expectation: From ~(235.9, 6.5, 40.0), Anchor
  ~(257.9, 40.5, 54.0), To ~(352.6, 14.3, 40.9) → attach 42.9, rise 34.0,
  main ~11.1s, cut ~5.1s, **saving ~6.0s** (floor 4).
- **THE CLIMB** (heading +Z): Begin BEFORE `LaunchChain(3, ...)` with
  `anchorOffset = new Vector3(-10f, 42f, 20f)` (x NEGATIVE — the course's
  own convention pins lateral margin to the −x side, away from the Ring_1
  perch), verbs "grapple"; End AFTER `LaunchWeave(4, climbPerRing:4f, ...)`.
  Expectation: From ~(-43.5, 65.3, 216.4), Anchor ~(-53.5, 107.3, 236.4),
  To ~(-46.5, 114.9, 304.4) → attach 47.6, rise 42.0, main ~16.7s (the +49.6
  climb pays double), cut ~4.4s, **saving ~12.3s**.
- **THE PLUNGE** (heading −X): Begin BEFORE `GrappleGap(2, 80, ...)` with
  `anchorOffset = new Vector3(0f, 38f, 30f)`, verbs "grapple"; End AFTER
  `PlatformRun(3)` (extended span — a dive course's main path is already
  fast, so the cut must bypass more to matter). Expectation: From
  ~(-115.5, 70, -60), Anchor ~(-145.5, 108, -60), To ~(-280, 21, -60) →
  attach 48.4, rise 38.0, main ~14.3s (descent: no climb term), cut ~6.7s,
  **saving ~7.6s**.
- Add `CourseBuilder.AssertCrossCutDeclared("courseN", courseN);` beside each
  existing `AssertCourseBounds` call (three sites).

The implementer MUST log the regen's actual printed savings and compare to
these expectations in the task report — a large mismatch (>2s) means the
cursor arithmetic diverged from the review's simulation and needs a stop-
and-report, not silent offset tuning.

If an assert still fires at regen despite the review-computed margins
(attach > 100, rise > 60, saving < 4), STOP and report the actual vs
expected numbers — do not tune offsets or constants silently.

**Gate:** full cycle — regen MUST exit 0 (the new asserts run against real
geometry; a violation kills the batchmode build by design), tests 140,
player build.

### Task 3: docs (same commit)

- `docs/ARCHITECTURE.md` courses paragraph: cross-cut contract (authored
  anchor, Begin/End, analytic floor 8s, Exit(1) enforcement).
- `docs/DESIGN-GUARDRAILS.md` row 11 → PARTIAL: "authored + generator-
  validated cuts on all three courses; visible-from-path needs the owner's
  editor flythrough; tier-change literal test blocked by scoring constants
  (see Wave 2)". Add one line to §Iteration plan Wave 2's grades item:
  "includes the tier-width/time-bonus mismatch the cross-cut floor exposed
  (500 pts = 50 s at current constants)".
- This plan doc: no edits needed (it ships as-is in the commit).
