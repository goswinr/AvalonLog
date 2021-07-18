namespace AvalonLog

/// Shadows the ignore function to only accept structs
/// This is to prevent accidetially ignoring partially aplied functions that would return struct
module Util =
    
    /// Shadows the original 'ignore' function
    /// This is to prevent accidetially ignoring partially applied functions 
    /// This 'ignore' only work on value types (struct), 
    /// Reference types like objects and functions need to be ignored with 'ignoreObj'    
    let inline ignore (x:'T when 'T: struct) = ()

    /// Ignores any object (but not struct)
    /// For structs use 'ignore'
    let inline ignoreObj (x:obj) = ()

    /// the same as 'not isNull'
    let inline notNull x = match x with null -> false | _ -> true  
    
 
/// Utility functions for System.Windows.Media.SolidColorBrush       
module Brush = 
    open System
    open System.Windows.Media // for SolidColorBrush
    
    /// Clamp int to byte between 0 and 255
    let inline clampToByte (i:int) =
        if   i <=   0 then 0uy
        elif i >= 255 then 255uy
        else byte i

    /// Adds bytes to each color channel to increase brightness, negative values to make darker.
    /// Result will be clamped between 0 and 255
    let inline changeLuminace (amount:int) (col:Windows.Media.Color)=       
        let r = int col.R + amount |> clampToByte      
        let g = int col.G + amount |> clampToByte
        let b = int col.B + amount |> clampToByte
        Color.FromArgb(col.A, r,g,b)
  
    /// Adds bytes to each color channel to increase brightness
    /// result will be clamped between 0 and 255
    let brighter (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminace amount br.Color) 
  
    /// Removes bytes from each color channel to increase darkness, 
    /// result will be clamped between 0 and 255
    let darker  (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminace -amount br.Color) 

    /// Make it therad safe and faster
    let inline freeze(br:SolidColorBrush)= 
        if not br.IsFrozen then
            if br.CanFreeze then br.Freeze()
            //else                 eprintfn "Could not freeze SolidColorBrush: %A" br         
        br
  

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

    
  