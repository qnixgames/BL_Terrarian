using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor toggle for Terrain tree LOD.
/// LOD ON  = billboards / performance (good for Play and builds)
/// LOD OFF = full mesh detail (good for top-down Scene inspection)
/// </summary>
public static class TerrainTreeLodToggle
{
    private const string PrefKey = "BL_Terrarian.TreeLodEnabled";
    private const string MenuPath = "Tools/Reference Map/Tree LOD Enabled";

    private const float LodTreeDistance = 2000f;
    private const float LodBillboardStart = 90f;
    private const float LodFadeLength = 25f;
    private const int LodMaxMeshTrees = 350;
    private const float LodDetailDistance = 120f;

    private const float DetailTreeDistance = 10000f;
    private const float DetailBillboardStart = 10000f;
    private const float DetailFadeLength = 80f;
    private const int DetailMaxMeshTrees = 15000;
    private const float DetailDetailDistance = 400f;

    public static bool LodEnabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        private set => EditorPrefs.SetBool(PrefKey, value);
    }

    public static void SetLodEnabled(bool enabled)
    {
        LodEnabled = enabled;
        ApplyToOpenTerrains();
    }

    [MenuItem(MenuPath, priority = 500)]
    private static void ToggleLod()
    {
        LodEnabled = !LodEnabled;
        ApplyToOpenTerrains();
        Debug.Log(
            LodEnabled
                ? "Tree LOD ON — uzak ağaçlar billboard (Play/build)."
                : "Tree LOD OFF — Scene overview için full mesh detay.");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleLodValidate()
    {
        Menu.SetChecked(MenuPath, LodEnabled);
        return true;
    }

    [InitializeOnLoadMethod]
    private static void ApplyOnLoad()
    {
        EditorApplication.delayCall += ApplyToOpenTerrains;
    }

    public static void ApplyToOpenTerrains()
    {
        Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        for (int i = 0; i < terrains.Length; i++)
        {
            if (terrains[i] != null)
                Apply(terrains[i]);
        }
    }

    public static void Apply(Terrain terrain)
    {
        if (terrain == null)
            return;

        Undo.RecordObject(terrain, LodEnabled ? "Enable tree LOD" : "Disable tree LOD");

        if (LodEnabled)
        {
            terrain.treeDistance = LodTreeDistance;
            terrain.treeBillboardDistance = LodBillboardStart;
            terrain.treeCrossFadeLength = LodFadeLength;
            terrain.treeMaximumFullLODCount = LodMaxMeshTrees;
            terrain.detailObjectDistance = LodDetailDistance;
        }
        else
        {
            int planted = terrain.terrainData != null ? terrain.terrainData.treeInstanceCount : DetailMaxMeshTrees;
            terrain.treeDistance = DetailTreeDistance;
            terrain.treeBillboardDistance = DetailBillboardStart;
            terrain.treeCrossFadeLength = DetailFadeLength;
            terrain.treeMaximumFullLODCount = Mathf.Max(DetailMaxMeshTrees, planted + 500);
            terrain.detailObjectDistance = DetailDetailDistance;
        }

        terrain.detailObjectDensity = 1f;
        EditorUtility.SetDirty(terrain);
        if (terrain.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
    }
}
