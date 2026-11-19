using UnityEngine;

namespace AlienSkies.Movement
{
    [CreateAssetMenu(menuName = "AlienSkies/Movement Config", fileName = "MovementConfig")]
    public sealed class MovementConfig : ScriptableObject
    {
        public MovementSettings Settings = new MovementSettings();
    }
}
