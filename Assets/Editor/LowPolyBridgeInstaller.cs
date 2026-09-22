using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Replaces placeholder cube bridges with BluBlu Low Poly Forest SM_Bridge,
/// and fits WalkDeck / rail BoxColliders to the mesh bounds (not a world-aligned slab).
/// </summary>
[InitializeOnLoad]
public static class LowPolyBridgeInstaller
{
    private const string MenuRoot = "Tools/Reference Map/";
    private const string BridgePrefabPath =
        "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_Bridge.prefab";
    private const string SessionKey = "BL_Terrarian.LowPolyBridgesInstalled";
    private const string FordSessionKey = "BL_Terrarian.RiverFordsCleared";
    private const string ColliderSessionKey = "BL_Terrarian.BridgeCollidersV2";

    private const float RiverSouth = 0.48f;
    private const float RiverNorth = 0.52f;
    private static readonly float[] BridgeXs = { 0.25f, 0.50f, 0.75f };

    // Fraction of mesh height used as the walkable plank slab (from mesh bottom).
    private const float DeckHeightFraction = 0.20f;
    // Inset from each long side so the walkable strip sits between railings.
    private const float SideInsetFraction = 0.16f;
    // Rail thickness as fraction of mesh cross-width.
    private const float RailThicknessFraction = 0.10f;

    static LowPolyBridgeInstaller()
    {
        EditorApplication.delayCall += AutoInstallIfNeeded;
        EditorApplication.delayCall += AutoRefitCollidersIfNeeded;
    }

    [MenuItem(MenuRoot + "Replace Bridges (Low Poly Pack)", priority = 230)]
    public static void ReplaceFromMenu()
    {
        int n = ReplaceBridges();
        ReferenceMapBuilder.ApplyBridgeOnlyCrossing();
        SessionState.SetBool(SessionKey, true);
        SessionState.SetBool(FordSessionKey, true);
        SessionState.SetBool(ColliderSessionKey, true);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Low Poly bridges installed: " + n + " + river fords cleared.");
    }

    [MenuItem(MenuRoot + "Refit Bridge Colliders (match model)", priority = 231)]
    public static void RefitCollidersFromMenu()
    {
        int n = RefitAllBridgeColliders();
        SessionState.SetBool(ColliderSessionKey, true);
        if (n > 0)
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Bridge colliders refit to mesh: " + n);
    }

    private static void AutoInstallIfNeeded()
    {
        bool needBridges = !SessionState.GetBool(SessionKey, false) && HasPlaceholderBridges();
        bool needFords = !SessionState.GetBool(FordSessionKey, false) && HasRiverFordHeights();
        if (!needBridges && !needFords)
            return;

        int n = 0;
        if (needBridges)
        {
            n = ReplaceBridges();
            SessionState.SetBool(SessionKey, true);
            SessionState.SetBool(ColliderSessionKey, true);
        }

        if (needFords || n > 0)
        {
            ReferenceMapBuilder.ApplyBridgeOnlyCrossing();
            SessionState.SetBool(FordSessionKey, true);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(
            "Bridge-only crossing applied. Bridges replaced: " + n +
            ", river fords cleared: " + needFords);
    }

    private static void AutoRefitCollidersIfNeeded()
    {
        if (SessionState.GetBool(ColliderSessionKey, false))
            return;

        int n = RefitAllBridgeColliders();
        if (n > 0)
        {
            SessionState.SetBool(ColliderSessionKey, true);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Auto-refit bridge colliders to SM_Bridge mesh: " + n);
        }
    }

    private static bool HasRiverFordHeights()
    {
        Terrain terrain = FindMainTerrain();
        if (terrain == null || terrain.terrainData == null)
            return false;

        TerrainData data = terrain.terrainData;
        int res = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, res, res);

        for (int i = 0; i < BridgeXs.Length; i++)
        {
            int x = Mathf.RoundToInt(BridgeXs[i] * (res - 1));
            int z = Mathf.RoundToInt(0.5f * (res - 1));
            if (heights[z, x] > 0.07f)
                return true;
        }

        return false;
    }

    private static bool HasPlaceholderBridges()
    {
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null)
                continue;
            string n = all[i].name;
            if (!n.StartsWith("Bridge_"))
                continue;

            if (all[i].Find("WalkDeck") != null || all[i].Find("SM_Bridge") != null)
                continue;

            MeshFilter mf = all[i].GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.Contains("Cube"))
                return true;
        }

        return false;
    }

    public static int ReplaceBridges()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BridgePrefabPath);
        if (prefab == null)
        {
            Debug.LogError("Missing bridge prefab: " + BridgePrefabPath);
            return 0;
        }

        Terrain terrain = FindMainTerrain();
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("MainTerrain not found.");
            return 0;
        }

        Transform bridgesRoot = FindOrCreateBridgesRoot();
        ClearChildren(bridgesRoot);

        MeshFilter prefabFilter = prefab.GetComponentInChildren<MeshFilter>();
        if (prefabFilter == null || prefabFilter.sharedMesh == null)
        {
            Debug.LogError("SM_Bridge has no mesh.");
            return 0;
        }

        Bounds meshBounds = prefabFilter.sharedMesh.bounds;
        TerrainData data = terrain.terrainData;
        float playerHeight = 1.8f;
        float bridgeLen = (RiverNorth - RiverSouth) * data.size.z + playerHeight * 4f;
        float bridgeWidth = Mathf.Max(8f, playerHeight * 9f);

        int count = 0;
        for (int i = 0; i < BridgeXs.Length; i++)
        {
            float nx = BridgeXs[i];
            float nz = 0.5f;
            float bankY = SampleBankHeight(terrain, nx, nz);

            GameObject root = new GameObject("Bridge_" + (i + 1));
            Undo.RegisterCreatedObjectUndo(root, "Low Poly Bridge");
            root.transform.SetParent(bridgesRoot, false);
            root.transform.position = new Vector3(
                terrain.transform.position.x + nx * data.size.x,
                bankY,
                terrain.transform.position.z + nz * data.size.z);
            root.transform.rotation = Quaternion.identity;

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(visual, "SM_Bridge instance");
            visual.name = "SM_Bridge";
            visual.transform.SetParent(root.transform, false);

            FitVisualToSpan(visual, meshBounds, bridgeWidth, bridgeLen, bankY, root.transform.position);
            CreateModelFittedColliders(visual, prefabFilter.sharedMesh);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Rebuilds WalkDeck + rail colliders on every SM_Bridge already in the scene.
    /// </summary>
    public static int RefitAllBridgeColliders()
    {
        int count = 0;
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.name != "SM_Bridge")
                continue;

            MeshFilter filter = t.GetComponentInChildren<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;

            // Remove legacy world-aligned WalkDeck under Bridge_* root.
            Transform root = t.parent;
            if (root != null)
                DestroyNamedChild(root, "WalkDeck");

            CreateModelFittedColliders(t.gameObject, filter.sharedMesh);
            count++;
        }

        return count;
    }

    private static void FitVisualToSpan(
        GameObject visual,
        Bounds meshBounds,
        float targetWidth,
        float targetLength,
        float bankY,
        Vector3 rootWorldPos)
    {
        Vector3 size = meshBounds.size;
        if (size.x < 0.001f || size.y < 0.001f || size.z < 0.001f)
            return;

        bool lengthOnLocalX = size.x >= size.z;
        float yaw = lengthOnLocalX ? 90f : 0f;
        visual.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        float meshLength = lengthOnLocalX ? size.x : size.z;
        float meshWidth = lengthOnLocalX ? size.z : size.x;

        float scaleLength = targetLength / meshLength;
        float scaleWidth = targetWidth / meshWidth;
        float scaleHeight = Mathf.Clamp(Mathf.Min(scaleWidth, scaleLength) * 0.55f, 1.2f, 4.5f);

        if (lengthOnLocalX)
            visual.transform.localScale = new Vector3(scaleLength, scaleHeight, scaleWidth);
        else
            visual.transform.localScale = new Vector3(scaleWidth, scaleHeight, scaleLength);

        float localMinY = meshBounds.min.y;
        float worldMinOffset = localMinY * scaleHeight;
        visual.transform.position = new Vector3(
            rootWorldPos.x,
            bankY - worldMinOffset + 0.05f,
            rootWorldPos.z);

        // Disable author MeshCollider — we place fitted boxes instead.
        MeshCollider[] meshColliders = visual.GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < meshColliders.Length; i++)
        {
            if (meshColliders[i] != null)
                meshColliders[i].enabled = false;
        }
    }

    /// <summary>
    /// Colliders live in SM_Bridge local space so they track mesh scale/yaw.
    /// WalkDeck = bottom plank slab inset between rails; Rails = side barriers.
    /// </summary>
    private static void CreateModelFittedColliders(GameObject visual, Mesh mesh)
    {
        if (visual == null || mesh == null)
            return;

        DestroyNamedChild(visual.transform, "WalkDeck");
        DestroyNamedChild(visual.transform, "Rail_L");
        DestroyNamedChild(visual.transform, "Rail_R");

        MeshCollider[] meshColliders = visual.GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < meshColliders.Length; i++)
        {
            if (meshColliders[i] != null)
                meshColliders[i].enabled = false;
        }

        Bounds b = mesh.bounds;
        bool lengthOnLocalX = b.size.x >= b.size.z;

        float deckH = Mathf.Max(0.05f, b.size.y * DeckHeightFraction);
        float deckCenterY = b.min.y + deckH * 0.5f;

        float crossSize = lengthOnLocalX ? b.size.z : b.size.x;
        float walkCross = crossSize * (1f - 2f * SideInsetFraction);
        float spanSize = lengthOnLocalX ? b.size.x : b.size.z;

        // --- Walkable deck ---
        GameObject deck = new GameObject("WalkDeck");
        Undo.RegisterCreatedObjectUndo(deck, "Bridge WalkDeck");
        deck.transform.SetParent(visual.transform, false);
        deck.transform.localPosition = Vector3.zero;
        deck.transform.localRotation = Quaternion.identity;
        deck.transform.localScale = Vector3.one;

        BoxCollider deckBox = deck.AddComponent<BoxCollider>();
        deckBox.isTrigger = false;
        if (lengthOnLocalX)
        {
            deckBox.center = new Vector3(b.center.x, deckCenterY, b.center.z);
            deckBox.size = new Vector3(spanSize * 0.98f, deckH, walkCross);
        }
        else
        {
            deckBox.center = new Vector3(b.center.x, deckCenterY, b.center.z);
            deckBox.size = new Vector3(walkCross, deckH, spanSize * 0.98f);
        }

        // --- Side rails (keep CharacterController on the deck) ---
        float railThickness = Mathf.Max(0.04f, crossSize * RailThicknessFraction);
        float railHeight = Mathf.Max(deckH * 2f, b.size.y * 0.72f);
        float railCenterY = b.min.y + railHeight * 0.5f;
        float railOffset = crossSize * 0.5f - railThickness * 0.5f;

        CreateRail(
            visual.transform,
            "Rail_L",
            b,
            lengthOnLocalX,
            spanSize,
            railThickness,
            railHeight,
            railCenterY,
            -railOffset);
        CreateRail(
            visual.transform,
            "Rail_R",
            b,
            lengthOnLocalX,
            spanSize,
            railThickness,
            railHeight,
            railCenterY,
            railOffset);
    }

    private static void CreateRail(
        Transform parent,
        string name,
        Bounds meshBounds,
        bool lengthOnLocalX,
        float spanSize,
        float thickness,
        float height,
        float centerY,
        float crossOffset)
    {
        GameObject rail = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(rail, "Bridge Rail");
        rail.transform.SetParent(parent, false);
        rail.transform.localPosition = Vector3.zero;
        rail.transform.localRotation = Quaternion.identity;
        rail.transform.localScale = Vector3.one;

        BoxCollider box = rail.AddComponent<BoxCollider>();
        box.isTrigger = false;
        if (lengthOnLocalX)
        {
            box.center = new Vector3(meshBounds.center.x, centerY, meshBounds.center.z + crossOffset);
            box.size = new Vector3(spanSize * 0.96f, height, thickness);
        }
        else
        {
            box.center = new Vector3(meshBounds.center.x + crossOffset, centerY, meshBounds.center.z);
            box.size = new Vector3(thickness, height, spanSize * 0.96f);
        }
    }

    private static void DestroyNamedChild(Transform parent, string childName)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == childName)
                Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static float SampleBankHeight(Terrain terrain, float nx, float nz)
    {
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        float wx = origin.x + nx * size.x;
        float approachZ = origin.z + (RiverSouth - 0.01f) * size.z;
        float yApproach = terrain.SampleHeight(new Vector3(wx, 0f, approachZ)) + origin.y;
        float wz = origin.z + nz * size.z;
        float yMid = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + origin.y;
        return Mathf.Max(yApproach, yMid) + 0.15f;
    }

    private static Transform FindOrCreateBridgesRoot()
    {
        GameObject existing = GameObject.Find("Bridges");
        if (existing != null)
            return existing.transform;

        GameObject map = GameObject.Find("ReferenceMap");
        GameObject root = new GameObject("Bridges");
        Undo.RegisterCreatedObjectUndo(root, "Bridges root");
        if (map != null)
            root.transform.SetParent(map.transform, false);
        return root.transform;
    }

    private static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
    }

    private static Terrain FindMainTerrain()
    {
        Terrain[] all = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "MainTerrain")
                return all[i];
        }

        return Terrain.activeTerrain;
    }
}
