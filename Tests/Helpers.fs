module AvalonLog.Tests.Helpers

open System
open System.Collections
open System.Reflection
open System.Text
open System.Windows
open System.Windows.Media
open System.Windows.Threading
open Xunit

// The tests count UI updates and threadpool work items, so they must not run in parallel.
[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()

// Most tests use [<WpfFact>] from Xunit.StaFact: it runs the test on an STA thread with a WPF Dispatcher.
// It needs a test class with a constructor, it fails with 'Sequence contains no elements' for functions in an F# module.

let nl = Environment.NewLine

/// The number of lines, each ending with a new line.
let lineCount (txt:string) = txt.Split([| nl |], StringSplitOptions.None).Length - 1

/// Runs the Dispatcher of the current thread for ms milliseconds,
/// so that posted flushes, layout and rendering happen.
let pump (ms:int) =
    let frame = DispatcherFrame()
    let timer = DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
    timer.Interval <- TimeSpan.FromMilliseconds(float ms)
    timer.Tick.Add(fun _ -> timer.Stop(); frame.Continue <- false)
    timer.Start()
    Dispatcher.PushFrame frame

/// Runs the Dispatcher of the current thread till the condition is true.
/// Fails after timeoutMs.
let pumpUntil (timeoutMs:int) (condition: unit -> bool) =
    let sw = Diagnostics.Stopwatch.StartNew()
    while not (condition()) do
        if sw.ElapsedMilliseconds > int64 timeoutMs then
            failwithf "condition not met within %d ms" timeoutMs
        pump 10

/// Runs f on a background thread and keeps the Dispatcher of the current thread running till f is done.
let runInBackground (f: unit -> unit) =
    let mutable error : exn = null
    let thread = Threading.Thread((fun () -> try f () with e -> error <- e), IsBackground = true)
    thread.Start()
    while thread.IsAlive do
        pump 10
    if not (isNull error) then
        raise (Exception("the background thread failed", error))

/// The number of times text got added to the document, a flush of the print buffer.
let countFlushes (log:AvalonLog.AvalonLog) =
    let mutable n = 0
    log.AvalonEdit.TextChanged.Add(fun _ -> n <- n + 1)
    fun () -> n

/// The places where the text color changes, as (offset, brush).
/// Read via reflection from the private offsetColors field, they only change on the UI thread.
let colorChanges (log:AvalonLog.AvalonLog) : (int * SolidColorBrush) list =
    let field = typeof<AvalonLog.AvalonLog>.GetField("offsetColors", BindingFlags.NonPublic ||| BindingFlags.Instance)
    let flags = BindingFlags.Instance ||| BindingFlags.NonPublic ||| BindingFlags.Public
    [ for c in (field.GetValue log :?> IList) ->
        let off   = c.GetType().GetProperty("off"  , flags).GetValue(c) :?> int
        let brush = c.GetType().GetProperty("brush", flags).GetValue(c) :?> SolidColorBrush
        off, brush ]

/// An AvalonLog in a Window that is shown off screen, so that layout and rendering happen.
/// Dispose to close the Window.
type LogWindow() =
    let log = AvalonLog.AvalonLog()
    let win =
        Window( Content = log, Width = 600., Height = 300., Left = -30000., Top = -30000.,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false)
    do win.Show()

    member _.Log = log
    member _.Window = win

    /// Redraws and returns the colors of the given line, one char per character:
    /// r, g, b for red, green and blue; d for the default Foreground of the editor;
    /// s for the selection foreground; ? for any other color.
    member _.Colors (lineNumber:int) =
        let tv = log.AvalonEdit.TextArea.TextView
        tv.Redraw()
        win.UpdateLayout()
        let fg = (log.AvalonEdit.Foreground :?> SolidColorBrush).Color
        let selFg =
            match log.AvalonEdit.TextArea.SelectionForeground with
            | :? SolidColorBrush as b -> Some b.Color
            | _ -> None
        let sb = StringBuilder()
        for e in tv.GetVisualLine(lineNumber).Elements do
            let ch =
                match e.TextRunProperties.ForegroundBrush with
                | :? SolidColorBrush as b when b.Color = Colors.Red   -> 'r'
                | :? SolidColorBrush as b when b.Color = Colors.Lime  -> 'g'
                | :? SolidColorBrush as b when b.Color = Colors.Blue  -> 'b'
                | :? SolidColorBrush as b when b.Color = fg           -> 'd'
                | :? SolidColorBrush as b when Some b.Color = selFg   -> 's'
                | _ -> '?'
            sb.Append(ch, e.DocumentLength) |> ignore
        sb.ToString()

    /// Redraws and returns the number of visual elements the line is split into.
    member _.ElementCount (lineNumber:int) =
        let tv = log.AvalonEdit.TextArea.TextView
        tv.Redraw()
        win.UpdateLayout()
        tv.GetVisualLine(lineNumber).Elements.Count

    interface IDisposable with
        member _.Dispose() = win.Close()
