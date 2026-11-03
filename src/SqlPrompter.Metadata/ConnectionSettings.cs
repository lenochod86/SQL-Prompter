using System;
using System.Data.Common;

namespace SqlPrompter.Metadata
{
    public enum AuthenticationKind
    {
        Windows,
        SqlPassword,

        /// <summary>E.g. Entra ID; we cannot create such a connection yet.</summary>
        Unsupported,
    }

    /// <summary>
    /// Data for our own connection to the window's database. The password lives only in memory
    /// and is never written to the log or to disk.
    /// </summary>
    public sealed class ConnectionSettings
    {
        public ConnectionSettings(
            string server,
            string database,
            AuthenticationKind authentication,
            string userName,
            string password,
            string encrypt,
            string trustServerCertificate)
        {
            Server = server ?? throw new ArgumentNullException(nameof(server));
            Database = database ?? string.Empty;
            Authentication = authentication;
            UserName = userName ?? string.Empty;
            Password = password;
            Encrypt = encrypt;
            TrustServerCertificate = trustServerCertificate;
        }

        public string Server { get; }

        /// <summary>Empty = default database of the login.</summary>
        public string Database { get; }

        public AuthenticationKind Authentication { get; }

        public string UserName { get; }

        public string Password { get; }

        public string Encrypt { get; }

        public string TrustServerCertificate { get; }

        public bool CanConnect => Authentication != AuthenticationKind.Unsupported;

        /// <summary>Metadata cache key. Does not contain the password.</summary>
        public string CacheKey => string.Join("|", Server, Database, Authentication, UserName).ToLowerInvariant();

        public string BuildConnectionString(string applicationName)
        {
            if (!CanConnect)
            {
                throw new InvalidOperationException("The authentication type is not supported.");
            }

            var builder = new DbConnectionStringBuilder
            {
                ["Data Source"] = Server,
                ["Application Name"] = applicationName,
                ["Connect Timeout"] = 15,
            };

            if (Database.Length > 0)
            {
                builder["Initial Catalog"] = Database;
            }

            if (Authentication == AuthenticationKind.Windows)
            {
                builder["Integrated Security"] = true;
            }
            else
            {
                builder["User ID"] = UserName;
                builder["Password"] = Password ?? string.Empty;
                builder["Persist Security Info"] = false;
            }

            // Encryption is taken over from the window's settings in SSMS; otherwise SqlClient 6 (Encrypt=Mandatory)
            // would reject a server with a self-signed certificate.
            if (!string.IsNullOrEmpty(Encrypt))
            {
                builder["Encrypt"] = Encrypt;
            }

            if (!string.IsNullOrEmpty(TrustServerCertificate))
            {
                builder["Trust Server Certificate"] = TrustServerCertificate;
            }

            return builder.ConnectionString;
        }

        public override string ToString() => Server + "/" + (Database.Length > 0 ? Database : "(default)") + " (" + Authentication + ", " + UserName + ")";
    }
}
