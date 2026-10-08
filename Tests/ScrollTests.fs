namespace AvalonLog.Tests

open System
open Xunit
open AvalonLog.Tests.Helpers

type ScrollTests() =

    let atEnd (w:LogWindow) =
        w.Window.UpdateLayout()
        let ed = w.Log.AvalonEdit
        ed.VerticalOffset + ed.ViewportHeight >= ed.ExtentHeight - 1.0

    let printLines (log:AvalonLog.AvalonLog) n =
        for i in 1 .. n do
            log.AppendLine(sprintf "line %d" i)

    [<WpfFact>]
    member _.``new text scrolls to the end only if the view is at the end`` () =
        use w = new LogWindow()
        let ed = w.Log.AvalonEdit
        printLines w.Log 200
        pump 200
        Assert.True(atEnd w, "should follow the new text")
        ed.ScrollToHome()
        pump 100
        printLines w.Log 50
        pump 200
        w.Window.UpdateLayout()
        Assert.Equal(0.0, ed.VerticalOffset) // stays where the user scrolled to
        ed.ScrollToEnd()
        pump 100
        printLines w.Log 50
        pump 200
        Assert.True(atEnd w, "should follow the new text again after scrolling back to the end")

    [<WpfFact>]
    member _.``after Clear new text is followed again`` () =
        use w = new LogWindow()
        printLines w.Log 200
        pump 200
        w.Log.AvalonEdit.ScrollToHome()
        pump 100
        w.Log.Clear()
        printLines w.Log 200
        pump 200
        Assert.True(atEnd w)

    [<WpfFact>]
    member _.``with WordWrap long lines are followed to the end`` () =
        // a wrapped last line gets taller during layout, that must not count as the user scrolling up
        use w = new LogWindow()
        w.Log.WordWrap <- true
        for _ in 1 .. 30 do
            w.Log.AppendLine(String('w', 700))
            pump 60
        pump 200
        Assert.True(atEnd w)
