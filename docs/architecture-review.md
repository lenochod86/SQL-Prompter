# Architecture review: is the in-process VSIX still the right approach? (#183)

Written 2026-11-06 after the MVP and phases 0 to 6 had been tested in SSMS 22.11. The question: **do the problems found during testing come from the early choice** (a classic in-process VSIX using the editor's MEF APIs), and would another architecture be better?

**Marks.** *Verified* = tested or inspected in SSMS 22.11 on this machine. *Not verified* = reasoned from documents or from reports of other people, or an attempt that did not give an answer.

## Recommendation (short)

**Keep the in-process VSIX.** No alternative that was examined removes more problems than it brings, and the one that looks most attractive (a separate language server) moves only the part of the code that has never been a problem.

Improve three things instead (each is a small issue, see [Migration plan](#migration-plan)):

1. **One completion path.** Today a list is run either by the editor's session (typing) or by our own controller (`InvokedList`, Ctrl+Space). Everything that is added to the list (the status bar, the column picker, sorting) has to be done twice. Move the typing path to our controller too, so that there is one list implementation.
2. **A compatibility gate for the undocumented SSMS access.** Everything undocumented is already in one file (`SsmsConnection.cs`, reflection into `ScriptFactory` / `UIConnectionInfo`) plus a few DTE command names. Add a startup self-check that tests every member we rely on, logs the result, switches the dependent feature off (instead of failing later at an arbitrary place) and tells the user once; add a smoke script for the test procedure after an SSMS update.
3. **Keep the "thin Ssms layer" rule strict**, because it is what keeps the other options open: `SqlPrompter.Core` and `SqlPrompter.Metadata` (15 000 lines, no SSMS types) could be moved into a process of their own without a rewrite if SSMS ever forces it.

Revisit the decision if one of these happens: SSMS documents support for out-of-process extensions *with an editor completion API*; an SSMS update breaks the in-process model in a way that cannot be fixed in a patch release; or the list of undocumented members grows beyond what the compatibility gate can cover.

## 1. Inventory of problems

Classes: **(a)** the VSIX / in-process model itself, **(b)** the VS editor completion API as SSMS hosts it, **(c)** undocumented SSMS internals, **(d)** our own design or implementation, **(e)** unavoidable in any approach.

### Closed bugs (all 13 issues labeled `bug`)

| Issue | Problem | Class | Remark |
|---|---|---|---|
| #144 | Ctrl+Space does not open the list behind a word after a table alias | **(b)** | A session started with `TriggerCompletion` from outside the typing path never calls `GetCompletionContextAsync`. Solved with our own list (`InvokedList`). |
| #135 | The list stays on top of other applications | **(a)/(d)** | A WPF popup of our presenter needs correct ownership. |
| #137 | Rendering artifact on the Coloring options page | **(a)** | The legacy Options host. Solved by an own window. |
| #140 | Tabs are not restored when SSMS starts | **(c)/(e)** | SSMS itself does not restore tabs; restoring needs the undocumented `ScriptFactory`. |
| #168 | Tab restore reopens tabs that were closed by hand | **(c)/(d)** | The order of events at shutdown is undocumented (all windows close before `OnBeginShutdown`). |
| #141 | Show suggestions after a comma | (d) | A missing feature of the trigger logic. |
| #131 | Selected completion item is hard to read | (d) | Colors of our presenter. |
| #126 | Snippet picker does not scroll with the arrow keys | (d) | |
| #124 | Snippet not inserted on Space, no list after a FROM clause | (d) | |
| #87 | Placeholder names disappear after inserting a snippet | (d) | |
| #85 | JOIN list: error on Backspace, no explicit invocation, Enter does not insert | (d)/(b) | |
| #66, #11 | CI (xUnit analyzer, Node.js versions) | (e) | |

Of the 11 product bugs, **5 touch the platform** ((a), (b), (c)) and **6 are our own**. The feeling that many problems come from the early decision is therefore partly right: the platform bugs are the expensive ones (they forced an own list, own windows and own tab storage), but they are a minority.

### Findings in `docs/architecture.md` and during the work

| Finding | Class |
|---|---|
| The SDK of SSMS 22 has no command arguments for Ctrl+Left / Ctrl+Right (`WordPreviousCommandArgs` exists only in the runtime assembly), so the keys are bound through DTE to hidden commands. | (b) |
| The editor's WPF key events (Ctrl+Space, Ctrl+Left) are not delivered to extensions; only the command chain is. | (b) |
| The connection of the active window comes from `ScriptFactory.CurrentlyActiveWndConnectionInfo` / `UIConnectionInfo` by reflection (one file, `SsmsConnection.cs`). | (c) |
| The results grid has no API; the only hook is the DTE command `Edit.CopywithHeaders`. | (c) |
| DTE `DocumentEvents` do not fire for windows closed by the user (running document table events are used). | (c) |
| Third-party extensions are unsupported by Microsoft, reports are closed without investigation ([FAQ][faq]). SSMS 22.3 contained a breaking change for extensions; Redgate SQL Prompt crashed SSMS 22.3 on startup until a fix release ("Method not found" in `Microsoft.Extensions.DependencyInjection`) ([Redgate community][redgate]). *Not verified here; reported by others.* | (a) |
| Entra ID with MFA: the connection cannot be recreated from `RenewableToken` (not verified; the user does not use it). | (c) |
| A stale popup stays on screen when the text is replaced by a paste while a list is open (seen while driving SSMS by script). | (d) |

**What the inventory says.** The platform problems come from three places: how the editor starts and routes completion (b), how little of SSMS is exposed (c), and the unsupported status (a). None of them is caused by *where our code runs*. They would exist in any approach that has to show a list in this editor and to read the connection of the window.

## 2. Requirements and constraints

Requirements: completion with our list only (the built-in list is suppressed where we take part), snippets, formatting, coloring of tabs and results, history and tabs, code analysis and warnings, results grid actions, column picker and quick actions, definition panel, metadata that never blocks the UI (target 50 ms from the cache).

Constraints: SSMS 22 only (amd64 and arm64), MIT license and open source, no SQL Prompt binaries analyzed, no telemetry, read-only metadata, passwords never stored, no dependency on SSMS types in `Core` and `Metadata`.

Two of the requirements are **in-process by nature**: the list is drawn in the editor and reads the editor's buffer and caret, and the connection and its password exist only in the SSMS process (`InMemoryPassword`). Any architecture has an in-process part; the question is only how large it is.

## 3. Alternatives

### A. Keep the VSIX, own the whole completion UI (recommended, as an improvement)

One list implementation driven from the editor's command chain (`TypeChar`, `Backspace`, `Return`, `Tab`, `Down`/`Up`, `Escape`) for typing and for Ctrl+Space, instead of the editor's session plus `InvokedList`.

- **Removes:** the two list paths; the duplicated status bar (`SessionFooter` was added in #179 only because the session list had none); the differences in sorting between the paths (found in #181: `InvokedList` did not sort by group); a whole class of "works for Ctrl+Space but not for typing" bugs. It is also the base for the planned further changes of the list (#61).
- **Costs and risks:** the editor's session also does things for us for free (tracking the applicable span while typing, filtering, commit characters, the "no list while a snippet field is typed" integration). `InvokedList` already re-implements most of them (verified: typed filter, commit on Tab/Enter, Esc, picker mode). Typing must not slow down: filtering and building must stay off the UI thread. Medium effort.
- **Verified:** the own list with its own command handlers works in SSMS 22.11 and shares the source, presenter and commit code with the session (#144, #175, #177, #179). **Not verified:** driving every typing case (backspace, IME, fast typing) from it.

### B. In-process VSIX plus a separate language server (LSP)

A thin client in SSMS speaks the Language Server Protocol to a server process that holds the parser, analysis and metadata cache.

- **Is the LSP client in SSMS?** *Verified:* SSMS 22.11 ships `Microsoft.VisualStudio.LanguageServer.Client` 18.11 (MEF components in the catalog, a `LanguageServer` extension). **A probe did not work:** `prototype/SqlPrompter.LspProbe` (a `[ContentType("SQL")]` `ILanguageClient` that starts a small server, installed next to the VSIX) was composed into the MEF catalog, but **the client was never instantiated** (its constructor was not called) in a query window and in a `.sql` file opened from disk. The cause was not found (the broker may activate clients only for some kinds of documents or content types, or on a condition not met in SSMS). So it is *not verified that the VS language client can be used in SSMS at all.*
- **What it would move:** parsing, context analysis, ranking, metadata snapshots. These are pure functions in `Core` and `Metadata`; they are tested outside SSMS and are fast, so they were never the problem (the inventory has no bug in them that the process model would have prevented).
- **What stays in SSMS anyway:** the list and its presenter, the commit logic, the connection and password, tabs, history, the grid menu, coloring, the definition panel, quick actions (about 10 000 lines of the SSMS layer). LSP has no concept for most of these.
- **Costs:** the whole text goes over a pipe on every keystroke or incremental sync; a second process to start, stop and survive SSMS crashes; an arm64 build of the server; the metadata must be loaded in the server (it needs the connection and the password, so the client would pass credentials to another process) or fetched from the client over RPC. LSP completion is rendered by VS's own LSP source, so our items (headings, check boxes, usage ranking, preselect rules, footers) would need custom protocol extensions anyway.
- **Gains:** crash isolation of the analysis, a server reusable in another editor, independence from the .NET Framework 4.8 and from assembly conflicts in the server part. These are real but are not what has been hurting.
- **Verdict:** not worth it now. The option stays open because `Core` does not depend on SSMS.

### C. VisualStudio.Extensibility (out-of-process extensions)

- *Verified (files of the SSMS 22.11 installation):* `Microsoft.VisualStudio.Extensibility.dll`, `.Contracts.dll`, `.Framework.dll` and the package `Microsoft.VisualStudio.Extensibility.VSPackage` 18.11 are installed, and the contracts contain `LanguageServerProvider` and tool window / command types. **No type for completion was found in the contracts or in the editor contracts** (`Microsoft.VisualStudio.Extensibility.Editor.Contracts.dll`): the model has text view listeners, commands and language servers, but no way to provide or render a completion list. The installation has no extension host executable (only `ServiceHub.Host.Extensibility.Contracts.dll`), and no ServiceHub process runs.
- *Not verified:* a real VisualStudio.Extensibility extension was not built and loaded. Microsoft states no support for it in SSMS ([FAQ][faq]); a search found no document that says otherwise.
- **Verdict:** cannot replace the in-process extension (no completion API) and probably cannot run at all. It could one day host the non-editor features (history, tabs); not now.

### D. Parser: ScriptDOM or the own tolerant analyzer

The earlier decision stands: while typing, SQL is almost never valid, so the completion and formatting engines use the own tolerant lexer and clause analysis; ScriptDOM is for full analysis of valid statements. 1 121 unit tests (most with the caret marked by `|`) cover the own analyzer, and the position map of #178 works on it. The recurring problems were never in the parser. **No change.**

### E. What other products do

*Not verified here, from public reports:* Redgate SQL Prompt is an in-process extension of SSMS and Visual Studio and had the same exposure to SSMS 22.3 ([Redgate community][redgate]); the open-source MSSQL Tool is a free SSMS extension with completion, history and result processing ([wiki][mssqltool]). The practice in this field is the in-process VSIX; nobody is known to ship a working out-of-process completion for SSMS.

### F. An external tool next to SSMS

Cannot show a list in the editor or read its buffer. Rejected.

## 4. Assessment

| | Removes platform problems | New risks | Effort | Verdict |
|---|---|---|---|---|
| Keep as is | none | the two list paths keep costing time | none | not enough |
| **A. Own list for typing too** | the session-vs-own differences ((b)) | typing regressions, keystroke latency | medium | **do it, step by step** |
| **Compatibility gate** | surprises after an SSMS update ((c)) | none | small | **do it** |
| B. Language server | crash isolation of analysis | process lifecycle, credentials, arm64, latency, LSP cannot render our list; client not verified to work in SSMS | large | no |
| C. VisualStudio.Extensibility | none (no completion API) | probably cannot run | large | no |
| D. Only ScriptDOM | none | fails on incomplete SQL | large | no |

Stability across SSMS updates is the main risk of every in-process variant and is not removed by the others either (B and C still need a thin in-process part for the list and the connection). Arm64: the VSIX is AnyCPU and the SSMS layer has no native code; a server (B) would need an arm64 build. Testability is the same today as for B because the logic is already in testable libraries.

## Migration plan

Small issues that can be done one after another without breaking the current VSIX (each with its own tests and a manual check in SSMS):

1. **Compatibility gate.** A `SsmsCompatibility` check at startup: lists the reflected types and members and the DTE commands used (`ScriptFactory`, `UIConnectionInfo` members, `Edit.CopywithHeaders`, the hidden commands) with *found / missing*; writes them to the log; disables the dependent feature and shows one message if something is missing. A smoke script (`drive-ssms`) that runs the main scenarios after an SSMS update. Record the SSMS build tested in `docs/architecture.md`.
2. **Typing path through our controller.** In steps: (a) the controller handles typing in a feature flag *Run the list myself* (default off), reusing `InvokedList`; (b) typing cases (backspace, delete, fast typing, snippet fields, comma and space triggers) with tests where possible and a checklist in SSMS; (c) default on, the session source remains only as a fallback; (d) remove the session path and `SessionFooter` when the user agrees.
3. **One sort and filter implementation** shared by the controller and tests (today `SortText` is ordered separately in two places).
4. **Document the boundary:** a list of what `Core` / `Metadata` / `Ssms` may reference, checked in CI (no SSMS or VS types in `Core` / `Metadata`; already true, make it enforced).

## Decision requested

Approve the recommendation (keep the in-process VSIX; do steps 1 to 4) or choose another direction. The decision is recorded in `docs/roadmap.md` (Decisions).

[faq]: https://learn.microsoft.com/en-sg/ssms/faq
[redgate]: https://productsupport.red-gate.com/hc/en-us/community/posts/33887404886301-ssms22-sql-prompt-11-crash-after-latest-ssms22-update-10-02-2026?page=2
[mssqltool]: https://github.com/PuLuShen/MSSQLTOOL/wiki
