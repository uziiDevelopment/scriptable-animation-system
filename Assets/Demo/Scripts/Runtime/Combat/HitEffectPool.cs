using System.Collections.Generic;
using UnityEngine;

namespace Demo.Scripts.Runtime.Combat
{
    /// <summary>
    /// Simple object pool for hit effect particles.
    /// Singleton for easy access from HitscanWeapon.
    /// </summary>
    public class HitEffectPool : MonoBehaviour
    {
        public static HitEffectPool Instance { get; private set; }

        [Header("Pool Settings")]
        [SerializeField] private int initialPoolSize = 20;
        [SerializeField] private float effectLifetime = 2f;

        private Dictionary<GameObject, Queue<GameObject>> _pools = new Dictionary<GameObject, Queue<GameObject>>();
        private Dictionary<GameObject, GameObject> _instanceToPrefab = new Dictionary<GameObject, GameObject>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Get an effect from the pool, or create a new one if pool is empty
        /// </summary>
        public GameObject GetEffect(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            // Create pool for this prefab if it doesn't exist
            if (!_pools.ContainsKey(prefab))
            {
                _pools[prefab] = new Queue<GameObject>();
                WarmPool(prefab, initialPoolSize);
            }

            GameObject effect;
            var pool = _pools[prefab];

            if (pool.Count > 0)
            {
                effect = pool.Dequeue();
                effect.transform.SetPositionAndRotation(position, rotation);
                effect.SetActive(true);
            }
            else
            {
                effect = Instantiate(prefab, position, rotation, transform);
                _instanceToPrefab[effect] = prefab;
            }

            // Auto-return to pool after lifetime
            StartCoroutine(ReturnAfterDelay(effect, effectLifetime));

            return effect;
        }

        /// <summary>
        /// Return an effect to the pool
        /// </summary>
        public void ReturnEffect(GameObject effect)
        {
            if (effect == null) return;

            effect.SetActive(false);

            if (_instanceToPrefab.TryGetValue(effect, out var prefab))
            {
                if (_pools.TryGetValue(prefab, out var pool))
                {
                    pool.Enqueue(effect);
                }
            }
        }

        /// <summary>
        /// Pre-warm the pool with instances
        /// </summary>
        public void WarmPool(GameObject prefab, int count)
        {
            if (prefab == null) return;

            if (!_pools.ContainsKey(prefab))
            {
                _pools[prefab] = new Queue<GameObject>();
            }

            var pool = _pools[prefab];

            for (int i = 0; i < count; i++)
            {
                var effect = Instantiate(prefab, transform);
                effect.SetActive(false);
                _instanceToPrefab[effect] = prefab;
                pool.Enqueue(effect);
            }
        }

        private System.Collections.IEnumerator ReturnAfterDelay(GameObject effect, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnEffect(effect);
        }
    }
}
