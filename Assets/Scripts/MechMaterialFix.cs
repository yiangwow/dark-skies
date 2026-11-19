using UnityEngine;

/// <summary>
/// Replaces a model's imported (magenta / non-URP) materials with a single
/// URP/Lit metallic material, so meshes brought in from glTF/FBX render
/// correctly in the Universal Render Pipeline.
///
/// Usage: add to the root of an imported model (e.g. the mech). Runs in the
/// Editor too, so the magenta vanishes immediately. Tweak colour/metallic
/// /smoothness in the Inspector.
/// </summary>
[ExecuteAlways]
public class MechMaterialFix : MonoBehaviour
{
    [Tooltip("Base colour of the mech.")]
    public Color color = new Color(0.55f, 0.57f, 0.62f);

    [Range(0f, 1f)] public float metallic = 0.8f;
    [Range(0f, 1f)] public float smoothness = 0.55f;

    private Material _material;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    private void Apply()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) return;

        if (_material == null)
            _material = new Material(urpLit) { name = "MechMaterial (runtime)" };

        _material.SetColor("_BaseColor", color);
        _material.SetFloat("_Metallic", metallic);
        _material.SetFloat("_Smoothness", smoothness);

        // Point every sub-mesh slot on every child renderer at our material.
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            var slots = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < slots.Length; i++) slots[i] = _material;
            r.sharedMaterials = slots;
        }
    }
}
