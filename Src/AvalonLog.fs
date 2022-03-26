namespace AvalonLog

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
open AvalonEditB.Document


/// A TextWriter that writes using a function.
/// To set Console.Out to a text writer get one via AvalonLog.GetTextWriter(red,green,blue)
type LogTextWriter(write,writeLine) = 
    inherit TextWriter()
    override _.Encoding = Text.Encoding.Default // ( UTF-16 )
    
    override _.Write     (s:string)  = 
        //if s.Contains "\u001b" then  write ("esc"+s) else write ("?"+s) //debugging for using  spectre ?? https://github.com/spectreconsole/spectre.console/discussions/573            
        write (s) 
    
    override _.WriteLine (s:string)  = // actually never used in F# printfn, but maybe buy other too using the console or error out , see  https://github.com/dotnet/fsharp/issues/3712
        //if s.Contains "\u001b" then  writeLine ("eSc"+s) else writeLine ("?"+s) 
        writeLine (s)    
    
    override _.WriteLine ()          = writeLine ("")


    (*
       trying to enable ANSI Control sequences for https://github.com/spectreconsole/spectre.console

       but doesn't work yet ESC char seam to be swallowed by Console.SetOut to textWriter. see:

       //https://stackoverflow.com/a/34078058/969070
       //let stdout = Console.OpenStandardOutput()
       //let con = new StreamWriter(stdout, Encoding.ASCII)      
       
       The .Net Console.WriteLine uses an internal __ConsoleStream that checks if the Console.Out is as file handle or a console handle. 
       By default it uses a console handle and therefor writes to the console by calling WriteConsoleW. In the remarks you find:
       
       Although an application can use WriteConsole in ANSI mode to write ANSI characters, consoles do not support ANSI escape sequences. 
       However, some functions provide equivalent functionality. For more information, see SetCursorPos, SetConsoleTextAttribute, and GetConsoleCursorInfo.
       
       To write the bytes directly to the console without WriteConsoleW interfering a simple file-handle/stream will do which is achieved by calling OpenStandardOutput. 
       By wrapping that stream in a StreamWriter so we can set it again with Console.SetOut we are done. The byte sequences are send to the OutputStream and picked up by AnsiCon.
  
       let strWriter = l.AvalonLog.GetStreamWriter( LogColors.consoleOut) // Encoding.ASCII ??  
       Console.SetOut(strWriter)
/// A TextWriter that writes using a function.
/// To set Console.Out to a text writer get one via AvalonLog.GetTextWriter(red,green,blue)
type LogStreamWriter(ms:MemoryStream,write,writeLine) = 
    inherit StreamWriter(ms)
    override _.Encoding = Text.Encoding.Default // ( UTF-16 )
    override _.Write (s:string) :  unit = 
        if s.Contains "\u001b" then  write ("esc"+s) else write ("?"+s) //use specter ?? https://github.com/spectreconsole/spectre.console/discussions/573            
        //write (s) 
    override _.WriteLine (s:string)  :  unit = // actually never used in F# printfn, but maybe buy other too using the console or error out , see  https://github.com/dotnet/fsharp/issues/3712
        if s.Contains "\u001b" then  writeLine ("eSc"+s) else writeLine ("?"+s) 
        //writeLine (s)    
    override _.WriteLine ()          = writeLine ("")
       *)




/// <summary>A ReadOnly text AvalonEdit Editor that provides colored appending via printfn like functions. </summary>
/// <remarks>Use the hidden member AvalonEdit if you need to access the underlying TextEditor class from AvalonEdit for styling.
/// Don't append or change the AvalonEdit.Text property directly. This will mess up the coloring.
/// Only use the printfn and Append functions of this class.</remarks>
type AvalonLog () = 
    inherit ContentControl()  // the most simple and generic type of UIelement container, like a <div> in html

    /// Stores all the locations where a new color starts.
    /// Will be searched via binary search in colorizing transformers
    let offsetColors = ResizeArray<NewColor>( [ {off = -1 ; brush=null} ] )    // null is console out // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) ..


    /// Same as default foreground in underlaying AvalonEdit.
    /// Will be changed if AvalonEdit foreground brush changes
    let mutable defaultBrush    = Brushes.Black     |> freeze // should be same as default foreground. Will be set on foreground color changes

    /// Used for printing with custom rgb values
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
    let colo = new ColorizingTransformer(log, offsetColors, defaultBrush)

    do
        base.Content <- log  //nest Avalonedit inside a simple ContentControl to hide most of its functionality

        log.FontFamily <- FontFamily("Consolas")
        log.FontSize <- 14.0
        log.IsReadOnly <- true
        log.Encoding <- Text.Encoding.Default // = UTF-16
        log.ShowLineNumbers  <- true
        log.Options.EnableHyperlinks <- true
        log.TextArea.SelectionCornerRadius <- 0.0
        log.TextArea.SelectionBorder <- null
        log.TextArea.TextView.LinkTextForegroundBrush <- Brushes.Blue |> Brush.freeze //Hyper-links color


        log.TextArea.TextView.LineTransformers.Add(colo) // to actually draw colored text
        log.TextArea.SelectionChanged.Add colo.SelectionChangedDelegate // to exclude selected text from being colored

        // to highlight all instances of the selected word
        log.TextArea.TextView.LineTransformers.Add(hiLi)
        log.TextArea.SelectionChanged.Add hiLi.SelectionChangedDelegate

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
    let mutable dontPrintJustBuffer = false // for use in this.Clear() to make sure a print after a clear does not get swallowed



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

    /// must be called in sync
    let printToLog() = 
        let txt = lock buffer getBufferText //lock for safe access
        if txt.Length > 0 then //might be empty from calls during dontPrintJustBuffer = true
            log.AppendText(txt)     // TODO is it possible that avalonedit skips adding some escape ANSI characters to document?? then docLength could be out of sync !! TODO
            log.ScrollToEnd()
            if log.WordWrap then log.ScrollToEnd() //this is needed a second time. see  https://github.com/dotnet/fsharp/issues/3712
            stopWatch.Restart()
    
    let newLine = Environment.NewLine


    /// Adds string on UI thread  every 150ms then scrolls to end after 300ms.
    /// Optionally adds new line at end.
    /// Sets line color on LineColors dictionary for DocumentColorizingTransformer.
    /// printOrBuffer (txt:string, addNewLine:bool, typ:SolidColorBrush)
    let printOrBuffer (txt:string, addNewLine:bool, brush:SolidColorBrush) = // TODO check for escape sequence characters and dont print or count them, how many are skiped by avaedit during Text.Append??
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
            if docLength > maxCharsInLog then // needed when log gets piled up with exception messages form Avalonedit rendering pipeline.
                stillLessThanMaxChars <- false                
                log.Dispatcher.Invoke(printToLog) 
                let itsOverTxt = sprintf "%s%s  **** STOP OF LOGGING **** Log has more than %d characters! Clear Log view first %s%s%s%s " newLine newLine maxCharsInLog  newLine newLine  newLine newLine
                lock buffer (fun () ->  
                     offsetColors.Add { off = docLength; brush = Brushes.Red |> freeze}
                     buffer.AppendLine(itsOverTxt)  |> ignoreObj
                     docLength <- docLength + itsOverTxt.Length
                    )
                log.Dispatcher.Invoke(printToLog)
                    
                //previous version: (suffers from race condition where Async.SwitchToContext SyncAvalonLog.context does not work)
                //async {
                //    do! Async.SwitchToContext SyncAvalonLog.context
                //    printToLog()// runs with a lock too                    
                //    log.AppendText(sprintf "%s%s  *** STOP OF LOGGING *** Log has more than %d characters! clear Log view first" newLine newLine maxCharsInLog)
                //    log.ScrollToEnd()
                //    log.ScrollToEnd() // call twice because of https://github.com/icsharpcode/AvalonEdit/issues/226
                //    } |> Async.StartImmediate

            // check if we are in the process of clearing the view
            elif dontPrintJustBuffer then // wait really long before printing
                async {
                    let k = Interlocked.Increment printCallsCounter
                    do! Async.Sleep 100
                    while dontPrintJustBuffer do // wait till dontPrintJustBuffer is set true from end of this.Clear() call
                        do! Async.Sleep 100
                    if !printCallsCounter = k  then //it is the last call for 100 ms
                        // on why using Invoke: https://stackoverflow.com/a/19009579/969070
                        log.Dispatcher.Invoke(printToLog)
                    } |> Async.StartImmediate

            // normal case:
            else
                // check the two criteria for actually printing
                // PRINT CASE 1: since the last printing call more than 100 ms have elapsed. this case is used if a lot of print calls arrive at the log for a more than 100 ms.
                // PRINT CASE 2, wait 70 ms and print if nothing else has been added to the buffer during the last 70 ms

                if stopWatch.ElapsedMilliseconds > 100L  then // PRINT CASE 1: only add to document every 100ms
                    // printToLog() will also reset stopwatch.
                    // on why using Invoke: https://stackoverflow.com/a/19009579/969070
                    log.Dispatcher.Invoke( printToLog) // TODO a bit faster probably and less verbose than async here but would this propagate exceptions too ?

                    //previous version:
                    //async {
                    //    do! Async.SwitchToContext SyncAvalonLog.context // slower to start but this would this propagate exceptions too ?
                    //    printToLog() // runs with a lock too
                    //    } |> Async.StartImmediate

                else
                    /// do timing as low level as possible: see Async.Sleep in  https://github.com/dotnet/fsharp/blob/main/src/fsharp/FSharp.Core/async.fs#L1587
                    let mutable timer :option<Timer> = None
                    let k = Interlocked.Increment printCallsCounter
                    let action =  TimerCallback(fun _ ->
                        if !printCallsCounter = k  then  log.Dispatcher.Invoke(printToLog) //PRINT CASE 2, it is the last call for 70 ms, there has been no other Increment to printCallsCounter
                        if timer.IsSome then timer.Value.Dispose() // dispose inside callback, like in Async.Sleep
                        )
                    timer <- Some (new Threading.Timer(action, null, dueTime = 70 , period = -1))

                    // previous version:
                    //async {
                    //    let k = Interlocked.Increment printCallsCounter
                    //    do! Async.Sleep 100
                    //    if !printCallsCounter = k  then //PRINT CASE 2, it is the last call for 100 ms
                    //        log.Dispatcher.Invoke(printToLog)
                    //    } |> Async.StartImmediate

     //-----------------------------------------------------------
     //----------------------exposed AvalonEdit members:------------------------------------------
     //------------------------------------------------------------


    member  _.VerticalScrollBarVisibility   with get() = log.VerticalScrollBarVisibility     and set v = log.VerticalScrollBarVisibility <- v
    member  _.HorizontalScrollBarVisibility with get() = log.HorizontalScrollBarVisibility   and set v = log.HorizontalScrollBarVisibility <- v
    member  _.FontFamily       with get() = log.FontFamily                  and set v = log.FontFamily <- v
    member  _.FontSize         with get() = log.FontSize                    and set v = log.FontSize  <- v
    //member  _.Encoding         with get() = log.Encoding                    and set v = log.Encoding <- v
    member  _.ShowLineNumbers  with get() = log.ShowLineNumbers             and set v = log.ShowLineNumbers <- v
    member  _.EnableHyperlinks with get() = log.Options.EnableHyperlinks    and set v = log.Options.EnableHyperlinks  <- v

    /// Get all text in this AvalonLog
    member _.Text() = log.Text

    /// Get all text in Segment AvalonLog
    member _.Text(seg:ISegment) = log.Document.GetText(seg)

    /// Get the current Selection
    member _.Selection = log.TextArea.Selection

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
    //----------------------AvalonLog specific members:----------
    //------------------------------------------------------------

    /// The maximum amount of characters this AvaloLog can display.
    /// By default this about one Million characters
    /// This is to avoid freezing the UI when the AvaloLog is flooded with text.
    /// When the maximum is reached a message will be printed at the end, then the printing stops until the content is cleared.
    member _.MaximumCharacterAllowance
        with get () = maxCharsInLog
        and  set v  = maxCharsInLog <- v


    /// To access the underlying  AvalonEdit TextEditor class
    /// Don't append , clear or modify the Text property directly!
    /// This will mess up the coloring.
    /// Only use the printfn family of functions to add text to AvalonLog
    /// Use this member only for styling changes
    /// use #nowarn "44" to disable the obsolete warning
    [<Obsolete("It is not actually obsolete, but normally not used, so hidden from editor tools. In F# use #nowarn \"44\" to disable the obsolete warning")>]
    member _.AvalonEdit = log

    /// The Highlighter for selected text
    member _.SelectedTextHighLighter = hiLi

    /// Clear all Text. (thread-safe)
    /// The Color of the last print will still be remembered
    /// e.g. for log.AppendWithLastColor(..)
    member _.Clear() :unit = 
        lock buffer (fun () ->
            dontPrintJustBuffer <- true
            buffer.Clear() |>  ignoreObj
            docLength <- 0
            prevMsgBrush <- null
            stillLessThanMaxChars <- true
            printCallsCounter := 0L            
            )

        // log.Dispatcher.Invoke needed.
        // If this would be done via async{ and do! Async.SwitchToContext a subsequent call via Dispatcher.Invoke ( like print to log) would still come before.
        // It starts faster than async with SwitchToContext
        log.Dispatcher.Invoke( fun () ->
            log.Clear()
            offsetColors.Clear() // this should be done after log.clear() to avoid race condition where log tries to redraw but offsetColors is already empty
            offsetColors.Add {off = -1 ; brush=null}  // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) ..
            //log.SelectionLength <- 0
            //log.SelectionStart <- 0
            defaultBrush <- (log.Foreground.Clone() :?> SolidColorBrush |> Brush.freeze)   // TODO or remember custom brush ?
            log.TextArea.TextView.linesCollapsedVisualPosOffThrowCount <- 0 // custom property in AvalonEditB to avoid throwing too many exceptions. set 0 so exceptions appear again
            stopWatch.Restart() // works async too
            dontPrintJustBuffer <- false // this is important to release pending prints stuck in while loop in printOrBuffer()
            )


    /// Returns a thread-safe Textwriter that prints to AvalonLog in Color
    /// for use as use System.Console.SetOut(textWriter)
    /// or System.Console.SetError(textWriter)
    member _.GetTextWriter(red, green, blue) = 
        let br = Brush.ofRGB red green blue
        new LogTextWriter   (fun s -> printOrBuffer (s, false, br)
                            ,fun s -> printOrBuffer (s, true , br)
                            )

    /// Returns a thread-safe Textwriter that prints to AvalonLog in Color
    /// for use as use System.Console.SetOut(textWriter)
    /// or System.Console.SetError(textWriter)
    member _.GetTextWriter(br:SolidColorBrush) = 
        let fbr = br|> freeze
        new LogTextWriter   (fun s -> printOrBuffer (s, false, fbr)
                            ,fun s -> printOrBuffer (s, true , fbr)
                            )

    /// Returns a thread-safe Textwriter that only prints to AvalonLog
    /// if the predicate returns true for the string sent to the text writer.
    /// The provide Color will be used.
    member _.GetConditionalTextWriter(predicate:string->bool, br:SolidColorBrush) = 
        let fbr = br|> freeze
        new LogTextWriter   (fun s -> if predicate s then printOrBuffer (s, false, fbr)
                            ,fun s -> if predicate s then printOrBuffer (s, true , fbr)
                            )


    /// Returns a thread-safe Textwriter that only prints to AvalonLog
    /// if the predicate returns true for the string sent to the text writer.
    /// The predicate can also be used for other side effects before printing.
    /// The provided red, green and blue Color values will be used will be used.
    /// Integers will be clamped to be between 0 and 255
    member _.GetConditionalTextWriter(predicate:string->bool, red, green, blue) = 
        let br = Brush.ofRGB red green blue
        new LogTextWriter   (fun s -> if predicate s then printOrBuffer (s, false, br)
                            ,fun s -> if predicate s then printOrBuffer (s, true , br)
                            )

    (*
    part of trying to enable ANSI Control sequences for https://github.com/spectreconsole/spectre.console
    https://stackoverflow.com/a/34078058/969070

    member _.GetStreamWriter(br:SolidColorBrush) = 
        let fbr = br|> freeze
        new LogStreamWriter (new MemoryStream()
                            ,fun s -> printOrBuffer (s, false, fbr)
                            ,fun s -> printOrBuffer (s, true , fbr)
                            )
    
    member _.GetStreamWriter(red, green, blue) = 
        let br = Brush.ofRGB red green blue
        new LogStreamWriter (new MemoryStream()
                            ,fun s -> printOrBuffer (s, false, br)
                            ,fun s -> printOrBuffer (s, true , br)
                            )
    *)

    //--------------------------------------
    // Append string:
    //--------------------------------------

    /// Print string using default color (Black)
    member _.Append (s) = 
        printOrBuffer (s, false, defaultBrush )

    /// Print string using red, green and blue color values (each between 0 and 255).
    /// (without adding a new line at the end).
    member _.AppendWithColor (red, green, blue, s) = 
        setCustomBrush (red,green,blue)
        printOrBuffer (s, false, customBrush )

    /// Print string using the Brush provided.
    /// (without adding a new line at the end).
    member _.AppendWithBrush (br:SolidColorBrush, s) = 
        customBrush <- br
        printOrBuffer (s, false, customBrush )

    /// Print string using the last Brush or color provided.
    /// (without adding a new line at the end
    member _.AppendWithLastColor (s) = 
        printOrBuffer (s, false, customBrush)

    //--------------------------------------
    // AppendLine string:
    //--------------------------------------

    /// Print string using default color (Black)
    /// Adds a new line at the end
    member _.AppendLine (s) = 
        printOrBuffer (s, true, defaultBrush )

    /// Print string using red, green and blue color values (each between 0 and 255).
    /// Adds a new line at the end
    member _.AppendLineWithColor (red, green, blue, s) = 
        setCustomBrush (red,green,blue)
        printOrBuffer (s, true, customBrush )

    /// Print string using the Brush provided.
    /// Adds a new line at the end.
    member _.AppendLineWithBrush (br:SolidColorBrush, s) = 
        customBrush <- br
        printOrBuffer (s, true, customBrush)

    /// Print string using the last Brush or color provided.
    /// Adds a new line at the end
    member _.AppendLineWithLastColor (s) = 
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
