using UnityEngine;
using AlienSkies.Movement;

namespace AlienSkies.Combat
{
    /// <summary>Owner-only first-person weapon rig. Static models get procedural
    /// life: fire kick, speed bob, and momentum lurch driven by MovementCore
    /// velocity — the gun physically reacts to zips and grapple flings.</summary>
    public sealed class ViewmodelRig : MonoBehaviour
    {
        public PlayerMotor Motor;
        public PlayerCombat Combat;
        public GameObject[] WeaponModels = new GameObject[0];

        Vector3 _basePos;
        Vector3 _kick;
        float _bobT;

        void Awake() => _basePos = transform.localPosition;

        void OnEnable()
        {
            if (Combat == null) return;
            Combat.OnFired += HandleFired;
            Combat.OnWeaponChanged += ShowWeapon;
            ShowWeapon(Combat.CurrentWeapon);
        }

        void OnDisable()
        {
            if (Combat == null) return;
            Combat.OnFired -= HandleFired;
            Combat.OnWeaponChanged -= ShowWeapon;
        }

        void HandleFired(int weapon) =>
            _kick += new Vector3(0f, 0.02f, -(weapon == 0 ? 0.22f : weapon == 2 ? 0.16f : 0.07f));

        void ShowWeapon(int index)
        {
            for (int i = 0; i < WeaponModels.Length; i++)
                if (WeaponModels[i] != null) WeaponModels[i].SetActive(i == index);
        }

        void LateUpdate()
        {
            var v = Motor != null ? Motor.Core.Velocity : Vector3.zero;
            float speed = new Vector3(v.x, 0f, v.z).magnitude;

            _bobT += Time.deltaTime * Mathf.Lerp(2f, 15f, Mathf.Clamp01(speed / 30f));
            Vector3 bob = new Vector3(Mathf.Sin(_bobT) * 0.012f,
                                      Mathf.Abs(Mathf.Cos(_bobT)) * 0.009f, 0f)
                          * Mathf.Clamp01(speed / 8f);

            // Momentum lurch: gun drags opposite to velocity (in camera space).
            Vector3 lurch = transform.parent.InverseTransformDirection(-v) * 0.0035f;
            lurch = Vector3.ClampMagnitude(lurch, 0.1f);

            _kick = Vector3.Lerp(_kick, Vector3.zero, Time.deltaTime * 9f);
            transform.localPosition = _basePos + bob + lurch + _kick;
        }
    }
}
