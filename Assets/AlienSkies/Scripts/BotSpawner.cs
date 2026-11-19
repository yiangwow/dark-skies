using UnityEngine;
using UnityEngine.InputSystem;

namespace AlienSkies.Bots
{
    /// <summary>Practice bot toggle: press B to spawn (or despawn) one bot.
    /// Uses the regular player prefab with a BotDriver added right after
    /// Instantiate — before the motor's first Update decides human-vs-bot.</summary>
    public sealed class BotSpawner : MonoBehaviour
    {
        public GameObject PlayerPrefab;

        GameObject _bot;

        void Start()
        {
            if (AlienSkies.Run.RunManager.Instance != null)
                AlienSkies.Run.RunManager.Instance.RunStarted += DespawnBot;
        }

        void OnDestroy()
        {
            if (AlienSkies.Run.RunManager.Instance != null)
                AlienSkies.Run.RunManager.Instance.RunStarted -= DespawnBot;
        }

        void Update()
        {
            if (PlayerPrefab == null) return;
            if (Cursor.lockState != CursorLockMode.Locked) return; // pause overlay's own advertised key must not spawn a bot behind it
            if (AlienSkies.Run.RunManager.Instance != null && AlienSkies.Run.RunManager.Instance.Core.State == AlienSkies.Run.RunState.Running) return; // practice only at the start line
            var kb = Keyboard.current;
            if (kb == null || !kb.bKey.wasPressedThisFrame) return;

            if (_bot != null)
            {
                DespawnBot();
                return;
            }

            _bot = Instantiate(PlayerPrefab,
                AlienSkies.Combat.SpawnPointRegistry.Farthest(Vector3.zero),
                Quaternion.identity);
            var driver = _bot.AddComponent<BotDriver>();
            // The practice bot fires the PLAYER config's rockets — lead with
            // their REAL speed (knob==real, the enemy-threat lesson; the 200
            // m/s cannonball tune desynced the brain's 150 default).
            driver.SetProjectileSpeed(_bot.GetComponent<AlienSkies.Combat.PlayerCombat>().RocketSpeed);
        }

        void DespawnBot()
        {
            if (_bot != null) { Destroy(_bot); _bot = null; }
        }
    }
}
