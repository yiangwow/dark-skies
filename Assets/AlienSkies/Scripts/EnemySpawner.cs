using UnityEngine;
using System.Collections.Generic;
using AlienSkies.Combat;
using AlienSkies.Movement;
using AlienSkies.Bots;

namespace AlienSkies.Run
{
    /// <summary>Populates the run with enemy drones: the practice-bot pattern
    /// (Player prefab + BotDriver) at authored posts on real geometry, low HP
    /// via config swap, magenta threat-rim body. Most posts are stationary;
    /// each level also has one orbiter that strafes a player-facing ring
    /// (BotBrain edge-sensing keeps it off the ledge). Removal paths: death
    /// (short linger so the explosion reads) and
    /// kill-plane (immediate — PlayerCombat's handler would otherwise
    /// full-heal the drone and the motor would pile it at arena center).
    /// Both paths sweep the drone's in-flight rockets so a destroyed shooter
    /// can't charge the player a false suicide. The full set repopulates
    /// when a run starts or restarts — pre-clock kills buy nothing.</summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        public GameObject PlayerPrefab;
        public CombatConfig EnemyConfig;      // low MaxHealth variant
        public Material EnemyBodyMaterial;    // magenta NeonRim — threat color
        public Vector3[] Posts = new Vector3[0];
        public Vector3[] Orbiters = new Vector3[0];
        public float OrbiterRadius = 7f;
        /// <summary>Lead-math-only knob (BotBrain.ProjectileSpeed), mirrored
        /// from LevelDef.EnemyProjectileSpeed; NOT the rocket's real speed
        /// (that stays WeaponStats.ProjectileSpeed). No-levels fallback.</summary>
        public float EnemyProjectileSpeed = 150f;
        public Vector3[] Turrets = new Vector3[0];
        public AudioClip TurretFireClip; // builder wires the CC0 rifle clip (Task 3)

        const float CorpseLinger = 0.6f; // let the death explosion read

        sealed class Drone
        {
            public GameObject Go;
            public PlayerCombat Pc;
            public PlayerMotor Motor;
            public System.Action OnFell;
            public float DeadAt = -1f;
            public bool Fell;
        }

        // Turrets are motor-less and static: reusing Drone with a null Motor
        // NREs every frame in Update's d.Pc dereference and corrupts the
        // rocket-sweep semantics — a separate, minimal record instead. Their
        // only removal paths are death (the turret's own Destroy) and
        // Repopulate; kill-plane falls don't apply.
        sealed class TurretRecord
        {
            public GameObject Go;
        }

        readonly List<Drone> _live = new List<Drone>();
        readonly List<TurretRecord> _liveTurrets = new List<TurretRecord>();

        void Start()
        {
            if (RunManager.Instance != null)
            {
                RunManager.Instance.RunStarted += Repopulate;
                RunManager.Instance.RunReset += Repopulate;
                RunManager.Instance.LevelChanged += HandleLevelChanged;
                // Start order between RunManager and EnemySpawner is
                // undefined — if RunManager.Start's SelectLevel(0) already
                // fired LevelChanged before this subscription existed, this
                // pull-once makes it order-proof (review).
                HandleLevelChanged();
            }
            Repopulate();
        }

        void OnDestroy()
        {
            if (RunManager.Instance != null)
            {
                RunManager.Instance.RunStarted -= Repopulate;
                RunManager.Instance.RunReset -= Repopulate;
                RunManager.Instance.LevelChanged -= HandleLevelChanged;
            }
        }

        void HandleLevelChanged()
        {
            var lvl = RunManager.Instance != null ? RunManager.Instance.CurrentLevelDef : null;
            if (lvl != null) SetLoadout(lvl.Posts, lvl.Orbiters, lvl.OrbiterRadius, lvl.EnemyProjectileSpeed, lvl.Turrets);
        }

        /// <summary>Levels hand their loadout in here; the serialized
        /// Posts/Orbiters/OrbiterRadius/EnemyProjectileSpeed/Turrets fields
        /// remain the no-levels fallback.</summary>
        public void SetLoadout(Vector3[] posts, Vector3[] orbiters, float orbiterRadius, float projectileSpeed, Vector3[] turrets)
        {
            Posts = posts ?? new Vector3[0];
            Orbiters = orbiters ?? new Vector3[0];
            OrbiterRadius = orbiterRadius;
            EnemyProjectileSpeed = projectileSpeed;
            Turrets = turrets ?? new Vector3[0];
            Repopulate();
        }

        void Update()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var d = _live[i];
                if (d.Go == null) { _live.RemoveAt(i); continue; }
                if (d.Fell)
                {
                    Remove(d); // fell into the void: no linger, no heal, gone
                    _live.RemoveAt(i);
                    continue;
                }
                if (d.Pc.IsDead && d.DeadAt < 0f)
                    d.DeadAt = Time.time;
                else if (d.DeadAt >= 0f && Time.time - d.DeadAt > CorpseLinger)
                {
                    Remove(d);
                    _live.RemoveAt(i);
                }
            }

            // Turrets have no fall/linger path — the turret destroys itself
            // (with its own 0.4s delay) on death; just sweep the stale entry
            // once that Destroy has actually run.
            for (int i = _liveTurrets.Count - 1; i >= 0; i--)
                if (_liveTurrets[i].Go == null) _liveTurrets.RemoveAt(i);
        }

        void Repopulate()
        {
            BotDriver.ResetSeedCounter(); // per-run-deterministic bot RNG (guardrail 5)
            foreach (var d in _live)
                if (d.Go != null) Remove(d);
            _live.Clear();
            foreach (var t in _liveTurrets)
                if (t.Go != null) Destroy(t.Go);
            _liveTurrets.Clear();
            foreach (var p in Posts) Spawn(p, stationary: true);
            foreach (var p in Orbiters) Spawn(p, stationary: false);
            foreach (var p in Turrets) SpawnTurret(p);
        }

        void SpawnTurret(Vector3 at)
        {
            var go = new GameObject("Enemy_Turret");
            go.transform.position = at;
            var turret = go.AddComponent<Turret>();
            turret.FireClip = TurretFireClip;
            _liveTurrets.Add(new TurretRecord { Go = go });
        }

        void Spawn(Vector3 at, bool stationary)
        {
            if (PlayerPrefab == null) return;
            var go = Instantiate(PlayerPrefab, at, Quaternion.identity);
            go.name = stationary ? "Enemy_Post" : "Enemy_Orbiter";

            var driver = go.AddComponent<BotDriver>();
            driver.SetStationary(stationary);
            if (!stationary) driver.SetOrbitRadius(OrbiterRadius);
            driver.SetProjectileSpeed(EnemyProjectileSpeed); // both posts and orbiters fire

            var pc = go.GetComponent<PlayerCombat>();
            if (EnemyConfig != null)
            {
                // Awake already built CombatCore from the player config;
                // swapping Config then resetting rebuilds it with enemy HP.
                pc.Config = EnemyConfig;
                pc.ServerResetForNewRound();
            }

            var motor = go.GetComponent<PlayerMotor>();
            if (EnemyBodyMaterial != null && motor.BodyRenderer != null)
                motor.BodyRenderer.sharedMaterial = EnemyBodyMaterial;

            var drone = new Drone { Go = go, Pc = pc, Motor = motor };
            drone.OnFell = () => drone.Fell = true; // handled next Update
            motor.KillPlaneCrossed += drone.OnFell;
            _live.Add(drone);
        }

        void Remove(Drone d)
        {
            if (d.Motor != null && d.OnFell != null)
                d.Motor.KillPlaneCrossed -= d.OnFell;
            // Sweep this drone's in-flight rockets: a detonation from a
            // destroyed shooter reads as attacker==null and charges the
            // victim a suicide (review-caught misattribution).
            foreach (var r in FindObjectsByType<Rocket>(FindObjectsSortMode.None))
                if (r.Shooter == d.Pc) Destroy(r.gameObject);
            Destroy(d.Go);
        }
    }
}
