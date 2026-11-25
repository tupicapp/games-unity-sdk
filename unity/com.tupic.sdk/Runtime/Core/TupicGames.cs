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
        readonly TupicAuth _auth;

        internal TupicGames(TupicEnvironment env, TupicApi api, string gameId, TupicAnalytics analytics, TupicAuth auth)
        {
            _env = env; _api = api; _gameId = gameId; _analytics = analytics; _auth = auth;
        }

        // Sessions that already submitted (or that games-core answered 409 for): games-core takes one score per session.
        readonly HashSet<string> _submitted = new HashSet<string>();
        static readonly System.Text.RegularExpressions.Regex SessionIdFormat =
            new System.Text.RegularExpressions.Regex("^[A-Za-z0-9_.:-]{8,100}$");

        /// <summary>
        /// Submits the score of the current play session, once, as the signed-in user (games-core takes the player
        /// from the token only; there is no name to send). A guest cannot submit: TupicSignInRequiredException, and
        /// nothing is sent. A second submit in the same session throws InvalidOperationException; start a new
        /// session (Analytics.NewSession, TupicGame.NewGame) for the next run. A 409 from games-core also ends it.
        /// </summary>
        public async Task<Dictionary<string, object>> SubmitScoreAsync(long score, int level = 1, double playTimeSeconds = 0,
            CancellationToken ct = default)
        {
            if (!_auth.IsSignedIn)
                throw new TupicSignInRequiredException("A signed-in player is required to submit a score; sign in first (guests have no scores).");
            var session = _analytics.SessionId;
            if (!SessionIdFormat.IsMatch(session ?? ""))
                throw new InvalidOperationException("The session id is not one games-core accepts (8-100 of A-Z a-z 0-9 _ . : -).");
            if (_submitted.Contains(session))
                throw new InvalidOperationException("This session already submitted its score; start a new session for the next run.");
            var body = new Dictionary<string, object>
            {
                ["score"] = score,
                ["level"] = level,
                ["session_id"] = session,
                ["play_time"] = playTimeSeconds,
            };
            try
            {
                var r = await _api.JsonAsync("Submit score", "POST",
                    _env.GamesApi + "/games/" + Uri.EscapeDataString(_gameId) + "/scores", body, ct);
                _submitted.Add(session);
                return r;
            }
            catch (TupicApiException e) when (e.Status == 409)
            {
                _submitted.Add(session);
                throw;
            }
        }
    }

    /// <summary>The call needs a signed-in user (e.g. a score); nothing was sent.</summary>
    public sealed class TupicSignInRequiredException : InvalidOperationException
    {
        public TupicSignInRequiredException(string message) : base(message) { }
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
