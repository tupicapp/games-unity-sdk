// Tupic SDK core: games-core scores and analytics, the same calls the web game SDK (game-sdk.js / analytics.js) makes.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tupic.Core
{
    public sealed class TupicGames
    {
        readonly TupicEnvironment _env;
        readonly TupicApi _api;
        readonly string _gameId;
        readonly TupicAnalytics _analytics;

        internal TupicGames(TupicEnvironment env, TupicApi api, string gameId, TupicAnalytics analytics)
        {
            _env = env; _api = api; _gameId = gameId; _analytics = analytics;
        }

        /// <summary>Submits a score. games-core takes the user name from the token when one is sent.</summary>
        public Task<Dictionary<string, object>> SubmitScoreAsync(long score, int level = 1, string username = null,
            double playTimeSeconds = 0, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object>
            {
                ["score"] = score,
                ["level"] = level,
                ["session_id"] = _analytics.SessionId,
                ["play_time"] = playTimeSeconds,
            };
            if (!string.IsNullOrEmpty(username)) body["username"] = username;
            return _api.JsonAsync("Submit score", "POST",
                _env.GamesApi + "/games/" + Uri.EscapeDataString(_gameId) + "/scores", body, ct, optionalAuth: true);
        }
    }

    public sealed class TupicAnalytics
    {
        /// <summary>The event names games-core accepts (anything else is a 400 there, so it is refused here).</summary>
        public static readonly string[] AcceptedEvents = { "game_start", "game_end", "level_complete", "score_update", "game_pause", "game_resume" };
        public const string Platform = "unity";

        readonly TupicEnvironment _env;
        readonly TupicApi _api;
        readonly string _gameId;
        public string SessionId { get; private set; } = NewSessionId();
        public Func<DateTimeOffset> Clock = () => DateTimeOffset.UtcNow;

        internal TupicAnalytics(TupicEnvironment env, TupicApi api, string gameId) { _env = env; _api = api; _gameId = gameId; }

        /// <summary>Starts a new play session (a new SessionId), e.g. on restart.</summary>
        public void NewSession() => SessionId = NewSessionId();

        public Task GameStartAsync(CancellationToken ct = default) => TrackAsync("game_start", null, ct);

        public Task LevelCompleteAsync(int level, long score, CancellationToken ct = default) =>
            TrackAsync("level_complete", new Dictionary<string, object> { ["level"] = level, ["score"] = score }, ct);

        public Task ScoreUpdateAsync(long score, CancellationToken ct = default) =>
            TrackAsync("score_update", new Dictionary<string, object> { ["score"] = score }, ct);

        public Task GameEndAsync(long score, int level, string reason = null, CancellationToken ct = default)
        {
            var data = new Dictionary<string, object> { ["score"] = score, ["level"] = level };
            if (!string.IsNullOrEmpty(reason)) data["reason"] = reason;
            return TrackAsync("game_end", data, ct);
        }

        public Task GamePauseAsync(CancellationToken ct = default) => TrackAsync("game_pause", null, ct);
        public Task GameResumeAsync(CancellationToken ct = default) => TrackAsync("game_resume", null, ct);

        public Task TrackAsync(string eventName, Dictionary<string, object> data = null, CancellationToken ct = default)
        {
            if (Array.IndexOf(AcceptedEvents, eventName) < 0)
                throw new ArgumentException($"games-core does not accept the analytics event '{eventName}'. Accepted: {string.Join(", ", AcceptedEvents)}.", nameof(eventName));
            var body = new Dictionary<string, object>
            {
                ["event"] = eventName,
                ["game_id"] = _gameId,
                ["session_id"] = SessionId,
                ["platform"] = Platform,
                ["timestamp"] = Clock().ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["data"] = data ?? new Dictionary<string, object>(),
            };
            return _api.JsonAsync("Analytics event " + eventName, "POST", _env.GamesApi + "/analytics/event", body, ct, optionalAuth: true);
        }

        static string NewSessionId() => Guid.NewGuid().ToString("N");
    }
}
