namespace AvalonLog

open System.Windows.Media // for color brushes
open AvalonLog.Brush

module Global =
    

    /// Same as default forgroundin underlaing AvalonEdit. 
    /// Will be set on AvalonEdit foreground brush changes
    let mutable defaultBrush    = Brushes.Black     |> freeze // should be same as default forground. Will be set on foreground changes

    /// used for printing with rgb values
    let mutable customBrush     = Brushes.Black     |> freeze   // will be changed anyway on first call
    
    let inline clamp (i:int) =
        if   i <=   0 then 0uy
        elif i >= 255 then 255uy
        else byte i

    let setCustom(red,green,blue) = 
        let r = clamp red
        let g = clamp green
        let b = clamp blue
        let col = customBrush.Color
        if col.R <> r || col.G <> g || col.B <> b then // only change if different
            customBrush <- freeze (new SolidColorBrush(Color.FromRgb(r,g,b)))      

