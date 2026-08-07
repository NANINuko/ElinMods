using System;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

[HarmonyPatch(typeof(Zone), nameof(Zone.Generate))]
internal static class Patch_Zone_Generate_WorldWeave
{
    [HarmonyPrefix]
    private static void Prefix(Zone __instance)
    {
        WorldWeaveState.MarkPending(__instance);
        WorldWeaveState.BeginZoneGeneration(__instance);
    }

    [HarmonyFinalizer]
    private static Exception Finalizer(Zone __instance, Exception __exception)
    {
        WorldWeaveState.EndZoneGeneration(__instance);
        return __exception;
    }
}

[HarmonyPatch(typeof(Zone), nameof(Zone.AddCard), new Type[] { typeof(Card), typeof(int), typeof(int) })]
internal static class Patch_Zone_AddCard_WorldWeave
{
    [HarmonyPostfix]
    private static void Postfix(Zone __instance, Card t)
    {
        WorldWeaveState.MarkGeneratedCard(__instance, t);
    }
}

[HarmonyPatch(typeof(Card), nameof(Card.Destroy))]
internal static class Patch_Card_Destroy_WorldWeave
{
    [HarmonyPrefix]
    private static bool Prefix(Card __instance)
    {
        return WorldWeaveState.ShouldAllowDestroy(__instance);
    }
}

[HarmonyPatch(typeof(Zone_Field), nameof(Zone_Field.OnGenerateMap))]
internal static class Patch_Zone_Field_OnGenerateMap_WorldWeave
{
    [HarmonyPostfix]
    private static void Postfix(Zone_Field __instance)
    {
        WorldWeaveLogic.TrySpawnForZone(__instance);
    }
}

[HarmonyPatch(typeof(Zone), nameof(Zone.OnGenerateMap))]
internal static class Patch_Zone_OnGenerateMap_WorldWeave
{
    [HarmonyPostfix]
    private static void Postfix(Zone __instance)
    {
        if (__instance is Zone_Wilds)
        {
            WorldWeaveLogic.TrySpawnForZone(__instance);
        }
    }
}

[HarmonyPatch(typeof(TraitBaseContainer), nameof(TraitBaseContainer.Prespawn))]
internal static class Patch_TraitBaseContainer_Prespawn
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !WorldWeaveState.LoadingTemplate;
    }
}

[HarmonyPatch(typeof(ModManager), nameof(ModManager.RefreshMods))]
internal static class Patch_ModManager_RefreshMods_WorldWeave
{
    [HarmonyPrefix]
    private static void Prefix(ref Action onComplete)
    {
        Action original = onComplete;
        onComplete = delegate
        {
            try
            {
                original?.Invoke();
            }
            finally
            {
                try
                {
                    WorldWeaveLogic.Reload();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WorldWeave] Auto reload failed: " + ex);
                }
            }
        };
    }
}