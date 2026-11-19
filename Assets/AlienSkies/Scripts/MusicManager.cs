using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace AlienSkies.Audio
{
    /// <summary>Streams the player's own tracks from StreamingAssets/Music
    /// (.ogg/.wav), shuffled and gapless-looped, and rides MusicCore's
    /// intensity: combat opens the low-pass filter and volume, quiet stretches
    /// muffle down to a cruise, round end ducks everything. No tracks in the
    /// folder = silence (drop files in, no rebuild needed — streamed at
    /// runtime, nothing is committed to the repo).</summary>
    public sealed class MusicManager : MonoBehaviour
    {
        public float BaseVolume = 0.5f;

        public MusicCore Core { get; } = new MusicCore();

        // Global shader float: world materials (NeonRim/NeonPlanet/CosmicSkybox)
        // read this to make emission breathe with combat heat.
        static readonly int HeatId = Shader.PropertyToID("_AlienHeat");

        AudioSource _source;
        AudioLowPassFilter _lowPass;
        readonly List<string> _playlist = new List<string>();
        int _next;
        AlienSkies.Combat.PlayerCombat _bound;
        float _rebindTimer;
        float _level; // smoothed mix level

        void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f; // 2D — it's the soundtrack, not a sound
            _source.volume = 0f;
            _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            _lowPass.cutoffFrequency = 22000f;
        }

        void Start()
        {
            string dir = System.IO.Path.Combine(Application.streamingAssetsPath, "Music");
            if (System.IO.Directory.Exists(dir))
                foreach (var f in System.IO.Directory.GetFiles(dir))
                    if (f.EndsWith(".ogg") || f.EndsWith(".wav"))
                        _playlist.Add(f);
            if (_playlist.Count == 0)
            {
                Debug.Log("MusicManager: no tracks — drop .ogg/.wav files into StreamingAssets/Music");
                return;
            }
            var rng = new System.Random();
            for (int i = _playlist.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (_playlist[i], _playlist[j]) = (_playlist[j], _playlist[i]);
            }
            StartCoroutine(RunPlaylist());
        }

        IEnumerator RunPlaylist()
        {
            while (true)
            {
                string path = _playlist[_next];
                _next = (_next + 1) % _playlist.Count;
                var type = path.EndsWith(".ogg") ? AudioType.OGGVORBIS : AudioType.WAV;
                using (var req = UnityWebRequestMultimedia.GetAudioClip("file://" + path, type))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"MusicManager: {req.error} ({path})");
                        continue;
                    }
                    _source.clip = DownloadHandlerAudioClip.GetContent(req);
                }
                _source.Play();
                // isPlaying goes false on focus loss too — don't skip tracks then.
                while (_source.isPlaying || !Application.isFocused)
                    yield return null;
            }
        }

        void Update()
        {
            Bind();
            Core.RoundOver = AlienSkies.Run.RunManager.Instance != null
                             && AlienSkies.Run.RunManager.Instance.InputsFrozen;
            Core.Tick(Time.deltaTime);
            Shader.SetGlobalFloat(HeatId, Core.Intensity);

            // Ease toward the target mix — the music breathes, it doesn't snap.
            _level = Mathf.Lerp(_level, Core.Level, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            _source.volume = BaseVolume * _level;
            // Squared response: cruise sits muffled, combat blows the filter open.
            _lowPass.cutoffFrequency = Mathf.Lerp(700f, 22000f, _level * _level);
        }

        // Globals persist across editor play-mode exits — zero it so the next
        // play session (or a scene with no MusicManager) doesn't inherit stale heat.
        void OnDisable()
        {
            Shader.SetGlobalFloat(HeatId, 0f);
        }

        void Bind()
        {
            if (_bound != null) return;
            _rebindTimer -= Time.deltaTime;
            if (_rebindTimer > 0f) return;
            _rebindTimer = 1f;
            foreach (var pc in FindObjectsByType<AlienSkies.Combat.PlayerCombat>(FindObjectsSortMode.None))
            {
                if (!pc.IsLocal) continue;
                _bound = pc;
                var motor = pc.GetComponent<AlienSkies.Movement.PlayerMotor>();
                // Guardrail 7: being hit COSTS a tier (was RegisterDamage — the audit's
                // "getting hit raises your multiplier" inversion). SELF-SPLASH IS EXEMPT:
                // rocket jumps are the first-class movement verb (guardrail 12) — the
                // health cost is their price, the multiplier must not double-charge.
                // LastDamageSource is set by TakeDamage before OnDamaged fires.
                // MaxMultiplier comes from the live run settings so the band math
                // matches scoring exactly.
                pc.OnDamaged += _ =>
                {
                    if (pc.LastDamageSource == AlienSkies.Combat.DamageSourceKind.SelfSplash) return;
                    Core.DropTier(AlienSkies.Run.RunManager.Instance != null
                        ? AlienSkies.Run.RunManager.Instance.MaxMultiplier : 4);
                };
                // Guardrail 6: kills pay by movement state; non-kill hits are a
                // rate-limited trickle (MusicCore.RegisterHit's own cooldown caps ALL
                // weapons at ~0.114/s, under the 0.12/s decay — no weapon can hold a
                // tier by plinking, shotgun pellet bursts collapse to one add).
                // Bare firing pays nothing.
                pc.OnHitConfirmed += (dmg, killed) =>
                {
                    if (!killed) { Core.RegisterHit(); return; }
                    var st = MusicCore.KillMoveState.Grounded;
                    if (motor != null)
                    {
                        var mc = motor.Core;
                        // IsGrounded is the real CharacterController signal (Task 1);
                        // SurfaceContactRemaining ORs in planet-coyote contact —
                        // platforms never arm it (PlanetSurface-gated), planets do.
                        st = (mc.IsGrappling || mc.IsZipping) ? MusicCore.KillMoveState.Flow
                           : (mc.IsGrounded || mc.SurfaceContactRemaining > 0f)
                               ? MusicCore.KillMoveState.Grounded
                               : MusicCore.KillMoveState.Airborne;
                    }
                    Core.RegisterKill(st);
                };
                break;
            }
        }
    }
}
