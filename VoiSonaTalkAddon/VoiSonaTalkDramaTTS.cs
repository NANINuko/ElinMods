using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KKElinWithTTS.Core;

namespace VoiSonaTalkForElin
{
    internal static class DramaTtsContext
    {
        [ThreadStatic]
        internal static DramaEventTalk CurrentTalk;
    }

    internal sealed class DramaTtsOverrideEntry
    {
        public string Text;
        public bool NoSpeak;
    }

    internal static class DramaTtsOverrideStore
    {
        private static ConditionalWeakTable<DramaEventTalk, DramaTtsOverrideEntry> _table =
            new ConditionalWeakTable<DramaEventTalk, DramaTtsOverrideEntry>();

        private static readonly object _lock = new object();

        internal static void Clear()
        {
            lock (_lock)
            {
                _table = new ConditionalWeakTable<DramaEventTalk, DramaTtsOverrideEntry>();
            }
        }

        internal static void Set(DramaEventTalk talk, string rawText)
        {
            if (talk == null || string.IsNullOrWhiteSpace(rawText))
            {
                return;
            }

            string trimmed = rawText.TrimStart();

            DramaTtsOverrideEntry entry = new DramaTtsOverrideEntry();

            if (trimmed.StartsWith("#nospeak", StringComparison.OrdinalIgnoreCase))
            {
                entry.NoSpeak = true;
                entry.Text = string.Empty;
            }
            else
            {
                entry.NoSpeak = false;
                entry.Text = rawText;
            }

            lock (_lock)
            {
                _table.Remove(talk);
                _table.Add(talk, entry);
            }
        }

        internal static bool TryGet(
            DramaEventTalk talk,
            out DramaTtsOverrideEntry entry)
        {
            lock (_lock)
            {
                if (talk != null
                    && _table.TryGetValue(talk, out entry)
                    && entry != null)
                {
                    return true;
                }
            }

            entry = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(DramaManager), nameof(DramaManager.Load))]
    internal static class DramaManagerLoadPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            DramaTtsContext.CurrentTalk = null;
            DramaTtsOverrideStore.Clear();
            DramaTtsPlaybackStopper.StopCurrent();
        }
    }

    [HarmonyPatch(typeof(DramaManager), nameof(DramaManager.ParseLine))]
    internal static class DramaManagerParseLinePatch
    {
        [HarmonyPrefix]
        private static void Prefix(DramaManager __instance, out DramaEventTalk __state)
        {
            __state = __instance != null ? __instance.lastTalk : null;
        }

        [HarmonyPostfix]
        private static void Postfix(
            DramaManager __instance,
            Dictionary<string, string> item,
            DramaEventTalk __state)
        {
            if (__instance == null || item == null)
            {
                return;
            }

            DramaEventTalk current = __instance.lastTalk;
            if (current == null || ReferenceEquals(current, __state))
            {
                return;
            }

            if (item.TryGetValue("text_TTS", out string ttsText) &&
                !string.IsNullOrWhiteSpace(ttsText))
            {
                DramaTtsOverrideStore.Set(current, ttsText);
            }
        }
    }

    [HarmonyPatch(typeof(DramaEventTalk), nameof(DramaEventTalk.Play))]
    internal static class DramaEventTalkPlayPatch
    {
        [HarmonyPrefix]
        private static void Prefix(DramaEventTalk __instance)
        {
            DramaTtsContext.CurrentTalk = __instance;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            DramaTtsContext.CurrentTalk = null;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(DramaEventTalk), nameof(DramaEventTalk.ResetDialog))]
    internal static class DramaEventTalkResetDialogPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            DramaTtsPlaybackStopper.StopCurrent();
        }
    }

    [HarmonyPatch(typeof(DialogDrama), nameof(DialogDrama.Deactivate))]
    internal static class DialogDramaDeactivatePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            DramaTtsPlaybackStopper.StopCurrent();
        }
    }

    [HarmonyPatch(typeof(DramaManager), nameof(DramaManager.Hide))]
    internal static class DramaManagerHidePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            DramaTtsPlaybackStopper.StopCurrent();
        }
    }

    [HarmonyPatch(typeof(DramaSequence), nameof(DramaSequence.Exit))]
    internal static class DramaSequenceExitPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            DramaTtsPlaybackStopper.StopCurrent();
        }
    }

    [HarmonyPatch(typeof(DramaActor), nameof(DramaActor.Talk))]
    internal static class DramaActorTalkPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            DramaActor __instance,
            string text,
            List<DramaChoice> choices,
            bool center,
            bool unknown)
        {
            try
            {
                if (__instance == null || string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                // 新しい台詞が来たら前の読み上げを止める。
                DramaTtsPlaybackStopper.StopCurrent();

                string speechText =
                    BuildSpeechText(__instance, text, DramaTtsContext.CurrentTalk);

                if (string.IsNullOrWhiteSpace(speechText))
                {
                    return;
                }

                TtsRequest request = BuildRequest(__instance, speechText);
                if (request == null)
                {
                    return;
                }

                TtsPlaybackManager.Instance?.Speak(request);
            }
            catch (Exception ex)
            {
                if (VoiSonaTalkPlugin.Log != null)
                {
                    VoiSonaTalkPlugin.Log.LogError(
                        "[DramaTTS] Failed to speak drama line: " + ex);
                }
            }
        }

        private static string BuildSpeechText(
            DramaActor actor,
            string text,
            DramaEventTalk currentTalk)
        {
            if (actor == null || string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string source = text;

            if (currentTalk != null
                && DramaTtsOverrideStore.TryGet(currentTalk, out DramaTtsOverrideEntry entry)
                && entry != null)
            {
                if (entry.NoSpeak)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(entry.Text))
                {
                    source = entry.Text;
                }
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            if (IsNoSpeak(source))
            {
                return string.Empty;
            }

            try
            {
                Person owner = actor.owner;
                if (owner != null && !owner.HumanSpeak)
                {
                    if (!source.StartsWith("(", StringComparison.Ordinal))
                    {
                        source = "(" + source + ")";
                    }

                    source = source.Replace("。)", ")");
                }
            }
            catch
            {
            }

            try
            {
                source = actor.ConvertAdv(source);
            }
            catch
            {
            }

            try
            {
                source = GameLang.ConvertDrama(
                    source,
                    actor.owner != null ? actor.owner.chara : null);
            }
            catch
            {
            }

            Chara chara = actor.owner != null ? actor.owner.chara : null;
            if (chara != null)
            {
                try
                {
                    if (chara.HasElement(1232))
                    {
                        BackerContent.GakiConvert(ref source, "babu");
                    }
                    else
                    {
                        string charaId = chara.id;

                        if (string.Equals(
                                charaId,
                                "adv_gaki",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            BackerContent.GakiConvert(ref source);
                        }
                        else if (string.Equals(
                                     charaId,
                                     "corgon",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            BackerContent.GakiConvert(ref source, "mokyu");
                        }
                    }
                }
                catch
                {
                }
            }

            source = source
                .Replace("((", "(")
                .Replace("))", ")")
                .Replace("（（", "（")
                .Replace("））", "）");

            return TtsTextSanitizer.Sanitize(source);
        }

        private static bool IsNoSpeak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return text.TrimStart()
                .StartsWith("#nospeak", StringComparison.OrdinalIgnoreCase);
        }

        private static TtsRequest BuildRequest(DramaActor actor, string speechText)
        {
            if (actor == null || string.IsNullOrWhiteSpace(speechText))
            {
                return null;
            }

            Chara chara = actor.owner != null ? actor.owner.chara : null;

            if (TryGetSetting(actor.id, out CharaVoiceSetting actorSetting))
            {
                if (actorSetting == null || actorSetting.IsNoneEngine)
                {
                    return null;
                }

                return BuildRequestFromSetting(
                    actor,
                    chara,
                    actorSetting,
                    speechText);
            }

            if (chara != null &&
                TryGetSetting(chara.uid.ToString(), out CharaVoiceSetting charaSetting))
            {
                if (charaSetting == null || charaSetting.IsNoneEngine)
                {
                    return null;
                }

                return BuildRequestFromSetting(
                    actor,
                    chara,
                    charaSetting,
                    speechText);
            }

            // 声が割り当てられていないなら無音。
            return null;
        }

        private static bool TryGetSetting(
            string id,
            out CharaVoiceSetting setting)
        {
            setting = null;

            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            IReadOnlyDictionary<string, CharaVoiceSetting> settings =
                CharaVoiceManager.Settings;

            if (settings != null &&
                settings.TryGetValue(id, out setting) &&
                setting != null)
            {
                return true;
            }

            setting = null;
            return false;
        }

        private static TtsRequest BuildRequestFromSetting(
            DramaActor actor,
            Chara chara,
            CharaVoiceSetting setting,
            string speechText)
        {
            return new TtsRequest
            {
                RawText = speechText,
                CleanText = speechText,
                Engine = setting.Engine,
                Speaker = string.IsNullOrWhiteSpace(setting.Speaker)
                    ? TtsSpeakerResolver.DefaultSpeaker
                    : setting.Speaker.Trim(),
                SpeakerCard = chara,
                PortraitId = actor.id,
                VoiceParams = new TtsVoiceParams
                {
                    Speed = setting.Speed,
                    Pitch = setting.Pitch,
                    Intonation = setting.Intonation,
                    Volume = setting.Volume
                }
            };
        }
    }
}