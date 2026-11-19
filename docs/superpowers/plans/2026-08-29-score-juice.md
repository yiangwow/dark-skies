# Score Juice + Presentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or executing-plans. Checkbox steps.

**Goal:** Make the scoring FELT and the boot/pause presentable: a rising "+N ×M" kill feed under the score, a heat bar that shows the music charging your multiplier, a glitch-materialized title card on boot, and a pause overlay in the Machine's register.

**Architecture:** One tiny pure-core addition (`RunCore.LastKillPoints`, +1 test → **116**), one event (`RunManager.Scored(points, mult)`), everything else OnGUI in the existing style vocabulary (ShadowLabel, Palette, the MachineVoice glitch-materialize trick). `TitleCard` is a scene MB the builder adds; pause is drawn by MovementHUD when the cursor is unlocked — the clock deliberately KEEPS RUNNING (identity: "THE CLOCK DOES NOT WAIT."), so zero simulation changes.

**Gate:** `bash tools/run_tests.sh` → **116** from Task 1 on. Branch `feature/overnight-polish`.

## Global Constraints

Repo `/mnt/c/Users/computer/My project` (quote the space); Unity Editor closed for headless; author `yiangwow`, no attribution; commit `.meta`s; scene via builder only; every gate FOR REAL; NEVER push (orchestrator pushes) — EXCEPTION to prior task patterns: do not run `git push` at all; no destructive git (a prior worker used `git checkout` on its own uncommitted plan-file edit — even that is banned: commit-as-WIP or leave it).

---

### Task 1: Kill feed + heat bar

**Files:** Modify `Assets/AlienSkies/Scripts/RunCore.cs` (one property), `Assets/AlienSkies/Tests/RunCoreTests.cs` (+1 test), `Assets/AlienSkies/Scripts/RunManager.cs` (one event), `Assets/AlienSkies/Scripts/MovementHUD.cs` (feed + bar).

- [ ] **Step 1: RunCore** — add `public int LastKillPoints { get; private set; }`; set it in `RegisterKill` (`LastKillPoints = _s.KillScore * Multiplier;` immediately before `Score += ...`, and use it: `Score += LastKillPoints;`); reset to 0 in `Reset()`.
- [ ] **Step 2: Test** (RunCoreTests):

```csharp
    [Test]
    public void LastKillPoints_TracksTheScoredAmount()
    {
        var c = NewCore();
        c.StartRun();
        c.RegisterKill(1f); // x4 -> 400
        Assert.AreEqual(400, c.LastKillPoints);
        c.RegisterKill(0f); // x1 -> 100
        Assert.AreEqual(100, c.LastKillPoints);
        c.Reset();
        Assert.AreEqual(0, c.LastKillPoints);
    }
```

- [ ] **Step 3: RunManager** — add `public event System.Action<int, int> Scored;` (points, multiplier); in `HandleHitConfirmed`, after `Core.RegisterKill(...)`, add `if (killed && Core.State == RunState.Running) Scored?.Invoke(Core.LastKillPoints, Core.Multiplier);` — **review: RegisterKill no-ops outside Running (pre-clock bot kills, post-grade rocket confirms), which would feed "+0 ×1" without the state gate.**
- [ ] **Step 4: MovementHUD** — kill feed + heat bar:
  1. Fields: `readonly List<(string text, float age)> _feed = new();` (+ `using System.Collections.Generic;`) and `bool _scoredHooked;`. **Subscription is LAZY-IN-UPDATE, mandatory (review major: the scene YAML orders the Player instance BEFORE RunManager, and the HUD ships enabled — an OnEnable subscription silently never fires):** in `Update()`, `if (!_scoredHooked && AlienSkies.Run.RunManager.Instance != null) { RunManager.Instance.Scored += HandleScored; _scoredHooked = true; }`; in `OnDisable`, unsubscribe and clear the flag (bot clones of the prefab get enabled-then-disabled around their first frame — keep it symmetric). `HandleScored(points, mult)` adds `($"+{points}  x{mult}", 0f)`.
  2. In `Update()`: age entries (`age += Time.deltaTime`), drop past 1.2 s, cap the list at 5 (drop oldest).
  3. In `OnGUI()`, under the SCORE line: draw entries newest-first, each rising (`y -= age * 30f`) and fading (`alpha = 1 - age/1.2`), fontSize 18, color = Palette.Amber for x1–x2, Palette.Magenta for x3+ (the hot kills read hot).
  4. **Heat bar** directly under the SCORE line (reuse the existing `Bar` helper): width 200, height 6, centered; `frac = _music != null ? _music.Core.Intensity : 0f` (the file's existing guard pattern — review: never dereference `_music` bare); fill color `Color.Lerp(Palette.Cyan, Palette.Magenta, frac)`; three thin tick marks at 1/3, 2/3, 3/3 (the x2/x3/x4 thresholds — draw 2px darker slivers over the bar). The ×N label already lives on the SCORE line; keep it there.
- [ ] **Step 5: Gate** → PASS **116**. Commit — `feat: kill feed + heat bar - the music visibly charges the score`

### Task 2: Title card + pause overlay

**Files:** Create `Assets/AlienSkies/Scripts/TitleCard.cs`; modify `Assets/AlienSkies/Scripts/MovementHUD.cs` (pause overlay), `Assets/AlienSkies/Scripts/BotSpawner.cs` (ONE line — review: B lacked the documented cursor-lock gate, so the pause screen's own advertised key could spawn a bot behind the overlay: add `Cursor.lockState == CursorLockMode.Locked` to its Update gate), `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` (add TitleCard GO next to MachineVoice).

- [ ] **Step 1: TitleCard.cs** — scene MB, OnGUI overlay shown from boot until a KEYBOARD key press ONLY — `var kb = UnityEngine.InputSystem.Keyboard.current; if (kb != null && kb.anyKey.wasPressedThisFrame) ...` (null-guarded per the codebase pattern; **review: an LMB dismissal fires the equipped ROCKET the same frame — the first interaction with the game must not be a point-blank rocket. Keyboard-only, and the cue reads "press any key to begin".**); while visible: full-screen black at alpha 0.85, centered "ALIEN SKIES" (fontSize ~72, bold, white) rendered through the SAME char-corruption materialize used by MachineVoice (copy the ~10-line glyph-substitution block; corruption decays over the first 1.0 s), below it a static line in Palette.Magenta: `"THE GYROSCOPE IS WATCHING."` (original; fontSize 22), bottom-center cue in Palette.Cyan fontSize 16: `"press any key to begin — TAB at the start line changes spheres"`. After dismissal the component disables itself (`enabled = false`). No interaction with RunManager (the clock starts on first MOVEMENT, which usually is the dismissing input — acceptable and honest).
- [ ] **Step 2: Pause overlay in MovementHUD** — at the TOP of `OnGUI` (before other drawing, after EnsureStyles): if `Cursor.lockState != CursorLockMode.Locked` AND the TitleCard is not showing (`FindFirstObjectByType<TitleCard>() is { enabled: true }` → skip; cache the reference lazily), draw: dim full-screen black alpha 0.6; centered "PAUSED — THE CLOCK DOES NOT WAIT." (Palette.Magenta, 30, bold); below, a controls block (Palette.Cyan, 18, left-aligned, one line each): move WASD · jump SPACE (planets launch) · grapple hold F · zip RMB · fire LMB · weapons 1/2/3/scroll · sphere TAB (start line) · bot B (start line) · resume ESC; below that, current sphere name + `BEST {run.BestScore}` (only when `RunManager.Instance != null` — the file's existing guard pattern). Then `return` — the pause screen replaces the HUD (crosshair etc. hidden). The run state keeps ticking by design.
- [ ] **Step 3: Builder** — next to the MachineVoice creation: `new GameObject("TitleCard").AddComponent<AlienSkies.Run.TitleCard>();` (namespace per file; no wiring).
- [ ] **Step 4: Gate** → 116. Commit — `feat: glitch title card + pause overlay - the clock does not wait`

### Task 3: Regenerate + verify + commit

- [ ] Rebuild scene (exit 0, clean log; TitleCard GO present in scene YAML); tests 116/116; screenshots fresh (title card is play-mode OnGUI — not visible in shots, expected); `bash tools/build_player.sh` exit 0; commit — `build: regenerate scene with title card`.
