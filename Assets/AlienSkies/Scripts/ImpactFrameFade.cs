using UnityEngine;

namespace AlienSkies.Combat
{
    /// <summary>Drives the ImpactFrame shader's _Progress 0->1 over its life,
    /// then disables the renderer (the spawner's Destroy handles cleanup).</summary>
    public sealed class ImpactFrameFade : MonoBehaviour
    {
        public float Life = 0.4f;

        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        Renderer _renderer;
        Material _mat; // instance, so overlapping kills don't share progress
        float _t;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mat = _renderer.material;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float p = _t / Life;
            if (p >= 1f) { _renderer.enabled = false; return; }
            _mat.SetFloat(ProgressId, p);
        }

        void OnDestroy() { if (_mat != null) Destroy(_mat); }
    }
}
