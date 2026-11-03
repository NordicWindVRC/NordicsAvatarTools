# NordicWind - Avatar Tools

Free tools that help you build and optimize VRChat avatars in Unity. They do their work only
when you build or upload the avatar, on a temporary copy, so your scene, prefabs and models
are never changed. They work together with Modular Avatar, VRCFury, Avatar Optimizer (AAO) and
Meshia, but don't need any of them.

## What's included

The **NordicWind - Avatar Tools** package contains three tools. Each one is a separate
component that you add only where you need it:

| Tool | Use it to… |
|---|---|
| [Nordic's Submesh Splitter](NordicsSubmeshSplitter/README.md) | Split a mesh into one object per material, so you can hide, simplify or cut individual parts. They're joined back together automatically when you build. |
| [Nordic's Blendshape Toggle](NordicsBlendshapeToggle/README.md) | Keep clothing and prop toggles working after meshes are merged for performance. |
| [Nordic's Avatar Statistics](NordicsAvatarStatistics/README.md) | See your avatar's VRChat performance rank, and what's causing it, before you upload. |

## Getting started

### What you need

- A VRChat avatar project created with the **VRChat Creator Companion (VCC)**, on Unity 2022.3.
- **NDMF**. If your project already has Modular Avatar or VRCFury, you have it. If not, VCC adds
  it for you when you install the tools.

### Install with VCC

> The NordicWind VCC listing is not published yet. These steps apply once it is.

1. Open the listing page and click **Add to VCC**, or in VCC go to **Settings → Packages →
   Add Repository** and paste the listing URL.
2. In VCC, open your avatar project with **Manage Project**.
3. Find **NordicWind - Avatar Tools** in the package list and click **+** to add it.
4. Open the project in Unity. The tools are under **Add Component → NordicWind**.

Updates appear in VCC like any other package.

### Install manually, for now

1. Download the repository and open `Packages/app.nordicwind.avatar-tools/`.
2. Copy the tool folders you want (for example `NordicsSubmeshSplitter`) into your project's
   `Assets/NordicWind/` folder. **Include the `.meta` files**, or Unity loses track of the
   components in your avatars.
3. Make sure NDMF is installed in the project.

Each tool's README explains how to use it, starting with a short *Quick start*.

## Versions

The package has its own version, shown in VCC. Each tool also has its own version and
changelog (`CHANGELOG.md` in its folder), so you can see exactly what changed in each one.

## AI use and transparency

> **In short:** I designed these tools and made every decision about what they do and how.
> The code and documentation were written together with AI, under my direction, and I
> reviewed and tested them myself in Unity on real avatars. The tools themselves don't use
> AI and never send data anywhere.

### My stance

I see AI as a capable tool, not as an author. I use it openly for idea bouncing, code
review, learning, documentation, boilerplate, prototyping, data analysis, project planning
and concept generation, and for building development tools like these.

Where I draw the line depends on what is being made:

- **When I say I developed something, I made it by hand.** AI may have helped along the
  way, with reviews, research, prototypes or tooling, but the finished result is my own work.
- **My creative work, such as VRChat avatars and worlds,** is made by me. AI-assisted tools
  like these may be used while building it, but the result is human-made.
- **Tools I release are the exception, and I say so openly.** Their code can be written
  together with AI, and this section describes exactly how.

### How AI was used in these tools

| Area | Who |
|---|---|
| Purpose, requirements and scope of each tool | Me |
| Architecture and design decisions: the non-destructive build model, the Submesh Splitter's "split parts only for explicit actions" rule, compatibility and error-handling policy, naming and packaging | Me |
| Investigating how NDMF, VRCFury, Modular Avatar, Avatar Optimizer and Meshia behave, by reading their source | Together with AI. Conclusions checked and decisions made by me |
| C# code and EditMode tests | Written together with AI coding assistants (including Claude by Anthropic), directed and reviewed by me |
| Documentation (READMEs, changelogs, design guidelines) | Written together with AI, reviewed and edited by me |
| Testing in Unity, on real avatars combined with other tools, and the final decision to release | Me |

### What this means for you

- The tools are free to use in personal and commercial projects, and I use them in my own
  work. They're provided under the MIT license, without warranty. They are reviewed and
  tested on real avatars, but like any software they may contain bugs: check your builds
  before you rely on them, and please report problems.
- If your project or organization has its own rules about AI-written code, note that the
  tools' code was co-written with AI, as described above. The tools only run in the Unity
  Editor at build time. Nothing from them ships as code in your avatar.
- What you create with these tools is yours. They only process your avatar at build time
  and don't generate any content.

## License

[MIT](LICENSE.md)
