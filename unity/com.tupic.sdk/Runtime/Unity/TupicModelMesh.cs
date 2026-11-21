// Builds a Unity Mesh from a text-stored model (glTF 2.0 JSON, buffers as data: URIs), the same document the web
// engine loads. glTF is right-handed; Unity is left-handed: X is mirrored and the winding reversed.
using Tupic.Core;
using UnityEngine;

namespace Tupic.Unity
{
    public static class TupicModelMesh
    {
        public static Mesh FromText(string gltfJson, string name = "TupicModel")
        {
            var d = GltfText.Decode(gltfJson);
            var n = d.Positions.Length / 3;
            var v = new Vector3[n];
            for (var i = 0; i < n; i++) v[i] = new Vector3(-d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]);
            var tri = new int[d.Indices.Length];
            for (var i = 0; i + 2 < tri.Length; i += 3) { tri[i] = d.Indices[i]; tri[i + 1] = d.Indices[i + 2]; tri[i + 2] = d.Indices[i + 1]; }
            var mesh = new Mesh { name = name, indexFormat = n > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.vertices = v;
            if (d.Uvs != null)
            {
                var uv = new Vector2[n];
                for (var i = 0; i < n; i++) uv[i] = new Vector2(d.Uvs[i * 2], 1f - d.Uvs[i * 2 + 1]);
                mesh.uv = uv;
            }
            mesh.triangles = tri;
            if (d.Normals != null)
            {
                var nn = new Vector3[n];
                for (var i = 0; i < n; i++) nn[i] = new Vector3(-d.Normals[i * 3], d.Normals[i * 3 + 1], d.Normals[i * 3 + 2]);
                mesh.normals = nn;
            }
            else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
