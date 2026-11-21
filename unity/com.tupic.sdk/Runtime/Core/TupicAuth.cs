// Tupic SDK core: sign-in with Keycloak. A game is a public OIDC client: authorization code + PKCE (S256) with a
// loopback redirect (desktop) or the page origin (WebGL). No client secret ever ships in a game. Tokens stay in
// memory unless the engine layer supplies a store; they are never logged.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tupic.Core
{
    public sealed class TokenSet
    {
        public string AccessToken;
        public string RefreshToken;
        public DateTimeOffset ExpiresAt;
    }

    public sealed class LoginRequest
    {
        public string Url;
        public string State;
        public string CodeVerifier;
        public string RedirectUri;
    }

    public interface ITokenStore
    {
        TokenSet Load();
        void Save(TokenSet tokens);
        void Clear();
    }

    public sealed class MemoryTokenStore : ITokenStore
    {
        TokenSet _t;
        public TokenSet Load() => _t;
        public void Save(TokenSet tokens) => _t = tokens;
        public void Clear() => _t = null;
    }

    public sealed class TupicAuth
    {
        /// <summary>A cached access token is replaced this long before it expires.</summary>
        public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);

        readonly TupicEnvironment _env;
        readonly IHttpTransport _http;
        readonly ITokenStore _store;
        readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        public Func<DateTimeOffset> Clock = () => DateTimeOffset.UtcNow;
        public string Scope = "openid profile";
        /// <summary>State of the last BeginLogin, for callers that only keep the code from the redirect.</summary>
        public string PendingState { get; private set; }
        LoginRequest _pending;

        public TupicAuth(TupicEnvironment env, IHttpTransport http, ITokenStore store = null)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _store = store ?? new MemoryTokenStore();
        }

        public bool IsSignedIn => _store.Load() != null;

        /// <summary>Step 1: the URL to open in the system browser (or the page, on WebGL).</summary>
        public LoginRequest BeginLogin(string redirectUri)
        {
            // Desktop: loopback http redirect (RFC 8252). WebGL games sign in through the page, not through this call.
            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var r) || r.Scheme != "http" || !r.IsLoopback)
                throw new ArgumentException("redirectUri must be a loopback http URL, e.g. http://127.0.0.1:53682/callback.");
            var verifier = RandomUrlSafe(48);
            var state = RandomUrlSafe(16);
            var challenge = Base64Url(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var q = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = _env.ClientId,
                ["redirect_uri"] = redirectUri,
                ["scope"] = Scope,
                ["state"] = state,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
            };
            _pending = new LoginRequest { Url = _env.AuthorizeEndpoint + "?" + FormEncode(q), State = state, CodeVerifier = verifier, RedirectUri = redirectUri };
            PendingState = state;
            return _pending;
        }

        /// <summary>Step 2: exchange the code from the redirect; the state must match.</summary>
        public async Task<TokenSet> CompleteLoginAsync(LoginRequest request, string code, string state, CancellationToken ct = default)
        {
            if (request == null || string.IsNullOrEmpty(state) || !string.Equals(state, request.State, StringComparison.Ordinal))
                throw new InvalidOperationException("Login state mismatch: the redirect does not belong to this login.");
            return await TokenRequest(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = _env.ClientId,
                ["code"] = code,
                ["redirect_uri"] = request.RedirectUri,
                ["code_verifier"] = request.CodeVerifier,
            }, ct).ConfigureAwait(false);
        }

        /// <summary>Automated tests against the local stack only (Keycloak's local e2e client).</summary>
        public Task<TokenSet> LoginWithPasswordForTestsAsync(string username, string password, CancellationToken ct = default)
        {
            if (!_env.AllowPasswordGrant || _env.Name != "local")
                throw new InvalidOperationException("The password grant is only for automated tests against the local stack.");
            return TokenRequest(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = _env.ClientId,
                ["username"] = username,
                ["password"] = password,
                ["scope"] = "openid",
            }, ct);
        }

        /// <summary>A valid access token: cached, refreshed RefreshMargin before expiry, one refresh for concurrent callers.</summary>
        public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var t = _store.Load() ?? throw new InvalidOperationException("Not signed in to Tupic.");
                if (Clock() < t.ExpiresAt - RefreshMargin) return t.AccessToken;
                if (string.IsNullOrEmpty(t.RefreshToken)) throw new InvalidOperationException("Session expired: sign in again.");
                var fresh = await TokenRequestUnlocked(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = _env.ClientId,
                    ["refresh_token"] = t.RefreshToken,
                }, ct).ConfigureAwait(false);
                return fresh.AccessToken;
            }
            finally { _lock.Release(); }
        }

        public void SignOut() => _store.Clear();

        /// <summary>The claims of a JWT (not verified: the services verify; the game only reads sub and names).</summary>
        public static Dictionary<string, object> ReadClaims(string jwt)
        {
            var parts = (jwt ?? "").Split('.');
            if (parts.Length < 2) throw new FormatException("Not a JWT");
            var p = parts[1].Replace('-', '+').Replace('_', '/');
            p += new string('=', (4 - p.Length % 4) % 4);
            return Json.ParseObject(Encoding.UTF8.GetString(Convert.FromBase64String(p)));
        }

        public async Task<Dictionary<string, object>> CurrentClaimsAsync(CancellationToken ct = default) =>
            ReadClaims(await GetAccessTokenAsync(ct).ConfigureAwait(false));

        async Task<TokenSet> TokenRequest(Dictionary<string, string> form, CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try { return await TokenRequestUnlocked(form, ct).ConfigureAwait(false); }
            finally { _lock.Release(); }
        }

        async Task<TokenSet> TokenRequestUnlocked(Dictionary<string, string> form, CancellationToken ct)
        {
            var r = await _http.SendAsync(new HttpRequestSpec
            {
                Method = "POST", Url = _env.TokenEndpoint, Body = Encoding.UTF8.GetBytes(FormEncode(form)),
                ContentType = "application/x-www-form-urlencoded",
            }, ct).ConfigureAwait(false);
            if (!r.Ok)
            {
                string detail;
                try { var o = r.JsonObject(); detail = o.Str("error") + ": " + o.Str("error_description"); }
                catch (FormatException) { detail = "HTTP " + r.Status; }
                throw new TupicApiException("Keycloak token request failed (" + r.Status + "): " + TupicLog.Redact(detail), r.Status);
            }
            var j = r.JsonObject();
            var tokens = new TokenSet
            {
                AccessToken = j.Str("access_token") ?? throw new TupicApiException("Keycloak returned no access token", r.Status),
                RefreshToken = j.Str("refresh_token") ?? _store.Load()?.RefreshToken,
                ExpiresAt = Clock().AddSeconds(Math.Max(1, j.Long("expires_in", 60))),
            };
            _store.Save(tokens);
            TupicLog.Info("Tupic sign-in: token obtained (" + form["grant_type"] + ")");
            return tokens;
        }

        static string FormEncode(Dictionary<string, string> f)
        {
            var sb = new StringBuilder();
            foreach (var kv in f)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
            }
            return sb.ToString();
        }

        static string RandomUrlSafe(int bytes)
        {
            var b = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Base64Url(b);
        }

        static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
