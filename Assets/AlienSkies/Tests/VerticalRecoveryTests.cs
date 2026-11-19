using NUnit.Framework;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Tests
{
    public class VerticalRecoveryTests
    {
        static MovementSettings S() => new MovementSettings();
        static MoveInput Jump() => new MoveInput { JumpPressed = true };
        static MoveInput NoInput() => new MoveInput();
        const float Dt = 0.02f;

        [Test]
        public void JumpBuffer_PressBeforeLanding_JumpsOnLanding()
        {
            var c = new MovementCore(S());
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);   // press mid-air
            c.Tick(NoInput(), Vector3.zero, grounded: false, Dt);
            c.Tick(NoInput(), Vector3.zero, grounded: true, Dt); // land within window
            Assert.AreEqual(S().JumpSpeed, c.Velocity.y, 1e-3f, "buffered jump fires on landing");
        }

        [Test]
        public void JumpBuffer_ExpiresBeforeLanding_NoJump()
        {
            var s = S();
            var c = new MovementCore(s);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            for (int i = 0; i < 20; i++) // 0.4s airborne >> buffer window
                c.Tick(NoInput(), Vector3.zero, grounded: false, Dt);
            c.Tick(NoInput(), Vector3.zero, grounded: true, Dt);
            Assert.Less(c.Velocity.y, s.JumpSpeed * 0.5f, "stale buffer must not jump");
        }

        [Test]
        public void Coyote_JumpJustAfterLeavingGround_StillJumps()
        {
            var c = new MovementCore(S());
            c.Tick(NoInput(), Vector3.zero, grounded: true, Dt);  // arm coyote
            c.Tick(NoInput(), Vector3.zero, grounded: false, Dt); // walk off ledge
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);    // press within window
            Assert.AreEqual(S().JumpSpeed, c.Velocity.y, 1e-3f, "coyote jump fires");
        }

        [Test]
        public void Coyote_ExpiresInAir_NoJump()
        {
            var s = S();
            var c = new MovementCore(s);
            c.Tick(NoInput(), Vector3.zero, grounded: true, Dt);
            for (int i = 0; i < 20; i++) // 0.4s falling >> coyote window
                c.Tick(NoInput(), Vector3.zero, grounded: false, Dt);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            Assert.Less(c.Velocity.y, 0f, "no ghost jumps long after leaving ground");
        }

        [Test]
        public void PlanetLaunch_JumpAfterContact_LaunchesAlongNormal()
        {
            var s = S();
            var c = new MovementCore(s);
            var normal = new Vector3(1f, 1f, 0f).normalized;
            c.RegisterSurfaceContact(normal);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            float along = Vector3.Dot(c.Velocity, normal);
            Assert.Greater(along, s.PlanetLaunchSpeed * 0.8f, "launch adds speed along the normal");
        }

        [Test]
        public void PlanetLaunch_IsAdditive_KeepsIncomingMomentum()
        {
            var s = S();
            var c = new MovementCore(s);
            c.SetVelocity(new Vector3(30f, 0f, 0f)); // flying past the planet
            c.RegisterSurfaceContact(Vector3.up);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            Assert.Greater(c.Velocity.x, 25f, "horizontal momentum survives the launch");
            Assert.Greater(c.Velocity.y, s.PlanetLaunchSpeed * 0.8f, "and the kick is added on top");
        }

        [Test]
        public void PlanetLaunch_BeatsGroundJump_WhenGroundedOnPlanet()
        {
            var s = S();
            var c = new MovementCore(s);
            c.RegisterSurfaceContact(Vector3.up);
            c.Tick(Jump(), Vector3.zero, grounded: true, Dt);
            Assert.Greater(c.Velocity.y, s.JumpSpeed * 1.5f, "planet top gives the launch, not the hop");
        }

        [Test]
        public void PlanetLaunch_ContactConsumedOnUse()
        {
            var s = S();
            var c = new MovementCore(s);
            c.RegisterSurfaceContact(Vector3.up);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            float yAfterFirst = c.Velocity.y;
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt); // no new contact
            Assert.Less(c.Velocity.y, yAfterFirst, "second jump must not re-launch from one touch");
        }

        [Test]
        public void PlanetLaunch_ContactExpires()
        {
            var s = S();
            var c = new MovementCore(s);
            c.RegisterSurfaceContact(Vector3.up);
            for (int i = 0; i < 20; i++) // 0.4s >> contact window
                c.Tick(NoInput(), Vector3.zero, grounded: false, Dt);
            c.Tick(Jump(), Vector3.zero, grounded: false, Dt);
            Assert.Less(c.Velocity.y, 0f, "stale contact must not launch");
        }
    }
}
