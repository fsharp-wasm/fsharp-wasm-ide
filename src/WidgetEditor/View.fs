namespace FSharp.Wasm.Ide.WidgetEditor

open System
open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.SceneDsl
open FSharp.Graphics.Widgets
open FSharp.Graphics.Interact

module View =

    [<Literal>]
    let TopH = 52.0f
    [<Literal>]
    let PaletteW = 190.0f
    [<Literal>]
    let InspectorW = 270.0f
    [<Literal>]
    let CodeH = 230.0f

    // ---- small drawing helpers ---------------------------------------------------------------

    /// What an element looks like, in 22 x 14 pixels: the palette shows it instead of only naming it.
    let private icon (th: Theme) (k: Kind) : Scene =
        let ink = th.TextMuted
        let accent = th.Accent
        match k with
        | KButton -> rectangle { bounds 0 2 22 10; radius 3; fill accent }
        | KLabel -> Group [ rectangle { bounds 0 3 22 2; radius 1; fill ink }; rectangle { bounds 0 8 15 2; radius 1; fill ink } ]
        | KHeading -> Group [ rectangle { bounds 0 1 16 5; radius 2; fill th.Text }; rectangle { bounds 0 9 22 2; radius 1; fill ink } ]
        | KToggle -> Group [ rectangle { bounds 0 2 22 10; radius 5; fill accent }; circle { bounds 13 4 6 6; fill th.OnAccent } ]
        | KCheck -> Group [ rectangle { bounds 5 1 12 12; radius 3; fill accent }; rectangle { bounds 8 6 6 2; radius 1; fill th.OnAccent } ]
        | KSlider -> Group [ rectangle { bounds 0 6 22 2; radius 1; fill ink }; rectangle { bounds 0 6 12 2; radius 1; fill accent }; circle { bounds 8 3 8 8; fill th.Text } ]
        | KText -> Group [ rectangle { bounds 0 1 22 12; radius 3; stroke ink 1.5f }; rectangle { bounds 4 4 1 6; fill accent } ]
        | KProgress -> Group [ rectangle { bounds 0 5 22 4; radius 2; fill ink }; rectangle { bounds 0 5 14 4; radius 2; fill accent } ]
        | KCard -> Group [ rectangle { bounds 0 0 22 14; radius 3; stroke ink 1.5f }; rectangle { bounds 4 4 9 2; radius 1; fill th.Text } ]
        | KDivider -> rectangle { bounds 0 6 22 2; radius 1; fill ink }

    /// The mark of the tool: an accent tile with a little layout in it.
    let private logo (th: Theme) : Scene =
        Group [ rectangle { bounds 0 0 28 28; radius 8; vgradient th.AccentHover th.AccentPressed; shadow 0 2 6 (rgba 0 0 0 90) }
                rectangle { bounds 6 7 16 5; radius 2; fill th.OnAccent }
                rectangle { bounds 6 15 9 6; radius 2; fill (Color.withAlpha 0.75f th.OnAccent) }
                rectangle { bounds 17 15 5 6; radius 2; fill (Color.withAlpha 0.75f th.OnAccent) } ]

    // ---- the designed widgets ---------------------------------------------------------------

    let private sized (n: Node) : float32 voption = if Kind.hasFontSize n.Kind && n.FontSize > 0.0f then ValueSome n.FontSize else ValueNone

    /// The element as it is built, before it is turned and faded.
    let private nodeBase (ui: Ui<Msg>) (n: Node) : Scene =
        let id = sprintf "n%d" n.Id
        match n.Kind with
        | KButton ->
            Button.view ui id
                { Button.make n.Text ignore with
                    Variant = n.Variant
                    Width = (if n.W > 0.0f then ValueSome n.W else ValueNone) }
        | KLabel -> Label.viewSized ui Label.Body (sized n) n.Text
        | KHeading -> Label.viewSized ui Label.Heading (sized n) n.Text
        | KToggle -> Toggle.view ui id n.Text n.On (fun v -> ui.Send(SetOn(n.Id, v)))
        | KCheck -> CheckBox.view ui id n.Text n.On (fun v -> ui.Send(SetOn(n.Id, v)))
        | KSlider ->
            Slider.view ui id
                { Min = 0.0f; Max = 1.0f; Value = n.Value; Width = n.W
                  OnChange = fun v -> ui.Send(SetValue(n.Id, v)) }
        | KText -> TextField.view ui id { Value = n.Text; Placeholder = "Type here"; Width = n.W }
        | KProgress -> ProgressBar.view ui n.W n.Value
        | KCard -> Panel.view ui Panel.card (Label.viewSized ui Label.Heading (sized n) n.Text)
        | KDivider -> Divider.horizontal ui n.W

    /// The middle of the element as it is built: what it turns about.
    let centre (ui: Ui<Msg>) (n: Node) : Vec2 =
        let b = Ui.measure ui (nodeBase ui n)
        Vec2.create (b.X + b.W * 0.5f) (b.Y + b.H * 0.5f)

    /// The element as it is drawn: turned about its middle, and faded.
    let private nodeWidget (ui: Ui<Msg>) (n: Node) : Scene =
        let w = nodeBase ui n
        let turned = if n.Rotation = 0.0f then w else Scene.rotate n.Rotation (centre ui n) w
        if n.Opacity >= 1.0f then turned else Scene.opacity n.Opacity turned

    /// Drag-to-move overlay. Pointer events carry window coordinates, so the drag is relative to
    /// the press point; the closure (created at press time) keeps receiving the captured moves.
    let private dragOverlay (ui: Ui<Msg>) (selected: bool) (n: Node) (b: Rect) : Scene =
        let down : Vec2 voption ref = ref ValueNone
        let sx, sy = n.X, n.Y
        // An element being designed is a picture of a widget, not a live one: for the keyboard and for assistive technology it is this one control, which takes the focus
        // and with it the selection, and which the arrow keys move.
        let name = Kind.name n.Kind + (if Kind.hasText n.Kind && n.Text <> "" then ": " + n.Text else "")
        let described (overlay: Scene) =
            Ui.semantic ui ("node-" + string n.Id) Role.Button name
                (fun sem -> { sem with Description = "Arrow keys move it, Delete removes it"; Selected = selected })
                (fun a -> match a with SemanticAction.Activate -> ui.Send(Select(Some n.Id)) | _ -> ())
                overlay
        described <| Cursor(CursorKind.Move, Reactive(
            (fun e ->
                match e with
                | PointerDown(p, 0) ->
                    down.Value <- ValueSome p
                    ui.Send(Select(Some n.Id))
                    true
                | PointerMove p when down.Value.IsSome ->
                    let d = down.Value.Value
                    ui.Send(MoveTo(n.Id, Model.snap (sx + p.X - d.X), Model.snap (sy + p.Y - d.Y)))
                    true
                | PointerUp _ when down.Value.IsSome ->
                    down.Value <- ValueNone
                    true
                | _ -> false),
            rectangle { bounds b; fill Color.Transparent }))

    let private widthHandle (ui: Ui<Msg>) (th: Theme) (n: Node) (b: Rect) : Scene =
        let down : float32 voption ref = ref ValueNone
        let w0 = b.W
        let handle = rectangle { bounds (b.X + b.W + 1.0f) (b.Y + b.H * 0.5f - 9.0f) 8.0f 18.0f; radius 3.0f; fill th.Accent }
        Cursor(CursorKind.ResizeEW, Reactive(
            (fun e ->
                match e with
                | PointerDown(p, 0) -> down.Value <- ValueSome p.X; true
                | PointerMove p when down.Value.IsSome ->
                    ui.Send(SetW(n.Id, w0 + p.X - down.Value.Value)); true
                | PointerUp _ when down.Value.IsSome -> down.Value <- ValueNone; true
                | _ -> false),
            handle))

    let private canvas (chrome: Ui<Msg>) (m: Model) (w: float32) (h: float32) : Scene =
        let th = if m.DarkDesign then Theme.dark else Theme.light
        let dui = { chrome with Theme = th; State = (if m.Preview then m.Ui else UiState.empty) }
        let grid =
            if m.Preview then Empty
            else
                let minor = Color.withAlpha 0.10f th.TextMuted
                let major = Color.withAlpha 0.20f th.TextMuted
                let tone i = if i % 4 = 0 then major else minor
                Group [ for gx in 1 .. int (w / 32.0f) -> rectangle { bounds (gx * 32) 0 1 (int h); fill (tone gx) }
                        for gy in 1 .. int (h / 32.0f) -> rectangle { bounds 0 (gy * 32) (int w) 1; fill (tone gy) } ]
        let boundsOf (n: Node) : Rect =
            let b0 = Ui.measure dui (nodeWidget dui n)
            Rect.create b0.X b0.Y (Scalar.max' b0.W 24.0f) (Scalar.max' b0.H 18.0f)
        let node (n: Node) : Scene =
            let widget = nodeWidget dui n
            let picture = Semantic(Semantics.create "" Role.None "", widget)
            let body = if m.Preview then widget else Group [ picture; dragOverlay chrome (m.Selected = Some n.Id) n (boundsOf n) ]
            shift n.X n.Y body
        // What marks the selection is drawn above every element, so that a neighbour never covers the handles or the size.
        let selection : Scene =
            match m.Selected |> Option.bind (fun id -> m.Nodes |> List.tryFind (fun n -> n.Id = id)) with
            | Some n when not m.Preview ->
                let b = boundsOf n
                let x0, y0, x1, y1 = b.X - 4.0f, b.Y - 4.0f, b.X + b.W + 4.0f, b.Y + b.H + 4.0f
                let corner (cx: float32) (cy: float32) =
                    rectangle { bounds (cx - 4.0f) (cy - 4.0f) 8.0f 8.0f; radius 2.0f; fill Color.White; stroke th.Accent 1.5f }
                let size = sprintf "%d × %d" (int b.W) (int b.H)
                shift n.X n.Y
                    (Group [ rectangle { bounds x0 y0 (x1 - x0) (y1 - y0); radius 4.0f; stroke th.Accent 1.5f }
                             corner x0 y0; corner x1 y0; corner x0 y1; corner x1 y1
                             shift ((x0 + x1) * 0.5f - Chip.width chrome size * 0.5f) (y1 + 8.0f) (Chip.view chrome size (rgba 8 12 24 235) Color.White)
                             (if Kind.hasWidth n.Kind && n.Rotation = 0.0f then widthHandle chrome th n b else Empty) ])
            | _ -> Empty
        Clip(Rect.create 0.0f 0.0f w h,
             Group [ rectangle { bounds 0.0f 0.0f w h; fill th.Background }
                     grid
                     Group (m.Nodes |> List.map node)
                     selection
                     (if List.isEmpty m.Nodes && not m.Preview then
                         let hint = "Nothing here yet — click an element in the palette to add it"
                         shift (w * 0.5f - Ui.textWidth chrome 14.0f hint * 0.5f) (h * 0.5f - 10.0f)
                               (Text(hint, Ui.textStyle chrome 14.0f th.TextMuted))
                      else Empty) ])

    // ---- chrome -----------------------------------------------------------------------------

    /// The palette sits at (16, 16) of its panel, which starts below the top bar.
    let private paletteOrigin = Vec2.create 16.0f (TopH + 16.0f)
    let private paletteCaption = "Click or drag to add"

    let private entryButton (ui: Ui<Msg>) (k: Kind) : Scene =
        Button.view ui ("pal-" + Kind.name k)
            { Button.make (Kind.name k) (fun () -> ui.Send(Add k)) with Variant = Button.Secondary; Width = ValueSome 158.0f }

    /// Where each entry is, in the palette's own space: the heading, the caption and then one entry below the other, 8 px apart.  They are placed here and not by a column, so
    /// that the place of an entry in the window is known: a drag starts on it, and the pointer is then reported in its space.
    let private paletteRects (ui: Ui<Msg>) : (Kind * Rect) list =
        let head = (Ui.measure ui (Label.view ui Label.Heading "Elements")).H
        let caption = (Ui.measure ui (Label.view ui Label.Caption paletteCaption)).H
        let _, placed =
            Kind.all
            |> List.fold (fun (y, acc) k ->
                let h = (Ui.measure ui (entryButton ui k)).H
                y + h + 8.0f, (k, Rect.create 0.0f y 158.0f h) :: acc) (head + 8.0f + caption + 8.0f, [])
        List.rev placed

    /// A rectangle that the pointer can press, drag from and release: the three messages are the caller's, and `at` turns a point of the rectangle (its own space) into the space the model
    /// wants it in.  What the release means (a click, a drop) is the model's: it knows whether the pointer moved.
    let private dragHandle (at: Vec2 -> Vec2) (press: Vec2 -> unit) (move: Vec2 -> unit) (release: Vec2 -> unit) (r: Rect) : Scene =
        let pressed = ref false
        shift r.X r.Y
            (Cursor(CursorKind.Grab, Reactive(
                (fun e ->
                    match e with
                    | PointerDown(p, 0) -> pressed.Value <- true; press (at p); true
                    | PointerMove p when pressed.Value -> move (at p); true
                    | PointerUp(p, 0) when pressed.Value -> pressed.Value <- false; release (at p); true
                    | _ -> false),
                rectangle { bounds 0.0f 0.0f r.W r.H; fill Color.Transparent })))

    /// Where the palette ends, in its own space: what is below it starts there.
    let private paletteBottom (ui: Ui<Msg>) : float32 =
        match List.rev (paletteRects ui) with
        | (_, r) :: _ -> r.Y + r.H
        | [] -> 0.0f

    /// The press, the moves and the release of one entry of the palette.  Its handler sees the pointer in the entry's own space; the model wants the window's: the entry's corner, then the pointer.
    let private entryOverlay (ui: Ui<Msg>) (canvas: Rect) (k: Kind) (r: Rect) : Scene =
        let at (p: Vec2) = Vec2.add (Vec2.add paletteOrigin (Vec2.create r.X r.Y)) p
        dragHandle at (fun p -> ui.Send(DragPress(k, p))) (fun p -> ui.Send(DragMove(p, canvas))) (fun p -> ui.Send(DragRelease(p, canvas))) r

    let private palette (ui: Ui<Msg>) (canvas: Rect) : Scene =
        let head = Label.view ui Label.Heading "Elements"
        let entries =
            [ for (k, r) in paletteRects ui ->
                Group [ shift r.X r.Y (entryButton ui k)
                        shift (r.X + 12.0f) (r.Y + 10.0f) (icon ui.Theme k)
                        entryOverlay ui canvas k r ] ]
        Group [ head
                shift 0.0f ((Ui.measure ui head).H + 8.0f) (Label.view ui Label.Caption paletteCaption)
                Group entries ]

    // ---- the layers: the elements in the order they are stacked, the one in front first ------------------------------------------------------------------------------------------

    /// The list of layers: a row for each element, which selects it and, dragged, puts it elsewhere in the stack; a mark between two rows says where it would land.  The pointer is reported in the
    /// space of the list (its corner is the origin, a row is `Layers.rowH` high), which is what the model's slots are in.  `avail` is the height the panel has left; rows that do not fit are counted.
    let private layers (ui: Ui<Msg>) (m: Model) (avail: float32) : Scene =
        let th = ui.Theme
        let head = Label.view ui Label.Heading "Layers"
        let caption = Label.view ui Label.Caption "Drag, or Page Up / Down"
        let listY = (Ui.measure ui head).H + 8.0f + (Ui.measure ui caption).H + 8.0f
        let fits = max 1 (int ((avail - listY) / Layers.rowH))
        let nodes = Layers.shown m.Nodes
        let lifted = Drag.payload m.LayerDrag
        let row (i: int) (n: Node) : Scene =
            let y = float32 i * Layers.rowH
            let selected = (m.Selected = Some n.Id)
            let label = Kind.name n.Kind + (if Kind.hasText n.Kind && n.Text <> "" then " · " + n.Text else "")
            let ty = (Layers.rowH - th.FontSize * 1.2f) * 0.5f
            let dim = if lifted = Some n.Id then 0.45f else 1.0f
            let body =
                Opacity(dim, Group [ rectangle { bounds 0.0f 2.0f Layers.rowW (Layers.rowH - 4.0f); radius 6.0f
                                                 fill (if selected then Color.withAlpha 0.22f th.Accent else Color.Transparent)
                                                 stroke (if selected then th.Accent else Color.Transparent) 1.0f }
                                     shift 8.0f ((Layers.rowH - 14.0f) * 0.5f) (icon th n.Kind)
                                     shift 38.0f ty (Clip(Rect.create 0.0f 0.0f (Layers.rowW - 46.0f) (th.FontSize * 1.4f), Text(label, Ui.textStyle ui th.FontSize (if selected then th.Text else th.TextMuted)))) ])
            let at (p: Vec2) = Vec2.add p (Vec2.create 0.0f y)
            let handle =
                dragHandle at (fun p -> ui.Send(LayerPress(n.Id, p))) (fun p -> ui.Send(LayerMove p)) (fun p -> ui.Send(LayerRelease p)) (Rect.create 0.0f y Layers.rowW Layers.rowH)
            let described (overlay: Scene) =
                Ui.semantic ui ("layer-" + string n.Id) Role.Button ("Layer " + string (i + 1) + " of " + string nodes.Length + ": " + label)
                    (fun sem -> { sem with Description = "Select it; drag it, or Page Up and Page Down, to put it elsewhere in the stack"; Selected = selected })
                    (fun a -> match a with SemanticAction.Activate -> ui.Send(Select(Some n.Id)) | _ -> ())
                    overlay
            Group [ shift 0.0f y body; described handle ]
        let mark =
            match Drag.over m.LayerDrag with
            | Some d -> rectangle { bounds d.Mark.X d.Mark.Y d.Mark.W d.Mark.H; radius 1.5f; fill th.Accent }
            | None -> Empty
        let hidden = nodes.Length - fits
        Group [ head
                shift 0.0f ((Ui.measure ui head).H + 8.0f) caption
                shift 0.0f listY (Group [ Group (nodes |> List.truncate fits |> List.mapi row); mark ])
                (if hidden > 0 then shift 0.0f (listY + float32 fits * Layers.rowH + 2.0f) (Label.view ui Label.Caption ("+ " + string hidden + " more")) else Empty) ]

    /// While an entry is dragged: the surface that takes the drop says so, over it the widget itself (a picture of it, half transparent) is at the place where it would land, in the frame of the
    /// size it has, and its name is at the pointer.
    let private dragFeedback (ui: Ui<Msg>) (m: Model) (canvas: Rect) : Scene =
        match m.Drag with
        | Dragging (kind, _, at, _) ->
            let th = if m.DarkDesign then Theme.dark else Theme.light
            let design = { ui with Theme = th; State = UiState.empty }
            let widget = nodeWidget design (Model.newNode 0 kind 0.0f 0.0f)
            let extent = Ui.measure design widget
            let corner = Vec2.create (canvas.X + Model.snap (at.X - canvas.X)) (canvas.Y + Model.snap (at.Y - canvas.Y))
            let over = Rect.contains at canvas
            let accent = ui.Theme.Accent
            let target =
                rectangle { bounds (canvas.X + 1.0f) (canvas.Y + 1.0f) (canvas.W - 2.0f) (canvas.H - 2.0f)
                            fill (Color.withAlpha (if over then 0.06f else 0.03f) accent); stroke (Color.withAlpha (if over then 0.9f else 0.4f) accent) 2.0f }
            let landing =
                if over then
                    Group [ rectangle { bounds corner.X corner.Y (Scalar.max' extent.W 24.0f) (Scalar.max' extent.H 18.0f); radius 4.0f
                                        fill (Color.withAlpha 0.10f accent); stroke accent 1.5f }
                            shift corner.X corner.Y (Opacity(0.7f, Scene.inert widget)) ]
                else Empty
            Group [ target; landing; shift (at.X + 14.0f) (at.Y + 14.0f) (Chip.view ui (Kind.name kind) accent ui.Theme.OnAccent) ]
        | _ -> Empty

    let private inspector (ui: Ui<Msg>) (m: Model) (canvasW: float32) : Scene =
        match m.Selected |> Option.bind (fun id -> m.Nodes |> List.tryFind (fun n -> n.Id = id)) with
        | None ->
            column {
                gap 8
                child (Label.view ui Label.Heading "Inspector")
                child (Label.view ui Label.Muted "Select an element on the canvas.")
                child (Label.view ui Label.Caption "Arrow keys nudge, Delete removes.")
            }
        | Some n ->
            let send = ui.Send
            let grid = Properties.range 0.0f 1.0f 8.0f
            let fontSize = Properties.range 8.0f 96.0f 1.0f |> Properties.unit "px"
            column {
                gap 12
                child (Group [ Label.view ui Label.Heading "Inspector"
                               shift (Properties.Width - Chip.width ui (Kind.name n.Kind)) 3.0f (Chip.view ui (Kind.name n.Kind) ui.Theme.Accent ui.Theme.OnAccent) ])
                childIf (Kind.hasText n.Kind) (Properties.text ui "insp-text" "Text" n.Text "(empty)")
                child (Properties.slider ui "insp-x" "X" { grid with Max = canvasW } n.X (fun v -> send (MoveTo(n.Id, Model.snap v, n.Y))))
                child (Properties.slider ui "insp-y" "Y" { grid with Max = 600.0f } n.Y (fun v -> send (MoveTo(n.Id, n.X, Model.snap v))))
                childIf (Kind.hasWidth n.Kind)
                    (Properties.slider ui "insp-w" "Width" { grid with Min = 40.0f; Max = 600.0f } (max 40.0f n.W) (fun v -> send (SetW(n.Id, v))))
                childIf (Kind.hasFontSize n.Kind)
                    (column { gap 6
                              child (Properties.number ui "insp-fontsize" "Font size" fontSize n.FontSize (fun v -> send (SetFontSize(n.Id, v))))
                              child (Properties.presets ui "insp-fontsize" fontSize [ 12.0f; 16.0f; 24.0f; 36.0f ] n.FontSize (fun v -> send (SetFontSize(n.Id, v)))) })
                child (Properties.angle ui "insp-rot" "Rotation" n.Rotation (fun v -> send (SetRotation(n.Id, v))))
                child (Properties.slider ui "insp-opacity" "Opacity" (Properties.range 0.0f 1.0f 0.05f |> Properties.decimals 2) n.Opacity (fun v -> send (SetOpacity(n.Id, v))))
                childIf (Kind.hasValue n.Kind)
                    (Properties.slider ui "insp-v" "Value" (Properties.range 0.0f 1.0f 0.01f |> Properties.decimals 2) n.Value (fun v -> send (SetValue(n.Id, v))))
                childIf (Kind.hasOn n.Kind) (Properties.toggle ui "insp-on" "Checked / on" n.On (fun v -> send (SetOn(n.Id, v))))
                child (Divider.horizontal ui Properties.Width)
                child (row {
                    gap 8
                    child (Button.view ui "insp-dup" { Button.make "Duplicate" (fun () -> send Duplicate) with Variant = Button.Secondary })
                    child (Button.view ui "insp-front" { Button.make "To front" (fun () -> send BringToFront) with Variant = Button.Secondary })
                })
                child (row {
                    gap 8
                    child (Button.view ui "insp-forward" { Button.make "Forward" (fun () -> send (MoveLayer(n.Id, -1))) with Variant = Button.Secondary })
                    child (Button.view ui "insp-backward" { Button.make "Backward" (fun () -> send (MoveLayer(n.Id, 1))) with Variant = Button.Secondary })
                })
                childIf (n.Kind = KButton)
                    (Properties.field ui "Variant" ""
                        (Dropdown.view ui "insp-variant"
                            { Options = Model.variants |> List.map Model.variantName; Selected = List.findIndex ((=) n.Variant) Model.variants
                              Open = m.VariantOpen; Width = Properties.Width
                              OnToggle = (fun () -> send ToggleVariantMenu)
                              OnSelect = fun i -> send (SetVariant(n.Id, Model.variants.[i])) }))
                child (Button.view ui "insp-del" { Button.make "Delete" (fun () -> send Delete) with Variant = Button.Danger; Width = ValueSome Properties.Width })
            }

    let private topBar (ui: Ui<Msg>) (m: Model) (w: float32) : Scene =
        let send = ui.Send
        let buttons =
            row {
                gap 8
                child (Button.view ui "top-undo"
                        { Button.make "Undo" (fun () -> send Undo) with Variant = Button.Secondary; Disabled = not (Model.canUndo m) })
                child (Button.view ui "top-redo"
                        { Button.make "Redo" (fun () -> send Redo) with Variant = Button.Secondary; Disabled = not (Model.canRedo m) })
                child (Button.view ui "top-preview"
                        { Button.make (if m.Preview then "Preview: on" else "Preview: off") (fun () -> send TogglePreview) with
                            Variant = (if m.Preview then Button.Primary else Button.Secondary) })
                child (Button.view ui "top-code"
                        { Button.make "Export F#" (fun () -> send ToggleCode) with
                            Variant = (if m.ShowCode then Button.Primary else Button.Secondary) })
                child (Button.view ui "top-theme"
                        { Button.make (if m.DarkDesign then "Design: dark" else "Design: light") (fun () -> send ToggleTheme) with
                            Variant = Button.Secondary })
            }
        let bw = (Ui.measure ui buttons).W
        let title = Label.view ui Label.Heading "Widget Editor"
        let titleW = (Ui.measure ui title).W
        Group [
            rectangle { bounds 0.0f 0.0f w TopH; vgradient ui.Theme.SurfaceRaised ui.Theme.Surface; shadow 0 2 10 (rgba 0 0 0 70) }
            rectangle { bounds 0.0f (TopH - 1.0f) w 1.0f; fill ui.Theme.Border }
            shift 16 12 (logo ui.Theme)
            shift 54 13 title
            shift (54.0f + titleW + 12.0f) 16 (Chip.view ui "FSharp.Graphics" ui.Theme.Surface ui.Theme.TextMuted)
            shift (w - bw - 20.0f) 9 buttons
        ]

    let private codePanel (ui: Ui<Msg>) (m: Model) (w: float32) : Scene =
        let th = ui.Theme
        let lines =
            Export.generate (centre ui) m
            |> List.map (fun l ->
                let color = if l.TrimStart().StartsWith "open" then th.TextMuted else th.Text
                Text((if l = "" then " " else l), Ui.textStyle ui 12.0f color))
        let body = column { gap 3; children lines }
        Group [
            rectangle { bounds 0.0f 0.0f w CodeH; fill th.SurfaceRaised; shadow 0 -4 12 (rgba 0 0 0 80) }
            rectangle { bounds 0.0f 0.0f w 1.0f; fill th.Border }
            shift 16 10 (Label.view ui Label.Caption "Generated F#  (scroll)")
            shift 16 34 (ScrollView.view ui { Width = w - 32.0f; Height = CodeH - 44.0f; Offset = m.CodeScroll
                                              OnScroll = fun v -> ui.Send(ScrollCode v) } body)
        ]

    /// Whole-window view. `ui` carries the chrome theme, text engine, interaction state and dispatch.
    let view (ui: Ui<Msg>) (size: Vec2) (m: Model) : Scene =
        let w, h = size.X, size.Y
        let ui = { ui with State = m.Ui }
        let canvasW = w - PaletteW - InspectorW
        let canvasH = h - TopH
        let canvasRect = Rect.create PaletteW TopH canvasW canvasH
        // The regions, in the order the keyboard walks them: the toolbar, the elements, the surface, the inspector (the scene draws the toolbar last, on top).
        let region (id: string) (role: Role) (name: string) (order: int) (content: Scene) : Scene =
            Semantic({ Semantics.create id role name with Order = order }, content)
        let page =
            Group [
                rectangle { bounds 0.0f 0.0f w h; fill ui.Theme.Background }
                region "surface" Role.Region "Design surface" 2 (shift PaletteW TopH (canvas ui m canvasW canvasH))
                // side panels
                region "elements" Role.Region "Elements" 1
                    (shift 0.0f TopH (Group [ rectangle { bounds 0.0f 0.0f PaletteW canvasH; fill ui.Theme.Surface; shadow 2 0 10 (rgba 0 0 0 60) }
                                              rectangle { bounds (PaletteW - 1.0f) 0.0f 1.0f canvasH; fill ui.Theme.Border }
                                              shift 16 16 (palette ui canvasRect)
                                              shift 16.0f (16.0f + paletteBottom ui + 16.0f) (layers ui m (canvasH - 16.0f - paletteBottom ui - 32.0f)) ]))
                region "inspector" Role.Region "Inspector" 3
                    (shift (w - InspectorW) TopH (Group [ rectangle { bounds 0.0f 0.0f InspectorW canvasH; fill ui.Theme.Surface; shadow -2 0 10 (rgba 0 0 0 60) }
                                                          rectangle { bounds 0.0f 0.0f 1.0f canvasH; fill ui.Theme.Border }
                                                          shift 16 16 (inspector ui m canvasW) ]))
                (if m.ShowCode then shift PaletteW (h - CodeH) (codePanel ui m canvasW) else Empty)
                region "toolbar" Role.Toolbar "Editor actions" 0 (topBar ui m w)
                dragFeedback ui m canvasRect
            ]
        Ui.rootText ui (fun k -> ui.Send(Key k)) (fun t -> ui.Send(Typed t)) page
