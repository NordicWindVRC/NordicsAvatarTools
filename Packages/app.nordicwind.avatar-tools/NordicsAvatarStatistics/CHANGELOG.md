# Changelog — Nordic's Avatar Statistics

Newest first. Versions follow [SemVer](https://semver.org). This tool's version is independent
of the *NordicWind - Avatar Tools* bundle version.

## 0.1.36 — unreleased

EditMode tests pass in Unity 2022.3.22 (2026-11-03). Not yet verified on an upload.

### Added
- A note on the face-tracking rows when another tool, such as VRCFury Visemes or BlendShape
  Link, turned native face tracking on during the build.
- EditMode tests for rank thresholds, bounds ranks, Quest hard limits, and synced parameter
  bits compared with the SDK's own `CalcTotalCost()`.

### Changed
- Package and NDMF name `app.nordicwind.avatar-statistics`. The NDMF plugin class is renamed
  `NordicsAvatarStatisticsPlugin`. MIT license.
- Console warnings start with `[Nordic's Avatar Statistics]`.

### Removed
- The temporary PhysBone collision-check and constraint-depth diagnostic panels.
- Per-parameter audit data that was collected but never shown, and an unused field.

## Pre-release history

| Version | Summary |
|---|---|
| 0.1.35 | Parameter panels refresh after the late SDK callback, so VRCFury-compressed values show. Bounds use the SDK's own value when available. |
| 0.1.34 | Face-tracking rows only when supported and enabled. Shorter notes. |
| 0.1.33 | Corrected how SDK metrics (collision checks, constraints) are read. |
| 0.1.32 | Collision checks, constraint count and depth, and face-tracking metrics come from the VRChat SDK. |
| 0.1.31 | Cleaner parameter display. |
| 0.1.30 | Hotfix: registry made accessible to the NDMF assembly. |
| 0.1.29 | Late SDK callback that reads parameters after processors outside NDMF. |
| 0.1.28 | Blend shapes tile. |
| 0.1.27 | Synced parameter memory and parameter count budgets. |
| 0.1.26 | Separate PC and Quest texture memory, using each platform's import settings. |
| 0.1.25 | Texture header order. |
| 0.1.24 | Texture type shown in headers (Normal, RGB, RGBA, sRGB). |
| 0.1.23 | Texture details: streaming, mip filter and Crunch. Clearer descriptions. |
| 0.1.22 | Material slot counting matches VRChat. Temporary audits removed. |
| 0.1.21 | Temporary material-slot and constraint-depth audits. |
| 0.1.20 | Triangle counting matches VRChat. |
| 0.1.19 | Hotfix: missing import. |
| 0.1.17 | Collision checks use radius-eligible counting. |
| 0.1.16 | Experimental collision-check counting method. |
| 0.1.15 | Metrics ordered by optimization relevance. PhysBone diagnostics. |
| 0.1.14 | Alternative collision-check comparison. |
| 0.1.13 | The remaining VRChat performance categories. Zero rows hidden. |
| 0.1.12 | PhysBone affected-transform diagnostics. |
| 0.1.11 | Corrected an accuracy claim. |
| 0.1.10 | Registers with Avatar Optimizer to avoid its unknown-component warning. |
| 0.1.9 | Bounds use per-axis thresholds. Quest limits, and *Stripped* for components removed on mobile. |
| 0.1.7 | Clearer performance rows with a position marker on each rating bar. |
| 0.1.6 | New report layout with tabs, and PC and Quest performance previews. |
| 0.1.4 | GPU-format texture memory estimates. |
| 0.1.3 | Placement check looks for the VRC Avatar Descriptor on the same object. |
| 0.1.2 | Made classes accessible across assemblies. |
| 0.1.1 | Fixed the runtime assembly reference to NDMF. |
