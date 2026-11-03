using System;
using System.Reflection;
using System.Threading;
using KKElinWithTTS.Core;
using UnityEngine;

namespace VoiSonaTalkForElin
{
    internal static class DramaTtsPlaybackStopper
    {
        private static readonly BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo CurrentCtsField =
            typeof(TtsPlaybackManager).GetField("_currentCts", Flags);

        private static readonly FieldInfo AudioSourceField =
            typeof(TtsPlaybackManager).GetField("_audioSource", Flags);

        private static readonly FieldInfo WavQueueField =
            typeof(TtsPlaybackManager).GetField("_wavQueue", Flags);

        private static readonly FieldInfo ClipQueueField =
            typeof(TtsPlaybackManager).GetField("_clipQueue", Flags);

        private static readonly FieldInfo WasPlayingField =
            typeof(TtsPlaybackManager).GetField("_wasPlaying", Flags);

        private static readonly FieldInfo IntervalTimerField =
            typeof(TtsPlaybackManager).GetField("_intervalTimer", Flags);

        private static readonly FieldInfo CurrentClipVolumeScaleField =
            typeof(TtsPlaybackManager).GetField("_currentClipVolumeScale", Flags);

        private static readonly FieldInfo PlaybackGenerationField =
            typeof(TtsPlaybackManager).GetField("_playbackGeneration", Flags);

        internal static void StopCurrent()
        {
            try
            {
                TtsPlaybackManager instance = TtsPlaybackManager.Instance;
                if (instance == null)
                {
                    return;
                }

                CancellationTokenSource cts =
                    CurrentCtsField != null
                        ? CurrentCtsField.GetValue(instance) as CancellationTokenSource
                        : null;

                if (cts != null)
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch
                    {
                    }
                }

                if (CurrentCtsField != null)
                {
                    CurrentCtsField.SetValue(instance, null);
                }

                ClearConcurrentQueue(
                    WavQueueField != null ? WavQueueField.GetValue(instance) : null);

                ClearQueue(
                    ClipQueueField != null ? ClipQueueField.GetValue(instance) : null);

                AudioSource audioSource =
                    AudioSourceField != null
                        ? AudioSourceField.GetValue(instance) as AudioSource
                        : null;

                if (audioSource != null)
                {
                    audioSource.Stop();
                    audioSource.clip = null;
                }

                if (WasPlayingField != null)
                {
                    WasPlayingField.SetValue(instance, false);
                }

                if (IntervalTimerField != null)
                {
                    IntervalTimerField.SetValue(instance, 0f);
                }

                if (CurrentClipVolumeScaleField != null)
                {
                    CurrentClipVolumeScaleField.SetValue(instance, 1f);
                }

                if (PlaybackGenerationField != null)
                {
                    int current = 0;
                    object value = PlaybackGenerationField.GetValue(instance);
                    if (value is int i)
                    {
                        current = i;
                    }

                    PlaybackGenerationField.SetValue(instance, current + 1);
                }
            }
            catch (Exception ex)
            {
                if (VoiSonaTalkPlugin.Log != null)
                {
                    VoiSonaTalkPlugin.Log.LogWarning(
                        "[DramaTTS] Failed to stop playback: " + ex.Message);
                }
            }
        }

        private static void ClearQueue(object queueObj)
        {
            if (queueObj == null)
            {
                return;
            }

            try
            {
                MethodInfo clear = queueObj.GetType().GetMethod("Clear");
                if (clear != null)
                {
                    clear.Invoke(queueObj, null);
                }
            }
            catch
            {
            }
        }

        private static void ClearConcurrentQueue(object queueObj)
        {
            if (queueObj == null)
            {
                return;
            }

            try
            {
                MethodInfo tryDequeue = queueObj.GetType().GetMethod("TryDequeue");
                if (tryDequeue == null)
                {
                    return;
                }

                object[] args = new object[] { null };

                while (true)
                {
                    bool dequeued = (bool)tryDequeue.Invoke(queueObj, args);
                    if (!dequeued)
                    {
                        break;
                    }
                }
            }
            catch
            {
            }
        }
    }
}
