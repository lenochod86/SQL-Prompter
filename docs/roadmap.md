# SQL Prompter: plán

Cíl je nahradit SQL Prompt od Redgate v každodenní práci v SSMS 22. Architektura a ověřená fakta o SSMS jsou v [architecture.md](architecture.md), tento dokument řeší, co a v jakém pořadí stavět.

**Stav:** odsouhlasené pořadí a rozhodnutí (2026-11-03), viz [Rozhodnutí](#rozhodnutí) na konci.

## Priority uživatele

1. **IntelliSense**, které nabídne všechno a dá se na něj spolehnout, včetně návrhu JOINů a automatických aliasů.
2. **Snippety**, včetně převzetí stávajících snippetů ze SQL Promptu.
3. **Barvení záložek dotazů** podle serveru a databáze.
4. **F12**: otevření kódu procedury, pohledu, funkce nebo triggeru v novém okně dotazu.
5. **Zkratky:** F12, Ctrl+Mezerník, Shift+F5 (spuštění příkazu pod kurzorem). Další přibudou.

Uživatel používá skoro všechny funkce SQL Promptu. Nejvíc IntelliSense, barvu záložky, F12, snippety a aliasy. Formátování chce taky, ale až nakonec.

Prostředí:
- databáze mají přes sto tabulek a hodně procedur, pohledů a triggerů;
- přihlašuje se přes Windows a SQL login, Entra ID se nepoužívá;
- zatím pro jednoho uživatele, později open source pod licencí MIT.

## Podklady

Uživatel dodal export ze SQL Promptu 10.14:
- nastavení (`Prompt.settings`, ZIP s XML);
- 127 snippetů ve formátu JSON a jejich zálohu v XML;
- vlastní styl formátování ve dvou formátech;
- snímky obrazovky voleb a funkcí.

Podklady leží mimo repozitář a do něj se nekopírují, protože obsahují názvy serverů, databází a interní snippety. Testy importu proto používají vlastní ukázková data se stejnou strukturou.

Binární soubory SQL Promptu se neanalyzují ani nepoužívají. Jde o proprietární software a k náhradě stačí exporty a popis chování.

Z exportu vyplývá, co uživatel používá:

| Oblast | Nastavení v SQL Promptu | Dopad na plán |
|---|---|---|
| Vkládání návrhu | Enter, mezera a Tab vkládají; tečka, čárka, závorky a středník ne | Fáze 0 |
| Kvalifikace | Objekty se schématem, sloupce s aliasem | Fáze 1 |
| Aliasy | Automatické aliasy s `AS`, 10 vlastních aliasů (název tabulky → alias), bez ignorovaných předpon | Fáze 1 |
| Sloupce | Po `SELECT` všechny sloupce databáze, sloupce v pořadí z tabulky | Fáze 1 |
| JOINy | Podle cizích klíčů, shody názvů sloupců a jednotlivých sloupců vícesloupcových klíčů; prohození pořadí sloupců v podmínce | Fáze 2 |
| Snippety | Zástupné symboly `$CURSOR$`, `$SELECTEDTEXT$`, `$SELECTIONSTART$`/`$SELECTIONEND$`, `$DATE(formát)$`, `$USER$`, `$DBNAME$` a vlastní pole s výchozí hodnotou; dialog pro jejich vyplnění | Fáze 3 |
| Barvy | 8 prostředí (název a barva) a 17 pravidel server/databáze → prostředí, barva i ve stavovém řádku | Fáze 4 |
| Vložený kód | Úplný `INSERT` bez `VALUES`, parametry procedur s výchozími hodnotami a `DECLARE` pro OUTPUT, závorky u funkcí, `ALTER` při úpravě objektu | Fáze 5, 6 |
| Ostatní | Historie dotazů a obnova záložek, analýza kódu, varování před spuštěním, zvýraznění stejných objektů, automatické uzavírání `'` a `[` | Fáze 7 |
| Formátování | Vlastní styl (tabulátory, čárky na začátku řádku, zarovnání aliasů, velká písmena …) a akce: `N` u řetězců, středníky, odstranění zbytečných závorek, přidání `AS` | Fáze 8 |

## Fáze

Každá fáze končí verzí VSIX, kterou uživatel ručně otestuje. Velikosti jsou orientační: **S** dny, **M** týden až dva, **L** několik týdnů.

### Fáze 0: Doladění MVP (S)

- Vkládání jako v SQL Promptu: Enter, mezera a Tab návrh vloží. Tečka, čárka, závorky a středník ho nevloží, jen zavřou seznam a zapíšou se. Tečka pak otevře sloupce, takže `e.` funguje i bez vkládání. Klávesy pro vkládání budou nastavitelné jako v SQL Promptu.
- Ověřit prázdný seznam u proměnných, filtr SQL Prompter a výkon na velké databázi.
- Ověřit přihlášení přes Windows.
- Obnova metadat po DDL spuštěném v okně (`CREATE`, `ALTER`, `DROP`).

### Fáze 1: Znalost skriptu a aliasy (M)

- **Základ nastavení:** stránky v Tools → Options → SQL Prompter, soubor JSON, export a import. Každá další funkce přidává svoje volby.
- **Import nastavení ze SQL Promptu** (`.settings`), postupně podle toho, co je hotové. V této fázi aliasy, kvalifikace a vkládání.
- **Model skriptu:** definice ve skriptu, počítané na pozadí:
  - sloupce CTE a odvozených tabulek;
  - `#tabulky` z `CREATE TABLE` a `SELECT … INTO`;
  - `@tabulky`;
  - proměnné a parametry s typy, včetně parametrů upravované procedury.
- **Automatické aliasy** při vložení tabulky:
  - z velkých písmen názvu (`OrderItems` → `oi`) a s ohledem na kolize v dotazu;
  - vlastní aliasy;
  - ignorované předpony;
  - volba `AS`.
- **Kvalifikace:** objekty se schématem, sloupce s aliasem (`a.Sloupec`).
- Po `SELECT` bez `FROM` sloupce všech tabulek databáze. Po výběru sloupce se doplní `FROM` s jeho tabulkou (a aliasem).
- Třídílné názvy a sledování `USE`. Synonyma.
- **Linked servery, načítané líně:**
  - na začátku se nabízí jen jejich názvy (`sys.servers`), nic dalšího se nenačítá;
  - teprve po výběru linked serveru se načtou jeho databáze;
  - po výběru databáze schémata a objekty;
  - sloupce až při použití tabulky.

  Úvodní načtení tak nezdržuje ani zákazník s pěti linked servery plnými tabulek.

### Fáze 2: JOINy (M)

- Metadata primárních a cizích klíčů včetně vícesloupcových.
- Za `JOIN` nahoře tabulky propojené cizím klíčem s tabulkami v dotazu a položka s celou klauzulí `Tabulka AS t ON t.IdX = x.IdX`.
- Za `ON` úplné podmínky:
  1. podle cizích klíčů;
  2. podle shody názvů sloupců;
  3. volitelně podle shody typů;
  4. jednotlivé sloupce vícesloupcových klíčů.
- Volba pořadí sloupců v podmínce (nová tabulka vlevo nebo vpravo).
- Testy na schématu podobném skutečnému (konvence `IdTabulka` ↔ `Tabulka.IdTabulka`).

### Fáze 3: Snippety (M)

- **Úložiště ve stejném formátu JSON jako SQL Prompt** (`prefix`, `description`, `body`, `placeholders`). Stávající složku se snippety jde načíst přímo nebo zkopírovat. Import i ze záložního XML.
- **Návrhy snippetů v seznamu** s vlastní ikonou a filtrem a rozbalení zkratky Tabem.
- **Zástupné symboly** podle tabulky výše. Způsob vyplňování vlastních polí, tedy dialog, nebo pole v textu přepínaná Tabem, je zatím otevřený (viz Rozhodnutí).
- **Snippety nad výběrem:** `$SELECTEDTEXT$` (např. obalení výběru `ISNULL(…)`), vyvolané z nabídky akcí nad výběrem.
- **Správa snippetů:** seznam, úpravy, nový snippet z výběru.

### Fáze 4: Barvení podle serveru a databáze (S až M, nejdřív průzkum)

- **Průzkum:** jak v SSMS 22 obarvit záložku dokumentu, rámeček okna a stavový řádek. SSMS umí vlastní barvu připojení jen ve stavovém řádku a ta se nastavuje v dialogu připojení. Pro barvu záložky ani rámečku API ověřené nemáme.
- Obarví se záložka, a pokud to jde, i rámeček okna dotazu a stavový řádek.
- Prostředí (název a barva) a pravidla server + databáze → prostředí, se zástupnými znaky.
- Import barev ze SQL Promptu.

### Fáze 5: Navigace a spouštění (S až M, nejdřív průzkum)

- **F12 na objektu pod kurzorem:**
  - procedura, pohled, funkce nebo trigger se otevře v novém okně připojeném ke stejnému serveru a databázi jako `CREATE OR ALTER`;
  - tabulka otevře skript `CREATE TABLE`.
- **Průzkum:** otevření nového okna s připojením. Kandidát je `ScriptFactory.CreateNewBlankScript` s připojením aktuálního okna, které už umíme číst.
- **Shift+F5: spuštění příkazu pod kurzorem.** Hranice příkazu určí stávající dělení na příkazy, příkaz se vybere, spustí přes Query.Execute a výběr se vrátí. Ověřit kolizi zkratky v SSMS.
- **Ctrl+F12:** zobrazení objektu v Object Exploreru (později).

### Fáze 6: Rozšířené IntelliSense (L)

- **`INSERT`:** úplný příkaz se seznamem sloupců, volitelně s `VALUES` a výchozími hodnotami.
- **`EXEC`:** procedury s parametry, výchozími hodnotami a `DECLARE` pro výstupní parametry.
- **Funkce** se závorkami a parametry.
- **Rozbalení `*` Tabem** na seznam sloupců s aliasy.
- **Panel s definicí objektu** vedle seznamu (skript `CREATE`, jako na snímku ze SQL Promptu) a nápověda k parametrům.
- **Řazení podle používání.** Statistika zůstává lokálně, nic se neodesílá.

### Fáze 7: Nástroje (M až L)

Uživatel používá skoro všechny, pořadí uvnitř fáze se domluví před jejím začátkem.

- Historie dotazů a obnova záložek po spuštění SSMS.
- Refaktoring:
  - kvalifikace názvů;
  - rozbalení `*`;
  - vložení středníků;
  - odstranění hranatých závorek;
  - nepoužité proměnné;
  - přehled skriptu.
- Varování před spuštěním (`DELETE` nebo `UPDATE` bez `WHERE` …) a analýza kódu.
- Akce nad výběrem: přidat čárky, přidat uvozovky a čárky.
- Zvýraznění stejných objektů ve skriptu, automatické uzavírání `'` a `[`.
- Script as INSERT z mřížky výsledků (zatím nevíme, jestli to v SSMS 22 jde).

### Fáze 8: Formátování (L)

Uživatel ho chce, ale není to priorita, proto je na konci.

- Formátovač nad tokeny se strukturou ze ScriptDOM. Zachová komentáře, a když by se lišil význam, úpravu neprovede.
- Volby podle stylu SQL Promptu a import stylu `.json`. Uživatelův styl je hlavní testovací případ.
- Akce formátování: `N` u řetězců, středníky, odstranění zbytečných závorek, přidání nebo odebrání `AS`, velikost písmen, volitelně kvalifikace a rozbalení `*`.
- Formátování dokumentu nebo výběru a velká písmena během psaní.

## Rizika

- **Nepodporované API SSMS.** Barvení záložek, F12 do nového okna a spuštění výběru závisí na API, které nemusí existovat nebo se může změnit. Proto se tyto fáze otevírají průzkumem.
- **Formátování** je nejpracnější a chyba v něm poškodí skript. Ochranou je kontrola tokenů, zlaté testy a formátování jen na příkaz uživatele.
- **Výkon** u velkých skriptů a databází: model skriptu po dávkách mezi `GO`, metadata cachovaná po databázích.

## Rozhodnutí

Uživatel 2026-11-03:

1. **Vkládání návrhů** jako v exportovaném nastavení: Enter, mezera a Tab vkládají; tečka, čárka, závorky, středník a `]` ne. Klávesy jsou nastavitelné.
2. **Vlastní pole ve snippetech:** zatím otevřené. Na výběr je dialog (jako SQL Prompt) a pole v textu přepínaná Tabem (jako Visual Studio).
3. **Barvení:** záložka, a pokud to půjde, i rámeček okna (a stavový řádek).
4. **F12:** procedury, pohledy, funkce a triggery jako `CREATE OR ALTER`, tabulky jako `CREATE TABLE`.
5. **`SELECT` bez `FROM`:** po výběru sloupce doplnit `FROM` s jeho tabulkou.
6. **Formátování** až na konec.
7. **Nástroje:** používá skoro všechny, pořadí se domluví před fází 7.
8. **Linked servery:** ano, ale líně. Nejdřív jen názvy, objekty až po výběru serveru.
