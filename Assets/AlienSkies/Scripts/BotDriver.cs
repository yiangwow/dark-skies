using UnityEngine;
using AlienSkies.Movement;
using AlienSkies.Combat;

namespace AlienSkies.Bots
{
    /// <summary>Bot driver on a spawned player prefab instance. A plain
    /// MonoBehaviour added by BotSpawner right after Instantiate; input is
    /// produced inside the motor's Update via ExternalInput, so bots and the
    /// human run the exact same simulation path.</summary>
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class BotDriver : MonoBehaviour
    {
        PlayerMotor _motor;
        PlayerCombat _combat;
        CharacterController _cc;
        BotBrain _brain;
        PlayerCombat _target;
        float _retargetTimer;
        Vector3 _lastWish; // previous frame's wish dir, for the edge probe
        static int _nextSeed = 1337; // distinct behavior per spawned bot

        /// <summary>Guardrail 5 (restart-instant-deterministic): EnemySpawner
        /// resets this at every Repopulate so each run's spawn sequence gets
        /// the SAME seeds — bot cadence/aim-error/orbit RNG must not diverge
        /// between otherwise identical runs (bests are beatable by execution
        /// alone). Practice-bot spawns between runs can't shift it.</summary>
        public static void ResetSeedCounter() => _nextSeed = 1337;

        // Telegraph beam: on a CHILD GameObject — PlayerMotor's rope already
        // occupies the root's single Renderer slot, so a second LineRenderer
        // added to the root would return null. Not parented under
        // CameraPivot: that transform is inactive on bots.
        LineRenderer _telegraphLine;
        Material _telegraphMaterial;
        BotBrain.Intent _lastIntent;
        int _lastIntentFrame = -1;

        void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _combat = GetComponent<PlayerCombat>();
            _cc = GetComponent<CharacterController>();
            _brain = new BotBrain(_nextSeed++) { Stationary = true };
            _motor.ExternalInput = BuildInput;

            var telegraphGO = new GameObject("TelegraphLine");
            telegraphGO.transform.SetParent(transform, false);
            _telegraphLine = telegraphGO.AddComponent<LineRenderer>();
            _telegraphLine.positionCount = 2;
            _telegraphLine.startWidth = 0.06f;
            _telegraphLine.endWidth = 0.02f;
            // HDR unlit so bloom reads the warning line clearly at speed.
            _telegraphMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _telegraphMaterial.SetColor("_BaseColor", new Color(1f, 0f, 1f) * 3f);
            _telegraphLine.material = _telegraphMaterial;
            _telegraphLine.enabled = false;
        }

        void OnDestroy()
        {
            if (_telegraphMaterial != null) Destroy(_telegraphMaterial);
        }

        /// <summary>Spawners call this right after AddComponent: Awake has
        /// already built the brain, so mutate it directly.</summary>
        public void SetStationary(bool stationary) => _brain.Stationary = stationary;
        public void SetOrbitRadius(float r) => _brain.OrbitRadius = r;
        public void SetProjectileSpeed(float s) => _brain.ProjectileSpeed = s;

        void LateUpdate()
        {
            // Force OFF whenever dead or the input path went quiet (BuildInput
            // stops running the frame the drone dies) — otherwise a gate inside
            // BuildInput would freeze the beam ON through the corpse linger.
            bool recent = _lastIntentFrame == Time.frameCount || _lastIntentFrame == Time.frameCount - 1;
            _telegraphLine.enabled = recent && _lastIntent.Telegraphing && !_combat.IsDead;
        }

        PlayerMotor.InputFrame BuildInput()
        {
            float dt = Time.deltaTime;
            RefreshTarget(dt);

            // Edge probe: cast along LAST frame's wish dir (one frame stale —
            // irrelevant at bot speeds) so EdgeAhead reflects the ground the
            // bot is about to walk onto, not the ground under it right now.
            bool edge = false;
            if (_cc.isGrounded && _lastWish.sqrMagnitude > 0.01f)
            {
                Vector3 probe = transform.position + _lastWish.normalized * 1.6f + Vector3.up * 0.5f;
                // queriesHitTriggers is ON project-wide — ignore trigger volumes
                // so no gate or future trigger can ever read as floor.
                // DefaultRaycastLayers also hits other characters' capsules, so
                // a bot standing at the probe point can misread as floor for a
                // frame; self-limiting (that capsule also blocks the walk, and
                // the next clear frame re-detects the edge) — add a
                // character-excluding layer mask here if it ever plays badly.
                edge = !Physics.Raycast(probe, Vector3.down, 5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            }

            var sense = new BotBrain.Sense
            {
                SelfPos = _motor.CameraPivot.position,
                HasTarget = _target != null,
                Grounded = _cc.isGrounded,
                EdgeAhead = edge,
            };
            if (_target != null)
            {
                var targetMotor = _target.GetComponent<PlayerMotor>();
                sense.TargetPos = targetMotor.CameraPivot.position;
                sense.TargetVelocity = targetMotor != null ? targetMotor.Core.Velocity : Vector3.zero;
                // Eye-to-eye: rays starting inside our own capsule don't hit it.
                sense.TargetVisible =
                    !Physics.Linecast(sense.SelfPos, sense.TargetPos, out var hit)
                    || hit.collider.GetComponentInParent<PlayerCombat>() == _target;
            }

            var intent = _brain.Tick(in sense, dt);

            if (intent.Fire && !_combat.IsDead)
                _combat.FireRocket(sense.SelfPos + intent.AimDir * 1.2f, intent.AimDir);

            // Cache for LateUpdate to gate the telegraph beam's visibility;
            // update its endpoints now while sense/intent are in scope.
            _lastIntent = intent;
            _lastIntentFrame = Time.frameCount;
            if (intent.Telegraphing)
            {
                float beamLen = _target != null
                    ? Mathf.Min(Vector3.Distance(sense.SelfPos, sense.TargetPos), 60f)
                    : 60f;
                _telegraphLine.SetPosition(0, sense.SelfPos);
                _telegraphLine.SetPosition(1, sense.SelfPos + intent.TelegraphAim * beamLen);
            }

            _lastWish = intent.WishDir;

            var oneTime = new PlayerMotor.OneTimeInput { JumpPressed = intent.Jump };
            return new PlayerMotor.InputFrame(intent.WishDir, intent.Yaw, oneTime);
        }

        void RefreshTarget(float dt)
        {
            _retargetTimer -= dt;
            if (_retargetTimer > 0f && _target != null && !_target.IsDead) return;
            _retargetTimer = 1f;
            _target = null;
            float best = float.MaxValue;
            foreach (var pc in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            {
                if (pc == _combat || pc.IsDead) continue;
                if (pc.GetComponent<BotDriver>() != null) continue; // humans only
                float d = (pc.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; _target = pc; }
            }
        }
    }
}
