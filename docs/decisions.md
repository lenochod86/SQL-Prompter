# Decisions

Decisions of the user, recorded in the order they were made. New decisions are written on the GitHub issue they belong to; this file keeps the earlier ones (it used to be the Decisions section of the roadmap, which is gone: work is planned from the issues only).

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
19. **Code analysis (#51)**: squiggles with a tooltip, run in the background; the user agreed with the proposed rules. Severities proposed by Claude: warnings = `= NULL`, JOIN without ON; hints = INSERT without columns, unused variable; `SELECT *` and NOLOCK off by default.

By the user on 2026-11-06 (answer on #183, architecture review):

20. **Architecture:** the in-process VSIX stays (see [architecture-review.md](architecture-review.md)). Improvements, each its own issue: a compatibility gate for the undocumented SSMS access (#186), the typing path through our own list controller so that there is one list implementation (#187), one sort and filter implementation (#188), and CI enforcement of the dependency boundary of Core and Metadata (#189). A language server and VisualStudio.Extensibility are not pursued; revisit if SSMS documents out-of-process extensions with an editor completion API or breaks the in-process model.

By the user on 2026-11-10 (issue #417):

21. **Built-in snippets (#417):** the setting *Install the built-in snippets* is removed; the built-in snippets are installed once each automatically, and the button *Reset built-in snippets...* in Settings > Snippets (with a Yes / No confirmation, default No) always writes all of them back in their original text, also deleted and edited ones; a snippet of the user with the prefix of a built-in one is replaced too, the other snippets are not touched. *Open folder* moved next to *Snippet folder* and *Import snippets...* to the Snippets page of the settings; the *Reload* button of the snippet editor was removed (the folders are watched). The editor has one row of buttons with Close at the right edge.

By the user on 2026-11-10 (issue #420):

22. **Ctrl+Shift+F5 (#420):** if the key is already bound to another command in the SQL query editor, the binding is overwritten (unlike Shift+F5, which is set only when free); the log names the command that lost it.
