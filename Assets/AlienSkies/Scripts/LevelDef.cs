using UnityEngine;

namespace AlienSkies.Run
{
    /// <summary>One sphere: pure authored data, built by PlaygroundBuilder
    /// into RunManager.Levels. The Gate object (ring + beacon + trigger) is a
    /// scene child the RunManager toggles per selection.</summary>
    [System.Serializable]
    public class LevelDef
    {
        public string Name = "SPHERE 1";
        public Vector3 Start;
        public float ParTime = 90f;
        public int STargetScore = 2000;
        public Vector3[] Posts = new Vector3[0];
        public Vector3[] Orbiters = new Vector3[0];
        public float OrbiterRadius = 7f; // one orbiter per level; platform-scaled or it lives at the rim
        public Vector3[] Turrets = new Vector3[0];
        /// <summary>Feeds BotBrain.ProjectileSpeed for time-of-flight leading
        /// ONLY — the rocket's real speed stays WeaponStats.ProjectileSpeed
        /// (150) regardless of this value. Course 1 = 150 (honest, matches the
        /// real rocket). Integration intends 170/190 for courses 2/3 once a
        /// per-level CombatConfig makes the real speed match this knob; until
        /// then every course MUST keep this at 150 too, or the brain leads a
        /// shot that flies slower than it assumes (systematic under-lead).</summary>
        public float EnemyProjectileSpeed = 150f;
        public GameObject Gate;
        public string BestKeyPrefix = "sphere1"; // sphere1 keeps its old bests
    }
}
