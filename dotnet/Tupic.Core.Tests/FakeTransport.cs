using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tupic.Core;

/// <summary>Records every request and answers from a script keyed by "METHOD url-prefix".</summary>
public sealed class FakeTransport : IHttpTransport
{
    public readonly List<HttpRequestSpec> Requests = new List<HttpRequestSpec>();
    readonly List<(string method, string urlPrefix, Func<HttpRequestSpec, HttpResult> answer)> _routes =
        new List<(string, string, Func<HttpRequestSpec, HttpResult>)>();

    public FakeTransport On(string method, string urlPrefix, int status, object json = null) =>
        On(method, urlPrefix, _ => Reply(status, json));

    public FakeTransport On(string method, string urlPrefix, Func<HttpRequestSpec, HttpResult> answer)
    {
        _routes.Add((method, urlPrefix, answer));
        return this;
    }

    public static HttpResult Reply(int status, object json = null) => new HttpResult
    {
        Status = status,
        Body = json == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(json as string ?? Json.Serialize(json)),
    };

    public Task<HttpResult> SendAsync(HttpRequestSpec request, CancellationToken ct = default)
    {
        Requests.Add(request);
        var route = _routes.LastOrDefault(r => r.method == request.Method && request.Url.StartsWith(r.urlPrefix, StringComparison.Ordinal));
        if (route.answer == null) return Task.FromResult(new HttpResult { Status = 599, Error = "no fake route for " + request.Method + " " + request.Url });
        return Task.FromResult(route.answer(request));
    }

    public static Dictionary<string, string> Form(HttpRequestSpec r) =>
        Encoding.UTF8.GetString(r.Body).Split('&').Select(p => p.Split('=')).ToDictionary(
            kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv.Length > 1 ? kv[1].Replace('+', ' ') : ""));

    public static Dictionary<string, object> JsonBody(HttpRequestSpec r) => Json.ParseObject(Encoding.UTF8.GetString(r.Body));
}
