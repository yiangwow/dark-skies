using NUnit.Framework;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Tests
{
    public class GrappleTests
    {
        const float Dt = 0.016f;
        static MoveInput NoInput => new MoveInput { WishDir = Vector3.zero, JumpPressed = false };

        [Test]
        public void Pull_AcceleratesTowardAnchor()
        {
            var core = new MovementCore(new MovementSettings());
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.Greater(core.Velocity.x, 0f);
        }

        [Test]
        public void Attach_YanksToMinimumClosingSpeed()
        {
            var s = new MovementSettings();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.GreaterOrEqual(core.Velocity.x, s.GrappleAttachSpeed - 1e-3f,
                "first grapple tick must snap to the attach speed, not wind up from zero");
        }

        [Test]
        public void Attach_DoesNotStackOnExistingClosingSpeed()
        {
            var s = new MovementSettings();
            var core = new MovementCore(s);
            float fast = s.GrappleAttachSpeed * 2f;
            core.SetVelocity(new Vector3(fast, 0f, 0f));
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            float expectedMax = fast + s.GrapplePullAccel * Dt + 1e-3f;
            Assert.LessOrEqual(core.Velocity.x, expectedMax,
                "attach yank must top-up to the minimum, not add on top of existing speed");
        }

        [Test]
        public void Pull_SuppressesGravity()
        {
            var core = new MovementCore(new MovementSettings());
            core.StartGrapple(new Vector3(100f, 0f, 0f)); // level anchor: any -y is gravity leaking in
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(0f, core.Velocity.y, 1e-4f, "gravity must not fight the zip");
        }

        [Test]
        public void Pull_NearAnchor_StopsAccelerating()
        {
            var core = new MovementCore(new MovementSettings());
            core.StartGrapple(new Vector3(0.5f, 0f, 0f));
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(0f, core.Velocity.magnitude, 1e-4f, "inside arrival radius nothing should pull");
        }

        [Test]
        public void Release_PreservesSpeedAndAddsBoost()
        {
            var s = new MovementSettings();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            core.SetVelocity(new Vector3(20f, 0f, 0f));
            core.StopGrapple();
            Assert.IsFalse(core.IsGrappling);
            Assert.GreaterOrEqual(core.Velocity.magnitude, 20f, "release must never cost speed");
            Assert.Greater(core.Velocity.magnitude, 20f + s.GrappleReleaseBoost - 1f, "boost missing");
        }

        [Test]
        public void Release_WhenNotGrappling_IsANoOp()
        {
            var core = new MovementCore(new MovementSettings());
            core.SetVelocity(new Vector3(20f, 0f, 0f));
            core.StopGrapple();
            Assert.AreEqual(20f, core.Velocity.magnitude, 1e-3f);
        }

        [Test]
        public void Steering_WorksMidGrapple()
        {
            var core = new MovementCore(new MovementSettings());
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            var input = new MoveInput { WishDir = Vector3.forward, JumpPressed = false };
            core.Tick(input, Vector3.zero, grounded: false, Dt);
            Assert.Greater(core.Velocity.z, 0f, "player must be able to steer during the zip");
        }

        [Test]
        public void ClampAgainstSurface_KillsOnlyTheIntoComponent()
        {
            var core = new MovementCore(new MovementSettings());
            core.SetVelocity(new Vector3(5f, -10f, 0f));
            core.ClampAgainstSurface(Vector3.up);
            Assert.AreEqual(0f, core.Velocity.y, 1e-4f);
            Assert.AreEqual(5f, core.Velocity.x, 1e-4f, "sliding along the surface must survive");
        }

        [Test]
        public void Reset_StopsGrapple()
        {
            var core = new MovementCore(new MovementSettings());
            core.StartGrapple(Vector3.one * 50f);
            core.Reset();
            Assert.IsFalse(core.IsGrappling);
        }
    }
}
