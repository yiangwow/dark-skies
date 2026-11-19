using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AlienSkies.EditorTools
{
    /// <summary>Parametric course generator (courses plan, 2026-08-29, Task 1):
    /// walks a Cursor through typed segments to lay out a directional lane.
    /// SELF-ENFORCING: every gap/climb a segment emits is checked against the
    /// review-computed CAPABILITY BUDGET table (see the plan) via AssertBudget,
    /// which kills the batchmode build (Exit(1)) on violation — impossible
    /// geometry cannot be silently built.
    ///
    /// CURSOR CONTRACT (review blocker fix — seams were un-audited voids):
    /// Cursor.P is ALWAYS the far edge of the segment's last-placed footing,
    /// at surface height. Every segment places its FIRST footing within its
    /// verb's capability budget of the INCOMING c.P — never past it just
    /// because a previous segment "left room". H/Right are the course's fixed
    /// heading frame; none of the three pinned courses turn, so a segment
    /// never rotates them — Right exists only for lateral jitter and the
    /// corridor bookkeeping in Step 3.
    ///
    /// Reuses PlaygroundBuilder's exact primitive builders (CreatePlatform,
    /// CreateDisc, CreateWorld, CreateSegmentRing, CreateSpire, BuildGate,
    /// NeonMaterial, GetOrCreateMaterial — widened to `internal` for this)
    /// so course geometry looks and behaves identically to the hand-authored
    /// arena instead of drifting into a second visual language.</summary>
    public static class CourseBuilder
    {
        // ------------------------------------------------------------------
        // Step 1: Spine — Cursor, CourseResult, the enforcement helper.
        // ------------------------------------------------------------------

        /// <summary>See the CURSOR CONTRACT above. P/H/Right form a fixed local
        /// frame: H is the course heading (unit, may include a vertical
        /// component for a climbing/diving course), Right is horizontal and
        /// perpendicular to H, used only for lateral jitter/corridor math.</summary>
        public struct Cursor
        {
            public Vector3 P;
            public Vector3 H;
            public Vector3 Right;
        }

        /// <summary>What a finished course hands back to PlaygroundBuilder for
        /// LevelDef wiring: player start, the finish gate, and every combat
        /// waypoint segments dropped along the way. Posts stay stationary;
        /// Orbiters patrol — the same distinction EnemySpawner/LevelDef
        /// already use (see LevelDef.Posts/Orbiters, both Vector3[] — call
        /// ToArray() on these when assigning).</summary>
        public class CourseResult
        {
            public Vector3 Start;
            public GameObject Gate;
            public readonly List<Vector3> Posts = new List<Vector3>();
            public readonly List<Vector3> Orbiters = new List<Vector3>();
            /// <summary>One entry per CombatPocket segment: that pocket's
            /// platform center, at surfaceY (Task 3 placement source of
            /// truth — the builder wires LevelDef.Turrets from these).</summary>
            public readonly List<Vector3> Turrets = new List<Vector3>();
            public CrossCutDef CrossCut; // exactly one per course (guardrail 11)
        }

        /// <summary>Builds a Cursor at a course's pinned origin/heading (Pinned
        /// geometry table). Right = horizontal, perpendicular to H, via
        /// cross(up, H) — e.g. heading +X gives Right = -Z; heading +Z gives
        /// Right = +X (THE CLIMB's "-x side" lateral pin means a caller passes
        /// a NEGATIVE lateral value against this Right).</summary>
        public static Cursor NewCursor(Vector3 origin, Vector3 heading)
        {
            Vector3 h = heading.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, h).normalized;
            return new Cursor { P = origin, H = h, Right = right };
        }

        /// <summary>The enforcement helper every segment calls (pinned verbatim
        /// from the plan). Batchmode-only Exit(1): an interactive editor run
        /// just logs the error so a designer can see and fix it in-scene.</summary>
        static void AssertBudget(string course, string what, float value, float budget)
        {
            if (value <= budget) return;
            Debug.LogError($"CourseBuilder[{course}]: {what} = {value:0.0} exceeds budget {budget:0.0}");
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(1);
        }

        // Shared corridor half-width for Step 3's course-level assert (Global
        // Constraints / pinned geometry: |lateral| <= 25 for every course).
        const float CorridorHalfWidth = 25f;

        /// <summary>Step 3: course-level asserts — call once after a course's
        /// last segment. Checks total advance along H against the course's own
        /// pinned cap (THE CROSSING's 330 m, etc.) and the cursor's accumulated
        /// lateral drift against the shared 25 m corridor half-width. Both
        /// routed through AssertBudget, so either violation kills a batchmode
        /// build exactly like a per-segment one would.</summary>
        public static void AssertCourseBounds(string course, Vector3 origin, Cursor c, float advanceCap)
        {
            Vector3 delta = c.P - origin;
            float advance = Vector3.Dot(delta, c.H);
            float lateral = Vector3.Dot(delta, c.Right);
            AssertBudget(course, "total advance", advance, advanceCap);
            AssertBudget(course, "corridor |lateral|", Mathf.Abs(lateral), CorridorHalfWidth);
        }

        /// <summary>Courses verify round: a course-level elevation-band check
        /// for climbing courses (THE CLIMB's own pinned 110-130 m total-gain
        /// window), called once after a course's last segment — same
        /// AssertBudget enforcement as every other check in this file, just
        /// expressed as two one-sided bounds since AssertBudget itself only
        /// checks "value &lt;= budget".</summary>
        public static void AssertFinishElevation(string course, Cursor c, float minY, float maxY)
        {
            AssertBudget(course, "finish elevation (above band)", c.P.y, maxY);
            AssertBudget(course, "finish elevation (below band)", minY - c.P.y, 0f);
        }

        // ------------------------------------------------------------------
        // Cross-cut invariant (guardrail 11, crosscuts plan REVISION 2): every
        // course declares one authored, generator-validated cross-cut. See the
        // plan for the analytic time model and the review-proven saving bound.
        // ------------------------------------------------------------------

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

        // ------------------------------------------------------------------
        // Shared look: reuse PlaygroundBuilder's exact material recipes/names
        // so course geometry is asset-identical to the hand-authored arena
        // (GetOrCreateMaterial loads the existing asset if BuildArena already
        // ran first, and re-applying the same values is idempotent otherwise).
        // ------------------------------------------------------------------

        static Material PlatformBodyMat() =>
            PlaygroundBuilder.NeonMaterial("NeonPlatform", AlienSkies.Palette.Cyan, 1.1f, 0.55f);

        static Material PlatformTrimMat()
        {
            var m = PlaygroundBuilder.GetOrCreateMaterial("NeonTrim", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            m.SetColor("_BaseColor", AlienSkies.Palette.Cyan * 2.4f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material DiscEdgeMat()
        {
            var m = PlaygroundBuilder.GetOrCreateMaterial("RingCyan", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            m.SetColor("_BaseColor", AlienSkies.Palette.Cyan * 1.9f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material AmberRingMat()
        {
            var m = PlaygroundBuilder.GetOrCreateMaterial("RingAmber", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            m.SetColor("_BaseColor", AlienSkies.Palette.Amber * 2.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material VioletRingMat()
        {
            var m = PlaygroundBuilder.GetOrCreateMaterial("RingViolet", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            m.SetColor("_BaseColor", AlienSkies.Palette.Violet * 1.9f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Amber pip marking a segment's exit point (Step 2 preamble:
        /// "drops an amber breadcrumb pip at exit"). Wayfinding only — no
        /// collider, tiny, never a footing.</summary>
        static void DropBreadcrumb(string course, Vector3 exitP)
        {
            var pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pip.name = $"{course}_Breadcrumb";
            pip.transform.position = exitP + Vector3.up * 0.3f;
            pip.transform.localScale = Vector3.one * 0.5f;
            pip.GetComponent<Renderer>().sharedMaterial = AmberRingMat();
            Object.DestroyImmediate(pip.GetComponent<Collider>());
        }

        // ------------------------------------------------------------------
        // Step 2: Segments.
        // ------------------------------------------------------------------

        /// <summary>First footing of a course: a 12x12 platform CENTERED on the
        /// course's own pinned origin (the origin is already given at surface
        /// height in the Pinned Geometry table, so there is no "incoming
        /// footing" to budget against here). Player spawns 1.5 m above the
        /// surface — same settle-on-landing margin the arena's own spawns use.</summary>
        public static void StartPad(string course, ref Cursor c, CourseResult result)
        {
            const float size = 12f, thickness = 1f;
            Vector3 topCenter = c.P;
            var bodyMat = PlatformBodyMat();
            var trimMat = PlatformTrimMat();

            PlaygroundBuilder.CreatePlatform($"{course}_StartPad", topCenter - Vector3.up * thickness * 0.5f,
                new Vector3(size, thickness, size), bodyMat, trimMat);

            result.Start = topCenter + Vector3.up * 1.5f;
            c.P = topCenter + c.H * (size * 0.5f);
            DropBreadcrumb(course, c.P);
        }

        /// <summary>Approximates the CAPABILITY BUDGET table's "Gap landing
        /// HIGHER by dY" row. The table's own formula (0.7 x 10 x t_desc(dY))
        /// needs the real jump-arc physics (initial vertical speed, gravity)
        /// from MovementSettings to evaluate t_desc, which is not pinned
        /// anywhere this generator can read it from. The table DOES give two
        /// concrete points — flat (dY=0, physical ceiling 11.25 m, budget 8 m)
        /// and +2 m (physical ceiling 9.4 m, so budget = 0.7*9.4 = 6.58 m) — so
        /// this linearly interpolates/extrapolates the budget between them.
        /// DEVIATION (flagged in the task report): none of the three pinned
        /// courses rises inside a PlatformRun, so this is unexercised today;
        /// it exists so the segment's contract is complete per Step 2's text.</summary>
        static float ShrinkForRise(float dY)
        {
            const float flatBudget = 8f;
            const float dyAnchor = 2f;
            const float budgetAtAnchor = 0.7f * 9.4f; // 6.58
            float t = dY / dyAnchor;
            return Mathf.Max(0f, flatBudget + (budgetAtAnchor - flatBudget) * t);
        }

        /// <summary>Chain of `count` discs (d10) crossing gaps derived from the
        /// flat-jump budget (8 m) minus whatever lateral jitter is drawn —
        /// review fix: a flat 7.5 m alongH with +/-3 m lateral produced an
        /// 8.08 m diagonal over budget, so alongH is SOLVED from the lateral
        /// draw instead of picked independently (alongH = sqrt(budget^2 -
        /// lateral^2)). The first disc uses a tighter 7.5 m entry budget
        /// (spec-pinned) in the same formula; every other pair uses the flat
        /// 8 m budget, shrunk by ShrinkForRise when `riseTotal` is nonzero.
        /// Deterministic per-course jitter: `rng` is the caller's seeded
        /// stream, so a fixed seed reproduces the exact same lane. "Near
        /// edge"/"far edge" are approximated as center -/+ radius along H,
        /// the same simplification every segment in this file uses for round
        /// footings.</summary>
        public static void PlatformRun(string course, ref Cursor c, CourseResult result, System.Random rng,
            int count, float riseTotal = 0f)
        {
            const float radius = 5f, thickness = 0.8f; // d10
            const float flatBudget = 8f;
            const float firstBudget = 7.5f; // spec-pinned first-disc entry
            const float lateralRange = 3f; // the review's own worked example

            var bodyMat = PlatformBodyMat();
            var edgeMat = DiscEdgeMat();
            float riseEach = count > 0 ? riseTotal / count : 0f;

            for (int i = 0; i < count; i++)
            {
                float lateral = ((float)rng.NextDouble() * 2f - 1f) * lateralRange;
                float budget = i == 0 ? firstBudget : (riseEach > 0.01f ? ShrinkForRise(riseEach) : flatBudget);
                // Float-precision guard (Task 3 rebuild — geometry gate fired
                // here too, course3's first disc): alongH is solved so
                // alongH^2 + lateral^2 == budget^2 exactly in real math, but
                // the sqrt/square/sqrt round-trip below can recombine to a
                // diagonal a hair over budget in float (observed: "PlatformRun
                // first-disc near edge = 7.5 exceeds budget 7.5"), depending on
                // the RNG-drawn lateral's bit pattern — not a real geometry
                // violation. Solve against a target 1 mm under budget so the
                // float diagonal is provably <= the real budget regardless of
                // rounding noise; AssertBudget still checks against the real
                // (unshrunk) budget.
                const float precisionGuard = 0.001f;
                float target = budget - precisionGuard;
                float alongH = Mathf.Sqrt(Mathf.Max(0f, target * target - lateral * lateral));
                float diagonal = Mathf.Sqrt(alongH * alongH + lateral * lateral);
                string what = i == 0 ? "PlatformRun first-disc near edge" : $"PlatformRun[{i}] diagonal";
                AssertBudget(course, what, diagonal, budget);

                Vector3 center = c.P + c.H * (alongH + radius) + c.Right * lateral + Vector3.up * riseEach;
                PlaygroundBuilder.CreateDisc($"{course}_Run_{i:00}", center, radius, thickness, bodyMat, edgeMat);
                c.P = center + c.H * radius;
            }
            DropBreadcrumb(course, c.P);
        }

        /// <summary>Floorless void crossed by grapple swings: `anchors` NeonPlanet
        /// spheres (d8, colliders — GrappleAimer carries no layer mask, so any
        /// collider is grapple-able, review-verified) hover +18 above an
        /// interpolated baseline running from the incoming edge to the catch's
        /// near edge, spaced evenly across `length`. Every consecutive swing
        /// (including the takeoff-edge-to-first-anchor swing) is asserted
        /// against the 72 m swing budget. The catch platform's near edge sits
        /// exactly `length` out; its horizontal distance PAST the last
        /// anchor's plumb line is asserted against the 60 m cap rather than
        /// assumed. `elevationGain` (optional) lifts the far end by that much,
        /// linearly distributed across the anchors and the catch platform —
        /// THE CLIMB's "+15 total elevation" GrappleGap uses this. Anchors are
        /// ALWAYS freshly built here (never reused) — the plan's Pinned
        /// Geometry section deleted the "reuse hub anchors" idea as
        /// geometrically false, so there is no reuse code path to accidentally
        /// call. `worldIndexStart` only affects CreateWorld's cosmetic
        /// name/hue index, so callers can keep planet names unique scene-wide.</summary>
        public static void GrappleGap(string course, ref Cursor c, CourseResult result, System.Random rng,
            int anchors, float length, float elevationGain = 0f, int worldIndexStart = 0)
        {
            const float anchorDiameter = 8f;
            const float anchorHover = 18f;
            const float swingBudget = 72f;
            const float catchNearCap = 60f;
            const float catchSize = 16f;

            Vector3 startEdge = c.P;
            Vector3 catchNear = startEdge + c.H * length + Vector3.up * elevationGain;

            Vector3 prevSwingPoint = startEdge; // first swing is measured from the takeoff edge
            Vector3 lastAnchorHoriz = startEdge;
            for (int i = 0; i < anchors; i++)
            {
                float t = (i + 1f) / (anchors + 1f); // interior spacing between start and catch-near
                Vector3 baseline = Vector3.Lerp(startEdge, catchNear, t);
                Vector3 anchorPos = baseline + Vector3.up * anchorHover;

                float swing = Vector3.Distance(prevSwingPoint, anchorPos);
                AssertBudget(course, $"GrappleGap[{i}] swing", swing, swingBudget);

                PlaygroundBuilder.CreateWorld(anchorPos, anchorDiameter, worldIndexStart + i, rng);
                // Per-verb color (courses verify round): violet is reserved
                // for grapple, so every grapple anchor gets a small violet
                // pip ring — a wayfinding read distinct from LaunchWeave's
                // cyan thread rings.
                PlaygroundBuilder.CreateSegmentRing($"{course}_GrappleAnchor_{i:00}_Pip", anchorPos,
                    anchorDiameter * 0.5f + 1f, Quaternion.Euler(90f, 0f, 0f), 12,
                    new Vector3(0.4f, 0.2f, 1.0f), VioletRingMat(), false);
                prevSwingPoint = anchorPos;
                lastAnchorHoriz = baseline;
            }

            float catchOffset = Vector3.Distance(
                new Vector3(catchNear.x, 0f, catchNear.z), new Vector3(lastAnchorHoriz.x, 0f, lastAnchorHoriz.z));
            AssertBudget(course, "GrappleGap catch near-edge past last anchor", catchOffset, catchNearCap);

            var bodyMat = PlatformBodyMat();
            var trimMat = PlatformTrimMat();
            Vector3 catchCenter = catchNear + c.H * (catchSize * 0.5f);
            PlaygroundBuilder.CreatePlatform($"{course}_GrappleCatch", catchCenter - Vector3.up * 0.5f,
                new Vector3(catchSize, 1f, catchSize), bodyMat, trimMat);

            c.P = catchNear + c.H * catchSize; // far edge, surface height
            DropBreadcrumb(course, c.P);
        }

        /// <summary>Chain of PlanetSurface spheres (d12, r=6) launched off with
        /// SPACE: spacing 14 m along H, +14 elevation per pad after the first
        /// (rows: hop <=15 horizontal, climb <=14.8 vertical — both asserted
        /// as insurance even though 14 is a fixed constant, so a future edit
        /// to `spacing`/`elevPerPad` alone can't silently exceed the budget).
        /// ENTRY RULE (round-3 review — sphere geometry): a naive "lowest
        /// surface <=+1.8" first pad puts every reachable contact >=35 degrees
        /// below the equator with the kick aimed down-and-back, so instead the
        /// entry targets the pad's TOP (apex = center + radius) at <=+1.8 m
        /// above incoming c.P, with its near edge (approximated as
        /// centerHoriz - radius along H) at <=6 m out — inside the ~7.8 m
        /// standable-cap band instead of the ~9.8 m band an 8 m entry would
        /// land in. Cursor exits at the LAST pad's TOP (sphere-center +
        /// radius, NOT "surface" generally — the sphere-vs-surface bookkeeping
        /// the plan calls out explicitly).
        /// `lateralJitterRange` (optional, added beyond Step 2's literal text):
        /// THE CLIMB's course spec pins LaunchChain's jitter to the -x side
        /// for margin against a nearby perch disc, and Task 2 does not list
        /// this file among its edits — so the hook has to exist here. Sign
        /// pins direction (pass negative for -Right), magnitude per pad is
        /// drawn from `rng` in [0, |range|]; 0 (default) means no jitter.</summary>
        public static void LaunchChain(string course, ref Cursor c, CourseResult result, System.Random rng,
            int pads, int worldIndexStart = 0, float lateralJitterRange = 0f)
        {
            const float radius = 6f;      // d12
            const float spacing = 14f;    // along H, pad-center to pad-center
            const float elevPerPad = 14f; // hop/climb rows: 15 / 14.8
            const float entryRise = 1.8f;
            const float entryNear = 6f;
            // Float-precision guard (Task 3 rebuild — geometry gate fired here
            // first): entryRise isn't exactly representable in binary, and it
            // round-trips through incoming.y (large by this point in a course)
            // via "(entryRise - radius) then + radius" before AssertBudget
            // compares it — that chain can land a hair on the wrong side of
            // the exact 1.8 m boundary depending on incoming.y's bit pattern
            // (observed: 1.8000002 m > 1.8f), a rounding-direction gamble, not
            // a real geometry violation. placementRise nudges the ACTUAL pad
            // 1 mm under the rule's ceiling (imperceptible in play, ~100x any
            // float error at these magnitudes) so the value AssertBudget sees
            // is provably <= budget; AssertBudget and the 1.8 m rule itself
            // are untouched.
            const float precisionGuard = 0.001f;
            float placementRise = entryRise - precisionGuard;

            Vector3 incoming = c.P;
            Vector3 center = incoming + c.H * (entryNear + radius) + Vector3.up * (placementRise - radius);
            Vector3 top = center + Vector3.up * radius;
            AssertBudget(course, "LaunchChain first-pad TOP height", top.y - incoming.y, entryRise);
            float actualNear = Vector3.Distance(
                new Vector3(incoming.x, 0f, incoming.z), new Vector3(center.x, 0f, center.z)) - radius;
            AssertBudget(course, "LaunchChain first-pad near edge", actualNear, entryNear);
            AssertBudget(course, "LaunchChain hop spacing", spacing, 15f);
            AssertBudget(course, "LaunchChain hop elevation", elevPerPad, 14.8f);

            PlaygroundBuilder.CreateWorld(center, radius * 2f, worldIndexStart, rng);

            for (int p = 1; p < pads; p++)
            {
                float lateral = lateralJitterRange == 0f ? 0f : (float)rng.NextDouble() * lateralJitterRange;
                center = center + c.H * spacing + Vector3.up * elevPerPad + c.Right * lateral;
                PlaygroundBuilder.CreateWorld(center, radius * 2f, worldIndexStart + p, rng);
            }

            c.P = center + Vector3.up * radius; // last pad's TOP
            DropBreadcrumb(course, c.P);
        }

        /// <summary>Rings-of-light slalom — replaces the review-killed empty
        /// RingSlalom (colliders:false rings thread nothing on their own,
        /// which stranded THE CLIMB): a d6 PlanetSurface planet under EVERY
        /// ring does the actual launching; the d10 ring is a collider-free
        /// visual thread hovering just above its planet. Planets step +13 m
        /// along H and +2 m up each (asserted against the hop/climb rows,
        /// 15/14.8, as insurance). `climbPerRing` adds an EXTRA per-planet
        /// rise on top of the base +2 — the rings just track their planet's
        /// position, so extra climb reaches the course THROUGH the planets,
        /// never an independent ring formula (per Step 2's text). ENTRY: the
        /// same "hop onto the cap" rule as LaunchChain's first pad (that
        /// segment's own doc references this one by name) — TOP <=+1.8 m
        /// above incoming c.P, near edge <=6 m out, both asserted exactly as
        /// LaunchChain does. Per-verb color (courses verify round): the
        /// thread rings use the CYAN ring recipe (DiscEdgeMat/RingCyan) —
        /// violet is reserved for GrappleGap's anchor pips.</summary>
        public static void LaunchWeave(string course, ref Cursor c, CourseResult result, System.Random rng,
            int rings, float climbPerRing = 0f, int worldIndexStart = 0)
        {
            const float radius = 3f;     // d6 planet
            const float ringRadius = 5f; // d10 visual ring
            const float spacing = 13f;
            const float baseRise = 2f;
            const float entryRise = 1.8f;
            const float entryNear = 6f;
            // Float-precision guard — same issue as LaunchChain's identical
            // pattern (see its comment): entryRise round-trips through
            // incoming.y via "(entryRise - radius) then + radius" and can land
            // a hair over the exact 1.8 m boundary depending on incoming.y's
            // bit pattern. This is where the Task 3 rebuild's geometry gate
            // actually fired (course1's LaunchWeave: 1.8000002 m > 1.8f).
            // placementRise nudges the actual planet 1 mm under the ceiling;
            // AssertBudget and the 1.8 m rule are untouched.
            const float precisionGuard = 0.001f;
            float placementRise = entryRise - precisionGuard;

            // CYAN ring recipe (courses verify round) — violet is reserved
            // for GrappleGap's anchor pips.
            var ringMat = DiscEdgeMat();

            Vector3 incoming = c.P;
            Vector3 center = incoming + c.H * (entryNear + radius) + Vector3.up * (placementRise - radius);
            Vector3 top = center + Vector3.up * radius;
            AssertBudget(course, "LaunchWeave first-planet TOP height", top.y - incoming.y, entryRise);
            // Near-edge assert (courses verify round) — same check LaunchChain
            // already performs on its first pad.
            float actualNear = Vector3.Distance(
                new Vector3(incoming.x, 0f, incoming.z), new Vector3(center.x, 0f, center.z)) - radius;
            AssertBudget(course, "LaunchWeave first-planet near edge", actualNear, entryNear);

            for (int i = 0; i < rings; i++)
            {
                if (i > 0)
                {
                    float rise = baseRise + climbPerRing;
                    AssertBudget(course, $"LaunchWeave[{i}] spacing", spacing, 15f);
                    AssertBudget(course, $"LaunchWeave[{i}] rise", rise, 14.8f);
                    center = center + c.H * spacing + Vector3.up * rise;
                }
                PlaygroundBuilder.CreateWorld(center, radius * 2f, worldIndexStart + i, rng);
                PlaygroundBuilder.CreateSegmentRing($"{course}_Weave_{i:00}", center + Vector3.up * (radius + 2f),
                    ringRadius, Quaternion.Euler(90f, 0f, 0f), 16, new Vector3(0.8f, 0.35f, 2.6f), ringMat, false);
            }

            c.P = center + Vector3.up * radius; // last planet's TOP
            DropBreadcrumb(course, c.P);
        }

        /// <summary>26x26 combat platform whose NEAR edge sits a flat-jump's
        /// 7.5 m past incoming c.P (fixed spec distance, asserted against the
        /// 8 m flat-jump budget as insurance). `postCount` stationary enemy
        /// posts scatter across the platform; when `withOrbiter`, the FIRST
        /// post becomes an orbiter instead of a post (BuildArena's own "stage
        /// is the orbiter, the rest stay posts" pattern — named so a post
        /// array index can never double-count into both roles). A pair of
        /// spire cover pieces (BuildArena's NeonSpire look) breaks sightlines.
        /// Advance = 26 + 7.5, derived not round, per the plan's own text.</summary>
        public static void CombatPocket(string course, ref Cursor c, CourseResult result, System.Random rng,
            int postCount, bool withOrbiter)
        {
            const float nearEdge = 7.5f, size = 26f, thickness = 1f;
            AssertBudget(course, "CombatPocket near edge", nearEdge, 8f);

            var bodyMat = PlatformBodyMat();
            var trimMat = PlatformTrimMat();
            Vector3 near = c.P + c.H * nearEdge;
            Vector3 center = near + c.H * (size * 0.5f);
            PlaygroundBuilder.CreatePlatform($"{course}_CombatPocket", center - Vector3.up * thickness * 0.5f,
                new Vector3(size, thickness, size), bodyMat, trimMat);
            // CreatePlatform's `pos` arg is the box CENTER, and we pass it
            // center-shifted DOWN by thickness*0.5 above — so the box's real
            // center sits at (center.y - thickness*0.5), and its walkable top
            // is real-center.y + thickness*0.5 == center.y exactly (courses
            // verify round: this used to double-count the thickness offset,
            // floating spires above the platform and posts +2.0 instead of
            // the arena's own +1.0 convention).
            float surfaceY = center.y;
            // Turret placement source of truth (Task 3): the pocket's own
            // platform center, at surfaceY exactly — the pedestal is its own
            // footing, no +1.0f post-style offset. Mirrors the Posts pattern
            // below (one entry per CombatPocket call).
            result.Turrets.Add(new Vector3(center.x, surfaceY, center.z));

            var spireMat = PlaygroundBuilder.NeonMaterial("NeonSpire", AlienSkies.Palette.Cyan, 1.5f, 0.6f, 3.2f);
            var tipMat = AmberRingMat();
            PlaygroundBuilder.CreateSpire(new Vector3(center.x - size * 0.25f, surfaceY, center.z - size * 0.2f), 4f, spireMat, tipMat);
            PlaygroundBuilder.CreateSpire(new Vector3(center.x + size * 0.2f, surfaceY, center.z + size * 0.25f), 4f, spireMat, tipMat);

            for (int i = 0; i < postCount; i++)
            {
                float px = center.x + ((float)rng.NextDouble() * 2f - 1f) * (size * 0.5f - 3f);
                float pz = center.z + ((float)rng.NextDouble() * 2f - 1f) * (size * 0.5f - 3f);
                var post = new Vector3(px, surfaceY + 1.0f, pz); // arena convention
                if (i == 0 && withOrbiter) result.Orbiters.Add(post);
                else result.Posts.Add(post);
            }

            c.P = near + c.H * size; // far edge
            DropBreadcrumb(course, c.P);
        }

        /// <summary>Straight-down dive: marker rings trace the fall line, ONE
        /// redirect planet sits ON that line (review: an off-line planet pulls
        /// players away from the catch, so the earlier +/-10 idea was
        /// deleted), and a 16x16 catch platform lands at the bottom.
        /// `lateralOffset` (optional; 0 = catch centered directly under the
        /// start) moves the catch sideways along Right; the table's dive-drift
        /// row (18.5 m standstill drift per 40 m of drop) scales linearly with
        /// `drop`, and the catch's near edge may only use 65% of it — the
        /// self-enforcing check that row exists for. Cursor exits at the
        /// catch's far edge, still advancing along H so total-advance/corridor
        /// bookkeeping stays meaningful through a segment that is mostly
        /// vertical.</summary>
        public static void DiveShaft(string course, ref Cursor c, CourseResult result, System.Random rng,
            float drop, float lateralOffset = 0f, int worldIndexStart = 0)
        {
            const float driftPer40 = 18.5f;
            const float driftFraction = 0.65f;
            const float catchSize = 16f;
            const float ringSpacing = 8f;

            float driftBudget = driftFraction * driftPer40 * (drop / 40f);
            AssertBudget(course, "DiveShaft catch lateral offset", Mathf.Abs(lateralOffset), driftBudget);

            Vector3 top = c.P;
            Vector3 bottom = top + Vector3.down * drop + c.Right * lateralOffset;

            var ringMat = AmberRingMat();
            int rings = Mathf.Max(1, Mathf.RoundToInt(drop / ringSpacing));
            for (int i = 1; i <= rings; i++)
            {
                float t = i / (float)(rings + 1);
                Vector3 p = Vector3.Lerp(top, bottom, t);
                PlaygroundBuilder.CreateSegmentRing($"{course}_DiveRing_{i:00}", p, 3f,
                    Quaternion.Euler(90f, 0f, 0f), 12, new Vector3(0.6f, 0.3f, 1.4f), ringMat, false);
            }

            // Redirect planet ON the fall line, two-thirds of the way down.
            Vector3 redirectPos = Vector3.Lerp(top, bottom, 0.66f);
            PlaygroundBuilder.CreateWorld(redirectPos, 10f, worldIndexStart, rng);

            var bodyMat = PlatformBodyMat();
            var trimMat = PlatformTrimMat();
            Vector3 catchCenter = bottom + c.H * (catchSize * 0.5f);
            PlaygroundBuilder.CreatePlatform($"{course}_DiveCatch", catchCenter - Vector3.up * 0.5f,
                new Vector3(catchSize, 1f, catchSize), bodyMat, trimMat);

            c.P = catchCenter + c.H * (catchSize * 0.5f); // far edge, surface height
            DropBreadcrumb(course, c.P);
        }

        /// <summary>Course finish: reuses BuildGate's ring+beacon+trigger,
        /// offset +10 m along H past the incoming c.P (review: placing the
        /// gate exactly at c.P made its halo coincide with the previous
        /// segment's last ring/breadcrumb). `preGateAnchorDistance` (optional;
        /// THE CLIMB uses ~15-20 m per the plan) drops one extra grapple-able
        /// world that many meters before the gate so the final approach stays
        /// coverable by a verb instead of a blind final glide.</summary>
        public static void GateFinish(string course, ref Cursor c, CourseResult result, System.Random rng,
            float preGateAnchorDistance = 0f, int worldIndexStart = 0)
        {
            const float offset = 10f;
            Vector3 gatePos = c.P + c.H * offset;

            if (preGateAnchorDistance > 0f)
            {
                Vector3 anchorPos = gatePos - c.H * preGateAnchorDistance + Vector3.up * 6f;
                PlaygroundBuilder.CreateWorld(anchorPos, 8f, worldIndexStart, rng);
            }

            result.Gate = PlaygroundBuilder.BuildGate($"Gate_{course}", gatePos);
            c.P = gatePos;
            DropBreadcrumb(course, c.P);
        }
    }
}
