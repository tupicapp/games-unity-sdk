# Changelog

All notable changes to the Tupic Game SDK. The format follows Keep a Changelog, and versions follow Semantic
Versioning (tags `vMAJOR.MINOR.PATCH`).

## [0.1.0] - 2026-11-22

First version.

### Core (engine-agnostic C#)
- JSON, an HTTP transport interface, log redaction (tokens and secrets never reach a log), and an environment guard
  (a real environment must be asked for explicitly).
- Sign-in with Keycloak: authorization code with PKCE (S256), refresh before expiry, and the host page's token for
  games hosted on Tupic.
- Scores and analytics (accepted event names only), Insights (calendar, theme, game registration), Chain
  (read-only reward stats), Assets (create plus upload), Live (relay), Models (glTF text: OBJ import, stats,
  validation, decode).

### Unity
- UnityWebRequest transport, WebGL host bridge, `TupicGame` component, text-model meshes, and the "Tupic Stars"
  sample.
- Tupic menu: sample scene, OBJ import, WebGL build for Tupic, batch entry point.
