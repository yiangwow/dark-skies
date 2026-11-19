using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using AlienSkies.Combat;
using AlienSkies.Movement;

namespace AlienSkies.Tests
{
    /// <summary>Replays CmdFireHitscan's spherecast against real instantiated
    /// player prefabs — proves the rifle/shotgun trace can actually find a
    /// player's collider and resolve it to PlayerCombat.</summary>
    public class HitscanGeometryTests
    {
        const float TraceRadius = 0.35f; // keep in lockstep with CombatSettings.HitscanRadius

        static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AlienSkies/Prefabs/Player.prefab");
            Assert.IsNotNull(prefab, "player prefab exists");
            return prefab;
        }

        [Test]
        public void HitscanRay_HitsTheOtherPlayer_NotTheShooter()
        {
            var prefab = LoadPrefab();
            var shooter = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var target = Object.Instantiate(prefab, new Vector3(0f, 0f, 20f), Quaternion.identity);
            try
            {
                Physics.SyncTransforms();
                var pivot = shooter.GetComponent<PlayerMotor>().CameraPivot;
                // Exactly what CmdFireHitscan does: thick trace from the eye.
                bool hit = Physics.SphereCast(pivot.position, TraceRadius, Vector3.forward, out var h, 500f);
                Assert.IsTrue(hit, "trace should hit the target's controller capsule");
                var victim = h.collider.GetComponentInParent<PlayerCombat>();
                Assert.IsNotNull(victim, "hit collider resolves to a PlayerCombat");
                Assert.AreSame(target.GetComponent<PlayerCombat>(), victim,
                    "the ray finds the target, not the shooter's own body");
            }
            finally
            {
                Object.DestroyImmediate(shooter);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ThickTrace_ForgivesAGraze_ThatARayWouldMiss()
        {
            var prefab = LoadPrefab();
            // Aim line passes 0.5m from the target's axis: outside the 0.4
            // capsule (a ray whiffs), inside capsule + trace radius (hit).
            // NOT tighter: at eye height the trace passes near the capsule's
            // cap, so the real reach there is less than radius + trace.
            var shooter = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var target = Object.Instantiate(prefab, new Vector3(0.5f, 0f, 20f), Quaternion.identity);
            try
            {
                Physics.SyncTransforms();
                var pivot = shooter.GetComponent<PlayerMotor>().CameraPivot;
                Assert.IsFalse(Physics.Raycast(pivot.position, Vector3.forward, 500f),
                    "a thin ray misses this graze");
                bool hit = Physics.SphereCast(pivot.position, TraceRadius, Vector3.forward, out var h, 500f);
                Assert.IsTrue(hit, "the thick trace lands it");
                Assert.AreSame(target.GetComponent<PlayerCombat>(),
                    h.collider.GetComponentInParent<PlayerCombat>());
            }
            finally
            {
                Object.DestroyImmediate(shooter);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void HitscanRay_AtShotgunRange_StillHits()
        {
            var prefab = LoadPrefab();
            var shooter = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var target = Object.Instantiate(prefab, new Vector3(0f, 0f, 8f), Quaternion.identity);
            try
            {
                Physics.SyncTransforms();
                var pivot = shooter.GetComponent<PlayerMotor>().CameraPivot;
                bool hit = Physics.SphereCast(pivot.position, TraceRadius, Vector3.forward, out var h, 500f);
                Assert.IsTrue(hit, "point-blank trace connects");
                Assert.IsNotNull(h.collider.GetComponentInParent<PlayerCombat>());
            }
            finally
            {
                Object.DestroyImmediate(shooter);
                Object.DestroyImmediate(target);
            }
        }
    }
}
