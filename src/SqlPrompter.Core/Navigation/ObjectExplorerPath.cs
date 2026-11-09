using System;
using System.Linq;

namespace SqlPrompter.Core.Navigation
{
    /// <summary>An object as the catalog of the database knows it, for finding its node in Object Explorer (#222).</summary>
    public sealed class LocatedObject
    {
        /// <summary>The name of the server as SQL Server knows it (<c>SERVERPROPERTY('ServerName')</c>); Object Explorer names the server node like that.</summary>
        public string Server { get; set; }

        public string Database { get; set; }

        public string Schema { get; set; }

        public string Name { get; set; }

        /// <summary>The <c>sys.objects</c> type: U table, V view, P procedure, FN / IF / TF function, SN synonym, SO sequence, TR trigger.</summary>
        public string TypeCode { get; set; }

        /// <summary>For a DML trigger: the table or view it is on (the trigger node is below it).</summary>
        public string ParentSchema { get; set; }

        public string ParentName { get; set; }

        public string ParentTypeCode { get; set; }
    }

    /// <summary>The URN of an object in the form Object Explorer (<c>IObjectExplorerService.FindNode</c>) takes, and the path to copy when the node cannot be selected.</summary>
    public static class ObjectExplorerPath
    {
        /// <summary>The URN, or null for a kind of object that has no node of its own that this finds (a database trigger, a type).</summary>
        public static string Urn(LocatedObject o)
        {
            if (o == null || string.IsNullOrEmpty(o.Server) || string.IsNullOrEmpty(o.Database) || string.IsNullOrEmpty(o.Name))
            {
                return null;
            }

            var database = "Server[@Name=" + Quote(o.Server) + "]/Database[@Name=" + Quote(o.Database) + "]";
            switch ((o.TypeCode ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "U":
                    return database + "/" + Schemed("Table", o.Schema, o.Name);
                case "V":
                    return database + "/" + Schemed("View", o.Schema, o.Name);
                case "P":
                case "PC":
                    return database + "/" + Schemed("StoredProcedure", o.Schema, o.Name);
                case "FN":
                case "IF":
                case "TF":
                case "FS":
                case "FT":
                    return database + "/" + Schemed("UserDefinedFunction", o.Schema, o.Name);
                case "SN":
                    return database + "/" + Schemed("Synonym", o.Schema, o.Name);
                case "SO":
                    return database + "/" + Schemed("Sequence", o.Schema, o.Name);
                case "TR":
                    var owner = (o.ParentTypeCode ?? string.Empty).Trim().ToUpperInvariant() == "V" ? "View" : "Table";
                    return string.IsNullOrEmpty(o.ParentName) ? null : database + "/" + Schemed(owner, o.ParentSchema, o.ParentName) + "/Trigger[@Name=" + Quote(o.Name) + "]";
                default:
                    return null;
            }
        }

        /// <summary>[database].[schema].[name] for the clipboard.</summary>
        public static string Qualified(string database, string schema, string name) =>
            string.Join(".", new[] { database, schema, name }.Where(p => !string.IsNullOrEmpty(p)).Select(Bracket));

        private static string Schemed(string kind, string schema, string name) =>
            kind + "[@Name=" + Quote(name) + (string.IsNullOrEmpty(schema) ? string.Empty : " and @Schema=" + Quote(schema)) + "]";

        private static string Quote(string value) => "'" + (value ?? string.Empty).Replace("'", "''") + "'";

        private static string Bracket(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
    }
}
