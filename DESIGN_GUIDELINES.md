# NordicWind Component Design Guidelines

Rules shared by every NordicWind tool (Advanced Atlaser, Avatar Statistics,
Blendshape Toggle, Submesh Splitter, Texture Wrapper, and anything new).
Where existing code differs, follow this guide for new work and fix the old code when
you next touch it. Each rule names its exceptions.

---

## 0. What each tool is for

Each tool's README says what it does and decides; the [repository README](README.md) lists the
packages. Every tool depends only on NDMF and the VRChat SDK where it can, and on another
NordicWind package only through its public API (each `package.json` lists the dependencies).

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
7. **Don't tie behavior to a specific version.** Find SDK and tool features at runtime
   by checking whether a member exists, not by comparing version numbers. Fall back
   gracefully when a member is missing. Version numbers can appear in comments as
   context, never as conditions.
8. **Diagnostics are temporary.** Remove investigation logs, snapshots and audit panels
   once the question is answered. Only the Advanced Atlaser keeps diagnostic output, and
   it stays opt-in through a Tools-menu toggle stored in `EditorPrefs` and runs only on
   avatars that use the component.
9. **Call estimates what they are.** When a value approximates the behavior of VRChat
   or a shader, the UI says "estimate" and the docs explain how it may differ.
10. **Model meaning, not sources.** Shared data types describe the meaning of a value,
   for example `Lighting.Mode`, not where it came from, for example "Poiyomi 8 lighting".
   Code that knows about a particular source stays at the input boundary.
11. **The inspector and the build agree.** Inspectors edit through
    `SerializedObject`/`SerializedProperty` or `Undo`, so every change can be undone.
    The inspector shows the same validation messages the build pass would raise, by
    calling the same validator.

## 4. Documentation

**Every fact has one home.** Other documents link to it instead of repeating it, so a change
updates one place. Overview documents stay short and leave details to the document that owns
them.

- **README.md** is the overview of what the tool does now: a short summary, requirements,
  install, getting started, limitations, and links to the detailed documents. It describes
  settings only as far as a user needs to find them; the inspector's tooltips explain each one.
  No history, no design detail, no test status. The Advanced Atlaser's README also lists every
  supported input and output shader.
- **CHANGELOG.md** holds each tool's history, newest first. The current version is one
  entry, `## <version> — unreleased` until it ships (then the release date), with
  **Added / Changed / Fixed / Removed** sections. Its first line says how it was verified, for
  example `EditMode tests pass in Unity 2022.3.22. Verified on a build of a test avatar`, or
  that it wasn't compiled in Unity. Older versions may be condensed to a one-line summary.
- **Design documents** are named in capitals with underscores, such as `UNIFIED_MATERIAL.md`,
  and describe the current design in detail, one topic in one place. They say "since 0.24.0",
  never "Revision 24".
- **Plans** list what's next and later. What's done is recorded in the changelog and the
  design, not kept in the plan.
- **API references** (the Texture Wrapper's `API.md`) own every member's behavior; the README
  links to them.
- **Versioning: SemVer at two levels.** Each tool has its own version and CHANGELOG. The
  *NordicWind - Avatar Tools* bundle has its own version, which changes whenever any included
  tool changes. Don't use "Revision N".
- **American English spelling** everywhere: documentation, code comments, identifiers, log
  messages and inspector text ("color", "center", "behavior", "normalize", "gray").
- **Code comments explain *why*.** Leave version numbers out of code comments and log
  strings ("v0.2.3 …"), because git and the CHANGELOG track history. Comments point to design
  sections by name, not to plan phases.
- `/// <summary>` is required on public types and members of runtime assemblies and on
  any API that another NordicWind package consumes (for example Texture Wrapper).
  Serialized fields that users see get a `[Tooltip]`.
- **AI transparency.** The bundle user guide
  (`Packages/app.nordicwind.avatar-tools/README.md`) holds the *AI use and transparency*
  statement. Every other README links to it with a short *AI transparency* note; separately
  shipped packages (such as the Texture Wrapper) carry a self-contained note. Keep the "How AI
  was used" table accurate when the way of working changes, and never describe AI-written code
  or docs as hand-written.

## 4a. Packaging for VCC

- **Distribution:** small, finished tools share one VCC package, the bundle *NordicWind -
  Avatar Tools* (`app.nordicwind.avatar-tools`), each still in its own folder with its own
  assemblies, docs and tests. The Texture Wrapper is a separate library package
  (`app.nordicwind.texture-wrapper`), installed as a dependency. The Advanced Atlaser is a
  separate package (`app.nordicwind.advanced-atlaser`): it's large, changes often and is
  released as a beta, which a bundle can't be on its own.
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
- Test names are sentences in PascalCase that state the expected behavior, for example
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
