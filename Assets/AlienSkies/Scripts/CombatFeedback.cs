using System.Collections;
using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Owner-only combat feel: Perlin camera shake from FeedbackCore
    /// trauma, procedural hit/kill blips (no asset needed), trauma sources
    /// (firing, being hit, nearby explosions). Shakes the CAMERA child, not the
    /// pivot — the pivot's rotation is owned by the motor's pitch control.</summary>
    public sealed class CombatFeedback : MonoBehaviour
    {
        public PlayerCombat Combat;
        public Transform ShakeTarget;   // MainCamera transform (zero local pose at rest)
        public AudioSource FeedbackAudio;
        public GameObject ImpactFramePrefab;   // full-screen anime pop on kill (ZTest-Always billboard)
        public GameObject GlitchPassPrefab;    // full-screen corruption overlay on death/void reset

        public FeedbackCore Core { get; } = new FeedbackCore();

        AudioClip _hitBlip, _killBlip, _tierBlip;
        float _noiseSeed;
        int _lastMultiplier = 1;
        bool _hitstopRunning;

        void Awake()
        {
            _noiseSeed = Random.value * 100f;
            _hitBlip = MakeBlip(1400f, 0.05f);
            _killBlip = MakeBlip(700f, 0.25f);
            _tierBlip = MakeBlip(1000f, 0.12f);
        }

        /// <summary>Sine blip with a squared decay envelope — reads as a crisp
        /// arcade hit tick without needing an audio asset.</summary>
        static AudioClip MakeBlip(float freq, float duration)
        {
            const int rate = 44100;
            int n = (int)(rate * duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = 1f - i / (float)n;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * i / rate) * env * env * 0.5f;
            }
            var clip = AudioClip.Create($"blip{(int)freq}", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnEnable()
        {
            CombatFx.ExplosionSpawned += HandleExplosion;
            if (Combat == null) return;
            Combat.OnFired += HandleFired;
            Combat.OnHitConfirmed += HandleHitConfirmed;
            Combat.OnDamaged += HandleDamaged;
        }

        void OnDisable()
        {
            CombatFx.ExplosionSpawned -= HandleExplosion;
            // A destroyed/disabled component must never leave the game
            // frozen — restore timeScale here even if OnDestroy never runs.
            if (_hitstopRunning) { Time.timeScale = 1f; _hitstopRunning = false; }
            if (Combat == null) return;
            Combat.OnFired -= HandleFired;
            Combat.OnHitConfirmed -= HandleHitConfirmed;
            Combat.OnDamaged -= HandleDamaged;
        }

        void Start()
        {
            // Reset-cause glitch is a full-screen overlay tied to the run,
            // not to this player's own hits — gate to the local player only
            // or every bot's (deactivated) camera would spawn one too.
            if (Combat == null || !Combat.IsLocal) return;
            if (AlienSkies.Run.RunManager.Instance != null)
                AlienSkies.Run.RunManager.Instance.RunReset += HandleRunReset;
        }

        void OnDestroy()
        {
            if (_hitBlip != null) Destroy(_hitBlip);
            if (_killBlip != null) Destroy(_killBlip);
            if (_tierBlip != null) Destroy(_tierBlip);
            if (AlienSkies.Run.RunManager.Instance != null)
                AlienSkies.Run.RunManager.Instance.RunReset -= HandleRunReset;
        }

        void HandleRunReset()
        {
            var run = AlienSkies.Run.RunManager.Instance;
            // Death glitch fires ONLY on actual deaths — a finished run or a
            // manual R restart is a clean cut, not a corruption event.
            if (run == null ||
                (run.LastResetCause != AlienSkies.Run.RunManager.ResetCause.Death &&
                 run.LastResetCause != AlienSkies.Run.RunManager.ResetCause.Void)) return;
            if (GlitchPassPrefab == null || ShakeTarget == null) return;
            var fx = Instantiate(GlitchPassPrefab, ShakeTarget);
            fx.transform.localPosition = new Vector3(0f, 0f, 2f);
            fx.transform.localRotation = Quaternion.identity;
            Destroy(fx, 1f);
        }

        void HandleFired(int weapon)
        {
            Core.AddTrauma(weapon == 0 ? 0.25f : weapon == 2 ? 0.18f : 0.08f);
            // Directional kick (guardrail 1: camera kick opposite fire) — the
            // rocket's recoil reads much larger than hitscan; rifle/shotgun
            // still kick, just subtly.
            Core.AddKick(weapon == 0 ? 1.6f : 0.5f);
        }

        void HandleHitConfirmed(float damage, bool killed)
        {
            Core.RegisterHit(killed);
            if (killed)
            {
                // Kill trauma FLOOR (guardrail 2): HandleExplosion's trauma is
                // distance-scaled and fades to ~0 past ~20m, but the ladder
                // must never invert — a kill can never shake less than a fire
                // (0.25).
                Core.AddTrauma(0.45f);
                // Kill-only hitstop, clamped not stacked: a chain of kills
                // must not compound the freeze past a single 60ms window
                // (guardrail 2 — bounded total frozen time). Local player
                // only; this class already only binds the local camera.
                if (!_hitstopRunning) StartCoroutine(HitstopRoutine());
            }
            if (FeedbackAudio != null)
                FeedbackAudio.PlayOneShot(killed ? _killBlip : _hitBlip, 0.7f);
            if (killed && ImpactFramePrefab != null && ShakeTarget != null)
            {
                // Parented to the camera so it stays glued to the view at speed.
                var frame = Instantiate(ImpactFramePrefab, ShakeTarget);
                frame.transform.localPosition = new Vector3(0f, 0f, 2f);
                Destroy(frame, 1.5f);
            }
        }

        // Deliberately leaves Time.fixedDeltaTime untouched — this scales
        // perceived time only, not the physics step.
        IEnumerator HitstopRoutine()
        {
            _hitstopRunning = true;
            Time.timeScale = 0.1f;
            yield return new WaitForSecondsRealtime(0.06f);
            Time.timeScale = 1f;
            _hitstopRunning = false;
        }

        void HandleDamaged(float fraction) => Core.RegisterDamageTaken(fraction);

        void HandleExplosion(Vector3 at)
        {
            if (ShakeTarget == null) return;
            float dist = Vector3.Distance(ShakeTarget.position, at);
            Core.AddTrauma(Mathf.Clamp01(1f - dist / 30f) * 0.7f);
        }

        void Update()
        {
            Core.Tick(Time.deltaTime);
            TickTierUp();
            if (ShakeTarget == null) return;
            // Guardrail 3: shake is a setting — scaled here, not in
            // FeedbackCore's trauma math, same doctrine as SpeedFeel's FOV delta.
            float m = Core.ShakeMagnitude * AlienSkies.Movement.FeelSettings.ShakeScale;
            // Kick must still drive a rotation write even when shake
            // magnitude is ~0 (e.g. a single rocket with no other trauma
            // active) — the early-out/reset only fires when BOTH channels
            // are spent.
            if (m <= 0.0001f && Core.KickPitch <= 0.0001f)
            {
                ShakeTarget.localPosition = Vector3.zero;
                ShakeTarget.localRotation = Quaternion.identity;
                return;
            }
            float t = Time.time * 18f;
            // Rotational-only (guardrail 2): a positional offset reads as
            // motion at 60 m/s, not impact — localPosition stays zero.
            ShakeTarget.localPosition = Vector3.zero;
            // Kick (guardrail 1: camera kick opposite fire) composes with
            // trauma shake in this ONE rotation write — subtracted from the
            // X euler so the camera tips up, opposite the fire direction. It
            // must never get its own transform write.
            ShakeTarget.localRotation = Quaternion.Euler(
                (Mathf.PerlinNoise(_noiseSeed, t) - 0.5f) * 2.4f * m - Core.KickPitch,
                (Mathf.PerlinNoise(_noiseSeed + 17f, t) - 0.5f) * 2.4f * m,
                (Mathf.PerlinNoise(_noiseSeed + 31f, t) - 0.5f) * 6f * m);
        }

        /// <summary>Tier-up sting (guardrail 2): the heat multiplier
        /// otherwise changes silently even though it outranks a kill. Rising
        /// edge only — a tier DROP getting its own feedback is a wave-2
        /// design item (§Iteration plan).</summary>
        void TickTierUp()
        {
            var run = AlienSkies.Run.RunManager.Instance;
            if (run == null) return;
            int current = run.Core.Multiplier;
            if (current > _lastMultiplier && run.Core.State == AlienSkies.Run.RunState.Running)
            {
                if (FeedbackAudio != null) FeedbackAudio.PlayOneShot(_tierBlip, 0.9f);
                Core.AddTrauma(0.3f);
            }
            _lastMultiplier = current;
        }
    }
}
