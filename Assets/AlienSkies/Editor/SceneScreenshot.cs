using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AlienSkies.EditorTools
{
    /// <summary>Captures verification screenshots of the MovementPlayground scene
    /// headlessly (-executeMethod AlienSkies.EditorTools.SceneScreenshot.Capture).
    /// Writes PNGs to TestResults/ (git-ignored).</summary>
    public static class SceneScreenshot
    {
        const string ScenePath = "Assets/AlienSkies/Scenes/MovementPlayground.unity";

        [MenuItem("AlienSkies/Capture Playground Screenshots")]
        public static void Capture()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(ScenePath);
            System.IO.Directory.CreateDirectory("TestResults");

            // Courses plan (Task 2 unwind checklist item 5): the old
            // GateSphere2 wake-and-shoot block retired along with the arena
            // routes (shot_gate2.png is gone) — replaced with one lane shot
            // per course, same FindObjectsInactive + Exit(1) pattern (Course
            // 2/3's gates are authored INACTIVE, same as GateSphere2 was, so
            // each is woken for its own shot and restored before the next).
            // Course 3 (a dive, not a flat/climbing lane) uses its own
            // side-on framing instead of the shared high-angle formula — see
            // Course3DiveShot below.
            CourseLaneShot("Gate_course1", new Vector3(66.9f, 6.5f, 38.9f), Vector3.right, "TestResults/shot_course1.png", gateStartsActive: true);
            CourseLaneShot("Gate_course2", new Vector3(-38.9f, 6.5f, 46.9f), Vector3.forward, "TestResults/shot_course2.png", gateStartsActive: false);
            // Course 3 is THE PLUNGE — the generic top-down lane formula
            // (origin + up*90 - H*40, looking along H) looks straight down the
            // throat of a diving course instead of showing the dive. Course 3
            // gets its own side-on framing instead (see Course3DiveShot).
            Course3DiveShot("TestResults/shot_course3.png");

            // Player's-eye view from spawn, looking into the arena
            Shot(new Vector3(0f, 4.6f, -8f), Quaternion.Euler(2f, 15f, 0f), "TestResults/shot_spawn.png");
            // Wide establishing shot of the galaxy field
            Shot(new Vector3(-60f, 70f, -180f), Quaternion.Euler(14f, 18f, 0f), "TestResults/shot_wide.png");
            // Looking up through the field (grapple-chain sightline)
            Shot(new Vector3(0f, 2f, 0f), Quaternion.Euler(-35f, 40f, 0f), "TestResults/shot_up.png");
            // Top-down layout shot: arena-design verification (rings + stage)
            Shot(new Vector3(0f, 230f, 0f), Quaternion.Euler(90f, 0f, 0f), "TestResults/shot_arena_top.png");

            // Weapon visuals: player prefab (viewmodel rig) + rocket, seen from the
            // first-person pivot — verifies materials/orientation without a playtest.
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AlienSkies/Prefabs/Player.prefab");
            var rocketPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AlienSkies/Prefabs/Rocket.prefab");
            if (playerPrefab != null && rocketPrefab != null)
            {
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                player.transform.position = new Vector3(0f, 500f, 0f); // isolated above the arena
                var rocket = (GameObject)PrefabUtility.InstantiatePrefab(rocketPrefab);
                rocket.transform.position = new Vector3(-0.6f, 501.4f, 3f);
                rocket.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // side profile
                Shot(new Vector3(0f, 501.6f, 0f), Quaternion.identity, "TestResults/shot_weapons.png");
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(rocket);
            }

            // FX verification: pre-warmed explosion in front of the camera using
            // the same code-built visuals CombatFx.Explosion spawns at runtime
            // (the pack-prefab instantiation this file used to do is gone) —
            // core flash sphere + expanding fresnel shell sphere, mid-detonation,
            // + six debris streaks radiating from the epicenter.
            var explosionShellShader = Shader.Find("AlienSkies/ExplosionShell");
            if (explosionShellShader == null)
            {
                Debug.LogError("SceneScreenshot: missing AlienSkies/ExplosionShell shader");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            else
            {
                var at = new Vector3(0f, 502f, 8f);
                var spawned = new System.Collections.Generic.List<GameObject>();
                var tempMats = new System.Collections.Generic.List<Material>();

                var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(shell.GetComponent<Collider>());
                shell.transform.position = at;
                shell.transform.localScale = Vector3.one * 8f;
                // Temp material instance so this doesn't churn the asset .mat —
                // same pattern as coreMat below (never `.material` in edit mode).
                var shellMat = new Material(explosionShellShader);
                shellMat.SetFloat("_Progress", 0.35f);
                shell.GetComponent<Renderer>().sharedMaterial = shellMat;
                spawned.Add(shell);
                tempMats.Add(shellMat);

                var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(core.GetComponent<Collider>());
                core.transform.position = at;
                core.transform.localScale = Vector3.one * 4f; // mid-flash size
                var coreMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                coreMat.SetColor("_BaseColor", new Color(0.75f, 0.35f, 1f) * 3f); // palette violet
                core.GetComponent<Renderer>().sharedMaterial = coreMat;
                spawned.Add(core);
                tempMats.Add(coreMat);

                Vector3[] streakDirs =
                {
                    Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
                };
                for (int i = 0; i < streakDirs.Length; i++)
                {
                    var streakGO = new GameObject("Streak");
                    var line = streakGO.AddComponent<LineRenderer>();
                    line.positionCount = 2;
                    line.SetPosition(0, at + streakDirs[i] * 3f);
                    line.SetPosition(1, at + streakDirs[i] * 7f);
                    line.startWidth = 0.22f;
                    line.endWidth = 0.05f;
                    var streakMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    streakMat.SetColor("_BaseColor", i % 3 == 0
                        ? new Color(1f, 0.75f, 0.25f) * 3f   // amber sparks
                        : new Color(0.55f, 0.9f, 1f) * 3f);  // cyan sparks
                    line.sharedMaterial = streakMat;
                    spawned.Add(streakGO);
                    tempMats.Add(streakMat);
                }

                Shot(new Vector3(0f, 501.6f, 0f), Quaternion.identity, "TestResults/shot_fx.png");
                foreach (var go in spawned) Object.DestroyImmediate(go);
                foreach (var m in tempMats) Object.DestroyImmediate(m);
            }

            // Kill impact frame: the ImpactFrame shader's full-screen pop,
            // mid-flash, over the arena view — verifies the overlay reads
            // without a live kill.
            var framePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AlienSkies/Prefabs/ImpactFrame.prefab");
            var impactFrameShader = Shader.Find("AlienSkies/ImpactFrame");
            if (framePrefab == null || impactFrameShader == null)
            {
                Debug.LogError("SceneScreenshot: missing ImpactFrame prefab or AlienSkies/ImpactFrame shader");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            else
            {
                var camPos = new Vector3(0f, 4.6f, -8f);
                var camRot = Quaternion.Euler(2f, 15f, 0f);
                var frame = (GameObject)PrefabUtility.InstantiatePrefab(framePrefab);
                // Posed on the shot camera's own frame, 2m out: the camera is
                // yawed, so an axis-aligned quad would leave background gaps
                // at the frame edges.
                frame.transform.SetPositionAndRotation(
                    camPos + camRot * Vector3.forward * 2f, camRot);
                // Temp material instance so this doesn't churn the asset .mat —
                // same pattern as coreMat above (never `.material` in edit mode).
                var frameMat = new Material(impactFrameShader);
                frameMat.SetFloat("_Progress", 0.3f);
                frame.GetComponent<Renderer>().sharedMaterial = frameMat;
                Shot(camPos, camRot, "TestResults/shot_impactframe.png");
                Object.DestroyImmediate(frame);
                Object.DestroyImmediate(frameMat);
            }

            // Speed-line overlay verification: same code-built temp-material
            // pattern as ImpactFrame/GlitchPass above (never the shared asset
            // material in edit mode) — a quad 2m in front of a shot camera,
            // at a fixed mid-range intensity, torn down after the shot.
            var speedLinesShader = Shader.Find("AlienSkies/SpeedLines");
            if (speedLinesShader == null)
            {
                Debug.LogError("SceneScreenshot: missing AlienSkies/SpeedLines shader");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
            else
            {
                var camPos = new Vector3(0f, 4.6f, -8f);
                var camRot = Quaternion.Euler(2f, 15f, 0f);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "SpeedLinesShot";
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetPositionAndRotation(camPos + camRot * Vector3.forward * 2f, camRot);
                quad.transform.localScale = new Vector3(4.2f, 2.4f, 1f);
                var speedLinesMat = new Material(speedLinesShader);
                speedLinesMat.SetFloat("_Intensity", 0.55f);
                quad.GetComponent<Renderer>().sharedMaterial = speedLinesMat;
                Shot(camPos, camRot, "TestResults/shot_speedlines.png");
                Object.DestroyImmediate(quad);
                Object.DestroyImmediate(speedLinesMat);
            }

            // Heat verification: MusicManager drives the global shader float
            // "_AlienHeat" (0..1) from music intensity so world emission burns
            // hotter with the track — the same wide arena framing at both
            // ends of that range lets the two PNGs be diffed against each
            // other. Reset to 0 after: this is GLOBAL shader state that would
            // otherwise leak past Capture() into whatever runs next.
            var wideCamPos = new Vector3(-60f, 70f, -180f);
            var wideCamRot = Quaternion.Euler(14f, 18f, 0f);
            Shader.SetGlobalFloat("_AlienHeat", 0f);
            Shot(wideCamPos, wideCamRot, "TestResults/shot_heat0.png");
            Shader.SetGlobalFloat("_AlienHeat", 1f);
            Shot(wideCamPos, wideCamRot, "TestResults/shot_heat1.png");
            Shader.SetGlobalFloat("_AlienHeat", 0f);

            Debug.Log("SceneScreenshot: done");
        }

        static void Shot(Vector3 pos, Quaternion rot, string path)
        {
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.farClipPlane = 2000f;
            cam.allowHDR = true;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());

            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
            Debug.Log($"SceneScreenshot: wrote {path}");
        }

        /// <summary>Courses plan (Task 2 unwind checklist item 5): high-angle
        /// lane view for one course — camera at origin + up*90 - H*40, looking
        /// along H, per the plan's own framing. `gateStartsActive` mirrors
        /// which courses' gates PlaygroundBuilder leaves active at boot
        /// (Course 1 only) vs. inactive (Course 2/3) — same wake-before/
        /// restore-after pattern the retired GateSphere2 shot used.</summary>
        static void CourseLaneShot(string gateName, Vector3 origin, Vector3 heading, string path, bool gateStartsActive)
        {
            var gate = FindGateOrFail(gateName);
            if (gate == null) return; // FindGateOrFail already Exit(1)'d in batchmode

            if (!gateStartsActive) gate.SetActive(true);
            Vector3 h = heading.normalized;
            var camPos = origin + Vector3.up * 90f - h * 40f;
            var camRot = Quaternion.LookRotation(h, Vector3.up);
            Shot(camPos, camRot, path);
            if (!gateStartsActive) gate.SetActive(false); // restore before the remaining shots
        }

        /// <summary>Side-on framing for course 3 (THE PLUNGE) — the generic
        /// CourseLaneShot formula looks straight down a diving course rather
        /// than showing the dive. Computed from course 3's own pinned
        /// origin/heading (PlaygroundBuilder.cs) plus the "known" advance/drop
        /// already recorded there: the plan's segment-sum arithmetic (StartPad
        /// 6 + DiveShaft 16 + CombatPocket 33.5 + GrappleGap(2,80) 96 +
        /// DiveShaft 16 + PlatformRun(3) ~52.5 + GateFinish 10 ~= 230 m
        /// advance) and its two DiveShaft(50) segments (100 m total drop,
        /// 120 -> ~20). The camera sits at the course's own midpoint,
        /// offset sideways along Right (NOT along H, so it clears the 25 m
        /// corridor instead of flying down the middle of it) and up, looking
        /// back AT that midpoint — a side profile of the fall.</summary>
        static void Course3DiveShot(string path)
        {
            var origin = new Vector3(-60f, 120f, -60f);
            var heading = Vector3.left;
            const float knownAdvance = 230f; // see summary
            const float knownDrop = 100f;    // two DiveShaft(50) segments

            Vector3 h = heading.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, h).normalized; // NewCursor's own convention
            Vector3 end = origin + h * knownAdvance + Vector3.down * knownDrop;
            Vector3 mid = (origin + end) * 0.5f;

            var camPos = mid + right * 70f + Vector3.up * 30f;
            var camRot = Quaternion.LookRotation((mid - camPos).normalized, Vector3.up);
            Shot(camPos, camRot, path);
        }

        /// <summary>Same FindObjectsInactive + Exit(1) pattern the old
        /// GateSphere2 lookup used: FindObjectsByType + Include (never
        /// FindFirstObjectByType, which excludes inactive objects and would
        /// silently return null for Course 2/3's gates) — matched by name
        /// since an inactive root can't be found via GameObject.Find either.</summary>
        static GameObject FindGateOrFail(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == name) return t.gameObject;

            Debug.LogError($"SceneScreenshot: {name} not found (inactive-inclusive search)");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return null;
        }
    }
}
