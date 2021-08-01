# AvalonLog


[![AvalonLog on nuget.org](https://img.shields.io/nuget/v/AvalonLog.svg)](https://www.nuget.org/packages/AvalonLog/)
[![AvalonLog on fuget.org](https://www.fuget.org/packages/AvalonLog/badge.svg)](https://www.fuget.org/packages/AvalonLog)

![logo](https://raw.githubusercontent.com/goswinr/AvalonLog/main/Doc/logo400.png)

 A threadsave and colorful WPF text viewer for F# based on [AvalonEditB](https://github.com/goswinr/AvalonEditB)

### Usage
```fsharp
#r "PresentationCore"
#r "PresentationFramework"
#r "WindowsBase"
#r "AvalonEditB.dll" 
#r "AvalonLog.dll"

open System.Windows

let log = new AvalonLog.AvalonLog() 
let red =  log.printfColor 255 0 0
let blue = log.printfnColor 0 0 255
let green = log.printfnColor 0 155 0

red "%s" "Hello, "
blue "%s" "World!"
red "%s" "The answer is "
green "%d"  (40+2)

Application().Run(Window(Content=log))  // show WPF window
```
this will produce 

![WPF window](https://raw.githubusercontent.com/goswinr/AvalonLog/main/Doc/HelloWorld.png)

 
### Download

AvalonLog is available as [NuGet package](https://www.nuget.org/packages/AvalonLog). 

### How to build

Just run `dotnet build` 
 
 Logo by [LovePik](https://lovepik.com/image-401268798/crystal-parrot-side-cartoon.html)
