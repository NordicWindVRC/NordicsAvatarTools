# NordicWind Component Design Guidelines

Rules shared by every NordicWind tool (Advanced Atlaser, Avatar Statistics,
Blendshape Toggle, Submesh Splitter, Texture Wrapper, and anything new).
Where existing code differs, follow this guide for new work and fix the old code when
you next touch it. Each rule names its exceptions.

---

## 0. What each tool is for

| Tool | Purpose | Dependencies |
|---|---|---|
| **Submesh Splitter** | Splits a skinned mesh renderer into one renderer per submesh, keeping the original but disabled. Parts exist only for **explicit per-part actions**: Meshia settings per part, disabling a part to cut it, MA Mesh Cutter, a Blendshape Toggle on a slot. Everything general (materials, bounds, blendshapes, animations, AAO, VRCFury) acts on the original renderer, and those tools never see the parts. At build time the parts are recombined into the original renderer and removed. | NDMF, VRChat SDK |
| **Blendshape Toggle** | Emulates object toggles after renderers are merged or atlased. A generated blendshape shrinks a submesh and hides it inside the avatar. | NDMF |
| **Avatar Statistics** | Shows VRChat performance metrics and optimization statistics in the NDMF console that match what the game shows. Values come from the SDK, or are calculated where the SDK only gives estimates. Stays backwards compatible across SDK versions and supports face-tracking metrics. | NDMF, VRChat SDK |
| **Texture Wrapper** | Standalone CPU texture library, multithreaded with GPU acceleration possible later. Reads, creates and edits textures, builds UV island masks and bakes normal maps, for example normal correction after mesh reduction. **Its public API must stay compatible with existing callers.** | none |
| **Advanced Atlaser** | Work-in-progress material processor for atlasing, material optimization, normal baking, Quest or optimized variants, and non-destructive edits such as decals. Mapping is per material, not per submesh. Input parsers convert source shaders into one shader-agnostic material model. Output generators read only that model. | NDMF, Texture Wrapper |

Splitter decisions (2026-11-03):
- Splits can be made in Prefab Mode or on saved scene objects that aren't part of a
  prefab. Prefab instances in a scene must be edited in their prefab (the inspector offers
  **Open Prefab**).
- A split that is out of date with its source mesh stops the build with an error.
- Part names follow the current material and update when the split is regenerated.
- The original renderer owns materials, bounds, the blendshape list and enabled state.
- Components left unapplied on parts, and animations that target parts, stop the build.
- Vertices shared between submeshes are allowed, with an inspector warning about seams.

## 1. Naming

`<Feature>` is the PascalCase feature name, for example `AdvancedAtlaser` or `BlendshapeToggle`.
`<feature-id>` is the same name in kebab-case, for example `advanced-atlaser`.

| Thing | Convention | Example |
|---|---|---|
| Package folder | `Nordics<Feature>/` | `NordicsBlendshapeToggle/` |
| Package id / NDMF `QualifiedName` | `app.nordicwind.<feature-id>` (identical; reverse of the owned domain `nordicwind.app`) | `app.nordicwind.blendshape-toggle` |
| Display name / menu label | `Nordic's <Feature Words>` (with the apostrophe) | `Nordic's Blendshape Toggle` |
| Add Component menu | `NordicWind/Nordic's <Feature Words>` | |
| Tools menu | `Tools/NordicWind/<Feature Words>/<Action>` | `Tools/NordicWind/Advanced Atlaser/Verbose Logging` |
| Runtime assembly | `NordicWind.<Feature>.Runtime` | `NordicWind.TextureWrapper.Runtime` |
| Editor assembly | `NordicWind.<Feature>.Editor` | |
| Optional integration assemblies | `NordicWind.<Feature>.<NDMF\|AAOIntegration\|SDKIntegration>` | |
| Test assembly | `NordicWind.<Feature>.Tests` | |
| Namespaces | `NordicWind.<Feature>` (runtime), `NordicWind.<Feature>.Editor` (all editor assemblies), `NordicWind.<Feature>.Tests` | |
| Public component class | `Nordics<Feature>` (the user-facing type) | `NordicsAvatarStatistics` |
| NDMF plugin class | `Nordics<Feature>Plugin` | `NordicsBlendshapeTogglePlugin` |
| Inspector class | `Nordics<Component>Editor` | `NordicsAtlasOutputEditor` |
| Internal types | Named for what they do, with the feature prefix where it helps (`Atlas`, `Toggle`, `Split`, `AvatarStatistics`); **never** `Nordics` | `AtlasSubmeshBaker`, `SplitMeshReassembler` |
| Test class | `<ProductionClass>Tests` | `SplitMeshReassemblerTests` |
| Serialized fields | `camelCase`; `[SerializeField] private` or `internal` unless other assemblies need public access | `collapsedScale` |
| Public properties/methods | `PascalCase` | |
| Log, warning and exception prefix | `[Nordic's <Feature Words>]` | `[Nordic's Submesh Splitter] Reassembly: …` |
| EditorPrefs keys | `NordicWind.<Feature>.<Setting>` | |
| Asmdef version define | `NORDIC_<FEATURE>_HAS_<DEP>` | `NORDIC_ADVANCED_ATLASER_HAS_AAO` |
| Generated asset | `<generated folder>/<Feature>/<SourceObject>_<Kind>.asset` | `Body_SplitMeshes.asset` |

**One Unity object class per file, with matching names.** Every `MonoBehaviour` and
`ScriptableObject` lives in a file named exactly like the class. Unity finds the script
through the file. When the names don't match, assets are saved with a missing script and
won't load after an editor restart. That is how the Splitter lost track of its own
mesh containers before 0.2.19.

**Exceptions:**
- **Texture Wrapper** uses one namespace, `NordicWind.TextureWrapper`, for its whole API,
  including the editor exporters, so callers need a single `using`.
- **Advanced Atlaser** serialized fields are still `PascalCase` public fields. Convert them to
  `camelCase`, using `[FormerlySerializedAs]` and updating every `FindProperty` string, at
  the next structural change to its configuration model.

**Renaming serialized fields or script files:** use `[FormerlySerializedAs]` and keep
the script's `.meta` GUID. Without both, existing avatars lose their settings or the
component reference goes missing. Rename a published NDMF `QualifiedName` only together
with every `AfterPlugin`/`BeforePlugin` string that refers to it. Renaming an assembly is a
breaking change for every package that references it, so bump the minor version.

## 2. Folder layout

```
Packages/<package-id>/        a VPM package: package.json, README.md, CHANGELOG.md, LICENSE.md
  Nordics<Feature>/           one tool (inside a bundle, a tool has no package.json)
    README.md                 user documentation (see §4)
    CHANGELOG.md              the tool's own version history (see §4)
    Runtime/                  serialized components only, no logic
    Editor/                   inspectors, menus, and pure logic
    Editor/NDMF/              plugin registration and build passes
    Editor/<Integration>/     optional, versionDefine-gated assemblies
    Tests/Editor/             NUnit EditMode tests
Development/Nordics<Feature>/ tools that aren't released yet
```

Assets that persist in the user's project go to the folder set under
**Edit → Project Settings → NordicWind** (default `Assets/NordicWind/Generated`), in a
`<Feature>/` subfolder. Never put them inside a package folder, because a VCC-installed
package in `Packages/` is read-only. Debug output, which is Atlaser only, goes outside `Assets/`.

## 3. Design philosophy

1. **Non-destructive by default.** Configuration components implement
   `INDMFEditorOnly`. Build passes change only the NDMF build clone, and source
   meshes, materials and textures stay unchanged. Clone an asset before you change it,
   then register the clone with `ObjectRegistry.RegisterReplacedObject` and
   `context.AssetSaver`.
2. **Clean up after yourself.** Each plugin removes its own components from the clone
   once it has processed them, including disabled ones.
3. **Validate everything, then mutate.** Check the whole avatar before writing
   anything, so a failed build never leaves the clone half-processed.
4. **Fail loudly and helpfully.** When a result would be wrong, stop the build.
   The error says *what* is wrong, *where* (object path) and *how to fix it*. Never fall back silently.
   When you must fall back, report it through NDMF `ErrorReport` as a non-fatal warning.
   Empty `catch { }` blocks are not allowed. A reflection lookup may catch, but it must
   record why the value came back missing.
5. **Name the build phase and the reason.** Each `InPhase(...)` has a one-line comment
   that explains why it runs in that phase. It declares ordering against other tools with
   `AfterPlugin`/`BeforePlugin` and their real `QualifiedName`s.
6. **Depend on as little as possible.** The only allowed hard dependencies are NDMF and
   the VRChat SDK, and only when the tool actually compiles against them. The Texture
   Wrapper depends on neither. AAO, MA, VRCFury and Meshia are always optional: reach
   them through `versionDefines`-gated assemblies, reflection, or NDMF
   `AfterPlugin`/`BeforePlugin` strings. Keep the reflection for each integration in a
   single adapter class.
6a. **Compatible with the ecosystem, dependent on none of it.** Every tool must work
   with VRCFury, Modular Avatar, Avatar Optimizer (AAO), Meshia and similar tools,
   including their optimizations (merging, Mesh Cutter, Trace & Optimize, blendshape
   and parameter optimizers, parameter compression), whether or not they are installed.
   In practice:
   - Choose the NDMF phase deliberately, and declare ordering against these tools
     whenever your output must exist before they run or must read what they produced.
     The Splitter, for example, recombines after Meshia and before AAO.
   - Leave output that later tools can understand: keep the original renderers and
     paths that animations target, register replaced objects with `ObjectRegistry`,
     and keep generated blendshapes and bindings animated so optimizers don't strip them.
   - Read the avatar as it is when your pass runs, not as it was configured. Earlier
     tools may have merged, renamed or removed objects.
   - Some processors run outside NDMF, such as VRCFury's parameter compression, which is
     an SDK callback. Reach those through a late SDK callback, as Avatar Statistics does.
   - When a combination genuinely can't work, block it with a clear explanation, the way
     the Splitter blocks VRCFury's Blendshape Optimizer. Never break the result silently.
   - Test each tool on its own, then with each of these tools, then with all of them together.
7. **Don't tie behaviour to a specific version.** Find SDK and tool features at runtime
   by checking whether a member exists, not by comparing version numbers. Fall back
   gracefully when a member is missing. Version numbers can appear in comments as
   context, never as conditions.
8. **Diagnostics are temporary.** Remove investigation logs, snapshots and audit panels
   once the question is answered. Only the Advanced Atlaser keeps diagnostic output, and
   it stays opt-in through a Tools-menu toggle stored in `EditorPrefs` and runs only on
   avatars that use the component.
9. **Call estimates what they are.** When a value approximates the behaviour of VRChat
   or a shader, the UI says "estimate" and the docs explain how it may differ.
10. **Model meaning, not sources.** Shared data types describe the meaning of a value,
   for example `Lighting.Mode`, not where it came from, for example "Poiyomi 8 lighting".
   Code that knows about a particular source stays at the input boundary.
11. **The inspector and the build agree.** Inspectors edit through
    `SerializedObject`/`SerializedProperty` or `Undo`, so every change can be undone.
    The inspector shows the same validation messages the build pass would raise, by
    calling the same validator.

## 4. Documentation

- **README.md** describes the *current* behaviour only, in this order: one-paragraph
  summary, Requirements, Install, Usage, How it works (NDMF phases), Limitations,
  Verification status. Keep it short enough to read in five minutes.
- **CHANGELOG.md** holds each tool's history, newest first. The current version is one
  entry, `## <version> — unreleased` until it ships (then the release date), with
  **Added / Changed / Fixed / Removed** sections. Older versions may be condensed to a
  one-line summary. Don't write history into the README.
- **Other docs** are named in capitals with underscores, such as `UNIFIED_MATERIAL.md`, and
  describe the current design. They say "since 0.24.0", never "Revision 24".
- **Versioning: SemVer at two levels.** Each tool has its own version and CHANGELOG, used
  for tracking its changes. The *NordicWind - Avatar Tools* bundle has its own version, which
  changes whenever any included tool changes. Don't use "Revision N".
- **Verification status** is a single line at the top of every CHANGELOG entry, either
  `Verified: Unity 2022.3.x, SDK 3.x, NDMF 1.x — EditMode tests + Manual Bake on <avatar>`
  or `Unverified: not compiled in Unity`.
- **Code comments explain *why*.** Leave version numbers out of code comments and log
  strings ("v0.2.3 …"), because git and the CHANGELOG track history.
- `/// <summary>` is required on public types and members of runtime assemblies and on
  any API that another NordicWind package consumes (for example Texture Wrapper).
  Serialized fields that users see get a `[Tooltip]`.
- **AI transparency.** The bundle user guide
  (`Packages/app.nordicwind.avatar-tools/README.md`) holds the *AI use and transparency*
  statement: the policy and how AI was used. The repository README summarizes it and links
  there. Every tool README ends with a short *AI transparency* note that links to it.
  Separately shipped packages (such as the Texture Wrapper) carry a self-contained note. Keep the "How AI was used" table accurate when
  the way of working changes, and never describe AI-written code or docs as hand-written.

## 4a. Packaging for VCC

- **Distribution:** one VCC package, *NordicWind - Avatar Tools*
  (`app.nordicwind.avatar-tools`). It contains the finished tools, each still in its own
  folder with its own assemblies, docs and tests. The Texture Wrapper stays a separate
  library package (`app.nordicwind.texture-wrapper`) that the bundle depends on once the
  Atlaser ships. The Advanced Atlaser joins the bundle when it generates output.
- **License:** MIT. Every package carries `LICENSE.md` and `"license": "MIT"`.
- Every package folder is a VPM package. Its `package.json` contains `name`,
  `displayName`, `version`, `unity`, `description`, `author`, `vpmDependencies` and
  `legacyFolders`. `legacyFolders` maps `Assets\NordicWind\Nordics<Feature>` to that
  folder's GUID, so VCC removes the old copy that was installed in `Assets`.
- Dependencies use **minimum-version ranges only** (`">=1.7.0"`). Don't pin exact
  versions or add upper bounds unless a known breaking release requires one.
  Declare only what the package compiles against.
- Other NordicWind packages are declared the same way
  (`"app.nordicwind.texture-wrapper": ">=0.9.0"`).
- The package code must work from either `Assets/NordicWind/` or `Packages/`. Never
  build paths from the package's own location. Generated assets always go to
  `Assets/NordicWind/Generated/<Feature>/`.
- The Texture Wrapper's public API is a contract. Add overloads or optional parameters
  rather than changing existing signatures, and mark anything you remove `[Obsolete]`
  for at least one minor version before deleting it.

## 5. Testing

- Every package has `Tests/Editor/` with an asmdef named `NordicWind.<Feature>.Tests`.
  Set `autoReferenced: false` and `defineConstraints: ["UNITY_INCLUDE_TESTS"]`.
  Use NUnit tests that the Unity Test Runner discovers, not menu-item self-tests.
- **Keep logic testable.** Put geometry, math and validation in plain static or
  internal classes (like `ToggleGeometry`) that take inputs and return results.
  Build passes should only connect these classes to NDMF.
- **Minimum coverage per tool:**
  1. Each validation rule has a test that it rejects bad input.
  2. The core transformation has numeric tests on synthetic meshes or textures.
  3. A build-pass test runs the pass on a synthetic avatar and then checks the result,
     that the components were removed, and that the source assets are unchanged.
- Test names are sentences in PascalCase that state the expected behaviour, for example
  `DilationCopiesValidNormalAcrossUnusedPixels`. Group them into one test class per
  production class.
- Tests create every object they need and destroy it in `finally` or `[TearDown]`.
  They never load project avatars or write to `Assets/`.
- **Release checklist:** EditMode tests pass, NDMF Manual Bake succeeds on a reference
  avatar, a VRChat Build & Test succeeds, and the CHANGELOG verification line is
  updated. Until those steps are done, the release is marked `Unverified`.

## 6. Code style

- Follow the standard C# layout: spaces after keywords and commas, around operators,
  and one statement per line. Don't compress dense algorithm code onto single lines.
- Use `var` when the type is obvious. Use `$"..."` interpolation for messages.
- Error messages name the object with the full hierarchy path and repeat
  the same `[Nordic's <Feature>]` prefix used in logs.
