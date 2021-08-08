# AvalonLog


[![AvalonLog on nuget.org](https://img.shields.io/nuget/v/AvalonLog.svg)](https://www.nuget.org/packages/AvalonLog/)
[![AvalonLog on fuget.org](https://www.fuget.org/packages/AvalonLog/badge.svg)](https://www.fuget.org/packages/AvalonLog)

![logo](https://github.com/goswinr/AvalonLog/raw/main/Doc/logo400.png)

 A threadsave and colorful WPF text viewer for F# based on [AvalonEditB](https://github.com/goswinr/AvalonEditB). Works on .NET Framework 4.7.2 and .NET 5.0

### Usage
```fsharp
// #r "PresentationCore"
// #r "PresentationFramework"
// #r "WindowsBase"
#r "nuget: AvalonLog"

open System.Windows

let log = new AvalonLog.AvalonLog() 

let red   = log.printfColor 255 0 0  // without newline
let blue  = log.printfnColor 0 0 255 // with newline
let green = log.printfnColor 0 155 0 // with newline

red   "%s" "Hello, "
blue  "%s" "World!"
red   "%s" "The answer is "
green "%d" 42

Application().Run(Window(Content=log))  // show WPF window
```
this will produce 

![WPF window](https://github.com/goswinr/AvalonLog/raw/main/Doc/HelloWorld.png)
 
### Download

AvalonLog is available as [NuGet package](https://www.nuget.org/packages/AvalonLog). 

### How to build

Just run `dotnet build` 
 
### Licence

MIT

Logo by [LovePik](https://lovepik.com/image-401268798/crystal-parrot-side-cartoon.html)
