using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Per-weapon tuning. Damage is per hit (per pellet for the shotgun);
    /// ProjectileSpeed 0 means hitscan.</summary>
    [System.Serializable]
    public class WeaponStats
    {
        public string Name = "Weapon";
        public float Damage = 20f;
        public float FireInterval = 0.35f;
        public int Pellets = 1;
        public float SpreadDegrees = 0f;
        public float ProjectileSpeed = 0f;
        public float SplashRadius = 0f;
        public float Knockback = 0f;        // impulse m/s at splash epicenter
        public float SelfDamageScale = 0.5f;
    }

    /// <summary>All combat tuning numbers. Plain class so tests construct it
    /// directly; CombatConfig (ScriptableObject) wraps it for the editor.</summary>
    [System.Serializable]
    public class CombatSettings
    {
        public float MaxHealth = 100f;
        public float RespawnDelay = 3f;
        public float SwitchDelay = 0.25f;
        // Hitscan shots are spherecasts this thick — aim forgiveness that
        // fattens targets for bullets only, leaving movement capsules alone.
        public float HitscanRadius = 0.35f;
        public int FragTarget = 10;        // first to this many frags wins the round
        public float RoundResetDelay = 6f; // victory-screen seconds before the next round

        public WeaponStats[] Weapons = new[]
        {
            // 200/26 = the playtest-tuned cannonball rocket (2026-08-29):
            // default, CombatConfig.asset, and the spec test agree (snapshot
            // rule). The ENEMY config is authored back to 150/22 by the
            // builder — dodgeable rockets, gentler shove.
            new WeaponStats { Name = "Rocket",  Damage = 80f, FireInterval = 0.8f,  Pellets = 1,  SpreadDegrees = 0f, ProjectileSpeed = 200f, SplashRadius = 6f, Knockback = 26f, SelfDamageScale = 0.5f },
            new WeaponStats { Name = "Rifle",   Damage = 20f, FireInterval = 0.23f, Pellets = 1,  SpreadDegrees = 0f },
            new WeaponStats { Name = "Shotgun", Damage = 6f,  FireInterval = 0.9f,  Pellets = 20, SpreadDegrees = 7f },
        };
    }
}
