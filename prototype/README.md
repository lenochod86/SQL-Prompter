# SQL Prompter prototype

A diagnostic VSIX extension that answered the three questions from [`docs/architecture.md`](../docs/architecture.md#prototype) in SSMS 22. Its code is not used by the MVP; it measures, it does not build anything.

**Status:** done. All three questions are answered (see [Results](#results) and `docs/architecture.md`). The prototype is kept as a reference and a diagnostic tool.

Everything is logged to `%LOCALAPPDATA%\SqlPrompter\prototype.log`. Lines are tagged `[host]`, `[q1]`, `[q2]` and `[q3]` and show the thread (`UI` = the UI thread).

## What it does

| File | Question | What it finds out |
|---|---|---|
| `PrototypePackage.cs` | precondition | Does the package load? Logs the process, architecture, DTE version, editor and SqlClient assembly versions and adds the **Tools → SQL Prompter (prototype)** menu. |
| `TextViewDiagnostics.cs` | 1, 2, 3 | For every view logs the content type including base types, the buffers in the graph and `IAsyncCompletionBroker.IsCompletionSupported`. For a SQL view logs the buffer property keys (a possible place for the window connection). Watches all asynchronous completion sessions and logs the item count per source and texts offered by several sources. |
| `HelloCompletionSource.cs` | 1 | `IAsyncCompletionSource` for content type `any` that offers `hello` and `hello_sql_prompter` in the SQL editor. |
| `LegacyCompletionProbe.cs` | 1, 2 | Probe of the legacy `ICompletionSource`: logs the other completion sets and adds its own. Off by default. |
| `ConnectionProbe.cs` | 3 | Finds `ScriptFactory.Instance.CurrentlyActiveWndConnectionInfo` through reflection, logs its structure (secrets hidden) and candidate `*ConnectionInfo` types. Optionally opens its own connection through the SqlClient already loaded in SSMS and runs `SELECT @@SERVERNAME, DB_NAME(), SUSER_SNAME()`. |

The SQL editor is recognized by a content type whose name or base type contains `sql`. If that heuristic fails, set the environment variable `SQLPROMPTER_CONTENT_TYPE` to the exact name from the log before starting SSMS.

## Build

The project is SDK-style, so neither Visual Studio nor the VS SDK is needed. The `Microsoft.VSSDK.BuildTools` package provides the VSIX tools and the SDK downloads the .NET Framework reference assemblies itself.

- On Windows with the .NET SDK 8 or later: `dotnet build prototype\SqlPrompter.Prototype\SqlPrompter.Prototype.csproj -c Release`.
- Or download the artifact of the [Prototype VSIX](../.github/workflows/prototype.yml) workflow from the GitHub Actions Windows runner.

The output is `prototype\SqlPrompter.Prototype\bin\Release\net48\SqlPrompter.Prototype.vsix`. [`nuget.config`](../nuget.config) in the repository root limits package sources to nuget.org so that the build does not depend on the local NuGet configuration.

## Installing into SSMS 22

The manifest targets `Microsoft.VisualStudio.Ssms` `[22.0,23.0)`, the same Id the built-in SSMS 22.11 extensions in `Common7\IDE\Extensions` use (with `Version="[22.0,)"`). VSIXInstaller installs it into SSMS 22.11 (verified).

1. Close SSMS and double-click the `.vsix`.
2. Start SSMS and check that the log contains `=== SQL Prompter prototype loaded ===`.

A newer version (higher `Version` in the manifest) installs over an older one. SSMS 22.11 has no extension manager in its menus, so uninstall from the command line while SSMS is closed. `ab48d67c` is the `InstallationID` from `Common7\IDE\SSMS.isolation.ini`:

```powershell
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" /instanceIds:ab48d67c /uninstall:SqlPrompter.Prototype.c9efd892-d89f-4178-8e0d-a9b7f925e617
```

The prototype and the MVP must not be installed at the same time, otherwise "hello" shows up in the MVP's list.

## Test procedure

Delete the log before every run so that results do not mix.

### Question 1: MEF and `IAsyncCompletionSource`

1. Open a new query window (connected and not connected).
2. Find `[q1] New view` in the log and note the SQL editor's content type name, its base types and the value of `asyncCompletionSupported`.
3. Type `hel` and also try Ctrl+Space.
4. Positive answer: `hello` is in the list and the log contains `GetOrCreate`, `InitializeCompletion` and `GetCompletionContextAsync`.
5. If the log only shows `New view` and nothing else, the editor apparently does not use the asynchronous API. Repeat the test with the legacy API probe (see below).

### Question 2: duplicates with the built-in IntelliSense

1. With the built-in IntelliSense enabled, type `SELECT * FROM sys.ob`.
2. Watch whether **one** list appears (`hello` and built-in items together) or **two** separate lists.
3. The `[q2] items by source` log line shows whether the built-in IntelliSense adds items to the same asynchronous session. If only `HelloCompletionSource` is listed, the built-in IntelliSense runs outside the modern API.
4. Use **Tools → SQL Prompter (prototype) → Hello Suggestions Enabled** to turn our items off and repeat, to compare the same place with and without our source.
5. Turn off IntelliSense (**Query → IntelliSense Enabled**, Ctrl+Q, I, or *Tools → Options → Text Editor → Transact-SQL → IntelliSense*) and repeat. Note whether only the built-in list disappears and whether `hello` stays.
6. Legacy API probe: close SSMS, set `SQLPROMPTER_LEGACY_PROBE=1` and start SSMS from the same console. The `[q2] Legacy completion session` log lines show the other completion sets. The probe is off by default because a legacy source may switch the editor to legacy completion and distort question 1.

### Question 3: connection of the active window

Repeat for every authentication type available: Windows, SQL Server and Entra ID with MFA (Interactive). Record each result in the table below.

1. Connect a query window to a server and database.
2. **Tools → SQL Prompter (prototype) → Dump Active Window Connection.** The log shows where `ScriptFactory` is, the structure of `CurrentlyActiveWndConnectionInfo` and a summary (server, database, authentication type, whether a password is available). Passwords and tokens are never logged.
3. If `ScriptFactory` does not exist, use the `Candidate types` dump and the buffer property keys from `[q1]/[q3] New view` as hints for a replacement.
4. **Test Own Connection (Read-Only).** Note whether the connection opened and how long it took. For Entra ID note whether a sign-in window appeared (the token is not shared with SSMS) or the connection succeeded without a prompt.
5. Switch the database in the SSMS drop-down and repeat step 2. Note whether the change shows in `AdvancedOptions[DATABASE]` or elsewhere.

## Results

| Item | Result | SSMS version, date |
|---|---|---|
| VSIX builds without Visual Studio | yes | 2026-11-02 |
| `InstallationTarget` Id | `Microsoft.VisualStudio.Ssms`, as used by the built-in extensions | 22.11.3, 2026-11-02 |
| VSIX installs into SSMS 22 | yes | 22.11.3, 2026-11-02 |
| Package loads | yes, in the background, commands registered | 22.11.3, 2026-11-02 |
| SQL editor content type and base types | `"SQL"` → `"code"` → `"text"` → `"any"`, on a `"projection"` buffer | 22.11.3, 2026-11-02 |
| `IAsyncCompletionSource` works in the SQL editor | yes: `hello` appears after `hel`, `IsCompletionSupported=True`, `GetCompletionContextAsync` runs off the UI thread | 22.11.3, 2026-11-02 |
| Built-in IntelliSense: same session / separate list / legacy API | none of them: while our source provides items, the built-in list is not shown; when it does not participate, the built-in one works (`sys.ob` with `hello` off and on) | 22.11.3, 2026-11-02 |
| Built-in IntelliSense can be turned off without losing `hello` | yes | 22.11.3, 2026-11-02 |
| `ScriptFactory.CurrentlyActiveWndConnectionInfo` exists | yes, through reflection: `SQLEditors.dll` 22.200, type `UIConnectionInfo` from `Microsoft.SqlServer.RegSvrEnum` | 22.11.3, 2026-11-02 |
| Connection: Windows | not verified | |
| Connection: SQL Server (password available?) | yes: data including the password available (`AuthenticationType=1`); own connection with the window's `Encrypt`/`TrustServerCertificate` OK in 91–114 ms | 22.11.3, 2026-11-02 |
| Connection: Entra ID MFA (sign in again?) | not verified | |
| Database change in the window is visible | yes, `AdvancedOptions[DATABASE]` follows the drop-down (`test` → `tempdb`) | 22.11.3, 2026-11-02 |

## Language client probe (architecture review, #183)

`SqlPrompter.LspProbe` (a VSIX with a `[ContentType("SQL")]` `ILanguageClient`) and `SqlPrompter.LspProbe.Server` (a minimal language server over stdio that logs every message to `%LOCALAPPDATA%\SqlPrompter\lspprobe.log`) check whether the Visual Studio language client can be used in SSMS 22. Result on SSMS 22.11 (2026-11-06): the part was composed into the MEF catalog, but the client was never instantiated (see `docs/architecture-review.md`). To repeat it: build both projects, copy the server output to `%LOCALAPPDATA%\SqlPrompter\lspprobe\`, install the VSIX, open a SQL window and read `lspprobe-client.log`; uninstall with `VSIXInstaller.exe /uninstall:SqlPrompter.LspProbe.4f1d2c3e-7a55-4a0e-9f0b-2b6a1d9c0e11`.
