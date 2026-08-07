using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ionic.Zip;
using Newtonsoft.Json;
using UnityEngine;

internal static class WorldWeaveLogic
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
                if (TemplateSpawnPlugin.Instance != null && TemplateSpawnPlugin.Instance.MaxTemplatesPerMap != null)
                {
                    globalMax = Math.Max(1, TemplateSpawnPlugin.Instance.MaxTemplatesPerMap.Value);
                }

                Dictionary<string, int> perMapCount = new Dictionary<string, int>();
                HashSet<int> occupiedTemplateCells = new HashSet<int>();

                int placedCount = 0;
                bool hadAnyCandidates = false;
                int maxCandidateCount = 0;

                for (int i = 0; i < globalMax; i++)
                {
                    List<WorldWeaveTemplateEntry> candidates = new List<WorldWeaveTemplateEntry>();

                    for (int j = 0; j < entriesSnapshot.Count; j++)
                    {
                        WorldWeaveTemplateEntry entry = entriesSnapshot[j];
                        if (entry == null || !entry.loaded || entry.bundleConfig == null || !entry.IsEnabled)
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
                        if (entry.EffectiveMaxPerMap > 0 && count >= entry.EffectiveMaxPerMap)
                        {
                            continue;
                        }

                        float chance = Mathf.Clamp01(entry.EffectiveSpawnChance);
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
                        WorldWeaveTemplateEntry picked = PickWeighted(candidates);
                        if (picked == null)
                        {
                            break;
                        }

                        if (TryPlace(zone, picked, occupiedTemplateCells))
                        {
                            perMapCount[picked.Key] = GetCount(perMapCount, picked.Key) + 1;
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
                        TemplateSpawnPlugin.LogDebug("No eligible template candidates: " + DescribeZone(zone));
                    }
                }
                else
                {
                    TemplateSpawnPlugin.LogDebug("Spawn complete: zone=" + DescribeZone(zone) + " placed=" + placedCount);
                }
            }
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning("TrySpawnForZone failed: " + DescribeZone(zone) + " / " + ex);
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
                    int removed = WorldWeaveCacheManager.PurgeOldCaches(days);
                    if (removed > 0)
                    {
                        TemplateSpawnPlugin.LogInfo("Purged old caches: " + removed + " (older than " + days + " days)");
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
            TemplateSpawnPlugin.LogWarning("Cache maintenance failed: " + ex);
        }
    }

    private static void LoadTemplatesFromDisk(List<WorldWeaveTemplateEntry> target)
    {
        string root = GetTemplateRoot();
        if (!Directory.Exists(root))
        {
            TemplateSpawnPlugin.LogDebug("Template root not found: " + root);
            return;
        }

        string[] jsonFiles = Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly);
        TemplateSpawnPlugin.LogDebug("Scanning disk templates: root=" + root + " jsonFiles=" + jsonFiles.Length);

        for (int i = 0; i < jsonFiles.Length; i++)
        {
            string jsonPath = jsonFiles[i];
            string bundleId = Path.GetFileNameWithoutExtension(jsonPath);
            string zPath = Path.Combine(root, bundleId + ".z");

            if (!File.Exists(zPath))
            {
                TemplateSpawnPlugin.LogDebug("Missing archive for template config: " + jsonPath);
                continue;
            }

            if (!TryLoadBundleConfig(jsonPath, out WorldWeaveConfig bundleConfig))
            {
                continue;
            }

            LoadBundle(bundleId, jsonPath, zPath, bundleConfig, target);
        }
    }

    private static void LoadTemplatesFromLoadedMods(List<WorldWeaveTemplateEntry> target)
    {
        BaseModManager manager = BaseModManager.Instance;
        if (manager == null || manager.packages == null)
        {
            return;
        }

        TemplateSpawnPlugin.LogDebug("Scanning loaded mods for WorldWeave markers");

        foreach (BaseModPackage package in manager.packages)
        {
            if (package == null || !package.activated || package.dirInfo == null || !package.dirInfo.Exists)
            {
                continue;
            }

            string markerPath = Path.Combine(package.dirInfo.FullName, "WorldWeave.json");
            if (!File.Exists(markerPath))
            {
                continue;
            }

            LoadTemplatesFromPackageRoot(package.dirInfo, target);
        }
    }

    private static void LoadTemplatesFromPackageRoot(DirectoryInfo rootDir, List<WorldWeaveTemplateEntry> target)
    {
        if (rootDir == null || !rootDir.Exists)
        {
            return;
        }

        string templatesDir = Path.Combine(rootDir.FullName, "Templates");
        if (!Directory.Exists(templatesDir))
        {
            TemplateSpawnPlugin.LogDebug("Templates directory not found: " + templatesDir);
            return;
        }

        string[] jsonFiles = Directory.GetFiles(templatesDir, "*.json", SearchOption.TopDirectoryOnly);
        TemplateSpawnPlugin.LogDebug("Scanning package templates: root=" + templatesDir + " jsonFiles=" + jsonFiles.Length);

        for (int i = 0; i < jsonFiles.Length; i++)
        {
            string jsonPath = jsonFiles[i];
            string bundleId = Path.GetFileNameWithoutExtension(jsonPath);
            string zPath = Path.Combine(templatesDir, bundleId + ".z");

            if (!File.Exists(zPath))
            {
                TemplateSpawnPlugin.LogDebug("Missing archive for template config: " + jsonPath);
                continue;
            }

            if (!TryLoadBundleConfig(jsonPath, out WorldWeaveConfig bundleConfig))
            {
                continue;
            }

            LoadBundle(bundleId, jsonPath, zPath, bundleConfig, target);
        }
    }

    private static bool TryLoadBundleConfig(string jsonPath, out WorldWeaveConfig bundleConfig)
    {
        bundleConfig = null;

        try
        {
            bundleConfig = JsonConvert.DeserializeObject<WorldWeaveConfig>(File.ReadAllText(jsonPath));
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning("JSON parse failed: " + jsonPath + " / " + ex);
            return false;
        }

        if (bundleConfig == null)
        {
            TemplateSpawnPlugin.LogWarning("JSON config was empty or invalid: " + jsonPath);
            return false;
        }

        if (!bundleConfig.enabled)
        {
            TemplateSpawnPlugin.LogDebug("Bundle disabled: " + jsonPath);
            return false;
        }

        return true;
    }

    private static void LoadBundle(
        string bundleId,
        string jsonPath,
        string zPath,
        WorldWeaveConfig bundleConfig,
        List<WorldWeaveTemplateEntry> target)
    {
        bool usePersistentCache = IsPersistentCacheEnabled();

        List<WorldWeaveVariantConfig> variants = new List<WorldWeaveVariantConfig>();
        if (bundleConfig.variants != null && bundleConfig.variants.Count > 0)
        {
            for (int i = 0; i < bundleConfig.variants.Count; i++)
            {
                if (bundleConfig.variants[i] != null)
                {
                    variants.Add(bundleConfig.variants[i]);
                }
            }
        }
        else
        {
            variants.Add(new WorldWeaveVariantConfig
            {
                id = "default"
            });
        }

        TemplateSpawnPlugin.LogDebug(
            "Loading bundle: " + bundleId
            + " variants=" + variants.Count
            + " cache=" + (usePersistentCache ? "on" : "off"));

        int unnamedIndex = 0;
        Map fullMap = null;
        ZoneExportData sourceExport = null;
        string sourceTemp = null;
        bool sourceLoaded = false;
        bool sourceFailed = false;

        try
        {
            for (int i = 0; i < variants.Count; i++)
            {
                WorldWeaveVariantConfig variant = variants[i];
                if (variant == null)
                {
                    continue;
                }

                string variantId = variant.id;
                if (string.IsNullOrEmpty(variantId))
                {
                    variantId = "variant_" + unnamedIndex;
                    unnamedIndex++;
                }

                if (variant.enabled.HasValue && !variant.enabled.Value)
                {
                    TemplateSpawnPlugin.LogDebug("Skip disabled variant: " + bundleId + "/" + variantId);
                    continue;
                }

                WorldWeaveRect configuredRect = GetEffectiveActiveRect(bundleConfig, variant);

                if (usePersistentCache && WorldWeaveCacheManager.TryLoadCachedVariant(
                    bundleId,
                    variantId,
                    jsonPath,
                    zPath,
                    bundleConfig,
                    variant,
                    configuredRect,
                    out WorldWeaveTemplateEntry cachedEntry))
                {
                    TemplateSpawnPlugin.LogDebug(
                        "Cache hit: " + bundleId + "/" + variantId
                        + " rect=" + DescribeRect(cachedEntry.sourceRect));
                    AddOrReplaceTemplate(target, cachedEntry);
                    continue;
                }

                if (sourceFailed)
                {
                    TemplateSpawnPlugin.LogDebug("Skip uncached variant after source load failure: " + bundleId + "/" + variantId);
                    continue;
                }

                if (!sourceLoaded)
                {
                    try
                    {
                        sourceTemp = CreateTempDir();
                        TemplateSpawnPlugin.LogDebug("Extracting source archive: " + zPath);

                        using (ZipFile zip = ZipFile.Read(zPath))
                        {
                            zip.ExtractExistingFile = ExtractExistingFileAction.OverwriteSilently;
                            zip.ExtractAll(sourceTemp);
                        }

                        fullMap = LoadSourceMap(sourceTemp, out sourceExport);
                        sourceLoaded = fullMap != null && sourceExport != null;

                        if (!sourceLoaded)
                        {
                            sourceFailed = true;
                            TemplateSpawnPlugin.LogWarning(
                                "Source load failed: " + bundleId
                                + " / " + zPath
                                + " (json=" + jsonPath + ")");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        sourceFailed = true;
                        TemplateSpawnPlugin.LogWarning("Source unzip/load failed: " + bundleId + " / " + zPath + " / " + ex);
                        continue;
                    }
                }

                try
                {
                    WorldWeaveRect rect = ResolveRect(fullMap, configuredRect);
                    if (rect == null || !rect.IsValid)
                    {
                        TemplateSpawnPlugin.LogWarning(
                            "Active rect could not be resolved: "
                            + bundleId + "/" + variantId
                            + " configured=" + DescribeRect(configuredRect)
                            + " mapSize=" + (fullMap != null ? fullMap.Size.ToString() : "null"));
                        continue;
                    }

                    string cutTemp = CreateTempDir();
                    try
                    {
                        WorldWeaveTemplateEntry entry = BuildVariantCache(
                            bundleId,
                            jsonPath,
                            zPath,
                            bundleConfig,
                            variant,
                            variantId,
                            fullMap,
                            sourceExport,
                            rect,
                            cutTemp);

                        if (entry != null && entry.loaded)
                        {
                            if (usePersistentCache)
                            {
                                WorldWeaveCacheManager.SaveVariantCache(entry, jsonPath, zPath, cutTemp);
                            }

                            TemplateSpawnPlugin.LogDebug(
                                "Variant ready: " + bundleId + "/" + variantId
                                + " rect=" + DescribeRect(rect)
                                + " cache=" + (usePersistentCache ? "on" : "off"));

                            AddOrReplaceTemplate(target, entry);
                        }
                        else
                        {
                            TemplateSpawnPlugin.LogWarning(
                                "Variant build produced no data: " + bundleId + "/" + variantId
                                + " rect=" + DescribeRect(rect));
                        }
                    }
                    finally
                    {
                        DeleteDirectorySafe(cutTemp);
                    }
                }
                catch (Exception ex)
                {
                    TemplateSpawnPlugin.LogWarning("Variant build failed: " + bundleId + "/" + variantId + " / " + ex);
                }
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(sourceTemp))
            {
                DeleteDirectorySafe(sourceTemp);
            }
        }
    }

    private static void AddOrReplaceTemplate(List<WorldWeaveTemplateEntry> target, WorldWeaveTemplateEntry entry)
    {
        if (target == null || entry == null || !entry.loaded)
        {
            return;
        }

        for (int i = 0; i < target.Count; i++)
        {
            WorldWeaveTemplateEntry existing = target[i];
            if (existing != null && string.Equals(existing.Key, entry.Key, StringComparison.OrdinalIgnoreCase))
            {
                target[i] = entry;
                return;
            }
        }

        target.Add(entry);
    }

    private static Map LoadSourceMap(string sourceTemp, out ZoneExportData sourceExport)
    {
        sourceExport = null;

        if (string.IsNullOrEmpty(sourceTemp))
        {
            return null;
        }

        string sourceRoot = sourceTemp + Path.DirectorySeparatorChar;
        string mapPath = Path.Combine(sourceTemp, "map");
        string exportPath = Path.Combine(sourceTemp, "export");

        Map fullMap = GameIO.LoadFile<Map>(mapPath);
        if (fullMap == null)
        {
            return null;
        }

        Map activeMap = EClass._map;

        try
        {
            fullMap.SetReference();
            fullMap.Load(sourceRoot, import: false, partial: null);

            sourceExport = IO.LoadFile<ZoneExportData>(exportPath) ?? new ZoneExportData();
            if (sourceExport.serializedCards == null)
            {
                sourceExport.serializedCards = new SerializedCards();
            }

            using (WorldWeaveState.EnterTemplateImport())
            {
                fullMap.OnImport(sourceExport);
            }

            return fullMap;
        }
        finally
        {
            if (activeMap != null)
            {
                activeMap.SetReference();
            }
        }
    }

    private static WorldWeaveTemplateEntry BuildVariantCache(
        string bundleId,
        string jsonPath,
        string zPath,
        WorldWeaveConfig bundleConfig,
        WorldWeaveVariantConfig variantConfig,
        string variantId,
        Map fullMap,
        ZoneExportData sourceExport,
        WorldWeaveRect rect,
        string cutTemp)
    {
        if (fullMap == null || sourceExport == null || rect == null || !rect.IsValid)
        {
            return null;
        }

        ZoneExportData cutExport = new ZoneExportData
        {
            name = sourceExport.name,
            usermap = sourceExport.usermap,
            orgMap = null,
            serializedCards = new SerializedCards()
        };

        PartialMap savePartial = new PartialMap
        {
            offsetX = rect.x,
            offsetZ = rect.z,
            w = rect.w,
            h = rect.h
        };

        fullMap.Save(cutTemp + Path.DirectorySeparatorChar, cutExport, savePartial);

        string cutMapPath = Path.Combine(cutTemp, "map");
        Map cutMap = GameIO.LoadFile<Map>(cutMapPath);
        if (cutMap == null)
        {
            return null;
        }

        PartialMap loadPartial = new PartialMap
        {
            w = rect.w,
            h = rect.h
        };

        cutMap.Load(cutTemp + Path.DirectorySeparatorChar, import: false, partial: loadPartial);

        return new WorldWeaveTemplateEntry
        {
            bundleId = bundleId,
            variantId = variantId,
            jsonPath = jsonPath,
            zPath = zPath,
            bundleConfig = bundleConfig,
            variantConfig = variantConfig,
            map = cutMap,
            exportData = cutExport,
            sourceRect = rect,
            anchorX = rect.w / 2,
            anchorZ = rect.h / 2,
            loaded = true
        };
    }

    private static WorldWeaveRect GetEffectiveActiveRect(WorldWeaveConfig bundleConfig, WorldWeaveVariantConfig variantConfig)
    {
        if (variantConfig != null && variantConfig.activeRect != null && variantConfig.activeRect.IsValid)
        {
            return variantConfig.activeRect;
        }

        if (bundleConfig != null && bundleConfig.activeRect != null && bundleConfig.activeRect.IsValid)
        {
            return bundleConfig.activeRect;
        }

        return null;
    }

    private static bool TryPlace(Zone zone, WorldWeaveTemplateEntry entry, HashSet<int> occupiedTemplateCells)
    {
        if (zone == null || entry == null || entry.map == null || entry.exportData == null || entry.sourceRect == null || !entry.sourceRect.IsValid)
        {
            return false;
        }

        bool oldRelative = PartialMap.relative;
        Map activeMap = EClass._map;

        try
        {
            if (zone.map != null)
            {
                zone.map.SetReference();
            }

            PartialMap.relative = false;

            const int maxAttempts = 32;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Point target = PickPreferredTarget(zone.map);
                if (target == null || !target.IsValid)
                {
                    continue;
                }

                int startX = target.x - entry.anchorX;
                int startZ = target.z - entry.anchorZ;

                if (!AdjustStartToFit(zone.map, entry.sourceRect, ref startX, ref startZ))
                {
                    continue;
                }

                if (!CanPlaceWithoutOverlap(zone.map, occupiedTemplateCells, startX, startZ, entry.sourceRect.w, entry.sourceRect.h))
                {
                    continue;
                }

                if (entry.EffectiveReplaceExisting)
                {
                    ClearTargetArea(zone, startX, startZ, entry.sourceRect.w, entry.sourceRect.h);
                }

                ClearPlantsInArea(zone.map, startX, startZ, entry.sourceRect.w, entry.sourceRect.h);
                PartialMap partial = new PartialMap
                {
                    map = entry.map,
                    exportData = entry.exportData,
                    offsetX = entry.sourceRect.x,
                    offsetZ = entry.sourceRect.z,
                    w = entry.sourceRect.w,
                    h = entry.sourceRect.h,
                    dir = 0,
                    baseHeight = 0,
                    baseHeightWater = 0,
                    value = 0,
                    allowRotate = false,
                    ignoreBlock = false,
                    localOffsetX = 0,
                    localOffsetZ = 0,
                    editMode = false,
                    procedural = false,
                    ruinChance = 0f,
                    result = new PartialMap.Result(),
                    validPoints = new HashSet<int>()
                };

                FillValidPoints(partial.validPoints, zone.map, startX, startZ, entry.sourceRect.w, entry.sourceRect.h);
                partial.Apply(new Point(startX, startZ), PartialMap.ApplyMode.Apply);

                MarkOccupiedArea(occupiedTemplateCells, zone.map, startX, startZ, entry.sourceRect.w, entry.sourceRect.h);

                TemplateSpawnPlugin.LogDebug(
                    "Placed template: " + DescribeEntry(entry)
                    + " zone=" + DescribeZone(zone)
                    + " at=(" + startX + "," + startZ + ")"
                    + " size=" + entry.sourceRect.w + "x" + entry.sourceRect.h);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning(
                "Apply failed: " + DescribeEntry(entry)
                + " zone=" + DescribeZone(zone)
                + " / " + ex);
            return false;
        }
        finally
        {
            PartialMap.relative = oldRelative;

            if (activeMap != null)
            {
                activeMap.SetReference();
            }
        }
    }

    private static void ClearTargetArea(Zone zone, Point start, int w, int h)
    {
        if (zone == null || zone.map == null || start == null)
        {
            return;
        }

        ClearTargetArea(zone, start.x, start.z, w, h);
    }

    private static void ClearTargetArea(Zone zone, int startX, int startZ, int w, int h)
    {
        if (zone == null || zone.map == null)
        {
            return;
        }

        Map map = zone.map;
        int endX = startX + w;
        int endZ = startZ + h;

        List<Card> cards = map.Cards.ToList();
        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i];
            if (ShouldProtectFromTemplateClear(card))
            {
                continue;
            }

            if (!WorldWeaveState.IsGeneratedCard(zone, card))
            {
                continue;
            }

            bool overlap = false;
            card.ForeachPoint(delegate (Point p, bool main)
            {
                if (p != null && p.x >= startX && p.x < endX && p.z >= startZ && p.z < endZ)
                {
                    overlap = true;
                }
            });

            if (overlap)
            {
                card.Destroy();
            }
        }
    }

    private static void ClearPlantsInArea(Map map, int startX, int startZ, int w, int h)
    {
        if (map == null)
        {
            return;
        }

        Point p = new Point();
        int endX = startX + w;
        int endZ = startZ + h;

        for (int x = startX; x < endX; x++)
        {
            for (int z = startZ; z < endZ; z++)
            {
                if (!map.bounds.Contains(x, z))
                {
                    continue;
                }

                p.Set(x, z);
                if (map.TryGetPlant(p) != null)
                {
                    map.RemovePlant(p);
                }
            }
        }
    }

    private static bool ShouldProtectFromTemplateClear(Card card)
    {
        if (card == null || card.isDestroyed || card.pos == null)
        {
            return true;
        }

        if (card.IsGlobal)
        {
            return true;
        }

        if (card.isChara)
        {
            return true;
        }

        if (card.isPlayerCreation)
        {
            return true;
        }

        if (card.isNPCProperty)
        {
            return true;
        }

        if (card.isGifted || card.isLostProperty || card.isCopy || card.isStolen)
        {
            return true;
        }

        if (card.IsImportant || card.IsUnique || card.IsPowerful)
        {
            return true;
        }

        if (card.c_wasInPcParty)
        {
            return true;
        }

        return false;
    }

    private static WorldWeaveRect ResolveRect(Map sourceMap, WorldWeaveRect configured)
    {
        if (sourceMap == null || sourceMap.cells == null)
        {
            return null;
        }

        WorldWeaveRect rect;
        if (configured != null && configured.IsValid)
        {
            rect = new WorldWeaveRect
            {
                x = configured.x,
                z = configured.z,
                w = configured.w,
                h = configured.h
            };
        }
        else
        {
            rect = AutoDetectRect(sourceMap);
        }

        if (rect == null)
        {
            return null;
        }

        if (rect.x < 0 || rect.z < 0 || rect.x >= sourceMap.Size || rect.z >= sourceMap.Size)
        {
            return null;
        }

        if (rect.x + rect.w > sourceMap.Size)
        {
            rect.w = sourceMap.Size - rect.x;
        }

        if (rect.z + rect.h > sourceMap.Size)
        {
            rect.h = sourceMap.Size - rect.z;
        }

        if (rect.w < 1 || rect.h < 1)
        {
            return null;
        }

        return rect;
    }

    private static WorldWeaveRect AutoDetectRect(Map sourceMap)
    {
        if (sourceMap == null || sourceMap.cells == null)
        {
            return null;
        }

        int minX = sourceMap.Size;
        int minZ = sourceMap.Size;
        int maxX = -1;
        int maxZ = -1;

        for (int x = 0; x < sourceMap.Size; x++)
        {
            for (int z = 0; z < sourceMap.Size; z++)
            {
                Cell cell = sourceMap.cells[x, z];
                if (!HasContent(cell))
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (z < minZ) minZ = z;
                if (x > maxX) maxX = x;
                if (z > maxZ) maxZ = z;
            }
        }

        if (maxX < minX || maxZ < minZ)
        {
            return null;
        }

        return new WorldWeaveRect
        {
            x = minX,
            z = minZ,
            w = (maxX - minX) + 1,
            h = (maxZ - minZ) + 1
        };
    }

    private static bool HasContent(Cell cell)
    {
        if (cell == null)
        {
            return false;
        }

        if (cell._block != 0 || cell._floor != 0 || cell.obj != 0 || cell._bridge != 0 || cell._roofBlock != 0 || cell._deco != 0 || cell.decal != 0)
        {
            return true;
        }

        if (cell.effect != null)
        {
            return true;
        }

        if (cell.detail != null && (cell.detail.things.Count > 0 || cell.detail.charas.Count > 0))
        {
            return true;
        }

        return false;
    }

    private static Point PickPreferredTarget(Map map)
    {
        if (map == null)
        {
            return Point.Invalid;
        }

        int centerX = (map.bounds.x + map.bounds.maxX) / 2;
        int centerZ = (map.bounds.z + map.bounds.maxZ) / 2;
        int half = 15;

        int minX = Math.Max(map.bounds.x, centerX - half);
        int maxX = Math.Min(map.bounds.maxX, centerX + half - 1);
        int minZ = Math.Max(map.bounds.z, centerZ - half);
        int maxZ = Math.Min(map.bounds.maxZ, centerZ + half - 1);

        if (maxX >= minX && maxZ >= minZ)
        {
            return new Point(
                minX + EClass.rnd(maxX - minX + 1),
                minZ + EClass.rnd(maxZ - minZ + 1));
        }

        Point fallback = map.bounds.GetRandomSurface(centered: true, walkable: false, allowWater: true);
        if (fallback != null && fallback.IsValid)
        {
            return fallback;
        }

        return map.bounds.GetRandomSurface(centered: false, walkable: false, allowWater: true);
    }

    private static bool AdjustStartToFit(Map map, WorldWeaveRect rect, ref int startX, ref int startZ)
    {
        if (map == null || rect == null)
        {
            return false;
        }

        int minX = map.bounds.x;
        int minZ = map.bounds.z;
        int maxStartX = map.bounds.maxX - rect.w + 1;
        int maxStartZ = map.bounds.maxZ - rect.h + 1;

        if (maxStartX < minX || maxStartZ < minZ)
        {
            return false;
        }

        if (startX < minX)
        {
            startX = minX;
        }
        else if (startX > maxStartX)
        {
            startX = maxStartX;
        }

        if (startZ < minZ)
        {
            startZ = minZ;
        }
        else if (startZ > maxStartZ)
        {
            startZ = maxStartZ;
        }

        return true;
    }

    private static bool CanPlaceWithoutOverlap(Map map, HashSet<int> occupiedTemplateCells, int startX, int startZ, int w, int h)
    {
        if (map == null || occupiedTemplateCells == null)
        {
            return false;
        }

        int endX = startX + w;
        int endZ = startZ + h;

        for (int x = startX; x < endX; x++)
        {
            for (int z = startZ; z < endZ; z++)
            {
                if (!map.bounds.Contains(x, z))
                {
                    return false;
                }

                int index = x + z * map.Size;
                if (occupiedTemplateCells.Contains(index))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void FillValidPoints(HashSet<int> validPoints, Map map, int startX, int startZ, int w, int h)
    {
        if (validPoints == null || map == null)
        {
            return;
        }

        int endX = startX + w;
        int endZ = startZ + h;

        for (int x = startX; x < endX; x++)
        {
            for (int z = startZ; z < endZ; z++)
            {
                if (!map.bounds.Contains(x, z))
                {
                    continue;
                }

                validPoints.Add(x + z * map.Size);
            }
        }
    }

    private static void MarkOccupiedArea(HashSet<int> occupiedTemplateCells, Map map, int startX, int startZ, int w, int h)
    {
        if (occupiedTemplateCells == null || map == null)
        {
            return;
        }

        int endX = startX + w;
        int endZ = startZ + h;

        for (int x = startX; x < endX; x++)
        {
            for (int z = startZ; z < endZ; z++)
            {
                if (!map.bounds.Contains(x, z))
                {
                    continue;
                }

                occupiedTemplateCells.Add(x + z * map.Size);
            }
        }
    }

    private static WorldWeaveTemplateEntry PickWeighted(List<WorldWeaveTemplateEntry> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        int total = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            total += candidates[i].EffectiveWeight;
        }

        if (total <= 0)
        {
            return candidates[0];
        }

        int roll = EClass.rnd(total);
        int acc = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            acc += candidates[i].EffectiveWeight;
            if (roll < acc)
            {
                return candidates[i];
            }
        }

        return candidates[candidates.Count - 1];
    }

    private static bool IsTargetZone(Zone zone)
    {
        return zone is Zone_Field || zone is Zone_Wilds;
    }

    private static bool IsAllowedZone(WorldWeaveTemplateEntry entry, Zone zone)
    {
        if (entry == null || entry.bundleConfig == null)
        {
            return false;
        }

        List<string> zoneTypes = entry.bundleConfig.zoneTypes;
        if (zoneTypes != null && zoneTypes.Count > 0)
        {
            string typeName = zone.GetType().Name;
            for (int i = 0; i < zoneTypes.Count; i++)
            {
                if (string.Equals(zoneTypes[i], typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private static bool IsAllowedBiome(WorldWeaveTemplateEntry entry, Zone zone)
    {
        if (entry == null || entry.bundleConfig == null)
        {
            return false;
        }

        List<string> biomes = entry.bundleConfig.biomes;
        if (biomes != null && biomes.Count > 0)
        {
            string biomeId = zone.IdBiome;
            for (int i = 0; i < biomes.Count; i++)
            {
                if (string.Equals(biomes[i], biomeId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private static int GetCount(Dictionary<string, int> dict, string key)
    {
        int value;
        return dict.TryGetValue(key, out value) ? value : 0;
    }

    private static string GetTemplateRoot()
    {
        string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(dllDir))
        {
            dllDir = ".";
        }

        return Path.Combine(dllDir, "Templates");
    }

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ElinWorldWeave", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteDirectorySafe(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }

    private static string DescribeZone(Zone zone)
    {
        if (zone == null)
        {
            return "zone=null";
        }

        return zone.GetType().Name + "#" + zone.uid + " biome=" + zone.IdBiome;
    }

    private static string DescribeEntry(WorldWeaveTemplateEntry entry)
    {
        if (entry == null)
        {
            return "entry=null";
        }

        return entry.bundleId + "/" + entry.variantId;
    }

    private static string DescribeRect(WorldWeaveRect rect)
    {
        if (rect == null)
        {
            return "rect=null";
        }

        return "[" + rect.x + "," + rect.z + " " + rect.w + "x" + rect.h + "]";
    }
}
