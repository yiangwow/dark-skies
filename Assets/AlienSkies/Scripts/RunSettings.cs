namespace AlienSkies.Run
{
    /// <summary>Run-loop tuning. Par/target values are per-level knobs — for
    /// v1 there is one level (Sphere 1), so they live here.</summary>
    [System.Serializable]
    public class RunSettings
    {
        public float ParTime = 90f;             // seconds; under par earns the time bonus
        public int KillScore = 100;             // base points per kill, before multiplier
        public int MaxMultiplier = 4;           // heat maps 0..1 onto x1..xMax
        public float TimeBonusPerSecond = 10f;  // points per second under par
        public int STargetScore = 2000;         // final score for an S rank; A/B/C at 75/50/25%
        public float RestartDelay = 2.5f;       // grade screen dwell before auto-restart

        /// <summary>Copy with per-level overrides — levels swap par/target
        /// without mutating the shared config asset.</summary>
        public RunSettings WithOverrides(float parTime, int sTargetScore) =>
            new RunSettings
            {
                ParTime = parTime,
                KillScore = KillScore,
                MaxMultiplier = MaxMultiplier,
                TimeBonusPerSecond = TimeBonusPerSecond,
                STargetScore = sTargetScore,
                RestartDelay = RestartDelay,
            };
    }
}
