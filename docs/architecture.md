# Architecture

## Layers

1. **`FSharp.Wasm.Ide.WidgetEditor`** — the editor, no host. `Model` holds the nodes of the layout, the selection and the UI state
   (`Msg` = what the user does); `Model.update` is a pure function. `View.view ui size model` builds a `Scene` from widgets of
   `FSharp.Graphics.Widgets`; the event handlers of that scene send messages (`App.Dispatch` runs `update` at once, the next frame's
   `Build` shows the new model). `Export` turns the nodes into F# source for the code panel. The library uses only the subset
   fsharp-wasm compiles, so the same sources run under .NET (the session test) and as WebAssembly GC.
2. **`FSharp.Graphics.Canvas`** (fsharp-wasm-graphics) — the host: `CanvasHost` owns the frame (size, input through the hit tester,
   build → resolve → emit → draw); `Gfx` are the imports of `runtime/gfx.js`; `CanvasTextEngine` measures text with the canvas.
3. **`WidgetEditor.Browser`** — the module: a `CanvasHost`, an `App` and the six exports `runtime/gfx.js` (`mount`) calls. Everything it
   needs from JavaScript is in `gfx.js`; a key arrives as a string through `Gfx.pendingKey` because a module can send strings out but
   not receive them.

## Testing

* `tests/WidgetEditor.Session` is the unit of behaviour: the same `App` under .NET, the software renderer instead of the canvas, the
  real `Stage.Input` (hit test, focus, key path). A change of the model shows up as a failed check; the PNGs are for the eyes.
* `tests/e2e/editor.mjs` is the unit of integration: the compiled module in Chromium. It cannot read widgets, so it judges the canvas
  (a hash of the pixels, the number of colours) after each action and checks that the canvas keeps the keyboard focus.

## Dependencies

`lib/graphics` (widgets, canvas host), `lib/runtime` (the JavaScript glue and the e2e helpers), `lib/fsharp-wasm-core` (the F# base
library the compiler builds programs with). The compiler is not a submodule; `build/build.sh` takes a release binary or a checkout.
