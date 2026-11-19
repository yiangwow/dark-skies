using UnityEngine;
using AlienSkies.Combat;
using AlienSkies.Audio; // MusicManager is a SIBLING namespace — review-caught CS0246

namespace AlienSkies.Run
{
    /// <summary>Scene singleton: maps run events to MachineVoiceCore triggers
    /// and renders the active line with a glitch-materialize effect. The
    /// Gyroscope has opinions about you.</summary>
    public sealed class MachineVoice : MonoBehaviour
    {
        const float ChainWindow = 4f;
        const int ChainCount = 3;
        const float IdleAfter = 12f;

        public MachineVoiceCore Core { get; private set; }

        RunManager _run;
        PlayerCombat _player;
        MusicManager _music;
        RunState _lastState = RunState.Ready;
        float _idleTimer;
        int _recentKills;
        float _chainTimer;
        GUIStyle _style;

        static readonly char[] GlitchGlyphs = "▓▒░#/\\|01<>¥".ToCharArray();

        void Awake() => Core = new MachineVoiceCore((int)System.DateTime.Now.Ticks);

        void Start()
        {
            _run = RunManager.Instance;
            _music = FindFirstObjectByType<MusicManager>();
            if (_run != null)
            {
                _run.RunStarted += HandleRunStarted;
                _run.RunReset += HandleRunReset;
                _run.LevelChanged += HandleLevelChanged;
            }
            BindPlayer();
        }

        void OnDestroy()
        {
            if (_run != null)
            {
                _run.RunStarted -= HandleRunStarted;
                _run.RunReset -= HandleRunReset;
                _run.LevelChanged -= HandleLevelChanged;
            }
            if (_player != null) _player.OnHitConfirmed -= HandleHit;
        }

        void BindPlayer()
        {
            foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            {
                if (!pc.IsLocal) continue;
                _player = pc;
                _player.OnHitConfirmed += HandleHit;
                return;
            }
        }

        void HandleRunStarted()
        {
            _idleTimer = 0f;
            Core.Post(VoiceTrigger.RunStart);
        }

        void HandleRunReset()
        {
            if (_run.LastResetCause == RunManager.ResetCause.Void) Core.Post(VoiceTrigger.DeathByVoid);
            else if (_run.LastResetCause == RunManager.ResetCause.Death)
            {
                // Guardrail 8: name the killer instead of the generic death
                // line whenever the source is known. None/player-null (e.g.
                // rocket-plane deaths outside the beam/drone/self paths)
                // falls back to the original generic line.
                var source = _run.Player != null ? _run.Player.LastDamageSource : DamageSourceKind.None;
                switch (source)
                {
                    case DamageSourceKind.Turret: Core.Post(VoiceTrigger.DeathByTurret); break;
                    case DamageSourceKind.Drone: Core.Post(VoiceTrigger.DeathByDrone); break;
                    case DamageSourceKind.SelfSplash: Core.Post(VoiceTrigger.DeathBySelf); break;
                    default: Core.Post(VoiceTrigger.Death); break;
                }
            }
            // PostGrade: the rank line already spoke on completion.
        }

        // Boot guard (review): RunManager.Start's SelectLevel(0) fires
        // LevelChanged; if OUR Start ran first we'd taunt at boot and the
        // cooldown would eat the first RunStart line. Speak only after the
        // first Update frame.
        bool _sawFirstFrame;

        void HandleLevelChanged()
        {
            if (_sawFirstFrame) Core.Post(VoiceTrigger.LevelSwitch);
        }

        void HandleHit(float damage, bool killed)
        {
            if (!killed) return;
            _recentKills++;
            _chainTimer = ChainWindow;
            float heat = _music != null ? _music.Core.Intensity : 0f;
            if (_recentKills >= ChainCount) { _recentKills = 0; Core.Post(VoiceTrigger.Chain); }
            else if (heat >= 0.99f) Core.Post(VoiceTrigger.KillAtMaxHeat);
            else Core.Post(VoiceTrigger.Kill);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Core.Tick(dt);
            if (_player == null) BindPlayer();
            if (_run == null) return;

            if (_chainTimer > 0f) { _chainTimer -= dt; if (_chainTimer <= 0f) _recentKills = 0; }

            // Rank commentary the frame the grade lands.
            var state = _run.Core.State;
            if (state == RunState.Complete && _lastState != RunState.Complete)
            {
                // EXCLUSIVE (review: both are priority — the second post would
                // overwrite the first before a single OnGUI ran). A new best
                // wins the voice line; the rank is on the grade screen anyway.
                if (_run.LastRunWasBest) Core.Post(VoiceTrigger.NewBest);
                else switch (_run.Core.Rank)
                {
                    case "S": Core.Post(VoiceTrigger.RankS); break;
                    case "A": Core.Post(VoiceTrigger.RankA); break;
                    case "D": Core.Post(VoiceTrigger.RankD); break;
                    default: Core.Post(VoiceTrigger.RankMid); break;
                }
            }
            _lastState = state;
            _sawFirstFrame = true;

            // Idle nagging at the start line.
            if (state == RunState.Ready)
            {
                _idleTimer += dt;
                if (_idleTimer >= IdleAfter) { _idleTimer = 0f; Core.Post(VoiceTrigger.Idle); }
            }
            else _idleTimer = 0f;
        }

        void OnGUI()
        {
            string line = Core.CurrentLine;
            if (line == null) return;
            if (_style == null)
            {
                _style = new GUIStyle
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            // Materialize: early in the line's life, characters render as
            // glitch glyphs with decreasing probability; a per-second flicker
            // corrupts one random slice later on.
            float age = Core.LineAge;
            float corrupt = Mathf.Clamp01(1f - age / 0.5f);
            var chars = line.ToCharArray();
            var rng = new System.Random(line.GetHashCode() ^ (int)(age * 30f));
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ') continue;
                if (rng.NextDouble() < corrupt * 0.8f)
                    chars[i] = GlitchGlyphs[rng.Next(GlitchGlyphs.Length)];
            }
            string shown = new string(chars);

            var rect = new Rect(Screen.width / 2f - 500f, Screen.height * 0.70f, 1000f, 32f);
            float fade = Mathf.Clamp01((MachineVoiceCore.LineDuration - age) / 0.6f);
            var magenta = AlienSkies.Palette.Magenta;
            _style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * fade);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), shown, _style);
            _style.normal.textColor = new Color(magenta.r, magenta.g, magenta.b, fade);
            GUI.Label(rect, shown, _style);
        }
    }
}
