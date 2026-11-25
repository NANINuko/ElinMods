using System;
using System.Collections.Generic;
using UnityEngine;

internal static partial class WorldWeaveLogic
{
    private static readonly object _reloadLock = new object();
    private static readonly List<WorldWeaveTemplateEntry> _templates = new List<WorldWeaveTemplateEntry>();
    private static bool _initialized;

    public static bool Reload()
    {
        lock (_reloadLock)
        {
            try
            {
                TemplateSpawnPlugin.LogDebug("Reload start");

                List<WorldWeaveTemplateEntry> next = new List<WorldWeaveTemplateEntry>();

                LoadTemplatesFromDisk(next);
                LoadTemplatesFromLoadedMods(next);

                _templates.Clear();
                _templates.AddRange(next);

                _initialized = true;
                MaintainCache();

                if (_templates.Count == 0)
                {
                    TemplateSpawnPlugin.LogWarning("Reload complete but no templates were loaded");
                }
                else
                {
                    TemplateSpawnPlugin.LogInfo("Reload complete: templates=" + _templates.Count);
                }

                return true;
            }
            catch (Exception ex)
            {
                TemplateSpawnPlugin.LogWarning("Reload failed: " + ex);
                return false;
            }
        }
    }

    public static void TrySpawnForZone(Zone zone)
    {
        try
        {
            if (zone == null || zone.map == null || EClass.game == null || EClass._map == null)
            {
                return;
            }

            if (!IsTargetZone(zone))
            {
                return;
            }

            if (!WorldWeaveState.ConsumePending(zone))
            {
                return;
            }

            EnsureInitialized();

            using (WorldWeaveState.BeginTemplatePlacement(zone))
            using (WorldWeaveState.SuppressGeneratedMarking())
            {
                List<WorldWeaveTemplateEntry> entriesSnapshot;

                lock (_reloadLock)
                {
                    if (_templates.Count == 0)
                    {
                        TemplateSpawnPlugin.LogDebug("No loaded templates available: " + DescribeZone(zone));
                        return;
                    }

                    entriesSnapshot = new List<WorldWeaveTemplateEntry>(_templates);
                }

                if (entriesSnapshot.Count == 0)
                {
                    TemplateSpawnPlugin.LogDebug("No loaded templates available: " + DescribeZone(zone));
                    return;
                }

                int globalMax = 1;

                if (TemplateSpawnPlugin.Instance != null
                    && TemplateSpawnPlugin.Instance.MaxTemplatesPerMap != null)
                {
                    globalMax = Math.Max(
                        1,
                        TemplateSpawnPlugin.Instance.MaxTemplatesPerMap.Value);
                }

                float spawnChanceMultiplier = 1f;

                if (TemplateSpawnPlugin.Instance != null
                    && TemplateSpawnPlugin.Instance.SpawnChanceMultiplier != null)
                {
                    spawnChanceMultiplier = Math.Max(
                        0f,
                        TemplateSpawnPlugin.Instance.SpawnChanceMultiplier.Value);
                }

                Dictionary<string, int> perMapCount =
                    new Dictionary<string, int>();

                HashSet<int> occupiedTemplateCells =
                    new HashSet<int>();

                int placedCount = 0;
                bool hadAnyCandidates = false;
                int maxCandidateCount = 0;

                for (int i = 0; i < globalMax; i++)
                {
                    List<WorldWeaveTemplateEntry> candidates =
                        new List<WorldWeaveTemplateEntry>();

                    for (int j = 0; j < entriesSnapshot.Count; j++)
                    {
                        WorldWeaveTemplateEntry entry = entriesSnapshot[j];

                        if (entry == null
                            || !entry.loaded
                            || entry.bundleConfig == null
                            || !entry.IsEnabled)
                        {
                            continue;
                        }

                        if (!IsAllowedZone(entry, zone))
                        {
                            continue;
                        }

                        if (!IsAllowedBiome(entry, zone))
                        {
                            continue;
                        }

                        int count = GetCount(perMapCount, entry.Key);

                        if (entry.EffectiveMaxPerMap > 0
                            && count >= entry.EffectiveMaxPerMap)
                        {
                            continue;
                        }

                        float chance = Mathf.Clamp01(
                            entry.EffectiveSpawnChance
                            * spawnChanceMultiplier);

                        if (Rand.Range(0f, 1f) > chance)
                        {
                            continue;
                        }

                        candidates.Add(entry);
                    }

                    if (candidates.Count == 0)
                    {
                        continue;
                    }

                    hadAnyCandidates = true;

                    if (candidates.Count > maxCandidateCount)
                    {
                        maxCandidateCount = candidates.Count;
                    }

                    bool placed = false;

                    while (candidates.Count > 0)
                    {
                        WorldWeaveTemplateEntry picked =
                            PickWeighted(candidates);

                        if (picked == null)
                        {
                            break;
                        }

                        if (TryPlace(
                            zone,
                            picked,
                            occupiedTemplateCells))
                        {
                            perMapCount[picked.Key] =
                                GetCount(perMapCount, picked.Key) + 1;

                            placedCount++;
                            placed = true;
                            break;
                        }

                        candidates.Remove(picked);
                    }

                    if (!placed)
                    {
                        continue;
                    }
                }

                if (placedCount == 0)
                {
                    if (hadAnyCandidates)
                    {
                        TemplateSpawnPlugin.LogWarning(
                            "Placement failed: zone=" + DescribeZone(zone)
                            + " candidates=" + maxCandidateCount
                            + " templates=" + entriesSnapshot.Count
                            + " maxPerMap=" + globalMax);
                    }
                    else
                    {
                        TemplateSpawnPlugin.LogDebug(
                            "No eligible template candidates: "
                            + DescribeZone(zone));
                    }
                }
                else
                {
                    TemplateSpawnPlugin.LogDebug(
                        "Spawn complete: zone=" + DescribeZone(zone)
                        + " placed=" + placedCount);
                }
            }
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning(
                "TrySpawnForZone failed: "
                + DescribeZone(zone)
                + " / "
                + ex);
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Reload();
    }

    private static bool IsPersistentCacheEnabled()
    {
        TemplateSpawnPlugin plugin = TemplateSpawnPlugin.Instance;

        if (plugin == null || plugin.UsePersistentCache == null)
        {
            return true;
        }

        return plugin.UsePersistentCache.Value;
    }

    private static bool ShouldPurgeOldCachesOnReload()
    {
        TemplateSpawnPlugin plugin = TemplateSpawnPlugin.Instance;

        if (plugin == null || plugin.PurgeOldCachesOnReload == null)
        {
            return false;
        }

        return plugin.PurgeOldCachesOnReload.Value;
    }

    private static bool ShouldLogCacheStatisticsOnReload()
    {
        TemplateSpawnPlugin plugin = TemplateSpawnPlugin.Instance;

        if (plugin == null || plugin.LogCacheStatisticsOnReload == null)
        {
            return false;
        }

        return plugin.LogCacheStatisticsOnReload.Value;
    }

    private static int GetCacheRetentionDays()
    {
        TemplateSpawnPlugin plugin = TemplateSpawnPlugin.Instance;

        if (plugin == null || plugin.CacheRetentionDays == null)
        {
            return 30;
        }

        return Math.Max(0, plugin.CacheRetentionDays.Value);
    }

    private static void MaintainCache()
    {
        if (!IsPersistentCacheEnabled())
        {
            return;
        }

        try
        {
            if (ShouldPurgeOldCachesOnReload())
            {
                int days = GetCacheRetentionDays();

                if (days > 0)
                {
                    int removed =
                        WorldWeaveCacheManager.PurgeOldCaches(days);

                    if (removed > 0)
                    {
                        TemplateSpawnPlugin.LogInfo(
                            "Purged old caches: "
                            + removed
                            + " (older than "
                            + days
                            + " days)");
                    }
                }
            }

            if (ShouldLogCacheStatisticsOnReload())
            {
                WorldWeaveCacheManager.LogStatistics();
            }
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning(
                "Cache maintenance failed: " + ex);
        }
    }
}