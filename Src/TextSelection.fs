namespace AvalonLog

open AvalonLog.Util
open System
open ICSharpCode
open System.Windows.Media // for color brushes

 
/// Highlight-all-occurrences-of-selected-text in Log Text View
type SelectedTextHighlighter (lg:AvalonEdit.TextEditor) = 
    inherit AvalonEdit.Rendering.DocumentColorizingTransformer()    
    
    let colorHighlight =      Brushes.Blue |> Brush.brighter 210  |> Brush.freeze
    
    let mutable highTxt = null
    let mutable curSelStart = -1

    // events for status bar
    let highlightClearedEv  = new Event<unit>()
    let highlightChangedEv  = new Event<string*int>()
    [<CLIEvent>]
    member this.OnHighlightCleared = highlightClearedEv.Publish
    [<CLIEvent>]
    member this.OnHighlightChanged = highlightChangedEv.Publish
   

    member this.ColorHighlight = colorHighlight
    
    //member this.HighlightText  with get() = highTxt and set v = highTxt <- v
    //member this.CurrentSelectionStart  with get() = curSelStart and set v = curSelStart <- v
    
    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:AvalonEdit.Document.DocumentLine) =       
        //  from https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit
        //try    
            if notNull highTxt  then
                let  lineStartOffset = line.Offset;
                let  text = lg.Document.GetText(line)            
                let mutable  index = text.IndexOf(highTxt, 0, StringComparison.Ordinal)
    
                while index >= 0 do      
                    let st = lineStartOffset + index  // startOffset
                    let en = lineStartOffset + index + highTxt.Length // endOffset   
    
                    if curSelStart <> st  then // skip the actual current selection
                        base.ChangeLinePart( st,en, fun el -> el.TextRunProperties.SetBackgroundBrush(colorHighlight))
                    let start = index + highTxt.Length // search for next occurrence // TODO or just +1 ???????
                    index <- text.IndexOf(highTxt, start, StringComparison.Ordinal)
        
        //with e -> LogFile.Post <| sprintf "LogSelectedTextHighlighter override this.ColorizeLine failed with %A" e
        
    member this.SelectionChangedDelegate ( a:EventArgs) =
        // for text view:
        let selTxt = lg.SelectedText    // for block selection this will contain everything from first segment till last segment, even the unselected.       
        let checkTx = selTxt.Trim()
        let doHighlight = 
            checkTx.Length > 1 // minimum 2 non whitecpace characters?
            && not <| selTxt.Contains("\n")  //no line beaks          
            && not <| selTxt.Contains("\r")  //no line beaks
            //&& config.Settings.SelectAllOccurences
            
        if doHighlight then 
            highTxt <- selTxt
            curSelStart <- lg.SelectionStart
            lg.TextArea.TextView.Redraw()
        
            // for status bar : 
            let doc = lg.Document // get in sync first !
            async{
                let tx = doc.CreateSnapshot().Text
                let mutable  index = tx.IndexOf(selTxt, 0, StringComparison.Ordinal)                
                let mutable k = 0
                //let mutable anyInFolding = false
                while index >= 0 do        
                    k <- k+1 
                                
                    let st =  index + selTxt.Length // endOffset // TODO or just +1 ???????
                    if st >= tx.Length then 
                        index <- -99
                        //eprintfn "index  %d in %d ??" st tx.Length    
                    else
                        index <- tx.IndexOf(selTxt, st, StringComparison.Ordinal)
                                   
                do! Async.SwitchToContext Sync.syncContext
                highlightChangedEv.Trigger(selTxt, k  )    // will update status bar 
                }   |> Async.Start
    
        else
            if notNull highTxt then // to ony redraw if it was not null before
                highTxt <- null
                highlightClearedEv.Trigger()
                lg.TextArea.TextView.Redraw() // to clear highlight
 