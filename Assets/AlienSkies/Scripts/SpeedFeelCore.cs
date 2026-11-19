using UnityEngine;

namespace AlienSkies.Movement
{
    /// <summary>Pure speed-presentation math: FOV kick, zip burst, strafe
    /// roll, speed-line intensity. Deterministic; the SpeedFeel MB just
    /// applies these to the camera every frame.</summary>
    public sealed class SpeedFeelCore
    {
        public const float BaseFov = 60f;
        public const float MaxKick = 15f;
        public const float ZipBurstFov = 6f;
        public const float ZipBurstDecay = 4f;   // FOV/s
        public const float MaxRollDegrees = 2.5f;
        public const float LineStartSpeed = 25f;
        public const float LineFullSpeed = 70f;
        public const float LineMaxIntensity = 0.6f;
        public const float AccelSmoothRate = 8f;         // 1/s; same lerp cadence SpeedFeel already uses for FOV
        public const float AccelInnerRadiusSpan = 20f;   // m/s^2 of smoothed accel that fully closes the inward pull
        // Must equal SpeedLines.shader's mask inner edge -- an unset
        // _InnerRadius property has to reproduce today's annulus exactly.
        public const float LineInnerRadiusBase = 0.25f;
        public const float LineInnerRadiusMaxShrink = 0.15f;

        public float ZipBurst { get; private set; }
        public float SmoothedAccel { get; private set; }

        float _prevSpeed;
        bool _hasSpeedSample;   // first tick has no delta yet -- lazy-init like PlayerMotor's _initialized

        public void RegisterZip() => ZipBurst = ZipBurstFov;

        /// <summary>Advances zip decay and the smoothed-accel estimate for
        /// the given frame speed. dt &lt;= 0 (paused frame) is a no-op --
        /// dividing by it would blow up rawAccel.</summary>
        public void Tick(float dt, float speed)
        {
            ZipBurst = Mathf.Max(0f, ZipBurst - ZipBurstDecay * dt);

            if (dt <= 0f) return;

            float rawAccel = _hasSpeedSample ? (speed - _prevSpeed) / dt : 0f;
            SmoothedAccel = Mathf.Lerp(SmoothedAccel, rawAccel, Mathf.Min(1f, AccelSmoothRate * dt));
            _prevSpeed = speed;
            _hasSpeedSample = true;
        }

        /// <summary>Target FOV for a horizontal speed (smoothing is the MB's job).</summary>
        public float FovFor(float speed) =>
            BaseFov + MaxKick * Mathf.Clamp01((speed - 12f) / 50f) + ZipBurst;

        /// <summary>Roll (degrees) from lateral velocity: sign = direction,
        /// magnitude scales with speed. lateral = local-space x velocity.</summary>
        public float RollFor(float lateralSpeed) =>
            -Mathf.Clamp(lateralSpeed / 12f, -1f, 1f) * MaxRollDegrees;

        /// <summary>Speed-line overlay intensity 0..LineMax.</summary>
        public float LineIntensityFor(float speed) =>
            LineMaxIntensity * Mathf.Clamp01((speed - LineStartSpeed) / (LineFullSpeed - LineStartSpeed));

        /// <summary>Annulus inner radius for the speed-line mask: gaining
        /// speed pulls the inner edge inward (note 32's accel-shrinks-
        /// innerRadius read), up to LineInnerRadiusMaxShrink at strong
        /// positive accel. Deceleration does nothing -- clamped at the base,
        /// so braking never flashes the screen.</summary>
        public float InnerRadiusFor() =>
            LineInnerRadiusBase - LineInnerRadiusMaxShrink * Mathf.Clamp01(SmoothedAccel / AccelInnerRadiusSpan);
    }
}
