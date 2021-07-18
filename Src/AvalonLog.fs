﻿namespace AvalonLog

open AvalonLog.Util
open AvalonLog.Brush
open System
open System.IO
open System.Threading
open AvalonEditB
open System.Windows.Media // for color brushes
open System.Text
open System.Diagnostics
open System.Windows.Controls


/// A TextWriter that writes using a function. 
/// To set Console.Out to a text writer get one via AvalonLog.GetTextWriter(red,green,blue)
type LogTextWriter(write,writeLine) =
    inherit TextWriter()
    override _.Encoding =  Text.Encoding.Default
    override _.Write     (s:string)  = write (s)
    override _.WriteLine (s:string)  = writeLine (s)    // actually never used in F# see  https://github.com/dotnet/fsharp/issues/3712   
    override _.WriteLine ()          = writeLine ("")    


/// A ReadOnly text AvalonEdit Editor that provides colored appending via printfn like functions
/// the property this.AvalonEdit holds the UI control.
/// Dont append or change the AvalonEdit.Text property directly. This will mess up the coloring
/// only use the printfn functions of this class
type AvalonLog () =    
    inherit ContentControl()  // the most simple and generic type of UIelement container, like a <div> in html  
    
    /// Stores all the locations where a new color starts. 
    /// Will be searched via binary serach in colorizing transformers
    let offsetColors = ResizeArray<NewColor>( [ {off = -1 ; brush=null} ] )    // null is console out // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) ..     
    

    /// Same as default forground in underlaying AvalonEdit. 
    /// Will be set on AvalonEdit foreground brush changes
    let mutable defaultBrush    = Brushes.Black     |> freeze // should be same as default forground. Will be set on foreground changes
    
    /// used for printing with rgb values
    let mutable customBrush     = Brushes.Black     |> freeze   // will be changed anyway on first call
    
    let setCustomBrush(red,green,blue) = 
        let r = clampToByte red
        let g = clampToByte green
        let b = clampToByte blue
        let col = customBrush.Color
        if col.R <> r || col.G <> g || col.B <> b then // only change if different
            customBrush <- freeze (new SolidColorBrush(Color.FromRgb(r,g,b)))      
    
    let log =  new TextEditor()    
    let hiLi = new SelectedTextHighlighter(log)
    let colo = new ColorizingTransformer(log,offsetColors,defaultBrush)

    do    
        base.Content <- log  //nest Avlonedit inside a simple ContentControl to hide most of its functionality

        log.FontFamily <- FontFamily("Consolas")
        log.FontSize <- 14.0
        log.IsReadOnly <- true
        log.Encoding <- Text.Encoding.Default // = UTF-16
        log.ShowLineNumbers  <- true
        log.Options.EnableHyperlinks <- true 
        log.TextArea.SelectionCornerRadius <- 0.0 
        log.TextArea.SelectionBorder <- null         
        log.TextArea.TextView.LinkTextForegroundBrush <- Brushes.Blue |> Brush.freeze //Hyperlinks color         
        
        log.TextArea.SelectionChanged.Add colo.SelectionChangedDelegate
        log.TextArea.TextView.LineTransformers.Add(colo)
        log.TextArea.SelectionChanged.Add hiLi.SelectionChangedDelegate
        log.TextArea.TextView.LineTransformers.Add(hiLi)

        Search.SearchPanel.Install(log) |> ignoreObj //TODO disable search and replace if using custom build?
        
        defaultBrush <- (log.Foreground.Clone() :?> SolidColorBrush |> Brush.freeze) // just to be sure they are the same
        //log.Foreground.Changed.Add ( fun _ -> LogColors.consoleOut <- (log.Foreground.Clone() :?> SolidColorBrush |> freeze)) // this event attaching can't  be done because it is already frozen

    let printCallsCounter = ref 0L
    let mutable prevMsgBrush = null //null is no color for console // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) .. 
    let stopWatch = Stopwatch.StartNew()
    let buffer =  new StringBuilder()
    let mutable docLength = 0  //to be able to have the doc length async
    let mutable maxCharsInLog = 1024_000 // about 10k lines with 100 chars each
    let mutable stillLessThanMaxChars = true
    let mutable dontPrintJustBuffer = false // for use in this.Clear() to make sur a print after a clear does not get swallowed        
    
    //-----------------------------------------------------------------------------------
    // The below functions are trying to work around double UI update in printfn for better UI performance, 
    // and the poor performance of log.ScrollToEnd().
    // https://github.com/dotnet/fsharp/issues/3712  
    // https://github.com/icsharpcode/AvalonEdit/issues/226
    //-----------------------------------------------------------------------------------

    let getBufferText () =
        let txt = buffer.ToString()
        buffer.Clear()  |> ignoreObj 
        txt

    let printToLog() =          
        let txt = lock buffer getBufferText //lock for safe access   
        if txt.Length>0 then //might be empty from calls during dontPrintJustBuffer = true
            log.AppendText(txt)     // TODO is it possibel that avalon edit skips adding some escape ANSI characters to document?? then docLength could be out of sync !! TODO
            log.ScrollToEnd()
            if log.WordWrap then log.ScrollToEnd() //this is needed a second time. see  https://github.com/dotnet/fsharp/issues/3712  
            stopWatch.Restart()

    let newLine = Environment.NewLine

    /// adds string on UI thread  every 150ms then scrolls to end after 300ms. 
    /// Optionally adds new line at end. 
    /// Sets line color on LineColors dictionay for DocumentColorizingTransformer. 
    /// printOrBuffer (txt:string, addNewLine:bool, typ:SolidColorBrush)
    let printOrBuffer (txt:string, addNewLine:bool, brush:SolidColorBrush) = // TODO check for escape sequence charctars and dont print or count them, how many are skiped by avaedit during Text.Append??
        if stillLessThanMaxChars && (txt.Length <> 0 || addNewLine) then            
            lock buffer (fun () ->  // or rwl.EnterWriteLock() //https://stackoverflow.com/questions/23661863/f-synchronized-access-to-list
                // Change color if needed:
                if prevMsgBrush <> brush then                    
                    offsetColors.Add { off = docLength; brush = brush } // TODO filter out ANSI escape chars first or just keep them in the doc but not in the visual line ??
                    prevMsgBrush <- brush 
            
                // add to buffer
                if addNewLine then
                    buffer.AppendLine(txt)  |> ignoreObj
                    docLength <- docLength + txt.Length + newLine.Length
                else
                    buffer.Append(txt)  |> ignoreObj
                    docLength <- docLength + txt.Length
            )

            // check if total text in log  is already to big , print it and then stop printing
            if docLength > maxCharsInLog then // neded when log gets piled up with exception messages form Avalonedit rendering pipeline.
                stillLessThanMaxChars <- false                
                async { 
                    do! Async.SwitchToContext SyncAvalonLog.context  
                    printToLog()// runs with a lock too
                    log.AppendText(sprintf "%s%s  *** STOP OF LOGGING *** Log has more than %d characters! clear Log view first" newLine newLine maxCharsInLog)
                    log.ScrollToEnd()
                    log.ScrollToEnd() // call twice because of https://github.com/icsharpcode/AvalonEdit/issues/226
                    } |> Async.StartImmediate  
            

            elif dontPrintJustBuffer then // wait really long before printing
                async {                        
                    let k = Interlocked.Increment printCallsCounter
                    do! Async.Sleep 100
                    while dontPrintJustBuffer do // wait till dontPrintJustBuffer is set true from end of this.Clear() call
                        do! Async.Sleep 100
                    if !printCallsCounter = k  then //it is the last call for 500 ms
                        log.Dispatcher.Invoke(printToLog)                 
                    } |> Async.StartImmediate 

            else
                // check the two criteria for actually printing
                // print case 1: sine the last printing more than 100ms have elapsed
                // print case 2, wait 0.1 seconds and print if nothing els has been added to the buffer during the last 100 ms
                if stopWatch.ElapsedMilliseconds > 100L  then // print case 1: only add to document every 100ms  
                    //log.Dispatcher.Invoke( printToLog) // TODO a bit faster probably and less verbose than async here but would this propagate exceptions too ?
                    async { 
                        do! Async.SwitchToContext SyncAvalonLog.context
                        printToLog() // runs with a lock too
                        } |> Async.StartImmediate                 
                else
                    async {                        
                        let k = Interlocked.Increment printCallsCounter
                        do! Async.Sleep 100
                        if !printCallsCounter = k  then //print case 2, it is the last call for 100 ms
                            log.Dispatcher.Invoke(printToLog)                 
                        } |> Async.StartImmediate 
                    
     //-----------------------------------------------------------    
     //----------------------exposed AvalonEdit members:------------------------------------------    
     //------------------------------------------------------------    


    member  _.VerticalScrollBarVisibility   with get() = log.VerticalScrollBarVisibility     and set v = log.VerticalScrollBarVisibility <- v
    member  _.HorizontalScrollBarVisibility with get() = log.HorizontalScrollBarVisibility   and set v = log.HorizontalScrollBarVisibility <- v
    member  _.FontFamily       with get() = log.FontFamily                  and set v = log.FontFamily <- v
    member  _.FontSize         with get() = log.FontSize                    and set v = log.FontSize  <- v
    member  _.Encoding         with get() = log.Encoding                    and set v = log.Encoding <- v   
    member  _.ShowLineNumbers  with get() = log.ShowLineNumbers             and set v = log.ShowLineNumbers <- v     
    member  _.EnableHyperlinks with get() = log.Options.EnableHyperlinks    and set v = log.Options.EnableHyperlinks  <- v

    /// Use true to enable Line Wrap.
    /// setting false will enable Horizontal ScrollBar Visibility 
    /// setting true will disable Horizontal ScrollBar Visibility 
    member _.WordWrap
        with get() = log.WordWrap   
        and set v = 
            if v then 
                log.WordWrap         <- true 
                log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled 
            else
                log.WordWrap         <- false 
                log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto 
        
    //-----------------------------------------------------------    
    //----------------------members:------------------------------------------    
    //------------------------------------------------------------    
    
    /// The maximum amount of charcters this AvaloLog can display.
    /// By default this about one Million characters
    /// This is to avaoid freezing the UI when the AvaloLog is flooded with text.
    /// When the maximum is reached a message will be printed at the end, then the printing stops untill the content is cleared.
    member _.MaximumCharacterAllowance
        with get () = maxCharsInLog
        and  set v  = maxCharsInLog <- v


    /// To access the underlying  AvalonEdit TextEditor
    /// Don't append , clear or modify the Text property directly! 
    /// This will mess up the coloring.
    /// Only use the printfn familly of functions to add text to  AvalonLog
    /// Use this member only for styling changes
    member _.AvalonEditInternal = log //  dangerous to expose this, because text can be manipulated directly !

    /// The Highligther for selected text
    member _.SelectedTextHighLighter = hiLi
    
    /// Clear all Text. (threadsafe)
    /// The Color of the last print will still be remebered
    /// e.g. for log.AppendWithLastColor(..)
    member _.Clear() :unit = 
        dontPrintJustBuffer<- true
        stopWatch.Restart()
        buffer.Clear() |> ignoreObj
        docLength <- 0
        prevMsgBrush <- null
        stillLessThanMaxChars <- true
        printCallsCounter := 0L    
        offsetColors.Clear()
        offsetColors.Add {off = -1 ; brush=null} //TODO use -1 instead? // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) .. 
        async{
            do! Async.SwitchToContext SyncAvalonLog.context        
            lock buffer (fun () ->                             
                log.SelectionLength <- 0
                log.SelectionStart <- 0
                log.Clear()
                defaultBrush <- (log.Foreground.Clone() :?> SolidColorBrush |> Brush.freeze)   // TODO or remeber custstom brush ?
                dontPrintJustBuffer <- false // this is important to release pending prints stuck in while loop in printOrBuffer()
                //log.TextArea.TextView.linesCollapsedVisualPosOffThrowCount <- 0 // custom property in Avalonedit to avoid throwing too many exceptions. set 0 so exceptions appear again // TODO Use custom build from AvalonLog            
            )
        }
        |> Async.StartImmediate  


    /// Returns a threadsafe Textwriter that prints to AvalonLog in Color  
    /// for use as use System.Console.SetOut(textWriter) 
    /// or System.Console.SetError(textWriter)
    member _.GetTextWriter(red,green,blue) =
        let br = Brush.ofRGB red green blue
        new LogTextWriter(
                (fun s -> printOrBuffer (s, false, br)), 
                (fun s -> printOrBuffer (s, true , br)) 
                )

    /// Returns a threadsafe Textwriter that prints to AvalonLog in Color  
    /// for use as use System.Console.SetOut(textWriter) 
    /// or System.Console.SetError(textWriter)
    member _.GetTextWriter(br:SolidColorBrush) =        
        let fbr = br|> freeze
        new LogTextWriter(
                (fun s -> printOrBuffer (s, false, fbr)), 
                (fun s -> printOrBuffer (s, true , fbr)) 
                )

    /// Returns a threadsafe Textwriter that prints to AvalonLog in Color
    /// and also calles another function with all strings it receives
    /// for use as use System.Console.SetOut(textWriter) 
    /// or System.Console.SetError(textWriter)
    member _.GetTextWriterEx(br:SolidColorBrush, alsoDoWithString:string->unit) =        
        let fbr = br|> freeze
        new LogTextWriter(
                (fun s -> printOrBuffer (s, false, fbr) ; alsoDoWithString s                       ), 
                (fun s -> printOrBuffer (s, true , fbr) ; alsoDoWithString (s+Environment.NewLine) ) 
                )


    //--------------------------------------    
    // Append string:
    //-------------------------------------- 
    
    /// Print string using default color (Black)
    member _.Append s = 
        printOrBuffer (s, false, defaultBrush )   

    /// Print string using red, green and blue color values (each between 0 and 255). 
    /// (without adding a new line at the end).
    member _.AppendWithColor red green blue s = 
        setCustomBrush (red,green,blue)
        printOrBuffer (s, false, customBrush )    
    
    /// Print string using the Brush provided.
    /// (without adding a new line at the end).
    member _.AppendWithBrush (br:SolidColorBrush) s = 
        customBrush <- br
        printOrBuffer (s, false, customBrush )       

    /// Print string using the last Brush or color provided.
    /// (without adding a new line at the end
    member _.AppendWithLastColor s =  
        printOrBuffer (s, false, customBrush)

    //--------------------------------------    
    // AppendLine string:
    //--------------------------------------  
    
    /// Print string using default color (Black)
    /// Adds a new line at the end
    member _.AppendLine s = 
        printOrBuffer (s, true, defaultBrush )

    /// Print string using red, green and blue color values (each between 0 and 255). 
    /// Adds a new line at the end
    member _.AppendLineWithColor red green blue s =
        setCustomBrush (red,green,blue)
        printOrBuffer (s, true, customBrush ) 
       
    /// Print string using the Brush provided.    
    /// Adds a new line at the end.
    member _.AppendLineWithBrush (br:SolidColorBrush) s =
        customBrush <- br
        printOrBuffer (s, true, customBrush)        
     
    /// Print string using the last Brush or color provided.
    /// Adds a new line at the end
    member _.AppendLineWithLastColor s =  
        printOrBuffer (s, true, customBrush)
   
   //--------------------------------------
   // with F# string formating:
   //--------------------------------------


    /// F# printf formating using the Brush provided.
    /// (without adding a new line at the end).
    member _.printfBrush (br:SolidColorBrush) s = 
        customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s, false, customBrush))  s    

    /// F# printfn formating using the Brush provided.
    /// Adds a new line at the end.
    member _.printfnBrush (br:SolidColorBrush) s = 
        customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s, true, customBrush))  s       
    
    /// F# printf formating using red, green and blue color values (each between 0 and 255). 
    /// (without adding a new line at the end)
    member _.printfColor red green blue msg =
        setCustomBrush (red,green,blue)
        Printf.kprintf (fun s -> printOrBuffer (s,false, customBrush ))  msg
    
    /// F# printfn formating using red, green and blue color values (each between 0 and 255).
    /// Adds a new line at the end
    member _.printfnColor red green blue msg =          
        setCustomBrush (red,green,blue)
        Printf.kprintf (fun s -> printOrBuffer (s,true, customBrush ))  msg       

    /// F# printf formating using the last Brush or color provided.
    /// (without adding a new line at the end
    member _.printfLastColor msg =     
        Printf.kprintf (fun s -> printOrBuffer (s, false, customBrush))  msg
   
    /// F# printfn formating using the last Brush or color provided.
    /// Adds a new line at the end
    member _.printfnLastColor msg =     
        Printf.kprintf (fun s -> printOrBuffer (s, true, customBrush))  msg