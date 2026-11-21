// Tupic SDK core: one entry point per game. Every service call goes out with the signed-in user's token; a game
// never carries a service secret, so anything a game must not decide (rewards, payouts) is simply not reachable.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tupic.Core
{
    public sealed class TupicClient
    {
        public TupicEnvironment Environment { get; }
        public string GameId { get; }
        public TupicAuth Auth { get; }
        public TupicGames Games { get; }
        public TupicAnalytics Analytics { get; }
        public TupicInsights Insights { get; }
        public TupicChain Chain { get; }
        public TupicAssets Assets { get; }
        public TupicLive Live { get; }
        public TupicModels Models { get; }

        public TupicClient(TupicEnvironment env, IHttpTransport transport, string gameId, ITokenStore store = null)
        {
            Environment = env ?? throw new ArgumentNullException(nameof(env));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (string.IsNullOrWhiteSpace(gameId)) throw new ArgumentException("gameId is required.", nameof(gameId));
            env.Validate();
            GameId = gameId;
            Auth = new TupicAuth(env, transport, store);
            var api = new TupicApi(transport, Auth);
            Analytics = new TupicAnalytics(env, api, gameId);
            Games = new TupicGames(env, api, gameId, Analytics);
            Insights = new TupicInsights(env, api, gameId);
            Chain = new TupicChain(env, api);
            Assets = new TupicAssets(env, api, transport);
            Live = new TupicLive(env, api, gameId);
            Models = new TupicModels(env, api, gameId);
        }
    }

    /// <summary>Shared plumbing: bearer header, JSON bodies, errors as TupicApiException.</summary>
    internal sealed class TupicApi
    {
        readonly IHttpTransport _http;
        readonly TupicAuth _auth;

        public TupicApi(IHttpTransport http, TupicAuth auth) { _http = http; _auth = auth; }

        public IHttpTransport Http => _http;

        /// <summary>Sends with the user's token. optionalAuth: a guest (not signed in) sends without one.</summary>
        public async Task<HttpResult> SendAsync(HttpRequestSpec spec, CancellationToken ct, bool optionalAuth = false)
        {
            if (!optionalAuth || _auth.IsSignedIn)
                spec.Headers["Authorization"] = "Bearer " + await _auth.GetAccessTokenAsync(ct);
            return await _http.SendAsync(spec, ct);
        }

        public async Task<Dictionary<string, object>> JsonAsync(string what, string method, string url, object body, CancellationToken ct,
            bool optionalAuth = false, bool notFoundIsNull = false)
        {
            var spec = new HttpRequestSpec { Method = method, Url = url };
            if (body != null)
            {
                spec.Body = Encoding.UTF8.GetBytes(Json.Serialize(body));
                spec.ContentType = "application/json";
            }
            var r = await SendAsync(spec, ct, optionalAuth);
            if (notFoundIsNull && r.Status == 404) return null;
            if (!r.Ok) throw TupicApiException.From(what, r);
            return ParseOrEmpty(what, r);
        }

        public static Dictionary<string, object> ParseOrEmpty(string what, HttpResult r)
        {
            if (r.Body == null || r.Body.Length == 0 || string.IsNullOrWhiteSpace(r.Text)) return new Dictionary<string, object>();
            try { return r.JsonObject(); }
            catch (FormatException) { throw new TupicApiException(what + " returned a response that is not a JSON object", r.Status); }
        }

        public static string Segment(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " is required.", name);
            foreach (var c in value)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') || c > 0x7f)
                    throw new ArgumentException(name + " contains characters that are not allowed in a path segment.", name);
            if (value == "." || value == "..") throw new ArgumentException(name + " is not a valid path segment.", name);
            return value;
        }
    }
}
