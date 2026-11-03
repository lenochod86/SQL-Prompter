# SQL Prompter

Rozšíření s vlastním IntelliSense pro SSMS 22, náhrada SQL Promptu od Redgate. Komunikuj česky.

Tyto instrukce platí v každém prostředí: lokálně na Windows, v cloudu i přes mobil. Postupuj všude stejně.

## Dokumenty

- `docs/architecture.md`: architektura, rozhodnutí a ověřená fakta o SSMS. Přečti před návrhem nebo změnou architektury.
- `docs/roadmap.md`: fáze s checkboxy a rozhodnutí uživatele. Podle něj vybírej další práci.
- `README.md`: sestavení, instalace a ruční test v SSMS.

## Aktuální stav

MVP 0.2.x běží v SSMS 22.11. Hotové a další body jsou v `docs/roadmap.md`, další na řadě je fáze 0.

## Na začátku sezení

1. `git status`, `git fetch` a aktuální větev. Necommitnuté změny nepřepisuj a nemaž, zeptej se.
2. Přečti `docs/roadmap.md` a otevřená issue.
3. Když pokračuješ na rozpracovaném issue, přepni se na jeho větev a srovnej ji s `main` (viz níže).

## Práce s gitem a GitHubem

Repozitář: `lenochod86/SQL-Prompter`, hlavní větev `main`.

### Postup pro každou práci

1. **Issue.** Každá samostatná práce (bod nebo skupina bodů z roadmapy, chyba, průzkum) má issue na GitHubu. Pokud ještě neexistuje, založ ho dřív, než začneš psát kód. Obsah česky:
   - název: krátký, bez čísla fáze (např. „Vkládání návrhů podle nastavení“);
   - popis: cíl, rozsah, kritéria hotovo a odkaz na bod v `docs/roadmap.md`;
   - štítky podle druhu: `feature`, `bug`, `research`, `docs` (založ, když chybí) a fáze (`fáze-0` …).
2. **Větev** z aktuálního `main`: `<číslo-issue>-<krátký-popis>`, malými písmeny a s pomlčkami, např. `12-vkladani-navrhu`. Na jedné větvi jen jedno issue.
3. **Commity** malé a logicky ucelené:
   - zpráva anglicky, v rozkazovacím způsobu, předmět do 72 znaků;
   - v těle proč a co, na konci `Refs #<číslo>`;
   - před každým commitem projdou testy (`dotnet test tests/SqlPrompter.Tests`) a sestavení VSIX, pokud se měnil `src/SqlPrompter.Ssms` a jsi na Windows.
4. **Průběžně** větev pushuj (`git push -u origin <větev>`), aby šla práce převzít z jiného prostředí.
5. **Pull request** do `main`, jakmile je práce připravená k posouzení:
   - česky: co se změnilo, jak to otestovat v SSMS, co ověřené je a co ne;
   - v popisu `Closes #<číslo>`.
6. **Schválení.** Merguje se až po výslovném souhlasu uživatele. Uživatel obvykle testuje VSIX z větve v SSMS.
7. **Squash merge** PR do `main`:
   - zpráva commitu = název PR a číslo PR, tělo shrne změnu;
   - potom smaž vzdálenou i lokální větev a přepni se na aktuální `main`.

### Pravidla

- Do `main` nikdy necommituj ani nepushuj přímo. Výjimkou je, když to uživatel výslovně řekne.
- Na `main` nikdy nedělej force push. Na vlastní větvi jen `git push --force-with-lease`, a to jen když je to nutné (např. po rebase).
- Než větev předáš k posouzení, srovnej ji s `main` přes `git rebase origin/main`. Konflikty řeš ty a popiš je.
- Neměň historii, kterou už někdo mohl stáhnout, pokud to není nutné.
- Do repozitáře nepatří: hesla, connection stringy, názvy serverů a databází uživatele, jeho snippety, export nastavení ani nic z `E:\Prompter` (soukromé podklady a proprietární soubory SQL Promptu). Testy používají vlastní ukázková data se stejnou strukturou.
- Binární soubory SQL Promptu neanalyzuj a nepoužívej. Stačí exporty a popis chování.
- Generované soubory (`bin/`, `obj/`, `.vs/`) zůstávají v `.gitignore`.
- Když nemáš přístup ke GitHubu (není `gh` ani GitHub nástroje), řekni to uživateli. Issue a PR nevynechávej potichu.

## Architektura a kód

- Logika patří do `SqlPrompter.Core` a `SqlPrompter.Metadata` (netstandard2.0). Tyto projekty nesmí záviset na VS SDK ani na API SSMS.
- `SqlPrompter.Ssms` zůstává tenká vrstva. Typy SSMS nepouštěj za její hranici.
- Na vlákně UI nedělej nic pomalého a při našeptávání nikdy nečekej na databázi.
- Metadata čti jen pro čtení. Neukládej hesla a nepřidávej telemetrii.
- API SSMS je nepodporované a nezdokumentované. Předpoklady ověřuj za běhu v SSMS a výsledky zapisuj do `docs/architecture.md`.
- Uživatel chce vidět jen náš seznam. Náš zdroj se proto účastní všude mimo řetězce a komentáře a tím vestavěný seznam potlačí. Kde nic nevíme, nabídni prázdný seznam (`ContextKind.Name`) nebo klíčová slova (`ContextKind.Fallback`), nikdy `DoesNotParticipateInCompletion`.
- Nové příkazy patří do menu SQL Prompter (`SqlPrompterCommands.vsct`).
- Nový kontext v `ContextAnalyzer` pokryj testem s kurzorem vyznačeným `|`. Každá oprava chyby má test, který ji zachytí.
- Komentáře v kódu česky, identifikátory anglicky, styl podle okolního kódu.

## Sestavení a verze

- Testy: `dotnet test tests/SqlPrompter.Tests` (kdekoli).
- VSIX: `dotnet build src/SqlPrompter.Ssms/SqlPrompter.Ssms.csproj -c Release` (jen Windows). Výsledek je v `src/SqlPrompter.Ssms/bin/Release/net48/SqlPrompter.Ssms.vsix`.
- Když `dotnet` na PATH chybí, nainstaluj .NET SDK do scratchpadu skriptem `dotnet-install.ps1` (bez administrátora) a nastav `DOTNET_ROOT`.
- S každým VSIX, který dostane uživatel, zvyš verzi v `source.extension.vsixmanifest` (jinak se přes starou nenainstaluje) a ověř ji uvnitř `.vsix`.
- Po změně `.vsct` zvyš verzi v `ProvideMenuResource`.

## Prostředí

- **Lokálně na Windows:** jde sestavit VSIX a nainstalovat ho do SSMS 22. Instalaci, odinstalaci a zásahy do konfigurace SSMS spouští uživatel. Ty mu dej přesný příkaz.
- **V cloudu:** SSMS ani sestavení VSIX nejsou k dispozici. Pracuj na Core, Metadata a testech. VSIX sestaví CI (`.github/workflows/build.yml`).
- **Přes mobil:** uživatel nemusí mít po ruce SSMS. Výsledky z SSMS si vyžádej jako snímek obrazovky nebo log (`%LOCALAPPDATA%\SqlPrompter\sqlprompter.log`).

## Jak pracovat

- Když něco nevíš nebo jde o rozhodnutí uživatele, zeptej se. Nevymýšlej si chování SSMS, SQL Promptu ani přání uživatele.
- Co se týká SSMS, neoznačuj za ověřené, dokud to neproběhlo v SSMS 22. Checkbox v `docs/roadmap.md` odškrtni až po ověření, na větvi daného issue.
- Výsledky hlas přesně: co prošlo, co selhalo (s výstupem) a co se netestovalo.
- Rozhodnutí uživatele zapisuj do `docs/roadmap.md` (sekce Rozhodnutí), ověřená fakta o SSMS do `docs/architecture.md`.
