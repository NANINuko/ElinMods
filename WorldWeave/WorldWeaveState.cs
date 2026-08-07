using System;
using System.Collections.Generic;

internal static class WorldWeaveState
{
    private static readonly HashSet<int> PendingZoneUids = new HashSet<int>();

    private static readonly Dictionary<int, int> GenerationDepthByZoneUid = new Dictionary<int, int>();
    private static readonly Dictionary<int, HashSet<int>> GeneratedCardUidsByZone = new Dictionary<int, HashSet<int>>();
    private static readonly HashSet<int> ActiveTemplatePlacementZoneUids = new HashSet<int>();

    private static int _templateImportDepth;
    private static int _suppressGeneratedMarkDepth;

    public static bool LoadingTemplate
    {
        get { return _templateImportDepth > 0; }
    }

    public static IDisposable EnterTemplateImport()
    {
        _templateImportDepth++;
        return new Scope(delegate
        {
            if (_templateImportDepth > 0)
            {
                _templateImportDepth--;
            }
        });
    }

    public static void BeginZoneGeneration(Zone zone)
    {
        if (zone == null)
        {
            return;
        }

        if (!(zone is Zone_Field) && !(zone is Zone_Wilds))
        {
            return;
        }

        int depth;
        if (!GenerationDepthByZoneUid.TryGetValue(zone.uid, out depth))
        {
            depth = 0;
        }

        GenerationDepthByZoneUid[zone.uid] = depth + 1;

        if (!GeneratedCardUidsByZone.ContainsKey(zone.uid))
        {
            GeneratedCardUidsByZone[zone.uid] = new HashSet<int>();
        }
    }

    public static void EndZoneGeneration(Zone zone)
    {
        if (zone == null)
        {
            return;
        }

        int depth;
        if (!GenerationDepthByZoneUid.TryGetValue(zone.uid, out depth))
        {
            return;
        }

        depth--;
        if (depth <= 0)
        {
            GenerationDepthByZoneUid.Remove(zone.uid);
            GeneratedCardUidsByZone.Remove(zone.uid);
        }
        else
        {
            GenerationDepthByZoneUid[zone.uid] = depth;
        }
    }

    public static IDisposable BeginTemplatePlacement(Zone zone)
    {
        if (zone != null)
        {
            ActiveTemplatePlacementZoneUids.Add(zone.uid);
        }

        return new Scope(delegate
        {
            if (zone != null)
            {
                ActiveTemplatePlacementZoneUids.Remove(zone.uid);
            }
        });
    }

    public static IDisposable SuppressGeneratedMarking()
    {
        _suppressGeneratedMarkDepth++;
        return new Scope(delegate
        {
            if (_suppressGeneratedMarkDepth > 0)
            {
                _suppressGeneratedMarkDepth--;
            }
        });
    }

    public static void MarkGeneratedCard(Zone zone, Card card)
    {
        if (zone == null || card == null)
        {
            return;
        }

        if (_suppressGeneratedMarkDepth > 0)
        {
            return;
        }

        int depth;
        if (!GenerationDepthByZoneUid.TryGetValue(zone.uid, out depth) || depth <= 0)
        {
            return;
        }

        HashSet<int> set;
        if (!GeneratedCardUidsByZone.TryGetValue(zone.uid, out set))
        {
            set = new HashSet<int>();
            GeneratedCardUidsByZone[zone.uid] = set;
        }

        set.Add(card.uid);
    }

    public static bool IsGeneratedCard(Zone zone, Card card)
    {
        if (zone == null || card == null)
        {
            return false;
        }

        HashSet<int> set;
        return GeneratedCardUidsByZone.TryGetValue(zone.uid, out set) && set.Contains(card.uid);
    }

    public static bool ShouldAllowDestroy(Card card)
    {
        if (card == null || card.isDestroyed || card.pos == null)
        {
            return true;
        }

        Zone zone = card.parent as Zone;
        if (zone == null || !ActiveTemplatePlacementZoneUids.Contains(zone.uid))
        {
            return true;
        }

        if (card.IsGlobal)
        {
            return false;
        }

        if (card.isChara)
        {
            return false;
        }

        if (card.isPlayerCreation)
        {
            return false;
        }

        if (card.isNPCProperty)
        {
            return false;
        }

        if (card.isGifted || card.isLostProperty || card.isCopy || card.isStolen)
        {
            return false;
        }

        if (card.IsImportant || card.IsUnique || card.IsPowerful)
        {
            return false;
        }

        if (card.c_wasInPcParty)
        {
            return false;
        }

        return IsGeneratedCard(zone, card);
    }

    public static void MarkPending(Zone zone)
    {
        if (zone == null)
        {
            return;
        }

        if (zone is Zone_Field || zone is Zone_Wilds)
        {
            PendingZoneUids.Add(zone.uid);
        }
    }

    public static bool ConsumePending(Zone zone)
    {
        if (zone == null)
        {
            return false;
        }

        return PendingZoneUids.Remove(zone.uid);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public Scope(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _onDispose?.Invoke();
        }
    }
}
