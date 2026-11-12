# STEP 03 — Data import and database editor

## 1. Status
- Status: COMPLETED
- Date: 2026-11-09
- Build version: 0.3.0
- Commit/reference: none yet (repository not initialised)
- Previous step: STEP 02 — Core database and entity model
- Next step: STEP 04 — Countries, leagues and competitions

## 2. Objectives
- CSV import, JSON import, data validation, duplicate detection, unique ID generation, import error reporting.
- Basic database editor: add/edit players and clubs, manage player attributes and contracts, edit league and
  competition information, import updated datasets.
- Imports must not silently overwrite existing information; existing records are updated only through an explicit operation.
- Deliverables: import service, import validation system, initial database editor, example import templates, import logs.

## 3. Implementation summary
- **Services (ports and use cases).** `Ebm.Services.Import`: `IDataImportService`, `ImportOptions` (`ImportMode.AddNew` /
  `AddOrUpdate`, `DryRun`, `AllOrNothing`), `ImportReport`, `ImportRecordResult`, `ImportRecordKind`, `ImportOutcome`.
  `Ebm.Services.Editing`: `IDatabaseEditor` (load snapshot; save player, club, player contract, league, competition) with
  draft records, implemented by `DatabaseEditor` (one DI scope per call). `Ebm.Services.Data`: `DuplicateKeys` (natural keys)
  and `DuplicateRecordException` (a `DomainException`).
- **DataImport.** `Sources/CsvParser` (RFC 4180), `Sources/ImportFields` (record types, section/file names, required and
  optional fields), `Sources/SourceReader` (CSV/JSON → raw records + file problems), `Validation/FieldReader` (typed field
  parsing that collects every field error), `Validation/EntityStore` (existing + accepted records indexed by ID and key),
  `Validation/ImportSession` (per-type mapping, reference checks, candidate-first validation, duplicate/identity rules,
  add or update), `DataImportService` (orchestration, single save, options) and `ImportLogWriter` (one log per import).
- **Domain.** Edit methods with the constructors' validation; constructors now delegate to them. Identity fields (IDs,
  country code, division league, contract club/person) have no setters.
- **Desktop.** The main window hosts the database editor: tabs Players, Clubs, Contracts, Leagues and competitions,
  Import. Generic `EditorPageViewModel<T>` (list + form, New/Save, page-level error message), one view model per page,
  `EditorLookups` (selection lists), `ImportViewModel`, `DatabaseEditorViewModel`, `IFilePicker` (testable dialog).
  Views are data templates in `Themes/Editor.xaml`.
- **Data.** `data/templates` (one CSV per type with an example row; JSON template) and `data/sample/italy-sample.json`
  (fictional league: 5 countries, 1 league, 2 divisions, 3 competitions, 8 arenas, 8 clubs, 24 players, 24 contracts),
  copied to the output `data\` folder.

## 4. Files changed
### Created
- `docs/IMPORT_FORMATS.md` — formats, field mappings, validation rules, modes, editor, data limitations (handoff document)
- `logs/STEP_03_LOG.md` — this log
- `data/templates/{countries,arenas,clubs,leagues,divisions,competitions,players,contracts}.csv`, `data/templates/dataset-template.json`
- `data/sample/italy-sample.json`
- `src/Ebm.Services/Import/ImportContracts.cs` — import port and report types
- `src/Ebm.Services/Editing/IDatabaseEditor.cs`, `DatabaseEditor.cs` — editor use cases
- `src/Ebm.Services/Data/DuplicateKeys.cs`, `DuplicateRecordException.cs`
- `src/Ebm.DataImport/DataImportService.cs`, `ImportLogWriter.cs` (+ `DataImportOptions`)
- `src/Ebm.DataImport/Sources/CsvParser.cs`, `ImportFields.cs`, `SourceReader.cs`
- `src/Ebm.DataImport/Validation/FieldReader.cs`, `EntityStore.cs`, `ImportSession.cs`
- `src/Ebm.Desktop/Startup/FilePicker.cs` — `IFilePicker` + dialog implementation
- `src/Ebm.Desktop/ViewModels/Editor/EditorPageViewModel.cs`, `EditorLookups.cs`, `PlayersPageViewModel.cs`,
  `ClubsPageViewModel.cs`, `ContractsPageViewModel.cs`, `CompetitionPagesViewModels.cs`, `ImportViewModel.cs`,
  `DatabaseEditorViewModel.cs`
- `src/Ebm.Desktop/Themes/Editor.xaml` — editor styles and page templates
- `tests/Ebm.Tests.Unit/DataImport/CsvParserTests.cs`, `tests/Ebm.Tests.Unit/Domain/EditRulesTests.cs`
- `tests/Ebm.Tests.Integration/Import/ImportTests.cs`, `tests/Ebm.Tests.Integration/Editing/DatabaseEditorTests.cs`

### Modified
- `src/Ebm.Domain/Players/Player.cs`, `Clubs/Club.cs`, `Clubs/Arena.cs`, `Countries/Country.cs`, `Competitions/League.cs`,
  `Competitions/Division.cs`, `Competitions/Competition.cs`, `Contracts/Contract.cs` — edit methods
- `src/Ebm.Services/ServiceCollectionExtensions.cs` — registers `IDatabaseEditor`
- `src/Ebm.DataImport/ServiceCollectionExtensions.cs` — `AddDataImport(importLogDirectory)` registers the import service
- `src/Ebm.DataImport/Ebm.DataImport.csproj` — Logging.Abstractions, `InternalsVisibleTo Ebm.Tests.Unit`
- `src/Ebm.Hosting/AppPaths.cs` (`ImportLogsDirectory`), `EbmHost.cs` (passes it)
- `src/Ebm.Desktop/MainWindow.xaml(.cs)` — editor tabs, status bar, data load on window load
- `src/Ebm.Desktop/App.xaml` — merges `Themes/Editor.xaml`
- `src/Ebm.Desktop/ViewModels/ShellViewModel.cs` — exposes the editor; welcome text removed
- `src/Ebm.Desktop/Startup/DesktopServiceCollectionExtensions.cs` — file picker, editor view model
- `src/Ebm.Desktop/Ebm.Desktop.csproj` — copies `data/**` to the output
- `tests/Ebm.Tests.Integration/TestSupport/TestDatabase.cs` — registers game services and data import; exposes services and temp folder
- `tests/Ebm.Tests.Integration/Ebm.Tests.Integration.csproj` — `RepositoryDataPath` metadata
- `tests/Ebm.Tests.Unit/Hosting/AppPathsTests.cs` — import log folder assertion
- `Directory.Build.props` (0.3.0), `Directory.Packages.props` (Logging.Abstractions)
- `README.md`, `docs/TECHNICAL_ARCHITECTURE.md`, `docs/DATABASE_SCHEMA.md`, `CHANGELOG.md`, `PROJECT_STATE.md`

### Removed
- none

## 5. Architecture and technical decisions
- **Ports in Services, implementation in DataImport.** The UI imports through `IDataImportService` and edits through
  `IDatabaseEditor`; Desktop still references neither DataImport nor Persistence (architecture tests unchanged and passing).
- **One text-based record model for both formats.** CSV rows and JSON objects become `RawRecord`s with string fields, so
  field mapping, validation and messages are written once. JSON numbers/objects are taken as their raw text.
- **Candidate-first validation.** Every record is first built as a new entity through the domain constructor. Only a
  fully valid candidate updates an existing tracked entity, so a bad record can never half-modify stored data.
- **Commit once, after validation.** All records go through one DI scope; the save is one transaction. Invalid records
  are reported and skipped by default (dependent records fail their reference check), with *dry run* and
  *all or nothing* options. Partial success keeps re-imports simple: the default mode refuses anything already present.
- **Explicit update mode.** `AddNew` refuses existing IDs; `AddOrUpdate` replaces all fields. Natural-key duplicates are
  refused in both modes, with the existing record's ID in the message so the user can switch to an update.
- **References by ID, countries by code.** Stable GUIDs keep players and clubs permanent across imports (Step 02
  decision). Countries use ISO codes because they are natural, stable and human-writable.
- **Duplicate keys in Services.** `DuplicateKeys` is the single definition for the importer and the editor. No database
  unique constraints were added: names are not unique in the real world (same-name players exist; the key adds date of birth).
- **Own CSV parser** (~100 lines, unit-tested) instead of a dependency: the format is RFC 4180 only.
- **Editor view models.** A generic list+form base keeps each page small; rule violations (`DomainException`,
  `DataConstraintException`) appear under the form, anything else still reaches the global handler. Numeric fields
  are edited as text and parsed on save, so invalid input is reported instead of silently ignored by WPF bindings.
- **No schema change.** The step needed none; the model check test confirms no pending model changes.

## 6. Database changes
- New entities: none
- Modified entities: none in the schema (domain edit methods only)
- Removed entities: none
- Migrations: none (schema stays `20261109114155_InitialSchema`)
- Compatibility considerations: none; databases from Step 02 open unchanged.

## 7. Tests and validation
| Test | Result | Notes |
|---|---|---|
| Build | PASS | Debug and Release, 0 warnings, 0 errors |
| Unit tests | PASS | 80/80 (19 new): CSV parsing (quotes, escapes, line breaks, malformed input), CSV file naming, edit methods and their validation, country codes, duplicate keys |
| Integration tests | PASS | 51/51 (16 new): sample JSON import (75 records), CSV templates import, re-import rejected without update, explicit update, invalid records with field-level messages and line numbers, duplicates in DB and within the import, ID generation and repeated IDs, dependent records of rejected ones, dry run and all-or-nothing, immutable contract club, malformed/unknown/missing files, editor add/edit/duplicate/invalid/not-found, contract and competition rules, editor view model (load, page error message, save, import page) |
| Manual tests | PASS (partial) | `Ebm.Desktop.exe --smoke-test` with a fresh data root exits 0 (main window with editor rendered, data loaded on window load); `data\sample` and `data\templates` present in the output; no errors in the log. A screenshot of the running window could not be captured from this session (window not in the foreground), so the layout was not visually reviewed |
| Regression tests | PASS | All Step 01/02 tests pass, including the real executable launch and the no-pending-migration check |

Command: `dotnet test EuropeanBasketballManager.slnx`

## 8. Known limitations
- Importable types: countries, arenas, clubs, leagues, divisions, competitions, players, player contracts. Not yet:
  seasons, participants, staff and staff contracts, club history, fixtures, matches, statistics, standings.
- The editor does not edit countries, arenas or divisions (import them); it never deletes records.
- Updates replace all fields; no partial-column updates.
- Duplicate checks load all records of a type into memory (fine for one country; revisit for Step 16's European database).
- Field errors hide domain-rule errors of the same record until they are fixed (two-stage reporting).
- The editor layout has not been reviewed visually (see §7); a quick manual pass is advised when next running the app.

## 9. Outstanding issues
- No version control yet (unchanged).

## 10. Handoff instructions
Step 04 — Countries, leagues and competitions:
1. Read `PROJECT_STATE.md`, this log, `docs/IMPORT_FORMATS.md` and `docs/DATABASE_SCHEMA.md`.
2. Define the competition rules schema (Step 02/03 accept any JSON object). Validate it in `Competition.UpdateRules`
   (domain) so the editor and the importer pick it up automatically; extend the sample `rules` objects.
3. Seasons and competition participants (club × competition × season) will need import support: add a schema entry in
   `Sources/ImportFields.cs`, a case in `ImportSession.Process`, a key in `DuplicateKeys`, and document it in
   `docs/IMPORT_FORMATS.md`. Keep the processing order in `ImportRecordKind` (enum order = dependency order).
4. If the editor needs countries/divisions/seasons pages, follow the `EditorPageViewModel<T>` pattern and add a data
   template to `Themes/Editor.xaml`; add save methods to `IDatabaseEditor`.
5. Use `data/sample/italy-sample.json` as the base dataset for the configurable initial country.
6. Bump `<Version>` to `0.4.0`; update `CHANGELOG.md`, `PROJECT_STATE.md` and write `logs/STEP_04_LOG.md`.

## 11. Project state
- Working features: application shell, configuration, logging, error handling, game database with migrations, CSV/JSON
  import with validation, duplicate detection and import logs, database editor (players with attributes, clubs, player
  contracts, leagues, competitions, import page)
- Current application status: builds, launches, all tests pass
- Database version: `20261109114155_InitialSchema`
- Build command: `dotnet build EuropeanBasketballManager.slnx`
- Test command: `dotnet test EuropeanBasketballManager.slnx` (headless: add `--filter "Category!=Launch"`)
- Run command: `dotnet run --project src/Ebm.Desktop`, then Import tab → `data\sample\italy-sample.json`

## 12. Completion checklist
- [x] Objectives implemented
- [x] Build successful
- [x] Tests passed
- [x] No critical regression
- [x] Documentation updated
- [x] PROJECT_STATE.md updated
- [x] Next step can start independently
