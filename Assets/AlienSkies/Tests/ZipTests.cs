using NUnit.Framework;
using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Tests
{
    public class ZipTests
    {
        const float Dt = 0.016f;
        static MovementSettings S() => new MovementSettings();
        static MoveInput NoInput => new MoveInput { WishDir = Vector3.zero, JumpPressed = false };

        [Test]
        public void StartZip_RequiresActiveGrapple()
        {
            var core = new MovementCore(S());
            Assert.IsFalse(core.StartZip(), "zip must be an upgrade off the hook, not a free teleport");
            Assert.IsFalse(core.IsZipping);
        }

        [Test]
        public void Zip_OverwritesMomentum_TowardAnchor()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            core.SetVelocity(new Vector3(0f, 0f, 30f)); // flying sideways, fast
            Assert.IsTrue(core.StartZip());
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(s.ZipSpeed, core.Velocity.x, 0.01f, "zip overwrites momentum, not adds to it");
            Assert.AreEqual(0f, core.Velocity.z, 0.01f, "old sideways momentum must be gone");
        }

        [Test]
        public void Zip_SuppressesGravityAndSteering()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            var input = new MoveInput { WishDir = Vector3.forward, JumpPressed = false };
            core.Tick(input, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(0f, core.Velocity.y, 1e-4f, "gravity must be off during zip");
            Assert.AreEqual(0f, core.Velocity.z, 1e-4f, "WASD must not bend a zip");
        }

        [Test]
        public void Zip_Arrival_DetachesAndClampsLandingSpeed_NoReleaseBoost()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(1.5f, 0f, 0f)); // inside ZipArriveRadius of origin
            core.SetVelocity(new Vector3(45f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.IsFalse(core.IsZipping, "arrival ends the zip");
            Assert.IsFalse(core.IsGrappling, "arrival detaches the hook");
            Assert.LessOrEqual(core.Velocity.magnitude, s.ZipLandSpeed + 0.01f,
                "arrival clamps speed so you stick the landing (and no release boost fires)");
        }

        [Test]
        public void Zip_Cooldown_BlocksImmediateReuse()
        {
            var core = new MovementCore(S());
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            core.StopGrapple();
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsFalse(core.StartZip(), "cooldown must block back-to-back zips");
        }

        [Test]
        public void Zip_Cooldown_ExpiresAfterConfiguredTime()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            core.StopGrapple();
            int ticks = Mathf.CeilToInt(s.ZipCooldown / Dt) + 2;
            for (int i = 0; i < ticks; i++)
                core.Tick(NoInput, Vector3.zero, grounded: false, Dt);
            Assert.AreEqual(0f, core.ZipCooldownRemaining, 1e-4f);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip(), "cooldown must recover after ZipCooldown seconds");
        }

        [Test]
        public void Zip_CancelViaStopGrapple_KeepsVelocityWithReleaseBoost()
        {
            var s = S();
            var core = new MovementCore(s);
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip());
            core.Tick(NoInput, Vector3.zero, grounded: false, Dt); // velocity = 45 along +x
            core.StopGrapple();
            Assert.IsFalse(core.IsZipping);
            Assert.GreaterOrEqual(core.Velocity.x, s.ZipSpeed - 0.01f,
                "cancel keeps zip momentum (release boost applies on top)");
        }

        [Test]
        public void Reset_ClearsZipStateAndCooldown()
        {
            var core = new MovementCore(S());
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            core.StartZip();
            core.Reset();
            Assert.IsFalse(core.IsZipping);
            Assert.AreEqual(0f, core.ZipCooldownRemaining, 1e-4f, "respawn must not carry zip cooldown");
            core.StartGrapple(new Vector3(100f, 0f, 0f));
            Assert.IsTrue(core.StartZip(), "zip must be available immediately after reset");
        }
    }
}
