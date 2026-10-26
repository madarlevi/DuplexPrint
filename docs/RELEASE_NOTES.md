Guided manual duplex printing with a redesigned square-edged interface, editable
settings.json, printer profile import/export and the original HP LaserJet 4250tn
page order. No separate PDF reader is needed.

- **Portable ZIP:** extract the complete folder and launch DuplexPrint.exe.
- **Installed MSIX (experimental):** Windows 11 24H2 or later; adds a DuplexPrint
  virtual print queue. A filename containing `unsigned` is a development artifact
  and requires proper publisher signing before ordinary installation.
- **Pre-release validation:** native capture activation and packaging are tested;
  installed queue creation, OS XPS→PDF conversion/UI handoff and physical HP paper
  reloading still require supervised Windows/printer testing. No unattended print
  jobs were used during development tests.

Wait for all first-pass sheets to leave the printer before reloading the stack
and clicking the second-pass button. Read the README and docs/INSTALLED_EDITION.md
before installing the experimental edition.
