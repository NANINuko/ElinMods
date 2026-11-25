using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Ionic.Zip;

internal static partial class WorldWeaveLogic
{
    private static void LoadTemplatesFromDisk(
        List<WorldWeaveTemplateEntry> target)
    {
        string root = GetTemplateRoot();

        if (!Directory.Exists(root))
        {
            TemplateSpawnPlugin.LogDebug(
                "Template root not found: " + root);
            return;
        }

        string[] jsonFiles = Directory.GetFiles(
            root,
            "*.json",
            SearchOption.TopDirectoryOnly);

        TemplateSpawnPlugin.LogDebug(
            "Scanning disk templates: root="
            + root
            + " jsonFiles="
            + jsonFiles.Length);

        for (int i = 0; i < jsonFiles.Length; i++)
        {
            string jsonPath = jsonFiles[i];

            string bundleId =
                Path.GetFileNameWithoutExtension(jsonPath);

            string zPath =
                Path.Combine(root, bundleId + ".z");

            if (!File.Exists(zPath))
            {
                TemplateSpawnPlugin.LogDebug(
                    "Missing archive for template config: "
                    + jsonPath);
                continue;
            }

            if (!TryLoadBundleConfig(
                jsonPath,
                out WorldWeaveConfig bundleConfig))
            {
                continue;
            }

            LoadBundle(
                bundleId,
                jsonPath,
                zPath,
                bundleConfig,
                target);
        }
    }

    private static void LoadTemplatesFromLoadedMods(
        List<WorldWeaveTemplateEntry> target)
    {
        BaseModManager manager = BaseModManager.Instance;

        if (manager == null || manager.packages == null)
        {
            return;
        }

        TemplateSpawnPlugin.LogDebug(
            "Scanning loaded mods for WorldWeave markers");

        foreach (BaseModPackage package in manager.packages)
        {
            if (package == null
                || !package.activated
                || package.dirInfo == null
                || !package.dirInfo.Exists)
            {
                continue;
            }

            string markerPath =
                Path.Combine(
                    package.dirInfo.FullName,
                    "WorldWeave.json");

            if (!File.Exists(markerPath))
            {
                continue;
            }

            LoadTemplatesFromPackageRoot(
                package.dirInfo,
                target);
        }
    }

    private static void LoadTemplatesFromPackageRoot(
        DirectoryInfo rootDir,
        List<WorldWeaveTemplateEntry> target)
    {
        if (rootDir == null || !rootDir.Exists)
        {
            return;
        }

        string templatesDir =
            Path.Combine(
                rootDir.FullName,
                "Templates");

        if (!Directory.Exists(templatesDir))
        {
            TemplateSpawnPlugin.LogDebug(
                "Templates directory not found: "
                + templatesDir);
            return;
        }

        string[] jsonFiles = Directory.GetFiles(
            templatesDir,
            "*.json",
            SearchOption.TopDirectoryOnly);

        TemplateSpawnPlugin.LogDebug(
            "Scanning package templates: root="
            + templatesDir
            + " jsonFiles="
            + jsonFiles.Length);

        for (int i = 0; i < jsonFiles.Length; i++)
        {
            string jsonPath = jsonFiles[i];

            string bundleId =
                Path.GetFileNameWithoutExtension(jsonPath);

            string zPath =
                Path.Combine(
                    templatesDir,
                    bundleId + ".z");

            if (!File.Exists(zPath))
            {
                TemplateSpawnPlugin.LogDebug(
                    "Missing archive for template config: "
                    + jsonPath);
                continue;
            }

            if (!TryLoadBundleConfig(
                jsonPath,
                out WorldWeaveConfig bundleConfig))
            {
                continue;
            }

            LoadBundle(
                bundleId,
                jsonPath,
                zPath,
                bundleConfig,
                target);
        }
    }

    private static bool TryLoadBundleConfig(
        string jsonPath,
        out WorldWeaveConfig bundleConfig)
    {
        bundleConfig = null;

        try
        {
            bundleConfig =
                JsonConvert.DeserializeObject<WorldWeaveConfig>(
                    File.ReadAllText(jsonPath));
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning(
                "JSON parse failed: "
                + jsonPath
                + " / "
                + ex);

            return false;
        }

        if (bundleConfig == null)
        {
            TemplateSpawnPlugin.LogWarning(
                "JSON config was empty or invalid: "
                + jsonPath);

            return false;
        }

        if (!bundleConfig.enabled)
        {
            TemplateSpawnPlugin.LogDebug(
                "Bundle disabled: " + jsonPath);

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
        bool usePersistentCache =
            IsPersistentCacheEnabled();

        List<WorldWeaveVariantConfig> variants =
            new List<WorldWeaveVariantConfig>();

        if (bundleConfig.variants != null
            && bundleConfig.variants.Count > 0)
        {
            for (int i = 0;
                 i < bundleConfig.variants.Count;
                 i++)
            {
                if (bundleConfig.variants[i] != null)
                {
                    variants.Add(
                        bundleConfig.variants[i]);
                }
            }
        }
        else
        {
            variants.Add(
                new WorldWeaveVariantConfig
                {
                    id = "default"
                });
        }

        TemplateSpawnPlugin.LogDebug(
            "Loading bundle: "
            + bundleId
            + " variants="
            + variants.Count
            + " cache="
            + (usePersistentCache ? "on" : "off"));

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
                WorldWeaveVariantConfig variant =
                    variants[i];

                if (variant == null)
                {
                    continue;
                }

                string variantId = variant.id;

                if (string.IsNullOrEmpty(variantId))
                {
                    variantId =
                        "variant_" + unnamedIndex;

                    unnamedIndex++;
                }

                if (variant.enabled.HasValue
                    && !variant.enabled.Value)
                {
                    TemplateSpawnPlugin.LogDebug(
                        "Skip disabled variant: "
                        + bundleId
                        + "/"
                        + variantId);

                    continue;
                }

                WorldWeaveRect configuredRect =
                    GetEffectiveActiveRect(
                        bundleConfig,
                        variant);

                if (usePersistentCache
                    && WorldWeaveCacheManager.TryLoadCachedVariant(
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
                        "Cache hit: "
                        + bundleId
                        + "/"
                        + variantId
                        + " rect="
                        + DescribeRect(
                            cachedEntry.sourceRect));

                    AddOrReplaceTemplate(
                        target,
                        cachedEntry);

                    continue;
                }

                if (sourceFailed)
                {
                    TemplateSpawnPlugin.LogDebug(
                        "Skip uncached variant after source load failure: "
                        + bundleId
                        + "/"
                        + variantId);

                    continue;
                }

                if (!sourceLoaded)
                {
                    try
                    {
                        sourceTemp = CreateTempDir();

                        TemplateSpawnPlugin.LogDebug(
                            "Extracting source archive: "
                            + zPath);

                        using (ZipFile zip = ZipFile.Read(zPath))
                        {
                            zip.ExtractExistingFile =
                                ExtractExistingFileAction.OverwriteSilently;

                            zip.ExtractAll(sourceTemp);
                        }

                        fullMap =
                            LoadSourceMap(
                                sourceTemp,
                                out sourceExport);

                        sourceLoaded =
                            fullMap != null
                            && sourceExport != null;

                        if (!sourceLoaded)
                        {
                            sourceFailed = true;

                            TemplateSpawnPlugin.LogWarning(
                                "Source load failed: "
                                + bundleId
                                + " / "
                                + zPath
                                + " (json="
                                + jsonPath
                                + ")");

                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        sourceFailed = true;

                        TemplateSpawnPlugin.LogWarning(
                            "Source unzip/load failed: "
                            + bundleId
                            + " / "
                            + zPath
                            + " / "
                            + ex);

                        continue;
                    }
                }

                try
                {
                    WorldWeaveRect rect =
                        ResolveRect(
                            fullMap,
                            configuredRect);

                    if (rect == null || !rect.IsValid)
                    {
                        TemplateSpawnPlugin.LogWarning(
                            "Active rect could not be resolved: "
                            + bundleId
                            + "/"
                            + variantId
                            + " configured="
                            + DescribeRect(configuredRect)
                            + " mapSize="
                            + (fullMap != null
                                ? fullMap.Size.ToString()
                                : "null"));

                        continue;
                    }

                    string cutTemp =
                        CreateTempDir();

                    try
                    {
                        WorldWeaveTemplateEntry entry =
                            BuildVariantCache(
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

                        if (entry != null
                            && entry.loaded)
                        {
                            if (usePersistentCache)
                            {
                                WorldWeaveCacheManager.SaveVariantCache(
                                    entry,
                                    jsonPath,
                                    zPath,
                                    cutTemp);
                            }

                            TemplateSpawnPlugin.LogDebug(
                                "Variant ready: "
                                + bundleId
                                + "/"
                                + variantId
                                + " rect="
                                + DescribeRect(rect)
                                + " cache="
                                + (usePersistentCache
                                    ? "on"
                                    : "off"));

                            AddOrReplaceTemplate(
                                target,
                                entry);
                        }
                        else
                        {
                            TemplateSpawnPlugin.LogWarning(
                                "Variant build produced no data: "
                                + bundleId
                                + "/"
                                + variantId
                                + " rect="
                                + DescribeRect(rect));
                        }
                    }
                    finally
                    {
                        DeleteDirectorySafe(
                            cutTemp);
                    }
                }
                catch (Exception ex)
                {
                    TemplateSpawnPlugin.LogWarning(
                        "Variant build failed: "
                        + bundleId
                        + "/"
                        + variantId
                        + " / "
                        + ex);
                }
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(sourceTemp))
            {
                DeleteDirectorySafe(
                    sourceTemp);
            }
        }
    }

    private static void AddOrReplaceTemplate(
        List<WorldWeaveTemplateEntry> target,
        WorldWeaveTemplateEntry entry)
    {
        if (target == null
            || entry == null
            || !entry.loaded)
        {
            return;
        }

        for (int i = 0; i < target.Count; i++)
        {
            WorldWeaveTemplateEntry existing =
                target[i];

            if (existing != null
                && string.Equals(
                    existing.Key,
                    entry.Key,
                    StringComparison.OrdinalIgnoreCase))
            {
                target[i] = entry;
                return;
            }
        }

        target.Add(entry);
    }

    private static Map LoadSourceMap(
        string sourceTemp,
        out ZoneExportData sourceExport)
    {
        sourceExport = null;

        if (string.IsNullOrEmpty(sourceTemp))
        {
            return null;
        }

        string sourceRoot =
            sourceTemp
            + Path.DirectorySeparatorChar;

        string mapPath =
            Path.Combine(
                sourceTemp,
                "map");

        string exportPath =
            Path.Combine(
                sourceTemp,
                "export");

        Map fullMap =
            GameIO.LoadFile<Map>(
                mapPath);

        if (fullMap == null)
        {
            return null;
        }

        Map activeMap = EClass._map;

        try
        {
            fullMap.SetReference();

            fullMap.Load(
                sourceRoot,
                import: false,
                partial: null);

            sourceExport =
                IO.LoadFile<ZoneExportData>(
                    exportPath)
                ?? new ZoneExportData();

            if (sourceExport.serializedCards == null)
            {
                sourceExport.serializedCards =
                    new SerializedCards();
            }

            using (WorldWeaveState.EnterTemplateImport())
            {
                fullMap.OnImport(
                    sourceExport);
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
        if (fullMap == null
            || sourceExport == null
            || rect == null
            || !rect.IsValid)
        {
            return null;
        }

        ZoneExportData cutExport =
            new ZoneExportData
            {
                name = sourceExport.name,
                usermap = sourceExport.usermap,
                orgMap = null,
                serializedCards = new SerializedCards()
            };

        PartialMap savePartial =
            new PartialMap
            {
                offsetX = rect.x,
                offsetZ = rect.z,
                w = rect.w,
                h = rect.h
            };

        fullMap.Save(
            cutTemp + Path.DirectorySeparatorChar,
            cutExport,
            savePartial);

        string cutMapPath =
            Path.Combine(
                cutTemp,
                "map");

        Map cutMap =
            GameIO.LoadFile<Map>(
                cutMapPath);

        if (cutMap == null)
        {
            return null;
        }

        PartialMap loadPartial =
            new PartialMap
            {
                w = rect.w,
                h = rect.h
            };

        cutMap.Load(
            cutTemp + Path.DirectorySeparatorChar,
            import: false,
            partial: loadPartial);

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

    private static WorldWeaveRect GetEffectiveActiveRect(
        WorldWeaveConfig bundleConfig,
        WorldWeaveVariantConfig variantConfig)
    {
        if (variantConfig != null
            && variantConfig.activeRect != null
            && variantConfig.activeRect.IsValid)
        {
            return variantConfig.activeRect;
        }

        if (bundleConfig != null
            && bundleConfig.activeRect != null
            && bundleConfig.activeRect.IsValid)
        {
            return bundleConfig.activeRect;
        }

        return null;
    }
}