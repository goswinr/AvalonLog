![Logo](https://raw.githubusercontent.com/goswinr/AvalonLog/main/Docs/img/logo128.png)
# AvalonLog

[![AvalonLog on nuget.org](https://img.shields.io/nuget/v/AvalonLog)](https://www.nuget.org/packages/AvalonLog/)
[![Build Status](https://github.com/goswinr/AvalonLog/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/AvalonLog/actions/workflows/build.yml)
[![Docs Build Status](https://github.com/goswinr/AvalonLog/actions/workflows/docs.yml/badge.svg)](https://github.com/goswinr/AvalonLog/actions/workflows/docs.yml)
[![license](https://img.shields.io/github/license/goswinr/AvalonLog)](LICENSE.md)
![code size](https://img.shields.io/github/languages/code-size/goswinr/AvalonLog.svg)

AvalonLog is a fast and thread-safe WPF text log viewer for colored text. Including F# printf formatting. Based on [AvalonEditB](https://github.com/goswinr/AvalonEditB). Works on .NET Framework 4.7.2 and .NET 6.0

Thread-safe means that it can be called from any thread.

Fast means
- it buffers repeated print calls and updates the view maximum 20 times per second. see ![source](https://github.com/goswinr/AvalonLog/blob/main/Src/AvalonLog.fs#L222)
- Avalonedit is fast, the view is virtualized. It can easily handle thousands of lines.

### Usage

Here an short example for F# interactive in .NET Framework.
(for net7.0 you would have to use it in a project)

```fsharp
#r "PresentationCore"
#r "PresentationFramework"
#r "WindowsBase"

#r "nuget: AvalonLog"

open System.Windows

let log = new AvalonLog.AvalonLog() // The main class wrapping an Avalonedit TextEditor as append only log.

// create some printing functions by partial application:
let red   = log.printfColor 255 0 0  // without newline
let blue  = log.printfnColor 0 0 255 // with newline
let green = log.printfnColor 0 155 0 // with newline

// print to log using F# printf formatting
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

See extracted API at [fuget.org](https://www.fuget.org/packages/AvalonLog/0.14.0/lib/net472/AvalonLog.dll/AvalonLog)

### Download

AvalonLog is available as [NuGet package](https://www.nuget.org/packages/AvalonLog).

### How to build

Just run `dotnet build`

### Changelog
see [CHANGELOG.md](https://github.com/goswinr/AvalonLog/blob/main/CHANGELOG.md)

### License

[MIT](https://github.com/goswinr/AvalonLog/blob/main/LICENSE.md)

Logo by [LovePik](https://lovepik.com/image-401268798/crystal-parrot-side-cartoon.html)

