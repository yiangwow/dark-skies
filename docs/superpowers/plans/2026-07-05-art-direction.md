# Art Direction A/B Implementation Plan (GitHub PR #11)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kill the "four styles stapled together" incoherence: one shared style foundation (palette, NeonRim shader, designed platforms, styled body + HUD) plus two switchable full looks — **Style B "Neon Vector"** (everything flat + rim-glow, zero pack textures) and **Style A "Stylized Packs"** (pack textures forced through the palette) — delivered as side-by-side screenshot sets for the user's verdict.

**Architecture:** A `Palette` constants class (runtime) is the single source of color truth. A new `AlienSkies/NeonRim` shader (dark flat base + HDR fresnel rim + soft sun shading) carries the neon look on any mesh. `PlaygroundBuilder` gains a `StyleMode` enum with two batch entry points; shared foundation is built identically in both modes, only planets/guns/rocket-visual branch.

**Tech Stack:** Classic CG unlit shader (same pattern as existing GlowOrb/CosmicSkybox — proven to render in this URP project), builder-generated materials, OnGUI HUD.

## Global Constraints

- Branch `feature/art-direction` off `feature/match-win` (stacked; PR #11 bases on `feature/match-win`).
- Git author the owner's git identity; NO Co-Authored-By; never push main; NEVER `git add -A`; every new Assets/ file needs its .meta.
- Repo root `/mnt/c/Users/computer/My project` — always quote the space.
- Tests: `bash tools/run_tests.sh` (Unity Editor CLOSED). Suite is 67 green; this plan adds 0 (visual work; compile gates only).
- **PREFAB RULE:** commit `Rocket.prefab` + `NetworkPlayer.prefab` + `DefaultPrefabObjects.asset` together after every rebuild; never `git checkout --` them.
- Palette (verbatim, from the arena's GalaxyPalette): Magenta `(1, 0.18, 0.53)`, Cyan `(0.22, 0.88, 1)`, Violet `(0.6, 0.36, 1)`, Green `(0.29, 1, 0.69)`, Amber `(1, 0.7, 0.12)`, Ink (dark base) `(0.045, 0.045, 0.10)`.
- Style assignments (decided): platforms = Ink base / Cyan rim + Cyan emissive edge trim; player body = Ink / **Amber** rim (enemy reads as an orange silhouette — TTGL); rocket launcher = Amber rim, rifle = Cyan rim, shotgun = Magenta rim; rocket projectile = Amber rim; planets (B) = per-planet palette color (dark base ×0.06, bright rim); planets (A) = current SGT look unchanged; GlowOrb landmark + skybox stay in both (already on-palette).
- Layout is untouchable: the `rng(42)` position/size stream must produce identical planet positions/diameters in both modes.
- Default `StyleMode` until the verdict: `NeonVector` (the exe ships B; flipping to A is one menu item / one batch arg).
- v1 non-goals: texture edits, new geometry beyond platform trim, animated shader effects, HUD textures/fonts beyond OnGUI styling.

---

### Task 1: Palette + NeonRim shader + shared foundation + StyleMode scaffolding

**Files:**
- Create: `Assets/AlienSkies/Scripts/Palette.cs`
- Create: `Assets/AlienSkies/Shaders/NeonRim.shader`
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs`

**Interfaces:**
- Produces: `AlienSkies.Palette` statics `Color Magenta, Cyan, Violet, Green, Amber, Ink` (+ `Color[] Planets`); shader `"AlienSkies/NeonRim"` with properties `_BaseColor, _RimColor, _RimPower, _RimIntensity, _SunDir, _Shading`; builder `public enum StyleMode { NeonVector, StylizedPacks }`, `public static StyleMode Style`, entry points `BuildNeonVector()` / `BuildStylizedPacks()`, helper `static Material NeonMaterial(string name, Color rim, float rimIntensity, float shading = 0.45f)`.

- [ ] **Step 1: Create `Assets/AlienSkies/Scripts/Palette.cs`:**

```csharp
using UnityEngine;

namespace AlienSkies
{
    /// <summary>The game's entire color language. Every material, HUD label, and
    /// effect tint must come from here — coherence is enforced, not hoped for.</summary>
    public static class Palette
    {
        public static readonly Color Magenta = new Color(1f, 0.18f, 0.53f);
        public static readonly Color Cyan = new Color(0.22f, 0.88f, 1f);
        public static readonly Color Violet = new Color(0.6f, 0.36f, 1f);
        public static readonly Color Green = new Color(0.29f, 1f, 0.69f);
        public static readonly Color Amber = new Color(1f, 0.7f, 0.12f);
        public static readonly Color Ink = new Color(0.045f, 0.045f, 0.10f);

        /// <summary>Hues planets draw from (indexed by look-rng).</summary>
        public static readonly Color[] Planets = { Magenta, Cyan, Violet, Green, Amber };
    }
}
```

- [ ] **Step 2: Create `Assets/AlienSkies/Shaders/NeonRim.shader`:**

```
Shader "AlienSkies/NeonRim"
{
    // Flat stylized surface: dark base, HDR fresnel rim in a palette color, and
    // a soft sun-side lambert term so shapes still read as volumes. The whole
    // neon-vector art direction hangs off this one shader.
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.045, 0.045, 0.10, 1)
        _RimColor ("Rim Color", Color) = (0.6, 0.36, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.6
        _RimIntensity ("Rim Intensity (HDR)", Range(0, 8)) = 2.4
        _SunDir ("Sun Direction", Vector) = (-0.35, 0.71, -0.61, 0)
        _Shading ("Sun Shading Amount", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _BaseColor, _RimColor;
            float4 _SunDir;
            float _RimPower, _RimIntensity, _Shading;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - wpos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 vd = normalize(i.viewDir);
                float ndl = saturate(dot(n, normalize(_SunDir.xyz)));
                float shade = lerp(1.0, 0.35 + 0.65 * ndl, _Shading);
                float fres = pow(1.0 - saturate(dot(n, vd)), _RimPower);
                float3 col = _BaseColor.rgb * shade + _RimColor.rgb * (fres * _RimIntensity);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 3: Builder scaffolding + shared foundation.** In `PlaygroundBuilder.cs`:

3a. Add near the top of the class (after `MatDir`):

```csharp
        public enum StyleMode { NeonVector, StylizedPacks }
        /// <summary>A/B art direction switch. Default = the neon-vector look
        /// until the user's verdict picks a winner.</summary>
        public static StyleMode Style = StyleMode.NeonVector;

        [MenuItem("AlienSkies/Build Playground (Neon Vector)")]
        public static void BuildNeonVector() { Style = StyleMode.NeonVector; Build(); }

        [MenuItem("AlienSkies/Build Playground (Stylized Packs)")]
        public static void BuildStylizedPacks() { Style = StyleMode.StylizedPacks; Build(); }
```

Remove the `[MenuItem("AlienSkies/Build Movement Playground")]` attribute from `Build()` (the two styled entries replace it).

3b. Neon material helper (next to `GetOrCreateMaterial`; idempotent — overrides values every build so the builder owns the look):

```csharp
        static Material NeonMaterial(string name, Color rim, float rimIntensity, float shading = 0.45f)
        {
            var m = GetOrCreateMaterial(name, () => new Material(Shader.Find("AlienSkies/NeonRim")));
            m.shader = Shader.Find("AlienSkies/NeonRim");
            m.SetColor("_BaseColor", AlienSkies.Palette.Ink);
            m.SetColor("_RimColor", rim);
            m.SetFloat("_RimIntensity", rimIntensity);
            m.SetFloat("_Shading", shading);
            m.SetVector("_SunDir", SunDir);
            EditorUtility.SetDirty(m);
            return m;
        }
```

3c. Platforms with edge trim — replace `CreateBox("SpawnPlatform", ...)` and the rest-platform loop's `CreateBox($"Platform_{i}", ...)` calls with `CreatePlatform(...)` (same names/positions/scales), and add:

```csharp
        static void CreatePlatform(string name, Vector3 pos, Vector3 scale, Material bodyMat, Material trimMat)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = pos;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = bodyMat;

            // Emissive trim strips along the walkable surface's edges: the light
            // language that makes graybox read as designed architecture.
            const float t = 0.22f, h = 0.14f;
            float topY = pos.y + scale.y * 0.5f + h * 0.5f;
            void Trim(Vector3 p, Vector3 s)
            {
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = $"{name}_trim";
                Object.DestroyImmediate(strip.GetComponent<Collider>()); // cosmetic only
                strip.transform.position = p;
                strip.transform.localScale = s;
                strip.transform.SetParent(box.transform, true);
                strip.GetComponent<Renderer>().sharedMaterial = trimMat;
            }
            Trim(new Vector3(pos.x, topY, pos.z + scale.z * 0.5f), new Vector3(scale.x + t, h, t));
            Trim(new Vector3(pos.x, topY, pos.z - scale.z * 0.5f), new Vector3(scale.x + t, h, t));
            Trim(new Vector3(pos.x + scale.x * 0.5f, topY, pos.z), new Vector3(t, h, scale.z + t));
            Trim(new Vector3(pos.x - scale.x * 0.5f, topY, pos.z), new Vector3(t, h, scale.z + t));
        }
```

In `BuildArena`, create the materials before the platform calls and pass them:

```csharp
            var platformMat = NeonMaterial("NeonPlatform", AlienSkies.Palette.Cyan, 1.1f, 0.55f);
            var trimMat = GetOrCreateMaterial("NeonTrim", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            trimMat.SetColor("_BaseColor", AlienSkies.Palette.Cyan * 2.4f);
            EditorUtility.SetDirty(trimMat);
```

(The old `platformMat` URP-Lit block is deleted — CreatePlatform replaces CreateBox for platforms; `CreateBox` itself stays for any other use.)

3d. Player body — in `BuildPlayer`, replace the `bodyMat` block with:

```csharp
            var bodyMat = NeonMaterial("NeonBody", AlienSkies.Palette.Amber, 3.2f, 0.25f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;
```

(Amber rim, hot: the enemy reads as a burning orange silhouette at any distance.)

- [ ] **Step 4: Rebuild (neon mode) + screenshot sanity.**

```bash
cd "/mnt/c/Users/computer/My project" && timeout -k 30 570 "/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\Users\computer\My project" -executeMethod AlienSkies.EditorTools.PlaygroundBuilder.BuildNeonVector -logFile - 2>&1 | grep -iE "error CS|PlaygroundBuilder: saved" | head -3
```

Then capture SceneScreenshot and Read `shot_spawn.png` — platforms should be dark slabs with glowing cyan edge trim; the arena must not be darker overall than before.

- [ ] **Step 5: Run suite** (compile gate) → 67/67.

- [ ] **Step 6: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/Palette.cs Assets/AlienSkies/Scripts/Palette.cs.meta Assets/AlienSkies/Shaders/NeonRim.shader Assets/AlienSkies/Shaders/NeonRim.shader.meta Assets/AlienSkies/Editor/PlaygroundBuilder.cs Assets/AlienSkies/Materials Assets/AlienSkies/Scenes/MovementPlayground.unity Assets/AlienSkies/Prefabs/NetworkPlayer.prefab Assets/AlienSkies/Prefabs/Rocket.prefab Assets/DefaultPrefabObjects.asset && git commit -m "feat: style foundation - Palette, NeonRim shader, trimmed platforms, amber body"
```

---

### Task 2: HUD restyle (shared by both styles)

**Files:**
- Modify: `Assets/AlienSkies/Scripts/MovementHUD.cs`

**Interfaces:** consumes `AlienSkies.Palette`; no new public surface.

- [ ] **Step 1: Restyle.** Apply these changes to `MovementHUD.cs`:

1a. In `EnsureStyles()` set palette colors and add a shadowed-label helper. Replace the whole `EnsureStyles` body and add the helper method:

```csharp
        void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold };
            _label.normal.textColor = AlienSkies.Palette.Cyan;
            _cross = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = 26 };
            _cross.normal.textColor = Color.white;
            _hitmark = new GUIStyle(_cross) { fontSize = 34 };
            _hitmark.normal.textColor = AlienSkies.Palette.Amber;
            _killLabel = new GUIStyle(_cross) { fontSize = 36 };
            _killLabel.normal.textColor = AlienSkies.Palette.Magenta;
        }

        /// <summary>Label with a drop shadow — the difference between debug text
        /// and UI. All HUD text goes through here.</summary>
        static void ShadowLabel(Rect r, string text, GUIStyle style)
        {
            var c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }
```

1b. Replace every direct `GUI.Label(` call in `OnGUI` with `ShadowLabel(` (same rects/args) EXCEPT the crosshair label (crosshairs don't want shadows). 

1c. HP becomes a bar + number. Replace the HP label block with:

```csharp
            float hpFrac = Mathf.Clamp01(Combat.Health / 100f);
            var barBack = new Rect(24f, Screen.height - 54f, 220f, 14f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(barBack, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(AlienSkies.Palette.Magenta, AlienSkies.Palette.Green, hpFrac);
            GUI.DrawTexture(new Rect(barBack.x, barBack.y, barBack.width * hpFrac, barBack.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            ShadowLabel(new Rect(barBack.x, barBack.y - 34f, 300f, 30f),
                        $"HP {Combat.Health:0}", new GUIStyle(_label) { fontSize = 26 });
```

1d. Zip cooldown becomes a thin bar under the crosshair. Replace the zip-cooldown label block with:

```csharp
            if (Motor.Core.ZipCooldownRemaining > 0f)
            {
                float cdFrac = 1f - Mathf.Clamp01(Motor.Core.ZipCooldownRemaining / 3f);
                var cdRect = new Rect(Screen.width / 2f - 40f, Screen.height / 2f + 26f, 80f, 5f);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(cdRect, Texture2D.whiteTexture);
                GUI.color = AlienSkies.Palette.Violet;
                GUI.DrawTexture(new Rect(cdRect.x, cdRect.y, cdRect.width * cdFrac, cdRect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
```

1e. Status color language: ZIP!/GRAPPLING labels use `_label` but with Green (`new GUIStyle(_label)` + `normal.textColor = AlienSkies.Palette.Green`); the "PLANET — SPACE to kick off!" cue uses Amber the same way. Splash colors: VICTORY stays gold-ish Amber, DEFEATED Magenta (replace the two `new Color(...)` literals with `AlienSkies.Palette.Amber` / `AlienSkies.Palette.Magenta`).

- [ ] **Step 2: Run suite** (compile gate) → 67/67.

- [ ] **Step 3: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Scripts/MovementHUD.cs && git commit -m "feat: HUD restyle - palette colors, drop shadows, HP + zip cooldown bars"
```

---

### Task 3: Style B — neon guns, rocket, planets

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs`

**Interfaces:** consumes `NeonMaterial`, `Palette`, `Style`/`StyleMode` (Task 1).

- [ ] **Step 1: Guns.** In `BuildPlayer`'s weapon-model loop, after the collider-strip loop, add:

```csharp
                if (Style == StyleMode.NeonVector)
                {
                    var gunMat = NeonMaterial($"NeonGun_{w}", GunRim(w), 2.2f, 0.5f);
                    foreach (var r in weaponModels[w].GetComponentsInChildren<Renderer>())
                        r.sharedMaterials = Repeat(gunMat, r.sharedMaterials.Length);
                }
```

and the two helpers next to `NeonMaterial`:

```csharp
        static Color GunRim(int weaponIndex) =>
            weaponIndex == 0 ? AlienSkies.Palette.Amber :
            weaponIndex == 1 ? AlienSkies.Palette.Cyan : AlienSkies.Palette.Magenta;

        static Material[] Repeat(Material m, int count)
        {
            var arr = new Material[count];
            for (int i = 0; i < count; i++) arr[i] = m;
            return arr;
        }
```

- [ ] **Step 2: Rocket visual.** In `BuildRocketPrefab`, after the `visual.transform.localRotation = ...` statement, add:

```csharp
            if (Style == StyleMode.NeonVector)
            {
                var rocketMat = NeonMaterial("NeonRocket", AlienSkies.Palette.Amber, 2.6f, 0.4f);
                foreach (var r in visual.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = Repeat(rocketMat, r.sharedMaterials.Length);
            }
```

- [ ] **Step 3: Planets.** In `BuildArena`, wrap the SgtPlanet creation: keep the `rng`-stream reads EXACTLY as they are (yaw/dist/height/pos/diameter/rot computed identically), then branch:

```csharp
                if (Style == StyleMode.NeonVector)
                {
                    // Neon planet: plain sphere, palette rim — no SGT, no textures.
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphere.name = $"Planet_{i:00}";
                    sphere.transform.SetPositionAndRotation(pos, rot);
                    sphere.transform.localScale = Vector3.one * diameter;
                    var hue = AlienSkies.Palette.Planets[lookRng.Next(AlienSkies.Palette.Planets.Length)];
                    var mat = new Material(Shader.Find("AlienSkies/NeonRim")); // scene-embedded
                    mat.name = $"PlanetLook_{i:00}";
                    mat.SetColor("_BaseColor", hue * 0.06f);
                    mat.SetColor("_RimColor", hue);
                    mat.SetFloat("_RimIntensity", 2.0f);
                    mat.SetFloat("_Shading", 0.6f);
                    mat.SetVector("_SunDir", SunDir);
                    sphere.GetComponent<Renderer>().sharedMaterial = mat;
                    // Primitive sphere collider is already radius 0.5 * scale = surface.
                    sphere.AddComponent<PlanetSurface>();
                }
                else
                {
                    /* existing SgtPlanet block, verbatim, including its
                       lookRng.NextDouble() calls, collider and PlanetSurface */
                }
```

IMPORTANT: the two branches consume different amounts of `lookRng` — that's acceptable (looks stream), but the `rng` stream reads must stay OUTSIDE the branch, before it, shared. Also note the primitive sphere needs NO extra SphereCollider (primitives ship one at the right size) — only the SGT branch keeps the manual `col.radius = 1f` collider.

- [ ] **Step 4: Rebuild neon + capture the B screenshot set:**

```bash
cd "/mnt/c/Users/computer/My project" && timeout -k 30 570 "/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\Users\computer\My project" -executeMethod AlienSkies.EditorTools.PlaygroundBuilder.BuildNeonVector -logFile - 2>&1 | grep -iE "error CS|PlaygroundBuilder: saved" | head -3 && timeout -k 30 570 "/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\Users\computer\My project" -executeMethod AlienSkies.EditorTools.SceneScreenshot.Capture -logFile - 2>&1 | grep -c "SceneScreenshot: wrote" && cd TestResults && for f in shot_*.png; do cp "$f" "neon_$f"; done
```

Read `neon_shot_spawn.png`, `neon_shot_wide.png`, `neon_shot_weapons.png` — verify: planets read as glowing rimmed spheres in 5 palette hues, gun is dark with a colored rim, nothing is magenta/black-broken. Iterate rim intensities if washed out or invisible (this is visual work: adjust, rebuild, recapture).

- [ ] **Step 5: Run suite** → 67/67. **Step 6: Commit**

```bash
cd "/mnt/c/Users/computer/My project" && git add Assets/AlienSkies/Editor/PlaygroundBuilder.cs Assets/AlienSkies/Materials Assets/AlienSkies/Scenes/MovementPlayground.unity Assets/AlienSkies/Prefabs/NetworkPlayer.prefab Assets/AlienSkies/Prefabs/Rocket.prefab Assets/DefaultPrefabObjects.asset && git commit -m "feat: Style B neon vector - rim-glow guns, rocket, palette planets"
```

---

### Task 4: Style A — pack textures forced through the palette

**Files:**
- Modify: `Assets/AlienSkies/Editor/PlaygroundBuilder.cs`

**Interfaces:** consumes Task 1/3 helpers; produces the `StylizedPacks` branches.

- [ ] **Step 1: Guns (A branch).** Extend the Task-3 gun styling with an else-branch:

```csharp
                else
                {
                    // Keep the pack textures but pull them into the palette: cool
                    // tint + hot palette emission so they stop reading "generic AAA".
                    var src = weaponModels[w].GetComponentsInChildren<Renderer>()[0].sharedMaterial;
                    var tinted = GetOrCreateMaterial($"StylizedGun_{w}", () => new Material(src));
                    tinted.shader = src.shader;
                    tinted.CopyPropertiesFromMaterial(src);
                    tinted.SetColor("_BaseColor", new Color(0.72f, 0.68f, 0.92f)); // cool violet-grey
                    tinted.SetColor("_EmissionColor", GunRim(w) * 2.5f);
                    tinted.EnableKeyword("_EMISSION");
                    tinted.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    EditorUtility.SetDirty(tinted);
                    foreach (var r in weaponModels[w].GetComponentsInChildren<Renderer>())
                        r.sharedMaterials = Repeat(tinted, r.sharedMaterials.Length);
                }
```

- [ ] **Step 2: Rocket visual (A branch)** — same pattern in `BuildRocketPrefab`:

```csharp
            else
            {
                var src = visual.GetComponentsInChildren<Renderer>()[0].sharedMaterial;
                var tinted = GetOrCreateMaterial("StylizedRocket", () => new Material(src));
                tinted.shader = src.shader;
                tinted.CopyPropertiesFromMaterial(src);
                tinted.SetColor("_BaseColor", new Color(0.72f, 0.68f, 0.92f));
                tinted.SetColor("_EmissionColor", AlienSkies.Palette.Amber * 2.5f);
                tinted.EnableKeyword("_EMISSION");
                tinted.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(tinted);
                foreach (var r in visual.GetComponentsInChildren<Renderer>())
                    r.sharedMaterials = Repeat(tinted, r.sharedMaterials.Length);
            }
```

(Planets A = the existing SGT else-branch from Task 3 — already done.)

- [ ] **Step 3: Rebuild packs mode + capture the A screenshot set:**

```bash
cd "/mnt/c/Users/computer/My project" && timeout -k 30 570 "/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\Users\computer\My project" -executeMethod AlienSkies.EditorTools.PlaygroundBuilder.BuildStylizedPacks -logFile - 2>&1 | grep -iE "error CS|PlaygroundBuilder: saved" | head -3 && timeout -k 30 570 "/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\Users\computer\My project" -executeMethod AlienSkies.EditorTools.SceneScreenshot.Capture -logFile - 2>&1 | grep -c "SceneScreenshot: wrote" && cd TestResults && for f in shot_*.png; do cp "$f" "packs_$f"; done
```

Read `packs_shot_spawn.png` + `packs_shot_weapons.png` — SGT planets as before, gun textured but violet-tinted with palette emission glow.

- [ ] **Step 4: Rebuild back to NEON (default for the exe), run suite, commit:**

```bash
cd "/mnt/c/Users/computer/My project" && timeout -k 30 570 "/mnt/c/Program Files/Unity/Hub/Editor/6000.5.1f1/Editor/Unity.exe" -batchmode -quit -projectPath "C:\Users\computer\My project" -executeMethod AlienSkies.EditorTools.PlaygroundBuilder.BuildNeonVector -logFile - 2>&1 | grep -iE "PlaygroundBuilder: saved" | head -1 && bash tools/run_tests.sh 2>&1 | tail -1 && git add Assets/AlienSkies/Editor/PlaygroundBuilder.cs Assets/AlienSkies/Materials Assets/AlienSkies/Scenes/MovementPlayground.unity Assets/AlienSkies/Prefabs/NetworkPlayer.prefab Assets/AlienSkies/Prefabs/Rocket.prefab Assets/DefaultPrefabObjects.asset && git commit -m "feat: Style A stylized packs - palette-tinted gun/rocket textures (SGT planets kept)"
```

---

### Task 5: Docs, exe, push, PR, verdict handoff

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: CLAUDE.md.** RESUME HERE (replace):

```markdown
> **▶ RESUME HERE (2026-07-05):** ART DIRECTION A/B (PR #11, stacked on #10): two switchable looks — Style B "Neon Vector" (flat + rim glow, zero pack textures; CURRENT DEFAULT, in the exe) vs Style A "Stylized Packs" (SGT planets + palette-tinted guns). Flip via menu AlienSkies → Build Playground (Neon Vector / Stylized Packs). Comparison shots: TestResults/neon_*.png vs packs_*.png. Awaiting user verdict; loser's branch code stays (one enum). Stack: #8 juice → #9 launch pads → #10 match win → #11 art.
```

Diary (top):

```markdown
- **2026-07-05** — PR #11 art direction A/B: Palette class (single color source), NeonRim shader (flat + HDR fresnel rim), platforms w/ emissive edge trim, amber-rim player body, HUD restyle (shadows, HP/zip bars, palette colors) — shared. Style B: neon guns/rocket/planets (no pack textures). Style A: pack textures palette-tinted + hot emission. StyleMode enum in builder, both capturable as neon_*/packs_* screenshot sets.
```

- [ ] **Step 2: Rebuild exe (neon default), commit docs, push, PR** (base `feature/match-win`):

```bash
cd "/mnt/c/Users/computer/My project" && rm -f Builds/AlienSkies.exe && bash tools/build_player.sh 2>&1 | tail -1 && git add CLAUDE.md docs/superpowers/plans/2026-07-05-art-direction.md && git commit -m "docs: RESUME HERE + diary for art direction A/B" && git push -u origin feature/art-direction && gh pr create --base feature/match-win --title "Art direction A/B: one palette, two looks - pick your game's face" --body "$(cat <<'EOF'
Stacked on PR #10. Kills the four-styles-stapled-together incoherence.

Shared foundation (both styles): Palette class = the only color source; NeonRim shader (dark flat + HDR fresnel rim); platforms get emissive edge trim; player body = amber rim silhouette; HUD restyled (drop shadows, HP + zip-cooldown bars, palette color language).

- Style B "Neon Vector" (DEFAULT, in the exe): zero pack textures - guns/rocket/planets all rim-glow flat. Coherent by construction.
- Style A "Stylized Packs": SGT textured planets + Tirgames gun textures pulled into the palette (cool tint + hot palette emission).

Switch: AlienSkies menu -> Build Playground (Neon Vector / Stylized Packs), or the matching -executeMethod entry points. Layout identical in both (same rng stream).

Screenshots: TestResults/neon_*.png vs packs_*.png (also posted in the session).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 3: Self-review checklist:** `git status --short` clean of surprises, zero unpushed commits, `gh pr view 11 --json state` OPEN, exe fresh.

- [ ] **Step 4: Hand off with the comparison.** Present both screenshot sets side by side; ask for the verdict (B default ships in the exe; playtesting A = one menu click + rebuild). Checkpoints: which world do you want to live in; enemy visibility (amber rim); do the neon planets lose too much of what you liked about the SGT ones.
