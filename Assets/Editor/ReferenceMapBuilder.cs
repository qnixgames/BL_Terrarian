using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds the reference square map from scratch on the selected MainTerrain:
/// shape → paint → river/bridges/buildings → dense trees.
/// Sizes are derived from the scene Player_Character height.
/// </summary>
public static class ReferenceMapBuilder
{
    private const string MenuRoot = "Tools/Reference Map/";
    private const string SceneRootName = "Reference Map Layout";
    private const string BackupFolder = "Assets/TerrainShapeStudies";
    private const string PendingRebuildFlag = "Assets/Editor/RebuildReferenceMap.flag";
    private const string PendingReplantFlag = "Assets/Editor/ReplantVegetation.flag";
    private const string PendingGrassFlag = "Assets/Editor/ReplantGrass.flag";

    private const string GrassLayerPath =
        "Assets/3rdParty/Retro Shaders Pro/Demo/Terrain/GrassLayer.terrainlayer";
    private const string GroundLayerPath =
        "Assets/3rdParty/Retro Shaders Pro/Demo/Terrain/GroundLayer.terrainlayer";
    private const string SandLayerPath =
        "Assets/3rdParty/Retro Shaders Pro/Demo/Terrain/SandLayer.terrainlayer";
    private const string RockLayerPath =
        "Assets/TerrainPaintPreparation/RockCliffLayer.terrainlayer";
    private const string VegetationFolder = "Assets/ReferenceMapVegetation";
    private const string PineFolder =
        "Assets/3rdParty/PSX Packs/PSX Nature/PSX Nature/Models/other-formats/FBX/";
    private const string NatureGrassFolder =
        "Assets/3rdParty/PSX Packs/PSX Nature/PSX Nature/Models/other-formats/FBX/";
    private const string NatureGrassTextureFolder =
        "Assets/3rdParty/PSX Packs/PSX Nature/PSX Nature/Textures/";
    private const string Nature2GrassFolder =
        "Assets/3rdParty/PSX Packs/PSX Nature II/Meshes/fbx/";
    private const string FallbackGrassTuftPath =
        "Assets/3rdParty/Retro Shaders Pro/Demo/Prefabs/GrassTuft.prefab";

    private static readonly string[] PineSourceNames =
    {
        "pine_tree_n_1",
        "pine_tree_n_1_2",
        "pine_tree_n_2",
        "pine_tree_n_2_2",
        "pine_tree_n_3",
        "pine_tree_n_3_2"
    };

    // PSX Nature II clumps/tall — proper foliage, not tiny single blades.
    private static readonly string[] QualityGrassMeshNames =
    {
        "SM_FOL_Grass_Clump_01_A",
        "SM_FOL_Grass_Clump_02_A",
        "SM_FOL_Grass_Clump_03_A",
        "SM_FOL_Grass_Clump_04_A",
        "SM_FOL_Grass_Tall_01_A",
        "SM_FOL_Grass_Tall_02_A"
    };

    // Normalized layout (x = east, y = north). Matches the reference top-down map.
    private const float RiverSouth = 0.44f;
    private const float RiverNorth = 0.56f;
    private const float BaseHeight = 0.14f;
    private const float RiverFloor = 0.015f;
    private const float EdgeRim = 0.22f;
    private const float RiverDepthWorld = 28f;
    private const float DefaultPlayerHeight = 1.8f;
    private const float PinePrefabBaseHeight = 9f;

    // Filled by EnsurePlayerScale from Player_Character + terrain size.
    private static float BridgeHalfWidth = 0.009f;
    private static float RoadHalfWidth = 0.006f;
    private static float ClearingRadius = 0.025f;
    private static float PlayerHeight = DefaultPlayerHeight;
    private static float TreeSpacing = 7.5f;
    private static float TreeScaleMin = 1.0f;
    private static float TreeScaleMax = 1.75f;
    private static float GrassHeightMin = 0.75f;
    private static float GrassHeightMax = 1.45f;
    // Detail density 0-15. Fill green ground near-max; independent of trees.
    private const int GrassDensityMin = 13;
    private const int GrassDensityMax = 15;

    private static readonly float[] BridgeXs = { 0.25f, 0.50f, 0.75f };

    private static readonly Vector2 NwHut = new Vector2(0.28f, 0.78f);
    private static readonly Vector2 NeTower = new Vector2(0.72f, 0.78f);
    private static readonly Vector2 NorthBooth = new Vector2(0.50f, 0.62f);
    private static readonly Vector2 SwSilo = new Vector2(0.22f, 0.28f);
    private static readonly Vector2 SeHouse = new Vector2(0.72f, 0.28f);

    private static readonly Vector2[] Clearings =
    {
        NwHut, NeTower, NorthBooth, SwSilo, SeHouse
    };

    [InitializeOnLoadMethod]
    private static void ConsumePendingRebuildFlag()
    {
        EditorApplication.delayCall += TryConsumePendingRebuildFlag;
        EditorApplication.delayCall += TryConsumePendingReplantFlag;
        EditorApplication.delayCall += TryConsumePendingGrassFlag;
        EditorApplication.update += PollPendingRebuildFlag;
    }

    private static double s_PollUntil;

    private static void PollPendingRebuildFlag()
    {
        if (s_PollUntil <= 0d)
            s_PollUntil = EditorApplication.timeSinceStartup + 60d;

        if (EditorApplication.timeSinceStartup > s_PollUntil)
        {
            EditorApplication.update -= PollPendingRebuildFlag;
            return;
        }

        TryConsumePendingRebuildFlag();
        TryConsumePendingReplantFlag();
        TryConsumePendingGrassFlag();
    }

    private static bool s_RebuildRunning;

    private static void TryConsumePendingRebuildFlag()
    {
        if (s_RebuildRunning || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (!File.Exists(PendingRebuildFlag))
        {
            EditorApplication.update -= PollPendingRebuildFlag;
            return;
        }

        s_RebuildRunning = true;
        EditorApplication.update -= PollPendingRebuildFlag;

        try
        {
            if (File.Exists(PendingRebuildFlag))
                File.Delete(PendingRebuildFlag);
            string meta = PendingRebuildFlag + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
            AssetDatabase.Refresh();
        }
        catch (IOException ex)
        {
            Debug.LogWarning("ReferenceMapBuilder: could not clear rebuild flag: " + ex.Message);
            s_RebuildRunning = false;
            return;
        }

        Debug.Log("ReferenceMapBuilder: pending player-scale rebuild flag detected.");
        if (!TryGetMainTerrain(out Terrain terrain))
        {
            s_RebuildRunning = false;
            return;
        }

        EnsurePlayerScale(terrain);
        BuildComplete(terrain);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        EditorSceneManager.SaveScene(terrain.gameObject.scene);
        s_RebuildRunning = false;
        Debug.Log("ReferenceMapBuilder: player-scale rebuild finished and scene saved.");
    }

    private static void TryConsumePendingReplantFlag()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (!File.Exists(PendingReplantFlag))
            return;

        try
        {
            File.Delete(PendingReplantFlag);
            string meta = PendingReplantFlag + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }
        catch (IOException)
        {
            return;
        }

        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        EnsurePlayerScale(terrain);
        BackupTerrainData(terrain.terrainData, "Before Vegetation");
        PlantVegetation(terrain);
        TerrainTreeLodToggle.SetLodEnabled(false);
        MarkDirty(terrain);
        EditorSceneManager.SaveScene(terrain.gameObject.scene);
        Debug.Log("ReferenceMapBuilder: dense trees + dense grass replanted (independent); Tree LOD OFF.");
    }

    private static void TryConsumePendingGrassFlag()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (!File.Exists(PendingGrassFlag))
            return;

        try
        {
            File.Delete(PendingGrassFlag);
            string meta = PendingGrassFlag + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }
        catch (IOException)
        {
            return;
        }

        Terrain terrain = FindMainTerrainSilent();
        if (terrain == null)
        {
            Debug.LogError("ReferenceMapBuilder: MainTerrain not found for grass replant.");
            return;
        }

        try
        {
            EnsurePlayerScale(terrain);
            Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Dense quality grass");
            PlantGrass(terrain, terrain.terrainData, new System.Random(91));
            TerrainTreeLodToggle.SetLodEnabled(false);
            MarkDirty(terrain);
            EditorSceneManager.SaveScene(terrain.gameObject.scene);
            Debug.Log("ReferenceMapBuilder: quality Nature II grass carpet applied (trees untouched).");
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private static Terrain FindMainTerrainSilent()
    {
        Terrain[] all = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "MainTerrain")
                return all[i];
        }

        return all.Length > 0 ? all[0] : null;
    }

    [MenuItem(MenuRoot + "01 Build Shape (heights + river bed)")]
    private static void MenuBuildShape()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        EnsurePlayerScale(terrain);
        BackupTerrainData(terrain.terrainData, "Before Shape");
        BuildShape(terrain);
        MarkDirty(terrain);
        Debug.Log("Reference map shape applied on MainTerrain.");
    }

    [MenuItem(MenuRoot + "02 Paint Surfaces")]
    private static void MenuPaint()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        EnsurePlayerScale(terrain);
        BackupTerrainData(terrain.terrainData, "Before Paint");
        PaintSurfaces(terrain);
        MarkDirty(terrain);
        Debug.Log("Reference map paint applied.");
    }

    [MenuItem(MenuRoot + "Remove River Ford Roads (bridge-only crossing)", priority = 225)]
    private static void MenuRemoveRiverFords()
    {
        ApplyBridgeOnlyCrossing();
    }

    /// <summary>
    /// Recarves the full river bed (no terrain fords) and repaints so roads stop at the banks.
    /// Crossing is mesh bridges only.
    /// </summary>
    public static void ApplyBridgeOnlyCrossing()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        EnsurePlayerScale(terrain);
        BackupTerrainData(terrain.terrainData, "Before Bridge-Only Crossing");
        BuildShape(terrain);
        PaintSurfaces(terrain);
        MarkDirty(terrain);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("River fords removed — roads stop at banks; cross only via bridges.");
    }

    [MenuItem(MenuRoot + "03 Place River, Bridges, Buildings")]
    private static void MenuStructures()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        EnsurePlayerScale(terrain);
        PlaceStructures(terrain);
        MarkDirty(terrain);
        Debug.Log("Reference map structures placed.");
    }

    [MenuItem(MenuRoot + "04 Plant Trees and Grass")]
    private static void MenuTrees()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        EnsurePlayerScale(terrain);
        BackupTerrainData(terrain.terrainData, "Before Vegetation");
        PlantVegetation(terrain);
        MarkDirty(terrain);
        Debug.Log("Dense pines and grass planted on MainTerrain.");
    }

    [MenuItem(MenuRoot + "Build Complete Reference Map")]
    private static void MenuBuildAll()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        EnsurePlayerScale(terrain);
        string summary =
            "Player_Character height: " + PlayerHeight.ToString("0.00") + " m\n" +
            "Road width: " + (RoadHalfWidth * 2f * terrain.terrainData.size.x).ToString("0.0") + " m\n" +
            "Bridge width: " + (BridgeHalfWidth * 2f * terrain.terrainData.size.x).ToString("0.0") + " m\n" +
            "Tree height target: ~" + (PinePrefabBaseHeight * TreeScaleMin).ToString("0.0") +
            "-" + (PinePrefabBaseHeight * TreeScaleMax).ToString("0.0") + " m\n\n" +
            "This will reshape, paint, place structures, plant PSX pines, and add grass.\n" +
            "Layout shape stays the same; sizes follow the player.";

        if (!EditorUtility.DisplayDialog(
                "Build Complete Reference Map",
                summary,
                "Build",
                "Cancel"))
            return;

        BuildComplete(terrain);
    }

    /// <summary>
    /// Batch / CLI entry: Unity -batchmode -executeMethod ReferenceMapBuilder.BuildCompleteCli
    /// </summary>
    public static void BuildCompleteCli()
    {
        string scenePath = "Assets/Scenes/SampleScene.unity";
        if (!File.Exists(scenePath))
        {
            Debug.LogError("SampleScene not found at " + scenePath);
            EditorApplication.Exit(1);
            return;
        }

        EditorSceneManager.OpenScene(scenePath);
        if (!TryGetMainTerrain(out Terrain terrain))
        {
            EditorApplication.Exit(1);
            return;
        }

        EnsurePlayerScale(terrain);
        BuildComplete(terrain);
        EditorSceneManager.SaveScene(terrain.gameObject.scene);
        Debug.Log(
            "CLI reference map rebuild done. PlayerHeight=" + PlayerHeight +
            " road=" + (RoadHalfWidth * 2f * terrain.terrainData.size.x) +
            " bridge=" + (BridgeHalfWidth * 2f * terrain.terrainData.size.x));
        EditorApplication.Exit(0);
    }

    private static void BuildComplete(Terrain terrain)
    {
        BackupTerrainData(terrain.terrainData, "Before Complete Reference Map");
        EnsurePlayerScale(terrain);
        BuildShape(terrain);
        PaintSurfaces(terrain);
        PlaceStructures(terrain);
        PlantVegetation(terrain);
        MarkDirty(terrain);
        Debug.Log(
            "Complete reference map built for Player_Character (" +
            PlayerHeight.ToString("0.00") + " m).");
    }

    private static void EnsurePlayerScale(Terrain terrain)
    {
        PlayerHeight = MeasurePlayerHeight();
        float mapSize = Mathf.Max(1f, terrain.terrainData.size.x);

        // Walkable dirt road ~6.5× player (≈12 m for 1.8 m) — matches prior route docs.
        float roadWidthWorld = PlayerHeight * 6.5f;
        // Bridges a bit wider so two characters / a cart can pass.
        float bridgeWidthWorld = PlayerHeight * 9f;
        // Clearing fits building + walk-around margin.
        float clearingWorld = PlayerHeight * 14f;

        RoadHalfWidth = (roadWidthWorld * 0.5f) / mapSize;
        BridgeHalfWidth = (bridgeWidthWorld * 0.5f) / mapSize;
        ClearingRadius = clearingWorld / mapSize;

        // Pines ~5–9× player height using the ~9 m prefab capsule as base.
        TreeScaleMin = (PlayerHeight * 5.5f) / PinePrefabBaseHeight;
        TreeScaleMax = (PlayerHeight * 9.5f) / PinePrefabBaseHeight;
        // Trees and grass are independent: dense canopy + dense ground cover.
        TreeSpacing = Mathf.Clamp(PlayerHeight * 4.2f, 6.5f, 9f);
        GrassHeightMin = Mathf.Clamp(PlayerHeight * 0.42f, 0.7f, 1.0f);
        GrassHeightMax = Mathf.Clamp(PlayerHeight * 0.85f, 1.2f, 1.8f);

        Debug.Log(
            "Player scale locked: height=" + PlayerHeight.ToString("0.00") +
            "m, road=" + roadWidthWorld.ToString("0.0") +
            "m, bridge=" + bridgeWidthWorld.ToString("0.0") +
            "m, clearing r=" + clearingWorld.ToString("0.0") +
            "m, treeScale=" + TreeScaleMin.ToString("0.00") +
            "-" + TreeScaleMax.ToString("0.00"));
    }

    private static float MeasurePlayerHeight()
    {
        GameObject player = GameObject.Find("Player_Character");
        if (player == null)
        {
            Debug.LogWarning(
                "Player_Character not found in scene; using default height " +
                DefaultPlayerHeight + " m.");
            return DefaultPlayerHeight;
        }

        Renderer[] renderers = player.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning(
                "Player_Character has no renderers; using default height " +
                DefaultPlayerHeight + " m.");
            return DefaultPlayerHeight;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                bounds.Encapsulate(renderers[i].bounds);
        }

        float height = bounds.size.y;
        if (height < 0.4f)
        {
            // Common FBX import: centimetres treated as metres.
            float scaled = height * 100f;
            if (scaled > 0.8f && scaled < 3.5f)
            {
                Debug.LogWarning(
                    "Player_Character bounds look centimetre-scale (" +
                    height.ToString("0.000") + " m). Using " +
                    scaled.ToString("0.00") + " m. Consider fixing FBX scale.");
                return scaled;
            }
        }

        if (height < 0.8f || height > 3.5f)
        {
            Debug.LogWarning(
                "Player_Character height " + height.ToString("0.00") +
                " m is outside expected human range; clamping toward " +
                DefaultPlayerHeight + " m.");
            return Mathf.Clamp(height, 1.2f, 2.5f);
        }

        return height;
    }

    // -------------------------------------------------------------------------
    // 01 Shape
    // -------------------------------------------------------------------------

    private static void BuildShape(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        int res = data.heightmapResolution;
        float[,] heights = new float[res, res];
        float bankBlend = 0.04f;

        for (int z = 0; z < res; z++)
        {
            float nz = z / (float)(res - 1);
            for (int x = 0; x < res; x++)
            {
                float nx = x / (float)(res - 1);
                Vector2 p = new Vector2(nx, nz);

                float height = BaseHeight;
                float edge = EdgeFactor(nx, nz);
                height = Mathf.Lerp(height, EdgeRim, edge * 0.85f);
                height += (Mathf.PerlinNoise(nx * 6.1f, nz * 6.1f) - 0.5f) * 0.012f;

                // River is always carved — no terrain ford under bridges.
                // Crossing is mesh bridges only.
                if (nz > RiverSouth && nz < RiverNorth)
                {
                    float toBank = Mathf.Min(nz - RiverSouth, RiverNorth - nz);
                    float t = Mathf.Clamp01(toBank / bankBlend);
                    float floor = RiverFloor;
                    // Keep absolute river depth roughly constant in world units.
                    float worldHeight = data.size.y;
                    float targetNorm = Mathf.Max(0f, BaseHeight - RiverDepthWorld / worldHeight);
                    floor = Mathf.Min(floor, targetNorm);
                    height = Mathf.Lerp(BaseHeight, floor, SmoothStep(t));
                }

                // Flatten clearings slightly.
                float clearDist = ClearingDistance(p);
                if (clearDist < ClearingRadius)
                {
                    float ct = 1f - clearDist / ClearingRadius;
                    height = Mathf.Lerp(height, BaseHeight, ct * ct);
                }

                heights[z, x] = Mathf.Clamp01(height);
            }
        }

        Undo.RegisterCompleteObjectUndo(data, "Reference map shape");
        data.SetHeights(0, 0, heights);
        terrain.Flush();
    }

    // -------------------------------------------------------------------------
    // 02 Paint
    // -------------------------------------------------------------------------

    private static void PaintSurfaces(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        TerrainLayer grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(GrassLayerPath);
        TerrainLayer ground = AssetDatabase.LoadAssetAtPath<TerrainLayer>(GroundLayerPath);
        TerrainLayer sand = AssetDatabase.LoadAssetAtPath<TerrainLayer>(SandLayerPath);
        TerrainLayer rock = AssetDatabase.LoadAssetAtPath<TerrainLayer>(RockLayerPath);

        if (grass == null || ground == null || sand == null)
        {
            EditorUtility.DisplayDialog(
                "Missing TerrainLayers",
                "Grass / Ground / Sand layers were not found under Retro Shaders Pro Demo/Terrain.",
                "OK");
            return;
        }

        List<TerrainLayer> layers = new List<TerrainLayer> { grass, ground, sand };
        if (rock != null)
            layers.Add(rock);

        Undo.RegisterCompleteObjectUndo(data, "Reference map paint");
        data.terrainLayers = layers.ToArray();

        int aRes = data.alphamapResolution;
        int layerCount = layers.Count;
        float[,,] map = new float[aRes, aRes, layerCount];
        float roadWorld = RoadHalfWidth * data.size.x * 2f;
        float shoulderWorld = roadWorld * 1.7f;

        for (int z = 0; z < aRes; z++)
        {
            float nz = z / (float)(aRes - 1);
            for (int x = 0; x < aRes; x++)
            {
                float nx = x / (float)(aRes - 1);
                Vector2 p = new Vector2(nx, nz);

                float roadDist = RouteDistance(p) * data.size.x;
                float clearDist = ClearingDistance(p) * data.size.x;
                float edge = EdgeFactor(nx, nz);

                float road = 1f - Mathf.Clamp01(roadDist / (roadWorld * 0.5f));
                float shoulder = 1f - Mathf.Clamp01(roadDist / (shoulderWorld * 0.5f));
                shoulder = Mathf.Max(0f, shoulder - road);

                float clearing = 1f - Mathf.Clamp01(clearDist / (ClearingRadius * data.size.x));
                clearing *= clearing;

                float rockW = rock != null ? Mathf.Clamp01((edge - 0.35f) / 0.55f) : 0f;
                float grassW = 1f;

                // River bed: sand/mud only — no dirt road ford under bridges.
                if (nz > RiverSouth && nz < RiverNorth)
                {
                    road = 0f;
                    shoulder = 0.55f;
                    clearing = 0f;
                    rockW = 0f;
                    grassW = 0.45f;
                }

                float sumRoad = Mathf.Clamp01(road + clearing * 0.65f);
                float sumShoulder = Mathf.Clamp01(Mathf.Max(shoulder, clearing * 0.35f) * (1f - sumRoad));
                float sumRock = rockW * (1f - sumRoad - sumShoulder);
                float sumGrass = Mathf.Max(0f, grassW - sumRoad - sumShoulder - sumRock);
                float total = sumGrass + sumRoad + sumShoulder + sumRock;
                if (total < 0.0001f)
                {
                    sumGrass = 1f;
                    total = 1f;
                }

                map[z, x, 0] = sumGrass / total;
                map[z, x, 1] = sumRoad / total;
                map[z, x, 2] = sumShoulder / total;
                if (layerCount > 3)
                    map[z, x, 3] = sumRock / total;
            }
        }

        data.SetAlphamaps(0, 0, map);
        terrain.Flush();
    }

    // -------------------------------------------------------------------------
    // 03 Structures
    // -------------------------------------------------------------------------

    private static void PlaceStructures(Terrain terrain)
    {
        RemoveSceneRoot();
        GameObject root = new GameObject(SceneRootName);
        Undo.RegisterCreatedObjectUndo(root, "Reference map structures");

        Transform riverRoot = new GameObject("River").transform;
        riverRoot.SetParent(root.transform, false);
        Transform bridgeRoot = new GameObject("Bridges").transform;
        bridgeRoot.SetParent(root.transform, false);
        Transform buildingRoot = new GameObject("Buildings").transform;
        buildingRoot.SetParent(root.transform, false);

        PlaceRiverPlane(terrain, riverRoot);
        PlaceBridges(terrain, bridgeRoot);
        PlaceBuildings(terrain, buildingRoot);
    }

    private static void PlaceRiverPlane(Terrain terrain, Transform parent)
    {
        TerrainData data = terrain.terrainData;
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;

        float midZ = (RiverSouth + RiverNorth) * 0.5f;
        float widthZ = (RiverNorth - RiverSouth) * size.z;
        float riverY = SampleWorldHeight(terrain, 0.1f, midZ) + 0.4f;

        GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "RiverWater";
        Undo.RegisterCreatedObjectUndo(plane, "River water plane");
        plane.transform.SetParent(parent, false);
        plane.transform.position = new Vector3(
            origin.x + size.x * 0.5f,
            riverY,
            origin.z + midZ * size.z);
        // Default Plane is 10x10; scale to full river band.
        plane.transform.localScale = new Vector3(size.x / 10f, 1f, widthZ / 10f);

        Renderer renderer = plane.GetComponent<Renderer>();
        renderer.sharedMaterial = CreateColorMaterial("ReferenceMap_RiverBlue", new Color(0.15f, 0.45f, 0.95f, 1f));

        Object.DestroyImmediate(plane.GetComponent<Collider>());
    }

    private static void PlaceBridges(Terrain terrain, Transform parent)
    {
        // Prefer low-poly pack bridge; fall back to cubes if missing.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_Bridge.prefab");
        if (prefab != null)
        {
            LowPolyBridgeInstaller.ReplaceBridges();
            // Move installed Bridges under the landmarks parent if needed.
            GameObject bridgesGo = GameObject.Find("Bridges");
            if (bridgesGo != null && parent != null && bridgesGo.transform.parent != parent)
                bridgesGo.transform.SetParent(parent, true);
            return;
        }

        TerrainData data = terrain.terrainData;
        float bridgeLen = (RiverNorth - RiverSouth) * data.size.z + PlayerHeight * 4f;
        float bridgeWidth = BridgeHalfWidth * 2f * data.size.x;
        float deckThickness = Mathf.Max(0.35f, PlayerHeight * 0.22f);
        Material wood = CreateColorMaterial("ReferenceMap_BridgeWood", new Color(0.45f, 0.32f, 0.18f));

        for (int i = 0; i < BridgeXs.Length; i++)
        {
            float bx = BridgeXs[i];
            float by = SampleWorldHeight(terrain, bx, 0.5f) + deckThickness * 0.45f;
            Vector3 pos = NormalizedToWorld(terrain, bx, 0.5f, by);

            GameObject bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bridge.name = "Bridge_" + (i + 1);
            Undo.RegisterCreatedObjectUndo(bridge, "Bridge");
            bridge.transform.SetParent(parent, false);
            bridge.transform.position = pos;
            bridge.transform.localScale = new Vector3(bridgeWidth, deckThickness, bridgeLen);
            bridge.GetComponent<Renderer>().sharedMaterial = wood;
        }
    }

    private static void PlaceBuildings(Terrain terrain, Transform parent)
    {
        Material wood = CreateColorMaterial("ReferenceMap_BuildingWood", new Color(0.42f, 0.28f, 0.16f));
        Material stone = CreateColorMaterial("ReferenceMap_SiloStone", new Color(0.55f, 0.55f, 0.52f));
        Material debris = CreateColorMaterial("ReferenceMap_Debris", new Color(0.35f, 0.25f, 0.14f));

        // Human-scale landmarks relative to Player_Character.
        // Hut/house eaves ~2.8–3.2× player so a ~2.1–2.3 m doorway reads correctly.
        float h = PlayerHeight;
        PlaceCube(parent, terrain, "NW_Hut", NwHut, new Vector3(h * 4.5f, h * 3.0f, h * 5.0f), wood, 20f);
        PlaceCube(parent, terrain, "NE_Tower", NeTower, new Vector3(h * 3.4f, h * 8.5f, h * 3.4f), wood, -15f);
        PlaceCube(parent, terrain, "North_Booth", NorthBooth, new Vector3(h * 3.0f, h * 2.5f, h * 3.0f), wood, 0f);

        PlaceCylinder(parent, terrain, "SW_Silo", SwSilo, new Vector3(h * 3.6f, h * 10f, h * 3.6f), stone, 0f);
        PlaceCube(
            parent,
            terrain,
            "SW_SiloAnnex",
            SwSilo + new Vector2(0.018f, -0.008f),
            new Vector3(h * 3.4f, h * 2.6f, h * 3.2f),
            wood,
            35f);

        PlaceCube(parent, terrain, "SE_House", SeHouse, new Vector3(h * 4.5f, h * 3.0f, h * 5.0f), wood, 140f);
        PlaceCube(
            parent,
            terrain,
            "SE_Debris_A",
            SeHouse + new Vector2(0.016f, -0.012f),
            new Vector3(h * 2.2f, h * 0.45f, h * 0.55f),
            debris,
            40f);
        PlaceCube(
            parent,
            terrain,
            "SE_Debris_B",
            SeHouse + new Vector2(0.022f, 0.008f),
            new Vector3(h * 1.8f, h * 0.4f, h * 0.5f),
            debris,
            -25f);
    }

    private static void PlaceCube(
        Transform parent,
        Terrain terrain,
        string name,
        Vector2 normalized,
        Vector3 scale,
        Material material,
        float yaw)
    {
        float ground = SampleWorldHeight(terrain, normalized.x, normalized.y);
        Vector3 pos = NormalizedToWorld(terrain, normalized.x, normalized.y, ground + scale.y * 0.5f);

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static void PlaceCylinder(
        Transform parent,
        Terrain terrain,
        string name,
        Vector2 normalized,
        Vector3 scale,
        Material material,
        float yaw)
    {
        float ground = SampleWorldHeight(terrain, normalized.x, normalized.y);
        Vector3 pos = NormalizedToWorld(terrain, normalized.x, normalized.y, ground + scale.y * 0.5f);

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    // -------------------------------------------------------------------------
    // 04 Trees + Grass
    // -------------------------------------------------------------------------

    private static void PlantVegetation(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        List<GameObject> pinePrefabs = EnsurePinePrefabs();
        if (pinePrefabs.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Missing pine trees",
                "Could not find PSX Nature pine FBX models under:\n" + PineFolder,
                "OK");
            return;
        }

        Undo.RegisterCompleteObjectUndo(data, "Reference map vegetation");

        TreePrototype[] treeProtos = new TreePrototype[pinePrefabs.Count];
        for (int i = 0; i < pinePrefabs.Count; i++)
            treeProtos[i] = new TreePrototype { prefab = pinePrefabs[i] };
        data.treePrototypes = treeProtos;

        float spacing = TreeSpacing;
        int stepsX = Mathf.FloorToInt(data.size.x / spacing);
        int stepsZ = Mathf.FloorToInt(data.size.z / spacing);
        List<TreeInstance> trees = new List<TreeInstance>(stepsX * stepsZ);

        System.Random rng = new System.Random(42);
        float excludeRoad = RoadHalfWidth + (PlayerHeight * 2.5f) / data.size.x;
        float excludeClear = ClearingRadius + (PlayerHeight * 2f) / data.size.x;

        for (int iz = 0; iz < stepsZ; iz++)
        {
            float nz = (iz + 0.5f) / stepsZ;
            for (int ix = 0; ix < stepsX; ix++)
            {
                float nx = (ix + 0.5f) / stepsX;
                float jx = ((float)rng.NextDouble() - 0.5f) * 0.008f;
                float jz = ((float)rng.NextDouble() - 0.5f) * 0.008f;
                Vector2 p = new Vector2(
                    Mathf.Clamp01(nx + jx),
                    Mathf.Clamp01(nz + jz));

                if (p.y > RiverSouth - 0.012f && p.y < RiverNorth + 0.012f)
                    continue;
                if (RouteDistance(p) < excludeRoad)
                    continue;
                if (ClearingDistance(p) < excludeClear)
                    continue;
                if (EdgeFactor(p.x, p.y) > 0.92f)
                    continue;

                float scale = Mathf.Lerp(TreeScaleMin, TreeScaleMax, (float)rng.NextDouble());
                trees.Add(new TreeInstance
                {
                    position = new Vector3(p.x, 0f, p.y),
                    widthScale = scale,
                    heightScale = scale,
                    rotation = (float)(rng.NextDouble() * Mathf.PI * 2.0),
                    color = Color.white,
                    lightmapColor = Color.white,
                    prototypeIndex = rng.Next(0, pinePrefabs.Count)
                });
            }
        }

        data.SetTreeInstances(trees.ToArray(), true);
        PlantGrass(terrain, data, rng);
        TerrainTreeLodToggle.Apply(terrain);
        terrain.Flush();
        Debug.Log(
            "Planted " + trees.Count + " dense pines (spacing " + spacing +
            "m) + independent dense grass.");
    }

    [MenuItem(MenuRoot + "05 Replant Grass Only")]
    private static void MenuReplantGrass()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        EnsurePlayerScale(terrain);
        Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Replant grass");
        PlantGrass(terrain, terrain.terrainData, new System.Random(77));
        TerrainTreeLodToggle.Apply(terrain);
        MarkDirty(terrain);
        Debug.Log("Grass replanted densely (trees unchanged).");
    }

    [MenuItem(MenuRoot + "06 Replant Trees Only")]
    private static void MenuReplantTrees()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        EnsurePlayerScale(terrain);
        TerrainData data = terrain.terrainData;
        DetailPrototype[] keepDetails = data.detailPrototypes;
        int detailRes = data.detailResolution;
        var keepMaps = new List<int[,]>();
        if (keepDetails != null)
        {
            for (int i = 0; i < keepDetails.Length; i++)
                keepMaps.Add(data.GetDetailLayer(0, 0, detailRes, detailRes, i));
        }

        BackupTerrainData(data, "Before Trees Only");
        PlantVegetation(terrain);
        if (keepMaps.Count > 0 && keepDetails != null && keepDetails.Length == keepMaps.Count)
        {
            data.detailPrototypes = keepDetails;
            for (int i = 0; i < keepMaps.Count; i++)
                data.SetDetailLayer(0, 0, i, keepMaps[i]);
            terrain.Flush();
        }

        TerrainTreeLodToggle.Apply(terrain);
        MarkDirty(terrain);
        Debug.Log("Trees replanted densely (previous grass layers restored).");
    }

    private static void PlantGrass(Terrain terrain, TerrainData data, System.Random rng)
    {
        List<DetailPrototype> details = BuildGrassPrototypes();
        if (details.Count == 0)
        {
            Debug.LogWarning("No grass prototypes available; skipped grass placement.");
            return;
        }

        // Finer grid + max density = continuous carpet (trees do not reduce this).
        if (data.detailResolution < 1024)
            data.SetDetailResolution(1024, 8);

        data.detailPrototypes = details.ToArray();

        int res = data.detailResolution;
        float excludeRoad = RoadHalfWidth + (PlayerHeight * 0.8f) / data.size.x;

        for (int layer = 0; layer < details.Count; layer++)
        {
            int[,] map = new int[res, res];
            for (int z = 0; z < res; z++)
            {
                float nz = z / (float)(res - 1);
                for (int x = 0; x < res; x++)
                {
                    float nx = x / (float)(res - 1);
                    Vector2 p = new Vector2(nx, nz);

                    if (p.y > RiverSouth - 0.004f && p.y < RiverNorth + 0.004f)
                        continue;
                    if (RouteDistance(p) < excludeRoad * 0.4f)
                        continue;

                    float noise = Mathf.PerlinNoise(nx * 55f + layer * 1.7f, nz * 55f);
                    int density = Mathf.RoundToInt(Mathf.Lerp(GrassDensityMin, GrassDensityMax, noise));
                    // Almost every green cell is packed.
                    if (rng.NextDouble() > 0.15)
                        density = GrassDensityMax;
                    map[z, x] = Mathf.Clamp(density, 0, 15);
                }
            }

            data.SetDetailLayer(0, 0, layer, map);
        }

        terrain.detailObjectDensity = 1f;
        terrain.detailObjectDistance = Mathf.Max(terrain.detailObjectDistance, 350f);
        Debug.Log(
            "Dense grass painted: " + details.Count + " prototypes, detailRes=" +
            res + ", density " + GrassDensityMin + "-" + GrassDensityMax + ".");
    }

    private static List<DetailPrototype> BuildGrassPrototypes()
    {
        List<DetailPrototype> list = new List<DetailPrototype>();
        List<GameObject> grassMeshes = EnsureQualityGrassPrefabs();

        Color healthy = new Color(0.42f, 0.68f, 0.28f);
        Color dry = new Color(0.38f, 0.55f, 0.22f);

        for (int i = 0; i < grassMeshes.Count; i++)
        {
            bool tall = grassMeshes[i].name.IndexOf("Tall", System.StringComparison.OrdinalIgnoreCase) >= 0;
            float minH = tall ? GrassHeightMax * 0.85f : GrassHeightMin;
            float maxH = tall ? GrassHeightMax * 1.15f : GrassHeightMax;
            float minW = tall ? GrassHeightMin * 0.9f : GrassHeightMin;
            float maxW = tall ? GrassHeightMax : GrassHeightMax * 1.1f;

            list.Add(new DetailPrototype
            {
                prototype = grassMeshes[i],
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minHeight = minH,
                maxHeight = maxH,
                minWidth = minW,
                maxWidth = maxW,
                noiseSpread = 0.4f,
                healthyColor = healthy,
                dryColor = dry
            });
        }

        GameObject tuft = AssetDatabase.LoadAssetAtPath<GameObject>(FallbackGrassTuftPath);
        if (tuft != null)
        {
            list.Add(new DetailPrototype
            {
                prototype = tuft,
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minHeight = GrassHeightMin * 0.85f,
                maxHeight = GrassHeightMax * 0.95f,
                minWidth = GrassHeightMin * 0.85f,
                maxWidth = GrassHeightMax * 0.95f,
                noiseSpread = 0.45f,
                healthyColor = healthy,
                dryColor = dry
            });
        }

        // Billboard carpet fill between clumps.
        string[] billboardPaths =
        {
            "Assets/3rdParty/Retro Shaders Pro/Demo/Textures/GrassTuft.png",
            NatureGrassTextureFolder + "grass_1.png",
            NatureGrassTextureFolder + "grass_2.png"
        };

        for (int i = 0; i < billboardPaths.Length && list.Count < 8; i++)
        {
            EnsureBillboardGrassTexture(billboardPaths[i]);
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(billboardPaths[i]);
            if (tex == null)
                continue;

            list.Add(new DetailPrototype
            {
                prototypeTexture = tex,
                usePrototypeMesh = false,
                renderMode = DetailRenderMode.GrassBillboard,
                minHeight = GrassHeightMin * 0.7f,
                maxHeight = GrassHeightMax * 0.9f,
                minWidth = GrassHeightMin * 0.7f,
                maxWidth = GrassHeightMax * 0.9f,
                noiseSpread = 0.55f,
                healthyColor = healthy,
                dryColor = dry
            });
        }

        return list;
    }

    private static List<GameObject> EnsurePinePrefabs()
    {
        EnsureFolder(VegetationFolder);
        List<GameObject> result = new List<GameObject>();

        for (int i = 0; i < PineSourceNames.Length; i++)
        {
            string name = PineSourceNames[i];
            string prefabPath = VegetationFolder + "/" + name + ".prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                result.Add(existing);
                continue;
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(PineFolder + name + ".fbx");
            if (source == null)
                continue;

            GameObject instance = Object.Instantiate(source);
            instance.name = name;
            if (instance.GetComponent<CapsuleCollider>() == null)
            {
                CapsuleCollider capsule = instance.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, PinePrefabBaseHeight * 0.5f, 0f);
                capsule.height = PinePrefabBaseHeight;
                capsule.radius = 1.2f;
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);
            if (prefab != null)
                result.Add(prefab);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return result;
    }

    private static List<GameObject> EnsureQualityGrassPrefabs()
    {
        EnsureFolder(VegetationFolder);

        // Batch-enable Read/Write so Terrain detail instancing can use the meshes.
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < QualityGrassMeshNames.Length; i++)
            {
                string fbxPath = Nature2GrassFolder + QualityGrassMeshNames[i] + ".fbx";
                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    EditorUtility.SetDirty(importer);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();

        List<GameObject> result = new List<GameObject>();
        for (int i = 0; i < QualityGrassMeshNames.Length; i++)
        {
            string name = QualityGrassMeshNames[i];
            string fbxPath = Nature2GrassFolder + name + ".fbx";
            string prefabPath = VegetationFolder + "/" + name + ".prefab";

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                result.Add(existing);
                continue;
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null)
            {
                Debug.LogWarning("Missing quality grass FBX: " + fbxPath);
                continue;
            }

            GameObject instance = Object.Instantiate(source);
            instance.name = name;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);
            if (prefab != null)
                result.Add(prefab);
        }

        // Fallback: Retro GrassTuft if Nature II failed to load.
        if (result.Count == 0)
        {
            GameObject tuft = AssetDatabase.LoadAssetAtPath<GameObject>(FallbackGrassTuftPath);
            if (tuft != null)
                result.Add(tuft);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Quality grass prefabs ready: " + result.Count);
        return result;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static void EnsureBillboardGrassTexture(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.alphaIsTransparency)
            return;

        importer.alphaIsTransparency = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.SaveAndReimport();
    }

    // -------------------------------------------------------------------------
    // Layout math
    // -------------------------------------------------------------------------

    private static bool IsBridge(Vector2 p)
    {
        if (p.y < RiverSouth || p.y > RiverNorth)
            return false;

        for (int i = 0; i < BridgeXs.Length; i++)
        {
            if (Mathf.Abs(p.x - BridgeXs[i]) <= BridgeHalfWidth)
                return true;
        }

        return false;
    }

    private static float RouteDistance(Vector2 p)
    {
        float d = float.MaxValue;

        // Main north-south spine — split at the river so there is no terrain ford.
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.50f, 0.04f), new Vector2(0.50f, RiverSouth)));
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.50f, RiverNorth), new Vector2(0.50f, 0.96f)));

        // North and south bank connectors.
        d = Mathf.Min(d, SegmentDistance(
            p,
            new Vector2(0.08f, RiverNorth + 0.03f),
            new Vector2(0.92f, RiverNorth + 0.03f)));
        d = Mathf.Min(d, SegmentDistance(
            p,
            new Vector2(0.08f, RiverSouth - 0.03f),
            new Vector2(0.92f, RiverSouth - 0.03f)));

        // Short approach stubs to each bridge end (banks only — not across water).
        for (int i = 0; i < BridgeXs.Length; i++)
        {
            float bx = BridgeXs[i];
            d = Mathf.Min(d, SegmentDistance(
                p,
                new Vector2(bx, RiverSouth - 0.03f),
                new Vector2(bx, RiverSouth)));
            d = Mathf.Min(d, SegmentDistance(
                p,
                new Vector2(bx, RiverNorth),
                new Vector2(bx, RiverNorth + 0.03f)));
        }

        // Diagonal / branch paths to clearings.
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.50f, RiverNorth + 0.03f), NwHut));
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.50f, RiverNorth + 0.03f), NeTower));
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.50f, RiverNorth + 0.03f), NorthBooth));
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.50f, RiverSouth - 0.03f), SwSilo));
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.50f, RiverSouth - 0.03f), SeHouse));

        // Soft arcs from side bridges toward NW / NE clearings (reference look).
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.25f, RiverNorth + 0.03f), NwHut));
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.75f, RiverNorth + 0.03f), NeTower));
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.25f, RiverSouth - 0.03f), SwSilo));
        d = Mathf.Min(d, SegmentDistance(p, new Vector2(0.75f, RiverSouth - 0.03f), SeHouse));

        return d;
    }

    private static float ClearingDistance(Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < Clearings.Length; i++)
            best = Mathf.Min(best, Vector2.Distance(p, Clearings[i]));
        return best;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-8f)
            return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        return Vector2.Distance(p, a + ab * t);
    }

    private static float EdgeFactor(float nx, float nz)
    {
        float edge = Mathf.Max(
            Mathf.Max(1f - nx, nx),
            Mathf.Max(1f - nz, nz));
        return Mathf.Clamp01((edge - 0.82f) / 0.18f);
    }

    private static float SmoothStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool TryGetMainTerrain(out Terrain terrain)
    {
        terrain = null;
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            Terrain[] all = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == "MainTerrain")
                {
                    terrain = all[i];
                    break;
                }
            }
        }
        else
        {
            terrain = go.GetComponent<Terrain>();
        }

        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog(
                "MainTerrain required",
                "Select MainTerrain in the Hierarchy, or ensure a GameObject named MainTerrain exists in the open scene.",
                "OK");
            return false;
        }

        return true;
    }

    private static void RemoveSceneRoot()
    {
        GameObject existing = GameObject.Find(SceneRootName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        // Also clear leftovers from the previous workflow tools.
        string[] legacy =
        {
            "Terrain Reference Layout - Built",
            "Terrain Landmark Stage",
            "Terrain Workflow Generated Vegetation",
            "Terrain Shape Study Preview - Variant B"
        };
        for (int i = 0; i < legacy.Length; i++)
        {
            GameObject go = GameObject.Find(legacy[i]);
            if (go != null)
                Undo.DestroyObjectImmediate(go);
        }
    }

    private static Material CreateColorMaterial(string assetName, Color color)
    {
        string folder = "Assets/TerrainPaintPreparation";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "TerrainPaintPreparation");

        string path = folder + "/" + assetName + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            if (existing.HasProperty("_BaseColor"))
                existing.SetColor("_BaseColor", color);
            existing.color = color;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.name = assetName;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        mat.color = color;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static Vector3 NormalizedToWorld(Terrain terrain, float nx, float nz, float y)
    {
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return new Vector3(origin.x + nx * size.x, y, origin.z + nz * size.z);
    }

    private static float SampleWorldHeight(Terrain terrain, float nx, float nz)
    {
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        float worldX = origin.x + nx * size.x;
        float worldZ = origin.z + nz * size.z;
        return terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + origin.y;
    }

    private static string BackupTerrainData(TerrainData source, string label)
    {
        if (!AssetDatabase.IsValidFolder(BackupFolder))
            AssetDatabase.CreateFolder("Assets", "TerrainShapeStudies");

        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = BackupFolder + "/MainTerrain - " + label + " " + stamp + ".asset";
        TerrainData copy = Object.Instantiate(source);
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        return path;
    }

    private static void MarkDirty(Terrain terrain)
    {
        EditorUtility.SetDirty(terrain.terrainData);
        EditorUtility.SetDirty(terrain);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        AssetDatabase.SaveAssets();
    }
}
