using System.Collections.Generic;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Combat
{
    /// <summary>Projectile: spherecast-stepped flight, splash damage with
    /// linear falloff, knockback into MovementCore velocity (rocket jumps
    /// included via SelfDamageScale + self-knockback).</summary>
    public sealed class Rocket : MonoBehaviour
    {
        public AudioClip ExplosionClip;

        PlayerCombat _shooter;
        Vector3 _velocity;
        WeaponStats _stats;
        float _life;

        public void Init(PlayerCombat shooter, Vector3 velocity, WeaponStats stats)
        {
            _shooter = shooter;
            _velocity = velocity;
            _stats = stats;
        }

        /// <summary>Who fired this — spawners sweep rockets whose shooter died.</summary>
        public PlayerCombat Shooter => _shooter;

        void Update()
        {
            float dt = Time.deltaTime;
            _life += dt;
            if (_life > 10f) { Explode(transform.position, Vector3.up); return; }

            Vector3 step = _velocity * dt;
            // 0.45 radius: generous direct-hit proximity — rockets should feel
            // like they clip people they nearly touch.
            if (Physics.SphereCast(transform.position, 0.45f, _velocity.normalized,
                    out var hit, step.magnitude + 0.05f))
            {
                // Don't detonate on the shooter's own capsule in the first moments.
                bool isShooter = hit.collider.GetComponentInParent<PlayerCombat>() == _shooter;
                if (!(isShooter && _life < 0.2f)) { Explode(hit.point, hit.normal, hitSurface: true); return; }
            }
            transform.position += step;
        }

        // hitSurface distinguishes a real SphereCast hit from the 10s timeout
        // path above, which passes Vector3.up with a mid-air position — a
        // flag (not a Vector3.up sentinel check) so the timeout's "no hit"
        // intent stays explicit at both call sites rather than implied.
        void Explode(Vector3 at, Vector3 surfaceNormal, bool hitSurface = false)
        {
            var hitVictims = new HashSet<IDamageable>();
            foreach (var col in Physics.OverlapSphere(at, _stats.SplashRadius))
            {
                var victim = col.GetComponentInParent<IDamageable>();
                if (victim == null || !hitVictims.Add(victim)) continue;

                var victimTransform = ((Component)victim).transform;
                Vector3 to = victimTransform.position + Vector3.up * 0.9f - at;
                float dist = to.magnitude;
                // OverlapSphere admits any victim whose COLLIDER intersects
                // the sphere, even when its +0.9m damage point sits outside
                // the radius — without this guard, falloff below goes
                // negative and PULLS the victim toward the blast.
                if (dist > _stats.SplashRadius) continue;
                float falloff = 1f - dist / _stats.SplashRadius;
                float dmg = _stats.Damage * falloff * (ReferenceEquals(victim, _shooter) ? _stats.SelfDamageScale : 1f);
                victim.TakeDamage(dmg, _shooter);

                // Turrets don't move — null-check before knocking them back.
                var motor = ((Component)victim).GetComponent<PlayerMotor>();
                if (motor != null)
                    motor.ApplyImpulse(to.normalized * (_stats.Knockback * falloff));
            }
            CombatFx.Explosion(at, surfaceNormal, SplashOrDefault());
            // Scorch only on a real surface hit (guardrail 1: permanence
            // accumulates run-evidence) — the timeout path's Vector3.up/
            // mid-air position has nothing to mark.
            if (hitSurface) CombatFx.Scorch(at, surfaceNormal);
            if (ExplosionClip != null) AudioSource.PlayClipAtPoint(ExplosionClip, at, 0.9f);
            Destroy(gameObject);
        }

        float SplashOrDefault() => _stats != null ? _stats.SplashRadius : 6f;
    }
}
