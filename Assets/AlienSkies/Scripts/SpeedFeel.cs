using UnityEngine;

namespace AlienSkies.Movement
{
    /// <summary>Camera-side presentation for speed: FOV kick, zip burst, strafe
    /// roll, and a speed-line overlay quad. Rolls RollTarget, NOT Cam.transform
    /// -- CombatFeedback owns the camera GO's local pose (re-asserts identity
    /// every frame outside shake) and would stomp a roll written there. All
    /// fields are wired by the builder at integration; every use is
    /// null-guarded so this degrades to a no-op with incomplete wiring.</summary>
    public sealed class SpeedFeel : MonoBehaviour
    {
        public PlayerMotor Motor;
        public Camera Cam;
        public Transform RollTarget;   // pivot between CameraPivot and MainCamera
        public Material SpeedLinesMaterial;

        public SpeedFeelCore Core { get; } = new SpeedFeelCore();

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int InnerRadiusId = Shader.PropertyToID("_InnerRadius");

        Renderer _quadRenderer;
        Material _mat;   // per-instance copy, so per-frame SetFloat never dirties the asset
        bool _wasZipping;

        void Awake()
        {
            if (SpeedLinesMaterial != null)
                BuildSpeedLineQuad();
        }

        void BuildSpeedLineQuad()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SpeedLines";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 2f);
            quad.transform.localScale = new Vector3(4.2f, 2.4f, 1f);

            _quadRenderer = quad.GetComponent<Renderer>();
            _quadRenderer.material = SpeedLinesMaterial;
            _mat = _quadRenderer.material; // instance copy (ImpactFrameFade pattern)
        }

        void Update()
        {
            if (Motor == null) return;
            float dt = Time.deltaTime;

            var v = Motor.Core.Velocity;
            float speed = v.magnitude;

            bool isZipping = Motor.Core.IsZipping;
            if (isZipping && !_wasZipping) Core.RegisterZip();
            _wasZipping = isZipping;

            Core.Tick(dt, speed);

            if (Cam != null)
            {
                // Settings scale the WIDENING DELTA only (guardrail 3), not
                // SpeedFeelCore's pure FOV math — base FOV always stays put
                // even with the setting at its floor.
                float delta = Core.FovFor(speed) - SpeedFeelCore.BaseFov;
                float targetFov = SpeedFeelCore.BaseFov + delta * FeelSettings.FovScale;
                Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, targetFov, 8f * dt);
            }

            if (RollTarget != null && Motor.Graphical != null)
            {
                float lateral = Vector3.Dot(v, Motor.Graphical.right);
                RollTarget.localRotation = Quaternion.Euler(0f, 0f, Core.RollFor(lateral));
            }

            if (_mat != null)
            {
                float intensity = Core.LineIntensityFor(speed);
                _mat.SetFloat(IntensityId, intensity);
                // Accel-driven annulus (guardrail 3): gaining speed pulls the
                // inner edge inward; braking leaves it at the resting radius.
                _mat.SetFloat(InnerRadiusId, Core.InnerRadiusFor());
                _quadRenderer.enabled = intensity > 0f;
            }
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
