using NUnit.Framework;
using UnityEngine;
using AlienSkies.Combat;

namespace AlienSkies.Tests
{
    public class TurretCoreTests
    {
        static Vector3 OnCircle(Vector3 center, float radius, float angleRad) =>
            center + new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad)) * radius;

        [Test]
        public void IdleUntilVisible()
        {
            var t = new TurretCore();
            var self = Vector3.zero;
            var inRangeTarget = new Vector3(10f, 0f, 0f);

            // Invisible, in range: never engages.
            for (int i = 0; i < 20; i++)
            {
                t.Tick(self, inRangeTarget, targetVisible: false, dt: 0.1f);
                Assert.AreEqual(TurretState.Idle, t.State, $"tick {i}: invisible target must not engage");
                Assert.IsFalse(t.FiredThisTick);
            }

            // Visible, but beyond Range: also never engages.
            var farTarget = new Vector3(TurretCore.Range + 10f, 0f, 0f);
            for (int i = 0; i < 20; i++)
            {
                t.Tick(self, farTarget, targetVisible: true, dt: 0.1f);
                Assert.AreEqual(TurretState.Idle, t.State, $"tick {i}: out-of-range target must not engage");
                Assert.IsFalse(t.FiredThisTick);
            }
        }

        [Test]
        public void AcquireSnapsAimOnFirstSight()
        {
            var t = new TurretCore();
            var self = Vector3.zero;
            var target = new Vector3(10f, 0f, 5f);

            t.Tick(self, target, targetVisible: true, dt: 0.1f);

            Assert.AreEqual(TurretState.Tracking, t.State);
            Assert.AreEqual(target, t.AimPoint, "first sight must snap the aim, not ease toward it");
        }

        [Test]
        public void FiresAfterTrackTime()
        {
            var t = new TurretCore();
            var self = Vector3.zero;
            var target = new Vector3(10f, 0f, 0f); // stationary, in range, visible
            float dt = 0.1f;

            t.Tick(self, target, true, dt); // acquire
            Assert.AreEqual(TurretState.Tracking, t.State);

            bool fired = false;
            for (int i = 0; i < 30 && !fired; i++)
            {
                t.Tick(self, target, true, dt);
                fired = t.FiredThisTick;
                if (!fired) Assert.AreEqual(TurretState.Tracking, t.State, $"tick {i}: still ramping");
            }

            Assert.IsTrue(fired, "turret must fire once TrackTime (0.9s) elapses");
            Assert.AreEqual(TurretState.Cooldown, t.State, "firing drops straight into cooldown");
        }

        [Test]
        public void FastTargetOutrunsTheLock()
        {
            var t = new TurretCore();
            var self = Vector3.zero;
            const float radius = 20f;
            const float speed = 60f; // tangential m/s
            float angularSpeedRad = speed / radius;
            float angularSpeedDeg = angularSpeedRad * Mathf.Rad2Deg;
            Assert.Greater(angularSpeedDeg, TurretCore.TrackDegPerSec,
                "test setup must exceed the track cap for this to be a valid 'outruns the lock' case");

            float dt = 0.05f;
            float angle = 0f;

            t.Tick(self, OnCircle(self, radius, angle), true, dt); // acquire: honest snap, no lag yet
            Assert.AreEqual(TurretState.Tracking, t.State);

            // Track for just under TrackTime so we sample the lag before it fires.
            int trackTicks = Mathf.FloorToInt(TurretCore.TrackTime / dt) - 1;
            for (int i = 0; i < trackTicks; i++)
            {
                angle += angularSpeedRad * dt;
                t.Tick(self, OnCircle(self, radius, angle), true, dt);
                Assert.IsFalse(t.FiredThisTick, $"tick {i}: must not fire before TrackTime elapses");
            }

            var targetPos = OnCircle(self, radius, angle);
            float lag = Vector3.Angle(t.AimPoint - self, targetPos - self);
            Assert.Greater(lag, 3f, "a target outrunning the angular-speed cap should leave the lock lagging");
        }

        [Test]
        public void SlowTargetIsTracked()
        {
            var t = new TurretCore();
            var self = Vector3.zero;
            const float radius = 20f;
            const float speed = 5f; // well under the cap: 5/20 rad = 14.3 deg/s
            float angularSpeedRad = speed / radius;
            float dt = 0.05f;
            float angle = 0f;

            t.Tick(self, OnCircle(self, radius, angle), true, dt); // acquire
            Assert.AreEqual(TurretState.Tracking, t.State);

            bool fired = false;
            Vector3 targetPos = OnCircle(self, radius, angle);
            for (int i = 0; i < 200 && !fired; i++)
            {
                angle += angularSpeedRad * dt;
                targetPos = OnCircle(self, radius, angle);
                t.Tick(self, targetPos, true, dt);
                fired = t.FiredThisTick;
            }

            Assert.IsTrue(fired, "setup: the turret should reach fire under a sustained slow-target track");
            float lag = Vector3.Angle(t.AimPoint - self, targetPos - self);
            Assert.Less(lag, 1f, "a target well under the cap should be tracked almost perfectly at fire time");
        }

        [Test]
        public void LosBreakResetsToIdle()
        {
            var self = Vector3.zero;
            var target = new Vector3(10f, 0f, 0f);
            float dt = 0.1f;

            // Reference: how many ticks a fresh, uninterrupted acquisition takes to fire.
            var fresh = new TurretCore();
            fresh.Tick(self, target, true, dt); // acquire
            int freshTicksToFire = 0;
            while (!fresh.FiredThisTick)
            {
                Assert.Less(freshTicksToFire, 1000, "runaway: fresh turret never fired");
                fresh.Tick(self, target, true, dt);
                freshTicksToFire++;
            }

            // Subject: track partway, break LOS mid-ramp, confirm the reset, then re-acquire.
            var t = new TurretCore();
            t.Tick(self, target, true, dt); // acquire
            t.Tick(self, target, true, dt);
            t.Tick(self, target, true, dt);
            Assert.AreEqual(TurretState.Tracking, t.State);
            Assert.Greater(t.TrackProgress, 0f, "should have made some ramp progress before the LOS break");

            t.Tick(self, target, false, dt); // visibility lost mid-track
            Assert.AreEqual(TurretState.Idle, t.State);
            Assert.AreEqual(0f, t.TrackProgress);
            Assert.IsFalse(t.FiredThisTick);

            t.Tick(self, target, true, dt); // re-engage
            Assert.AreEqual(TurretState.Tracking, t.State);
            Assert.AreEqual(0f, t.TrackProgress, "re-acquire restarts the ramp from zero, not a shortened remainder");

            int reacquireTicksToFire = 0;
            while (!t.FiredThisTick)
            {
                Assert.Less(reacquireTicksToFire, 1000, "runaway: re-acquired turret never fired");
                t.Tick(self, target, true, dt);
                reacquireTicksToFire++;
            }

            Assert.AreEqual(freshTicksToFire, reacquireTicksToFire,
                "re-acquisition after an LOS break must take exactly as long as a fresh acquisition (the full ramp)");
        }

        [Test]
        public void CooldownGatesReacquisition()
        {
            var t = new TurretCore();
            var self = Vector3.zero;
            var target = new Vector3(10f, 0f, 0f);
            // dt = 0.25 is exactly representable in float: 9 reps sum to exactly
            // 2.25 and 10 reps to exactly 2.5, with no accumulation drift near
            // the boundary (unlike dt=0.1, where 25 reps land at 2.4999998 and
            // miss it).
            float dt = 0.25f;

            t.Tick(self, target, true, dt); // acquire
            bool fired = false;
            for (int i = 0; i < 20 && !fired; i++)
            {
                t.Tick(self, target, true, dt);
                fired = t.FiredThisTick;
            }
            Assert.IsTrue(fired, "setup: turret must fire before the cooldown gate can be tested");
            Assert.AreEqual(TurretState.Cooldown, t.State);

            // 9 engaged ticks (2.25s) must stay in Cooldown.
            for (int i = 0; i < 9; i++)
            {
                t.Tick(self, target, true, dt);
                Assert.AreEqual(TurretState.Cooldown, t.State, $"cooldown tick {i}: 2.5s has not elapsed yet");
            }

            // The 10th (2.5s) exits Cooldown.
            t.Tick(self, target, true, dt);
            Assert.AreEqual(TurretState.Idle, t.State, "cooldown should exit exactly at CooldownTime");

            // The very next engaged tick re-acquires.
            t.Tick(self, target, true, dt);
            Assert.AreEqual(TurretState.Tracking, t.State);
        }
    }
}
