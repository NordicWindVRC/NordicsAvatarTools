# Nordic's Advanced Atlaser — Matching by look (planned design)

A planned extension of the output design in [ATLAS_OUTPUT.md](ATLAS_OUTPUT.md): how several
features build one map, how a source's look is measured and compared with the output's, and
how matcaps are placed and tuned with that measure. None of it is built yet.
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

### What it replaces

A map request holds one evaluation function today, and each feature that changes the map
wraps the function it finds. The base color is built this way by seven features: the color
feature creates it, and live global masks, glitter, baked matcaps, matcap fits, normal-map
shading and baked occlusion wrap it, in the order they resolve. Each wrapper has to know
whether the map holds the shared color, whether a step that rescales values above 1 is
installed, and what ran before it.

Two cases follow from this for baked matcaps, where every source has the same color and
nothing is layered on it: a matcap that adds isn't rescaled above 1, and it is added to the
color without the shared tint. Both were found by reading the code; the first phase covers
them with tests.

An output with one color map (VRChat Mobile Diffuse) puts nearly every feature into that map.

### A map is a list of contributions

A map request holds an ordered list of contributions. A feature adds one instead of wrapping:

| A contribution declares | For |
|---|---|
| Its feature and a label | Logs, timings and the overview |
| Its stage | The order (*Stages*, below) |
| Its sources | Which sources it applies to |
| Its kind: content, bake or shift | What the blocked view and the bounds on shifts count |
| Whether it can raise values | Whether the map's range step is needed |
| What it reads | Other maps at a stage, the world normal, the mesh's sections: made once per source and released when the last reader is done |
| How it applies | A change of the source's content in place |

A feature that writes into several maps (glitter into emission, metallic and base color) adds
one contribution to each, and they share what they prepare per source.

### Stages

Contributions run by stage, then by feature order, whatever order the features resolved in.
The stages follow *Order of baking*:

1. **Content:** the map's own value from the source (the color stack for the base color).
2. **Surface edits:** layers and adjustments baked into the map.
3. **Lighting-dependent bakes:** normal-map shading, matcaps, rims and occlusion, each mixed
   in as it is made.
4. **Shifts:** values moved for a live layer (matcap fits, level compensation).
5. **Final adjustments.**

The steps that features register today, to run after every feature has resolved or just
before the material is written, become stages and tuning (*The look model*, below).

### The map owns its range

- While contributions run, the content holds the **full value**, the shared color included,
  so every contribution sees the surface's real color.
- When they are done, the map splits the value once. A color that every source shares stays
  in the output's color and the map keeps the textures' own values, as today, so same-shader
  round trips are unchanged.
- Values above 1 are handled once, by the map: it is divided by its brightest value, which
  goes into the value that can carry it (the base color's color, the emission's strength). A
  map with nothing to carry it is limited smoothly (*A limit must never step*), and the log
  says so.
- The map knows which contribution raised its range and by how much, so that is reported and
  can be bounded (*Base color shifts*, below).

### Reading another map

A contribution or a fit that needs a map's value partway (the base color before the matcaps,
or after the surface layers) asks the map for its value at a stage, at the size it needs. The
map folds its contributions up to that stage. Within a source's bake the result is shared
between its readers.

### Memory

Pixels are float RGBA, 16 bytes each: 256 MB for a 4096 × 4096 map and 64 MB for a
2048 × 2048 source. The baker keeps working as *Memory budget* says, one texture at a time
and source by source. A contribution changes the source's content in place and releases its
temporaries before the next one runs. The peak stays at one source's content, one
contribution's temporaries and the shared inputs still in use. No contribution is kept as a
full-size layer.

The fits and the look model do need contributions side by side. They read them at sample
size (4 × 4 to 32 × 32 per source), where a whole stack takes a few kilobytes.

---

## The look model

### What it replaces

Three fits carry their own copy of the scenes and their own lighting (`AtlasLightingResponse`,
`AtlasSpecularResponse`, `AtlasRimResponse`). The highlight and rim fits assume plain Lambert
light whatever lighting was chosen, and the matcap fits use no scene. Every scene has a main
light. The lighting model chooses the reference source by comparing one unified setup with
another; how the output shader shows the result isn't measured.

### Scenes

One shared set, used by every fit:

| Scene | Main light | Ambient | Why |
|---|---|---|---|
| Bright | 1 | 0.3 | A lit world |
| Dim | 0.4 | 0.15 | The same at low light, where floors and limits start to show |
| Probes only | None | To be set, with a direction | Each shader turns the probes into light its own way, so shaders differ most here |
| Dark | None | To be set, low | The floors (minimum brightness, minimum light) decide the look |

The unified lighting records what each shader does without a main light, for every parser
(two parsers record it today), so the reference look can follow it.

### The reference look

One evaluator computes a source's look from its unified material, at sample points, in the
unified evaluation stages (S1–S7): the surface inputs, the lighting family with highlights and
reflections, the layers that follow the light, the overlays (matcaps over the lookup disc,
rims over the facing angle), emission and the final color. It is the unified definition in
executable form. The three models above become its parts and take their light from it. What
it doesn't cover yet is listed in one place.

Sample points are a source's base color samples with the values at the same places (masks,
roughness, metallic), in each scene, over the angles to the light and the view.

### The kept material

*Written materials are read back* compares the lighting a writer wrote with what the plan
asked for. The look model uses the same idea for every feature. The **kept material** is the
output unified material, written by the writer and parsed back by the output shader's own
parser: what the shader will show, in unified terms. The output's look is the reference look
of the kept material.

- An output shader is described once, by its parser. Formulas that describe a shader in the
  capabilities today (`SpecularFresnel`, `ReflectanceFade`, `RimAddsAfterLighting`) become
  unified modes that its parser sets and the reference look reads. Capabilities keep counts
  and availability (slots, maps, UV channels).
- Every output shader needs a parser. Each current and planned output is an input too.
- A fit evaluates many candidates, so each writer also gives the kept material directly,
  without writing a material (`Kept`). A test holds it equal to the read-back for every feature
  the writer maps.

### Differences

- Two looks are compared as today: displayed colors, the mean absolute difference, weighted
  by the sources' areas (*Fitting what the output lacks*).
- A **layer** (a matcap, a rim) is compared by its effect, the look with it minus the look
  without it, per sample point:
  - **kept:** what the output's effect and the source's have in common (the same direction,
    up to the source's size);
  - **missing:** what the source shows and the output doesn't;
  - **added:** what the output shows and the source doesn't.
- A layer's **cost** is the mean of missing + 2 × added. Something the artist didn't put there
  reads as a fault, while a weaker effect reads as a subtler material, so added counts double.
- Leaving a layer out costs its impact. A way of showing it is used only when it costs less:
  when it keeps more than twice what it adds.

### Tuning

1. The writer maps the output unified material by parameters, as today.
2. The difference between each source's reference look and the output's is measured per
   scene. Where it is within tolerance nothing is changed, so an exact mapping stays exact.
3. Otherwise the values the writer declares **tunable** are fitted, starting from the mapped
   ones: shared values across the sources (the ramp, shadow lift and tint, floors and limits,
   reflectance, slot strengths), and values in maps per source (mask levels, level
   compensation).
4. A fitted value replaces the mapped one only when it looks closer by a margin, so a result
   doesn't flip on a small change.
5. The difference left per source and scene is logged and shown in the overview.

**Level compensation** gives a source its level back through its base color, where the output
can't: a shift for a source that shares another's lighting (bounded as *Base color shifts*,
below, says), a bake for a layer that loses part of its effect (*Average in color*, below).

### Look values

Each source's unified material carries its look values: its level and contrast per scene,
and per view layer its role, impact and direction (*What a matcap is for*, below). They are
derived by the reference look from the material's own values and kept with the source
analysis cache. Automatic choices, the overview, the log and the tests read them. A parser
never writes them: a shader that looks different from its unified material needs a parser
fix or a field in the model.

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

1. **Look values are derived only.** Proposed: parsers never write them. The alternative is a
   parser-supplied correction where a shader isn't fully modeled, which would let shader
   behavior bypass the model.
2. **Added counts double.** The factor 2 is a starting value with the reason given in
   *Differences*. A higher factor leaves more out; a lower one approximates more.
3. **Views and normals.** Proposed: a sphere of normals, seen from the virtual view and from
   around the avatar in 45° steps, so the inspector and the build agree. The mesh's own
   normals would be closer, but the inspector doesn't have them.
4. **The new scenes' values.** The ambient level and direction for *Probes only*, and the
   level for *Dark*.
5. **The blocked view's weight.** Proposed: under *Automatic* it counts as much as the look
   with the shader.
6. **Shifts within range.** Proposed: under *Automatic* no texel is raised above 1.
7. **Average in color.** Which roles it is offered for (proposed: lighting, tint and
   reflection), and whether it needs a control of its own.
8. **Level compensation for lighting.** Whether a source's base color may be scaled to give
   it the level its own lighting had, bounded like matcap shifts.
9. **Roles.** The limits that separate them. Whether a rim matcap may use a rim slot, and a
   highlight or reflection matcap the output's own highlights and reflections, where no matcap
   slot is left.
10. **Order.** Whether map contributions are built before the Poiyomi writers of M3 or after.
