# Changelog — Nordic's Blendshape Toggle

Newest first. Versions follow [SemVer](https://semver.org). This tool's version is independent
of the *NordicWind - Avatar Tools* bundle version.

## 0.2.0 — 2026-11-04

EditMode tests pass in Unity 2022.3.22 (2026-11-03). Verified on a build of a test avatar
using VRCFury (Armature Link, face tracking, toggles), MA Mesh Cutter, Meshia and AAO Merge
Skinned Mesh. Not yet uploaded to VRChat.

### Added
- Works with Nordic's Submesh Splitter: a toggle on a split renderer acts on that material
  slot's part. The original renderer gets the name, so animations target it as usual.
- EditMode tests in `NordicWind.BlendshapeToggle.Tests` (replacing the menu self-test),
  including Splitter routing and the late geometry step.

### Changed
- Works in two steps. An empty placeholder shape with the toggle's name is added in NDMF
  Generating, so MA and VRCFury can find and animate it. The real geometry is written in NDMF
  Optimizing, after Meshia and before the Submesh Splitter and AAO, on the processed mesh.
- Package and NDMF name `app.nordicwind.blendshape-toggle`, runtime assembly
  `NordicWind.BlendshapeToggle.Runtime`, MIT license. Messages use the display name.

### Fixed
- A reversed toggle on a mesh that is also simplified (for example by Meshia) came out
  slightly deformed. The simplifier was working on the collapsed geometry.

### Removed
- The **Tools → NordicWind → Run Blendshape Toggle Geometry Tests** menu item.

## Pre-release history

| Version | Summary |
|---|---|
| 0.1.3 | Display name changed to *Nordic's Blendshape Toggle*. |
| 0.1.2 | **Reverse Blendshape** option: hidden at 0, shown at 100. |
| 0.1.1 | Runs in NDMF Generating, so VRCFury BlendShape actions can find the shape. |
| 0.1.0 | First version: collapse blendshapes for isolated material slots. |
