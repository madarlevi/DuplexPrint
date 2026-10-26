# Installed edition — experimental

The installed edition is a signed MSIX with the portable WPF application plus
an original native WinRT background-task DLL. It uses Microsoft's **Print
Support Virtual Printer** architecture, available from **Windows 11 24H2,
build 26100**. The portable application does not depend on this feature.

## Data flow

```text
Another app's Print dialog
  → DuplexPrint virtual queue (registered by MSIX deployment)
  → Windows-hosted per-user DuplexPrint.CaptureTask
  → Windows OpenXPS → PDF conversion with the print ticket
  → package LocalState/Inbox/<generated-guid>.pdf
  → interactive DuplexPrint agent
  → user approves fronts
  → reload instructions; user approves backs
  → selected physical Windows printer
```

There is **no interactive LocalSystem service**. Windows services run in a
separate session, so a system service directly opening a desktop window is
not a reliable design. Windows owns the background-task activation; the task
launches the packaged desktop process for the current user. The agent watches
its own inbox and can be opened manually from Start. Each pass requires a click.

The manifest deliberately omits `OutputFileTypes`: Windows should deliver to
the app rather than present a Save As file-printer dialog. Preferred input is
`application/oxps`; direct PDF passthrough is deliberately not declared, so
print-ticket processing stays in the Windows conversion path. The source
document's upstream print settings/page selection are applied by the producing
application/Windows before capture; DuplexPrint then handles the resulting
document. Choose one copy in the source app. Its physical passes are simplex,
one copy per captured document. Multiple-copy/collation behavior still needs
verification and is not offered by the guided UI.

Capture accepts supported streams only, caps a final PDF at 512 MiB, closes
streams, and renames `.partial` to `.pdf` only after a complete successful write.
The desktop agent rejects protected/invalid PDFs before physical printing.
The app excludes its own virtual queue from printer selection and rejects it
again in the print worker to prevent recursive submissions.

## Build and package

Requires the normal app build tools plus Windows SDK 10.0.26100.0 or later:

```powershell
./scripts/Package-Installed.ps1 -Version 1.2.0
```

The default is self-contained. For an explicit development build using the
installed .NET 10 Desktop Runtime, add `-FrameworkDependent`. MakeAppx validates
the manifest and creates the archive; the script checks actual archive entries.
Compilation and packaging do not install the application or add a printer.

```text
DuplexPrint-v1.2.0-win-x64-installed-unsigned.msix
  AppxManifest.xml
  DuplexPrint.Capture.dll
  Config/PrinterCapabilities.xml
  Assets/*.png
  App/
    DuplexPrint.exe
    DuplexPrint.PdfWorker.exe
    application/.NET files, documentation, licenses, configuration examples
  [MSIX block map and package metadata]
```

No `portable.mode`, private `settings.json`, compiler outputs, signing keys or
external PDF-reader executable belongs in this package.

## Signing and installation

**Unsigned MSIX files cannot be treated as ordinary end-user installers.**
The certificate subject must exactly match the manifest Publisher, and the
signing chain must be trusted on the destination computer. The default
`CN=madarlevi` is a development identity, not proof of a verified publisher.
Set the real publisher explicitly when building with your certificate:

```powershell
# Supply this through your local secret management, not a committed file.
$env:DUPLEXPRINT_CERTIFICATE_PASSWORD = '<certificate password>'
./scripts/Package-Installed.ps1 -Version 1.2.0 `
    -Publisher '<exact certificate Subject>' -CertificatePath '<private signing.pfx>'
```

`-TimestampUrl` optionally uses your certificate provider's timestamp service.
The script signs and verifies the package; it does not import certificates.
For public use, choose an appropriate trusted signing/Store distribution path.
For development, review Microsoft's self-signed-package instructions and test
trust changes only in a disposable VM. Never publish a PFX or private key.

A signed, trusted package opens in Windows App Installer. Installation registers
the queue from the manifest; uninstalling the app removes the package and its
registration. This registration/removal behavior still needs local VM testing.
Microsoft Store distribution may require additional capability/publisher review.

## Persistence and recovery

Data belongs to the current user, below:

```text
%LOCALAPPDATA%/Packages/<package-family>/LocalState/
  settings.json
  settings.json.bak
  Inbox/                         pending captures; .partial is never watched
  History/Submitted/             job windows closed after submission
  History/Cancelled/             job windows closed before completion
  capture-error.txt              last capture/conversion/launch error, if any
```

Pending `.pdf` files are scanned at startup so the initial captured document
is not lost if it arrived before the agent started. A running job locks the
printer/profile for both passes. Other captured jobs wait in order. Restarting
after a crash can offer the interrupted file again: check the Windows queue and
already printed sheets before approving it. A capture submission is not proof
of physical completion.

History contains document contents and is kept until the user removes it.
Delete old history explicitly when no job is using it. If capture converted a
PDF but desktop launch failed, the committed PDF remains in Inbox; open
DuplexPrint from Start. Failed incomplete writes are deleted when possible;
OS termination can leave stale `.partial` files. Originals opened manually
are never moved or deleted. Uninstall can remove package-local data.

## Validation boundary and manual checks

Validated locally without installing a printer:

- C++ compile with warnings as errors, using only SDK/OS components.
- DLL exports, unknown-class rejection, correct WinRT activation, null-trigger
  rejection and reference-count/unload lifetime.
- MakeAppx manifest validation, package creation and archive-content inspection.
- Desktop page-order/settings/retry tests with a controlled submission function.

**Not yet validated end to end:** trusted installation, virtual-queue creation,
real Windows background trigger, OpenXPS conversion with a job print ticket,
desktop UI launch from the task, concurrent captures, uninstall,
and physical HP LaserJet 4250tn paper feeding. No real print jobs were sent.

Before promoting this edition beyond experimental, test in a disposable,
updated Windows 11 24H2+ x64 VM with a trusted development signature:

1. Install and check exactly one DuplexPrint queue appears.
2. Print from Notepad/Word (OpenXPS path) and a PDF app; both must use the OpenXPS path.
3. Confirm one captured PDF opens without Save As, with the desktop agent closed
   and already running; close/reopen Start to check background activation.
4. Submit two documents and verify one-at-a-time queuing and recovered pending jobs.
5. Test Unicode paths, multiple paper sizes, landscape, selected source page ranges
   and failure/cancellation; check the Windows event/print logs on errors.
6. Physically test 1–6 numbered pages on the **exact HP input/output trays**, checking
   pairings, orientation and the blank back of odd-page jobs. Do not automate this.
7. Verify history/recovery, a standard-user session, multiple Windows users,
   package update and uninstall/queue removal.

Until those checks pass, the portable edition remains the tested manual-entry
path. The release workflow intentionally marks releases as prereleases.

## Official references

- [Virtual-printer manifest specification](https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/msix-manifest-specification-print-support-virtual-printer)
- [Print Support App v4 design guide](https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/print-support-app-v4-design-guide)
- [Conversion and host-side print-ticket processing](https://learn.microsoft.com/en-us/windows-hardware/drivers/devapps/print-support-app-design-guide#pdl-conversion-with-host-based-processing-flags)
- [Windows 11 24H2 API requirement](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.printing.workflow.printworkflowvirtualprintertriggerdetails)
- [FullTrustProcessLauncher](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.fulltrustprocesslauncher)
- [Interactive services and session isolation](https://learn.microsoft.com/en-us/windows/win32/services/interactive-services)
- [MSIX signing requirements](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)
