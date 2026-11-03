# SQL Prompter

Custom IntelliSense for the SQL editor in SQL Server Management Studio 22. The design and the prototype results are in [docs/architecture.md](docs/architecture.md), the plan in [docs/roadmap.md](docs/roadmap.md).

**Status:** MVP. Runs in SSMS 22.11; unit tests pass.

## Structure

| Folder | Contents |
|---|---|
| `src/SqlPrompter.Core` | Lexer, caret context analysis and suggestion building (netstandard2.0, no dependencies) |
| `src/SqlPrompter.Metadata` | Per-database metadata snapshots loaded from `sys.*` in the background (netstandard2.0) |
| `src/SqlPrompter.Ssms` | VSIX for SSMS 22: completion source, window connection, commands (net48) |
| `tests/SqlPrompter.Tests` | Tests of Core and Metadata (current .NET) |
| `prototype/` | Diagnostic prototype that verified the basic assumptions |

## Build

The .NET SDK 8 or later is enough; Visual Studio is not needed.

```powershell
dotnet test tests\SqlPrompter.Tests
dotnet build src\SqlPrompter.Ssms\SqlPrompter.Ssms.csproj -c Release
```

The VSIX is created in `src\SqlPrompter.Ssms\bin\Release\net48\SqlPrompter.Ssms.vsix`. The tests run on any OS; the VSIX builds on Windows only.

## Installing into SSMS 22

1. Close SSMS.
2. If the prototype is installed, uninstall it, otherwise "hello" would show up in the list as well:
   ```powershell
   & "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" /uninstall:SqlPrompter.Prototype.c9efd892-d89f-4178-8e0d-a9b7f925e617
   ```
3. Double-click `SqlPrompter.Ssms.vsix`.

Uninstall: `VSIXInstaller.exe /uninstall:SqlPrompter.0d66644a-dd92-4b87-ae9d-5d20a2df11c6`.

If SSMS reports after removing an extension that a package (e.g. `PrototypePackage`) did not load, it still holds the old registration. Run `SSMS.exe /updateconfiguration` from the `Common7\IDE` folder while SSMS is closed.

The **SQL Prompter** menu with the **Refresh Metadata** and **Open Log** commands is under **Extensions** by default. Move it to the main menu bar in **Extensions → Customize Menu…** by clearing SQL Prompter. The log is `%LOCALAPPDATA%\SqlPrompter\sqlprompter.log` and contains only metadata loading and errors, never passwords or script contents.

## Behavior

- The completion list always comes from SQL Prompter; you can tell by the **SQL Prompter** filter button at the bottom. The built-in IntelliSense stays enabled for error underlining and quick info, but its list is not shown.
- Where the extension does not know the context (`EXEC`, `VALUES`, `SET @x =` …), it offers keywords, functions and data types. For new names (alias after `AS`, `@variable`, `#table`) it offers nothing.
- Metadata, including columns, starts loading when a query window is activated. Right after connecting to a new database, table and column suggestions may be missing for a moment.
- Tab, Enter, Space, dot, comma, parentheses and operators insert the selected suggestion. After a table source, where an alias is typed, the selection is only soft and only Tab inserts it.
- The table list also opens after typing a space after `FROM`, `JOIN` and similar keywords.

## Manual test in SSMS

Connect a window to a database with a few tables and try:

| Type | Expected |
|---|---|
| `sel` | Keywords, `select` in lower case |
| `SELECT * FROM ` | Tables and views as `dbo.Table`, schemas |
| `SELECT * FROM sys.ob` | `objects` and other system views |
| `SELECT * FROM dbo.T t WHERE t.` | Columns of the table |
| `SELECT  FROM dbo.A a JOIN dbo.B b ON 1=1` with the caret after `SELECT` and Ctrl+Space | Columns of both tables, shared ones with their alias |
| `SELECT * FROM dbo.T t ` | `WHERE`, `JOIN` … without a firm selection: Enter starts a new line |
| `DECLARE @x int; SELECT @` | No list (the extension does not know variables yet) |
| `SELECT CAST(1 AS ` | Data types |
| `EXEC ` and Ctrl+Space | Keywords and functions without a firm selection |
| Window signed in with Entra ID | Keywords and functions, no tables or columns |

## License

The project will be released under the MIT license.
