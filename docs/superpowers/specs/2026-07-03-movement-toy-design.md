# Alien Skies Unity — Movement Toy (v1) — Design Spec

**Date:** 2026-07-03
**Status:** Approved by user (conversation, 2026-07-03)
**Repo:** `yiangwow/dark-skies` — becomes the home of Unity Alien Skies

## Context

Alien Skies (the UE5 project at `E:/projects/AlienSkies/`) spent its life on environment work — black hole shaders, mech viewmodel — and never reached gameplay. Decision: build the *game* in Unity, where Claude can own the full loop (C# code, text scenes, headless tests/builds), and invert the order — gameplay first, art later. The UE5 project remains the visual R&D home.

The first playable is a **movement toy**: if flinging yourself between floating islands feels amazing with zero art, the game works.

## Goal & Success Criterion

A first-person movement toy in a graybox cosmic arena: momentum-preserving movement plus a zip-grapple, where chaining grapples between floating "galaxies" compounds speed into flow.

**Success test:** the player flings themselves around for 10 minutes and doesn't want to stop.

**Out of scope for v1:** combat, art pass, menus, audio, multiplayer.

## Decisions (locked)

1. **Engine:** Unity 6 (`6000.5.1f1`), URP — existing project, existing repo.
2. **First slice:** movement toy (no combat).
3. **Home:** reuse the existing project ("My project" folder) and the `dark-skies` repo; product renamed to "Alien Skies".
4. **Grapple feel:** zip + momentum (Echo Point Nova style) — the hook pulls you toward the anchor fast; releasing keeps 100% of velocity; chains compound speed.

## Architecture

**Kinematic CharacterController with a pure-C# core.** Rejected alternative: Rigidbody/PhysX-driven movement — movement shooters fight the physics engine for feel (air control, crisp stops, tunneling at high speed).

- **`MovementCore`** (plain C# class, zero `UnityEngine` physics dependencies): input state in, velocity out, each tick. All movement/grapple math lives here. Fully unit-testable in EditMode with no scene.
- **`PlayerMotor`** (thin MonoBehaviour): reads input (new Input System), feeds `MovementCore`, applies the resulting velocity via `CharacterController.Move()`, raycasts for the grapple.
- **`MovementConfig`** (ScriptableObject): every tuning number — accelerations, friction, soft cap, grapple pull rate, aim-assist cone — in one asset so tuning is edit-one-asset, not recompile.

### Movement model (Quake lineage)

- Acceleration-based ground and air movement; air-strafing works.
- Friction applies on ground only; momentum is never silently destroyed in air.
- **Soft speed cap:** no hard ceiling — past a comfort speed, drag grows progressively. Grapple chains can exceed "normal" speed and that feels earned.

### Grapple (zip + momentum)

- Hold to fire a ray with a generous aim-assist cone (~5°); anchors on anything grappleable.
- While held: accelerate along the rope toward the anchor, with partial air-steering.
- On release: keep 100% of velocity, plus a small boost on release for the slingshot exit.

## Scene

One scene: `MovementPlayground`.

- Dark space skybox.
- Scattered field of graybox spheres and platforms at varied distances (stand-ins for galaxies).
- Kill-floor (fall volume) that respawns the player at a spawn point.
- Minimal UI: speedometer + grapple crosshair that indicates a valid target.
- Ugly on purpose.

## Testing & Verification

- **EditMode unit tests** on `MovementCore` (Unity Test Framework): e.g. release-preserves-speed, air-strafe respects soft cap, grapple acceleration along rope, friction only on ground.
- **Headless runs from WSL:** `/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe -batchmode -runTests ...` (path verified 2026-07-03).
- **Playtesting is the user's loop:** user presses Play in the editor and reports feel in vibe words; Claude translates vibe → mechanics (per standing feedback rule) and adjusts `MovementConfig`/`MovementCore`.

## Repo Hygiene (first commit batch)

- Delete tutorial junk: `Assets/TutorialInfo/`, `Assets/Readme.asset`, `UpgradeLog.htm`.
- `productName` → "Alien Skies" in ProjectSettings.
- Folder structure: `Assets/AlienSkies/{Scripts,Tests,Scenes,Config}`.
- Project `CLAUDE.md` brain file (vision, ADRs, diary — same format as the UE5 project).
- **ADR-001:** Unity chosen over UE5 for build-the-game velocity; UE5 project remains the visual R&D home.
- Git: authorship the owner's git identity (no Co-Authored-By), feature branches + PRs, never push to main.

## Division of Labor

- **Claude:** all C# under `Assets/`, tests, scene generation via editor scripts where practical, headless verification, git/PR flow.
- **User:** Unity Editor work (pressing Play, inspector wiring where needed), playtesting, feel verdicts.
