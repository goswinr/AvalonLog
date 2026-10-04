namespace AvalonLog

open System
open System.Windows.Media // for color brushes
open AvalonEditB
open AvalonLog.Util

/// Highlight-all-occurrences-of-selected-text in Log Text View
/// if the selection is more then two non-whitespace characters.
/// The highlighting stays when the selection is removed, till ClearHighlight() is called. (AvalonLog calls it when Escape is pressed.)
/// Selected text never gets this highlighting, because the selection has its own highlighting.
type SelectedTextHighlighter (lg:TextEditor) =
    inherit Rendering.DocumentColorizingTransformer()
    //  based on https://stackoverflow.com/questions/9223674/highlight-all-occurrences-of-selected-word-in-avalonedit

    let mutable isEnabled = true

    let mutable highTxt = null // will be set if there is a text to highlight

    let mutable colorHighlight = Brushes.Blue |> Brush.brighter 210  |> Brush.freeze

    let setBackground = Action<Rendering.VisualLineElement>(fun el -> el.TextRunProperties.SetBackgroundBrush(colorHighlight))

    /// Incremented for each new count search and on clearing,
    /// so that the result of an outdated search does not get raised. Only changed on the UI thread.
    let mutable searchId = 0

    // Events for a status bar or other UI
    let highlightClearedEv  = new Event<unit>()
    let highlightChangedEv  = new Event<string*ResizeArray<int>>()

    /// Counts the occurrences in the full document on a background thread, then raises highlightChangedEv in sync.
    /// Must be called from the UI thread.
    let countInBackground (txt:string) =
        searchId <- searchId + 1
        let thisId = searchId
        let doc = lg.Document // get doc in sync first !
        async{
            do! Async.Sleep 100 // in case the selection or the text changes again quickly, e.g. while dragging the selection
            if thisId = searchId then
                let tx = doc.CreateSnapshot().Text
                let locations = ResizeArray()
                let mutable  index = tx.IndexOf(txt, 0, StringComparison.Ordinal)
                while index >= 0 do
                    locations.Add(index)
                    let st =  index + txt.Length
                    if st >= tx.Length then
                        index <- -99
                    else
                        index <- tx.IndexOf(txt, st, StringComparison.Ordinal)

                do! Async.SwitchToContext SyncAvalonLog.context
                if thisId = searchId then // there was no newer search or clearing in the meantime
                    highlightChangedEv.Trigger(txt, locations)    // to update status bar or similar UI
            }   |> Async.Start

    let clearHighlight () =
        searchId <- searchId + 1 // so that the result of a search that is still running does not get raised
        if notNull highTxt then
            highTxt <- null
            lg.TextArea.TextView.Redraw() // to clear highlight
            highlightClearedEv.Trigger()

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

            if selTxt <> "" && selTxt <> highTxt then
                // for current text view:
                highTxt <- selTxt
                lg.TextArea.TextView.Redraw() // this triggers ColorizeLine on every visible line.

                // for events to complete count in full document :
                countInBackground selTxt

            // Else keep the highlighting till ClearHighlight() is called.
            // AvalonEdit itself redraws the lines where the selection changed, and ColorizeLine skips the selected text.


    /// Occurs when the highlighting gets cleared via ClearHighlight() or by disabling this highlighter.
    [<CLIEvent>]
    member _.OnHighlightCleared = highlightClearedEv.Publish

    /// Occurs when the selection changes to more than two non-whitespace Characters.
    /// And when the text changes while there is a highlighting, to update the count.
    /// Returns tuple of selected text and list of all start offsets in full text. (including invisible ones)
    [<CLIEvent>]
    member _.OnHighlightChanged = highlightChangedEv.Publish

    /// The color used for highlighting other occurrences of the selected text.
    member _.ColorHighlighting
        with get () = colorHighlight
        and  set v  =
            colorHighlight <- Brush.freeze v
            if notNull highTxt then lg.TextArea.TextView.Redraw() // to show the new color

    /// To enable or disable this highlighter, it is enabled by default.
    member _.IsEnabled
        with get () = isEnabled
        and  set on  =
            if on then
                isEnabled <- true
                selectionChanged()
            else
                clearHighlight() // raises OnHighlightCleared only if there was a highlight
                isEnabled <- false

    /// Removes the highlighting of all occurrences of the previously selected text.
    /// AvalonLog calls this when Escape is pressed.
    member _.ClearHighlight() = clearHighlight()


    /// The main override for DocumentColorizingTransformer.
    /// This gets called for every visible line on any view change.
    override _.ColorizeLine(line:Document.DocumentLine) =
        if isEnabled && notNull highTxt  then
            let  lineStartOffset = line.Offset;
            let  text = lg.Document.GetText(line)

            // The selected parts of this line, they don't get highlighted. Computed on first use:
            let mutable selChecked = false
            let mutable selOnLine : ResizeArray<Editing.SelectionSegment> = null // stays null if nothing is selected on this line

            let mutable  index = text.IndexOf(highTxt, 0, StringComparison.Ordinal)
            while index >= 0 do
                let st = lineStartOffset + index  // startOffset
                let en = st + highTxt.Length // end offset is the first character without highlighting

                if not selChecked then
                    selChecked <- true
                    let sel = lg.TextArea.Selection
                    if not sel.IsEmpty then
                        for seg in sel.Segments do // more than one for rectangular selection
                            if seg.EndOffset > line.Offset && seg.StartOffset < line.EndOffset then
                                if isNull selOnLine then selOnLine <- ResizeArray()
                                selOnLine.Add seg

                if isNull selOnLine then
                    base.ChangeLinePart(st, en, setBackground)
                else
                    // only highlight the parts that are not selected:
                    let mutable from = st
                    for seg in selOnLine do
                        if seg.EndOffset > from && seg.StartOffset < en then
                            if seg.StartOffset > from then base.ChangeLinePart(from, seg.StartOffset, setBackground)
                            from <- max from seg.EndOffset
                    if from < en then base.ChangeLinePart(from, en, setBackground)

                let start = index + highTxt.Length // search for next occurrence // TODO or just +1 ???????
                index <- text.IndexOf(highTxt, start, StringComparison.Ordinal)


    member _.SelectionChangedDelegate (_:EventArgs) =
        selectionChanged()

    /// To update the count of occurrences via OnHighlightChanged when the text changes.
    member _.TextChangedDelegate (_:EventArgs) =
        if isEnabled && notNull highTxt then
            countInBackground highTxt

