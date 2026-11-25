using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

internal static partial class WorldWeaveLogic
{
    private static WorldWeaveRect ResolveRect(
        Map sourceMap,
        WorldWeaveRect configured)
    {
        if (sourceMap == null
            || sourceMap.cells == null)
        {
            return null;
        }

        WorldWeaveRect rect;

        if (configured != null
            && configured.IsValid)
        {
            rect =
                new WorldWeaveRect
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

        if (rect.x < 0
            || rect.z < 0
            || rect.x >= sourceMap.Size
            || rect.z >= sourceMap.Size)
        {
            return null;
        }

        if (rect.x + rect.w > sourceMap.Size)
        {
            rect.w =
                sourceMap.Size - rect.x;
        }

        if (rect.z + rect.h > sourceMap.Size)
        {
            rect.h =
                sourceMap.Size - rect.z;
        }

        if (rect.w < 1
            || rect.h < 1)
        {
            return null;
        }

        return rect;
    }

    private static WorldWeaveRect AutoDetectRect(
        Map sourceMap)
    {
        if (sourceMap == null
            || sourceMap.cells == null)
        {
            return null;
        }

        int minX = sourceMap.Size;
        int minZ = sourceMap.Size;
        int maxX = -1;
        int maxZ = -1;

        for (int x = 0;
             x < sourceMap.Size;
             x++)
        {
            for (int z = 0;
                 z < sourceMap.Size;
                 z++)
            {
                Cell cell =
                    sourceMap.cells[x, z];

                if (!HasContent(cell))
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (z < minZ)
                {
                    minZ = z;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (z > maxZ)
                {
                    maxZ = z;
                }
            }
        }

        if (maxX < minX
            || maxZ < minZ)
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

        if (cell._block != 0
            || cell._floor != 0
            || cell.obj != 0
            || cell._bridge != 0
            || cell._roofBlock != 0
            || cell._deco != 0
            || cell.decal != 0)
        {
            return true;
        }

        if (cell.effect != null)
        {
            return true;
        }

        if (cell.detail != null
            && (cell.detail.things.Count > 0
                || cell.detail.charas.Count > 0))
        {
            return true;
        }

        return false;
    }

    private static Point PickPreferredTarget(
        Map map)
    {
        if (map == null)
        {
            return Point.Invalid;
        }

        int centerX =
            (map.bounds.x + map.bounds.maxX) / 2;

        int centerZ =
            (map.bounds.z + map.bounds.maxZ) / 2;

        int half = 15;

        int minX =
            Math.Max(
                map.bounds.x,
                centerX - half);

        int maxX =
            Math.Min(
                map.bounds.maxX,
                centerX + half - 1);

        int minZ =
            Math.Max(
                map.bounds.z,
                centerZ - half);

        int maxZ =
            Math.Min(
                map.bounds.maxZ,
                centerZ + half - 1);

        if (maxX >= minX
            && maxZ >= minZ)
        {
            return new Point(
                minX + EClass.rnd(
                    maxX - minX + 1),
                minZ + EClass.rnd(
                    maxZ - minZ + 1));
        }

        Point fallback =
            map.bounds.GetRandomSurface(
                centered: true,
                walkable: false,
                allowWater: true);

        if (fallback != null
            && fallback.IsValid)
        {
            return fallback;
        }

        return map.bounds.GetRandomSurface(
            centered: false,
            walkable: false,
            allowWater: true);
    }

    private static bool AdjustStartToFit(Map map, WorldWeaveRect rect, ref int startX, ref int startZ)
    {
        if (map == null || rect == null)
        {
            return false;
        }

        int minX = map.bounds.x;
        int minZ = map.bounds.z;
        int maxStartX = map.bounds.maxX;
        int maxStartZ = map.bounds.maxZ;

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
                if (map.bounds.Contains(x, z))
                {
                    int index = x + z * map.Size;
                    if (occupiedTemplateCells.Contains(index))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
    private static void FillValidPoints(
        HashSet<int> validPoints,
        Map map,
        int startX,
        int startZ,
        int w,
        int h)
    {
        if (validPoints == null
            || map == null)
        {
            return;
        }

        int endX = startX + w;
        int endZ = startZ + h;

        for (int x = startX;
             x < endX;
             x++)
        {
            for (int z = startZ;
                 z < endZ;
                 z++)
            {
                if (!map.bounds.Contains(x, z))
                {
                    continue;
                }

                validPoints.Add(
                    x + z * map.Size);
            }
        }
    }

    private static void MarkOccupiedArea(
        HashSet<int> occupiedTemplateCells,
        Map map,
        int startX,
        int startZ,
        int w,
        int h)
    {
        if (occupiedTemplateCells == null
            || map == null)
        {
            return;
        }

        int endX = startX + w;
        int endZ = startZ + h;

        for (int x = startX;
             x < endX;
             x++)
        {
            for (int z = startZ;
                 z < endZ;
                 z++)
            {
                occupiedTemplateCells.Add(
                    x + z * map.Size);
            }
        }
    }
    private static WorldWeaveTemplateEntry PickWeighted(
        List<WorldWeaveTemplateEntry> candidates)
    {
        if (candidates == null
            || candidates.Count == 0)
        {
            return null;
        }

        int total = 0;

        for (int i = 0;
             i < candidates.Count;
             i++)
        {
            total +=
                candidates[i].EffectiveWeight;
        }

        if (total <= 0)
        {
            return candidates[0];
        }

        int roll =
            EClass.rnd(total);

        int acc = 0;

        for (int i = 0;
             i < candidates.Count;
             i++)
        {
            acc +=
                candidates[i].EffectiveWeight;

            if (roll < acc)
            {
                return candidates[i];
            }
        }

        return candidates[candidates.Count - 1];
    }

    private static bool IsTargetZone(
        Zone zone)
    {
        return zone is Zone_Field
            || zone is Zone_Wilds;
    }

    private static bool IsAllowedZone(
        WorldWeaveTemplateEntry entry,
        Zone zone)
    {
        if (entry == null
            || entry.bundleConfig == null)
        {
            return false;
        }

        List<string> zoneTypes =
            entry.bundleConfig.zoneTypes;

        if (zoneTypes != null
            && zoneTypes.Count > 0)
        {
            string typeName =
                zone.GetType().Name;

            for (int i = 0;
                 i < zoneTypes.Count;
                 i++)
            {
                if (string.Equals(
                    zoneTypes[i],
                    typeName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private static bool IsAllowedBiome(
        WorldWeaveTemplateEntry entry,
        Zone zone)
    {
        if (entry == null
            || entry.bundleConfig == null)
        {
            return false;
        }

        List<string> biomes =
            entry.bundleConfig.biomes;

        if (biomes != null
            && biomes.Count > 0)
        {
            string biomeId =
                zone.IdBiome;

            for (int i = 0;
                 i < biomes.Count;
                 i++)
            {
                if (string.Equals(
                    biomes[i],
                    biomeId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private static int GetCount(
        Dictionary<string, int> dict,
        string key)
    {
        int value;

        return dict.TryGetValue(
            key,
            out value)
            ? value
            : 0;
    }

    private static string GetTemplateRoot()
    {
        string dllDir =
            Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);

        if (string.IsNullOrEmpty(dllDir))
        {
            dllDir = ".";
        }

        return Path.Combine(
            dllDir,
            "Templates");
    }

    private static string CreateTempDir()
    {
        string dir =
            Path.Combine(
                Path.GetTempPath(),
                "ElinWorldWeave",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(dir);

        return dir;
    }

    private static void DeleteDirectorySafe(
        string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path)
                && Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    true);
            }
        }
        catch
        {
        }
    }

    private static string DescribeZone(
        Zone zone)
    {
        if (zone == null)
        {
            return "zone=null";
        }

        return zone.GetType().Name
            + "#"
            + zone.uid
            + " biome="
            + zone.IdBiome;
    }

    private static string DescribeEntry(
        WorldWeaveTemplateEntry entry)
    {
        if (entry == null)
        {
            return "entry=null";
        }

        return entry.bundleId
            + "/"
            + entry.variantId;
    }

    private static string DescribeRect(
        WorldWeaveRect rect)
    {
        if (rect == null)
        {
            return "rect=null";
        }

        return "["
            + rect.x
            + ","
            + rect.z
            + " "
            + rect.w
            + "x"
            + rect.h
            + "]";
    }
}