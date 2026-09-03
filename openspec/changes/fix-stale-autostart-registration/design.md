## Context

`AutostartReconciler` has been in the tree since change 11 and its shape is sound: it reads before it
writes, it writes only on a mismatch, and a failure never stops the application from starting. The
defect is entirely in what it reads.

```csharp
if (_autostart.IsEnabled() == wanted)
{
    return;
}
```

`IsEnabled()` is `key?.GetValue(ValueName) is not null` on Windows and `File.Exists(PlistPath)` on
macOS. Both answer *is there a registration*, and neither answers *is it ours*. Measured on Apple
Silicon on 2026-09-03: with the agent naming a Debug build and the packaged bundle running, the
reconciler read `true`, compared it with `StartWithSystem = true`, and returned — no write, no log
line, nothing to notice.

The `autostart` spec is already on the other side of this. *Autostart is enabled* says the
registration "exists and names the running executable". Only the reconcile requirement's notion of
agreement is thinner than the enable requirement's notion of correctness, which is exactly the gap a
delta closes.

## Goals / Non-Goals

**Goals:**

- A registration that will launch the wrong thing is corrected on the next launch or save.
- The reconciler still writes nothing, and logs nothing, when the registration is already right.
- One read per reconcile, as before.
- The correction is legible in the log as something other than a first-time enable.

**Non-Goals:**

- No new mechanism on either platform: still one `Run` value, still one plist, still no `launchctl`.
- No attempt to find or repair registrations under other names.
- No change to the `startWithSystem` setting, its default, or the General tab.

## Decisions

### D1 — Three states, not a boolean and not two predicates

`AutostartRegistration` is `Absent`, `Stale`, `Current`, returned by `IAutostartService.Read()`.

The reconciler must distinguish exactly three cases — write nothing, create, correct — so a type with
three values is the one that fits. The alternatives were both worse:

- **Keeping `IsEnabled()` and adding `PointsAtThisExecutable()`.** Two reads on the common path, two
  overlapping predicates whose combinations include one that cannot occur, and a second method that
  has no meaningful answer when nothing is registered.
- **Making `IsEnabled()` mean "enabled and current".** The name would then be a lie in the one case
  that matters, and `Disable` would have nothing correct to test against: a stale registration is
  still a registration, and turning the setting off must remove it.

`IsEnabled()` had exactly one production caller. Replacing it is a smaller change than living beside
it.

### D2 — `Current` means "byte-for-byte what `Enable` would write"

Not "the path matches after normalisation". Each implementation compares against the exact string it
would write:

| | compared |
|---|---|
| Windows | the `Run` value against `$"\"{Environment.ProcessPath}\""` |
| macOS | the plist file's whole text against `Plist(Environment.ProcessPath)` |

Two things follow, and both are wanted. **An unquoted Windows registration of this very executable is
`Stale`** — correctly, because the quoting is not cosmetic: without it a path under *Program Files*
is read as a command and its first space as an argument separator, so such an entry is one that will
not launch. And **a macOS plist that differs only cosmetically is `Stale`** and is rewritten into the
canonical one, which is harmless — `Enable` overwrites — and happens once, because the comparison is
stable from then on.

Comparing the whole macOS file rather than parsing out `ProgramArguments` is the same decision made
once more: it needs no parser, it cannot be defeated by a plist that does not parse, and it catches
label and format drift as well as a wrong path. The test that asserts a file which is not a plist at
all reads as `Stale` is there to say so.

### D3 — `Stale` is repointed with one `Enable`, not a `Disable` and an `Enable`

`SetValue` replaces and `File.WriteAllText` overwrites, so both implementations already land on a
single correct entry from either starting state. A delete-then-create would leave a window in which
the user has no registration at all, for no benefit.

### D4 — A null `ProcessPath` reads as `Stale` rather than throwing

`Environment.ProcessPath` is null only for a host that cannot name its own executable. `Enable`
already treats that as fatal, and rightly. But `Read` must not: if it threw, a process that cannot
name itself could no longer *un*register either, and turning the setting off would fail on a machine
where it used to work. `Stale` keeps `Disable` reachable and lets `Enable` raise the descriptive
`AutostartException` it already has.

### Rejected alternatives

- **Re-registering on every reconcile.** One line, and it deletes the property the reconciler was
  built around — `GlobalHotkeyService.OnSettingsChanged`'s misleading rebind line is the mistake it
  was written not to repeat.
- **Comparing only the executable path on macOS, via `XDocument`.** A parser, an exception path for a
  plist that does not parse, and blind to label or format drift.
- **`OrdinalIgnoreCase` on Windows.** It would avoid one rewrite when some other tool has changed the
  case of the path, at the cost of a comparison that no longer means "what `Enable` writes". The
  rewrite it avoids is idempotent and invisible.

## Risks / Trade-offs

- **One extra rewrite on the first launch after this ships**, for any user whose registration differs
  cosmetically from the canonical form. → Harmless and once; the log line says what happened.
- **The macOS comparison reads the whole plist rather than calling `File.Exists`.** → It is under a
  kilobyte, once per save.
- **`Read` is a wider contract than `IsEnabled` was**, so a future implementation has more to get
  right. → It is also the contract the spec already described; the two native implementations and
  their tests are the whole surface.

## Migration Plan

None. There is no stored state to migrate: the first reconcile after the update either finds the
registration correct and does nothing, or finds it stale and corrects it.

## Open Questions

- **Does a Windows registration survive an MSI upgrade at the same path?** If the installer keeps the
  install location across versions the value never goes stale there, and this fix is exercised only by
  a developer build, a relocation or a changed install location. It changes nothing about the fix;
  change 12's task 9.4 is where it would be observed.
