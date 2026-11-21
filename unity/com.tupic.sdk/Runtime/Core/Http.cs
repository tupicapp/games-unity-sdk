// Tupic SDK core: HTTP is an interface, so each engine plugs in its own transport (UnityWebRequest on Unity, which
// also works in WebGL; HttpClient in plain .NET; HTTPRequest in Godot; FHttpModule in Unreal).
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tupic.Core
{
    public sealed class HttpRequestSpec
    {
        public string Method = "GET";
        public string Url;
        public Dictionary<string, string> Headers = new Dictionary<string, string>();
        public byte[] Body;
        public string ContentType;

        public static HttpRequestSpec JsonPost(string url, object body) => new HttpRequestSpec
        {
            Method = "POST", Url = url, Body = Encoding.UTF8.GetBytes(Json.Serialize(body)), ContentType = "application/json",
        };
    }

    public sealed class HttpResult
    {
        public int Status;
        public byte[] Body = Array.Empty<byte>();
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Error;
        public bool Ok => Status >= 200 && Status < 300;
        public string Text => Encoding.UTF8.GetString(Body ?? Array.Empty<byte>());
        public Dictionary<string, object> JsonObject() => Json.ParseObject(Text);
    }

    public interface IHttpTransport
    {
        Task<HttpResult> SendAsync(HttpRequestSpec request, CancellationToken ct = default);
    }

    /// <summary>A transport on System.Net.Http.HttpClient (desktop .NET, Unity standalone, tests).</summary>
    public sealed class HttpClientTransport : IHttpTransport
    {
        readonly HttpClient _client;
        public HttpClientTransport(HttpClient client = null) { _client = client ?? new HttpClient(); }

        public async Task<HttpResult> SendAsync(HttpRequestSpec spec, CancellationToken ct = default)
        {
            using var msg = new HttpRequestMessage(new HttpMethod(spec.Method), spec.Url);
            if (spec.Body != null)
            {
                msg.Content = new ByteArrayContent(spec.Body);
                if (spec.ContentType != null) msg.Content.Headers.TryAddWithoutValidation("Content-Type", spec.ContentType);
            }
            foreach (var h in spec.Headers) msg.Headers.TryAddWithoutValidation(h.Key, h.Value);
            try
            {
                using var resp = await _client.SendAsync(msg, ct).ConfigureAwait(false);
                var result = new HttpResult { Status = (int)resp.StatusCode, Body = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false) };
                foreach (var h in resp.Headers) result.Headers[h.Key] = string.Join(",", h.Value);
                foreach (var h in resp.Content.Headers) result.Headers[h.Key] = string.Join(",", h.Value);
                return result;
            }
            catch (HttpRequestException e)
            {
                return new HttpResult { Status = 0, Error = e.Message };
            }
        }
    }

    public class TupicApiException : Exception
    {
        public int Status { get; }
        public TupicApiException(string message, int status) : base(message) { Status = status; }

        internal static TupicApiException From(string what, HttpResult r)
        {
            var detail = r.Error;
            if (detail == null)
            {
                try
                {
                    var o = r.JsonObject();
                    detail = o.Str("error") ?? o.Str("detail") ?? o.Str("message");
                }
                catch (FormatException) { detail = r.Text.Length > 200 ? r.Text.Substring(0, 200) : r.Text; }
            }
            return new TupicApiException($"{what} failed ({r.Status}): {TupicLog.Redact(detail)}", r.Status);
        }
    }

    /// <summary>Logging hook. Every message passes through Redact, so a token never reaches a log.</summary>
    public static class TupicLog
    {
        public static Action<string> Sink = _ => { };

        public static void Info(string message) => Sink(Redact(message));

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)bearer\s+[A-Za-z0-9\-_\.=]+", "Bearer <redacted>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"eyJ[A-Za-z0-9\-_]{8,}\.[A-Za-z0-9\-_]{8,}\.[A-Za-z0-9\-_]+", "<jwt>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?i)(client_secret|password|refresh_token|access_token|code_verifier)=([^&\s""]+)", "$1=<redacted>");
            return text;
        }
    }
}
