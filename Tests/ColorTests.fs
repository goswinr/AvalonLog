namespace AvalonLog.Tests

open System
open System.Threading
open System.Windows.Media
open Xunit
open AvalonLog.Tests.Helpers

type ColorTests() =

    [<WpfFact>]
    member _.``a color change in the middle of a line splits it into two runs, not one per character`` () =
        use w = new LogWindow()
        w.Log.printfColor 255 0 0 ">"
        w.Log.printfnColor 0 0 255 "%s" (String('x', 2000))
        pumpUntil 1000 (fun () -> w.Log.Text().Length > 2000)
        Assert.Equal(2, w.ElementCount 1)

    [<WpfFact>]
    member _.``colors are drawn where they were printed, but not on selected text`` () =
        use w = new LogWindow()
        let log = w.Log
        log.printfColor 255 0 0 "rr"
        log.printfColor 0 0 255 "bb"
        log.Append "dd"
        log.printfnColor 255 0 0 "rr"
        log.printfnColor 0 0 255 "bbbb"
        log.Append "dd"
        log.printfnColor 0 0 255 "bb"
        pumpUntil 1000 (fun () -> log.Text().EndsWith("ddbb" + nl))
        Assert.Equal("rrbbddrr", w.Colors 1)
        Assert.Equal("bbbb", w.Colors 2)
        Assert.Equal("ddbb", w.Colors 3)
        Assert.Equal(4, w.ElementCount 1)
        log.AvalonEdit.Select(1, 4) // selected text is not colored, so that the selection foreground shows
        Assert.Equal("rssssdrr", w.Colors 1)
        log.AvalonEdit.Select(0, 0)
        Assert.Equal("rrbbddrr", w.Colors 1)

    [<WpfFact>]
    member _.``two threads printing in different colors each get their own color`` () =
        let log = AvalonLog.AvalonLog()
        let n = 10_000
        runInBackground (fun () ->
            let red  = Thread(fun () -> for _ in 1 .. n do log.printfnColor 255 0 0 "R")
            let blue = Thread(fun () -> for _ in 1 .. n do log.printfnColor 0 0 255 "B")
            red.Start(); blue.Start()
            red.Join(); blue.Join() )
        pumpUntil 2000 (fun () -> log.Text().Length = 2 * n * (1 + nl.Length))
        let txt = log.Text()
        let changes = colorChanges log |> Array.ofList
        let mutable wrong = 0
        for k in 0 .. changes.Length - 1 do
            let st, br = changes.[k]
            let en = if k + 1 < changes.Length then fst changes.[k+1] else txt.Length
            for i in max 0 st .. en - 1 do
                match txt.[i] with
                | 'R' when isNull br || br.Color <> Colors.Red  -> wrong <- wrong + 1
                | 'B' when isNull br || br.Color <> Colors.Blue -> wrong <- wrong + 1
                | _ -> ()
        Assert.Equal(0, wrong)

    [<WpfFact>]
    member _.``Clear while another thread prints keeps that thread's color`` () =
        let log = AvalonLog.AvalonLog()
        log.MaximumCharacterAllowance <- 20_000_000
        let red = AvalonLog.Brush.ofRGB 255 0 0
        let w = log.GetTextWriter red
        let mutable running = true
        let producer =
            Thread((fun () ->
                        let mutable i = 0
                        while running do
                            w.WriteLine("red line " + string i)
                            i <- i + 1 ), IsBackground = true)
        producer.Start()
        try
            for _ in 1 .. 30 do
                pump 40
                log.Clear()
                pumpUntil 2000 (fun () -> log.Text().Length > 0)
                let hasRed = colorChanges log |> List.exists (fun (_, br) -> Object.ReferenceEquals(br, red))
                Assert.True(hasRed, "the text printed after Clear() lost its color")
        finally
            running <- false
            producer.Join()

    [<WpfFact>]
    member _.``text in the default color follows changes of the Foreground`` () =
        use w = new LogWindow()
        let log = w.Log
        log.AppendWithLastColor "L" // no color used yet, so the last color is the default
        log.Append "dd"
        log.AppendLineWithColor(255, 0, 0, "rr")
        log.AppendLine "dddd"
        pumpUntil 1000 (fun () -> log.Text().EndsWith("dddd" + nl))
        Assert.Equal("dddrr", w.Colors 1)
        log.AvalonEdit.Foreground <- Brushes.Green
        // 'd' is the current Foreground, now green:
        Assert.Equal("dddrr", w.Colors 1)
        Assert.Equal("dddd", w.Colors 2)

    [<WpfFact>]
    member _.``an unfrozen brush created on a worker thread can be printed from there`` () =
        use w = new LogWindow()
        runInBackground (fun () ->
            let br = SolidColorBrush Colors.Lime // not frozen, belongs to this worker thread
            w.Log.AppendLineWithBrush(br, "gg")
            w.Log.printfnBrush br "%s" "gg" )
        pumpUntil 1000 (fun () -> w.Log.Text() = "gg" + nl + "gg" + nl)
        Assert.Equal("gg", w.Colors 1)
        Assert.Equal("gg", w.Colors 2)

    [<WpfFact>]
    member _.``an unfrozen brush of the UI thread can be printed from a worker thread`` () =
        use w = new LogWindow()
        let br = SolidColorBrush Colors.Lime // not frozen, belongs to the UI thread
        runInBackground (fun () -> w.Log.AppendLineWithBrush(br, "gg"))
        pumpUntil 1000 (fun () -> w.Log.Text() = "gg" + nl)
        Assert.Equal("gg", w.Colors 1)
        Assert.False(br.IsFrozen) // only the thread a brush belongs to can freeze it
