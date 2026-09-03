# packaging

Everything that turns a build into something a person can install. Nothing here is compiled into the
application or read at runtime; `dotnet build` and `dotnet test` never look in this directory.

| Path | |
|---|---|
| `icon/` | the application icon — `app-icon.svg` is the source, `AppIcon.icns` (macOS) and `app-icon.ico` (Windows) are exported from it by hand and committed as binaries |
| `macos/` | `build-app.sh` assembles `Pisum Whisper.app`, `build-pkg.sh` wraps it in a `.pkg`, `Info.plist.template` is the bundle's plist before version substitution, and `postinstall` is the root script the installer runs |
| `windows/` | `Pisum.Whisper.wxs` is the WiX v6 source for the MSI and `build-msi.ps1` publishes and compiles it. The `.wxs` harvests the published directory, which `build-msi.ps1` passes as an absolute `-define` — WiX resolves a relative pattern against the *current directory*, not the `.wxs` — so the payload is published into `windows/publish/`, kept untracked by the `.gitignore` beside it |
| `chocolatey/` | the Chocolatey package — `pisum-whisper.nuspec` and the `tools/` install and uninstall scripts, which download the released MSI rather than carrying it |
| `bump-version.sh` | decides the version the *next* release will carry and writes it into `Directory.Build.props` — the one script here that runs before a build rather than after one |

Each script takes the version as its one argument and is the same command a person and a workflow
run, so nothing about a release exists only inside `.github/workflows/`. `bump-version.sh` is that
rule applied to the one step that used to be done by hand:

```bash
./packaging/bump-version.sh patch        # 0.1.0 -> 0.1.1, and prints 0.1.1
./packaging/bump-version.sh minor        # 0.1.0 -> 0.2.0
./packaging/bump-version.sh 0.2.0-rc.1   # an exact version, for a pre-release
```

It touches git not at all — the edit is one line that `git diff` shows and `git checkout` undoes —
and prints the new version on stdout with everything else on stderr, so `VERSION=$(...)` is the
whole of the calling contract. **A keyword bump from a pre-release resolves to its release core**:
`0.2.0-rc.1` was a rehearsal for `0.2.0`, so `patch` there gives `0.2.0` rather than skipping to
`0.2.1`. Cutting a further pre-release means naming it exactly.

Running `Release` from the Actions tab does exactly this and then commits, tags and pushes, which is
the whole difference between the two ways of starting a release; pushing a `v*` tag by hand still
works and skips the bump job. **The dispatch run carries on to build and publish itself** rather
than leaving the pushed tag to start a second run — a push made with `GITHUB_TOKEN` raises no
workflow event, so a bump job that only tagged would leave a tag and no release.

**This directory is `packaging/`, not `packages/`, and the name is load-bearing.** `.gitignore` line
156 is `**/[Pp]ackages/*`, so the reference project's name for this directory would leave every file
in it silently untracked — created, populated, committed empty, and the release failing with no diff
to look at. Confirm with `git check-ignore -v packaging/windows/x`, which prints nothing.

**Both platforms ship unsigned, by decision** — see the change's `design.md`, D6. That is why the
macOS `postinstall` strips the quarantine attribute: it is what keeps installation to one step
without a Developer ID, not a workaround for a signing step that was forgotten.
