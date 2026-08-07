using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

[Serializable]
internal sealed class WorldWeaveCacheManifest
{
    public int schemaVersion = 1;

    public string cacheKey;
    public string bundleId;
    public string variantId;

    public string jsonPath;
    public long jsonLength;
    public long jsonWriteUtcTicks;

    public string zPath;
    public long zLength;
    public long zWriteUtcTicks;

    public int sourceRectX;
    public int sourceRectZ;
    public int sourceRectW;
    public int sourceRectH;

    public int anchorX;
    public int anchorZ;
}

[Serializable]
internal sealed class WorldWeaveCacheStatistics
{
    public string rootPath;
    public int bundleCount;
    public int variantCount;
    public int cacheCount;
    public int brokenCount;
    public long totalBytes;

    public override string ToString()
    {
        return "root=" + rootPath
            + " bundles=" + bundleCount
            + " variants=" + variantCount
            + " caches=" + cacheCount
            + " broken=" + brokenCount
            + " size=" + FormatBytes(totalBytes);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return bytes + units[unitIndex];
        }

        return value.ToString(value < 10d ? "0.00" : "0.0") + units[unitIndex];
    }
}

internal static class WorldWeaveCacheManager
{
    private const int SchemaVersion = 1;
    private const string RootFolderName = "WorldWeaveCache";
    private const string ManifestFileName = "cache.json";
    private const string MapFileName = "map";
    private const string ExportFileName = "export";

    private static readonly object _lock = new object();

    private static void Trace(string message)
    {
        TemplateSpawnPlugin.LogDebug("[Cache] " + message);
    }

    private static string DescribeRect(WorldWeaveRect rect)
    {
        if (rect == null)
        {
            return "null";
        }

        return rect.x + "," + rect.z + " " + rect.w + "x" + rect.h;
    }

    private static string GetCacheRoot()
    {
        string root = CorePath.modData;
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(CorePath.RootData, "Mod");
        }

        return Path.Combine(root, RootFolderName);
    }

    private static string GetBundleRoot(string bundleId)
    {
        return Path.Combine(GetCacheRoot(), SanitizeSegment(bundleId));
    }

    private static string GetVariantRoot(string bundleId, string variantId)
    {
        return Path.Combine(GetBundleRoot(bundleId), SanitizeSegment(variantId));
    }

    private static string GetSignatureRoot(string bundleId, string variantId, string signature)
    {
        return Path.Combine(GetVariantRoot(bundleId, variantId), signature);
    }

    private static string SanitizeSegment(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "_";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder sb = new StringBuilder(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool bad = false;

            for (int j = 0; j < invalid.Length; j++)
            {
                if (c == invalid[j])
                {
                    bad = true;
                    break;
                }
            }

            sb.Append(bad ? '_' : c);
        }

        return sb.ToString();
    }

    private static bool TryGetFileStamp(string path, out string stamp)
    {
        stamp = null;

        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }

            FileInfo info = new FileInfo(path);
            string fullPath = Path.GetFullPath(path).ToLowerInvariant();
            stamp = fullPath + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryFillStamp(string path, out long length, out long ticks)
    {
        length = 0;
        ticks = 0;

        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            FileInfo info = new FileInfo(path);
            length = info.Length;
            ticks = info.LastWriteTimeUtc.Ticks;
        }
        catch
        {
        }
    }

    private static string BuildSourceSignature(string jsonPath, string zPath)
    {
        if (!TryGetFileStamp(jsonPath, out string jsonStamp))
        {
            return null;
        }

        if (!TryGetFileStamp(zPath, out string zStamp))
        {
            return null;
        }

        string raw = jsonStamp + "||" + zStamp;
        using (SHA256 sha = SHA256.Create())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(raw);
            byte[] hash = sha.ComputeHash(bytes);

            StringBuilder sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }

            return sb.ToString();
        }
    }

    private static string GetManifestPath(string cacheDir)
    {
        return Path.Combine(cacheDir, ManifestFileName);
    }

    private static string GetMapPath(string cacheDir)
    {
        return Path.Combine(cacheDir, MapFileName);
    }

    private static string GetExportPath(string cacheDir)
    {
        return Path.Combine(cacheDir, ExportFileName);
    }

    private static WorldWeaveRect MakeRect(int x, int z, int w, int h)
    {
        return new WorldWeaveRect
        {
            x = x,
            z = z,
            w = w,
            h = h
        };
    }

    private static bool IsRectSame(WorldWeaveRect a, WorldWeaveRect b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        return a.x == b.x && a.z == b.z && a.w == b.w && a.h == b.h;
    }

    private static long GetDirectorySize(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return 0;
        }

        long total = 0;

        try
        {
            string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    total += new FileInfo(files[i]).Length;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return total;
    }

    public static bool TryLoadCachedVariant(
        string bundleId,
        string variantId,
        string jsonPath,
        string zPath,
        WorldWeaveConfig bundleConfig,
        WorldWeaveVariantConfig variantConfig,
        WorldWeaveRect configuredRect,
        out WorldWeaveTemplateEntry entry)
    {
        entry = null;
        string entryId = bundleId + "/" + variantId;

        try
        {
            string signature = BuildSourceSignature(jsonPath, zPath);
            if (string.IsNullOrEmpty(signature))
            {
                Trace("MISS " + entryId + " signature unavailable");
                return false;
            }

            string cacheDir = GetSignatureRoot(bundleId, variantId, signature);
            string manifestPath = GetManifestPath(cacheDir);
            string mapPath = GetMapPath(cacheDir);
            string exportPath = GetExportPath(cacheDir);

            if (!File.Exists(manifestPath) || !File.Exists(mapPath) || !File.Exists(exportPath))
            {
                Trace("MISS " + entryId + " missing cache files");
                return false;
            }

            WorldWeaveCacheManifest manifest = IO.LoadFile<WorldWeaveCacheManifest>(manifestPath);
            if (manifest == null)
            {
                Trace("MISS " + entryId + " manifest unreadable");
                return false;
            }

            if (manifest.schemaVersion != SchemaVersion)
            {
                Trace("MISS " + entryId + " schema mismatch: " + manifest.schemaVersion + " != " + SchemaVersion);
                return false;
            }

            if (!string.Equals(manifest.cacheKey, signature, StringComparison.Ordinal))
            {
                Trace("MISS " + entryId + " signature mismatch");
                return false;
            }

            if (!string.Equals(manifest.bundleId ?? "", bundleId ?? "", StringComparison.Ordinal))
            {
                Trace("MISS " + entryId + " bundle mismatch");
                return false;
            }

            if (!string.Equals(manifest.variantId ?? "", variantId ?? "", StringComparison.Ordinal))
            {
                Trace("MISS " + entryId + " variant mismatch");
                return false;
            }

            if (configuredRect != null && configuredRect.IsValid)
            {
                WorldWeaveRect savedRect = MakeRect(
                    manifest.sourceRectX,
                    manifest.sourceRectZ,
                    manifest.sourceRectW,
                    manifest.sourceRectH);

                if (!IsRectSame(configuredRect, savedRect))
                {
                    Trace(
                        "MISS " + entryId + " rect mismatch"
                        + " configured=" + DescribeRect(configuredRect)
                        + " cached=" + DescribeRect(savedRect));
                    return false;
                }
            }

            Map cachedMap = GameIO.LoadFile<Map>(mapPath);
            if (cachedMap == null)
            {
                Trace("MISS " + entryId + " map unreadable");
                return false;
            }

            PartialMap loadPartial = new PartialMap
            {
                w = manifest.sourceRectW,
                h = manifest.sourceRectH
            };

            Map activeMap = EClass._map;
            try
            {
                cachedMap.SetReference();
                cachedMap.Load(cacheDir + Path.DirectorySeparatorChar, import: false, partial: loadPartial);
            }
            finally
            {
                if (activeMap != null)
                {
                    activeMap.SetReference();
                }
            }


            ZoneExportData exportData = IO.LoadFile<ZoneExportData>(exportPath) ?? new ZoneExportData();
            if (exportData.serializedCards == null)
            {
                exportData.serializedCards = new SerializedCards();
            }

            entry = new WorldWeaveTemplateEntry
            {
                bundleId = bundleId,
                variantId = variantId,
                jsonPath = jsonPath,
                zPath = zPath,
                bundleConfig = bundleConfig,
                variantConfig = variantConfig,
                map = cachedMap,
                exportData = exportData,
                sourceRect = MakeRect(
                    manifest.sourceRectX,
                    manifest.sourceRectZ,
                    manifest.sourceRectW,
                    manifest.sourceRectH),
                anchorX = manifest.anchorX,
                anchorZ = manifest.anchorZ,
                loaded = true
            };

            Trace("HIT " + entryId + " rect=" + DescribeRect(entry.sourceRect));
            return true;
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning("Cache load failed: " + entryId + " / " + ex);

            try
            {
                string signature = BuildSourceSignature(jsonPath, zPath);
                if (!string.IsNullOrEmpty(signature))
                {
                    DeleteCacheBySignature(bundleId, variantId, signature);
                }
            }
            catch
            {
            }

            entry = null;
            return false;
        }
    }

    public static bool SaveVariantCache(
        WorldWeaveTemplateEntry entry,
        string jsonPath,
        string zPath,
        string sourceDir)
    {
        if (entry == null || !entry.loaded || entry.map == null || entry.exportData == null || entry.sourceRect == null || !entry.sourceRect.IsValid)
        {
            Trace("SAVE skip: invalid entry");
            return false;
        }

        string entryId = entry.bundleId + "/" + entry.variantId;

        if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
        {
            Trace("SAVE skip " + entryId + " sourceDir missing: " + sourceDir);
            return false;
        }

        string signature = BuildSourceSignature(jsonPath, zPath);
        if (string.IsNullOrEmpty(signature))
        {
            Trace("SAVE skip " + entryId + " signature unavailable");
            return false;
        }

        string cacheDir = GetSignatureRoot(entry.bundleId, entry.variantId, signature);
        string manifestPath = GetManifestPath(cacheDir);
        string exportPath = GetExportPath(cacheDir);

        try
        {
            lock (_lock)
            {
                if (Directory.Exists(cacheDir))
                {
                    IO.DeleteDirectory(cacheDir);
                }

                IO.CreateDirectory(cacheDir);
                IO.CopyAll(sourceDir, cacheDir, overwrite: true);
                IO.SaveFile(exportPath, entry.exportData, compress: true);

                WorldWeaveCacheManifest manifest = new WorldWeaveCacheManifest
                {
                    schemaVersion = SchemaVersion,
                    cacheKey = signature,
                    bundleId = entry.bundleId,
                    variantId = entry.variantId,
                    jsonPath = jsonPath,
                    zPath = zPath,
                    sourceRectX = entry.sourceRect.x,
                    sourceRectZ = entry.sourceRect.z,
                    sourceRectW = entry.sourceRect.w,
                    sourceRectH = entry.sourceRect.h,
                    anchorX = entry.anchorX,
                    anchorZ = entry.anchorZ
                };

                TryFillStamp(jsonPath, out manifest.jsonLength, out manifest.jsonWriteUtcTicks);
                TryFillStamp(zPath, out manifest.zLength, out manifest.zWriteUtcTicks);

                IO.SaveFile(manifestPath, manifest, compress: false);

                int purged = PurgeOtherSignatures(entry.bundleId, entry.variantId, jsonPath, zPath);
                if (purged > 0)
                {
                    TemplateSpawnPlugin.LogInfo("Purged stale cache generations: " + purged + " / " + entry.Key);
                }
            }

            Trace("SAVE done " + entryId + " rect=" + DescribeRect(entry.sourceRect));
            return true;
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning("Cache save failed: " + entry.bundleId + "/" + entry.variantId + " / " + ex);
            return false;
        }
    }

    public static bool DeleteAllCaches()
    {
        lock (_lock)
        {
            string root = GetCacheRoot();
            if (!Directory.Exists(root))
            {
                return false;
            }

            IO.DeleteDirectory(root);
            return true;
        }
    }

    public static bool DeleteBundleCaches(string bundleId)
    {
        lock (_lock)
        {
            string path = GetBundleRoot(bundleId);
            if (!Directory.Exists(path))
            {
                return false;
            }

            IO.DeleteDirectory(path);
            return true;
        }
    }

    public static bool DeleteVariantCaches(string bundleId, string variantId)
    {
        lock (_lock)
        {
            string path = GetVariantRoot(bundleId, variantId);
            if (!Directory.Exists(path))
            {
                return false;
            }

            IO.DeleteDirectory(path);
            return true;
        }
    }

    public static bool DeleteCurrentSignatureCache(string bundleId, string variantId, string jsonPath, string zPath)
    {
        string signature = BuildSourceSignature(jsonPath, zPath);
        if (string.IsNullOrEmpty(signature))
        {
            return false;
        }

        return DeleteCacheBySignature(bundleId, variantId, signature);
    }

    public static int PurgeOtherSignatures(string bundleId, string variantId, string keepJsonPath, string keepZPath)
    {
        string keepSignature = BuildSourceSignature(keepJsonPath, keepZPath);
        if (string.IsNullOrEmpty(keepSignature))
        {
            return 0;
        }

        string variantRoot = GetVariantRoot(bundleId, variantId);
        if (!Directory.Exists(variantRoot))
        {
            return 0;
        }

        int removed = 0;

        lock (_lock)
        {
            string[] dirs = Directory.GetDirectories(variantRoot);
            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                string name = Path.GetFileName(dir);

                if (string.Equals(name, keepSignature, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IO.DeleteDirectory(dir);
                removed++;
            }
        }

        return removed;
    }

    public static int PurgeOldCaches(int olderThanDays)
    {
        if (olderThanDays <= 0)
        {
            return 0;
        }

        string root = GetCacheRoot();
        if (!Directory.Exists(root))
        {
            return 0;
        }

        DateTime threshold = DateTime.UtcNow.AddDays(-olderThanDays);
        int removed = 0;

        lock (_lock)
        {
            string[] manifests = Directory.GetFiles(root, ManifestFileName, SearchOption.AllDirectories);
            for (int i = 0; i < manifests.Length; i++)
            {
                string manifestPath = manifests[i];

                try
                {
                    if (File.GetLastWriteTimeUtc(manifestPath) < threshold)
                    {
                        string cacheDir = Path.GetDirectoryName(manifestPath);
                        if (!string.IsNullOrEmpty(cacheDir) && Directory.Exists(cacheDir))
                        {
                            IO.DeleteDirectory(cacheDir);
                            removed++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    TemplateSpawnPlugin.LogWarning("Old cache purge failed: " + manifestPath + " / " + ex);
                }
            }
        }

        return removed;
    }

    public static WorldWeaveCacheStatistics GetStatistics()
    {
        WorldWeaveCacheStatistics stats = new WorldWeaveCacheStatistics
        {
            rootPath = GetCacheRoot()
        };

        string root = stats.rootPath;
        if (!Directory.Exists(root))
        {
            return stats;
        }

        HashSet<string> bundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string[] manifests = Directory.GetFiles(root, ManifestFileName, SearchOption.AllDirectories);
            stats.cacheCount = manifests.Length;

            for (int i = 0; i < manifests.Length; i++)
            {
                string manifestPath = manifests[i];
                string cacheDir = Path.GetDirectoryName(manifestPath);

                if (!string.IsNullOrEmpty(cacheDir))
                {
                    stats.totalBytes += GetDirectorySize(cacheDir);

                    DirectoryInfo signatureDir = new DirectoryInfo(cacheDir);
                    DirectoryInfo variantDir = signatureDir.Parent;
                    DirectoryInfo bundleDir = variantDir != null ? variantDir.Parent : null;

                    if (variantDir != null)
                    {
                        variants.Add(variantDir.FullName);
                    }

                    if (bundleDir != null)
                    {
                        bundles.Add(bundleDir.FullName);
                    }

                    bool hasMap = File.Exists(GetMapPath(cacheDir));
                    bool hasExport = File.Exists(GetExportPath(cacheDir));
                    if (!hasMap || !hasExport)
                    {
                        stats.brokenCount++;
                    }
                }
                else
                {
                    stats.brokenCount++;
                }
            }
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning("Cache statistics failed: " + ex);
        }

        stats.bundleCount = bundles.Count;
        stats.variantCount = variants.Count;
        return stats;
    }

    public static void LogStatistics()
    {
        try
        {
            TemplateSpawnPlugin.LogInfo("Cache stats: " + GetStatistics());
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning("Cache stats log failed: " + ex);
        }
    }

    private static bool DeleteCacheBySignature(string bundleId, string variantId, string signature)
    {
        string cacheDir = GetSignatureRoot(bundleId, variantId, signature);
        if (!Directory.Exists(cacheDir))
        {
            return false;
        }

        IO.DeleteDirectory(cacheDir);
        return true;
    }
}
