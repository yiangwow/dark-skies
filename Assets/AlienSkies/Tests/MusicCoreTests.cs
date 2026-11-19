using NUnit.Framework;
using UnityEngine;
using AlienSkies.Audio;

namespace AlienSkies.Tests
{
    public class MusicCoreTests
    {
        [Test]
        public void StartsCold_AtFloorLevel()
        {
            var m = new MusicCore();
            Assert.AreEqual(0f, m.Intensity, 1e-4f);
            Assert.AreEqual(m.FloorLevel, m.Level, 1e-4f);
        }

        [Test]
        public void KillHeat_ScalesWithMoveState()
        {
            var grounded = new MusicCore(); grounded.RegisterKill(MusicCore.KillMoveState.Grounded);
            var airborne = new MusicCore(); airborne.RegisterKill(MusicCore.KillMoveState.Airborne);
            var flow = new MusicCore(); flow.RegisterKill(MusicCore.KillMoveState.Flow);
            Assert.AreEqual(0.33f, grounded.Intensity, 1e-4f);
            Assert.AreEqual(0.60f, airborne.Intensity, 1e-4f);
            Assert.AreEqual(0.72f, flow.Intensity, 1e-4f);
            Assert.Greater(flow.Intensity, airborne.Intensity);
            Assert.Greater(airborne.Intensity, grounded.Intensity);

            // Kill-vs-hit ordering (Events_HeatInOrder's surviving intent): a
            // lone hit trickle stays well under a lone grounded kill.
            var hit = new MusicCore(); hit.RegisterHit();
            Assert.Less(hit.Intensity, grounded.Intensity);
        }

        [Test]
        public void Intensity_ClampsAtOne()
        {
            var m = new MusicCore();
            // Cycle through all three states so no kill ever repeats the
            // previous one's state — staleness never engages (streak stays
            // 0), yet the undiscounted sum (~5.28) still clears MaxHeat.
            var states = new[]
            {
                MusicCore.KillMoveState.Grounded,
                MusicCore.KillMoveState.Airborne,
                MusicCore.KillMoveState.Flow,
            };
            for (int i = 0; i < 10; i++) m.RegisterKill(states[i % states.Length]);
            Assert.AreEqual(1f, m.Intensity, 1e-4f);
            Assert.AreEqual(1f, m.Level, 1e-4f);
        }

        [Test]
        public void SameStateKills_GoStale_StateChangeRestores()
        {
            var m = new MusicCore();
            m.RegisterKill(MusicCore.KillMoveState.Grounded); // 0.33
            m.RegisterKill(MusicCore.KillMoveState.Grounded); // + 0.2145 (streak 1: 0.33 * 0.65)
            m.RegisterKill(MusicCore.KillMoveState.Grounded); // + ~0.1394 (streak 2: 0.33 * 0.65^2)
            Assert.AreEqual(0.33f + 0.2145f + 0.1394f, m.Intensity, 1e-3f, "staleness compounds on repeats");

            // A further-staled 4th grounded kill (streak 3: 0.33 * 0.65^3 ~=
            // 0.0906) would land at ~0.77, well under 1 — so hitting the
            // clamp here proves the state change earned the FULL 0.60, not a
            // continued-stale amount.
            m.RegisterKill(MusicCore.KillMoveState.Airborne);
            Assert.AreEqual(1f, m.Intensity, 1e-4f, "state change restores full gain, pushing past the clamp");
        }

        [Test]
        public void Heat_DecaysToCold()
        {
            var m = new MusicCore();
            m.RegisterKill(MusicCore.KillMoveState.Grounded);
            for (int i = 0; i < 600; i++) m.Tick(0.02f); // 12 s
            Assert.AreEqual(0f, m.Intensity, 1e-4f);
        }

        [Test]
        public void HotStreak_HoldsFullIntensityBriefly()
        {
            var m = new MusicCore();
            m.RegisterKill(MusicCore.KillMoveState.Flow);
            m.RegisterKill(MusicCore.KillMoveState.Airborne);
            m.RegisterKill(MusicCore.KillMoveState.Flow); // overshoots into the buffer
            m.Tick(1f); // one second of decay
            Assert.AreEqual(1f, m.Intensity, 1e-4f, "overshoot buffer keeps the peak hot");
        }

        [Test]
        public void RoundOver_DucksTheLevel()
        {
            var m = new MusicCore();
            m.RegisterKill(MusicCore.KillMoveState.Grounded);
            float loud = m.Level;
            m.RoundOver = true;
            Assert.AreEqual(loud * m.DuckFactor, m.Level, 1e-4f);
            m.RoundOver = false;
            Assert.AreEqual(loud, m.Level, 1e-4f);
        }

        [Test]
        public void ResetHeat_ZeroesIntensity()
        {
            var c = new MusicCore();
            c.RegisterKill(MusicCore.KillMoveState.Grounded);
            c.RegisterKill(MusicCore.KillMoveState.Grounded);
            Assert.Greater(c.Intensity, 0f);
            c.ResetHeat();
            Assert.AreEqual(0f, c.Intensity);
        }

        [Test]
        public void DropTier_LandsExactlyOneTierDown()
        {
            const int maxMultiplier = 4;
            const int bands = maxMultiplier - 1; // 3

            foreach (var seed in new[] { 0.5f, 0.8f, 1.0f })
            {
                // Public fields are MusicCore's config contract: seed heat
                // exactly via a first (undiscounted) kill rather than reaching
                // into private state.
                var m = new MusicCore { KillHeatGrounded = seed };
                m.RegisterKill(MusicCore.KillMoveState.Grounded);
                int tierBefore = 1 + Mathf.Min(bands, Mathf.FloorToInt(m.Intensity * bands + 0.0001f));

                m.DropTier(maxMultiplier);
                int tierAfter = 1 + Mathf.Min(bands, Mathf.FloorToInt(m.Intensity * bands + 0.0001f));

                Assert.AreEqual(tierBefore - 1, tierAfter, $"seed {seed}: expected exactly one tier down");
            }

            // At tier 1 there is nothing below: heat halves instead.
            var floor = new MusicCore { KillHeatGrounded = 0.2f };
            floor.RegisterKill(MusicCore.KillMoveState.Grounded);
            float before = floor.Intensity;
            floor.DropTier(maxMultiplier);
            Assert.AreEqual(before * 0.5f, floor.Intensity, 1e-4f);
        }

        [Test]
        public void CampTest_GroundedLoopFallsOutOfTopTiers()
        {
            var m = new MusicCore();
            const int maxMultiplier = 4;
            const int bands = maxMultiplier - 1;

            for (int cycle = 0; cycle < 5; cycle++)
            {
                m.RegisterKill(MusicCore.KillMoveState.Grounded);
                Assert.LessOrEqual(m.Intensity, 0.34f, $"cycle {cycle}: grounded camping must go stale");
                int tier = 1 + Mathf.Min(bands, Mathf.FloorToInt(m.Intensity * bands + 0.0001f));
                Assert.Less(tier, 3, $"cycle {cycle}: a stationary loop must never hold tier 3+");

                for (int i = 0; i < 80; i++) m.Tick(0.05f); // 4 s of decay between kills
            }
        }

        [Test]
        public void ResetHeat_ClearsStaleness()
        {
            var m = new MusicCore();
            m.RegisterKill(MusicCore.KillMoveState.Grounded);
            m.RegisterKill(MusicCore.KillMoveState.Grounded); // streak 1: discounted
            m.ResetHeat();
            m.RegisterKill(MusicCore.KillMoveState.Grounded);
            Assert.AreEqual(0.33f, m.Intensity, 1e-4f, "ResetHeat must clear the same-state streak, not just heat");
        }

        [Test]
        public void HitTrickle_RateLimited()
        {
            var m = new MusicCore();
            for (int i = 0; i < 16; i++) m.RegisterHit(); // one frame's worth of shotgun pellets
            Assert.AreEqual(0.08f, m.Intensity, 1e-4f, "cooldown must collapse the whole pellet burst to one add");

            m.Tick(0.7f); // cooldown elapses; heat also decays 0.12*0.7 = 0.084 in that window
            float beforeSecondHit = m.Intensity;
            m.RegisterHit();
            Assert.Greater(m.Intensity, beforeSecondHit, "cooldown cleared: the next hit must add again");

            // Sustained worst case: hit right as the cooldown clears, every cycle.
            var sustained = new MusicCore();
            for (int i = 0; i < 10; i++)
            {
                sustained.RegisterHit();
                Assert.Less(sustained.Intensity, 0.1f, "no weapon may hold a tier by plinking at the cooldown's own rate");
                sustained.Tick(0.7f);
            }
        }
    }
}
