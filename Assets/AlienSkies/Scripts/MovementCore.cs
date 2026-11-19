using UnityEngine;

namespace AlienSkies.Movement
{
    /// <summary>Complete simulation state of a MovementCore — everything a network
    /// reconcile must restore for replayed ticks to be deterministic.</summary>
    public struct MovementCoreState
    {
        public Vector3 Velocity;
        public bool IsGrappling;
        public Vector3 GrappleAnchor;
        public bool IsZipping;
        public bool AttachPending;
        public float ZipCooldown;
        public float JumpBuffer;
        public float Coyote;
        public float SurfaceContact;
        public Vector3 SurfaceNormal;
    }

    /// <summary>Pure movement math. No MonoBehaviour, no Physics — fully unit-testable.
    /// Quake lineage: friction on ground only; acceleration capped along the wish
    /// direction so momentum from grapples/falls is never destroyed by steering.</summary>
    public sealed class MovementCore
    {
        const float GroundStickSpeed = -2f;

        readonly MovementSettings _s;

        public Vector3 Velocity { get; private set; }
        public bool IsGrappling { get; private set; }
        public Vector3 GrappleAnchor { get; private set; }
        /// <summary>The real CharacterController grounded signal for this
        /// tick — NOT the same thing as SurfaceContactRemaining, which is a
        /// planet-only coyote window (PlanetSurface-gated; platforms never
        /// arm it). Consumers that need "is this a camper on a platform"
        /// must read this, not the coyote window.</summary>
        public bool IsGrounded { get; private set; }

        bool _attachPending;
        float _zipCooldown;
        float _jumpBuffer;
        float _coyote;
        float _surfaceContact;
        Vector3 _surfaceNormal;

        public bool IsZipping { get; private set; }
        public float ZipCooldownRemaining => Mathf.Max(_zipCooldown, 0f);
        public float SurfaceContactRemaining => Mathf.Max(_surfaceContact, 0f);

        /// <summary>Motor calls this from OnControllerColliderHit for PLANET
        /// colliders only — arms a short window in which jump kicks off the
        /// surface along its normal (the vertical-recovery mechanic).</summary>
        public void RegisterSurfaceContact(Vector3 normal)
        {
            _surfaceNormal = normal;
            _surfaceContact = _s.CoyoteTime;
        }

        public MovementCore(MovementSettings settings) => _s = settings;

        public void SetVelocity(Vector3 v) => Velocity = v;

        public MovementCoreState CaptureState() => new MovementCoreState
        {
            Velocity = Velocity,
            IsGrappling = IsGrappling,
            GrappleAnchor = GrappleAnchor,
            IsZipping = IsZipping,
            AttachPending = _attachPending,
            ZipCooldown = _zipCooldown,
            JumpBuffer = _jumpBuffer,
            Coyote = _coyote,
            SurfaceContact = _surfaceContact,
            SurfaceNormal = _surfaceNormal,
        };

        public void ApplyState(in MovementCoreState s)
        {
            Velocity = s.Velocity;
            IsGrappling = s.IsGrappling;
            GrappleAnchor = s.GrappleAnchor;
            IsZipping = s.IsZipping;
            _attachPending = s.AttachPending;
            _zipCooldown = s.ZipCooldown;
            _jumpBuffer = s.JumpBuffer;
            _coyote = s.Coyote;
            _surfaceContact = s.SurfaceContact;
            _surfaceNormal = s.SurfaceNormal;
        }

        public void Reset()
        {
            Velocity = Vector3.zero;
            IsGrappling = false;
            IsZipping = false;
            _attachPending = false;
            _zipCooldown = 0f;
            _jumpBuffer = 0f;
            _coyote = 0f;
            _surfaceContact = 0f;
        }

        public void Tick(in MoveInput input, Vector3 position, bool grounded, float dt)
        {
            IsGrounded = grounded;
            if (_zipCooldown > 0f) _zipCooldown -= dt;
            _jumpBuffer = input.JumpPressed ? _s.JumpBufferTime : _jumpBuffer - dt;
            _coyote = grounded ? _s.CoyoteTime : _coyote - dt;
            _surfaceContact -= dt;

            if (IsZipping)
            {
                ZipTowardAnchor(position);
                return; // zip owns velocity: no steering, gravity, jump, or caps
            }

            if (grounded && !IsGrappling)
            {
                ApplyGroundFriction(dt);
                Accelerate(input.WishDir, _s.GroundMaxSpeed, _s.GroundAccel, dt);
                if (Velocity.y < GroundStickSpeed)
                    Velocity = new Vector3(Velocity.x, GroundStickSpeed, Velocity.z);
            }
            else
            {
                float accel = IsGrappling ? _s.GrappleSteerAccel : _s.AirAccel;
                Accelerate(input.WishDir, _s.AirControlMaxSpeed, accel, dt);
                SteerAir(input.WishDir, dt);
            }

            if (IsGrappling)
                PullTowardAnchor(position, dt);
            else if (!grounded)
                Velocity += Vector3.down * (_s.Gravity * dt);

            if (_jumpBuffer > 0f)
            {
                if (_surfaceContact > 0f)
                {
                    // Planet kick: additive along the surface normal so incoming
                    // momentum carries through — and it beats the ground hop, so
                    // planet tops launch too.
                    Velocity += _surfaceNormal * _s.PlanetLaunchSpeed;
                    _jumpBuffer = 0f;
                    _surfaceContact = 0f;
                }
                else if (grounded || _coyote > 0f)
                {
                    Velocity = new Vector3(Velocity.x, _s.JumpSpeed, Velocity.z);
                    _jumpBuffer = 0f;
                    _coyote = 0f;
                }
            }

            ApplySoftCap(dt);
        }

        void ApplyGroundFriction(float dt)
        {
            var horiz = new Vector3(Velocity.x, 0f, Velocity.z);
            float speed = horiz.magnitude;
            if (speed <= 0f) return;
            float newSpeed = Mathf.Max(speed - speed * _s.GroundFriction * dt, 0f);
            horiz *= newSpeed / speed;
            Velocity = new Vector3(horiz.x, Velocity.y, horiz.z);
        }

        void Accelerate(Vector3 wishDir, float maxSpeed, float accel, float dt)
        {
            if (wishDir.sqrMagnitude < 1e-6f) return;
            float speedAlong = Vector3.Dot(Velocity, wishDir);
            float addSpeed = maxSpeed - speedAlong;
            if (addSpeed <= 0f) return;
            Velocity += wishDir * Mathf.Min(accel * dt, addSpeed);
        }

        /// <summary>Momentum steering: rotate the horizontal velocity toward the held
        /// direction without adding or removing speed. This is what lets WASD bend a
        /// 40 m/s flight path — quake accel alone caps out at AirControlMaxSpeed.</summary>
        void SteerAir(Vector3 wishDir, float dt)
        {
            if (wishDir.sqrMagnitude < 1e-6f) return;
            var horiz = new Vector3(Velocity.x, 0f, Velocity.z);
            float speed = horiz.magnitude;
            if (speed < 1f) return; // nothing meaningful to redirect
            var steered = Vector3.RotateTowards(
                horiz / speed, wishDir, _s.AirSteerRate * Mathf.Deg2Rad * dt, 0f);
            Velocity = new Vector3(steered.x * speed, Velocity.y, steered.z * speed);
        }

        void PullTowardAnchor(Vector3 position, float dt)
        {
            var toAnchor = GrappleAnchor - position;
            if (toAnchor.sqrMagnitude < 1f) { _attachPending = false; return; } // arrived
            var dir = toAnchor.normalized;
            if (_attachPending)
            {
                // Attach yank: guarantee a minimum closing speed the instant the hook
                // connects, so the zip snaps instead of winding up.
                _attachPending = false;
                float closing = Vector3.Dot(Velocity, dir);
                if (closing < _s.GrappleAttachSpeed)
                    Velocity += dir * (_s.GrappleAttachSpeed - closing);
            }
            Velocity += dir * (_s.GrapplePullAccel * dt);
        }

        /// <summary>Zip-strike: overwrite velocity with a straight-line flight to the
        /// anchor. Re-aims every tick so it truly arrives; on arrival it detaches and
        /// clamps speed so you stick the landing instead of skidding off.</summary>
        void ZipTowardAnchor(Vector3 position)
        {
            var toAnchor = GrappleAnchor - position;
            if (toAnchor.magnitude <= _s.ZipArriveRadius)
            {
                IsZipping = false;
                IsGrappling = false; // arrival: no release boost — landing, not flinging
                if (Velocity.magnitude > _s.ZipLandSpeed)
                    Velocity = Velocity.normalized * _s.ZipLandSpeed;
                return;
            }
            Velocity = toAnchor.normalized * _s.ZipSpeed;
        }

        public bool StartZip()
        {
            if (!IsGrappling || IsZipping || _zipCooldown > 0f) return false;
            IsZipping = true;
            _attachPending = false; // zip supersedes the attach yank
            _zipCooldown = _s.ZipCooldown;
            return true;
        }

        void ApplySoftCap(float dt)
        {
            float speed = Velocity.magnitude;
            if (speed <= _s.SoftCapSpeed) return;
            float over = (speed - _s.SoftCapSpeed) / _s.SoftCapSpeed;
            Velocity *= Mathf.Max(1f - _s.SoftCapDrag * over * dt, 0f);
        }

        // --- Grapple API (exercised by Task 5's tests) ---

        public void StartGrapple(Vector3 anchor)
        {
            IsGrappling = true;
            GrappleAnchor = anchor;
            _attachPending = true;
        }

        public void StopGrapple()
        {
            if (!IsGrappling) return;
            IsGrappling = false;
            IsZipping = false;
            _attachPending = false;
            var dir = Velocity.sqrMagnitude > 1e-4f ? Velocity.normalized : Vector3.up;
            Velocity += dir * _s.GrappleReleaseBoost + Vector3.up * _s.GrappleReleaseUpBoost;
        }

        /// <summary>Collision response: remove the velocity component pointing into a
        /// surface (called by the motor from OnControllerColliderHit).</summary>
        public void ClampAgainstSurface(Vector3 normal)
        {
            float into = Vector3.Dot(Velocity, normal);
            if (into < 0f) Velocity -= normal * into;
        }
    }
}
