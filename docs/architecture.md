# SQL Prompter: návrh architektury

Rozšíření s vlastním IntelliSense pro SQL Server Management Studio 22.

**Stav:** prototyp v SSMS 22.11 odpověděl na všechny tři otázky (viz [Prototyp](#prototyp)). MVP je napsané a otestované jednotkovými testy, v SSMS zatím neběželo (viz [MVP](#mvp)).

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
| Metadata | Dotazy na `sys.*` přes `System.Data.Common`, připojení z `Microsoft.Data.SqlClient` v procesu SSMS | Rychlé, jen pro čtení, bez závislosti na SMO a bez druhé kopie SqlClient |

SSMS 22 je postavené na Visual Studiu 2026 ([FAQ][faq]). Podle [poznámek autora jiného rozšíření][sqlfluff] se používá `Microsoft.VisualStudio.SDK` 17.14, protože balík 18.x neexistuje. Prototyp to v SSMS 22.11 potvrdil.

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
| `SqlPrompter.Metadata` | netstandard2.0 | Core a `System.Data.Common`; konkrétní `DbConnection` dodá hostitel |
| `SqlPrompter.Ssms` | net48, VSIX | VS SDK, API SSMS, oba projekty výše |
| Testy | Aktuální .NET | Core a Metadata, případy typu `SELECT u.| FROM dbo.Users u` s vyznačenou pozicí kurzoru |

## Průběh našeptání

1. Na vlákně UI (`InitializeCompletion`) se vyřízne dávka mezi řádky `GO`, rozdělí se na tokeny a určí se kontext, třeba „sloupec aliasu `u`“.
2. Ve stejném kroku se zjistí, co z metadat chybí, a jeho načtení se spustí na pozadí.
3. Na pozadí (`GetCompletionContextAsync`) se z kontextu a aktuálního snímku sestaví a seřadí návrhy.

Na databázi se v tomto průběhu nikdy nečeká. Metadata se začnou načítat už při aktivaci okna dotazu a se seznamem objektů se načtou i sloupce uživatelských objektů, takže bývají připravená dřív, než je uživatel potřebuje. Co chybí (sloupce systémových pohledů, první načtení), doplní se při dalším našeptání. Objekty ze skriptu (CTE, dočasné tabulky, proměnné) jsou etapa 3.

## Rozhodnutí

- **Dva parsery.** Během psaní je SQL skoro vždy nedokončené, proto našeptávání používá vlastní tolerantní lexer a lehkou analýzu klauzulí. ScriptDOM od Microsoftu slouží k úplné analýze platných příkazů a pozdější diagnostice. ScriptDOM je [open source pod licencí MIT](https://github.com/microsoft/sqlscriptdom) a podporuje netstandard2.0 (ověřeno 2026-11-02). Plán dalších etap je v [roadmap.md](roadmap.md).
- **Bez `Microsoft.SqlServer.Management.SqlParser` jako základu.** [Není open source][sqlparser] a podmínky jeho licence nejsou ověřené.
- **Metadata jako neměnné snímky.** Každá databáze má vlastní snímek. Sloupce uživatelských objektů se načtou spolu s objekty, sloupce systémových pohledů až při potřebě. Obnova proběhne podle `sys.objects.modify_date`, po DDL v okně a ručním příkazem.
- **Zabezpečení.** Hesla se nikam neukládají. Metadata na disk jen volitelně, protože i názvy objektů můžou být citlivé. Žádná telemetrie.
- **Cílové výkonnostní limity (návrh).** Do 50 ms pro seznam z cache a žádné čekání na síť ve vlákně UI.
- **Jen náš seznam.** Naše účast potlačí vestavěný seznam (prototyp, otázka 2). Uživatel chce vidět jen návrhy SQL Prompteru (rozhodnuto 2026-11-02), proto se rozšíření účastní všude mimo řetězce, komentáře a čísla. Vestavěné IntelliSense zůstává zapnuté kvůli podtrhávání chyb a popiskům.
  - Kde se píše nový nebo neznámý název (alias za `AS`, `@proměnná`, `#tabulka`, neznámý kvalifikátor, třídílný název), je seznam prázdný.
  - Za `CAST(x AS` a `DECLARE @x` se nabízí datové typy.
  - Na místech, kterým rozšíření nerozumí (`EXEC`, `VALUES`, `SET @x =` …), se nabízí klíčová slova, funkce a typy bez pevného výběru.
  - S CTE nebo dočasnou tabulkou se nabídnou sloupce známých zdrojů, aliasy a funkce.
  - Návrhy mají filtr „SQL Prompter“, podle kterého je seznam poznat.
  - Neověřeno: jestli prázdný seznam vestavěný seznam opravdu potlačí.
- **SqlClient z procesu SSMS.** Vlastní připojení vytváří `Microsoft.Data.SqlClient.SqlConnection` z kopie, kterou má SSMS načtenou (ověřeno prototypem, verze 6.0.0.0). VSIX žádný SqlClient nepřibaluje a Metadata pracuje jen s `DbConnection`. Balík SqlClient 6 navíc netstandard2.0 nepodporuje.
- **Systémové objekty.** Metadata se čtou ze `sys.all_objects` a `sys.all_columns`, takže `sys.objects` nebo `INFORMATION_SCHEMA.TABLES` fungují stejně jako ve vestavěném IntelliSense. Bez schématu se systémové objekty nenabízejí.
- **Vkládání.** Návrh vloží Tab, Enter a také mezera, tečka, čárka, závorky a operátory, takže `e.` vloží alias a otevře jeho sloupce. Při vkládání jen Tabem a Enterem zůstával seznam po mezeře nebo tečce otevřený (ověřeno v SSMS 22.11). Tam, kde uživatel nejspíš píše vlastní název (za zdrojem tabulky nebo za hodnotou), je výběr jen naznačený (`SoftSelection`) a tyto znaky návrh nevkládají. Klíčová slova se vkládají malými písmeny, když je tak uživatel začal psát.
- **Obnova metadat v MVP.** Nejvýš jednou za 30 s se při našeptání ověří otisk `sys.objects` (počet a nejnovější `modify_date`) a po změně se snímek načte znovu. K tomu slouží ruční příkaz SQL Prompter → Obnovit metadata.
- **Vlastní menu.** SQL Prompter má samostatné menu (rodič `IDG_VS_MM_TOOLSADDINS`). Příkazy jsou ve skupinách, další funkce do nich přibudou. SSMS stejně jako VS od verze 2019 přesouvá menu rozšíření pod **Extensions** a rozšíření to nemůže změnit. Do hlavní lišty je vrátí uživatel v **Extensions → Customize Menu…** (ověřeno v SSMS 22.11: menu pod Tools zmizelo a v hlavní liště se samo neobjevilo).
- **Sestavení VSIX.** Projekt před každým sestavením maže mezivýsledek manifestu v `obj`, jinak by přírůstkové sestavení nepřebalilo VSIX po změně verze. Obnova po DDL v okně zatím chybí.

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

**Otázka 2: ano, duplicity nevznikají.**

- Kontrolní test na `SELECT * FROM sys.ob`: s vypnutým `hello` se zobrazí vestavěný seznam SSMS, se zapnutým jen náš seznam. Vestavěné IntelliSense do asynchronní relace nepřidává žádné položky a jeho seznam se vedle našeho neukáže.
- Kde náš zdroj vrátí `DoesNotParticipateInCompletion`, vestavěné IntelliSense funguje dál. S vypnutým vestavěným IntelliSense náš zdroj funguje taky.
- Důsledek pro návrh: kde se náš zdroj účastní, nahrazuje vestavěné IntelliSense úplně, takže musí nabídnout vše potřebné. Kde nemá co nabídnout (třeba kontext, který zatím neumí), má vrátit `DoesNotParticipateInCompletion` a nechat prostor vestavěnému IntelliSense. Vestavěné IntelliSense uživatel vypínat nemusí.
- Neověřeno, jakým mechanismem SSMS vestavěné IntelliSense potlačí. Pro MVP to stačí, protože chování je stabilní a ovladatelné z našeho zdroje.

**Otázka 3: pro přihlášení SQL Serverem ano, Windows a Entra ID zbývají.**

- Vlastní připojení z údajů okna (včetně hesla z paměti SSMS a převzatého `Encrypt`/`TrustServerCertificate`) se otevře za zhruba 100 ms.
- `AdvancedOptions[DATABASE]` sleduje databázi zvolenou v rozbalovacím seznamu okna, takže stačí číst ho při každém našeptání.

- `ScriptFactory.Instance.CurrentlyActiveWndConnectionInfo` (`SQLEditors.dll` 22.200) je dosažitelný reflexí. `UIConnectionInfo` (`Microsoft.SqlServer.RegSvrEnum` 18.100) obsahuje `ServerName`, `UserName`, `AuthenticationType` (SQL Server = 1), `Password` a `InMemoryPassword` (`SecureString`) a `RenewableToken` (u SQL přihlášení `null`, kandidát pro Entra ID).
- `AdvancedOptions` obsahuje `DATABASE`, `ENCRYPT_CONNECTION`, `TRUST_SERVER_CERTIFICATE`, `CONNECTION_TIMEOUT`, `PACKET_SIZE` a údaje o serveru. Vlastní připojení musí šifrování převzít, jinak SqlClient 6 (`Encrypt=Mandatory`) selže na certifikátu podepsaném sám sebou.
- Mezi kandidátními typy je `Microsoft.SqlServer.Management.ServerInformation.IServerConnectionInformation` (`Microsoft.SqlServer.Management.SDK.SqlStudio` 22.200). Může se hodit jako náhrada.
- Neověřeno: přihlášení přes Windows a Entra ID s MFA a to, jestli se `DATABASE` změní i po `USE` ve skriptu.

Zjištěno z instalace SSMS 22.11.3:

- `productId` je `Microsoft.VisualStudio.Product.Ssms`.
- Vestavěná rozšíření používají `InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[22.0,)"`.
- Mezi instalovanými balíčky je `Microsoft.VisualStudio.Component.CoreEditor`.
- VSIXInstaller nainstaluje VSIX s cílem `Microsoft.VisualStudio.Ssms` `[22.0,23.0)` a prerekvizitou `Microsoft.VisualStudio.Component.CoreEditor` (ověřeno).
- `ScriptFactory` je dosažitelný reflexí, bez odkazu na sestavení SSMS při sestavení (ověřeno).
- Vlastní připojení může používat `Microsoft.Data.SqlClient`, který už je v procesu SSMS načtený, takže ho VSIX nemusí přibalovat (ověřeno).
- SSMS 22.11 nemá v nabídce správce rozšíření; odinstaluje se přes `VSIXInstaller.exe /uninstall:<Id>`.
- Pokud manifest povoluje i Visual Studio, VSIXInstaller může rozšíření nainstalovat tam místo do SSMS. Manifest proto cílí jen na SSMS.
- Po změně `.vsct` je potřeba zvýšit verzi v `ProvideMenuResource`, jinak SSMS zobrazí nabídky z mezipaměti.

## MVP

Kód je v `src/` a testy v `tests/`. Sestavení: `dotnet build SqlPrompter.slnx -c Release` na Windows, testy `dotnet test tests/SqlPrompter.Tests` kdekoli. Postup ručního testu je v [README](../README.md).

Co MVP umí:

- Klíčová slova na začátku příkazu, za zdrojem tabulky (`WHERE`, `JOIN` …) a ve výrazech, datové typy za `CAST(x AS` a `DECLARE @x`.
- Tabulky, pohledy a tabulkové funkce za `FROM`, `JOIN`, `APPLY`, `UPDATE`, `INSERT INTO`, `DELETE FROM`, `MERGE`, `USING` a `TRUNCATE`/`DROP`/`ALTER TABLE`, i se schématem (`sales.|`). Bez schématu se vkládají jako `schema.tabulka`.
- Sloupce aliasu nebo tabulky (`u.|`, `dbo.Users.|`) a ve výrazech (`SELECT`, `WHERE`, `ON`, `GROUP BY`, `HAVING`, `ORDER BY`, `SET`) sloupce všech zdrojů bloku, aliasy a běžné vestavěné funkce. Sloupec, který je ve více zdrojích, se vkládá s aliasem.
- Bloky dotazu: poddotaz ve výrazu vidí zdroje vnějšího dotazu, odvozená tabulka a CTE ne; `UNION` odděluje bloky. Příkazy se oddělují i bez středníku.

Stav: jednotkové testy procházejí, VSIX se sestaví. Ověření v SSMS 22 zbývá.

## Etapy

1. **Prototyp:** statický návrh „hello“ v SQL editoru a odpovědi na tři otázky výše.
2. **MVP:** klíčová slova, schémata, tabulky, sloupce, aliasy a kontext `FROM`, `JOIN`, `WHERE`.
3. **Znalost skriptu:** CTE, dočasné tabulky, proměnné a návrh podmínek `JOIN` podle cizích klíčů.
4. **Další funkce:** nápověda k parametrům procedur, popisky objektů, snippety a řazení podle používání.

## Prostředí pro vývoj

Core, Metadata a jejich testy jdou vyvíjet v cloudu. VSIX se sestavuje na Windows a SSMS v cloudovém prostředí nepoběží, takže prototyp a ruční testy běží na Windows s SSMS 22, případně přes Remote Control. Visual Studio ke sestavení potřeba není, stačí .NET SDK 8 nebo novější. VSIX sestavuje i Windows runner v GitHub Actions (`.github/workflows/build.yml`).

## Otevřené otázky

- Bude rozšíření veřejné, nebo interní? U veřejné verze je potřeba řešit licenci, podepisování a název, protože „SQL Prompter“ se hodně podobá produktu SQL Prompt od Redgate.
- Přihlášení přes Windows a Entra ID s MFA: ověřit `AuthenticationType` a jestli jde z `RenewableToken` vytvořit vlastní připojení. Do té doby se pro Entra ID metadata nenačítají a nabízí se jen klíčová slova.

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
