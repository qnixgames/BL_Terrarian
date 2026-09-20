using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene-open fix for IsNormalized(dir) spam caused by Dynamic Grass + RayTracing/Shadows
/// and Elman Player Canvas with zero localScale.
/// Root cause: MeshRenderer defaults to RayTracingMode=DynamicTransform (serialized 2) and
/// CastShadows=On. Unity native RTAS / shadow code asserts IsNormalized(dir) once per
/// grass chunk per Scene View frame (~469×).
/// </summary>
[InitializeOnLoad]
public static class DynamicGrassAssertFix
{
    private static double _lastLogTime;

    static DynamicGrassAssertFix()
    {
        EditorApplication.delayCall += () => Apply(log: true);
        EditorApplication.playModeStateChanged += _ => EditorApplication.delayCall += () => Apply(log: false);
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += () => Apply(log: true);
    }

    private static void OnSceneSaving(Scene scene, string path)
    {
        // Re-apply right before save so YAML never re-serializes Dynamic RT / shadows.
        Apply(log: false);
    }

    [MenuItem("Tools/Reference Map/Fix IsNormalized Assert (Grass/Player)", priority = 250)]
    private static void ApplyFromMenu()
    {
        int n = Apply(log: true);
        if (n > 0)
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(
            "IsNormalized fix applied on " + n +
            " grass chunks (RayTracing Off, Shadows Off) + Player canvas scale. Save the scene.");
    }

    private static int Apply(bool log)
    {
        int fixedChunks = 0;
        int changed = 0;

        DynamicGrassChunk[] chunks =
            Object.FindObjectsByType<DynamicGrassChunk>(FindObjectsInactive.Include);
        for (int i = 0; i < chunks.Length; i++)
        {
            if (chunks[i] == null)
                continue;

            MeshRenderer r = chunks[i].MeshRenderer;
            bool needsFix = false;
            if (r != null)
            {
                SerializedObject so = new SerializedObject(r);
                SerializedProperty rt = so.FindProperty("m_RayTracingMode");
                needsFix =
                    r.shadowCastingMode != ShadowCastingMode.Off ||
                    r.motionVectorGenerationMode != MotionVectorGenerationMode.ForceNoMotion ||
                    (rt != null && rt.intValue != 0);
            }

            chunks[i].ApplySafeRendererSettings();
            EditorUtility.SetDirty(chunks[i].gameObject);
            if (r != null)
                EditorUtility.SetDirty(r);
            fixedChunks++;
            if (needsFix)
                changed++;
        }

        DynamicGrassTerrain[] terrains =
            Object.FindObjectsByType<DynamicGrassTerrain>(FindObjectsInactive.Include);
        for (int i = 0; i < terrains.Length; i++)
        {
            if (terrains[i] != null)
                EditorUtility.SetDirty(terrains[i]);
        }

        FixPlayerZeroScaleCanvas();

        if (log && fixedChunks > 0 && EditorApplication.timeSinceStartup - _lastLogTime > 2.0)
        {
            _lastLogTime = EditorApplication.timeSinceStartup;
            Debug.Log(
                "[DynamicGrassAssertFix] Hardened " + fixedChunks +
                " grass chunks (" + changed + " needed RayTracing/Shadows Off).");
        }

        return fixedChunks;
    }

    private static void FixPlayerZeroScaleCanvas()
    {
        // Prefab asset
        string prefabPath =
            "Assets/3rdParty/ElmanGameDevTools/FirstPersonControllerPro/Player/Prefab/Player.prefab";
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabRoot != null)
        {
            bool changed = false;
            Canvas[] canvases = prefabRoot.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] == null)
                    continue;
                canvases[i].gameObject.SetActive(false);
                RectTransform rt = canvases[i].transform as RectTransform;
                if (rt != null && rt.localScale.sqrMagnitude < 1e-8f)
                {
                    rt.localScale = Vector3.one;
                    changed = true;
                }
            }

            // Tiny UI scale under camera can also poison transform basis.
            Transform[] all = prefabRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                Vector3 s = all[i].localScale;
                if (s.x * s.x + s.y * s.y + s.z * s.z < 1e-8f)
                {
                    all[i].localScale = Vector3.one;
                    changed = true;
                }
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        // Live scene instances
        Canvas[] sceneCanvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        for (int i = 0; i < sceneCanvases.Length; i++)
        {
            Canvas c = sceneCanvases[i];
            if (c == null)
                continue;
            if (c.transform.root != null && c.transform.root.name.Contains("Player"))
            {
                c.gameObject.SetActive(false);
                if (c.transform.localScale.sqrMagnitude < 1e-8f)
                    c.transform.localScale = Vector3.one;
            }
        }
    }
}
