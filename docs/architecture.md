# SQL Prompter: architecture

An extension with custom IntelliSense for SQL Server Management Studio 22.

**Status:** the prototype answered all three questions in SSMS 22.11 (see [Prototype](#prototype)). The MVP runs in SSMS 22.11 (see [MVP](#mvp)). Next steps are in [roadmap.md](roadmap.md).

## Summary

C# and a classic VSIX extension running in the SSMS process. All logic lives in libraries that do not depend on SSMS; the SSMS layer is as thin as possible.

## Main risk

Microsoft states that third-party extensions are not supported in SSMS. SSMS does not block them, but closes reports about them without investigation ([SSMS FAQ][faq]). An SSMS update may break the extension at any time.

## Technology

| Layer | Choice | Reason |
|---|---|---|
| Language | C# | Native language of the editor API |
| SSMS add-in | .NET Framework 4.8, AnyCPU | SSMS is 64-bit and also exists for Arm64 ([FAQ][faq]) |
| Core | .NET Standard 2.0 | Builds and tests also outside Windows |
| Editor integration | MEF and `IAsyncCompletionSource` ([walkthrough][completion]) | Modern API with background computation |
| Metadata | Queries on `sys.*` through `System.Data.Common`, connection from the `Microsoft.Data.SqlClient` in the SSMS process | Fast, read-only, no SMO dependency and no second SqlClient copy |

SSMS 22 is built on Visual Studio 2026 ([FAQ][faq]). According to [another extension author's notes][sqlfluff], `Microsoft.VisualStudio.SDK` 17.14 is used because there is no 18.x package. The prototype confirmed this in SSMS 22.11.

VisualStudio.Extensibility is not used: no document confirms support in SSMS, and according to [the same source][sqlfluff] replacements for parts of the editor API are missing.

## Structure

```text
┌──────────────────── SSMS 22 process ────────────────────┐
│  SqlPrompter.Ssms (VSIX, thin layer)                     │
│  completion · commit · settings                          │
│  current connection                                      │
│        │ own interfaces, no SSMS types                   │
│  ┌─────▼──────────────┐   ┌──────────────────────────┐   │
│  │ SqlPrompter.Core   │◄──│ SqlPrompter.Metadata     │   │
│  │ lexer              │   │ per-database cache       │   │
│  │ context analysis   │   │ background loading       │   │
│  │ suggestion ranking │   └────────────┬─────────────┘   │
│  └────────────────────┘                │                 │
└────────────────────────────────────────┼─────────────────┘
                                         ▼
                                    SQL Server (sys.*)
```

| Project | Target | May depend on |
|---|---|---|
| `SqlPrompter.Core` | netstandard2.0 | Nothing from VS, SSMS or a database |
| `SqlPrompter.Metadata` | netstandard2.0 | Core and `System.Data.Common`; the host supplies the concrete `DbConnection` |
| `SqlPrompter.Ssms` | net48, VSIX | VS SDK, SSMS API, both projects above |
| Tests | Current .NET | Core and Metadata, cases like `SELECT u.| FROM dbo.Users u` with the caret marked |

## Completion flow

1. On the UI thread (`InitializeCompletion`) the batch between `GO` lines is extracted, tokenized and the context determined, e.g. "column of alias `u`".
2. In the same step, missing metadata is identified and loaded in the background.
3. In the background (`GetCompletionContextAsync`) suggestions are built and ranked from the context and the current snapshot.

This flow never waits for the database. Metadata starts loading when a query window is activated, and the columns of user objects are loaded together with the objects, so they are usually ready before the user needs them. Whatever is missing (columns of system views, the very first load) is added on the next completion. Objects defined in the script (CTEs, temporary tables, table variables, variables, parameters) come from the script model, see below.

## Decisions

- **Two parsers.** While typing, SQL is almost always incomplete, so completion uses its own tolerant lexer and a light clause analysis. Microsoft ScriptDOM is used for full analysis of valid statements (formatting, refactoring, diagnostics). ScriptDOM is [open source under the MIT license](https://github.com/microsoft/sqlscriptdom) and supports netstandard2.0 (verified 2026-11-02).
- **Not based on `Microsoft.SqlServer.Management.SqlParser`.** It is [not open source][sqlparser] and its license terms are not verified.
- **Metadata as immutable snapshots.** Every database has its own snapshot. Columns of user objects are loaded with the objects, columns of system views on demand. Refresh by `sys.objects.modify_date`, after DDL in the window and by a manual command.
- **Security.** Passwords are never stored. Metadata goes to disk only optionally, because even object names may be sensitive. No telemetry.
- **Target performance (draft).** Under 50 ms for a list from the cache and no waiting for the network on the UI thread.
- **Only our list.** Our participation suppresses the built-in list (prototype, question 2). The user wants to see only SQL Prompter's suggestions (decided 2026-11-02), so the extension participates everywhere except in strings, comments and numbers. The built-in IntelliSense stays enabled for error underlining and quick info.
  - Where a new or unknown name is typed (alias after `AS`, a variable being declared, `#table` outside a table position, unknown qualifier, three-part name), the list is empty.
  - After `@`, the variables and parameters defined in the script are offered (`ContextKind.Variable`).
  - After `CAST(x AS` and `DECLARE @x`, data types are offered.
  - At positions the extension does not understand (`EXEC`, `VALUES`, `SET @x =` …), keywords, functions and types are offered without a firm selection.
  - With a CTE, derived table, temporary table or table variable, the columns of known sources, aliases and functions are offered.
  - Suggestions carry the "SQL Prompter" filter, which makes the list recognizable.
  - Not verified yet: whether an empty list really suppresses the built-in one.
- **Script model** (`ScriptModel` in Core, implemented in #15, not verified in SSMS yet). Definitions in the batch before the caret, built from text only and in the background (`GetCompletionContextAsync`), so the UI thread is not slowed down:
  - CTEs of the statement at the caret, `#tables` from `CREATE TABLE` and `SELECT … INTO`, `@tables` from `DECLARE @t TABLE`, variables from `DECLARE`, and the parameters of `CREATE/ALTER PROCEDURE/FUNCTION`;
  - columns of a CTE, `SELECT … INTO` table or derived table come from the first `SELECT` of its query (alias, `AS`, `alias = expr`, last part of `a.b`, `x.*` and `*`); `SELECT *` is expanded from the metadata snapshot and the objects whose columns are not loaded yet are requested in the background (`ScriptColumns.MissingColumns`);
  - items without a name (`SELECT 1`) are skipped, and only the first block of a `UNION` names the columns;
  - the model is never trusted for what it cannot see: a source it cannot resolve stays unknown, as before.
- **SqlClient from the SSMS process.** Our own connection creates `Microsoft.Data.SqlClient.SqlConnection` from the copy SSMS has loaded (verified by the prototype, version 6.0.0.0). The VSIX ships no SqlClient and Metadata works with `DbConnection` only. The SqlClient 6 package does not support netstandard2.0 anyway.
- **System objects.** Metadata is read from `sys.all_objects` and `sys.all_columns`, so `sys.objects` or `INFORMATION_SCHEMA.TABLES` work as in the built-in IntelliSense. System objects are not offered without a schema.
- **Commit keys.** As in the user's SQL Prompt settings: Tab, Enter and Space insert the suggestion; a dot, comma, parenthesis, semicolon, `]` or operator does not (`CommitKeys` in Core, configurable in Tools → Options → SQL Prompter → Behavior since 0.3.0, #9). Every character that ends a word must still end the session: when only Tab and Enter were commit characters, the list stayed open after a space or a dot (verified in SSMS 22.11). The commit manager therefore reports non-inserting characters as a handled commit without inserting anything, so the list closes, the character is typed and a dot opens the next list (verified in SSMS 22.11). Where the user probably types a name of their own (after a table source or an operand), the selection is soft (`SoftSelection`) and typed characters do not commit. Keywords are inserted in lower case when the user started typing in lower case.
- **Metadata refresh in the MVP.** At most once every 30 s, completion checks a fingerprint of `sys.objects` (count and latest `modify_date`) and reloads the snapshot after a change. The manual command SQL Prompter → Refresh Metadata drops all snapshots instead.
- **Refresh after DDL in the window (#8).** The package subscribes to `BeforeExecute` of the DTE command `Query.Execute` and takes the selection, or the whole document, of the active window. `SchemaChangeDetector` (Core, lexer-based, ignores comments and strings) recognizes `CREATE`/`ALTER`/`DROP` of tables, views, procedures, functions, triggers, schemas, synonyms, types and sequences, `sp_rename` and `SELECT … INTO`. If it finds one, the fingerprint is checked 1, 3, 10 and 30 s later regardless of the 30 s interval, because execution is asynchronous and the event fires before it ends. Verified against SQL Server 2025: the fingerprint changes after `CREATE TABLE`, `ALTER TABLE … ADD` and a column `sp_rename`. Verified in SSMS 22.11: the watcher finds `Query.Execute` and the log shows the snapshot refreshed after DDL ran in the window.
- **Verified in SSMS 22.11 with VSIX 0.6.1 (2026-11-04), driven by Claude through UI Automation.**
  - Every item of the list shows "SQL Prompter" in its suffix (#20).
  - `USE db` on an earlier line makes completion use that database although the window is connected to `master`; the log shows the metadata of that database loading (#25). The first completion after a database is first used is empty, the next one has its objects (loading is in the background).
  - `Db.`, `Db..` and `Db.dbo.` offer the objects of the other database, a synonym is offered like a table and its columns come from the target (#25).
  - Choosing a table after `FROM` inserts `dbo.OrderItems AS oi`; columns are inserted as `u.Email` (#23).
  - `SELECT em` without `FROM` offers `Email (Users)`; choosing it gives `SELECT u.Email FROM dbo.Users AS u`, also with `, Name WHERE 1 = 1` after the caret (the `FROM` goes before `WHERE`) (#24). The commit manager must compute the caret position from `span.Start.Position`: `span.Start + n` throws on the replaced snapshot and the default commit then inserts the text a second time.
  - Not verified yet: the options pages, Export and Import Settings, custom aliases, ignored prefixes and the keyword case option.
- **Driving SSMS from a script.** `SSMS.exe -S localhost,1433 -U claude -d master -C -N Optional` shows a confirmation dialog and a login dialog without a password (`-P` is not accepted). `System.Windows.Automation` finds them: invoke "Yes", then set the Password edit through `ValuePattern` and invoke "Connect". The editor is the `Edit` named "Text Editor": `SetFocus`, paste text through the clipboard (no completion triggers) and `SendKeys` for typing. The completion list is a `ListBox` of `IntellisenseMenuItem` elements whose `Text` children hold the name and the suffix; the editor text is readable through `TextPattern`. Killing SSMS with an unsaved window shows a recovery dialog on the next start ("Do Not Recover"); close it with Alt+F4 and "Don't Save" instead.
- **Linked servers (#26, verified in SSMS 22.11 with VSIX 0.8.1; test linked server `SqlPrompterLS` to the Docker instance itself, created with a provider string `Encrypt=Optional;TrustServerCertificate=Yes;UID=…;PWD=…` because the test login cannot create login mappings).** The first load only adds `SELECT name FROM sys.servers WHERE is_linked = 1`, so the initial load does not depend on what is behind the servers. `Srv.` offers the databases (`EXEC sp_catalogs @server_name`), `Srv.Db.` its schemas and objects (`EXEC sp_tables_ex @table_server, @table_catalog`: several hundred rows including system objects, flagged as such, takes a few seconds), `Srv.Db.schema.` and `Srv.Db..` the objects of a schema, and a four-part source such as `Srv.Db.dbo.T t` gets its columns from `EXEC sp_columns_ex` when `t.` is typed. `LinkedServerCache` (Metadata) never waits: it returns what is cached, starts the load in the background (15 s command timeout, 30 s overall) and remembers a failure for 60 s; the first completion after choosing a server or database is therefore empty and the next one has the names. A schema or database of the current database wins over a linked server of the same name. A server that cannot be reached (`BadLS` without a login mapping) only logs "Linked server: loading … failed" and the normal list keeps working. Columns of remote tables carry the type from `TYPE_NAME` with the size, and `NULLABLE`.
- **Keys and JOIN suggestions (#28, verified in SSMS 22.11 with VSIX 0.9.1).** The first metadata load now also reads `sys.foreign_keys` with their column pairs (`sys.foreign_key_columns`, in column order, so multi-column keys keep their order) and the primary keys (`sys.indexes.is_primary_key`); the DDL refresh brings them with the rest because constraints change the `sys.objects` fingerprint. After `JOIN` the tables that have a key to a table of the query, or that a table of the query points to, come first (group −1, sort key "-01") as one item with the whole clause, e.g. `Doprava ON d.IdObjednavka = p.IdObjednavka AND d.IdPolozka = p.IdPolozka`, inserted as `Crm.Doprava AS d ON …`; the name of the key is the suffix and two keys between the same tables give two items. The new table stands left of the `=` for now (the option for the order is #29). The word being typed is parsed as a table source of its own, so it is not counted as a used alias. Columns carry a key kind and the custom list shows a key icon after the type (primary key wins over foreign key). Icons of the custom list are themed with `ImageThemingUtilities.SetImageBackgroundColor` and the theme background (without it they are drawn dark); the list width is estimated from the longest row (420 to 1000 DIPs).
- **Conditions after ON (#29, verified in SSMS 22.11 with VSIX 0.10.0).** At the start of a condition after `ON` (or `AND` / `OR` following it) the analyzer sets `JoinTarget`, the table introduced by the JOIN whose ON is being edited (found through the token index of the source that follows the nearest `JOIN` before the `ON`; the sources parse the whole block, so `JOIN a ON | JOIN b` still finds `a`). `JoinSuggestions.Conditions` links it to every table before it in the query: the whole foreign key (columns joined by `AND`), with the option every single column pair of a multi-column key, then conditions for column names both tables have (those that are a key column in one of the tables before the others), skipping what a key already covers. They are items of the new kind `JoinCondition` (own icon) and sort before everything else: negative groups −4 to −1, which needed the sort key to be `(Group + 100)` as three digits, because the old `Group.ToString("D2")` put "-01" before "-04". Options page *Joins*: new table on the left (off by default: the user's SQL Prompt has "Swap order of columns in join clauses" on, which puts the new table on the right, `u.IdUser = o.IdUser`, while SQL Prompt's own default is `table2.id = table1.id`; confirmed from the user's screenshot of that option), single columns of multi-column keys, columns with the same name, same name needs the same type. The SQL Prompt import takes `IncludePartialFKJoinConditions`, `AlwaysIncludeNameMatchedJoinConditions` and `FlipColumnsOnJoinCondition`; `FlipColumnsOnJoinCondition` (swap) is imported as "new table on the left" = not swap. SQL Prompt's "always include type-matched conditions" (columns of the same type with different names) is not imported or implemented.
- **Custom completion list (research #61, verified in SSMS 22.11 with VSIX 0.7.1).** SSMS honors an exported `ICompletionPresenterProvider` (`Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion`, contract in `Microsoft.VisualStudio.Language.dll`): with `[ContentType("SQL")]` and `[Order(Before = "default")]` the editor calls `GetOrCreate(textView)` and then `Open`/`Update`/`Close` of our `ICompletionPresenter` instead of showing its own list (the log line "Presenter requested" proves it). The editor keeps doing the filtering, the key handling and the commit; the presenter only draws what `CompletionPresentationViewModel` holds: `ItemList` (`CompletionItemWithHighlight` with the matched spans), `SelectedItemIndex`, `UseSoftSelection` and `ApplicableToSpan`, and reports mouse input through `CompletionItemSelected` and `CommitRequested`. `Items` of the view model is obsolete, use `ItemList`. `ICompletionPresenter` is `IDisposable`.
  - The prototype (`SqlCompletionPresenter.cs`) is a WPF `Popup` (no activation, `PlacementTarget` the text view, offsets from `GetCharacterBounds` minus the viewport) with one row per item: `CrispImage` from the item's icon moniker, the name with the typed text in bold, a detail in the link color (column type and nullability, carried as the `SqlPrompter.Detail` property of the item), the suffix on the right. Theme brushes through `SetResourceReference` with `VsBrushes` keys work (`HighlightKey` for the selection, `ControlLinkTextKey` for the detail).
  - Verified: Down and Tab/Enter commit the right item, the first row is only a soft (grey) selection until the first Down, Esc closes, typing narrows the list and bolds the match, a mouse click selects and a double click commits, and the position is right on a 3840×2160 screen at the user's scaling.
  - Since 0.7.2: only 12 row visuals exist and are rebound while scrolling (`ScrollBar` of our own, mouse wheel, the selection always scrolled into view), so thousands of items are cheap; a second popup to the right shows the description of the selected item; the list opens above the caret line when it does not fit below (verified with the caret on the last visible line). The list is a fixed 600 DIPs wide.
  - Not done or not verified yet: the filter buttons ("All Suggestions", Column Picker), key icons (needs key metadata, #28), the light theme, several monitors with different scaling, and the width adapting to the content.
- **Own menu.** SQL Prompter has its own menu (parent `IDG_VS_MM_TOOLSADDINS`). Commands are grouped; more features will add to the groups. Like VS since 2019, SSMS moves extension menus under **Extensions** and an extension cannot change that. The user moves it back to the main menu bar in **Extensions → Customize Menu…** (verified in SSMS 22.11: the menu disappeared from Tools and did not appear in the main menu bar by itself).
- **VSIX build.** The project deletes the intermediate manifest in `obj` before every build; otherwise an incremental build would not repackage the VSIX after a version change.

## Prototype

Without positive answers there was no point in building the rest:

1. **Does a MEF extension load in SSMS 22 and work in the SQL editor?** Content type names are not reliably documented, so the prototype logs them at runtime. It must also show that the SQL editor really uses `IAsyncCompletionSource`.
2. **Can duplicates next to the built-in IntelliSense be avoided?** It may be necessary to turn off the built-in completion or suppress its results.
3. **How to get the connection of the active window?** `ScriptFactory.CurrentlyActiveWndConnectionInfo` is [documented for older versions only][scriptfactory]. Signing in with Entra ID and MFA needs separate verification, because the connection may not be easy to recreate.

### Prototype results

The code is in [`prototype/`](../prototype/README.md): a diagnostic VSIX that logs everything to `%LOCALAPPDATA%\SqlPrompter\prototype.log`. Its README contains the test procedure and the results table. The VSIX builds with `dotnet build` without Visual Studio (SDK-style project with the `Microsoft.VSSDK.BuildTools` 17.14 package). It first ran in SSMS 22.11.3 on 2026-11-02.

**Question 1: yes.** The MEF extension loads and `IAsyncCompletionSource` works in the SQL editor: typing `hel` offers `hello`.

- The content type of the SQL editor is `"SQL"` with base types `"code"` → `"text"` → `"any"`. The view sits on a `"projection"` buffer.
- `IAsyncCompletionBroker.IsCompletionSupported` returns `True` for `"SQL"`. `GetCompletionContextAsync` runs off the UI thread.
- The editor assemblies in SSMS have version 18.0.0.0. An extension built against `Microsoft.VisualStudio.SDK` 17.14 binds to them, so the SDK 17.14 hypothesis holds.
- `Microsoft.Data.SqlClient` 6.0.0.0 is loaded in SSMS. DTE reports `Version=18.0` and `Edition=SSMS`.
- The buffer properties of a SQL window contain nothing related to the connection.

**Question 2: yes, no duplicates.**

- Control test with `SELECT * FROM sys.ob`: with `hello` turned off the built-in SSMS list appears, with it turned on only our list. The built-in IntelliSense adds no items to the asynchronous session and its list is not shown next to ours.
- Where our source returns `DoesNotParticipateInCompletion`, the built-in IntelliSense keeps working. With the built-in IntelliSense turned off, our source still works.
- Consequence: where our source participates, it replaces the built-in IntelliSense completely. The user chose to see only our list (see Decisions), so the built-in IntelliSense never needs to be turned off.
- Not verified how SSMS suppresses the built-in IntelliSense. It is enough for now because the behavior is stable and controlled by our source.

**Question 3: yes for SQL Server authentication; Windows and Entra ID remain.**

- Our own connection from the window's data (including the password from SSMS memory and the window's `Encrypt`/`TrustServerCertificate`) opens in about 100 ms.
- `AdvancedOptions[DATABASE]` follows the database selected in the window's drop-down, so reading it on every completion is enough.
- `ScriptFactory.Instance.CurrentlyActiveWndConnectionInfo` (`SQLEditors.dll` 22.200) is reachable through reflection. `UIConnectionInfo` (`Microsoft.SqlServer.RegSvrEnum` 18.100) contains `ServerName`, `UserName`, `AuthenticationType` (SQL Server = 1), `Password` and `InMemoryPassword` (`SecureString`), and `RenewableToken` (`null` for SQL logins, a candidate for Entra ID).
- `AdvancedOptions` contains `DATABASE`, `ENCRYPT_CONNECTION`, `TRUST_SERVER_CERTIFICATE`, `CONNECTION_TIMEOUT`, `PACKET_SIZE` and server details. Our connection must take over the encryption settings, otherwise SqlClient 6 (`Encrypt=Mandatory`) fails on a self-signed certificate.
- `Microsoft.SqlServer.Management.ServerInformation.IServerConnectionInformation` (`Microsoft.SqlServer.Management.SDK.SqlStudio` 22.200) is among the candidate types and may be useful as a replacement.
- Not verified: Windows authentication (issue #7), Entra ID with MFA, and whether `DATABASE` also changes after `USE` in the script.

Found in the SSMS 22.11.3 installation:

- The `productId` is `Microsoft.VisualStudio.Product.Ssms`.
- The built-in extensions use `InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[22.0,)"`.
- `Microsoft.VisualStudio.Component.CoreEditor` is among the installed packages.
- VSIXInstaller installs a VSIX targeting `Microsoft.VisualStudio.Ssms` `[22.0,23.0)` with the prerequisite `Microsoft.VisualStudio.Component.CoreEditor` (verified).
- `ScriptFactory` is reachable through reflection without referencing SSMS assemblies at build time (verified).
- Our own connection can use the `Microsoft.Data.SqlClient` already loaded in the SSMS process, so the VSIX does not have to ship it (verified).
- SSMS 22.11 has no extension manager in its menus; uninstall with `VSIXInstaller.exe /uninstall:<Id>`.
- If the manifest also allows Visual Studio, VSIXInstaller may install the extension there instead of SSMS. The manifest therefore targets SSMS only.
- After changing `.vsct`, the version in `ProvideMenuResource` must be bumped, otherwise SSMS shows cached menus.
- After an extension's files are removed, SSMS may keep its package registration and report that the package did not load; `SSMS.exe /updateconfiguration` rebuilds the configuration.

## MVP

The code is in `src/` and the tests in `tests/`. Build: `dotnet build SqlPrompter.slnx -c Release` on Windows; tests: `dotnet test tests/SqlPrompter.Tests` anywhere. The manual test procedure is in the [README](../README.md).

What the MVP does:

- Keywords at the start of a statement, after a table source (`WHERE`, `JOIN` …) and in expressions; data types after `CAST(x AS` and `DECLARE @x`.
- Tables, views and table-valued functions after `FROM`, `JOIN`, `APPLY`, `UPDATE`, `INSERT INTO`, `DELETE FROM`, `MERGE`, `USING` and `TRUNCATE`/`DROP`/`ALTER TABLE`, also with a schema (`sales.|`). Without a schema they are inserted as `schema.table`.
- Columns of an alias or table (`u.|`, `dbo.Users.|`), and in expressions (`SELECT`, `WHERE`, `ON`, `GROUP BY`, `HAVING`, `ORDER BY`, `SET`) the columns of all sources of the block, aliases and common built-in functions. A column present in several sources is inserted with its alias.
- Query blocks: a subquery in an expression sees the outer query's sources, a derived table and a CTE do not; `UNION` separates blocks. Statements are separated even without semicolons.

Status: runs in SSMS 22.11; the items still to verify are in issue #7.

## Development environment

Core, Metadata and their tests can be developed in the cloud. The VSIX is built on Windows and SSMS does not run in the cloud, so the prototype and manual tests run on Windows with SSMS 22, possibly through Remote Control. Visual Studio is not required to build; the .NET SDK 8 or later is enough. A Windows runner in GitHub Actions also builds the VSIX (`.github/workflows/build.yml`). A SQL Server test instance in Docker is available locally (`.claude/skills/test-database/`).

## Open questions

- Name: "SQL Prompter" is very close to Redgate's SQL Prompt. Decide before the public open source release (MIT license).
- Entra ID with MFA: check `AuthenticationType` and whether our own connection can be created from `RenewableToken`. Until then no metadata is loaded for Entra ID and only keywords are offered. The user does not use Entra ID, so this has low priority.

## Sources

- [SSMS FAQ][faq]
- [Notes on an extension for SSMS 22 (sqlfluff-extension-ssms)][sqlfluff]
- [Modern completion walkthrough][completion]
- [`ScriptFactory.CurrentlyActiveWndConnectionInfo`][scriptfactory]
- [Microsoft SqlParser][sqlparser]

[faq]: https://learn.microsoft.com/en-sg/ssms/faq
[sqlfluff]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/issues/69
[completion]: https://github.com/Microsoft/vs-editor-api/wiki/Modern-completion-walkthrough
[scriptfactory]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.management.ui.vsintegration.editors.scriptfactory.currentlyactivewndconnectioninfo?view=sqlserver-2016
[sqlparser]: https://github.com/microsoft/SqlParser
