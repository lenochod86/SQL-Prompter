---
name: create-issue
description: Create or update a well-structured GitHub issue for SQL Prompter from a bug report, wish or screenshot the user describes. Use when the user reports a problem, asks for a feature or wants an issue created, or when a session is used only for issue management.
---

# Creating SQL Prompter issues

Turn what the user says (often in Czech, often with a screenshot) into one issue in `lenochod86/SQL-Prompter`, then answer in Czech with a short recap. In an issue-management session do not implement anything: another session does the work.

## Rules

- **Language:** the issue (title, body, comments, labels) is in English. The chat reply is in Czech.
- **Privacy:** never put the user's server or database names, passwords, connection strings or personal data into an issue. Describe screenshots with neutral names (`SrvA`, `DbA`, `dbo.TableA`).
- **Account:** the GitHub tools may act as `lenochod86`; the user allowed creating issues under either account. Assignee follows the label (see below).
- **One problem, one issue.** If an earlier issue covers the topic: closed and fixed -> new follow-up issue that links it; open -> add a comment or update its body. A user correction to an existing issue goes into that issue (body update or comment), not into a new one.
- **Check for duplicates first:** list open issues (`mcp__github__list_issues`) before creating.
- **Do not invent SSMS behavior.** A guess about the cause goes under "Hypothesis (to be verified)". A wrong hypothesis the user corrects is replaced, and the user's correction is recorded as a comment.
- Images cannot be uploaded with the tools. Describe the screenshot in the issue in words and tell the user to drag the image in by hand.

## Before writing

Read just enough code to ground the issue (the relevant class, the likely cause). Name the real classes/files in the issue. Docs: `docs/architecture.md`, `docs/roadmap.md`.

## Structure of the body

```
## Problem / Goal
What the user sees or wants, with a short description of the screenshot.

## Findings (code read, to be verified)   <- only when something was read
Likely cause, with class/file names. Mark it as unverified.

## Scope
What changes, what explicitly does not.

## Acceptance criteria
Testable bullets. Cover dark and light theme when visual. Where logic can live in
Core/Metadata, say that it gets tests (every new context in ContextAnalyzer: a test
with the caret marked `|`; every bug fix: a test that catches it).

Related: #n, #m
```

Title: short, descriptive, English, no phase number.

## Labels and assignee

- Kind: `bug`, `enhancement`, `documentation` or `research`.
- Phase: `phase-0` unless the roadmap says otherwise.
- State: `ready` (thought through, work can start), `backlog` (not now), `needs-research` (SSMS behavior must be found out first), `needs-info` (missing facts, say which in the issue), `needs-user` (a decision of the user), `requires-testing` (user tests a VSIX).
- Assignee: `needs-user`, `requires-testing`, `requires-review` -> `lenochod86`; everything else -> `claude-lenochod86`.
- A visual change or a change in `src/SqlPrompter.Ssms` that must be checked in SSMS gets `requires-testing` when the VSIX is delivered, not at creation.

## Questions to the user

Every question asked in chat is also posted as a comment on the issue (English, same content) and the issue gets `needs-user`. When the user answers in chat, add a short comment with the decision.

## Closing and splitting

Close an issue only when the user says so (state reason `completed`). When a part stays open, close the old issue and create a separate backlog issue for the rest, linking both ways.

## Reply to the user (Czech)

Short recap: link, title, labels, assignee, the hypothesis (marked unverified), what the issue requires, what the user must do (e.g. send a log, drag in the images). Then ask whether there is anything else.
