using System;
using System.Data.Common;

namespace SqlPrompter.Metadata
{
    public enum AuthenticationKind
    {
        Windows,
        SqlPassword,

        /// <summary>Např. Entra ID; připojení zatím neumíme vytvořit.</summary>
        Unsupported,
    }

    /// <summary>
    /// Údaje pro vlastní připojení k databázi okna. Heslo žije jen v paměti
    /// a nikdy se nezapisuje do logu ani na disk.
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

        /// <summary>Prázdné = výchozí databáze přihlášení.</summary>
        public string Database { get; }

        public AuthenticationKind Authentication { get; }

        public string UserName { get; }

        public string Password { get; }

        public string Encrypt { get; }

        public string TrustServerCertificate { get; }

        public bool CanConnect => Authentication != AuthenticationKind.Unsupported;

        /// <summary>Klíč cache metadat. Neobsahuje heslo.</summary>
        public string CacheKey => string.Join("|", Server, Database, Authentication, UserName).ToLowerInvariant();

        public string BuildConnectionString(string applicationName)
        {
            if (!CanConnect)
            {
                throw new InvalidOperationException("Typ ověření není podporovaný.");
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

            // Šifrování se přebírá z nastavení okna v SSMS, jinak by SqlClient 6 (Encrypt=Mandatory)
            // odmítl server s certifikátem podepsaným sám sebou.
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

        public override string ToString() => Server + "/" + (Database.Length > 0 ? Database : "(výchozí)") + " (" + Authentication + ", " + UserName + ")";
    }
}
