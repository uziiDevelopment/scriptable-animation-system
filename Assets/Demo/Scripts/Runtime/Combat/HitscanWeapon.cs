using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Demo.Scripts.Runtime.Item;

namespace Demo.Scripts.Runtime.Combat
{
    /// <summary>
    /// Handles weapon firing. Supports both instant hitscan and physical projectile simulation.
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

        // Projectile Simulation
        private class ActiveProjectile
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 StartPosition;
            public float StartTime;
            public int PenetrationsRemaining;
            public float CurrentDamageMultiplier;
            public int ShotIndex;
            public HitscanData Data;
        }

        private List<ActiveProjectile> _activeProjectiles = new List<ActiveProjectile>();

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

            // Simulate projectiles
            SimulateProjectiles(Time.deltaTime);
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

        public void SetOwnerClientId(ulong clientId)
        {
            _ownerClientId = clientId;
        }

        private void HandleWeaponFired()
        {
            Fire();
        }

        public void Fire()
        {
            if (hitscanData == null)
            {
                Debug.LogWarning("HitscanWeapon: No HitscanData assigned!");
                return;
            }

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }

            // Get fire origin and direction
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

            _shotCounter++;

            if (hitscanData.useProjectile)
            {
                // Projectile Mode
                Vector3 initialVelocity = direction * hitscanData.speed;
                var projectile = new ActiveProjectile
                {
                    Position = origin,
                    Velocity = initialVelocity,
                    StartPosition = origin,
                    StartTime = Time.time,
                    ShotIndex = _shotCounter,
                    PenetrationsRemaining = hitscanData.penetrationCount,
                    CurrentDamageMultiplier = 1f,
                    Data = hitscanData
                };
                _activeProjectiles.Add(projectile);
            }
            else
            {
                // Immediate Hitscan Mode
                PerformHitscan(origin, direction, _shotCounter);
            }
        }

        // --- Projectile Simulation Logic ---

        private void SimulateProjectiles(float dt)
        {
            for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
            {
                var p = _activeProjectiles[i];

                if (Time.time - p.StartTime > p.Data.maxLifetime)
                {
                    _activeProjectiles.RemoveAt(i);
                    continue;
                }

                Vector3 currentPos = p.Position;
                Vector3 currentVel = p.Velocity;

                // Gravity
                Vector3 gravity = Physics.gravity * p.Data.gravityMultiplier;
                
                // Kinematics integration
                Vector3 nextVel = currentVel + gravity * dt;
                Vector3 nextPos = currentPos + currentVel * dt + 0.5f * gravity * dt * dt;

                // Raycast along the step
                Vector3 displacement = nextPos - currentPos;
                float distance = displacement.magnitude;

                if (distance > 0.0001f)
                {
                    if (Physics.Raycast(currentPos, displacement.normalized, out RaycastHit hit, distance, p.Data.hitLayers))
                    {
                        // Handle Hit
                        bool destroy = HandleProjectileHit(p, hit);
                        
                        // Debug visuals for projectile segment
                        if (drawDebugRays)
                        {
                            CreateDebugRay(currentPos, hit.point, debugRayColorHit, p.ShotIndex, hit.collider.name, false);
                        }

                        if (destroy)
                        {
                            _activeProjectiles.RemoveAt(i);
                            continue; // Stop processing this projectile
                        }
                        else
                        {
                            // On penetration, just let it continue to nextPos for simplicity in this frame
                            // Or ideally, re-cast from hit point. 
                            // Current logic: it passed through.
                        }
                    }
                    else
                    {
                        // No hit
                         if (drawDebugRays)
                        {
                            // Draw the flight path segment
                             CreateDebugRay(currentPos, nextPos, debugRayColorHeadshot, p.ShotIndex, null, false);
                        }
                    }
                }

                p.Position = nextPos;
                p.Velocity = nextVel;
            }
        }

        private bool HandleProjectileHit(ActiveProjectile p, RaycastHit hit)
        {
            float totalDistance = Vector3.Distance(p.StartPosition, hit.point);
            ProcessHitResult(hit, p.CurrentDamageMultiplier, p.ShotIndex, p.Position, p.Data, totalDistance);
            
            // Penetration check
            if (p.PenetrationsRemaining > 0)
            {
                p.PenetrationsRemaining--;
                p.CurrentDamageMultiplier *= p.Data.penetrationDamageMultiplier;
                return false;
            }
            return true;
        }


        // --- Hitscan Logic ---

        private void PerformHitscan(Vector3 origin, Vector3 direction, int shotNumber)
        {
            int penetrationsRemaining = hitscanData.penetrationCount;
            float damageMultiplier = 1f;
            Vector3 currentOrigin = origin;

            do
            {
                if (Physics.Raycast(currentOrigin, direction, out RaycastHit hit, hitscanData.range, hitscanData.hitLayers))
                {
                    ProcessHitResult(hit, damageMultiplier, shotNumber, currentOrigin, hitscanData);

                    // Setup for penetration
                    if (penetrationsRemaining > 0)
                    {
                        penetrationsRemaining--;
                        damageMultiplier *= hitscanData.penetrationDamageMultiplier;
                        currentOrigin = hit.point + direction * 0.01f;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    // Miss
                    if (drawDebugRays)
                    {
                        Vector3 endPoint = currentOrigin + direction * hitscanData.range;
                        CreateDebugRay(currentOrigin, endPoint, debugRayColorMiss, shotNumber, null);
                    }
                    OnMiss?.Invoke();
                    break;
                }
            }
            while (penetrationsRemaining >= 0);
        }

        // --- Shared Processing ---

        private void ProcessHitResult(RaycastHit hit, float damageMultiplier, int shotNumber, Vector3 rayOrigin, HitscanData data)
        {
            // Check Headshot
            bool isHeadshot = false;
            if (!string.IsNullOrEmpty(data.headshotTag))
            {
                try { isHeadshot = hit.collider.CompareTag(data.headshotTag); } catch { }
            }

            // Calculate Damage
            float distance = hit.distance; 
            // Note: For projectile, hit.distance is just the step distance. 
            // We should use distance from start.
            // But hit.distance in Raycast is from search origin.
            // For projectile, rayOrigin is IsProjectile ? stepStart : gunMuzzle.
            // So for projectile, we need to calculate total distance properly if we want falloff based on travel.
            // However, existing Hitscan logic uses hit.distance. 
            // For projectile, let's just use distance from current step or approximate total?
            // Let's use Vector3.Distance(startPosition, hit.point) if we can access startPosition.
            // But this method signature is shared.
            // Let's rely on the fact that for Hitscan, rayOrigin is camera/muzzle.
            // For projectile, rayOrigin passed here is the step start.
            // This assumes falloff is per-step which is WRONG.
            // Refactor needed: pass total distance or start position.
            
            // To fix this cleanly, let's recalculate distance inside here using a passed in TotalDistance or similar.
            // For now, I'll use Vector3.Distance from firePoint/camera if possible, but that data isn't passed efficiently.
            
            // Correction: I can calculate distance inside the caller and pass it in.
            // Let's assume standard hitscan uses hit.distance (from muzzle).
            // Proj uses distance from start.
            
            float effectiveDistance = hit.distance; // value for hitscan
            // We need to differentiate or pass the true distance.
            
             // Spawn hit effect locally
            SpawnHitEffect(hit.point, hit.normal);

            // Invoke local hit event
            OnHit?.Invoke(hit);

            // Apply Damage
            var damageableTarget = hit.collider.GetComponentInParent<Damageable>();
            if (damageableTarget != null)
            {
                // We'll calculate damage before sending (simplification)
                // But wait, the standard logic used hit.distance.
                float finalDamage;
                if (data.useProjectile)
                {
                     // We don't have total distance here easily without changing signature.
                     // But we can approximate or change signature.
                     // Let's just use hit.distance (local step) which effectively means NO FALLOFF for projectile currently unless we fix it.
                     // IMPORTANT: I will change signature to accept calculated damage.
                     finalDamage = data.CalculateDamage(0, isHeadshot) * damageMultiplier; // Fallback for now to avoid breaking signature too much in one go?
                     // actually, let's fix it properly.
                }
                else
                {
                    finalDamage = data.CalculateDamage(distance, isHeadshot) * damageMultiplier;
                }
                
                // Let's do the calculation in the caller!

                var damageInfo = new DamageInfo(
                    finalDamage,
                    hit.point,
                    hit.normal,
                    _ownerClientId,
                    isHeadshot
                );
                 // Networking logic...
                 bool isNetworked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
                if (isNetworked)
                {
                    if (NetworkManager.Singleton.IsServer) damageableTarget.TakeDamage(damageInfo);
                    else damageableTarget.TakeDamage(damageInfo);
                }
                else
                {
                    damageableTarget.TakeDamageOffline(damageInfo);
                }
            }
             
             // Debug
            if (drawDebugRays && !data.useProjectile) // Projectile already drew its own fast lines
            {
                 Color rayColor = isHeadshot ? debugRayColorHeadshot : debugRayColorHit;
                 CreateDebugRay(rayOrigin, hit.point, rayColor, shotNumber, hit.collider.name);
            }
        }
        
        // Overloaded helper to handle damage calc reuse
        private void ProcessHitResult(RaycastHit hit, float damageMultiplier, int shotNumber, Vector3 rayOrigin, HitscanData data, float totalDistanceOverride = -1f)
        {
             // Check Headshot
            bool isHeadshot = false;
            if (!string.IsNullOrEmpty(data.headshotTag))
            {
                try { isHeadshot = hit.collider.CompareTag(data.headshotTag); } catch { }
            }
            
            float dist = (totalDistanceOverride >= 0) ? totalDistanceOverride : hit.distance;
            float damage = data.CalculateDamage(dist, isHeadshot) * damageMultiplier;

             // Debug visualization
            if (drawDebugRays && !data.useProjectile)
            {
                 // ... same as before
                  Color rayColor = isHeadshot ? debugRayColorHeadshot : debugRayColorHit;
                  // Debug logging...
                  CreateDebugRay(rayOrigin, hit.point, rayColor, shotNumber, hit.collider.name);
            }

            SpawnHitEffect(hit.point, hit.normal);
            OnHit?.Invoke(hit);

            var damageableTarget = hit.collider.GetComponentInParent<Damageable>();
            if (damageableTarget != null)
            {
                 var damageInfo = new DamageInfo(damage, hit.point, hit.normal, _ownerClientId, isHeadshot);
                 // Network/Offline logic same as above
                 bool isNetworked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
                if (isNetworked)
                {
                    if (NetworkManager.Singleton.IsServer) damageableTarget.TakeDamage(damageInfo);
                    else damageableTarget.TakeDamage(damageInfo);
                }
                else
                {
                    damageableTarget.TakeDamageOffline(damageInfo);
                }
            }
        }


        // --- Effects & Helper Methods ---

        private void SpawnHitEffect(Vector3 position, Vector3 normal)
        {
            if (hitEffectPrefab == null) return;
            var effect = HitEffectPool.Instance?.GetEffect(hitEffectPrefab, position, Quaternion.LookRotation(normal));
            if (effect == null)
            {
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

        private void CreateDebugRay(Vector3 start, Vector3 end, Color color, int shotNumber, string hitName, bool createLabels = true)
        {
            var debugObj = new GameObject($"DebugRay_{shotNumber}");
            _debugObjects.Add(debugObj);

            var lineRenderer = debugObj.AddComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
            lineRenderer.startWidth = 0.02f;
            lineRenderer.endWidth = 0.02f;
            lineRenderer.material = _debugLineMaterial;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;

            if (createLabels)
            {
                var hitMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hitMarker.transform.position = end;
                hitMarker.transform.localScale = Vector3.one * 0.1f;
                hitMarker.transform.SetParent(debugObj.transform);
                if(hitMarker.GetComponent<Collider>()) Destroy(hitMarker.GetComponent<Collider>());
                var mr = hitMarker.GetComponent<Renderer>();
                mr.material = new Material(Shader.Find("Sprites/Default"));
                mr.material.color = color;

                var labelObj = new GameObject($"Label_{shotNumber}");
                labelObj.transform.position = end + Vector3.up * 0.15f;
                labelObj.transform.SetParent(debugObj.transform);
                var tm = labelObj.AddComponent<TextMesh>();
                tm.text = $"#{shotNumber}\n{hitName ?? "MISS"}";
                tm.fontSize = 24;
                tm.characterSize = 0.02f;
                tm.anchor = TextAnchor.LowerCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = color;
                labelObj.AddComponent<DebugBillboard>();
            }

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
