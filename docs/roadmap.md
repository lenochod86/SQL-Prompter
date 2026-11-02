# SQL Prompter: plán

Cíl je plně nahradit SQL Prompt od Redgate v každodenní práci v SSMS 22, tedy IntelliSense, snippety, formátování a nastavení. Hlavní důraz je na spolehlivé IntelliSense, které nabídne všechno, co v daném místě dává smysl, včetně návrhu celých JOINů podle cizích klíčů a názvů sloupců.

Architektura a ověřená fakta o SSMS jsou v [architecture.md](architecture.md). Tento dokument řeší, co a v jakém pořadí stavět.

## Výchozí stav (2026-11-02)

- MVP 0.2.1 běží v SSMS 22.11 a nabízí klíčová slova, schémata, tabulky, pohledy, sloupce, aliasy a vestavěné funkce.
- Vlastní seznam se zobrazuje všude, vestavěné IntelliSense zůstává jen kvůli podtrhávání chyb a popiskům.
- Metadata se načítají na pozadí při aktivaci okna. Podporované je přihlášení SQL Serverem.
- Výsledek 119 jednotkových testů: všechny prošly. Ruční test v SSMS průběžně.

## Inventář funkcí

Přehled toho, co SQL Prompt (a u JOINů [dbForge SQL Complete][dbforge] a [ApexSQL Complete][apexsql]) nabízí a co z toho chceme. Priorita: **A** nutné pro náhradu, **B** výrazně pomáhá, **C** později nebo možná.

### IntelliSense

| Funkce | Priorita | Etapa |
|---|---|---|
| Klíčová slova, schémata, tabulky, pohledy, sloupce, aliasy | A | hotovo (MVP) |
| CTE, odvozené tabulky, dočasné tabulky a tabulkové proměnné ze skriptu včetně jejich sloupců | A | 2 |
| Proměnné a parametry (`@x`) včetně typu | A | 2 |
| Automatický alias při vložení tabulky (`dbo.OrderItems oi`) | A | 2 |
| Objekty jiné databáze (`db.schema.tabulka`), synonyma, `USE` ve skriptu | A | 2 |
| **Návrh JOINu**: tabulky související cizím klíčem nahoře a celý `JOIN … ON …` jako jedna položka | A | 3 |
| **Návrh podmínky za `ON`**: podle cizích klíčů, pak podle názvů sloupců (`UserId` ↔ `Users.Id`, stejné názvy) | A | 3 |
| Sloupce se seznamem v `INSERT INTO t (` a kostra `VALUES` | A | 4 |
| Procedury v `EXEC` a jejich parametry, skalární a tabulkové funkce s parametry | A | 4 |
| Nápověda k parametrům při psaní (signature help) | B | 4 |
| Rozbalení `*` na seznam sloupců (Tab za `*`) | A | 4 |
| Popisek objektu po najetí myší (sloupce, typy, definice) | B | 4 |
| `GROUP BY` doplněné ze sloupců v `SELECT`, aliasy v `ORDER BY` | B | 4 |
| Řazení podle používání (lokální statistika, nic se neodesílá) | B | 4 |
| Návrhy klíčových slov podle gramatiky místa (`ORDER BY` jen kde dává smysl …) | B | 4 |
| Kontrola a doplnění středníků | C | 7 |

### Snippety

| Funkce | Priorita | Etapa |
|---|---|---|
| Vestavěná sada snippetů s běžnými zkratkami (`ssf`, `scf`, `ii`, `uu`, `df`, `ct` …) | A | 5 |
| Zástupné symboly: kurzor, vybraný text, datum, uživatel, databáze, pole pro vyplnění | A | 5 |
| Vlastní snippety uživatele, soubor ve sdíleném adresáři pro tým | A | 5 |
| **Import snippetů ze SQL Promptu** | A | 5 |
| Editor snippetů v nastavení | B | 5 |

### Formátování

| Funkce | Priorita | Etapa |
|---|---|---|
| Formátovat dokument nebo výběr jednou zkratkou | A | 6 |
| Pojmenované styly s detailním nastavením (zarovnání, čárky, odsazení, zalomení, velikost písmen) | A | 6 |
| Zachování komentářů a jistota, že se nezmění význam skriptu | A | 6 |
| Velká písmena u klíčových slov během psaní | B | 6 |
| Import stylu ze SQL Promptu (co půjde převést) | B | 6 |

### Refaktoring a nástroje

| Funkce | Priorita | Etapa |
|---|---|---|
| Doplnit schémata k názvům objektů (qualify object names) | B | 7 |
| Rozbalit všechny `*` ve skriptu | B | 7 |
| Přejmenovat alias nebo proměnnou ve skriptu | B | 7 |
| Najít nepoužité proměnné a parametry | B | 7 |
| Definice objektu (F12) a vyhledání v Object Exploreru | B | 7 |
| Spustit aktuální příkaz pod kurzorem | B | 7 |
| Statická analýza: `DELETE`/`UPDATE` bez `WHERE`, `SELECT *`, `NOLOCK`, zastaralá syntaxe … | B | 8 |
| Historie záložek a obnovení neuložených oken | C | 9 |
| Barvení záložek podle serveru nebo prostředí | C | 9 |
| AI funkce (vysvětlení, oprava dotazu) | C | mimo plán |

### Nastavení

Nastavení vzniká průběžně od etapy 1:

- stránky v **Tools → Options → SQL Prompter**;
- soubor JSON, který jde exportovat a sdílet s týmem;
- příkazy v menu **SQL Prompter**;
- klávesové zkratky přiřaditelné v **Tools → Options → Keyboard**, výchozí jako v SQL Promptu tam, kde se nebijí se SSMS.

## Technické předpoklady

Tyto základy se staví v etapách, které je potřebují, ale mají vliv na celý návrh:

1. **Model skriptu.** Dnešní analyzátor odpovídá jen na otázku „co je na pozici kurzoru“. Etapy 2–4 a 7 potřebují znát celý skript: definice CTE, dočasných tabulek, proměnných a sloupce odvozených tabulek. Vznikne model skriptu, který se počítá na pozadí po změně textu a cachuje se pro snímek bufferu. Analyzátor kontextu na vlákně UI ho jen čte.
2. **ScriptDOM pro platné příkazy.** [ScriptDOM je open source pod licencí MIT][scriptdom] a podporuje netstandard2.0. Hodí se pro formátování, refaktoring a analýzu, kde je skript platný. Pro našeptávání v rozepsaném SQL zůstává vlastní tolerantní analyzátor (dva parsery, viz architektura). Pozor: generátor skriptu ze ScriptDOM zahazuje komentáře, formátovač proto musí pracovat nad tokeny, nejen nad stromem.
3. **Rozšíření metadat.** Do snímku přibudou primární a cizí klíče (včetně vícesloupcových), indexy, procedury a funkce s parametry, synonyma, uživatelské typy a databáze na serveru. Snímky dalších databází se načítají až při použití třídílného názvu.
4. **Nastavení.** Jedna třída nastavení v Core s výchozími hodnotami, kterou čtou analyzátor, návrhy, snippety i formátovač. SSMS vrstva ji jen zobrazuje a ukládá.
5. **Výkon.** Měřítko a testy na velkých skriptech (10 000 řádků) a velkých databázích (desítky tisíc objektů). Cíl z architektury, do 50 ms pro seznam, platí i pro JOINy.
6. **Bezpečnost změn.** Každá úprava textu (formátování, refaktoring) se ověřuje porovnáním tokenů bez bílých znaků a komentářů. Když se liší, úprava se neprovede.

## Etapy

Pořadí sleduje hodnotu pro každodenní práci: nejdřív spolehlivost, pak věci, které chybí nejvíc (znalost skriptu, JOINy), potom rychlé výhry (snippety) a nakonec velké celky (formátování, refaktoring).

### 1. Stabilizace MVP (malá)

- Projít ruční test v SSMS a opravit nalezené chyby: chování vkládání, prázdný seznam u proměnných, filtr SQL Prompter.
- Ověřit přihlášení Windows a najít cestu pro Entra ID (`RenewableToken`).
- Obnova metadat po DDL v okně (`CREATE`/`ALTER`/`DROP` provedené přes Execute).
- Základ nastavení: stránka v Options, zapnutí a vypnutí rozšíření, velikost písmen klíčových slov, chování vkládání.
- Test výkonu na velké databázi.

**Hotovo, když:** týden běžné práce bez zásahu do nastavení a bez chyby v logu.

### 2. Znalost skriptu (střední)

- Model skriptu (viz technické předpoklady).
- Sloupce CTE, odvozených tabulek, `#tabulek` (`CREATE TABLE`, `SELECT … INTO`) a `@tabulek`.
- Proměnné a parametry s typy, také parametry procedury při úpravě `ALTER PROCEDURE`.
- Automatické aliasy (z velkých písmen nebo prvních písmen slov, s ohledem na kolize).
- Třídílné názvy a sledování `USE`.

**Hotovo, když:** v dotazu s CTE, dočasnou tabulkou nebo proměnnou nikde nechybí návrh, který by nabídl SQL Prompt.

### 3. JOINy (střední)

- Metadata cizích klíčů včetně vícesloupcových a odkazů na sebe sama.
- Za `JOIN` nahoře tabulky, které s tabulkami v dotazu spojuje cizí klíč, a k nim položka s celou klauzulí: `dbo.Orders o ON o.UserId = u.Id`.
- Za `ON` návrhy úplných podmínek:
  1. podle cizích klíčů;
  2. podle názvů: `XxxId` ↔ `Xxx.Id`, stejný název a typ;
  3. podle pravidel, která si uživatel nastaví (předpony, přípony, výjimky).
- Řazení: cizí klíč > shoda názvu podle konvence > shoda názvu a typu.
- Testy na realistickém schématu (například vlastní databáze uživatele nebo AdventureWorks).

**Hotovo, když:** u běžných dotazů nad vaší databází se správný JOIN objeví na prvním místě.

### 4. Snippety (malá až střední)

- Formát snippetů v JSON a vestavěná sada.
- Rozbalení zkratkou s Tabem a návrhy snippetů v seznamu (vlastní ikona a filtr).
- Zástupné symboly a pohyb mezi poli Tabem.
- Import ze SQL Promptu (export jeho snippetů jako XML nebo JSON).
- Správa v Options a sdílená složka pro tým.

**Hotovo, když:** všechny snippety, které teď používáte v SQL Promptu, fungují po importu stejně.

### 5. Rozšířené IntelliSense (velká)

- `INSERT` se seznamem sloupců a kostrou `VALUES`.
- `EXEC` s procedurami a parametry, funkce s parametry, nápověda k parametrům.
- Rozbalení `*`, popisky po najetí myší, doplnění `GROUP BY`.
- Řazení podle používání.

### 6. Formátování (velká)

- Formátovač nad tokeny s informacemi o struktuře ze ScriptDOM, se zachováním komentářů.
- Sada voleb podle stylů SQL Promptu a několik výchozích stylů.
- Testy se zlatými soubory (vstup, styl, očekávaný výstup) a kontrola, že se nezměnil význam.
- Formátování výběru i celého dokumentu, velká písmena během psaní.
- Náhled stylu v Options.

**Hotovo, když:** formátovač dá na vašich skriptech výsledek, který nemusíte ručně upravovat.

### 7. Refaktoring a nástroje (střední)

- Doplnění schémat, rozbalení `*`, přejmenování aliasu nebo proměnné, nepoužité proměnné, středníky.
- Definice objektu (F12), vyhledání v Object Exploreru, spuštění aktuálního příkazu.

### 8. Analýza kódu (střední)

- Pravidla jako podtržení s vlastním popiskem, nastavitelná a vypínatelná.
- Musí se ověřit, že nekoliduje s podtrháváním vestavěného IntelliSense.

### 9. Pracovní prostředí (podle zájmu)

- Historie záložek, obnovení neuložených oken, barvy záložek podle prostředí.

### Průběžně

- Distribuce: podepsaný VSIX, verze z CI, aktualizace (např. GitHub Releases nebo soukromá galerie).
- Rozhodnutí o názvu, licenci a zveřejnění (viz otevřené otázky).

## Rizika

- **Nepodporované API SSMS.** Aktualizace SSMS může rozbít připojení přes `ScriptFactory`, potlačení vestavěného seznamu nebo menu. Ochrana: diagnostika v logu, rychlé ověření po každé aktualizaci SSMS a co nejtenčí vazba na SSMS.
- **Formátování.** Je to nejpracnější část a chyba v něm poškodí skript. Ochrana: kontrola tokenů, zlaté testy, formátování jen na příkaz uživatele.
- **Výkon u velkých skriptů.** Model skriptu se musí počítat přírůstkově nebo po dávkách mezi `GO`, jinak zpomalí psaní.
- **Klávesové zkratky.** Některé zkratky SQL Promptu SSMS používá jinak. Výchozí zkratky se musí vybrat tak, aby nepřebíjely zkratky SSMS.
- **Entra ID.** Bez vlastního připojení nejsou metadata. Pokud nepůjde použít token SSMS, bude potřeba vlastní přihlášení přes MSAL.

## Otevřené otázky

1. Které funkce SQL Promptu používáte denně? Podle toho upravím pořadí etap 4 až 7.
2. Máte vlastní snippety nebo styl formátování v SQL Promptu? Export by se hodil jako testovací data pro import.
3. Jak velké jsou databáze, se kterými pracujete (počet tabulek a sloupců)? Určí to, kolik metadat se smí načítat najednou.
4. Používáte Entra ID nebo Windows přihlášení?
5. Bude rozšíření jen pro vás a tým, nebo veřejné? Ovlivňuje to název („SQL Prompter“ je blízko „SQL Prompt“), licenci a podepisování.
6. Které klávesové zkratky ze SQL Promptu chcete zachovat (např. formátování)?

[dbforge]: https://devart.com/dbforge/sql/sqlcomplete/code-completion.html
[apexsql]: https://blog.apexsql.com/?p=172
[scriptdom]: https://github.com/microsoft/sqlscriptdom
