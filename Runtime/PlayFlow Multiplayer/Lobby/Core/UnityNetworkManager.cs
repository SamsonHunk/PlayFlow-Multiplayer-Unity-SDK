using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace PlayFlow
{
    public class UnityNetworkManager : MonoBehaviour, INetworkManager
    {
        private PlayFlowSettings _settings;

        public void Initialize(PlayFlowSettings settings)
        {
            _settings = settings;
        }

        private string GetErrorMessage(UnityWebRequest webRequest)
        {
            if (!string.IsNullOrEmpty(webRequest.downloadHandler?.text))
            {
                return $"{webRequest.error} - {webRequest.downloadHandler.text}";
            }

            return webRequest.error ?? "Unknown error";
        }

        private void ApplyCommonHeaders(UnityWebRequest webRequest, string apiKey, Dictionary<string, string> extraHeaders)
        {
            webRequest.SetRequestHeader("api-key", apiKey);
            if (extraHeaders != null)
            {
                foreach (var kvp in extraHeaders)
                {
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    webRequest.SetRequestHeader(kvp.Key, kvp.Value ?? string.Empty);
                }
            }
        }

        // INetworkManager implementation
        public IEnumerator Get(string url, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null)
        {
            using (var webRequest = UnityWebRequest.Get(url))
            {
                webRequest.timeout = (int)_settings.requestTimeout;
                ApplyCommonHeaders(webRequest, apiKey, extraHeaders);

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(webRequest.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(GetErrorMessage(webRequest));
                }
            }
        }

        public IEnumerator Post(string url, string json, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null)
        {
            using (var webRequest = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json ?? string.Empty);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                ApplyCommonHeaders(webRequest, apiKey, extraHeaders);
                webRequest.timeout = (int)_settings.requestTimeout;

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(webRequest.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(GetErrorMessage(webRequest));
                }
            }
        }

        public IEnumerator Put(string url, string json, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null)
        {
            using (var webRequest = UnityWebRequest.Put(url, json ?? string.Empty))
            {
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                ApplyCommonHeaders(webRequest, apiKey, extraHeaders);
                webRequest.timeout = (int)_settings.requestTimeout;

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(webRequest.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(GetErrorMessage(webRequest));
                }
            }
        }

        public IEnumerator Delete(string url, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null)
        {
            using (var webRequest = UnityWebRequest.Delete(url))
            {
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                ApplyCommonHeaders(webRequest, apiKey, extraHeaders);
                webRequest.timeout = (int)_settings.requestTimeout;

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(webRequest.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(GetErrorMessage(webRequest));
                }
            }
        }

        // V3: PATCH is used for /me (update player state) and /me/settings (update lobby settings).
        public IEnumerator Patch(string url, string json, string apiKey, System.Action<string> onSuccess, System.Action<string> onError, Dictionary<string, string> extraHeaders = null)
        {
            using (var webRequest = new UnityWebRequest(url, "PATCH"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json ?? string.Empty);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                ApplyCommonHeaders(webRequest, apiKey, extraHeaders);
                webRequest.timeout = (int)_settings.requestTimeout;

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(webRequest.downloadHandler.text);
                }
                else
                {
                    onError?.Invoke(GetErrorMessage(webRequest));
                }
            }
        }
    }
}
