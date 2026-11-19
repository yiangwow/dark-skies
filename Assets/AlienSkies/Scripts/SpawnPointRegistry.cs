using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Scene-placed respawn points (wired by PlaygroundBuilder). Static
    /// access because respawns are decided by whichever PlayerCombat is serving.</summary>
    public sealed class SpawnPointRegistry : MonoBehaviour
    {
        public Transform[] Points = new Transform[0];

        static SpawnPointRegistry _instance;

        void Awake() => _instance = this;

        public static Vector3 Farthest(Vector3 from)
        {
            if (_instance == null || _instance.Points.Length == 0)
                return new Vector3(0f, 3f, 0f);
            Transform best = _instance.Points[0];
            float bestSqr = -1f;
            foreach (var t in _instance.Points)
            {
                float d = (t.position - from).sqrMagnitude;
                if (d > bestSqr) { bestSqr = d; best = t; }
            }
            return best.position;
        }
    }
}
