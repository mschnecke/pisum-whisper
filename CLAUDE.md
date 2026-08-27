# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Status

Early scaffold: project configuration only, no source code yet. What exists:

- `Pisum.Whisper.slnx` — an empty solution in the XML `.slnx` format; no projects added yet.
  Add projects with `dotnet sln Pisum.Whisper.slnx add <path>`.
- `global.json` — pins the .NET SDK to `10.0.400`.
- `.idea/.idea.Pisum.Whisper/` — Rider project files; Rider is the IDE in use.
- `openspec/` — spec-driven change workflow (see below).

There is no buildable code, test suite, or lint configuration. **When the first project is added,
replace this section with real build/test/lint commands and an architecture overview.** Do not
leave this notice in place once the project has substance.

## What this is

Hotkey-driven dictation: hold a global hotkey to record speech, release to transcribe it via AI,
and the transcript is pasted at the cursor position. Nothing about *how* — audio capture, the
transcription backend, the tray/UI shell, the hotkey mechanism — has been decided yet. Confirm
with the user before scaffolding any of it.

## Stack

.NET on the `10.0.400` SDK, developed in JetBrains Rider on Windows. Target framework, UI
framework, and transcription backend are all still open.

## Spec-driven workflow (OpenSpec)

`openspec/config.yaml` sets `schema: spec-driven`. Change proposals live in `openspec/changes/`,
completed ones move to `openspec/changes/archive/`, and capability specs land in `openspec/specs/`
— all three are empty so far. Drive the workflow with the `/opsx:*` commands (`explore`, `propose`,
`apply`, `sync`, `archive`); the backing skills are in `.claude/skills/openspec-*`. Project context
and per-artifact rules can be filled in at the bottom of `openspec/config.yaml` (all commented out
today).

## Remote

`origin` is `git@github.pisum:mschnecke/pisum-whisper.git`. `github.pisum` is an SSH host alias for
github.com — this is a **GitHub** repo, so use the `gh` CLI (not `glab`) for PRs, issues, and CI.

## Code Intelligence

For any question about code inside this repo — how something works,
how X reaches Y, blast radius of a change, where a symbol is used —
call `codegraph_explore` first. Don't grep or re-read files for
these questions; the tool returns verbatim source, call paths
(including dynamic-dispatch hops), and blast radius in one call.

The server is registered in `mcp.json` at the repo root (`codegraph serve --mcp`); its index lives
in `.codegraph/`, which is git-ignored apart from that ignore file.

## Tool Preference: JetBrains MCP over built-in tools

When a JetBrains IDE MCP server is connected, ALWAYS prefer its tools over
built-in file/search tools for anything touching source files in this repo:

- Finding files → use `find_files_by_name_substring` / `search_in_files_by_text`
  instead of `Grep`/`Glob`/`find`/`grep` via Bash
- Reading file content → use `get_file_text_by_path` instead of `Read`
- Editing/replacing text → use `replace_text_in_file` / `replace_specific_text`
  instead of the built-in `Edit` tool or `sed`/`awk` via Bash
- Creating files → use `create_new_file_with_text` instead of `Write`

Rationale: the JetBrains tools operate on the IDE's live index (respects
.gitignore, refactoring-safe, triggers IDE-side formatting/inspections),
so results and edits stay consistent with what's open in Rider. Only fall
back to built-in tools if the MCP connection is unavailable or a JetBrains
tool call fails.
