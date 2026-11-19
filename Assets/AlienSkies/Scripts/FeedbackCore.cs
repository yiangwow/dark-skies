using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Pure feedback state: trauma-based screenshake (magnitude =
    /// trauma², so small hits whisper and big hits roar) plus hitmarker /
    /// kill-flash / damage-flash timers. CombatFeedback drives it per frame.</summary>
    public sealed class FeedbackCore
    {
        public const float TraumaDecay = 1.4f;      // trauma units per second
        public const float HitmarkerTime = 0.18f;   // seconds
        public const float KillFlashTime = 0.9f;    // seconds
        public const float DamageFlashDecay = 1.2f; // flash units per second
        public const float KickDecay = 14f;         // kick degrees/sec of ease-out — fast, ~0.15s to settle

        public float Trauma { get; private set; }
        public float ShakeMagnitude => Trauma * Trauma;
        public float HitmarkerRemaining { get; private set; }
        public float KillFlashRemaining { get; private set; }
        public float DamageFlash { get; private set; }
        /// <summary>Degrees, positive = camera tips UP — opposite the fire
        /// direction, the classic recoil read. This is DIRECTIONAL feedback
        /// (guardrail 1: camera kick opposite fire) and composes with trauma
        /// shake in CombatFeedback's single rotation write — it must never
        /// get its own transform write.</summary>
        public float KickPitch { get; private set; }

        public void AddTrauma(float amount) => Trauma = Mathf.Clamp01(Trauma + amount);

        /// <summary>Hard clamp at 4°: stacked rockets must never wrench the view.</summary>
        public void AddKick(float degrees) => KickPitch = Mathf.Min(4f, KickPitch + degrees);

        public void RegisterHit(bool killed)
        {
            HitmarkerRemaining = HitmarkerTime;
            if (killed) KillFlashRemaining = KillFlashTime;
        }

        /// <summary>fraction = damage / max health.</summary>
        public void RegisterDamageTaken(float fraction)
        {
            DamageFlash = Mathf.Clamp01(DamageFlash + fraction * 2f);
            AddTrauma(fraction * 1.2f);
        }

        public void Tick(float dt)
        {
            Trauma = Mathf.Max(0f, Trauma - TraumaDecay * dt);
            HitmarkerRemaining = Mathf.Max(0f, HitmarkerRemaining - dt);
            KillFlashRemaining = Mathf.Max(0f, KillFlashRemaining - dt);
            DamageFlash = Mathf.Max(0f, DamageFlash - DamageFlashDecay * dt);
            KickPitch = Mathf.Max(0f, KickPitch - KickDecay * dt);
        }
    }
}
