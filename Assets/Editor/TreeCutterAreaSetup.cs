using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Converts TreeCutterArea into a round clear-cut: dirt paint, remove terrain trees,
/// place stump/bush/tree props from CutTreeAreaModels. Keeps SE_House.
/// </summary>
public static class TreeCutterAreaSetup
{
    private const string MenuRoot = "Tools/Reference Map/";
    private const string PendingFlag = "Assets/Editor/ApplyTreeCutterArea.flag";
    private const string PendingDirtFlag = "Assets/Editor/RepaintTreeCutterDirt.flag";
    private const string AreaObjectName = "TreeCutterArea";
    private const string PropsRootName = "Cut Tree Area Props";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ModelsFolder = "Assets/CutTreeAreaModels";
    private const string CutDirtLayerPath = "Assets/TerrainPaintPreparation/CutAreaDirt.terrainlayer";
    private const string GroundDiffusePath =
        "Assets/3rdParty/Retro Shaders Pro/Demo/Textures/Ground/Ground067_1K-PNG_Color.png";
    private const string GroundNormalPath =
        "Assets/3rdParty/Retro Shaders Pro/Demo/Textures/Ground/Ground067_1K-PNG_NormalGL.png";

    private const string StumpPath = ModelsFolder + "/SM_Stump_01.prefab";
    private const string BushPath = ModelsFolder + "/SM_Bush.prefab";
    private const string Tree01Path = ModelsFolder + "/SM_Tree_01.prefab";
    private const string Tree02Path = ModelsFolder + "/SM_Tree_02.prefab";

    // Keep props clear of the house footprint.
    private const float HouseClearRadius = 14f;
    private const float PropSpacing = 9.5f;
    private const float EdgeSoftness = 0.18f;
    // Fill grass gaps between cut circle and nearby road/shoulder dirt.
    private const float RoadConnectMaxGap = 48f;
    private const float RoadLayerWeightMin = 0.32f;

    [InitializeOnLoadMethod]
    private static void HookPendingFlag()
    {
        EditorApplication.delayCall += TryConsumePendingFlag;
        EditorApplication.delayCall += TryConsumePendingDirtFlag;
        EditorApplication.update += PollPendingFlag;
    }

    private static double s_PollUntil;

    private static void PollPendingFlag()
    {
        if (s_PollUntil <= 0d)
            s_PollUntil = EditorApplication.timeSinceStartup + 90d;

        if (EditorApplication.timeSinceStartup > s_PollUntil)
        {
            EditorApplication.update -= PollPendingFlag;
            return;
        }

        TryConsumePendingFlag();
        TryConsumePendingDirtFlag();
    }

    private static bool s_Running;

    /// <summary>Called from ReferenceMapBuilder poll so a flag apply works even if this type loaded late.</summary>
    public static void TryConsumePendingFlagPublic()
    {
        TryConsumePendingFlag();
        TryConsumePendingDirtFlag();
    }

    private static void TryConsumePendingFlag()
    {
        if (s_Running || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (!File.Exists(PendingFlag))
            return;

        s_Running = true;
        EditorApplication.update -= PollPendingFlag;

        try
        {
            if (File.Exists(PendingFlag))
                File.Delete(PendingFlag);
            string meta = PendingFlag + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
            AssetDatabase.Refresh();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("TreeCutterAreaSetup: could not clear flag: " + ex.Message);
        }

        ApplyFromCommandLine();
        s_Running = false;
    }

    private static void TryConsumePendingDirtFlag()
    {
        if (s_Running || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (!File.Exists(PendingDirtFlag))
            return;

        s_Running = true;
        try
        {
            if (File.Exists(PendingDirtFlag))
                File.Delete(PendingDirtFlag);
            string meta = PendingDirtFlag + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }
        catch (System.Exception)
        {
            s_Running = false;
            return;
        }

        RepaintDirtFromCommandLine();
        s_Running = false;
    }

    [MenuItem(MenuRoot + "Apply Tree Cutter Area (clear-cut)", priority = 260)]
    public static void ApplyFromMenu()
    {
        if (!Apply())
            return;
        Debug.Log("TreeCutterAreaSetup: clear-cut applied.");
    }

    [MenuItem(MenuRoot + "Repaint Tree Cutter Dirt Only", priority = 261)]
    public static void RepaintDirtFromMenu()
    {
        if (!RepaintDirtOnly())
            return;
        Debug.Log("TreeCutterAreaSetup: dirt repaint applied.");
    }

    /// <summary>Batch / flag entry: opens SampleScene if needed, then applies.</summary>
    public static void ApplyFromCommandLine()
    {
        if (!EnsureSampleSceneOpen())
            return;

        if (!Apply())
        {
            Debug.LogError("TreeCutterAreaSetup: Apply failed.");
            return;
        }

        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("TreeCutterAreaSetup: clear-cut applied and scene saved.");
    }

    public static void RepaintDirtFromCommandLine()
    {
        if (!EnsureSampleSceneOpen())
            return;

        if (!RepaintDirtOnly())
        {
            Debug.LogError("TreeCutterAreaSetup: dirt repaint failed.");
            return;
        }

        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("TreeCutterAreaSetup: dirt repaint saved.");
    }

    private static bool EnsureSampleSceneOpen()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.IsValid() && active.path == ScenePath)
            return true;

        if (!EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single).IsValid())
        {
            Debug.LogError("TreeCutterAreaSetup: failed to open " + ScenePath);
            return false;
        }

        return true;
    }

    public static bool RepaintDirtOnly()
    {
        Transform area = FindArea();
        if (area == null)
        {
            EditorUtility.DisplayDialog(
                "TreeCutterArea missing",
                "Place a GameObject named TreeCutterArea in the scene (the marker cube).",
                "OK");
            return false;
        }

        Terrain terrain = FindMainTerrain();
        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog(
                "MainTerrain required",
                "MainTerrain was not found in the open scene.",
                "OK");
            return false;
        }

        Vector3 center = area.position;
        float radius = 0.5f * Mathf.Max(area.lossyScale.x, area.lossyScale.z);
        if (radius < 5f)
            radius = 5f;

        Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Repaint cut-area dirt");
        float reach = PaintRoundDirt(terrain, center, radius);
        ClearDetailGrassInCircle(terrain, center, reach);
        RemoveTerrainTreesInCircle(terrain, center, reach);
        StripDynamicGrassInCircle(center, reach);
        DynamicGrassTerrainBuilder.Build(terrain);

        EditorUtility.SetDirty(terrain.terrainData);
        EditorUtility.SetDirty(terrain);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

        Debug.Log(
            "TreeCutterAreaSetup: dirt-only radius=" + radius.ToString("0.0") +
            "m reach=" + reach.ToString("0.0") +
            "m center=(" + center.x.ToString("0.0") + ", " + center.z.ToString("0.0") + ")");
        return true;
    }

    public static bool Apply()
    {
        Transform area = FindArea();
        if (area == null)
        {
            EditorUtility.DisplayDialog(
                "TreeCutterArea missing",
                "Place a GameObject named TreeCutterArea in the scene (the marker cube).",
                "OK");
            return false;
        }

        Terrain terrain = FindMainTerrain();
        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog(
                "MainTerrain required",
                "MainTerrain was not found in the open scene.",
                "OK");
            return false;
        }

        GameObject stumpPrefab = LoadPrefab(StumpPath);
        GameObject bushPrefab = LoadPrefab(BushPath);
        GameObject tree01Prefab = LoadPrefab(Tree01Path);
        GameObject tree02Prefab = LoadPrefab(Tree02Path);
        if (stumpPrefab == null || bushPrefab == null || tree01Prefab == null || tree02Prefab == null)
        {
            EditorUtility.DisplayDialog(
                "Missing CutTreeAreaModels",
                "Expected SM_Stump_01, SM_Bush, SM_Tree_01, SM_Tree_02 under:\n" + ModelsFolder,
                "OK");
            return false;
        }

        Vector3 center = area.position;
        float radius = 0.5f * Mathf.Max(area.lossyScale.x, area.lossyScale.z);
        if (radius < 5f)
            radius = 5f;

        Vector3? housePos = FindHousePosition();

        Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Tree cutter clear-cut");
        float reach = PaintRoundDirt(terrain, center, radius);
        RemoveTerrainTreesInCircle(terrain, center, reach);
        ClearDetailGrassInCircle(terrain, center, reach);

        PlaceCutProps(
            terrain,
            center,
            radius,
            housePos,
            stumpPrefab,
            bushPrefab,
            tree01Prefab,
            tree02Prefab);

        HideAreaMarker(area.gameObject);

        StripDynamicGrassInCircle(center, reach);
        // Rebuild dynamic grass so dirt circle no longer has green grass meshes.
        DynamicGrassTerrainBuilder.Build(terrain);

        EditorUtility.SetDirty(terrain.terrainData);
        EditorUtility.SetDirty(terrain);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

        Debug.Log(
            "TreeCutterAreaSetup: radius=" + radius.ToString("0.0") +
            "m reach=" + reach.ToString("0.0") +
            "m center=(" + center.x.ToString("0.0") + ", " + center.z.ToString("0.0") + ")");
        return true;
    }

    private static Transform FindArea()
    {
        GameObject go = GameObject.Find(AreaObjectName);
        return go != null ? go.transform : null;
    }

    private static Terrain FindMainTerrain()
    {
        Terrain[] all = Object.FindObjectsByType<Terrain>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "MainTerrain")
                return all[i];
        }

        return all.Length > 0 ? all[0] : null;
    }

    private static Vector3? FindHousePosition()
    {
        GameObject house = GameObject.Find("SE_House");
        if (house != null)
            return house.transform.position;

        // Fallback: search by name contains House under buildings.
        Transform[] all = Object.FindObjectsByType<Transform>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name.IndexOf("House", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return all[i].position;
        }

        return null;
    }

    private static GameObject LoadPrefab(string path)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void HideAreaMarker(GameObject area)
    {
        MeshRenderer mr = area.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.enabled = false;

        Collider col = area.GetComponent<Collider>();
        if (col != null)
            col.enabled = false;
    }

    private static void RemoveTerrainTreesInCircle(Terrain terrain, Vector3 center, float radius)
    {
        TerrainData data = terrain.terrainData;
        Vector3 tPos = terrain.transform.position;
        Vector3 size = data.size;
        float radiusSq = radius * radius;

        TreeInstance[] trees = data.treeInstances;
        List<TreeInstance> kept = new List<TreeInstance>(trees.Length);
        int removed = 0;

        for (int i = 0; i < trees.Length; i++)
        {
            TreeInstance t = trees[i];
            float wx = tPos.x + t.position.x * size.x;
            float wz = tPos.z + t.position.z * size.z;
            float dx = wx - center.x;
            float dz = wz - center.z;
            if (dx * dx + dz * dz <= radiusSq)
            {
                removed++;
                continue;
            }

            kept.Add(t);
        }

        data.SetTreeInstances(kept.ToArray(), true);
        Debug.Log("TreeCutterAreaSetup: removed " + removed + " terrain trees.");
    }

    /// <summary>
    /// Paints the round cut clearing and bridges dirt to nearby road/shoulder so
    /// Dynamic Grass gaps between area and path disappear. Returns outer reach radius.
    /// </summary>
    private static float PaintRoundDirt(Terrain terrain, Vector3 center, float radius)
    {
        TerrainData data = terrain.terrainData;
        int dirtIdx = EnsureDirtLayerIndex(data);
        if (dirtIdx < 0)
        {
            Debug.LogError("TreeCutterAreaSetup: no dirt/ground layer on terrain.");
            return radius;
        }

        int grassIdx = FindLayerIndex(data.terrainLayers, "GrassLayer");
        int groundIdx = FindLayerIndex(data.terrainLayers, "GroundLayer");
        int sandIdx = FindLayerIndex(data.terrainLayers, "SandLayer");
        int aRes = data.alphamapResolution;
        float[,,] map = data.GetAlphamaps(0, 0, aRes, aRes);
        int layerCount = map.GetLength(2);
        Vector3 tPos = terrain.transform.position;
        Vector3 size = data.size;

        float centerNx = (center.x - tPos.x) / size.x;
        float centerNz = (center.z - tPos.z) / size.z;
        float radiusNx = radius / size.x;
        float radiusNz = radius / size.z;
        float soft = 1f - EdgeSoftness;
        float texelWorld = size.x / Mathf.Max(1, aRes - 1);
        float maxBridgeTexels = RoadConnectMaxGap / Mathf.Max(0.001f, texelWorld);

        // Road / shoulder mask from current splat (before we overwrite).
        bool[,] isRoad = new bool[aRes, aRes];
        int roadCount = 0;
        for (int z = 0; z < aRes; z++)
        {
            for (int x = 0; x < aRes; x++)
            {
                float roadW = 0f;
                if (groundIdx >= 0)
                    roadW += map[z, x, groundIdx];
                if (sandIdx >= 0)
                    roadW += map[z, x, sandIdx] * 0.85f;
                if (roadW >= RoadLayerWeightMin)
                {
                    isRoad[z, x] = true;
                    roadCount++;
                }
            }
        }

        float[,] distRoad = BuildDistanceField(isRoad, aRes);

        float[,] dirtStrength = new float[aRes, aRes];
        int painted = 0;
        int bridged = 0;
        float maxReach = radius;

        for (int z = 0; z < aRes; z++)
        {
            float nz = z / (float)(aRes - 1);
            float dNz = (nz - centerNz) / Mathf.Max(0.0001f, radiusNz);
            for (int x = 0; x < aRes; x++)
            {
                float nx = x / (float)(aRes - 1);
                float dNx = (nx - centerNx) / Mathf.Max(0.0001f, radiusNx);
                float distNorm = Mathf.Sqrt(dNx * dNx + dNz * dNz);

                float dirt = 0f;
                if (distNorm <= 1f)
                {
                    if (distNorm <= soft)
                        dirt = 1f;
                    else
                        dirt = 1f - Mathf.SmoothStep(0f, 1f, (distNorm - soft) / (1f - soft + 0.0001f));

                    if (distNorm > soft * 0.85f)
                    {
                        float wx = tPos.x + nx * size.x;
                        float wz = tPos.z + nz * size.z;
                        float n = Mathf.PerlinNoise(wx * 0.05f, wz * 0.05f);
                        dirt = Mathf.Clamp01(dirt * Mathf.Lerp(0.92f, 1.05f, n));
                    }
                }

                // Bridge: texels whose path cut→road is shorter than the max gap.
                // distToCut in texels ≈ (distNorm - 1) * radius / texel for outside; 0 inside.
                float distCutTexels = Mathf.Max(0f, distNorm - 1f) * (radius / texelWorld);
                float dRoad = distRoad[z, x];
                if (dRoad < float.MaxValue * 0.5f && distCutTexels + dRoad <= maxBridgeTexels)
                {
                    // Stronger near both cut and road; fills the grass strip between them.
                    float bridge = 1f - Mathf.Clamp01((distCutTexels + dRoad) / maxBridgeTexels);
                    bridge = Mathf.SmoothStep(0.35f, 1f, bridge);
                    if (bridge > dirt)
                    {
                        if (dirt < 0.01f)
                            bridged++;
                        dirt = bridge;
                    }
                }

                if (dirt < 0.02f)
                    continue;

                dirtStrength[z, x] = dirt;
                float reachHere = distNorm * radius;
                if (reachHere > maxReach)
                    maxReach = reachHere;
                painted++;
            }
        }

        for (int z = 0; z < aRes; z++)
        {
            for (int x = 0; x < aRes; x++)
            {
                float dirt = dirtStrength[z, x];
                if (dirt < 0.02f)
                    continue;

                for (int l = 0; l < layerCount; l++)
                    map[z, x, l] = 0f;

                map[z, x, dirtIdx] = dirt;
                float remain = 1f - dirt;
                if (remain > 0.001f && grassIdx >= 0)
                    map[z, x, grassIdx] = remain;
                else if (remain > 0.001f)
                    map[z, x, dirtIdx] = 1f;
            }
        }

        data.SetAlphamaps(0, 0, map);
        terrain.Flush();

        int cx = Mathf.Clamp(Mathf.RoundToInt(centerNx * (aRes - 1)), 0, aRes - 1);
        int cz = Mathf.Clamp(Mathf.RoundToInt(centerNz * (aRes - 1)), 0, aRes - 1);
        float[,,] check = data.GetAlphamaps(cx, cz, 1, 1);
        float dirtW = check[0, 0, dirtIdx];
        float grassW = grassIdx >= 0 ? check[0, 0, grassIdx] : -1f;
        Debug.Log(
            "TreeCutterAreaSetup: painted " + painted + " texels (bridge+" + bridged +
            "), roadsDetected=" + roadCount +
            ", reach=" + maxReach.ToString("0.0") + "m" +
            ", center dirt=" + dirtW.ToString("0.00") +
            " grass=" + grassW.ToString("0.00"));

        return Mathf.Max(radius, maxReach + 4f);
    }

    private static float[,] BuildDistanceField(bool[,] seeds, int aRes)
    {
        float[,] dist = new float[aRes, aRes];
        var queue = new Queue<Vector2Int>(aRes * 8);
        const float inf = 1e9f;

        for (int z = 0; z < aRes; z++)
        {
            for (int x = 0; x < aRes; x++)
            {
                if (seeds[z, x])
                {
                    dist[z, x] = 0f;
                    queue.Enqueue(new Vector2Int(x, z));
                }
                else
                {
                    dist[z, x] = inf;
                }
            }
        }

        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        float[] step = { 1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f };

        while (queue.Count > 0)
        {
            Vector2Int p = queue.Dequeue();
            float baseD = dist[p.y, p.x];
            for (int i = 0; i < 8; i++)
            {
                int nx = p.x + dx[i];
                int nz = p.y + dz[i];
                if (nx < 0 || nz < 0 || nx >= aRes || nz >= aRes)
                    continue;
                float nd = baseD + step[i];
                if (nd + 0.001f < dist[nz, nx])
                {
                    dist[nz, nx] = nd;
                    queue.Enqueue(new Vector2Int(nx, nz));
                }
            }
        }

        return dist;
    }

    private static string LayerNames(TerrainLayer[] layers)
    {
        if (layers == null)
            return "(none)";
        var names = new List<string>(layers.Length);
        for (int i = 0; i < layers.Length; i++)
            names.Add(layers[i] != null ? layers[i].name : "null");
        return string.Join(",", names);
    }

    /// <summary>
    /// Prefer a dedicated CutAreaDirt layer when the terrain already has free slots;
    /// otherwise reuse GroundLayer (always present on the reference map).
    /// </summary>
    private static int EnsureDirtLayerIndex(TerrainData data)
    {
        TerrainLayer[] current = data.terrainLayers;
        int cutIdx = FindLayerIndex(current, "CutAreaDirt");
        if (cutIdx >= 0)
            return cutIdx;

        int groundIdx = FindLayerIndex(current, "GroundLayer");

        // URP TerrainLit commonly samples up to 4 layers strongly; avoid going past 4.
        if (current != null && current.Length >= 4)
        {
            if (groundIdx >= 0)
                return groundIdx;
            return current.Length > 1 ? 1 : 0;
        }

        TerrainLayer dirt = AssetDatabase.LoadAssetAtPath<TerrainLayer>(CutDirtLayerPath);
        if (dirt == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/TerrainPaintPreparation"))
                AssetDatabase.CreateFolder("Assets", "TerrainPaintPreparation");

            dirt = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundDiffusePath),
                normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundNormalPath),
                tileSize = new Vector2(6f, 6f),
                diffuseRemapMin = new Vector4(0f, 0f, 0f, 0f),
                diffuseRemapMax = new Vector4(0.9f, 0.58f, 0.34f, 1f),
                normalScale = 1.1f
            };
            AssetDatabase.CreateAsset(dirt, CutDirtLayerPath);
            AssetDatabase.SaveAssets();
        }

        List<TerrainLayer> layers = new List<TerrainLayer>(current ?? new TerrainLayer[0]);
        layers.Add(dirt);
        data.terrainLayers = layers.ToArray();
        return layers.Count - 1;
    }

    private static int FindLayerIndex(TerrainLayer[] layers, string name)
    {
        if (layers == null)
            return -1;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] != null && layers[i].name == name)
                return i;
        }

        return -1;
    }

    private static void StripDynamicGrassInCircle(Vector3 center, float radius)
    {
        GameObject root = GameObject.Find("Dynamic Grass");
        if (root == null)
            return;

        float radiusSq = radius * radius * 1.15f;
        List<GameObject> remove = new List<GameObject>();
        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform t = root.transform.GetChild(i);
            float dx = t.position.x - center.x;
            float dz = t.position.z - center.z;
            if (dx * dx + dz * dz <= radiusSq)
                remove.Add(t.gameObject);
        }

        for (int i = 0; i < remove.Count; i++)
            Undo.DestroyObjectImmediate(remove[i]);

        if (remove.Count > 0)
            Debug.Log("TreeCutterAreaSetup: stripped " + remove.Count + " Dynamic Grass chunks in cut area.");
    }

    private static void ClearDetailGrassInCircle(Terrain terrain, Vector3 center, float radius)
    {
        TerrainData data = terrain.terrainData;
        DetailPrototype[] protos = data.detailPrototypes;
        if (protos == null || protos.Length == 0)
            return;

        int res = data.detailResolution;
        Vector3 tPos = terrain.transform.position;
        Vector3 size = data.size;
        float radiusSq = radius * radius;

        for (int layer = 0; layer < protos.Length; layer++)
        {
            int[,] map = data.GetDetailLayer(0, 0, res, res, layer);
            bool changed = false;
            for (int z = 0; z < res; z++)
            {
                float wz = tPos.z + ((z + 0.5f) / res) * size.z;
                for (int x = 0; x < res; x++)
                {
                    float wx = tPos.x + ((x + 0.5f) / res) * size.x;
                    float dx = wx - center.x;
                    float dz = wz - center.z;
                    if (dx * dx + dz * dz > radiusSq)
                        continue;
                    if (map[z, x] == 0)
                        continue;
                    map[z, x] = 0;
                    changed = true;
                }
            }

            if (changed)
                data.SetDetailLayer(0, 0, layer, map);
        }
    }

    private static void PlaceCutProps(
        Terrain terrain,
        Vector3 center,
        float radius,
        Vector3? housePos,
        GameObject stumpPrefab,
        GameObject bushPrefab,
        GameObject tree01Prefab,
        GameObject tree02Prefab)
    {
        GameObject existing = GameObject.Find(PropsRootName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        GameObject root = new GameObject(PropsRootName);
        Undo.RegisterCreatedObjectUndo(root, "Cut tree area props");

        System.Random rng = new System.Random(7331);
        int steps = Mathf.Max(4, Mathf.FloorToInt((radius * 2f) / PropSpacing));
        int placed = 0;

        for (int iz = 0; iz < steps; iz++)
        {
            for (int ix = 0; ix < steps; ix++)
            {
                float nx = (ix + 0.5f) / steps;
                float nz = (iz + 0.5f) / steps;
                float jx = ((float)rng.NextDouble() - 0.5f) * PropSpacing * 0.7f;
                float jz = ((float)rng.NextDouble() - 0.5f) * PropSpacing * 0.7f;

                float wx = center.x - radius + nx * radius * 2f + jx;
                float wz = center.z - radius + nz * radius * 2f + jz;
                float dx = wx - center.x;
                float dz = wz - center.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);

                // Round footprint with slightly ragged edge.
                float edgeNoise = Mathf.PerlinNoise(wx * 0.05f, wz * 0.05f) * 0.12f;
                if (dist > radius * (0.92f + edgeNoise))
                    continue;

                // Sparse near the very center / denser mid-ring.
                float ring = dist / radius;
                float chance = Mathf.Lerp(0.35f, 0.9f, ring);
                if (rng.NextDouble() > chance)
                    continue;

                if (housePos.HasValue)
                {
                    float hx = wx - housePos.Value.x;
                    float hz = wz - housePos.Value.z;
                    if (hx * hx + hz * hz < HouseClearRadius * HouseClearRadius)
                        continue;
                }

                float wy = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + terrain.transform.position.y;
                Vector3 pos = new Vector3(wx, wy, wz);
                float yaw = (float)(rng.NextDouble() * 360.0);
                float roll = (float)rng.NextDouble();

                // Mostly stumps; some leftover trees / bushes; occasional felled tree.
                if (roll < 0.55)
                {
                    Spawn(stumpPrefab, root.transform, pos, yaw, Scale(rng, 0.85f, 1.25f));
                    placed++;

                    // Occasional bush beside stump.
                    if (rng.NextDouble() < 0.35)
                    {
                        float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                        float off = 1.2f + (float)rng.NextDouble() * 2.2f;
                        Vector3 bPos = pos + new Vector3(Mathf.Cos(ang) * off, 0f, Mathf.Sin(ang) * off);
                        bPos.y = terrain.SampleHeight(bPos) + terrain.transform.position.y;
                        if (!TooCloseToHouse(bPos, housePos))
                        {
                            Spawn(bushPrefab, root.transform, bPos, yaw + 40f, Scale(rng, 0.7f, 1.15f));
                            placed++;
                        }
                    }
                }
                else if (roll < 0.72)
                {
                    Spawn(bushPrefab, root.transform, pos, yaw, Scale(rng, 0.75f, 1.2f));
                    placed++;
                }
                else if (roll < 0.88)
                {
                    GameObject tree = rng.NextDouble() < 0.5 ? tree01Prefab : tree02Prefab;
                    Spawn(tree, root.transform, pos, yaw, Scale(rng, 0.7f, 1.1f));
                    placed++;
                }
                else
                {
                    // Felled tree lying on the ground.
                    GameObject tree = rng.NextDouble() < 0.5 ? tree01Prefab : tree02Prefab;
                    float tilt = 78f + (float)rng.NextDouble() * 14f;
                    float tiltAxis = (float)(rng.NextDouble() * 360.0);
                    GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(tree, root.transform);
                    Undo.RegisterCreatedObjectUndo(go, "Felled tree");
                    go.transform.position = pos;
                    go.transform.rotation = Quaternion.Euler(tilt, yaw, 0f) * Quaternion.Euler(0f, tiltAxis, 0f);
                    float s = Scale(rng, 0.65f, 1.0f);
                    go.transform.localScale = Vector3.one * s;
                    placed++;
                }
            }
        }

        Debug.Log("TreeCutterAreaSetup: placed " + placed + " cut-area props.");
    }

    private static bool TooCloseToHouse(Vector3 pos, Vector3? housePos)
    {
        if (!housePos.HasValue)
            return false;
        float hx = pos.x - housePos.Value.x;
        float hz = pos.z - housePos.Value.z;
        return hx * hx + hz * hz < HouseClearRadius * HouseClearRadius;
    }

    private static float Scale(System.Random rng, float min, float max)
    {
        return Mathf.Lerp(min, max, (float)rng.NextDouble());
    }

    private static void Spawn(GameObject prefab, Transform parent, Vector3 pos, float yaw, float scale)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, prefab.name);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
    }
}
