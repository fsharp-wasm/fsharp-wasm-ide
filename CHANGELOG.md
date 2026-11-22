# Changelog

All notable changes to this repository are documented here (newest first).

## [Unreleased]

### Added
- **Undo and redo** (`Edit.History`): every change of the elements is one `Replace` of the whole set in the log of FSharp.Wasm.Edit, and the log
  keeps the selection too, so undoing an addition or a deletion puts it back where it was. The moves of one drag, the width of one drag, the
  keystrokes into one text and the nudges of one element are one step each. Buttons in the bar (disabled while there is nothing to undo or redo),
  and Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) on the canvas.
- **Drag and drop from the palette** (`FSharp.Graphics.Interact`): an entry is dragged onto the surface, where a frame at the size the element will
  have shows where it lands; it lands where it is let go, on the 8 px grid, and is selected. A click on an entry still adds it (cascaded), Escape
  cancels a drag. The palette shows an icon per kind and the drag has a name tag at the pointer.
- **Keyboard and screen reader order**: the regions of the editor (toolbar, elements, surface, inspector) are semantic regions in that order, every
  element on the surface is one control with its name, and the designed pictures are hidden from the tab order (they are what is being designed).
- Checks, measured 2026-11-22: the session test has 24 checks (undo and redo, selection restore, the drag and drop paths, Escape, a click), the
  Chromium test 16 (undo and redo compared pixel for pixel below the bar, a palette drag, Ctrl+Z, Escape). The module is 245 369 bytes.

### Changed
- The libraries are pinned at the revisions that have the keyboard and pointer routing the editor now relies on (fsharp-wasm-graphics 8f09eff:
  keys and the wheel go to the handlers of the current frame, `Role.None` hides a subtree; fsharp-wasm-runtime 5b1fd6a: Undo / Redo shortcuts;
  fsharp-wasm-core 529ec49: `Ops.insertAt`) and the compiler at 65d6d2e (a string as `seq<char>`, equality of `bool[]` and `byte[]`).
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
