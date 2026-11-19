using UnityEngine;
using AlienSkies.Combat;
using AlienSkies.Movement;
using AlienSkies.Audio;

namespace AlienSkies.Run
{
    /// <summary>Scene singleton driving RunCore: starts the timer on the
    /// player's first movement, scores kills through the live music heat,
    /// freezes play on completion, restarts the run on death or kill-plane
    /// (arcade rule: a run is one unbroken attempt). Persists bests in
    /// PlayerPrefs (per-level keys; v1 = sphere1).</summary>
    public sealed class RunManager : MonoBehaviour
    {
        public static RunManager Instance { get; private set; }

        /// <summary>Why the last RestartRun() happened — set BEFORE
        /// RestartRun() runs so RunReset subscribers (the Voice, the glitch
        /// pass) read the right cause.</summary>
        public enum ResetCause { PostGrade, Death, Void, Manual }

        public RunConfig Config;
        public LevelDef[] Levels = new LevelDef[0];

        public RunCore Core { get; private set; }
        public bool InputsFrozen { get; private set; }
        public int BestScore { get; private set; }
        public float BestTime { get; private set; }
        public string BestRank { get; private set; }
        public float RestartRemaining { get; private set; }
        public int CurrentLevel { get; private set; }
        public ResetCause LastResetCause { get; private set; }
        public bool LastRunWasBest { get; private set; }
        /// <summary>Forwards RunSettings' band count — read by MusicManager.Bind
        /// so DropTier's band math matches RunCore.MultiplierFor's mapping
        /// exactly (guardrail 7).</summary>
        public int MaxMultiplier => S.MaxMultiplier;
        public LevelDef CurrentLevelDef =>
            Levels != null && Levels.Length > 0 ? Levels[Mathf.Clamp(CurrentLevel, 0, Levels.Length - 1)] : null;
        /// <summary>The bound local player — null until BindPlayer finds one.
        /// The Voice reads this for death attribution (guardrail 8).</summary>
        public PlayerCombat Player => _player;

        /// <summary>Fired when the clock starts (Ready -> Running).</summary>
        public event System.Action RunStarted;
        /// <summary>Fired on every restart (death, kill-plane, post-grade).</summary>
        public event System.Action RunReset;
        /// <summary>Fired on every level selection (rebuild, gate toggle,
        /// reposition, repopulate).</summary>
        public event System.Action LevelChanged;
        /// <summary>Fired on every scored kill (points, multiplier) — HUD
        /// kill feed / heat bar hook. Gated to Running so pre-clock bot
        /// kills and post-grade rocket confirms (both no-ops in RunCore)
        /// never fire a "+0 x1" event.</summary>
        public event System.Action<int, int> Scored;

        PlayerCombat _player;
        PlayerMotor _motor;
        MusicManager _music;
        Vector3 _fallbackStart; // BindPlayer capture — the no-levels fallback

        RunSettings S => Config != null ? Config.Settings : _fallback;
        readonly RunSettings _fallback = new RunSettings();

        string BestScoreKey => (CurrentLevelDef?.BestKeyPrefix ?? "sphere1") + "_best_score";
        string BestTimeKey => (CurrentLevelDef?.BestKeyPrefix ?? "sphere1") + "_best_time";
        string BestRankKey => (CurrentLevelDef?.BestKeyPrefix ?? "sphere1") + "_best_rank";

        Vector3 RunStart => CurrentLevelDef != null ? CurrentLevelDef.Start : _fallbackStart;

        void Awake()
        {
            Instance = this;
            Core = new RunCore(S);
            BestScore = PlayerPrefs.GetInt(BestScoreKey, 0);
            BestTime = PlayerPrefs.GetFloat(BestTimeKey, 0f);
            BestRank = PlayerPrefs.GetString(BestRankKey, "");
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _music = FindFirstObjectByType<MusicManager>();
            BindPlayer();
            // Idempotent first selection: positions the player at the level
            // start and arms the right gate. The builder already places the
            // player at Sphere 1's start, so this is a no-op teleport on boot.
            if (Levels.Length > 0) SelectLevel(0);
        }

        // The human player: the one motor that reads devices. Bots have
        // ExternalInput and must never drive the run.
        void BindPlayer()
        {
            foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            {
                if (!pc.IsLocal) continue;
                _player = pc;
                _motor = pc.GetComponent<PlayerMotor>();
                _player.OnHitConfirmed += HandleHitConfirmed;
                _motor.KillPlaneCrossed += HandleKillPlane;
                _fallbackStart = _motor.transform.position;
                return;
            }
        }

        void HandleHitConfirmed(float damage, bool killed)
        {
            if (killed) Core.RegisterKill(_music != null ? _music.Core.Intensity : 0f);
            if (killed && Core.State == RunState.Running) Scored?.Invoke(Core.LastKillPoints, Core.Multiplier);
        }

        // Kill-plane fires MID-simulation and the motor teleports to its own
        // fallback point right AFTER the event — restarting synchronously here
        // would be overwritten. Defer to the top of the next Update instead
        // (review-caught position clobber). Only a Running run restarts; in
        // Complete the grade screen must survive falling off the perch.
        void HandleKillPlane()
        {
            if (Core.State == RunState.Running) { LastResetCause = ResetCause.Void; _restartPending = true; }
            // Sanctioned deviation (courses plan, Task 2 Step 3): pre-run falls
            // off the new outward courses used to strand players at the arena
            // atrium — the motor's own kill-plane fallback teleport (see
            // PlayerMotor.SpawnPoint) lands them at (0,3,0), and with no
            // Ready-state restart to bring them back to CurrentLevelDef.Start
            // they were stuck there. Cause is set BEFORE RestartRun, same as
            // the Running-state branch above, per RunManager's own documented
            // contract (subscribers like the Voice/glitch pass read it).
            else if (Core.State == RunState.Ready && CurrentLevelDef != null)
            {
                LastResetCause = ResetCause.Void;
                _restartPending = true;
            }
        }

        bool _restartPending;

        void Update()
        {
            float dt = Time.deltaTime;
            if (_player == null) { BindPlayer(); if (_player == null) return; }

            if (_restartPending) { _restartPending = false; RestartRun(); return; }

            // Guardrail 5: one keypress from ANY state back to the start line —
            // the restart is the most-executed transition in the game, so it
            // must never wait on a death, a grade dwell, or a kill-plane dive.
            var kbRestart = UnityEngine.InputSystem.Keyboard.current;
            if (kbRestart != null && kbRestart.rKey.wasPressedThisFrame
                && Cursor.lockState == CursorLockMode.Locked)
            {
                LastResetCause = ResetCause.Manual; // clean reset: no death glitch, no taunt
                RestartRun();
                return;
            }

            switch (Core.State)
            {
                case RunState.Ready:
                    // Level select — TAB, start line only, BEFORE the
                    // movement check so switching never starts the clock.
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    if (kb != null && kb.tabKey.wasPressedThisFrame && Levels.Length > 1
                        && Cursor.lockState == CursorLockMode.Locked) // project convention: all gameplay keys are lock-gated
                    {
                        SelectLevel((CurrentLevel + 1) % Levels.Length);
                        break; // reposition this frame; movement check resumes next
                    }

                    // First input starts the clock. HORIZONTAL velocity only:
                    // the player spawns above the platform and gravity-settles
                    // past any total-speed threshold with zero input
                    // (review-caught false start). Looking around also must
                    // not start a speedrun.
                    var v = _motor.Core.Velocity;
                    if (new Vector3(v.x, 0f, v.z).sqrMagnitude > 0.05f)
                    {
                        Core.StartRun();
                        if (_music != null) _music.Core.ResetHeat(); // runs start cold — no pre-clock pumping
                        RunStarted?.Invoke();
                    }
                    // Killed at the start line before moving (bot rocket):
                    // the run never started — just reclaim the start position.
                    if (_player.IsDead) { LastResetCause = ResetCause.Death; RestartRun(); }
                    break;

                case RunState.Running:
                    Core.Tick(dt);
                    if (_player.IsDead) { LastResetCause = ResetCause.Death; RestartRun(); }
                    break;

                case RunState.Complete:
                    // Deaths/falls here are cosmetic — the dwell finishes and
                    // restarts regardless; the motor's own kill-plane teleport
                    // parks the body meanwhile.
                    RestartRemaining -= dt;
                    if (RestartRemaining <= 0f) { LastResetCause = ResetCause.PostGrade; RestartRun(); }
                    break;
            }
        }

        /// <summary>Called by the ExitGate trigger.</summary>
        public void CompleteRun()
        {
            if (Core.State != RunState.Running) return;
            Core.CompleteRun();
            InputsFrozen = true;
            RestartRemaining = S.RestartDelay;
            LastRunWasBest = Core.FinalScore > BestScore;
            if (Core.FinalScore > BestScore)
            {
                BestScore = Core.FinalScore;
                BestTime = Core.Time;
                PlayerPrefs.SetInt(BestScoreKey, BestScore);
                PlayerPrefs.SetFloat(BestTimeKey, BestTime);
                PlayerPrefs.Save();
            }
            // Guardrail 9 (course-select readout): persisted independently of
            // the score best-check above — ranks and scores move together
            // today, but a worse run must never overwrite a better stored
            // rank, so this re-checks rather than riding the score branch.
            if (RunCore.RankOrder(Core.Rank) > RunCore.RankOrder(BestRank))
            {
                BestRank = Core.Rank;
                PlayerPrefs.SetString(BestRankKey, BestRank);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Death, kill-plane, or post-grade: back to the start line.
        /// A run starts cold — the heat/multiplier must not leak in from the
        /// previous attempt (review-caught scoring exploit).</summary>
        void RestartRun()
        {
            // Sanctioned deviation (courses plan, Task 2 Step 3, research
            // Reset()-contract fold): sweep every live rocket first — restarts
            // are deterministic, no orphaned projectile survives into the new
            // attempt to charge a false hit/knockback. Scene-load-free, so
            // this stays well under the <200 ms restart budget by construction.
            foreach (var r in FindObjectsByType<Rocket>(FindObjectsSortMode.None))
                Destroy(r.gameObject);

            Core.Reset();
            InputsFrozen = false;
            RestartRemaining = 0f;
            if (_music != null) _music.Core.ResetHeat();
            _player.ServerResetForNewRound(); // fresh CombatCore + revive (name is a de-net relic)
            _motor.RespawnAt(RunStart);
            // (No rotation write: the motor re-derives root rotation from the
            // Graphical every frame — keeping the old facing matches respawns.)
            RunReset?.Invoke();
        }

        /// <summary>Switch sphere — start line only (Ready). Rebuilds RunCore
        /// with the level's par/target, toggles gates, moves the player,
        /// repopulates enemies (via LevelChanged), loads that level's bests.</summary>
        public void SelectLevel(int index)
        {
            if (Core.State != RunState.Ready) return;
            if (Levels == null || Levels.Length == 0) return;
            CurrentLevel = Mathf.Clamp(index, 0, Levels.Length - 1);
            var lvl = Levels[CurrentLevel];

            Core = new RunCore(S.WithOverrides(lvl.ParTime, lvl.STargetScore));
            for (int i = 0; i < Levels.Length; i++)
                if (Levels[i].Gate != null) Levels[i].Gate.SetActive(i == CurrentLevel);

            BestScore = PlayerPrefs.GetInt(BestScoreKey, 0);
            BestTime = PlayerPrefs.GetFloat(BestTimeKey, 0f);
            BestRank = PlayerPrefs.GetString(BestRankKey, "");

            if (_motor != null) _motor.RespawnAt(lvl.Start);
            if (_music != null) _music.Core.ResetHeat();
            LevelChanged?.Invoke();
        }

        /// <summary>Stored best rank for ANY course, not just the current one
        /// (guardrail 9's course-select readout needs every course's data at
        /// once). Reads PlayerPrefs directly rather than the cached BestRank,
        /// which only tracks CurrentLevelDef. "" when out of range or never
        /// completed.</summary>
        public string BestRankFor(int levelIndex)
        {
            if (Levels == null || levelIndex < 0 || levelIndex >= Levels.Length) return "";
            return PlayerPrefs.GetString(Levels[levelIndex].BestKeyPrefix + "_best_rank", "");
        }

        /// <summary>Stored best score for ANY course — same reasoning as
        /// BestRankFor. 0 when out of range or never completed.</summary>
        public int BestScoreFor(int levelIndex)
        {
            if (Levels == null || levelIndex < 0 || levelIndex >= Levels.Length) return 0;
            return PlayerPrefs.GetInt(Levels[levelIndex].BestKeyPrefix + "_best_score", 0);
        }
    }
}
