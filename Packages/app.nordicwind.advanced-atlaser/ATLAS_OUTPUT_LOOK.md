# Nordic's Advanced Atlaser — Matching by look (planned design)

A planned extension of the output design in [ATLAS_OUTPUT.md](ATLAS_OUTPUT.md): how a
source's look is measured and compared with the output's, how matcaps are placed and tuned
with that measure, and what remains of building maps from contributions. It isn't built yet.
[ATLAS_OUTPUT_PLAN.md](ATLAS_OUTPUT_PLAN.md) lists its phases. When a phase is built, its part
of this document moves into the output design and leaves this one.

Names in *italics* are sections of the output design, or of this document where marked
*below*. Shader behavior stated here was read in the shaders' sources and in VRChat's
documentation of its shader fallback system.

---

## Goals

- **The same look in every world.** An atlased avatar is as bright as the original and has
  the same contrast under a bright main light, a dim one, light probes alone and near
  darkness, whichever shader families the sources and the output belong to.
- **Parameters first, look second.** Values are carried over by meaning, as today, and a
  source the output can reproduce keeps its exact values (*Principles*). Matching by look adds
  to that: it measures what the mapping lost and tunes the controls the output has left.
- **A matcap right, or not there.** Matcaps change the look more than most features, and one
  shown wrongly (a highlight where there was none, a part twice as bright) is worse than one
  left out. Every way of showing a matcap is measured before it is used.
- **One owner per map.** Where several features write into one map, the map decides the
  order, the range and the shared color once, so no feature needs to know what the others did.
- **No more memory.** Build memory stays within *Memory budget*.
- **Linear growth.** A new output shader adds its writer and uses its parser. Shared features
  get no new branches for how its shader computes something.

## Terms

| Term | Meaning |
|---|---|
| **Look** | The displayed (sRGB) color of a point on the surface in a scene, from a view |
| **Reference look** | A source's look, computed from its unified material |
| **Kept material** | The output unified material as the output shader shows it, in unified terms |
| **Contribution** | One feature's change to one map |
| **Bake** | A contribution that puts a look into a map: the map still looks right on its own |
| **Shift** | A contribution that moves a map away from the surface's own value so that a live layer brings it back: right only together with that layer |
| **Blocked view** | What a viewer who has blocked the avatar's shaders sees |
| **Impact** | How much a layer changes the look: the mean difference between the look with it and without it |

---

## Map contributions

Maps are built from contributions: see *Map contributions* in the output design. What remains
planned:

- **Reading a map at a stage.** A contribution or a fit that needs a map's value partway (the
  base color before the matcaps, or after the surface layers) asks the map for its value at a
  stage, at the size it needs. Within a source's bake the result is shared between its
  readers. Today such readers evaluate the color stack themselves, or take a snapshot.
- **Declared inputs.** A contribution declares what it reads (the world normal, the mesh's
  sections), so shared inputs are made once per source and released when the last reader is
  done.
- **Full values in every carried map.** The emission map is relative to its layer's strength,
  and a feature that raises the strength rescales what the map held (glitter). With the map
  holding full values and dividing by its carrier at the end, as the base color does with its
  shared color, a feature only adds its part.
- **Maps with nothing to carry their range** are limited smoothly (*A limit must never step*),
  and the log says so.
- **Reporting.** The map reports which contribution raised its range and by how much, which
  the bounds on shifts use (*Base color shifts*, below).

The fits and the look model read contributions side by side at sample size (4 × 4 to 32 × 32
per source), where a whole stack takes a few kilobytes; nothing full-size is kept per
contribution.

---

## The look model

Built so far (see *The look model* in the output design): the four scenes, two of them
without a main light, a lighting's level and contrast, the kept material read back from the
written material, the sources listed where the kept lighting looks different from their own,
the lighting's shared values tuned where the output shader shows the chosen lighting
differently, the base color scaled for sources the atlas's lighting shows brighter or darker
than their own, and a layer's cost as a measure. What remains planned:

### The reference look

The look model covers the lighting alone today. The highlight and rim fits assume plain
Lambert light whatever lighting was chosen, so they use the scenes with a main light only,
and the matcap fits use no scene.

One evaluator computes a source's look from its unified material, at sample points, in the
unified evaluation stages (S1–S7): the surface inputs, the lighting family with highlights and
reflections, the layers that follow the light, the overlays (matcaps over the lookup disc,
rims over the facing angle), emission and the final color. It is the unified definition in
executable form. The fits' own models become its parts and take their light from it. What it
doesn't cover yet is listed in one place.

Sample points are a source's base color samples with the values at the same places (masks,
roughness, metallic), in each scene, over the angles to the light and the view.

### The kept material

The kept material is read back after writing today, and only its lighting is compared. For
tuning, the kept lighting is read from a scratch material before the atlas's material exists.

- **Every feature.** The output's look is the reference look of the kept material, compared
  with each source's reference look.
- **Formulas leave the capabilities.** Formulas that describe a shader in the capabilities
  (`SpecularFresnel`, `ReflectanceFade`, `RimAddsAfterLighting`) become unified modes that its
  parser sets and the reference look reads. Capabilities keep counts and availability (slots,
  maps, UV channels). Every output shader needs a parser for this; each current and planned
  output is an input too.
- **Beyond the lighting.** The scratch material holds the lighting alone. A fit of another
  feature (a matcap, a rim) reads its kept part the same way, with that feature's part of the
  output unified material written to it.

### Differences

Layers are compared by their cost (*Comparing layers* in the output design). Matcaps and rims
choose by it (*Matcaps*, below).

### Tuning

The lighting's shared values are tuned (*Tuning the lighting* in the output design). What
remains planned:

- **The ramp's shape.** For an output that shades through a generated ramp texture, the
  ramp's keys are fitted to the chosen lighting's shading over the angle to the light (a
  wrapped or physically based source on Toon Standard), instead of the nearest preset. The
  fit works at the ramp texture's resolution, with the source's ramp read as finely, so a
  sharp edge stays sharp.
- **Other shared values:** reflectance and slot strengths, with the features that own them.
- **Values in maps per source:** mask levels, and a bake for a layer that loses part of its
  effect (*Average in color*, below), next to the level compensation for the lighting (*Level
  compensation* in the output design).

### Look values

A lighting's level and contrast are computed where they are needed, from the material's own
values: for the build's log, and for the inspector's line under *Lighting from*. What remains
planned: per view layer its role, impact and direction (*What a matcap is for*, below),
derived the same way by the reference look and kept with the source analysis cache where
computing them takes long. A parser never writes look values: a shader that looks different
from its unified material needs a parser fix or a field in the model.

---

## Matcaps

### What a matcap is for

*Reading intent from content* sorts matcaps into effects and environment matcaps by one
measure. Matcaps serve more purposes than two, and the purpose decides which ways of showing
one are acceptable. Each matcap is measured as its slot looks it up, on a sphere of normals:

| Measure | Meaning |
|---|---|
| Impact | How much it changes the look on its sources |
| Direction | Whether it brightens, darkens or both, and each side's share |
| Bake loss | The cost of freezing it at the virtual view, seen from around the avatar |
| Spread | The share of its light in its brightest tenth (today's measure) |
| Evenness | How close it is to one color over the disc |
| Edge weight | How much of it lies at the disc's edge, alike all around |

The **role** follows from the measures:

| Role | What it does | Measured as | Can share a slot | Can be baked |
|---|---|---|---|---|
| Tint | Colors the surface | Even | Yes | Yes |
| Lighting | Stands in for the world's light: soft shading, sky and ground | Low bake loss, low spread | With other lighting matcaps | Yes |
| Rim | Lights or darkens the edges | Edge weight, alike all around | On the same parts | No |
| Highlight | Gloss, shine, glints | High spread | On the same parts, with the same lookup | No |
| Reflection | Shows surroundings, metal | The reflection projection, or detail over the whole disc | On the same parts, with the same lookup | No |

The role names the matcap in the inspector and the log, and sets which ways are offered.
Among those, the cost decides. The rules in use today (an effect keeps its own slot, a
highlight isn't baked by *Automatic*) follow from the cost: a highlight frozen by a bake is
missing where it should be and added where it stayed. They are kept as tests.

### Brightening, darkening or both

- In unified terms, add, screen and mixed brighten, multiply darkens while its color is at
  most 1, and replace goes either way with the base color. A matcap with several weights, and
  matcaps stacked on the same parts, often do both.
- An output slot with every blend and weights (Poiyomi, as its writer declares) shows them as
  they are.
- An output slot with one blend at a time shows one side. Toon Standard's slot changes the
  base color before lighting, by adding the matcap × the strength or by multiplying with
  lerp(1, matcap, strength). The strength is 0–1 times the mask, and there is no tint, so its
  multiply can only darken and its add can only brighten.

### Ways to show a matcap

Every way the output offers is a candidate with a cost:

| Way | What it does | Base color |
|---|---|---|
| Slot | The matcap as it is | Unchanged |
| One side | The slot's nearest blend; the other side is lost | Unchanged |
| Two slots | One slot multiplies and one adds | Unchanged |
| Shifted base | The base color is raised and the slot multiplies it down, or lowered and the slot adds to it | Shift |
| Combined slot | One generated texture for several matcaps | Unchanged |
| Baked | Drawn into the color and the emission from the virtual view | Bake |
| Average in color | Its average effect over the views goes into the base color; the variation is lost | Bake |
| Left out | Nothing of it shows | Unchanged |

*Average in color* can accompany a way that loses part of the effect (one side, left out). It
keeps the part as bright as it was, which is most of what a lighting matcap does.

### Base color shifts

A shift is the only way to show both sides in a slot with one blend. It covers a range: no
shift is *One side*, and the full shift raises the base color to the brightest the matcap
makes it. Today's fit always takes the full shift. A shift has side effects, and each is
bounded:

- **The blocked view** shows the shifted map without the slot (*The blocked view*, below).
- **Every feature that reads the base color** reads the shifted one: the base color mixes of
  rims and emissions, shadow colors taken from the base color, outlines. They are part of the
  kept material's look, so the cost counts them.
- **Range.** A shift above 1 goes into the shared color and takes levels from the whole atlas
  (*Small values need range*). Under *Automatic* a shift stays within the map's range: a
  texel is raised to 1 at most.

### The blocked view

VRChat draws an avatar whose shaders a viewer has blocked with a fallback shader, chosen by
the material's `VRCFallback` tag, and copies the standard properties to it: the main texture
and color, the emission, the normal map and others. A matcap slot isn't among them.

| The output's fallback | The blocked view shows |
|---|---|
| The output shader itself (Toon Standard: its tag names it, and every property is copied) | The same look |
| Toon, Unlit or Standard (the choices of a Poiyomi output) | The base color map × the color, without the view layers |
| Hidden | Nothing |

Where the blocked view shows the base color alone, a map is acceptable anywhere between the
surface's own color and its average look with the shader. A bake stays within that range. A
shift beyond it counts as a difference in the blocked view: the avatar shows too bright with
a raised base, too dark with a lowered one. With the other two fallbacks a shift costs
nothing there.

### Automatic

1. Each matcap group is measured: role, impact, direction.
2. Groups without impact are left out, as today.
3. Groups take slots in the order of their impact. Each takes the way with the lowest cost
   among those its role and the output offer, with the slots that are left. The cost is the
   look's, plus the blocked view's difference where a shift is used.
4. A group without a slot of its own is combined, baked, kept as its average or left out,
   whichever costs least.
5. The log names each group's role, the way it is shown and how much of it is kept. The
   inspector shows the same next to the group's placement.

### Controls

- **Placement** per matcap, as today: *Automatic*, its own slot, combined with another,
  baked, off.
- **Base color shift**, shown while *Automatic* would shift and the blocked view shows the
  base color:
  - *Automatic*: the blocked view counts as much as the look with the shader;
  - *Keep the base color*: no shifts;
  - *Full*: the look with the shader alone decides, and the range bound is lifted.

Rims use the same cost for their fits (`AtlasRimResponse` today).

---

## Open questions

1. **Views and normals.** Proposed: a sphere of normals, seen from the virtual view and from
   around the avatar in 45° steps, so the inspector and the build agree. The mesh's own
   normals would be closer, but the inspector doesn't have them.
2. **The new scenes' values.** In place as starting values: *Probes only* at 0.5 on average
   with 0.25 more along the dominant direction, *Dark* at 0.02. To be confirmed.
3. **The blocked view's weight.** Proposed: under *Automatic* it counts as much as the look
   with the shader.
4. **Shifts within range.** Proposed: under *Automatic* no texel is raised above 1.
5. **Average in color.** Which roles it is offered for (proposed: lighting, tint and
   reflection), and whether it needs a control of its own.
6. **Roles.** The limits that separate them. Whether a rim matcap may use a rim slot, and a
   highlight or reflection matcap the output's own highlights and reflections, where no matcap
   slot is left.
