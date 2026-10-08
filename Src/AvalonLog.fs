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


/// <summary>A ReadOnly text AvalonEdit Editor that provides colored appending via printfn like functions. </summary>
/// <remarks>Use the hidden member AvalonEdit if you need to access the underlying TextEditor class from AvalonEdit for styling.
/// Don't append or change the AvalonEdit.Text property directly. This will mess up the coloring.
/// Only use the printfn and Append functions of this class.</remarks>
type AvalonLog () =
    inherit ContentControl()  // the most simple and generic type of UIelement container, like a <div> in html

    /// Stores all the locations where a new color starts.
    /// Will be searched via binary search in colorizing transformers.
    /// Only used on the UI thread.
    let offsetColors = ResizeArray<NewColor>( [ {off = -1 ; brush=null} ] )    // null is console out // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) ..

    /// The color changes in the text that is still in the buffer.
    /// The offsets are relative to the start of the buffer.
    /// Guarded by lock buffer. Moved to offsetColors on the UI thread in printToLog().
    let pendingColors = ResizeArray<NewColor>()


    /// Same as default foreground in underlying AvalonEdit.
    /// Will be changed if AvalonEdit foreground brush changes
    let mutable defaultBrush    = Brushes.Black     |> freeze // should be same as default foreground. Will be set on foreground color changes

    /// The last brush or color used, for e.g. AppendWithLastColor.
    /// Each print call reads it only once, so that a print from another thread can't change the color in between.
    let mutable customBrush     = Brushes.Black     |> freeze   // will be changed anyway on first call

    /// Frozen brushes by their RGB value, so that printing with the same color does not create a new brush each time.
    let brushCache = Collections.Concurrent.ConcurrentDictionary<int, SolidColorBrush>()

    /// Returns a frozen brush for these red, green and blue values (each clamped to 0-255).
    let getBrush(red,green,blue) =
        let r = clampToByte red
        let g = clampToByte green
        let b = clampToByte blue
        let key = (int r <<< 16) ||| (int g <<< 8) ||| int b
        match brushCache.TryGetValue key with
        | true, br -> br
        | _ ->
            if brushCache.Count > 1000 then brushCache.Clear() // in case a lot of different colors get used, e.g. for a gradient
            brushCache.GetOrAdd(key, freeze (new SolidColorBrush(Color.FromRgb(r,g,b))))

    let log =  new TextEditor()
    let hiLi = new SelectedTextHighlighter(log)
    let color = new ColorizingTransformer(log, offsetColors)

    let searchPanel = Search.SearchPanel.Install(log, enableReplace = false)  // disable replace via search replace dialog

    let mutable isAlive = true

    do
        base.Content <- log  //nest Avalonedit inside a simple ContentControl to hide most of its functionality

        log.FontFamily <- FontFamily("Cascadia Code") // default font
        log.FontSize <- 14.0
        log.IsReadOnly <- true
        log.Document.UndoStack.SizeLimit <- 0 // read only, so don't keep a copy of every appended text for undo
        log.Encoding <- Text.Encoding.Default // = UTF-16
        log.ShowLineNumbers  <- true
        log.Options.EnableHyperlinks <- true
        log.TextArea.SelectionCornerRadius <- 0.0
        log.TextArea.SelectionBorder <- null
        log.TextArea.TextView.LinkTextForegroundBrush <- Brushes.Blue |> Brush.freeze //Hyper-links color

        log.TextArea.TextView.LineTransformers.Add(color) // to actually draw colored text
        log.TextArea.SelectionChanged.Add color.SelectionChangedDelegate // to exclude selected text from being colored

        // to highlight all instances of the selected word
        log.TextArea.TextView.LineTransformers.Add(hiLi)
        log.TextArea.SelectionChanged.Add hiLi.SelectionChangedDelegate
        log.TextChanged.Add hiLi.TextChangedDelegate // to update the count of occurrences
        log.KeyDown.Add (fun e -> if e.Key = Windows.Input.Key.Escape then hiLi.ClearHighlight()) // the highlighting stays till Escape is pressed. (If the search panel is open, Escape closes it first.)

        match log.TextArea.LeftMargins.[0]  with  // the line number margin
        | :? Editing.LineNumberMargin as lm -> lm.HighlightCurrentLineNumber <- false // disable highlighting of current line number
        | _ -> ()

        defaultBrush <- (log.Foreground.Clone() :?> SolidColorBrush |> Brush.freeze) // just to be sure they are the same
        //log.Foreground.Changed.Add ( fun _ -> LogColors.consoleOut <- (log.Foreground.Clone() :?> SolidColorBrush |> freeze)) // this event attaching can't  be done because it is already frozen

    let mutable prevMsgBrush = null //null is no color for console // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) ..
    let buffer =  new StringBuilder()
    let mutable docLength = 0  //to be able to have the doc length async
    let mutable maxCharsInLog = 1024_000 // about 10k lines with 100 chars each
    let mutable stillLessThanMaxChars = true

    let mutable printInterval : int64 = 50L //100L

    let mutable lastPrintDelay : int = 30 //70

    /// True from the first print into an empty buffer till printToLog() takes the buffer.
    /// Guarded by lock buffer.
    let mutable flushPending = false

    /// Stopwatch timestamp of when flushPending was set. Guarded by lock buffer.
    let mutable pendingSince = 0L

    /// Stopwatch timestamp of the last time printToLog() took text from the buffer. Guarded by lock buffer.
    let mutable lastFlush = Stopwatch.GetTimestamp()

    let msSince (timestamp:int64) = (Stopwatch.GetTimestamp() - timestamp) * 1000L / Stopwatch.Frequency

    //-----------------------------------------------------------------------------------
    // Print calls from any thread only append to the buffer.
    // The text gets added to the document on the UI thread in printToLog(), at most every printInterval.
    // This is to avoid the double UI update in printfn (it calls Write several times per line)
    // and the poor performance of log.ScrollToEnd().
    // https://github.com/dotnet/fsharp/issues/3712
    // https://github.com/icsharpcode/AvalonEdit/issues/226
    //-----------------------------------------------------------------------------------

    /// Returns the buffered text and its color changes and clears both, call within lock buffer.
    let takeBuffer () =
        flushPending <- false
        let txt = buffer.ToString()
        if txt.Length > 0 then
            lastFlush <- Stopwatch.GetTimestamp()
            buffer.Clear()  |> ignore<StringBuilder>
        let cols = if pendingColors.Count = 0 then Array.Empty() else pendingColors.ToArray()
        pendingColors.Clear()
        txt, cols

    /// Must be called on the UI thread.
    /// This is the only place where offsetColors grows.
    let printToLog() =
        let txt, cols = lock buffer takeBuffer //lock for safe access
        if txt.Length > 0 then //might be empty if a previous call already printed it
            // the color offsets are relative to the buffer, make them relative to the document:
            let docOff = log.Document.TextLength
            for c in cols do
                offsetColors.Add { off = docOff + c.off; brush = c.brush }
            log.AppendText(txt)
            log.ScrollToEnd()
            if log.WordWrap then log.ScrollToEnd() //this is needed a second time. see  https://github.com/dotnet/fsharp/issues/3712

    /// The one timer that triggers printToLog() on the UI thread.
    /// It only runs while a flush is pending. Started in scheduleFlush().
    let flushTimer =
        new Timer( TimerCallback(fun _ ->
            // This runs on a threadpool thread, an unhandled exception here would terminate the host process (e.g. Revit).
            try
                let disp = log.Dispatcher
                if isAlive && not disp.HasShutdownStarted then
                    disp.BeginInvoke(Action printToLog) |> ignore<Windows.Threading.DispatcherOperation>
            with _ ->
                () // e.g. the dispatcher started to shut down after the check above
            ), null, Timeout.Infinite, Timeout.Infinite)

    /// Makes sure that printToLog() will run soon, call within lock buffer.
    /// Returns true if the caller should call printToLog() right away (see below).
    let scheduleFlush () =
        if not flushPending then
            flushPending <- true
            pendingSince <- Stopwatch.GetTimestamp()
            // wait at least lastPrintDelay so that all the parts of a printfn call get printed together,
            // and at least printInterval since the last flush:
            let wait = max (int64 lastPrintDelay) (printInterval - msSince lastFlush)
            flushTimer.Change(max 0L wait, -1L) |> ignore<bool>
            false
        else
            // The flush posted to the UI thread can only run once the UI thread is idle.
            // So if the UI thread itself prints for a long time, print inline every printInterval:
            msSince pendingSince > printInterval && log.Dispatcher.CheckAccess()

    let newLine = Environment.NewLine

    // let debugFile =
    //     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AvalonLogDebug.txt")
    //     |>  fun p -> IO.File.AppendAllText(p, "AvalonLogDebug.txt created" + Environment.NewLine); p


    /// Adds the string to the buffer, optionally with a new line at end.
    /// Records the color change for the ColorizingTransformer if needed.
    /// The buffer gets added to the document on the UI thread in printToLog().
    /// Never blocks, may be called from any thread.
    let printOrBuffer (txt:string, addNewLine:bool, brush:SolidColorBrush) = // TODO check for escape sequence characters and don't print or count them, how many are skipped by ava-edit during Text.Append??
        // IO.File.AppendAllText(debugFile, txt + (if addNewLine then Environment.NewLine else "") + "£")
        let txt = if isNull txt then "" else txt
        if stillLessThanMaxChars && (txt.Length <> 0 || addNewLine) && isAlive then
            let flushNow =
              lock buffer (fun () ->  // or rwl.EnterWriteLock() //https://stackoverflow.com/questions/23661863/f-synchronized-access-to-list
                if not stillLessThanMaxChars then
                    false // check again within the lock, another thread might have just reached the maximum
                else
                    // check if total text in log would get too big. Needed when log gets piled up with exception messages form Avalonedit rendering pipeline.
                    let room = maxCharsInLog - docLength // might be negative if MaximumCharacterAllowance was lowered
                    let fullLength = txt.Length + (if addNewLine then newLine.Length else 0)
                    let fits = fullLength <= room
                    let printLength = if fits then txt.Length else max 0 (min txt.Length room) // print only the part of txt that fits

                    if fits || printLength > 0 then
                        // Change color if needed:
                        if not (Object.ReferenceEquals(prevMsgBrush, brush)) then
                            pendingColors.Add { off = buffer.Length; brush = brush }
                            prevMsgBrush <- brush
                        // add to buffer
                        buffer.Append(txt, 0, printLength)  |> ignore<StringBuilder>
                        if fits && addNewLine then
                            buffer.Append(newLine)  |> ignore<StringBuilder>

                    if fits then
                        docLength <- docLength + fullLength
                    else
                        // the maximum is reached, add the stop message, then ignore all prints till the log gets cleared:
                        stillLessThanMaxChars <- false
                        let itsOverTxt = sprintf "%s%s  **** STOP OF LOGGING **** Log has more than %d characters! Clear Log view first %s%s%s%s %s" newLine newLine maxCharsInLog  newLine newLine  newLine newLine newLine
                        let red = Brushes.Red |> freeze
                        pendingColors.Add { off = buffer.Length; brush = red }
                        prevMsgBrush <- red
                        buffer.Append(itsOverTxt)  |> ignore<StringBuilder>
                        docLength <- docLength + printLength + itsOverTxt.Length

                    scheduleFlush()
                )

            if flushNow then
                printToLog()



    //-----------------------------------------------------------
    //----------------------exposed AvalonEdit members:----------
    //-----------------------------------------------------------

    /// If not alive, all print calls are ignored and nothing more gets sent to the UI thread.
    /// Set it to false when the host shuts down. (Printing also stops by itself once the UI Dispatcher starts shutting down.)
    member _.IsAlive
        with get() = isAlive
        and set v  = isAlive <- v


    member  _.VerticalScrollBarVisibility   with get() = log.VerticalScrollBarVisibility     and set v = log.VerticalScrollBarVisibility <- v
    member  _.HorizontalScrollBarVisibility with get() = log.HorizontalScrollBarVisibility   and set v = log.HorizontalScrollBarVisibility <- v
    member  _.FontFamily       with get() = log.FontFamily                  and set v = log.FontFamily <- v
    member  _.FontSize         with get() = log.FontSize                    and set v = log.FontSize  <- v
    //member  _.Encoding         with get() = log.Encoding                    and set v = log.Encoding <- v
    member  _.ShowLineNumbers  with get() = log.ShowLineNumbers             and set v = log.ShowLineNumbers <- v
    member  _.EnableHyperlinks with get() = log.Options.EnableHyperlinks    and set v = log.Options.EnableHyperlinks  <- v

    /// The delay in milliseconds from a print call (after a pause in printing) until the text shows on screen.
    /// This is so that all the parts of a printfn call show up together. (printfn calls Write several times per line)
    /// Default is 30 ms.
    member _.LastPrintDelay
        with get() = lastPrintDelay
        and set v = lastPrintDelay <- v

    /// The minimum time in milliseconds between two updates of the text on screen.
    /// Any print calls arriving during this time will be buffered and printed in one go.
    /// Default is 50 ms, so the screen gets updated at most 20 times per second.
    member _.PrintInterval
        with get() = printInterval
        and set v = printInterval <- v

    /// Get all text in this AvalonLog
    member _.Text() = log.Text

    /// Get all text in Segment AvalonLog
    member _.Text(seg:ISegment) = log.Document.GetText(seg)

    /// Get the current Selection
    member _.Selection = log.TextArea.Selection

    /// The SearchPanel from AvalonEditB
    member _.SearchPanel = searchPanel

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

    /// The maximum amount of characters this AvalonLog can display.
    /// By default this about one Million characters
    /// This is to avoid freezing the UI when the AvalonLog is flooded with text.
    /// When the maximum is reached a message will be printed at the end, then the printing stops until the content is cleared.
    /// A print that would go beyond the maximum gets cut off at the maximum.
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
        // All on the UI thread, where offsetColors is used and printToLog() runs.
        // Invoke and not BeginInvoke, so that a print after this call does not get cleared too.
        log.Dispatcher.Invoke( fun () ->
            lock buffer (fun () ->
                buffer.Clear() |>  ignore<StringBuilder>
                pendingColors.Clear()
                docLength <- 0
                prevMsgBrush <- null
                stillLessThanMaxChars <- true
                )
            // Prints arriving from now on are buffered with their color offsets relative to the buffer.
            // printToLog() can only add them to offsetColors after this function, so they are not affected by the clearing below.
            offsetColors.Clear()
            offsetColors.Add {off = -1 ; brush=null}  // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) ..
            log.Clear()
            defaultBrush <- (log.Foreground.Clone() :?> SolidColorBrush |> Brush.freeze)   // TODO or remember custom brush ?
            )


    /// Returns a thread-safe TextWriter that prints to AvalonLog in Color
    /// for use as use System.Console.SetOut(textWriter)
    /// or System.Console.SetError(textWriter)
    member _.GetTextWriter(red, green, blue) =
        let br = Brush.ofRGB red green blue
        new LogTextWriter   (fun s -> printOrBuffer (s, false, br)
                            ,fun s -> printOrBuffer (s, true , br)
                            )

    /// Returns a thread-safe TextWriter that prints to AvalonLog in Color
    /// for use as use System.Console.SetOut(textWriter)
    /// or System.Console.SetError(textWriter)
    member _.GetTextWriter(br:SolidColorBrush) =
        let fbr = br|> freeze
        new LogTextWriter   (fun s -> printOrBuffer (s, false, fbr)
                            ,fun s -> printOrBuffer (s, true , fbr)
                            )

    /// Returns a thread-safe TextWriter that only prints to AvalonLog
    /// if the predicate returns true for the string sent to the text writer.
    /// The provide Color will be used.
    member _.GetConditionalTextWriter(predicate:string->bool, br:SolidColorBrush) =
        let fbr = br|> freeze
        new LogTextWriter   (fun s -> if predicate s then printOrBuffer (s, false, fbr)
                            ,fun s -> if predicate s then printOrBuffer (s, true , fbr)
                            )


    /// Returns a thread-safe TextWriter that only prints to AvalonLog
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
    //--------- Append string: -------------
    //--------------------------------------

    /// Print string using default color (Black)
    member _.Append (s) =
        printOrBuffer (s, false, defaultBrush )

    /// Print string using red, green and blue color values (each between 0 and 255).
    /// (without adding a new line at the end).
    member _.AppendWithColor (red, green, blue, s) =
        let br = getBrush (red,green,blue)
        customBrush <- br
        printOrBuffer (s, false, br)

    /// Print string using the Brush provided.
    /// (without adding a new line at the end).
    member _.AppendWithBrush (br:SolidColorBrush, s) =
        customBrush <- br
        printOrBuffer (s, false, br)

    /// Print string using the last Brush or color provided.
    /// (without adding a new line at the end
    member _.AppendWithLastColor (s) =
        printOrBuffer (s, false, customBrush)

    //--------------------------------------
    //--------- AppendLine string:----------
    //--------------------------------------

    /// Print string using default color (Black)
    /// Adds a new line at the end
    member _.AppendLine (s) =
        printOrBuffer (s, true, defaultBrush )

    /// Print string using red, green and blue color values (each between 0 and 255).
    /// Adds a new line at the end
    member _.AppendLineWithColor (red, green, blue, s) =
        let br = getBrush (red,green,blue)
        customBrush <- br
        printOrBuffer (s, true, br)

    /// Print string using the Brush provided.
    /// Adds a new line at the end.
    member _.AppendLineWithBrush (br:SolidColorBrush, s) =
        customBrush <- br
        printOrBuffer (s, true, br)

    /// Print string using the last Brush or color provided.
    /// Adds a new line at the end
    member _.AppendLineWithLastColor (s) =
        printOrBuffer (s, true, customBrush)

   //--------------------------------------
   //--- with F# string formatting:--------
   //--------------------------------------


    /// F# printf formatting using the Brush provided.
    /// (without adding a new line at the end).
    member _.printfBrush (br:SolidColorBrush) s =
        customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s, false, br))  s

    /// F# printfn formatting using the Brush provided.
    /// Adds a new line at the end.
    member _.printfnBrush (br:SolidColorBrush) s =
        customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s, true, br))  s

    /// F# printf formatting using red, green and blue color values (each between 0 and 255).
    /// (without adding a new line at the end)
    member _.printfColor red green blue msg =
        let br = getBrush (red,green,blue)
        customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s,false, br))  msg

    /// F# printfn formatting using red, green and blue color values (each between 0 and 255).
    /// Adds a new line at the end
    member _.printfnColor red green blue msg =
        let br = getBrush (red,green,blue)
        customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s,true, br))  msg

    /// F# printf formatting using the last Brush or color provided.
    /// (without adding a new line at the end
    member _.printfLastColor msg =
        let br = customBrush
        Printf.kprintf (fun s -> printOrBuffer (s, false, br))  msg

    /// F# printfn formatting using the last Brush or color provided.
    /// Adds a new line at the end
    member _.printfnLastColor msg =
        let br = customBrush
        Printf.kprintf (fun s -> printOrBuffer (s, true, br))  msg



// module ILoggingColors =
//     let mutable trace = Brushes.Gray |> freeze
//     let mutable debug  = Brushes.Teal |> freeze

//     let mutable information = Brushes.Blue |> freeze

//     let mutable warning = Brushes.Orange |> freeze

//     let mutable error = Brushes.Red |> freeze

//     let mutable critical = Brushes.DarkRed |> freeze



// let iLogger = { // ILogger interface as F# object expression

//     new  ILogger with

//         member this.Log(logLevel, _eventId, state, except, formatter) =
//             let message = formatter.Invoke(state, except)
//             match logLevel with
//             | LogLevel.None         -> ()
//             | LogLevel.Trace        -> print (ILoggingColors.trace, message)
//             | LogLevel.Debug        -> print (ILoggingColors.debug, message)
//             | LogLevel.Information  -> print (ILoggingColors.information, message)
//             | LogLevel.Warning      -> print (ILoggingColors.warning, message)
//             | LogLevel.Error        -> print (ILoggingColors.error, message)
//             | LogLevel.Critical     -> print (ILoggingColors.critical, message)
//             | _                     -> print (Brushes.Black, message)

//         member _.IsEnabled(_logLevel) =
//             isAlive

//         member _.BeginScope(_state) =
//             { new IDisposable with
//                 member _.Dispose() = () }
//     }

    // The ILogger interface for logging:
    // Trace -> Gray
    // Debug -> Teal
    // Information -> Blue
    // Warning -> Orange
    // Error -> Red
    // Critical -> DarkRed
    // None -> no logging
    // member _.ILogger = iLogger
