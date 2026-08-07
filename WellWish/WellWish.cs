using System;
using System.Diagnostics;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

[BepInPlugin(WellWishNotifierPlugin.PluginGuid, WellWishNotifierPlugin.PluginName, WellWishNotifierPlugin.PluginVersion)]
public sealed class WellWishNotifierPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.naninuko.elin.wellwishnotifier";
    public const string PluginName = "Elin Well Wish Notifier";
    public const string PluginVersion = "1.0.0";

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(WellWishNotifierPlugin).Assembly);
        Logger.LogInfo(PluginName + " loaded");
    }

    private void OnDestroy()
    {
        try
        {
            _harmony?.UnpatchSelf();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[WellWishNotifier] Unpatch failed: " + ex);
        }
    }
}

internal static class WellWishNotifierLogic
{
    private const string NotifiedFlagKey = "com.naninuko.elin.wellwish.notified_ready";
    private const string WellWishKeyItemAlias = "well_wish";
    private const string Message = "井戸の願いを再びかなえられるようになりました。";

    public static void TryNotify()
    {
        try
        {
            if (EClass.game == null || EClass.player == null || EClass.pc == null)
                return;

            if (EClass.player.wellWished)
                return;

            if (EClass.player.CountKeyItem(WellWishKeyItemAlias) <= 0)
                return;

            if (IsAlreadyNotified())
                return;

            SetNotified(true);
            EnqueueDialogOk(Message);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[WellWishNotifier] TryNotify failed: " + ex);
        }
    }

    public static void ClearNotified()
    {
        try
        {
            SetNotified(false);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[WellWishNotifier] ClearNotified failed: " + ex);
        }
    }

    public static bool IsWellWishKeyItem(int id)
    {
        try
        {
            if (EClass.sources?.keyItems?.alias == null)
                return false;

            if (!EClass.sources.keyItems.alias.TryGetValue(WellWishKeyItemAlias, out var row))
                return false;

            return row != null && row.id == id;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAlreadyNotified()
    {
        if (EClass.player?.dialogFlags == null)
            return false;

        return EClass.player.dialogFlags.TryGetValue(NotifiedFlagKey, out var value) && value != 0;
    }

    private static void SetNotified(bool value)
    {
        if (EClass.player?.dialogFlags == null)
            return;

        EClass.player.dialogFlags[NotifiedFlagKey] = value ? 1 : 0;
    }

    private static void EnqueueDialogOk(string message)
    {
        try
        {
            if (EClass.core?.actionsNextFrame != null)
            {
                EClass.core.actionsNextFrame.Add(() => Dialog.Ok(message, null));
                return;
            }
        }
        catch
        {
        }

        Dialog.Ok(message, null);
    }
}

[HarmonyPatch(typeof(GameDate), nameof(GameDate.AdvanceYear))]
internal static class Patch_GameDate_AdvanceYear
{
    private static bool _wasWellWished;

    [HarmonyPrefix]
    private static void Prefix()
    {
        _wasWellWished = EClass.player != null && EClass.player.wellWished;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_wasWellWished)
        {
            WellWishNotifierLogic.ClearNotified();
        }

        WellWishNotifierLogic.TryNotify();
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.OnLoad))]
internal static class Patch_Player_OnLoad
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        WellWishNotifierLogic.TryNotify();
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.ModKeyItem), new[] { typeof(string), typeof(int), typeof(bool) })]
internal static class Patch_Player_ModKeyItem_String
{
    [HarmonyPostfix]
    private static void Postfix(string alias, int num, bool msg)
    {
        if (num <= 0)
            return;

        if (string.Equals(alias, "well_wish", StringComparison.OrdinalIgnoreCase))
        {
            WellWishNotifierLogic.TryNotify();
        }
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.ModKeyItem), new[] { typeof(int), typeof(int), typeof(bool) })]
internal static class Patch_Player_ModKeyItem_Int
{
    [HarmonyPostfix]
    private static void Postfix(int id, int num, bool msg)
    {
        if (num <= 0)
            return;

        if (WellWishNotifierLogic.IsWellWishKeyItem(id))
        {
            WellWishNotifierLogic.TryNotify();
        }
    }
}
