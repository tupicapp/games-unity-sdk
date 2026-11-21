using System;
using System.Collections.Generic;
using Tupic.Core;
using Xunit;

public class JsonTests
{
    [Fact]
    public void RoundTripsObjectsArraysAndNumbers()
    {
        var o = Json.ParseObject("{\"a\":1,\"b\":[true,null,\"x\\n\\u00e9\"],\"c\":{\"d\":2.5,\"e\":-3e2}}");
        Assert.Equal(1L, o["a"]);
        Assert.Equal(true, ((List<object>)o["b"])[0]);
        Assert.Null(((List<object>)o["b"])[1]);
        Assert.Equal("x\né", ((List<object>)o["b"])[2]);
        Assert.Equal(2.5, o.Obj("c")["d"]);
        Assert.Equal(-300.0, o.Obj("c")["e"]);
        Assert.Equal(o.Obj("c")["d"], Json.ParseObject(Json.Serialize(o)).Obj("c")["d"]);
    }

    [Fact]
    public void EscapesScriptBreakingCharacters()
    {
        var s = Json.Serialize(new Dictionary<string, object> { ["n"] = "</script><b>&\u2028" });
        Assert.DoesNotContain("</script>", s);
        Assert.Contains("\\u003c", s);
        Assert.Equal("</script><b>&\u2028", Json.ParseObject(s).Str("n"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"a\":}")]
    [InlineData("[1,2")]
    [InlineData("\"abc")]
    [InlineData("{} x")]
    public void RejectsMalformedJson(string text) => Assert.Throws<FormatException>(() => Json.Parse(text));
}

public class RedactionTests
{
    [Fact]
    public void NeverLetsATokenIntoALog()
    {
        var jwt = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.c2lnbmF0dXJlLXZhbHVl";
        var line = TupicLog.Redact($"Authorization: Bearer {jwt} body=client_secret=abc&password=hunter2&code_verifier=xyz raw {jwt}");
        Assert.DoesNotContain(jwt, line);
        Assert.DoesNotContain("hunter2", line);
        Assert.DoesNotContain("abc&", line);
        Assert.DoesNotContain("xyz", line);
        string logged = null;
        TupicLog.Sink = m => logged = m;
        TupicLog.Info("token " + jwt);
        Assert.Equal("token <jwt>", logged);
    }
}

public class EnvironmentTests
{
    [Fact]
    public void LocalIsTheLaptopStack()
    {
        var env = TupicEnvironment.Local();
        env.Validate();
        Assert.All(env.Hosts(), h => Assert.EndsWith(".tupic.app", h));
        Assert.Equal("https://iam.tupic.app/realms/tupic/protocol/openid-connect/token", env.TokenEndpoint);
    }

    [Fact]
    public void RemoteNeedsAnExplicitOptIn()
    {
        Assert.Throws<InvalidOperationException>(() => TupicEnvironment.Remote("development", "tupic.dev", allowRemote: false));
        var dev = TupicEnvironment.Remote("development", "tupic.dev", allowRemote: true);
        dev.Validate();
        Assert.False(dev.AllowPasswordGrant);
    }

    [Fact]
    public void LocalPointingAtARemoteHostIsRefused()
    {
        var env = TupicEnvironment.Local();
        env.GamesApi = "https://api.tupic.dev/games/api/v1";
        Assert.Throws<InvalidOperationException>(() => env.Validate());
        var remote = TupicEnvironment.Remote("prod", "tupic.com", true);
        remote.AllowPasswordGrant = true;
        Assert.Throws<InvalidOperationException>(() => remote.Validate());
    }
}
