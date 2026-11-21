// Tupic SDK core: streaming a game to Tupic Live through games-core's relay, the same bridge the web engine uses
// (setup -> relay/start -> relay/chunk/{channel} ... -> relay/stop; games-core relays to Live v3). The stream key is a
// credential: it is sent to games-core only and never logged.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tupic.Core
{
    public sealed class TupicLive
    {
        readonly TupicEnvironment _env;
        readonly TupicApi _api;
        readonly string _gameId;

        internal TupicLive(TupicEnvironment env, TupicApi api, string gameId) { _env = env; _api = api; _gameId = gameId; }

        /// <summary>Sets up a channel and starts the relay. mimeType is the recorder's container/codec, e.g. video/webm;codecs=vp8,opus.</summary>
        public async Task<TupicLiveSession> StartAsync(string gameName, string mimeType, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(mimeType)) throw new ArgumentException("mimeType is required.", nameof(mimeType));
            var setup = await _api.JsonAsync("Live setup", "POST", _env.GamesApi + "/tupiclive/streams/setup",
                new Dictionary<string, object> { ["game_name"] = gameName ?? "TupicGame Stream", ["game_id"] = _gameId }, ct).ConfigureAwait(false);
            var channelId = setup.Str("channel_id") ?? throw new TupicApiException("Live setup returned no channel", 0);
            var streamKey = setup.Str("stream_key") ?? throw new TupicApiException("Live setup returned no stream key", 0);
            var rtmpUrl = setup.Str("rtmp_url");
            await _api.JsonAsync("Live relay start", "POST", _env.GamesApi + "/tupiclive/relay/start", new Dictionary<string, object>
            {
                ["channel_id"] = channelId, ["rtmp_url"] = rtmpUrl, ["stream_key"] = streamKey, ["mime_type"] = mimeType,
            }, ct).ConfigureAwait(false);
            TupicLog.Info($"Tupic Live: relay started on channel {channelId}");
            return new TupicLiveSession(_env, _api, channelId, setup.Str("stream_id"), mimeType);
        }
    }

    public sealed class TupicLiveSession
    {
        readonly TupicEnvironment _env;
        readonly TupicApi _api;
        readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        public string ChannelId { get; }
        public string StreamId { get; }
        public string MimeType { get; }
        public bool IsActive { get; private set; } = true;

        internal TupicLiveSession(TupicEnvironment env, TupicApi api, string channelId, string streamId, string mimeType)
        {
            _env = env; _api = api; ChannelId = channelId; StreamId = streamId; MimeType = mimeType;
        }

        /// <summary>Sends one recorded chunk. False when the session is over (stopped here, or the relay ended: 409).</summary>
        public async Task<bool> SendChunkAsync(byte[] chunk, CancellationToken ct = default)
        {
            if (!IsActive) return false;
            if (chunk == null || chunk.Length == 0) return true;
            var spec = new HttpRequestSpec
            {
                Method = "POST", Url = _env.GamesApi + "/tupiclive/relay/chunk/" + Uri.EscapeDataString(ChannelId),
                Body = chunk, ContentType = "application/octet-stream",
            };
            spec.Headers["X-TupicLive-Mime-Type"] = MimeType;
            var r = await _api.SendAsync(spec, ct).ConfigureAwait(false);
            if (r.Status == 409)
            {
                IsActive = false;
                TupicLog.Info($"Tupic Live: relay is no longer active on channel {ChannelId}; session ended");
                return false;
            }
            if (!r.Ok) throw TupicApiException.From("Live chunk", r);
            return true;
        }

        /// <summary>Stops the relay. Safe to call more than once.</summary>
        public async Task StopAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!IsActive) return;
                IsActive = false;
                await _api.JsonAsync("Live relay stop", "POST", _env.GamesApi + "/tupiclive/relay/stop",
                    new Dictionary<string, object> { ["channel_id"] = ChannelId }, ct).ConfigureAwait(false);
                TupicLog.Info($"Tupic Live: relay stopped on channel {ChannelId}");
            }
            finally { _lock.Release(); }
        }
    }
}
