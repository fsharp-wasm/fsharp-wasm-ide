namespace FSharp.Wasm.Ide.WidgetEditor

open FSharp.Math
open FSharp.Graphics.Core
open FSharp.Graphics.Scene
open FSharp.Graphics.Tea
open FSharp.Graphics.Widgets

/// Hosts the TEA program. The scene's event handlers call `Dispatch`, which runs `update`
/// immediately; the next frame's `Build` renders the new model.
type App(text: ITextEngine) =
    let mutable model = fst (Model.init ())

    let program : Program<Model, Msg> =
        { Init = Model.init
          Update = Model.update
          View = fun m -> View.view { Theme = Theme.dark; Text = text; State = m.Ui; Time = 0.0f
                                      Lift = UiMsg; Send = ignore } (Vec2.create 1280.0f 800.0f) m }

    member _.Model = model
    member _.Program = program

    member _.Dispatch(msg: Msg) =
        let m', _cmd = Model.update model msg
        model <- m'

    /// The scene of the current model for a canvas of `size`; `time` (milliseconds) drives the caret and other animations.
    member this.Build(time: float32, size: Vec2) : Scene =
        let ui : Ui<Msg> =
            { Theme = Theme.dark; Text = text; State = model.Ui; Time = time
              Lift = UiMsg; Send = this.Dispatch }
        View.view ui size model
