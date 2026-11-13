# Nordic's Advanced Atlaser — Normal Baker (design draft)

> **Status: draft, not built.** A small editor utility for later.

## Purpose

Some meshes come with a lower-detail version, for example a Quest version of a PC avatar mesh,
but without a normal map baked for it. The Normal Baker makes one: it bakes the shading of an
**original** mesh onto a **current** (lower-detail) mesh and saves it as a PNG normal map. That
map goes into a source's **Pre-baked Normal Map** slot on Nordic's Atlas Output.

It complements the build's normal correction rather than replacing it. The build only sees
changes made during the build, for example decimation by Meshia or AAO. It can't know how the
current mesh differs from a higher-detail mesh it was made from before the build.

## User interface

A dockable editor window, opened from **Tools → NordicWind → Advanced Atlaser → Normal Baker**.
It never uses the current selection: everything is set in the window.

```
┌ Normal Baker ─────────────────────────────────┐
│ Original  [ drop a mesh here        ]  (◉)     │
│           Submesh  [ 1: Body (Skin)      ▾ ]   │
│ Current   [ drop a mesh here        ]  (◉)     │
│           Submesh  [ 1: Body (Skin)      ▾ ]   │
│ Size      [ 4096 ▾ ] × [ 4096 ▾ ]              │
│ Max correction angle  [ 60 ]°                  │
│                                [ Generate… ]   │
│ Last bake: 2,841,120 texels; 0.4% overlapping  │
│ UVs and 0.1% above 60° left uncorrected.       │
└───────────────────────────────────────────────┘
```

- **Original** and **Current** are slots. Drag in a mesh from the Project window (a `Mesh`
  asset, or a model such as an FBX) or from the scene (an object with a `SkinnedMeshRenderer`
  or a `MeshRenderer`). The object picker (◉) works too.
- Each slot has a **Submesh** dropdown listing that mesh's submeshes. For a scene object the
  material name is shown next to each submesh.
- **Size** defaults to 4096 × 4096 (512 to 8192).
- **Max correction angle** defaults to 60°; larger corrections fade out, as in the build.
- **Generate…** bakes, then opens a **save dialog** where you pick the PNG's location and name.
  - Inside `Assets`, the PNG is imported as a normal map.
  - Elsewhere, it's only written; import it as a normal map yourself.
- One summary line after each bake, like the build's: texels baked, how much was left
  uncorrected because of overlapping UVs or the angle limit, and the largest correction kept.
  A warning only when the two meshes' UVs don't match (see below).
- The slots and settings are kept while the window is open and docked, but they aren't saved
  with the scene or project.

## How it works

It reuses the build's bake, so the results match what the build would do.

1. **Capture both meshes** as mesh sections: vertices, normals and tangents.
   - A **scene object** is captured the way the build does it (`AtlasGeometryPose.Capture`),
     with its transform and current pose applied.
   - A **mesh asset** is used as it is, in its own mesh space, without a pose.
   - Position doesn't matter, only orientation. Two scene objects can stand anywhere, but must
     be in the same pose. Mixing a scene object with a mesh asset works only when the object
     isn't rotated or posed differently from the asset; the window warns when the transforms
     differ in rotation or scale.
2. **Bake by UV correspondence**, exactly like the build: paint the original's normals and the
   current mesh's tangent frame into UV space with the Texture Wrapper, then
   `BakeNormalDifference` with the safety rules (overlapping UVs and corrections beyond the
   angle limit get no correction).
3. **Dilate** the result over the whole texture, then **export** it with the Texture Wrapper's
   PNG exporter, imported as a normal map.

**Refactoring this needs:** the core of `AtlasSubmeshBaker` (from mesh sections to a dilated
correction) becomes one shared function, used by both the build and the Normal Baker. The build's
behaviour stays the same.

## Requirements and limits

- **The UVs must match.** Baking by UV correspondence works when the current mesh keeps the
  original's UV layout, which most decimation tools do. If the lower-detail mesh was unwrapped
  again, UV correspondence can't work, and a different bake (ray casting from one surface to the
  other) would be needed. That's out of scope. The baker detects it when the two meshes' UV
  coverage differs a lot, and says so instead of saving a misleading map.
- UV0 is used, like the build.
- The save dialog starts in the shared NordicWind *generated assets folder*
  (`ProjectSettings/NordicWindSettings.json`), with `<current mesh>_<submesh>_Normal.png` as the
  suggested name. The Atlaser reads that settings file itself, because the existing settings
  code belongs to another package.

## Tests

- Identical original and current mesh: the map is flat.
- A curved original and a flat lower-detail mesh with the same UVs: decoding the map on the
  current mesh reproduces the original shading (as in the whole-mesh tests).
- Different UV layouts: detected and reported, nothing saved.
- Transforms: rotated, mirrored and differently scaled renderers give the same map.
- Slots: mesh assets, models, skinned and static scene objects are all accepted; anything
  else is refused. The submesh lists match the meshes.

## Decisions

- **One submesh per slot.** A material split over several submeshes isn't supported.
- **No bake button on Nordic's Atlas Output.** The baker is only a separate window. A button
  that bakes and assigns the result from the output's inspector would complicate prefab
  editing and similar cases. Authors assign the saved PNG to *Pre-baked Normal Map* themselves.
