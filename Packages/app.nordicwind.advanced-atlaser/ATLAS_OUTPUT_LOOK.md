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
the lighting's shared values and a generated ramp's shape tuned where the output shader
shows the chosen lighting differently, the base color scaled for sources the atlas's lighting
shows brighter or darker than their own, a layer's cost as a measure, and a view layer's
look in the unified evaluation stages. What remains planned:

### The reference look

Built (*The look model* in the output design): a view layer's look at a sample point, in
the stages S1–S5, which the matcap measures, the rim fit and the rims' costs use. What
remains planned:

- **Highlights and reflections.** The highlight fit has its own model, which assumes plain
  Lambert light whatever lighting was chosen, so it uses the scenes with a main light only.
  With highlights and reflections as part of the lighting stage, it takes its light from
  the reference look.
- **The matcap slot fit** works on the base color before the light, so it uses no scene.
- **Sample points** are a source's base colors alone. With the values at the same places
  (masks, roughness, metallic), read from a map at a stage (*Map contributions*, above),
  one set of points serves every fit.
- **One evaluator from the unified material.** Today each feature describes its layers to
  the reference look. Computed from a source's unified material as a whole, in the stages
  S1–S7, it is the unified definition in executable form, and what it doesn't cover is
  listed in one place.

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

The lighting's shared values and a generated ramp's shape are tuned (*Tuning the lighting* in
the output design). What remains planned:

- **Other shared values:** reflectance and slot strengths, with the features that own them.
- **Values in maps per source:** mask levels, and a bake for a layer that loses part of its
  effect (*Average in color*, below), next to the level compensation for the lighting (*Level
  compensation* in the output design).

### Look values

A lighting's level and contrast are computed where they are needed, from the material's own
values, for the build's log. What remains
planned: per view layer its role, impact and direction (*What a matcap is for*, below),
derived the same way by the reference look and kept with the source analysis cache where
computing them takes long. A parser never writes look values: a shader that looks different
from its unified material needs a parser fix or a field in the model.

---

## Matcaps

### What a matcap is for

Each matcap is measured by its effect and given a role, which the inspector and the log name
(*What a matcap is for* in the output design), and *Automatic* placement follows the role:
highlights are effects, and lighting with a highlight is guarded. What a role can share
follows from the cost (*Comparing layers* in the output design):

| Role | Shares a slot | Baked by *Automatic* |
|---|---|---|
| Lighting | With other lighting matcaps, by what its level and contrast lose | In the baked mode |
| Rim | On the same parts, where the stack is exact; across parts only where that costs less than leaving it out, point by point | In the baked mode |
| Highlight | On the same parts, with the same lookup | In the baked mode, only one that mostly darkens |

The rules in use before (an effect keeps its own slot, a highlight isn't baked by
*Automatic*) are kept as tests.

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
keeps the part as bright as it was, which is most of what a lighting matcap does. Built
(*Base color shifts* in the output design): for every source of a slot shared across parts,
as a placement the user chooses, and for a lighting matcap that ends up without a slot. A
matcap shown on one side is covered by the shift's bounds, which stop at its average look.
*Automatic* chooses it by its cost for a lighting matcap without a slot of its own
(*Automatic*, below).

### Base color shifts

A shift is the only way to show both sides in a slot with one blend. How far it goes, and
what viewers who block the shader see of it, is built (*Base color shifts* in the output
design). What remains planned:

- **Every feature that reads the base color** reads the shifted one: the base color mixes of
  rims and emissions, shadow colors taken from the base color, outlines. They are part of the
  kept material's look, so the cost counts them.
- **A hidden fallback** shows nothing to viewers who block the shader, so a shift costs
  nothing there; it is treated as showing the base color today.
- **Bakes** stay between the surface's own color and its average look with the shader, as
  shifts do under *Automatic*.

### Automatic

1. Each matcap group is measured: role, impact, direction.
2. Groups without impact are left out, as today.
3. Groups take slots in the order of their impact. Each takes the way with the lowest cost
   among those its role and the output offer, with the slots that are left. The cost is the
   look's, plus the blocked view's difference where a shift is used.
4. A group without a slot of its own is combined, kept as its average or left out, whichever
   costs least. A bake is the user's choice in the slots mode: it stays in place from every
   side and uses the virtual light rig's settings. The log names it where it would keep
   clearly more.
5. The log names each group's role, the way it is shown and how much of it is kept. The
   inspector shows the same next to the group's placement.

Built (*Comparing layers* and the matcaps' and rims' rows in the output design): a lighting
matcap's shading (level and contrast) and what each way keeps of it; step 4 for a lighting
matcap between a slot shared across parts and the average in color, and for a rim or a
highlight between a shared slot and being left out, point by point; the same for rims in
rim slots and drawn into matcap slots; and step 5, with the inspector saying how each one
ends up shown. What remains:

- **The order of step 3.** Slots are taken by how visible a matcap is (its area × its
  blends' strength × its mask's average), effects first, not yet by its measured impact.
- **The ways of step 3.** A group takes its own slot as it is, or fitted through the base
  color by the user's *Base color shift*. One side, two slots and a shifted base aren't
  compared by their cost yet, with the blocked view's difference.

### Controls

- **Placement** per matcap, as today: *Automatic*, its own slot, combined with another,
  baked, off.
- **Base color shift**, as built (*Base color shifts* in the output design).

Rims are placed by the same cost, and their fit takes its light from the reference look
(`AtlasRimResponse`).

---

## Open questions

1. **Views and normals.** In place for the matcap measures: a sphere of normals, seen from the
   front and from around the avatar in 45° steps, so the inspector and the build agree. The
   mesh's own normals would be closer, but the inspector doesn't have them.
2. **The new scenes' values.** In place as starting values: *Probes only* at 0.5 on average
   with 0.25 more along the dominant direction, *Dark* at 0.02. To be confirmed. A rim
   colored by the reflection probe takes the scene's ambient light as the probe's color; a
   scene could carry an environment level of its own instead.
3. **The blocked view's weight.** In place: under *Automatic* it counts as much as the look
   with the shader, which stops a shift at the average look. With a lower weight, a matcap
   that is bright on average would shift in full.
4. **Average in color.** In place: *Automatic* keeps the average of lighting matcaps only,
   and the user can place any matcap so. To be confirmed.
5. **Roles.** The limits are checked against one avatar's matcaps and two collections. A lit
   sphere with a strong hot spot comes within 0.05 of the highlight's spread limit, and a
   gold sphere stays lighting; both are judgment calls. Whether a rim
   matcap may use a rim slot, and a highlight the output's own highlights, where no matcap
   slot is left.
6. **The cost of a bake.** In place: a lighting matcap's bake, average and shared slot are
   judged by its level and contrast, not point by point (*Comparing layers* in the output
   design). To be confirmed by eye, with the margin by which a slot that follows the view is
   preferred to the average (a quarter of the matcap's shading).
