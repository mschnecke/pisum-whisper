# packaging

Everything that turns a build into something a person can install. Nothing here is compiled into the
application or read at runtime; `dotnet build` and `dotnet test` never look in this directory.

| Path | |
|---|---|
| `icon/` | the application icon — `app-icon.svg` is the source, `AppIcon.icns` (macOS) and `app-icon.ico` (Windows) are exported from it by hand and committed as binaries |
| `macos/` | `build-app.sh` assembles `Pisum Whisper.app`, `build-pkg.sh` wraps it in a `.pkg`, `Info.plist.template` is the bundle's plist before version substitution, and `postinstall` is the root script the installer runs |
| `windows/` | `Pisum.Whisper.wxs` is the WiX v6 source for the MSI and `build-msi.ps1` publishes and compiles it. The `.wxs` harvests `publish\**`, which resolves relative to the `.wxs` itself, so the payload is published into `windows/publish/` — kept untracked by the `.gitignore` beside it |
| `chocolatey/` | the Chocolatey package — `pisum-whisper.nuspec` and the `tools/` install and uninstall scripts, which download the released MSI rather than carrying it |

Each script takes the version as its one argument and is the same command a person and a workflow
run, so nothing about a release exists only inside `.github/workflows/`.

**This directory is `packaging/`, not `packages/`, and the name is load-bearing.** `.gitignore` line
156 is `**/[Pp]ackages/*`, so the reference project's name for this directory would leave every file
in it silently untracked — created, populated, committed empty, and the release failing with no diff
to look at. Confirm with `git check-ignore -v packaging/windows/x`, which prints nothing.

**Both platforms ship unsigned, by decision** — see the change's `design.md`, D6. That is why the
macOS `postinstall` strips the quarantine attribute: it is what keeps installation to one step
without a Developer ID, not a workaround for a signing step that was forgotten.
