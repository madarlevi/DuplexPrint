# Third-party notices

DuplexPrint's original C# and native helper code is MIT licensed by
Madár Levente (madarlevi). That grant does not relicense third-party software.

## Windows PDF and printing components

PDF rendering uses the OS `Windows.Data.Pdf` API. Printing uses .NET's
System.Drawing.Printing and the installed Windows printer driver. The PDF
engine is an operating-system component: its binaries/source are not bundled
or relicensed, and Windows updates maintain it. No external PDF viewer is used.

- https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf
- https://learn.microsoft.com/en-us/dotnet/api/system.drawing.printing.printdocument

The experimental installed edition also calls Windows Print Support Virtual
Printer, XPS-to-PDF conversion, package-local storage and FullTrustProcessLauncher
APIs. These are Windows OS components, not third-party printer/PDF binaries
distributed in this repository. CaptureTask is original MIT-licensed source;
it uses the same SDK headers and static-runtime terms described below.

- https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/print-support-app-v4-design-guide
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/msix-manifest-specification-print-support-virtual-printer

## C++/WinRT — MIT

The native helper compiles SDK C++/WinRT headers. Copyright (c) Microsoft
Corporation. The projection code is MIT licensed; its notice is included in
[licenses/CppWinRT-LICENSE.txt](licenses/CppWinRT-LICENSE.txt).

- https://github.com/microsoft/cppwinrt
- https://github.com/microsoft/cppwinrt/blob/master/LICENSE

## Microsoft C++ Standard Library and runtime

Microsoft STL is Apache-2.0 WITH LLVM-exception. Its full upstream license,
including the compiled-object exception, is retained in
[licenses/Microsoft-STL-LICENSE.txt](licenses/Microsoft-STL-LICENSE.txt).
Copyright (c) Microsoft Corporation; see the upstream project for contributors.

- https://github.com/microsoft/STL
- https://github.com/microsoft/STL/blob/main/LICENSE.txt

The helper uses the **release, statically linked MSVC runtime** (`/MT`), not
the debug CRT. The MSVC CRT/toolchain has separate Microsoft license and
redistribution terms; it is not relicensed MIT by this project. Builders and
distributors must use appropriately licensed tools and comply with the
applicable Visual Studio/Build Tools terms. No compiler, SDK, debug runtime,
or separate VC installer is placed in the ZIP. The inspected native executable
imports only Windows system libraries, without a separate VC-runtime DLL.

- https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files
- https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution
- https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution
- https://visualstudio.microsoft.com/license-terms/

## PDFsharp 6.2.4 — MIT

Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany.
The pinned 6.2.4 package is MIT licensed. Full notice:
[licenses/PDFsharp-LICENSE.txt](licenses/PDFsharp-LICENSE.txt).

- https://www.nuget.org/packages/PDFsharp/6.2.4
- https://github.com/empira/PDFsharp/blob/v6.2.4/LICENSE

Later versions may differ: upstream describes AGPL/commercial licensing from
6.5.0. Review version-specific terms before any upgrade.

## Microsoft .NET libraries and optional bundled runtime

The resolved package graph includes Microsoft.Extensions.Logging.Abstractions
8.0.3 and Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2 under MIT.
Their copyright/license and component notices are preserved in `licenses/`.
Framework-provided dependencies may be pruned by .NET 10; lock files record the
resolved package graph. No additional managed PDF-engine package was added.

Self-contained ZIPs also contain .NET and Windows Desktop runtime binaries.
The package script requires their exact runtime-pack LICENSE and third-party
notice files under `licenses/runtime/`. The primary .NET license is MIT, with
additional component terms in those notices. Framework-dependent ZIPs use the
separately installed runtime and do not redistribute it.

- https://github.com/dotnet/runtime
- https://github.com/dotnet/wpf

The previous PDF-viewer dependency is not part of this version. No GPL/AGPL
PDF-engine binary or source is included. This review supports applying MIT
to the original code, but does not guarantee every possible redistribution;
toolchain rights and ownership of original contributions remain the
maintainer's responsibility. Official sources reviewed on 2026-10-26.
