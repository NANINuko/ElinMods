using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[BepInPlugin(TemplateSpawnPlugin.PluginGuid, TemplateSpawnPlugin.PluginName, TemplateSpawnPlugin.PluginVersion)]
public sealed class TemplateSpawnPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.naninuko.elin.worldweave";
    public const string PluginName = "WorldWeave";
    public const string PluginVersion = "1.0.0";

    internal static TemplateSpawnPlugin Instance;

    internal ConfigEntry<int> MaxTemplatesPerMap;
    internal ConfigEntry<bool> UsePersistentCache;
    internal ConfigEntry<bool> PurgeOldCachesOnReload;
    internal ConfigEntry<int> CacheRetentionDays;
    internal ConfigEntry<bool> LogCacheStatisticsOnReload;
    internal ConfigEntry<bool> EnableDetailedLogs;

    private Harmony _harmony;

    internal static bool IsDetailedLoggingEnabled
    {
        get
        {
            return Instance != null
                && Instance.EnableDetailedLogs != null
                && Instance.EnableDetailedLogs.Value;
        }
    }

    internal static void LogInfo(string message)
    {
        if (Instance != null)
        {
            Instance.Logger.LogInfo("[WorldWeave] " + message);
        }
        else
        {
            Debug.Log("[WorldWeave] " + message);
        }
    }

    internal static void LogWarning(string message)
    {
        if (Instance != null)
        {
            Instance.Logger.LogWarning("[WorldWeave] " + message);
        }
        else
        {
            Debug.LogWarning("[WorldWeave] " + message);
        }
    }

    internal static void LogDebug(string message)
    {
        if (!IsDetailedLoggingEnabled)
        {
            return;
        }

        if (Instance != null)
        {
            Instance.Logger.LogInfo("[WorldWeave][D] " + message);
        }
        else
        {
            Debug.Log("[WorldWeave][D] " + message);
        }
    }

    private void Awake()
    {
        Instance = this;

        MaxTemplatesPerMap = Config.Bind(
            "General",
            "MaxTemplatesPerMap",
            1,
            "1マップあたりに配置を試みるテンプレート数の上限");

        UsePersistentCache = Config.Bind(
            "Cache",
            "UsePersistentCache",
            true,
            "展開済みテンプレートを永続キャッシュとして保存して再利用する");

        PurgeOldCachesOnReload = Config.Bind(
            "Cache",
            "PurgeOldCachesOnReload",
            true,
            "Mod再読み込み時に古いキャッシュを削除する");

        CacheRetentionDays = Config.Bind(
            "Cache",
            "CacheRetentionDays",
            30,
            "この日数より古いキャッシュを削除する。0以下なら削除しない");

        LogCacheStatisticsOnReload = Config.Bind(
            "Cache",
            "LogCacheStatisticsOnReload",
            true,
            "Mod再読み込み時にキャッシュ統計をログ出力する");

        EnableDetailedLogs = Config.Bind(
            "Debug",
            "EnableDetailedLogs",
            false,
            "詳細ログを出す。失敗原因の追跡用。普段はfalse推奨");

        try
        {
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(TemplateSpawnPlugin).Assembly);
        }
        catch (Exception ex)
        {
            LogWarning("Harmony patch failed: " + ex);
            throw;
        }

        LogInfo(
            "loaded"
            + " version=" + PluginVersion
            + " MaxTemplatesPerMap=" + MaxTemplatesPerMap.Value
            + " UsePersistentCache=" + UsePersistentCache.Value
            + " PurgeOldCachesOnReload=" + PurgeOldCachesOnReload.Value
            + " CacheRetentionDays=" + CacheRetentionDays.Value
            + " LogCacheStatisticsOnReload=" + LogCacheStatisticsOnReload.Value
            + " DetailedLogs=" + EnableDetailedLogs.Value);
    }

    private void OnDestroy()
    {
        try
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }
        }
        catch (Exception ex)
        {
            LogWarning("Unpatch failed: " + ex);
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}