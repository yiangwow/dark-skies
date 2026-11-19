# Design Guardrails — Alien Skies

Standing rules for what makes THIS game good, distilled 2026-08-29 from a
6-lens research pass (game feel, speedrun design, score loops, music games,
small-game identity, fairness) + the 40 internal research notes, then audited
against the real build. Check every feature and tune against these. Amend only
with the owner.

## Contents
1. [Scorecard](#scorecard)
2. [The guardrails](#the-guardrails)
3. [Refusals](#refusals)
4. [Iteration plan](#iteration-plan)

## Scorecard

Initial audit at `fe4b67c` (2026-08-29, no clean passes); rows are updated
in the same commit as the work that moves them.

| # | Guardrail | Cluster | Verdict | Headline gap |
|---|-----------|---------|---------|--------------|
| 1 | rocket-wall-test | feel | PARTIAL | trail/muzzle flash/directional kick/scorch/layered thump all shipped; whole-flight trackability + wall-clip judgment need the owner's eyes |
| 2 | ladder-monotonic | feel | PARTIAL | hitstop/rotational-shake/kill-floor/tier-up sting all shipped (Wave 1); channel-magnitude instrumentation + 5-kill-chain frame-step need a capture run |
| 3 | speed-rendered-not-reported | feel | PARTIAL | wind layer + FOV/shake/wind settings shipped (Wave 1); accel now pulls the annulus inward (0.25 base, 0.10 at full climb); HUD-less tier-ranking clips + per-course dressing (art session) remain |
| 4 | glitch-tells-truth | feel | VIOLATES | kill-spike scanline over the reticle zone; magenta worn by non-threats; **turret beam fires in friendly cyan**; no unified glitch scalar |
| 5 | restart-instant-deterministic | loop | PARTIAL | R-restart + per-run-stable bot seeds shipped (Wave 1); 200ms frame-count + identical-input replay need a capture run |
| 6 | heat-moves-or-dies | loop | PARTIAL | movement-keyed + camp-safe + no victim pump; milk/pace halves still need playtest |
| 7 | hit-drops-one-tier | loop | MEETS | pending playtest confirmation |
| 8 | every-death-teaches | loop | PARTIAL | telegraphs good; the Voice now names turret/drone/self-splash deaths (Wave 1); the "what killed you?" playtest protocol is the owner's half |
| 9 | grades-are-sentences | loop | PARTIAL | delta-to-next, mid-run best ping, course readout + per-course rank all shipped; decomposing the grade into STATED criteria is the Wave-2 grades design chat |
| 10 | one-course-one-face | levels | VIOLATES | the "same map, 3 parts" verdict: shared palette/skybox/playlist; no per-course track/BPM |
| 11 | shortcut-visible-and-kept | levels | PARTIAL | authored + generator-validated cuts on all three courses; visible-from-path needs the owner's editor flythrough; tier-change literal test blocked by scoring constants (see Wave 2) |
| 12 | rocket-is-two-verbs | identity | PARTIAL | rocket-jump tuned but no route requires it; rifle/shotgun still shipped pending rocket-only confirm |
| 13 | music-pays-never-gates | identity | PARTIAL | nothing gates (trivially); but zero beat infrastructure — nothing pays either |
| 14 | heat-audible-as-stems | identity | VIOLATES | volume+low-pass ride, not stems; player can't hear which tier they hold |
| 15 | refusal-list-shipped | identity | VIOLATES | doctrine scattered across ADRs/diary; no single artifact (this doc's §Refusals is the fix) |

## The guardrails

### Feel

**1. rocket-wall-test** — The rocket must be satisfying fired at a bare wall:
same-frame muzzle flash, camera kick opposite fire, viewmodel kickback with
eased recovery, a projectile trackable for its whole flight at zip speed, a
persistent trail/scorch, layered fire sound. Never a wind-up delay.
*Why:* flavor-over-count concentrates the whole arsenal's feedback budget in
one trigger pull (Nijman screenshake talk; Swink's <100ms correction loop).
*Test:* wall-firing clip satisfying on its own; keypress→first response ≤6
frames at 60fps.

**2. ladder-monotonic** — Rank events (fire < hit < kill < multikill/tier-up <
death); every channel (shake, loudness, particles, tear size, hitstop) must be
larger for the higher event. One 0-1 trauma accumulator, squared, Perlin,
rotational-only, clamped. Hitstop 40-80ms on kills ONLY, clamped not stacked.
*Why:* at 60 m/s under breakcore, amplitude IS the information channel
(Eiserloh trauma-camera; linear shake makes small events loud and big quiet).
*Test:* log channel magnitudes per event — any inversion fails; frame-step a
5-kill chain — freezes on kills only, total <150ms.

**3. speed-rendered-not-reported** — The three speed tiers (10/26/60 m/s) must
be tellable apart from a HUD-less 1s clip: velocity-scaled FOV (+10-15° at
zip), accel-swelling speedline annulus, speed-scaled wind layer, near-path
parallax. Never steal the camera; FOV/shake/blur are settings.
*Why:* perceived speed comes from motion relative to context, not the velocity
float (Swink); mastery must pay in sensation.
*Test:* playtester ranks HUD-less clips; camera transform shows no non-input
motion beyond the kick budget; settings exist.

**4. glitch-tells-truth** — In the worst-case frame (zip + top heat + tears),
threats stay the highest value-contrast elements. Glitch budget: tears ≤25% of
frame, never the reticle zone, one intensity scalar with a ceiling. The threat
hue (magenta) is never worn by anything that can't hurt you — and threat
attacks never wear the friendly hue.
*Why:* the glitch identity is uniquely able to camouflage its own threat layer
(Superhot's three-hue lesson: threat color = readability system AND identity).
*Test:* grayscale the worst-case screenshot — threats must pop; grep materials
for hue leaks.

### Loop

**5. restart-instant-deterministic** — One keypress from anywhere to a
controllable, deterministically reset course in <200ms; timer arms on first
input; death spectacle interruptible frame 1. Identical inputs ⇒ identical
runs: no RNG may touch time or score.
*Why:* the restart runs hundreds of times more than any other transition;
determinism is the load-bearing wall under bests/grades/medals ("everyone
above you overcame the exact same obstacles").
*Test:* frame-count restart; replay identical inputs twice — any divergence
fails.

**6. heat-moves-or-dies** — Heat gain keys on (movement-state × kill-method) —
never weapon identity — with staleness decay. No stationary loop sustains x3+
past ~5s. Score never pays for slowness: best-known strategy finishes within
~10% of gold pace.
*Why:* re-keys Ultrakill freshness for a one-weapon game — the meter becomes a
movement tutor; Garegga's law: any system exploitable by playing worse will be.
*Test:* camp test, milk test, grounded-vs-grapple kill shows different gain.

**7. hit-drops-one-tier** — Damage at x4 leaves exactly x3 (music steps down
one layer). Only death zeroes heat.
*Why:* full resets train quit-restarting; bounded drops keep every run
recoverable and make the music dip a narrative beat.
*Test:* scripted hit per tier asserts tier-1; grep for non-death resets.

**8. every-death-teaches** — No damage without ≥300ms readable warning; at
death the killer is identifiable — the glitch cascade must not erase the
source, or the Gyroscope names it.
*Why:* ~265ms is the human floor (KI guide); attributed deaths get retried,
unattributed ones end sessions (Celeste forgiveness talk). The Voice is an
attribution channel other games don't have.
*Test:* frame-count every windup; "what killed you?" wrong >1/10 fails.

**9. grades-are-sentences** — Every grade decomposes into stated criteria; the
grade screen prints the concrete delta to the NEXT grade; course select shows
per-course grade + gap; beating a best is announced mid-run.
*Why:* P-rank is a sentence; Trackmania retains by showing the gap at
decision time; with 3 courses the select screen IS the metagame.
*Test:* player states what to change to grade up without guessing.

### Levels

**10. one-course-one-face** — Before generation, each course has: a
one-sentence thesis, its own track with its own BPM, and palette/skybox/glitch
flavor derived from that track. Par sits within one bar of a section boundary.
Layout variation alone never counts as a new course.
*Why:* the "same map, 3 parts" fix. Identity comes from a per-course
constraint the generator obeys (Neon White, Dusk, NecroDancer's
track-as-floor-identity). Choices decided WITH the owner.
*Test:* random 3s HUD-less clip → playtester names the course.

**11. shortcut-visible-and-kept** — Every course has ≥1 cross-cut visible from
the main path, using only granted verbs, saving a grade tier — validated by
the generator. Discovered skips are patched only if they trivialize the
course's lesson.
*Why:* the "what if I just..." moment is the genre's strongest retention beat
(Neon White invariant); patching skips converts invested players to
ex-players.
*Test:* generator rejects layouts without a qualifying cross-cut.

### Identity

**12. rocket-is-two-verbs** — The rocket is a traversal verb, not just a gun:
rocket-jump is first-class, at least one Ace line requires it, and no intended
encounter makes full-stop aiming optimal (pockets clearable at >50% run
speed). No second weapon until the rocket carries both verbs.
*Why:* one weapon survives flavor-over-count only if deep on the movement axis
(Devil Daggers' dagger, Ultrakill's rockets-as-mobility).
*Test:* Ace line requires the self-kick; pocket-clear speed timing.

**13. music-pays-never-gates** — No verb is ever disabled/delayed for off-beat
play; on-beat only ADDS. Quantize outputs (audio flourishes snap to grid), not
inputs. Music-driven enemy events lock to the half-time spine (~90-110 BPM),
cosmetics ride the chop. No gold-route segment demands an on-beat input at a
constrained moment.
*Why:* stress destroys timing exactly at peak engagement (NecroDancer's
forgiveness, Hellsinger's miss = no punishment, BPM's two-clock failure);
beat-locked volleys multiply the frozen roster for free.
*Test:* deliberate off-beat run at 100% effect; arrhythmic input still sounds
on-grid.

**14. heat-audible-as-stems** — Each heat tier maps to a discrete stem layer —
not a volume/filter ride — with hysteresis, and the fullest arrangement
exclusive to the top tier. Tier changes audible within one bar.
*Why:* the central conceit (music heat = multiplier) is unfalsifiable to the
player through a low-pass knob; Hellsinger's 5 instrumentation gradations make
the track itself the meter. Needs the owner's music as stems/sections — art
session constraint.
*Test:* blind-HUD tier identification; tier-1 vs top-tier audio diff.

**15. refusal-list-shipped** — Keep §Refusals below current; check every
feature proposal against it before implementation.

## Refusals

What this game deliberately does not have. Each absence is load-bearing
identity (VVVVVV/Devil Daggers/Superhot pattern). Amend only with the owner.

**Decided (already in force):**
- **No store assets** — everything procedural (ADR-004; every pack fought the identity).
- **No networking / multiplayer** — ADR-004; the tax made the game unreachable.
- **No third enemy type** — roster FROZEN at drones + turrets (flavor over count).
- **No second weapon while the rocket-only lean holds** — pending the owner's final confirm on deleting rifle/shotgun.
- **No SS2 lines** — the Gyroscope speaks only original writing.

**Proposed, pending owner ratification (do NOT enforce yet):**
- No upgrade/unlock trees. No ammo/reload. No minimap. No difficulty modes
  (the course ladder and medal tiers are the difficulty system).

## Iteration plan

**Wave 1 — unambiguous mechanical fixes (no design input needed):**
rocket trail regression (asset-back the material), restart keybind (R), bot
seed determinism (per-run-stable seeds), kill-spike off the reticle zone,
killer attribution (source-typed death lines), feedback ladder wiring
(hitstop, rotational-only shake, kill trauma floor, tier-up sting), wind
layer + minimal settings, mid-run best ping + grade-delta line.

**Wave 2 — needs a design chat with the owner first:**
freshness-heat movement-keyed redesign (recorded gate), hit-drops-one-tier
(same chat), Ace/medal route layer (includes the tier-width/time-bonus
mismatch the cross-cut floor exposed: 500 pts = 50 s at current constants),
cross-cut flythrough sign-off (all three now authored + validated; the
visible-from-path half is the owner's editor check), rocket-only confirm → rifle/shotgun
deletion, refusal ratification.

**Wave 3 — art/vibe co-design session (owner-reserved):**
per-course identity (track/BPM/palette/skybox — guardrail 10), heat as stems
(needs music delivered as stems/sections — guardrail 14), beat infrastructure
+ half-time spine (guardrail 13), the threat-color contract (who may wear
magenta — guardrail 4), per-course speed dressing (guardrail 3).
