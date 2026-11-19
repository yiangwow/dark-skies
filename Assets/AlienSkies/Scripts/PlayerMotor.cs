using UnityEngine;
using UnityEngine.InputSystem;

namespace AlienSkies.Movement
{
    /// <summary>The player motor. Input is read and simulated in the same
    /// Update frame with MovementCore — one code path for the human (device
    /// input) and bots (ExternalInput). De-networked descendant of the
    /// prediction-based NetworkPlayerMotor; the Graphical child remains (rig,
    /// rope, body hang off it) but no longer detaches — per-frame simulation
    /// needs no tick smoothing.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        public struct OneTimeInput
        {
            public bool JumpPressed;
            public bool GrappleStarted;
            public Vector3 GrappleAnchor;
            public bool GrappleStopped;
            public bool ZipPressed;

            public void ResetState()
            {
                JumpPressed = false;
                GrappleStarted = false;
                GrappleAnchor = default;
                GrappleStopped = false;
                ZipPressed = false;
            }
        }

        public struct InputFrame
        {
            public Vector3 WishDir; // world-space
            public float Yaw;       // body yaw in degrees
            public OneTimeInput OneTime;

            public InputFrame(Vector3 wishDir, float yaw, OneTimeInput oneTime)
            {
                WishDir = wishDir;
                Yaw = yaw;
                OneTime = oneTime;
            }
        }

        static readonly Vector3 SpawnPoint = new Vector3(0f, 3f, 0f);

        public MovementConfig Config;
        public Transform CameraPivot;
        public Transform Graphical;     // visual child; owns visual yaw
        public Renderer BodyRenderer;
        public bool InputsEnabled = true;   // PlayerCombat gates this while dead
        public event System.Action KillPlaneCrossed; // once per fall
        [SerializeField] float _mouseSensitivity = 0.08f;

        public MovementCore Core { get; private set; }
        public bool HasGrappleTarget { get; private set; }

        /// <summary>Input source for AI-driven players (practice bot). When
        /// set, the frame's input comes from here instead of devices — and this
        /// motor is not the human player (see IsHuman).</summary>
        public System.Func<InputFrame> ExternalInput;

        public bool IsHuman => ExternalInput == null;

        CharacterController _cc;
        LineRenderer _rope;
        Material _ropeMaterial;
        float _pitch;
        OneTimeInput _oneTime;
        bool _initialized;

        MovementSettings S => Config != null ? Config.Settings : _fallback;
        readonly MovementSettings _fallback = new MovementSettings();

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            Core = new MovementCore(S);
            BuildRope();
        }

        void BuildRope()
        {
            _rope = gameObject.AddComponent<LineRenderer>();
            _rope.positionCount = 2;
            _rope.startWidth = 0.08f;
            _rope.endWidth = 0.03f;
            // HDR unlit so bloom makes the rope read as an energy line
            _ropeMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _ropeMaterial.SetColor("_BaseColor", new Color(0.29f, 1f, 0.69f) * 2.6f);
            _rope.material = _ropeMaterial;
            _rope.enabled = false;
        }

        void OnDestroy()
        {
            if (_ropeMaterial != null) Destroy(_ropeMaterial);
        }

        // Human-vs-bot is decided by ExternalInput, which BotDriver assigns in
        // its Awake — after this component's Awake (AddComponent order in
        // BotSpawner) but before the first Update. So one-time setup runs
        // lazily on the first frame, not in Start (same-frame race).
        void Initialize()
        {
            _initialized = true;
            CameraPivot.gameObject.SetActive(IsHuman);
            if (IsHuman)
            {
                // Exactly one AudioListener: ours.
                var mine = CameraPivot.GetComponentInChildren<AudioListener>(true);
                foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (l != mine) l.enabled = false;
                Cursor.lockState = CursorLockMode.Locked;
                // The human shouldn't see the inside of their own body capsule.
                if (BodyRenderer != null) BodyRenderer.enabled = false;
            }
            var hud = GetComponent<MovementHUD>();
            if (hud != null) hud.enabled = IsHuman;
        }

        void Update()
        {
            if (!_initialized) Initialize();

            ReadDeviceInput();
            Simulate(BuildInput(), Time.deltaTime);

            // Rope renders from simulation state; anchor at the visual body.
            _rope.enabled = Core.IsGrappling;
            if (Core.IsGrappling)
            {
                var visualPos = Graphical != null ? Graphical.position : transform.position;
                _rope.SetPosition(0, visualPos + Vector3.down * 0.3f);
                _rope.SetPosition(1, Core.GrappleAnchor);
            }
        }

        void ReadDeviceInput()
        {
            if (!IsHuman) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;

            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;

            if (cursorLocked)
            {
                // Yaw rotates the Graphical; the sim root follows in Simulate
                // (kept split so bots can drive yaw through InputFrame too).
                Vector2 look = mouse.delta.ReadValue() * _mouseSensitivity;
                Graphical.Rotate(0f, look.x, 0f);
                _pitch = Mathf.Clamp(_pitch - look.y, -89f, 89f);
                CameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }

            // One-time inputs, consumed in BuildInput. Gated while dead —
            // corpses can look around but not act.
            if (!InputsEnabled) return;
            Vector3 anchor = default;
            HasGrappleTarget = cursorLocked && GrappleAimer.TryFindAnchor(
                CameraPivot.position, CameraPivot.forward, S, transform, out anchor);
            if (cursorLocked && kb.fKey.wasPressedThisFrame && HasGrappleTarget)
            {
                _oneTime.GrappleStarted = true;
                _oneTime.GrappleAnchor = anchor;
            }
            if (!cursorLocked || kb.fKey.wasReleasedThisFrame)
                _oneTime.GrappleStopped = true;
            if (cursorLocked && mouse.rightButton.wasPressedThisFrame)
                _oneTime.ZipPressed = true;
            if (cursorLocked && kb.spaceKey.wasPressedThisFrame)
                _oneTime.JumpPressed = true;
        }

        InputFrame BuildInput()
        {
            if (ExternalInput != null)
                return InputsEnabled ? ExternalInput() : default;
            if (!InputsEnabled) return new InputFrame(Vector3.zero, Graphical.eulerAngles.y, default);

            var kb = Keyboard.current;
            Vector3 wish = Vector3.zero;
            if (kb != null && Cursor.lockState == CursorLockMode.Locked)
            {
                float f = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                float r = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                wish = Graphical.forward * f + Graphical.right * r; // aim frame = visual frame
                wish.y = 0f;
                if (wish.sqrMagnitude > 1e-4f) wish.Normalize();
            }

            var frame = new InputFrame(wish, Graphical.eulerAngles.y, _oneTime);
            _oneTime.ResetState();
            return frame;
        }

        void Simulate(InputFrame frame, float delta)
        {
            // Sim root follows the input yaw (human: from Graphical; bot: from
            // BotBrain intent). The Graphical is then re-aligned to the root
            // EVERY frame, for humans too: it is an attached child now (the
            // old code detached it via the tick smoother), so leaving its
            // accumulated local yaw in place would compound with the root's
            // new world yaw next frame — a positive feedback loop that spins
            // the camera forever after one mouse movement. Re-aligning zeroes
            // the local yaw each frame; pitch is untouched (it lives on
            // CameraPivot).
            transform.rotation = Quaternion.Euler(0f, frame.Yaw, 0f);
            if (Graphical != null)
                Graphical.rotation = transform.rotation;

            if (frame.OneTime.GrappleStarted) Core.StartGrapple(frame.OneTime.GrappleAnchor);
            if (frame.OneTime.GrappleStopped) Core.StopGrapple();
            if (frame.OneTime.ZipPressed) Core.StartZip();

            var input = new MoveInput { WishDir = frame.WishDir, JumpPressed = frame.OneTime.JumpPressed };
            Core.Tick(input, transform.position, _cc.isGrounded, delta);
            _cc.Move(Core.Velocity * delta);

            if (transform.position.y < S.KillY)
            {
                KillPlaneCrossed?.Invoke();
                Teleport(SpawnPoint);
            }
        }

        /// <summary>Inject external velocity (rocket knockback).</summary>
        public void ApplyImpulse(Vector3 impulse) => Core.SetVelocity(Core.Velocity + impulse);

        /// <summary>Respawn teleport: move and reset all movement state.</summary>
        public void RespawnAt(Vector3 position)
        {
            Teleport(position);
            Core.Reset();
        }

        void Teleport(Vector3 position)
        {
            // CharacterController must be disabled around direct position writes or
            // its internal physics state stays at the old position until next simulate.
            _cc.enabled = false;
            transform.position = position;
            _cc.enabled = true;
            if (position == SpawnPoint) Core.Reset();
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Core.ClampAgainstSurface(hit.normal);
            // Planets arm the jump-key kick (vertical recovery); the marker keeps
            // platforms out of it.
            if (hit.collider.GetComponent<PlanetSurface>() != null)
                Core.RegisterSurfaceContact(hit.normal);
        }
    }
}
