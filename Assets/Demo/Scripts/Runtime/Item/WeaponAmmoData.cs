using UnityEngine;

namespace Demo.Scripts.Runtime.Item
{
    [CreateAssetMenu(menuName = "KINEMATION/FPS Animator General/Weapon Ammo Data")]
    public class WeaponAmmoData : ScriptableObject
    {
        [Header("Magazine")]
        [Tooltip("Number of rounds the magazine can hold")]
        public int magazineSize = 30;

        [Header("Reserve Ammo")]
        [Tooltip("Maximum reserve ammo this weapon can carry")]
        public int maxReserveAmmo = 120;

        [Tooltip("Reserve ammo the weapon starts with")]
        public int startingReserveAmmo = 90;

        [Header("Reload Behavior")]
        [Tooltip("Allow reloading before magazine is empty")]
        public bool allowPartialReload = true;

        [Tooltip("Add +1 capacity when reloading with a round still chambered")]
        public bool useChamberedRound = true;
    }
}
