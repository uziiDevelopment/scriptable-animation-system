using System;
using Unity.Netcode;
using UnityEngine;

namespace Demo.Scripts.Runtime.Combat
{
    /// <summary>
    /// NetworkBehaviour that allows an object to receive damage.
    /// Attach to any GameObject that should have health (players, enemies, destructibles).
    /// </summary>
    public class Damageable : NetworkBehaviour
    {
        [Header("Health")]
        [SerializeField] private float maxHealth = 100f;

        // Events (subscribe to these for UI/gameplay reactions)
        public event Action<DamageInfo> OnDamaged;
        public event Action OnDeath;
        public event Action<float, float> OnHealthChanged; // (currentHealth, maxHealth)

        // Networked health - only server can write, all clients can read
        private NetworkVariable<float> _currentHealth = new NetworkVariable<float>(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Offline health for single-player mode
        private float _offlineHealth;
        private bool _isDead;

        private bool IsNetworked => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public float CurrentHealth => IsNetworked ? _currentHealth.Value : _offlineHealth;
        public float MaxHealth => maxHealth;
        public bool IsDead => _isDead;
        public float HealthPercent => CurrentHealth / maxHealth;

        private void Awake()
        {
            // Initialize offline health
            _offlineHealth = maxHealth;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Initialize health on server
            if (IsServer)
            {
                _currentHealth.Value = maxHealth;
            }

            // Subscribe to health changes on all clients
            _currentHealth.OnValueChanged += HandleHealthChanged;

            // Invoke initial health event
            OnHealthChanged?.Invoke(_currentHealth.Value, maxHealth);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _currentHealth.OnValueChanged -= HandleHealthChanged;
        }

        /// <summary>
        /// Apply damage to this object. Can be called from any client, but only server processes it.
        /// Use TakeDamageOffline() for single-player / offline mode.
        /// </summary>
        public void TakeDamage(DamageInfo damageInfo)
        {
            if (!IsServer) return;
            if (_isDead) return;

            float newHealth = Mathf.Max(0f, _currentHealth.Value - damageInfo.Damage);
            _currentHealth.Value = newHealth;

            // Notify all clients about the damage
            NotifyDamageClientRpc(damageInfo);

            if (newHealth <= 0f && !_isDead)
            {
                _isDead = true;
                NotifyDeathClientRpc();
            }
        }

        /// <summary>
        /// Apply damage in offline / single-player mode (no network checks).
        /// </summary>
        public void TakeDamageOffline(DamageInfo damageInfo)
        {
            if (_isDead) return;

            float newHealth = Mathf.Max(0f, _offlineHealth - damageInfo.Damage);
            _offlineHealth = newHealth;

            // Invoke events locally
            OnDamaged?.Invoke(damageInfo);
            OnHealthChanged?.Invoke(_offlineHealth, maxHealth);

            if (newHealth <= 0f && !_isDead)
            {
                _isDead = true;
                OnDeath?.Invoke();
            }
        }

        /// <summary>
        /// Heal this object. Works in both networked (server only) and offline modes.
        /// </summary>
        public void Heal(float amount)
        {
            if (_isDead) return;

            if (IsNetworked)
            {
                if (!IsServer) return;
                _currentHealth.Value = Mathf.Min(maxHealth, _currentHealth.Value + amount);
            }
            else
            {
                _offlineHealth = Mathf.Min(maxHealth, _offlineHealth + amount);
                OnHealthChanged?.Invoke(_offlineHealth, maxHealth);
            }
        }

        /// <summary>
        /// Reset health to max. Works in both networked (server only) and offline modes.
        /// </summary>
        public void ResetHealth()
        {
            if (IsNetworked)
            {
                if (!IsServer) return;
                _isDead = false;
                _currentHealth.Value = maxHealth;
            }
            else
            {
                _isDead = false;
                _offlineHealth = maxHealth;
                OnHealthChanged?.Invoke(_offlineHealth, maxHealth);
            }
        }

        [ClientRpc]
        private void NotifyDamageClientRpc(DamageInfo damageInfo)
        {
            OnDamaged?.Invoke(damageInfo);
        }

        [ClientRpc]
        private void NotifyDeathClientRpc()
        {
            _isDead = true;
            OnDeath?.Invoke();
        }

        private void HandleHealthChanged(float previousValue, float newValue)
        {
            OnHealthChanged?.Invoke(newValue, maxHealth);
        }
    }
}
