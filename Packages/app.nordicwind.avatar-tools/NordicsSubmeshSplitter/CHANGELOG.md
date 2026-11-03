# Changelog — Nordic's Submesh Splitter

Newest first. Versions follow [SemVer](https://semver.org). This tool's version is independent
of the *NordicWind - Avatar Tools* bundle version.

## 0.3.0 — unreleased

EditMode tests pass in Unity 2022.3.22 (2026-11-03). Verified on a build of a test avatar
using VRCFury (Armature Link, face tracking, toggles), MA Mesh Cutter, Meshia and AAO Merge
Skinned Mesh. Not yet uploaded to VRChat.

### Added
- Splitting scene objects that aren't part of a prefab, in saved scenes. Prefab instances in a
  scene show **Open Prefab** instead.
- Protection against outdated splits: a fingerprint of the source mesh is stored. The
  inspector shows an error and builds stop until you click **Update split meshes**.
- Regeneration adds or removes parts when the number of materials changes, and renames parts
  after their current material.
- VRCFury compatibility, including Blendshape Optimizer. During SDK builds and VRCFury Play
  Mode the parts are kept out of VRCFury's view, so it only ever sees the original renderer.
  Bone rebinding by VRCFury Armature Link is copied to the parts.
- Blendshapes that another tool renamed on the original (VRCFury face tracking: `SIL` →
  `VF_FT_SIL`) keep their data under the new name.
- Build checks with clear messages: another tool changed the original mesh, a component on a
  part was never applied, or an animation targets a split part. A dialog shows the reason,
  and Play Mode is stopped.
- **Tools → NordicWind → Submesh Splitter → Clean Up Unused Split Mesh Assets.**
- A warning when vertices are shared between submeshes.
- EditMode tests for reassembly, materials, bounds, blendshape order, baking and renames,
  build checks, isolation and bone sync, the fingerprint and stale detection.

### Changed
- The original renderer owns materials, bounds and its enabled state at build time.
- The merged blendshape list follows the original's current list and order, so eyelid
  settings stay valid. Shapes a general tool removed are baked in at their weight.
- Package and NDMF name `app.nordicwind.submesh-splitter`, runtime assembly
  `NordicWind.SubmeshSplitter.Runtime`, editor namespace `NordicWind.SubmeshSplitter.Editor`,
  internal classes renamed with the `Split` prefix, MIT license.
- The source object may have other components. Meshes with vertices shared between submeshes
  are allowed.

### Fixed
- Generated containers were saved with a missing script, so the Splitter created a new
  container on every update after an editor restart. Existing containers were repaired.
- Duplicated Splitters (Ctrl+D) shared and overwrote each other's meshes.
- The ordering constraint after Meshia used a plugin name that matched nothing.
- Bounds were overwritten with mesh-space bounds.
- Renderers inside a nested FBX or prefab couldn't be split in Prefab Mode.
- The inspector was slow on meshes with many blendshapes (fingerprinting took seconds).
- Split parts of outfits attached with VRCFury Armature Link ended up at the origin.

### Removed
- The VRCFury Blendshape Optimizer build block.
- The unused face/head renderer setting, the animation diagnostics, and seven unused
  diagnostic files.

## Pre-release history

| Version | Summary |
|---|---|
| 0.2.18 | Temporary animation-binding diagnostics (removed in 0.3.0). |
| 0.2.17 | Reuses the original renderer's bone order and bindposes when merging. |
| 0.2.16 | Merges only the vertices each part actually uses. |
| 0.2.15 | Removed verbose diagnostics and the CSV report. Simplified the inspector. |
| 0.2.14 | VRCFury Blendshape Optimizer block also cancels SDK builds. |
| 0.2.13 | Blocked builds that use VRCFury Blendshape Optimizer. |
| 0.2.12 | Temporary VRCFury compatibility diagnostics. |
| 0.2.11 | Disabled split parts are left out of the build. |
| 0.2.10 | Generated meshes are replaced as new sub-assets instead of being overwritten in place. |
| 0.2.9 | Split by cloning the mesh and keeping one submesh, preserving all vertex data. |
| 0.2.8 | UV channels keep their dimension (2D/3D/4D). |
| 0.2.7 | Temporary UV and vertex-layout diagnostics. |
| 0.2.6 | Optional forcing of 2D UVs for MA Mesh Cutter (later removed). |
| 0.2.4 | Diagnostics for MA Mesh Cutter testing. |
| 0.2.3 | Diagnostics comparing blendshape data early and late in the build. |
| 0.2.2 | Diagnostics for blendshape deformation, and the original renderer is always enabled after the build. |
| 0.2.1 | Re-enables the original renderer after reassembly. |
| 0.2.0 | Reassembly at build time: the processed parts are merged back into the original renderer. |
