# Genuine application captures

- `main-window.png`: the real WPF main window with a generated five-page PDF
  selected, before printing.
- `settings-window.png`: the real WPF settings editor with the original HP profile.

Captured on 2026-10-26 by displaying the actual application window classes and
exporting their rendered **client areas** with WPF RenderTargetBitmap. These are
not mockups and do not show a simulated successful print. The visible Windows
printer came from the real installed-printer list. No print calls were made.
The settings path is shortened by the application's normal display formatting.

To reproduce on Windows after building the regression harness:

```powershell
.\tests\DuplexPrint.Tests\bin\Debug\net10.0-windows\DuplexPrint.Tests.exe --capture-ui "$PWD/assets/screenshots"
```

For an OS-framed screenshot, launch DuplexPrint normally, select a nonsensitive
PDF, and use Windows Snipping Tool. Do not click the print button for a screenshot.
Check that filenames, account paths, printer network addresses and other private
information are not visible before committing.
