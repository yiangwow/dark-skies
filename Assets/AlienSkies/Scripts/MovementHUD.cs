using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using AlienSkies.Audio;

namespace AlienSkies.Movement
{
    /// <summary>OnGUI HUD in the game's palette: shadowed labels, HP and
    /// zip-cooldown bars, consistent color language (cyan = info, green =
    /// movement state, amber = opportunity, magenta = threat/kill).</summary>
    public sealed class MovementHUD : MonoBehaviour
    {
        public PlayerMotor Motor;
        public AlienSkies.Combat.PlayerCombat Combat;
        public AlienSkies.Combat.CombatFeedback Feedback;
        GUIStyle _label, _state, _cue, _cross, _hitmark, _killLabel;
        MusicManager _music;
        AlienSkies.Run.TitleCard _titleCard;
        float _smoothDt;

        readonly List<(string text, float age)> _feed = new();
        bool _scoredHooked;

        // FEEL settings selection (guardrail 3) — which pause-overlay row
        // F3/arrows act on. 0=FOV, 1=SHAKE, 2=WIND.
        int _feelSelected;

        // Mid-run best ping (guardrail 9): fires once per run, reset on
        // start/reset via the same RunManager events as the kill feed.
        bool _bestPaceFired;
        float _bestPaceRemaining;

        void Awake()
        {
            // Touch FeelSettings now so its PlayerPrefs load (static field
            // init) has happened before the pause overlay's first frame.
            _ = FeelSettings.FovScale;
        }

        void Update()
        {
            _smoothDt = _smoothDt <= 0f
                ? Time.unscaledDeltaTime
                : Mathf.Lerp(_smoothDt, Time.unscaledDeltaTime, 0.08f);

            // Lazy-in-Update, not OnEnable: the scene orders the Player
            // instance before RunManager, and this HUD ships enabled — an
            // OnEnable subscription here would silently never fire.
            if (!_scoredHooked && AlienSkies.Run.RunManager.Instance != null)
            {
                var rm = AlienSkies.Run.RunManager.Instance;
                rm.Scored += HandleScored;
                rm.RunStarted += HandleRunStarted;
                rm.RunReset += HandleRunReset;
                _scoredHooked = true;
            }

            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                var e = _feed[i];
                e.age += Time.deltaTime;
                if (e.age > 1.2f) { _feed.RemoveAt(i); continue; }
                _feed[i] = e;
            }
            while (_feed.Count > 5) _feed.RemoveAt(0);

            if (_bestPaceRemaining > 0f) _bestPaceRemaining -= Time.deltaTime;

            UpdateFeelInput();
        }

        /// <summary>F3/arrows only act while the pause overlay is actually up —
        /// same gate OnGUI uses to draw it (cursor unlocked, title card done).</summary>
        void UpdateFeelInput()
        {
            if (_titleCard == null) _titleCard = FindFirstObjectByType<AlienSkies.Run.TitleCard>();
            bool paused = Cursor.lockState != CursorLockMode.Locked
                          && !(_titleCard != null && _titleCard.enabled);
            if (!paused) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f3Key.wasPressedThisFrame) _feelSelected = (_feelSelected + 1) % 3;

            float step = kb.rightArrowKey.wasPressedThisFrame ? 0.1f
                       : kb.leftArrowKey.wasPressedThisFrame ? -0.1f : 0f;
            if (step == 0f) return;

            // Round to one decimal so repeated +/-0.1 presses can't drift off
            // the grid on float error.
            switch (_feelSelected)
            {
                case 0: FeelSettings.FovScale = Mathf.Round((FeelSettings.FovScale + step) * 10f) / 10f; break;
                case 1: FeelSettings.ShakeScale = Mathf.Round((FeelSettings.ShakeScale + step) * 10f) / 10f; break;
                default: FeelSettings.WindScale = Mathf.Round((FeelSettings.WindScale + step) * 10f) / 10f; break;
            }
        }

        void OnDisable()
        {
            // Symmetric with the lazy hook above — bot clones of the prefab
            // get enabled-then-disabled around their first frame.
            if (AlienSkies.Run.RunManager.Instance != null)
            {
                var rm = AlienSkies.Run.RunManager.Instance;
                rm.Scored -= HandleScored;
                rm.RunStarted -= HandleRunStarted;
                rm.RunReset -= HandleRunReset;
            }
            _scoredHooked = false;
        }

        void HandleScored(int points, int mult)
        {
            _feed.Add(($"+{points}  x{mult}", 0f));

            // Mid-run best ping (guardrail 9): only once a real best exists
            // (BestScore 0 means no completed run yet) and only the first
            // time this run's live score clears it.
            var run = AlienSkies.Run.RunManager.Instance;
            if (!_bestPaceFired && run != null && run.BestScore > 0 && run.Core.Score > run.BestScore)
            {
                _bestPaceFired = true;
                _bestPaceRemaining = 2f;
            }
        }

        void HandleRunStarted() => _bestPaceFired = false;
        void HandleRunReset() => _bestPaceFired = false;

        void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold };
            _label.normal.textColor = AlienSkies.Palette.Cyan;
            _state = new GUIStyle(_label);
            _state.normal.textColor = AlienSkies.Palette.Green;
            _cue = new GUIStyle(_label);
            _cue.normal.textColor = AlienSkies.Palette.Amber;
            _cross = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = 26 };
            _cross.normal.textColor = Color.white;
            _hitmark = new GUIStyle(_cross) { fontSize = 34 };
            _hitmark.normal.textColor = AlienSkies.Palette.Amber;
            _killLabel = new GUIStyle(_cross) { fontSize = 36 };
            _killLabel.normal.textColor = AlienSkies.Palette.Magenta;
        }

        /// <summary>Label with a drop shadow — the difference between debug text
        /// and UI. All HUD text goes through here (crosshair excepted).</summary>
        static void ShadowLabel(Rect r, string text, GUIStyle style)
        {
            var c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        static void Bar(Rect r, float frac, Color fill)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void OnGUI()
        {
            if (Motor == null || Motor.Core == null) return;
            EnsureStyles();

            // Pause overlay: drawn instead of the HUD whenever the cursor is
            // unlocked — EXCEPT while the boot title card is still up (its own
            // ESC-less flow shouldn't be double-covered by the pause screen).
            // The run clock is NOT stopped here (identity: "THE CLOCK DOES NOT
            // WAIT.") — zero simulation changes, presentation only.
            if (_titleCard == null) _titleCard = FindFirstObjectByType<AlienSkies.Run.TitleCard>();
            bool titleShowing = _titleCard != null && _titleCard.enabled;
            if (Cursor.lockState != CursorLockMode.Locked && !titleShowing)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;

                var pauseTitle = new GUIStyle(_cross) { fontSize = 30, fontStyle = FontStyle.Bold };
                pauseTitle.normal.textColor = AlienSkies.Palette.Magenta;
                ShadowLabel(new Rect(Screen.width / 2f - 300f, Screen.height / 2f - 170f, 600f, 40f),
                            "PAUSED — THE CLOCK DOES NOT WAIT.", pauseTitle);

                var controls = new GUIStyle(_label) { fontSize = 18 };
                string[] controlLines =
                {
                    "move WASD",
                    "jump SPACE (planets launch)",
                    "grapple hold F",
                    "zip RMB",
                    "fire LMB",
                    "weapons 1/2/3/scroll",
                    "sphere TAB (start line)",
                    "bot B (start line)",
                    "resume ESC",
                };
                float controlsTop = Screen.height / 2f - 100f;
                for (int i = 0; i < controlLines.Length; i++)
                    ShadowLabel(new Rect(Screen.width / 2f - 150f, controlsTop + i * 26f, 400f, 24f),
                                controlLines[i], controls);

                var pauseRun = AlienSkies.Run.RunManager.Instance;
                if (pauseRun != null)
                {
                    string sphereName = pauseRun.CurrentLevelDef?.Name ?? "";
                    ShadowLabel(new Rect(Screen.width / 2f - 150f, controlsTop + controlLines.Length * 26f + 20f, 400f, 26f),
                                $"{sphereName}   BEST {pauseRun.BestScore}", controls);
                }

                // FEEL settings (guardrail 3): scale-only knobs, never raw
                // per-effect tuning. No blur row — the game has no blur pass.
                float feelTop = controlsTop + controlLines.Length * 26f + 56f;
                ShadowLabel(new Rect(Screen.width / 2f - 150f, feelTop - 26f, 400f, 24f),
                            "FEEL", controls);
                string[] feelLabels = { "FOV SCALE", "SHAKE", "WIND" };
                float[] feelValues = { FeelSettings.FovScale, FeelSettings.ShakeScale, FeelSettings.WindScale };
                for (int i = 0; i < feelLabels.Length; i++)
                {
                    bool selected = i == _feelSelected;
                    string marker = selected ? "> " : "  ";
                    ShadowLabel(new Rect(Screen.width / 2f - 150f, feelTop + i * 26f, 400f, 24f),
                                $"{marker}{feelLabels[i],-10} {feelValues[i]:0.0}", selected ? _cue : controls);
                }
                ShadowLabel(new Rect(Screen.width / 2f - 150f, feelTop + feelLabels.Length * 26f + 6f, 400f, 24f),
                            "F3 select   LEFT/RIGHT adjust", controls);
                return;
            }

            var v = Motor.Core.Velocity;
            float h = new Vector3(v.x, 0f, v.z).magnitude;
            ShadowLabel(new Rect(24f, 24f, 400f, 32f), $"{v.magnitude:0.0} m/s   (horizontal {h:0.0})", _label);
            if (Motor.Core.IsZipping)
                ShadowLabel(new Rect(24f, 56f, 400f, 32f), "ZIP!", _state);
            else if (Motor.Core.IsGrappling)
                ShadowLabel(new Rect(24f, 56f, 400f, 32f), "GRAPPLING (RMB to zip)", _state);
            if (Motor.Core.ZipCooldownRemaining > 0f)
            {
                float cdFrac = 1f - Mathf.Clamp01(Motor.Core.ZipCooldownRemaining / 3f);
                Bar(new Rect(Screen.width / 2f - 40f, Screen.height / 2f + 26f, 80f, 5f),
                    cdFrac, AlienSkies.Palette.Violet);
            }
            if (Motor.Core.SurfaceContactRemaining > 0f)
                ShadowLabel(new Rect(24f, 120f, 400f, 32f), "PLANET — SPACE to kick off!", _cue);

            if (_smoothDt > 0f)
                ShadowLabel(new Rect(Screen.width - 140f, 56f, 120f, 30f), $"{1f / _smoothDt:0} fps", _label);

            _cross.normal.textColor = Motor.HasGrappleTarget ? AlienSkies.Palette.Green : Color.white;
            GUI.Label(new Rect(Screen.width / 2f - 20f, Screen.height / 2f - 20f, 40f, 40f),
                      Motor.HasGrappleTarget ? "[+]" : "+", _cross);

            if (Combat == null) return;
            float hpFrac = Mathf.Clamp01(Combat.Health / 100f);
            var barBack = new Rect(24f, Screen.height - 54f, 220f, 14f);
            Bar(barBack, hpFrac, Color.Lerp(AlienSkies.Palette.Magenta, AlienSkies.Palette.Green, hpFrac));
            ShadowLabel(new Rect(barBack.x, barBack.y - 34f, 300f, 30f),
                        $"HP {Combat.Health:0}", new GUIStyle(_label) { fontSize = 26 });
            ShadowLabel(new Rect(Screen.width - 220f, Screen.height - 56f, 200f, 32f),
                        $"[{Combat.CurrentWeapon + 1}] {Combat.CurrentWeaponName}", _label);
            var run = AlienSkies.Run.RunManager.Instance;
            if (run != null)
            {
                if (_music == null) _music = Object.FindFirstObjectByType<MusicManager>();
                int liveMult = run.Core.MultiplierFor(_music != null ? _music.Core.Intensity : 0f);
                string levelName = run.CurrentLevelDef?.Name;
                ShadowLabel(new Rect(Screen.width / 2f - 160f, 24f, 320f, 32f),
                            $"SCORE {run.Core.Score}   x{liveMult}",
                            new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter });

                // Heat bar — between the SCORE line and the level name (the
                // literal "directly under SCORE" slot at y~56 is already the
                // level name row; y~50 tucks the 6px bar in without a collision).
                float heatFrac = _music != null ? _music.Core.Intensity : 0f;
                var heatRect = new Rect(Screen.width / 2f - 100f, 50f, 200f, 6f);
                Bar(heatRect, heatFrac, Color.Lerp(AlienSkies.Palette.Cyan, AlienSkies.Palette.Magenta, heatFrac));
                for (int t = 1; t <= 3; t++)
                {
                    float tx = heatRect.x + heatRect.width * (t / 3f);
                    GUI.color = new Color(0f, 0f, 0f, 0.5f);
                    GUI.DrawTexture(new Rect(tx - 1f, heatRect.y, 2f, heatRect.height), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }

                if (!string.IsNullOrEmpty(levelName))
                    ShadowLabel(new Rect(Screen.width / 2f - 160f, 56f, 320f, 24f),
                                levelName,
                                new GUIStyle(_cue) { fontSize = 16, alignment = TextAnchor.MiddleCenter });
                if (run.Core.State == AlienSkies.Run.RunState.Running)
                    ShadowLabel(new Rect(Screen.width / 2f - 60f, 80f, 120f, 30f),
                                $"{run.Core.Time:0.0}s",
                                new GUIStyle(_cue) { alignment = TextAnchor.MiddleCenter });
                if (run.Core.State == AlienSkies.Run.RunState.Ready && run.Levels.Length > 1)
                    ShadowLabel(new Rect(Screen.width / 2f - 100f, Screen.height - 48f, 200f, 30f),
                                "TAB — next sphere",
                                new GUIStyle(_cue) { alignment = TextAnchor.MiddleCenter });

                // Course-select readout (guardrail 9): every course's stored
                // rank/score in one glance, so "which course is my weakest"
                // never requires TAB-cycling and memorizing. Ready-only, same
                // lock-gate as the TAB hint above; parked in the right column
                // clear of the FPS counter (ends y~86) above and the weapon
                // name row (starts Screen.height-56) below.
                if (run.Core.State == AlienSkies.Run.RunState.Ready
                    && Cursor.lockState == CursorLockMode.Locked
                    && run.Levels != null && run.Levels.Length > 0)
                {
                    float colX = Screen.width - 360f;
                    float rowY = 100f;
                    var headerStyle = new GUIStyle(_cue) { fontSize = 14 };
                    ShadowLabel(new Rect(colX, rowY, 340f, 20f), "COURSES", headerStyle);
                    rowY += 22f;

                    var rowStyle = new GUIStyle(_label) { fontSize = 14 };
                    string weakestName = null;
                    int weakestOrder = int.MaxValue;
                    int weakestScore = int.MaxValue;
                    for (int i = 0; i < run.Levels.Length; i++)
                    {
                        var lvl = run.Levels[i];
                        string rank = run.BestRankFor(i);
                        int score = run.BestScoreFor(i);
                        bool isCurrent = i == run.CurrentLevel;
                        string marker = isCurrent ? "> " : "  ";
                        string rankText = string.IsNullOrEmpty(rank) ? "—" : rank;
                        var style = isCurrent ? new GUIStyle(_cue) { fontSize = 14 } : rowStyle;
                        ShadowLabel(new Rect(colX, rowY, 340f, 20f),
                                    $"{marker}{lvl.Name,-14} {rankText,-3} {score,6}", style);
                        rowY += 20f;

                        // Weakest = lowest RankOrder; ties break on lower
                        // stored score. Unplayed ("") sorts below D, so an
                        // unplayed course always wins this comparison.
                        int order = AlienSkies.Run.RunCore.RankOrder(rank);
                        if (order < weakestOrder || (order == weakestOrder && score < weakestScore))
                        {
                            weakestOrder = order;
                            weakestScore = score;
                            weakestName = lvl.Name;
                        }
                    }

                    if (weakestName != null)
                    {
                        string weakestLine = weakestOrder == AlienSkies.Run.RunCore.RankOrder("")
                            ? $"WEAKEST: {weakestName} (UNPLAYED)"
                            : $"WEAKEST: {weakestName}";
                        ShadowLabel(new Rect(colX, rowY + 4f, 340f, 20f), weakestLine,
                                    new GUIStyle(_cue) { fontSize = 14 });
                    }
                }

                // Kill feed — spawns below y~110 (the timer row ends there)
                // so the 36px rise (1.2s * 30f/s) never reaches into it.
                for (int i = _feed.Count - 1; i >= 0; i--)
                {
                    var e = _feed[i];
                    float alpha = Mathf.Clamp01(1f - e.age / 1.2f);
                    int mult = int.Parse(e.text.Substring(e.text.IndexOf('x') + 1));
                    var baseColor = mult >= 3 ? AlienSkies.Palette.Magenta : AlienSkies.Palette.Amber;
                    var feedStyle = new GUIStyle(_label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
                    feedStyle.normal.textColor = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
                    ShadowLabel(new Rect(Screen.width / 2f - 160f, 114f - e.age * 30f, 320f, 24f), e.text, feedStyle);
                }

                // Mid-run best ping (guardrail 9) — announced once, below the
                // kill feed's travel range (max rise ~78px) so it never collides.
                if (_bestPaceRemaining > 0f)
                {
                    var paceStyle = new GUIStyle(_label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
                    paceStyle.normal.textColor = AlienSkies.Palette.Amber;
                    ShadowLabel(new Rect(Screen.width / 2f - 160f, 150f, 320f, 28f), "BEST PACE", paceStyle);
                }
            }
            if (Combat.IsDead)
                ShadowLabel(new Rect(Screen.width / 2f - 200f, Screen.height / 2f + 40f, 400f, 40f),
                            $"DEAD — respawn in {Mathf.Max(0f, Combat.RespawnRemaining):0.0}s",
                            new GUIStyle(_cross) { fontSize = 30 });

            if (run != null && run.Core.State == AlienSkies.Run.RunState.Complete)
            {
                var rank = new GUIStyle(_cross) { fontSize = 110 };
                rank.normal.textColor = run.Core.Rank == "S" ? AlienSkies.Palette.Amber
                                     : run.Core.Rank == "D" ? AlienSkies.Palette.Magenta
                                     : AlienSkies.Palette.Cyan;
                ShadowLabel(new Rect(Screen.width / 2f - 100f, Screen.height / 2f - 170f, 200f, 130f),
                            run.Core.Rank, rank);
                var line = new GUIStyle(_cross) { fontSize = 24 };
                string gradeLevelName = run.CurrentLevelDef?.Name;
                ShadowLabel(new Rect(Screen.width / 2f - 250f, Screen.height / 2f - 20f, 500f, 32f),
                            (string.IsNullOrEmpty(gradeLevelName) ? "" : $"{gradeLevelName}   ")
                            + $"SCORE {run.Core.FinalScore}   TIME {run.Core.Time:0.0}s   KILLS {run.Core.Kills}   PEAK x{run.Core.PeakMultiplier}",
                            line);
                ShadowLabel(new Rect(Screen.width / 2f - 250f, Screen.height / 2f + 16f, 500f, 32f),
                            $"BEST {run.BestScore}" + (run.BestTime > 0f ? $" ({run.BestTime:0.0}s)" : ""),
                            new GUIStyle(line) { fontSize = 20 });
                // Grade delta (guardrail 9): the concrete points needed to
                // rank up, or the ceiling call-out once there's nowhere left to go.
                int toNext = run.Core.PointsToNextRank();
                ShadowLabel(new Rect(Screen.width / 2f - 250f, Screen.height / 2f + 52f, 500f, 26f),
                            toNext > 0 ? $"NEXT RANK IN +{toNext} PTS" : "CEILING REACHED",
                            new GUIStyle(line) { fontSize = 18 });
                ShadowLabel(new Rect(Screen.width / 2f - 250f, Screen.height / 2f + 82f, 500f, 32f),
                            $"restart in {Mathf.Max(0f, run.RestartRemaining):0.0}s",
                            new GUIStyle(line) { fontSize = 18 });
            }

            if (Feedback == null) return;
            var fc = Feedback.Core;
            if (fc.DamageFlash > 0f)
            {
                GUI.color = new Color(1f, 0.08f, 0.08f, fc.DamageFlash * 0.35f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            if (fc.HitmarkerRemaining > 0f)
                GUI.Label(new Rect(Screen.width / 2f - 20f, Screen.height / 2f - 20f, 40f, 40f),
                          "×", _hitmark);
            if (fc.KillFlashRemaining > 0f)
                ShadowLabel(new Rect(Screen.width / 2f - 150f, Screen.height / 2f - 110f, 300f, 44f),
                            "K I L L", _killLabel);
        }
    }
}
