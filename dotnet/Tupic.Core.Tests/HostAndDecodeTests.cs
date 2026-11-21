using System;
using System.Linq;
using System.Threading.Tasks;
using Tupic.Core;
using Xunit;

public class HostAndDecodeTests
{
    const string Cube = "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 0 0 1\nv 1 0 1\nv 1 1 1\nv 0 1 1\n" +
                        "f 1 2 3 4\nf 5 6 7 8\nf 1 2 6 5\nf 2 3 7 6\nf 3 4 8 7\nf 4 1 5 8\n";

    [Fact]
    public async Task HostTokenIsUsedWithoutAnyTokenRequest()
    {
        var t = new FakeTransport();
        var auth = new TupicAuth(TupicEnvironment.Local(), t);
        auth.UseHostToken("host-at", DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.Equal("host-at", await auth.GetAccessTokenAsync());
        Assert.Empty(t.Requests);
    }

    [Fact]
    public async Task ExpiredHostTokenAsksTheHostAgain()
    {
        var auth = new TupicAuth(TupicEnvironment.Local(), new FakeTransport());
        auth.UseHostToken("old", DateTimeOffset.UtcNow.AddSeconds(5)); // inside the refresh margin, no refresh token
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => auth.GetAccessTokenAsync());
        Assert.Contains("host", e.Message);
        Assert.Throws<ArgumentException>(() => auth.UseHostToken("", DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public void DecodeGivesMeshArrays()
    {
        var gltf = GltfText.FromObj(Cube, "cube");
        var m = GltfText.Decode(gltf);
        Assert.Equal(m.Positions.Length / 3, GltfText.Stats(gltf).vertexCount);
        Assert.Equal(36, m.Indices.Length);
        Assert.All(m.Indices, i => Assert.InRange(i, 0, m.Positions.Length / 3 - 1));
        Assert.Equal(1f, m.Positions.Max());
        Assert.Equal(0f, m.Positions.Min());
    }

    [Fact]
    public void DecodeRefusesExternalBuffers()
    {
        var gltf = GltfText.FromObj(Cube, "cube");
        var bad = System.Text.RegularExpressions.Regex.Replace(gltf, "\"uri\"\\s*:\\s*\"data:[^\"]*\"", "\"uri\":\"https://evil/x.bin\"");
        Assert.Throws<ArgumentException>(() => GltfText.Decode(bad));
    }
}
