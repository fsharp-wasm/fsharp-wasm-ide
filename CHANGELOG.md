# Changelog

All notable changes to this repository are documented here (newest first).

## [Unreleased]

### Changed
- **A first visual pass on the editor** (`src/WidgetEditor/View.fs`, the layout and every hit area are unchanged): the mark and the name
  "Widget Editor" instead of "GUI Builder — demo", a top bar with a gradient and a shadow, shadows of the side panels onto the surface,
  a palette that shows what each element looks like (a small icon per kind), value chips in the inspector and the kind of the selected
  element, handles on all four corners of the selection and a readout of its size, drawn above every element (a neighbour no longer
  covers them), a quieter grid with a stronger line every fourth cell, a hint on an empty surface, and no "Loaded." banner on the
  development page once the module runs. What the issue "Widget editor: make it look polished" still lists is open: numeric entry in
  the inspector, rulers, a layers list, undo/redo buttons, theme-level changes (those belong to fsharp-wasm-graphics). The module is now
  169 KB. The compiler pin moves to the revision that builds it (a generic union declared in the program was a type mismatch in the
  revision before).

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
