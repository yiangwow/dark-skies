using UnityEngine;
using UnityEngine.InputSystem;
using AlienSkies.Movement;

namespace AlienSkies.Combat
{
    /// <summary>Who last damaged this player — Turret is the only null-attacker
    /// path (Turret.cs calls TakeDamage(BeamDamage, null)); SelfSplash is your
    /// own rocket. The Voice reads this at death to name the killer (guardrail
    /// 8: unattributed deaths end sessions).</summary>
    public enum DamageSourceKind { None, Drone, Turret, SelfSplash }

    /// <summary>Combat glue: local input fires directly; CombatCore owns
    /// health/death/frags. What used to be SyncVar OnChange handlers are now
    /// direct calls at the mutation sites; what used to be RPCs are methods.</summary>
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerCombat : MonoBehaviour, IDamageable
    {
        public CombatConfig Config;
        public GameObject RocketPrefab;
        public Material ExplosionShellMaterial;
        public AudioSource FireAudio;
        public AudioClip RocketClip;
        public AudioClip RifleClip;
        public AudioClip ShotgunClip;

        public event System.Action<int> OnFired;         // weapon index (local player)
        public event System.Action<int> OnWeaponChanged; // weapon index (local player)
        public event System.Action<float, bool> OnHitConfirmed; // damage, killed (attacker)
        public event System.Action<float> OnDamaged;            // damage fraction (victim)

        public float Health => _core.Health;
        public int Frags => _core.Frags;
        public int Deaths => _core.Deaths;
        public bool IsDead => _core.IsDead;
        /// <summary>Who dealt the most recent damage — read by the Voice at
        /// death to name the killer (guardrail 8).</summary>
        public DamageSourceKind LastDamageSource { get; private set; }
        public float RespawnRemaining { get; private set; }
        public int CurrentWeapon => _weapons.CurrentIndex;
        /// <summary>Real speed of the rockets THIS combatant fires (config or
        /// fallback). Anything predicting flight time must read this — a lead
        /// knob that disagrees with real speed systematically misses.</summary>
        public float RocketSpeed => S.Weapons[0].ProjectileSpeed;
        public string CurrentWeaponName => S.Weapons[_weapons.CurrentIndex].Name;
        public int FragTarget => S.FragTarget;

        /// <summary>True for the human player: their motor reads devices.
        /// Bots get an ExternalInput before their first frame.</summary>
        public bool IsLocal => _motor.ExternalInput == null;

        CombatCore _core;
        WeaponCore _weapons;
        PlayerMotor _motor;
        readonly float[] _lastFire = new float[8];
        /// <summary>Procedural low-end layer under the rocket clip (guardrail
        /// 1: layered fire sound) — built once in Awake, same squared-decay
        /// envelope shape as CombatFeedback's MakeBlip.</summary>
        AudioClip _thumpClip;

        CombatSettings S => Config != null ? Config.Settings : _fallback;
        readonly CombatSettings _fallback = new CombatSettings();

        void Awake()
        {
            if (ExplosionShellMaterial != null) CombatFx.ShellMaterial = ExplosionShellMaterial;
            _motor = GetComponent<PlayerMotor>();
            _core = new CombatCore(S);
            _weapons = new WeaponCore(S);
            _motor.KillPlaneCrossed += HandleKillPlane;
            _thumpClip = MakeThump();
        }

        /// <summary>~0.12s 110Hz sine, squared-decay envelope — the thump is
        /// the body, the clip is the crack (guardrail 1: layered fire sound).
        /// PlayerCombat has no shared blip infrastructure of its own (that
        /// lives in CombatFeedback), so this is a small private helper with
        /// the same envelope shape rather than a new shared utility.</summary>
        static AudioClip MakeThump()
        {
            const int rate = 44100;
            const float freq = 110f;
            const float duration = 0.12f;
            int n = (int)(rate * duration);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = 1f - i / (float)n;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * i / rate) * env * env * 0.6f;
            }
            var clip = AudioClip.Create("fireThump", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnDestroy()
        {
            _motor.KillPlaneCrossed -= HandleKillPlane;
            if (_thumpClip != null) Destroy(_thumpClip);
        }

        void HandleKillPlane()
        {
            // A dead body falling into space must NOT reset the core: doing so
            // clears _core.IsDead and the respawn timer branch never fires
            // (permanent death). The motor has already parked the corpse at the
            // spawn point; let the timer finish.
            if (_core.IsDead) return;

            // Arcade rule: falling into space while alive = instant
            // self-respawn, -1 frag.
            _core.AddSuicide();
            _core.Respawn();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _weapons.Tick(dt);
            bool roundOver = AlienSkies.Run.RunManager.Instance != null && AlienSkies.Run.RunManager.Instance.InputsFrozen;
            _motor.InputsEnabled = !_core.IsDead && !roundOver;

            if (_core.IsDead && _core.TickRespawn(dt))
                Respawn();

            if (!IsLocal) return;
            if (RespawnRemaining > 0f) RespawnRemaining -= dt;
            if (_core.IsDead || roundOver || Cursor.lockState != CursorLockMode.Locked) return;

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || kb == null) return;

            int before = _weapons.CurrentIndex;
            if (kb.digit1Key.wasPressedThisFrame) _weapons.TrySwitch(0);
            if (kb.digit2Key.wasPressedThisFrame) _weapons.TrySwitch(1);
            if (kb.digit3Key.wasPressedThisFrame) _weapons.TrySwitch(2);
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0.01f) _weapons.SwitchNext(1);
            else if (scroll < -0.01f) _weapons.SwitchNext(-1);
            if (_weapons.CurrentIndex != before) OnWeaponChanged?.Invoke(_weapons.CurrentIndex);

            if (mouse.leftButton.isPressed && _weapons.TryFire())
                FireCurrent();
        }

        void FireCurrent()
        {
            int w = _weapons.CurrentIndex;
            Vector3 origin = _motor.CameraPivot.position;
            Vector3 dir = _motor.CameraPivot.forward;

            OnFired?.Invoke(w);
            PlayFireSound(w);

            if (S.Weapons[w].ProjectileSpeed > 0f)
            {
                // Same-frame muzzle flash (guardrail 1: rocket-wall-test).
                CombatFx.MuzzleFlash(origin + dir * 1.2f, dir);
                FireRocket(origin + dir * 1.2f, dir);
            }
            else
                FireHitscan(w, origin, dir);
        }

        void PlayFireSound(int w)
        {
            if (FireAudio == null) return;
            AudioClip clip = w == 0 ? RocketClip : w == 1 ? RifleClip : ShotgunClip;
            if (clip == null) return;
            FireAudio.pitch = w == 1 ? 1.35f : 1f;
            FireAudio.PlayOneShot(clip, 0.8f);
            // Layered fire sound (guardrail 1): the thump is the body, the
            // clip is the crack — rocket only, matching its outsized kick.
            if (w == 0 && _thumpClip != null) FireAudio.PlayOneShot(_thumpClip, 0.9f);
        }

        bool FireGate(int w, WeaponStats stats)
        {
            if (Time.time - _lastFire[w] < stats.FireInterval * 0.9f) return false;
            _lastFire[w] = Time.time;
            return true;
        }

        void FireHitscan(int weaponIndex, Vector3 origin, Vector3 dir)
        {
            if (_core.IsDead) return;
            if (weaponIndex < 0 || weaponIndex >= S.Weapons.Length) return;
            var stats = S.Weapons[weaponIndex];
            if (stats.ProjectileSpeed > 0f) return; // rockets use FireRocket
            if (!FireGate(weaponIndex, stats)) return;

            for (int p = 0; p < stats.Pellets; p++)
            {
                Vector3 pdir = ApplySpread(dir, stats.SpreadDegrees, p);
                Vector3 end;
                Vector3 surfaceNormal = Vector3.up;
                bool hitSomething = RaycastIgnoringSelf(origin, pdir, out var hit);
                if (hitSomething)
                {
                    // Overlapping-at-start spherecast hits report point (0,0,0).
                    end = hit.distance > 0f ? hit.point : origin;
                    surfaceNormal = hit.normal;
                    var victim = hit.collider.GetComponentInParent<IDamageable>();
                    if (victim != null && (object)victim != this)
                        victim.TakeDamage(stats.Damage, this);
                }
                else end = origin + pdir * 500f;
                CombatFx.Tracer(origin + pdir * 1.0f, end);
                if (hitSomething) CombatFx.Impact(end, surfaceNormal);
            }
        }

        // Spherecast past our own colliders: every shot is HitscanRadius thick,
        // fattening targets for bullets without touching anyone's movement capsule.
        bool RaycastIgnoringSelf(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = default;
            float bestDist = float.MaxValue;
            foreach (var h in Physics.SphereCastAll(origin, S.HitscanRadius, dir, 500f))
            {
                if (h.collider.GetComponentInParent<PlayerCombat>() == this) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; }
            }
            return bestDist < float.MaxValue;
        }

        static Vector3 ApplySpread(Vector3 dir, float degrees, int pellet)
        {
            if (pellet == 0 || degrees <= 0f) return dir; // first pellet flies true
            Vector3 axis = Vector3.Cross(dir, Random.onUnitSphere).normalized;
            return Quaternion.AngleAxis(Random.Range(0f, degrees), axis) * dir;
        }

        /// <summary>The one validated rocket-fire path — shared by the local
        /// player and AI drivers (BotDriver).</summary>
        public void FireRocket(Vector3 origin, Vector3 dir)
        {
            if (_core.IsDead || RocketPrefab == null) return;
            var stats = S.Weapons[0];
            if (!FireGate(0, stats)) return;

            var go = Instantiate(RocketPrefab, origin, Quaternion.LookRotation(dir));
            go.GetComponent<Rocket>().Init(this, dir * stats.ProjectileSpeed, stats);

            // The human hears their own fire via FireAudio (FireCurrent);
            // enemy/bot launches were silent — a recorded §9 gap. Spatial
            // one-shot at the muzzle so incoming rockets are audible.
            if (!IsLocal && RocketClip != null)
                AudioSource.PlayClipAtPoint(RocketClip, origin, 0.6f);
        }

        public void TakeDamage(float amount, PlayerCombat attacker)
        {
            if (_core.IsDead) return;
            // The Voice reads this at death to name the killer (guardrail 8:
            // unattributed deaths end sessions). Turret.cs is the only
            // null-attacker path (beam damage); self is your own splash.
            LastDamageSource = attacker == this ? DamageSourceKind.SelfSplash
                : attacker != null ? DamageSourceKind.Drone
                : DamageSourceKind.Turret;
            float prev = _core.Health;
            bool died = _core.ApplyDamage(amount);
            // Victim hurt feedback — was the health SyncVar's OnChange.
            if (_core.Health < prev)
                OnDamaged?.Invoke((prev - _core.Health) / S.MaxHealth);
            if (attacker != null && attacker != this)
                attacker.NotifyHit(amount, died);
            if (!died) return;

            RespawnRemaining = S.RespawnDelay; // was the dead SyncVar's OnChange
            if (attacker != null && attacker != this)
                attacker.AddFrag();
            else
                _core.AddSuicide(); // rocket-jumped yourself to death
            CombatFx.Explosion(transform.position + Vector3.up * 0.9f, 2.5f);
        }

        // Only the human gets attacker-side hit feedback — was the
        // Owner.IsValid gate ("AI players have no client"). Without it, bot
        // hit-confirms drive CombatFeedback into the bot's deactivated camera:
        // PlayOneShot warnings every hit, impact frames on every bot kill.
        public void NotifyHit(float damage, bool killed)
        {
            if (IsLocal) OnHitConfirmed?.Invoke(damage, killed);
        }

        public void AddFrag() => _core.AddFrag();

        /// <summary>Full combat reset: fresh CombatCore (zeroes frags/deaths/
        /// health), revive. Position is the caller's business (RunManager
        /// owns the start line; bot respawns keep using Respawn()).</summary>
        public void ServerResetForNewRound() => _core = new CombatCore(S);

        void Respawn()
        {
            _core.Respawn();
            _motor.RespawnAt(SpawnPointRegistry.Farthest(transform.position));
        }
    }
}
