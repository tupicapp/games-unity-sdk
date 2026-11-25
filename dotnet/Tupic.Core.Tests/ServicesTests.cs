using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tupic.Core;
using Xunit;

public class ServicesTests
{
    static readonly TupicEnvironment Env = TupicEnvironment.Local("games-unity");
    const string Game = "g-1";

    static async Task<(TupicClient client, FakeTransport t)> Client(Action<FakeTransport> routes = null)
    {
        var t = new FakeTransport().On("POST", Env.TokenEndpoint, 200,
            new Dictionary<string, object> { ["access_token"] = "user-at", ["expires_in"] = 300, ["refresh_token"] = "rt" });
        routes?.Invoke(t);
        var client = new TupicClient(Env, t, Game);
        await client.Auth.LoginWithPasswordForTestsAsync("u", "p");
        return (client, t);
    }

    static string Bearer(HttpRequestSpec r) => r.Headers.TryGetValue("Authorization", out var v) ? v : null;

    // ── Games: scores, leaderboard, analytics (mirrors game-sdk.js / analytics.js) ──
    // games-core's score policy: a signed-in user only (the player comes from the token), one submit per session
    // (a second is 409), session_id ^[A-Za-z0-9_.:-]{8,100}$.
    [Fact]
    public async Task ScoreIsSubmittedWithTheUserTokenAndNoUsername()
    {
        var (c, t) = await Client(f => f.On("POST", Env.GamesApi + "/games/g-1/scores", 201,
            new Dictionary<string, object> { ["status"] = "ok", ["score"] = 120, ["level"] = 3 }));
        var r = await c.Games.SubmitScoreAsync(120, 3);
        var req = t.Requests.Last();
        Assert.Equal("Bearer user-at", Bearer(req));
        var body = FakeTransport.JsonBody(req);
        Assert.Equal(120L, body["score"]);
        Assert.Equal(3L, body["level"]);
        Assert.False(body.ContainsKey("username"));
        Assert.Equal(c.Analytics.SessionId, body["session_id"]);
        Assert.Matches("^[A-Za-z0-9_.:-]{8,100}$", c.Analytics.SessionId);
        Assert.Equal("ok", r.Str("status"));
    }

    [Fact]
    public async Task AGuestCannotSubmitAScoreAndNothingIsSent()
    {
        var t = new FakeTransport().On("POST", Env.GamesApi + "/games/g-1/scores", 201);
        var c = new TupicClient(Env, t, Game);
        var e = await Assert.ThrowsAsync<TupicSignInRequiredException>(() => c.Games.SubmitScoreAsync(50, 1));
        Assert.Contains("signed-in", e.Message);
        Assert.Empty(t.Requests);
    }

    [Fact]
    public async Task OneSubmitPerSessionAndANewSessionSubmitsAgain()
    {
        var (c, t) = await Client(f => f.On("POST", Env.GamesApi + "/games/g-1/scores", 201));
        await c.Games.SubmitScoreAsync(10, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Games.SubmitScoreAsync(20, 1));
        Assert.Single(t.Requests.Where(r => r.Url.EndsWith("/scores")));
        c.Analytics.NewSession();
        await c.Games.SubmitScoreAsync(30, 2);
        var sent = t.Requests.Where(r => r.Url.EndsWith("/scores")).Select(FakeTransport.JsonBody).ToList();
        Assert.Equal(2, sent.Count);
        Assert.NotEqual(sent[0].Str("session_id"), sent[1].Str("session_id"));
    }

    [Fact]
    public async Task AServer409MarksTheSessionAsSubmitted()
    {
        var (c, t) = await Client(f => f.On("POST", Env.GamesApi + "/games/g-1/scores", 409,
            new Dictionary<string, object> { ["error"] = "This session already has a score" }));
        var e = await Assert.ThrowsAsync<TupicApiException>(() => c.Games.SubmitScoreAsync(10, 1));
        Assert.Equal(409, e.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Games.SubmitScoreAsync(10, 1));
        Assert.Single(t.Requests.Where(r => r.Url.EndsWith("/scores")));
    }

    [Fact]
    public async Task AnalyticsUsesTheEventNamesGamesCoreAccepts()
    {
        var (c, t) = await Client(f => f.On("POST", Env.GamesApi + "/analytics/event", 200));
        await c.Analytics.GameStartAsync();
        await c.Analytics.LevelCompleteAsync(2, 300);
        await c.Analytics.ScoreUpdateAsync(310);
        await c.Analytics.GameEndAsync(310, 2, "gameover");
        var sent = t.Requests.Where(r => r.Url.EndsWith("/analytics/event")).Select(FakeTransport.JsonBody).ToList();
        Assert.Equal(new[] { "game_start", "level_complete", "score_update", "game_end" }, sent.Select(e => e.Str("event")));
        Assert.All(sent, e => { Assert.Equal(Game, e.Str("game_id")); Assert.Equal(c.Analytics.SessionId, e.Str("session_id")); Assert.Equal("unity", e.Str("platform")); });
        Assert.Equal("gameover", sent[3].Obj("data").Str("reason"));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Analytics.TrackAsync("level_start"));
    }

    // ── Insights: called with the signed-in user's token (never a service secret in a game) ──
    [Fact]
    public async Task InsightsCultureCallsUseTheUserToken()
    {
        var (c, t) = await Client(f => f
            .On("GET", Env.InsightsApi + "/geography/countries/AE/calendar", 200, new Dictionary<string, object> { ["calendar"] = new List<object>() })
            .On("GET", Env.InsightsApi + "/themes/country/AE", 200, new Dictionary<string, object> { ["themes"] = new List<object>() })
            .On("POST", Env.InsightsApi + "/game/register", 201, new Dictionary<string, object> { ["status"] = "registered" }));
        await c.Insights.GetCalendarAsync("AE");
        await c.Insights.GetThemeAsync("ae");
        var reg = await c.Insights.RegisterGameAsync("Runner", "runner", "AE", "https://games.tupic.app/games/g-1", new[] { "https://storage/x.png" });
        Assert.All(t.Requests.Skip(1), r => Assert.Equal("Bearer user-at", Bearer(r)));
        var body = FakeTransport.JsonBody(t.Requests.Last());
        Assert.Equal(Game, body["game_id"]);
        Assert.Equal("unity", body.Obj("metadata").Str("engine"));
        Assert.Equal("registered", reg.Str("status"));
    }

    [Fact]
    public void DefaultRegionComesFromTheDeviceCulture() =>
        Assert.Equal("AE", TupicInsights.RegionFromCulture("ar-AE"));

    [Fact]
    public async Task InsightsNotFoundIsNullNotAnError()
    {
        var (c, _) = await Client(f => f.On("GET", Env.InsightsApi + "/themes/country/ZZ", 404, new Dictionary<string, object> { ["error"] = "no theme for country" }));
        Assert.Null(await c.Insights.GetThemeAsync("ZZ"));
    }

    // ── Chain: read-only; rewards are decided server-side, never by the game ──
    [Fact]
    public async Task ChainIsReadOnly()
    {
        var (c, t) = await Client(f => f.On("GET", Env.ChainApi + "/rewards/types", 200, new Dictionary<string, object> { ["types"] = new List<object>() }));
        await c.Chain.GetRewardTypesAsync();
        Assert.Equal(Env.ChainApi + "/rewards/types", t.Requests.Last().Url);
        var methods = typeof(TupicChain).GetMethods().Where(m => m.DeclaringType == typeof(TupicChain)).Select(m => m.Name).ToList();
        Assert.DoesNotContain(methods, m => m.IndexOf("Distribute", StringComparison.OrdinalIgnoreCase) >= 0
                                         || m.IndexOf("Mint", StringComparison.OrdinalIgnoreCase) >= 0
                                         || m.IndexOf("Transfer", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public async Task ChainAddressIsValidated()
    {
        var (c, _) = await Client();
        await Assert.ThrowsAsync<ArgumentException>(() => c.Chain.GetRewardStatsAsync("../admin"));
    }

    // ── Assets: every game asset goes to assets-core (create + presigned upload) ──
    [Fact]
    public async Task UploadCreatesTheAssetThenPutsTheBytes()
    {
        var (c, t) = await Client(f => f
            .On("POST", Env.AssetsApi + "/assets", 201, new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["id"] = "a-1", ["url"] = "https://storage.tupic.app/x/file.png" },
                ["upload_url"] = "https://storage.tupic.app/presigned?sig=1",
                ["upload_content_type"] = "image/png",
            })
            .On("PUT", "https://storage.tupic.app/presigned", 200));
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var a = await c.Assets.UploadAsync(png, "hero.png", "image/png");
        var create = t.Requests.First(r => r.Method == "POST" && r.Url == Env.AssetsApi + "/assets");
        var body = FakeTransport.JsonBody(create);
        Assert.Equal("image/png", body["mime_type"]);
        Assert.Equal("games", body["service"]);
        Assert.Equal("games-unity", body["source_client"]);
        Assert.Equal("Bearer user-at", Bearer(create));
        var put = t.Requests.Last();
        Assert.Equal("PUT", put.Method);
        Assert.Equal("image/png", put.ContentType);
        Assert.Null(Bearer(put));
        Assert.Equal(png, put.Body);
        Assert.Equal("a-1", a.AssetId);
        Assert.Equal("https://storage.tupic.app/x/file.png", a.Url);
        Assert.Equal(TupicAssets.Sha256Hex(png), a.Sha256);
    }

    [Fact]
    public async Task UnsupportedFormatIsAClearError()
    {
        var (c, _) = await Client(f => f.On("POST", Env.AssetsApi + "/assets", 422, new Dictionary<string, object> { ["message"] = "unsupported upload media" }));
        var e = await Assert.ThrowsAsync<TupicApiException>(() => c.Assets.UploadAsync(new byte[] { 1 }, "m.gltf", "model/gltf+json"));
        Assert.Contains("422", e.Message);
    }

    [Fact]
    public void ManifestRoundTrips()
    {
        var m = new AssetManifest { GameId = Game };
        m.Entries["player"] = new AssetRef { AssetId = "a1", Url = "https://s/p.png", MimeType = "image/png", Sha256 = "ab", SourcePath = "Assets/Art/p.png" };
        var back = AssetManifest.FromJson(m.ToJson());
        Assert.Equal("a1", back.Entries["player"].AssetId);
        Assert.Equal("Assets/Art/p.png", back.Entries["player"].SourcePath);
    }

    // ── Live: the same bridge as the web engine (games-core relays to Live v3) ──
    [Fact]
    public async Task LiveSessionSetsUpRelaysChunksAndStops()
    {
        var (c, t) = await Client(f => f
            .On("POST", Env.GamesApi + "/tupiclive/streams/setup", 201, new Dictionary<string, object>
                { ["channel_id"] = "ch-1", ["stream_key"] = "ch-1/sk-secret", ["rtmp_url"] = "rtmp://host/live", ["stream_id"] = "s-1" })
            .On("POST", Env.GamesApi + "/tupiclive/relay/start", 200)
            .On("POST", Env.GamesApi + "/tupiclive/relay/chunk/ch-1", 200)
            .On("POST", Env.GamesApi + "/tupiclive/relay/stop", 200));
        string logged = "";
        TupicLog.Sink = m => logged += m + "\n";
        var live = await c.Live.StartAsync("Runner", "video/webm;codecs=vp8,opus");
        await live.SendChunkAsync(new byte[] { 1, 2, 3 });
        await live.StopAsync();
        var start = FakeTransport.JsonBody(t.Requests.First(r => r.Url.EndsWith("/relay/start")));
        Assert.Equal("ch-1/sk-secret", start["stream_key"]);
        Assert.Equal("video/webm;codecs=vp8,opus", start["mime_type"]);
        var chunk = t.Requests.First(r => r.Url.EndsWith("/relay/chunk/ch-1"));
        Assert.Equal("video/webm;codecs=vp8,opus", chunk.Headers["X-TupicLive-Mime-Type"]);
        Assert.Single(t.Requests.Where(r => r.Url.EndsWith("/relay/stop")));
        Assert.DoesNotContain(t.Requests, r => r.Url.Contains("/programs/"));
        Assert.DoesNotContain("sk-secret", logged);
        await live.StopAsync(); // idempotent
        Assert.Single(t.Requests.Where(r => r.Url.EndsWith("/relay/stop")));
    }

    [Fact]
    public async Task ChunkAfterTheRelayStoppedEndsTheSession()
    {
        var (c, _) = await Client(f => f
            .On("POST", Env.GamesApi + "/tupiclive/streams/setup", 201, new Dictionary<string, object> { ["channel_id"] = "ch-1", ["stream_key"] = "k", ["rtmp_url"] = "r" })
            .On("POST", Env.GamesApi + "/tupiclive/relay/start", 200)
            .On("POST", Env.GamesApi + "/tupiclive/relay/chunk/ch-1", 409, new Dictionary<string, object> { ["error"] = "Relay is not active for this channel" })
            .On("POST", Env.GamesApi + "/tupiclive/relay/stop", 200));
        var live = await c.Live.StartAsync("Runner", "video/webm");
        Assert.False(await live.SendChunkAsync(new byte[] { 1 }));
        Assert.False(live.IsActive);
    }
}
