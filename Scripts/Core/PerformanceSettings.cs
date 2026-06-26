using Godot;
using System;

public static class PerformanceSettings
{
    private static bool _initialized;

    // Quality presets
    public enum QualityPreset { Low, Medium, High, Ultra }

    public static QualityPreset CurrentPreset { get; private set; } = QualityPreset.High;

    // Cache limits
    public static int MaxMeshCacheSize { get; private set; } = 500;
    public static int MaxTextureCacheSize { get; private set; } = 1000;
    public static int MaxNifCacheSize { get; private set; } = 300;

    // Rendering
    public static float ShadowCascadeSplit1 { get; private set; } = 0.05f;
    public static float ShadowCascadeSplit2 { get; private set; } = 0.15f;
    public static float ShadowCascadeSplit3 { get; private set; } = 0.4f;
    public static float ShadowMaxDistance { get; private set; } = 300f;
    public static int ShadowAtlasSize { get; private set; } = 4096;

    // LOD
    public static float TerrainLodDistance { get; private set; } = 60f;
    public static float PropLodDistance { get; private set; } = 80f;
    public static bool EnableFrustumCulling { get; private set; } = true;
    public static bool EnablePropLod { get; private set; } = true;
    public static bool EnableTerrainLod { get; private set; } = true;

    // Queue
    public static int MaxInstancesPerFrame { get; private set; } = 100;

    public static void Initialize()
    {
        if (_initialized) return;

        string presetStr = GetConfigValue("Performance", "QualityPreset", "High");
        if (Enum.TryParse(presetStr, true, out QualityPreset preset))
            ApplyPreset(preset);

        ApplyOverrides();

        _initialized = true;
    }

    private static string GetConfigValue(string section, string key, string defaultValue)
    {
        try
        {
            string configPath = ProjectSettings.GlobalizePath("res://config.json");
            if (System.IO.File.Exists(configPath))
            {
                string json = System.IO.File.ReadAllText(configPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty(section, out var sec) && sec.TryGetProperty(key, out var val))
                {
                    return val.ToString();
                }
            }
        }
        catch { }
        return defaultValue;
    }

    public static void ApplyPreset(QualityPreset preset)
    {
        CurrentPreset = preset;

        switch (preset)
        {
            case QualityPreset.Low:
                MaxMeshCacheSize = 100;
                MaxTextureCacheSize = 200;
                MaxNifCacheSize = 80;
                ShadowCascadeSplit1 = 0.1f;
                ShadowCascadeSplit2 = 0.3f;
                ShadowCascadeSplit3 = 0.6f;
                ShadowMaxDistance = 100f;
                ShadowAtlasSize = 1024;
                TerrainLodDistance = 40f;
                PropLodDistance = 40f;
                MaxInstancesPerFrame = 50;
                EnableFrustumCulling = false;
                EnablePropLod = true;
                EnableTerrainLod = true;
                break;

            case QualityPreset.Medium:
                MaxMeshCacheSize = 300;
                MaxTextureCacheSize = 500;
                MaxNifCacheSize = 150;
                ShadowCascadeSplit1 = 0.07f;
                ShadowCascadeSplit2 = 0.2f;
                ShadowCascadeSplit3 = 0.5f;
                ShadowMaxDistance = 200f;
                ShadowAtlasSize = 2048;
                TerrainLodDistance = 50f;
                PropLodDistance = 60f;
                MaxInstancesPerFrame = 75;
                EnableFrustumCulling = true;
                EnablePropLod = true;
                EnableTerrainLod = true;
                break;

            case QualityPreset.High:
                MaxMeshCacheSize = 500;
                MaxTextureCacheSize = 1000;
                MaxNifCacheSize = 300;
                ShadowCascadeSplit1 = 0.05f;
                ShadowCascadeSplit2 = 0.15f;
                ShadowCascadeSplit3 = 0.4f;
                ShadowMaxDistance = 300f;
                ShadowAtlasSize = 4096;
                TerrainLodDistance = 60f;
                PropLodDistance = 80f;
                MaxInstancesPerFrame = 100;
                EnableFrustumCulling = true;
                EnablePropLod = true;
                EnableTerrainLod = true;
                break;

            case QualityPreset.Ultra:
                MaxMeshCacheSize = 1000;
                MaxTextureCacheSize = 2000;
                MaxNifCacheSize = 500;
                ShadowCascadeSplit1 = 0.03f;
                ShadowCascadeSplit2 = 0.1f;
                ShadowCascadeSplit3 = 0.3f;
                ShadowMaxDistance = 500f;
                ShadowAtlasSize = 8192;
                TerrainLodDistance = 80f;
                PropLodDistance = 120f;
                MaxInstancesPerFrame = 200;
                EnableFrustumCulling = true;
                EnablePropLod = true;
                EnableTerrainLod = true;
                break;
        }
    }

    public static void ApplyOverrides()
    {
        try
        {
            string configPath = ProjectSettings.GlobalizePath("res://config.json");
            if (System.IO.File.Exists(configPath))
            {
                string json = System.IO.File.ReadAllText(configPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("Rendering", out var rend))
                {
                    if (rend.TryGetProperty("ShadowAtlasSize", out var v))
                        ShadowAtlasSize = v.GetInt32();
                    if (rend.TryGetProperty("MaxMeshCache", out var v2))
                        MaxMeshCacheSize = v2.GetInt32();
                    if (rend.TryGetProperty("MaxTextureCache", out var v3))
                        MaxTextureCacheSize = v3.GetInt32();
                    if (rend.TryGetProperty("MaxInstancesPerFrame", out var v4))
                        MaxInstancesPerFrame = v4.GetInt32();
                }
            }
        }
        catch { }
    }
}
