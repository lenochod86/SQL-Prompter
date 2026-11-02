# SQL Prompter

Rozšíření s vlastním IntelliSense pro SSMS 22. Komunikuj česky.

Před návrhem nebo změnou architektury si přečti `docs/architecture.md`. Plán etap je v `docs/roadmap.md`.

## Aktuální stav

Prototyp v `prototype/` na všechny tři otázky odpověděl kladně. MVP je v `src/` a `tests/`, testy procházejí, ale v SSMS 22 ještě neběželo. Další krok je ruční test podle `README.md`. Neověřené zůstává i připojení přes Windows a Entra ID s MFA.

## Pravidla

- Logika patří do `SqlPrompter.Core` a `SqlPrompter.Metadata` (netstandard2.0). Tyto projekty nesmí záviset na VS SDK ani na API SSMS.
- `SqlPrompter.Ssms` zůstává tenká vrstva. Typy SSMS nepouštěj za její hranici.
- Na vlákně UI nedělej nic pomalého a při našeptávání nikdy nečekej na databázi.
- Metadata čti jen pro čtení. Neukládej hesla a nepřidávej telemetrii.
- API SSMS je nepodporované a nezdokumentované. Předpoklady ověřuj za běhu v SSMS a výsledky zapisuj do `docs/architecture.md`.
- Uživatel chce vidět jen náš seznam. Náš zdroj se proto účastní všude mimo řetězce a komentáře a tím vestavěný seznam potlačí. Kde nic nevíme, nabídni prázdný seznam (`ContextKind.Name`) nebo klíčová slova (`ContextKind.Fallback`), nikdy `DoesNotParticipateInCompletion`.
- Nové příkazy patří do menu SQL Prompter v hlavní liště (`SqlPrompterCommands.vsct`).
- Nový kontext v `ContextAnalyzer` pokryj testem s kurzorem vyznačeným `|`.
- Sestavení: `dotnet test tests/SqlPrompter.Tests`, VSIX `dotnet build src/SqlPrompter.Ssms/SqlPrompter.Ssms.csproj -c Release` (jen Windows). Po změně `.vsct` zvyš verzi v `ProvideMenuResource` a s každým VSIX pro uživatele verzi v manifestu, jinak se přes starou nenainstaluje.

## Prostředí

VSIX se sestavuje na Windows a SSMS v cloudovém sezení neběží. V cloudu pracuj na Core, Metadata a testech. Co se týká SSMS, neoznačuj za ověřené, dokud to neproběhlo v SSMS 22.
