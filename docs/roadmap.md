# SQL Prompter: roadmap

The goal is to replace Redgate SQL Prompt in everyday work in SSMS 22. The architecture and verified facts about SSMS are in [architecture.md](architecture.md); this document covers what to build and in which order. Each item links to its GitHub issue once one exists.

**Status:** order and decisions agreed on 2026-11-03, see [Decisions](#decisions) at the end.

## User priorities

1. **IntelliSense** that offers everything and can be relied on, including JOIN suggestions and automatic aliases.
2. **Snippets**, including the user's existing SQL Prompt snippets.
3. **Query tab coloring** by server and database.
4. **F12**: open the code of a procedure, view, function or trigger in a new query window.
5. **Shortcuts:** F12, Ctrl+Space, Shift+F5 (run the statement at the caret). More will follow.

The user uses almost all SQL Prompt features, most of all IntelliSense, tab colors, F12, snippets and aliases. Formatting is wanted too, but last.

Environment:
- databases have over a hundred tables and many procedures, views and triggers;
- sign-in with Windows authentication and SQL logins; Entra ID is not used;
- a single user for now, later open source under the MIT license.

## Source material

The user provided an export from SQL Prompt 10.14:
- settings (`Prompt.settings`, a ZIP with XML files);
- 127 snippets in JSON and their XML backup;
- a custom formatting style in two formats;
- screenshots of options and features.

The material stays outside the repository and is never copied into it, because it contains server and database names and internal snippets. Import tests therefore use our own sample data with the same structure.

SQL Prompt binaries are neither analyzed nor used. It is proprietary software; exports and behavior descriptions are enough.

What the export shows the user relies on:

| Area | SQL Prompt setting | Phase |
|---|---|---|
| Commit keys | Enter, Space and Tab commit; dot, comma, parentheses and semicolon do not | 0 |
| Qualification | Objects with schema, columns with alias | 1 |
| Aliases | Automatic aliases with `AS`, 10 custom aliases (table name → alias), no ignored prefixes | 1 |
| Columns | All database columns after `SELECT`, columns in table order | 1 |
| JOINs | By foreign keys, matching column names and individual columns of multi-column keys; swapped column order in the condition | 2 |
| Snippets | Placeholders `$CURSOR$`, `$SELECTEDTEXT$`, `$SELECTIONSTART$`/`$SELECTIONEND$`, `$DATE(format)$`, `$USER$`, `$DBNAME$` and custom fields with default values | 3 |
| Colors | 8 environments (name and color) and 17 server/database → environment rules, color also in the status bar | 4 |
| Inserted code | Full `INSERT` without `VALUES`, procedure parameters with default values and `DECLARE` for OUTPUT, parentheses for functions, `ALTER` when editing an object | 5, 6 |
| Other | Query history and tab restore, code analysis, execution warnings, highlighting matching objects, auto-closing `'` and `[` | 7 |
| Formatting | Custom style (tabs, leading commas, aligned aliases, upper case …) and actions: `N` prefix for strings, semicolons, removing unnecessary brackets, adding `AS` | 8 |

## Phases

Every phase ends with a VSIX the user tests manually. Sizes are rough: **S** days, **M** one or two weeks, **L** several weeks. Done items are `[x]`, open items `[ ]`.

### Done: prototype and MVP

- [x] Prototype: MEF and `IAsyncCompletionSource` in the SSMS 22 SQL editor, suppressing the built-in list, connection through `ScriptFactory`
- [x] VSIX builds without Visual Studio (`dotnet build`), CI for tests and VSIX
- [x] Tolerant lexer, statement splitting without semicolons, query blocks, table sources and aliases
- [x] Keywords, schemas, tables, views, table-valued functions, columns, aliases, built-in functions, data types
- [x] Alias columns (`e.`), columns of all sources in an expression, outer query columns in a subquery
- [x] Only the SQL Prompter list (the built-in one is not shown), "SQL Prompter" filter in the list
- [x] Metadata loaded in the background when a window is activated, user object columns with the objects, change check every 30 s
- [x] SQL login authentication
- [x] SQL Prompter menu (Refresh Metadata, Open Log), movable to the main menu bar
- [x] Test database skill for Claude Code (#3)
- [x] Repository text in English (#5)

### Phase 0: MVP polish (S)

- [x] Commit keys like SQL Prompt: Enter, Space and Tab commit; dot, comma, parentheses, semicolon and `]` do not commit, they close the list and are typed (a dot then opens the columns) (#6)
- [x] Configurable commit keys (#9)
- [x] Make the SQL Prompter list recognizable at a glance (own icon or marker, like SQL Prompt's list); today it is hard to tell it from the built-in one while testing (#20)
- [x] Research: our own completion list (presenter) in place of the standard one; prototype with type, highlight and alias columns (#61)
- [x] Verify in SSMS: empty list for variables, SQL Prompter filter, soft selection while typing an alias (#7)
- [x] Verify performance on a large database (#7)
- [x] Verify Windows authentication (#7)
- [x] Refresh metadata after DDL executed in the window (`CREATE`, `ALTER`, `DROP`) (#8)
- [x] CI checks: VSIX and menu version bumps, one build per push, warnings as errors, Dependabot (#17)

### Phase 1: Script knowledge and aliases (M) (#22)

- [x] **Settings foundation:** pages in Tools → Options → SQL Prompter, JSON file, export and import. Every later feature adds its options (#9).
- [x] **SQL Prompt settings import** (`.settings`): aliases, qualification, commit keys. Other parts follow with later phases (#9).
- [ ] **Script model**, i.e. definitions in the script computed in the background (#15):
  - [ ] columns of CTEs and derived tables
  - [ ] `#tables` from `CREATE TABLE` and `SELECT … INTO`
  - [ ] `@tables`
  - [ ] variables and parameters with types, including the parameters of the procedure being edited
- [ ] **Automatic aliases** when inserting a table (#23):
  - [x] from the capital letters of the name (`OrderItems` → `oi`), avoiding collisions in the query
  - [ ] custom aliases
  - [ ] ignored prefixes
  - [ ] `AS` option
- [x] **Qualification:** objects with schema, columns with alias (`a.Column`) (#23)
- [x] After `SELECT` without `FROM`, columns of all tables in the database; choosing a column adds `FROM` with its table and alias (#24)
- [x] Three-part names and tracking `USE` (#25)
- [x] Synonyms (#25)
- [x] **Linked servers, loaded lazily**, so that the initial load stays fast even for a customer with five linked servers full of tables (#26):
  - [x] only their names first (`sys.servers`)
  - [x] the server's databases after choosing a server
  - [x] schemas and objects after choosing a database
  - [x] columns only when a table is used

### Phase 2: JOINs (M) (#27)

- [x] Metadata of primary and foreign keys, including multi-column keys (#28)
- [x] After `JOIN`, tables linked by a foreign key to the tables in the query first, with an item for the whole clause `Table AS t ON t.IdX = x.IdX` (#28)
- [x] Complete conditions after `ON` (#29):
  - [x] by foreign keys
  - [x] by matching column names
  - [ ] optionally by matching types
  - [x] individual columns of multi-column keys
- [x] Option for the column order in the condition (new table left or right) (#29)
- [x] Tests on a schema similar to real ones (convention `IdTable` ↔ `Table.IdTable`) (#29, #100)

### Phase 3: Snippets (M) (#30)

- [x] **Storage in the same JSON format as SQL Prompt** (`prefix`, `description`, `body`, `placeholders`). An existing snippet folder can be read directly or copied. (#31)
- [x] Import from the XML backup (`.sqlpromptsnippet`) (#31)
- [x] Snippet suggestions in the list with their own icon and filter; expanding a shortcut with Tab (#32)
- [x] Placeholders: `$CURSOR$`, `$SELECTEDTEXT$`, `$SELECTIONSTART$`/`$SELECTIONEND$`, `$DATE(format)$`, `$USER$`, `$DBNAME$` (#32)
- [x] **Custom fields directly in the text:** (#33)
  - highlighted and navigated with Tab;
  - a repeated field is updated everywhere at once;
  - Enter or Esc ends editing and the caret ends at `$CURSOR$`.
- [x] Snippets on a selection (`$SELECTEDTEXT$`, e.g. wrapping the selection in `ISNULL(…)`) from a selection actions menu (#34)
- [x] Snippet management: list, editing, new snippet from a selection (#34)

### Phase 4: Coloring by server and database (S to M, research first) (#35)

- [x] **Research (tab verified; frame and status bar move to #37):** how to color the document tab, the window frame and the status bar in SSMS 22. SSMS only has a custom connection color in the status bar, set in the connect dialog. There is no verified API for tab or frame colors. (#36)
- [x] Tab color (#37)
- [x] Query window frame color (the border of the editor pane follows the tab accent) (#37)
- [x] Status bar color (the shell status bar; SSMS own per-window bar is not touched) (#37)
- [x] Environments (name and color) and server + database → environment rules with wildcards (#38)
- [ ] Import of SQL Prompt colors (#38): implemented and unit tested, not yet run through the menu in SSMS

### Phase 5: Navigation and execution (S to M, research first) (#39)

- [x] **Research:** opening a new window with a connection. Candidate: `ScriptFactory.CreateNewBlankScript` with the current window's connection, which we can already read. (#40)
- [x] **F12 on the object at the caret:** (#41)
  - [x] a procedure, view, function or trigger opens in a new window on the same server and database as `CREATE OR ALTER`
  - [x] a table opens its `CREATE TABLE` script
- [x] **Shift+F5: run the statement at the caret.** The existing statement splitting finds its bounds; the statement is selected, run through Query.Execute and the selection restored. Check for a shortcut conflict. (#42)
- [ ] Ctrl+F12: show the object in Object Explorer (later) (#41)

### Phase 6: Extended IntelliSense (L) (#43)

- [x] `INSERT`: full statement with the column list, optionally with `VALUES` and default values (#44)
- [x] `EXEC`: procedures with parameters, default values and `DECLARE` for output parameters (#44)
- [x] Functions with parentheses (the caret goes inside when they take parameters); parameter names come with parameter info (#44, #45)
- [x] Expanding `*` with Tab into a column list with aliases (#45)
- [x] Object definition panel next to the list (`CREATE` script, as in SQL Prompt) (#46)
- [x] Parameter info (#45)
- [x] Ranking by usage (statistics stay local, nothing is sent anywhere) (#47)

### Phase 7: Tools (M to L) (#48)

The user uses almost all of them. Agreed order (decision 16): #52 editor helpers, #49 history and tabs, #51 warnings and analysis, #53 script as INSERT, #50 refactoring.

- [x] Query history: every run with server, database, time and closing time, a window with filter and preview, opening in a new tab (#102)
- [ ] Restoring tabs when SSMS starts (#103)
- [ ] Refactoring (#50):
  - [ ] qualify names
  - [ ] expand `*`
  - [ ] insert semicolons
  - [ ] remove square brackets
  - [ ] unused variables
  - [ ] script summary
- [x] Execution warnings: `DELETE` or `UPDATE` without `WHERE` ask for confirmation (#106)
- [ ] Code analysis (#51)
- [x] Selection actions: add commas, add quotes and commas (#52)
- [x] Highlighting matching objects in the script (#52)
- [x] Auto-closing `'` and `[` (#52)
- [ ] Script as INSERT from the results grid (not yet known whether SSMS 22 allows it) (#53)

### Phase 8: Formatting (L) (#54)

Wanted, but not a priority, so it comes last.

- [ ] Formatter over tokens with structure from ScriptDOM; keeps comments and refuses a change that would alter the meaning (#55)
- [ ] Options following the SQL Prompt style and import of a `.json` style (the user's style is the main test case) (#56)
- [ ] Formatting actions: `N` prefix for strings, semicolons, removing unnecessary brackets, adding or removing `AS`, casing, optionally qualification and expanding `*` (#57)
- [ ] Formatting the document or the selection (#57)
- [ ] Upper case while typing (#57)

### Phase 9: GUI enhancements (M to L) (#83)

The settings are text-based today (colors as `Name=#RRGGBB`, rules as delimited strings, snippet placeholders as text). This phase replaces them with proper graphical controls. It comes last, after formatting. More items are added when they come up.

- [ ] User friendly editing of the coloring settings: a color picker, environments and rules as editable lists with a dropdown of environments, validation in the UI (#84)
- [ ] Snippet management: placeholders as a table, SQL coloring of the snippet text, a cleaner list without descriptions (#88)

## Risks

- **Unsupported SSMS API.** Tab coloring, F12 into a new window and running a selection depend on APIs that may not exist or may change. Those phases therefore start with research.
- **Formatting** is the most laborious part and a bug in it damages the script. Protection: token comparison, golden tests and formatting only on the user's command.
- **Performance** with large scripts and databases: script model per batch between `GO` lines, metadata cached per database.

## Decisions

By the user on 2026-11-03:

1. **Commit keys** as in the exported settings: Enter, Space and Tab commit; dot, comma, parentheses, semicolon and `]` do not. The keys are configurable.
2. **Custom snippet fields:** directly in the text as in Visual Studio. The snippet is inserted with default values and the fields are highlighted. Tab moves to the next field and the same field is updated everywhere at once. No dialog as in SQL Prompt.
3. **Coloring:** the tab and, if possible, the window frame (and the status bar).
4. **F12:** procedures, views, functions and triggers as `CREATE OR ALTER`, tables as `CREATE TABLE`.
5. **`SELECT` without `FROM`:** choosing a column adds `FROM` with its table.
6. **Formatting** last.
7. **Tools:** almost all are used; the order is agreed before phase 7.
8. **Linked servers:** yes, but lazily. Names first, objects only after choosing a server.
9. **Language:** everything in the repository is in English; the conversation with the user stays in Czech.
10. **Workflow:** an issue, a branch and a squash-merged pull request for every piece of work; GitHub deletes merged branches automatically.

By the user on 2026-11-04:

11. **GUI enhancements** come as the last phase, after formatting. The first item is a user friendly editor for the coloring settings, because typing color values as text is too hard to use. The snippet management window follows.

By the user on 2026-11-04 (answers on #44):

12. **INSERT:** the column list is offered in three ways chosen in the settings: automatically when the table is chosen after `INSERT INTO` (default), as a second item, or after typing `(` behind the table name. Identity, computed and rowversion columns are left out; columns with defaults and nullable ones are included. Layout (setting): one line, each column on its own line (default), or as many as fit the editor width. After the column list (setting): a new line (default) or `VALUES` with a placeholder per column (`''`, `N''`, `0`, `NULL` for nullable columns).
13. **EXEC:** after the procedure all parameters are inserted, named, each on its own line aligned with the first one, with the default value as written in the procedure (`NULL` when it cannot be read); OUTPUT parameters get `DECLARE` lines above the `EXEC` and are passed as `@p = @p OUTPUT`.

By the user on 2026-11-04 (answers on #45):

14. **Expanding `*`:** Tab with the caret right after the `*`; one column per line, qualified with the alias where the table has one (no alias is made up); all columns of all tables in source order, `a.*` expands only `a`; always all columns (identity and rowversion included).
15. **Parameter info:** a tooltip with the signature and the current parameter in bold, for the functions and procedures of the database (not the built-in functions; a separate issue if wanted later). It shows automatically after `(` and `,` and on Ctrl+Shift+Space.

By the user on 2026-11-05 (answers on #48 and #49):

16. **Order of phase 7** by use: #52, #49, #51, #53, #50.
17. **Query history (#49):** records the text, server, database, the time of the run and, when the query tab is closed, the time it was closed; all runs; kept forever by default with an option to keep only N days; no exclusion of servers or databases; the plain text lives in a local file. A window like the snippet manager (filter, list, text preview), no shortcut for now; Enter or double click opens the query in a new tab on the same server and database. History comes before tab restoration.
18. **Restoring tabs (#49):** all tabs, unsaved text included; tabs reconnect automatically. Passwords are never stored: tabs that need a password open unconnected with the server and database remembered (option (a) on #49), after research on what SSMS itself exposes.
