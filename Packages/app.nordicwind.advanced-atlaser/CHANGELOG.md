# Changelog — Nordic's Advanced Atlaser

Newest first. Versions follow [SemVer](https://semver.org). Early versions were numbered as
"Revision N", which corresponds to version `0.N.0`.

## 0.40.0-beta.1

EditMode tests pass in Unity 2022.3.22. Verified on builds of a test avatar with NDMF 1.14.8
and AAO 1.9.19.

First public beta. Builds combine an avatar's materials into atlased materials for Poiyomi
Toon 10 or VRChat Mobile Toon Standard, feature by feature, from the source shaders the README
lists. Requires NDMF 1.7.0 or later and Nordic's Texture Wrapper 0.10.0 or later. Settings may
still change between beta versions.

## Pre-release history

| Version | Summary |
|---|---|
| 0.40.0 (development) | Atlas outputs end to end: the output model and inspector, auto setup, every initial feature on Poiyomi Toon 10 and VRChat Mobile Toon Standard, the mesh remapper, Bake to Assets, the baking toolkit (normal-map shading, occlusion and matcaps baked), the Normal Baker window. |
| 0.39.0 | Blend-shape geometry exclusion with a threshold and *Any* / *All Vertices* mode per entry. Normal corrections use full dilation. |
| 0.38.0 | Nested AAO Merge Skinned Mesh sources are discovered. |
| 0.37.0 | Fixed a hang at 4096² caused by a per-pixel loop after dilation. |
| 0.35.0 | Configurable normal-correction padding (later replaced by full dilation). |
| 0.34.0 | Optional exclusion of geometry moved by selected blend shapes. |
| 0.33.0 | Pose-aware geometry capture, without requiring AAO. |
| 0.32.0 | Geometry correction in avatar-root space. Requires Texture Wrapper 0.8.3. |
| 0.30.0 | Debug menu: verbose logging, intermediate map export and build summary. |
| 0.29.0 | Material-centric geometry baking across all original renderers. |
| 0.28.0 | Original geometry captured early in the build. Capture diagnostics. |
| 0.24.0–0.26.0 | Unified material model: input parsers for Standard, VRChat Mobile, Mochie and Poiyomi 7–10, global masks, and Poiyomi inspector mappings. |
