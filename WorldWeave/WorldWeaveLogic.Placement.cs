using System;
using System.Collections.Generic;
using System.Linq;

internal static partial class WorldWeaveLogic
{
    private static bool TryPlace(
        Zone zone,
        WorldWeaveTemplateEntry entry,
        HashSet<int> occupiedTemplateCells)
    {
        if (zone == null
            || entry == null
            || entry.map == null
            || entry.exportData == null
            || entry.sourceRect == null
            || !entry.sourceRect.IsValid)
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

            for (int attempt = 0;
                 attempt < maxAttempts;
                 attempt++)
            {
                Point target =
                    PickPreferredTarget(zone.map);

                if (target == null
                    || !target.IsValid)
                {
                    continue;
                }

                int startX =
                    target.x - entry.anchorX;

                int startZ =
                    target.z - entry.anchorZ;

                if (!AdjustStartToFit(
                    zone.map,
                    entry.sourceRect,
                    ref startX,
                    ref startZ))
                {
                    continue;
                }

                if (!CanPlaceWithoutOverlap(
                    zone.map,
                    occupiedTemplateCells,
                    startX,
                    startZ,
                    entry.sourceRect.w,
                    entry.sourceRect.h))
                {
                    continue;
                }

                if (entry.EffectiveReplaceExisting)
                {
                    ClearTargetArea(
                        zone,
                        startX,
                        startZ,
                        entry.sourceRect.w,
                        entry.sourceRect.h);
                }

                ClearPlantsInArea(
                    zone.map,
                    startX,
                    startZ,
                    entry.sourceRect.w,
                    entry.sourceRect.h);

                PartialMap partial =
                    new PartialMap
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

                FillValidPoints(
                    partial.validPoints,
                    zone.map,
                    startX,
                    startZ,
                    entry.sourceRect.w,
                    entry.sourceRect.h);

                partial.Apply(new Point(startX, startZ), PartialMap.ApplyMode.Apply);

                MarkOccupiedArea(
                    occupiedTemplateCells,
                    zone.map,
                    startX,
                    startZ,
                    entry.sourceRect.w,
                    entry.sourceRect.h);

                TemplateSpawnPlugin.LogDebug(
                    "Placed template: "
                    + DescribeEntry(entry)
                    + " zone="
                    + DescribeZone(zone)
                    + " at=("
                    + startX
                    + ","
                    + startZ
                    + ")"
                    + " size="
                    + entry.sourceRect.w
                    + "x"
                    + entry.sourceRect.h);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            TemplateSpawnPlugin.LogWarning(
                "Apply failed: "
                + DescribeEntry(entry)
                + " zone="
                + DescribeZone(zone)
                + " / "
                + ex);

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

    private static void ClearTargetArea(
        Zone zone,
        Point start,
        int w,
        int h)
    {
        if (zone == null
            || zone.map == null
            || start == null)
        {
            return;
        }

        ClearTargetArea(
            zone,
            start.x,
            start.z,
            w,
            h);
    }

    private static void ClearTargetArea(
        Zone zone,
        int startX,
        int startZ,
        int w,
        int h)
    {
        if (zone == null
            || zone.map == null)
        {
            return;
        }

        Map map = zone.map;

        int endX = startX + w;
        int endZ = startZ + h;

        List<Card> cards =
            map.Cards.ToList();

        for (int i = 0;
             i < cards.Count;
             i++)
        {
            Card card = cards[i];

            if (ShouldProtectFromTemplateClear(card))
            {
                continue;
            }

            if (!WorldWeaveState.IsGeneratedCard(
                zone,
                card))
            {
                continue;
            }

            bool overlap = false;

            card.ForeachPoint(
                delegate (Point p, bool main)
                {
                    if (p != null
                        && p.x >= startX
                        && p.x < endX
                        && p.z >= startZ
                        && p.z < endZ)
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

    private static void ClearPlantsInArea(
        Map map,
        int startX,
        int startZ,
        int w,
        int h)
    {
        if (map == null)
        {
            return;
        }

        Point p = new Point();

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

                p.Set(x, z);

                if (map.TryGetPlant(p) != null)
                {
                    map.RemovePlant(p);
                }
            }
        }
    }

    private static bool ShouldProtectFromTemplateClear(
        Card card)
    {
        if (card == null
            || card.isDestroyed
            || card.pos == null)
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

        if (card.isGifted
            || card.isLostProperty
            || card.isCopy
            || card.isStolen)
        {
            return true;
        }

        if (card.IsImportant
            || card.IsUnique
            || card.IsPowerful)
        {
            return true;
        }

        if (card.c_wasInPcParty)
        {
            return true;
        }

        return false;
    }
}