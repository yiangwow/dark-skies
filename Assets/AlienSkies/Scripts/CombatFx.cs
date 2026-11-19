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

        /// <summary>Cached once — this fires on every rocket shot, and a
        /// fresh Material per pull would churn the GC on the game's most
        /// frequent event (guardrail 1: rocket-wall-test, same-frame flash).</summary>
        static Material _muzzleMat;

        /// <summary>Same-frame muzzle flash: bright sphere + point light, gone
        /// in a blink. No scale/shell animation, so unlike SpawnSphere above
        /// this skips the CombatFx per-frame Update lifecycle entirely.</summary>
        public static void MuzzleFlash(Vector3 at, Vector3 dir)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            go.transform.rotation = Quaternion.LookRotation(dir);
            go.transform.localScale = Vector3.one * 0.35f;
            if (_muzzleMat == null)
            {
                // Universal Render Pipeline/Unlit: the RocketTrail asset
                // material already ships this shader in builds, so a bare
                // Shader.Find here is safe (unlike ExplosionShell above,
                // which needs the asset-backed reference chain to survive
                // shader stripping).
                _muzzleMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _muzzleMat.SetColor("_BaseColor", new Color(1f, 0.75f, 0.25f) * 4f);
            }
            go.GetComponent<Renderer>().sharedMaterial = _muzzleMat;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 6f;
            light.intensity = 3f;
            light.color = new Color(1f, 0.75f, 0.25f);

            Object.Destroy(go, 0.06f);
        }

        /// <summary>Cached once, same reasoning as _muzzleMat.</summary>
        static Material _scorchMat;

        /// <summary>Persistent scorch mark (guardrail 1: rocket-wall-test —
        /// permanence accumulates run-evidence). 12s is long enough to mark a
        /// fight and short enough to never pile up unboundedly across a run.</summary>
        public static void Scorch(Vector3 at, Vector3 surfaceNormal)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            // 0.03 along the normal: z-fight guard against the surface it marks.
            go.transform.position = at + surfaceNormal * 0.03f;
            go.transform.rotation = Quaternion.LookRotation(-surfaceNormal);
            go.transform.localScale = Vector3.one * 2.5f;
            if (_scorchMat == null)
            {
                _scorchMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _scorchMat.SetColor("_BaseColor", new Color(0.06f, 0.03f, 0.10f));
            }
            go.GetComponent<Renderer>().sharedMaterial = _scorchMat;
            Object.Destroy(go, 12f);
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
