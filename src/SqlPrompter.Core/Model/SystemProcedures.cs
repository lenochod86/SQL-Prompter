using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlPrompter.Core.Model
{
    /// <summary>
    /// The system procedures that are typed again and again after EXEC (#218), with their parameters, so that they are offered, inserted with
    /// their parameters and shown by the parameter info although they are not in the metadata of the database (they live in master / sys).
    /// </summary>
    public static class SystemProcedures
    {
        public const string Schema = "sys";

        private static DbParameter P(string name, string type, bool hasDefault = false, string defaultText = null, bool output = false) =>
            new DbParameter(name, type, output, hasDefault, defaultText);

        private static DbProcedure Proc(string name, params DbParameter[] parameters) => new DbProcedure(0, Schema, name, parameters);

        public static IReadOnlyList<DbProcedure> All { get; } = new[]
        {
            Proc("sp_columns", P("@table_name", "nvarchar(384)"), P("@table_owner", "nvarchar(384)", true, "NULL"), P("@table_qualifier", "sysname", true, "NULL"), P("@column_name", "nvarchar(384)", true, "NULL")),
            Proc("sp_configure", P("@configname", "varchar(35)", true, "NULL"), P("@configvalue", "int", true, "NULL")),
            Proc("sp_executesql", P("@stmt", "nvarchar(max)"), P("@params", "nvarchar(max)", true, "NULL")),
            Proc("sp_fkeys", P("@pktable_name", "sysname", true, "NULL"), P("@pktable_owner", "sysname", true, "NULL"), P("@fktable_name", "sysname", true, "NULL"), P("@fktable_owner", "sysname", true, "NULL")),
            Proc("sp_help", P("@objname", "nvarchar(776)", true, "NULL")),
            Proc("sp_helpconstraint", P("@objname", "nvarchar(776)"), P("@nomsg", "varchar(5)", true, "NULL")),
            Proc("sp_helpdb", P("@dbname", "sysname", true, "NULL")),
            Proc("sp_helpindex", P("@objname", "nvarchar(776)")),
            Proc("sp_helptext", P("@objname", "nvarchar(776)"), P("@columnname", "sysname", true, "NULL")),
            Proc("sp_helpuser", P("@name_in_db", "sysname", true, "NULL")),
            Proc("sp_pkeys", P("@table_name", "sysname"), P("@table_owner", "sysname", true, "NULL")),
            Proc("sp_recompile", P("@objname", "nvarchar(776)")),
            Proc("sp_refreshsqlmodule", P("@name", "nvarchar(776)"), P("@namespace", "nvarchar(20)", true, "NULL")),
            Proc("sp_refreshview", P("@viewname", "nvarchar(776)")),
            Proc("sp_rename", P("@objname", "nvarchar(1035)"), P("@newname", "sysname"), P("@objtype", "varchar(13)", true, "NULL")),
            Proc("sp_spaceused", P("@objname", "nvarchar(776)", true, "NULL"), P("@updateusage", "varchar(5)", true, "NULL")),
            Proc("sp_stored_procedures", P("@sp_name", "nvarchar(390)", true, "NULL"), P("@sp_owner", "nvarchar(384)", true, "NULL")),
            Proc("sp_tables", P("@table_name", "nvarchar(384)", true, "NULL"), P("@table_owner", "nvarchar(384)", true, "NULL")),
            Proc("sp_updatestats", P("@resample", "char(8)", true, "NULL")),
            Proc("sp_who", P("@loginame", "sysname", true, "NULL")),
            Proc("sp_who2", P("@loginame", "sysname", true, "NULL")),
        };

        /// <summary>The system procedure with this name (case-insensitive), or null.</summary>
        public static DbProcedure Find(string name) =>
            All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
