namespace AvalonLog


module Util =

    /// This ignore only work on Value types, 
    /// Objects and functions need to be ignored with 'ignoreObj'
    /// This is to prevent accidetially ignoring partially aplied functions that would return struct
    let inline ignore (x:'T when 'T: struct) = ()

    /// Ignores any object (and struct)
    /// For structs use 'ignore'
    let inline ignoreObj (x:obj) = ()

    let inline notNull x = match x with null -> false | _ -> true  //not (Object.ReferenceEquals(ob,null))
    
    /// returns maybeNullvalue if it is not null, else alternativeValue 
    let inline ifNull alternativeValue maybeNullvalue = match maybeNullvalue with null -> alternativeValue | _ -> maybeNullvalue  



open System
open System.Threading
open System.Windows.Threading

type Sync private () =    
        
    static let mutable ctx : SynchronizationContext = null  // will be set in main UI STAThread    
    
    /// the UI SynchronizationContext to switch to inside async CEs
    static member syncContext = ctx
    
    /// to ensure SynchronizationContext is set up.
    static member  installSynchronizationContext () =         
        if SynchronizationContext.Current = null then 
            DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher) |> SynchronizationContext.SetSynchronizationContext
        ctx <- SynchronizationContext.Current
            
        if isNull ctx then 
            // reporting this to the UI instead would not work since there is no sync context for the UI
            let time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff") // to ensure unique file names  
            let filename = sprintf "AvalonLog-SynchronizationContext failed-%s.txt" time
            let desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            let file = IO.Path.Combine(desktop,filename)
            try IO.File.WriteAllText(file, "Failed to get DispatcherSynchronizationContext") with _ -> () // file might be open or locked    
    
    
        
    static member doSync (f) = 
        async {
            do! Async.SwitchToContext ctx
            f()
            } |> Async.StartImmediate
    
    
    

module Brush = 
    
    open System.Windows.Media // for color brushes

    /// Adds bytes to each color channel to increase brightness, negative values to make darker
    /// result will be clamped between 0 and 255
    let changeLuminace (amount:int) (col:Windows.Media.Color)=
        let inline clamp x = if x<0 then 0uy elif x>255 then 255uy else byte(x)
        let r = int col.R + amount |> clamp      
        let g = int col.G + amount |> clamp
        let b = int col.B + amount |> clamp
        Color.FromArgb(col.A, r,g,b)
    
    ///Adds bytes to each color channel to increase brightness
    /// result will be clamped between 0 and 255
    let brighter (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminace amount br.Color) 
    
    ///Removes bytes from each color channel to increase darkness, 
    /// result will be clamped between 0 and 255
    let darker  (amount:int) (br:SolidColorBrush)  = SolidColorBrush(changeLuminace -amount br.Color) 


    //make it therad safe and faster
    let inline freeze(br:SolidColorBrush)= 
        if not br.IsFrozen then
            if br.CanFreeze then br.Freeze()
            else                 eprintfn "Could not freeze SolidColorBrush: %A" br         
        br
    
    /// returns a frozen SolidColorBrush
    let make (red,green,blue) = 
        let r = byte red
        let g = byte green
        let b = byte blue
        freeze (new SolidColorBrush(Color.FromRgb(r,g,b)))  

