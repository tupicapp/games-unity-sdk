// The "Tupic" menu: sample scene, OBJ import as a text model (glTF 2.0 JSON with metadata), and the WebGL build in
// the format Tupic Games accepts (Build/ uncompressed + tupic-unity.json, zipped). Batch entry points for CI.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Tupic.Core;
using Tupic.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tupic.Editor
{
    public static class TupicMenu
    {
        const string SamplePath = "Assets/Scenes/TupicSample.unity";
        const string ModelsDir = "Assets/Resources/TupicModels";
        public const string SdkVersion = "0.2.0";

        // A "T" from two boxes, as OBJ: the sample's text model.
        const string LogoObj =
            "v -1 0.6 -0.2\nv 1 0.6 -0.2\nv 1 1 -0.2\nv -1 1 -0.2\nv -1 0.6 0.2\nv 1 0.6 0.2\nv 1 1 0.2\nv -1 1 0.2\n" +
            "v -0.2 -1 -0.2\nv 0.2 -1 -0.2\nv 0.2 0.6 -0.2\nv -0.2 0.6 -0.2\nv -0.2 -1 0.2\nv 0.2 -1 0.2\nv 0.2 0.6 0.2\nv -0.2 0.6 0.2\n" +
            "f 1 2 3 4\nf 8 7 6 5\nf 1 5 6 2\nf 2 6 7 3\nf 3 7 8 4\nf 4 8 5 1\n" +
            "f 9 10 11 12\nf 16 15 14 13\nf 9 13 14 10\nf 10 14 15 11\nf 11 15 16 12\nf 12 16 13 9\n";

        [MenuItem("Tupic/Create Sample Scene")]
        public static void CreateSampleScene()
        {
            var model = ImportObjText(LogoObj, "tupic_logo.obj", "TupicLogo");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("Tupic");
            go.AddComponent<TupicGame>();
            go.AddComponent<TupicSampleGame>().model = model;
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, SamplePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SamplePath, true) };
            Debug.Log("[Tupic] Sample scene: " + SamplePath);
        }

        [MenuItem("Tupic/Import OBJ as Text Model...")]
        public static void ImportObjMenu()
        {
            var path = EditorUtility.OpenFilePanel("OBJ model", "", "obj");
            if (!string.IsNullOrEmpty(path)) ImportObjFile(path);
        }

        /// <summary>OBJ file -> glTF 2.0 text (buffers embedded) + metadata, under Assets/Resources/TupicModels.</summary>
        public static TextAsset ImportObjFile(string path) =>
            ImportObjText(File.ReadAllText(path), Path.GetFileName(path), Path.GetFileNameWithoutExtension(path));

        static TextAsset ImportObjText(string obj, string sourceFile, string name)
        {
            var gltf = GltfText.FromObj(obj, name);
            GltfText.Validate(gltf);
            var stats = GltfText.Stats(gltf);
            Directory.CreateDirectory(ModelsDir);
            var asset = $"{ModelsDir}/{name}.gltf.txt";
            File.WriteAllText(asset, gltf);
            string sha;
            using (var h = SHA256.Create()) sha = string.Concat(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(obj)).Select(b => b.ToString("x2")));
            File.WriteAllText($"{ModelsDir}/{name}.meta.json", Json.Serialize(new System.Collections.Generic.Dictionary<string, object>
            {
                ["name"] = name, ["format"] = "gltf", ["format_version"] = "2.0", ["vertex_count"] = stats.vertexCount,
                ["triangle_count"] = stats.triangleCount, ["texture_refs"] = stats.textureRefs.Cast<object>().ToList(),
                ["source_file"] = sourceFile, ["source_sha256"] = sha, ["created_by"] = Environment.UserName,
                ["created_at"] = DateTime.UtcNow.ToString("o"),
            }));
            AssetDatabase.Refresh();
            Debug.Log($"[Tupic] Text model {asset}: {stats.vertexCount} vertices, {stats.triangleCount} triangles");
            return AssetDatabase.LoadAssetAtPath<TextAsset>(asset);
        }

        /// <summary>Shaders the runtime creates materials from by name must be in the build (Always Included Shaders).</summary>
        static void AlwaysInclude(params string[] names)
        {
            var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (sh == null) continue;
                var found = false;
                for (var i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == sh) found = true;
                if (found) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = sh;
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tupic/Build WebGL for Tupic")]
        public static void BuildMenu()
        {
            var dir = EditorUtility.SaveFolderPanel("Output folder", "", "TupicBuild");
            if (!string.IsNullOrEmpty(dir)) BuildWebGL(dir);
        }

        /// <summary>WebGL build (compression Disabled) + tupic-unity.json, zipped to &lt;outDir&gt;/tupic-unity-webgl.zip.</summary>
        public static string BuildWebGL(string outDir)
        {
            AlwaysInclude("Standard", "Unlit/Color");
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.companyName = "Tupic";
            var playerDir = Path.Combine(outDir, "WebGL");
            if (Directory.Exists(playerDir)) Directory.Delete(playerDir, true);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(scenes, playerDir, BuildTarget.WebGL, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("[Tupic] WebGL build failed: " + report.summary.result);
            var manifest = new System.Collections.Generic.Dictionary<string, object>
            {
                ["format"] = "tupic-unity-webgl", ["version"] = 1, ["product_name"] = PlayerSettings.productName,
                ["product_version"] = PlayerSettings.bundleVersion, ["unity_version"] = Application.unityVersion,
                ["sdk"] = new System.Collections.Generic.Dictionary<string, object> { ["name"] = "com.tupic.sdk", ["version"] = SdkVersion },
                ["build"] = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["loader"] = "Build/WebGL.loader.js", ["framework"] = "Build/WebGL.framework.js",
                    ["data"] = "Build/WebGL.data", ["code"] = "Build/WebGL.wasm",
                },
            };
            File.WriteAllText(Path.Combine(playerDir, "tupic-unity.json"), Json.Serialize(manifest));
            var zip = Path.Combine(outDir, "tupic-unity-webgl.zip");
            if (File.Exists(zip)) File.Delete(zip);
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(Path.Combine(playerDir, "tupic-unity.json"), "tupic-unity.json");
                foreach (var f in new[] { "WebGL.loader.js", "WebGL.framework.js", "WebGL.data", "WebGL.wasm" })
                    z.CreateEntryFromFile(Path.Combine(playerDir, "Build", f), "Build/" + f);
            }
            Debug.Log("[Tupic] Package for Tupic Games: " + zip);
            return zip;
        }
    }

    /// <summary>Batch mode: Unity -batchmode -quit -projectPath P -executeMethod Tupic.Editor.TupicBatch.SampleBuild -tupicOut DIR</summary>
    public static class TupicBatch
    {
        public static void SampleBuild()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-tupicOut");
            var outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("TupicBuild");
            TupicMenu.CreateSampleScene();
            TupicMenu.BuildWebGL(outDir);
        }
    }
}
