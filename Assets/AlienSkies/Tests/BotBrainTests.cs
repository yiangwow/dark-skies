using NUnit.Framework;
using UnityEngine;
using AlienSkies.Bots;

namespace AlienSkies.Tests
{
    public class BotBrainTests
    {
        static BotBrain.Sense SenseAt(Vector3 self, Vector3 target, bool visible = true, bool grounded = false) =>
            new BotBrain.Sense
            {
                SelfPos = self,
                TargetPos = target,
                HasTarget = true,
                TargetVisible = visible,
                Grounded = grounded,
            };

        [Test]
        public void NoTarget_Idles()
        {
            var brain = new BotBrain(1);
            var intent = brain.Tick(new BotBrain.Sense(), 0.02f);
            Assert.IsFalse(intent.Fire);
            Assert.IsFalse(intent.Jump);
            Assert.AreEqual(Vector3.zero, intent.WishDir);
        }

        [Test]
        public void FarTarget_MovesToward()
        {
            var brain = new BotBrain(1);
            var s = SenseAt(Vector3.zero, new Vector3(100f, 0f, 0f));
            var intent = brain.Tick(in s, 0.02f);
            Assert.Greater(Vector3.Dot(intent.WishDir, Vector3.right), 0.4f,
                "far outside the orbit ring the bot should close distance");
            Assert.AreEqual(0f, intent.WishDir.y, 1e-4f, "wish dir stays flat");
            Assert.AreEqual(1f, intent.WishDir.magnitude, 1e-3f);
        }

        [Test]
        public void CrowdedTarget_BacksOff()
        {
            var brain = new BotBrain(1);
            var s = SenseAt(Vector3.zero, new Vector3(5f, 0f, 0f));
            var intent = brain.Tick(in s, 0.02f);
            Assert.Less(Vector3.Dot(intent.WishDir, Vector3.right), 0f,
                "inside the ring the bot should open distance");
        }

        [Test]
        public void OnOrbitRing_Strafes()
        {
            var brain = new BotBrain(1);
            var s = SenseAt(Vector3.zero, new Vector3(brain.OrbitRadius, 0f, 0f));
            var intent = brain.Tick(in s, 0.02f);
            Assert.Less(Mathf.Abs(Vector3.Dot(intent.WishDir, Vector3.right)), 0.15f,
                "on the ring movement should be tangential");
        }

        [Test]
        public void Fire_RequiresVisibilityRangeAndCadence()
        {
            var brain = new BotBrain(1);
            var s = SenseAt(Vector3.zero, new Vector3(30f, 0f, 0f));

            // Spawn grace: no fire in the first second.
            Assert.IsFalse(brain.Tick(in s, 0.02f).Fire);

            // Burn the grace with a HIDDEN target so nothing fires (and resets
            // the cadence) while the clock runs down.
            var hidden = SenseAt(Vector3.zero, new Vector3(30f, 0f, 0f), visible: false);
            for (int i = 0; i < 120; i++) brain.Tick(in hidden, 0.02f);

            Assert.IsFalse(brain.Tick(in hidden, 0.02f).Fire, "no fire without line of sight");

            var far = SenseAt(Vector3.zero, new Vector3(brain.FireRange + 50f, 0f, 0f));
            Assert.IsFalse(brain.Tick(in far, 0.02f).Fire, "no fire out of range");

            // Round-3 hold-fire rule (SANCTIONED rewrite — this tail used to
            // assert an immediate fire on re-exposure; under hold-fire that is
            // now wrong by design): re-exposure raises the cooldown to a full
            // TelegraphTime instead of firing immediately, so every shot gets
            // a warning window. Assert the withhold over a window short of the
            // exact 25-tick boundary (float accumulation over 25 steps of
            // dt=0.02 could land the crossing on tick 24 or 25 — asserting the
            // exact tick would flake), then bound-search for the eventual shot,
            // then confirm cadence still blocks the tick right after.
            bool sawTelegraph = false;
            for (int i = 0; i < 22; i++)
            {
                var intent = brain.Tick(in s, 0.02f);
                Assert.IsFalse(intent.Fire, $"hold-fire should withhold through the telegraph window (tick {i})");
                sawTelegraph |= intent.Telegraphing;
            }
            Assert.IsTrue(sawTelegraph, "re-exposure should telegraph before firing");

            bool fired = false;
            for (int i = 0; i < 10 && !fired; i++)
                fired = brain.Tick(in s, 0.02f).Fire;
            Assert.IsTrue(fired, "the held shot eventually fires");

            Assert.IsFalse(brain.Tick(in s, 0.02f).Fire, "cadence blocks the next tick");
        }

        [Test]
        public void Leading_AimsAheadOfAMovingTarget()
        {
            var brain = new BotBrain(1);
            var s = new BotBrain.Sense
            {
                SelfPos = Vector3.zero,
                TargetPos = new Vector3(30f, 0f, 0f),
                TargetVelocity = new Vector3(0f, 0f, 20f), // lateral to the line of sight
                HasTarget = true,
                TargetVisible = true,
            };
            // dist 30, ProjectileSpeed 150 (default) -> tof 0.2s -> lead offset
            // 20 * 0.2 = 4m laterally. Fresh brain: spawn-grace cooldown keeps
            // this tick from firing, so AimDir carries no aim-error roll.
            var intent = brain.Tick(in s, 0.02f);
            Vector3 ledDir = (new Vector3(30f, 0f, 4f) - s.SelfPos).normalized;
            Assert.Less(Vector3.Angle(intent.AimDir, ledDir), brain.AimErrorDegrees + 0.5f);
        }

        [Test]
        public void Leading_ClampsAtMaxLeadTime()
        {
            var brain = new BotBrain(1) { ProjectileSpeed = 5f };
            var s = new BotBrain.Sense
            {
                SelfPos = Vector3.zero,
                TargetPos = new Vector3(30f, 0f, 0f),
                TargetVelocity = new Vector3(0f, 0f, 20f),
                HasTarget = true,
                TargetVisible = true,
            };
            // dist 30 / ProjectileSpeed 5 = 6s tof, clamped to 1.2s.
            var intent = brain.Tick(in s, 0.02f);
            Vector3 expected = ((s.TargetPos + s.TargetVelocity * 1.2f) - s.SelfPos).normalized;
            Assert.Less(Vector3.Angle(intent.AimDir, expected), brain.AimErrorDegrees + 0.5f);
        }

        [Test]
        public void StationaryTarget_UnchangedAim()
        {
            var brain = new BotBrain(1);
            var s = SenseAt(Vector3.zero, new Vector3(30f, 0f, 0f)); // TargetVelocity defaults zero
            var intent = brain.Tick(in s, 0.02f);
            Vector3 expected = (s.TargetPos - s.SelfPos).normalized;
            Assert.Less(Vector3.Angle(intent.AimDir, expected), brain.AimErrorDegrees + 0.5f);
        }

        [Test]
        public void Telegraph_PrecedesFire()
        {
            var brain = new BotBrain(5);
            var s = SenseAt(Vector3.zero, new Vector3(30f, 0f, 0f));
            int firstTelegraphTick = -1;
            int firstFireTick = -1;
            int shots = 0;
            for (int tick = 0; tick < 500 && shots < 3; tick++)
            {
                var intent = brain.Tick(in s, 0.02f);
                if (intent.Telegraphing && firstTelegraphTick < 0) firstTelegraphTick = tick;
                if (intent.Fire)
                {
                    if (firstFireTick < 0) firstFireTick = tick;
                    shots++;
                }
            }
            Assert.GreaterOrEqual(firstTelegraphTick, 0, "should telegraph at some point");
            Assert.GreaterOrEqual(firstFireTick, 0, "should fire at some point");
            Assert.Less(firstTelegraphTick, firstFireTick, "telegraph must precede the first shot");
            Assert.AreEqual(3, shots, "legacy cadence keeps producing shots");
        }

        [Test]
        public void FirstShotAfterExposure_IsTelegraphed()
        {
            var brain = new BotBrain(9);
            var hidden = SenseAt(Vector3.zero, new Vector3(30f, 0f, 0f), visible: false);
            for (int i = 0; i < 200; i++) brain.Tick(in hidden, 0.02f); // burn cooldown deeply negative while hidden

            var visible = SenseAt(Vector3.zero, new Vector3(30f, 0f, 0f));
            bool sawTelegraph = false;
            for (int i = 0; i < 22; i++)
            {
                var intent = brain.Tick(in visible, 0.02f);
                Assert.IsFalse(intent.Fire, $"hold-fire should withhold through the telegraph window (tick {i})");
                sawTelegraph |= intent.Telegraphing;
            }
            Assert.IsTrue(sawTelegraph, "should telegraph during the held window");

            bool fired = false;
            for (int i = 0; i < 10 && !fired; i++)
                fired = brain.Tick(in visible, 0.02f).Fire;
            Assert.IsTrue(fired, "the held shot eventually fires");
        }

        [Test]
        public void Aim_StaysInsideErrorCone()
        {
            var brain = new BotBrain(7);
            var s = SenseAt(Vector3.zero, new Vector3(30f, 4f, 10f));
            for (int i = 0; i < 60; i++) brain.Tick(in s, 0.02f);

            Vector3 perfect = (s.TargetPos - s.SelfPos).normalized;
            int shots = 0;
            for (int tick = 0; tick < 20000 && shots < 20; tick++)
            {
                var intent = brain.Tick(in s, 0.02f);
                if (!intent.Fire) continue;
                shots++;
                Assert.LessOrEqual(Vector3.Angle(perfect, intent.AimDir), brain.AimErrorDegrees + 1e-3f);
            }
            Assert.AreEqual(20, shots, "sampled enough error rolls");
        }

        [Test]
        public void Hops_OnlyWhenGrounded_AndOnTimer()
        {
            var brain = new BotBrain(3);
            var air = SenseAt(Vector3.zero, new Vector3(30f, 0f, 0f), grounded: false);
            for (int i = 0; i < 300; i++)
                Assert.IsFalse(brain.Tick(in air, 0.02f).Jump, "never jump airborne");

            var ground = SenseAt(Vector3.zero, new Vector3(30f, 0f, 0f), grounded: true);
            int jumps = 0;
            for (int i = 0; i < 500; i++)
                if (brain.Tick(in ground, 0.02f).Jump) jumps++;
            Assert.Greater(jumps, 0, "grounded bot hops eventually");
            Assert.Less(jumps, 12, "10 seconds should yield a handful of hops, not spam");
        }

        [Test]
        public void Stationary_HoldsPosition_ButStillFires()
        {
            var brain = new BotBrain(11) { Stationary = true };
            bool fired = false;
            for (int i = 0; i < 200; i++)
            {
                var intent = brain.Tick(SenseAt(Vector3.zero, new Vector3(40f, 0f, 0f), grounded: true), 0.02f);
                Assert.AreEqual(Vector3.zero, intent.WishDir);
                Assert.IsFalse(intent.Jump);
                fired |= intent.Fire;
            }
            Assert.IsTrue(fired, "a stationary bot must still shoot");
        }

        [Test]
        public void SameSeed_SameBehavior()
        {
            var a = new BotBrain(42);
            var b = new BotBrain(42);
            var s = SenseAt(Vector3.zero, new Vector3(25f, 3f, 12f), grounded: true);
            for (int i = 0; i < 400; i++)
            {
                var ia = a.Tick(in s, 0.02f);
                var ib = b.Tick(in s, 0.02f);
                Assert.AreEqual(ia.Fire, ib.Fire);
                Assert.AreEqual(ia.Jump, ib.Jump);
                Assert.AreEqual(ia.WishDir, ib.WishDir);
                Assert.AreEqual(ia.AimDir, ib.AimDir);
            }
        }

        // The file's real helper is SenseAt(self, target, visible, grounded) with
        // grounded DEFAULTING FALSE — spell grounded:true or the hop test is
        // vacuous (review-caught). 18m = OrbitRadius puts tick 1 in pure-tangent.
        static BotBrain.Sense OrbitSense() =>
            SenseAt(Vector3.zero, new Vector3(18f, 0f, 0f), grounded: true);

        [Test]
        public void EdgeAhead_BacksAwayFromTheLedge()
        {
            var b = new BotBrain(11);
            var s = OrbitSense();
            var preEdge = b.Tick(in s, 0.05f).WishDir; // walking somewhere (tangent)
            s.EdgeAhead = true;
            var edgeWish = b.Tick(in s, 0.05f).WishDir;
            // STRICT + CORRECT VECTOR (two review rounds): the safe direction is
            // the reverse of where the bot was walking — NOT toward the target,
            // who is usually off the platform in a speedrun game.
            Assert.Less(Vector3.Dot(edgeWish, preEdge), -0.9f, "did not back away from the ledge");
            // Round 3: the retreat is LATCHED — a second consecutive edge tick
            // must keep retreating, not invert back toward the ledge.
            var edgeWish2 = b.Tick(in s, 0.05f).WishDir;
            Assert.Less(Vector3.Dot(edgeWish2, preEdge), -0.9f, "retreat self-inverted on a sustained edge");
        }

        [Test]
        public void EdgeAhead_SuppressesHop()
        {
            var b = new BotBrain(11);
            var s = OrbitSense();
            s.EdgeAhead = true;
            // Arm the hop timer generously, then tick at the edge: never jump.
            for (int k = 0; k < 60; k++)
            {
                var i = b.Tick(in s, 0.25f);
                Assert.IsFalse(i.Jump, "hopped at a ledge");
            }
        }

        [Test]
        public void EdgeFlipsOrbitDirection_OnceAndLatched()
        {
            var b = new BotBrain(11);
            var s = OrbitSense();
            var before = b.Tick(in s, 0.05f).WishDir;      // pre-edge tangent
            s.EdgeAhead = true;
            b.Tick(in s, 0.05f);                            // edge: flip + inward
            b.Tick(in s, 0.05f);                            // still edge: latched, no second flip
            s.EdgeAhead = false;
            var after = b.Tick(in s, 0.05f).WishDir;       // tangent again, sign flipped
            Assert.Less(Vector3.Dot(after, before), 0f, "orbit direction did not flip");
        }

        [Test]
        public void NoEdge_BehaviorUnchanged()
        {
            var a = new BotBrain(23);
            var b = new BotBrain(23);
            var s1 = OrbitSense();               // EdgeAhead defaults false
            var s2 = OrbitSense(); s2.EdgeAhead = false;
            Assert.AreEqual(a.Tick(in s1, 0.1f).WishDir, b.Tick(in s2, 0.1f).WishDir);
        }

        [Test]
        public void Stationary_IgnoresEdge()
        {
            var b = new BotBrain(11) { Stationary = true };
            var s = OrbitSense();
            s.EdgeAhead = true;
            Assert.AreEqual(Vector3.zero, b.Tick(in s, 0.1f).WishDir);
        }
    }
}
