// Designed by KINEMATION, 2025.

using KINEMATION.FPSAnimationFramework.Runtime.Camera;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.FPSAnimationFramework.Runtime.Recoil;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.KAnimationCore.Runtime.Input;

using Demo.Scripts.Runtime.AttachmentSystem;

using System;
using System.Collections.Generic;
using Demo.Scripts.Runtime.Character;
using UnityEngine;

namespace Demo.Scripts.Runtime.Item
{
    public class Weapon : FPSItem
    {
        [Header("General")]
        [SerializeField] [Range(0f, 120f)] private float fieldOfView = 90f;
        
        [SerializeField] private FPSAnimationAsset reloadClip;
        [SerializeField] private FPSCameraAnimation cameraReloadAnimation;
        
        [SerializeField] private FPSAnimationAsset grenadeClip;
        [SerializeField] private FPSCameraAnimation cameraGrenadeAnimation;

        [Header("Recoil")]
        [SerializeField] private FPSAnimationAsset fireClip;
        [SerializeField] private RecoilAnimData recoilData;
        [SerializeField] private RecoilPatternSettings recoilPatternSettings;
        [SerializeField] private FPSCameraShake cameraShake;
        [Min(0f)] [SerializeField] private float fireRate;

        [SerializeField] private bool supportsAuto;
        [SerializeField] private bool supportsBurst;
        [SerializeField] private int burstLength;

        [Header("Audio")]
        [SerializeField] private AudioClip fireSound;
        [Tooltip("Random pitch variation for auto-fire (prevents phasing)")]
        [SerializeField] [Range(0f, 0.2f)] private float audioPitchRandomness = 0.05f;
        private AudioSource _audioSource;

        [Header("Attachments")]

        [SerializeField]
        private AttachmentGroup<BaseAttachment> barrelAttachments = new AttachmentGroup<BaseAttachment>();

        [SerializeField]
        private AttachmentGroup<BaseAttachment> gripAttachments = new AttachmentGroup<BaseAttachment>();

        [SerializeField]
        private List<AttachmentGroup<ScopeAttachment>> scopeGroups = new List<AttachmentGroup<ScopeAttachment>>();

        [Header("Ammo")]
        [SerializeField] private CaliberData caliberData;
        [SerializeField] private WeaponAmmoData ammoData;

        public CaliberData Caliber => caliberData;

        // Ammo runtime state (persists between equip/unequip)
        private WeaponState _weaponState = WeaponState.Ready;
        private int _currentAmmo = -1;
        private int _reserveAmmo = -1;
        private bool _ammoInitialized;
        private bool _isReloading;

        // Events for UI
        public event Action<int, int> OnAmmoChanged;
        public event Action<WeaponState> OnStateChanged;

        // Event for combat system (hitscan/projectile)
        public event Action OnWeaponFired;

        // Public properties for UI
        public WeaponState CurrentState => _weaponState;
        public int CurrentAmmo => _currentAmmo;
        public int ReserveAmmo => _reserveAmmo;
        public int MagazineSize => ammoData != null ? ammoData.magazineSize : 0;
        public bool HasAmmoData => ammoData != null;

        //~ Controller references

        private FPSController _fpsController;
        private Animator _controllerAnimator;
        private UserInputController _userInputController;
        private IPlayablesController _playablesController;
        private FPSCameraController _fpsCameraController;
        
        private FPSAnimator _fpsAnimator;
        private FPSAnimatorEntity _fpsAnimatorEntity;

        private RecoilAnimation _recoilAnimation;
        private RecoilPattern _recoilPattern;
        
        //~ Controller references
        
        private Animator _weaponAnimator;
        private int _scopeIndex;
        
        private float _lastRecoilTime;
        private int _bursts;
        private FireMode _fireMode = FireMode.Semi;
        
        private static readonly int CurveEquip = Animator.StringToHash("CurveEquip");
        private static readonly int CurveUnequip = Animator.StringToHash("CurveUnequip");

        private void OnActionEnded()
        {
            if (_fpsController == null) return;

            if (_isReloading)
            {
                CompleteReload();
                _isReloading = false;
            }

            _fpsController.ResetActionState();
        }

        protected void UpdateTargetFOV(bool isAiming)
        {
            float fov = fieldOfView;
            float sensitivityMultiplier = 1f;
            
            if (isAiming && scopeGroups.Count != 0)
            {
                var scope = scopeGroups[_scopeIndex].GetActiveAttachment();
                fov *= scope.aimFovZoom;

                sensitivityMultiplier = scopeGroups[_scopeIndex].GetActiveAttachment().sensitivityMultiplier;
            }

            _userInputController.SetValue("SensitivityMultiplier", sensitivityMultiplier);
            _fpsCameraController.UpdateTargetFOV(fov);
        }

        protected void UpdateAimPoint()
        {
            if (scopeGroups.Count == 0) return;

            var scope = scopeGroups[_scopeIndex].GetActiveAttachment().aimPoint;
            _fpsAnimatorEntity.defaultAimPoint = scope;
        }
        
        protected void InitializeAttachments()
        {
            foreach (var attachmentGroup in scopeGroups)
            {
                attachmentGroup.Initialize(_fpsAnimator);
            }
            
            _scopeIndex = 0;
            if (scopeGroups.Count == 0) return;

            UpdateAimPoint();
            UpdateTargetFOV(false);
        }
        
        public override void OnEquip(GameObject parent)
        {
            if (parent == null) return;

            _fpsAnimator = parent.GetComponent<FPSAnimator>();
            _fpsAnimatorEntity = GetComponent<FPSAnimatorEntity>();

            // Audio Logic
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                // Optionally add one if missing, or user must add it.
                // For a framework, it's safer to just add it if missing to avoid null ref, 
                // but usually user should configure. Let's add it dynamically if missing.
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 1f; // 3D sound
                _audioSource.playOnAwake = false;
            }

            _fpsController = parent.GetComponent<FPSController>();
            _weaponAnimator = GetComponentInChildren<Animator>();

            _controllerAnimator = parent.GetComponent<Animator>();
            _userInputController = parent.GetComponent<UserInputController>();
            _playablesController = parent.GetComponent<IPlayablesController>();
            _fpsCameraController = parent.GetComponentInChildren<FPSCameraController>();

            if (overrideController != _controllerAnimator.runtimeAnimatorController)
            {
                _playablesController.UpdateAnimatorController(overrideController);
            }

            InitializeAttachments();
            InitializeAmmo();

            _recoilAnimation = parent.GetComponent<RecoilAnimation>();
            _recoilPattern = parent.GetComponent<RecoilPattern>();

            _fpsAnimator.LinkAnimatorProfile(gameObject);

            barrelAttachments.Initialize(_fpsAnimator);
            gripAttachments.Initialize(_fpsAnimator);

            _recoilAnimation.Init(recoilData, fireRate, _fireMode);

            if (_recoilPattern != null)
            {
                _recoilPattern.Init(recoilPatternSettings);
            }

            _fpsAnimator.LinkAnimatorLayer(equipMotion);
        }

        public override void OnUnEquip()
        {
            _fpsAnimator.LinkAnimatorLayer(unEquipMotion);
        }

        public override bool OnAimPressed()
        {
            _userInputController.SetValue(FPSANames.IsAiming, true);
            UpdateTargetFOV(true);
            _recoilAnimation.isAiming = true;
            
            return true;
        }

        public override bool OnAimReleased()
        {
            _userInputController.SetValue(FPSANames.IsAiming, false);
            UpdateTargetFOV(false);
            _recoilAnimation.isAiming = false;
            
            return true;
        }

        public override bool OnFirePressed()
        {
            // Check ammo system if configured
            if (!CanFire())
            {
                return false;
            }

            // Do not allow firing faster than the allowed fire rate.
            if (Time.unscaledTime - _lastRecoilTime < 60f / fireRate)
            {
                return false;
            }

            _lastRecoilTime = Time.unscaledTime;
            _bursts = burstLength;

            OnFire();

            return true;
        }

        public override bool OnFireReleased()
        {
            if (_recoilAnimation != null)
            {
                _recoilAnimation.Stop();
            }
            
            if (_recoilPattern != null)
            {
                _recoilPattern.OnFireEnd();
            }
            
            CancelInvoke(nameof(OnFire));
            return true;
        }

        public override bool OnReload()
        {
            if (!FPSAnimationAsset.IsValid(reloadClip))
            {
                return false;
            }

            // Check ammo system if configured
            if (!CanReload())
            {
                return false;
            }

            // Set reloading state
            _isReloading = true;
            SetState(WeaponState.Reloading);

            _playablesController.PlayAnimation(reloadClip, 0f);

            if (_weaponAnimator != null)
            {
                _weaponAnimator.Rebind();
                _weaponAnimator.Play("Reload", 0);
            }

            if (_fpsCameraController != null)
            {
                _fpsCameraController.PlayCameraAnimation(cameraReloadAnimation);
            }

            Invoke(nameof(OnActionEnded), reloadClip.clip.length * 0.85f);

            OnFireReleased();
            return true;
        }

        public override bool OnGrenadeThrow()
        {
            if (!FPSAnimationAsset.IsValid(grenadeClip))
            {
                return false;
            }

            _playablesController.PlayAnimation(grenadeClip, 0f);
            
            if (_fpsCameraController != null)
            {
                _fpsCameraController.PlayCameraAnimation(cameraGrenadeAnimation);
            }
            
            Invoke(nameof(OnActionEnded), grenadeClip.clip.length * 0.8f);
            return true;
        }
        
        private void OnFire()
        {
            // Consume ammo if ammo system is configured
            if (ammoData != null)
            {
                _currentAmmo--;
                OnAmmoChanged?.Invoke(_currentAmmo, _reserveAmmo);

                if (_currentAmmo <= 0)
                {
                    _currentAmmo = 0;
                    SetState(WeaponState.Empty);
                }
            }

            // Play Fire Sound
            if (_audioSource != null && fireSound != null)
            {
                _audioSource.PlayOneShot(fireSound);
            }

            if (_weaponAnimator != null)
            {
                _weaponAnimator.Play("Fire", 0, 0f);
            }

            _fpsCameraController.PlayCameraShake(cameraShake);

            if (fireClip != null) _playablesController.PlayAnimation(fireClip);

            if (_recoilAnimation != null && recoilData != null)
            {
                _recoilAnimation.Play();
            }

            if (_recoilPattern != null)
            {
                _recoilPattern.OnFireStart();
            }

            // Notify combat system (hitscan/projectile)
            OnWeaponFired?.Invoke();

            if (_recoilAnimation.fireMode == FireMode.Semi)
            {
                Invoke(nameof(OnFireReleased), 60f / fireRate);
                return;
            }

            if (_recoilAnimation.fireMode == FireMode.Burst)
            {
                _bursts--;

                if (_bursts == 0)
                {
                    OnFireReleased();
                    return;
                }
            }

            // Stop firing if out of ammo
            if (ammoData != null && _currentAmmo <= 0)
            {
                OnFireReleased();
                return;
            }

            Invoke(nameof(OnFire), 60f / fireRate);
        }

        public override void OnCycleScope()
        {
            if (scopeGroups.Count == 0) return;
            
            _scopeIndex++;
            _scopeIndex = _scopeIndex > scopeGroups.Count - 1 ? 0 : _scopeIndex;
            
            UpdateAimPoint();
            UpdateTargetFOV(true);
        }

        private void CycleFireMode()
        {
            if (_fireMode == FireMode.Semi && supportsBurst)
            {
                _fireMode = FireMode.Burst;
                _bursts = burstLength;
                return;
            }

            if (_fireMode != FireMode.Auto && supportsAuto)
            {
                _fireMode = FireMode.Auto;
                return;
            }

            _fireMode = FireMode.Semi;
        }
        
        public override void OnChangeFireMode()
        {
            CycleFireMode();
            _recoilAnimation.fireMode = _fireMode;
        }

        public override void OnAttachmentChanged(int attachmentTypeIndex)
        {
            if (attachmentTypeIndex == 1)
            {
                barrelAttachments.CycleAttachments(_fpsAnimator);
                return;
            }

            if (attachmentTypeIndex == 2)
            {
                gripAttachments.CycleAttachments(_fpsAnimator);
                return;
            }

            if (scopeGroups.Count == 0) return;
            scopeGroups[_scopeIndex].CycleAttachments(_fpsAnimator);
            UpdateAimPoint();
        }

        #region Ammo System

        public bool CanFire()
        {
            // If no ammo data, allow infinite firing (backwards compatible)
            if (ammoData == null) return true;

            return _weaponState == WeaponState.Ready && _currentAmmo > 0;
        }

        public bool CanReload()
        {
            // If no ammo data, allow reload if animation exists (backwards compatible)
            if (ammoData == null) return true;

            if (_weaponState != WeaponState.Ready && _weaponState != WeaponState.Empty)
                return false;

            if (_reserveAmmo <= 0)
                return false;

            int maxCapacity = GetMaxMagazineCapacity();

            if (!ammoData.allowPartialReload && _currentAmmo > 0)
                return false;

            return _currentAmmo < maxCapacity;
        }

        private void InitializeAmmo()
        {
            if (_ammoInitialized || ammoData == null) return;

            _currentAmmo = ammoData.magazineSize;
            _reserveAmmo = ammoData.startingReserveAmmo;
            _weaponState = WeaponState.Ready;
            _ammoInitialized = true;

            OnAmmoChanged?.Invoke(_currentAmmo, _reserveAmmo);
            OnStateChanged?.Invoke(_weaponState);
        }

        private void CompleteReload()
        {
            if (ammoData == null) return;

            int maxCapacity = GetMaxMagazineCapacity();
            int ammoNeeded = maxCapacity - _currentAmmo;
            int ammoToAdd = Mathf.Min(ammoNeeded, _reserveAmmo);

            _currentAmmo += ammoToAdd;
            _reserveAmmo -= ammoToAdd;

            _weaponState = _currentAmmo > 0 ? WeaponState.Ready : WeaponState.Empty;

            OnAmmoChanged?.Invoke(_currentAmmo, _reserveAmmo);
            OnStateChanged?.Invoke(_weaponState);
        }

        private int GetMaxMagazineCapacity()
        {
            if (ammoData == null) return 0;

            int maxCapacity = ammoData.magazineSize;

            // +1 for chambered round if reloading with ammo still in magazine
            if (ammoData.useChamberedRound && _currentAmmo > 0)
                maxCapacity++;

            return maxCapacity;
        }

        private void SetState(WeaponState newState)
        {
            if (_weaponState == newState) return;

            _weaponState = newState;
            OnStateChanged?.Invoke(_weaponState);
        }

        #endregion
    }
}