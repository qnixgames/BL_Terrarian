using UnityEngine;

/// <summary>
/// Distance LOD for Dynamic Grass FX chunks:
/// near = full tessellation, mid = reduced stages, far = disabled.
/// Shader tessellation (_ViewLOD) still softens density with camera distance.
/// </summary>
[DisallowMultipleComponent]
public class DynamicGrassTerrain : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform lodTarget;
    [SerializeField] private DynamicGrassChunk[] chunks;

    [Header("Distance LOD (meters)")]
    [SerializeField] private float nearDistance = 35f;
    [SerializeField] private float midDistance = 70f;
    [SerializeField] private float maxDistance = 110f;

    [Header("Tessellation overrides")]
    [SerializeField] private float nearMaxStages = 7f;
    [SerializeField] private float midMaxStages = 3f;
    [SerializeField] private float nearViewLod = 48f;
    [SerializeField] private float midViewLod = 28f;

    [SerializeField] private float updateInterval = 0.2f;

    private static readonly int MaxStagesId = Shader.PropertyToID("_MaxStages");
    private static readonly int ViewLodId = Shader.PropertyToID("_ViewLOD");

    private MaterialPropertyBlock _mpb;
    private float _nextUpdateTime;
    private Camera _camera;

    public DynamicGrassChunk[] Chunks => chunks;

    public void SetChunks(DynamicGrassChunk[] builtChunks)
    {
        chunks = builtChunks;
    }

    public void SetLodTarget(Transform target)
    {
        lodTarget = target;
    }

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        ResolveLodTarget();
        HardenAllChunks();
    }

    private void OnEnable()
    {
        HardenAllChunks();
        ForceUpdate();
    }

    private void HardenAllChunks()
    {
        if (chunks == null)
            return;

        for (int i = 0; i < chunks.Length; i++)
        {
            if (chunks[i] != null)
                chunks[i].ApplySafeRendererSettings();
        }
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _nextUpdateTime)
            return;

        _nextUpdateTime = Time.unscaledTime + updateInterval;
        UpdateLod();
    }

    public void ForceUpdate()
    {
        _nextUpdateTime = 0f;
        UpdateLod();
    }

    private void ResolveLodTarget()
    {
        if (lodTarget != null)
            return;

        if (Camera.main != null)
        {
            lodTarget = Camera.main.transform;
            _camera = Camera.main;
            return;
        }

        _camera = FindFirstObjectByType<Camera>();
        if (_camera != null)
            lodTarget = _camera.transform;
    }

    private void UpdateLod()
    {
        if (chunks == null || chunks.Length == 0)
            return;

        if (lodTarget == null)
            ResolveLodTarget();

        if (lodTarget == null)
            return;

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        Vector3 eye = lodTarget.position;

        for (int i = 0; i < chunks.Length; i++)
        {
            DynamicGrassChunk chunk = chunks[i];
            if (chunk == null)
                continue;

            MeshRenderer renderer = chunk.MeshRenderer;
            if (renderer == null)
                continue;

            float distance = Vector3.Distance(eye, chunk.WorldCenter) - chunk.BoundingRadius;

            if (distance > maxDistance)
            {
                if (renderer.enabled)
                    renderer.enabled = false;
                continue;
            }

            if (!renderer.enabled)
                renderer.enabled = true;

            bool near = distance <= nearDistance;
            float stages = near ? nearMaxStages : midMaxStages;
            float viewLod = near ? nearViewLod : midViewLod;

            // Soft falloff between mid and max.
            if (distance > midDistance)
            {
                float t = Mathf.InverseLerp(midDistance, maxDistance, distance);
                stages = Mathf.Lerp(midMaxStages, 2f, t);
                viewLod = Mathf.Lerp(midViewLod, 16f, t);
            }

            renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(MaxStagesId, stages);
            _mpb.SetFloat(ViewLodId, viewLod);
            renderer.SetPropertyBlock(_mpb);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        nearDistance = Mathf.Max(5f, nearDistance);
        midDistance = Mathf.Max(nearDistance + 1f, midDistance);
        maxDistance = Mathf.Max(midDistance + 1f, maxDistance);
        updateInterval = Mathf.Max(0.05f, updateInterval);
    }
#endif
}
