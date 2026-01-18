using Unity.Netcode;
using UnityEngine;
using Demo.Scripts.Runtime.Character;

namespace Demo.Scripts.Runtime.Network
{
    /// <summary>
    /// Handles network ownership for player. Disables input on non-owned players.
    /// Add this to your player prefab alongside NetworkObject.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayer : NetworkBehaviour
    {
        [Header("Components to disable on remote players")]
        [SerializeField] private MonoBehaviour[] localOnlyComponents;

        [Header("GameObjects to disable on remote players")]
        [SerializeField] private GameObject[] localOnlyObjects;

        private FPSController _fpsController;
        private FPSMovement _fpsMovement;
        private Camera _playerCamera;
        private AudioListener _audioListener;

        private void Awake()
        {
            // Cache common components
            _fpsController = GetComponent<FPSController>();
            _fpsMovement = GetComponent<FPSMovement>();
            _playerCamera = GetComponentInChildren<Camera>();
            _audioListener = GetComponentInChildren<AudioListener>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsOwner)
            {
                // This is our local player - enable everything
                EnableLocalPlayer();
                Debug.Log($"[NetworkPlayer] Local player spawned (Client {OwnerClientId})");
            }
            else
            {
                // This is a remote player - disable input/camera
                DisableRemotePlayer();
                Debug.Log($"[NetworkPlayer] Remote player spawned (Client {OwnerClientId})");
            }
        }

        private void EnableLocalPlayer()
        {
            // Enable controller components
            if (_fpsController != null) _fpsController.enabled = true;
            if (_fpsMovement != null) _fpsMovement.enabled = true;

            // Enable camera and audio
            if (_playerCamera != null) _playerCamera.enabled = true;
            if (_audioListener != null) _audioListener.enabled = true;

            // Enable any custom local-only components
            foreach (var comp in localOnlyComponents)
            {
                if (comp != null) comp.enabled = true;
            }

            // Enable any custom local-only objects
            foreach (var obj in localOnlyObjects)
            {
                if (obj != null) obj.SetActive(true);
            }
        }

        private void DisableRemotePlayer()
        {
            // Disable controller components (no input for remote players)
            if (_fpsController != null) _fpsController.enabled = false;
            if (_fpsMovement != null) _fpsMovement.enabled = false;

            // Disable camera and audio (only local player should have active camera)
            if (_playerCamera != null) _playerCamera.enabled = false;
            if (_audioListener != null) _audioListener.enabled = false;

            // Disable any custom local-only components
            foreach (var comp in localOnlyComponents)
            {
                if (comp != null) comp.enabled = false;
            }

            // Disable any custom local-only objects
            foreach (var obj in localOnlyObjects)
            {
                if (obj != null) obj.SetActive(false);
            }
        }
    }
}
