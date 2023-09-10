namespace AvalonLog

/// Utility functions for System.Windows.Media.SolidColorBrush
module Brush = 
    open System
    open System.Windows.Media // for SolidColorBrush

    /// Make it thread-safe and faster
    let inline freeze(br:SolidColorBrush)= 
        if not br.IsFrozen then
            if br.CanFreeze then br.Freeze()
            //else                 eprintfn "Could not freeze SolidColorBrush: %A" br
        br

    /// Clamp int to byte between 0 and 255
    let inline clampToByte (i:int) = 
        if   i <=   0 then 0uy
        elif i >= 255 then 255uy
        else byte i

    /// Get a frozen brush of red, green and blue values.
    /// int gets clamped to 0-255
    let inline ofRGB r g b = 
        SolidColorBrush(Color.FromArgb(255uy, clampToByte r, clampToByte g, clampToByte b))
        |> freeze

    /// Get a transparent frozen brush of alpha, red, green and blue values.
    /// int gets clamped to 0-255
    let inline ofARGB a r g b = 
        SolidColorBrush(Color.FromArgb(clampToByte a, clampToByte r, clampToByte g, clampToByte b))
        |> freeze


    /// Adds bytes to each color channel to increase brightness, negative values to make darker.
    /// Result will be clamped between 0 and 255
    let inline changeLuminance (amount:int) (col:Windows.Media.Color)= 
        let r = int col.R + amount |> clampToByte
        let g = int col.G + amount |> clampToByte
        let b = int col.B + amount |> clampToByte
        Color.FromArgb(col.A, r,g,b)    

    /// Adds bytes to each color channel to increase brightness
    /// result will be clamped between 0 and 255
    let brighter (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminance amount br.Color)

    /// Removes bytes from each color channel to increase darkness,
    /// result will be clamped between 0 and 255
    let darker  (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminance -amount br.Color)




