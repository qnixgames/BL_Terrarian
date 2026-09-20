# Terrain Workflow Runner QA

## Scope

`Assets/Editor/TerrainWorkflowRunner.cs` adds one editor window and one action:

`Tools > Terrain Workflow > Run Complete Reference Map`

The action runs the numbered workflow in order:

1. safety backup
2. Jim's shape preview and shared route layout
3. MainTerrain height design
4. Jim's river depression and route reconstruction
5. surface paint
6. Dwight's landmark and bridge structure stage
7. vegetation placement
8. validation and final report

The active scene is marked dirty but is never saved by the runner.

## Static checks

- Runner braces are balanced after integration changes.
- Required workflow scripts are checked before execution.
- Numbered `/02`, `/04`, and `/05` menu paths plus Jim/Dwight runner hooks are checked before execution.
- Optional layer and prefab paths are checked before execution.
- Missing optional layers are replaced with generated fallback TerrainLayers.
- Missing optional structure and vegetation assets are replaced with cube instances.
- Vegetation is capped at 420 trees and 850 grass instances, with tree scale
  `3.2`–`4.8` and grass scale `1.15`–`1.75`.

## Runtime checks performed by the runner

- Selected object remains the active `MainTerrain`.
- TerrainData size and heightmap resolution remain unchanged.
- Exactly four TerrainLayers are assigned.
- Sampled alphamap cells remain normalized.
- Generated structure and vegetation roots exist.
- A camera-facing vegetation preview diagnostic is created from the active Scene view.
- TerrainData backup and Undo are created before mutation.
- A failed run attempts Undo rollback and removes generated roots.

## Visual checklist

After running, inspect an angled Scene view and confirm:

- canopy reads as continuous forest away from routes;
- river and all three bridges remain unobstructed;
- roads remain readable from the camera;
- five landmarks and the arena remain visible;
- the second run does not increase generated object counts.

## Runtime ownership

Jim's `TerrainReferenceReconstructionTool.BuildTerrainAndRoutes` owns the river depression,
river mesh, and route objects. Dwight's `TerrainLandmarkPlacementStage.Build` owns bridges,
landmarks, arena pads, and structure fallbacks. The runner does not create duplicate
landmark or bridge roots.

Unity Editor execution is still required for visual verification.
