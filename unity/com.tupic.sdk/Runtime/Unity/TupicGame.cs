// Drop on a GameObject: connects the game to Tupic (session, analytics, scores) the same way web games do.
using System;
using System.Threading.Tasks;
using Tupic.Core;
using UnityEngine;

namespace Tupic.Unity
{
    public sealed class TupicGame : MonoBehaviour
    {
        [Tooltip("Game id on Tupic Games; hosted builds read it from the play frame.")]
        public string gameId = "";
        public long Score { get; private set; }
        public int Level { get; private set; } = 1;
        public TupicClient Client { get; private set; }
        public event Action<long> ScoreChanged;

        void Awake()
        {
            var id = TupicHost.IsHosted ? TupicHost.GameId : gameId;
            Client = new TupicClient(TupicEnvironment.Local(), new UnityWebRequestTransport(), string.IsNullOrEmpty(id) ? "local" : id);
            RefreshHostToken();
        }

        async void Start() { await Safe(() => Client.Analytics.GameStartAsync()); }

        /// <summary>The play frame hands over a fresh token from time to time; pick it up before each call.</summary>
        public void RefreshHostToken()
        {
            var t = TupicHost.Token;
            if (!string.IsNullOrEmpty(t)) Client.Auth.UseHostToken(t, DateTimeOffset.UtcNow.AddMinutes(4));
        }

        /// <summary>Play again: new analytics session, score and level reset.</summary>
        public async void NewGame()
        {
            Score = 0; Level = 1; ScoreChanged?.Invoke(Score);
            Client.Analytics.NewSession();
            RefreshHostToken();
            await Safe(() => Client.Analytics.GameStartAsync());
        }

        public void AddScore(long points) { Score += points; ScoreChanged?.Invoke(Score); }

        public async void CompleteLevel()
        {
            RefreshHostToken();
            await Safe(() => Client.Analytics.LevelCompleteAsync(Level, Score));
            Level++;
        }

        public async void EndGame(string reason = "gameover")
        {
            RefreshHostToken();
            await Safe(() => Client.Analytics.GameEndAsync(Score, Level, reason));
            await Safe(() => Client.Games.SubmitScoreAsync(Score, Level, TupicHost.Get("username")));
        }

        static async Task Safe(Func<Task> call)
        {
            try { await call(); }
            catch (Exception e) { Debug.LogWarning("[Tupic] " + TupicLog.Redact(e.Message)); }
        }
    }
}
