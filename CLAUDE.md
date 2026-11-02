# SQL Prompter

Rozšíření s vlastním IntelliSense pro SSMS 22. Komunikuj česky.

Před návrhem nebo změnou architektury si přečti `docs/architecture.md`.

## Aktuální stav

Projekt je ve fázi prototypu. Diagnostické VSIX v `prototype/` je napsané, ale ještě neběželo v SSMS 22. Další krok je spustit ho podle `prototype/README.md` a zapsat výsledky do `docs/architecture.md`. Dokud prototyp neodpoví na své tři otázky, nestav MVP.

## Pravidla

- Logika patří do `SqlPrompter.Core` a `SqlPrompter.Metadata` (netstandard2.0). Tyto projekty nesmí záviset na VS SDK ani na API SSMS.
- `SqlPrompter.Ssms` zůstává tenká vrstva. Typy SSMS nepouštěj za její hranici.
- Na vlákně UI nedělej nic pomalého a při našeptávání nikdy nečekej na databázi.
- Metadata čti jen pro čtení. Neukládej hesla a nepřidávej telemetrii.
- API SSMS je nepodporované a nezdokumentované. Předpoklady ověřuj za běhu v SSMS a výsledky zapisuj do `docs/architecture.md`.

## Prostředí

VSIX se sestavuje na Windows a SSMS v cloudovém sezení neběží. V cloudu pracuj na Core, Metadata a testech. Co se týká SSMS, neoznačuj za ověřené, dokud to neproběhlo v SSMS 22.
