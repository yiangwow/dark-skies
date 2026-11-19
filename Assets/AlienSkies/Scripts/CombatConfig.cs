using UnityEngine;

namespace AlienSkies.Combat
{
    [CreateAssetMenu(menuName = "AlienSkies/Combat Config")]
    public class CombatConfig : ScriptableObject
    {
        public CombatSettings Settings = new CombatSettings();
    }
}
