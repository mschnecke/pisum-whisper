## Why

Avalonia ships first-party headless test integration for **xUnit and NUnit only** — there is no
`Avalonia.Headless.MSTest` and never has been. Change 10 is the settings window, so the question is
not whether to leave MSTest but when: before change 10, or hand-roll the plumbing
`Avalonia.Headless.XUnit` packages as `[AvaloniaFact]`. That makes this a roadmap dependency, not
housekeeping, and it is why the version is pinned to what Avalonia is built against (design.md D1).

Change 9's tray icon needs no test-framework decision, so now is the smaller pass. No behavioural
reference exists: `W:\github-pisum-transcript` is Rust and is silent on the test harness.

## What Changes

- Replace `MSTest` and `Microsoft.NET.Test.Sdk` in both test projects with one `xunit.v3` 3.2.2
  reference. 3.2.2, not the current 4.0.0, because it is what `Avalonia.Headless.XUnit` 12.1.1 is
  built against; the mismatch resolves silently and would surface at change 10.
- Opt `dotnet test` into Microsoft.Testing.Platform through `global.json`. **BREAKING for developer
  workflow**: `dotnet test --filter <expr>` is VSTest syntax and stops working. Archived tasks
  quoting it are historical records, not rewritten.
- Rewrite the MSTest attributes across 53 classes and 350 test methods — `[TestClass]` deleted,
  lifecycle methods becoming a constructor and `Dispose`; design.md D3 carries the full mapping.
- Adopt xUnit's parallel-by-collection default rather than opting back out to MSTest's sequential
  execution (design.md D6 audits what that touches).
- Fix the code xUnit's analysers flag — blocking task waits, cancellation-less delays — rather than
  suppressing the diagnostics, so warnings-as-errors keeps its meaning.
- Update `README.md`, `openspec/config.yaml` and `CLAUDE.md`, which name MSTest or its CLI.

## Non-goals

- No changes to `src/`. Not one production file is touched.
- No new tests, no deleted tests, no changed assertions. Shouldly, FakeItEasy and `SharpHook.Testing`
  all stay.
- No CI wiring. That is change 12's job.
- No xUnit fixtures (`IClassFixture`, `IAsyncLifetime`): a constructor and `Dispose` reproduce
  today's per-test lifecycle exactly.
- **No Avalonia headless tests.** This change only makes them possible; writing them is change 10's
  work. It adds no `Avalonia.Headless.XUnit` reference, only proof one would resolve and run.

## Capabilities

### New Capabilities

None — tooling. It changes how tests run, not what the app does; `.openspec.yaml` sets
`skip_specs: true`.

### Modified Capabilities

None. No requirement in the eight synced specs changes; their tests must pass unchanged in meaning.

## Impact

- `Directory.Packages.props`, `global.json`, both test `.csproj` files, 61 test source files.
- `README.md`, `openspec/config.yaml`, `CLAUDE.md`, `openspec/ROADMAP.md`.
- Rider's test runner, which must discover MTP tests.
