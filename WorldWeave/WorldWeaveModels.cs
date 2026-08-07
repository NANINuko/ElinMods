using System;
using System.Collections.Generic;

[Serializable]
public sealed class WorldWeaveRect
{
    public int x;
    public int z;
    public int w;
    public int h;

    public bool IsValid
    {
        get { return w > 0 && h > 0; }
    }
}

[Serializable]
public sealed class WorldWeaveVariantConfig
{
    public string id;

    public bool? enabled;
    public float? spawnChance;
    public int? weight;
    public int? maxPerMap;
    public bool? replaceExisting;

    public WorldWeaveRect activeRect = new WorldWeaveRect();
}

[Serializable]
public sealed class WorldWeaveConfig
{
    public bool enabled = true;
    public float spawnChance = 0.10f;
    public int weight = 1;
    public int maxPerMap = 1;
    public bool replaceExisting = true;

    public List<string> zoneTypes = new List<string>();
    public List<string> biomes = new List<string>();

    public WorldWeaveRect activeRect = new WorldWeaveRect();
    public List<WorldWeaveVariantConfig> variants = new List<WorldWeaveVariantConfig>();
}

internal sealed class WorldWeaveTemplateEntry
{
    public string bundleId;
    public string variantId;
    public string zPath;
    public string jsonPath;

    public WorldWeaveConfig bundleConfig;
    public WorldWeaveVariantConfig variantConfig;

    public bool loaded;
    public Map map;
    public ZoneExportData exportData;

    public WorldWeaveRect sourceRect;
    public int anchorX;
    public int anchorZ;

    public string Key
    {
        get { return bundleId + ":" + variantId; }
    }

    public bool IsEnabled
    {
        get
        {
            if (bundleConfig == null || !bundleConfig.enabled)
            {
                return false;
            }

            if (variantConfig != null && variantConfig.enabled.HasValue && !variantConfig.enabled.Value)
            {
                return false;
            }

            return true;
        }
    }

    public float EffectiveSpawnChance
    {
        get
        {
            if (variantConfig != null && variantConfig.spawnChance.HasValue)
            {
                return variantConfig.spawnChance.Value;
            }

            return bundleConfig != null ? bundleConfig.spawnChance : 0.10f;
        }
    }

    public int EffectiveWeight
    {
        get
        {
            int value = 1;
            if (variantConfig != null && variantConfig.weight.HasValue)
            {
                value = variantConfig.weight.Value;
            }
            else if (bundleConfig != null)
            {
                value = bundleConfig.weight;
            }

            return Math.Max(1, value);
        }
    }

    public int EffectiveMaxPerMap
    {
        get
        {
            if (variantConfig != null && variantConfig.maxPerMap.HasValue)
            {
                return variantConfig.maxPerMap.Value;
            }

            if (bundleConfig != null)
            {
                return bundleConfig.maxPerMap;
            }

            return 1;
        }
    }

    public bool EffectiveReplaceExisting
    {
        get
        {
            if (variantConfig != null && variantConfig.replaceExisting.HasValue)
            {
                return variantConfig.replaceExisting.Value;
            }

            return bundleConfig != null ? bundleConfig.replaceExisting : true;
        }
    }
}