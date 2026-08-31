## Why

The two test projects run on MSTest 4 over VSTest, which is the one part of the stack that was
picked by default rather than chosen. xUnit.net v3 is where the .NET test ecosystem has landed: it
is Microsoft.Testing.Platform-native, so the test assembly becomes a self-executing binary instead
of something hosted by `vstest.console`, and it brings first-party analysers the existing
warnings-as-errors gate can enforce. Now it is one mechanical pass over 372 tests; after changes 9
through 12 it is the same pass over a much larger suite.

There is no behavioural reference here: `W:\github-pisum-transcript` is a Rust project and
specifies nothing about the .NET test harness.

## What Changes

- Replace the `MSTest` and `Microsoft.NET.Test.Sdk` package references in both test projects with a
  single `xunit.v3` reference. Version pinned in `Directory.Packages.props` like every other package.
- Opt `dotnet test` into Microsoft.Testing.Platform through `global.json`.
  **BREAKING for developer workflow**: `dotnet test --filter <expr>` is VSTest syntax and stops
  working; MTP takes `--filter-query` / `--filter-uid`. Archived tasks that quote the old syntax are
  historical records and are not rewritten.
- Rewrite the MSTest attributes across 53 classes: `[TestClass]` deleted, `[TestMethod]` to `[Fact]`
  or `[Theory]`, `[DataRow]` to `[InlineData]`, `[TestInitialize]` to a constructor, `[TestCleanup]`
  to `IDisposable.Dispose`, `[Ignore]` to `Skip`.
- Adopt xUnit's parallel-by-collection default rather than opting back out to MSTest's sequential
  execution.
- Fix the code that xUnit's analysers flag — blocking waits on tasks and cancellation-less delays —
  rather than suppressing the diagnostics, so warnings-as-errors keeps its meaning.
- Update `README.md` and `openspec/config.yaml`, both of which name MSTest.

## Non-goals

- No changes to `src/`. Not one production file is touched.
- No new tests, no deleted tests, no changed assertions. Shouldly stays; FakeItEasy stays;
  `SharpHook.Testing` stays.
- No CI wiring. That is change 12's job.
- No move to xUnit fixtures (`IClassFixture`, `IAsyncLifetime`): a constructor and `Dispose`
  reproduce today's per-test lifecycle exactly.

## Capabilities

### New Capabilities

None. This is tooling: it changes how the tests run, not what the application does.
`.openspec.yaml` sets `skip_specs: true`.

### Modified Capabilities

None. No requirement in any of the eight synced specs changes; every one of their tests must pass
unchanged in meaning after the migration.

## Impact

- `Directory.Packages.props`, `global.json`, both test `.csproj` files.
- 64 test source files; 350 test methods across both test projects.
- `README.md`, `openspec/config.yaml`, `CLAUDE.md`.
- Rider's test runner, which must discover MTP tests.
