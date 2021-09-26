#r "PresentationCore" // WPF via fsi only works in .NET Framework, not net5.0
#r "PresentationFramework"
#r "WindowsBase"

#r "nuget: AvalonLog"

open System.Windows

let log = new AvalonLog.AvalonLog() // The main class wraping an Avalonedit TextEditor as append only log.

// create some printing functions by partial application:
let red   = log.printfColor 255 0 0  // without newline
let blue  = log.printfnColor 0 0 255 // with newline
let green = log.printfnColor 0 155 0 // with newline

// print to log using F# printf formating
red   "%s" "Hello, "
blue  "%s" "World!"
red   "%s" "The answer is "
green "%d" 42


Application().Run(Window(Content=log))  // show WPF window




