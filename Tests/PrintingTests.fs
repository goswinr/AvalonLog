namespace AvalonLog.Tests

open System
open System.Threading
open System.Windows.Threading
open Xunit
open AvalonLog.Tests.Helpers

type PrintingTests() =

    [<WpfFact>]
    member _.``a printfn from another thread updates the screen once, not once per part`` () =
        // printfn calls Write several times per line, these parts should show up together
        let log = AvalonLog.AvalonLog()
        let flushes = countFlushes log
        let w = log.GetTextWriter(255, 0, 0)
        let lines = 10
        runInBackground (fun () ->
            for i in 1 .. lines do
                fprintfn w "step %d of %d" i lines
                Thread.Sleep 120 )
        pumpUntil 2000 (fun () -> log.Text().EndsWith("step 10 of 10" + nl))
        Assert.True(flushes() <= lines + 2, sprintf "%d screen updates for %d printfn lines, expected about one per line" (flushes()) lines)

#if NET // ThreadPool.CompletedWorkItemCount is not available on .NET Framework
    [<WpfFact>]
    member _.``printing 100k lines takes a few threadpool work items, not one per Write call`` () =
        let log = AvalonLog.AvalonLog()
        log.MaximumCharacterAllowance <- Int32.MaxValue
        let w = log.GetTextWriter(255, 0, 0)
        let n = 100_000
        let before = ThreadPool.CompletedWorkItemCount
        runInBackground (fun () -> for i in 1 .. n do fprintfn w "line %d of %s" i "some text")
        pumpUntil 5000 (fun () -> log.Text().EndsWith(sprintf "line %d of some text%s" n nl))
        let workItems = ThreadPool.CompletedWorkItemCount - before
        Assert.True(workItems < 5_000L, sprintf "%d threadpool work items for %d printfn lines" workItems n)
        Assert.Equal(n, lineCount (log.Text()))
#endif

    [<WpfFact>]
    member _.``a single print from another thread shows up`` () =
        let log = AvalonLog.AvalonLog()
        runInBackground (fun () -> log.AppendLine "x")
        pumpUntil 1000 (fun () -> log.Text() = "x" + nl)

    [<WpfFact>]
    member _.``printing on the busy UI thread still updates the document every PrintInterval`` () =
        // e.g. a script running on the UI thread in Fesh.Revit, the posted flush can't run till it is done
        let log = AvalonLog.AvalonLog()
        let flushes = countFlushes log
        let sw = Diagnostics.Stopwatch.StartNew()
        let mutable i = 0
        while sw.ElapsedMilliseconds < 500L do // not pumping the Dispatcher
            log.AppendLine(sprintf "ui line %d" i)
            i <- i + 1
            Thread.Sleep 1
        let flushesWhileBusy = flushes()
        pumpUntil 2000 (fun () -> lineCount (log.Text()) = i)
        Assert.True(flushesWhileBusy >= 3, sprintf "only %d updates during 500 ms of printing on the UI thread" flushesWhileBusy)

    [<Fact>]
    member _.``printing after the UI Dispatcher shut down does not throw`` () =
        let mutable log = Unchecked.defaultof<AvalonLog.AvalonLog>
        use ready = new ManualResetEventSlim()
        let ui = Thread((fun () ->
                            log <- AvalonLog.AvalonLog()
                            ready.Set()
                            Dispatcher.Run() ), IsBackground = true)
        ui.SetApartmentState ApartmentState.STA
        ui.Start()
        ready.Wait()
        log.AppendLine "before shutdown"
        Thread.Sleep 200
        log.Dispatcher.InvokeShutdown()
        ui.Join()
        for _ in 1 .. 5 do
            log.AppendLine "after shutdown"
            Thread.Sleep 100 // the flush timer fires in between, an exception on its thread would end the test process

    [<WpfFact>]
    member _.``the read-only log keeps no undo history`` () =
        let log = AvalonLog.AvalonLog()
        log.AppendLine "a"
        pumpUntil 1000 (fun () -> log.Text().Length > 0)
        Assert.False(log.AvalonEdit.Document.UndoStack.CanUndo)

    [<WpfFact>]
    member _.``a print beyond MaximumCharacterAllowance is cut off, then printing stops till Clear`` () =
        let log = AvalonLog.AvalonLog()
        log.MaximumCharacterAllowance <- 100
        log.AppendLine(String('a', 1000))
        log.AppendLine "after"
        pumpUntil 1000 (fun () -> log.Text().Contains "STOP OF LOGGING")
        pump 100
        let txt = log.Text()
        Assert.Equal(String('a', 100), txt.Split([| nl |], StringSplitOptions.None).[0])
        Assert.DoesNotContain("after", txt)
        log.Clear()
        log.AppendLine "resumed"
        pumpUntil 1000 (fun () -> log.Text() = "resumed" + nl)

    [<WpfFact>]
    member _.``the stop message is printed once when several threads reach MaximumCharacterAllowance together`` () =
        for _ in 1 .. 30 do // a race, 30 rounds so that it shows up reliably
            let log = AvalonLog.AvalonLog()
            log.MaximumCharacterAllowance <- 5_000
            runInBackground (fun () ->
                let threads = [ for _ in 1 .. 8 -> Thread((fun () -> for _ in 1 .. 2000 do log.AppendLine "xxxxxxxxxx"), IsBackground = true) ]
                for t in threads do t.Start()
                for t in threads do t.Join() )
            pumpUntil 1000 (fun () -> log.Text().Contains "STOP OF LOGGING")
            pump 100
            let banners = log.Text().Split([| "STOP OF LOGGING" |], StringSplitOptions.None).Length - 1
            Assert.Equal(1, banners)
