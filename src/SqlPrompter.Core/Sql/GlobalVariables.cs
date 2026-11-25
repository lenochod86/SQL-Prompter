using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Sql
{
    /// <summary>A global variable (a system function with two at signs) with its type and meaning.</summary>
    public sealed class GlobalVariable
    {
        public GlobalVariable(string name, string typeName, string meaning)
        {
            Name = name;
            TypeName = typeName;
            Meaning = meaning;
        }

        public string Name { get; }

        public string TypeName { get; }

        public string Meaning { get; }
    }

    /// <summary>The global variables of SQL Server (<c>@@ROWCOUNT</c>).</summary>
    public static class GlobalVariables
    {
        public static readonly IReadOnlyList<GlobalVariable> All = new[]
        {
            new GlobalVariable("@@ROWCOUNT", "int", "Number of rows affected by the last statement."),
            new GlobalVariable("@@ERROR", "int", "Error number of the last statement, 0 when it succeeded."),
            new GlobalVariable("@@TRANCOUNT", "int", "Number of open BEGIN TRANSACTION levels in the session."),
            new GlobalVariable("@@IDENTITY", "numeric(38,0)", "Last identity value inserted in the session, also by triggers; see SCOPE_IDENTITY()."),
            new GlobalVariable("@@VERSION", "nvarchar", "Version, edition and operating system of the server."),
            new GlobalVariable("@@SERVERNAME", "nvarchar", "Name of the local server."),
            new GlobalVariable("@@SPID", "smallint", "ID of the current session."),
            new GlobalVariable("@@FETCH_STATUS", "int", "Result of the last FETCH: 0 success, -1 failed or past the end, -2 row missing, -9 no fetch."),
            new GlobalVariable("@@DBTS", "varbinary(8)", "Last rowversion value used in the database."),
            new GlobalVariable("@@LANGUAGE", "nvarchar", "Name of the language of the session."),
            new GlobalVariable("@@LOCK_TIMEOUT", "int", "Lock time-out of the session in milliseconds, -1 for none."),
            new GlobalVariable("@@MAX_CONNECTIONS", "int", "Maximum number of simultaneous user connections."),
            new GlobalVariable("@@NESTLEVEL", "int", "Nesting level of the executing procedure or trigger."),
            new GlobalVariable("@@OPTIONS", "int", "Bit mask of the current SET options."),
            new GlobalVariable("@@PROCID", "int", "Object ID of the executing procedure, function or trigger."),
            new GlobalVariable("@@SERVICENAME", "nvarchar", "Name of the service under which SQL Server runs."),
            new GlobalVariable("@@TEXTSIZE", "int", "Current value of SET TEXTSIZE."),
            new GlobalVariable("@@TIMETICKS", "int", "Microseconds per tick."),
            new GlobalVariable("@@CONNECTIONS", "int", "Connections attempted since the server started."),
            new GlobalVariable("@@CPU_BUSY", "int", "Time the server has spent working since it started, in ticks."),
            new GlobalVariable("@@CURSOR_ROWS", "int", "Number of rows in the last opened cursor."),
            new GlobalVariable("@@DATEFIRST", "tinyint", "First day of the week set by SET DATEFIRST (1 = Monday)."),
            new GlobalVariable("@@IDLE", "int", "Time the server has been idle since it started, in ticks."),
            new GlobalVariable("@@IO_BUSY", "int", "Time the server has spent on input and output since it started, in ticks."),
            new GlobalVariable("@@LANGID", "smallint", "ID of the language of the session."),
            new GlobalVariable("@@MAX_PRECISION", "tinyint", "Precision level of decimal and numeric data."),
            new GlobalVariable("@@MICROSOFTVERSION", "int", "Version number of the server as an integer (undocumented)."),
            new GlobalVariable("@@PACKET_ERRORS", "int", "Network packet errors since the server started."),
            new GlobalVariable("@@PACK_RECEIVED", "int", "Network packets received since the server started."),
            new GlobalVariable("@@PACK_SENT", "int", "Network packets sent since the server started."),
            new GlobalVariable("@@REMSERVER", "nvarchar(128)", "Name of the remote server that called the procedure (deprecated)."),
            new GlobalVariable("@@TOTAL_ERRORS", "int", "Disk write errors since the server started."),
            new GlobalVariable("@@TOTAL_READ", "int", "Disk reads since the server started."),
            new GlobalVariable("@@TOTAL_WRITE", "int", "Disk writes since the server started."),
        };

        public static IEnumerable<string> Names => All.Select(v => v.Name);
    }
}
