using UnityEngine;

namespace Demo.Scripts.Runtime.Item
{
    [CreateAssetMenu(menuName = "KINEMATION/FPS Animator General/Caliber Data")]
    public class CaliberData : ScriptableObject
    {
        [Header("Identity")]
        public string caliberName = "9x19mm";
        [TextArea] public string description = "Standard pistol cartridge.";

        [Header("Physical Properties")]
        [Tooltip("Mass of the bullet in kilograms (for physics calculations if needed)")]
        public float bulletMass = 0.008f; // 8g
        
        [Tooltip("Air resistance drag coefficient")]
        public float airDrag = 0.05f;

        [Header("Ballistics")]
        [Tooltip("Muzzle velocity in m/s")]
        public float muzzleVelocity = 360f;

        [Tooltip("Gravity multiplier (1 = normal Physics.gravity)")]
        public float gravityMultiplier = 1f;

        [Tooltip("Maximum lifetime of the projectile in seconds")]
        public float maxLifetime = 5f;

        [Header("Terminal Ballistics")]
        [Tooltip("Base damage per hit")]
        public float baseDamage = 25f;

        [Tooltip("Damage multiplier for headshots")]
        public float headshotMultiplier = 2f;

        [Tooltip("Damage falloff over distance (X = distance in meters, Y = damage multiplier)")]
        public AnimationCurve damageFalloff = AnimationCurve.Linear(0f, 1f, 100f, 0.5f);

        [Header("Penetration")]
        [Tooltip("Number of objects the bullet can penetrate")]
        [Range(0, 10)]
        public int penetrationCount = 0;

        [Tooltip("Damage multiplier after each penetration")]
        [Range(0f, 1f)]
        public float penetrationDamageMultiplier = 0.5f;

        [Tooltip("Layers that this caliber can hit/penetrate")]
        public LayerMask hitLayers = ~0;

        [Header("Visuals")]
        public GameObject projectilePrefab;
        public GameObject casingPrefab;
    }
}
