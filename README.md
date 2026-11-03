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
| [Nordic's Advanced Atlaser](Development/NordicsAdvancedAtlaser/README.md) | Material atlasing and optimized variants | In development, not released |

## AI use and transparency

> **In short:** I designed these tools and made every decision about what they do and how.
> The code and documentation were written together with AI, under my direction, and I
> reviewed and tested them myself in Unity on real avatars. The tools themselves don't use
> AI and never send data anywhere.

The full statement, including how AI was used, is in the
[user guide](Packages/app.nordicwind.avatar-tools/README.md#ai-use-and-transparency).

## Repository layout

```
Packages/
  app.nordicwind.avatar-tools/      VCC bundle: package.json, user guide, bundled tools
    NordicsSubmeshSplitter/         each tool: Runtime, Editor, Tests, README, CHANGELOG
    NordicsBlendshapeToggle/
    NordicsAvatarStatistics/
  app.nordicwind.texture-wrapper/   separate VCC package (dependency)
Development/
  NordicsAdvancedAtlaser/           unreleased work
DESIGN_GUIDELINES.md                conventions for naming, compatibility, docs and tests
```

Each tool keeps its own SemVer version and changelog, and each package has its own version.

## License

[MIT](LICENSE.md)
