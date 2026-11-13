using System;
using System.Collections.Generic;
using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Analysis
{
    internal static partial class CatalogRules
    {
        private static readonly HashSet<string> TableHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NOLOCK", "HOLDLOCK", "READUNCOMMITTED", "READCOMMITTED", "REPEATABLEREAD", "SERIALIZABLE", "UPDLOCK", "ROWLOCK", "PAGLOCK", "TABLOCK", "TABLOCKX", "XLOCK",
            "READPAST", "NOWAIT", "READCOMMITTEDLOCK", "FORCESEEK", "FORCESCAN", "INDEX", "NOEXPAND", "SNAPSHOT",
        };

        private static readonly Dictionary<string, string> DeprecatedSystemTables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sysobjects"] = "sys.objects", ["syscolumns"] = "sys.columns", ["sysindexes"] = "sys.indexes, sys.partitions and sys.allocation_units", ["sysindexkeys"] = "sys.index_columns",
            ["sysusers"] = "sys.database_principals", ["sysdatabases"] = "sys.databases", ["sysfiles"] = "sys.database_files", ["sysfilegroups"] = "sys.filegroups",
            ["syslogins"] = "sys.server_principals and sys.sql_logins", ["sysprocesses"] = "sys.dm_exec_sessions, sys.dm_exec_requests and sys.dm_exec_connections",
            ["sysmessages"] = "sys.messages", ["sysconstraints"] = "sys.check_constraints, sys.default_constraints and sys.key_constraints", ["sysdepends"] = "sys.sql_expression_dependencies",
            ["sysforeignkeys"] = "sys.foreign_key_columns", ["sysreferences"] = "sys.foreign_keys", ["sysmembers"] = "sys.database_role_members", ["syscomments"] = "sys.sql_modules",
            ["systypes"] = "sys.types", ["sysxlogins"] = "sys.server_principals", ["syscurconfigs"] = "sys.configurations", ["sysaltfiles"] = "sys.master_files",
            ["sysdevices"] = "sys.backup_devices", ["syscharsets"] = "sys.syscharsets (compatibility only; use the collation functions)", ["sysservers"] = "sys.servers",
            ["sysperfinfo"] = "sys.dm_os_performance_counters", ["syscacheobjects"] = "sys.dm_exec_cached_plans and sys.dm_exec_plan_attributes", ["syslockinfo"] = "sys.dm_tran_locks",
            ["sysprotects"] = "sys.database_permissions", ["sysfulltextcatalogs"] = "sys.fulltext_catalogs", ["sysremotelogins"] = "sys.remote_logins", ["sysoledbusers"] = "sys.linked_logins",
        };

        private static readonly Dictionary<string, string> DeprecatedProcedures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sp_dboption"] = "ALTER DATABASE ... SET", ["sp_addtype"] = "CREATE TYPE", ["sp_droptype"] = "DROP TYPE", ["sp_bindefault"] = "DEFAULT constraints", ["sp_unbindefault"] = "DEFAULT constraints",
            ["sp_bindrule"] = "CHECK constraints", ["sp_unbindrule"] = "CHECK constraints", ["sp_change_users_login"] = "ALTER USER", ["sp_addserver"] = "sp_addlinkedserver", ["sp_addextendedproc"] = "CLR integration",
            ["sp_dropextendedproc"] = "CLR integration", ["sp_attach_db"] = "CREATE DATABASE ... FOR ATTACH", ["sp_attach_single_file_db"] = "CREATE DATABASE ... FOR ATTACH", ["sp_certify_removable"] = "CREATE DATABASE",
            ["sp_create_removable"] = "CREATE DATABASE", ["sp_dbremove"] = "DROP DATABASE", ["sp_renamedb"] = "ALTER DATABASE ... MODIFY NAME", ["sp_helpdevice"] = "sys.backup_devices",
            ["sp_addlogin"] = "CREATE LOGIN", ["sp_droplogin"] = "DROP LOGIN", ["sp_adduser"] = "CREATE USER", ["sp_dropuser"] = "DROP USER", ["sp_grantdbaccess"] = "CREATE USER",
            ["sp_revokedbaccess"] = "DROP USER", ["sp_grantlogin"] = "CREATE LOGIN", ["sp_revokelogin"] = "DROP LOGIN", ["sp_denylogin"] = "ALTER LOGIN ... DISABLE", ["sp_changeobjectowner"] = "ALTER SCHEMA ... TRANSFER",
            ["sp_addalias"] = "database users and roles", ["sp_dropalias"] = "database users and roles", ["sp_resetstatus"] = "ALTER DATABASE", ["sp_addremotelogin"] = "linked servers", ["sp_dropremotelogin"] = "linked servers",
            ["sp_remoteoption"] = "linked servers", ["sp_helpremotelogin"] = "linked servers", ["sp_fulltext_database"] = "CREATE FULLTEXT CATALOG", ["sp_fulltext_catalog"] = "CREATE FULLTEXT CATALOG",
            ["sp_fulltext_table"] = "CREATE FULLTEXT INDEX", ["sp_fulltext_column"] = "CREATE FULLTEXT INDEX",
        };

        private static readonly Dictionary<string, string> DeprecatedFunctions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fn_virtualfilestats"] = "sys.dm_io_virtual_file_stats", ["fn_servershareddrives"] = "sys.dm_io_cluster_shared_drives", ["fn_get_sql"] = "sys.dm_exec_sql_text",
            ["fn_virtualservernodes"] = "sys.dm_os_cluster_nodes", ["fn_trace_geteventinfo"] = "Extended Events", ["fn_trace_getfilterinfo"] = "Extended Events",
            ["fn_trace_getinfo"] = "Extended Events", ["fn_trace_gettable"] = "Extended Events",
        };

        private static void Deprecated(RuleContext c)
        {
            var t = c.T;
            var kinds = new[]
            {
                CodeIssueKind.DEP001, CodeIssueKind.DEP002, CodeIssueKind.DEP003, CodeIssueKind.DEP006, CodeIssueKind.DEP007, CodeIssueKind.DEP009, CodeIssueKind.DEP011, CodeIssueKind.DEP012,
                CodeIssueKind.DEP013, CodeIssueKind.DEP014, CodeIssueKind.DEP015, CodeIssueKind.DEP016, CodeIssueKind.DEP018, CodeIssueKind.DEP019, CodeIssueKind.DEP020, CodeIssueKind.DEP021, CodeIssueKind.ST002,
                CodeIssueKind.DEP022, CodeIssueKind.DEP025, CodeIssueKind.DEP026, CodeIssueKind.DEP027,
            };
            if (!kinds.Any(c.On))
            {
                return;
            }

            for (var i = 0; i < t.Count; i++)
            {
                var token = t[i];
                if (token.Kind == TokenKind.OpenParen && c.On(CodeIssueKind.DEP001))
                {
                    HintWithoutWith(c, i);
                }

                if (token.Kind != TokenKind.Word)
                {
                    continue;
                }

                var statementStart = c.StatementAt(i)[0] == i;
                switch (token.Upper)
                {
                    case "WRITETEXT":
                    case "UPDATETEXT":
                    case "READTEXT":
                        if (c.On(CodeIssueKind.DEP002) && statementStart)
                        {
                            c.Add(CodeIssueKind.DEP002, token, token.Text.ToUpperInvariant() + " is deprecated; use the varchar(max), nvarchar(max) and varbinary(max) types and the .WRITE clause of UPDATE.");
                        }

                        break;
                    case "GROUP":
                        if (c.On(CodeIssueKind.DEP003) && c.Is(i + 1, "BY") && c.Is(i + 2, "ALL"))
                        {
                            c.Add(CodeIssueKind.DEP003, t[i + 2].Start, 3, "GROUP BY ALL is deprecated; use a LEFT JOIN or a UNION ALL for the empty groups.");
                        }

                        break;
                    case "SETUSER":
                        if (c.On(CodeIssueKind.DEP006) && statementStart)
                        {
                            c.Add(CodeIssueKind.DEP006, token, "SETUSER is deprecated; use EXECUTE AS.");
                        }

                        break;
                    case "TAPE":
                        if (c.On(CodeIssueKind.DEP007) && c.IsOp(i + 1, "="))
                        {
                            c.Add(CodeIssueKind.DEP007, token, "Backup to tape is deprecated; back up to disk or to a URL.");
                        }

                        break;
                    case "DBCC":
                        Dbcc(c, i);
                        break;
                    case "SET":
                        if (statementStart)
                        {
                            SetOptions(c, i);
                        }

                        break;
                    case "READONLY":
                    case "READWRITE":
                        if (c.On(CodeIssueKind.DEP015) && c.Is(c.StatementAt(i)[0], "ALTER") && c.Is(c.StatementAt(i)[0] + 1, "DATABASE"))
                        {
                            c.Add(CodeIssueKind.DEP015, token, token.Text.ToUpperInvariant() + " is deprecated; use " + (token.Upper == "READONLY" ? "READ_ONLY" : "READ_WRITE") + ".");
                        }

                        break;
                    case "TORN_PAGE_DETECTION":
                        if (c.On(CodeIssueKind.DEP016))
                        {
                            c.Add(CodeIssueKind.DEP016, token, "TORN_PAGE_DETECTION is deprecated; use PAGE_VERIFY TORN_PAGE_DETECTION.");
                        }

                        break;
                    case "GRANT":
                    case "DENY":
                    case "REVOKE":
                        if (c.On(CodeIssueKind.DEP018) && statementStart && c.Is(i + 1, "ALL"))
                        {
                            c.Add(CodeIssueKind.DEP018, t[i + 1], "The ALL permission is deprecated; name the permissions.");
                        }

                        break;
                    case "DROP":
                        if (c.On(CodeIssueKind.DEP022) && c.Is(i + 1, "INDEX"))
                        {
                            DropIndexOldForm(c, i + 2);
                        }

                        break;
                }

                if (c.On(CodeIssueKind.DEP019) && DeprecatedSystemTables.TryGetValue(token.Text, out var replacement) && !c.IsKind(i + 1, TokenKind.OpenParen))
                {
                    c.Add(CodeIssueKind.DEP019, token, token.Text + " is a deprecated compatibility view; use " + replacement + ".");
                }

                if (c.On(CodeIssueKind.DEP025) && DeprecatedProcedures.TryGetValue(token.Text, out var procedureReplacement))
                {
                    c.Add(CodeIssueKind.DEP025, token, token.Text + " is deprecated; use " + procedureReplacement + ".");
                }

                if (c.On(CodeIssueKind.DEP027) && DeprecatedFunctions.TryGetValue(token.Text, out var functionReplacement))
                {
                    c.Add(CodeIssueKind.DEP027, token, token.Text + " is deprecated; use " + functionReplacement + ".");
                }
            }

            if (c.On(CodeIssueKind.DEP020))
            {
                NumberedProcedures(c);
            }

            if (c.On(CodeIssueKind.DEP021) || c.On(CodeIssueKind.DEP026) || c.On(CodeIssueKind.ST002))
            {
                SelectLists(c);
            }
        }

        private static void HintWithoutWith(RuleContext c, int open)
        {
            // FROM t (NOLOCK) / FROM t AS a (NOLOCK): the hint list of a table source without WITH.
            var first = open + 1;
            if (!c.IsKind(first, TokenKind.Word) || !TableHints.Contains(c.T[first].Text) || c.Is(open - 1, "WITH") || open == 0 || !c.T[open - 1].IsNamePart || c.T[open - 1].IsWord("IN"))
            {
                return;
            }

            // INDEX is also the start of CREATE INDEX name (...): a hint follows a table name or an alias after FROM, JOIN, a comma, or AS.
            var p = open - 1;
            if (c.Is(p - 1, "AS"))
            {
                p -= 2;
            }

            while (p > 1 && c.IsKind(p - 1, TokenKind.Dot))
            {
                p -= 2;
            }

            var before = p - 1;
            var inSource = c.Is(before, "FROM") || c.Is(before, "JOIN") || c.IsKind(before, TokenKind.Comma) || c.Is(before, "UPDATE") || c.Is(before, "INTO") || c.Is(before, "DELETE");
            if (!inSource)
            {
                return;
            }

            if (c.Is(first, "INDEX") && !(c.IsKind(first + 1, TokenKind.OpenParen) || c.IsOp(first + 1, "=")))
            {
                return;
            }

            c.Add(CodeIssueKind.DEP001, c.T[open], "A table hint without the WITH keyword is deprecated; write WITH (" + c.T[first].Text.ToUpperInvariant() + ").", new CodeFix(c.T[open].Start, 0, "WITH "));
        }

        private static void Dbcc(RuleContext c, int i)
        {
            var next = c.UpperAt(i + 1);
            if (next == "DBREINDEX" && c.On(CodeIssueKind.DEP009))
            {
                c.Add(CodeIssueKind.DEP009, c.T[i + 1], "DBCC DBREINDEX is deprecated; use ALTER INDEX ... REBUILD.");
            }
            else if (next == "INDEXDEFRAG" && c.On(CodeIssueKind.DEP011))
            {
                c.Add(CodeIssueKind.DEP011, c.T[i + 1], "DBCC INDEXDEFRAG is deprecated; use ALTER INDEX ... REORGANIZE.");
            }
            else if (next == "SHOWCONTIG" && c.On(CodeIssueKind.DEP012))
            {
                c.Add(CodeIssueKind.DEP012, c.T[i + 1], "DBCC SHOWCONTIG is deprecated; use sys.dm_db_index_physical_stats.");
            }
        }

        private static void SetOptions(RuleContext c, int i)
        {
            var option = c.UpperAt(i + 1);
            var value = c.UpperAt(i + 2);
            if (option == "ROWCOUNT")
            {
                if (c.On(CodeIssueKind.DEP014) && !(c.IsKind(i + 2, TokenKind.Number) && c.T[i + 2].Text == "0"))
                {
                    c.Add(CodeIssueKind.DEP014, c.T[i + 1], "SET ROWCOUNT does not affect INSERT, UPDATE and DELETE in future versions; use TOP.");
                }

                return;
            }

            if (!c.On(CodeIssueKind.DEP013))
            {
                return;
            }

            if ((option == "ANSI_NULLS" || option == "ANSI_PADDING" || option == "CONCAT_NULL_YIELDS_NULL") && value == "OFF")
            {
                c.Add(CodeIssueKind.DEP013, c.T[i + 1].Start, c.T[i + 2].End - c.T[i + 1].Start, "SET " + option + " OFF is deprecated; it will always be ON.");
            }
            else if (option == "OFFSETS")
            {
                c.Add(CodeIssueKind.DEP013, c.T[i + 1], "SET OFFSETS is deprecated.");
            }
        }

        private static void DropIndexOldForm(RuleContext c, int from)
        {
            var j = from;
            if (c.Is(j, "IF") && c.Is(j + 1, "EXISTS"))
            {
                j += 2;
            }

            if (!c.IsKind(j, TokenKind.Word) && !c.IsKind(j, TokenKind.QuotedIdentifier))
            {
                return;
            }

            var k = j;
            var parts = 1;
            while (c.IsKind(k + 1, TokenKind.Dot) && c.T.Count > k + 2 && c.T[k + 2].IsNamePart)
            {
                k += 2;
                parts++;
            }

            if (parts >= 2 && !c.Is(k + 1, "ON"))
            {
                c.Add(CodeIssueKind.DEP022, c.T[j].Start, c.T[k].End - c.T[j].Start, "DROP INDEX table.index is deprecated; write DROP INDEX index ON table.");
            }
        }

        private static void NumberedProcedures(RuleContext c)
        {
            foreach (var m in c.Modules.Where(m => m.Kind == "PROC" && m.Name >= 0))
            {
                if (c.IsKind(m.Name + 1, TokenKind.Semicolon) && c.IsKind(m.Name + 2, TokenKind.Number) && c.Touches(m.Name, m.Name + 1))
                {
                    c.Add(CodeIssueKind.DEP020, c.T[m.Name + 1].Start, c.T[m.Name + 2].End - c.T[m.Name + 1].Start, "Numbered procedures are deprecated; create a separate procedure.");
                }
            }
        }

        private static readonly HashSet<string> NotAnAliasStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WHEN", "THEN", "ELSE", "AND", "OR", "NOT", "IN", "LIKE", "IS", "BETWEEN", "SELECT", "DISTINCT", "TOP", "ALL", "AS", "CASE", "ESCAPE", "COLLATE", "PERCENT", "TIES", "WITH",
        };

        /// <summary>The rules about the items of a select list: a string literal as the alias (DEP021), three- and four-part column names (DEP026) and the alias with = (ST002).</summary>
        private static void SelectLists(RuleContext c)
        {
            for (var s = 0; s < c.T.Count; s++)
            {
                if (!c.Is(s, "SELECT"))
                {
                    continue;
                }

                var end = c.ListEnd(s, RuleContext.SelectStops);
                var itemStart = s + 1;
                // Skip DISTINCT / ALL / TOP n [PERCENT] [WITH TIES].
                while (itemStart < end && (c.Is(itemStart, "DISTINCT") || c.Is(itemStart, "ALL")))
                {
                    itemStart++;
                }

                if (c.Is(itemStart, "TOP"))
                {
                    itemStart++;
                    if (c.IsKind(itemStart, TokenKind.OpenParen))
                    {
                        var close = c.Matching(itemStart);
                        itemStart = close < 0 ? end : close + 1;
                    }
                    else
                    {
                        itemStart++;
                    }

                    if (c.Is(itemStart, "PERCENT"))
                    {
                        itemStart++;
                    }

                    if (c.Is(itemStart, "WITH") && c.Is(itemStart + 1, "TIES"))
                    {
                        itemStart += 2;
                    }
                }

                var depth = c.Depth[s];
                var start = itemStart;
                for (var i = itemStart; i <= end && i <= c.T.Count; i++)
                {
                    if (i < end && !(c.Depth[i] == depth && c.IsKind(i, TokenKind.Comma)))
                    {
                        continue;
                    }

                    if (i > start)
                    {
                        SelectItem(c, start, i, depth);
                    }

                    start = i + 1;
                }
            }
        }

        private static void SelectItem(RuleContext c, int start, int end, int depth)
        {
            var t = c.T;
            // 'name' = expression
            if (t[start].Kind == TokenKind.String && c.IsOp(start + 1, "=") && !c.IsOp(start + 2, "=") && end > start + 2)
            {
                if (c.On(CodeIssueKind.DEP021))
                {
                    c.Add(CodeIssueKind.DEP021, t[start], "A string literal as a column alias is deprecated; write the expression AS [name].");
                }
            }
            else if (t[start].IsNamePart && c.IsOp(start + 1, "=") && !c.IsOp(start + 2, "=") && !t[start].IsWord("CASE") && !(t[start].Kind == TokenKind.Word && Sql.Keywords.IsReserved(t[start].Text)) && end > start + 2 && !c.IsKind(start + 1, TokenKind.Dot))
            {
                if (c.On(CodeIssueKind.ST002))
                {
                    var expression = c.Script.Substring(t[start + 2].Start, t[end - 1].End - t[start + 2].Start);
                    var alias = t[start].Text;
                    c.Add(CodeIssueKind.ST002, t[start].Start, t[start + 1].End - t[start].Start, "The alias " + alias + " = expression is the old style; write expression AS " + alias + ".",
                        new CodeFix(t[start].Start, t[end - 1].End - t[start].Start, expression + " AS " + alias));
                }
            }

            // expression 'alias'
            if (c.On(CodeIssueKind.DEP021))
            {
                for (var i = start + 1; i < end; i++)
                {
                    if (t[i].Kind != TokenKind.String || c.Depth[i] != depth)
                    {
                        continue;
                    }

                    var p = t[i - 1];
                    var follows = p.Kind == TokenKind.CloseParen || p.Kind == TokenKind.Number || p.Kind == TokenKind.Variable || p.Kind == TokenKind.QuotedIdentifier || p.Kind == TokenKind.String ||
                        (p.Kind == TokenKind.Word && !NotAnAliasStart.Contains(p.Text) && !Sql.Keywords.IsReserved(p.Text));
                    if (follows && !(p.Kind == TokenKind.Word && p.IsWord("NULL")))
                    {
                        c.Add(CodeIssueKind.DEP021, t[i], "A string literal as a column alias is deprecated; write AS [name].");
                    }
                }
            }

            // schema.table.column
            if (c.On(CodeIssueKind.DEP026))
            {
                for (var i = start; i < end; i++)
                {
                    if (c.Depth[i] != depth || !t[i].IsNamePart || (i > start && c.IsKind(i - 1, TokenKind.Dot)))
                    {
                        continue;
                    }

                    var k = i;
                    var parts = 1;
                    while (c.IsKind(k + 1, TokenKind.Dot) && k + 2 < end && t[k + 2].IsNamePart)
                    {
                        k += 2;
                        parts++;
                    }

                    if (parts >= 3 && !c.IsKind(k + 1, TokenKind.OpenParen))
                    {
                        c.Add(CodeIssueKind.DEP026, t[i].Start, t[k].End - t[i].Start, "A column name with " + parts + " parts is deprecated; use the table name or an alias in front of the column.");
                    }

                    i = k;
                }
            }
        }
    }
}
