# Terrain shape studies

These are reversible top-down studies for the existing `SampleScene` Terrain.
They preserve the scene footprint (`1,000 x 1,000` world units, centered at
`(0, 0, 0)`) and show only walkable shape, route width, and high perimeter
intent. They do not change Terrain textures, trees, details, or the live scene.

The customer map reference is now reflected in these studies:

- the main route runs east-west through the middle and includes bridge
  crossings;
- a north-west camp/save area connects by a branch;
- a central upper shop/structure sits above the main route;
- a north-east tower sits on elevated ground;
- a south-west tower/area connects through branching routes;
- a south-east circular arena/boss area connects from the lower route;
- routes are at least 12 world units wide, leaving room for the existing
  player;
- the outer 60 world units are a raised perimeter to hide the skybox.

## Variants

| File | Layout | Strength | Trade-off |
| --- | --- | --- | --- |
| `variant-a-central-spine.svg` | Straight east-west route with direct landmark branches | Clearest landmark navigation | Fewer route choices |
| `variant-b-s-curve.svg` | Meandering east-west route with connected lower loop | Best pacing and region transitions | Slightly longer travel |
| `variant-c-y-fork.svg` | Central bridge hub with separated lower branches | Strongest choice points | More route balancing |

**Recommendation: Variant B (S-curve).** It gives the clearest sequence of
south entry, central woodland, and north mountain while retaining optional
side loops. It is a study only; do not apply it to the live Terrain until the
customer confirms the map arrangement.

Open the SVG files in a browser or vector editor for the visual comparison.

## River and walkway connection audit

`river-walkway-audit.svg` records the corrected map interpretation used by
the editor tools:

- the deep river occupies normalized `y=0.42..0.58` across the map and is
  never a walkable route;
- only the west (`x≈0.20`), center (`x≈0.50`), and east (`x≈0.80`) bridge
  corridors cross the river;
- the north bank and south bank have separate walkways;
- the NW camp/save area connects to the west bridge;
- the upper shop connects to the center bridge;
- the NE tower connects to the east bridge;
- the SW tower/area connects from the west bridge's south side;
- a central south route reaches the center bridge from the lower island edge;
- the SE arena/boss connects along the south bank through the east bridge
  junction and does not cross the river.

The shared `TerrainWorkflowLayout` definition is used by Shape Preview,
MainTerrain height-route protection, and Surface Paint. Any point inside the
river outside those three bridge corridors returns as non-walkable.

## Visible Variant B preview

The shape-study generator requires an explicitly selected active source
Terrain. With MainTerrain selected, run
`Tools > Terrain Workflow > 02 Shape Preview > Generate Visible Variant B Preview`.
The action regenerates the Variant B TerrainData asset and creates a visible,
grey Variant B Terrain beside the source Terrain. It does not modify the
source Terrain, its size, transform, textures, trees, details, or height data.
Delete the `Terrain Shape Study Preview - Variant B` GameObject or use Undo
when finished reviewing it.

Because the previous generator only created assets, the existing Variant B
asset must be regenerated with this menu action to create the visible preview.

## MainTerrain height design

`mainterrain-height-before-after.svg` shows the height-only design requested
from the latest Unity reference. Select the active MainTerrain and run
`Tools > Terrain Workflow > 03 MainTerrain Height Design > Apply Continuous Mountain Edge`.
This action:

- creates a TerrainData backup and registers Undo;
- keeps the existing S route and interior heights;
- raises a continuous moderate-width edge band;
- uses smooth falloff so it does not form a broad plateau;
- adds low-frequency crest variation along the full edge;
- adds low-amplitude interior undulation away from the S route;
- preserves terrain size, transform, textures, trees, and details.

Unity has confirmed the source TerrainData at
`Assets/TerrainShapeStudies/MainTerrain`. The filesystem scan in this
workspace did not see the binary asset, which indicates an unsaved or import
state mismatch rather than a confirmed Unity-side absence. Select the active
MainTerrain that references this asset. The tool logs the selected Terrain
name, asset path, GUID, size, normalized height ranges, and changed sample
count.

If the saved terrain contains an earlier height result, run
`Tools > Terrain Workflow > 01 Safety and Restore > Restore MainTerrain From Backup`
first, then run the continuous-edge action. The restore menu selects the
oldest matching workflow backup and remains Undoable.

## Reference-layout acceptance checklist

Run `Tools > Terrain Workflow > Run Complete Reference Map` with the active
`MainTerrain` selected. The generated Scene view should show:

- a broad rectangular playable island with a continuous, gently sloped
  perimeter; no hard front cliff or flat raised plateau;
- a readable horizontal river across normalized `y=0.42..0.58`, with exactly
  three crossings at `x≈0.20`, `0.50`, and `0.80`;
- connected north and south bank routes plus branches to the camp/save,
  shop, NE tower, SW tower, and SE arena/boss;
- dense trees outside the river, routes, and landmark exclusion zones.

The builder creates `Terrain Layout Acceptance Diagnostic` under
`Terrain Reference Layout - Built` and reports the normalized river and bridge
coordinates. Rerunning the button must reuse the normalized layout without
adding roots or duplicate landmarks.

## Numbered Unity workflow

Use the menus in this order:

1. `Tools > Terrain Workflow > 01 Safety and Restore`
   - `Backup Selected MainTerrain`
   - `Restore MainTerrain From Backup`
2. `Tools > Terrain Workflow > 02 Shape Preview`
   - `Generate Visible Variant B Preview`
3. `Tools > Terrain Workflow > 03 MainTerrain Height Design`
   - `Apply Continuous Mountain Edge`
4. `Tools > Terrain Workflow > 04 Surface Paint`
   - `Prepare Selected MainTerrain Paint`
   - enable **Shape approval complete**, validate layers, then apply paint
5. `Tools > Terrain Workflow > 05 Vegetation`
   - `Prepare and Apply Placement`
   - enable **Shape and painting approvals are complete**, configure exclusions,
     validate references, then apply placement
6. `Tools > Terrain Workflow > Run Complete Reference Map`
   - runs the deterministic one-button build, including the river/routes,
     landmarks/bridges, and vegetation stages

Every mutating action requires an explicitly selected active MainTerrain,
creates a backup where applicable, and keeps the scene unsaved until the user
chooses to save it. The old unnumbered Terrain menu actions were removed.
