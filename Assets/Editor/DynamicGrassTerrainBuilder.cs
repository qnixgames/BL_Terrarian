using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds Dynamic Grass FX meshes on terrain cells painted with the grass TerrainLayer.
/// Chunks enable frustum culling + distance LOD via DynamicGrassTerrain.
/// </summary>
public static class DynamicGrassTerrainBuilder
{
    private const string MenuRoot = "Tools/Reference Map/";
    private const string RootName = "Dynamic Grass";
    private const string MaterialPath = "Assets/Dynamic Grass FX/Terrain Grass Material.mat";
    private const string UrpShaderName = "Bytesized/GrassURP";
    private const string GrassLayerName = "GrassLayer";
    private const string MeshFolder = "Assets/DynamicGrassGenerated";

    // Coarse base mesh: tessellation (_ViewLOD / _MaxStages) densifies near camera.
    private const float CellSize = 4f;
    private const float ChunkWorldSize = 40f;
    private const float GrassWeightThreshold = 0.4f;
    private const float HeightOffset = 0.05f;

    [MenuItem(MenuRoot + "Build Dynamic Grass (green areas)", priority = 220)]
    public static void BuildFromMenu()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        Build(terrain);
    }

    [MenuItem(MenuRoot + "Clear Dynamic Grass", priority = 221)]
    public static void ClearFromMenu()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing == null)
        {
            Debug.Log("Dynamic Grass: nothing to clear.");
            return;
        }

        Undo.DestroyObjectImmediate(existing);
        ClearGeneratedMeshes();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Dynamic Grass cleared.");
    }

    [MenuItem(MenuRoot + "Remove Terrain Detail Grass", priority = 222)]
    public static void RemoveTerrainDetailGrassMenu()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        ClearTerrainDetailGrass(terrain);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Terrain detail grass removed — only Dynamic Grass remains.");
    }

    public static void Build(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog("Dynamic Grass", "Terrain / TerrainData missing.", "OK");
            return;
        }

        TerrainData data = terrain.terrainData;
        int grassLayer = FindGrassLayerIndex(data);
        if (grassLayer < 0)
        {
            EditorUtility.DisplayDialog(
                "Dynamic Grass",
                "GrassLayer TerrainLayer not found on MainTerrain.",
                "OK");
            return;
        }

        // Dynamic grass replaces Unity terrain detail meshes on green areas.
        ClearTerrainDetailGrass(terrain);

        Material material = EnsureMaterial();
        if (material == null)
            return;

        EnsureMeshFolder();
        ClearGeneratedMeshes();

        GameObject root = GameObject.Find(RootName);
        if (root != null)
            Undo.DestroyObjectImmediate(root);

        root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Dynamic Grass");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        DynamicGrassTerrain controller = root.AddComponent<DynamicGrassTerrain>();
        Camera cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        if (cam != null)
            controller.SetLodTarget(cam.transform);

        Vector3 origin = terrain.transform.position;
        Vector3 size = data.size;
        int cellsX = Mathf.Max(1, Mathf.CeilToInt(size.x / CellSize));
        int cellsZ = Mathf.Max(1, Mathf.CeilToInt(size.z / CellSize));
        int chunkCells = Mathf.Max(1, Mathf.RoundToInt(ChunkWorldSize / CellSize));

        float[,,] alphamap = data.GetAlphamaps(0, 0, data.alphamapResolution, data.alphamapResolution);
        int aRes = data.alphamapResolution;

        List<DynamicGrassChunk> builtChunks = new List<DynamicGrassChunk>();
        int chunkIndex = 0;
        int totalQuads = 0;

        try
        {
            for (int cz = 0; cz < cellsZ; cz += chunkCells)
            {
                for (int cx = 0; cx < cellsX; cx += chunkCells)
                {
                    int endX = Mathf.Min(cx + chunkCells, cellsX);
                    int endZ = Mathf.Min(cz + chunkCells, cellsZ);

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Dynamic Grass",
                            "Building chunk " + (chunkIndex + 1),
                            (float)chunkIndex / Mathf.Max(1, (cellsX / chunkCells) * (cellsZ / chunkCells))))
                    {
                        break;
                    }

                    List<Vector3> vertices = new List<Vector3>();
                    List<Vector3> normals = new List<Vector3>();
                    List<Vector4> tangents = new List<Vector4>();
                    List<int> indices = new List<int>();

                    float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                    float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
                    int quads = 0;

                    for (int z = cz; z < endZ; z++)
                    {
                        for (int x = cx; x < endX; x++)
                        {
                            float nx0 = x / (float)cellsX;
                            float nz0 = z / (float)cellsZ;
                            float nx1 = (x + 1) / (float)cellsX;
                            float nz1 = (z + 1) / (float)cellsZ;
                            float nxc = (nx0 + nx1) * 0.5f;
                            float nzc = (nz0 + nz1) * 0.5f;

                            float grass = SampleGrass(alphamap, aRes, grassLayer, nxc, nzc);
                            if (grass < GrassWeightThreshold)
                                continue;

                            // Skip very steep slopes — grass looks wrong and wastes tessellation.
                            Vector3 normal = data.GetInterpolatedNormal(nxc, nzc);
                            if (normal.y < 0.55f)
                                continue;

                            Vector3 p00 = SampleWorld(terrain, origin, size, nx0, nz0);
                            Vector3 p10 = SampleWorld(terrain, origin, size, nx1, nz0);
                            Vector3 p01 = SampleWorld(terrain, origin, size, nx0, nz1);
                            Vector3 p11 = SampleWorld(terrain, origin, size, nx1, nz1);

                            int baseIndex = vertices.Count;
                            vertices.Add(p00);
                            vertices.Add(p10);
                            vertices.Add(p01);
                            vertices.Add(p11);

                            normals.Add(normal);
                            normals.Add(normal);
                            normals.Add(normal);
                            normals.Add(normal);

                            Vector3 tangent = Vector3.Cross(normal, Vector3.forward);
                            if (tangent.sqrMagnitude < 0.0001f)
                                tangent = Vector3.Cross(normal, Vector3.right);
                            tangent.Normalize();
                            Vector4 t4 = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
                            tangents.Add(t4);
                            tangents.Add(t4);
                            tangents.Add(t4);
                            tangents.Add(t4);

                            // Two triangles. Winding matches upward normal.
                            indices.Add(baseIndex + 0);
                            indices.Add(baseIndex + 2);
                            indices.Add(baseIndex + 1);
                            indices.Add(baseIndex + 1);
                            indices.Add(baseIndex + 2);
                            indices.Add(baseIndex + 3);

                            Encapsulate(ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ, p00);
                            Encapsulate(ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ, p10);
                            Encapsulate(ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ, p01);
                            Encapsulate(ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ, p11);
                            quads++;
                        }
                    }

                    if (quads == 0)
                        continue;

                    Mesh mesh = new Mesh
                    {
                        name = "DynamicGrassChunk_" + chunkIndex,
                        indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
                    };
                    mesh.SetVertices(vertices);
                    mesh.SetNormals(normals);
                    mesh.SetTangents(tangents);
                    mesh.SetTriangles(indices, 0, true);
                    mesh.RecalculateBounds();

                    string meshPath = MeshFolder + "/DynamicGrassChunk_" + chunkIndex + ".asset";
                    AssetDatabase.CreateAsset(mesh, meshPath);

                    GameObject chunkGo = new GameObject("GrassChunk_" + chunkIndex);
                    Undo.RegisterCreatedObjectUndo(chunkGo, "Build Dynamic Grass Chunk");
                    chunkGo.transform.SetParent(root.transform, false);

                    MeshFilter filter = chunkGo.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;

                    MeshRenderer renderer = chunkGo.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.allowOcclusionWhenDynamic = false;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    // RayTracingMode enum not in URP API — force serialized Off (0).
                    SerializedObject so = new SerializedObject(renderer);
                    SerializedProperty rt = so.FindProperty("m_RayTracingMode");
                    if (rt != null)
                    {
                        rt.intValue = 0;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }

                    Vector3 center = new Vector3(
                        (minX + maxX) * 0.5f,
                        (minY + maxY) * 0.5f,
                        (minZ + maxZ) * 0.5f);
                    float radius = Vector3.Distance(
                        center,
                        new Vector3(maxX, maxY, maxZ));

                    DynamicGrassChunk chunk = chunkGo.AddComponent<DynamicGrassChunk>();
                    chunk.Configure(center, radius);
                    builtChunks.Add(chunk);

                    totalQuads += quads;
                    chunkIndex++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        controller.SetChunks(builtChunks.ToArray());
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log(
            "Dynamic Grass built: " + builtChunks.Count + " chunks, " +
            totalQuads + " quads. Terrain detail grass cleared.");
    }

    /// <summary>
    /// Removes Unity Terrain detail prototypes (mesh/texture grass) so only Dynamic Grass shows.
    /// Trees are left untouched.
    /// </summary>
    public static void ClearTerrainDetailGrass(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null)
            return;

        TerrainData data = terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(data, "Clear terrain detail grass");
        Undo.RecordObject(terrain, "Clear terrain detail grass");

        int res = data.detailResolution;
        int layerCount = data.detailPrototypes != null ? data.detailPrototypes.Length : 0;
        if (res > 0 && layerCount > 0)
        {
            int[,] empty = new int[res, res];
            for (int i = 0; i < layerCount; i++)
                data.SetDetailLayer(0, 0, i, empty);
        }

        data.detailPrototypes = new DetailPrototype[0];
        terrain.detailObjectDensity = 0f;
        terrain.detailObjectDistance = 0f;
        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
    }

    private static Vector3 SampleWorld(Terrain terrain, Vector3 origin, Vector3 size, float nx, float nz)
    {
        float y = terrain.SampleHeight(new Vector3(origin.x + nx * size.x, 0f, origin.z + nz * size.z))
                  + origin.y + HeightOffset;
        return new Vector3(origin.x + nx * size.x, y, origin.z + nz * size.z);
    }

    private static float SampleGrass(float[,,] alphamap, int aRes, int layer, float nx, float nz)
    {
        int ax = Mathf.Clamp(Mathf.RoundToInt(nx * (aRes - 1)), 0, aRes - 1);
        int az = Mathf.Clamp(Mathf.RoundToInt(nz * (aRes - 1)), 0, aRes - 1);
        // Alphamap indexing: [y, x, layer] where y = Z, x = X.
        return alphamap[az, ax, layer];
    }

    private static void Encapsulate(
        ref float minX, ref float minY, ref float minZ,
        ref float maxX, ref float maxY, ref float maxZ,
        Vector3 p)
    {
        if (p.x < minX) minX = p.x;
        if (p.y < minY) minY = p.y;
        if (p.z < minZ) minZ = p.z;
        if (p.x > maxX) maxX = p.x;
        if (p.y > maxY) maxY = p.y;
        if (p.z > maxZ) maxZ = p.z;
    }

    private static int FindGrassLayerIndex(TerrainData data)
    {
        TerrainLayer[] layers = data.terrainLayers;
        if (layers == null)
            return -1;

        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] != null && layers[i].name == GrassLayerName)
                return i;
        }

        // ReferenceMapBuilder paints grass as layer 0.
        return layers.Length > 0 ? 0 : -1;
    }

    private static Material EnsureMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find(UrpShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "Dynamic Grass",
                "URP shader '" + UrpShaderName + "' not found. Wait for import, then retry.",
                "OK");
            return null;
        }

        if (existing != null)
        {
            if (existing.shader != shader)
            {
                existing.shader = shader;
                EditorUtility.SetDirty(existing);
            }
            return existing;
        }

        Material mat = new Material(shader)
        {
            name = "Terrain Grass Material"
        };
        mat.SetColor("_TopColor", new Color(0.18f, 0.32f, 0.12f, 1f));
        mat.SetColor("_BottomColor", new Color(0.02f, 0.08f, 0.03f, 1f));
        mat.SetFloat("_TranslucentGain", 0.12f);
        mat.SetFloat("_WindStrength", 0.35f);
        mat.SetFloat("_ViewLOD", 48f);
        mat.SetFloat("_MaxStages", 7f);
        mat.SetFloat("_BaseStages", -0.5f);
        mat.SetFloat("_BladeWidth", 0.05f);
        mat.SetFloat("_BladeWidthRandom", 0.02f);
        mat.SetFloat("_BladeHeight", 0.55f);
        mat.SetFloat("_BladeHeightRandom", 0.25f);
        mat.SetFloat("_BladeForward", 0.38f);
        mat.SetFloat("_BladeCurve", 2f);
        mat.SetFloat("_BendRotationRandom", 0.2f);

        AssetDatabase.CreateAsset(mat, MaterialPath);
        return mat;
    }

    private static void EnsureMeshFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/DynamicGrassGenerated"))
            AssetDatabase.CreateFolder("Assets", "DynamicGrassGenerated");
    }

    private static void ClearGeneratedMeshes()
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder))
            return;

        string[] guids = AssetDatabase.FindAssets("t:Mesh", new[] { MeshFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AssetDatabase.DeleteAsset(path);
        }
    }

    private static bool TryGetMainTerrain(out Terrain terrain)
    {
        terrain = null;
        Terrain[] all = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "MainTerrain")
            {
                terrain = all[i];
                return true;
            }
        }

        if (Selection.activeGameObject != null)
        {
            terrain = Selection.activeGameObject.GetComponent<Terrain>();
            if (terrain != null)
                return true;
        }

        EditorUtility.DisplayDialog(
            "MainTerrain required",
            "Select MainTerrain in the Hierarchy, or ensure a GameObject named MainTerrain exists.",
            "OK");
        return false;
    }
}
