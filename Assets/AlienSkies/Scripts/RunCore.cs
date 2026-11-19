using UnityEngine;

namespace AlienSkies.Run
{
    public enum RunState { Ready, Running, Complete }

    /// <summary>The run loop: timer, score, music-heat multiplier, rank.
    /// Pure C# — the timer starts on the first input (RunManager decides what
    /// counts as input), kills score through the heat multiplier, the exit
    /// gate completes the run, death resets it. Combat is the score; the
    /// music is the multiplier (ADR-004).</summary>
    public sealed class RunCore
    {
        public RunState State { get; private set; } = RunState.Ready;
        public float Time { get; private set; }
        public int Score { get; private set; }
        public int Kills { get; private set; }
        public int Multiplier { get; private set; } = 1;
        public int PeakMultiplier { get; private set; } = 1;
        public int LastKillPoints { get; private set; }
        public int FinalScore { get; private set; }
        public string Rank { get; private set; } = "";

        // Rank thresholds as a fraction of STargetScore (RunSettings' own
        // comment: "A/B/C at 75/50/25%") — named so CompleteRun and
        // PointsToNextRank read the same boundaries instead of duplicating them.
        const float RankSFrac = 1f;
        const float RankAFrac = 0.75f;
        const float RankBFrac = 0.5f;
        const float RankCFrac = 0.25f;

        readonly RunSettings _s;

        public RunCore(RunSettings settings) => _s = settings ?? new RunSettings();

        /// <summary>Heat 0..1 maps to x1..xMax in integer steps: x1 at zero
        /// heat, xMax only at (or very near) full heat.</summary>
        public int MultiplierFor(float heat)
        {
            float h = Mathf.Clamp01(heat);
            return 1 + Mathf.Min(_s.MaxMultiplier - 1, Mathf.FloorToInt(h * (_s.MaxMultiplier - 1) + 0.0001f));
        }

        public void StartRun()
        {
            if (State != RunState.Ready) return;
            State = RunState.Running;
        }

        public void Tick(float dt)
        {
            if (State == RunState.Running) Time += dt;
        }

        public void RegisterKill(float heat)
        {
            if (State != RunState.Running) return;
            Multiplier = MultiplierFor(heat);
            PeakMultiplier = Mathf.Max(PeakMultiplier, Multiplier);
            LastKillPoints = _s.KillScore * Multiplier;
            Score += LastKillPoints;
            Kills++;
        }

        public void CompleteRun()
        {
            if (State != RunState.Running) return;
            State = RunState.Complete;
            float underPar = Mathf.Max(0f, _s.ParTime - Time);
            FinalScore = Score + Mathf.RoundToInt(underPar * _s.TimeBonusPerSecond);
            float frac = _s.STargetScore > 0 ? (float)FinalScore / _s.STargetScore : 0f;
            Rank = frac >= RankSFrac ? "S" : frac >= RankAFrac ? "A" : frac >= RankBFrac ? "B" : frac >= RankCFrac ? "C" : "D";
        }

        /// <summary>Grade-delta (guardrail 9): FinalScore points still needed to
        /// reach the next rank up, using the same thresholds as CompleteRun's
        /// Rank assignment. Zero once Rank is already S.</summary>
        public int PointsToNextRank()
        {
            if (Rank == "S" || _s.STargetScore <= 0) return 0;
            float nextFrac = Rank == "A" ? RankSFrac
                            : Rank == "B" ? RankAFrac
                            : Rank == "C" ? RankBFrac
                            : RankCFrac; // D, or "" (not yet completed)
            int threshold = Mathf.CeilToInt(nextFrac * _s.STargetScore);
            return Mathf.Max(0, threshold - FinalScore);
        }

        /// <summary>Total order over rank strings — D worse than C worse than
        /// B worse than A worse than S — for comparing a completed run's rank
        /// against a stored best without re-deriving thresholds (guardrail 9,
        /// course-select readout). "" (no completed run yet) sorts below D,
        /// the worst possible standing.</summary>
        public static int RankOrder(string rank)
        {
            switch (rank)
            {
                case "S": return 5;
                case "A": return 4;
                case "B": return 3;
                case "C": return 2;
                case "D": return 1;
                default: return 0;
            }
        }

        public void Reset()
        {
            State = RunState.Ready;
            Time = 0f;
            Score = 0;
            Kills = 0;
            Multiplier = 1;
            PeakMultiplier = 1;
            LastKillPoints = 0;
            FinalScore = 0;
            Rank = "";
        }
    }
}
