using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AlienSkies.Movement;
using AlienSkies.Combat;

namespace AlienSkies.EditorTools
{
    /// <summary>Builds the MovementPlayground scene from code so it is
    /// rebuildable headlessly (-executeMethod). Graybox geometry with a
    /// TTGL-flavored cosmic dressing: nebula skybox, emissive galaxies, bloom.</summary>
    public static class PlaygroundBuilder
    {
        const string ScenePath = "Assets/AlienSkies/Scenes/MovementPlayground.unity";
        const string ConfigPath = "Assets/AlienSkies/Config/MovementConfig.asset";
        const string CombatConfigPath = "Assets/AlienSkies/Config/CombatConfig.asset";
        const string EnemyCombatConfigPath = "Assets/AlienSkies/Config/EnemyCombatConfig.asset";
        const string RunConfigPath = "Assets/AlienSkies/Config/RunConfig.asset";
        const string PostFXPath = "Assets/AlienSkies/Config/PlaygroundPostFX.asset";
        const string MatDir = "Assets/AlienSkies/Materials";

        // One sun rotation shared by the scene light and the planet shader's
        // day/night shading so lit hemispheres agree with the directional light.
        static readonly Quaternion SunRotation = Quaternion.Euler(45f, 30f, 0f);
        static Vector3 SunDir => -(SunRotation * Vector3.forward);

        // TTGL cosmic palette: saturated, varied, bold
        static readonly Color[] GalaxyPalette =
        {
            new Color(1.00f, 0.18f, 0.53f), // magenta
            new Color(0.22f, 0.88f, 1.00f), // cyan
            new Color(0.60f, 0.36f, 1.00f), // violet
            new Color(0.29f, 1.00f, 0.69f), // spiral green
            new Color(1.00f, 0.70f, 0.12f), // amber
            new Color(0.95f, 0.35f, 0.95f), // pink
        };

        [MenuItem("AlienSkies/Build Playground (Neon Vector)")]
        public static void BuildNeonVector() { Build(); }

        public static void Build()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return; // user cancelled the save prompt

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var config = AssetDatabase.LoadAssetAtPath<MovementConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MovementConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            var combatConfig = AssetDatabase.LoadAssetAtPath<CombatConfig>(CombatConfigPath);
            if (combatConfig == null)
            {
                combatConfig = ScriptableObject.CreateInstance<CombatConfig>();
                AssetDatabase.CreateAsset(combatConfig, CombatConfigPath);
            }

            var runConfig = AssetDatabase.LoadAssetAtPath<AlienSkies.Run.RunConfig>(RunConfigPath);
            if (runConfig == null)
            {
                runConfig = ScriptableObject.CreateInstance<AlienSkies.Run.RunConfig>();
                AssetDatabase.CreateAsset(runConfig, RunConfigPath);
            }

            var enemyCombatConfig = AssetDatabase.LoadAssetAtPath<CombatConfig>(EnemyCombatConfigPath);
            if (enemyCombatConfig == null)
            {
                enemyCombatConfig = ScriptableObject.CreateInstance<CombatConfig>();
                AssetDatabase.CreateAsset(enemyCombatConfig, EnemyCombatConfigPath);
            }
            // Builder owns the values every build (snapshot rule, §7 of the field
            // manual): enemies are a low-HP config swap on the player pattern.
            enemyCombatConfig.Settings.MaxHealth = 40f;
            // Playtest verdict 2026-08-29 ("just 1taps me easy"): the shared
            // 80-dmg rocket was PvP duel tuning — in a speedrun, death restarts
            // the run, so enemy rockets pressure (30) instead of executing.
            // Knockback stays full: being SHOVED mid-run is the fun danger.
            // The player's own rocket keeps 80 (still one-shots 40 HP drones).
            enemyCombatConfig.Settings.Weapons[0].Damage = 30f;
            // Pinned explicitly now that the C# defaults carry the player's
            // 200/26 cannonball tune: a recreated enemy asset must NOT seed
            // those — enemy rockets stay dodgeable with the gentler shove.
            enemyCombatConfig.Settings.Weapons[0].ProjectileSpeed = 150f;
            enemyCombatConfig.Settings.Weapons[0].Knockback = 22f;
            EditorUtility.SetDirty(enemyCombatConfig);
            // Playtest verdict 2026-08-29 ("the rocket needs to be faster
            // jumpier even, like cannon balls"): the rocket is the hero
            // weapon — snappier flight, harder self-kick on rocket jumps.
            // PLAYER config only: enemy rockets stay 150/22 (dodgeable, and
            // their shove is tuned above with the 30-dmg pressure change).
            combatConfig.Settings.Weapons[0].ProjectileSpeed = 200f;
            combatConfig.Settings.Weapons[0].Knockback = 26f;
            EditorUtility.SetDirty(combatConfig);

            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/AlienSkies", "Materials");

            // Dim sun: emissives and the sky carry the scene
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f; // planets are lit geometry now — they need real light
            sun.color = new Color(0.85f, 0.8f, 1f);
            sun.transform.rotation = SunRotation;

            BuildAtmosphere();
            BuildPostFX();
            // Arena first: BuildPlayer reads the arena's SpawnPointRegistry to
            // place the player instance, so the registry must already exist.
            // Courses plan (Task 2, round 3): gates are no longer BuildArena
            // locals — each course's own GateFinish segment builds its gate
            // (ring + beacon + trigger, same shared BuildGate helper) while
            // building that course inside BuildPlayer, where RunManager.Levels
            // is also authored — so the CourseResult holding the gate
            // reference never has to be threaded back out through BuildArena.
            BuildArena();
            BuildPlayer(config, combatConfig, runConfig, enemyCombatConfig);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"PlaygroundBuilder: saved {ScenePath}");
        }

        static void BuildAtmosphere()
        {
            var sky = GetOrCreateMaterial("CosmicSkybox", () =>
                new Material(Shader.Find("AlienSkies/CosmicSkybox")));
            // Set look params every build (idempotent) so the builder, not stale
            // material assets, owns the sky's look.
            sky.SetFloat("_NebulaIntensity", 1.0f);
            sky.SetFloat("_StarDensity", 0.85f);
            sky.SetFloat("_StarBrightness", 3.6f);
            sky.SetFloat("_BandIntensity", 0.55f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.20f, 0.15f, 0.32f);

            // Depth haze so the far galaxy field recedes instead of pasting flat
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0022f;
            RenderSettings.fogColor = new Color(0.05f, 0.02f, 0.10f);
        }

        static void BuildPostFX()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostFXPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, PostFXPath);
            }
            // Override values every build so the builder owns the grade
            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>();
            bloom.intensity.Override(1.6f);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.65f);
            if (!profile.TryGet(out ColorAdjustments grading)) grading = profile.Add<ColorAdjustments>();
            grading.saturation.Override(15f);
            grading.postExposure.Override(0.2f);
            if (!profile.TryGet(out Vignette vignette)) vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.4f);
            EditorUtility.SetDirty(profile);

            var volGO = new GameObject("PostFX");
            var vol = volGO.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.profile = profile;
        }

        const string PrefabDir = "Assets/AlienSkies/Prefabs";

        static void BuildPlayer(MovementConfig config, CombatConfig combatConfig, AlienSkies.Run.RunConfig runConfig, CombatConfig enemyCombatConfig)
        {
            // --- Player prefab (spawned by Fish-Net on connect, not placed in scene)
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/AlienSkies", "Prefabs");

            var player = new GameObject("Player");
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            // Graphical rig: detached at runtime and tick-smoothed, so visuals glide
            // between network ticks while the simulation root steps (judder fix).
            var graphical = new GameObject("Graphical").transform;
            graphical.SetParent(player.transform, false);

            // Visible body so the other player sees you (owner hides it at runtime)
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(graphical, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            // Amber rim silhouette: the enemy reads as a burning orange outline
            // at any distance — Gurren orange, in the shared style language.
            var bodyMat = NeonMaterial("NeonBody", AlienSkies.Palette.Amber, 3.2f, 0.25f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(graphical, false);
            pivot.localPosition = new Vector3(0f, 1.6f, 0f);

            // Speed-feel wiring: three owners, three transforms — pitch lives
            // on CameraPivot (PlayerMotor.cs), roll lives on RollPivot
            // (SpeedFeel, below), and shake lives on the camera GO itself
            // (CombatFeedback.ShakeTarget, unchanged) — no two effects ever
            // write the same transform.
            var rollPivot = new GameObject("RollPivot").transform;
            rollPivot.SetParent(pivot, false);
            rollPivot.localPosition = Vector3.zero;
            rollPivot.localRotation = Quaternion.identity;

            var camGO = new GameObject("MainCamera") { tag = "MainCamera" };
            camGO.transform.SetParent(rollPivot, false);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.farClipPlane = 2000f;
            cam.allowHDR = true;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            camGO.AddComponent<AudioListener>();

            var motor = player.AddComponent<PlayerMotor>();
            motor.Config = config;
            motor.CameraPivot = pivot;
            motor.Graphical = graphical;
            motor.BodyRenderer = body.GetComponent<Renderer>();

            // Speed-feel: FOV kick, strafe roll (RollPivot, not the camera
            // itself), and a speed-line overlay quad — all null-guarded no-ops
            // until wired, wired here.
            var speedLinesMat = GetOrCreateMaterial("SpeedLines", () => new Material(Shader.Find("AlienSkies/SpeedLines")));
            var speedFeel = camGO.AddComponent<SpeedFeel>();
            speedFeel.Motor = motor;
            speedFeel.Cam = cam;
            speedFeel.RollTarget = rollPivot;
            speedFeel.SpeedLinesMaterial = speedLinesMat;

            // Wind layer (guardrail 3): same site and Motor reference as
            // SpeedFeel above — presence/pitch derive from Motor.Core.Velocity,
            // nothing else to wire.
            var windAudio = camGO.AddComponent<WindAudio>();
            windAudio.Motor = motor;

            var hud = player.AddComponent<MovementHUD>();
            hud.Motor = motor;

            // --- Combat: audio, weapons rig (owner-only, under the camera), PlayerCombat
            var fireAudio = camGO.AddComponent<AudioSource>();
            fireAudio.playOnAwake = false;
            fireAudio.spatialBlend = 0f;

            var rigGO = new GameObject("WeaponRig");
            rigGO.transform.SetParent(pivot, false);
            rigGO.transform.localPosition = new Vector3(0.3f, -0.34f, 0.55f);
            rigGO.transform.localRotation = Quaternion.identity;

            var weaponModels = new GameObject[3];
            for (int w = 0; w < 3; w++)
            {
                var steel = new Color(0.10f, 0.11f, 0.18f); // neutral dark body; accent lives on the edges
                var gunMat = NeonMaterial($"NeonGun_{w}", GunRim(w), 2.0f, 0.6f, rimPower: 3.2f, baseColor: steel);
                weaponModels[w] = BuildProceduralWeapon(w, gunMat);
                weaponModels[w].transform.SetParent(rigGO.transform, false);
                weaponModels[w].SetActive(w == 0);
            }

            var combat = player.AddComponent<PlayerCombat>();
            combat.Config = combatConfig;
            combat.RocketPrefab = BuildRocketPrefab();
            combat.FireAudio = fireAudio;
            // CC0 audio (Kenney Sci-fi Sounds, see Assets/AlienSkies/Audio/CC0/PROVENANCE.md)
            // replaces the unverified-license Laser Weapons Sound Pack.
            combat.RocketClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AlienSkies/Audio/CC0/rocket_fire.ogg");
            // Short punchy blast (playtest: the continuous beam read as a railgun)
            combat.RifleClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AlienSkies/Audio/CC0/rifle_fire.ogg");
            combat.ShotgunClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AlienSkies/Audio/CC0/shotgun_fire.ogg");

            var rig = rigGO.AddComponent<ViewmodelRig>();
            rig.Motor = motor;
            rig.Combat = combat;
            rig.WeaponModels = weaponModels;

            hud.Combat = combat;

            combat.ExplosionShellMaterial = GetOrCreateMaterial("ExplosionShell", () => new Material(Shader.Find("AlienSkies/ExplosionShell")));
            if (combat.ExplosionShellMaterial == null) Debug.LogError("missing AlienSkies/ExplosionShell shader");

            var feedback = player.AddComponent<CombatFeedback>();
            feedback.Combat = combat;
            feedback.ShakeTarget = camGO.transform;
            feedback.FeedbackAudio = fireAudio;
            feedback.ImpactFramePrefab = BuildImpactFramePrefab();
            if (feedback.ImpactFramePrefab == null) Debug.LogError("missing impact frame prefab");
            feedback.GlitchPassPrefab = BuildGlitchPassPrefab();
            if (feedback.GlitchPassPrefab == null) Debug.LogError("missing glitch pass prefab");
            hud.Feedback = feedback;

            string prefabPath = $"{PrefabDir}/Player.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(player, prefabPath);
            Object.DestroyImmediate(player);

            // The player exists from t=0 — no connect flow, no scene camera.
            // NOTE ON SCOPE: this lands inside BuildPlayer(MovementConfig, CombatConfig),
            // which has NO `scene` local (that lives in Build()) — use the single-argument
            // InstantiatePrefab overload: it lands in the active scene, and the builder's
            // freshly created scene is the only one open (NewSceneMode.Single).
            var playerPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AlienSkies/Prefabs/Player.prefab");
            var playerInstance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefabAsset);
            playerInstance.name = "Player";
            // First wing spawn — the registry pattern already used elsewhere in the file:
            var pts = Object.FindFirstObjectByType<SpawnPointRegistry>().Points;
            playerInstance.transform.SetPositionAndRotation(pts[0].position, pts[0].rotation);

            // Practice bot: press B to spawn/despawn one.
            var game = new GameObject("Game");
            var bots = game.AddComponent<AlienSkies.Bots.BotSpawner>();
            bots.PlayerPrefab = prefab;

            // Dynamic music: streams user tracks from StreamingAssets/Music.
            new GameObject("MusicManager").AddComponent<AlienSkies.Audio.MusicManager>();

            // Run loop: scene singleton driving RunCore (finds player/music at runtime).
            var runGO = new GameObject("RunManager");
            var runMgr = runGO.AddComponent<AlienSkies.Run.RunManager>();
            runMgr.Config = runConfig;

            // The Voice of the Machine: finds RunManager/PlayerCombat/MusicManager
            // at runtime, no wiring needed here.
            new GameObject("MachineVoice").AddComponent<AlienSkies.Run.MachineVoice>();

            // Boot title card: glitch-materialize "ALIEN SKIES", dismissed by
            // any keyboard press. No wiring needed here either.
            new GameObject("TitleCard").AddComponent<AlienSkies.Run.TitleCard>();

            // Enemies: the practice-bot pattern (this same Player.prefab + BotDriver).
            // Most posts are stationary; each course also converts one post into an
            // orbiter that strafes a player-facing ring (BotBrain edge-sensing keeps
            // it off the ledge — see BotBrain.EdgeAhead / BotDriver's probe). Low HP
            // via config swap, magenta threat-rim body — verified against the
            // NeonBody recipe above.
            var enemyBodyMat = NeonMaterial("NeonEnemyBody", AlienSkies.Palette.Magenta, 3.2f, 0.25f);

            var enemyGO = new GameObject("EnemySpawner");
            var enemies = enemyGO.AddComponent<AlienSkies.Run.EnemySpawner>();
            enemies.PlayerPrefab = playerPrefabAsset; // the loaded Player.prefab
            enemies.EnemyConfig = enemyCombatConfig;
            enemies.EnemyBodyMaterial = enemyBodyMat;
            // Beam turrets (Task 3): CC0 audio (Kenney Sci-fi Sounds), same
            // clip the player's own rifle uses — see the RifleClip note above.
            enemies.TurretFireClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/AlienSkies/Audio/CC0/rifle_fire.ogg");

            // Courses plan (Task 2): the three hand-authored arena routes
            // (Sphere 1/2/3, their stage-post literals, and their hand-placed
            // gates) are retired. CourseBuilder walks each pinned origin/
            // heading through the segment specs below (Task 2 Step 1, round-3
            // arithmetic) and hands back a CourseResult — Start, Gate, and the
            // combat waypoints its CombatPocket segments dropped — so LevelDefs
            // are authored right where those results are already in scope,
            // instead of threading gate references back out through BuildArena.

            // COURSE 1 — THE CROSSING: origin 20 m beyond Wing_0's outer edge
            // (NE), heading +X. Advance hard-capped at <=330 m (pinned
            // geometry); per-segment sum (Task 2 Step 1, SINGLE SOURCE):
            // StartPad[12] + PlatformRun(3)[~52.5] + GrappleGap(2,60)[~76]
            // + CombatPocket(2,orbiter)[33.5] + LaunchWeave(4)[~50]
            // + CombatPocket(3,false)[33.5] + PlatformRun(2)[35] + GateFinish[10]
            // ~= 302 m <= 330, with jitter margin.
            var course1Origin = new Vector3(66.9f, 6.5f, 38.9f);
            var course1Rng = new System.Random(101);
            var c1 = CourseBuilder.NewCursor(course1Origin, Vector3.right);
            var course1 = new CourseBuilder.CourseResult();
            CourseBuilder.StartPad("course1", ref c1, course1);
            CourseBuilder.PlatformRun("course1", ref c1, course1, course1Rng, 3);
            CourseBuilder.GrappleGap("course1", ref c1, course1, course1Rng, 2, 60f, worldIndexStart: 100);
            CourseBuilder.CombatPocket("course1", ref c1, course1, course1Rng, 2, true);
            // Cross-cut (guardrail 11, crosscuts plan): swing over the weave,
            // kick past the pocket, trading kill points for time — the
            // guardrail's intended tension. Draws nothing from course1Rng.
            CourseBuilder.BeginCrossCut("course1", ref c1, course1,
                new Vector3(-14f, 34f, 22f), "grapple+launch", 120);
            CourseBuilder.LaunchWeave("course1", ref c1, course1, course1Rng, 4, worldIndexStart: 110);
            CourseBuilder.CombatPocket("course1", ref c1, course1, course1Rng, 3, false);
            CourseBuilder.PlatformRun("course1", ref c1, course1, course1Rng, 2);
            CourseBuilder.EndCrossCut("course1", ref c1, course1);
            CourseBuilder.GateFinish("course1", ref c1, course1, course1Rng);
            CourseBuilder.AssertCourseBounds("course1", course1Origin, c1, 330f);
            CourseBuilder.AssertCrossCutDeclared("course1", course1);

            // COURSE 2 — THE CLIMB: origin at Wing_1's outer edge (NW),
            // heading +Z, no standoff (z_end <= ~401, so advance cap =
            // 401 - 46.9 = 354.1). Elevation sum (Task 2 Step 1, SINGLE
            // SOURCE): 6.5 (origin y) + LaunchChain(4)[+43.8] + CombatPocket
            // (flat) + GrappleGap[+15] + LaunchChain(3)[+29.8]
            // + LaunchWeave(4, climbPerRing 4)[+16] ~= 111.1 per the plan's
            // own arithmetic. DEVIATION (flagged in the task report with the
            // real numbers): the actual CourseBuilder.LaunchWeave formula
            // (entryRise 1.8 + (rings-1)*(baseRise+climbPerRing)) computes
            // +19.8 for these args, not +16 — real total ~114.9, which still
            // sits inside the pinned 110-130 assert band, so the call is left
            // exactly as specified rather than hand-tuned to hit the plan's
            // illustrative figure. LaunchChain lateral jitter pinned to the
            // -x side (this course's Right is +X per NewCursor's heading-+Z
            // rule) for margin against the Ring_1 perch disc at
            // (-33.3, 20, 97.5) — cheap insurance; the plan does not pin a
            // magnitude, so 3 m is used here (same order as PlatformRun's own
            // lateral draw elsewhere in this file).
            var course2Origin = new Vector3(-38.9f, 6.5f, 46.9f);
            var course2Rng = new System.Random(102);
            var c2 = CourseBuilder.NewCursor(course2Origin, Vector3.forward);
            var course2 = new CourseBuilder.CourseResult();
            CourseBuilder.StartPad("course2", ref c2, course2);
            CourseBuilder.LaunchChain("course2", ref c2, course2, course2Rng, 4, worldIndexStart: 200, lateralJitterRange: -3f);
            CourseBuilder.CombatPocket("course2", ref c2, course2, course2Rng, 2, false);
            CourseBuilder.GrappleGap("course2", ref c2, course2, course2Rng, 2, 60f, elevationGain: 15f, worldIndexStart: 210);
            // Cross-cut (guardrail 11, crosscuts plan): x NEGATIVE — the
            // course's own convention pins lateral margin to the -x side,
            // away from the Ring_1 perch. Draws nothing from course2Rng.
            CourseBuilder.BeginCrossCut("course2", ref c2, course2,
                new Vector3(-10f, 42f, 20f), "grapple", 250);
            CourseBuilder.LaunchChain("course2", ref c2, course2, course2Rng, 3, worldIndexStart: 220, lateralJitterRange: -3f);
            CourseBuilder.LaunchWeave("course2", ref c2, course2, course2Rng, 4, climbPerRing: 4f, worldIndexStart: 230);
            CourseBuilder.EndCrossCut("course2", ref c2, course2);
            // Pre-gate anchor world 15-20 m before the gate (plan's own
            // range; 18 m picked as the midpoint) so the final approach stays
            // coverable by a verb instead of a blind final glide.
            CourseBuilder.GateFinish("course2", ref c2, course2, course2Rng, preGateAnchorDistance: 18f, worldIndexStart: 240);
            CourseBuilder.AssertCourseBounds("course2", course2Origin, c2, 354.1f);
            CourseBuilder.AssertCrossCutDeclared("course2", course2);
            // Courses verify round: the elevation-sum comment above pins THE
            // CLIMB's finish to a 110-130 m total-elevation-gain band (actual
            // ~114.9 m per that same arithmetic) — assert it instead of only
            // trusting the comment.
            CourseBuilder.AssertFinishElevation("course2", c2, 110f, 130f);

            // COURSE 3 — THE PLUNGE: origin (-60, 120, -60), heading -X;
            // corridor z in [-85,-35] is exactly the shared +-25 m half-width
            // around this origin's z=-60, so no course-specific override is
            // needed there. DEVIATION (flagged in the task report): the plan
            // does not pin an explicit "advance <= X" cap for this course the
            // way it does for Course 1 (330 m, explicit) and Course 2 (derived
            // from "z_end <= ~401"); this task's own segment sum (StartPad 6 +
            // DiveShaft 16 + CombatPocket 33.5 + GrappleGap(2,80) 96 +
            // DiveShaft 16 + PlatformRun(3) ~52.5 + GateFinish 10 ~= 230)
            // lands the gate near x ~= -290, matching the plan's own "Gate
            // lands ~= (-293, 21, -60)" to within a few meters — so 260 m
            // (~13% margin over that ~230 m expectation, in the same spirit
            // as Course 1's ~330-over-~302 margin) is used as the cap here.
            var course3Origin = new Vector3(-60f, 120f, -60f);
            var course3Rng = new System.Random(103);
            var c3 = CourseBuilder.NewCursor(course3Origin, Vector3.left);
            var course3 = new CourseBuilder.CourseResult();
            CourseBuilder.StartPad("course3", ref c3, course3);
            CourseBuilder.DiveShaft("course3", ref c3, course3, course3Rng, 50f, worldIndexStart: 300);
            CourseBuilder.CombatPocket("course3", ref c3, course3, course3Rng, 2, true);
            // Cross-cut (guardrail 11, crosscuts plan): a dive course's main
            // path is already fast, so the cut must bypass more (through the
            // extended PlatformRun) to matter. Draws nothing from course3Rng.
            CourseBuilder.BeginCrossCut("course3", ref c3, course3,
                new Vector3(0f, 38f, 30f), "grapple", 330);
            CourseBuilder.GrappleGap("course3", ref c3, course3, course3Rng, 2, 80f, worldIndexStart: 310);
            CourseBuilder.DiveShaft("course3", ref c3, course3, course3Rng, 50f, worldIndexStart: 320);
            CourseBuilder.PlatformRun("course3", ref c3, course3, course3Rng, 3);
            CourseBuilder.EndCrossCut("course3", ref c3, course3);
            CourseBuilder.GateFinish("course3", ref c3, course3, course3Rng);
            CourseBuilder.AssertCourseBounds("course3", course3Origin, c3, 260f);
            CourseBuilder.AssertCrossCutDeclared("course3", course3);

            // Corridor tenants (round-3 audit, recorded per Task 2 Step 1 —
            // NOT implemented as a runtime collider scan: that capability
            // ("CourseBuilder LOGS every pre-existing collider inside a lane
            // box") would live in CourseBuilder.cs, which is outside this
            // task's file scope; flagged in the task report). ACCEPTED:
            // Course 1's lane runs near backdrop Planet_24 (collider +
            // PlanetSurface, ~25 m over the lane, PROMOTED to the course's
            // intended visible cross-cut) and Planet_21 clipping the edge,
            // plus Asteroid_05/06 over the start zone. Course 3's lane passes
            // ~27 m above the shattered world (Planet_06, center
            // (-72,34,-78), r9) — an accepted, forgiving below-lane tenant.
            // The Colossus/Machine collider-free sweeps are spectacle; expect
            // arm-through-lane frames.

            // Fallback loadout for EnemySpawner's serialized fields (used only
            // if RunManager.Instance is ever null at Start — see
            // EnemySpawner.HandleLevelChanged); re-pointed to Course 1's own
            // result (round 3: CourseResult carries no radius field, so the
            // constant is stated directly here, matching every LevelDef below).
            enemies.Posts = course1.Posts.ToArray();
            enemies.Orbiters = course1.Orbiters.ToArray();
            enemies.OrbiterRadius = 7f;

            // Three courses replace the three hand-authored arena routes.
            // Player still spawns at Course 1's own start; RunManager.Start's
            // SelectLevel(0) positions them there on boot.
            runMgr.Levels = new[]
            {
                new AlienSkies.Run.LevelDef
                {
                    Name = "COURSE 1 — THE CROSSING",
                    Start = course1.Start,
                    ParTime = 90f, STargetScore = 2000,
                    Posts = course1.Posts.ToArray(), Orbiters = course1.Orbiters.ToArray(), OrbiterRadius = 7f,
                    // Two CombatPocket calls on this course; the turret goes
                    // on the SECOND pocket only (Task 3 placement plan).
                    Turrets = new[] { course1.Turrets[1] },
                    Gate = course1.Gate, BestKeyPrefix = "course1",
                },
                new AlienSkies.Run.LevelDef
                {
                    Name = "COURSE 2 — THE CLIMB",
                    Start = course2.Start,
                    ParTime = 75f, STargetScore = 2000,
                    Posts = course2.Posts.ToArray(), Orbiters = course2.Orbiters.ToArray(), OrbiterRadius = 7f,
                    // Single CombatPocket on this course — one turret.
                    Turrets = new[] { course2.Turrets[0] },
                    Gate = course2.Gate, BestKeyPrefix = "course2",
                },
                new AlienSkies.Run.LevelDef
                {
                    Name = "COURSE 3 — THE PLUNGE",
                    Start = course3.Start,
                    ParTime = 60f, STargetScore = 2200,
                    Posts = course3.Posts.ToArray(), Orbiters = course3.Orbiters.ToArray(), OrbiterRadius = 7f,
                    // Single CombatPocket on this course — one turret.
                    Turrets = new[] { course3.Turrets[0] },
                    Gate = course3.Gate, BestKeyPrefix = "course3",
                },
            };
            // Deactivate Course 2's and Course 3's gates only now that Levels
            // already holds the reference (scope-bridge review): any future
            // lookup that reaches for a gate by searching the scene instead of
            // reading Levels would miss an inactive object and silently break
            // the level.
            course2.Gate.SetActive(false);
            course3.Gate.SetActive(false);
        }

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

        static GameObject BuildRocketPrefab()
        {
            // Always rebuilt so builder fixes land, but SAVED OVER the same path
            // (never DeleteAsset): that keeps the GUID stable. The player prefab
            // is rewired to the fresh object in this same Build() run — commit
            // Rocket.prefab + Player.prefab TOGETHER, always; a reverted player
            // prefab points at dead fileIDs and rockets silently stop spawning
            // (playtest 2026-07-05).
            string path = $"{PrefabDir}/Rocket.prefab";

            var rocket = new GameObject("Rocket");
            var r = rocket.AddComponent<Rocket>();
            // CC0 audio (Kenney Sci-fi Sounds) — see the RocketClip note above.
            r.ExplosionClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/AlienSkies/Audio/CC0/explosion.ogg");

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.name = "Visual";
            visual.transform.SetParent(rocket.transform, false);
            // Capsule's long axis is Y; rotate so the nose points forward (+Z).
            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var rocketMat = NeonMaterial("NeonRocket", AlienSkies.Palette.Amber, 2.2f, 0.6f, rimPower: 3.0f, baseColor: new Color(0.10f, 0.11f, 0.18f));
            foreach (var rend in visual.GetComponentsInChildren<Renderer>())
                rend.sharedMaterials = Repeat(rocketMat, rend.sharedMaterials.Length);

            var trail = rocket.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.startWidth = 0.25f;
            trail.endWidth = 0.02f;
            // Guardrail 1 audit 2026-08-29: this was `new Material(...)` inline —
            // the EXACT landmine documented one function down (SaveAsPrefabAsset
            // nulls in-memory materials) — so the shipped rocket flew trailless.
            var trailMat = GetOrCreateMaterial("RocketTrail", () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetColor("_BaseColor", new Color(0.75f, 0.4f, 1f) * 3f); // palette violet
                return m;
            });
            trail.sharedMaterial = trailMat;

            var prefab = PrefabUtility.SaveAsPrefabAsset(rocket, path);
            Object.DestroyImmediate(rocket);
            return prefab;
        }

        /// <summary>Full-screen quad driven by the ImpactFrame shader — the
        /// pack-free kill-pop prefab. The material MUST be asset-backed
        /// (GetOrCreateMaterial, not `new Material` inline): SaveAsPrefabAsset
        /// silently nulls references to in-memory materials, which is exactly
        /// how Rocket.prefab's TrailRenderer slot ended up `{fileID: 0}`. An
        /// in-memory material here ships a kill pop that renders nothing.</summary>
        static GameObject BuildImpactFramePrefab()
        {
            // Same save-over-path pattern as BuildRocketPrefab: keep the GUID
            // stable across rebuilds so CombatFeedback.ImpactFramePrefab never
            // points at a dead fileID.
            string path = $"{PrefabDir}/ImpactFrame.prefab";

            var shader = Shader.Find("AlienSkies/ImpactFrame");
            if (shader == null)
            {
                Debug.LogError("BuildImpactFramePrefab: AlienSkies/ImpactFrame shader not found");
                return null;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ImpactFrame";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.localScale = new Vector3(4.2f, 2.4f, 1f);

            var mat = GetOrCreateMaterial("ImpactFrame", () => new Material(shader));
            quad.GetComponent<Renderer>().sharedMaterial = mat;
            // Playtest verdict 2026-08-29 ("a bit too much on the eyes"):
            // shorter pop — pairs with the shader's softer boost/alpha.
            var fade = quad.AddComponent<ImpactFrameFade>();
            fade.Life = 0.28f;

            var prefab = PrefabUtility.SaveAsPrefabAsset(quad, path);
            Object.DestroyImmediate(quad);
            return prefab;
        }

        /// <summary>Full-screen quad driven by the GlitchPass shader — a
        /// digital-corruption overlay spawned on death/void resets. Same
        /// asset-backed-material doctrine as BuildImpactFramePrefab (see its
        /// summary): SaveAsPrefabAsset nulls in-memory material references.</summary>
        static GameObject BuildGlitchPassPrefab()
        {
            string path = $"{PrefabDir}/GlitchPass.prefab";

            var shader = Shader.Find("AlienSkies/GlitchPass");
            if (shader == null)
            {
                Debug.LogError("BuildGlitchPassPrefab: AlienSkies/GlitchPass shader not found");
                return null;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "GlitchPass";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.localScale = new Vector3(4.2f, 2.4f, 1f);

            var mat = GetOrCreateMaterial("GlitchPass", () => new Material(shader));
            quad.GetComponent<Renderer>().sharedMaterial = mat;
            var fade = quad.AddComponent<ImpactFrameFade>();
            fade.Life = 0.35f;

            var prefab = PrefabUtility.SaveAsPrefabAsset(quad, path);
            Object.DestroyImmediate(quad);
            return prefab;
        }

        static void BuildArena()
        {
            var platformMat = NeonMaterial("NeonPlatform", AlienSkies.Palette.Cyan, 1.1f, 0.55f);
            var trimMat = GetOrCreateMaterial("NeonTrim", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            trimMat.SetColor("_BaseColor", AlienSkies.Palette.Cyan * 2.4f);
            EditorUtility.SetDirty(trimMat);
            // Amber trim marks the center stage — the color language's
            // "opportunity" applied to geometry: this is the platform worth holding.
            var stageTrimMat = GetOrCreateMaterial("NeonTrimStage", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            stageTrimMat.SetColor("_BaseColor", AlienSkies.Palette.Amber * 2.4f);
            EditorUtility.SetDirty(stageTrimMat);

            // Every world is a plain sphere with a NeonPlanet material: fbm
            // continents/gas bands + coasts + caps + terminator + rim, all
            // hue-rotated from the same shader (see CreateWorld). Same rng
            // stream: positions and sizes unchanged.

            // --- Authored arena: "the rings". Random scatter made space, not
            // encounters — this layout pulls both players toward an elevated
            // center stage and gives every route a movement verb: launch
            // planets on the cardinals vault you upward, high anchor worlds
            // above the stage feed grapple dives, the outer ring is disengage
            // space. Looks still come from the lookRng stream for variety.

            // Vertical duel column: atrium on the floor, stage in the sky.
            CreatePlatform("Atrium", Vector3.zero, new Vector3(14f, 1f, 14f), platformMat, trimMat);
            CreatePlatform("CenterStage", new Vector3(0f, 24f, 0f), new Vector3(30f, 2f, 30f), platformMat, stageTrimMat);

            var lookRng = new System.Random(1337); // looks only — layout is authored
            int world = 0;

            // Launch planets on the cardinals: touch one, hit SPACE, fly at the stage.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                CreateWorld(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 30f + Vector3.up * 10f,
                            14f, world++, lookRng);
            }

            // Wing platforms on the diagonals — spawn homes with stage sightlines.
            var wingCenters = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.PI / 4f + i * Mathf.PI / 2f;
                wingCenters[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 55f + Vector3.up * 6f;
                CreatePlatform($"Wing_{i}", wingCenters[i], new Vector3(16f, 1f, 16f), platformMat, trimMat);
            }

            // Compass beacons: one colored light pillar per wing, readable from
            // anywhere — you always know which quadrant you're facing.
            var beaconColors = new[]
                { AlienSkies.Palette.Cyan, AlienSkies.Palette.Green, AlienSkies.Palette.Violet, AlienSkies.Palette.Magenta };
            for (int i = 0; i < 4; i++)
                CreateBeacon(wingCenters[i] + Vector3.up * 0.5f, beaconColors[i], 58f);

            // High anchor worlds above the stage: grapple targets for dive-bombs.
            CreateWorld(new Vector3(-14f, 55f, 10f), 10f, world++, lookRng);
            CreateWorld(new Vector3(16f, 62f, -8f), 10f, world++, lookRng);

            // --- The Gyroscope: the arena reads as a giant cosmic instrument.
            var ringCyanMat = GetOrCreateMaterial("RingCyan", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            ringCyanMat.SetColor("_BaseColor", AlienSkies.Palette.Cyan * 1.9f);
            EditorUtility.SetDirty(ringCyanMat);
            var ringVioletMat = GetOrCreateMaterial("RingViolet", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            ringVioletMat.SetColor("_BaseColor", AlienSkies.Palette.Violet * 1.9f);
            EditorUtility.SetDirty(ringVioletMat);
            var ringAmberMat = GetOrCreateMaterial("RingAmber", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            ringAmberMat.SetColor("_BaseColor", AlienSkies.Palette.Amber * 2.2f);
            EditorUtility.SetDirty(ringAmberMat);

            // Two tilted rings wrap the stage — and they TURN, counter-rotating
            // in their own tilted planes. A frozen gyroscope was a contradiction.
            // Spinning means collider-free (prediction rule); at r46/62 the
            // dashes were never real platforms, and spires/asteroids/wings keep
            // plenty of grapple anchors in range.
            var gyroOuter = CreateSegmentRing("Gyro_Outer", new Vector3(0f, 24f, 0f), 62f,
                Quaternion.Euler(28f, 0f, 8f), 30, new Vector3(1.8f, 0.5f, 6f), ringCyanMat, false);
            gyroOuter.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 4f, 0f);
            var gyroInner = CreateSegmentRing("Gyro_Inner", new Vector3(0f, 24f, 0f), 46f,
                Quaternion.Euler(-35f, 40f, 0f), 24, new Vector3(1.5f, 0.5f, 5f), ringVioletMat, false);
            gyroInner.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, -7f, 0f);
            // The halo: a slow-spinning amber crown above the stage. Visual
            // only — no colliders under a SlowSpin, ever.
            var halo = CreateSegmentRing("Gyro_Halo", new Vector3(0f, 40f, 0f), 18f,
                Quaternion.identity, 16, new Vector3(1.1f, 0.35f, 3.4f), ringAmberMat, false);
            halo.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 9f, 0f);

            // The Helix: a spiral of glowing discs climbing the duel column
            // from the Atrium to the stage — violet, the movement-tech color.
            var discMat = NeonMaterial("NeonDisc", AlienSkies.Palette.Violet, 1.3f, 0.5f);
            for (int i = 0; i < 10; i++)
            {
                float t = i / 9f;
                float a = t * Mathf.PI * 3f; // 1.5 turns
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 17f + Vector3.up * (2f + t * 20f);
                CreateDisc($"Helix_{i}", p, 3.2f, 0.6f, discMat, ringVioletMat);
            }

            // Spire gardens on the east/west flanks: cover, grapple anchors,
            // and perch tips. Heights from a dedicated stream (authored-random).
            var spireMat = NeonMaterial("NeonSpire", AlienSkies.Palette.Cyan, 1.5f, 0.6f, 3.2f);
            var wackyRng = new System.Random(7);
            foreach (float side in new[] { 1f, -1f })
                for (int i = 0; i < 6; i++)
                {
                    float a = (float)(wackyRng.NextDouble() * Mathf.PI * 2.0);
                    float r = 4f + (float)wackyRng.NextDouble() * 11f;
                    var basePos = new Vector3(side * 78f + Mathf.Cos(a) * r,
                        -6f + (float)wackyRng.NextDouble() * 5f, Mathf.Sin(a) * r);
                    CreateSpire(basePos, 14f + (float)wackyRng.NextDouble() * 26f, spireMat, ringAmberMat);
                }

            // Asteroid field in the north-east octant: squashed rocks, every
            // one a grapple anchor AND a mini launch pad — a parkour lane.
            var asteroidMat = NeonMaterial("NeonAsteroid", AlienSkies.Palette.Violet, 0.8f, 0.75f);
            for (int i = 0; i < 12; i++)
            {
                float a = 0.35f + (float)wackyRng.NextDouble() * 0.85f; // ~20°–69°
                float dist = 58f + (float)wackyRng.NextDouble() * 45f;
                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = $"Asteroid_{i:00}";
                rock.transform.position = new Vector3(Mathf.Cos(a) * dist,
                    5f + (float)wackyRng.NextDouble() * 35f, Mathf.Sin(a) * dist);
                rock.transform.rotation = Quaternion.Euler(
                    (float)wackyRng.NextDouble() * 360f, (float)wackyRng.NextDouble() * 360f, 0f);
                float s = 2.5f + (float)wackyRng.NextDouble() * 4f;
                rock.transform.localScale = new Vector3(s, s * (0.55f + (float)wackyRng.NextDouble() * 0.35f), s * 0.85f);
                rock.GetComponent<Renderer>().sharedMaterial = asteroidMat;
                rock.AddComponent<PlanetSurface>(); // hop off a rock mid-chase
            }

            // The shattered world: a planet with orbiting wreckage, off in the
            // south-west — landmark number two, and pure spectacle.
            CreateWorld(new Vector3(-72f, 34f, -78f), 18f, world++, lookRng);
            var wreck = new GameObject("Wreckage");
            wreck.transform.position = new Vector3(-72f, 34f, -78f);
            wreck.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 8f, 3f);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = $"Shard_{i}";
                shard.transform.SetParent(wreck.transform, false);
                shard.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (13f + (i % 3) * 2.2f)
                                                + Vector3.up * ((i % 4) - 1.5f) * 1.8f;
                shard.transform.localRotation = Quaternion.Euler(i * 47f, i * 83f, i * 29f);
                shard.transform.localScale = new Vector3(1.6f + (i % 3), 0.9f + (i % 2) * 0.8f, 1.2f);
                shard.GetComponent<Renderer>().sharedMaterial = spireMat;
                Object.DestroyImmediate(shard.GetComponent<Collider>()); // it spins
            }

            // --- Debris belt: a thin orbiting ring of rubble between the arena
            // and the sky — the parallax layer that makes your own speed (and
            // the stage's stillness) readable. Decorative: no colliders.
            var belt = new GameObject("Debris_Belt");
            belt.transform.SetPositionAndRotation(new Vector3(0f, 24f, 0f), Quaternion.Euler(6f, 0f, -4f));
            belt.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 2.2f, 0f);
            var beltRng = new System.Random(99);
            for (int i = 0; i < 48; i++)
            {
                var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = $"Belt_{i:00}";
                chunk.transform.SetParent(belt.transform, false);
                float ba = (float)(beltRng.NextDouble() * Mathf.PI * 2.0);
                float br = 85f + (float)beltRng.NextDouble() * 30f;
                chunk.transform.localPosition = new Vector3(Mathf.Cos(ba) * br,
                    -8f + (float)beltRng.NextDouble() * 16f, Mathf.Sin(ba) * br);
                chunk.transform.localRotation = Quaternion.Euler(
                    (float)beltRng.NextDouble() * 360f, (float)beltRng.NextDouble() * 360f,
                    (float)beltRng.NextDouble() * 360f);
                float bs = 0.8f + (float)beltRng.NextDouble() * 2.2f;
                chunk.transform.localScale = new Vector3(bs, bs * 0.5f, bs * 0.8f);
                chunk.GetComponent<Renderer>().sharedMaterial = asteroidMat;
                Object.DestroyImmediate(chunk.GetComponent<Collider>()); // it spins
            }

            // --- The Machine: three colossal nested rings wrapping the whole
            // play space, each on its own tilt and period — the "background
            // spinner". Dimmer than arena rings so the fight keeps color
            // priority; the sky should turn, not shout.
            var machineCyanMat = GetOrCreateMaterial("MachineCyan", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            machineCyanMat.SetColor("_BaseColor", AlienSkies.Palette.Cyan * 1.5f);
            EditorUtility.SetDirty(machineCyanMat);
            var machineVioletMat = GetOrCreateMaterial("MachineViolet", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            machineVioletMat.SetColor("_BaseColor", AlienSkies.Palette.Violet * 1.5f);
            EditorUtility.SetDirty(machineVioletMat);
            var machineA = CreateSegmentRing("Machine_A", new Vector3(0f, 24f, 0f), 240f,
                Quaternion.Euler(60f, 0f, 0f), 26, new Vector3(7f, 2.5f, 30f), machineVioletMat, false);
            machineA.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 1.6f, 0f);
            var machineB = CreateSegmentRing("Machine_B", new Vector3(0f, 24f, 0f), 300f,
                Quaternion.Euler(75f, 55f, 0f), 30, new Vector3(9f, 3f, 38f), machineCyanMat, false);
            machineB.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, -1.1f, 0f);
            var machineC = CreateSegmentRing("Machine_C", new Vector3(0f, 24f, 0f), 370f,
                Quaternion.Euler(12f, 0f, -20f), 34, new Vector3(11f, 3.5f, 46f), machineCyanMat, false);
            machineC.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 0.8f, 0f);

            // --- The Procession: colossal monoliths on a slow orbital rail far
            // outside, each slab tumbling on its own. Set dressing only.
            var procession = new GameObject("Procession");
            procession.transform.SetPositionAndRotation(new Vector3(0f, 24f, 0f), Quaternion.Euler(4f, 0f, 10f));
            procession.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 0.35f, 0f);
            var monoRng = new System.Random(777);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f + (float)monoRng.NextDouble() * 0.4f;
                float r = 420f + (float)monoRng.NextDouble() * 40f;
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = $"Monolith_{i}";
                slab.transform.SetParent(procession.transform, false);
                slab.transform.localPosition = new Vector3(Mathf.Cos(a) * r,
                    -40f + (float)monoRng.NextDouble() * 100f, Mathf.Sin(a) * r);
                slab.transform.localRotation = Quaternion.Euler(
                    (float)monoRng.NextDouble() * 30f, (float)monoRng.NextDouble() * 360f,
                    (float)monoRng.NextDouble() * 30f);
                slab.transform.localScale = new Vector3(14f + (float)monoRng.NextDouble() * 12f,
                    70f + (float)monoRng.NextDouble() * 70f, 9f + (float)monoRng.NextDouble() * 8f);
                slab.GetComponent<Renderer>().sharedMaterial = machineVioletMat;
                Object.DestroyImmediate(slab.GetComponent<Collider>());
                slab.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(
                    0.4f + (float)monoRng.NextDouble(), 0.8f + (float)monoRng.NextDouble() * 1.2f, 0f);
            }

            // --- The Colossus: a four-armed rotor sweeping the gap between the
            // Machine rings. The biggest single mover in the sky.
            var colossus = new GameObject("Colossus");
            colossus.transform.SetPositionAndRotation(new Vector3(0f, 24f, 0f), Quaternion.Euler(18f, 0f, -6f));
            colossus.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 0.7f, 0f);
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.name = $"Colossus_Arm_{i}";
                arm.transform.SetParent(colossus.transform, false);
                arm.transform.localPosition = dir * 270f;
                arm.transform.localRotation = Quaternion.LookRotation(dir);
                arm.transform.localScale = new Vector3(5f, 5f, 150f);
                arm.GetComponent<Renderer>().sharedMaterial = machineCyanMat;
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tip.name = $"Colossus_Tip_{i}";
                tip.transform.SetParent(colossus.transform, false);
                tip.transform.localPosition = dir * 352f;
                tip.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(45f, 0f, 45f);
                tip.transform.localScale = Vector3.one * 22f;
                tip.GetComponent<Renderer>().sharedMaterial = machineVioletMat;
                Object.DestroyImmediate(tip.GetComponent<Collider>());
            }

            // --- Orbital stations: three composite megastructures riding one
            // rail counter to the Colossus, each slowly turning on its own hub.
            var stations = new GameObject("Stations");
            stations.transform.SetPositionAndRotation(new Vector3(0f, 24f, 0f), Quaternion.Euler(-30f, 20f, 0f));
            stations.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, -0.45f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float a = i / 3f * Mathf.PI * 2f;
                var hub = new GameObject($"Station_{i}");
                hub.transform.SetParent(stations.transform, false);
                hub.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 320f;
                hub.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 3f, 0f);
                var core = GameObject.CreatePrimitive(PrimitiveType.Cube);
                core.name = "Core";
                core.transform.SetParent(hub.transform, false);
                core.transform.localScale = new Vector3(20f, 34f, 20f);
                core.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
                core.GetComponent<Renderer>().sharedMaterial = machineCyanMat;
                Object.DestroyImmediate(core.GetComponent<Collider>());
                for (int d = -1; d <= 1; d += 2)
                {
                    var deck = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    deck.name = $"Deck_{(d < 0 ? "Lower" : "Upper")}";
                    deck.transform.SetParent(hub.transform, false);
                    deck.transform.localPosition = Vector3.up * (d * 12f);
                    deck.transform.localScale = new Vector3(46f, 1.6f, 46f);
                    deck.GetComponent<Renderer>().sharedMaterial = machineVioletMat;
                    Object.DestroyImmediate(deck.GetComponent<Collider>());
                }
            }

            // Backdrop worlds beyond the play ring — solid and landable (you CAN
            // fly out there, so they keep colliders + launch pads; playtest
            // caught the ghost planets). They spin via the shader's _SpinSpeed —
            // surface turns, sphere collider is rotation-invariant, prediction
            // never notices. Orbiting them is the one thing colliders forbid.
            var rng = new System.Random(42);
            for (int i = 0; i < 24; i++)
            {
                float yaw = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float dist = 160f + (float)rng.NextDouble() * 120f;
                float height = -10f + (float)rng.NextDouble() * 120f;
                float diameter = 8f + (float)rng.NextDouble() * 26f;
                CreateWorld(new Vector3(Mathf.Cos(yaw) * dist, height, Mathf.Sin(yaw) * dist),
                            diameter, world++, lookRng);
            }

            // Focal landmark: a BLACK HOLE on the horizon — near-black body,
            // one blazing magenta limb, and two counter-turning accretion
            // rings near edge-on. Mature menace, not a big pink ball.
            var coreMat = GetOrCreateMaterial("GalaxyCore", () =>
                new Material(Shader.Find("AlienSkies/NeonPlanet")));
            coreMat.shader = Shader.Find("AlienSkies/NeonPlanet"); // convert pre-existing GlowOrb asset
            coreMat.SetColor("_Color", Palette.Magenta);
            coreMat.SetFloat("_SurfaceIntensity", 0.05f); // the void itself
            coreMat.SetFloat("_RimIntensity", 6f);        // photon-hot limb
            coreMat.SetFloat("_NoiseScale", 2.6f);
            coreMat.SetFloat("_Banding", 0f);
            coreMat.SetFloat("_SpinSpeed", 0.01f);
            coreMat.SetVector("_SunDir", SunDir);
            coreMat.SetFloat("_NightSide", 1f); // no terminator on a void
            EditorUtility.SetDirty(coreMat);
            var focal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            focal.name = "GalaxyCore";
            focal.transform.position = new Vector3(300f, 190f, 720f);
            focal.transform.localScale = Vector3.one * 330f;
            Object.DestroyImmediate(focal.GetComponent<Collider>());
            focal.GetComponent<Renderer>().sharedMaterial = coreMat;

            var accHotMat = GetOrCreateMaterial("AccretionHot", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            accHotMat.SetColor("_BaseColor", Palette.Magenta * 2.6f);
            EditorUtility.SetDirty(accHotMat);
            // Near-horizontal plane = seen close to edge-on from the arena —
            // the thin blazing ellipse across the void, not a face-on target.
            var accHot = CreateSegmentRing("Accretion_Hot", focal.transform.position, 225f,
                Quaternion.Euler(12f, 0f, 24f), 48, new Vector3(26f, 3f, 42f), accHotMat, false);
            accHot.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, 1.6f, 0f);
            var accFaint = CreateSegmentRing("Accretion_Faint", focal.transform.position, 290f,
                Quaternion.Euler(12f, 0f, 24f), 40, new Vector3(34f, 2f, 55f), machineVioletMat, false);
            accFaint.AddComponent<SlowSpin>().DegreesPerSecond = new Vector3(0f, -0.9f, 0f);

            // Outer ring: disengage/heal space and long-duel perches, spiraling
            // up — round discs, so the silhouette differs from the square wings.
            for (int i = 1; i <= 6; i++)
            {
                float yaw = i * 1.9f;
                var pos = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * (95f + i * 8f)
                          + Vector3.up * (12f + i * 8f);
                CreateDisc($"Ring_{i}", pos, 5.5f, 0.8f, platformMat, ringCyanMat);
            }

            // Respawn points: the four wings + two mid-ring perches — never the
            // stage (you fight INTO the center, you don't wake up owning it).
            var registry = new GameObject("SpawnPoints").AddComponent<SpawnPointRegistry>();
            var pts = new System.Collections.Generic.List<Transform>();
            void AddSpawn(Vector3 p)
            {
                var t = new GameObject($"Spawn_{pts.Count}").transform;
                t.SetParent(registry.transform, false);
                t.position = p;
                pts.Add(t);
            }
            foreach (var w in wingCenters) AddSpawn(w + Vector3.up * 3f);
            for (int i = 3; i <= 4; i++)
            {
                float yaw = i * 1.9f;
                AddSpawn(new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * (95f + i * 8f)
                         + Vector3.up * (12f + i * 8f + 3f));
            }
            registry.Points = pts.ToArray();

            // Courses plan (Task 2): course finish gates are now built by
            // each course's own GateFinish segment (CourseBuilder), called
            // from BuildPlayer where CourseResults/LevelDefs are in scope.
            // The three hand-placed Sphere gates this function used to
            // build/return (far-perch, above the high-anchor world, at
            // Wing_0) are retired along with the arena routes they served.
        }

        /// <summary>One sphere's finish line: amber segment-ring halo + a
        /// beacon pillar (findability from anywhere in the arena) + a trigger
        /// sphere carrying ExitGate. Returns the root GameObject `name`, so
        /// RunManager can SetActive() the whole gate per level selection.
        /// Beacon material is a NEW unlit asset (review-pinned) — the ring
        /// is URP/Unlit, NOT NeonRim, and NeonRim's dark base would undercut
        /// findability, so this must not reuse/rename any existing Neon
        /// material.</summary>
        // Widened to internal (courses plan, round 3, Task 1): CourseBuilder.cs
        // is a sibling static class in this same assembly/namespace that needs
        // to reuse these exact builders so course geometry looks and behaves
        // identically to the hand-authored arena instead of drifting.
        internal static GameObject BuildGate(string name, Vector3 center)
        {
            var ringAmberMat = GetOrCreateMaterial("RingAmber", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            ringAmberMat.SetColor("_BaseColor", AlienSkies.Palette.Amber * 2.2f);
            EditorUtility.SetDirty(ringAmberMat);

            var root = new GameObject(name);
            root.transform.position = center;

            var halo = CreateSegmentRing($"{name}_Halo", center, 5f,
                Quaternion.identity, 16, new Vector3(1.1f, 0.35f, 3.4f), ringAmberMat, false);
            halo.transform.SetParent(root.transform, true);

            // Beacon pillar: a thin vertical cylinder. Unity's cylinder
            // primitive mesh is 2 units tall, so localScale.y = 20f makes a
            // 40m pillar; local position +20 up puts its BASE at the gate
            // (the cylinder's pivot is at its own local center).
            var beaconMat = GetOrCreateMaterial("GateBeacon", () =>
                new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            beaconMat.SetColor("_BaseColor", AlienSkies.Palette.Amber * 3f);
            EditorUtility.SetDirty(beaconMat);
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon";
            Object.DestroyImmediate(beacon.GetComponent<Collider>());
            beacon.transform.SetParent(root.transform, false);
            beacon.transform.localPosition = Vector3.up * 20f;
            beacon.transform.localScale = new Vector3(0.6f, 20f, 0.6f);
            beacon.GetComponent<Renderer>().sharedMaterial = beaconMat;

            var trigger = new GameObject("ExitGate");
            trigger.transform.SetParent(root.transform, false);
            trigger.transform.localPosition = Vector3.zero;
            var triggerCollider = trigger.AddComponent<SphereCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.radius = 4f;
            trigger.AddComponent<AlienSkies.Run.ExitGate>();

            return root;
        }

        /// <summary>Ring of glowing box segments — light dashes around a circle.
        /// Walkable/grappleable when colliders stay on; pass false for anything
        /// that will spin (SlowSpin + colliders would fight prediction).</summary>
        // Widened to internal (courses plan, Task 1) — see BuildGate's note above.
        internal static GameObject CreateSegmentRing(string name, Vector3 center, float radius,
            Quaternion orientation, int segments, Vector3 segmentSize, Material mat, bool colliders)
        {
            var parent = new GameObject(name);
            parent.transform.SetPositionAndRotation(center, orientation);
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = $"{name}_{i:00}";
                seg.transform.SetParent(parent.transform, false);
                seg.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                seg.transform.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
                seg.transform.localScale = segmentSize; // z = along the ring
                seg.GetComponent<Renderer>().sharedMaterial = mat;
                if (!colliders) Object.DestroyImmediate(seg.GetComponent<Collider>());
            }
            return parent;
        }

        /// <summary>Round platform: cylinder body + unlit edge halo. The default
        /// capsule collider would dome the surface — swapped for a mesh collider.</summary>
        // Widened to internal (courses plan, Task 1) — see BuildGate's note above.
        internal static void CreateDisc(string name, Vector3 pos, float radius, float thickness,
            Material bodyMat, Material edgeMat)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = name;
            body.transform.position = pos;
            body.transform.localScale = new Vector3(radius * 2f, thickness * 0.5f, radius * 2f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.AddComponent<MeshCollider>();
            var edge = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            edge.name = $"{name}_Edge";
            edge.transform.position = pos;
            edge.transform.localScale = new Vector3(radius * 2.08f, thickness * 0.18f, radius * 2.08f);
            edge.GetComponent<Renderer>().sharedMaterial = edgeMat;
            Object.DestroyImmediate(edge.GetComponent<Collider>());
        }

        /// <summary>Tall obelisk with a hot tip — cover, grapple anchor, perch.</summary>
        // Widened to internal (courses plan, Task 1) — see BuildGate's note above.
        internal static void CreateSpire(Vector3 basePos, float height, Material mat, Material tipMat)
        {
            var spire = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spire.name = "Spire";
            spire.transform.position = basePos + Vector3.up * (height * 0.5f);
            spire.transform.localScale = new Vector3(1.6f, height, 1.6f);
            spire.GetComponent<Renderer>().sharedMaterial = mat;
            var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tip.name = "SpireTip";
            tip.transform.position = basePos + Vector3.up * (height + 0.45f);
            tip.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
            tip.GetComponent<Renderer>().sharedMaterial = tipMat;
            Object.DestroyImmediate(tip.GetComponent<Collider>());
        }

        /// <summary>Vertical pillar of light — a compass landmark, pure visual.</summary>
        static void CreateBeacon(Vector3 basePos, Color color, float height)
        {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "Beacon";
            beam.transform.position = basePos + Vector3.up * (height * 0.5f);
            beam.transform.localScale = new Vector3(0.35f, height, 0.35f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); // scene-embedded
            mat.name = "BeaconLook";
            mat.SetColor("_BaseColor", color * 3f);
            beam.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(beam.GetComponent<Collider>());
        }

        /// <summary>One world, either style: NeonPlanet procedural sphere or SGT
        /// planet. Position/size are authored by the caller; looks come from the
        /// shared lookRng stream. Every world is a grapple anchor + launch pad.</summary>
        // Widened to internal (courses plan, Task 1) — see BuildGate's note above.
        internal static GameObject CreateWorld(Vector3 pos, float diameter, int i, System.Random lookRng)
        {
            var rot = Quaternion.Euler(0f, i * 37f, 0f);
            // Neon planet: plain sphere, palette rim — no SGT, no textures.
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"Planet_{i:00}";
            sphere.transform.SetPositionAndRotation(pos, rot);
            sphere.transform.localScale = Vector3.one * diameter;
            var hue = AlienSkies.Palette.Planets[lookRng.Next(AlienSkies.Palette.Planets.Length)];
            // Procedural world, not a flat ball (playtest: "just balls"):
            // fbm continents/gas bands + coasts + caps + terminator + rim,
            // all in one palette hue. ~30% are banded gas giants.
            var neonMat = new Material(Shader.Find("AlienSkies/NeonPlanet")); // scene-embedded
            neonMat.name = $"PlanetLook_{i:00}";
            neonMat.SetColor("_Color", hue);
            neonMat.SetFloat("_Banding", lookRng.NextDouble() < 0.3 ? 1f : 0f);
            neonMat.SetFloat("_NoiseScale", 2.4f + (float)lookRng.NextDouble() * 1.8f);
            neonMat.SetFloat("_RimIntensity", 2.8f);
            // Visible surface spin, varied per world — the sphere collider
            // is rotation-invariant, so "spin + collision" both hold.
            neonMat.SetFloat("_SpinSpeed", 0.015f + (float)lookRng.NextDouble() * 0.05f);
            // Night sides glow faintly instead of reading as dead blobs.
            neonMat.SetFloat("_NightSide", 0.32f);
            neonMat.SetVector("_SunDir", SunDir);
            sphere.GetComponent<Renderer>().sharedMaterial = neonMat;
            // Primitive sphere ships a collider at the right size already.
            sphere.AddComponent<PlanetSurface>();
            return sphere;
        }

        static void CreateBox(string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = pos;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // Widened to internal (courses plan, Task 1) — see BuildGate's note above.
        internal static Material NeonMaterial(string name, Color rim, float rimIntensity, float shading = 0.45f, float rimPower = 2.6f, Color? baseColor = null)
        {
            var m = GetOrCreateMaterial(name, () => new Material(Shader.Find("AlienSkies/NeonRim")));
            m.shader = Shader.Find("AlienSkies/NeonRim");
            m.SetColor("_BaseColor", baseColor ?? AlienSkies.Palette.Ink);
            m.SetColor("_RimColor", rim);
            m.SetFloat("_RimIntensity", rimIntensity);
            m.SetFloat("_RimPower", rimPower);
            m.SetFloat("_Shading", shading);
            m.SetVector("_SunDir", SunDir);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Color GunRim(int weaponIndex) =>
            weaponIndex == 0 ? AlienSkies.Palette.Amber :
            weaponIndex == 1 ? AlienSkies.Palette.Cyan : AlienSkies.Palette.Magenta;

        static Material[] Repeat(Material m, int count)
        {
            var arr = new Material[count];
            for (int i = 0; i < count; i++) arr[i] = m;
            return arr;
        }

        // Widened to internal (courses plan, Task 1) — see BuildGate's note above.
        internal static void CreatePlatform(string name, Vector3 pos, Vector3 scale, Material bodyMat, Material trimMat)
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

        // Widened to internal (courses plan, Task 1) — see BuildGate's note above.
        internal static Material GetOrCreateMaterial(string name, System.Func<Material> create)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = create();
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }
    }
}
