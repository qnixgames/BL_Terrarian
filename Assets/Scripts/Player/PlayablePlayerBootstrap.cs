using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// On Play: places FPS player on the south road, attaches Player_Character model,
/// fixes colliders, and silences known IsNormalized sources (zero-scale UI, grass shadows).
/// </summary>
public static class PlayablePlayerBootstrap
{
    private const string TerrainName = "MainTerrain";
    private const string CharacterName = "Player_Character";
    private const float RoadStartNormX = 0.50f;
    private const float RoadStartNormZ = 0.06f;
    private const float TargetCharacterHeight = 1.8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnSceneLoaded()
    {
        Terrain terrain = FindMainTerrain();
        if (terrain == null || terrain.terrainData == null)
            return;

        CharacterController controller = FindBestCharacterController();
        if (controller == null)
            controller = CreateFallbackPlayer();
        if (controller == null)
            return;

        EnsureTerrainCollider(terrain);
        ConfigureCharacterController(controller);
        DisableConflictingBehaviours(controller.gameObject);
        FixZeroScaleTransforms(controller.transform.root);

        TerrainFirstPersonController fps = controller.GetComponent<TerrainFirstPersonController>();
        if (fps == null)
            fps = controller.gameObject.AddComponent<TerrainFirstPersonController>();

        Transform cam = FindPlayerCamera(controller.gameObject);
        if (cam == null)
            cam = CreateCamera(controller.gameObject);

        cam.tag = "MainCamera";

        // Capsule mesh is only a stand-in — hide it when real character is present.
        MeshRenderer capsule = controller.GetComponent<MeshRenderer>();
        if (capsule != null)
            capsule.enabled = false;
        MeshFilter capsuleFilter = controller.GetComponent<MeshFilter>();
        if (capsuleFilter != null)
            capsuleFilter.hideFlags = HideFlags.None;

        Transform character = AttachPlayerCharacter(controller);
        ConfigureFirstPersonCamera(cam, controller.transform, fps, character);

        DeduplicateCameras(controller.gameObject);
        PlaceOnRoadStart(controller, terrain);
        SilenceGrassShadowAssertions();
        SilenceNightLightShadowAssertions();
        WireDynamicGrass(cam);

        if (character != null)
            Debug.Log("Player_Character attached. First-person camera active. WASD + mouse.");
        else
            Debug.Log("First-person camera active. WASD + mouse.");
    }

    private static Terrain FindMainTerrain()
    {
        Terrain[] all = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == TerrainName)
                return all[i];
        }

        return null;
    }

    private static CharacterController FindBestCharacterController()
    {
        CharacterController[] controllers =
            Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude);
        if (controllers.Length == 0)
            return null;

        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null && controllers[i].GetComponentInChildren<Camera>(true) != null)
                return controllers[i];
        }

        return controllers[0];
    }

    private static CharacterController CreateFallbackPlayer()
    {
        GameObject playerGo = new GameObject("Player");
        CharacterController cc = playerGo.AddComponent<CharacterController>();
        ConfigureCharacterController(cc);
        CreateCamera(playerGo);
        return cc;
    }

    private static Transform CreateCamera(GameObject parent)
    {
        GameObject camGo = new GameObject("Main Camera");
        camGo.transform.SetParent(parent.transform, false);
        camGo.tag = "MainCamera";
        camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        return camGo.transform;
    }

    private static void EnsureTerrainCollider(Terrain terrain)
    {
        TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider == null)
        {
            terrainCollider = terrain.gameObject.AddComponent<TerrainCollider>();
            terrainCollider.terrainData = terrain.terrainData;
        }

        terrainCollider.enabled = true;
        if (terrainCollider.terrainData == null)
            terrainCollider.terrainData = terrain.terrainData;
    }

    private static void DisableConflictingBehaviours(GameObject host)
    {
        MonoBehaviour[] behaviours = host.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour mb = behaviours[i];
            if (mb == null || mb is TerrainFirstPersonController)
                continue;

            string typeName = mb.GetType().Name;
            if (typeName == "PlayerController"
                || typeName == "MouseLook"
                || typeName == "PlayerMusic"
                || typeName == "RGBMaterialEffect")
            {
                mb.enabled = false;
            }
        }

        // Elman UI canvas ships with 0,0,0 scale and can trip transform asserts.
        Canvas[] canvases = host.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null)
                canvases[i].gameObject.SetActive(false);
        }
    }

    private static void FixZeroScaleTransforms(Transform root)
    {
        if (root == null)
            return;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null)
                continue;

            Vector3 s = t.localScale;
            if (s.x * s.x + s.y * s.y + s.z * s.z < 1e-8f)
                t.localScale = Vector3.one;
        }
    }

    private static Transform FindPlayerCamera(GameObject host)
    {
        Camera local = host.GetComponentInChildren<Camera>(true);
        if (local != null)
            return local.transform;

        Transform root = host.transform.root;
        Camera any = root.GetComponentInChildren<Camera>(true);
        return any != null ? any.transform : null;
    }

    private static void ConfigureCharacterController(CharacterController cc)
    {
        cc.height = TargetCharacterHeight;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, TargetCharacterHeight * 0.5f, 0f);
        cc.slopeLimit = 50f;
        cc.stepOffset = 0.4f;
        cc.skinWidth = 0.08f;
        cc.minMoveDistance = 0f;
        cc.enabled = true;
    }

    private static Transform AttachPlayerCharacter(CharacterController controller)
    {
        GameObject characterGo = FindCharacterIncludingInactive();
        if (characterGo == null)
        {
            Debug.LogWarning("Player_Character not found in scene.");
            return null;
        }

        characterGo.SetActive(true);

        // Strip physics so only CharacterController drives movement.
        Collider[] cols = characterGo.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].enabled = false;
        }

        Rigidbody[] bodies = characterGo.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null)
                bodies[i].isKinematic = true;
        }

        Transform host = controller.transform;
        characterGo.transform.SetParent(host, false);
        characterGo.transform.localRotation = Quaternion.identity;
        characterGo.transform.localScale = Vector3.one;
        characterGo.transform.localPosition = Vector3.zero;

        Bounds bounds = CalculateRendererBounds(characterGo);
        float height = Mathf.Max(0.001f, bounds.size.y);

        // Centimetre-scale FBX imports appear tiny in metres.
        if (height < 0.5f)
        {
            characterGo.transform.localScale = Vector3.one * 100f;
            bounds = CalculateRendererBounds(characterGo);
            height = Mathf.Max(0.001f, bounds.size.y);
        }

        float scale = TargetCharacterHeight / height;
        characterGo.transform.localScale = characterGo.transform.localScale * scale;

        // Plant feet on the CharacterController pivot (bottom ≈ transform.position).
        bounds = CalculateRendererBounds(characterGo);
        float lift = host.position.y - bounds.min.y;
        characterGo.transform.position += new Vector3(0f, lift, 0f);

        return characterGo.transform;
    }

    private static GameObject FindCharacterIncludingInactive()
    {
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == CharacterName)
                return all[i].gameObject;
        }

        return GameObject.Find(CharacterName);
    }

    private static Bounds CalculateRendererBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                b.Encapsulate(renderers[i].bounds);
        }

        return b;
    }

    private static void ConfigureFirstPersonCamera(
        Transform cam,
        Transform followTarget,
        TerrainFirstPersonController fps,
        Transform character)
    {
        if (cam.parent != followTarget)
            cam.SetParent(followTarget, false);

        // Eye height relative to CharacterController pivot (feet).
        cam.localPosition = new Vector3(0f, 1.6f, 0.08f);
        cam.localRotation = Quaternion.identity;

        fps.Configure(
            followTarget.GetComponent<CharacterController>(),
            cam,
            thirdPerson: false,
            cameraOffset: Vector3.zero,
            lookAtOffset: Vector3.zero);

        // Hide body mesh so it doesn't clip into the FPS camera.
        if (character == null)
            return;

        Renderer[] renderers = character.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }
    }

    private static void DeduplicateCameras(GameObject playerHost)
    {
        Camera[] cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
        for (int i = 0; i < cams.Length; i++)
        {
            if (cams[i] == null)
                continue;

            bool isPlayerCam = cams[i].transform.IsChildOf(playerHost.transform)
                               || cams[i].transform.IsChildOf(playerHost.transform.root);
            if (!isPlayerCam && cams[i].CompareTag("MainCamera"))
                cams[i].tag = "Untagged";

            if (!isPlayerCam)
            {
                AudioListener otherListener = cams[i].GetComponent<AudioListener>();
                if (otherListener != null)
                    otherListener.enabled = false;
            }
        }
    }

    private static void PlaceOnRoadStart(CharacterController controller, Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        Vector3 size = data.size;

        float worldX = origin.x + RoadStartNormX * size.x;
        float worldZ = origin.z + RoadStartNormZ * size.z;
        float groundY = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + origin.y;

        Transform t = controller.transform;
        if (t.parent != null && t.parent.name == "Player")
        {
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            Transform crouch = t.parent.Find("CrouthCheck");
            if (crouch != null)
                crouch.localPosition = new Vector3(0f, 1.7f, 0f);
        }

        Transform moveRoot = t.parent != null && t.parent.name == "Player" ? t.parent : t;
        moveRoot.SetPositionAndRotation(
            new Vector3(worldX, groundY + 0.05f, worldZ),
            Quaternion.identity);

        controller.enabled = false;
        controller.enabled = true;
    }

    private static void SilenceGrassShadowAssertions()
    {
        // Tessellated GS + Dynamic RayTracing / shadows → native IsNormalized(dir) spam.
        DynamicGrassChunk[] chunks =
            Object.FindObjectsByType<DynamicGrassChunk>(FindObjectsInactive.Include);
        for (int i = 0; i < chunks.Length; i++)
        {
            if (chunks[i] != null)
                chunks[i].ApplySafeRendererSettings();
        }
    }

    private static void SilenceNightLightShadowAssertions()
    {
        // Directional moonlight must not cast day-length tree shadows at night.
        Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        for (int i = 0; i < allLights.Length; i++)
        {
            Light light = allLights[i];
            if (light == null)
                continue;
            if (light.type == LightType.Directional)
                light.shadows = LightShadows.None;
        }

        GameObject root = GameObject.Find("Night Atmosphere Lights");
        if (root == null)
            return;

        Light[] lights = root.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null)
                lights[i].shadows = LightShadows.None;
        }
    }

    private static void WireDynamicGrass(Transform cam)
    {
        if (cam == null)
            return;

        DynamicGrassTerrain[] grasses =
            Object.FindObjectsByType<DynamicGrassTerrain>(FindObjectsInactive.Exclude);
        for (int i = 0; i < grasses.Length; i++)
        {
            if (grasses[i] != null)
            {
                grasses[i].SetLodTarget(cam);
                grasses[i].ForceUpdate();
            }
        }
    }
}
