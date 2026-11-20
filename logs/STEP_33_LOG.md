# STEP 33 — Modding and custom database support

## 1. Status
- Completed 2026-11-21. Version 0.33.0.
- Build: PASS (Debug, 0 warnings, 0 errors). Tests: see §5.

## 2. Objectives
Custom database import, custom competition definitions, custom club and player creation, custom league rules,
database export, database validation, optional community database packages; custom data must not corrupt the active
save.

| Acceptance criterion | Where |
|---|---|
| Custom databases can be loaded | Packages installed as a new game or into the current game (`ModPackageService`); an exported database imports into a new game unchanged (`An_exported_database_imports_into_a_new_game_unchanged`) |
| Custom competitions can be configured | Competitions with a `rules` object via import, editor or package; a package adding a cup to a running career (`Packages_are_validated_and_never_leave_the_current_game_broken`) |
| Data validation catches invalid references | Record validation rejects a package contract with an unknown player before anything is written; package manifest checks (`Broken_packages_are_refused_with_a_reason`); whole-database validator |
| Existing careers remain unaffected by database changes | Restore point + all-or-nothing import + undo on new validation errors; career manager, club, season and date unchanged after installs; a saved career loads unchanged after a new-game install (`A_package_made_from_the_game_starts_a_new_game_and_saved_careers_are_unaffected`) |

Already available from earlier steps and reused: CSV/JSON import (Step 04), editor for clubs, players, contracts,
leagues and competitions (Steps 05–12), competition and cup rules (Steps 07, 18), save files (Step 14).

## 3. Implementation summary
- Services contracts `Ebm.Services.Modding`: `IDatabaseExportService`, `IDatabaseValidator`, `IModPackageService`
  and their records (`PackageManifest`, `PackageCheck`, `PackageInstallResult`, `ValidationReport`).
- `Ebm.DataImport.Modding`: `DatabaseExportService` (import field names, sorted, written to a temp file then moved),
  `DatabaseValidator` (fixed rule set), `ModPackageService` (zip read with file-name checks, extraction to a temp
  folder, dry-run check, install with restore point via `ISaveGameService`, package creation).
- Hosting: `AppPaths.ModsDirectory` (`<DataRoot>\mods`); `AddDataImport(logs, mods)`.
- UI: *Mods and export* tab (`ModsViewModel`, `Themes/Mods.xaml`); `IFilePicker` gains export and package dialogs
  (default interface methods, so test fakes are unchanged).
- Also in this step (separate commits): the UI refresh (Courtside Light theme, grouped sidebar with icons, type
  scale, placeholders, rating badges, button roles) and global value formatting (`DisplayFormatting`).
- Decisions: the package format is the import format plus a manifest (one parser, one rule set); installs are protected
  by a normal save as restore point rather than a second database; only errors introduced by an install undo it.
- No database migration.

## 4. Files changed
- New: `src/Ebm.Services/Modding/IModding.cs`, `src/Ebm.DataImport/Modding/DatabaseExportService.cs`,
  `DatabaseValidator.cs`, `ModPackageService.cs`, `src/Ebm.Desktop/ViewModels/Modding/ModsViewModel.cs`,
  `src/Ebm.Desktop/Themes/Mods.xaml`, `tests/Ebm.Tests.Integration/Modding/ModdingTests.cs`, `docs/MODDING.md`, this
  log.
- Modified: `ImportLogWriter.cs` (`DataImportOptions.ModsDirectory`), `Ebm.DataImport/ServiceCollectionExtensions.cs`,
  `AppPaths.cs`, `EbmHost.cs`, `FilePicker.cs`, `DatabaseEditorViewModel.cs`, `App.xaml`, `MainWindow.xaml`,
  `Directory.Build.props` (0.33.0), `docs/IMPORT_FORMATS.md`, `CHANGELOG.md`, `PROJECT_STATE.md`.

## 5. Tests
- Unit: architecture tests 7 PASS.
- Integration (affected classes, by user decision): 59 PASS: `ModdingTests` (8 new: export round trip, protected
  installs, new-game install and saved careers, 4 broken packages, Mods screen view model and render), import, saves,
  host composition, database editor, render tests, database initializer.
- `NewsServiceTests` failed once while running alongside other classes (injury news set differed) and passed when
  run alone. It looks like an existing sensitivity to test order or parallelism. Step 33 does not touch news or injuries.
- Full fast suite and slow suite not run.

## 6. Known limitations
- Staff, tactics, fixtures, history and finances are not in the package format (generated or derived by the game).
- Installing into the current game updates records by ID, so a package can change the club a career manages.
- Database validation is a fixed rule set; it does not simulate a season.
- Packages are not signed; the game reads only JSON/CSV data from them.

## 7. Handoff instructions
- Step 34 (optimization and release): include `docs/MODDING.md` in the release notes; consider shipping
  `data/real-leagues` as an `.ebmpack` (create it from a new game with *Create a package*).
- Run the full fast suite and the slow suite before the release (Steps 29–33 ran affected classes only), and look into
  the `NewsServiceTests` sensitivity.

## 8. Outstanding issues
- Full integration fast suite and slow suite not run on Steps 29–33 code (user instruction).
