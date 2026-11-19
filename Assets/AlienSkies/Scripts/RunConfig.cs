using UnityEngine;

namespace AlienSkies.Run
{
    [CreateAssetMenu(menuName = "AlienSkies/Run Config")]
    public sealed class RunConfig : ScriptableObject
    {
        public RunSettings Settings = new RunSettings();
    }
}
