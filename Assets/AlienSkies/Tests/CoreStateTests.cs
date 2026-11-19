using NUnit.Framework;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Tests
{
    public class CoreStateTests
    {
        const float Dt = 0.016f;
        static MovementSettings S() => new MovementSettings();
        static MoveInput NoInput => new MoveInput { WishDir = Vector3.zero, JumpPressed = false };

        [Test]
        public void CaptureApply_RoundTrip_ProducesIdenticalSimulation()
        {
            var a = new MovementCore(S());
            var b = new MovementCore(S());
            a.SetVelocity(new Vector3(12f, 3f, -4f));
            a.StartGrapple(new Vector3(50f, 20f, 10f));
            a.Tick(NoInput, Vector3.zero, grounded: false, Dt); // consume attach yank
            a.RegisterSurfaceContact(new Vector3(0f, 0f, 1f));

            b.ApplyState(a.CaptureState());
            Assert.Greater(b.SurfaceContactRemaining, 0f, "surface contact must survive reconcile");
            var pos = new Vector3(1f, 2f, 3f);
            for (int i = 0; i < 60; i++)
            {
                a.Tick(NoInput, pos, grounded: false, Dt);
                b.Tick(NoInput, pos, grounded: false, Dt);
            }
            Assert.AreEqual(a.Velocity, b.Velocity, "reconcile depends on snapshot restoring exact simulation");
            Assert.AreEqual(a.IsGrappling, b.IsGrappling);
        }

        [Test]
        public void ApplyState_RestoresZipCooldown()
        {
            var a = new MovementCore(S());
            a.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(a.StartZip()); // cooldown now running on A

            var b = new MovementCore(S());
            b.ApplyState(a.CaptureState());
            b.StopGrapple();
            b.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsFalse(b.StartZip(), "cooldown must survive the snapshot or clients could zip-spam after reconciles");
        }

        [Test]
        public void ApplyState_MidZip_ContinuesZipFlight()
        {
            var s = S();
            var a = new MovementCore(s);
            a.StartGrapple(new Vector3(100f, 0f, 0f));
            a.StartZip();

            var b = new MovementCore(s);
            b.ApplyState(a.CaptureState());
            b.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.IsTrue(b.IsZipping);
            Assert.AreEqual(s.ZipSpeed, b.Velocity.x, 0.01f, "restored zip must keep flying at zip speed");
        }

        [Test]
        public void Determinism_SameInputsSamePositions_SameTrajectory()
        {
            var a = new MovementCore(S());
            var b = new MovementCore(S());
            var wish = new MoveInput { WishDir = Vector3.forward, JumpPressed = false };
            var pos = Vector3.zero;
            for (int i = 0; i < 120; i++)
            {
                bool grounded = i < 10;
                var input = (i % 3 == 0) ? wish : NoInput;
                if (i == 30) { a.StartGrapple(new Vector3(0f, 40f, 80f)); b.StartGrapple(new Vector3(0f, 40f, 80f)); }
                if (i == 45) { a.StartZip(); b.StartZip(); }
                if (i == 60) { a.StopGrapple(); b.StopGrapple(); }
                a.Tick(input, pos, grounded, Dt);
                b.Tick(input, pos, grounded, Dt);
                pos += a.Velocity * Dt;
            }
            Assert.AreEqual(a.Velocity, b.Velocity, "prediction requires deterministic re-simulation");
        }
    }
}
