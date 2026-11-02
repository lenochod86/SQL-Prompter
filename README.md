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

Pokud SSMS po odebrání rozšíření hlásí, že se balíček (např. `PrototypePackage`) nenačetl, drží ještě starou registraci. Při zavřeném SSMS pomůže `SSMS.exe /updateconfiguration` ze složky `Common7\IDE`.

Menu **SQL Prompter** s příkazy **Obnovit metadata** a **Otevřít log** je ve výchozím stavu pod **Extensions**. Do hlavní lišty ho přesunete v **Extensions → Customize Menu…** zrušením zaškrtnutí u SQL Prompter. Log je v `%LOCALAPPDATA%\SqlPrompter\sqlprompter.log` a obsahuje jen načítání metadat a chyby, žádná hesla ani obsah skriptů.

## Jak se chová

- Seznam našeptávání je vždy od SQL Prompteru a poznáte ho podle tlačítka filtru **SQL Prompter** dole. Vestavěné IntelliSense zůstává zapnuté kvůli podtrhávání chyb a popiskům, jeho seznam se ale neukazuje.
- Kde rozšíření kontext nezná (`EXEC`, `VALUES`, `SET @x =` …), nabídne klíčová slova, funkce a datové typy. U nových názvů (alias za `AS`, `@proměnná`, `#tabulka`) nenabídne nic.
- Metadata se začnou načítat po aktivaci okna dotazu, včetně sloupců. Těsně po připojení k nové databázi můžou návrhy tabulek a sloupců chvíli chybět.
- Vybraný návrh vloží Tab, Enter, mezera, tečka, čárka, závorka nebo operátor. Za tabulkou, kde se píše alias, je výběr jen naznačený a vloží ho jen Tab.
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
| `DECLARE @x int; SELECT @` | Žádný seznam (proměnné zatím rozšíření nezná) |
| `SELECT CAST(1 AS ` | Datové typy |
| `EXEC ` a Ctrl+Mezerník | Klíčová slova a funkce bez pevného výběru |
| Okno přihlášené přes Entra ID | Klíčová slova a funkce, bez tabulek a sloupců |
