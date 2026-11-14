/// Headless session test of the widget editor: a scripted pointer and keyboard session runs through the real hit tester and
/// key path, the scenes are rendered by the software renderer, and the model is checked after each step.  Exit code 1 when a
/// check fails.  `dotnet run --project tests/WidgetEditor.Session [out-dir]` writes the screenshots of the session to out-dir.
module FSharp.Wasm.Ide.WidgetEditor.Session.Program

open System
open System.IO
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.Render
open FSharp.Graphics.Host
open FSharp.Graphics.Widgets
open FSharp.Wasm.Ide.WidgetEditor

let white = Color.rgba 255uy 255uy 255uy 255uy

[<EntryPoint>]
let main argv =
    let out = if argv.Length > 0 then argv.[0] else Path.Combine(Path.GetTempPath(), "widget-editor-shots")
    Directory.CreateDirectory out |> ignore
    let fonts = FontLocator.loadDefault ()
    let text = GlyphAuraTextEngine fonts :> ITextEngine
    let mutable failures = 0

    let save (name: string) (w: int) (h: int) (buf: byte[]) (len: int) =
        let r = SoftwareRenderer(w, h, fonts)
        match CommandReader.run r buf len with
        | Decoded n ->
            File.WriteAllBytes(Path.Combine(out, name + ".png"), Png.encode w h (r.ToRgba8 white))
            printfn "%-22s %7d bytes  %5d cmds" name len n
        | Malformed(off, msg) ->
            failures <- failures + 1
            eprintfn "%s: MALFORMED at %d: %s" name off msg

    // the widget editor driven through the real hit-tester, key path and software renderer
    let w, h = 1280, 800
    let app = App text
    let stage = Stage(text, float32 w, float32 h)
    let mutable t = 0.0f
    let frame (name: string option) =
        let struct (buf, len) = stage.Frame(app.Build(t, Vec2.create (float32 w) (float32 h)))
        name |> Option.iter (fun n -> save n w h buf len)
        t <- t + 100.0f
    let move x y = frame None; stage.Input(PointerMove(Vec2.create x y)) |> ignore
    let click x y =
        move x y
        frame None
        stage.Input(PointerDown(Vec2.create x y, 0)) |> ignore
        frame None
        stage.Input(PointerUp(Vec2.create x y, 0)) |> ignore
    let drag (x0: float32) y0 x1 y1 =
        move x0 y0
        frame None
        stage.Input(PointerDown(Vec2.create x0 y0, 0)) |> ignore
        for i in 1 .. 6 do
            let k = float32 i / 6.0f
            frame None
            stage.Input(PointerMove(Vec2.create (x0 + (x1 - x0) * k) (y0 + (y1 - y0) * k))) |> ignore
        frame None
        stage.Input(PointerUp(Vec2.create x1 y1, 0)) |> ignore
    let key k = frame None; stage.Input(KeyDown k) |> ignore

    frame (Some "editor-1-initial")
    let before = app.Model
    // drag the "Sign in" heading (node 1) 160px right / 40px down
    drag 250.0f 112.0f 410.0f 152.0f
    frame (Some "editor-2-dragged")
    let n1 (m: Model) = m.Nodes |> List.find (fun n -> n.Id = 1)
    printfn "drag: heading %A -> %A" (n1 before |> fun n -> n.X, n.Y) (n1 app.Model |> fun n -> n.X, n.Y)
    let check (what: string) (ok: bool) =
        printfn "check %-34s %s" what (if ok then "ok" else "FAILED")
        if not ok then failures <- failures + 1
    check "pointer drag moves the element" ((n1 app.Model).X = (n1 before).X + 160.0f && (n1 app.Model).Y = (n1 before).Y + 40.0f)
    // add a slider from the palette, then open the code panel
    click 100.0f 336.0f
    frame (Some "editor-3-added")
    check "palette click adds + selects" (app.Model.Nodes.Length = before.Nodes.Length + 1 && app.Model.Selected = Some 6)
    // focus the inspector text field of a button and type into it through the key path
    app.Dispatch (Select (Some 5))
    app.Dispatch (UiMsg (UiMsg.Focus "insp-text"))
    for k in [ "Backspace"; "Backspace"; "O"; "K"; "Space"; "Number1" ] do key k
    check "inspector text editing via keys" ((app.Model.Nodes |> List.find (fun n -> n.Id = 5)).Text = "Sign ok 1")
    // Shift is tracked through the real key path (ShiftLeft down/up)
    app.Dispatch (Select (Some 5))
    app.Dispatch (UiMsg (UiMsg.Focus "insp-text"))
    key "ShiftLeft"; key "A"; key "Number1"
    stage.Input(KeyUp "ShiftLeft") |> ignore
    key "B"
    check "Shift gives capitals and symbols" ((app.Model.Nodes |> List.find (fun n -> n.Id = 5)).Text = "Sign ok 1A!b")
    app.Dispatch (UiMsg (UiMsg.Blur))
    // Tab walks the inspector controls; arrows then adjust the focused slider
    app.Dispatch (Select (Some 6))
    key "Tab"
    check "Tab moves focus (x slider)" (app.Model.Ui.Focus = Some "insp-x")
    let x0 = (app.Model.Nodes |> List.find (fun n -> n.Id = 6)).X
    key "Right"
    check "arrow adjusts focused slider" ((app.Model.Nodes |> List.find (fun n -> n.Id = 6)).X = x0 + 8.0f)
    app.Dispatch (UiMsg (UiMsg.Blur))
    app.Dispatch (Select (Some 6))
    app.Dispatch ToggleCode
    frame (Some "editor-4-code")
    app.Dispatch ToggleCode
    app.Dispatch TogglePreview
    frame (Some "editor-5-preview")
    // pointer-driven slider: click the preview-mode slider track at 75 % of its width
    app.Dispatch (UiMsg (UiMsg.Blur))
    if not app.Model.Preview then app.Dispatch TogglePreview
    frame None
    let sl = app.Model.Nodes |> List.find (fun n -> n.Id = 6)
    // canvas origin is (190, 52); slider track spans X + 8 .. X + W - 8
    let cx = 190.0f + sl.X + 8.0f + (sl.W - 16.0f) * 0.75f
    click cx (52.0f + sl.Y + 10.0f)
    check "slider click jumps to local position" (abs ((app.Model.Nodes |> List.find (fun n -> n.Id = 6)).Value - 0.75f) < 0.02f)
    if failures > 0 then 1 else 0
