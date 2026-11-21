# Architecture

```
Runtime/Core (netstandard2.1, engine-agnostic)        Runtime/Unity (UnityEngine)          Editor
  Json, Http (IHttpTransport), TupicEnvironment          UnityWebRequestTransport            TupicMenu (sample, OBJ import,
  TupicAuth (PKCE, refresh, host token)                  TupicHost (+ Plugins/WebGL           WebGL build + manifest)
  TupicClient: Games, Analytics, Insights, Chain,          TupicBridge.jslib)                 TupicBatch (CI)
    Assets, Live, Models                                 TupicGame, TupicModelMesh,
  GltfText (OBJ -> glTF text, Stats, Validate, Decode)   TupicSampleGame
```

- **Transport**: the core never touches the network itself; Unity uses UnityWebRequest (works on WebGL), tests use a
  fake transport, a desktop tool can use HttpClientTransport.
- **Auth**: standalone/Editor builds sign in with authorization code + PKCE (S256) on a loopback redirect; tokens
  refresh 30 s before expiry. Hosted WebGL builds take the token from the play frame (`window.__TUPIC_TOKEN`, handed
  over by games-web), the same way web games do (`TupicAuth.UseHostToken`). The password grant exists only for the
  local stack's tests.
- **Environment guard**: the local environment (`*.tupic.app`) is the default; a remote one needs `allowRemote`.
- **Hosting on Tupic**: games-core stores the uploaded Build/ files, saves a host page (Unity loader) as the game's
  latest build, and the play frame injects game id, token handoff and the Live bridge; games-web plays it in the
  usual iframe.
- **Models**: glTF 2.0 JSON, buffers as base64 data: URIs, stored by games-core (`/games/<id>/models`) with metadata;
  the SDK uploads/loads them, the web engine (TupicModels) and Unity (TupicModelMesh) build meshes from the same text.
  Nothing goes to assets-core.
- **Export from the web**: games-core `/games/<id>/export/unity-project` produces a Unity project with this package
  embedded, the game's data and a generic runtime.

## Other engines

The core is plain C#: Godot 4 (.NET) can use Runtime/Core as is with a Godot HTTPRequest transport and a
JavaScriptBridge host bridge for its web export. Unreal would need a C++ port of the same HTTP contracts (they are
small: see TupicClient and the games-core routes); the glTF text format loads through Unreal's glTF importer.
