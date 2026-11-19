using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Stationary beam turret: self-builds its own body (pedestal +
    /// head, both shootable) in Awake, wraps a TurretCore for the aim/fire
    /// state machine, and renders the tracking laser on a child LineRenderer
    /// (root has no free Renderer slot to spare — same reason BotDriver's
    /// telegraph line lives on a child). IDamageable so player hitscan/splash
    /// treats it exactly like a player capsule.</summary>
    public sealed class Turret : MonoBehaviour, IDamageable
    {
        public float MaxHealth = 60f;
        public AudioClip FireClip; // wired by the builder (Task 3) to the CC0 rifle clip

        const float BeamDamage = 25f;

        public TurretCore Core { get; private set; }
        public bool IsDead => _health <= 0f;

        float _health;
        Transform _head;
        LineRenderer _laserLine;
        Material _laserMaterial;

        PlayerCombat _player;
        AlienSkies.Movement.PlayerMotor _playerMotor;
        float _rebindTimer;

        // Shared across every turret — the body look never varies per
        // instance, so (unlike the laser) there is nothing to leak: one
        // material for the game's lifetime, same pattern as CombatFx.ShellMaterial.
        static Material _bodyMaterial;

        void Awake()
        {
            _health = MaxHealth;
            Core = new TurretCore();
            BuildVisuals();
        }

        void OnDestroy()
        {
            // Per-instance (color/width lerp with TrackProgress), unlike the
            // shared body material above — mirrors BotDriver's
            // _telegraphMaterial cleanup.
            if (_laserMaterial != null) Destroy(_laserMaterial);
        }

        void BuildVisuals()
        {
            if (_bodyMaterial == null)
            {
                _bodyMaterial = new Material(Shader.Find("AlienSkies/NeonRim"));
                _bodyMaterial.SetColor("_BaseColor", AlienSkies.Palette.Ink);
                _bodyMaterial.SetColor("_RimColor", AlienSkies.Palette.Magenta);
                _bodyMaterial.SetFloat("_RimIntensity", 3f);
            }

            // Pedestal: h1.2, base at the turret's own origin (its footing).
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Pedestal";
            pedestal.transform.SetParent(transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            pedestal.transform.localScale = new Vector3(0.8f, 0.6f, 0.8f); // default h2 * 0.6 = 1.2
            pedestal.GetComponent<Renderer>().sharedMaterial = _bodyMaterial;
            // Collider KEPT (default CapsuleCollider) — the turret must be shootable.

            // Head: d1.2, resting on top of the pedestal.
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(transform, false);
            head.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            head.transform.localScale = Vector3.one * 1.2f;
            head.GetComponent<Renderer>().sharedMaterial = _bodyMaterial;
            // Collider KEPT (default SphereCollider) — shootable, and this is
            // the turret's eye/muzzle point below.
            _head = head.transform;

            var laserGO = new GameObject("LaserLine");
            laserGO.transform.SetParent(transform, false);
            _laserLine = laserGO.AddComponent<LineRenderer>();
            _laserLine.positionCount = 2;
            _laserMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _laserLine.material = _laserMaterial;
            _laserLine.enabled = false;
        }

        void Update()
        {
            BindPlayer();
            if (_player == null) { _laserLine.enabled = false; return; }

            Vector3 headPos = _head.position;
            Vector3 playerEye = _playerMotor.CameraPivot.position;

            // Eye-to-eye linecast (BotDriver's own visibility clause):
            // starting the ray inside the player's capsule means a naive hit
            // is the player's own collider, not an obstruction. Also excuse a
            // hit on the turret's own body, in case a down-angled head ever
            // clips its own pedestal collider at the ray's start.
            bool targetVisible =
                !Physics.Linecast(headPos, playerEye, out var los)
                || los.collider.GetComponentInParent<PlayerCombat>() == _player
                || los.collider.GetComponentInParent<Turret>() == this;

            Core.Tick(headPos, playerEye, targetVisible, Time.deltaTime);

            bool tracking = Core.State == TurretState.Tracking;
            _laserLine.enabled = tracking;
            if (tracking)
            {
                _laserLine.SetPosition(0, headPos);
                _laserLine.SetPosition(1, Core.AimPoint);
                float w = Mathf.Lerp(0.03f, 0.10f, Core.TrackProgress);
                _laserLine.startWidth = w;
                _laserLine.endWidth = w;
                Color c = Color.Lerp(AlienSkies.Palette.Cyan, AlienSkies.Palette.Magenta, Core.TrackProgress);
                _laserMaterial.SetColor("_BaseColor", c * 3f);
            }

            if (Core.FiredThisTick) FireBeam(headPos);
        }

        void FireBeam(Vector3 headPos)
        {
            Vector3 toAim = Core.AimPoint - headPos;
            float dist = toAim.magnitude;
            Vector3 dir = dist > 0.0001f ? toAim / dist : transform.forward;

            RaycastHit best = default;
            float bestDist = float.MaxValue;
            foreach (var h in Physics.SphereCastAll(headPos, 0.35f, dir, dist))
            {
                // Self-filter: down-angled shots would otherwise stop on the
                // turret's own pedestal.
                if (h.collider.GetComponentInParent<Turret>() == this) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; }
            }
            bool hitSomething = bestDist < float.MaxValue;
            Vector3 beamEnd = hitSomething ? best.point : headPos + dir * dist;

            if (hitSomething)
            {
                var victim = best.collider.GetComponentInParent<IDamageable>();
                if (victim != null) victim.TakeDamage(BeamDamage, null);
            }

            // Bright flash: two tracers stacked for thickness — kept simple
            // and per-instance rather than adding a wide-beam variant to the
            // shared CombatFx.
            CombatFx.Tracer(headPos, beamEnd);
            CombatFx.Tracer(headPos, beamEnd);
            if (FireClip != null) AudioSource.PlayClipAtPoint(FireClip, headPos, 0.9f);
        }

        public void TakeDamage(float amount, PlayerCombat attacker)
        {
            if (IsDead) return;
            _health -= amount;
            bool died = _health <= 0f;
            attacker?.NotifyHit(amount, died);
            if (!died) return;

            // Player-death attribution note (accepted): a killing hit from
            // this turret runs PlayerCombat.TakeDamage(dmg, null) — the
            // legacy attacker-null branch (AddSuicide). Harmless bookkeeping;
            // player Frags feed nothing since RunCore took over, and
            // RunManager's restart flow is attacker-agnostic.
            CombatFx.Explosion(transform.position + Vector3.up * 0.9f, Vector3.up, 3f);
            _laserLine.enabled = false;
            enabled = false; // Update must stop — a corpse at TrackProgress
                              // ~1.0 would otherwise beam the player during
                              // the destroy window.
            Destroy(gameObject, 0.4f);
        }

        void BindPlayer()
        {
            if (_player != null) return;
            _rebindTimer -= Time.deltaTime;
            if (_rebindTimer > 0f) return;
            _rebindTimer = 1f;
            foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            {
                if (!pc.IsLocal) continue;
                _player = pc;
                _playerMotor = pc.GetComponent<AlienSkies.Movement.PlayerMotor>();
                break;
            }
        }
    }
}
