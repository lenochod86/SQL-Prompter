using System.Linq;
using SqlPrompter.Core.Lexing;

namespace SqlPrompter.Core.Sql
{
    public static class SqlIdentifier
    {
        /// <summary>Returns the identifier in square brackets if it cannot be written without them.</summary>
        public static string Quote(string name, bool force = false)
        {
            if (!force && !NeedsQuoting(name))
            {
                return name;
            }

            return "[" + name.Replace("]", "]]") + "]";
        }

        public static bool NeedsQuoting(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            if (!Lexer.IsWordStart(name[0]) || name[0] == '#' || !name.All(c => Lexer.IsWordChar(c)) || name.Contains('@'))
            {
                return true;
            }

            return Keywords.IsReserved(name);
        }
    }
}
