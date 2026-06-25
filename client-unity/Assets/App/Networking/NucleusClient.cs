using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Guidance.Runtime
{
    /// <summary>
    /// One Nucleus server as reported by the server's GET /omni/nucleus endpoint.
    /// Never carries credentials — only the key, label, and base URL.
    /// </summary>
    [Serializable]
    public sealed class NucleusEndpointDto
    {
        public string key;
        public string name;
        public string server;
        public bool active;
    }

    [Serializable]
    public sealed class NucleusListResponse
    {
        public string active;
        public NucleusEndpointDto[] endpoints;
    }

    /// <summary>
    /// Thin HTTP client for the server's Nucleus selection endpoints
    /// (GET /omni/nucleus, POST /omni/nucleus/active). Follows the same
    /// UnityWebRequest + completed-callback pattern as the HTTP bridge
    /// transport, so no MonoBehaviour/coroutine is required. Callbacks run on
    /// Unity's main thread.
    /// </summary>
    public static class NucleusClient
    {
        public static void FetchList(string baseUrl, Action<NucleusListResponse> onSuccess, Action<string> onError)
        {
            var url = baseUrl.TrimEnd('/') + "/omni/nucleus";
            var request = UnityWebRequest.Get(url);
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    request.Dispose();
                    return;
                }

                NucleusListResponse parsed = null;
                try { parsed = JsonUtility.FromJson<NucleusListResponse>(request.downloadHandler.text); }
                catch (Exception ex) { onError?.Invoke($"parse error: {ex.Message}"); request.Dispose(); return; }

                request.Dispose();
                if (parsed == null) { onError?.Invoke("empty/invalid nucleus list"); return; }
                onSuccess?.Invoke(parsed);
            };
        }

        public static void SetActive(string baseUrl, string key, Action<string> onSuccess, Action<string> onError)
        {
            var url = baseUrl.TrimEnd('/') + "/omni/nucleus/active";
            var json = JsonUtility.ToJson(new NucleusSelection { key = key });

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    request.Dispose();
                    return;
                }

                var text = request.downloadHandler.text;
                request.Dispose();
                onSuccess?.Invoke(text);
            };
        }

        [Serializable]
        private sealed class NucleusSelection
        {
            public string key;
        }
    }
}
