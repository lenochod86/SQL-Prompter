# SQL Prompter

A custom IntelliSense extension for SSMS 22, intended to replace Redgate SQL Prompt. It will be released as open source under the MIT license.

These instructions apply in every environment: locally on Windows, in the cloud and from mobile. Work the same way everywhere.

## Language

- **Talk to the user in Czech.**
- **Everything that goes into the repository is in English:** code, identifiers, comments, UI text (menus, options, suggestion descriptions, messages), log messages, documentation, issues, pull requests, commit messages and labels.
- Test data and SQL keywords in tests may contain other languages only when a test needs them (e.g. non-ASCII identifiers).

## Documents

- `docs/architecture.md`: architecture, decisions and verified facts about SSMS. Read it before designing or changing the architecture.
- `docs/decisions.md`: the user's earlier decisions. Work is planned only from the GitHub issues (there is no roadmap file).
- `README.md`: build, installation and manual testing in SSMS.

## Current state

MVP 0.2.x runs in SSMS 22.11. What is done and what comes next is in the GitHub issues (labels `phase-N` give the area).

## At the start of a session

1. Run `git status` and `git fetch`, and check the current branch. Never overwrite or delete uncommitted changes; ask first.
2. Read the open issues.
   - Work only on issues assigned to `claude-lenochod86`. Do not start an issue assigned to the user or to nobody; if one seems to be next, ask the user (and comment on the issue) instead.
   - Work through those issues in ascending order of their number, unless the user says otherwise.
   - If an issue has sub-issues, finish all of them before sending the pull request, unless the user agrees on something else.
3. When continuing work on an issue, switch to its branch and bring it up to date with `main` (see below).

## Git and GitHub

Repository: `lenochod86/SQL-Prompter`, main branch `main`.

Claude works on GitHub as its own account, `claude-lenochod86` (a collaborator with write access), so the user can tell their own actions from Claude's. The token is in the `GH_TOKEN` environment variable and commits use that account as the author (local `git config`). Do not switch to the user's account, do not change the git author, and never write the token anywhere. If `gh api user` does not return `claude-lenochod86`, stop and tell the user.

### Workflow for every piece of work

1. **Issue.** Every separate piece of work (a feature or group of features, a bug, a research task) has a GitHub issue. Create it before writing code if it does not exist yet.
   - Title: short and descriptive, without the phase number (e.g. "Configurable commit keys for suggestions").
   - Body: goal, scope and acceptance criteria.
   - Labels: the kind (`enhancement`, `bug`, `documentation`, `research`) and the phase (`phase-0`, `phase-1`, …). Create a label if it is missing.
   - Use `gh` (on Windows `C:\Program Files\GitHub CLI\gh.exe` if it is not on PATH) or the GitHub tools of the environment.
2. **Branch** from the current `main`, named `<issue-number>-<short-description>`, lowercase with hyphens, e.g. `12-commit-keys`. One issue per branch.
3. **Commits** are small and self-contained.
   - Imperative subject line of at most 72 characters.
   - The body says why and what, and ends with `Refs #<number>`.
   - Before every commit the tests pass (`dotnet test tests/SqlPrompter.Tests`). If `src/SqlPrompter.Ssms` changed and you are on Windows, the VSIX builds too.
4. **Push** the branch (`git push -u origin <branch>`) so the work can be picked up from another environment. Before a push run `pwsh .github/scripts/Verify-Local.ps1` (what CI runs: the tests, the version checks against `origin/main` and, on Windows, the VSIX build with warnings as errors; `-SkipVsix` when `src/SqlPrompter.Ssms` did not change): a red push wastes billed Actions minutes. Push a branch as few times as possible.
5. **Pull request** to `main` when the work is ready for review.
   - Describe what changed, how to test it in SSMS, and what is verified and what is not.
   - If the change is visible to the user (a feature, option, key, command or window), update the matching page of `docs/help` in the same pull request and retake the screenshots of any window that changed (the help rules are in `docs/architecture.md`, section "User help").
   - Include `Closes #<number>`.
   - CI runs the tests and the version checks on every push to the pull request, but the Windows VSIX build (it costs twice the billed minutes, the repository is private) only for a pull request with the label `build-vsix`. Add the label `build-vsix` when the work is ready, so that the VSIX of the final commit is built once; it is a required step before a merge.
6. **Approval.** Merge only after the user explicitly agrees. The user usually tests the VSIX from the branch in SSMS.
7. **Squash merge** the pull request into `main` (the repository allows only squash merges).
   - The commit message is the PR title with the PR number; the body summarizes the change.
   - GitHub deletes the remote branch automatically. Then switch to the updated `main` and delete the local branch.

### Questions and issues that need the user

Chat messages can get lost or overlooked, so everything that needs the user's attention is also recorded on GitHub.

- Every question to the user (a decision, a missing fact, a request for an SSMS test, a request for approval) is also posted as a comment on the related issue or pull request, in English, with the same content as in chat. If there is no related issue, create one.
- Every issue or pull request that waits for the user gets a label that says what it waits for. Create a label if it is missing. Remove the label as soon as the user has answered or done what was needed.
  - `needs-user`: a decision, an answer or a missing fact (the question is in a comment).
  - `requires-testing`: the user should test the VSIX from the branch in SSMS (the comment says what to test and which version).
  - `requires-review`: a pull request is ready and waits for the user's review and approval to merge.
  - `blocked`: work cannot continue until something outside the issue happens (the comment says what).
  - `needs-research`: the behavior of SSMS or its API must be found out before implementing. It means "waits for research", unlike `research`, which marks the kind of work.
  - `needs-info`: the issue lacks a specification or material that Claude must supply (e.g. a precise scope or acceptance criteria).
  - `ready`: the issue is thought through and work on it can start. Remove it when the work starts.
  - An item can have more than one of these labels.
- The assignee shows who is on the move. An issue or pull request labeled `needs-user`, `requires-testing` or `requires-review` is assigned to the user (`lenochod86`). Everything else is assigned to Claude (`claude-lenochod86`). When the label is added or removed, change the assignee too.
- When the user answers in chat, add a short comment with the answer to the issue, so the decision stays in the history.

### Rules

- Never commit or push directly to `main` unless the user explicitly says so.
- Exception: changes only to CI (`.github/`), tool configuration (`.claude/`, `.gitignore`, `nuget.config` …) or the instructions in `CLAUDE.md` need no issue or pull request. Commit them straight to `main` (tests pass, CI stays green) and push. Product code, tests and `docs/` still go through the issue workflow.
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
- **Themes.** SSMS has light and dark themes (and others); the user works in the dark one. Every window, control, color and icon must follow the theme of SSMS (`VSColorTheme` / `EnvironmentColors`, `VsBrushes` for WPF, `WinFormsTheme.Apply` for WinForms windows, `SnippetUi` for WPF windows) and be checked in the **dark and the light** theme where the change is visual. Never hard-code a color that only works on one background; derive it from the theme. A WinForms control that does not look right in one theme is a bug.

## Build and versions

- Tests: `dotnet test tests/SqlPrompter.Tests` (anywhere).
- VSIX: `dotnet build src/SqlPrompter.Ssms/SqlPrompter.Ssms.csproj -c Release` (Windows only). Output: `src/SqlPrompter.Ssms/bin/Release/net48/SqlPrompter.Ssms.vsix`.
- If `dotnet` is not on PATH, install the .NET SDK into the scratchpad with `dotnet-install.ps1` (no admin rights needed) and set `DOTNET_ROOT`.
- For every VSIX handed to the user, bump the version in `source.extension.vsixmanifest` (otherwise it will not install over the old one) and check the version inside the `.vsix`.
- After changing `.vsct`, bump the version in `ProvideMenuResource`.
- CI enforces both bumps in pull requests (`.github/scripts/Check-Versions.ps1`): any change under `src/` needs a higher VSIX version than `main`. CI also builds with warnings as errors. Billed Actions minutes are limited: a push to a pull request runs only the Ubuntu job (tests, version checks); the Windows VSIX build runs for a pull request with the label `build-vsix` (and manually with *Run workflow*); a push to `main` runs the tests only; the prototype workflow is manual only (#336). The VSIX job runs on the **self-hosted runner** of the user's PC (label `vsix`, installed in `E:\actions-runner` as the Windows service `actions.runner.lenochod86-SQL-Prompter.vojta-pc`, started automatically; no billed minutes). If it is not running the job waits in the queue: ask the user to start the service, or temporarily change `runs-on` in `build.yml` to `windows-latest`. Before the repository is made public the runner must be removed (checklist in #336).

## Environments

- **Locally on Windows:** the VSIX can be built and installed into SSMS 22. You may install and uninstall the VSIX yourself (VSIXInstaller from the SSMS installation, with SSMS closed) and drive SSMS to verify your work: start it, connect to the test instance, take screenshots and read the log. Ask before changing SSMS configuration beyond that, and before closing an SSMS window that may hold the user's unsaved work. **Exception: switching the SSMS color theme (Tools > Options > Environment > General) to check a window in the light and the dark theme is allowed without asking; switch it back to the user's theme (dark) when done.** The SSMS you start from the scripts uses the same settings as the user's own: do not start it or install a VSIX while the user's own SSMS is running. Never touch the user's own servers. A SQL Server test instance runs in Docker on `localhost,1433`; use the `test-database` skill (`.claude/skills/test-database/`) to create test databases and data yourself.
- **In the cloud:** neither SSMS nor the VSIX build is available. Work on Core, Metadata and tests; CI builds the VSIX for pull requests that have the label `build-vsix` (`.github/workflows/build.yml`); branch pushes without a pull request are not built, so open a pull request or run the workflow manually.
- **From mobile:** the user may not have SSMS at hand. Ask for SSMS results as a screenshot or the log (`%LOCALAPPDATA%\SqlPrompter\sqlprompter.log`).

## How to work

- If you do not know something, or it is the user's decision, ask. Do not make up SSMS or SQL Prompt behavior or the user's wishes.
- Do not mark anything about SSMS as verified until it has run in SSMS 22.
- Report results precisely: what passed, what failed (with output) and what was not tested.
- Record the user's decisions as a comment on the issue they belong to and verified SSMS facts in `docs/architecture.md`.
