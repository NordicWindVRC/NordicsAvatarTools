# Nordic's Avatar Statistics

See your avatar's **VRChat performance rank** and what is driving it, without uploading.
The report is based on the avatar as it will be uploaded, after every other tool (Modular
Avatar, VRCFury, Avatar Optimizer and so on) has done its work, not on the unprocessed avatar
in your scene.

The goal is to match what VRChat shows in-game as closely as possible. Most values do match
exactly. A few, such as texture memory, can differ slightly; see
[How accurate is it?](#how-accurate-is-it).

## Before you start

You need a VRChat avatar project with **NDMF**. If you already use Modular Avatar or VRCFury,
you have it.

## Quick start

1. Select your avatar's top object, the one with the **VRC Avatar Descriptor**.
2. Click **Add Component** and choose **NordicWind → Nordic's Avatar Statistics**.
3. Build the avatar in one of these ways:
   - right-click the avatar and choose **NDM Framework → Manual bake avatar** (quickest)
   - enter **Play Mode**
   - use **Build & Test** or **Build & Publish** in the VRChat SDK
4. Open **Tools → NDM Framework → Show NDMF Console** and expand
   **Nordic's Avatar Statistics — Build Results**.

The component has no settings and is removed from the uploaded avatar.

## Reading the report

- **Summary tiles:** triangles, meshes, material slots, materials, textures, blendshapes, the
  estimated **PC and Quest rank**, and the texture memory your avatar uses on the GPU.
- **PC and Quest performance:** every VRChat performance category, each with its value, a
  colour bar from Excellent to Very Poor, and the limit for each rank. Categories at zero
  are hidden, but still count toward the rank. On Quest, components VRChat removes are marked
  *Stripped*, and hard limits are shown.
- **Parameter budgets:** how much of the 256-bit synced parameter memory and of the custom
  parameter limit you use, measured *after* tools that compress parameters (such as VRCFury's
  parameter compressor). When compression made a difference, the size before compression is
  shown too.
- **Meshes, Materials and Textures tabs:** every item with its details. Click **Ping … in Project**
  to find the file.

### Face tracking rows

*Face Tracking Operations* and *Forwarded Parameters* appear when the built avatar has native
face tracking turned on. Some tools turn it on during the build even when it's off on your
avatar descriptor, for example VRCFury's *Visemes* and *BlendShape Link* features. The row then
says so. The cost is real in-game, so it still counts.

## How accurate is it?

Values that VRChat's own SDK can calculate come straight from the SDK: PhysBone collision
checks, constraints, face tracking and bounds. Everything else is counted from the built
avatar, the same way VRChat counts it. In testing, every category matches what VRChat shows
except **texture memory**. That one is very close but not exact, because VRChat's
calculation includes overhead it doesn't document.

## Troubleshooting

| Problem | What to do |
|---|---|
| No report in the NDMF Console | Check that the component is on the object with the **VRC Avatar Descriptor**, then build again. |
| A row shows *N/A* | The installed VRChat SDK couldn't calculate that value. Updating the SDK usually fixes it. |
| Numbers differ from what VRChat shows | Texture memory can differ slightly (see above). For anything else, report it with the values from both. |

## Limitations

- Inactive objects are counted too.
- Every texture assigned to a material counts, even if the shader doesn't use it.
- Tools that change the avatar at the very end of the build, after this report, aren't
  included.

## Technical details

<details>
<summary>When and how the statistics are collected</summary>

- Collection runs in NDMF **PlatformFinish**, after the regular optimization phases, on the
  build copy.
- A late VRChat SDK callback (order `int.MaxValue - 10`) refreshes the parameter budgets after
  processors that run outside NDMF, such as VRCFury's parameter compressor at
  `int.MaxValue - 100`.
- PC texture memory uses Standalone importer settings and Quest uses Android settings, both
  with GPU-format mip-chain sizes. Generated textures, textures that aren't `Texture2D` and
  unsupported importer formats fall back to their current allocation, and the report says
  how many did.
- Newer SDK features are detected by checking whether they exist, not by version number, so
  older SDKs keep working.

</details>

## Version and status

See `CHANGELOG.md`. The EditMode tests are in `NordicWind.AvatarStatistics.Tests`.

## AI transparency

This tool was designed, specified and tested by NordicWind. Its code and documentation were
written together with AI. See [AI use and transparency](../README.md#ai-use-and-transparency)
for the full statement.
