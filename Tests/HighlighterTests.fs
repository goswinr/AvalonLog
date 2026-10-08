namespace AvalonLog.Tests

open System.Threading
open System.Windows
open Xunit
open AvalonLog.Tests.Helpers

type HighlighterTests() =

    [<WpfFact>]
    member _.``selecting text raises OnHighlightChanged with all occurrences, also without a WPF Application`` () =
        Assert.Null(Application.Current) // like in a plugin of a host that is not a WPF Application
        let log = AvalonLog.AvalonLog()
        let uiThread = Thread.CurrentThread.ManagedThreadId
        let mutable result = None
        log.SelectedTextHighLighter.OnHighlightChanged.Add(fun (txt, offsets) ->
            result <- Some (txt, List.ofSeq offsets, Thread.CurrentThread.ManagedThreadId = uiThread) )
        log.AppendLine "foo bar foo baz foo"
        pumpUntil 1000 (fun () -> log.Text().Length > 0)
        log.AvalonEdit.Select(0, 3)
        pumpUntil 2000 (fun () -> result.IsSome)
        Assert.Equal(Some ("foo", [0; 8; 16], true), result) // true: raised on the UI thread
