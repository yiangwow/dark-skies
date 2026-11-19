using UnityEngine;

namespace AlienSkies
{
    /// <summary>Decorative rotation, client-local. NEVER put colliders under
    /// one of these — moving geometry would fight networked prediction.</summary>
    public sealed class SlowSpin : MonoBehaviour
    {
        public Vector3 DegreesPerSecond = new Vector3(0f, 10f, 0f);

        void Update() => transform.Rotate(DegreesPerSecond * Time.deltaTime, Space.Self);
    }
}
