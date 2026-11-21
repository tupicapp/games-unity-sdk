# Tupic SDK for Unity

Makes a Unity game a Tupic game: sign-in (Keycloak), scores and analytics (games-core), Insights (registration,
region, calendar, themes), Chain (read-only), Live (the web bridge), Assets (assets-core) and 3D models stored as
text (glTF 2.0 JSON in games-core). A WebGL build uploads to Tupic Games and plays in games-web like any web game.

Unity 6.3 (6000.3). Package: `unity/com.tupic.sdk` (UPM). Engine-agnostic core: `Runtime/Core` (netstandard2.1,
no Unity types), tested with dotnet in `dotnet/`.

## Install

Add the package folder to the project (Packages/com.tupic.sdk, or `"com.tupic.sdk": "file:<path>"` in
Packages/manifest.json).

## Use

```csharp
// On a GameObject: TupicGame (session, analytics, scores). Hosted on Tupic it reads the game id and the user's
// token from the play frame; in the Editor or standalone, sign in with Client.Auth (PKCE, loopback redirect).
var tupic = GetComponent<Tupic.Unity.TupicGame>();
tupic.AddScore(10);
tupic.CompleteLevel();
tupic.EndGame("victory");             // analytics game_end + score submission

var theme = await tupic.Client.Insights.GetThemeAsync("AE");   // null when Insights has nothing
var mesh = Tupic.Unity.TupicModelMesh.FromText(textAsset.text); // a text-stored model
```

## Menu: Tupic

- **Create Sample Scene**: "Tupic Stars" (collect the stars; scores, levels, a text model spinning).
- **Import OBJ as Text Model...**: OBJ -> glTF 2.0 JSON with embedded buffers + metadata (counts, texture refs,
  source SHA-256, creator, time) under Assets/Resources/TupicModels.
- **Build WebGL for Tupic**: WebGL (compression Disabled) + `tupic-unity.json`, zipped as `tupic-unity-webgl.zip`.
  Upload: `POST /games/api/v1/games/<id>/unity-build` (multipart `file`).

Batch: `Unity -batchmode -quit -projectPath P -executeMethod Tupic.Editor.TupicBatch.SampleBuild -tupicOut DIR`
(do not pass -nographics: the Personal licence then has no entitlement).

## Tests

`bash tools/dotnet-test.sh` (Windows dotnet through WSL; 44 tests).

## Not in this repo's scope

Rewards are never paid by a game: Chain is read-only here; rewards are decided server-side
(games-core docs/GAME_REWARDS_CONTRACTS.md).
