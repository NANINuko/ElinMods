using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KKElinWithTTS.Core;
using Newtonsoft.Json.Linq;

namespace VoiSonaTalkForElin
{
    public sealed class VoiSonaTalkEngine : ITtsEngine
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30.0)
        };

        public string EngineId
        {
            get { return "VoiSonaTalk"; }
        }

        public string EngineName
        {
            get { return "VoiSona Talk"; }
        }

        public bool PlaysDirectly
        {
            get { return false; }
        }

        public async Task<byte[]> SynthesizeAsync(
            string text,
            string speaker,
            TtsVoiceParams voiceParams = null,
            CancellationToken ct = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string baseUrl = GetBaseUrl();

            string voiceName = VoiSonaTalkPlugin.VoiceName.Value;
            string voiceVersion = VoiSonaTalkPlugin.VoiceVersion.Value;
            string language = VoiSonaTalkPlugin.Language.Value;

            JObject globalParameters = new JObject();

            if (voiceParams != null)
            {
                globalParameters["speed"] = voiceParams.Speed;
                globalParameters["pitch"] = voiceParams.Pitch;
                globalParameters["intonation"] = voiceParams.Intonation;
                globalParameters["volume"] = voiceParams.Volume;
            }

            JObject requestBody = new JObject();
            requestBody["text"] = text;
            requestBody["destination"] = "memory";
            requestBody["force_enqueue"] = true;
            requestBody["language"] = language;
            requestBody["voice_name"] = voiceName;
            requestBody["voice_version"] = voiceVersion;
            requestBody["global_parameters"] = globalParameters;

            string requestJson = requestBody.ToString(Newtonsoft.Json.Formatting.None);

            using (HttpRequestMessage request = CreateRequest(
                HttpMethod.Post,
                baseUrl + "/api/talk/v1/speech-syntheses"))
            {
                request.Content = new StringContent(
                    requestJson,
                    Encoding.UTF8,
                    "application/json");

                using (HttpResponseMessage response =
                    await Http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string responseText =
                        await response.Content.ReadAsStringAsync()
                            .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception(
                            "VoiSona Talk synthesis request failed: HTTP " +
                            (int)response.StatusCode + " " + responseText);
                    }

                    JObject result = JObject.Parse(responseText);
                    JToken uuidToken = result["uuid"];

                    if (uuidToken == null ||
                        string.IsNullOrWhiteSpace(uuidToken.ToString()))
                    {
                        throw new Exception(
                            "VoiSona Talk response did not contain uuid: " +
                            responseText);
                    }

                    string uuid = uuidToken.ToString();
                    return await WaitForWavAsync(
                        baseUrl,
                        uuid,
                        ct).ConfigureAwait(false);
                }
            }
        }

        public async Task<bool> CheckHealthAsync(
            CancellationToken ct = default(CancellationToken))
        {
            string baseUrl = GetBaseUrl();

            try
            {
                using (HttpRequestMessage request = CreateRequest(
                    HttpMethod.Get,
                    baseUrl + "/api/talk/v1/voices"))
                using (HttpResponseMessage response =
                    await Http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private static async Task<byte[]> WaitForWavAsync(
            string baseUrl,
            string uuid,
            CancellationToken ct)
        {
            int timeoutSeconds =
                Math.Max(1, VoiSonaTalkPlugin.TimeoutSeconds.Value);

            int pollInterval =
                Math.Max(100, VoiSonaTalkPlugin.PollIntervalMs.Value);

            DateTime deadline =
                DateTime.UtcNow.AddSeconds(timeoutSeconds);

            string statusUrl = baseUrl +
                "/api/talk/v1/speech-syntheses/" +
                Uri.EscapeDataString(uuid);

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                using (HttpRequestMessage request = CreateRequest(
                    HttpMethod.Get,
                    statusUrl))
                using (HttpResponseMessage response =
                    await Http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    string responseText =
                        await response.Content.ReadAsStringAsync()
                            .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception(
                            "VoiSona Talk status request failed: HTTP " +
                            (int)response.StatusCode + " " + responseText);
                    }

                    JObject status = JObject.Parse(responseText);
                    string state = status["state"] == null
                        ? ""
                        : status["state"].ToString();

                    if (string.Equals(
                            state,
                            "succeeded",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return await DownloadWavAsync(
                            baseUrl,
                            uuid,
                            ct).ConfigureAwait(false);
                    }

                    if (string.Equals(
                            state,
                            "failed",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception(
                            "VoiSona Talk synthesis job failed: " +
                            responseText);
                    }
                }

                await System.Threading.Tasks.Task.Delay(
                    pollInterval,
                    ct).ConfigureAwait(false);
            }

            throw new TimeoutException(
                "VoiSona Talk synthesis timed out after " +
                timeoutSeconds + " seconds.");
        }

        private static async Task<byte[]> DownloadWavAsync(
            string baseUrl,
            string uuid,
            CancellationToken ct)
        {
            string wavUrl = baseUrl +
                "/api/talk/v1/speech-syntheses/" +
                Uri.EscapeDataString(uuid) + "/wav";

            for (int attempt = 0; attempt < 5; attempt++)
            {
                using (HttpRequestMessage request = CreateRequest(
                    HttpMethod.Get,
                    wavUrl))
                using (HttpResponseMessage response =
                    await Http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] wav =
                            await response.Content.ReadAsByteArrayAsync()
                                .ConfigureAwait(false);

                        if (wav == null || wav.Length < 12 ||
                            wav[0] != (byte)'R' ||
                            wav[1] != (byte)'I' ||
                            wav[2] != (byte)'F' ||
                            wav[3] != (byte)'F' ||
                            wav[8] != (byte)'W' ||
                            wav[9] != (byte)'A' ||
                            wav[10] != (byte)'V' ||
                            wav[11] != (byte)'E')
                        {
                            throw new Exception(
                                "VoiSona Talk returned data that is not a WAV file.");
                        }

                        return wav;
                    }

                    string responseText =
                        await response.Content.ReadAsStringAsync()
                            .ConfigureAwait(false);

                    if ((response.StatusCode == HttpStatusCode.Conflict ||
                         response.StatusCode == HttpStatusCode.ServiceUnavailable) &&
                        attempt < 4)
                    {
                        await System.Threading.Tasks.Task.Delay(
                            200,
                            ct).ConfigureAwait(false); continue;
                    }

                    throw new Exception(
                        "VoiSona Talk WAV download failed: HTTP " +
                        (int)response.StatusCode + " " + responseText);
                }
            }

            throw new TimeoutException(
                "VoiSona Talk WAV download timed out.");
        }

        private static HttpRequestMessage CreateRequest(
            HttpMethod method,
            string url)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);

            string username = VoiSonaTalkPlugin.UserName.Value ?? "";
            string password = VoiSonaTalkPlugin.Password.Value ?? "";

            string credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(username + ":" + password));

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);

            return request;
        }

        private static string GetBaseUrl()
        {
            string url = VoiSonaTalkPlugin.ApiUrl.Value ?? "";
            return url.Trim().TrimEnd('/');
        }
    }
}