using UnityEngine;

namespace Demo.Scripts.Runtime.Combat
{
    [CreateAssetMenu(menuName = "KINEMATION/FPS Animator General/Hitscan Data")]
    public class HitscanData : ScriptableObject
    {
        [Header("Damage")]
        [Tooltip("Base damage per hit")]
        public float damage = 25f;

        [Tooltip("Damage multiplier for headshots")]
        public float headshotMultiplier = 2f;

        [Header("Range")]
        [Tooltip("Maximum range of the hitscan in meters")]
        public float range = 100f;

        [Tooltip("Damage falloff over distance (X = normalized distance 0-1, Y = damage multiplier)")]
        public AnimationCurve damageFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0.5f);

        [Header("Penetration")]
        [Tooltip("Number of objects the bullet can penetrate (0 = no penetration)")]
        [Range(0, 5)]
        public int penetrationCount = 0;

        [Tooltip("Damage multiplier after each penetration")]
        [Range(0f, 1f)]
        public float penetrationDamageMultiplier = 0.5f;

        [Header("Layers")]
        [Tooltip("Layers that can be hit by this weapon")]
        public LayerMask hitLayers = ~0;

        [Tooltip("Tag used to identify headshot colliders")]
        public string headshotTag = "Head";

        /// <summary>
        /// Calculate damage at a given distance, applying falloff
        /// </summary>
        public float CalculateDamage(float distance, bool isHeadshot)
        {
            float normalizedDistance = Mathf.Clamp01(distance / range);
            float falloffMultiplier = damageFalloff.Evaluate(normalizedDistance);
            float finalDamage = damage * falloffMultiplier;

            if (isHeadshot)
            {
                finalDamage *= headshotMultiplier;
            }

            return finalDamage;
        }
    }
}
