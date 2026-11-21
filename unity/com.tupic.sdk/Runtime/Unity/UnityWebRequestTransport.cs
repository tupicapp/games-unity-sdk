// The SDK's HTTP transport on UnityWebRequest (works on every Unity target, including WebGL, where System.Net.Http
// does not). Completion is signalled by the request's own async operation, on Unity's main thread.
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tupic.Core;
using UnityEngine.Networking;

namespace Tupic.Unity
{
    public sealed class UnityWebRequestTransport : IHttpTransport
    {
        public Task<HttpResult> SendAsync(HttpRequestSpec request, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<HttpResult>();
            var req = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                disposeDownloadHandlerOnDispose = true,
                disposeUploadHandlerOnDispose = true,
            };
            if (request.Body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(request.Body);
                if (!string.IsNullOrEmpty(request.ContentType)) req.SetRequestHeader("Content-Type", request.ContentType);
            }
            foreach (var h in request.Headers) req.SetRequestHeader(h.Key, h.Value);
            var reg = ct.Register(() => { req.Abort(); tcs.TrySetCanceled(); });
            req.SendWebRequest().completed += _ =>
            {
                reg.Dispose();
                var result = new HttpResult
                {
                    Status = (int)req.responseCode,
                    Body = req.downloadHandler?.data ?? new byte[0],
                    Error = req.result == UnityWebRequest.Result.Success ? null : req.error,
                };
                var headers = req.GetResponseHeaders();
                if (headers != null) foreach (var kv in headers) result.Headers[kv.Key] = kv.Value;
                req.Dispose();
                tcs.TrySetResult(result);
            };
            return tcs.Task;
        }
    }
}
