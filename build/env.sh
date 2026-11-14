# source build/env.sh — the environment of a build in this repository.
#
# fsharp-wasm-graphics finds the FSharp.Wasm.Core sources (the F# base library every program compiled by fsharp-wasm is built
# with) through FSHARP_WASM_CORE_DIR; here they are the `lib/fsharp-wasm-core` submodule.
#
#   git submodule update --init lib/graphics lib/runtime lib/fsharp-wasm-core
#   git -C lib/graphics submodule update --init lib/GlyphAura        # only for the headless session test (software renderer)
#
# The compiler is not a submodule: build/build.sh takes a release binary (FSHARP_WASM_BIN) or a checkout (FSHARP_WASM_DIR).
_ide_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export FSHARP_WASM_CORE_DIR="${FSHARP_WASM_CORE_DIR:-$_ide_root/lib/fsharp-wasm-core}"
unset _ide_root
