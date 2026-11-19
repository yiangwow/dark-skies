using NUnit.Framework;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Tests
{
    public class SoftCapTests
    {
        const float Dt = 0.016f;
        static MoveInput NoInput => new MoveInput { WishDir = Vector3.zero, JumpPressed = false };

        [Test]
        public void SpeedAboveCap_DecaysButIsNotHardClamped()
        {
            var s = new MovementSettings();
            var core = new MovementCore(s);
            float startSpeed = s.SoftCapSpeed * 2f;
            core.SetVelocity(new Vector3(startSpeed, 0f, 0f));
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.Less(core.Velocity.x, startSpeed, "drag must engage above the cap");
            Assert.Greater(core.Velocity.x, s.SoftCapSpeed, "one tick must not hard-clamp to the cap");
        }

        [Test]
        public void SpeedAtCap_IsUntouched()
        {
            var s = new MovementSettings();
            var core = new MovementCore(s);
            core.SetVelocity(new Vector3(s.SoftCapSpeed, 0f, 0f));
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(s.SoftCapSpeed, core.Velocity.x, 1e-4f);
        }

        [Test]
        public void DragGrowsWithExcessSpeed()
        {
            var s = new MovementSettings();
            var slightlyOver = new MovementCore(s);
            slightlyOver.SetVelocity(new Vector3(s.SoftCapSpeed * 1.1f, 0f, 0f));
            slightlyOver.Tick(NoInput, Vector3.zero, false, Dt);
            float lossSlight = s.SoftCapSpeed * 1.1f - slightlyOver.Velocity.x;

            var wayOver = new MovementCore(s);
            wayOver.SetVelocity(new Vector3(s.SoftCapSpeed * 3f, 0f, 0f));
            wayOver.Tick(NoInput, Vector3.zero, false, Dt);
            float lossWay = s.SoftCapSpeed * 3f - wayOver.Velocity.x;

            float fracSlight = lossSlight / (s.SoftCapSpeed * 1.1f);
            float fracWay = lossWay / (s.SoftCapSpeed * 3f);
            Assert.Greater(fracWay, fracSlight, "drag must scale with how far past the cap you are");
        }
    }
}
