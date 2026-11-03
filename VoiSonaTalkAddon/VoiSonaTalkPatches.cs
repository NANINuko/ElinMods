
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using HarmonyLib;

using KKElinWithTTS.Core;

namespace VoiSonaTalkForElin
{
    internal static class VoiSonaUiTypeResolver
    {
        internal static Type FindLayoutTtsSettings()
        {
            const string typeName =
                "KKElinWithTTS.UI.LayoutTtsSettings";

            Type type = AccessTools.TypeByName(typeName);

            if (type != null)
            {
                return type;
            }

            try
            {
                Assembly assembly =
                    Assembly.Load("KKElinWithTTS.UI");

                type = assembly.GetType(typeName, false);

                if (type != null)
                {
                    return type;
                }
            }
            catch (Exception ex)
            {
                VoiSonaTalkPlugin.Log.LogWarning(
                    "Could not load KKElinWithTTS.UI: "
                    + ex.Message);
            }

            foreach (Assembly assembly in
                AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false);

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }

    [HarmonyPatch]
    internal static class VoiSonaRegistryPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(TtsEngineRegistry),
                "Reinitialize");
        }

        private static void Postfix()
        {
            // Reinitialize clears the registry.
            // Register VoiSona again after it finishes.
            VoiSonaTalkPlugin.RegisterEngine();
        }
    }

    [HarmonyPatch]
    internal static class VoiSonaSpeakerListPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(TtsSpeakerProvider),
                "GetSpeakersAsync",
                new Type[]
                {
                    typeof(string),
                    typeof(bool)
                });
        }

        private static bool Prefix(
            string engineAlias,
            bool forceRefresh,
            ref Task<List<SpeakerInfo>> __result)
        {
            if (!IsVoiSona(engineAlias))
            {
                return true;
            }

            List<SpeakerInfo> speakers =
                new List<SpeakerInfo>();

            speakers.Add(
                new SpeakerInfo
                {
                    Name = "雨衣",
                    SpeakerUuid =
                        VoiSonaTalkPlugin.VoiceName.Value,

                    Styles = new List<SpeakerStyleInfo>
                    {
                        new SpeakerStyleInfo
                        {
                            Name = "標準",
                            Id = 0
                        }
                    }
                });

            TaskCompletionSource<List<SpeakerInfo>> tcs =
                new TaskCompletionSource<List<SpeakerInfo>>();

            tcs.SetResult(speakers);

            __result = tcs.Task;

            return false;
        }

        private static bool IsVoiSona(string engine)
        {
            return string.Equals(
                       engine,
                       "VoiSonaTalk",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   string.Equals(
                       engine,
                       "VoiSona",
                       StringComparison.OrdinalIgnoreCase);
        }
    }

    // This class intentionally has no HarmonyPatch attribute.
    // It is applied manually so a missing UI type cannot cause
    // PatchAll() to fail.
    internal static class VoiSonaNormalizeEnginePatch
    {
        internal static void Apply(Harmony harmony)
        {
            Type type =
                VoiSonaUiTypeResolver.FindLayoutTtsSettings();

            if (type == null)
            {
                VoiSonaTalkPlugin.Log.LogWarning(
                    "LayoutTtsSettings was not found. "
                    + "NormalizeEngineId patch skipped.");

                return;
            }

            MethodInfo target = AccessTools.Method(
                type,
                "NormalizeEngineId",
                new Type[]
                {
                    typeof(string)
                });

            if (target == null)
            {
                VoiSonaTalkPlugin.Log.LogWarning(
                    "NormalizeEngineId was not found. "
                    + "NormalizeEngineId patch skipped.");

                return;
            }

            MethodInfo postfix = AccessTools.Method(
                typeof(VoiSonaNormalizeEnginePatch),
                "Postfix");

            if (postfix == null)
            {
                VoiSonaTalkPlugin.Log.LogWarning(
                    "NormalizeEngineId Postfix was not found.");

                return;
            }

            harmony.Patch(
                target,
                postfix: new HarmonyMethod(postfix));

            VoiSonaTalkPlugin.Log.LogInfo(
                "NormalizeEngineId patch applied.");
        }

        private static void Postfix(
            string engine,
            ref string __result)
        {
            if (string.Equals(
                    engine,
                    "VoiSona",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    engine,
                    "VoiSonaTalk",
                    StringComparison.OrdinalIgnoreCase))
            {
                __result = "VoiSonaTalk";
            }
        }
    }

    internal static class VoiSonaSettingsUi
    {
        internal static void AddEngineChoice()
        {
            Type type =
                VoiSonaUiTypeResolver.FindLayoutTtsSettings();

            if (type == null)
            {
                VoiSonaTalkPlugin.Log.LogWarning(
                    "Could not find LayoutTtsSettings. "
                    + "The engine can still be registered, "
                    + "but the UI choice could not be added.");

                return;
            }

            ReplaceArrayField(
                type,
                "AvailableEngines",
                new string[]
                {
                    "None",
                    "Voicevox",
                    "AivisSpeech",
                    "Sharevox",
                    "Lmroid",
                    "Edge",
                    "OpenAI",
                    "Player2",
                    "Windows",
                    "Bouyomi",
                    "VoiSonaTalk"
                });

            ReplaceArrayField(
                type,
                "EngineDisplayNames",
                new string[]
                {
                    "None",
                    "VOICEVOX",
                    "AivisSpeech",
                    "SHAREVOX",
                    "LMROID",
                    "Edge TTS",
                    "OpenAI / Custom",
                    "Player2",
                    "Windows",
                    "棒読みちゃん",
                    "VoiSona Talk (雨衣)"
                });
        }

        private static void ReplaceArrayField(
            Type type,
            string fieldName,
            string[] values)
        {
            FieldInfo field = AccessTools.Field(
                type,
                fieldName);

            if (field == null)
            {
                VoiSonaTalkPlugin.Log.LogWarning(
                    "Could not find UI field: "
                    + fieldName);

                return;
            }

            try
            {
                field.SetValue(null, values);

                VoiSonaTalkPlugin.Log.LogInfo(
                    "Updated UI field: " + fieldName);
            }
            catch (Exception ex)
            {
                VoiSonaTalkPlugin.Log.LogWarning(
                    "Could not replace "
                    + fieldName
                    + ": "
                    + ex);
            }
        }
    }
}
