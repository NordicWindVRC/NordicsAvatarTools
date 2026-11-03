# Nordic's Blendshape Toggle

The usual way to toggle clothing or props on an avatar is to turn their objects on and off.
Optimization tools often merge those objects into one mesh to improve performance, and then
that kind of toggle no longer works.

The Blendshape Toggle solves this. It adds a **blendshape** (a shape slider, like the ones used
for facial expressions) that shrinks one part of a mesh down to an invisible point. You toggle
the part by animating that blendshape, and it keeps working after merging.

## Before you start

- You need a VRChat avatar project with **NDMF**. If you already use Modular Avatar or VRCFury,
  you have it.
- The part you want to toggle must have **its own material slot** on the mesh. If the whole
  outfit uses one material, the tool can't tell the pieces apart.
- The mesh must have **Read/Write** enabled. If it doesn't, the build tells you. Select the
  model file, and in the Inspector's **Model** tab tick **Read/Write**, then click **Apply**.

## Quick start

1. Select the object with the mesh, the one with the **Skinned Mesh Renderer**.
2. Click **Add Component** and choose **NordicWind → Nordic's Blendshape Toggle**.
3. Choose the **Material Slot** of the part to toggle.
4. Give it a **Blendshape Name** that's unique on this mesh, for example `Hide Jacket`.
5. Choose how it should start:
   - **Reverse Blendshape off** (default): the part is **visible** at 0 and **hidden** at 100.
   - **Reverse Blendshape on**: the part starts **hidden** (0) and is **shown** at 100. Good
     for optional items such as a backpack. Name it after showing, for example `Show Backpack`.
6. Animate it. For example, in a **VRCFury Toggle**, add a **BlendShape** action, type the exact
   name and set the value to **100**.

Unity's blendshape picker won't list the name, because the shape only exists in the built
avatar. Type the name exactly as you wrote it in the component.

You can add several Blendshape Toggles to one object, one per part.

### Where the part shrinks to

The **Center Mode** decides where the part collapses:

| Center Mode | Collapses to |
|---|---|
| Submesh Center (default) | The middle of the part itself |
| Mesh Center | The middle of the whole mesh |
| Transform | The object you drag into **Target Transform**, for example a bone hidden inside the body |

Pick a point **inside** the avatar, so the collapsed part can't be seen. **Collapsed Scale**
controls how small it gets. The default, 0.001, is right for nearly everything.

## Common tasks

| I want to… | Do this |
|---|---|
| Toggle part of a mesh that the Submesh Splitter split | Add the Blendshape Toggle to the **original** object (for example Body), not to a split part. Pick the part's material slot and animate the original object as usual. |
| Toggle with Modular Avatar | Use an MA animation, such as a Shape Changer or an animator, that sets the blendshape on the object the toggle is on. |
| Check the result before uploading | Right-click the avatar and choose **NDM Framework → Manual bake avatar**, then try the blendshape on the baked copy. |

## Troubleshooting

| Problem | What to do |
|---|---|
| Build stops: *shares vertex … with submesh …* | The part is connected to another material's part. Separate them in your 3D program, or split the mesh with the Submesh Splitter and toggle the part there. |
| Build stops: *Blendshape name already exists* | Choose a name the mesh doesn't already have. |
| Build stops: *Mesh is not readable* | Enable **Read/Write** on the model (see *Before you start*). |
| The toggle does nothing in-game | Check that the animation uses the **exact** name and targets the object the component is on, or the original object if the mesh was split. |
| A tiny speck is visible when hidden | Use **Transform** center mode with a bone inside the body. |
| The part looks distorted in some poses | Parts whose vertices follow different bones can spread out when the avatar moves. Toggle parts that are weighted to one bone, such as props, give the cleanest result. |

## Works with

Modular Avatar, VRCFury (including Blendshape Optimizer), Avatar Optimizer, Meshia and Nordic's
Submesh Splitter. None of them are required. Avatar Optimizer may remove a blendshape that
nothing animates, so make sure an animation uses it.

## Limitations

- The part's polygons are still there when hidden, so performance ranks don't improve. It
  hides parts, it doesn't remove them.
- A reversed toggle must be the only toggle on its material slot.
- Tools that need the toggle's shape before the late build stage, such as an MA Mesh Cutter
  that selects vertices by this blendshape, only see an empty placeholder.

## Technical details

<details>
<summary>How the toggle works during a build</summary>

1. **NDMF Generating:** every toggle on the avatar is validated, and the animated renderer gets
   an **empty** blendshape with the toggle's name. MA and VRCFury can then find and animate it.
   The collapse center is recorded in mesh space.
2. **NDMF Optimizing, after Meshia and before the Submesh Splitter and AAO:** the real geometry
   is written onto the renderer's **processed** mesh, replacing the placeholder in place.
   Simplifiers and cutters never see a reversed toggle's collapsed geometry, and the shape
   matches the final vertices.

Each step clones the mesh and registers it with NDMF's ObjectRegistry, so later tools can
follow the replacement. The source mesh is never changed. On a split renderer, the geometry
goes onto that slot's split part, and the original keeps the placeholder.

</details>

## Version and status

See `CHANGELOG.md`. The EditMode tests are in `NordicWind.BlendshapeToggle.Tests`.

## AI transparency

This tool was designed, specified and tested by NordicWind. Its code and documentation were
written together with AI. See [AI use and transparency](../README.md#ai-use-and-transparency)
for the full statement.
