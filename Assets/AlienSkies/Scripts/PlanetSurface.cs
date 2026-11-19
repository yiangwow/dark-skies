using UnityEngine;

namespace AlienSkies.Movement
{
    /// <summary>Marker: this collider is a planet — touching it arms the jump-key
    /// launch (kick off along the surface normal). Builder adds it to every
    /// planet sphere's collider object; platforms deliberately don't get it.</summary>
    public sealed class PlanetSurface : MonoBehaviour { }
}
