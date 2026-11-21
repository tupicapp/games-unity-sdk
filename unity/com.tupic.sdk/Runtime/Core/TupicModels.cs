// Tupic SDK core: a game's 3D models in games-core, stored as glTF 2.0 text (see GltfText). The engine layer loads the
// returned glTF JSON itself (in Unity, the importer does).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tupic.Core
{
    public sealed class ModelInfo
    {
        public string Id;
        public string GameId;
        public string Name;
        public string Format;
        public string FormatVersion;
        public long VertexCount;
        public long TriangleCount;
        public string[] TextureRefs = Array.Empty<string>();
        public string SourceSha256;
        public string SourceFile;
        public string CreatedBy;
        public DateTimeOffset? CreatedAt;
        public long SizeBytes;

        public static ModelInfo FromDictionary(Dictionary<string, object> o)
        {
            if (o == null) return null;
            var refs = new List<string>();
            foreach (var r in o.Arr("texture_refs") ?? new List<object>()) if (r != null) refs.Add(Convert.ToString(r, CultureInfo.InvariantCulture));
            return new ModelInfo
            {
                Id = o.Str("id"),
                GameId = o.Str("game_id"),
                Name = o.Str("name"),
                Format = o.Str("format"),
                FormatVersion = o.Str("format_version"),
                VertexCount = o.Long("vertex_count"),
                TriangleCount = o.Long("triangle_count"),
                TextureRefs = refs.ToArray(),
                SourceSha256 = o.Str("source_sha256"),
                SourceFile = o.Str("source_file"),
                CreatedBy = o.Str("created_by"),
                CreatedAt = DateTimeOffset.TryParse(o.Str("created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : (DateTimeOffset?)null,
                SizeBytes = o.Long("size_bytes"),
            };
        }
    }

    public sealed class ModelDocument
    {
        public ModelInfo Info;
        /// <summary>The glTF 2.0 document as JSON text.</summary>
        public string GltfJson;
    }

    public sealed class TupicModels
    {
        readonly TupicEnvironment _env;
        readonly TupicApi _api;
        readonly string _gameId;

        internal TupicModels(TupicEnvironment env, TupicApi api, string gameId) { _env = env; _api = api; _gameId = gameId; }

        string Root => _env.GamesApi + "/games/" + Uri.EscapeDataString(_gameId) + "/models";

        /// <summary>Converts an OBJ file to glTF text, validates it and stores it. sha256 is of the original OBJ bytes.</summary>
        public Task<ModelInfo> UploadObjAsync(byte[] objBytes, string fileName, string name, CancellationToken ct = default)
        {
            if (objBytes == null || objBytes.Length == 0) throw new ArgumentException("The OBJ file is empty.", nameof(objBytes));
            var text = new UTF8Encoding(false).GetString(objBytes).TrimStart('﻿');
            var gltf = GltfText.FromObj(text, name);
            return UploadGltfAsync(gltf, objBytes, fileName, name, ct);
        }

        /// <summary>Stores a glTF 2.0 text document. sourceBytes is the original file it was made from (hashed, not sent).</summary>
        public async Task<ModelInfo> UploadGltfAsync(string gltfJson, byte[] sourceBytes, string fileName, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required.", nameof(name));
            if (sourceBytes == null) throw new ArgumentNullException(nameof(sourceBytes));
            GltfText.Validate(gltfJson);
            var body = new Dictionary<string, object>
            {
                ["name"] = name,
                ["format"] = "gltf",
                ["format_version"] = "2.0",
                ["gltf"] = Json.ParseObject(gltfJson),
                ["source_sha256"] = TupicAssets.Sha256Hex(sourceBytes),
                ["source_file"] = Path.GetFileName(fileName ?? ""),
            };
            var r = await _api.JsonAsync("Model upload", "POST", Root, body, ct);
            var info = ModelInfo.FromDictionary(r.Obj("model")) ?? throw new TupicApiException("Model upload returned no model", 0);
            TupicLog.Info($"Tupic model stored: {info.Id} ({info.TriangleCount} triangles)");
            return info;
        }

        public async Task<List<ModelInfo>> ListAsync(CancellationToken ct = default)
        {
            var r = await _api.JsonAsync("Model list", "GET", Root, null, ct);
            var list = new List<ModelInfo>();
            foreach (var m in r.Arr("models") ?? new List<object>())
                if (m is Dictionary<string, object> o) list.Add(ModelInfo.FromDictionary(o));
            return list;
        }

        public async Task<ModelDocument> GetAsync(string modelId, CancellationToken ct = default)
        {
            var r = await _api.JsonAsync("Model get", "GET", Root + "/" + TupicApi.Segment(modelId, nameof(modelId)), null, ct);
            var m = r.Obj("model") ?? throw new TupicApiException("Model get returned no model", 0);
            var gltf = m.Obj("gltf") ?? throw new TupicApiException("Model get returned no glTF document", 0);
            return new ModelDocument { Info = ModelInfo.FromDictionary(m), GltfJson = Json.Serialize(gltf) };
        }

        /// <summary>The model's glTF 2.0 JSON text, ready for the engine's importer.</summary>
        public async Task<string> LoadAsync(string modelId, CancellationToken ct = default) =>
            (await GetAsync(modelId, ct)).GltfJson;

        public Task DeleteAsync(string modelId, CancellationToken ct = default) =>
            _api.JsonAsync("Model delete", "DELETE", Root + "/" + TupicApi.Segment(modelId, nameof(modelId)), null, ct);
    }
}
