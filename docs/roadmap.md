# SQL Prompter: plán

Cíl je nahradit SQL Prompt od Redgate v každodenní práci v SSMS 22. Architektura a ověřená fakta o SSMS jsou v [architecture.md](architecture.md), tento dokument řeší, co a v jakém pořadí stavět.

**Stav:** návrh k projednání (2026-11-03). Otázky, na kterých závisí rozsah, jsou na konci.

## Priority uživatele

1. **IntelliSense**, které nabídne všechno a dá se na něj spolehnout, včetně návrhu JOINů a automatických aliasů.
2. **Snippety**, včetně převzetí stávajících snippetů ze SQL Promptu.
3. **Barvení záložek dotazů** podle serveru a databáze.
4. **F12**: otevření kódu procedury, pohledu, funkce nebo triggeru v novém okně dotazu.
5. **Zkratky:** F12, Ctrl+Mezerník, Shift+F5 (spuštění příkazu pod kurzorem). Další přibudou.

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
| Formátování | Vlastní styl (tabulátory, čárky na začátku řádku, zarovnání aliasů, velká písmena …) a akce: `N` u řetězců, středníky, odstranění zbytečných závorek, přidání `AS` | Fáze 7 |
| Ostatní | Historie dotazů a obnova záložek, analýza kódu, varování před spuštěním, zvýraznění stejných objektů, automatické uzavírání `'` a `[` | Fáze 8 |

## Fáze

Každá fáze končí verzí VSIX, kterou uživatel ručně otestuje. Velikosti jsou orientační: **S** dny, **M** týden až dva, **L** několik týdnů.

### Fáze 0: Doladění MVP (S)

- Vkládání jako v SQL Promptu: Enter, mezera a Tab návrh vloží. Tečka, čárka, závorky a středník ho nevloží, jen zavřou seznam a zapíšou se. Tečka pak otevře sloupce, takže `e.` funguje i bez vkládání.
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
- Po `SELECT` sloupce všech tabulek databáze. Po výběru sloupce se doplní i tabulka? (otázka 5)
- Třídílné názvy a sledování `USE`. Synonyma.

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
- **Zástupné symboly** podle tabulky výše. U vlastních polí dialog, nebo pole v textu přepínaná Tabem? (otázka 2)
- **Snippety nad výběrem:** `$SELECTEDTEXT$` (např. obalení výběru `ISNULL(…)`), vyvolané z nabídky akcí nad výběrem.
- **Správa snippetů:** seznam, úpravy, nový snippet z výběru.

### Fáze 4: Barvení podle serveru a databáze (S až M, nejdřív průzkum)

- **Průzkum:** jak v SSMS 22 obarvit záložku dokumentu, případně rámeček okna nebo stavový řádek. SSMS umí vlastní barvu připojení jen ve stavovém řádku a ta se nastavuje v dialogu připojení. Pro barvu záložky API ověřené nemáme.
- Prostředí (název a barva) a pravidla server + databáze → prostředí, se zástupnými znaky.
- Import barev ze SQL Promptu.

### Fáze 5: Navigace a spouštění (S až M, nejdřív průzkum)

- **F12 na objektu pod kurzorem:**
  - procedura, pohled, funkce nebo trigger se otevře v novém okně připojeném ke stejnému serveru a databázi jako `ALTER` (nebo `CREATE OR ALTER`, otázka 4);
  - tabulka otevře skript `CREATE TABLE` (otázka 4).
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

### Fáze 7: Formátování (L)

- Formátovač nad tokeny se strukturou ze ScriptDOM. Zachová komentáře, a když by se lišil význam, úpravu neprovede.
- Volby podle stylu SQL Promptu a import stylu `.json`. Uživatelův styl je hlavní testovací případ.
- Akce formátování: `N` u řetězců, středníky, odstranění zbytečných závorek, přidání nebo odebrání `AS`, velikost písmen, volitelně kvalifikace a rozbalení `*`.
- Formátování dokumentu nebo výběru a velká písmena během psaní.

### Fáze 8: Nástroje (podle zájmu)

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

## Rizika

- **Nepodporované API SSMS.** Barvení záložek, F12 do nového okna a spuštění výběru závisí na API, které nemusí existovat nebo se může změnit. Proto se tyto fáze otevírají průzkumem.
- **Formátování** je nejpracnější a chyba v něm poškodí skript. Ochranou je kontrola tokenů, zlaté testy a formátování jen na příkaz uživatele.
- **Výkon** u velkých skriptů a databází: model skriptu po dávkách mezi `GO`, metadata cachovaná po databázích.

## Otázky

1. **Vkládání návrhů:** převzít nastavení ze SQL Promptu, tedy Enter, mezera a Tab vkládají, tečka, čárka a závorky ne?
2. **Vlastní pole ve snippetech:** dialog jako v SQL Promptu, nebo pole přímo v textu, mezi kterými se přepíná Tabem (jako ve Visual Studiu)?
3. **Barvení:** stačí obarvit záložku a stavový řádek? Pomohl by i barevný rámeček okna?
4. **F12:**
   - otevírat procedury jako `ALTER`, nebo `CREATE OR ALTER`?
   - co u tabulky: skript `CREATE TABLE`, nebo nic?
5. **Sloupce po `SELECT` bez `FROM`:** při výběru sloupce z jiné tabulky doplnit `FROM` s tou tabulkou, jako to dělá SQL Prompt?
6. **Formátování:** kam patří v pořadí? V prioritách ho nemáš, ale máš vlastní styl a formátování na Ctrl+Shift+F.
7. **Fáze 8:** které nástroje opravdu používáš? Na snímcích je historie dotazů, akce nad výběrem a varování před spuštěním.
8. **Linked servery:** potřebuješ návrhy objektů přes linked server?
