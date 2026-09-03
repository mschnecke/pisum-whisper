## Context

See `proposal.md` — *Why*. What follows is the state this change starts from, measured rather than
assumed.

**There is no `.github/` directory.** Not an empty one — the repository has never had CI. Eleven
changes and five off-sequence ones have been merged on the strength of a local `dotnet build` and
`dotnet test`, and the one quality gate is `TreatWarningsAsErrors` in `Directory.Build.props`.

**There is no application icon.** `src/Pisum.Whisper.App/Assets/` holds twelve files, all of them
tray glyphs: three states × (`.png`, `Template.png`, `.win.svg`, `.mac.svg`). A tray glyph is a
16 pt monochrome silhouette drawn to survive being a template image; it is not an application icon,
and neither the MSI's Start-menu shortcut nor the `.app` bundle can be assembled without one.

**There is no version anywhere.** `Directory.Build.props` sets `TargetFramework`, `LangVersion`,
`Nullable`, `ImplicitUsings` and `TreatWarningsAsErrors`, and no `Version`. Every assembly is
therefore `1.0.0.0`.

**The publish output was measured on 2026-09-03, on this machine, at `10.0.400`:**

| | files | size | note |
|---|---|---|---|
| `osx-arm64`, self-contained, R2R | 266 | **132 MB** | `.pdb` total 192 KB |
| `win-x64`, self-contained, R2R | 270 | **228 MB** | `.pdb` total **100 MB**, in two files |

Both were produced *from macOS* — the `win-x64` cross-publish with ReadyToRun succeeded, which
matters only as a fallback, since D3 builds each on its own runner anyway.

The native assets that survive publish are the ones the risky dependencies carry:
`libuiohook.dylib` / `uiohook.dll` (SharpHook), `libminiaudio.dylib` / `miniaudio.dll` (SoundFlow),
`libAvaloniaNative.dylib` / `av_libglesv2.dll`, `libSkiaSharp` and `libHarfBuzzSharp`.

**The apphost is already ad-hoc signed, and its identity is wrong.** `codesign -dv` on the published
`osx-arm64` apphost reports:

```
Identifier=apphost
CodeDirectory v=20400 ... flags=0x2(adhoc)
Signature=adhoc
Info.plist=not bound
Sealed Resources=none
```

arm64 Mach-O binaries must carry at least an ad-hoc signature to execute at all, so the SDK applies
one. "Unsigned" in this change therefore never means *no signature* — it means *no Developer ID*.
`Identifier=apphost` and `Info.plist=not bound` are both defects this change fixes (D6).

**No signing material exists and none is being bought.** `security find-identity -v` on this machine
finds one identity, `localhost`. `gh secret list` on `mschnecke/pisum-whisper` is empty. Xcode is not
installed — only Command Line Tools, which do carry `notarytool`.

**The reference is the evidence for shipping unsigned.** `src-tauri/tauri.conf.json` has
`"signingIdentity": null`; `scripts/create-macos-pkg.sh` calls `pkgbuild`/`productbuild` with no
`--sign`; `scripts/postinstall` runs `xattr -rd com.apple.quarantine`. Its tap is on **v0.1.18** and
consumes that unsigned `.pkg` through a `pkg` stanza. `mschnecke/homebrew-pisum-whisper` exists with
one commit and a one-line README — a placeholder waiting for this change.

## Goals / Non-Goals

**Goals:**

- One command per platform that turns a clean checkout into an installer, runnable by a person and
  by CI without divergence — no step that exists only in a workflow file.
- Every artifact of a release carries one version, derived from the tag.
- The packaging is exercised on every pull request, not only at release time, so it cannot rot
  between releases.
- The costs of shipping unsigned are written where a user meets them, not only here.

**Non-Goals (design level, beyond the proposal's):**

- No installer UI beyond what WiX and `productbuild` give by default: no license page, no feature
  tree, no install-location choice.
- No per-user Windows install. D4 takes the per-machine route, and the two are not both supported.
- No `launchctl bootstrap` in the macOS postinstall. `MacOsAutostart` deliberately writes only a
  plist, and packaging must not quietly acquire a second mechanism for the same job.
- No self-contained *deployment* decisions revisited — framework-dependent is not evaluated, because
  requiring users to install a .NET runtime is a worse first-run than a large download.

## Decisions

### D1 — Self-contained and ReadyToRun; not single-file, not trimmed

`dotnet publish -c Release -r <rid> --self-contained true -p:PublishReadyToRun=true`.

*Not single-file.* `PublishSingleFile` with `IncludeNativeLibrariesForSelfExtract` extracts
`libuiohook`, `libminiaudio` and the Skia pair to a temporary directory on first run. On macOS that
puts the native code the application depends on outside the bundle whose signature is the thing
being reasoned about, and on both platforms it adds a path the application does not control to a
process that already has a startup-failure story it is careful about (`Core/Diagnostics/`). The MSI
harvests a directory as easily as a file and the `.app` wants its files in `Contents/MacOS`
regardless, so single-file buys nothing here.

*Not trimmed.* Avalonia 12.1 resolves controls and styles reflectively, and `TreatWarningsAsErrors`
is on solution-wide — so the IL2xxx trim warnings would be build errors, and silencing them is the
failure mode trimming is famous for. The 132 MB / 228 MB in *Context* is the price, and it is paid.

### D2 — Delete `libSkiaSharp.pdb` and `libHarfBuzzSharp.pdb`, keep the managed ones

Measured, on `win-x64`:

```
 80.1 MB  libSkiaSharp.pdb
 19.9 MB  libHarfBuzzSharp.pdb
  0.2 MB  Pisum.Whisper.{App,Core,Platform}.pdb
```

Two files are 100 MB of the 228, and they are native symbol files for third-party C++ that nobody
here will ever load into a debugger. Deleting them takes `win-x64` to ~128 MB, in line with
`osx-arm64`.

The three managed `.pdb`s stay. `-p:DebugType=none` would remove all five in one property and is
rejected for that reason: `DictationOrchestrator`'s catch-all and `StartupFailure.Describe` both log
exceptions, and a stack trace without line numbers from an installed build is exactly the report
that cannot be acted on. 0.2 MB is not a trade.

The deletion happens in the packaging script, after publish, so `dotnet publish` stays the ordinary
command and the removal is visible where the payload is assembled.

### D3 — Each RID builds on its own runner

`windows-latest` builds `win-x64` and the MSI; `macos-latest` builds `osx-arm64`, the bundle and the
`.pkg`. Not because cross-publish fails — it was measured working, see *Context* — but because
`wix`, `pkgbuild`, `productbuild`, `codesign`, `iconutil` and `xattr` are native tools on their own
platform, so a cross-building job would need every one of them replaced.

`macos-latest` is Apple Silicon, which is what makes `osx-arm64` a native R2R compile rather than a
cross one. This is an assumption about GitHub's runner images and is listed under *Risks*.

### D4 — Windows: WiX v6 as a .NET tool, per-machine, one `.wxs`

`dotnet tool install --global wix` (v6), invoked as `wix build`. WiX 5 introduced the `<Files>`
element, so the published directory is harvested with

```xml
<Files Include="publish\**" />
```

and there is no `heat`/HeatWave step and no generated component list to keep in sync — which is the
single largest source of MSI maintenance and the reason older WiX advice does not apply here.

*Per-machine*, into `ProgramFiles64Folder`, with the Start-menu shortcut in `ProgramMenuFolder`.
Per-user would avoid elevation, but Chocolatey installs elevated and drives the MSI with `/qn`,
which is the per-machine convention; the reference is per-machine for the same reason. Nothing in
the product is machine-wide either way — settings are `~/.pisum-whisper.json` and autostart is
`HKCU\...\Run` — so the install scope is a distribution decision, not a behavioural one.

The shortcut carries the AUMID:

```xml
<Shortcut ...>
  <ShortcutProperty Key="System.AppUserModel.ID" Value="net.pisum.whisper" />
</Shortcut>
```

**This satisfies no requirement, and the proposal's first draft was wrong to say it did.** Change 11
chose a drawn Avalonia window as the notification transport precisely so that it placed no
requirement on this change, and its `proposal.md` says so. What the AUMID does buy is that
`spikes -- notify`'s three observational questions — held open in the tree, unrun, for exactly this
moment — become answerable, alongside the WinForms balloon control run recorded in change 11's
`design.md`. That is a follow-up, not this change.

### D5 — macOS: assemble the bundle by hand, then `pkgbuild` + `productbuild`

There is no `dotnet` verb for a `.app`, so the script builds the layout:

```
Pisum Whisper.app/Contents/
  Info.plist
  MacOS/          <- the whole publish output, apphost included
  Resources/AppIcon.icns
```

`Info.plist` declares `CFBundleIdentifier = net.pisum.whisper` (matching `MacOsAutostart.Label`,
which is already that string — the bundle identity and the launch-agent label are then one fact),
`CFBundleExecutable = Pisum.Whisper.App`, `CFBundlePackageType = APPL`, `CFBundleIconFile =
AppIcon`, `CFBundleShortVersionString` and `CFBundleVersion` from the tag, `LSMinimumSystemVersion`,
`NSMicrophoneUsageDescription`, and `LSUIElement = true`.

`LSUIElement` and `MacOSPlatformOptions { ShowInDock = false }` in `Program.BuildAvaloniaApp` do the
same job by different means and both stay. The plist key is what the *operating system* reads before
the process starts; the Avalonia option is what governs a `dotnet run` that has no bundle at all.
Removing either breaks one of the two ways this application is launched.

Then `pkgbuild --root <staged> --install-location / --scripts <dir>` and `productbuild --package`,
neither with `--sign` — mirroring `scripts/create-macos-pkg.sh`, whose structure this follows
closely enough to be worth reading beside it.

**Windows/macOS difference worth stating plainly:** the Windows installer is a declarative `.wxs`
compiled by a tool that owns install, repair and uninstall. The macOS installer is a shell script
producing an archive plus a root-privileged postinstall. They are not symmetric and no attempt is
made to make them so.

### D6 — Ad-hoc re-sign the bundle with a stable identifier, and strip quarantine in postinstall

*Ad-hoc, not Developer ID.* The user's decision, and the reference's eighteen shipped releases are
the evidence it works. What is **not** accepted is leaving the signature as the SDK left it. After
the bundle is assembled:

```bash
codesign --force --deep --sign - \
  --identifier net.pisum.whisper \
  "Pisum Whisper.app"
```

This fixes both defects measured in *Context*: `Identifier=apphost` becomes the real bundle
identifier, and `Info.plist=not bound` / `Sealed Resources=none` become bound and sealed, so the
`LSUIElement` and microphone-purpose keys are covered by the signature rather than loose beside it.
It costs nothing and needs no certificate.

*Quarantine.* `scripts/postinstall`, run as root by `installer` (and therefore by Homebrew's `pkg`
stanza too):

```bash
xattr -rd com.apple.quarantine "/Applications/Pisum Whisper.app" 2>/dev/null || true
```

This is the step the proposal's first draft called papering over a symptom, now adopted with the
reason recorded. It is also **what makes the `.pkg` the only viable shape**: a `.dmg` or a zipped
`.app` has no root script, so unsigned distribution through either would put a right-click-Open or a
terminal command in front of every user. The artifact format and the signing decision are one
decision, not two.

The reference's postinstall does two further things — an `osascript` notification and an `open` of
the app as the console user. Neither is carried over: this application has its own first-launch flow
(`ShowFirstLaunch` opens the settings window and shows a welcome notification) and launching from a
root script as another user is a step with no benefit here.

### D7 — `packaging/`, not `packages/`

```
packaging/windows/Pisum.Whisper.wxs
packaging/windows/build-msi.ps1
packaging/macos/Info.plist.template
packaging/macos/build-app.sh
packaging/macos/build-pkg.sh
packaging/macos/postinstall
packaging/chocolatey/pisum-whisper.nuspec
packaging/chocolatey/tools/chocolateyinstall.ps1
packaging/chocolatey/tools/chocolateyuninstall.ps1
```

The reference calls this `packages/`. **`.gitignore` line `**/[Pp]ackages/*` would silently ignore
every file in it** — the directory would be created, populated, committed empty and the release
would fail with no diff to look at. `packaging/` is not matched by any rule in that file.

### D8 — One version, from the tag

`Directory.Build.props` gains `<Version>0.1.0</Version>` as the development default. `release.yml`
strips the `v` from the tag and passes `-p:Version=$VERSION` to publish, and the same value to the
WiX build, the `Info.plist` substitution, `pkgbuild --version` and the nuspec.

The reference's `workflow_dispatch` bump job — patch/minor/major, edit four files, commit, tag, push
— is **not** ported. It is the part of its release pipeline most tied to `package.json` and
`Cargo.toml`, so it is a rewrite rather than a port, and the proposal makes the hand-written tag a
non-goal.

### D9 — The test run carries one filter, and every skip explains itself

```bash
dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual
```

One filter, and it is the only one this change will ever need. Anything that cannot run on a
platform **declares itself skipped, with its reason, in the runner output** — which is what
`ManualTests.Enabled`, `WindowsOnly.Enabled` and (after `ready-the-suite-for-ci`)
`TimingTests.Enabled` are for. A name in a workflow file is invisible where it matters: a skip
prints its reason beside the test, a `--filter-not-method` prints nothing and silently drops the
count by one.

**An earlier draft of this design excluded the rotation latency test by name with
`--filter-not-method`, and that was wrong twice.** It
contradicted this change's own spec, which requires the run to exclude only tests that need a person
at the machine — and that test needs no person, only a quiet machine. And it hid the omission in the
place least likely to be read. `ready-the-suite-for-ci` moves the gate into the test instead, so
there is nothing here to exclude.

**The macOS leg is red on `main` today, and not because of packaging.** Measured 2026-09-03: 625
selected, 615 pass, **5 fail**, 5 skip — the five being `PresetsViewModelTests` and
`SettingsEditorTests` write-failure tests whose `FileShare.None` arrangement is Windows-only, plus
the rotation flake intermittently. `ready-the-suite-for-ci` fixes all of it and **blocks task group
5 of this change**; see that change's `design.md` for the `rename(2)` versus `MoveFileEx`
asymmetry behind it.

Expected counts once it has landed, and they differ per platform on purpose:

| | selected | skipped | passed |
|---|---|---|---|
| `windows-latest` | 625 | 1 (timing) | 624 |
| `macos-latest` | 625 | 6 (timing + 5 `WindowsAutostartTests`) | 619 |

### D10 — The application icon is drawn once and committed as `.ico` and `.icns`

Derived from `tray-idle.win.svg`'s geometry on a filled ground — the tray glyph is a silhouette
designed to be a template image and reads as nothing at 512 pt on its own.

Committed as built binaries beside a `.svg` source, matching `App/Assets/`'s existing precedent
exactly (`tray-idle.png` beside `tray-idle.win.svg`, with the `.svg` excluded from
`AvaloniaResource`). The alternative — generating `.icns` from SVG in CI — needs `iconutil` plus a
rasteriser on the runner and makes the icon a build output nobody has looked at. The icon is
reviewed by eye or not at all, so it is reviewed in the diff.

`.icns` is produced by `iconutil -c icns` from an `.iconset`; `.ico` is a multi-resolution file
produced once by hand. Neither tool runs in CI.

### D11 — Two workflows, and the package managers are downstream of a published release

`ci.yml` on `pull_request`: matrix over `windows-latest` and `macos-latest`; restore, build the
solution, run the tests as in D9, then build the platform's installer and upload it as a workflow
artifact. Building the installer on every PR is what stops the packaging rotting between releases,
and it is the requirement *Continuous integration proves the installers can still be built* exists
for.

`release.yml` on `push` of `v*`: build both, create the release, upload both, then fan out —
`repository_dispatch` to `mschnecke/homebrew-pisum-whisper` for the cask, and a `choco pack` +
`dotnet nuget push` to MyGet for Windows. Both fan-outs run only after both artifacts are published,
so the spec's "no release carrying only one platform" holds.

The tap repository gets `casks/pisum-whisper.rb` and an `update-cask.yml` that mirrors the
transcript tap's, including its `caveats` block — which is where D6's Accessibility re-grant is told
to macOS users at the moment they install.

### Rejected alternatives

- **`PublishSingleFile`** — extracts native libraries to a temp directory outside the signed bundle.
- **Trimming** — Avalonia's reflective control resolution, against solution-wide warnings-as-errors.
- **`-p:DebugType=none`** — removes the 0.2 MB of managed symbols along with the 100 MB of native
  ones, and the managed ones are what make a logged stack trace actionable.
- **MSIX** — needs a certificate the machine trusts and sideloading friction, to solve nothing the
  MSI does not.
- **Inno Setup / NSIS** — not an MSI, and Chocolatey's `Install-ChocolateyPackage` path here expects
  one.
- **Velopack / Squirrel** — built around auto-update, which is a stated non-goal.
- **`.dmg` or a zipped `.app`** — no root script, so no way to clear quarantine; see D6.
- **Developer ID and notarization** — a purchase, and the reference demonstrates unsigned ships.
- **A published `.zip` of the publish output and no installer** — no Start-menu entry, no uninstall,
  and on macOS it would leave the bundle wherever the user unzipped it, which matters because
  autostart registers `Environment.ProcessPath`.
- **Porting the reference's version-bump workflow** — tied to `package.json`/`Cargo.toml`; see D8.
- **Per-user Windows install** — avoids elevation but fights the Chocolatey convention; see D4.

## Risks / Trade-offs

- **The Accessibility grant is re-prompted on every update.** → Not mitigated; it is the accepted
  cost of D6. It is disclosed in `README.md` and in the cask's `caveats`, which is where the
  reference discloses it too. This is the single largest user-facing cost of this change and the one
  most likely to be regretted later; reversing it is buying a Developer ID and changing two scripts.
- **The download is 128–132 MB per platform.** → D1 and D2 take it as far as it goes without
  trimming. Self-contained is what makes "install and it works" true.
- **SmartScreen warns on the MSI until the download builds reputation.** → Documented in
  `README.md`. Nothing in the application is functionally gated on a Windows signature, unlike macOS.
- **`macos-latest` being Apple Silicon is an assumption about GitHub's runner images.** → If it ever
  regresses to x64, the `osx-arm64` publish becomes a cross-compile and the R2R step is what breaks
  first. Detected by the release build failing, not silently.
- **`FileLoggingRotationTests` flakes, and five App tests fail outright on macOS.** → Both were
  `ready-the-suite-for-ci`'s and it has landed: the rotation test is gated on `TimingTests`, the
  five write-failure tests are portable, and the macOS leg measured green on this branch on
  2026-09-03. What is left of the risk is the merge — D9's numbers are `main`'s. Neither was
  worked around here.
- **Three secrets do not exist**: `HOMEBREW_TAP_TOKEN`, `MYGET_API_KEY`, and whatever MyGet needs
  today. `gh secret list` is empty. → A prerequisite task, and the release workflow fails loudly on
  a missing one rather than skipping the step.
- **Two other repositories are touched.** `mschnecke/homebrew-pisum-whisper` gets its first real
  commit, and MyGet gets a feed. No change so far has left this repository. → The tap work is
  isolated in its own task group and its own pull request.
- **The postinstall runs as root.** → It is nine lines, does one `xattr`, and is in the diff. The
  reference's two extra behaviours are deliberately not carried over (D6).
- **`ManualTests.Enabled` covers four tests that CI can never run.** → Unchanged by this change, and
  it is why the *Artifact status* table in `ROADMAP.md` still lists ten open manual tasks. CI does
  not close them.

## Migration Plan

There is nothing installed to migrate from, so the "migration" is the first install and the way out
of it.

1. `packaging/` and the icon land first, verified locally on both platforms — a person runs the two
   scripts and installs the result. Nothing is published.
2. `ci.yml` lands second, on a pull request that proves it by running.
3. `release.yml` lands third and is proven with a **pre-release tag** (`v0.1.0-rc.1`) whose release
   is deleted afterwards. Publishing a real `v0.1.0` is a separate act.
4. The package-manager fan-out lands last, because it is the only part that writes outside this
   repository and the only part that cannot be rehearsed without a published release to point at.

**Rollback** at any step is deleting a workflow file: nothing in `src/` changes behaviour, and the
only source edits are `Directory.Build.props` gaining a `<Version>` and `App.csproj` gaining the
icon. A bad release is deleted from the Releases page; a bad cask is a revert in the tap.

## Open Questions

- **Does the installed bundle's Accessibility grant behave as D6 predicts?** The prediction — grant
  survives a relaunch, is lost on an update — follows from ad-hoc signing, but the only measurement
  in this project is `bootstrap-solution`'s task 1.4, which found the grant belonging to *Rider* for
  a `dotnet run` build and could not test anything outside that ancestry. An installed `.app`
  launched from Finder is the first binary this project has ever had outside it. Needs Apple
  Silicon. Does not change the specs or the approach either way — it changes what `README.md` says.
- **Does `NSMicrophoneUsageDescription` in a real bundle change what a refused microphone looks
  like?** `add-dictation-pipeline`'s task 6.4 has been open across two hardware sittings, once
  abandoned, and `DictationFailure.Describe` deliberately has no macOS "Microphone Access Required"
  branch because nobody has observed one. A bundle that owns its own TCC prompt is the first setup in
  which that case is reachable at all. Answering it belongs to change 8, not here; this change is
  what makes it answerable.
- **Is MyGet still the right Windows feed?** The reference pushes to
  `https://www.myget.org/F/mschnecke/api/v3/index.json`. Whether that account is still live is
  unverified, and the alternative — the Chocolatey community repository — adds moderation and a
  package-review turnaround. It changes one URL and one secret, not the design.
