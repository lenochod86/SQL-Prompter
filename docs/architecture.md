# SQL Prompter: návrh architektury

Rozšíření s vlastním IntelliSense pro SQL Server Management Studio 22.

**Stav:** návrh a nespuštěný prototyp. V SSMS 22 zatím nic neběželo. Závěry označené jako hypotéza musí potvrdit prototyp (viz [Prototyp](#prototyp)).

## Shrnutí

C# a klasické VSIX rozšíření běžící v procesu SSMS. Veškerá logika je v knihovnách nezávislých na SSMS, vazba na SSMS je co nejtenčí.

## Hlavní riziko

Microsoft uvádí, že rozšíření třetích stran nejsou v SSMS podporovaná. SSMS je neblokuje, ale hlášení k nim uzavírá bez prošetření ([SSMS FAQ][faq]). Aktualizace SSMS tak může rozšíření kdykoli rozbít.

## Technologie

| Vrstva | Volba | Důvod |
|---|---|---|
| Jazyk | C# | Nativní jazyk rozhraní editoru |
| Doplněk v SSMS | .NET Framework 4.8, AnyCPU | SSMS je 64bitové a existuje i pro Arm64 ([FAQ][faq]) |
| Jádro | .NET Standard 2.0 | Sestavení a testy i mimo Windows |
| Napojení na editor | MEF a `IAsyncCompletionSource` ([walkthrough][completion]) | Moderní API s výpočtem na pozadí |
| Metadata | `Microsoft.Data.SqlClient`, dotazy na `sys.*` | Rychlé, jen pro čtení, bez závislosti na SMO |

SSMS 22 je postavené na Visual Studiu 2026 ([FAQ][faq]). Podle [poznámek autora jiného rozšíření][sqlfluff] se používá `Microsoft.VisualStudio.SDK` 17.14, protože balík 18.x neexistuje. Sám autor uvádí, že to zatím neověřil v praxi, takže jde o hypotézu.

VisualStudio.Extensibility se nepoužívá: podporu v SSMS žádný dokument nepotvrzuje a podle [stejného zdroje][sqlfluff] chybí náhrady části rozhraní editoru.

## Struktura

```text
┌───────────────── proces SSMS 22 ─────────────────┐
│  SqlPrompter.Ssms (VSIX, tenká vrstva)           │
│  našeptávání · potvrzení výběru · nastavení      │
│  zjištění aktuálního připojení                   │
│        │ vlastní rozhraní, bez typů SSMS         │
│  ┌─────▼─────────────┐   ┌─────────────────────┐ │
│  │ SqlPrompter.Core  │◄──│ SqlPrompter.Metadata│ │
│  │ lexer             │   │ cache po databázích │ │
│  │ analýza kontextu  │   │ načítání na pozadí  │ │
│  │ řazení návrhů     │   └──────────┬──────────┘ │
│  └───────────────────┘              │            │
└─────────────────────────────────────┼────────────┘
                                      ▼
                                 SQL Server (sys.*)
```

| Projekt | Cíl | Smí záviset na |
|---|---|---|
| `SqlPrompter.Core` | netstandard2.0 | Nic z VS, SSMS ani databáze |
| `SqlPrompter.Metadata` | netstandard2.0 | `Microsoft.Data.SqlClient` |
| `SqlPrompter.Ssms` | net48, VSIX | VS SDK, API SSMS, oba projekty výše |
| Testy | Aktuální .NET | Core a Metadata, případy typu `SELECT u.| FROM dbo.Users u` s vyznačenou pozicí kurzoru |

## Průběh našeptání

1. Rychlá kontrola na vlákně UI podle tokenů zjistí, jestli má našeptávání na dané pozici smysl.
2. Na pozadí se vyřízne aktuální příkaz a určí se kontext, třeba „sloupec aliasu `u`“.
3. Kontext se spojí s metadaty z cache a s objekty ze skriptu (CTE, dočasné tabulky, proměnné).
4. Výsledek se seřadí a zobrazí.

Na databázi se v tomto průběhu nikdy nečeká. Když sloupce ještě nejsou načtené, doplní se při dalším našeptání.

## Rozhodnutí

- **Dva parsery.** Během psaní je SQL skoro vždy nedokončené, proto našeptávání používá vlastní tolerantní lexer a lehkou analýzu klauzulí. ScriptDOM od Microsoftu slouží k úplné analýze platných příkazů a pozdější diagnostice. Licenci ScriptDOM je potřeba ověřit před prvním použitím.
- **Bez `Microsoft.SqlServer.Management.SqlParser` jako základu.** [Není open source][sqlparser] a podmínky jeho licence nejsou ověřené.
- **Metadata jako neměnné snímky.** Každá databáze má vlastní snímek a sloupce se načítají až při potřebě. Obnova proběhne podle `sys.objects.modify_date`, po DDL v okně a ručním příkazem.
- **Zabezpečení.** Hesla se nikam neukládají. Metadata na disk jen volitelně, protože i názvy objektů můžou být citlivé. Žádná telemetrie.
- **Cílové výkonnostní limity (návrh).** Do 50 ms pro seznam z cache a žádné čekání na síť ve vlákně UI.

## Prototyp

Bez kladných odpovědí nemá smysl stavět zbytek:

1. **Načte se MEF rozšíření v SSMS 22 a funguje v SQL editoru?** Názvy typů obsahu nejsou spolehlivě zdokumentované, takže je prototyp za běhu vypíše. Musí také ukázat, že SQL editor opravdu používá `IAsyncCompletionSource`.
2. **Půjde se vedle vestavěného IntelliSense vyhnout duplicitám?** Možná bude potřeba vestavěné našeptávání vypnout nebo jeho výsledky potlačit.
3. **Jak získat připojení aktivního okna?** `ScriptFactory.CurrentlyActiveWndConnectionInfo` je [zdokumentované jen pro starší verze][scriptfactory]. Zvlášť je potřeba ověřit přihlášení přes Entra ID a MFA, kde nemusí jít připojení jednoduše znovu vytvořit.

### Stav prototypu

Kód je v [`prototype/`](../prototype/README.md): diagnostické VSIX, které vše zapisuje do `%LOCALAPPDATA%\SqlPrompter\prototype.log`. README obsahuje postup testu a tabulku výsledků. VSIX se sestaví příkazem `dotnet build` bez Visual Studia (projekt ve formátu SDK s balíkem `Microsoft.VSSDK.BuildTools` 17.14). První běh v SSMS 22.11.3 proběhl 2026-11-02.

**Otázka 1: ano.** MEF rozšíření se načte a v SQL editoru funguje `IAsyncCompletionSource`: po napsání `hel` se nabídne `hello`.

- Typ obsahu SQL editoru je `"SQL"` s bázovými typy `"code"` → `"text"` → `"any"`. Pohled leží nad projekčním bufferem `"projection"`.
- `IAsyncCompletionBroker.IsCompletionSupported` vrací pro `"SQL"` hodnotu `True`. `GetCompletionContextAsync` běží mimo vlákno UI.
- Sestavení editoru v SSMS mají verzi 18.0.0.0. Rozšíření sestavené proti `Microsoft.VisualStudio.SDK` 17.14 se na ně naváže, takže hypotéza o SDK 17.14 platí.
- V SSMS je načtený `Microsoft.Data.SqlClient` 6.0.0.0. DTE hlásí `Version=18.0` a `Edition=SSMS`.
- Vlastnosti bufferu SQL okna neobsahují nic, co by souviselo s připojením.

**Otázka 2: předběžně bez duplicit, nutný kontrolní test.**

- Při psaní (`CREATE TABLE …`) se zobrazil jen seznam s `hello`. Vestavěné IntelliSense žádný vlastní seznam neukázalo a do asynchronní relace nepřidalo žádné položky.
- S vypnutým vestavěným IntelliSense `hello` zůstává.
- Neověřeno je, jestli vestavěný seznam potlačuje naše relace, nebo jestli v testovaných místech nic nenabízel. Prototyp 0.0.2 má v nabídce příkaz pro vypnutí `hello`, takže jde porovnat stejné místo s naším zdrojem a bez něj.

**Otázka 3: `ScriptFactory` v SSMS 22 funguje, zbývá vlastní připojení a další typy ověření.**

- `ScriptFactory.Instance.CurrentlyActiveWndConnectionInfo` (`SQLEditors.dll` 22.200) je dosažitelný reflexí. `UIConnectionInfo` (`Microsoft.SqlServer.RegSvrEnum` 18.100) obsahuje `ServerName`, `UserName`, `AuthenticationType` (SQL Server = 1), `Password` a `InMemoryPassword` (`SecureString`) a `RenewableToken` (u SQL přihlášení `null`, kandidát pro Entra ID).
- `AdvancedOptions` obsahuje `DATABASE`, `ENCRYPT_CONNECTION`, `TRUST_SERVER_CERTIFICATE`, `CONNECTION_TIMEOUT`, `PACKET_SIZE` a údaje o serveru. Vlastní připojení musí šifrování převzít, jinak SqlClient 6 (`Encrypt=Mandatory`) selže na certifikátu podepsaném sám sebou.
- Mezi kandidátními typy je `Microsoft.SqlServer.Management.ServerInformation.IServerConnectionInformation` (`Microsoft.SqlServer.Management.SDK.SqlStudio` 22.200). Může se hodit jako náhrada.
- Neověřeno: úspěšné vlastní připojení, přihlášení přes Windows a Entra ID s MFA a to, jestli `DATABASE` sleduje změnu databáze v okně.

Zjištěno z instalace SSMS 22.11.3:

- `productId` je `Microsoft.VisualStudio.Product.Ssms`.
- Vestavěná rozšíření používají `InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[22.0,)"`.
- Mezi instalovanými balíčky je `Microsoft.VisualStudio.Component.CoreEditor`.
- VSIXInstaller nainstaluje VSIX s cílem `Microsoft.VisualStudio.Ssms` `[22.0,23.0)` a prerekvizitou `Microsoft.VisualStudio.Component.CoreEditor` (ověřeno).

Hypotézy, které prototyp ověřuje navíc:

- `ScriptFactory` je dosažitelný reflexí, bez odkazu na sestavení SSMS při sestavení.
- Vlastní připojení může používat `Microsoft.Data.SqlClient`, který už je v procesu SSMS načtený, takže ho VSIX nemusí přibalovat.

## Etapy

1. **Prototyp:** statický návrh „hello“ v SQL editoru a odpovědi na tři otázky výše.
2. **MVP:** klíčová slova, schémata, tabulky, sloupce, aliasy a kontext `FROM`, `JOIN`, `WHERE`.
3. **Znalost skriptu:** CTE, dočasné tabulky, proměnné a návrh podmínek `JOIN` podle cizích klíčů.
4. **Další funkce:** nápověda k parametrům procedur, popisky objektů, snippety a řazení podle používání.

## Prostředí pro vývoj

Core, Metadata a jejich testy jdou vyvíjet v cloudu. VSIX se sestavuje na Windows a SSMS v cloudovém prostředí nepoběží, takže prototyp a ruční testy běží na Windows s SSMS 22, případně přes Remote Control. VSIX může sestavovat Windows runner v GitHub Actions.

## Otevřené otázky

- Bude rozšíření veřejné, nebo interní? U veřejné verze je potřeba řešit licenci, podepisování a název, protože „SQL Prompter“ se hodně podobá produktu SQL Prompt od Redgate.
- Je na vývojovém počítači Visual Studio 2026 s nástroji pro vývoj rozšíření?

## Zdroje

- [SSMS FAQ][faq]
- [Poznámky k rozšíření pro SSMS 22 (sqlfluff-extension-ssms)][sqlfluff]
- [Modern completion walkthrough][completion]
- [`ScriptFactory.CurrentlyActiveWndConnectionInfo`][scriptfactory]
- [Microsoft SqlParser][sqlparser]

[faq]: https://learn.microsoft.com/en-sg/ssms/faq
[sqlfluff]: https://github.com/mnigliazzo/sqlfluff-extension-ssms/issues/69
[completion]: https://github.com/Microsoft/vs-editor-api/wiki/Modern-completion-walkthrough
[scriptfactory]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.management.ui.vsintegration.editors.scriptfactory.currentlyactivewndconnectioninfo?view=sqlserver-2016
[sqlparser]: https://github.com/microsoft/SqlParser
