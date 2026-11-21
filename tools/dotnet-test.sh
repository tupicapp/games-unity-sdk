#!/usr/bin/env bash
# Run the core unit tests with the Windows .NET SDK through WSL interop. The sources are copied to a Windows path
# (D:\tupic\sdk-build) because dotnet.exe on \\wsl$ paths is slow and unreliable. Offline restore from the local
# NuGet cache (pinned versions); nothing is installed.
set -euo pipefail
SRC="$(cd "$(dirname "$0")/.." && pwd)"
DST=/mnt/d/tupic/sdk-build
mkdir -p "$DST"
rsync -a --delete --exclude bin --exclude obj --exclude .git "$SRC/dotnet/" "$DST/dotnet/"
mkdir -p "$DST/unity/com.tupic.sdk/Runtime/Core"
rsync -a --delete "$SRC/unity/com.tupic.sdk/Runtime/Core/" "$DST/unity/com.tupic.sdk/Runtime/Core/"
cd "$DST/dotnet/Tupic.Core.Tests"
"/mnt/c/Program Files/dotnet/dotnet.exe" test --nologo -v q --logger "trx;LogFileName=core-tests.trx" "$@"
