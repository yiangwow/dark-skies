using UnityEngine;

namespace AlienSkies.Movement
{
    /// <summary>Finds a grapple anchor along the look ray: exact raycast first,
    /// then a sphere cast as aim assist. Skips the player's own colliders.</summary>
    public static class GrappleAimer
    {
        // Matches MovementCore's 1m arrival radius: closer anchors would be pull
        // no-ops anyway. Also guards spherecast self-overlap (distance 0) hits.
        const float MinGrappleDistance = 1f;

        static readonly RaycastHit[] Hits = new RaycastHit[64];

        public static bool TryFindAnchor(Vector3 origin, Vector3 dir, MovementSettings s,
                                         Transform self, out Vector3 anchor)
        {
            anchor = default;
            var ray = new Ray(origin, dir);
            if (Physics.Raycast(ray, out var hit, s.GrappleRange)
                && hit.transform != self && !hit.transform.IsChildOf(self)
                && hit.distance >= MinGrappleDistance)
            {
                anchor = hit.point;
                return true;
            }
            return TrySphereCast(ray, s.AimAssistRadius, s.GrappleRange, self, out anchor);
        }

        static bool TrySphereCast(Ray ray, float radius, float range, Transform self, out Vector3 anchor)
        {
            anchor = default;
            int n = Physics.SphereCastNonAlloc(ray, radius, Hits, range);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.transform == self || h.transform.IsChildOf(self)) continue;
                if (h.distance < MinGrappleDistance) continue;
                if (h.distance < bestDist) { bestDist = h.distance; anchor = h.point; found = true; }
            }
            return found;
        }
    }
}
