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
open FSharp.Graphics.Interact
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
    let selBeforeAdd = app.Model.Selected
    click 100.0f 336.0f
    frame (Some "editor-3-added")
    check "palette click adds + selects" (app.Model.Nodes.Length = before.Nodes.Length + 1 && app.Model.Selected = Some 6)
    // the log: the drag of the heading was one step (not one per pointer move), adding the element another
    let afterAdd = app.Model
    app.Dispatch Undo
    check "undo takes the added element out" (app.Model.Nodes.Length = before.Nodes.Length && Model.canRedo app.Model)
    check "undo puts the selection back" (app.Model.Selected = selBeforeAdd)
    app.Dispatch Undo
    check "undo takes the whole drag back, in one step" ((n1 app.Model).X = (n1 before).X && (n1 app.Model).Y = (n1 before).Y && not (Model.canUndo app.Model))
    app.Dispatch Redo
    app.Dispatch Redo
    check "redo brings both back, selected" (app.Model.Nodes = afterAdd.Nodes && app.Model.NextId = afterAdd.NextId && app.Model.Selected = afterAdd.Selected)
    // focus the inspector text field of a button and type into it through the key path
    app.Dispatch (Select (Some 5))
    app.Dispatch (UiMsg (UiMsg.Focus "insp-text"))
    for k in [ "Backspace"; "Backspace"; "O"; "K"; "Space"; "Number1" ] do key k
    check "inspector text editing via keys" ((app.Model.Nodes |> List.find (fun n -> n.Id = 5)).Text = "Sign ok 1")
    // the six keys are one step of the log
    let text5 () = (app.Model.Nodes |> List.find (fun n -> n.Id = 5)).Text
    app.Dispatch Undo
    check "undo takes the typing back in one step" (text5 () = "Sign in")
    app.Dispatch Redo
    check "redo types it again" (text5 () = "Sign ok 1")
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
    // an element on the surface is one control: focusing it selects it; the next stop of the keyboard is its inspector
    app.Dispatch (UiMsg (UiMsg.Focus "node-6"))
    check "focusing an element selects it" (app.Model.Selected = Some 6)
    frame None
    key "Tab"
    check "Tab leads from the element to its inspector (x slider)" (app.Model.Ui.Focus = Some "insp-x")
    let x0 = (app.Model.Nodes |> List.find (fun n -> n.Id = 6)).X
    key "Right"
    // the toolkit steps a focused slider by 5 % of its range; the editor keeps an element on its 8 px grid
    let x1 = (app.Model.Nodes |> List.find (fun n -> n.Id = 6)).X
    check "arrow adjusts focused slider" (x1 > x0 && x1 % 8.0f = 0.0f)
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
    // delete and the keys of undo and redo (what the browser sends for Ctrl+Z, Ctrl+Y) through the real key path
    app.Dispatch (UiMsg (UiMsg.Blur))
    app.Dispatch (Select (Some 6))
    let count = app.Model.Nodes.Length
    key "Delete"
    check "Delete removes the selected element" (app.Model.Nodes.Length = count - 1 && app.Model.Selected.IsNone)
    key "Undo"
    check "the key Undo brings it back" (app.Model.Nodes.Length = count)
    key "Redo"
    check "the key Redo removes it again" (app.Model.Nodes.Length = count - 1)
    app.Dispatch Undo
    app.Dispatch (Add KLabel)
    check "a new change ends what could be redone" (not (Model.canRedo app.Model))
    // dragging an entry of the palette onto the surface: a click adds it as before, a drag puts it where it is let go
    if app.Model.Preview then app.Dispatch TogglePreview
    app.Dispatch (UiMsg (UiMsg.Blur))
    let count0 = app.Model.Nodes.Length
    let last () = List.last app.Model.Nodes
    move 100.0f 336.0f
    frame None
    stage.Input(PointerDown(Vec2.create 100.0f 336.0f, 0)) |> ignore
    for (x, y) in [ (160.0f, 330.0f); (300.0f, 320.0f); (520.0f, 301.0f) ] do
        frame None
        stage.Input(PointerMove(Vec2.create x y)) |> ignore
    frame (Some "editor-6-dragging")
    check "while it is dragged the entry is being dragged, and nothing is added yet" (Drag.isDragging app.Model.Drag && app.Model.Nodes.Length = count0)
    stage.Input(PointerUp(Vec2.create 520.0f 301.0f, 0)) |> ignore
    frame (Some "editor-7-dropped")
    check "dropped on the surface it lands where it is let go (on the grid) and is selected"
        (app.Model.Nodes.Length = count0 + 1 && (last ()).Kind = KSlider && (last ()).X = Model.snap (520.0f - 190.0f) && (last ()).Y = Model.snap (301.0f - 52.0f)
         && app.Model.Selected = Some (last ()).Id && app.Model.Drag = Idle)
    app.Dispatch Undo
    check "undo takes the dropped element out in one step" (app.Model.Nodes.Length = count0)
    // let go off the surface (over the inspector), or cancelled with Escape: nothing is added
    drag 100.0f 336.0f 1180.0f 300.0f
    check "let go off the surface adds nothing" (app.Model.Nodes.Length = count0 && app.Model.Drag = Idle)
    move 100.0f 336.0f
    frame None
    stage.Input(PointerDown(Vec2.create 100.0f 336.0f, 0)) |> ignore
    frame None
    stage.Input(PointerMove(Vec2.create 400.0f 300.0f)) |> ignore
    key "Escape"
    stage.Input(PointerUp(Vec2.create 400.0f 300.0f, 0)) |> ignore
    check "Escape cancels the drag: nothing is added" (app.Model.Nodes.Length = count0 && app.Model.Drag = Idle)
    click 100.0f 336.0f
    check "a click on an entry still adds it" (app.Model.Nodes.Length = count0 + 1 && (last ()).Kind = KSlider)
    if failures > 0 then 1 else 0
