# Changelog

All notable changes to this repository are documented here (newest first).

## [Unreleased]

### Added
- **Widget editor** (`src/WidgetEditor`): a visual editor for `FSharp.Graphics.Widgets` layouts — a palette, a design surface, an
  inspector, a preview mode and an export to F# — moved here from the GUI builder demo of fsharp-wasm-graphics. As a library it takes a
  time and a size and gives a `Scene` (`App.Build`), so any host can draw it.
- **Browser module** (`src/WidgetEditor.Browser`): `WidgetEditor.wasm`, 153 KB, built with `bash build/build.sh`; it draws through
  `FSharp.Graphics.Canvas` and `runtime/gfx.js`. Runs on the pages of fsharp-wasm-runtime.
- **Session test** (`tests/WidgetEditor.Session`): the scripted pointer and keyboard session (drag, palette click, key editing, Shift,
  Tab focus, arrow keys on a slider, slider click) through the real hit tester, seven checks, five screenshots.
- **Chromium test** (`tests/e2e/editor.mjs`) and a development page (`web/index.html`, `bash build/serve.sh`).
- CI: the .NET build and the session test on every push; the module compiled by the compiler (checked out at a fixed revision, its
  patched F# compiler cached) and driven in Chromium.
- Apache License 2.0 (`LICENSE`, `NOTICE`).
