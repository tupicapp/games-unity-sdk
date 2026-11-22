# Contributing

Thank you for helping. Until a licence is chosen (see `LICENSE`), outside contributions cannot be accepted. You can
still open an issue to report a bug or ask for a feature.

## How changes are made

1. Open an issue first for anything larger than a small fix, so we can agree on the approach.
2. Write the failing test first, then the change. Core tests: `dotnet test dotnet/Tupic.Core.Tests`.
3. Keep `Runtime/Core` free of Unity types: it must build as plain netstandard2.1.
4. Keep the SDK WebGL-safe: no threads or thread pool, no `System.Net` sockets in code that runs in a WebGL build.
5. Never log a token or a secret; use `TupicLog`, which redacts them.
6. Update `CHANGELOG.md`, and bump `version` in `unity/com.tupic.sdk/package.json` following Semantic Versioning.
7. Open a pull request. The code owners review it.
