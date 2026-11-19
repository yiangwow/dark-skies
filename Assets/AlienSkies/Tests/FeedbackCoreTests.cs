using NUnit.Framework;
using AlienSkies.Combat;

namespace AlienSkies.Tests
{
    public class FeedbackCoreTests
    {
        [Test]
        public void AddTrauma_ClampsToOne()
        {
            var f = new FeedbackCore();
            f.AddTrauma(0.7f);
            f.AddTrauma(0.7f);
            Assert.AreEqual(1f, f.Trauma, 1e-4f);
        }

        [Test]
        public void Tick_DecaysTraumaAndFloorsAtZero()
        {
            var f = new FeedbackCore();
            f.AddTrauma(0.5f);
            f.Tick(0.1f);
            Assert.AreEqual(0.5f - FeedbackCore.TraumaDecay * 0.1f, f.Trauma, 1e-4f);
            f.Tick(10f);
            Assert.AreEqual(0f, f.Trauma, 1e-4f);
        }

        [Test]
        public void ShakeMagnitude_IsTraumaSquared()
        {
            var f = new FeedbackCore();
            f.AddTrauma(0.5f);
            Assert.AreEqual(0.25f, f.ShakeMagnitude, 1e-4f);
        }

        [Test]
        public void RegisterHit_SetsHitmarker_OnlyKillSetsKillFlash()
        {
            var f = new FeedbackCore();
            f.RegisterHit(false);
            Assert.AreEqual(FeedbackCore.HitmarkerTime, f.HitmarkerRemaining, 1e-4f);
            Assert.AreEqual(0f, f.KillFlashRemaining, 1e-4f);
            f.RegisterHit(true);
            Assert.AreEqual(FeedbackCore.KillFlashTime, f.KillFlashRemaining, 1e-4f);
        }

        [Test]
        public void RegisterDamageTaken_RaisesFlashAndTrauma()
        {
            var f = new FeedbackCore();
            f.RegisterDamageTaken(0.3f);
            Assert.Greater(f.DamageFlash, 0f);
            Assert.Greater(f.Trauma, 0f);
            f.RegisterDamageTaken(5f);
            Assert.AreEqual(1f, f.DamageFlash, 1e-4f, "flash clamps to 1");
        }

        [Test]
        public void Tick_CountsDownTimers()
        {
            var f = new FeedbackCore();
            f.RegisterHit(true);
            f.RegisterDamageTaken(0.5f);
            f.Tick(FeedbackCore.HitmarkerTime + 0.01f);
            Assert.AreEqual(0f, f.HitmarkerRemaining, 1e-4f);
            Assert.Greater(f.KillFlashRemaining, 0f, "kill flash outlives hitmarker");
            f.Tick(10f);
            Assert.AreEqual(0f, f.KillFlashRemaining, 1e-4f);
            Assert.AreEqual(0f, f.DamageFlash, 1e-4f);
        }

        [Test]
        public void AddKick_ClampsAtFour()
        {
            var f = new FeedbackCore();
            f.AddKick(3f);
            f.AddKick(3f);
            Assert.AreEqual(4f, f.KickPitch, 1e-4f);
        }

        [Test]
        public void Tick_DecaysKickToZeroAndNeverNegative()
        {
            var f = new FeedbackCore();
            // KickDecay is 14 deg/s: the kick must survive the intermediate
            // tick, so seed 3 and tick 0.1 (removes 1.4) — a 1-degree kick
            // would already floor at zero here and assert nothing real.
            f.AddKick(3f);
            f.Tick(0.1f);
            Assert.AreEqual(3f - FeedbackCore.KickDecay * 0.1f, f.KickPitch, 1e-4f);
            f.Tick(10f);
            Assert.AreEqual(0f, f.KickPitch, 1e-4f);
        }
    }
}
