using UnityEngine;

namespace AlienSkies.Audio
{
    /// <summary>Pure-C# dynamic music brain: combat events add heat, heat
    /// decays over time, and the resulting Level (floor + intensity, ducked at
    /// round end) drives the mix — volume and low-pass cutoff. Heat can
    /// overshoot 1 slightly so a hot streak stays at full intensity for a
    /// moment instead of sagging the instant the fight pauses.</summary>
    public sealed class MusicCore
    {
        public enum KillMoveState { Grounded, Airborne, Flow } // Flow = grapple or zip

        public float DecayPerSecond = 0.12f;
        public float DuckFactor = 0.35f;   // round-over multiplier
        public float FloorLevel = 0.45f;   // music never fully disappears
        public float MaxHeat = 1.5f;       // overshoot buffer above full intensity

        public float HitHeat = 0.08f;           // attacker-side non-kill hit: trickle only
        public float HitTrickleCooldown = 0.7f; // min seconds between hit-trickle adds
        public float KillHeatGrounded = 0.33f;
        public float KillHeatAirborne = 0.60f;
        public float KillHeatFlow = 0.72f;
        public float StalenessFactor = 0.65f; // consecutive same-state kill multiplier

        public bool RoundOver;

        float _heat;
        KillMoveState _lastKillState;
        bool _hasKilled;
        int _sameStateStreak;
        float _hitCooldown;

        public float Intensity => _heat < 1f ? _heat : 1f;

        /// <summary>0..1 mix level for the manager to ride.</summary>
        public float Level
        {
            get
            {
                float level = FloorLevel + (1f - FloorLevel) * Intensity;
                return RoundOver ? level * DuckFactor : level;
            }
        }

        /// <summary>Non-kill hit trickle, RATE-LIMITED: one add per
        /// HitTrickleCooldown regardless of weapon — the shotgun's pellet loop
        /// raises up to 16 hit-confirms in ONE frame (16 x 0.08 = instant x4
        /// from a dead stop), and the rifle's 0.207s gate would out-run decay
        /// (0.386/s vs 0.12/s). Capped: max 0.08/0.7 = 0.114/s &lt; decay — no
        /// weapon can hold a tier by plinking. Cooldown ticks in Tick().</summary>
        public void RegisterHit()
        {
            if (_hitCooldown > 0f) return;
            _hitCooldown = HitTrickleCooldown;
            AddHeat(HitHeat);
        }

        public void RegisterKill(KillMoveState state)
        {
            _sameStateStreak = _hasKilled && state == _lastKillState ? _sameStateStreak + 1 : 0;
            _hasKilled = true; _lastKillState = state;
            float baseHeat = state == KillMoveState.Flow ? KillHeatFlow
                           : state == KillMoveState.Airborne ? KillHeatAirborne
                           : KillHeatGrounded;
            AddHeat(baseHeat * Mathf.Pow(StalenessFactor, _sameStateStreak));
        }

        /// <summary>Guardrail 7: damage costs exactly one multiplier tier — heat
        /// lands just under the current tier's floor (top of the tier below). At
        /// tier 1 there is nothing below: heat halves instead (still recoverable).
        /// maxMultiplier mirrors RunCore.MultiplierFor's mapping.</summary>
        public void DropTier(int maxMultiplier)
        {
            int bands = Mathf.Max(1, maxMultiplier - 1);
            int tier = 1 + Mathf.Min(bands, Mathf.FloorToInt(Intensity * bands + 0.0001f));
            _heat = tier > 1 ? Mathf.Max(0f, (float)(tier - 1) / bands - 0.02f)
                             : _heat * 0.5f;
        }

        void AddHeat(float amount)
        {
            _heat += amount;
            if (_heat > MaxHeat) _heat = MaxHeat;
        }

        public void Tick(float dt)
        {
            _heat -= DecayPerSecond * dt;
            if (_heat < 0f) _heat = 0f;
            if (_hitCooldown > 0f) _hitCooldown -= dt;
        }

        /// <summary>Runs start cold: RunManager zeroes the scoring heat at
        /// every run boundary so multipliers can't leak between attempts.
        /// Also clears the kill-state streak/hit cooldown — a stale streak
        /// or an armed cooldown must not leak into the next run.</summary>
        public void ResetHeat()
        {
            _heat = 0f;
            _hasKilled = false;
            _sameStateStreak = 0;
            _hitCooldown = 0f;
        }
    }
}
