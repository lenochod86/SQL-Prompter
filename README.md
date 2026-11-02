# SQL Prompter

Rozšíření s vlastním IntelliSense pro SQL editor v SQL Server Management Studio 22. Návrh a výsledky prototypu jsou v [docs/architecture.md](docs/architecture.md).

**Stav:** MVP. Jednotkové testy procházejí, v SSMS 22 zatím neověřené.

## Struktura

| Složka | Obsah |
|---|---|
| `src/SqlPrompter.Core` | Lexer, rozbor kontextu na pozici kurzoru a sestavení návrhů (netstandard2.0, bez závislostí) |
| `src/SqlPrompter.Metadata` | Snímky metadat po databázích, načítání z `sys.*` na pozadí (netstandard2.0) |
| `src/SqlPrompter.Ssms` | VSIX pro SSMS 22: zdroj našeptávání, připojení okna, příkazy (net48) |
| `tests/SqlPrompter.Tests` | Testy jádra a metadat (aktuální .NET) |
| `prototype/` | Diagnostický prototyp, který ověřil základní předpoklady |

## Sestavení

Stačí .NET SDK 8 nebo novější, Visual Studio není potřeba.

```powershell
dotnet test tests\SqlPrompter.Tests
dotnet build src\SqlPrompter.Ssms\SqlPrompter.Ssms.csproj -c Release
```

VSIX vznikne v `src\SqlPrompter.Ssms\bin\Release\net48\SqlPrompter.Ssms.vsix`. Testy běží i mimo Windows, VSIX se sestavuje jen na Windows.

## Instalace do SSMS 22

1. Zavřete SSMS.
2. Pokud je nainstalovaný prototyp, odinstalujte ho, jinak by se v seznamu objevovalo i „hello“:
   ```powershell
   & "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe" /uninstall:SqlPrompter.Prototype.c9efd892-d89f-4178-8e0d-a9b7f925e617
   ```
3. Poklepejte na `SqlPrompter.Ssms.vsix`.

Odinstalace: `VSIXInstaller.exe /uninstall:SqlPrompter.0d66644a-dd92-4b87-ae9d-5d20a2df11c6`.

V nabídce **Tools → SQL Prompter** jsou příkazy **Obnovit metadata** a **Otevřít log**. Log je v `%LOCALAPPDATA%\SqlPrompter\sqlprompter.log` a obsahuje jen načítání metadat a chyby, žádná hesla ani obsah skriptů.

## Jak se chová

- Kde rozšíření umí nabídnout úplný seznam, nahradí vestavěné IntelliSense. Jinde (proměnné, dočasné tabulky, CTE, `EXEC`, typy …) se neúčastní a funguje vestavěné IntelliSense.
- Při prvním našeptání v databázi se metadata teprve načítají na pozadí, takže se ještě ukáže vestavěné IntelliSense. Naše návrhy se objeví při dalším našeptání. Stejně je to se sloupci tabulky, která se v okně objeví poprvé.
- Návrh se vkládá klávesou Tab nebo Enter, mezera ani tečka ho nevkládají.
- Seznam tabulek se otevře i po napsání mezery za `FROM`, `JOIN` a podobně.

## Ruční test v SSMS

Připojte okno k databázi s několika tabulkami a vyzkoušejte:

| Napište | Očekávání |
|---|---|
| `sel` | Klíčová slova, `select` malými písmeny |
| `SELECT * FROM ` | Tabulky a pohledy jako `dbo.Tabulka`, schémata |
| `SELECT * FROM sys.ob` | `objects` a další systémové pohledy |
| `SELECT * FROM dbo.T t WHERE t.` | Sloupce tabulky |
| `SELECT  FROM dbo.A a JOIN dbo.B b ON 1=1` s kurzorem za `SELECT` a Ctrl+Mezerník | Sloupce obou tabulek, společné s aliasem |
| `SELECT * FROM dbo.T t ` | `WHERE`, `JOIN` … bez pevného výběru: Enter odřádkuje |
| `DECLARE @x int; SELECT @` | Vestavěné IntelliSense (proměnné) |
| Okno přihlášené přes Entra ID | Jen klíčová slova, jinak vestavěné IntelliSense |
