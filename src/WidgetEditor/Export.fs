namespace FSharp.Wasm.Ide.WidgetEditor

open System
open FSharp.Math
open FSharp.Graphics.Widgets

/// Turns the design into F# source that uses the Widgets library directly.
module Export =

    let private esc (s: string) = s.Replace("\\", "\\\\").Replace("\"", "\\\"")
    let private f (v: float32) = sprintf "%.1ff" v

    let private variant = function
        | Button.Primary -> "Button.Primary" | Button.Secondary -> "Button.Secondary"
        | Button.Danger -> "Button.Danger" | Button.Ghost -> "Button.Ghost"

    /// `ValueSome 24.0f` for an element that has the size of its letters set, `ValueNone` for the one that takes the theme's.
    let private size (n: Node) : string = if Kind.hasFontSize n.Kind && n.FontSize > 0.0f then sprintf "(ValueSome %s)" (f n.FontSize) else "ValueNone"

    let private expr (n: Node) : string =
        let id = sprintf "\"n%d\"" n.Id
        match n.Kind with
        | KButton ->
            let width = if n.W > 0.0f then sprintf "; Width = ValueSome %s" (f n.W) else ""
            sprintf "Button.view ui %s { Button.make \"%s\" ignore with Variant = %s%s }" id (esc n.Text) (variant n.Variant) width
        | KLabel -> sprintf "Label.viewSized ui Label.Body %s \"%s\"" (size n) (esc n.Text)
        | KHeading -> sprintf "Label.viewSized ui Label.Heading %s \"%s\"" (size n) (esc n.Text)
        | KToggle -> sprintf "Toggle.view ui %s \"%s\" %b ignore" id (esc n.Text) n.On
        | KCheck -> sprintf "CheckBox.view ui %s \"%s\" %b ignore" id (esc n.Text) n.On
        | KSlider -> sprintf "Slider.view ui %s { Min = 0.0f; Max = 1.0f; Value = %s; Width = %s; OnChange = ignore }" id (f n.Value) (f n.W)
        | KText -> sprintf "TextField.view ui %s { Value = \"%s\"; Placeholder = \"Type here\"; Width = %s }" id (esc n.Text) (f n.W)
        | KProgress -> sprintf "ProgressBar.view ui %s %s" (f n.W) (f n.Value)
        | KCard -> sprintf "Panel.view ui Panel.card (Label.viewSized ui Label.Heading %s \"%s\")" (size n) (esc n.Text)
        | KDivider -> sprintf "Divider.horizontal ui %s" (f n.W)

    /// `centre` is the middle of an element as it is built: what a turned element turns about.
    let generate (centre: Node -> Vec2) (m: Model) : string list =
        [ yield "open FSharp.Math"
          yield "open FSharp.Graphics.Scene"
          yield "open FSharp.Graphics.SceneDsl"
          yield "open FSharp.Graphics.Widgets"
          yield ""
          yield "let view (ui: Ui<'Msg>) : Scene ="
          yield "    Group ["
          for n in m.Nodes do
              let c = centre n
              let turned = if n.Rotation = 0.0f then "" else sprintf " |> Scene.rotate %s (Vec2.create %s %s)" (f n.Rotation) (f c.X) (f c.Y)
              let faded = if n.Opacity >= 1.0f then "" else sprintf " |> Scene.opacity %s" (f n.Opacity)
              yield sprintf "        %s%s%s |> shift %d %d" (expr n) turned faded (int n.X) (int n.Y)
          yield "    ]" ]
