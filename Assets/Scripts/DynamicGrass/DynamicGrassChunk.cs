using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One tessellated grass patch. LOD manager toggles renderer and MPB stages by distance.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
[ExecuteAlways]
public class DynamicGrassChunk : MonoBehaviour
{
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private Vector3 worldCenter;
    [SerializeField] private float boundingRadius = 24f;

    public MeshRenderer MeshRenderer => meshRenderer != null ? meshRenderer : (meshRenderer = GetComponent<MeshRenderer>());
    public Vector3 WorldCenter => worldCenter;
    public float BoundingRadius => boundingRadius;

    private void OnEnable()
    {
        ApplySafeRendererSettings();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplySafeRendererSettings();
    }
#endif

    public void Configure(Vector3 center, float radius)
    {
        worldCenter = center;
        boundingRadius = radius;
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();
        ApplySafeRendererSettings();
    }

    /// <summary>
    /// Tessellation/geometry grass + Dynamic RayTracing / shadow casting triggers
    /// native Unity assert: IsNormalized(dir, 0.0001f) once per chunk per frame.
    /// RayTracingMode enum is unavailable in this URP player API — set via serialized int in editor.
    /// </summary>
    public void ApplySafeRendererSettings()
    {
        MeshRenderer r = MeshRenderer;
        if (r == null)
            return;

        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = true;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        r.allowOcclusionWhenDynamic = false;

#if UNITY_EDITOR
        // m_RayTracingMode: 0 = Off (avoids CS0103 — RayTracingMode type not in URP scripting API)
        ForceRayTracingOff(r);
#endif
    }

#if UNITY_EDITOR
    private static void ForceRayTracingOff(MeshRenderer renderer)
    {
        UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(renderer);
        UnityEditor.SerializedProperty prop = so.FindProperty("m_RayTracingMode");
        if (prop != null && prop.intValue != 0)
        {
            prop.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
#endif
}
