namespace AvalonLog

open AvalonLog.Util
open System
open System.IO
open System.Threading
open ICSharpCode
open System.Windows.Media // for color brushes
open System.Text
open System.Diagnostics
open System.Windows.Controls



/// A TextWriter that writes using a function (to an Avalonedit Control). used in FSI session constructor   
type LogTextWriter(writeStr) =
    inherit TextWriter()
    override this.Encoding =  Text.Encoding.Default
    override this.Write     (s:string)  = writeStr (s)
    override this.WriteLine (s:string)  = writeStr (s + Environment.NewLine)    // actually never used in F# see  https://github.com/dotnet/fsharp/issues/3712   
    override this.WriteLine ()          = writeStr (    Environment.NewLine)    


/// A ReadOnly text AvalonEdit Editor that provides colored appending
/// call ApplyConfig() once config is set up too, (config depends on this Log instance)
type AvalonLog () =    
    do 
        Sync.installSynchronizationContext ()    
    
    let offsetColors = ResizeArray<NewColor>( [ {off = -1 ; brush=null} ] )    // null is console out // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) .. 
    
    let log =  new AvalonEdit.TextEditor()    
    let hiLi = new SelectedTextHighlighter(log)
    let colo = new ColorizingTransformer(log,offsetColors)    
    
    
    do    
       
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

        AvalonEdit.Search.SearchPanel.Install(log) |> ignoreObj //TODO disable search and replace if using custom build?
        
        Global.defaultBrush <- (log.Foreground.Clone() :?> SolidColorBrush |> Brush.freeze) // just to be sure they are the same
        //log.Foreground.Changed.Add ( fun _ -> LogColors.consoleOut <- (log.Foreground.Clone() :?> SolidColorBrush |> freeze)) // this event attaching can't  be done because it is already frozen

    let printCallsCounter = ref 0L
    let mutable prevMsgBrush = null //null is no color for console // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) .. 
    let stopWatch = Stopwatch.StartNew()
    let buffer =  new StringBuilder()
    let mutable docLength = 0  //to be able to have the doc length async
    let maxCharsInLog = 512_000 // about 5k lines with 100 chars each
    let mutable stillLessThanMaxChars = true

    let getBufferText () =
        let txt = buffer.ToString()
        buffer.Clear()  |> ignoreObj 
        txt

    // The below functions are trying to work around double UI update in printfn for better UI performance, 
    // and the poor performance of log.ScrollToEnd().
    // see  https://github.com/dotnet/fsharp/issues/3712  
    let printToLog() =          
        let txt = lock buffer getBufferText //lock for safe access    // or rwl.EnterWriteLock() //https://stackoverflow.com/questions/23661863/f-synchronized-access-to-list
        log.AppendText(txt)       
        log.ScrollToEnd()
        if log.WordWrap then log.ScrollToEnd() //this is needed a second time. see  https://github.com/dotnet/fsharp/issues/3712  
        stopWatch.Restart()

    let newLine = Environment.NewLine

    /// adds string on UI thread  every 150ms then scrolls to end after 300ms.
    /// otipnally adds new line at end
    /// sets line color on LineColors dictionay for DocumentColorizingTransformer
    /// printOrBuffer (txt:string, addNewLine:bool, typ:SolidColorBrush)
    let printOrBuffer (txt:string, addNewLine:bool, brush:SolidColorBrush) =
        if stillLessThanMaxChars && txt.Length <> 0 then
            // Change color if needed:
            if prevMsgBrush <> brush then 
                lock buffer (fun () -> 
                    offsetColors.Add { off = docLength; brush = brush } // TODO filter out ANSI escape chars first or just keep them in the doc but not in the visual line ??
                    prevMsgBrush <- brush )
               
            
            // add to buffer locked:
            if addNewLine then 
                lock buffer (fun () -> 
                    buffer.AppendLine(txt)  |> ignoreObj
                    docLength <- docLength + txt.Length + newLine.Length) 
            else                
                lock buffer (fun () -> 
                    buffer.Append(txt)  |> ignoreObj
                    docLength <- docLength + txt.Length   ) 
            
            // check if buffer is already to big , print it and then stop printing
            if docLength > maxCharsInLog then // neded when log gets piled up with exception messages form Avalonedit rendering pipeline.
                stillLessThanMaxChars <- false
                async {
                    do! Async.SwitchToContext Sync.syncContext
                    printToLog()
                    log.AppendText(sprintf "%s%s  *** STOP OF LOGGING *** Log has more than %d characters! clear Log view first" newLine newLine maxCharsInLog)
                    log.ScrollToEnd()
                    log.ScrollToEnd()
                    } |> Async.StartImmediate
            else
                // check the two criteria for actually printing
                // print case 1: sine the last printing more than 100ms have elapsed
                // print case 2, wait 0.1 seconds and print if nothing els has been added to the buffer during the last 100 ms
                if stopWatch.ElapsedMilliseconds > 100L  then // print case 1: only add to document every 100ms  
                    async {
                        do! Async.SwitchToContext Sync.syncContext
                        printToLog()
                        } |> Async.StartImmediate                 
                else
                    async {                        
                        let k = Interlocked.Increment printCallsCounter
                        do! Async.Sleep 100
                        if !printCallsCounter = k  then //print case 2, it is the last call for 100 ms
                            do! Async.SwitchToContext Sync.syncContext
                            printToLog()                
                        } |> Async.StartImmediate 
               
    

        
    //-----------------------------------------------------------    
    //----------------------members:------------------------------------------    
    //------------------------------------------------------------    
    
    /// use true to enable Line wrap.
    /// will enable HorizontalScrollBarVisibility if setting false
    member this.setLineWrap(v)=
        if v then 
            log.WordWrap         <- true 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Disabled 
        else
            log.WordWrap         <- false 
            log.HorizontalScrollBarVisibility <- ScrollBarVisibility.Auto 
    
    /// to access the underlying  Avalonedit Texteditor
    member this.AvalonEdit = log

    member this.SelectedTextHighLighter = hiLi
    
    /// thread safe
    member this.Clear() = 
        Sync.doSync (fun () -> 
            log.SelectionLength <- 0
            log.SelectionStart <- 0        
            log.Clear()
            docLength <- 0
            prevMsgBrush <- null
            stillLessThanMaxChars <- true
            offsetColors.Clear()
            Global.defaultBrush <- (log.Foreground.Clone() :?> SolidColorBrush |> Brush.freeze)   // TODO or remeber custstom brush ?
            offsetColors.Add {off = -1 ; brush=null} //TODO use -1 instead? // null check done in  this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) .. 
            
            //log.TextArea.TextView.linesCollapsedVisualPosOffThrowCount <- 0 // custom property in Avalonedit to avoid throwing too many exceptions. set 0 so exceptions appear again // TODO Use custom build from AvalonLog
            ///GlobalErrorHandeling.throwCount <- 0 // set 0 so exceptions appear again
            )
    

    /// Returns a Textwriter that prints to AvalonLog in Color  
    /// for use as use System.Console.SetOut(textWriter) 
    /// or System.Console.SetError(textWriter)
    member this.GetTextWriter(red,green,blue) =
        let br = Brush.make(red,green,blue)
        new LogTextWriter(fun s -> printOrBuffer (s, false, br))

        
    // for use with a string:

    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print (without adding a new line at the end)
    member this.PrintColor red green blue s = 
            Global.setCustom (red,green,blue)
            printOrBuffer (s, false, Global.customBrush )    
       
    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Adds a new line at the end
    member this.PrintnColor red green blue s =
            Global.setCustom (red,green,blue)
            printOrBuffer (s, true, Global.customBrush ) 

    /// Provide a frozen Brush 
    /// Then print (without adding a new line at the end)
    member this.PrintBrush (br:SolidColorBrush) s = 
            Global.customBrush <- br
            printOrBuffer (s, false, Global.customBrush )    
       
    /// Provide a frozen Brush 
    /// Adds a new line at the end
    member this.PrintnBrush (br:SolidColorBrush) s =
            Global.customBrush <- br
            printOrBuffer (s, true, Global.customBrush) 
            
   
   // with F# string formating:


    /// Print using the Brush provided 
    /// (without adding a new line at the end)
    member this.PrintfBrush (br:SolidColorBrush) s = 
        Global.customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s, false, Global.customBrush))  s    

    /// Print using the Brush provided
    /// Adds a new line at the end
    member this.PrintfnBrush (br:SolidColorBrush) s = 
        Global.customBrush <- br
        Printf.kprintf (fun s -> printOrBuffer (s, true, Global.customBrush))  s       
    

    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print (without adding a new line at the end)
    member this.PrintfColor red green blue msg =
        Global.setCustom (red,green,blue)
        Printf.kprintf (fun s -> printOrBuffer (s,false, Global.customBrush ))  msg
    
    /// Change custom color to a RGB value ( each between 0 and 255) 
    /// Then print. Adds a new line at the end
    member this.PrintfnColor red green blue msg =          
        Global.setCustom (red,green,blue)
        Printf.kprintf (fun s -> printOrBuffer (s,true, Global.customBrush ))  msg       

    /// Print using the last Brush or color provided
    /// (without adding a new line at the end
    member this.PrintfLastColor msg =     
        Printf.kprintf (fun s -> printOrBuffer (s, false, Global.customBrush))  msg
   
    /// Print using the last Brush or color provided 
    /// Adds a new line at the end
    member this.PrintfnLastColor msg =     
        Printf.kprintf (fun s -> printOrBuffer (s, true, Global.customBrush))  msg