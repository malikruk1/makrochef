#!/usr/bin/env bash
set -euo pipefail
STEP="${1:?вкажи пункт}"
echo "=== GATE $STEP ==="
dotnet build --nologo -warnaserror
dotnet test --nologo
case "$STEP" in
  2.3) curl -fsS localhost:8080/health | grep -q '"db":"ok"' ;;
  5.1) dotnet test --nologo --filter "FullyQualifiedName~SolverTests" ;;
esac
echo "=== GATE $STEP PASSED ==="
