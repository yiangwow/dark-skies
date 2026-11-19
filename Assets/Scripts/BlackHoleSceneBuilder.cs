using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Procedurally builds a stylized black-hole scene at runtime: a black core,
/// glowing photon rings, a swirling particle accretion disk, a starfield,
/// orbiting planets, and an asteroid/debris belt. Plus a framing camera,
/// lighting, and a global Bloom volume so the bright bits actually glow.
///
/// Usage: add this component to an empty GameObject and press Play.
/// Everything is tunable from the Inspector — tweak and re-Play to taste.
/// </summary>
public class BlackHoleSceneBuilder : MonoBehaviour
{
    [Header("Black hole core")]
    public float coreRadius = 2f;
    public Color ringColor = new Color(1f, 0.55f, 0.15f);
    [Tooltip("HDR brightness of the photon rings (drives Bloom glow).")]
    public float ringIntensity = 4f;

    [Header("Accretion disk (particles)")]
    public int diskMaxParticles = 2500;
    public float diskEmissionRate = 350f;
    public float diskInnerRadius = 3.2f;
    public float diskThickness = 1.4f;
    [Tooltip("Swirl speed of the disk particles around the core.")]
    public float diskSpinSpeed = 35f;
    public Color diskColorHot = new Color(1f, 0.9f, 0.6f);
    public Color diskColorCool = new Color(1f, 0.35f, 0.1f);

    [Header("Planets")]
    public int planetCount = 4;
    public float planetInnerRadius = 9f;
    public float planetSpacing = 4.5f;

    [Header("Debris belt")]
    public int debrisCount = 45;
    public float beltInnerRadius = 14f;
    public float beltOuterRadius = 18f;

    [Header("Motion")]
    [Tooltip("Reference orbital speed (deg/s) at the innermost planet; outer bodies move slower (Kepler-ish).")]
    public float baseOrbitSpeed = 45f;

    [Header("Starfield")]
    public int starCount = 900;
    public float starShellRadius = 220f;

    private Transform _root;

    private void Start()
    {
        _root = new GameObject("BlackHoleScene").transform;

        BuildCoreAndRings();
        BuildAccretionDisk();
        BuildStarfield();
        BuildPlanets();
        BuildDebris();
        SetupLighting();
        SetupCameraAndPostFx();
    }

    // --- Core + glowing photon rings -------------------------------------

    private void BuildCoreAndRings()
    {
        GameObject core = CreateSphere("Core", Vector3.zero, coreRadius * 2f,
            MakeUnlitMaterial(Color.black));
        core.transform.SetParent(_root);

        // Two concentric flat rings just outside the event horizon.
        CreateRing("PhotonRingInner", coreRadius * 1.55f, 0.12f, ringColor, ringIntensity);
        CreateRing("PhotonRingOuter", coreRadius * 1.9f, 0.07f,
            Color.Lerp(ringColor, Color.white, 0.4f), ringIntensity * 0.8f);
    }

    private void CreateRing(string name, float radius, float width, Color color, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root);

        var lr = go.AddComponent<LineRenderer>();
        const int segments = 128;
        lr.positionCount = segments;
        lr.loop = true;
        lr.useWorldSpace = false;
        lr.widthMultiplier = width;
        lr.numCapVertices = 4;
        lr.material = MakeEmissiveMaterial(color, intensity);
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;

        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
    }

    // --- Swirling accretion disk -----------------------------------------

    private void BuildAccretionDisk()
    {
        var go = new GameObject("AccretionDisk");
        go.transform.SetParent(_root);
        go.transform.position = Vector3.zero;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = 5f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(diskColorHot, diskColorCool);
        main.maxParticles = diskMaxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = diskEmissionRate;

        // Lay a flat ring of emission in the XZ plane.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = diskInnerRadius + diskThickness * 0.5f;
        shape.donutRadius = diskThickness;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        // Spin particles around the core (Y axis) => orbiting disk.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.orbitalY = diskSpinSpeed;
        vel.radial = -0.15f; // slow inspiral toward the core

        // Fade particles out over their lifetime.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = MakeParticleAdditiveMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
    }

    // --- Starfield backdrop ----------------------------------------------

    private void BuildStarfield()
    {
        var go = new GameObject("Starfield");
        go.transform.SetParent(_root);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.duration = 1f;
        main.startLifetime = 10000f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            Color.white, new Color(0.7f, 0.8f, 1f));
        main.maxParticles = starCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)starCount) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = starShellRadius;
        shape.radiusThickness = 0f; // emit on the shell surface only

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = MakeParticleAdditiveMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
    }

    // --- Planets ----------------------------------------------------------

    private static readonly Color[] PlanetColors =
    {
        new Color(0.3f, 0.6f, 1f),   // blue
        new Color(0.9f, 0.5f, 0.3f), // rust
        new Color(0.5f, 0.85f, 0.6f),// teal-green
        new Color(0.8f, 0.75f, 0.4f),// sandy
        new Color(0.7f, 0.4f, 0.9f), // violet
    };

    private void BuildPlanets()
    {
        for (int i = 0; i < planetCount; i++)
        {
            float radius = planetInnerRadius + i * planetSpacing;
            float size = Random.Range(1f, 2.2f);
            Color color = PlanetColors[i % PlanetColors.Length];

            GameObject planet = CreateSphere($"Planet_{i}", Vector3.zero, size,
                MakeLitMaterial(color));
            planet.transform.SetParent(_root);

            AddOrbit(planet, radius,
                inclination: Random.Range(-12f, 12f),
                node: Random.Range(0f, 360f),
                phase: Random.Range(0f, 360f));
        }
    }

    // --- Debris belt ------------------------------------------------------

    private void BuildDebris()
    {
        var debrisColor = new Color(0.5f, 0.45f, 0.4f);
        for (int i = 0; i < debrisCount; i++)
        {
            float radius = Random.Range(beltInnerRadius, beltOuterRadius);
            float size = Random.Range(0.2f, 0.55f);

            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = $"Debris_{i}";
            StripCollider(rock);
            rock.transform.SetParent(_root);
            rock.transform.localScale = Vector3.one * size;
            rock.transform.rotation = Random.rotation;
            rock.GetComponent<MeshRenderer>().sharedMaterial =
                MakeLitMaterial(debrisColor * Random.Range(0.7f, 1.2f));

            AddOrbit(rock, radius,
                inclination: Random.Range(-9f, 9f),
                node: Random.Range(0f, 360f),
                phase: Random.Range(0f, 360f));
        }
    }

    // --- Shared helpers ---------------------------------------------------

    private void AddOrbit(GameObject body, float radius, float inclination, float node, float phase)
    {
        // Kepler-ish: inner bodies orbit faster than outer ones.
        float speed = baseOrbitSpeed * Mathf.Sqrt(planetInnerRadius / radius);
        body.AddComponent<Orbiter>()
            .Initialize(Vector3.zero, radius, speed, inclination, node, phase);
    }

    private GameObject CreateSphere(string name, Vector3 pos, float diameter, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        StripCollider(go);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * diameter;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    private static void StripCollider(GameObject go)
    {
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
    }

    private void SetupLighting()
    {
        var go = new GameObject("KeyLight");
        go.transform.SetParent(_root);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.9f, 0.95f, 1f);
        light.intensity = 1.1f;
        go.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.06f, 0.06f, 0.09f);
    }

    private void SetupCameraAndPostFx()
    {
        Camera cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 16f, -32f);
            cam.transform.LookAt(Vector3.zero);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        // Global Bloom so the rings, disk and stars glow.
        var volGo = new GameObject("PostFX");
        volGo.transform.SetParent(_root);
        var volume = volGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.profile = profile;

        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(1.4f);
        bloom.threshold.Override(0.7f);
        bloom.scatter.Override(0.75f);
    }

    // --- Materials --------------------------------------------------------

    private static Material MakeLitMaterial(Color color)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", 0.25f);
        return m;
    }

    private static Material MakeUnlitMaterial(Color color)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", color);
        return m;
    }

    private static Material MakeEmissiveMaterial(Color color, float intensity)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", Color.black);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", color * intensity);
        return m;
    }

    private static Material MakeParticleAdditiveMaterial()
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        // Transparent + additive blending for a glowing-gas look.
        m.SetFloat("_Surface", 1f);  // 0 = Opaque, 1 = Transparent
        m.SetFloat("_Blend", 1f);    // 1 = Additive
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_ALPHAMODULATE_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }
}
