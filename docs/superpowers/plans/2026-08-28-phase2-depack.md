# Phase 2: De-pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove every store asset pack (SGT/CW, Tirgames, Vefects, Hovl) and replace their roles with code-built visuals, so the game is 100% procedural per ADR-004 — same feel, ~1.1 GB lighter.

**Architecture:** Four excisions, each with a procedural replacement built the way the repo already builds art (hand-written shaders + primitives + Palette): Style A/SGT dies (Style B is already the shipped look); Tirgames meshes → code-built neon weapon silhouettes under the unchanged ViewmodelRig; Vefects impact frame → full-screen quad + hand-written ZTest-Always shader (there is NO fallback today — kills lose their pop if this ships broken); Hovl explosions → upgraded CombatFx (core flash + new fresnel shockwave shell + debris streaks). The Laser Weapons Sound Pack is KEPT this phase (free tier, but license text is absent from the repo — flagged as an open user decision, not silently deleted into soundless weapons).

**Tech Stack:** Unity 6000.5.1f1, URP, hand-written HLSL (`Assets/AlienSkies/Shaders/`). Headless gates: `bash tools/run_tests.sh`, builder `-executeMethod`, `SceneScreenshot.Capture`, `tools/build_player.sh`.

**Spec:** `docs/superpowers/specs/2026-08-28-singleplayer-pivot-design.md` (§5 art direction, Phase 2 in §6)

## Global Constraints

- Unity Editor CLOSED for every headless command (`tasklist.exe | grep -i unity`).
- Repo root: `/mnt/c/Users/computer/My project` (quote the space). Branch: `feature/singleplayer-pivot`.
- Git author the owner's git identity; NO Co-Authored-By or Claude attribution. Commit `.meta` files with their assets.
- Never hand-edit `MovementPlayground.unity`; the builder regenerates it (Task 5).
- Pure cores and tests untouched (except none are needed — this phase is glue/editor/shader work).
- Every task ends with `bash tools/run_tests.sh` green (85 tests; compile gate) then a commit. On failure read `TestResults/editmode.xml` + `TestResults/editmode.log` (grep `error CS`).
- DO NOT touch `Assets/Scenes/SampleScene.unity`, `Assets/Scripts/`, `Assets/ImportedModels/` (June black-hole spike — repo CLAUDE.md says leave), the Laser Weapons Sound Pack, or `StreamingAssets`.
- Deleting the packs is sanctioned including the ~1025 untracked Tirgames + ~363 untracked Vefects files (spec §8; re-importable from the Asset Store).

---

### Task 1: Excise Style A + SGT/CW

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` (region edits below)
- Modify: `Assets/AlienSkies/Editor/SceneScreenshot.cs` (region edits below)
- Modify: `Assets/AlienSkies/Scripts/PlanetSurface.cs` (COMMENT-ONLY: line 7 "every SgtPlanet's SphereCollider object" → "every planet sphere's collider object" — permitted despite the pure-scripts constraint, it changes no code)
- Modify: `Assets/AlienSkies/Editor/AlienSkies.Editor.asmdef` (drop `"SpaceGraphicsToolkit"`, `"SpaceGraphicsToolkit.Galaxy"`, `"CW.Common"` from `references`)
- Delete: `Assets/Plugins/CW/` (+ `.meta`; if `Assets/Plugins/` becomes empty, delete it and its `.meta` too), `Assets/AlienSkies/Materials/StylizedGun_0.mat`, `StylizedGun_1.mat`, `StylizedGun_2.mat`, `StylizedRocket.mat` (+ `.meta`s)

**Interfaces:**
- Produces: a builder with NO StyleMode — `BuildNeonVector()` remains the only entry point (`tools/` and docs reference that exact name; keep it); `CreateWorld` loses its `basePlanetMat` parameter (NeonPlanet path only).
- Consumes: nothing new.

- [ ] **Step 1: Builder edits** (scout line numbers from the current file — verify while reading):
  1. Delete usings: `SpaceGraphicsToolkit`, `SpaceGraphicsToolkit.Jovian`, `SpaceGraphicsToolkit.Galaxy`, `SpaceGraphicsToolkit.LightAndShadow`, `CW.Common` (lines 8–12).
  2. Delete `StyleMode` enum, the `Style` static field, and the `BuildStylizedPacks()` menu method (lines ~43–52). Keep `BuildNeonVector()`; inline whatever it needs (it just sets the dead static — now it only calls `Build()`).
  3. Delete every `if (Style == StyleMode.NeonVector)` branch by KEEPING the NeonVector arm unconditionally and deleting the Style-A arm: weapon materials (~227–249 — keep the NeonRim `NeonGun_{w}` creation), rocket visual (~391–410 — keep NeonRim `NeonRocket`), `CreateWorld` (~881–930 — keep the primitive-sphere NeonPlanet arm; delete the `SgtPlanet.Create` arm).
  4. Delete `sun.gameObject.AddComponent<SgtLight>()` (~85) and `camGO.AddComponent<SgtCamera>()` (~191).
  5. Delete the `Planet (01).mat` load + its error INCLUDING the SGT-era comment block above it (~439–450 — "Solid SgtPlanets… lit by the SgtLight sun"; write a fresh NeonPlanet-only comment), and remove the `basePlanetMat` parameter from `CreateWorld`'s signature and from every call site (~471, 491–492, 571, 732–733).
  6. Delete `ForceSyncJovian` and `ForceSyncGalaxy` (~936–954) — already dead (no Jovian/Galaxy is ever created).
- [ ] **Step 2: SceneScreenshot edits:** delete `using SpaceGraphicsToolkit;` (line 5), the SgtJovian/SgtGalaxy/SgtPlanet sync loops (~28–33), and `go.AddComponent<SgtCamera>()` in `Shot()` (~120).
- [ ] **Step 3: asmdef edit** — remove the three entries from the `references` array (valid JSON, no trailing comma).
- [ ] **Step 4: Deletions**

```bash
cd "/mnt/c/Users/computer/My project"
git rm -r -q Assets/Plugins/CW
git rm -q Assets/Plugins/CW.meta 2>/dev/null || git rm -q Assets/Plugins.meta 2>/dev/null || true
# If Assets/Plugins is now empty on disk: rm -rf "Assets/Plugins" and git rm its .meta.
git rm -q Assets/AlienSkies/Materials/StylizedGun_0.mat Assets/AlienSkies/Materials/StylizedGun_0.mat.meta \
          Assets/AlienSkies/Materials/StylizedGun_1.mat Assets/AlienSkies/Materials/StylizedGun_1.mat.meta \
          Assets/AlienSkies/Materials/StylizedGun_2.mat Assets/AlienSkies/Materials/StylizedGun_2.mat.meta \
          Assets/AlienSkies/Materials/StylizedRocket.mat Assets/AlienSkies/Materials/StylizedRocket.mat.meta
```

  (Check the actual filenames with `ls Assets/AlienSkies/Materials/` first; adjust if the builder names them differently.)
- [ ] **Step 5: Gate.** `bash tools/run_tests.sh` → PASS 85. Also `grep -rn "Sgt\|CwHelper\|CW\.Common\|StyleMode\|StylizedPacks" Assets/AlienSkies/ --include="*.cs"` → zero hits.
- [ ] **Step 6: Commit** — `refactor: excise Style A and SGT/CW - NeonVector is the only look`

---

### Task 2: Procedural neon viewmodels; delete Tirgames

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs`
- Delete: `Assets/TirgamesAssets/` (tracked subset via git rm, untracked remainder via rm -rf, + `.meta`)

**Interfaces:**
- Produces: `static GameObject BuildProceduralWeapon(int index, Material mat)` in PlaygroundBuilder — returns a collider-free primitive assembly, **nose along +Z in rig-local space** (the 180° rig flip existed only for Tirgames' backwards convention and is removed).
- Consumes: the `NeonGun_{w}` NeonRim materials Task 1 kept; `ViewmodelRig` (UNCHANGED — it only needs SetActive-able children; kick/bob/lurch move the whole rig).

- [ ] **Step 1: Replace the weapon-model loading loop.** SCOPE TRAP (caught in review): after Task 1, the `NeonGun_{w}` material creation lives BELOW the load lines inside the same loop body — `gunMat` must be created before `BuildProceduralWeapon` uses it, and the loop bound must stop referencing the deleted `weaponPrefabPaths` array. Replace the whole loop (~211–226 array + load/parent/scale/strip lines, plus the now-redundant foreach-renderers material assignment in the kept NeonVector arm) with this shape (keep the builder's real NeonGun material-creation call — copy its argument list verbatim from the kept arm):

```csharp
var weaponModels = new GameObject[3];
for (int w = 0; w < 3; w++)
{
    var gunMat = /* the kept NeonVector arm's NeonGun_{w} material creation, verbatim */;
    weaponModels[w] = BuildProceduralWeapon(w, gunMat);
    weaponModels[w].transform.SetParent(rigGO.transform, false);
    weaponModels[w].SetActive(w == 0);
}
```

  **Change the rig's local rotation from `Quaternion.Euler(0f, 180f, 0f)` to `Quaternion.identity`** (~209) and delete the "Tirgames guns model +Z toward player" comment. Add this method:

```csharp
/// <summary>Code-built weapon silhouette: primitive assembly, nose along +Z,
/// no colliders (viewmodels must never block raycasts), one NeonRim material.
/// The rim light does the design work — these are silhouettes, not models.</summary>
static GameObject BuildProceduralWeapon(int index, Material mat)
{
    var root = new GameObject("NeonWeapon_" + index);

    void Part(PrimitiveType type, Vector3 localPos, Vector3 localScale, Vector3 localEuler)
    {
        var p = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(p.GetComponent<Collider>());
        p.transform.SetParent(root.transform, false);
        p.transform.localPosition = localPos;
        p.transform.localScale = localScale;
        p.transform.localRotation = Quaternion.Euler(localEuler);
        p.GetComponent<Renderer>().sharedMaterial = mat;
    }

    switch (index)
    {
        case 0: // rocket launcher: fat tube + muzzle ring + grip + sight
            Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.10f), new Vector3(0.16f, 0.28f, 0.16f), new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.38f), new Vector3(0.20f, 0.03f, 0.20f), new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cube, new Vector3(0f, -0.13f, -0.08f), new Vector3(0.05f, 0.12f, 0.08f), Vector3.zero);
            Part(PrimitiveType.Cube, new Vector3(0f, 0.11f, 0.05f), new Vector3(0.03f, 0.04f, 0.10f), Vector3.zero);
            break;
        case 1: // rifle: long body + thin barrel + stock + sight
            Part(PrimitiveType.Cube, new Vector3(0f, 0f, 0.05f), new Vector3(0.06f, 0.10f, 0.42f), Vector3.zero);
            Part(PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0.40f), new Vector3(0.035f, 0.14f, 0.035f), new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cube, new Vector3(0f, -0.03f, -0.22f), new Vector3(0.05f, 0.08f, 0.12f), Vector3.zero);
            Part(PrimitiveType.Cube, new Vector3(0f, 0.08f, 0.12f), new Vector3(0.02f, 0.03f, 0.08f), Vector3.zero);
            break;
        default: // shotgun: twin barrels + chunky body + pump
            Part(PrimitiveType.Cylinder, new Vector3(-0.035f, 0.01f, 0.28f), new Vector3(0.05f, 0.19f, 0.05f), new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, new Vector3(0.035f, 0.01f, 0.28f), new Vector3(0.05f, 0.19f, 0.05f), new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.09f, 0.09f, 0.26f), Vector3.zero);
            Part(PrimitiveType.Cube, new Vector3(0f, -0.05f, 0.22f), new Vector3(0.10f, 0.05f, 0.10f), Vector3.zero);
            break;
    }
    return root;
}
```

  Wire each returned root into `weaponModels[w]`, `SetActive(w == 0)`, keep `localScale = Vector3.one`.
- [ ] **Step 2: Rocket visual:** in `BuildRocketPrefab`, delete the `SciFiRocket01.prefab` load (~378–383 with its fallback) and ALWAYS build the capsule visual (the current fallback path: capsule primitive, `Euler(90,0,0)`, NeonRocket material from Task 1). Delete `ConvertWeaponMaterialsToUrp()` (~314–361) and its call (~87).
- [ ] **Step 3: Delete the pack.**

```bash
cd "/mnt/c/Users/computer/My project"
git rm -r -q Assets/TirgamesAssets 2>/dev/null
git rm -q Assets/TirgamesAssets.meta 2>/dev/null || true
rm -rf Assets/TirgamesAssets   # the ~1025 untracked files
```

- [ ] **Step 4: Gate.** `bash tools/run_tests.sh` → PASS 85. `grep -rn "Tirgames\|SciFiRocket\|SciFiRifle\|SciFiShotGun\|ConvertWeaponMaterials" Assets/AlienSkies/ --include="*.cs"` → zero hits.
- [ ] **Step 5: Commit** — `feat: procedural neon viewmodels replace Tirgames; rocket is a neon capsule`

---

### Task 3: Shader impact frame; delete Vefects

**Files:**
- Create: `Assets/AlienSkies/Shaders/ImpactFrame.shader`
- Create: `Assets/AlienSkies/Scripts/ImpactFrameFade.cs`
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` (new `BuildImpactFramePrefab()`, rewire `feedback.ImpactFramePrefab`)
- Modify: `Assets/AlienSkies/Editor/SceneScreenshot.cs` (shot_impactframe block rebuilt HERE — it references the Vefects path, so leaving it to Task 4 fails this task's gate)
- Produce (generated, commit): `Assets/AlienSkies/Materials/ImpactFrame.mat` — the material MUST be an asset: `SaveAsPrefabAsset` silently nulls references to in-memory materials (proof in-repo: Rocket.prefab's TrailRenderer slot is `{fileID: 0}` for exactly this reason). An in-memory material here ships a kill pop that renders nothing.
- Delete: `Assets/Vefects/` (tracked via git rm, untracked via rm -rf, + `.meta`)
- Note: `CombatFeedback.cs` is UNCHANGED — same field, same camera-parent spawn at local (0,0,2), same `Destroy(frame, 1.5f)`.

**Interfaces:**
- Produces: `Assets/AlienSkies/Prefabs/ImpactFrame.prefab` — a quad that fills the view at 2 m (scale 4.2×2.4), shader-driven ~0.4 s black/white anime pop, then invisible.
- Consumes: `CombatFeedback.ImpactFramePrefab` (existing serialized field, wired by the builder).

- [ ] **Step 1: Write the shader** at `Assets/AlienSkies/Shaders/ImpactFrame.shader`:

```shaderlab
// Full-screen black/white anime impact pop. Replaces the Vefects pack:
// same contract - camera-parented quad, ZTest Always so it draws over
// everything, dead after ~0.4s (driven by ImpactFrameFade via _Progress).
Shader "AlienSkies/ImpactFrame"
{
    Properties
    {
        _Progress ("Progress 0..1", Range(0, 1)) = 0
        _Spikes ("Spike count", Float) = 14
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Progress;
            float _Spikes;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A a)
            {
                V o;
                o.pos = TransformObjectToHClip(a.pos.xyz);
                o.uv = a.uv;
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float2 c = i.uv - 0.5;
                float ang = atan2(c.y, c.x);
                float r = length(c) * 2.0;

                // Jagged radial spikes: black wedges over a white core, the
                // classic anime impact frame. Spike edges wobble with progress.
                float spikes = sin(ang * _Spikes + _Progress * 3.0)
                             + 0.5 * sin(ang * (_Spikes * 2.0 + 1.0) - _Progress * 5.0);
                float wedge = step(0.15 + 0.85 * r, saturate(0.5 + 0.5 * spikes));

                // Two-frame flicker: invert black/white halfway through.
                float invert = step(0.5, frac(_Progress * 4.0));
                float v = abs(wedge - invert);

                // Hard cut: fully opaque until 0.85, then snap out.
                float alpha = 1.0 - smoothstep(0.85, 1.0, _Progress);
                return float4(v.xxx, alpha);
            }
            ENDHLSL
        }
    }
}
```

- [ ] **Step 2: Write the fade driver** at `Assets/AlienSkies/Scripts/ImpactFrameFade.cs`:

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Drives the ImpactFrame shader's _Progress 0->1 over its life,
    /// then disables the renderer (the spawner's Destroy handles cleanup).</summary>
    public sealed class ImpactFrameFade : MonoBehaviour
    {
        public float Life = 0.4f;

        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        Renderer _renderer;
        Material _mat; // instance, so overlapping kills don't share progress
        float _t;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mat = _renderer.material;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float p = _t / Life;
            if (p >= 1f) { _renderer.enabled = false; return; }
            _mat.SetFloat(ProgressId, p);
        }

        void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}
```

- [ ] **Step 3: Builder:** add `BuildImpactFramePrefab()` next to `BuildRocketPrefab` (same save-over-path pattern, `Assets/AlienSkies/Prefabs/ImpactFrame.prefab`):
  - a `PrimitiveType.Quad` named `ImpactFrame`; strip its collider with `Object.DestroyImmediate(quad.GetComponent<Collider>())` (editor context — `Destroy()` throws in edit mode);
  - `localScale = new Vector3(4.2f, 2.4f, 1f)`;
  - material: **asset-backed via the builder's existing `GetOrCreateMaterial` pattern** (~1018–1028) — `GetOrCreateMaterial("ImpactFrame", () => new Material(Shader.Find("AlienSkies/ImpactFrame")))` producing `Assets/AlienSkies/Materials/ImpactFrame.mat`, assigned to the quad's `sharedMaterial`. `Debug.LogError` + skip save if `Shader.Find` returns null;
  - add the `ImpactFrameFade` component; save the prefab.

  **Call site (explicit — review caught that nothing invoked it):** at ~280–282, replace the Vefects `LoadAssetAtPath` with `feedback.ImpactFramePrefab = BuildImpactFramePrefab();` — mirroring `combat.RocketPrefab = BuildRocketPrefab();` at ~255 — and keep the null-check error line after it.
- [ ] **Step 3b: SceneScreenshot shot_impactframe rebuild** (~94–106): instantiate `Assets/AlienSkies/Prefabs/ImpactFrame.prefab` posed on the shot camera's own frame — `position = cam.transform.position + cam.transform.rotation * Vector3.forward * 2f; rotation = cam.transform.rotation;` (the shot camera is yawed `Euler(2,15,0)`; an axis-aligned quad leaves background gaps at the frame edges). Drive the flash WITHOUT touching the prefab's asset material (edit mode: `renderer.material` logs an instancing error; writing `_Progress` into `sharedMaterial` commits churn into the .mat): build a temporary `new Material(Shader.Find("AlienSkies/ImpactFrame"))`, `SetFloat("_Progress", 0.3f)`, assign to the instance's `sharedMaterial`, `DestroyImmediate` the temp material after the shot — the same pattern as the existing `coreMat` at ~84–91. Hard-fail when the prefab or shader is missing: `Debug.LogError` + `if (Application.isBatchMode) EditorApplication.Exit(1);` — NEW behavior for this file (its current style is silent skips); copy the pattern from `PlayerBuilder.cs:21-22`.
- [ ] **Step 4: Delete the pack.**

```bash
cd "/mnt/c/Users/computer/My project"
git rm -r -q Assets/Vefects 2>/dev/null
git rm -q Assets/Vefects.meta 2>/dev/null || true
rm -rf Assets/Vefects
```

- [ ] **Step 5: Gate.** `bash tools/run_tests.sh` → PASS 85. `grep -rn "Vefects" Assets/AlienSkies/ --include="*.cs"` → zero hits.
- [ ] **Step 6: Commit** — `feat: hand-written impact frame shader replaces Vefects`

---

### Task 4: Procedural explosions; delete Hovl; screenshot + manifest sweep

**Files:**
- Create: `Assets/AlienSkies/Shaders/ExplosionShell.shader`
- Modify: `Assets/AlienSkies/Scripts/CombatFx.cs` (rewrite below)
- Modify: `Assets/AlienSkies/Scripts/PlayerCombat.cs` (REPLACE the FX-prefab statics wiring: `ExplosionFxPrefab`/`ImpactFxPrefab` fields + their `Awake` assignments become one `public Material ExplosionShellMaterial;` field with `if (ExplosionShellMaterial != null) CombatFx.ShellMaterial = ExplosionShellMaterial;` in `Awake`)
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs` (Hovl loads ~269–274 → wire `combat.ExplosionShellMaterial = GetOrCreateMaterial("ExplosionShell", () => new Material(Shader.Find("AlienSkies/ExplosionShell")));`)
- Modify: `Assets/AlienSkies/Editor/SceneScreenshot.cs` (fx shot rebuilt, hard-fail — impactframe shot was already rebuilt in Task 3)
- Modify: `Packages/manifest.json` (remove `com.unity.postprocessing`, `com.unity.multiplayer.center`, `com.unity.shadergraph` lines) **and commit the regenerated `Packages/packages-lock.json`** (the gate's Unity run rewrites it; both files are tracked)
- Produce (generated, commit): `Assets/AlienSkies/Materials/ExplosionShell.mat`
- Delete: `Assets/Hovl Studio/` (fully tracked; + `.meta`)

**Interfaces:**
- Produces: `CombatFx.Explosion(at, normal, radius)` / `CombatFx.Impact(at, normal)` self-contained; `public static Material ShellMaterial` on CombatFx, wired from PlayerCombat.
- **WHY the material asset chain (review blocker):** Unity strips shaders nothing references from player builds. A bare runtime `Shader.Find("AlienSkies/ExplosionShell")` works in the editor and every screenshot, then returns null in the shipped exe — explosions dead, no gate catches it. The chain scene → Player.prefab → `PlayerCombat.ExplosionShellMaterial` → ExplosionShell.mat → shader forces the shader into the build. CombatFx copies the asset per instance (`new Material(ShellMaterial)`) so `_Progress` stays per-explosion; `Shader.Find` remains only as the editor-context fallback.
- Consumes: Palette colors inlined as before (violet core, magenta shell, cyan debris/sparks).

- [ ] **Step 1: Write `ExplosionShell.shader`:**

```shaderlab
// Expanding energy shell: additive fresnel rim that fades with _Progress.
// The explosion is this + the core flash + debris streaks - all code, no packs.
Shader "AlienSkies/ExplosionShell"
{
    Properties
    {
        _Color ("HDR Color", Color) = (4, 1.4, 5.2, 1)
        _RimPower ("Rim power", Float) = 2.2
        _Progress ("Progress 0..1", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            Cull Back
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Color;
            float _RimPower;
            float _Progress;

            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; };

            V vert(A a)
            {
                V o;
                o.wp = TransformObjectToWorld(a.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.wn = TransformObjectToWorldNormal(a.n);
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float rim = pow(1.0 - saturate(dot(normalize(i.wn), v)), _RimPower);
                float fade = saturate(1.0 - _Progress);
                return float4(_Color.rgb, rim * fade * fade);
            }
            ENDHLSL
        }
    }
}
```

- [ ] **Step 2: Rewrite `CombatFx.cs`** — replace the whole file with:

```csharp
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Combat visuals, 100% code-built (ADR-004): explosions are a
    /// core flash + expanding fresnel shockwave shell + radial debris streaks;
    /// hitscan impacts are a small spark. The pack-era prefab dispatch and
    /// hue-shift machinery died with the store assets (git history keeps the
    /// recolor-any-authored-effect pattern if it is ever needed again).</summary>
    public sealed class CombatFx : MonoBehaviour
    {
        /// <summary>Asset-backed shell material, wired by PlayerCombat from the
        /// builder. This reference chain is what ships the shell shader in the
        /// player build — a bare Shader.Find would be stripped and explode only
        /// in the editor. Editor tools without a PlayerCombat fall back to
        /// Shader.Find, which always works there.</summary>
        public static Material ShellMaterial;

        /// <summary>Every explosion notifies listeners (proximity screenshake).</summary>
        public static event System.Action<Vector3> ExplosionSpawned;

        float _life, _maxLife;
        Material _mat;
        LineRenderer _line;
        float _startScale = 1f, _maxScale;
        bool _shell;

        public static void Explosion(Vector3 at, float radius) =>
            Explosion(at, Vector3.up, radius);

        public static void Explosion(Vector3 at, Vector3 surfaceNormal, float radius)
        {
            ExplosionSpawned?.Invoke(at);
            // Core: instant violet volume + bloom pop at the epicenter.
            SpawnSphere(at, radius * 0.25f, radius * 1.6f, 0.3f,
                new Color(0.75f, 0.35f, 1f) * 3f, shell: false);
            // Shockwave: magenta energy shell racing past the splash radius.
            SpawnSphere(at, radius * 0.3f, radius * 2.2f, 0.45f,
                new Color(1f, 0.25f, 0.75f) * 4f, shell: true);
            Debris(at, radius);
        }

        /// <summary>Hitscan impact spark: small, cyan, gone in a blink.</summary>
        public static void Impact(Vector3 at, Vector3 surfaceNormal)
        {
            SpawnSphere(at + surfaceNormal * 0.05f, 0.18f, 0.55f, 0.12f,
                new Color(0.55f, 0.9f, 1f) * 2.5f, shell: false);
        }

        public static void Tracer(Vector3 from, Vector3 to)
        {
            SpawnStreak(from, to, 0.09f, 0.05f, new Color(0.55f, 0.9f, 1f) * 4f);
        }

        static void Debris(Vector3 at, float radius)
        {
            for (int i = 0; i < 10; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                Color c = i % 3 == 0
                    ? new Color(1f, 0.75f, 0.25f) * 3f   // amber sparks
                    : new Color(0.55f, 0.9f, 1f) * 3f;   // cyan sparks
                SpawnStreak(at + dir * radius * 0.4f, at + dir * radius * 1.5f, 0.22f, 0.03f, c);
            }
        }

        static void SpawnSphere(Vector3 at, float startScale, float endScale,
            float life, Color hdr, bool shell)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            go.transform.localScale = Vector3.one * startScale;
            var fx = go.AddComponent<CombatFx>();
            fx._maxLife = life;
            fx._startScale = startScale;
            fx._maxScale = endScale;
            fx._shell = shell;
            fx._mat = shell
                ? (ShellMaterial != null
                    ? new Material(ShellMaterial) // per-instance copy: _Progress is per-explosion
                    : new Material(Shader.Find("AlienSkies/ExplosionShell")))
                : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fx._mat.SetColor(shell ? "_Color" : "_BaseColor", hdr);
            go.GetComponent<Renderer>().sharedMaterial = fx._mat;
        }

        static void SpawnStreak(Vector3 from, Vector3 to, float life, float width, Color hdr)
        {
            var go = new GameObject("Streak");
            var fx = go.AddComponent<CombatFx>();
            fx._maxLife = life;
            fx._line = go.AddComponent<LineRenderer>();
            fx._line.positionCount = 2;
            fx._line.SetPosition(0, from);
            fx._line.SetPosition(1, to);
            fx._line.startWidth = width;
            fx._line.endWidth = width * 0.4f;
            fx._mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fx._mat.SetColor("_BaseColor", hdr);
            fx._line.material = fx._mat;
        }

        void Update()
        {
            _life += Time.deltaTime;
            float t = _life / _maxLife;
            if (t >= 1f) { Destroy(_mat); Destroy(gameObject); return; }
            if (_line == null)
                transform.localScale = Vector3.one * Mathf.Lerp(_startScale, _maxScale, t);
            if (_shell) _mat.SetFloat("_Progress", t);
        }
    }
}
```

- [ ] **Step 3: PlayerCombat:** replace the `ExplosionFxPrefab` / `ImpactFxPrefab` fields (~15–16) with `public Material ExplosionShellMaterial;` and replace the two guarded static assignments in `Awake()` (~50–51) with `if (ExplosionShellMaterial != null) CombatFx.ShellMaterial = ExplosionShellMaterial;`. Builder: replace the two Hovl loads + wiring + error lines (~269–274) with `combat.ExplosionShellMaterial = GetOrCreateMaterial("ExplosionShell", () => new Material(Shader.Find("AlienSkies/ExplosionShell")));` (+ a null-shader LogError).
- [ ] **Step 4: SceneScreenshot:** rebuild the `shot_fx.png` block (~60–92) — instead of instantiating a Hovl prefab, pose the new visuals directly with the same materials the runtime uses: one sphere with the ExplosionShell material (`_Progress` 0.35, scale 8) using a TEMPORARY material instance assigned to `sharedMaterial` and `DestroyImmediate`d after the shot (same pattern as the existing `coreMat` ~84–91 — never `.material` in edit mode), one URP-Unlit violet sphere (scale 4), six streak LineRenderers radiating from the center. **Hard-fail** if `Shader.Find("AlienSkies/ExplosionShell")` returns null: `Debug.LogError` + `if (Application.isBatchMode) EditorApplication.Exit(1);` — NEW behavior for this file (current style is silent skips); pattern from `PlayerBuilder.cs:21-22`. The Hovl path + comment die with the rewrite (there is no ApplyPaletteShift call in this file — review corrected the earlier claim).
- [ ] **Step 5: manifest.json:** remove the `com.unity.postprocessing` (dead — zero users), `com.unity.multiplayer.center` (networking leftover), and `com.unity.shadergraph` (Hovl was its only direct consumer; it remains as a URP transitive) lines.
- [ ] **Step 6: Delete the pack.**

```bash
cd "/mnt/c/Users/computer/My project"
git rm -r -q "Assets/Hovl Studio" && git rm -q "Assets/Hovl Studio.meta"
```

- [ ] **Step 7: Gate.** `bash tools/run_tests.sh` → PASS 85. `grep -rn "Hovl\|ApplyPaletteShift\|ExplosionPrefab\|ImpactPrefab" Assets/AlienSkies/ --include="*.cs"` → zero hits (comments included — the plan's replacement CombatFx comment is already worded to pass).
- [ ] **Step 8: Commit** (include `Packages/packages-lock.json` — the gate run regenerates it) — `feat: fully procedural explosions and sparks replace Hovl; manifest sweep`

---

### Task 5: Regenerate + verify everything

- [ ] **Step 1:** Rebuild scene + prefabs: the same `-executeMethod AlienSkies.EditorTools.PlaygroundBuilder.BuildNeonVector` invocation as Phase 1 Task 5 (mirror tools scripts, log to `TestResults/rebuild_scene.log`). Expect exit 0 and **zero `missing …` errors in the log** (every pack path is gone — any `missing weapon prefab/material` line is a Task 2 regression).
- [ ] **Step 2:** `bash tools/run_tests.sh` → **85/85**.
- [ ] **Step 3:** Screenshots via `-executeMethod ...SceneScreenshot.Capture` → all 7 PNGs regenerate, including `shot_weapons.png` (procedural guns), `shot_fx.png` (shell + core + streaks), `shot_impactframe.png` (quad at `_Progress` 0.3).
- [ ] **Step 4:** `bash tools/build_player.sh` → verify via the `PlayerBuilder: Succeeded` line in the newest log (the script's own exit code is unreliable — known bug), music masters installed.
- [ ] **Step 4b: Material-persistence check (review blocker guard):** `grep -A2 "m_Materials" "Assets/AlienSkies/Prefabs/ImpactFrame.prefab"` must show a real guid reference, NOT `{fileID: 0}`; `Assets/AlienSkies/Materials/ImpactFrame.mat` and `ExplosionShell.mat` must exist and be staged. A `{fileID: 0}` slot means the material wasn't asset-backed and the kill pop ships broken.
- [ ] **Step 5:** Commit regenerated `Assets/AlienSkies/Prefabs` (now incl. `ImpactFrame.prefab`) + `Assets/AlienSkies/Scenes` + `Assets/AlienSkies/Materials` — `build: regenerate scene and prefabs pack-free`.
- [ ] **Step 6:** STOP — the orchestrator (not a subagent) reviews the three art screenshots before docs/PR: weapon silhouettes readable? shell explosion reads as energy, not soap bubble? impact frame reads as an anime pop?

---

### Task 6: Docs + PR (orchestrator-owned)

ARCHITECTURE.md (§5 art: shaders list +2, FX section rewritten, impact frame bullet, §8 pack table → "none — ADR-004 zero store assets; Laser Weapons Sound Pack kept pending license verification", asset-policy line), README (viewmodel/FX description if mentioned), CLAUDE.md (RESUME → Phase 3 next; diary; **flag the sound-pack license question to the user explicitly**). Push, verify diff, PR remains #22 (same branch) — update the PR body checklist instead of opening a new PR.
