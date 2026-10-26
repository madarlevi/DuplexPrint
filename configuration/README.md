# Printer settings and profiles

`settings.example.json` is a template, not a machine-specific preference file.
The settings window shows the exact active `settings.json` path.

- Portable ZIP: next to `DuplexPrint.exe` (the `portable.mode` marker selects this).
- Installed MSIX: `%LOCALAPPDATA%\Packages\<package-family>\LocalState\settings.json`.
- Unpackaged development: `%LOCALAPPDATA%\DuplexPrint\settings.json`.

Use UTF-8. To edit manually, close the app, copy the example to the active path
if needed, edit it, and restart. Alternatively use **JSON szerkesztése** and
**Újraolvasás** inside settings, then save. Saving keeps the previous file as
`settings.json.bak`. Malformed/unsupported files load safe HP defaults and show
a warning without silently replacing the original. A successful print attempt
only remembers the selected printer when the existing settings file was valid.

`profile` defines ascending/descending orders independently and a back rotation
of 0° or 180°. The initial HP LaserJet 4250tn profile preserves the author's
original ascending/ascending workflow. It has not been physically revalidated
for a specified tray. A profile is locked for both passes once printing begins.

Import/export works on the profile object alone; see `profiles/` and
`profile.schema.json`. The app does not fetch profiles from the internet.
Personal `settings.json`, backups and captured documents are ignored by Git.
