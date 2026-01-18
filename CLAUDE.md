# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**FPS Animation Framework** - A modular procedural animation system for Unity FPS games by KINEMATION. Uses Unity's Playables API to layer procedural animations on top of the standard Animator system, enabling dynamic weapon handling, recoil, and character animations.

**Unity Version:** 6000.1.13f1

## Build & Development

This is a Unity project. Open in Unity Hub and use Unity Editor for building/testing. No command-line build scripts exist.

**Key Package Dependencies:**
- Unity Input System (1.14.0)
- Universal Render Pipeline (17.1.0)
- Timeline (1.8.7)
- Netcode for GameObjects (1.13.0)

## Architecture

### Assembly Structure (5 Main Assemblies)

```
KINEMATION/
├── Shared/
│   ├── KAnimationCore/
│   │   ├── Runtime/   → KAnimationCore.Runtime (math, rig, input primitives)
│   │   └── Editor/    → KAnimationCore.Editor (rig tools, debugging)
│   └── ScriptableWidget/  → UI widgets for asset selection
├── FPSAnimationFramework/
│   ├── Runtime/       → FPSAnimationFramework.Runtime (core animation system)
│   └── Editor/        → FPSAnimationFramework.Editor (wizards, inspectors)
└── ProceduralRecoilAnimationSystem/
    └── Runtime/       → RecoilAnimation.Runtime (procedural recoil)
```

### Core Component Hierarchy

**On Character GameObject:**
1. **FPSAnimator** (`Runtime/Core/FPSAnimator.cs`) - Main orchestrator, initializes and links subsystems
2. **FPSBoneController** (`Runtime/Core/FPSBoneController.cs`) - Manages animation layer stack and job execution
3. **FPSPlayablesController** (`Runtime/Playables/FPSPlayablesController.cs`) - Playables graph with 3 mixers (overlay, slots, overrides)
4. **UserInputController** (`Shared/KAnimationCore/Runtime/Input/UserInputController.cs`) - Centralized input property management
5. **FPSCameraController** (`Runtime/Camera/FPSCameraController.cs`) - Camera animation, shake, FOV

### Layer System

Animation layers implement `IAnimationLayerJob` and follow a Settings + Job pattern:
- **FPSAnimatorLayerSettings** (ScriptableObject) - Configuration asset
- **LayerJob** (IAnimationJob struct) - Runtime animation processing

14 built-in layers: WeaponLayer, AdditiveLayer, AdsLayer, AttachHandLayer, BlendingLayer, CollisionLayer, IkLayer, IkMotionLayer, LookLayer, PoseOffsetLayer, PoseSamplerLayer, SwayLayer, TurnLayer, ViewLayer

**Layer Lifecycle:**
```
Initialize(LayerJobData) → CreatePlayable(graph) → UpdatePlayableJobData() → ProcessAnimation() → LateUpdate() → Destroy()
```

### Rig System

- **KRig** (ScriptableObject) - Defines skeleton structure with bone hierarchy and named chains
- **KRigComponent** - Runtime handler mapping KRig bones to scene transforms
- **KRigElementChain** - Named bone chains for IK (SpineRootChain, RightHandChain, LeftHandChain, etc.)

### Input Property System

Layers reference input via `[InputProperty]` attribute on string fields. UserInputController manages bool, int, float, Vector2-4 properties with optional interpolation.

Key properties (from FPSANames): PlayablesWeight, StabilizationWeight, LeanInput, AimingWeight, IsAiming, MouseInput, MouseDeltaInput, MoveInput, TurnOffset

## Namespace Conventions

- `KINEMATION.KAnimationCore.*` - Shared foundation (KTransform, KMath, KRig, input)
- `KINEMATION.FPSAnimationFramework.*` - Main framework (FPSAnimator, layers, camera)
- `KINEMATION.ProceduralRecoilAnimationSystem.*` - Recoil system
- `Demo.Scripts.*` - Example implementation (FPSController, FPSMovement, weapons)

## Creating New Animation Layers

1. Create ScriptableObject extending `FPSAnimatorLayerSettings`
2. Override `CreateAnimationJob()` returning `IAnimationLayerJob`
3. Create struct implementing `IAnimationJob` with `ProcessAnimation(AnimationStream)`
4. Add `[CreateAssetMenu]` with menu path `FPSANames.FileMenuLayers + "YourLayer"`

## Key Patterns

**Transform Stream Binding:** Jobs bind to animator's TransformStream for non-allocating bone modification during animation evaluation.

**ScriptableObject Configuration:** All major systems (profiles, layers, rigs, input configs) use asset-based configuration via CreateAssetMenu.

**Asset Menu Paths:**
- `"KINEMATION/FPS Animator General/"` - Core assets (profiles, rigs)
- `"KINEMATION/FPS Animator Layers/"` - Layer settings

## Standard Bone/Chain Names

Bones: WeaponBone, WeaponBoneAdditive, IK WeaponBone, IK RightHand, IK LeftHand, IK RightElbow, IK LeftElbow

Chains: SpineRootChain, PelvisChain, RightHandChain, LeftHandChain, RightFootChain, LeftFootChain

## Demo Project

`Assets/Demo/` contains example implementation:
- **FPSController** - Player character with weapon equipping, aiming states, action states
- **FPSMovement** - Locomotion (walking, sprinting, crouch, prone, jump)
- **FPSItem** - Base weapon class
- **AttachmentSystem** - Scopes and attachments

## External Documentation

GitBook: https://kinemation.gitbook.io/scriptable-animation-system/
