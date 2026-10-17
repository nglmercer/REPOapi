#!/usr/bin/env bash
set -euo pipefail
REPO_API_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_API_ROOT"
DOTNET_BIN="${DOTNET:-dotnet}"
"$DOTNET_BIN" build src/RepoRestApi/RepoRestApi.csproj -c Release --nologo -p:ContinuousIntegrationBuild=true
REPO_API_STAGE="$(mktemp -d)"
trap 'rm -rf -- "$REPO_API_STAGE"' EXIT
mkdir -p "$REPO_API_STAGE/BepInEx/plugins/RepoRestApi" "$REPO_API_STAGE/BepInEx/plugins/REPOLib" "$REPO_API_STAGE/docs" "$REPO_API_STAGE/third-party" artifacts
REPO_API_OUTPUT="$REPO_API_ROOT/src/RepoRestApi/bin/Release/netstandard2.1"
cp "$REPO_API_OUTPUT/RepoRestApi.dll" "$REPO_API_OUTPUT/RepoRestApi.Core.dll" "$REPO_API_OUTPUT/Newtonsoft.Json.dll" "$REPO_API_STAGE/BepInEx/plugins/RepoRestApi/"
cp "$REPO_API_OUTPUT/REPOLib.dll" "$REPO_API_STAGE/BepInEx/plugins/REPOLib/"
cp README.md LICENSE "$REPO_API_STAGE/"
cp docs/*.md docs/openapi.json "$REPO_API_STAGE/docs/"
cp third-party/* "$REPO_API_STAGE/third-party/"
cp -R examples "$REPO_API_STAGE/examples"
# Create a new archive so removed files cannot survive from an older package.
rm -f -- "$REPO_API_ROOT/artifacts/RepoRestApi-1.0.0.zip"
if command -v zip >/dev/null 2>&1; then
  (cd "$REPO_API_STAGE" && zip -qr "$REPO_API_ROOT/artifacts/RepoRestApi-1.0.0.zip" .)
else
  python3 - "$REPO_API_STAGE" "$REPO_API_ROOT/artifacts/RepoRestApi-1.0.0.zip" <<'PYZIP'
import sys, zipfile
from pathlib import Path
root = Path(sys.argv[1])
with zipfile.ZipFile(sys.argv[2], "w", zipfile.ZIP_DEFLATED) as archive:
    for source in sorted(root.rglob("*")):
        if source.is_file():
            archive.write(source, source.relative_to(root))
PYZIP
fi
sha256sum artifacts/RepoRestApi-1.0.0.zip > artifacts/RepoRestApi-1.0.0.zip.sha256
printf 'Package: %s/artifacts/RepoRestApi-1.0.0.zip\n' "$REPO_API_ROOT"
