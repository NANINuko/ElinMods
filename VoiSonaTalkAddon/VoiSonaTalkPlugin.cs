
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKElinWithTTS.Core;

namespace VoiSonaTalkForElin
{
    [BepInPlugin(
        "naninuko.elin.voisonatalk",
        "VoiSona Talk for Elin",
        "1.0.0")]
    [BepInDependency("net.KK.ElinWithTTS")]
    public class VoiSonaTalkPlugin : BaseUnityPlugin
    {
        internal static VoiSonaTalkPlugin Instance { get; private set; }

        internal static ConfigEntry<string> ApiUrl;
        internal static ConfigEntry<string> UserName;
        internal static ConfigEntry<string> Password;
        internal static ConfigEntry<string> VoiceName;
        internal static ConfigEntry<string> VoiceVersion;
        internal static ConfigEntry<string> Language;
        internal static ConfigEntry<int> PollIntervalMs;
        internal static ConfigEntry<int> TimeoutSeconds;
        internal static ManualLogSource Log;


        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;

            ApiUrl = Config.Bind(
                "VoiSona Talk",
                "ApiUrl",
                "http://127.0.0.1:32766",
                "VoiSona Talk API base URL.");

            UserName = Config.Bind(
                "VoiSona Talk",
                "UserName",
                "",
                "VoiSona Talk API Basic Auth username.");

            Password = Config.Bind(
                "VoiSona Talk",
                "Password",
                "",
                "VoiSona Talk API Basic Auth password.");

            VoiceName = Config.Bind(
                "Voice",
                "VoiceName",
                "ui_ja_JP",
                "VoiSona Talk voice_name.");

            VoiceVersion = Config.Bind(
                "Voice",
                "VoiceVersion",
                "2.0.0",
                "VoiSona Talk voice_version.");

            Language = Config.Bind(
                "Voice",
                "Language",
                "ja_JP",
                "Synthesis language.");

            PollIntervalMs = Config.Bind(
                "Advanced",
                "PollIntervalMs",
                250,
                "Milliseconds between synthesis status checks.");

            TimeoutSeconds = Config.Bind(
                "Advanced",
                "TimeoutSeconds",
                120,
                "Maximum time to wait for a synthesis job.");
            Log = Logger;

            _harmony = new Harmony("naninuko.elin.voisonatalk");

            try
            {
                _harmony.PatchAll(typeof(VoiSonaTalkPlugin).Assembly);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to apply patches: " + ex);
            }
            VoiSonaSettingsUi.AddEngineChoice();

            RegisterEngine();

            Logger.LogInfo("VoiSona Talk for Elin initialized.");
        }

        internal static void RegisterEngine()
        {
            try
            {
                TtsEngineRegistry.Register(
                    "VoiSonaTalk",
                    new VoiSonaTalkEngine());

                // Alias for manually entered engine IDs.
                TtsEngineRegistry.Register(
                    "VoiSona",
                    new VoiSonaTalkEngine());
            }
            catch (Exception ex)
            {
                if (Instance != null)
                {
                    Instance.Logger.LogError(
                        "Failed to register VoiSona Talk engine: " + ex);
                }
            }
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }
    }
}
