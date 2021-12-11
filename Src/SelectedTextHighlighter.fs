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

    let mutable isEnabled = true

    let mutable highTxt = null // will be set if there is a text to highlight

    let mutable curSelStart = -1
    let mutable curSelEnd = -1 // end  is the last character with highlighting

    let mutable colorHighlight = Brushes.Blue |> Brush.brighter 210  |> Brush.freeze

    // Events for a status bar or other UI
    let highlightClearedEv  = new Event<unit>()
    let highlightChangedEv  = new Event<string*ResizeArray<int>>()

    let selectionChanged () = 
        if isEnabled then
            let selTxt = 
                let sel = lg.TextArea.Selection
                if sel.Length < 2 then ""                                      //only highlight if 2 or more characters selected
                elif sel.StartPosition.Line <> sel.EndPosition.Line then ""    //only highlight if one-line-selection
                else
                    let selt = lg.SelectedText //sel.GetText() // for block selection this will contain everything from first segment till last segment, even the unselected.
                    if  selt.Trim().Length < 2 then ""// minimum 2 non whitespace characters?
                    else selt

            if selTxt <>"" then
                // for current text view:
                highTxt <- selTxt
                curSelStart <- lg.SelectionStart
                curSelEnd <- curSelStart + selTxt.Length - 1
                lg.TextArea.TextView.Redraw() // this triggers ColorizeLine on every visible line.

                // for events to complete count in full document :
                let doc = lg.Document // get doc in sync first !
                async{
                    // get and search text in background thread to get total count too:
                    // then raise event in sync with this info:
                    let tx = doc.CreateSnapshot().Text
                    let locations = ResizeArray()
                    let mutable  index = tx.IndexOf(selTxt, 0, StringComparison.Ordinal)
                    while index >= 0 do
                        locations.Add(index)
                        let st =  index + selTxt.Length
                        if st >= tx.Length then
                            index <- -99
                        else
                            index <- tx.IndexOf(selTxt, st, StringComparison.Ordinal)

                    do! Async.SwitchToContext SyncAvalonLog.context
                    highlightChangedEv.Trigger(selTxt, locations)    // to update status bar or similar UI
                    }   |> Async.Start

            else
                if notNull highTxt then // to only redraw if it was not null before but should be null now because selTxt=""
                    highTxt <- null
                    lg.TextArea.TextView.Redraw() // to clear highlight
                    highlightClearedEv.Trigger()


    /// Occurs when the selection clears or is less than two non-whitespace Characters.
    [<CLIEvent>]
    member _.OnHighlightCleared = highlightClearedEv.Publish

    /// Occurs when the selection changes to more than two non-whitespace Characters.
    /// Returns tuple of selected text and list of all start offsets in full text. (including invisible ones)
    [<CLIEvent>]
    member _.OnHighlightChanged = highlightChangedEv.Publish

    /// The color used for highlighting other occurrences of the selected text.
    member _.ColorHighlighting
        with get () = colorHighlight
        and  set v  = colorHighlight <- Brush.freeze v

    /// To enable or disable this highlighter, on by default.
    member _.IsEnabled
        with get () = isEnabled
        and  set on  = 
            isEnabled <- on
            if on then selectionChanged()
            elif notNull highTxt then // now off, clear highlight if any
                lg.TextArea.TextView.Redraw() // to clear highlight
                highlightClearedEv.Trigger() //there was a highlight before thats off now


    /// The main override for DocumentColorizingTransformer.
    /// This gets called for every visible line on any view change.
    override _.ColorizeLine(line:Document.DocumentLine) = 
        if isEnabled && notNull highTxt  then
            let  lineStartOffset = line.Offset;
            let  text = lg.Document.GetText(line)
            let mutable  index = text.IndexOf(highTxt, 0, StringComparison.Ordinal)
            while index >= 0 do
                let st = lineStartOffset + index  // startOffset
                let en = lineStartOffset + index + highTxt.Length - 1 // end offset is the last character with highlighting
                if (st < curSelStart || st > curSelEnd) && (en < curSelStart || en > curSelEnd )  then // skip the actual current selection
                    // here end offset needs + 1  to be the first character without highlighting
                    base.ChangeLinePart( st, en + 1, fun el -> el.TextRunProperties.SetBackgroundBrush(colorHighlight))
                let start = index + highTxt.Length // search for next occurrence // TODO or just +1 ???????
                index <- text.IndexOf(highTxt, start, StringComparison.Ordinal)


    member _.SelectionChangedDelegate (a:EventArgs) = 
        selectionChanged()

