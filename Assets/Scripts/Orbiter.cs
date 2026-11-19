using UnityEngine;

/// <summary>
/// Moves this object in a circular orbit around a center point, on a tilted
/// orbital plane. One job only: orbital motion. Reusable for any body
/// (planets, debris, moons) in a space scene.
/// </summary>
public class Orbiter : MonoBehaviour
{
    [Tooltip("World-space point being orbited.")]
    public Vector3 center = Vector3.zero;

    [Tooltip("Orbit radius in world units.")]
    public float radius = 10f;

    [Tooltip("Orbital angular speed in degrees per second. Negative = reverse.")]
    public float angularSpeedDeg = 30f;

    [Tooltip("Tilt of the orbital plane (degrees).")]
    public float inclinationDeg = 0f;

    [Tooltip("Rotation of the tilt axis around world-up (degrees).")]
    public float ascendingNodeDeg = 0f;

    [Range(0f, 360f), Tooltip("Starting angle on the orbit (degrees).")]
    public float phaseDeg = 0f;

    private float _angle;
    private Quaternion _planeRotation;

    /// <summary>Configure the orbit from code and snap to the start position.</summary>
    public void Initialize(Vector3 center, float radius, float angularSpeedDeg,
        float inclinationDeg, float ascendingNodeDeg, float phaseDeg)
    {
        this.center = center;
        this.radius = radius;
        this.angularSpeedDeg = angularSpeedDeg;
        this.inclinationDeg = inclinationDeg;
        this.ascendingNodeDeg = ascendingNodeDeg;
        this.phaseDeg = phaseDeg;
        _angle = phaseDeg;
        CachePlaneRotation();
        ApplyPosition();
    }

    private void OnEnable() => CachePlaneRotation();

    private void OnValidate() => CachePlaneRotation();

    private void CachePlaneRotation()
    {
        // Tilt the flat XZ orbit by `inclination`, then swing that tilt around Y.
        _planeRotation = Quaternion.AngleAxis(ascendingNodeDeg, Vector3.up)
                         * Quaternion.AngleAxis(inclinationDeg, Vector3.forward);
    }

    private void Update()
    {
        _angle += angularSpeedDeg * Time.deltaTime;
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        float rad = _angle * Mathf.Deg2Rad;
        Vector3 onPlane = new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
        transform.position = center + _planeRotation * onPlane;
    }
}
