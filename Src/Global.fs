namespace AvalonLog

open System.Windows.Media // for color brushes

module Global =
    open Brush

    /// Same as default forgroundin underlaing AvalonEdit. 
    /// Will be set on AvalonEdit foreground brush changes
    let mutable defaultBrush    = Brushes.Black     |> freeze // should be same as default forground. Will be set on foreground changes

    /// used for printing with rgb values
    let mutable customBrush     = Brushes.Black     |> freeze   // will be changed anyway on first call

    let setCustom(red,green,blue) = 
        let r = byte red
        let g = byte green
        let b = byte blue
        let col = customBrush.Color
        if col.R <> r || col.G <> g || col.B <> b then // only change if different
            customBrush <- freeze (new SolidColorBrush(Color.FromRgb(r,g,b)))      
