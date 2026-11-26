namespace FSharp.Wasm.Ide.WidgetEditor

open System
open FSharp.Math
open FSharp.Wasm.Data
open FSharp.Wasm.Edit
open FSharp.Graphics.Interact
open FSharp.Graphics.Tea
open FSharp.Graphics.Widgets

type Kind =
    | KButton | KLabel | KHeading | KToggle | KCheck | KSlider | KText | KProgress | KCard | KDivider

module Kind =
    let all = [ KButton; KLabel; KHeading; KToggle; KCheck; KSlider; KText; KProgress; KCard; KDivider ]
    let name = function
        | KButton -> "Button" | KLabel -> "Label" | KHeading -> "Heading" | KToggle -> "Toggle"
        | KCheck -> "CheckBox" | KSlider -> "Slider" | KText -> "TextField" | KProgress -> "Progress"
        | KCard -> "Card" | KDivider -> "Divider"
    let hasText = function KSlider | KProgress | KDivider -> false | _ -> true
    let hasWidth = function KButton | KSlider | KText | KProgress | KDivider -> true | _ -> false
    let hasValue = function KSlider | KProgress -> true | _ -> false
    let hasOn = function KToggle | KCheck -> true | _ -> false
    /// The elements whose letters have a size of their own (a button, a toggle and a field take the size of the theme).
    let hasFontSize = function KLabel | KHeading | KCard -> true | _ -> false
    /// The size of the letters of an element that has not been given one: what the toolkit draws it with.
    let naturalFontSize = function KHeading | KCard -> 18.0f | KLabel -> 14.0f | _ -> 0.0f
    let defaultText = function
        | KButton -> "Button" | KLabel -> "Label text" | KHeading -> "Heading" | KToggle -> "Toggle me"
        | KCheck -> "Check me" | KText -> "" | KCard -> "Card title" | _ -> ""

/// One element on the design canvas (absolute position; `W` = 0 means "natural width").
type Node =
    { Id: int
      Kind: Kind
      X: float32
      Y: float32
      W: float32
      Text: string
      On: bool
      Value: float32
      Variant: Button.Variant
      /// The size of the letters, in pixels (0 for an element that has none of its own).
      FontSize: float32
      /// Degrees, turned about the middle of the element.
      Rotation: float32
      /// 1 is opaque.
      Opacity: float32 }

/// What the log of changes keeps: the elements on the surface and which one is selected, so that undoing an addition or a deletion puts the selection back where it was.
type Snapshot = { Nodes: Node list; NextId: int; Selected: int option }

type Model =
    { Nodes: Node list
      NextId: int
      /// The undo log: every change of the elements is a `Replace` of the whole set; the moves of one drag, the keystrokes of one text and the nudges of one element are one step.
      History: History<Snapshot>
      /// An element of the palette being pressed or dragged (window coordinates); where it would land is where the pointer is.
      Drag: Session<Kind>
      /// An element of the layers list being pressed or dragged (the pointer in the space of the list); where it would land is the drop the session holds.
      LayerDrag: Session<int>
      /// The element whose text is being typed on the surface itself, when there is one: the keys and the typed text go into it.
      Inline: int option
      Selected: int option
      Ui: UiState
      DarkDesign: bool
      Preview: bool
      ShowCode: bool
      CodeScroll: float32
      VariantOpen: bool }

type Msg =
    | UiMsg of UiMsg
    | Add of Kind
    | Select of int option
    | MoveTo of id: int * x: float32 * y: float32
    | SetW of id: int * w: float32
    | SetText of id: int * string
    | SetOn of id: int * bool
    | SetValue of id: int * float32
    | ToggleVariantMenu
    | SetVariant of id: int * Button.Variant
    | Delete
    | Duplicate
    | BringToFront
    | Undo
    | Redo
    /// A press on a palette entry, a move of the pointer, the release: where (window coordinates) and the design surface in the window (the one place a drop lands).
    | DragPress of Kind * Vec2
    | DragMove of Vec2 * Rect
    | DragRelease of Vec2 * Rect
    | DragCancel
    /// A press on a row of the layers list, a move of the pointer, the release: the pointer in the space of the list (its corner is the origin, a row is `Layers.rowH` high).
    | LayerPress of int * Vec2
    | LayerMove of Vec2
    | LayerRelease of Vec2
    | LayerCancel
    /// One place in the list: -1 is one up (in front of the element that is above), 1 one down.
    | MoveLayer of id: int * by: int
    /// Type into the text of an element on the surface (a second click on the selected element, or F2): Enter and Escape end it.
    | BeginInline of int
    | EndInline
    | SetFontSize of int * float32
    | SetRotation of int * float32
    | SetOpacity of int * float32
    | Key of string
    /// What the person typed, as the platform made it (the layout, Shift, Caps Lock, AltGr, accents applied): into the text of the element when its field has the focus.
    | Typed of string
    | ToggleTheme
    | TogglePreview
    | ToggleCode
    | ScrollCode of float32

/// The layers: the elements in the order they are stacked, the one in front first.
module Layers =
    let rowH = 30.0f
    let rowW = 158.0f

    /// The elements as the list shows them: the one in front on top.
    let shown (nodes: Node list) : Node list = List.rev nodes

    /// The rows of the list as places that a drop can go to, in the space of the list.  Nothing nests.
    let slots (nodes: Node list) : Slot list =
        shown nodes |> List.mapi (fun i n -> { Id = n.Id; Bounds = Rect.create 0.0f (float32 i * rowH) rowW rowH; Flow = Flow.Down; Nests = false })

    /// The elements after `id` was dropped at `drop` of the list: above a row is in front of it (later in the stack), below is behind it.  A drop on itself changes nothing.
    let reorder (nodes: Node list) (id: int) (drop: Drop) : Node list =
        match nodes |> List.tryFind (fun n -> n.Id = id) with
        | Some moving when drop.Target <> id ->
            let rest = nodes |> List.filter (fun n -> n.Id <> id)
            (match rest |> List.tryFindIndex (fun n -> n.Id = drop.Target) with
             | Some at ->
                let index = if drop.Position = Before then at + 1 else at
                List.take index rest @ [ moving ] @ List.skip index rest
             | None -> nodes)
        | _ -> nodes

    /// Where one press of a key takes the element: a session of the keyboard from the place above its row, `by` places on, and the drop it ends on.  Moving down starts one place later,
    /// because the place below an element is the one it is in.
    let stepped (nodes: Node list) (id: int) (by: int) : Drop option =
        let all = slots nodes
        match all |> List.tryFind (fun s -> s.Id = id) with
        | Some own ->
            let session = Keyboard.grab id all (Some (Drop.place own Before)) |> Keyboard.step all (if by > 0 then by + 1 else by)
            Drag.release session |> Option.map snd
        | None -> None

module Model =
    let variants = [ Button.Primary; Button.Secondary; Button.Danger; Button.Ghost ]
    /// The name of a variant as the inspector's dropdown lists it (what `%A` prints for a case without fields).
    let variantName (v: Button.Variant) : string =
        match v with
        | Button.Primary -> "Primary"
        | Button.Secondary -> "Secondary"
        | Button.Danger -> "Danger"
        | Button.Ghost -> "Ghost"
    let snap (v: float32) = MathF.Round(v / 8.0f) * 8.0f

    let newNode (id: int) (kind: Kind) (x: float32) (y: float32) : Node =
        { Id = id; Kind = kind; X = x; Y = y
          W = (match kind with KSlider | KText | KProgress | KDivider -> 220.0f | _ -> 0.0f)
          Text = Kind.defaultText kind; On = (kind = KToggle); Value = 0.5f; Variant = Button.Primary
          FontSize = Kind.naturalFontSize kind; Rotation = 0.0f; Opacity = 1.0f }

    let init () : Model * Cmd<Msg> =
        let mk id kind x y f = f (newNode id kind x y)
        let nodes =
            [ mk 1 KHeading 48.0f 48.0f (fun n -> { n with Text = "Sign in" })
              mk 2 KLabel 48.0f 88.0f (fun n -> { n with Text = "Welcome back. Please enter your details." })
              mk 3 KText 48.0f 128.0f (fun n -> { n with W = 280.0f })
              mk 4 KCheck 48.0f 184.0f (fun n -> { n with Text = "Remember me"; On = true })
              mk 5 KButton 48.0f 224.0f (fun n -> { n with Text = "Sign in"; W = 280.0f }) ]
        { Nodes = nodes; NextId = 6; History = History.create 200; Drag = Idle; LayerDrag = Idle; Inline = None; Selected = Some 5; Ui = UiState.empty
          DarkDesign = true; Preview = false; ShowCode = false; CodeScroll = 0.0f; VariantOpen = false }, Cmd.none

    let private mapNode id f (m: Model) =
        { m with Nodes = m.Nodes |> List.map (fun n -> if n.Id = id then f n else n) }

    let private selectedNode (m: Model) =
        m.Selected |> Option.bind (fun id -> m.Nodes |> List.tryFind (fun n -> n.Id = id))


    /// A new element of `kind` with its corner at (x, y) on the surface, selected.
    let private addAt (kind: Kind) (x: float32) (y: float32) (m: Model) : Model =
        let n = newNode m.NextId kind (snap x) (snap y)
        { m with Nodes = m.Nodes @ [ n ]; NextId = m.NextId + 1; Selected = Some n.Id }

    /// A new element where the palette puts it: cascaded, so that new elements never stack exactly on top of each other.
    let private addCascaded (kind: Kind) (m: Model) : Model =
        let k = float32 (m.Nodes.Length % 8)
        addAt kind (80.0f + k * 24.0f) (80.0f + k * 24.0f) m

    /// The design surface as the one place a drop can land.
    let private surface (canvas: Rect) : Slot = { Id = 0; Bounds = canvas; Flow = Flow.Down; Nests = true }

    /// The element `id` moved `by` places in the stack (-1 up in the list, in front; 1 down).
    let private moveLayer (m: Model) (id: int) (by: int) : Model =
        match Layers.stepped m.Nodes id by with
        | Some drop -> { m with Nodes = Layers.reorder m.Nodes id drop }
        | None -> m

    let private stepOnce (m: Model) (msg: Msg) : Model =
            match msg with
            | UiMsg u -> { m with Ui = UiState.update m.Ui u }
            // they act on the log, not on the elements: `update` answers them before a step is taken
            | Undo | Redo -> m
            | Add kind -> addCascaded kind m
            | DragPress(kind, p) -> { m with Drag = Drag.press kind p }
            | DragMove(p, canvas) -> { m with Drag = Drag.move [ surface canvas ] p m.Drag }
            | DragRelease(p, canvas) ->
                let landed = Drag.move [ surface canvas ] p m.Drag
                let m1 = { m with Drag = Idle }
                (match m.Drag, Drag.release landed with
                 // let go without having moved: a click on the entry adds it
                 | Pressed (kind, _), _ -> addCascaded kind m1
                 // let go over the surface: the element lands there
                 | _, Some (kind, _) when Rect.contains p canvas -> addAt kind (p.X - canvas.X) (p.Y - canvas.Y) m1
                 | _ -> m1)
            | DragCancel -> { m with Drag = Idle }
            // the layers: a press selects the element and may become a drag; let go without having moved is a click, over a row it is the drop
            | LayerPress(id, p) -> { m with LayerDrag = Drag.press id p; Selected = Some id }
            | LayerMove p -> { m with LayerDrag = Drag.move (Layers.slots m.Nodes) p m.LayerDrag }
            | LayerRelease p ->
                let landed = Drag.move (Layers.slots m.Nodes) p m.LayerDrag
                let m1 = { m with LayerDrag = Idle }
                (match Drag.release landed with
                 | Some (id, drop) -> { m1 with Nodes = Layers.reorder m.Nodes id drop }
                 | None -> m1)
            | LayerCancel -> { m with LayerDrag = Idle }
            | MoveLayer(id, by) -> moveLayer m id by
            | BeginInline id ->
                (match m.Nodes |> List.tryFind (fun n -> n.Id = id) with
                 | Some n when Kind.hasText n.Kind && not m.Preview -> { m with Inline = Some id; Selected = Some id; Ui = UiState.update m.Ui UiMsg.Blur }
                 | _ -> m)
            | EndInline -> { m with Inline = None }
            | Select s -> { m with Selected = s }
            | MoveTo(id, x, y) -> mapNode id (fun n -> { n with X = max 0.0f x; Y = max 0.0f y }) m
            | SetW(id, w) -> mapNode id (fun n -> { n with W = max 40.0f (snap w) }) m
            | SetText(id, t) -> mapNode id (fun n -> { n with Text = t }) m
            | SetOn(id, v) -> mapNode id (fun n -> { n with On = v }) m
            | SetValue(id, v) -> mapNode id (fun n -> { n with Value = v }) m
            | SetFontSize(id, v) -> mapNode id (fun n -> { n with FontSize = Scalar.clamp 8.0f 96.0f v }) m
            | SetRotation(id, v) -> mapNode id (fun n -> { n with Rotation = Scalar.clamp 0.0f 360.0f v }) m
            | SetOpacity(id, v) -> mapNode id (fun n -> { n with Opacity = Scalar.clamp 0.0f 1.0f v }) m
            | ToggleVariantMenu -> { m with VariantOpen = not m.VariantOpen }
            | SetVariant(id, v) -> mapNode id (fun n -> { n with Variant = v }) { m with VariantOpen = false }
            | Delete ->
                match m.Selected with
                | Some id -> { m with Nodes = m.Nodes |> List.filter (fun n -> n.Id <> id); Selected = None }
                | None -> m
            | Duplicate ->
                match selectedNode m with
                | Some n ->
                    let c = { n with Id = m.NextId; X = n.X + 16.0f; Y = n.Y + 16.0f }
                    { m with Nodes = m.Nodes @ [ c ]; NextId = m.NextId + 1; Selected = Some c.Id }
                | None -> m
            | BringToFront ->
                match selectedNode m with
                | Some n -> { m with Nodes = (m.Nodes |> List.filter (fun x -> x.Id <> n.Id)) @ [ n ] }
                | None -> m
            | ToggleTheme -> { m with DarkDesign = not m.DarkDesign }
            | TogglePreview -> { m with Preview = not m.Preview; Selected = (if m.Preview then m.Selected else None) }
            | ToggleCode -> { m with ShowCode = not m.ShowCode; CodeScroll = 0.0f }
            | ScrollCode v -> { m with CodeScroll = v }
            | Key key ->
                // Tab, Enter, Space and the arrows on a focused control are the toolkit's (Ui.root): what reaches here is typing into the text of an element, and the keys
                // of the surface: the arrows nudge the selected element, Delete removes it, Escape deselects.
                match m.Ui.Focus, selectedNode m with
                // text typed on the surface: Enter and Escape end it, the rest is the text (Backspace and the arrows of the text, not the element)
                | _ when m.Inline.IsSome ->
                    (match key, m.Inline |> Option.bind (fun id -> m.Nodes |> List.tryFind (fun n -> n.Id = id)) with
                     | ("Enter" | "Escape"), _ -> { m with Inline = None }
                     | k, Some n when k <> "Tab" -> mapNode n.Id (fun n -> { n with Text = TextEdit.apply m.Ui.Shift k n.Text }) m
                     | _ -> m)
                | _ when key = "Escape" && m.Drag <> Idle -> { m with Drag = Idle }
                | _ when key = "Escape" && m.LayerDrag <> Idle -> { m with LayerDrag = Idle }
                | Some "insp-text", Some n when key <> "Tab" ->
                    match key with
                    | "Enter" | "Escape" -> { m with Ui = UiState.update m.Ui UiMsg.Blur }
                    | k -> mapNode n.Id (fun n -> { n with Text = TextEdit.apply m.Ui.Shift k n.Text }) m
                | _, Some n ->
                    let d = 8.0f
                    match key with
                    | "Delete" | "Backspace" -> { m with Nodes = m.Nodes |> List.filter (fun x -> x.Id <> n.Id); Selected = None }
                    | "F2" when Kind.hasText n.Kind -> { m with Inline = Some n.Id }
                    | "PageUp" -> moveLayer m n.Id -1
                    | "PageDown" -> moveLayer m n.Id 1
                    | "Left" -> mapNode n.Id (fun n -> { n with X = max 0.0f (n.X - d) }) m
                    | "Right" -> mapNode n.Id (fun n -> { n with X = n.X + d }) m
                    | "Up" -> mapNode n.Id (fun n -> { n with Y = max 0.0f (n.Y - d) }) m
                    | "Down" -> mapNode n.Id (fun n -> { n with Y = n.Y + d }) m
                    | "Escape" -> { m with Selected = None }
                    | _ -> m
                | _ -> m
            | Typed text ->
                match m.Ui.Focus, selectedNode m with
                | _ when m.Inline.IsSome -> mapNode m.Inline.Value (fun n -> { n with Text = TextEdit.typed text n.Text }) m
                | Some "insp-text", Some n -> mapNode n.Id (fun n -> { n with Text = TextEdit.typed text n.Text }) m
                | _ -> m

    /// A step, and the end of the typing on the surface when the element is no longer the one selected, or is gone, or the surface is not being edited.
    let private step (m: Model) (msg: Msg) : Model =
        let r = stepOnce m msg
        match r.Inline with
        | Some id when r.Selected <> Some id || r.Preview || not (r.Nodes |> List.exists (fun n -> n.Id = id)) -> { r with Inline = None }
        | _ -> r

    let private snapshot (m: Model) : Snapshot = { Nodes = m.Nodes; NextId = m.NextId; Selected = m.Selected }

    /// The set of elements as one item, so that the log of changes is `Replace [0]` of it.
    let private shape : Shape<Snapshot> = { Kids = (fun _ -> []); WithKids = (fun s _ -> s) }

    /// What ends a step of the log, so that the next change is a step of its own: another element, another text.
    let private closed (h: History<Snapshot>) : History<Snapshot> = { h with Open = "" }

    /// Which steps of the log one message continues: the moves of one drag, the width of one drag, the keystrokes into one text, the nudges of one element are one step.
    let private logKey (m: Model) (msg: Msg) : string =
        let sel = (match m.Selected with Some id -> string id | None -> "")
        match msg, m.Ui.Focus with
        // the text typed on the surface is one step, whatever the keys are (Backspace is no step of its own there)
        | Key ("Enter" | "Escape"), _ when m.Inline.IsSome -> ""
        | (Key _ | Typed _), _ when m.Inline.IsSome -> "inline:" + sel
        | MoveTo(id, _, _), _ -> "move:" + string id
        | SetW(id, _), _ -> "w:" + string id
        | SetText(id, _), _ -> "text:" + string id
        | SetValue(id, _), _ -> "value:" + string id
        | SetFontSize(id, _), _ -> "fontsize:" + string id
        | SetRotation(id, _), _ -> "rotation:" + string id
        | SetOpacity(id, _), _ -> "opacity:" + string id
        | Key ("Delete" | "Backspace"), None -> ""
        | Key "Tab", _ | Key "Enter", _ | Key "Escape", _ -> ""
        | (Key _ | Typed _), Some f when f.StartsWith "insp-" -> f + ":" + sel
        | Key _, None -> "nudge:" + sel
        | _ -> ""

    let private restore (s: Snapshot) (h: History<Snapshot>) (m: Model) : Model =
        { m with Nodes = s.Nodes; NextId = s.NextId; Selected = s.Selected; History = h; VariantOpen = false }

    let canUndo (m: Model) : bool = History.canUndo m.History
    let canRedo (m: Model) : bool = History.canRedo m.History

    let update (m: Model) (msg: Msg) : Model * Cmd<Msg> =
        match msg with
        | Undo | Key "Undo" ->
            (match History.undo shape [ snapshot m ] m.History with
             | Some ([ s ], h) -> restore s h m
             | _ -> m), Cmd.none
        | Redo | Key "Redo" ->
            (match History.redo shape [ snapshot m ] m.History with
             | Some ([ s ], h) -> restore s h m
             | _ -> m), Cmd.none
        // the pointer and the focus move all the time and change no element: no log, no comparison
        | UiMsg u ->
            let m' = step m msg
            // another control gets the focus: what is typed next is a change of its own; the focus of an element on the surface is its selection
            (match u with
             | UiMsg.Focus id when id.StartsWith "node-" ->
                { m' with Selected = Numbers.parseInt (id.Substring 5); History = closed m'.History }
             | UiMsg.Focus _ | UiMsg.Blur -> { m' with History = closed m'.History }
             | _ -> m'), Cmd.none
        | _ ->
            let m' = step m msg
            if m'.Nodes = m.Nodes && m'.NextId = m.NextId then
                // another selection ends the step in progress (the next drag is another step)
                (match msg with Select _ -> { m' with History = closed m'.History } | _ -> m'), Cmd.none
            else
                { m' with History = History.record (logKey m msg) (Replace ([ 0 ], snapshot m, snapshot m')) m.History }, Cmd.none
