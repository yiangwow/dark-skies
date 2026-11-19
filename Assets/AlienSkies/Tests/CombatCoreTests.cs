using NUnit.Framework;
using AlienSkies.Combat;

namespace AlienSkies.Tests
{
    public class CombatCoreTests
    {
        static CombatSettings S() => new CombatSettings();

        [Test]
        public void ApplyDamage_ReducesHealth()
        {
            var c = new CombatCore(S());
            c.ApplyDamage(30f);
            Assert.AreEqual(70f, c.Health, 1e-4f);
            Assert.IsFalse(c.IsDead);
        }

        [Test]
        public void ApplyDamage_KillingHit_ReturnsTrueOnceAndClampsToZero()
        {
            var c = new CombatCore(S());
            Assert.IsFalse(c.ApplyDamage(99f));
            Assert.IsTrue(c.ApplyDamage(50f), "the killing hit must report the kill");
            Assert.AreEqual(0f, c.Health, 1e-4f);
            Assert.IsTrue(c.IsDead);
            Assert.AreEqual(1, c.Deaths);
            Assert.IsFalse(c.ApplyDamage(10f), "corpses take no damage and report no kills");
            Assert.AreEqual(1, c.Deaths, "death must count once");
        }

        [Test]
        public void TickRespawn_CountsDownOnlyWhileDead()
        {
            var s = S();
            var c = new CombatCore(s);
            Assert.IsFalse(c.TickRespawn(10f), "alive players never signal respawn");
            c.ApplyDamage(999f);
            Assert.IsFalse(c.TickRespawn(s.RespawnDelay * 0.5f));
            Assert.IsTrue(c.TickRespawn(s.RespawnDelay * 0.6f), "timer elapsed -> respawn due");
        }

        [Test]
        public void Respawn_RestoresFullHealthAndLife()
        {
            var s = S();
            var c = new CombatCore(s);
            c.ApplyDamage(999f);
            c.Respawn();
            Assert.AreEqual(s.MaxHealth, c.Health, 1e-4f);
            Assert.IsFalse(c.IsDead);
        }

        [Test]
        public void FragAndSuicideCounters()
        {
            var c = new CombatCore(S());
            c.AddFrag();
            c.AddFrag();
            c.AddSuicide();
            Assert.AreEqual(1, c.Frags, "suicide costs a frag");
        }

        [Test]
        public void DefaultWeapons_MatchSpecOrderAndValues()
        {
            var s = S();
            Assert.AreEqual(3, s.Weapons.Length);
            Assert.AreEqual(200f, s.Weapons[0].ProjectileSpeed, "index 0 is the rocket (cannonball tune 2026-08-29)");
            Assert.AreEqual(26f, s.Weapons[0].Knockback, "rocket-jump kick shares the knockback stat");
            Assert.AreEqual(0f, s.Weapons[1].ProjectileSpeed, "index 1 is hitscan rifle");
            Assert.AreEqual(20, s.Weapons[2].Pellets, "index 2 is the shotgun");
        }
    }
}
