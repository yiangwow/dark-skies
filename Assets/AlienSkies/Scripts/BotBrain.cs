using UnityEngine;

namespace AlienSkies.Bots
{
    /// <summary>Pure-C# practice-bot brain: orbit-strafe movement, timed hops,
    /// cadenced fire with a deliberate aim-error cone. Deterministic for a seed
    /// so behavior is unit-testable; deliberately dumb — it exists to be fun to
    /// chase, not to win.</summary>
    public sealed class BotBrain
    {
        public struct Sense
        {
            public Vector3 SelfPos;    // eye position
            public Vector3 TargetPos;  // target eye position
            public Vector3 TargetVelocity; // default zero = legacy (unled) aim
            public bool HasTarget;
            public bool TargetVisible; // driver's line-of-sight check
            public bool Grounded;
            public bool EdgeAhead; // true = the floor ends just ahead along the last movement direction (default false = safe)
        }

        public struct Intent
        {
            public Vector3 WishDir; // flat, normalized (or zero)
            public bool Jump;
            public Vector3 AimDir;
            public bool Fire;
            public bool Telegraphing;   // advisory: recomputed every tick, current led aim
            public Vector3 TelegraphAim;
            public float Yaw;       // degrees, body orientation
        }

        public float OrbitRadius = 18f;
        public float FireRange = 80f;
        public float FireInterval = 1.2f;
        public float AimErrorDegrees = 6f;
        public float OrbitFlipInterval = 3.5f;
        public float HopInterval = 2.2f;
        /// <summary>Real rocket speed, used for time-of-flight leading only.</summary>
        public float ProjectileSpeed = 150f;
        /// <summary>Warning window before a shot: the last TelegraphTime seconds
        /// of the fire cooldown are visually telegraphed (see Intent.Telegraphing).</summary>
        public float TelegraphTime = 0.5f;
        /// <summary>Stand still, only aim and fire. Playtest: the orbit-strafe
        /// walked off ledges constantly — now edge-sensed (see EdgeAhead), so
        /// most posts stay Stationary by authoring choice, not necessity.</summary>
        public bool Stationary;

        readonly System.Random _rng;
        float _fireCooldown;
        float _orbitSign = 1f;
        float _orbitFlipTimer;
        float _hopTimer;
        bool _edgeLatched;     // reacted to the current edge encounter already
        Vector3 _prevWish;     // last SAFE (non-edge) wish, for the retreat vector
        Vector3 _retreatDir;   // latched retreat direction while EdgeAhead holds
        bool _wasFireEligible; // visible+in-range last tick, for the hold-fire rising edge

        public BotBrain(int seed)
        {
            _rng = new System.Random(seed);
            _orbitFlipTimer = OrbitFlipInterval;
            _hopTimer = HopInterval;
            _fireCooldown = 1f; // spawn grace — don't shoot the instant it exists
        }

        float NextFloat() => (float)_rng.NextDouble();

        public Intent Tick(in Sense s, float dt)
        {
            var intent = new Intent();
            _fireCooldown -= dt;
            if (!s.HasTarget)
            {
                // Reset the rising-edge tracker so a re-acquired target always
                // gets a fresh false->true transition (review finding: leaving
                // this stale across a no-target gap would skip the hold-fire
                // raise on re-acquisition — one un-telegraphed shot).
                _wasFireEligible = false;
                return intent;
            }

            Vector3 toTarget = s.TargetPos - s.SelfPos;
            float dist = toTarget.magnitude;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float flatDist = flat.magnitude;
            Vector3 fwd = flatDist > 1e-2f ? flat / flatDist : Vector3.forward;
            Vector3 tangent = Vector3.Cross(Vector3.up, fwd) * _orbitSign;

            _orbitFlipTimer -= dt;
            if (_orbitFlipTimer <= 0f)
            {
                _orbitSign = -_orbitSign;
                _orbitFlipTimer = OrbitFlipInterval * (0.6f + 0.8f * NextFloat());
            }

            if (!Stationary)
            {
                Vector3 wish;
                if (s.EdgeAhead)
                {
                    // Latched retreat: react once on the RISING edge (flip the
                    // orbit sign, capture a retreat vector), then keep issuing
                    // that SAME vector every tick the edge holds. Recomputing
                    // -_prevWish every tick would self-invert once the retreat
                    // itself became _prevWish (round-3 review finding).
                    if (!_edgeLatched)
                    {
                        _orbitSign = -_orbitSign;
                        var retreat = new Vector3(-_prevWish.x, 0f, -_prevWish.z);
                        // No usable prior wish (edge on the very first tick) —
                        // fall back to the reversed tangent.
                        _retreatDir = retreat.sqrMagnitude > 1e-6f ? retreat.normalized : -tangent;
                        _edgeLatched = true;
                    }
                    wish = _retreatDir;
                    // _prevWish deliberately NOT updated here — it must stay
                    // the last SAFE wish so the latch survives a sustained edge.
                }
                else
                {
                    _edgeLatched = false; // re-armed: the next edge reacts again

                    // Approach when far, back off when crowding, strafe on the ring.
                    if (flatDist > OrbitRadius * 1.25f) wish = fwd + tangent * 0.5f;
                    else if (flatDist < OrbitRadius * 0.75f) wish = tangent - fwd * 0.6f;
                    else wish = tangent;
                    wish = wish.normalized;
                    _prevWish = wish;
                }
                intent.WishDir = wish;

                // Hop on a jittered timer — reads alive, and is the planet-launch
                // key whenever the bot happens to be touching a world. Suppressed
                // at the edge: a jump here would launch the bot off the platform.
                _hopTimer -= dt;
                if (s.Grounded && !s.EdgeAhead && _hopTimer <= 0f)
                {
                    intent.Jump = true;
                    _hopTimer = HopInterval * (0.5f + NextFloat());
                }
            }

            bool eligible = s.TargetVisible && dist <= FireRange;
            // Hold-fire: raise the cooldown to a full telegraph window the
            // instant firing conditions are (re-)met, rising-edge only — a
            // level-triggered raise would pin the cooldown forever and the
            // bot would never fire. Without this the FIRST shot from every
            // post is un-telegraphed, since _fireCooldown parks at <=0 while
            // the target is hidden/out of range.
            if (eligible && !_wasFireEligible && _fireCooldown < TelegraphTime)
                _fireCooldown = TelegraphTime;
            _wasFireEligible = eligible;

            // Leading: aim at where the target will be after this shot's
            // time-of-flight (clamped so long-range leads don't look psychic).
            float tof = ProjectileSpeed > 1e-4f ? Mathf.Min(dist / ProjectileSpeed, 1.2f) : 1.2f;
            Vector3 leadPos = s.TargetPos + s.TargetVelocity * tof;
            Vector3 toLead = leadPos - s.SelfPos;
            float leadDist = toLead.magnitude;
            Vector3 aim = leadDist > 1e-3f ? toLead / leadDist : fwd;

            // Telegraph is advisory state recomputed every tick with the
            // CURRENT led aim; it does not gate anything itself.
            intent.Telegraphing = eligible && _fireCooldown <= TelegraphTime;
            if (intent.Telegraphing) intent.TelegraphAim = aim;

            if (eligible && _fireCooldown <= 0f)
            {
                intent.Fire = true;
                _fireCooldown = FireInterval * (0.8f + 0.4f * NextFloat());
                aim = ApplyAimError(aim);
            }
            intent.AimDir = aim;
            intent.Yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            return intent;
        }

        Vector3 ApplyAimError(Vector3 dir)
        {
            if (AimErrorDegrees <= 0f) return dir;
            var raw = new Vector3(NextFloat() - 0.5f, NextFloat() - 0.5f, NextFloat() - 0.5f);
            Vector3 axis = Vector3.Cross(dir, raw);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
            return Quaternion.AngleAxis(NextFloat() * AimErrorDegrees, axis.normalized) * dir;
        }
    }
}
