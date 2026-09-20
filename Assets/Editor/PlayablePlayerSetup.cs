using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor helper: place Player on the south road start and ensure colliders / FPS control.
/// Play mode also auto-runs PlayablePlayerBootstrap.
/// </summary>
public static class PlayablePlayerSetup
{
    private const string MenuRoot = "Tools/Reference Map/";

    [MenuItem(MenuRoot + "Setup Playable Player (road start)", priority = 230)]
    public static void SetupFromMenu()
    {
        Terrain terrain = null;
        Terrain[] all = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "MainTerrain")
            {
                terrain = all[i];
                break;
            }
        }

        if (terrain == null)
        {
            EditorUtility.DisplayDialog(
                "Setup Playable Player",
                "MainTerrain not found in the open scene.",
                "OK");
            return;
        }

        TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider == null)
        {
            terrainCollider = Undo.AddComponent<TerrainCollider>(terrain.gameObject);
            terrainCollider.terrainData = terrain.terrainData;
        }

        Undo.RecordObject(terrainCollider, "Enable TerrainCollider");
        terrainCollider.enabled = true;
        terrainCollider.terrainData = terrain.terrainData;

        CharacterController[] controllers =
            Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude);
        CharacterController cc = null;
        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null && controllers[i].GetComponentInChildren<Camera>(true) != null)
            {
                cc = controllers[i];
                break;
            }
        }

        if (cc == null && controllers.Length > 0)
            cc = controllers[0];

        if (cc == null)
        {
            EditorUtility.DisplayDialog(
                "Setup Playable Player",
                "No CharacterController found. Keep the Elman 'Player' prefab in the scene.",
                "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(cc.transform.root.gameObject, "Setup Playable Player");

        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.slopeLimit = 50f;
        cc.stepOffset = 0.4f;
        cc.skinWidth = 0.08f;
        cc.minMoveDistance = 0f;

        MonoBehaviour[] behaviours = cc.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] == null)
                continue;
            string n = behaviours[i].GetType().Name;
            if (n == "PlayerController" || n == "MouseLook")
                behaviours[i].enabled = false;
        }

        TerrainFirstPersonController fps = cc.GetComponent<TerrainFirstPersonController>();
        if (fps == null)
            fps = Undo.AddComponent<TerrainFirstPersonController>(cc.gameObject);

        Camera cam = cc.GetComponentInChildren<Camera>(true);
        if (cam == null)
            cam = cc.transform.root.GetComponentInChildren<Camera>(true);

        if (cam != null)
        {
            cam.tag = "MainCamera";
            cam.transform.SetParent(cc.transform, true);
            cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            fps.Configure(cc, cam.transform);
        }

        if (cc.transform.parent != null && cc.transform.parent.name == "Player")
            cc.transform.localPosition = Vector3.zero;

        Transform moveRoot = cc.transform.parent != null && cc.transform.parent.name == "Player"
            ? cc.transform.parent
            : cc.transform;

        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        Vector3 size = data.size;
        float worldX = origin.x + 0.50f * size.x;
        float worldZ = origin.z + 0.06f * size.z;
        float groundY = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + origin.y;
        moveRoot.SetPositionAndRotation(
            new Vector3(worldX, groundY + 0.05f, worldZ),
            Quaternion.identity);

        DynamicGrassTerrain grass = Object.FindFirstObjectByType<DynamicGrassTerrain>();
        if (grass != null && cam != null)
        {
            Undo.RecordObject(grass, "Wire Dynamic Grass LOD");
            grass.SetLodTarget(cam.transform);
        }

        Selection.activeGameObject = moveRoot.gameObject;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(
            "Playable Player ready on south road start. Press Play → focus Game view → WASD + mouse.");
    }
}
