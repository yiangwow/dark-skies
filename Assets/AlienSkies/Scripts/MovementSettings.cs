using UnityEngine;

namespace AlienSkies.Movement
{
    /// <summary>All tuning numbers for the movement model. Plain class so tests
    /// construct it directly; MovementConfig (ScriptableObject) wraps it for the editor.</summary>
    [System.Serializable]
    public class MovementSettings
    {
        [Header("Ground")]
        public float GroundAccel = 60f;
        public float GroundMaxSpeed = 10f;
        public float GroundFriction = 5.5f;

        [Header("Air")]
        public float AirAccel = 25f;
        public float AirControlMaxSpeed = 9f;
        public float AirSteerRate = 120f; // deg/s the held direction bends your flight path
        public float Gravity = 16f;
        public float JumpSpeed = 9f;
        public float JumpBufferTime = 0.15f; // press-early forgiveness
        public float CoyoteTime = 0.12f;     // press-late forgiveness (also the planet-contact window)

        [Header("Planet launch")]
        public float PlanetLaunchSpeed = 26f; // kick off a planet along its surface normal

        [Header("Soft speed cap")]
        public float SoftCapSpeed = 45f;
        public float SoftCapDrag = 0.5f;

        [Header("Grapple")]
        public float GrapplePullAccel = 100f;
        public float GrappleAttachSpeed = 12f;
        public float GrappleSteerAccel = 18f;
        public float GrappleReleaseBoost = 6f;
        public float GrappleReleaseUpBoost = 2.5f;
        public float GrappleRange = 120f;
        public float AimAssistRadius = 3f;

        [Header("Zip-strike")]
        public float ZipSpeed = 60f;         // straight-line flight speed to anchor (45 felt slow vs ~80 grapple flings)
        public float ZipArriveRadius = 2f;   // detach + clamp within this distance
        public float ZipLandSpeed = 8f;      // arrival speed cap so you stick the landing
        public float ZipCooldown = 3f;       // seconds between zips

        [Header("World")]
        public float KillY = -80f;
    }

    public struct MoveInput
    {
        public Vector3 WishDir;   // world-space desired move direction, y = 0, unit length or zero
        public bool JumpPressed;  // edge-triggered (pressed this frame)
    }
}
