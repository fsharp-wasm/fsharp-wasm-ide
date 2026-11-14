#!/usr/bin/env bash
# build.sh — compile the widget editor to WebAssembly GC:  dist/WidgetEditor.wasm
#
#   bash build/build.sh [--out dist]
#
# The compiler is not part of this repository; first hit wins:
#   FSHARP_WASM_BIN   the self-contained `fsharp-wasm` executable of a release (it carries FSharp.Wasm.Core beside it)
#   FSHARP_WASM_DIR   a checkout of fsharp-wasm: its CLI runs through `dotnet run`, and needs FSHARP_COMPILER = the patched
#                     fsc.dll built from the checkout's vendor/fsharp (the compiler's README has the details)
# `wasm-tools` must be on the PATH.  The page that runs the module: web/index.html (bash build/serve.sh).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
source "$ROOT/build/env.sh"
OUT="$ROOT/dist"
while [[ $# -gt 0 ]]; do
  case $1 in
    --out) mkdir -p "$2"; OUT="$(cd "$2" && pwd)"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done
command -v wasm-tools >/dev/null || { echo "wasm-tools not found (cargo install wasm-tools)" >&2; exit 1; }

PROJECT="$ROOT/src/WidgetEditor.Browser"
mkdir -p "$OUT"
if [[ -n "${FSHARP_WASM_BIN:-}" ]]; then
  "$FSHARP_WASM_BIN" "$PROJECT" --outDir "$OUT" --noCache
elif [[ -n "${FSHARP_WASM_DIR:-}" ]]; then
  dotnet run -c Release --project "$FSHARP_WASM_DIR/tools/FSharp.Wasm.Cli" -- "$PROJECT" --outDir "$OUT" --noCache
else
  echo "no compiler: set FSHARP_WASM_BIN (a release binary) or FSHARP_WASM_DIR (a fsharp-wasm checkout)" >&2
  exit 1
fi
wasm-tools validate "$OUT/WidgetEditor.wasm" --features all
echo "built: $OUT/WidgetEditor.wasm ($(wc -c < "$OUT/WidgetEditor.wasm") bytes)"
