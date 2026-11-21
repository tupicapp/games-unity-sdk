// Tupic SDK core: every game asset lives in assets-core. Upload = create the asset (user token), then PUT the bytes
// to the presigned upload URL it returns. The presigned URL carries its own signature, so no token is sent there.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tupic.Core
{
    public sealed class AssetRef
    {
        public string AssetId;
        public string Url;
        public string MimeType;
        public string Sha256;
        /// <summary>Where the file came from in the project (e.g. Assets/Art/player.png); for the editor, never sent.</summary>
        public string SourcePath;

        public Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
        {
            ["asset_id"] = AssetId, ["url"] = Url, ["mime_type"] = MimeType, ["sha256"] = Sha256, ["source_path"] = SourcePath,
        };

        public static AssetRef FromDictionary(Dictionary<string, object> o) => o == null ? null : new AssetRef
        {
            AssetId = o.Str("asset_id"), Url = o.Str("url"), MimeType = o.Str("mime_type"), Sha256 = o.Str("sha256"), SourcePath = o.Str("source_path"),
        };
    }

    /// <summary>A game's map of logical names to uploaded assets, stored as JSON next to the build.</summary>
    public sealed class AssetManifest
    {
        public string GameId;
        public Dictionary<string, AssetRef> Entries = new Dictionary<string, AssetRef>();

        public string ToJson()
        {
            var entries = new Dictionary<string, object>();
            foreach (var kv in Entries) entries[kv.Key] = kv.Value?.ToDictionary();
            return Json.Serialize(new Dictionary<string, object> { ["version"] = 1, ["game_id"] = GameId, ["entries"] = entries });
        }

        public static AssetManifest FromJson(string json)
        {
            var o = Json.ParseObject(json);
            var m = new AssetManifest { GameId = o.Str("game_id") };
            var entries = o.Obj("entries");
            if (entries != null)
                foreach (var kv in entries) m.Entries[kv.Key] = AssetRef.FromDictionary(kv.Value as Dictionary<string, object>);
            return m;
        }
    }

    public sealed class TupicAssets
    {
        readonly TupicEnvironment _env;
        readonly TupicApi _api;
        readonly IHttpTransport _http;

        internal TupicAssets(TupicEnvironment env, TupicApi api, IHttpTransport http) { _env = env; _api = api; _http = http; }

        public async Task<AssetRef> UploadAsync(byte[] bytes, string fileName, string mimeType, string visibility = "public",
            CancellationToken ct = default)
        {
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("Nothing to upload.", nameof(bytes));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName is required.", nameof(fileName));
            if (string.IsNullOrWhiteSpace(mimeType)) throw new ArgumentException("mimeType is required.", nameof(mimeType));
            var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            var sha = Sha256Hex(bytes);
            var created = await _api.JsonAsync("Asset create", "POST", _env.AssetsApi + "/assets", new Dictionary<string, object>
            {
                ["name"] = Path.GetFileName(fileName),
                ["extension"] = ext,
                ["mime_type"] = mimeType,
                ["license_type"] = "free",
                ["visibility"] = visibility,
                ["service"] = "games",
                ["source_client"] = _env.ClientId,
                ["file_size"] = bytes.LongLength,
            }, ct);

            var asset = created.Obj("asset") ?? created;
            var id = asset.Str("id") ?? throw new TupicApiException("Asset create returned no asset id", 0);
            var uploadUrl = created.Str("upload_url") ?? throw new TupicApiException("Asset create returned no upload URL", 0);
            var put = await _http.SendAsync(new HttpRequestSpec
            {
                Method = "PUT", Url = uploadUrl, Body = bytes, ContentType = created.Str("upload_content_type") ?? mimeType,
            }, ct);
            // The presigned URL is a credential: never let it reach a log or an exception message.
            if (!put.Ok) throw new TupicApiException($"Asset upload failed ({put.Status}) for asset {id}", put.Status);
            TupicLog.Info($"Tupic asset uploaded: {id} ({bytes.Length} bytes)");
            return new AssetRef { AssetId = id, Url = asset.Str("url"), MimeType = mimeType, Sha256 = sha };
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(bytes ?? Array.Empty<byte>());
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
