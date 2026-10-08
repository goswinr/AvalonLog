namespace AvalonLog.Tests

open System
open System.Text
open Xunit
open AvalonLog.Tests.Helpers

type TextWriterTests() =

    [<WpfFact>]
    member _.``all Write overloads reach the log`` () =
        // The base TextWriter.Write(char) does nothing, and the other overloads end up in it.
        let log = AvalonLog.AvalonLog()
        let w = log.GetTextWriter(255, 0, 0)
        w.Write 'A'
        w.Write [| 'B'; 'C' |]
        w.Write(StringBuilder "D")
        w.Write([| 'x'; 'E'; 'x' |], 1, 1)
#if NET
        w.Write("F".AsSpan())
#else
        w.Write "F"
#endif
        w.WriteLine 'G'
        w.Write "H"
        pumpUntil 2000 (fun () -> log.Text().EndsWith "H")
        Assert.Equal("ABCDEFG" + nl + "H", log.Text())

    [<WpfFact>]
    member _.``null strings print nothing and don't throw`` () =
        let log = AvalonLog.AvalonLog()
        let w = log.GetTextWriter(255, 0, 0)
        w.Write(null:string)
        w.WriteLine(null:string)
        log.Append null
        log.AppendLine null
        w.Write "end"
        pumpUntil 2000 (fun () -> log.Text().EndsWith "end")
        Assert.Equal(nl + nl + "end", log.Text())

    [<WpfFact>]
    member _.``the TextWriter encoding is UTF-8 without BOM`` () =
        let log = AvalonLog.AvalonLog()
        let w = log.GetTextWriter(255, 0, 0)
        Assert.Equal(Encoding.UTF8.CodePage, w.Encoding.CodePage)
        Assert.Empty(w.Encoding.GetPreamble())
