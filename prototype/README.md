# Prototyp SQL Prompter

Diagnostické VSIX rozšíření, které má v SSMS 22 odpovědět na tři otázky z [`docs/architecture.md`](../docs/architecture.md#prototyp). Kód se nepoužije pro MVP. Měří, nic nestaví.

**Stav:** VSIX se sestaví (Windows, .NET SDK 10.0.403, bez Visual Studia). V SSMS 22 zatím neběželo, takže nic z toho, co následuje, není ověřené.

Všechno se zapisuje do `%LOCALAPPDATA%\SqlPrompter\prototype.log`. Řádky jsou označené `[host]`, `[q1]`, `[q2]` a `[q3]` a uvádějí vlákno (`UI` = vlákno UI).

## Co dělá

| Soubor | Otázka | Co zjišťuje |
|---|---|---|
| `PrototypePackage.cs` | předpoklad | Načte se balíček? Zapíše proces, architekturu, verzi DTE, verze sestavení editoru a SqlClient a přidá nabídku **Nástroje → SQL Prompter (prototyp)**. |
| `TextViewDiagnostics.cs` | 1, 2, 3 | Pro každý pohled vypíše typ obsahu včetně bázových typů, buffery v grafu a `IAsyncCompletionBroker.IsCompletionSupported`. U SQL pohledu vypíše klíče vlastností bufferu (možné místo, kde je připojení okna). Sleduje všechny asynchronní relace našeptávání a vypíše počet položek podle zdroje a texty, které nabízí víc zdrojů. |
| `HelloCompletionSource.cs` | 1 | `IAsyncCompletionSource` pro typ obsahu `any`, který v SQL editoru nabídne `hello` a `hello_sql_prompter`. |
| `LegacyCompletionProbe.cs` | 1, 2 | Sonda staršího `ICompletionSource`: vypíše cizí sady návrhů a přidá vlastní. Ve výchozím stavu je vypnutá. |
| `ConnectionProbe.cs` | 3 | Reflexí najde `ScriptFactory.Instance.CurrentlyActiveWndConnectionInfo`, vypíše jeho strukturu (tajemství skrytá) a kandidátní typy `*ConnectionInfo`. Volitelně otevře vlastní připojení přes SqlClient, který už je v procesu SSMS, a spustí `SELECT @@SERVERNAME, DB_NAME(), SUSER_SNAME()`. |

SQL editor se pozná podle typu obsahu, jehož název nebo bázový typ obsahuje `sql`. Když tato heuristika selže, nastavte před spuštěním SSMS proměnnou `SQLPROMPTER_CONTENT_TYPE` na přesný název z logu.

## Sestavení

Projekt je ve formátu SDK, takže Visual Studio ani VS SDK není potřeba. Nástroje pro VSIX dodává balík `Microsoft.VSSDK.BuildTools` a referenční sestavení .NET Frameworku si SDK stáhne samo.

- Na Windows s .NET SDK 8 nebo novějším: `dotnet build prototype\SqlPrompter.Prototype\SqlPrompter.Prototype.csproj -c Release`.
- Nebo stáhněte artefakt z workflow [Prototyp VSIX](../.github/workflows/prototype.yml) na Windows runneru GitHub Actions.

Výsledek je `prototype\SqlPrompter.Prototype\bin\Release\net48\SqlPrompter.Prototype.vsix`. Soubor [`nuget.config`](../nuget.config) v kořeni repozitáře omezuje zdroje balíčků na nuget.org, aby sestavení nezáviselo na lokální konfiguraci NuGetu.

## Instalace do SSMS 22

Manifest cílí na `Microsoft.VisualStudio.Ssms` `[22.0,23.0)`. Stejné Id (s `Version="[22.0,)"`) používají vestavěná rozšíření SSMS 22.11 v `Common7\IDE\Extensions`. Jestli VSIXInstaller naši VSIX do SSMS nainstaluje, zatím ověřené není.

1. Zavřete SSMS a poklepejte na `.vsix`. Pokud VSIXInstaller SSMS 22 nenabídne, zapište chybu z jeho logu (odkaz je v okně instalátoru) do `docs/architecture.md`.
2. Pokud instalace selže na prerekvizitě `Microsoft.VisualStudio.Component.CoreEditor`, odeberte ji z manifestu a zapište to.
3. Spusťte SSMS a ověřte, že log obsahuje `=== Prototyp SQL Prompter načten ===`.

## Postup testu

Před každým během smažte log, aby se výsledky nemíchaly.

### Otázka 1: MEF a `IAsyncCompletionSource`

1. Otevřete nové okno dotazu (připojené i nepřipojené).
2. V logu najděte `[q1] Nový pohled` a opište název typu obsahu SQL editoru, jeho bázové typy a hodnotu `asyncCompletionSupported`.
3. Napište `hel` a zkuste také Ctrl+Mezerník.
4. Kladná odpověď: v seznamu je `hello` a log obsahuje `GetOrCreate`, `InitializeCompletion` a `GetCompletionContextAsync`.
5. Pokud se v logu objeví jen `Nový pohled` a nic dalšího, editor zřejmě asynchronní API nepoužívá. Pak zopakujte test se sondou staršího API (viz níže).

### Otázka 2: duplicity s vestavěným IntelliSense

1. Se zapnutým vestavěným IntelliSense napište `SELECT * FROM sys.ob`.
2. Pozorujte, jestli se objeví **jeden** seznam (položky `hello` a vestavěné dohromady), nebo **dva** samostatné seznamy.
3. V logu `[q2] položky podle zdroje` ukáže, jestli vestavěné IntelliSense dodává položky do stejné asynchronní relace. Pokud tam je jen `HelloCompletionSource`, vestavěné IntelliSense běží mimo moderní API.
4. Vypněte IntelliSense (**Dotaz → IntelliSense povoleno**, Ctrl+Q, I, nebo *Nástroje → Možnosti → Textový editor → Transact-SQL → IntelliSense*) a zopakujte. Zapište, jestli zmizí jen vestavěný seznam a jestli `hello` zůstane.
5. Sonda staršího API: zavřete SSMS, nastavte `SQLPROMPTER_LEGACY_PROBE=1` a SSMS spusťte ze stejné konzole. Log `[q2] Starší relace našeptávání` vypíše cizí sady návrhů. Sonda je ve výchozím stavu vypnutá, protože starší zdroj může přepnout editor do staršího režimu a zkreslit otázku 1.

### Otázka 3: připojení aktivního okna

Postup zopakujte pro každý typ ověření, který máte k dispozici: Windows, SQL Server a Entra ID s MFA (Interactive). U každého zapište výsledek do tabulky níže.

1. Připojte okno dotazu k serveru a databázi.
2. **Nástroje → SQL Prompter (prototyp) → Vypsat připojení aktivního okna.** Log vypíše, kde je `ScriptFactory`, strukturu `CurrentlyActiveWndConnectionInfo` a shrnutí (server, databáze, typ ověření, jestli je k dispozici heslo). Heslo ani tokeny se nikdy nevypisují.
3. Pokud `ScriptFactory` neexistuje, použijte výpis `Kandidátní typy` a klíče vlastností bufferu z `[q1]/[q3] Nový pohled` jako vodítko pro náhradu.
4. **Otestovat vlastní připojení (jen čtení).** Zapište, jestli se připojení otevřelo a za jak dlouho. U Entra ID zapište, jestli se objevilo přihlašovací okno (token se nesdílí s SSMS), nebo připojení proběhlo bez dotazu.
5. Přepněte databázi v rozbalovacím seznamu SSMS a krok 2 zopakujte. Zapište, jestli se změna projeví v `AdvancedOptions[DATABASE]` nebo jinde.

## Výsledky

Vyplňte po běhu v SSMS 22 a přeneste do `docs/architecture.md`.

| Bod | Výsledek | Verze SSMS, datum |
|---|---|---|
| VSIX se sestaví bez Visual Studia | ano | 2026-11-02 |
| Id `InstallationTarget` | `Microsoft.VisualStudio.Ssms` podle vestavěných rozšíření, instalace neověřena | 22.11.3, 2026-11-02 |
| Balíček se načte, nabídka je vidět | neověřeno | |
| Typ obsahu SQL editoru a jeho bázové typy | neověřeno | |
| `IAsyncCompletionSource` funguje v SQL editoru | neověřeno | |
| Vestavěné IntelliSense: stejná relace / samostatný seznam / starší API | neověřeno | |
| Lze vestavěné IntelliSense vypnout, aniž by zmizelo `hello` | neověřeno | |
| `ScriptFactory.CurrentlyActiveWndConnectionInfo` existuje | neověřeno | |
| Připojení: Windows | neověřeno | |
| Připojení: SQL Server (heslo dostupné?) | neověřeno | |
| Připojení: Entra ID MFA (nutné znovu přihlásit?) | neověřeno | |
| Změna databáze v okně je vidět | neověřeno | |
