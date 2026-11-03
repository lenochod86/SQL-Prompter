# SQL Prompter

A custom IntelliSense extension for SSMS 22, intended to replace Redgate SQL Prompt. It will be released as open source under the MIT license.

These instructions apply in every environment: locally on Windows, in the cloud and from mobile. Work the same way everywhere.

## Language

- **Talk to the user in Czech.**
- **Everything that goes into the repository is in English:** code, identifiers, comments, UI text (menus, options, suggestion descriptions, messages), log messages, documentation, issues, pull requests, commit messages and labels.
- Existing Czech text in the repository (code comments, UI strings, `docs/`) is being translated; until that is done, write all new or changed text in English.

## Documents

- `docs/architecture.md`: architecture, decisions and verified facts about SSMS. Read it before designing or changing the architecture.
- `docs/roadmap.md`: phases with checkboxes and the user's decisions. Pick the next work from it.
- `README.md`: build, installation and manual testing in SSMS.

## Current state

MVP 0.2.x runs in SSMS 22.11. What is done and what comes next is in `docs/roadmap.md`; phase 0 is next.

## At the start of a session

1. Run `git status` and `git fetch`, and check the current branch. Never overwrite or delete uncommitted changes; ask first.
2. Read `docs/roadmap.md` and the open issues.
3. When continuing work on an issue, switch to its branch and bring it up to date with `main` (see below).

## Git and GitHub

Repository: `lenochod86/SQL-Prompter`, main branch `main`.

### Workflow for every piece of work

1. **Issue.** Every separate piece of work (a roadmap item or group of items, a bug, a research task) has a GitHub issue. Create it before writing code if it does not exist yet.
   - Title: short and descriptive, without the phase number (e.g. "Configurable commit keys for suggestions").
   - Body: goal, scope, acceptance criteria and a link to the item in `docs/roadmap.md`.
   - Labels: the kind (`feature`, `bug`, `research`, `docs`; create them if missing) and the phase (`phase-0`, `phase-1`, …).
2. **Branch** from the current `main`, named `<issue-number>-<short-description>`, lowercase with hyphens, e.g. `12-commit-keys`. One issue per branch.
3. **Commits** are small and self-contained.
   - Imperative subject line of at most 72 characters.
   - The body says why and what, and ends with `Refs #<number>`.
   - Before every commit the tests pass (`dotnet test tests/SqlPrompter.Tests`). If `src/SqlPrompter.Ssms` changed and you are on Windows, the VSIX builds too.
4. **Push** the branch regularly (`git push -u origin <branch>`) so the work can be picked up from another environment.
5. **Pull request** to `main` when the work is ready for review.
   - Describe what changed, how to test it in SSMS, and what is verified and what is not.
   - Include `Closes #<number>`.
6. **Approval.** Merge only after the user explicitly agrees. The user usually tests the VSIX from the branch in SSMS.
7. **Squash merge** the pull request into `main`.
   - The commit message is the PR title with the PR number; the body summarizes the change.
   - Then delete the remote and local branch and switch to the updated `main`.

### Rules

- Never commit or push directly to `main` unless the user explicitly says so.
- Never force push to `main`. On your own branch use only `git push --force-with-lease`, and only when necessary (e.g. after a rebase).
- Before handing a branch over for review, rebase it on `origin/main`. Resolve conflicts yourself and describe them.
- Do not rewrite history that someone may already have pulled unless it is necessary.
- Never put into the repository: passwords, connection strings, the user's server or database names, the user's snippets or exported settings, or anything from `E:\Prompter` (private material and proprietary SQL Prompt files). Tests use our own sample data with the same structure.
- Do not analyze or use SQL Prompt binaries. Exports and behavior descriptions are enough.
- Generated files (`bin/`, `obj/`, `.vs/`) stay in `.gitignore`.
- If GitHub is not accessible (no `gh` and no GitHub tools), tell the user. Never silently skip issues or pull requests.

## Architecture and code

- Logic belongs in `SqlPrompter.Core` and `SqlPrompter.Metadata` (netstandard2.0). These projects must not depend on the VS SDK or the SSMS API.
- `SqlPrompter.Ssms` stays a thin layer. Do not let SSMS types cross its boundary.
- Never do anything slow on the UI thread and never wait for the database while completing.
- Read metadata only. Never store passwords and do not add telemetry.
- The SSMS API is unsupported and undocumented. Verify assumptions at runtime in SSMS and record the results in `docs/architecture.md`.
- The user wants to see only our completion list. Our source therefore participates everywhere except in strings, comments and numbers, which suppresses the built-in list. Where we know nothing, offer an empty list (`ContextKind.Name`) or keywords (`ContextKind.Fallback`), never `DoesNotParticipateInCompletion`.
- New commands go into the SQL Prompter menu (`SqlPrompterCommands.vsct`).
- Cover every new context in `ContextAnalyzer` with a test that marks the caret with `|`. Every bug fix has a test that catches the bug.
- Follow the style of the surrounding code.

## Build and versions

- Tests: `dotnet test tests/SqlPrompter.Tests` (anywhere).
- VSIX: `dotnet build src/SqlPrompter.Ssms/SqlPrompter.Ssms.csproj -c Release` (Windows only). Output: `src/SqlPrompter.Ssms/bin/Release/net48/SqlPrompter.Ssms.vsix`.
- If `dotnet` is not on PATH, install the .NET SDK into the scratchpad with `dotnet-install.ps1` (no admin rights needed) and set `DOTNET_ROOT`.
- For every VSIX handed to the user, bump the version in `source.extension.vsixmanifest` (otherwise it will not install over the old one) and check the version inside the `.vsix`.
- After changing `.vsct`, bump the version in `ProvideMenuResource`.

## Environments

- **Locally on Windows:** the VSIX can be built and installed into SSMS 22. The user runs installation, uninstallation and SSMS configuration changes; give them the exact command.
- **In the cloud:** neither SSMS nor the VSIX build is available. Work on Core, Metadata and tests; CI builds the VSIX (`.github/workflows/build.yml`).
- **From mobile:** the user may not have SSMS at hand. Ask for SSMS results as a screenshot or the log (`%LOCALAPPDATA%\SqlPrompter\sqlprompter.log`).

## How to work

- If you do not know something, or it is the user's decision, ask. Do not make up SSMS or SQL Prompt behavior or the user's wishes.
- Do not mark anything about SSMS as verified until it has run in SSMS 22. Tick a checkbox in `docs/roadmap.md` only after verification, on the issue's branch.
- Report results precisely: what passed, what failed (with output) and what was not tested.
- Record the user's decisions in `docs/roadmap.md` (Decisions section) and verified SSMS facts in `docs/architecture.md`.
