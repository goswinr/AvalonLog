namespace AvalonLog

open System
open System.Windows.Media // for color brushes
open AvalonEditB
open AvalonLog.Util
 
/// Highlight-all-occurrences-of-selected-text in Log Text View
/// if the selection is more then two non-whitespace characters.
type SelectedTextHighlighter (lg:TextEditor) = 
    inherit Rendering.DocumentColorizingTransformer()    
    //  based on https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit  
    
    let mutable highTxt = null
    let mutable curSelStart = -1

    // Events for a status bar or other UI
    let highlightClearedEv  = new Event<unit>()
    let highlightChangedEv  = new Event<string*int>()
    
    /// Occures when the selection clears or or is less than two non-whitespace Characters
    [<CLIEvent>]
    member _.OnHighlightCleared = highlightClearedEv.Publish
 
    /// Occures when the selection changes to more than two non-whitespace Characters
    [<CLIEvent>]
    member _.OnHighlightChanged = highlightChangedEv.Publish   

    /// The color used for highlighting other occurances
    member val ColorHighlight = Brushes.Blue |> Brush.brighter 210  |> Brush.freeze  with get,set
    
    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) = 
        if notNull highTxt  then
            let  lineStartOffset = line.Offset;
            let  text = lg.Document.GetText(line)            
            let mutable  index = text.IndexOf(highTxt, 0, StringComparison.Ordinal)    
            while index >= 0 do      
                let st = lineStartOffset + index  // startOffset
                let en = lineStartOffset + index + highTxt.Length // endOffset   
    
                if curSelStart <> st  then // skip the actual current selection
                    base.ChangeLinePart( st,en, fun el -> el.TextRunProperties.SetBackgroundBrush(this.ColorHighlight))
                let start = index + highTxt.Length // search for next occurrence // TODO or just +1 ???????
                index <- text.IndexOf(highTxt, start, StringComparison.Ordinal)        
 
        
    member _.SelectionChangedDelegate (a:EventArgs) =        
        let selTxt = lg.SelectedText    // for block selection this will contain everything from first segment till last segment, even the unselected. 
        let doHighlightNow = 
            selTxt.Trim().Length >= 2 // minimum 2 non whitespace characters?
            && not <| selTxt.Contains("\n")  //no line beaks          
            && not <| selTxt.Contains("\r")  //no line beaks
            
        if doHighlightNow then 

            // for current text view:
            highTxt <- selTxt
            curSelStart <- lg.SelectionStart
            lg.TextArea.TextView.Redraw()
        
            // for events to complete count in full document : 
            let doc = lg.Document // get doc in sync first !
            async{
                let tx = doc.CreateSnapshot().Text
                let mutable  index = tx.IndexOf(selTxt, 0, StringComparison.Ordinal)                
                let mutable k = 0                
                while index >= 0 do        
                    k <- k+1                                 
                    let st =  index + selTxt.Length // endOffset // TODO or just +1 ???????
                    if st >= tx.Length then 
                        index <- -99                          
                    else
                        index <- tx.IndexOf(selTxt, st, StringComparison.Ordinal)
                                   
                do! Async.SwitchToContext SyncAvalonLog.context
                highlightChangedEv.Trigger(selTxt, k)    // to update status bar or similar UI
                }   |> Async.Start
    
        else
            if notNull highTxt then // to ony redraw if it was not null before
                highTxt <- null
                highlightClearedEv.Trigger()
                lg.TextArea.TextView.Redraw() // to clear highlight
 