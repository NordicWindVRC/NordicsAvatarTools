# Nordic's Avatar Tools

Free, non-destructive NDMF tools for building and optimizing VRChat avatars in Unity. They
work together with Modular Avatar, VRCFury, Avatar Optimizer and Meshia without depending
on any of them.

**Using the tools?** Start with the
**[user guide](Packages/app.nordicwind.avatar-tools/README.md)**. It covers installation,
what each tool does, and getting started.

## Packages

| Package | Contents | Status |
|---|---|---|
| [`app.nordicwind.avatar-tools`](Packages/app.nordicwind.avatar-tools/README.md) | [Submesh Splitter](Packages/app.nordicwind.avatar-tools/NordicsSubmeshSplitter/README.md), [Blendshape Toggle](Packages/app.nordicwind.avatar-tools/NordicsBlendshapeToggle/README.md), [Avatar Statistics](Packages/app.nordicwind.avatar-tools/NordicsAvatarStatistics/README.md) | Preparing first release |
| [`app.nordicwind.texture-wrapper`](Packages/app.nordicwind.texture-wrapper/README.md) | Texture-processing library for tool developers ([API](Packages/app.nordicwind.texture-wrapper/API.md)) | Installed as a dependency |
| [`app.nordicwind.advanced-atlaser`](Packages/app.nordicwind.advanced-atlaser/README.md) | Nordic's Advanced Atlaser: material atlasing and optimized variants | Beta |

## AI use and transparency

The tools' code and documentation were written together with AI, under my direction. The
statement is in the [user guide](Packages/app.nordicwind.avatar-tools/README.md#ai-use-and-transparency).

## Repository layout

```
Packages/
  app.nordicwind.avatar-tools/      VCC bundle: package.json, user guide, bundled tools
    NordicsSubmeshSplitter/         each tool: Runtime, Editor, Tests, README, CHANGELOG
    NordicsBlendshapeToggle/
    NordicsAvatarStatistics/
  app.nordicwind.texture-wrapper/   separate VCC package (dependency)
  app.nordicwind.advanced-atlaser/  separate VCC package
DESIGN_GUIDELINES.md                conventions for naming, compatibility, docs and tests
```

Each tool keeps its own SemVer version and changelog, and each package has its own version.

## License

[MIT](LICENSE.md)
