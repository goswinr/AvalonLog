# AvalonLog

![code size](https://img.shields.io/github/languages/code-size/goswinr/AvalonLog.svg) 
[![License](https://img.shields.io/badge/license-MIT-green)](./LICENSE)

![logo](https://raw.githubusercontent.com/goswinr/AvalonLog/main/Doc/logo400.png)

Avalonlog is a fast and threadsave WPF text log viewer for colored text. Including F# printf formating . Based on [AvalonEditB](https://github.com/goswinr/AvalonEditB). Works on .NET Framework 4.7.2 and .NET 5.0

### Usage

Here an short example for F# interactive in .NET Framework.
(for net5.0 you would have to use it in a project)

```fsharp
#r "PresentationCore"
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
red   "Hello, "
blue  "World!"
red   "The answer"
green " is %d." (40 + 2)

Application().Run(Window(Content=log))  // show WPF window 
```
this will produce 

![WPF window](https://raw.githubusercontent.com/goswinr/AvalonLog/main/Doc/HelloWorld.png)


For C# there is  
```csharp
public void AppendWithBrush(SolidColorBrush br, string s)
```
and similar functions on the `AvalonLog` instance.
### Documentation

See extracted API at [fuget.org](https://www.fuget.org/packages/AvalonLog/0.4.0/lib/net472/AvalonLog.dll/AvalonLog)  

### Licence

MIT

Logo by [LovePik](https://lovepik.com/image-401268798/crystal-parrot-side-cartoon.html)

### Changelog

 `0.3.1` 
- Update Xml Docstrings

`0.4.0`
- Update to AvalonEditB `1.2.0` 
- Rename `GetTextWriterIf` to `GetConditionalTextWriter`