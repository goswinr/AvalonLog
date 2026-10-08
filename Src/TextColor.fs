namespace AvalonLog

open AvalonLog.Util
open System
open AvalonEditB
open System.Windows.Media // for color brushes


/// Describes the position in text where a new color starts
[<Struct>]
[<NoComparison>]
type internal NewColor =
    {
    off   : int
    brush : SolidColorBrush // brush must be frozen to be used async, null means the default foreground of the editor
    }

    /// Does binary search to find the index of the last item with an offset equal or smaller than currOff.
    /// Returns 0 if there is no such item.
    static member findIndex (cs:ResizeArray<NewColor>) currOff :int =
        //  cs has a least one item that is {off = -1 ; brush=null}, set in AvalonLog constructor
        let mutable lo = 0
        let mutable hi = cs.Count - 1
        let mutable found = 0
        while lo <= hi do
            let mid = lo + (hi - lo) / 2
            if cs.[mid].off <= currOff then
                found <- mid
                lo <- mid + 1
            else
                hi <- mid - 1
        found


/// To implement the actual colors from colored printing
type internal ColorizingTransformer(ed:TextEditor, offsetColors: ResizeArray<NewColor>) =
    inherit Rendering.DocumentColorizingTransformer()

    let mutable selStart = -9
    let mutable selEnd   = -9

    member _.SelectionChangedDelegate (_:EventArgs) =
        if ed.SelectionLength = 0 then // no selection
            selStart <- -9
            selEnd   <- -9
        else
            selStart <- ed.SelectionStart
            selEnd   <- selStart + ed.SelectionLength // this is the last selection in case of block selection too ! correct


    /// This gets called for every visible line on any view change
    override this.ColorizeLine(line:Document.DocumentLine) =
        if not line.IsDeleted then
            let stLn = line.Offset
            let enLn = line.EndOffset

            // The selected parts of this line, they don't get colored, so that the selection foreground shows:
            let selOnLine =
                if selStart = selEnd  || selStart > enLn || selEnd < stLn then null // no selection in general or on this line
                else
                    let sel = ResizeArray<Editing.SelectionSegment>()
                    for seg in ed.TextArea.Selection.Segments do // more than one for rectangular selection
                        if seg.EndOffset > stLn && seg.StartOffset < enLn then
                            sel.Add seg
                    if sel.Count = 0 then null else sel

            // walk forward from the color that is active at the line start, each color run gets colored once:
            let mutable i = NewColor.findIndex offsetColors stLn
            while i < offsetColors.Count && offsetColors.[i].off < enLn do
                let br = offsetColors.[i].brush
                if notNull br then // null is the default foreground, nothing to do
                    let st = max stLn offsetColors.[i].off
                    let en = if i + 1 < offsetColors.Count then min enLn offsetColors.[i+1].off else enLn
                    if st < en then
                        let setColor = Action<Rendering.VisualLineElement>(fun el -> el.TextRunProperties.SetForegroundBrush br)
                        if isNull selOnLine then
                            base.ChangeLinePart(st, en, setColor)
                        else
                            // only color the parts that are not selected:
                            let mutable from = st
                            for seg in selOnLine do
                                if seg.EndOffset > from && seg.StartOffset < en then
                                    if seg.StartOffset > from then base.ChangeLinePart(from, seg.StartOffset, setColor)
                                    from <- max from seg.EndOffset
                            if from < en then base.ChangeLinePart(from, en, setColor)
                i <- i + 1
