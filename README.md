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

- The completion list always comes from SQL Prompter; you can tell by the text **SQL Prompter** on the right of every item (option *Mark items with SQL Prompter*, on by default) and by the **SQL Prompter** filter button at the bottom. The built-in IntelliSense stays enabled for error underlining and quick info, but its list is not shown.
- Where the extension does not know the context (`EXEC`, `VALUES`, `SET @x =` …), it offers keywords, functions and data types. For new names (alias after `AS`, `@variable`, `#table`) it offers nothing.
- Metadata, including columns, starts loading when a query window is activated. Right after connecting to a new database, table and column suggestions may be missing for a moment.
- Tab, Enter and Space insert the selected suggestion. A dot, comma, parenthesis, semicolon, `]` or operator only closes the list and is typed, so `e.` keeps `e` and opens its columns. After a table source, where an alias is typed, the selection is only soft and only Tab inserts it.
- Settings: **Tools → Options → SQL Prompter → Behavior** (enable suggestions, commit keys, keyword case); changes apply at once. They are stored in `%APPDATA%\SqlPrompter\settings.json`. **SQL Prompter → Export Settings…** and **Import Settings…** copy them; import also reads a SQL Prompt `.settings` export (suggestions on/off and commit keys; Enter and Tab always insert).
- Choosing a table after `FROM`, `JOIN` or `APPLY` also inserts an alias made of the initials of the name (`OrderItems` → `dbo.OrderItems AS oi`; a clash with an alias in the query gives `oi1`). Not for `INSERT INTO`, `UPDATE`, `DELETE FROM`, table-valued functions or when an alias is already typed. Columns are inserted as `alias.Column`. Configure it in **Tools → Options → SQL Prompter → Aliases and Qualification** (custom aliases, ignored prefixes, `AS`, capitalising, schema qualification); the SQL Prompt import takes the alias options and custom aliases.
- After `SELECT` without a `FROM`, typing the start of a column name offers matching columns of all tables and views (table in the suffix, at most 2000, names starting with the text first; nothing before the first character is typed). Choosing one inserts it as `alias.Column` and adds `FROM dbo.Table AS alias` at the end of the select list; the alias options apply.
- Three-part names: after `FROM`, databases of the server are offered next to schemas; `OtherDb.`, `OtherDb..` and `OtherDb.dbo.` offer the objects of that database (loaded in the background on first use; a schema of the current database wins over a database of the same name). `USE Sales` on a line of its own makes completion follow that database from there on, also across `GO`. Synonyms are offered like tables and their columns come from the object they point to in the same database (chains included; targets on other databases or servers give no columns). Columns of tables in other databases and names with a server part are not supported yet.
- Linked servers: their names are offered after `FROM`; `Srv.` offers its databases, `Srv.Db.` the schemas and objects and a four-part source gets its columns, each loaded in the background when you get there (the first try is empty, the next one has them; a server that does not answer is only logged). Choosing a schema or database of the current database wins over a linked server of the same name.
- After `JOIN`, tables related by a foreign key to the tables of the query come first, each with the whole clause (`Orders AS o ON o.IdUser = u.IdUser`, several columns joined by `AND`); the key name is on the right. Columns show a key icon for primary and foreign keys.
- After `ON` (and `AND` / `OR` in the condition), conditions that link the joined table to the tables before it come first: the whole foreign key (several columns joined by `AND`) and each of its columns, then columns with the same name. Options page *Joins*: column order (by default the new table is on the right, `u.IdUser = o.IdUser`), single columns of keys, same-name matches (optionally only with the same type).
- Snippets are files in `%APPDATA%SqlPrompterSnippets` (or the folder set in *Tools → Options → SQL Prompter → Snippets*), one JSON file per snippet in the format of SQL Prompt's snippet folder; a folder copied from SQL Prompt works as is. *SQL Prompter → Import Snippets…* converts `.sqlpromptsnippet` backups (and copies `.json` files) into it; changes in the folder are picked up automatically. Expanding them in the completion list is the next step (#32).
- The table list also opens after typing a space after `FROM`, `JOIN` and similar keywords.
- After running a script that creates, alters or drops objects (or uses `sp_rename` or `SELECT … INTO`), the metadata of the window's database is refreshed within a few seconds. Otherwise changes are noticed at most 30 s later.

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
| Run `CREATE TABLE dbo.NewTable (Id int)`, wait a few seconds, then `SELECT * FROM New` | `dbo.NewTable` |
| Run `ALTER TABLE dbo.NewTable ADD Name int`, then `SELECT * FROM dbo.NewTable n WHERE n.` | `Name` among the columns |
| Window signed in with Entra ID | Keywords and functions, no tables or columns |

## License

The project will be released under the MIT license.
