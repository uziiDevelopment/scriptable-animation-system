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
    /// Uses CaliberData from Weapon if available, otherwise falls back to HitscanData.
    /// </summary>
    public class HitscanWeapon : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Legacy data. Use Weapon's CaliberData if checking 'Use Projectile'")]
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
            
            // Unified data source
            public float GravityMultiplier;
            public float MaxLifetime;
            public float PenetrationDmgMult;
            public LayerMask LayerMask;
            public string HeadshotTag;
            
            // Store reference to source for logic (or just copy needed values? Copying is safer)
            public CaliberData CaliberSource;
            public HitscanData LegacySource;
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
            bool useCaliber = _weapon != null && _weapon.Caliber != null;
            
            if (!useCaliber && hitscanData == null)
            {
                Debug.LogWarning("HitscanWeapon: No CaliberData (on Weapon) and no HitscanData assigned!");
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

            SpawnMuzzleFlash();

            _shotCounter++;

            // Mode Selection
            bool isProjectile = useCaliber || (hitscanData != null && hitscanData.useProjectile);

            if (isProjectile)
            {
                // Projectile Mode
                float speed = useCaliber ? _weapon.Caliber.muzzleVelocity : hitscanData.speed;
                float gravity = useCaliber ? _weapon.Caliber.gravityMultiplier : hitscanData.gravityMultiplier;
                float lifetime = useCaliber ? _weapon.Caliber.maxLifetime : hitscanData.maxLifetime;
                int pentCount = useCaliber ? _weapon.Caliber.penetrationCount : hitscanData.penetrationCount;
                float pentMult = useCaliber ? _weapon.Caliber.penetrationDamageMultiplier : hitscanData.penetrationDamageMultiplier;
                LayerMask mask = useCaliber ? _weapon.Caliber.hitLayers : hitscanData.hitLayers;
                string headTag = useCaliber ? "Head" : hitscanData.headshotTag; // Caliber doesn't store tag usually, assume "Head" or copy from somewhere? CaliberData has no tag field in my create step. Let's hardcode or add it. I'll hardcode "Head" for caliber or read from HitscanData if available as fallback config.
                
                // Note: I modified CaliberData to include projectile fields but maybe not all.
                // Let's check CaliberData definitions: I did not add Headshot Tag to CaliberData.
                // I'll stick to a default or check HitscanData for tag if available.
                if (hitscanData != null) headTag = hitscanData.headshotTag;

                Vector3 initialVelocity = direction * speed;
                
                var projectile = new ActiveProjectile
                {
                    Position = origin,
                    Velocity = initialVelocity,
                    StartPosition = origin,
                    StartTime = Time.time,
                    ShotIndex = _shotCounter,
                    PenetrationsRemaining = pentCount,
                    CurrentDamageMultiplier = 1f,
                    
                    GravityMultiplier = gravity,
                    MaxLifetime = lifetime,
                    PenetrationDmgMult = pentMult,
                    LayerMask = mask,
                    HeadshotTag = headTag,
                    
                    CaliberSource = useCaliber ? _weapon.Caliber : null,
                    LegacySource = useCaliber ? null : hitscanData
                };
                _activeProjectiles.Add(projectile);
            }
            else
            {
                // Immediate Hitscan Mode (Legacy)
                PerformHitscan(origin, direction, _shotCounter);
            }
        }

        // --- Projectile Simulation Logic ---

        private void SimulateProjectiles(float dt)
        {
            for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
            {
                var p = _activeProjectiles[i];

                if (Time.time - p.StartTime > p.MaxLifetime)
                {
                    _activeProjectiles.RemoveAt(i);
                    continue;
                }

                Vector3 currentPos = p.Position;
                Vector3 currentVel = p.Velocity;

                // Gravity
                Vector3 gravity = Physics.gravity * p.GravityMultiplier;
                
                // Kinematics integration
                Vector3 nextVel = currentVel + gravity * dt;
                Vector3 nextPos = currentPos + currentVel * dt + 0.5f * gravity * dt * dt;

                // Raycast along the step
                Vector3 displacement = nextPos - currentPos;
                float distance = displacement.magnitude;

                if (distance > 0.0001f)
                {
                    if (Physics.Raycast(currentPos, displacement.normalized, out RaycastHit hit, distance, p.LayerMask))
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
                    }
                    else
                    {
                        // No hit
                         if (drawDebugRays)
                        {
                            // Draw flight path
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
            
            // Calculate Damage
            float damage = 0f;
            bool isHeadshot = false;

            try { isHeadshot = hit.collider.CompareTag(p.HeadshotTag); } catch { }

            if (p.CaliberSource != null)
            {
                // Use Caliber Data
                float normalizedDist = Mathf.Clamp01(totalDistance / 200f); // Arbitrary max range for curve or just eval?
                // CaliberData has curve. Let's assume curve is mapped to distance directly if defined as such, 
                // BUT AnimationCurve is X-time usually. 
                // In CaliberData creation I said "X = distance". So we use distance directly.
                float multiplier = p.CaliberSource.damageFalloff.Evaluate(totalDistance);
                damage = p.CaliberSource.baseDamage * multiplier;
                if (isHeadshot) damage *= p.CaliberSource.headshotMultiplier;
            }
            else if (p.LegacySource != null)
            {
                damage = p.LegacySource.CalculateDamage(totalDistance, isHeadshot); // Using total distance now!
            }
            
            damage *= p.CurrentDamageMultiplier;

            // Apply Damage
            ApplyDamage(hit, damage, isHeadshot);

            // Effect
            SpawnHitEffect(hit.point, hit.normal);
            OnHit?.Invoke(hit);

            // Penetration check
            if (p.PenetrationsRemaining > 0)
            {
                p.PenetrationsRemaining--;
                p.CurrentDamageMultiplier *= p.PenetrationDmgMult;
                return false;
            }
            return true;
        }


        // --- Legacy Hitscan Logic ---

        private void PerformHitscan(Vector3 origin, Vector3 direction, int shotNumber)
        {
            int penetrationsRemaining = hitscanData.penetrationCount;
            float damageMultiplier = 1f;
            Vector3 currentOrigin = origin;

            do
            {
                if (Physics.Raycast(currentOrigin, direction, out RaycastHit hit, hitscanData.range, hitscanData.hitLayers))
                {
                    // Legacy processing
                    ProcessHitResultLegacy(hit, damageMultiplier, shotNumber, currentOrigin, hitscanData);

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

        private void ProcessHitResultLegacy(RaycastHit hit, float damageMultiplier, int shotNumber, Vector3 rayOrigin, HitscanData data)
        {
            bool isHeadshot = false;
            try { isHeadshot = hit.collider.CompareTag(data.headshotTag); } catch { }

            float damage = data.CalculateDamage(hit.distance, isHeadshot) * damageMultiplier;

            if (drawDebugRays)
            {
                Color rayColor = isHeadshot ? debugRayColorHeadshot : debugRayColorHit;
                CreateDebugRay(rayOrigin, hit.point, rayColor, shotNumber, hit.collider.name);
            }

            SpawnHitEffect(hit.point, hit.normal);
            OnHit?.Invoke(hit);
            
            ApplyDamage(hit, damage, isHeadshot);
        }
        
        private void ApplyDamage(RaycastHit hit, float damage, bool isHeadshot)
        {
             var damageableTarget = hit.collider.GetComponentInParent<Damageable>();
            if (damageableTarget != null)
            {
                 var damageInfo = new DamageInfo(damage, hit.point, hit.normal, _ownerClientId, isHeadshot);
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
