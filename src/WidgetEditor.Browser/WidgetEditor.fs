/// The module that becomes WidgetEditor.wasm: the exports are what runtime/gfx.js (`mount`) calls.
///   start()                         bind the canvas
///   onResize(w, h, ratio)           CSS size of the canvas and the device pixel ratio
///   onPointer(kind, x, y, button)   kind 0 down, 1 up, 2 move, 3 leave
///   onWheel(dx, dy)
///   onKey(kind)                     kind 0 down, 1 up; the key name is fetched with Gfx.pendingKey
///   frame(nowMs)                    build, resolve, draw (when the model changed, an event was taken, or a caret blinks: see CanvasHost.Frame)
module FSharp.Wasm.Ide.Browser.WidgetEditor

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.Canvas
open FSharp.Wasm.Ide.WidgetEditor

/// All mutable state of the page, in one object (a module-level `let mutable` is a wasm global; this keeps it together).
type Editor() =
    let text = CanvasTextEngine() :> ITextEngine
    let host = CanvasHost text
    let app = App text
    let mutable seen = -1

    member _.Resize(w: float32, h: float32, r: float32) = host.Resize(w, h, r)
    member _.Input(evt: InputEvent) = host.Input evt
    member _.Frame(nowMs: float) =
        // the model changes in `app.Dispatch`, which a handler calls; an event no handler took cannot change it, but this keeps it true whatever calls Dispatch
        if app.Version <> seen then
            seen <- app.Version
            host.Invalidate()
        host.Frame(nowMs, false, 0.0f, fun time size -> app.Build(time, size))

let private editor = Editor()

[<CompiledName("start")>]
let start () : unit = Gfx.init "gfx"

[<CompiledName("onResize")>]
let onResize (w: float) (h: float) (ratio: float) : unit = editor.Resize(float32 w, float32 h, float32 ratio)

[<CompiledName("onPointer")>]
let onPointer (kind: int) (x: float) (y: float) (button: int) : unit =
    let p = Vec2.create (float32 x) (float32 y)
    let evt =
        match kind with
        | 0 -> PointerDown(p, button)
        | 1 -> PointerUp(p, button)
        | 2 -> PointerMove p
        | _ -> PointerLeave p
    editor.Input evt |> ignore

[<CompiledName("onWheel")>]
let onWheel (dx: float) (dy: float) : unit = editor.Input(Scroll(Vec2.create (float32 dx) (float32 dy))) |> ignore

[<CompiledName("onKey")>]
let onKey (kind: int) : unit =
    let key = Gfx.pendingKey ()
    editor.Input(if kind = 0 then KeyDown key else KeyUp key) |> ignore

[<CompiledName("frame")>]
let frame (nowMs: float) : unit = editor.Frame nowMs
