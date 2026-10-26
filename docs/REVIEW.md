# Review and validation — 2026-10-26

## Scope and audit

Reviewed the WPF C#/XAML, original first-version instructions, project/solution,
native PDF helper, dependency locks, printer/layout code, queue/startup handling,
tests, packaging, notices and workflows. The baseline had manual duplex,
ascending odd fronts, ascending or reversed backs, one copy per simplex pass,
queued folder input and manual file selection. There was no automatic hardware
duplex feature.

The supplied printer model is **HP LaserJet 4250tn**; the input/output trays were
not specified. The default profile preserves the original ascending/ascending
orders and the original paper-reloading direction, with clearer Hungarian text.
Physical correctness on an unspecified tray cannot be established by software tests.

No root Git repository was present. No source history was rewritten, no remote
was modified and no release was published. Ignored snapshots under
`artifacts/original/`, `native-migration-baseline/` and `redesign-baseline/`
retain earlier working sources. Existing source files were not relocated.

## Changes and confirmed fixes

- Replaced the external PDF viewer with Windows.Data.Pdf rendering in an original
  C++/WinRT helper, plus PrintDocument in an isolated .NET worker.
- Fixed blank backs losing CropBox geometry, UTF-8 worker errors, unsafe or fragile
  process/path handling, snapshot/resource/temp ownership and failure advancement.
- Added the square-edged WPF design, visible two-pass actions, PDF drop support
  and a settings editor without changing the manual confirmation workflow.
- Added strict, bounded, versioned JSON settings; atomic save and backup; safe
  profile import/export, independent front/back order, back rotation and custom
  reloading instructions. Invalid preferences are retained until explicit repair,
  including if a print job succeeds using safe defaults.
- Verified first-pass completion waits for another click; duplicate clicks do not
  submit another pass, and second-pass retry does not reprint fronts.
- Prevented recursive printing into the application's own capture queue in both
  UI selection and the production worker.
- Fixed inbox activation errors escaping the dispatcher, first-capture recovery
  when the installed agent was not running, and reopening an existing hidden agent.
  Packaged captures archive only their own inputs; manual PDFs remain intact.
- Implemented an **experimental installed edition** using an OS-hosted per-user
  WinRT print task, native XPS→PDF API calls, atomic package-inbox delivery and
  desktop-agent launch. It does not create an interactive LocalSystem service.
- Prepared an MSIX manifest/PDC, original generated brand assets, optional signing,
  portable packaging, CI and tag-triggered **prerelease** workflow. No installer,
  certificate trust change or print job was executed here.
- Prepared a separately publishable profile repository under ignored
  `printer-profiles-repo/`, with HP JSON, schema, MIT license, README, contribution
  instructions and a Python standard-library validation workflow.

## Local results

| Check | Result |
| --- | --- |
| Locked NuGet restore from the existing package cache | Passed |
| Debug build, warnings as errors | Passed; zero warnings/errors |
| Release build, warnings as errors | Passed; zero warnings/errors |
| Debug regression harness | **71 passed, 0 failed** |
| Release regression harness | **71 passed, 0 failed** |
| Original orders, both back directions, 1–9 pages | Passed |
| All profile order combinations, 1–12 pages, blank positions and 180° backs | Passed |
| Actual Windows PDF rendering: order/colors, crop/rotation, text/vector/image/alpha, page sizes | Passed |
| 600-DPI A3, dimensions/memory limits, invalid/protected PDFs and cleanup | Passed |
| UI two-click boundary, duplicate clicks, locked options, back-only retry | Passed with an injected submission function; no real print job |
| Strict settings/backup/import, malformed/null/duplicate/unknown/oversized JSON | Passed |
| Unicode/spaces, safe child arguments, failures/timeouts/cancellation and source preservation | Passed |
| Native capture DLL compile and activation/lifetime tests | Passed; no OS print trigger |
| Profile repository Python validator | Passed for the included HP profile |
| Genuine WPF client-area captures | Two PNGs captured and visually inspected |
| PowerShell scripts | Syntax parsed successfully |
| Windows x64 framework-dependent ZIP | Created and inspected; requires .NET 10 Desktop Runtime |
| Experimental framework-dependent unsigned MSIX | MakeAppx validation and archive inspection passed |
| Self-contained publish | Not locally validated: runtime packs unavailable through restricted NuGet access |
| GitHub Actions / tag publication / signing | Prepared, not executed |
| Virtual queue installation and real capture→convert→UI handoff | Not executed |
| Physical printing | Not executed |

The harness really renders PDFs through Windows. UI pass-transition tests replace
only the printer submission function; they do **not** prove driver submission or
paper transport. The production print worker is tested only with a deliberately
nonexistent printer or the prohibited capture-queue name.

Artifacts and complete logs remain under ignored `artifacts/`. The packages
are named `DuplexPrint-v1.2.0-win-x64-framework-dependent.zip` and
`DuplexPrint-v1.2.0-win-x64-installed-framework-dependent-unsigned.msix`, each
with a SHA-256 sidecar. A filename suffix states the runtime/signature limitation.
The ZIP includes the app, native renderer, configuration templates, genuine
screenshots and notices; it contains no SumatraPDF, private settings, compiler
intermediates or debug symbols. MSIX contents additionally include the original
capture DLL, PDC, manifest and brand assets, without the portable marker.

Local SDK validation redirects writable user configuration to ignored paths,
uses the existing package cache and an explicit Windows platform moniker to
avoid a sandbox-restricted SDK lookup. Online audit/runtime-pack downloads were
disabled only for those local commands; source and CI do not suppress them.

## Remaining release checks

1. Run the prepared Windows CI with network access. Confirm the self-contained
   runtime files **and their exact license/third-party notices** are present.
   Complete an online dependency vulnerability audit.
2. Choose an appropriate package publisher/signing method. The default development
   identity `CN=madarlevi` and an unsigned artifact do not establish trusted
   installability. No private signing material is included.
3. In an updated Windows 11 24H2+ VM, verify queue installation/removal,
   real task activation, OpenXPS conversion with print tickets,
   first-launch/already-running UI handoff, multiple jobs and recovery.
4. Physically verify **the exact HP LaserJet 4250tn trays** with 1–6 numbered
   pages: pairing, reload direction, blank backs, orientation, quality/scaling
   and Windows queue errors/cancellation. Never run these unattended.
5. Review fine-detail/color-critical and representative user PDFs before promoting
   the OS raster-rendering path as production-ready for those documents.

The previous viewer fallback is retained only in ignored local snapshots, not
active shipping dependencies. If physical comparison reveals a critical rendering
regression, use that baseline while fixing/reassessing the engine; do not claim
universal equivalence from build success.

## Licensing and publication

Original code/assets/profiles use MIT under **Madár Levente (madarlevi)**.
PDFsharp remains pinned to MIT-licensed 6.2.4. C++/WinRT, Microsoft STL,
Microsoft Extensions and runtime notices are retained. OS PDF/print components
are not redistributed. Static MSVC runtime/toolchain terms still apply; the
license review is not a blanket legal guarantee.

README descriptions and actual screenshot references were checked. Personal
settings, generated documents, binaries, archives and keys are ignored. The
separate profile starter is intentionally excluded from the main repository.
The root is ready for the maintainer's Git initialization or copying into an
existing clone, as described in [the Hungarian guide](GITHUB_HU.md).
