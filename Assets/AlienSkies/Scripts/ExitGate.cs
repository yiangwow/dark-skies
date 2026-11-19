using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Run
{
    /// <summary>Touch to finish the run. The builder gives this a trigger
    /// sphere inside a neon ring; only the human player completes runs.</summary>
    [RequireComponent(typeof(SphereCollider))]
    public sealed class ExitGate : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            var motor = other.GetComponentInParent<PlayerMotor>();
            if (motor == null || !motor.IsHuman) return;
            if (RunManager.Instance != null) RunManager.Instance.CompleteRun();
        }
    }
}
