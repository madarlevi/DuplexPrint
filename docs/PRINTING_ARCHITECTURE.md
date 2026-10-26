# PDF printing architecture decision

Reviewed on 2026-10-26 against the actual .NET 10 WPF application.

## Decision

Use the **Windows.Data.Pdf OS renderer**, a small original C++/WinRT helper,
and **System.Drawing.Printing.PrintDocument** with the selected Windows driver.
The helper is compiled and included automatically. End users do not obtain a
PDF viewer or a native PDF-engine package. The original Sumatra integration
was preserved in an ignored local snapshot until render/layout regression
checks passed, then removed from active source and packaging.

This choice is an engineering assessment based on the scope and validation
below. It does not establish perfect compatibility with every PDF or printer.

## Alternatives investigated

| Option | Reliability and accuracy | License/compatibility | Size, maintenance and deployment | Decision |
| --- | --- | --- | --- | --- |
| GDI, WPF PrintDialog/XPS, or RAW spooler alone | Provide printing transport/layout but do not universally interpret arbitrary PDF. RAW PDF requires a PDF-capable target printer. | Windows/.NET APIs; WPF compatible. | Small, but cannot meet the generic-printer requirement without a renderer. | Insufficient by themselves. |
| Existing PDFsharp 6.2.4 alone | Creates/modifies PDFs but does not render arbitrary existing PDF pages. | MIT at this pinned version; already part of the application. | Retained for preparation; cannot replace a rendering engine. | Insufficient alone. |
| Windows.Data.Pdf with a .NET WinRT projection | Provides PDF-to-bitmap rendering; printing layout still needs implementation. | Supported Windows API; versioned Windows TFM adds the SDK targeting package. | OS maintains engine; straightforward with online targeting packages. | Viable. A native SDK projection was selected so this project needs no extra managed WinRT projection package and isolates the native renderer. |
| Windows.Data.Pdf via included C++/WinRT helper + GDI/.NET driver | Actual page content, crop, rotation, color primitives and 600-DPI geometry verified locally. Raster output requires fine-detail/color review. | OS component is not redistributed. C++/WinRT MIT; STL Apache-2.0 with LLVM exception; MSVC runtime has separate toolchain terms. | Approximately 0.34 MB native helper in this toolchain, no separate runtime installer. Developers need C++ tools/SDK. OS updates maintain PDF engine. | Selected for this Windows-only manual-duplex utility. |
| Fresh PDFium + PDFiumCore/bblanchon binaries | Technically capable; PDFium also exposes Windows HDC rendering. Needs page/settings/error handling and native crash isolation. | Binding/build-script license is not the complete native-library license. PDFium and all bundled component notices must accompany a pinned build. | Adds a separately versioned engine and security-update responsibility. Current packages/native assets were not restored or executed locally. | Strong alternative if the OS renderer fails representative production PDFs; not needed for the validated scope. |
| PDFtoImage | Maintained rendering wrapper; printing still needs driver/layout code. | MIT wrapper; PDFium, SkiaSharp and native assets have separate component terms. | Adds both PDFium and Skia native dependency chains to a Windows-only utility. | More dependencies than required here. |
| Original PdfiumViewer / Docnet's documented native version | Original PdfiumViewer is archived; Docnet README documents PDFium 5445. | Top-level permissive terms do not establish current native security or .NET 10/WPF suitability. | Would require maintained native replacements, version auditing and integration work. | Not chosen as an unexamined drop-in. |
| MuPDF/Ghostscript-backed integrations | Potentially capable renderers. | Common distributions introduce AGPL/GPL or commercial obligations. | Extra engine and redistribution review. | Unnecessary when a suitable OS renderer exists. |
| Shell/default viewer/Edge automation | Depends on associations, installed viewers, dialogs and external application behavior. | Deployment/environment dependent. | Cannot reliably preserve the required silent printer/page selection. | Rejected. |

The PDFium rows are feasibility assessments, not claims that those packages
were installed, tested or found insecure. No third-party native engine was
silently downloaded or included.

## Actual execution flow

1. The interactive WPF app validates the selected printer and uses PDFsharp
   6.2.4 to prepare both passes from one locked source snapshot. Source PDFs
   are retained. Each job owns a unique temporary directory.
2. The default HP profile preserves ascending odd fronts and ascending even
   backs. Profiles independently configure front/back order and optional 180°
   back rotation. Odd counts include one blank with the unpaired front's media
   box, crop box and rotation. One-page jobs have no back pass. Settings and the
   physical printer are fixed for both passes; the second pass requires a click.
3. For an explicitly confirmed pass, the app starts its own apphost with the
   internal `--print-pass` protocol. This bypasses the interactive instance's
   mutex, watcher, startup registration and windows.
4. The isolated .NET worker validates the exact printer, sets one copy,
   simplex and all prepared pages, and invokes the bundled native renderer.
5. The renderer calls `Windows.Data.Pdf`, writes PNGs into an owned child
   directory, and publishes a manifest only after every page succeeds. It
   never calls a printer API. Windows handles PDF parsing and rasterization.
6. The .NET worker validates/decodes every PNG before starting PrintDocument.
   It matches available paper sizes, applies page orientation and centers a
   proportionally reduced image within hardware margins. Smaller pages are
   not enlarged. Printer Graphics use explicit physical units and compensated
   hardware-origin offsets.
7. Parent state advances only after the print worker exits successfully.
   Worker stderr is explicitly UTF-8. Errors, timeout or requested cancellation
   do not advance the pass and instruct users to inspect the spool queue.

The native renderer has an eight-minute limit, inside a ten-minute pass
limit. Closing during active work requests cancellation; the child process
is stopped before cleanup. A stopped process does not retract a spool job.
These processes isolate faults but are not a security sandbox: they run as
the current user. Keep Windows and the compiler/runtime libraries updated.

## Quality and compatibility limits

The engine renders at 600 DPI to opaque white-background PNGs, respecting
crop/rotation and ignoring the OS high-contrast display setting. It does not
preserve vector commands, spot colors or a prepress color pipeline. Very small
text, forms/annotations, scans, uncommon fonts and color-critical PDFs need
production samples and physical comparison. Driver paper support and physical
feed orientation remain hardware-dependent.

Rendering is bounded to 16,384 pixels per dimension and 512 MiB of decoded
pixels per page. A3 at 600 DPI is covered by a regression test. Larger pages
are rejected instead of silently reducing resolution. Rendering a complete
pass first uses temporary disk space; substantial documents can hit memory,
disk or time limits. Errors leave the source document intact.

No automatic duplex option existed in the audited application. The existing
manual workflow remains; it deliberately requests simplex on each pass.

## Primary sources

- [Microsoft desktop printing APIs](https://learn.microsoft.com/en-us/windows/win32/printdocs/printdocs-printing)
- [PDFsharp rendering limitation](https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html)
- [MuPDF licensing](https://mupdf.readthedocs.io/en/1.28.5/license.html)
- [RAW print data](https://learn.microsoft.com/en-us/windows-hardware/drivers/print/raw-data-type)
- [Windows PDF rendering](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync)
- [PDF page Size includes crop/rotation](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.size)
- [Render options](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpagerenderoptions)
- [.NET desktop WinRT configuration](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)
- [PrintDocument](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.printing.printdocument)
- [PDFiumCore](https://github.com/Dtronix/PDFiumCore)
- [PDFium binaries and release notices](https://github.com/bblanchon/pdfium-binaries)
- [PDFtoImage dependency/serialization description](https://github.com/sungaila/PDFtoImage)
- [Archived PdfiumViewer](https://github.com/pvginkel/PdfiumViewer)
- [Docnet's documented native version](https://github.com/GowenGit/docnet)
- [C++/WinRT license](https://github.com/microsoft/cppwinrt/blob/master/LICENSE)
- [Microsoft STL license](https://github.com/microsoft/STL/blob/main/LICENSE.txt)
- [MSVC redistribution conditions](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files)

See THIRD_PARTY_NOTICES.md at the repository root for the actual distribution's
license inventory. In the release ZIP that file is named THIRD_PARTY_NOTICES.txt.
