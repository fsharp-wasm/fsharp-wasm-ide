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
    // typed text: the platform has made the characters (layout, Shift, Caps Lock, AltGr, accents), so a full stop, a comma, a capital without Shift, an umlaut, @ and the euro sign are typed as they are
    let text t = frame None; stage.Input(TextInput t) |> ignore
    for t in [ "."; ","; "H"; "ä"; "@"; "\u20AC"; "\"" ] do text t
    check "typed text is typed as it is" (text5 () = "Sign ok 1A!b.,H\u00E4@\u20AC\"")
    text "\n"
    check "a control character is not text" (text5 () = "Sign ok 1A!b.,H\u00E4@\u20AC\"")
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
    // the properties of an element that a designer sets: the size of its letters, its rotation and its opacity (the property widgets of the toolkit)
    let heading = app.Model.Nodes |> List.find (fun n -> n.Kind = KHeading)
    let nodeOf id = app.Model.Nodes |> List.find (fun n -> n.Id = id)
    app.Dispatch (UiMsg (UiMsg.Blur))
    app.Dispatch (Select (Some heading.Id))
    check "an element starts at the natural size of its letters, upright and opaque" (heading.FontSize = 18.0f && heading.Rotation = 0.0f && heading.Opacity = 1.0f)
    app.Dispatch (SetFontSize(heading.Id, 28.0f)); app.Dispatch (SetRotation(heading.Id, 15.0f)); app.Dispatch (SetOpacity(heading.Id, 0.5f))
    let styled = nodeOf heading.Id
    check "the size, the rotation and the opacity are set" (styled.FontSize = 28.0f && styled.Rotation = 15.0f && styled.Opacity = 0.5f)
    app.Dispatch (SetFontSize(heading.Id, 500.0f)); app.Dispatch (SetRotation(heading.Id, 720.0f)); app.Dispatch (SetOpacity(heading.Id, -3.0f))
    let clamped = nodeOf heading.Id
    check "they stay inside their limits" (clamped.FontSize = 96.0f && clamped.Rotation = 360.0f && clamped.Opacity = 0.0f)
    app.Dispatch (SetFontSize(heading.Id, 28.0f)); app.Dispatch (SetRotation(heading.Id, 15.0f)); app.Dispatch (SetOpacity(heading.Id, 0.5f))
    check "the code of the design says them (the size of the letters, the turn about the middle, the opacity)"
        (let code = Export.generate (fun _ -> Vec2.create 40.0f 9.0f) app.Model |> String.concat "\n"
         code.Contains "Label.viewSized ui Label.Heading (ValueSome 28.0f) \"Sign in\"" && code.Contains "Scene.rotate 15.0f (Vec2.create 40.0f 9.0f)" && code.Contains "Scene.opacity 0.5f" && code.Contains "open FSharp.Math")
    check "an element that was not given them is exported as before" (let code = Export.generate (fun _ -> Vec2.create 0.0f 0.0f) app.Model |> String.concat "\n" in code.Contains "Button.view ui \"n5\"" && not (code.Contains "Scene.rotate 0.0f"))
    app.Dispatch Undo
    check "undo takes one property back, one step each (the opacity was 0 before it was set to a half)" ((nodeOf heading.Id).Opacity = 0.0f && (nodeOf heading.Id).Rotation = 15.0f && (nodeOf heading.Id).FontSize = 28.0f)
    // the inspector is built from the property widgets: a heading has the size of its letters, a toggle does not; both have rotation and opacity
    let rows () = SemanticTree.walk (app.Build(0.0f, Vec2.create 1280.0f 800.0f)) |> Array.map (fun s -> s.Id)
    check "the inspector of a heading has the font size, the rotation and the opacity" (let r = rows () in [ "insp-fontsize-plus"; "insp-fontsize-minus"; "insp-rot"; "insp-rot-preset2"; "insp-opacity" ] |> List.forall (fun id -> Array.contains id r))
    app.Dispatch (Add KToggle)
    check "the inspector of a toggle has no font size, but the rotation and the opacity" (let r = rows () in not (Array.contains "insp-fontsize-plus" r) && Array.contains "insp-rot" r && Array.contains "insp-opacity" r)
    // the layers: the elements in the order they are stacked, the one in front first; a row selects, dragged it puts the element elsewhere in the stack, Page Up and Page Down move it a place
    if app.Model.Preview then app.Dispatch TogglePreview
    app.Dispatch (UiMsg (UiMsg.Blur))
    let order () = app.Model.Nodes |> List.map (fun n -> n.Id)
    // the place of a row in the window, as the scene that was last drawn has it
    let layerRect (id: int) : Rect =
        frame None
        (SemanticTree.collect stage.Resolved |> Array.find (fun n -> n.Info.Id = "layer-" + string id)).Bounds
    let centre (r: Rect) = Vec2.create (r.X + r.W * 0.5f) (r.Y + r.H * 0.5f)
    // start from a known stack: the five elements of the first design, in order 1 2 3 4 5 (5 in front)
    let ids0 = [ 1; 2; 3; 4; 5 ]
    while Model.canUndo app.Model do app.Dispatch Undo
    check "the stack at the start of the design is the order in which the elements were made" (order () = ids0)
    let rows = ids0 |> List.map (fun id -> id, layerRect id)
    check "the layers list shows the one in front on top, a row below the other" (rows |> List.map (fun (_, r) -> r.Y) |> List.pairwise |> List.forall (fun (a, b) -> a > b))
    let c1 = centre (layerRect 1)
    click c1.X c1.Y
    check "a click on a row selects the element, and moves nothing" (app.Model.Selected = Some 1 && order () = ids0 && app.Model.LayerDrag = Idle && not (Model.canUndo app.Model))
    // element 1 is at the bottom of the list; drag it to the top: above element 5, so it is in front
    let top = layerRect 5
    drag c1.X c1.Y c1.X (top.Y + 2.0f)
    check "dragged above the top row, an element comes to the front" (order () = [ 2; 3; 4; 5; 1 ] && app.Model.Selected = Some 1 && app.Model.LayerDrag = Idle)
    frame (Some "editor-8-layers")
    app.Dispatch Undo
    check "undo puts it back, in one step" (order () = ids0)
    // drag element 3 (the middle one) down between 2 and 1: behind 2
    let c3 = centre (layerRect 3)
    let r2 = layerRect 2
    drag c3.X c3.Y c3.X (r2.Y + r2.H - 2.0f)
    check "dragged to the lower half of the row below it, an element goes behind that one" (order () = [ 1; 3; 2; 4; 5 ])
    app.Dispatch Undo
    // while it is dragged the mark is where it would land, and Escape throws the drag away
    let c4 = centre (layerRect 4)
    stage.Input(PointerMove(c4)) |> ignore
    frame None
    stage.Input(PointerDown(c4, 0)) |> ignore
    frame None
    stage.Input(PointerMove(Vec2.create c4.X (c4.Y + 80.0f))) |> ignore
    frame (Some "editor-9-layer-dragging")
    check "dragging a layer is a session that has a drop" (Drag.isDragging app.Model.LayerDrag && (Drag.over app.Model.LayerDrag).IsSome && order () = ids0)
    key "Escape"
    stage.Input(PointerUp(Vec2.create c4.X (c4.Y + 80.0f), 0)) |> ignore
    check "Escape ends it and nothing moved" (app.Model.LayerDrag = Idle && order () = ids0 && not (Model.canUndo app.Model))
    // the keyboard: the same drop, by Page Up and Page Down, and the buttons of the inspector
    app.Dispatch (Select (Some 3))
    key "PageUp"
    check "Page Up brings the selected element one place forward" (order () = [ 1; 2; 4; 3; 5 ])
    key "PageDown"
    key "PageDown"
    check "and Page Down takes it back, and one more place behind" (order () = [ 1; 3; 2; 4; 5 ])
    key "PageDown"
    key "PageDown"
    check "at the back it stays at the back" (order () = [ 3; 1; 2; 4; 5 ])
    app.Dispatch (MoveLayer(5, -1))
    check "the front element cannot come further forward" (order () = [ 3; 1; 2; 4; 5 ])
    app.Dispatch (MoveLayer(4, -1))
    check "one place forward is what the button of the inspector does" (order () = [ 3; 1; 2; 5; 4 ])
    // every layer is a control for assistive technology, named by its place and what it is
    let rows2 = SemanticTree.walk (app.Build(t, Vec2.create (float32 w) (float32 h))) |> Array.filter (fun s -> s.Id.StartsWith "layer-")
    check "a layer is named by its place in the stack and what it is" (rows2.Length = app.Model.Nodes.Length && rows2 |> Array.forall (fun s -> s.Name.StartsWith "Layer " && s.Name.Contains " of "))
    // typing into the text of an element on the surface itself: a second click on the selected element, or F2; Enter and Escape end it; the typing is one step of the undo log
    app.Dispatch (UiMsg (UiMsg.Blur))
    while Model.canUndo app.Model do app.Dispatch Undo
    let textOf id = (app.Model.Nodes |> List.find (fun n -> n.Id = id)).Text
    let nodeRect (id: int) : Rect =
        frame None
        (SemanticTree.collect stage.Resolved |> Array.find (fun n -> n.Info.Id = "node-" + string id)).Bounds
    let heading1 = app.Model.Nodes |> List.find (fun n -> n.Kind = KHeading)
    let c = centre (nodeRect heading1.Id)
    app.Dispatch (Select None)
    click c.X c.Y
    check "the first click selects the element and types nothing" (app.Model.Selected = Some heading1.Id && app.Model.Inline.IsNone)
    click c.X c.Y
    check "a second click on the selected element types into its text on the surface" (app.Model.Inline = Some heading1.Id)
    frame (Some "editor-10-inline")
    let before = textOf heading1.Id
    app.Dispatch (Typed "!"); app.Dispatch (Typed "?")
    check "what is typed goes into the text of the element" (textOf heading1.Id = before + "!?")
    key "Backspace"
    check "Backspace takes a character of the text, and not the element" (textOf heading1.Id = before + "!" && app.Model.Nodes |> List.exists (fun n -> n.Id = heading1.Id))
    key "Enter"
    check "Enter ends it and keeps the text" (app.Model.Inline.IsNone && textOf heading1.Id = before + "!")
    app.Dispatch Undo
    check "the typing is one step of the undo log" (textOf heading1.Id = before)
    // F2 starts it for the selected element; Escape ends it; another selection ends it too
    key "F2"
    check "F2 types into the selected element" (app.Model.Inline = Some heading1.Id)
    app.Dispatch (Typed "x")
    key "Escape"
    check "Escape ends it" (app.Model.Inline.IsNone && textOf heading1.Id = before + "x")
    key "F2"
    let others = app.Model.Nodes |> List.filter (fun n -> n.Id <> heading1.Id)
    app.Dispatch (Select (Some others.Head.Id))
    check "selecting another element ends the typing" (app.Model.Inline.IsNone)
    // an element without a text has nothing to type into
    let divider = Model.newNode 99 KDivider 40.0f 40.0f
    app.Dispatch (Add KDivider)
    key "F2"
    check "an element with no text does not start it" (app.Model.Inline.IsNone)
    ignore divider
    if failures > 0 then 1 else 0
