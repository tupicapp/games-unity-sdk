using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tupic.Core;
using Xunit;

public class ModelTests
{
    static readonly TupicEnvironment Env = TupicEnvironment.Local("games-unity");
    const string Game = "g-1";

    const string CubeObj = @"# unit cube
o Cube
v -1 -1 -1
v  1 -1 -1
v  1  1 -1
v -1  1 -1
v -1 -1  1
v  1 -1  1
v  1  1  1
v -1  1  1
f 1 4 3 2
f 5 6 7 8
f 1 2 6 5
f 2 3 7 6
f 3 4 8 7
f 4 1 5 8
";

    static Dictionary<string, object> At(List<object> list, long i) => (Dictionary<string, object>)list[(int)i];
    static double[] Nums(Dictionary<string, object> o, string key) => o.Arr(key).Select(x => Convert.ToDouble(x)).ToArray();

    [Fact]
    public void CubeObjBecomesASelfContainedGltfDocument()
    {
        var gltf = GltfText.FromObj(CubeObj, "cube");
        var doc = Json.ParseObject(gltf);
        Assert.Equal("2.0", doc.Obj("asset").Str("version"));
        Assert.Equal("Tupic SDK", doc.Obj("asset").Str("generator"));
        Assert.Single(doc.Arr("meshes"));
        var prim = At(At(doc.Arr("meshes"), 0).Arr("primitives"), 0);
        var attrs = prim.Obj("attributes");
        Assert.False(attrs.ContainsKey("NORMAL"));
        Assert.False(attrs.ContainsKey("TEXCOORD_0"));

        var buffers = doc.Arr("buffers");
        Assert.Single(buffers);
        var uri = At(buffers, 0).Str("uri");
        Assert.StartsWith("data:application/octet-stream;base64,", uri);
        var bin = Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
        Assert.Equal(At(buffers, 0).Long("byteLength"), bin.Length);
        Assert.Equal(8 * 3 * 4 + 36 * 4, bin.Length);

        var accessors = doc.Arr("accessors");
        var views = doc.Arr("bufferViews");
        var pos = At(accessors, attrs.Long("POSITION"));
        Assert.Equal(5126L, pos.Long("componentType"));
        Assert.Equal("VEC3", pos.Str("type"));
        Assert.Equal(8L, pos.Long("count"));
        Assert.Equal(new[] { -1.0, -1.0, -1.0 }, Nums(pos, "min"));
        Assert.Equal(new[] { 1.0, 1.0, 1.0 }, Nums(pos, "max"));
        var idx = At(accessors, prim.Long("indices"));
        Assert.Equal(5125L, idx.Long("componentType"));
        Assert.Equal("SCALAR", idx.Str("type"));
        Assert.Equal(36L, idx.Long("count"));

        var posView = At(views, pos.Long("bufferView"));
        var idxView = At(views, idx.Long("bufferView"));
        Assert.Equal(0L, posView.Long("byteOffset"));
        Assert.Equal(96L, posView.Long("byteLength"));
        Assert.Equal(96L, idxView.Long("byteOffset"));
        Assert.Equal(144L, idxView.Long("byteLength"));
        Assert.All(views.Cast<Dictionary<string, object>>(), v => Assert.Equal(0, v.Long("byteOffset") % 4));

        // The bytes are what the accessors say: first position (-1,-1,-1), indices all < 8, first quad (1 4 3 2 -> ids 0 1 2 3) fanned 0,1,2 / 0,2,3.
        Assert.Equal(-1f, BitConverter.ToSingle(bin, 0));
        var indices = Enumerable.Range(0, 36).Select(i => BitConverter.ToUInt32(bin, 96 + i * 4)).ToArray();
        Assert.All(indices, i => Assert.InRange(i, 0u, 7u));
        Assert.Equal(new uint[] { 0, 1, 2, 0, 2, 3 }, indices.Take(6));

        var (vertices, triangles, textures) = GltfText.Stats(gltf);
        Assert.Equal(8, vertices);
        Assert.Equal(12, triangles);
        Assert.Empty(textures);
        GltfText.Validate(gltf);
    }

    [Fact]
    public void NormalsTexcoordsNegativeIndicesAndNgons()
    {
        // A pentagon (one ngon -> 3 triangles) written with negative (relative) indices, v/vt/vn on every corner.
        var obj = new StringBuilder();
        for (var i = 0; i < 5; i++)
        {
            var a = i * 2 * Math.PI / 5;
            obj.Append(FormattableString.Invariant($"v {Math.Cos(a):0.######} {Math.Sin(a):0.######} 0.5\n"));
            obj.Append(FormattableString.Invariant($"vt {i / 4.0:0.##} 0.25\n"));
        }
        obj.Append("vn 0 0 1\n");
        obj.Append("f -5/-5/-1 -4/-4/-1 -3/-3/-1 -2/-2/-1 -1/-1/-1\n");
        var doc = Json.ParseObject(GltfText.FromObj(obj.ToString(), "pentagon"));
        var prim = At(At(doc.Arr("meshes"), 0).Arr("primitives"), 0);
        var attrs = prim.Obj("attributes");
        var accessors = doc.Arr("accessors");
        Assert.Equal(5L, At(accessors, attrs.Long("NORMAL")).Long("count"));
        Assert.Equal("VEC2", At(accessors, attrs.Long("TEXCOORD_0")).Str("type"));
        Assert.Equal(9L, At(accessors, prim.Long("indices")).Long("count"));
        var pos = At(accessors, attrs.Long("POSITION"));
        Assert.Equal(0.5, Nums(pos, "min")[2]);
        Assert.Equal(1.0, Nums(pos, "max")[0]);

        // V is flipped for glTF (OBJ 0.25 -> 0.75).
        var uri = At(doc.Arr("buffers"), 0).Str("uri");
        var bin = Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
        var uvView = At(doc.Arr("bufferViews"), At(accessors, attrs.Long("TEXCOORD_0")).Long("bufferView"));
        Assert.Equal(0.75f, BitConverter.ToSingle(bin, (int)uvView.Long("byteOffset") + 4));
        Assert.Equal((5, 3), (GltfText.Stats(Json.Serialize(doc)).vertexCount, GltfText.Stats(Json.Serialize(doc)).triangleCount));
    }

    [Fact]
    public void BadObjIsAFormatError()
    {
        Assert.Throws<FormatException>(() => GltfText.FromObj("v 0 0 0\n", "empty"));
        Assert.Throws<FormatException>(() => GltfText.FromObj("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 4\n", "out of range"));
        Assert.Throws<FormatException>(() => GltfText.FromObj("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 -4\n", "negative out of range"));
    }

    [Fact]
    public void ValidateRejectsExternalBuffers()
    {
        var doc = Json.ParseObject(GltfText.FromObj(CubeObj, "cube"));
        ((Dictionary<string, object>)doc.Arr("buffers")[0])["uri"] = "https://evil/x.bin";
        var e = Assert.Throws<ArgumentException>(() => GltfText.Validate(Json.Serialize(doc)));
        Assert.Contains("data:", e.Message);

        Assert.Throws<ArgumentException>(() => GltfText.Validate(
            "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":4,\"uri\":\"https://evil/x.bin\"}]}"));
        Assert.Throws<ArgumentException>(() => GltfText.Validate("{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":4}]}"));
        Assert.Throws<ArgumentException>(() => GltfText.Validate("{\"asset\":{\"version\":\"1.0\"}}"));
    }

    [Fact]
    public void ValidateRejectsDocumentsOver15MB()
    {
        var big = "{\"asset\":{\"version\":\"2.0\"},\"extras\":\"" + new string('a', GltfText.MaxDocumentBytes) + "\"}";
        var e = Assert.Throws<ArgumentException>(() => GltfText.Validate(big));
        Assert.Contains("limit", e.Message);
    }

    [Fact]
    public void StatsListsExternalTexturesButNotEmbeddedOnes()
    {
        var doc = Json.ParseObject(GltfText.FromObj(CubeObj, "cube"));
        doc["images"] = new List<object>
        {
            new Dictionary<string, object> { ["uri"] = "textures/crate.png" },
            new Dictionary<string, object> { ["uri"] = "data:image/png;base64,iVBORw0KGgo=" },
        };
        var s = GltfText.Stats(Json.Serialize(doc));
        Assert.Equal(new[] { "textures/crate.png" }, s.textureRefs);

        // Without indices, triangles = vertex count / 3.
        var noIdx = "{\"asset\":{\"version\":\"2.0\"},\"accessors\":[{\"count\":9}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}]}";
        Assert.Equal((9, 3), (GltfText.Stats(noIdx).vertexCount, GltfText.Stats(noIdx).triangleCount));
    }

    // ── games-core model store ──

    static async Task<(TupicClient client, FakeTransport t)> Client(Action<FakeTransport> routes)
    {
        var t = new FakeTransport().On("POST", Env.TokenEndpoint, 200,
            new Dictionary<string, object> { ["access_token"] = "user-at", ["expires_in"] = 300, ["refresh_token"] = "rt" });
        routes(t);
        var client = new TupicClient(Env, t, Game);
        await client.Auth.LoginWithPasswordForTestsAsync("u", "p");
        return (client, t);
    }

    static Dictionary<string, object> Meta(string id = "m-1") => new Dictionary<string, object>
    {
        ["id"] = id, ["game_id"] = Game, ["name"] = "Crate", ["format"] = "gltf", ["format_version"] = "2.0",
        ["vertex_count"] = 8, ["triangle_count"] = 12, ["texture_refs"] = new List<object>(), ["source_sha256"] = "ab",
        ["source_file"] = "crate.obj", ["created_by"] = "user-1", ["created_at"] = "2026-11-22T10:00:00Z", ["size_bytes"] = 1234,
    };

    [Fact]
    public async Task UploadObjPostsTheGltfObjectAndTheSourceHash()
    {
        var (c, t) = await Client(f => f.On("POST", Env.GamesApi + "/games/g-1/models", 201, new Dictionary<string, object> { ["model"] = Meta() }));
        var objBytes = Encoding.UTF8.GetBytes(CubeObj);
        var info = await c.Models.UploadObjAsync(objBytes, "Models/crate.obj", "Crate");
        var req = t.Requests.Last();
        Assert.Equal("POST", req.Method);
        Assert.Equal(Env.GamesApi + "/games/g-1/models", req.Url);
        Assert.Equal("Bearer user-at", req.Headers["Authorization"]);
        var body = FakeTransport.JsonBody(req);
        Assert.Equal("Crate", body["name"]);
        Assert.Equal("gltf", body["format"]);
        Assert.Equal("2.0", body["format_version"]);
        Assert.Equal("crate.obj", body["source_file"]);
        var sha = body.Str("source_sha256");
        Assert.Matches("^[0-9a-f]{64}$", sha);
        Assert.Equal(TupicAssets.Sha256Hex(objBytes), sha);
        var gltf = body.Obj("gltf"); // an object, not a JSON string
        Assert.NotNull(gltf);
        Assert.Equal("2.0", gltf.Obj("asset").Str("version"));
        Assert.Equal(12, GltfText.Stats(Json.Serialize(gltf)).triangleCount);

        Assert.Equal("m-1", info.Id);
        Assert.Equal(12, info.TriangleCount);
        Assert.Equal(8, info.VertexCount);
        Assert.Equal(1234, info.SizeBytes);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero), info.CreatedAt);
    }

    [Fact]
    public async Task ListLoadAndDeleteUseTheModelRoutes()
    {
        var stored = Json.ParseObject(GltfText.FromObj(CubeObj, "cube"));
        var full = Meta();
        full["gltf"] = stored;
        var (c, t) = await Client(f => f
            .On("GET", Env.GamesApi + "/games/g-1/models", 200, new Dictionary<string, object> { ["models"] = new List<object> { Meta("m-1"), Meta("m-2") } })
            .On("GET", Env.GamesApi + "/games/g-1/models/m-1", 200, new Dictionary<string, object> { ["model"] = full })
            .On("DELETE", Env.GamesApi + "/games/g-1/models/m-1", 200));
        var list = await c.Models.ListAsync();
        Assert.Equal(new[] { "m-1", "m-2" }, list.Select(m => m.Id));

        var gltf = await c.Models.LoadAsync("m-1");
        Assert.Equal(Env.GamesApi + "/games/g-1/models/m-1", t.Requests.Last().Url);
        GltfText.Validate(gltf);
        Assert.Equal(12, GltfText.Stats(gltf).triangleCount);
        var doc = await c.Models.GetAsync("m-1");
        Assert.Equal("Crate", doc.Info.Name);

        await c.Models.DeleteAsync("m-1");
        Assert.Equal("DELETE", t.Requests.Last().Method);
        Assert.All(t.Requests.Skip(1), r => Assert.Equal("Bearer user-at", r.Headers["Authorization"]));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Models.LoadAsync("../admin"));
    }

    [Fact]
    public async Task AModelWithAnExternalBufferIsNeverUploaded()
    {
        var (c, t) = await Client(f => f.On("POST", Env.GamesApi + "/games/g-1/models", 201, new Dictionary<string, object> { ["model"] = Meta() }));
        var evil = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":4,\"uri\":\"https://evil/x.bin\"}]}";
        await Assert.ThrowsAsync<ArgumentException>(() => c.Models.UploadGltfAsync(evil, new byte[] { 1 }, "x.gltf", "X"));
        Assert.DoesNotContain(t.Requests, r => r.Url.Contains("/models"));
    }
}
