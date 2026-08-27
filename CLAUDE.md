# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Status

This repository is an empty scaffold: one commit containing `README.md`, `LICENSE` (MIT), and
`.gitignore`. There is no source code, build system, or test suite yet.

**When code is added, replace this section with real build/test/lint commands and an architecture
overview.** Do not leave this notice in place once the project has substance.

## Intended stack

`.gitignore` is GitHub's `VisualStudio.gitignore`, so the project is expected to be .NET / Visual
Studio. Nothing else about the stack is decided — confirm with the user before scaffolding.

## Remote

`origin` is `git@github.pisum:mschnecke/pisum-whisper.git`. `github.pisum` is an SSH host alias for
github.com — this is a **GitHub** repo, so use the `gh` CLI (not `glab`) for PRs, issues, and CI.
