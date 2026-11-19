using NUnit.Framework;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Tests
{
    public class MovementCoreTests
    {
        const float Dt = 0.016f;
        static MovementSettings S() => new MovementSettings();
        static MoveInput NoInput => new MoveInput { WishDir = Vector3.zero, JumpPressed = false };

        [Test]
        public void AirTick_NoInput_PreservesHorizontalVelocity()
        {
            var core = new MovementCore(S());
            core.SetVelocity(new Vector3(10f, 0f, 0f));
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(10f, core.Velocity.x, 1e-4f, "air movement must not destroy momentum");
        }

        [Test]
        public void AirTick_Gravity_PullsDown()
        {
            var core = new MovementCore(S());
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.Less(core.Velocity.y, 0f);
        }

        [Test]
        public void GroundTick_NoInput_FrictionSlows()
        {
            var core = new MovementCore(S());
            core.SetVelocity(new Vector3(10f, 0f, 0f));
            core.Tick(NoInput, Vector3.zero, grounded: true, Dt);
            Assert.Less(core.Velocity.x, 10f);
            Assert.Greater(core.Velocity.x, 0f, "friction must slow, not reverse");
        }

        [Test]
        public void GroundTick_Input_AcceleratesTowardWishDir()
        {
            var core = new MovementCore(S());
            var input = new MoveInput { WishDir = Vector3.forward, JumpPressed = false };
            core.Tick(input, Vector3.zero, grounded: true, Dt);
            Assert.Greater(core.Velocity.z, 0f);
        }

        [Test]
        public void AirStrafe_CannotExceedAirControlMaxSpeed_InWishDirection()
        {
            var s = S();
            var core = new MovementCore(s);
            core.SetVelocity(Vector3.forward * s.AirControlMaxSpeed);
            var input = new MoveInput { WishDir = Vector3.forward, JumpPressed = false };
            core.Tick(input, Vector3.zero, grounded: false, Dt);
            float along = Vector3.Dot(core.Velocity, Vector3.forward);
            Assert.LessOrEqual(along, s.AirControlMaxSpeed + 1e-3f);
        }

        [Test]
        public void AirSteer_BendsFlightPath_WithoutChangingSpeed()
        {
            var s = S();
            var core = new MovementCore(s);
            core.SetVelocity(new Vector3(30f, 0f, 0f));
            var input = new MoveInput { WishDir = Vector3.forward, JumpPressed = false };
            core.Tick(input, Vector3.zero, grounded: false, Dt);
            var horiz = new Vector3(core.Velocity.x, 0f, core.Velocity.z);
            Assert.Greater(core.Velocity.z, 0.3f, "held direction must bend the path");
            Assert.Less(core.Velocity.x, 30f, "bending must rotate the path, not just add sideways speed");
            Assert.LessOrEqual(horiz.magnitude, 30f + s.AirAccel * Dt + 1e-3f,
                "steering must never add speed beyond quake accel");
            Assert.GreaterOrEqual(horiz.magnitude, 30f - 1e-3f, "steering must never cost speed");
        }

        [Test]
        public void Jump_WhenGrounded_SetsVerticalSpeed()
        {
            var s = S();
            var core = new MovementCore(s);
            var input = new MoveInput { WishDir = Vector3.zero, JumpPressed = true };
            core.Tick(input, Vector3.zero, grounded: true, Dt);
            Assert.AreEqual(s.JumpSpeed, core.Velocity.y, 1e-4f);
        }

        [Test]
        public void GroundTick_ClampsAccumulatedFallSpeed()
        {
            var core = new MovementCore(S());
            core.SetVelocity(new Vector3(0f, -30f, 0f));
            core.Tick(NoInput, Vector3.zero, grounded: true, Dt);
            Assert.GreaterOrEqual(core.Velocity.y, -2.01f, "landing must not bank fall speed");
        }

        [Test]
        public void IsGrounded_TracksTheGroundedParameter()
        {
            var core = new MovementCore(S());
            core.Tick(NoInput, Vector3.zero, grounded: true, Dt);
            Assert.IsTrue(core.IsGrounded);
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.IsFalse(core.IsGrounded);
        }

        [Test]
        public void Reset_ZeroesVelocity()
        {
            var core = new MovementCore(S());
            core.SetVelocity(new Vector3(5f, 5f, 5f));
            core.Reset();
            Assert.AreEqual(0f, core.Velocity.magnitude, 1e-6f);
        }
    }
}
