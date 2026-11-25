# Tupic Game SDK for Unity

Makes a Unity game a Tupic game: sign-in, scores and analytics, regional culture data, reward stats, Live
streaming, assets, 3D models stored as text (glTF), and a WebGL build that plays on Tupic Games like any web game.

- Unity package: `unity/com.tupic.sdk` (UPM, Unity 6.3 / `6000.3`).
- Engine-agnostic core: `unity/com.tupic.sdk/Runtime/Core` (plain C#, netstandard2.1, no Unity types). The same
  sources build as a .NET library in `dotnet/`, where they are unit-tested.

## Install

In Unity: **Window → Package Manager → + → Add package from git URL…**, then:

```
https://github.com/tupicapp/games-unity-sdk.git?path=unity/com.tupic.sdk#v0.1.0
```

Or add it to `Packages/manifest.json`:

```json
{ "dependencies": { "com.tupic.sdk": "https://github.com/tupicapp/games-unity-sdk.git?path=unity/com.tupic.sdk#v0.1.0" } }
```

Pin a tag (`#v0.1.0`). Without one you get whatever `main` is at the time.

## Quick start

1. **Tupic → Create Sample Scene** creates `Assets/Scenes/TupicSample.unity`: "Tupic Stars", collect the stars.
   It has scores, levels, and a text model spinning.
2. Press Play. In the Editor, the game has no host page, so it plays as a guest unless you sign in (below).
3. Your own game: add the `TupicGame` component to a GameObject and call it from your game code:

```csharp
var tupic = GetComponent<Tupic.Unity.TupicGame>();
tupic.AddScore(10);
tupic.CompleteLevel();          // analytics: level_complete
tupic.EndGame("victory");       // analytics: game_end, then the score
tupic.NewGame();                // play again: a new session

var theme = await tupic.Client.Insights.GetThemeAsync("AE");    // null when Insights has nothing
var mesh  = Tupic.Unity.TupicModelMesh.FromText(textAsset.text); // a text-stored model
```

## Setup

- **Game id:** set `TupicGame.gameId` in the Inspector. A build hosted on Tupic reads the id from the play frame
  instead.
- **Environment:** `TupicEnvironment` holds the service URLs (sign-in, games, assets, insights, chain).
  - `TupicEnvironment.Local()` is the default. It is a development stack on `*.tupic.app`.
  - `TupicEnvironment.Remote(name, domain, allowRemote: true)` reaches a real Tupic environment. It must be asked
    for explicitly, so a test or a sample never reaches one by accident.
  - Known limitation in 0.1.0: the `TupicGame` component always uses `Local()`. To reach another environment, create
    a `TupicClient` yourself.
- **Sign-in:**
  - Standalone and Editor builds sign in with Keycloak: authorization code with PKCE (S256), on a loopback redirect
    (`http://127.0.0.1:<port>/…`, see `TupicAuth.BeginLogin`). Tokens refresh 30 s before they expire.
  - A WebGL build hosted on Tupic gets the signed-in user's token from the play frame (`window.__TUPIC_TOKEN`).
    The token never travels in a URL.
  - The OIDC client id defaults to `games-unity`.

## The Tupic menu (Unity Editor)

- **Tupic → Create Sample Scene:** the sample scene above, added to the build settings.
- **Tupic → Import OBJ as Text Model…:** converts an `.obj` file to glTF 2.0 JSON with embedded buffers, plus
  metadata (vertex and triangle counts, texture references, the source file's SHA-256, creator, time). The result is
  saved as a TextAsset under `Assets/Resources/TupicModels`.
- **Tupic → Build WebGL for Tupic:** a WebGL build with compression disabled, plus a `tupic-unity.json` manifest,
  zipped as `tupic-unity-webgl.zip`. Upload the zip to your game on Tupic Games, and it plays there.
- **Batch:** `Unity -batchmode -quit -projectPath <project> -executeMethod Tupic.Editor.TupicBatch.SampleBuild -tupicOut <dir>`.

## How the SDK uses each Tupic service

| Service | What the SDK does | Calls |
|---|---|---|
| **Scores and analytics** (Tupic Games) | Sends play events (`game_start`, `level_complete`, `score_update`, `game_pause`, `game_resume`, `game_end`; any other name is refused before sending) and submits the score at the end of a run. | `POST /analytics/event`, `POST /games/{id}/scores` |
| **Insights** | Reads a country's cultural calendar and theme, to localise a game. Can register the game with its genre, country, URL and asset URLs. Uses the user's token. | `GET /geography/countries/{cc}/calendar`, `GET /themes/country/{cc}`, `POST /game/register` |
| **Chain** | **Read-only:** reward types, and reward stats for a wallet address. The SDK never pays or sends a transaction; rewards are decided on the server. | `GET /rewards/types`, `GET /rewards/stats/{address}` |
| **Live** | Streams the game through Tupic Games' Live relay: sets up a stream, sends recorded chunks, stops. It depends on the Live service being available in the environment you use. | `POST /tupiclive/streams/setup`, `/tupiclive/relay/start`, `/chunk/{channel}`, `/stop` |
| **Assets** | Uploads a file: creates the asset, then PUTs the bytes to the upload URL it returns. Visibility defaults to `public`. | `POST /assets`, then `PUT <upload_url>` |
| **Models** | Saves and loads 3D models stored as glTF 2.0 JSON text on the game, the same format the web engine reads. | games API, `/games/{id}/models` |

**Score rules (Tupic Games):**
- A score needs a **signed-in user**; a guest's score is refused.
- The player is the token's user. A name sent in the body is ignored.
- Each play session submits **once**; a second submit for the same session is refused (409).
- The session id must be 8–100 characters from `A–Z a–z 0–9 _ . : -`.

The SDK follows these rules from 0.1.1 (see CHANGELOG).

## Tests

The core's unit tests run with .NET 8: `dotnet test dotnet/Tupic.Core.Tests`. `tools/dotnet-test.sh` does the
same through WSL with the Windows .NET SDK.

## Releasing

- A release is a SemVer tag `vX.Y.Z` on a green `main` commit: GitHub **Releases** → new release with the tag, or
  `git tag vX.Y.Z <commit> && git push origin vX.Y.Z`. The pull request before bumps `version` in
  `unity/com.tupic.sdk/package.json` and `CHANGELOG.md` to match.
- The tag is the version; nothing is published. Games pin it in the git URL (`#vX.Y.Z`, see Install) and move to a
  new one in a bump pull request (Dependabot does not read Unity's `Packages/manifest.json`).
- Fixes go to `main`, then the next patch tag. Only when `main` already holds an unreleased breaking change: branch
  `hotfix/` from the last tag, fix it, tag the patch there, and merge its pull request into `main` with a merge commit.
  No release branches, no cherry-picks.
- Never move or delete a published tag.

## Other engines

The core is plain C#. Godot 4 (.NET) can use `Runtime/Core` with its own HTTP transport. See
`docs/ARCHITECTURE.md`.

## Licence, security, contributing

- Licence: not decided yet (see `LICENSE`).
- To report a vulnerability, see `SECURITY.md`.
- To contribute, see `CONTRIBUTING.md`.
