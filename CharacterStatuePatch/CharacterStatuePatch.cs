using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

[BepInPlugin("elin.mod.characterstatuepatch", "Character Statue Patch", "1.0.1")]
public class CharacterStatuePatchPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        Logger.LogInfo("[Character Statue Patch] Loaded.");

        var h = new Harmony("elin.mod.characterstatuepatch");
        h.PatchAll(typeof(Patch_TraitCrafter_Craft));
        h.PatchAll(typeof(Patch_TraitStatue_GetMatColor));

        Logger.LogInfo("[Character Statue Patch] Harmony patches applied.");
    }

    private static CardRow PickWeighted(List<CardRow> list)
    {
        if (list == null || list.Count == 0)
            return null;

        int bonus = Mathf.Clamp(EClass.pc.Evalue(258) / 2, 1, 999999);
        int total = 0;
        int[] weights = new int[list.Count];

        for (int i = 0; i < list.Count; i++)
        {
            CardRow a = list[i];
            if (a == null)
            {
                weights[i] = 0;
                continue;
            }

            int w = Mathf.Max(50 - a.LV, Mathf.Clamp(bonus, 1, a.LV * 2));
            if (w < 1)
                w = 1;

            weights[i] = w;
            total += w;
        }

        if (total <= 0)
            return list[0];

        int roll = EClass.rnd(total);
        for (int i = 0; i < list.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
                return list[i];
        }

        return list[0];
    }

    [HarmonyPatch(typeof(TraitCrafter), nameof(TraitCrafter.Craft))]
    private static class Patch_TraitCrafter_Craft
    {
        static bool Prefix(TraitCrafter __instance, AI_UseCrafter ai, ref Thing __result)
        {
            if (!(__instance is TraitSculpture))
                return true;

            SourceRecipe.Row source = __instance.GetSource(ai);
            if (source == null)
            {
                __result = null;
                return false;
            }

            if (source.thing != "figure2")
                return true;

            if (ai == null || ai.ings == null || ai.ings.Count == 0)
            {
                __result = null;
                return false;
            }

            Thing materialSource = ai.ings[0];
            Thing t = ThingGen.Create("figure2");

            var candidates = new List<CardRow>();
            foreach (var row in EClass.sources.charas.rows)
            {
                if (row == null)
                    continue;

                if (row.HasTag(CTAG.noRandomProduct))
                    continue;

                candidates.Add(row);
            }

            candidates.Add(EClass.sources.cards.map["putty"]);
            candidates.Add(EClass.sources.cards.map["snail"]);

            CardRow cardRow = PickWeighted(candidates);
            if (cardRow == null)
            {
                __result = null;
                return false;
            }

            t.c_idRefCard = cardRow.id;
            t.ChangeMaterial(materialSource.material);

            t = CraftUtil.MixIngredients(t, ai.ings, CraftUtil.MixType.General, 999, EClass.pc).Thing;
            if (t == null)
            {
                __result = null;
                return false;
            }

            t.SetTier(0, false);
            t.SetEncLv(0);

            if (t.HasElement(1229) || t.HasElement(704) || t.HasElement(703))
                t.SetNum(1);
            else
                t.SetNum(source.num.Calc());

            __result = t;
            return false;
        }
    }

    [HarmonyPatch(typeof(TraitStatue), nameof(TraitStatue.GetMatColor))]
    private static class Patch_TraitStatue_GetMatColor
    {
        static bool Prefix(TraitStatue __instance, ref int __result)
        {
            if (__instance != null &&
                __instance.owner != null &&
                __instance.owner.id == "figure2")
            {
                __result = -1;
                return false;
            }

            return true;
        }
    }
}