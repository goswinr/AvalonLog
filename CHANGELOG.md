# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- Nothing yet.

## [0.20.0] - 2025-03-19
### Changed
- remove Microsoft.Extensions.Logging dependency and the ILogger interface implementation (to fix a method-not-found-exception in Velopack in Fesh.Revit)

## [0.19.0] - 2025-03-18
### Changed
- remove explicit FSharp.Core ref

## [0.18.0] - 2025-02-15
### Added
- ILogger interface

## [0.17.0] - 2024-11-19
### Added
- Documentation via [FSharp.Formatting](https://fsprojects.github.io/FSharp.Formatting/)
- Github Actions for CI/CD
### Changed
- pin FSharp.Core to latest version (for Fesh.Revit)

## [0.16.0] - 2024-11-10
### Added
- add .IsAlive property to turn on/off logging

## [0.15.8] - 2024-11-04
### Changed
- upgrade to FSharp.Core 8.0.400 to make it work for Fesh.Revit

## [0.15.0] - 2024-11-03
### Added
- Pen Utils
- Exposed delay time configuration
### Changed
- Faster redraw performance

## [0.14.0] - 2024-09-08
### Changed
- Update to AvalonEditB 2.4.0

## [0.13.0] - 2024-06-09
### Changed
- Update to AvalonEditB 2.3.0

## [0.12.0] - 2023-10-29
### Changed
- Update to AvalonEditB 2.2.0

## [0.11.0] - 2023-09-10
### Changed
- Disable replace in Log
- Update to AvalonEditB 2.1.0

## [0.10.0] - 2023-07-27
### Changed
- Update to AvalonEditB 2.0.0

## [0.9.1] - 2023-04-29
### Changed
- Update to AvalonEditB 1.8.0

## [0.9.0] - 2023-04-23
### Changed
- Update to AvalonEditB 1.7.0

## [0.8.3] - 2023-01-08
### Changed
- Update to AvalonEditB 1.6.0

## [0.8.2] - 2022-12-17
### Changed
- Target net7.0
- Update to AvalonEditB 1.5.1
- Update readme and fix typos

## [0.7.2] - 2022-08-06
### Fixed
- Typos in readme

## [0.7.1] - 2022-08-06
### Changed
- Use AvalonEditB `1.4.1`
### Fixed
- Typos in doc-strings

## [0.7.0] - 2022-07-30
### Fixed
- Crash when Log has more than 1000k characters

## [0.6.0] - 2022-01-09
### Fixed
- ConditionalTextWriter

## [0.5.0] - 2021-12-11
### Changed
- Update to AvalonEditB `1.3.0`
- Target net6.0 and net472
### Fixed
- Typos in docstring

## [0.4.0] - 2021-10-17
### Changed
- Update to AvalonEditB `1.2.0`
- Rename `GetTextWriterIf` to `GetConditionalTextWriter`

## [0.3.1] - 2021-09-26
### Changed
- Update XML doc-strings



[Unreleased]: https://github.com/goswinr/AvalonLog/compare/0.20.0...HEAD
[0.20.0]: https://github.com/goswinr/AvalonLog/compare/0.19.0...0.20.0
[0.19.0]: https://github.com/goswinr/AvalonLog/compare/0.18.0...0.19.0
[0.18.0]: https://github.com/goswinr/AvalonLog/compare/0.17.0...0.18.0
[0.17.0]: https://github.com/goswinr/AvalonLog/compare/0.16.0...0.17.0
[0.16.0]: https://github.com/goswinr/AvalonLog/compare/0.15.8...0.16.0
[0.15.8]: https://github.com/goswinr/AvalonLog/compare/0.15.0...0.15.8
[0.15.0]: https://github.com/goswinr/AvalonLog/compare/0.14.0...0.15.0
[0.14.0]: https://github.com/goswinr/AvalonLog/compare/0.13.0...0.14.0
[0.13.0]: https://github.com/goswinr/AvalonLog/compare/0.12.0...0.13.0
[0.12.0]: https://github.com/goswinr/AvalonLog/compare/0.11.0...0.12.0
[0.11.0]: https://github.com/goswinr/AvalonLog/compare/0.10.0...0.11.0
[0.10.0]: https://github.com/goswinr/AvalonLog/compare/0.9.3...0.10.0
[0.9.3]: https://github.com/goswinr/AvalonLog/compare/0.9.2...0.9.3
[0.9.2]: https://github.com/goswinr/AvalonLog/compare/0.9.1...0.9.2
[0.9.1]: https://github.com/goswinr/AvalonLog/compare/0.9.0...0.9.1
[0.9.0]: https://github.com/goswinr/AvalonLog/compare/0.8.3...0.9.0
[0.8.3]: https://github.com/goswinr/AvalonLog/compare/0.8.2...0.8.3
[0.8.2]: https://github.com/goswinr/AvalonLog/compare/0.7.2...0.8.2
[0.7.2]: https://github.com/goswinr/AvalonLog/compare/0.7.1...0.7.2
[0.7.1]: https://github.com/goswinr/AvalonLog/compare/0.7.0...0.7.1
[0.7.0]: https://github.com/goswinr/AvalonLog/compare/0.6.0...0.7.0
[0.6.0]: https://github.com/goswinr/AvalonLog/compare/0.5.0...0.6.0
[0.5.0]: https://github.com/goswinr/AvalonLog/compare/0.4.0...0.5.0
[0.4.0]: https://github.com/goswinr/AvalonLog/compare/0.3.1...0.4.0
[0.3.1]: https://github.com/goswinr/AvalonLog/releases/tag/0.3.1
