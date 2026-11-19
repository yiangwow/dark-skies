using NUnit.Framework;
using AlienSkies.Combat;

namespace AlienSkies.Tests
{
    public class WeaponCoreTests
    {
        static CombatSettings S() => new CombatSettings();

        [Test]
        public void StartsOnRocket_AndFireSetsCooldown()
        {
            var w = new WeaponCore(S());
            Assert.AreEqual(0, w.CurrentIndex);
            Assert.IsTrue(w.TryFire());
            Assert.IsFalse(w.TryFire(), "cooldown must block immediate refire");
        }

        [Test]
        public void Tick_ExpiresCooldown()
        {
            var s = S();
            var w = new WeaponCore(s);
            w.TryFire();
            w.Tick(s.Weapons[0].FireInterval + 0.01f);
            Assert.IsTrue(w.TryFire(), "cooldown elapsed -> can fire");
        }

        [Test]
        public void TrySwitch_Bounds_AndSameIndexRejected()
        {
            var w = new WeaponCore(S());
            Assert.IsFalse(w.TrySwitch(0), "already held");
            Assert.IsFalse(w.TrySwitch(-1));
            Assert.IsFalse(w.TrySwitch(3));
            Assert.IsTrue(w.TrySwitch(2));
            Assert.AreEqual(2, w.CurrentIndex);
        }

        [Test]
        public void Switch_AppliesSwapDelay()
        {
            var s = S();
            var w = new WeaponCore(s);
            w.TrySwitch(1);
            Assert.IsFalse(w.TryFire(), "swap delay must block instant fire");
            w.Tick(s.SwitchDelay + 0.01f);
            Assert.IsTrue(w.TryFire());
        }

        [Test]
        public void Switch_NeverShortensAnActiveCooldown()
        {
            var s = S();
            var w = new WeaponCore(s);
            w.TryFire(); // rocket: 0.8s cooldown
            w.TrySwitch(1);
            w.Tick(s.SwitchDelay + 0.01f);
            Assert.IsFalse(w.TryFire(), "switching must not bypass the rocket's cooldown");
        }

        [Test]
        public void SwitchNext_WrapsBothDirections()
        {
            var w = new WeaponCore(S());
            w.SwitchNext(-1);
            Assert.AreEqual(2, w.CurrentIndex);
            w.SwitchNext(1);
            Assert.AreEqual(0, w.CurrentIndex);
        }
    }
}
