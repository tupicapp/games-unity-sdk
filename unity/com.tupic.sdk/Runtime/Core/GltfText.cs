// Tupic SDK core: 3D models stored as text. A model is a glTF 2.0 JSON document whose single buffer is embedded as a
// base64 data: URI, so the whole model is one JSON value games-core can store, hash and serve. External buffer files
// are refused (a stored model must be self-contained and must never make a client fetch an arbitrary URL); external
// image URIs are allowed and reported as texture references.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tupic.Core
{
    public static class GltfText
    {
        public const string Generator = "Tupic SDK";
        public const string DataUriPrefix = "data:application/octet-stream;base64,";
        /// <summary>Largest glTF document (UTF-8 bytes) accepted for upload.</summary>
        public const int MaxDocumentBytes = 15 * 1024 * 1024;

        const int Float = 5126, UInt32 = 5125, ArrayBuffer = 34962, ElementArrayBuffer = 34963, Triangles = 4;

        /// <summary>Converts a Wavefront OBJ (v, vt, vn, f; polygons fan-triangulated; negative indices) to glTF 2.0 JSON.</summary>
        public static string FromObj(string objText, string name)
        {
            if (objText == null) throw new ArgumentNullException(nameof(objText));
            var positions = new List<float[]>();
            var texcoords = new List<float[]>();
            var normals = new List<float[]>();
            var faces = new List<int[][]>(); // per face: corners of (v, vt, vn), 0-based, -1 when absent

            var lineNo = 0;
            foreach (var raw in objText.Split('\n'))
            {
                lineNo++;
                var line = raw.Trim();
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash).Trim();
                if (line.Length == 0) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "v": positions.Add(Floats(parts, 3, 3, lineNo)); break;
                    case "vt": texcoords.Add(Floats(parts, 1, 2, lineNo)); break;
                    case "vn": normals.Add(Floats(parts, 3, 3, lineNo)); break;
                    case "f":
                        if (parts.Length < 4) throw new FormatException($"OBJ line {lineNo}: a face needs at least 3 vertices.");
                        var corners = new int[parts.Length - 1][];
                        for (var i = 1; i < parts.Length; i++)
                        {
                            var idx = parts[i].Split('/');
                            corners[i - 1] = new[]
                            {
                                Index(idx[0], positions.Count, lineNo, true),
                                idx.Length > 1 ? Index(idx[1], texcoords.Count, lineNo, false) : -1,
                                idx.Length > 2 ? Index(idx[2], normals.Count, lineNo, false) : -1,
                            };
                        }
                        faces.Add(corners);
                        break;
                    // o, g, s, usemtl, mtllib, l, p ... carry no geometry this converter keeps.
                }
            }
            if (faces.Count == 0) throw new FormatException("OBJ has no faces.");

            // An attribute is kept only when every face corner has it (glTF needs it on every vertex).
            bool hasUv = true, hasNormal = true;
            foreach (var f in faces)
                foreach (var c in f) { hasUv &= c[1] >= 0; hasNormal &= c[2] >= 0; }

            // One glTF vertex per distinct (v, vt, vn) combination.
            var map = new Dictionary<(int, int, int), uint>();
            var outPos = new List<float>();
            var outNrm = new List<float>();
            var outUv = new List<float>();
            var indices = new List<uint>();
            foreach (var f in faces)
            {
                var ids = new uint[f.Length];
                for (var i = 0; i < f.Length; i++)
                {
                    var key = (f[i][0], hasUv ? f[i][1] : -1, hasNormal ? f[i][2] : -1);
                    if (!map.TryGetValue(key, out var id))
                    {
                        id = (uint)map.Count;
                        map[key] = id;
                        outPos.AddRange(positions[key.Item1]);
                        if (hasNormal) outNrm.AddRange(normals[key.Item3]);
                        if (hasUv)
                        {
                            var t = texcoords[key.Item2];
                            outUv.Add(t[0]);
                            outUv.Add(1f - t[1]); // OBJ's V origin is bottom-left, glTF's top-left
                        }
                    }
                    ids[i] = id;
                }
                for (var i = 1; i + 1 < ids.Length; i++) { indices.Add(ids[0]); indices.Add(ids[i]); indices.Add(ids[i + 1]); }
            }
            var vertexCount = map.Count;

            // Buffer: POSITION | NORMAL | TEXCOORD_0 | indices. Every section is a multiple of 4 bytes, so all aligned.
            var bin = new List<byte>();
            var bufferViews = new List<object>();
            var accessors = new List<object>();
            var attributes = new Dictionary<string, object>();

            int AddView(int byteLength, int target)
            {
                bufferViews.Add(new Dictionary<string, object>
                {
                    ["buffer"] = 0, ["byteOffset"] = bin.Count - byteLength, ["byteLength"] = byteLength, ["target"] = target,
                });
                return bufferViews.Count - 1;
            }

            int AddFloatAccessor(List<float> data, int width, string type, bool minMax)
            {
                foreach (var v in data) bin.AddRange(LittleEndian(BitConverter.GetBytes(v)));
                var view = AddView(data.Count * 4, ArrayBuffer);
                var acc = new Dictionary<string, object>
                {
                    ["bufferView"] = view, ["byteOffset"] = 0, ["componentType"] = Float, ["count"] = data.Count / width, ["type"] = type,
                };
                if (minMax)
                {
                    var min = new double[width];
                    var max = new double[width];
                    for (var k = 0; k < width; k++) { min[k] = double.PositiveInfinity; max[k] = double.NegativeInfinity; }
                    for (var i = 0; i < data.Count; i++)
                    {
                        var k = i % width;
                        var v = ShortestDouble(data[i]);
                        if (v < min[k]) min[k] = v;
                        if (v > max[k]) max[k] = v;
                    }
                    acc["min"] = new List<object>(Array.ConvertAll(min, x => (object)x));
                    acc["max"] = new List<object>(Array.ConvertAll(max, x => (object)x));
                }
                accessors.Add(acc);
                return accessors.Count - 1;
            }

            attributes["POSITION"] = AddFloatAccessor(outPos, 3, "VEC3", true);
            if (hasNormal) attributes["NORMAL"] = AddFloatAccessor(outNrm, 3, "VEC3", false);
            if (hasUv) attributes["TEXCOORD_0"] = AddFloatAccessor(outUv, 2, "VEC2", false);

            foreach (var i in indices) bin.AddRange(LittleEndian(BitConverter.GetBytes(i)));
            var indexView = AddView(indices.Count * 4, ElementArrayBuffer);
            accessors.Add(new Dictionary<string, object>
            {
                ["bufferView"] = indexView, ["byteOffset"] = 0, ["componentType"] = UInt32, ["count"] = indices.Count, ["type"] = "SCALAR",
                ["min"] = new List<object> { 0L }, ["max"] = new List<object> { (long)(vertexCount - 1) },
            });
            var indexAccessor = accessors.Count - 1;

            var meshName = string.IsNullOrWhiteSpace(name) ? "model" : name;
            var doc = new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = Generator },
                ["scene"] = 0,
                ["scenes"] = new List<object> { new Dictionary<string, object> { ["nodes"] = new List<object> { 0 } } },
                ["nodes"] = new List<object> { new Dictionary<string, object> { ["mesh"] = 0, ["name"] = meshName } },
                ["meshes"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["name"] = meshName,
                        ["primitives"] = new List<object>
                        {
                            new Dictionary<string, object> { ["attributes"] = attributes, ["indices"] = indexAccessor, ["mode"] = Triangles },
                        },
                    },
                },
                ["buffers"] = new List<object>
                {
                    new Dictionary<string, object> { ["byteLength"] = bin.Count, ["uri"] = DataUriPrefix + Convert.ToBase64String(bin.ToArray()) },
                },
                ["bufferViews"] = bufferViews,
                ["accessors"] = accessors,
            };
            return Json.Serialize(doc);
        }

        /// <summary>Vertex and triangle counts of all mesh primitives, and the external (non-data:) image URIs.</summary>
        public static (int vertexCount, int triangleCount, string[] textureRefs) Stats(string gltfJson)
        {
            var doc = Json.ParseObject(gltfJson ?? throw new ArgumentNullException(nameof(gltfJson)));
            var accessors = doc.Arr("accessors") ?? new List<object>();
            long vertices = 0, triangles = 0;
            foreach (var m in doc.Arr("meshes") ?? new List<object>())
            {
                foreach (var p in (m as Dictionary<string, object>).Arr("primitives") ?? new List<object>())
                {
                    var prim = p as Dictionary<string, object>;
                    var attrs = prim.Obj("attributes");
                    if (attrs == null || !attrs.ContainsKey("POSITION")) continue;
                    var vc = AccessorCount(accessors, attrs.Long("POSITION", -1));
                    vertices += vc;
                    var n = prim.ContainsKey("indices") && prim["indices"] != null ? AccessorCount(accessors, prim.Long("indices", -1)) : vc;
                    switch (prim.Long("mode", Triangles))
                    {
                        case Triangles: triangles += n / 3; break;
                        case 5: case 6: triangles += Math.Max(0, n - 2); break; // strip, fan
                    }
                }
            }
            var refs = new List<string>();
            foreach (var i in doc.Arr("images") ?? new List<object>())
            {
                var uri = (i as Dictionary<string, object>).Str("uri");
                if (!string.IsNullOrEmpty(uri) && !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) refs.Add(uri);
            }
            return ((int)vertices, (int)triangles, refs.ToArray());
        }

        /// <summary>Throws ArgumentException unless the document is a self-contained glTF 2.0 text model of at most MaxDocumentBytes.</summary>
        public static void Validate(string gltfJson)
        {
            if (string.IsNullOrEmpty(gltfJson)) throw new ArgumentException("glTF document is empty.", nameof(gltfJson));
            var size = Encoding.UTF8.GetByteCount(gltfJson);
            if (size > MaxDocumentBytes)
                throw new ArgumentException($"glTF document is {size} bytes; the limit is {MaxDocumentBytes} bytes.", nameof(gltfJson));
            Dictionary<string, object> doc;
            try { doc = Json.ParseObject(gltfJson); }
            catch (FormatException e) { throw new ArgumentException("glTF document is not a JSON object: " + e.Message, nameof(gltfJson)); }
            var version = doc.Obj("asset").Str("version");
            if (version != "2.0") throw new ArgumentException($"glTF asset.version must be \"2.0\" (got \"{version}\").", nameof(gltfJson));

            var buffers = doc.Arr("buffers") ?? new List<object>();
            for (var i = 0; i < buffers.Count; i++)
            {
                var b = buffers[i] as Dictionary<string, object> ?? throw new ArgumentException($"buffers[{i}] is not an object.", nameof(gltfJson));
                var uri = b.Str("uri");
                if (string.IsNullOrEmpty(uri))
                    throw new ArgumentException($"buffers[{i}] has no uri (a binary GLB chunk); text models embed buffers as data: URIs.", nameof(gltfJson));
                if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"buffers[{i}].uri is not a data: URI; external buffer files are not allowed.", nameof(gltfJson));
                var comma = uri.IndexOf(',');
                if (comma < 0 || uri.Substring(0, comma).IndexOf(";base64", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new ArgumentException($"buffers[{i}].uri must be a base64 data: URI.", nameof(gltfJson));
                byte[] bytes;
                try { bytes = Convert.FromBase64String(uri.Substring(comma + 1)); }
                catch (FormatException) { throw new ArgumentException($"buffers[{i}].uri is not valid base64.", nameof(gltfJson)); }
                if (bytes.Length < b.Long("byteLength", 0))
                    throw new ArgumentException($"buffers[{i}] holds {bytes.Length} bytes but declares byteLength {b.Long("byteLength", 0)}.", nameof(gltfJson));
            }
        }

        static long AccessorCount(List<object> accessors, long index) =>
            index >= 0 && index < accessors.Count ? (accessors[(int)index] as Dictionary<string, object>).Long("count", 0) : 0;

        static float[] Floats(string[] parts, int min, int take, int lineNo)
        {
            if (parts.Length - 1 < min) throw new FormatException($"OBJ line {lineNo}: '{parts[0]}' needs {min} numbers.");
            var r = new float[take];
            for (var i = 0; i < take && i + 1 < parts.Length; i++)
                if (!float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]) || float.IsNaN(r[i]) || float.IsInfinity(r[i]))
                    throw new FormatException($"OBJ line {lineNo}: '{parts[i + 1]}' is not a number.");
            return r;
        }

        static int Index(string token, int count, int lineNo, bool required)
        {
            if (token.Length == 0)
            {
                if (required) throw new FormatException($"OBJ line {lineNo}: face corner without a vertex index.");
                return -1;
            }
            if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i) || i == 0)
                throw new FormatException($"OBJ line {lineNo}: bad index '{token}'.");
            var zeroBased = i > 0 ? i - 1 : count + i; // negative: relative to the elements defined so far
            if (zeroBased < 0 || zeroBased >= count) throw new FormatException($"OBJ line {lineNo}: index {i} is out of range.");
            return zeroBased;
        }

        static byte[] LittleEndian(byte[] b)
        {
            if (!BitConverter.IsLittleEndian) Array.Reverse(b);
            return b;
        }

        // The shortest decimal that round-trips the float (0.1f -> 0.1, not 0.10000000149011612).
        static double ShortestDouble(float f) => double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
