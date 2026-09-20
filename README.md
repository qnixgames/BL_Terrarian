# BL_Terrarian

## Terrain workflow

1. After shape approval, select the approved `MainTerrain` GameObject.
2. Run `Tools/Terrain Workflow/04 Surface Paint/01 Create Rock TerrainLayer`.
3. Confirm the console reports:
   - `Assets/TerrainPaintPreparation/RockCliffLayer.terrainlayer`
   - diffuse: `Assets/3rdParty/PSX Packs/PSX Nature II/Textures/Tiling/T_TILE_Rock_Cliff_01_A_BaseColour.png`
   - normal: `Assets/3rdParty/PSX Packs/PSX Nature II/Textures/Tiling/T_TILE_Rock_Cliff_01_A_Normal.png`
   - matching `512x512` inputs and `4x4` tile size.
4. Open `Tools/Terrain Workflow/04 Surface Paint/Prepare Selected MainTerrain Paint`, load the layers, and resolve any texture-resolution warning before the separate approval-gated paint action.

The current rock normal input is imported as `Default` rather than `Normal Map`; the creation tool logs this warning and painting should wait until that importer setting is corrected.

The rock-layer step creates or explicitly overwrites only the dedicated TerrainLayer asset. It does not modify `MainTerrain`, TerrainData, alphamaps, heights, or materials.