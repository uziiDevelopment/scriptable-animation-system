using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Demo.Scripts.Runtime.Item;

namespace Demo.Scripts.Runtime.Combat
{
    /// <summary>
    /// Handles hitscan weapon firing with optional network replication.
    /// Works in single-player (offline) and multiplayer modes.
    /// Attach to weapon prefab alongside Weapon component.
    /// </summary>
    public class HitscanWeapon : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private HitscanData hitscanData;

        [Header("Fire Point")]
        [Tooltip("If not assigned, will use main camera")]
        [SerializeField] private Transform firePoint;

        [Header("Effects")]
        [SerializeField] private GameObject hitEffectPrefab;
        [SerializeField] private GameObject muzzleFlashPrefab;

        [Header("Debug Visualization")]
        [SerializeField] private bool drawDebugRays;
        [SerializeField] private Color debugRayColorHit = Color.green;
        [SerializeField] private Color debugRayColorMiss = Color.red;
        [SerializeField] private Color debugRayColorHeadshot = Color.yellow;
        [SerializeField] private float debugRayLifetime = 5f;
        [Tooltip("Press this key to clear all debug rays")]
        [SerializeField] private KeyCode clearDebugRaysKey = KeyCode.C;

        // Events
        public event Action<RaycastHit> OnHit;
        public event Action OnMiss;

        private Weapon _weapon;
        private Camera _mainCamera;
        private ulong _ownerClientId;

        // Debug visualization
        private List<GameObject> _debugObjects = new List<GameObject>();
        private int _shotCounter = 0;
        private static Material _debugLineMaterial;

        private void Awake()
        {
            _weapon = GetComponent<Weapon>();
            CreateDebugMaterial();
        }

        private void CreateDebugMaterial()
        {
            if (_debugLineMaterial == null)
            {
                _debugLineMaterial = new Material(Shader.Find("Sprites/Default"));
            }
        }

        private void Update()
        {
            // Clear debug rays when key is pressed
            if (Input.GetKeyDown(clearDebugRaysKey))
            {
                ClearDebugRays();
            }
        }

        public void ClearDebugRays()
        {
            foreach (var obj in _debugObjects)
            {
                if (obj != null) Destroy(obj);
            }
            _debugObjects.Clear();
            _shotCounter = 0;
            Debug.Log("[HitscanWeapon] Debug rays cleared");
        }

        private void Start()
        {
            _mainCamera = Camera.main;

            if (_weapon != null)
            {
                _weapon.OnWeaponFired += HandleWeaponFired;
            }
        }

        private void OnDestroy()
        {
            if (_weapon != null)
            {
                _weapon.OnWeaponFired -= HandleWeaponFired;
            }
        }

        /// <summary>
        /// Set the owner's client ID for networked damage attribution
        /// </summary>
        public void SetOwnerClientId(ulong clientId)
        {
            _ownerClientId = clientId;
        }

        private void HandleWeaponFired()
        {
            Fire();
        }

        /// <summary>
        /// Perform hitscan fire. Called automatically when Weapon fires.
        /// </summary>
        public void Fire()
        {
            if (hitscanData == null)
            {
                Debug.LogWarning("HitscanWeapon: No HitscanData assigned!");
                return;
            }

            // Ensure we have camera reference
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            // Get fire origin and direction (prefer camera for FPS accuracy)
            Vector3 origin;
            Vector3 direction;

            if (_mainCamera != null)
            {
                origin = _mainCamera.transform.position;
                direction = _mainCamera.transform.forward;
            }
            else if (firePoint != null)
            {
                origin = firePoint.position;
                direction = firePoint.forward;
            }
            else
            {
                origin = transform.position;
                direction = transform.forward;
            }

            // Spawn muzzle flash locally
            SpawnMuzzleFlash();

            // Perform local raycast
            PerformHitscan(origin, direction);
        }

        private void PerformHitscan(Vector3 origin, Vector3 direction)
        {
            _shotCounter++;
            int currentShot = _shotCounter;

            int penetrationsRemaining = hitscanData.penetrationCount;
            float damageMultiplier = 1f;
            Vector3 currentOrigin = origin;

            do
            {
                if (Physics.Raycast(currentOrigin, direction, out RaycastHit hit, hitscanData.range, hitscanData.hitLayers))
                {
                    ProcessHit(hit, damageMultiplier, currentShot, currentOrigin);

                    // Setup for penetration
                    if (penetrationsRemaining > 0)
                    {
                        penetrationsRemaining--;
                        damageMultiplier *= hitscanData.penetrationDamageMultiplier;
                        currentOrigin = hit.point + direction * 0.01f; // Small offset past hit point
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    // Miss - ray went full distance without hitting anything
                    if (drawDebugRays)
                    {
                        Vector3 endPoint = currentOrigin + direction * hitscanData.range;
                        CreateDebugRay(currentOrigin, endPoint, debugRayColorMiss, currentShot, null);
                        Debug.Log($"[Shot #{currentShot}] MISS - No hit within {hitscanData.range}m range");
                    }
                    OnMiss?.Invoke();
                    break;
                }
            }
            while (penetrationsRemaining >= 0);
        }

        private void ProcessHit(RaycastHit hit, float damageMultiplier, int shotNumber, Vector3 rayOrigin)
        {
            // Check for headshot (use direct comparison to avoid errors if tag doesn't exist)
            bool isHeadshot = false;
            if (!string.IsNullOrEmpty(hitscanData.headshotTag))
            {
                try
                {
                    isHeadshot = hit.collider.CompareTag(hitscanData.headshotTag);
                }
                catch
                {
                    // Tag doesn't exist, ignore headshot detection
                }
            }

            // Calculate damage with falloff
            float distance = hit.distance;
            float damage = hitscanData.CalculateDamage(distance, isHeadshot) * damageMultiplier;

            // Debug visualization
            if (drawDebugRays)
            {
                Color rayColor = isHeadshot ? debugRayColorHeadshot : debugRayColorHit;

                // Build hit info string
                string hitObjectName = hit.collider.gameObject.name;
                string hitInfo = $"[Shot #{shotNumber}] HIT: {hitObjectName}\n" +
                                 $"  Distance: {distance:F2}m\n" +
                                 $"  Damage: {damage:F1}\n" +
                                 $"  Headshot: {isHeadshot}\n" +
                                 $"  Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}\n" +
                                 $"  Position: {hit.point}";

                var damageable = hit.collider.GetComponentInParent<Damageable>();
                if (damageable != null)
                {
                    hitInfo += $"\n  Target Health: {damageable.CurrentHealth:F1}/{damageable.MaxHealth}";
                }

                Debug.Log(hitInfo);
                CreateDebugRay(rayOrigin, hit.point, rayColor, shotNumber, hitObjectName);
            }

            // Spawn hit effect locally (for immediate feedback)
            SpawnHitEffect(hit.point, hit.normal);

            // Invoke local hit event
            OnHit?.Invoke(hit);

            // Check if we hit a damageable (get reference if not already retrieved for debug)
            var damageableTarget = hit.collider.GetComponentInParent<Damageable>();
            if (damageableTarget != null)
            {
                // Create damage info
                var damageInfo = new DamageInfo(
                    damage,
                    hit.point,
                    hit.normal,
                    _ownerClientId,
                    isHeadshot
                );

                // Check if we're in a networked game
                bool isNetworked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

                if (isNetworked)
                {
                    // Networked: send to server for validation
                    if (NetworkManager.Singleton.IsServer)
                    {
                        // We are the server, apply directly
                        damageableTarget.TakeDamage(damageInfo);
                    }
                    else
                    {
                        // We are a client, need to send RPC via a network helper
                        // For now, in client mode without server authority, just apply locally
                        // (Full implementation would use a NetworkBehaviour on the player to send RPCs)
                        damageableTarget.TakeDamage(damageInfo);
                    }
                }
                else
                {
                    // Offline / single-player: apply damage directly
                    damageableTarget.TakeDamageOffline(damageInfo);
                }
            }
        }

        private void SpawnHitEffect(Vector3 position, Vector3 normal)
        {
            if (hitEffectPrefab == null) return;

            var effect = HitEffectPool.Instance?.GetEffect(hitEffectPrefab, position, Quaternion.LookRotation(normal));
            if (effect == null)
            {
                // Fallback if no pool exists
                var instantiated = Instantiate(hitEffectPrefab, position, Quaternion.LookRotation(normal));
                Destroy(instantiated, 2f);
            }
        }

        private void SpawnMuzzleFlash()
        {
            if (muzzleFlashPrefab == null) return;

            Transform flashParent = firePoint != null ? firePoint : transform;
            var flash = Instantiate(muzzleFlashPrefab, flashParent.position, flashParent.rotation, flashParent);
            Destroy(flash, 0.1f);
        }

        private void CreateDebugRay(Vector3 start, Vector3 end, Color color, int shotNumber, string hitName)
        {
            // Create parent object for this debug ray
            var debugObj = new GameObject($"DebugRay_{shotNumber}");
            _debugObjects.Add(debugObj);

            // Create line renderer
            var lineRenderer = debugObj.AddComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
            lineRenderer.startWidth = 0.02f;
            lineRenderer.endWidth = 0.02f;
            lineRenderer.material = _debugLineMaterial;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;

            // Create sphere at hit point
            var hitMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hitMarker.name = $"HitPoint_{shotNumber}";
            hitMarker.transform.position = end;
            hitMarker.transform.localScale = Vector3.one * 0.1f;
            hitMarker.transform.SetParent(debugObj.transform);

            // Remove collider from marker
            var collider = hitMarker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // Set marker color
            var markerRenderer = hitMarker.GetComponent<Renderer>();
            markerRenderer.material = new Material(Shader.Find("Sprites/Default"));
            markerRenderer.material.color = color;

            // Create text label
            var labelObj = new GameObject($"Label_{shotNumber}");
            labelObj.transform.position = end + Vector3.up * 0.15f;
            labelObj.transform.SetParent(debugObj.transform);

            var textMesh = labelObj.AddComponent<TextMesh>();
            textMesh.text = $"#{shotNumber}\n{hitName ?? "MISS"}";
            textMesh.fontSize = 32;
            textMesh.characterSize = 0.02f;
            textMesh.anchor = TextAnchor.LowerCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = color;

            // Make text face camera
            var billboard = labelObj.AddComponent<DebugBillboard>();

            // Auto-destroy after lifetime (if lifetime > 0)
            if (debugRayLifetime > 0)
            {
                Destroy(debugObj, debugRayLifetime);
                StartCoroutine(RemoveFromListAfterDelay(debugObj, debugRayLifetime));
            }
        }

        private IEnumerator RemoveFromListAfterDelay(GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            _debugObjects.Remove(obj);
        }
    }

    /// <summary>
    /// Simple billboard component to make text always face the camera
    /// </summary>
    public class DebugBillboard : MonoBehaviour
    {
        private Camera _camera;

        private void Start()
        {
            _camera = Camera.main;
        }

        private void LateUpdate()
        {
            if (_camera != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - _camera.transform.position);
            }
        }
    }
}
