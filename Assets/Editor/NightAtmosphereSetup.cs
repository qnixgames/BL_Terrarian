using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Day / night atmosphere for SampleScene. Toggle via Tools menu.
/// </summary>
public static class NightAtmosphereSetup
{
    private const string MenuRoot = "Tools/Reference Map/";
    private const string PrefIsNight = "BL_Terrarian.AtmosphereIsNight";
    private const string NightSkyboxPath =
        "Assets/3rdParty/Retro Shaders Pro/Demo/Materials/Retro Skybox Night.mat";
    private const string DaySkyboxPath =
        "Assets/3rdParty/Retro Shaders Pro/Demo/Materials/Retro Skybox Procedural.mat";
    private const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";
    private const string GrassMaterialPath = "Assets/Dynamic Grass FX/Terrain Grass Material.mat";
    private const string GrassLayerPath =
        "Assets/3rdParty/Retro Shaders Pro/Demo/Terrain/GrassLayer.terrainlayer";

    // Day grass.
    private static readonly Color DayGrassTop = new Color(0.32f, 0.52f, 0.20f, 1f);
    private static readonly Color DayGrassBottom = new Color(0.08f, 0.20f, 0.07f, 1f);
    // Night Dynamic Grass — mid tone between crushed-black and washed-out bright.
    private static readonly Color NightGrassTop = new Color(0.28f, 0.46f, 0.18f, 1f);
    private static readonly Color NightGrassBottom = new Color(0.07f, 0.18f, 0.06f, 1f);

    [MenuItem(MenuRoot + "Toggle Day / Night", priority = 238)]
    public static void ToggleDayNight()
    {
        if (IsNight())
            ApplyDay();
        else
            ApplyNight();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(IsNight() ? "Atmosphere → NIGHT" : "Atmosphere → DAY");
    }

    [MenuItem(MenuRoot + "Toggle Day / Night", true)]
    private static bool ToggleDayNightValidate()
    {
        Menu.SetChecked(MenuRoot + "Toggle Day / Night", IsNight());
        return true;
    }

    [MenuItem(MenuRoot + "Apply Night Atmosphere", priority = 240)]
    public static void ApplyNightFromMenu()
    {
        ApplyNight();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Night atmosphere applied — moonlight, fog, Retro night skybox.");
    }

    [MenuItem(MenuRoot + "Apply Day Atmosphere", priority = 241)]
    public static void ApplyDayFromMenu()
    {
        ApplyDay();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Day atmosphere applied — sunlight, clear fog, day skybox.");
    }

    public static bool IsNight()
    {
        return EditorPrefs.GetBool(PrefIsNight, true);
    }

    public static void Apply()
    {
        ApplyNight();
    }

    public static void ApplyNight()
    {
        EditorPrefs.SetBool(PrefIsNight, true);

        Material nightSky = AssetDatabase.LoadAssetAtPath<Material>(NightSkyboxPath);
        if (nightSky != null)
            RenderSettings.skybox = nightSky;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.02f, 0.03f, 0.07f, 1f);
        RenderSettings.fogDensity = 0.0065f;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.04f, 0.05f, 0.12f);
        RenderSettings.ambientEquatorColor = new Color(0.03f, 0.04f, 0.07f);
        RenderSettings.ambientGroundColor = new Color(0.01f, 0.015f, 0.02f);
        RenderSettings.ambientIntensity = 0.55f;
        RenderSettings.reflectionIntensity = 0.35f;
        RenderSettings.subtractiveShadowColor = new Color(0.05f, 0.06f, 0.12f);

        Light moon = FindDirectionalLight();
        if (moon != null)
        {
            Undo.RecordObject(moon, "Night moonlight");
            Undo.RecordObject(moon.transform, "Night moonlight angle");

            moon.color = new Color(0.55f, 0.68f, 1f);
            moon.intensity = 0.38f;
            moon.shadows = LightShadows.None;
            moon.shadowStrength = 0.25f;
            moon.useColorTemperature = false;
            moon.bounceIntensity = 0.35f;
            moon.transform.rotation = Quaternion.Euler(72f, -25f, 0f);
            RenderSettings.sun = moon;
            EditorUtility.SetDirty(moon);
        }

        TuneVolumeProfileNight();
        EnsureWarmFillLights();
        ApplyGrassTint(night: true);
    }

    public static void ApplyDay()
    {
        EditorPrefs.SetBool(PrefIsNight, false);

        Material daySky = AssetDatabase.LoadAssetAtPath<Material>(DaySkyboxPath);
        if (daySky != null)
            RenderSettings.skybox = daySky;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        // Soft green-grey aerial perspective — hides finite map edge from high views.
        RenderSettings.fogColor = new Color(0.62f, 0.72f, 0.68f, 1f);
        RenderSettings.fogDensity = 0.004f;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.85f);
        RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.42f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.2f, 0.16f);
        RenderSettings.ambientIntensity = 1.05f;
        RenderSettings.reflectionIntensity = 0.85f;
        RenderSettings.subtractiveShadowColor = new Color(0.42f, 0.4f, 0.35f);

        Light sun = FindDirectionalLight();
        if (sun != null)
        {
            Undo.RecordObject(sun, "Day sunlight");
            Undo.RecordObject(sun.transform, "Day sunlight angle");

            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.useColorTemperature = false;
            sun.bounceIntensity = 1f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = sun;
            EditorUtility.SetDirty(sun);
        }

        TuneVolumeProfileDay();
        RemoveWarmFillLights();
        ApplyGrassTint(night: false);
    }

    private static void ApplyGrassTint(bool night)
    {
        Material grassMat = AssetDatabase.LoadAssetAtPath<Material>(GrassMaterialPath);
        if (grassMat != null)
        {
            Undo.RecordObject(grassMat, night ? "Night grass tint" : "Day grass tint");
            grassMat.SetColor("_TopColor", night ? NightGrassTop : DayGrassTop);
            grassMat.SetColor("_BottomColor", night ? NightGrassBottom : DayGrassBottom);
            if (grassMat.HasProperty("_TranslucentGain"))
                grassMat.SetFloat("_TranslucentGain", night ? 0.28f : 0.22f);
            if (grassMat.HasProperty("_Brightness"))
                grassMat.SetFloat("_Brightness", night ? 1.35f : 1.15f);
            EditorUtility.SetDirty(grassMat);
        }

        TerrainLayer grassLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(GrassLayerPath);
        if (grassLayer != null)
        {
            Undo.RecordObject(grassLayer, night ? "Night terrain grass" : "Day terrain grass");
            // DiffuseRemapMax scales terrain splat albedo (1,1,1 = full bright texture).
            if (night)
                grassLayer.diffuseRemapMax = new Vector4(0.88f, 0.95f, 0.75f, 1f);
            else
                grassLayer.diffuseRemapMax = new Vector4(1f, 1f, 1f, 1f);
            EditorUtility.SetDirty(grassLayer);
        }

        // Refresh terrain materials in open scenes.
        Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        for (int i = 0; i < terrains.Length; i++)
        {
            if (terrains[i] != null)
                terrains[i].Flush();
        }

        AssetDatabase.SaveAssets();
    }

    private static Light FindDirectionalLight()
    {
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }

    private static void TuneVolumeProfileNight()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
            return;

        Undo.RecordObject(profile, "Night volume grade");

        if (!profile.TryGet(out ColorAdjustments color))
            color = profile.Add<ColorAdjustments>(true);

        color.active = true;
        color.postExposure.overrideState = true;
        color.postExposure.value = -0.55f;
        color.contrast.overrideState = true;
        color.contrast.value = 18f;
        color.colorFilter.overrideState = true;
        color.colorFilter.value = new Color(0.72f, 0.8f, 1f);
        color.saturation.overrideState = true;
        color.saturation.value = -12f;

        if (!profile.TryGet(out WhiteBalance balance))
            balance = profile.Add<WhiteBalance>(true);
        balance.active = true;
        balance.temperature.overrideState = true;
        balance.temperature.value = -28f;
        balance.tint.overrideState = true;
        balance.tint.value = 8f;

        if (!profile.TryGet(out Vignette vignette))
            vignette = profile.Add<Vignette>(true);
        vignette.active = true;
        vignette.intensity.overrideState = true;
        vignette.intensity.value = 0.42f;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = 0.45f;
        vignette.color.overrideState = true;
        vignette.color.value = new Color(0.01f, 0.02f, 0.05f);

        if (!profile.TryGet(out Bloom bloom))
            bloom = profile.Add<Bloom>(true);
        bloom.active = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 0.85f;
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.45f;
        bloom.tint.overrideState = true;
        bloom.tint.value = new Color(0.75f, 0.85f, 1f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    private static void TuneVolumeProfileDay()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
            return;

        Undo.RecordObject(profile, "Day volume grade");

        if (!profile.TryGet(out ColorAdjustments color))
            color = profile.Add<ColorAdjustments>(true);

        color.active = true;
        color.postExposure.overrideState = true;
        color.postExposure.value = 0.05f;
        color.contrast.overrideState = true;
        color.contrast.value = 8f;
        color.colorFilter.overrideState = true;
        color.colorFilter.value = Color.white;
        color.saturation.overrideState = true;
        color.saturation.value = 5f;

        if (!profile.TryGet(out WhiteBalance balance))
            balance = profile.Add<WhiteBalance>(true);
        balance.active = true;
        balance.temperature.overrideState = true;
        balance.temperature.value = 8f;
        balance.tint.overrideState = true;
        balance.tint.value = 0f;

        if (!profile.TryGet(out Vignette vignette))
            vignette = profile.Add<Vignette>(true);
        vignette.active = true;
        vignette.intensity.overrideState = true;
        vignette.intensity.value = 0.18f;
        vignette.smoothness.overrideState = true;
        vignette.smoothness.value = 0.4f;
        vignette.color.overrideState = true;
        vignette.color.value = Color.black;

        if (!profile.TryGet(out Bloom bloom))
            bloom = profile.Add<Bloom>(true);
        bloom.active = true;
        bloom.threshold.overrideState = true;
        bloom.threshold.value = 1.05f;
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.2f;
        bloom.tint.overrideState = true;
        bloom.tint.value = Color.white;

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    private static void EnsureWarmFillLights()
    {
        Terrain terrain = FindMainTerrain();
        if (terrain == null || terrain.terrainData == null)
            return;

        RemoveWarmFillLights();

        GameObject root = new GameObject("Night Atmosphere Lights");
        Undo.RegisterCreatedObjectUndo(root, "Night fill lights");

        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;

        Vector2[] spots =
        {
            new Vector2(0.50f, 0.06f),
            new Vector2(0.28f, 0.78f),
            new Vector2(0.72f, 0.78f),
            new Vector2(0.50f, 0.62f),
            new Vector2(0.22f, 0.28f),
            new Vector2(0.72f, 0.28f),
            new Vector2(0.50f, 0.50f)
        };

        for (int i = 0; i < spots.Length; i++)
        {
            float wx = origin.x + spots[i].x * size.x;
            float wz = origin.z + spots[i].y * size.z;
            float wy = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + origin.y + 3.5f;

            GameObject go = new GameObject("NightFill_" + i);
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(wx, wy, wz);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.42f);
            light.intensity = i == 0 ? 2.2f : 1.4f;
            light.range = i == 0 ? 28f : 22f;
            light.shadows = LightShadows.None;
            light.bounceIntensity = 0.2f;
        }
    }

    private static void RemoveWarmFillLights()
    {
        GameObject root = GameObject.Find("Night Atmosphere Lights");
        if (root != null)
            Undo.DestroyObjectImmediate(root);
    }

    private static Terrain FindMainTerrain()
    {
        Terrain[] all = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "MainTerrain")
                return all[i];
        }

        return null;
    }
}
