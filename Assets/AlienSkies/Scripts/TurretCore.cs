using UnityEngine;

namespace AlienSkies.Combat
{
    public enum TurretState { Idle, Tracking, Cooldown }

    /// <summary>Beam-turret brain, pure C#: acquire on visibility, track with
    /// an ANGULAR-SPEED-CAPPED aim (a fast player outruns the lock — momentum
    /// is the dodge), fire an instant beam after the warning ramp, cool down.
    /// FiredThisTick is a one-tick pulse; AimPoint is world-space.</summary>
    public sealed class TurretCore
    {
        public const float TrackTime = 0.9f;
        public const float CooldownTime = 2.5f;
        // Playtest-tuned 2026-08-29, two rounds. 140 tracked QUADRUPLE the
        // player's top ground speed (GroundMaxSpeed 10 at 15m is ~38 deg/s) —
        // "i do not outrun it". 40 overcorrected — "make the turret faster".
        // 60: a full-speed strafe only breaks lock inside ~9.5m, so mid-range
        // survival takes REAL momentum (launch/grapple passes shake it out to
        // ~25m); walking speed gets beamed everywhere.
        public const float TrackDegPerSec = 60f;
        public const float Range = 90f;

        public TurretState State { get; private set; } = TurretState.Idle;
        public Vector3 AimPoint { get; private set; }
        public float TrackProgress { get; private set; } // 0..1 while Tracking
        public bool FiredThisTick { get; private set; }

        float _timer;

        public void Tick(Vector3 selfPos, Vector3 targetPos, bool targetVisible, float dt)
        {
            FiredThisTick = false;
            bool inRange = (targetPos - selfPos).magnitude <= Range;
            bool engaged = targetVisible && inRange;

            switch (State)
            {
                case TurretState.Idle:
                    if (engaged)
                    {
                        State = TurretState.Tracking;
                        _timer = 0f;
                        AimPoint = targetPos; // first sight = honest snap; the LAG develops as they move
                    }
                    break;

                case TurretState.Tracking:
                    if (!engaged) { State = TurretState.Idle; TrackProgress = 0f; break; }
                    _timer += dt;
                    TrackProgress = Mathf.Clamp01(_timer / TrackTime);
                    // Rotate the aim ray toward the target, capped in deg/s.
                    Vector3 cur = AimPoint - selfPos;
                    Vector3 want = targetPos - selfPos;
                    float maxDeg = TrackDegPerSec * dt;
                    Vector3 newDir = Vector3.RotateTowards(cur.normalized, want.normalized,
                        maxDeg * Mathf.Deg2Rad, 0f);
                    AimPoint = selfPos + newDir * want.magnitude;
                    if (_timer >= TrackTime)
                    {
                        FiredThisTick = true;
                        State = TurretState.Cooldown;
                        _timer = 0f;
                        TrackProgress = 0f;
                    }
                    break;

                case TurretState.Cooldown:
                    _timer += dt;
                    if (_timer >= CooldownTime) { State = TurretState.Idle; _timer = 0f; }
                    break;
            }
        }
    }
}
