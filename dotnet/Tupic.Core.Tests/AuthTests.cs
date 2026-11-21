using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Tupic.Core;
using Xunit;

public class AuthTests
{
    static readonly TupicEnvironment Env = TupicEnvironment.Local("games-unity");
    static Dictionary<string, object> TokenJson(string access = "at-1", int expiresIn = 300, string refresh = "rt-1") =>
        new Dictionary<string, object> { ["access_token"] = access, ["expires_in"] = expiresIn, ["refresh_token"] = refresh, ["token_type"] = "Bearer" };

    [Fact]
    public void AuthorizationUrlUsesPkceS256AndALoopbackRedirect()
    {
        var auth = new TupicAuth(Env, new FakeTransport());
        var req = auth.BeginLogin("http://127.0.0.1:53682/callback");
        var q = new Uri(req.Url).Query.TrimStart('?').Split('&');
        var p = new Dictionary<string, string>();
        foreach (var kv in q) { var a = kv.Split('='); p[a[0]] = Uri.UnescapeDataString(a[1]); }
        Assert.StartsWith(Env.AuthorizeEndpoint + "?", req.Url);
        Assert.Equal("code", p["response_type"]);
        Assert.Equal("games-unity", p["client_id"]);
        Assert.Equal("S256", p["code_challenge_method"]);
        Assert.Equal("http://127.0.0.1:53682/callback", p["redirect_uri"]);
        var expected = Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(req.CodeVerifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, p["code_challenge"]);
        Assert.InRange(req.CodeVerifier.Length, 43, 128);
        Assert.False(string.IsNullOrEmpty(p["state"]));
        Assert.Contains("openid", p["scope"]);
    }

    [Fact]
    public void NonLoopbackRedirectIsRefused() =>
        Assert.Throws<ArgumentException>(() => new TupicAuth(Env, new FakeTransport()).BeginLogin("https://evil.example/cb"));

    [Fact]
    public async Task CodeExchangeChecksStateAndSendsTheVerifier()
    {
        var t = new FakeTransport().On("POST", Env.TokenEndpoint, 200, TokenJson());
        var auth = new TupicAuth(Env, t);
        var req = auth.BeginLogin("http://127.0.0.1:5000/cb");
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.CompleteLoginAsync(req, "code-1", "wrong-state"));
        var tokens = await auth.CompleteLoginAsync(req, "code-1", req.State);
        var form = FakeTransport.Form(t.Requests[0]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal(req.CodeVerifier, form["code_verifier"]);
        Assert.Equal("code-1", form["code"]);
        Assert.False(form.ContainsKey("client_secret"), "a game is a public client: no secret ships in it");
        Assert.Equal("at-1", tokens.AccessToken);
    }

    [Fact]
    public async Task AccessTokenIsCachedAndRefreshedShortlyBeforeExpiry()
    {
        var now = new DateTimeOffset(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);
        var t = new FakeTransport()
            .On("POST", Env.TokenEndpoint, r => FakeTransport.Form(r)["grant_type"] == "refresh_token"
                ? FakeTransport.Reply(200, TokenJson("at-2", 300, "rt-2")) : FakeTransport.Reply(200, TokenJson("at-1", 300, "rt-1")));
        var auth = new TupicAuth(Env, t) { Clock = () => now };
        await auth.CompleteLoginAsync(auth.BeginLogin("http://127.0.0.1:5000/cb"), "c", auth.PendingState);
        now = now.AddSeconds(200);
        Assert.Equal("at-1", await auth.GetAccessTokenAsync());
        now = now.AddSeconds(75); // 25 s left: within the 30 s margin
        Assert.Equal("at-2", await auth.GetAccessTokenAsync());
        Assert.Equal("refresh_token", FakeTransport.Form(t.Requests[1])["grant_type"]);
        Assert.Equal("rt-1", FakeTransport.Form(t.Requests[1])["refresh_token"]);
    }

    [Fact]
    public async Task PasswordGrantOnlyAgainstTheLocalStack()
    {
        var t = new FakeTransport().On("POST", Env.TokenEndpoint, 200, TokenJson());
        await new TupicAuth(Env, t).LoginWithPasswordForTestsAsync("e2e-admin", "pw");
        Assert.Equal("password", FakeTransport.Form(t.Requests[0])["grant_type"]);
        var remote = TupicEnvironment.Remote("development", "tupic.dev", true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TupicAuth(remote, t).LoginWithPasswordForTestsAsync("u", "p"));
    }

    [Fact]
    public async Task TokenErrorIsReportedWithoutTheSecretOrToken()
    {
        var t = new FakeTransport().On("POST", Env.TokenEndpoint, 401, new Dictionary<string, object> { ["error"] = "invalid_grant", ["error_description"] = "Invalid user credentials" });
        var e = await Assert.ThrowsAsync<TupicApiException>(() => new TupicAuth(Env, t).LoginWithPasswordForTestsAsync("u", "secret-pw"));
        Assert.Equal(401, e.Status);
        Assert.DoesNotContain("secret-pw", e.Message);
        Assert.Contains("invalid_grant", e.Message);
    }

    [Fact]
    public async Task NotSignedInIsAClearError() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TupicAuth(Env, new FakeTransport()).GetAccessTokenAsync());

    [Fact]
    public void SubjectAndNameComeFromTheTokenClaims()
    {
        string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jwt = B64("{\"alg\":\"none\"}") + "." + B64("{\"sub\":\"u-42\",\"preferred_username\":\"jane\"}") + ".sig";
        var claims = TupicAuth.ReadClaims(jwt);
        Assert.Equal("u-42", claims.Str("sub"));
        Assert.Equal("jane", claims.Str("preferred_username"));
    }
}
