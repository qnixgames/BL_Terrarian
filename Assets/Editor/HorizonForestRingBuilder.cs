using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a cheap forest silhouette ring outside MainTerrain so high views
/// read as endless woods instead of a hard map edge.
/// </summary>
public static class HorizonForestRingBuilder
{
    private const string MenuRoot = "Tools/Reference Map/";
    private const string RootName = "Horizon Forest Ring";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PendingFlag = "Assets/Editor/BuildHorizonForest.flag";
    private const string VegetationFolder = "Assets/ReferenceMapVegetation";
    private const string PineFolder =
        "Assets/3rdParty/PSX Packs/PSX Nature/PSX Nature/Models/other-formats/FBX/";
    private const string SilhouetteMatPath =
        "Assets/TerrainPaintPreparation/HorizonForestSilhouette.mat";

    private static readonly string[] PineNames =
    {
        "pine_tree_n_1",
        "pine_tree_n_1_2",
        "pine_tree_n_2",
        "pine_tree_n_2_2",
        "pine_tree_n_3",
        "pine_tree_n_3_2"
    };

    private const float TreeHeightMin = 18f;
    private const float TreeHeightMax = 36f;

    [InitializeOnLoadMethod]
    private static void HookPendingFlag()
    {
        EditorApplication.delayCall += TryConsumePendingFlag;
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
    }

    private static bool s_Running;

    public static void TryConsumePendingFlagPublic()
    {
        TryConsumePendingFlag();
    }

    private static void TryConsumePendingFlag()
    {
        if (s_Running || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (!File.Exists(PendingFlag))
            return;

        s_Running = true;
        try
        {
            File.Delete(PendingFlag);
            string meta = PendingFlag + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }
        catch (IOException)
        {
            s_Running = false;
            return;
        }

        BuildFromCommandLine();
        s_Running = false;
    }

    [MenuItem(MenuRoot + "Build Horizon Forest Ring", priority = 266)]
    public static void BuildFromMenu()
    {
        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        Build(terrain);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        EditorSceneManager.SaveScene(terrain.gameObject.scene);
        Debug.Log("Horizon Forest Ring built and scene saved.");
    }

    [MenuItem(MenuRoot + "Clear Horizon Forest Ring", priority = 267)]
    public static void ClearFromMenu()
    {
        Clear();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Horizon Forest Ring cleared.");
    }

    public static void BuildFromCommandLine()
    {
        Scene active = SceneManager.GetActiveScene();
        if (!active.IsValid() || active.path != ScenePath)
        {
            if (!EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single).IsValid())
            {
                Debug.LogError("HorizonForestRingBuilder: failed to open " + ScenePath);
                return;
            }
        }

        if (!TryGetMainTerrain(out Terrain terrain))
            return;

        Build(terrain);
        // Also reinforce edge belt + ensure day fog if day mode.
        ReferenceMapBuilder_EdgeBeltProxy(terrain);
        if (!NightAtmosphereSetup.IsNight())
            NightAtmosphereSetup.ApplyDay();

        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("HorizonForestRingBuilder: ring + edge belt + day fog applied.");
    }

    // Avoid circular private access — call public reinforce API.
    private static void ReferenceMapBuilder_EdgeBeltProxy(Terrain terrain)
    {
        ReferenceMapBuilder.ReinforcedEdgeForestBelt(terrain);
    }

    public static void Clear()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);
    }

    public static void Build(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog("Horizon Forest", "MainTerrain / TerrainData missing.", "OK");
            return;
        }

        List<GameObject> pines = LoadPinePrefabs();
        if (pines.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Horizon Forest",
                "No pine prefabs found under " + VegetationFolder,
                "OK");
            return;
        }

        Clear();

        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        Vector3 size = data.size;
        Vector3 center = origin + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
        float half = Mathf.Max(size.x, size.z) * 0.5f;

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Horizon Forest Ring");
        root.transform.position = Vector3.zero;

        Material silMat = EnsureSilhouetteMaterial();
        System.Random rng = new System.Random(2026);
        int placed = 0;

        for (int row = 0; row < 3; row++)
        {
            float t = row / 2f;
            // Just outside the map half-extent so the ring sits beyond the playable terrain.
            float ringRadius = half * Mathf.Lerp(1.12f, 1.42f, t);
            int count = 160 + row * 24;
            for (int i = 0; i < count; i++)
            {
                float ang = (i / (float)count) * Mathf.PI * 2f;
                ang += ((float)rng.NextDouble() - 0.5f) * 0.04f;
                float r = ringRadius + ((float)rng.NextDouble() - 0.5f) * (half * 0.04f);
                float wx = center.x + Mathf.Cos(ang) * r;
                float wz = center.z + Mathf.Sin(ang) * r;

                float wy = SampleEdgeHeight(terrain, origin, size) + ((float)rng.NextDouble() * 2f);

                GameObject prefab = pines[rng.Next(0, pines.Count)];
                GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                Undo.RegisterCreatedObjectUndo(go, "Horizon tree");
                go.transform.position = new Vector3(wx, wy, wz);
                go.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);

                float h = Mathf.Lerp(TreeHeightMin, TreeHeightMax, (float)rng.NextDouble());
                float s = h / 9f;
                float squat = Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());
                go.transform.localScale = new Vector3(s * squat, s, s * squat);

                ApplyCheapRender(go);
                placed++;
            }
        }

        // Outer continuous silhouette cards for far fill (cheap quads facing inward).
        PlaceSilhouetteCards(root.transform, center, half * 1.42f, half * 1.58f, silMat, rng);

        StaticEditorFlags flags =
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccludeeStatic;
        SetStaticRecursive(root, flags);

        Debug.Log("Horizon Forest Ring: placed " + placed + " trees + silhouette cards.");
    }

    private static float SampleEdgeHeight(Terrain terrain, Vector3 origin, Vector3 size)
    {
        // Average a few near-edge samples.
        float sum = 0f;
        Vector2[] pts =
        {
            new Vector2(0.05f, 0.5f),
            new Vector2(0.95f, 0.5f),
            new Vector2(0.5f, 0.05f),
            new Vector2(0.5f, 0.95f)
        };
        for (int i = 0; i < pts.Length; i++)
        {
            float wx = origin.x + pts[i].x * size.x;
            float wz = origin.z + pts[i].y * size.z;
            sum += terrain.SampleHeight(new Vector3(wx, 0f, wz)) + origin.y;
        }

        return sum / pts.Length;
    }

    private static void PlaceSilhouetteCards(
        Transform parent,
        Vector3 center,
        float rInner,
        float rOuter,
        Material mat,
        System.Random rng)
    {
        const int cards = 48;
        GameObject cardRoot = new GameObject("Silhouette Cards");
        cardRoot.transform.SetParent(parent, false);

        for (int i = 0; i < cards; i++)
        {
            float ang = (i / (float)cards) * Mathf.PI * 2f;
            float r = Mathf.Lerp(rInner, rOuter, (float)rng.NextDouble());
            Vector3 pos = new Vector3(
                center.x + Mathf.Cos(ang) * r,
                center.y + Mathf.Lerp(8f, 14f, (float)rng.NextDouble()),
                center.z + Mathf.Sin(ang) * r);

            GameObject card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(card.GetComponent<Collider>());
            card.name = "ForestCard_" + i;
            card.transform.SetParent(cardRoot.transform, false);
            card.transform.position = pos;
            // Face toward map center.
            Vector3 look = center - pos;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                card.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            float w = Mathf.Lerp(28f, 48f, (float)rng.NextDouble());
            float h = Mathf.Lerp(22f, 40f, (float)rng.NextDouble());
            card.transform.localScale = new Vector3(w, h, 1f);

            MeshRenderer mr = card.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
    }

    private static void ApplyCheapRender(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            Object.DestroyImmediate(cols[i]);
    }

    private static Material EnsureSilhouetteMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(SilhouetteMatPath);
        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder("Assets/TerrainPaintPreparation"))
            AssetDatabase.CreateFolder("Assets", "TerrainPaintPreparation");

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        Color c = new Color(0.12f, 0.22f, 0.10f, 1f);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", c);
        AssetDatabase.CreateAsset(mat, SilhouetteMatPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static List<GameObject> LoadPinePrefabs()
    {
        List<GameObject> list = new List<GameObject>();
        for (int i = 0; i < PineNames.Length; i++)
        {
            string path = VegetationFolder + "/" + PineNames[i] + ".prefab";
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null)
                list.Add(go);
            else
            {
                GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(PineFolder + PineNames[i] + ".fbx");
                if (fbx != null)
                    list.Add(fbx);
            }
        }

        return list;
    }

    private static void SetStaticRecursive(GameObject go, StaticEditorFlags flags)
    {
        GameObjectUtility.SetStaticEditorFlags(go, flags);
        for (int i = 0; i < go.transform.childCount; i++)
            SetStaticRecursive(go.transform.GetChild(i).gameObject, flags);
    }

    private static bool TryGetMainTerrain(out Terrain terrain)
    {
        terrain = null;
        Terrain[] all = Object.FindObjectsByType<Terrain>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "MainTerrain")
            {
                terrain = all[i];
                return true;
            }
        }

        if (all.Length > 0)
        {
            terrain = all[0];
            return true;
        }

        EditorUtility.DisplayDialog("MainTerrain required", "MainTerrain not found in open scene.", "OK");
        return false;
    }
}
