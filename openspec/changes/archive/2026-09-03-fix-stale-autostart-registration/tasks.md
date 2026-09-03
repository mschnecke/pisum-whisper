## 1. The registration read

- [x] 1.1 Add `Core/Autostart/AutostartRegistration.cs` — `Absent`, `Stale`, `Current` — and replace `IAutostartService.IsEnabled()` with `AutostartRegistration Read()`, documenting that `Current` means the registration is what `Enable` would write now. Verify: `dotnet build Pisum.Whisper.slnx` stays at 0 warnings once every caller is moved.
- [x] 1.2 `WindowsAutostart.Read`: `Absent` when the value is missing, `Current` when it equals the quoted `Environment.ProcessPath`, `Stale` otherwise — including a value of another registry type and a null `ProcessPath` (design D4), so that turning the setting off can still remove it. Factor the quoting into one method both `Read` and `Enable` use, or the two drift. Verify: covered by 3.2.
- [x] 1.3 `MacOsAutostart.Read`: `Absent` when the file is missing, `Current` when its whole text equals the plist `Enable` would write, `Stale` otherwise — no parser (design D2). Verify: covered by 3.3.

## 2. The reconciler

- [x] 2.1 `AutostartReconciler.Reconcile` treats `Current` as agreement when the setting is on and `Absent` when it is off; everything else is a write. A `Stale` registration is repointed with one `Enable` (design D3). Verify: covered by 3.1.
- [x] 2.2 The log line says `repointed at this executable` rather than `enabled` when the registration was `Stale`. Verify: covered by 3.1's second test.

## 3. Tests

- [x] 3.1 `AutostartReconcilerTests`: a stale registration is repointed; repointing is reported as something other than enabling; a stale registration is removed when the setting is off. Move `FakeAutostartService` to the three-valued state. Verify: **the first two fail without the fix** — measured, 2 of 11 failing with the boolean comparison restored — and all 11 pass with it.
- [x] 3.2 `WindowsAutostartTests`: a registration naming another executable is `Stale` and is repointed by `Enable` leaving one value; an *unquoted* registration of this very executable is `Stale`, because the quoting is what makes a path under Program Files launch at all. Verify: both are gated on `WindowsOnly` and report skipped with their reason off Windows.
- [x] 3.3 `MacOsAutostartTests`: an agent naming another executable is `Stale` and is repointed; a file that is not a plist at all is `Stale`. The stale agent is written out in the test rather than produced by the code under test, or it would agree with whatever that produced. Verify: both run on any operating system, as the rest of that class does.
- [x] 3.4 The suite stays green and the counts move by exactly the tests added. Verify: `dotnet test Pisum.Whisper.slnx --filter-not-trait Category=Manual` reports 632 selected, 624 passed, 8 skipped, 0 failed on macOS — 625/619/6 before, plus seven tests, two of them Windows-gated.

## 4. Documentation

- [x] 4.1 `CLAUDE.md`'s *Notifications and autostart*: `Read` replaces `IsEnabled`, what `Current` means, that the defect was real and reproduced, and the three consequences to keep — one `Enable` repoints, the log line differs, and the macOS comparison is of the whole file. Verify: written down beside the paragraph it corrects.
- [x] 4.2 `README.md`'s autostart paragraph says a registration naming a different executable is rewritten on the next launch. Verify: written.

## 5. Verification

- [x] 5.1 Reproduce and fix on hardware. Verify: on Apple Silicon, with `~/Library/LaunchAgents/net.pisum.whisper.plist` naming a Debug build, launch the packaged bundle and confirm the plist is repointed at it and the log reads `Start at login was repointed at this executable to match the setting.` **Ran 2026-09-03; both confirmed.**
- [ ] 5.2 The same on win-x64: with `HKCU\...\Run\Pisum Whisper` naming another path, launch and confirm the value is rewritten and the log line appears. Verify: by hand on the Windows machine; the two `WindowsAutostartTests` cases cover the implementation, this covers the reconcile.
