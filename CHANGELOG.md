# Changelog

All notable changes to this project are documented here, one section per development step.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.3.0] — 2026-11-09 — Step 03: Data import and database editor

### Added
- Import service `IDataImportService` (`Ebm.Services.Import`, implemented in `Ebm.DataImport`): CSV files (one record
  type per file, named after it) and JSON dataset files (several sections), processed in dependency order.
  Importable: countries, arenas, clubs, leagues, divisions, competitions, players (with attributes), player contracts.
- Validation with per-record, field-level error messages; file-level problem reporting; references checked against
  the database and the records accepted earlier in the same import.
- Duplicate detection by ID and by natural key (`Ebm.Services.Data.DuplicateKeys`), shared by importer and editor.
- Unique ID generation for records without an ID.
- Import modes: add new only (default; existing records are rejected) and explicit add-or-update; options
  "validate only" (dry run) and "all or nothing". Accepted records are written in one transaction.
- Import logs: one report per import in `<DataRoot>\logs\imports\`, plus a summary in the application log.
- Database editor (`IDatabaseEditor`, `Ebm.Services.Editing`) and editor screens in the main window: players (with
  attribute ratings), clubs, player contracts, leagues, competitions (with rules JSON) and an import page.
- Domain edit methods: `Player.UpdateDetails`, `Club.UpdateDetails`, `Arena.UpdateDetails`, `Country.Rename`,
  `Country.NormaliseCode`, `League.UpdateDetails`, `Division.UpdateDetails`, `Competition.UpdateDetails`,
  `Contract.UpdateTerms`; `DuplicateRecordException`.
- `data/templates` (CSV and JSON templates) and `data/sample/italy-sample.json` (fictional league, 24 players),
  copied next to the executable.
- `docs/IMPORT_FORMATS.md`: formats, field mappings, validation rules, modes, editor, known data limitations.
- Unit tests (CSV parser, file naming, edit rules, duplicate keys) and integration tests (sample and template
  imports, invalid records, duplicates, update mode, dry run, all-or-nothing, malformed files, editor use cases,
  editor view model).

### Changed
- Entity constructors delegate to the edit methods (same validation); some reference-check messages are now
  worded for players (e.g. "Nationality must be set." instead of "nationalityId must be set.").
- `AddDataImport(importLogDirectory)` takes the import log folder; `AppPaths.ImportLogsDirectory` added.
- Main window: the welcome card is replaced by the database editor; record counts and an "Open logs folder"
  button are in the status bar.
- Package `Microsoft.Extensions.Logging.Abstractions` 10.0.14 added to DataImport.
- Version 0.3.0.

## [0.2.0] — 2026-11-09 — Step 02: Core database and entity model

### Added
- Domain entities (`Ebm.Domain`): Country, League, Division, Competition (rules stored as JSON data),
  CompetitionParticipant, Season, Club, Arena, Player with PlayerAttributes, StaffMember, Contract,
  PlayerClubHistoryEntry, Fixture, Match, PlayerMatchStatistics (with BoxScore) and LeagueStanding.
- Strongly typed, application-assigned GUID IDs for every entity (`Entity<TId>`, `IEntityId<T>`), and
  `DomainValidationException` for rule violations.
- Data access ports `IRepository<TEntity, TId>`, `IUnitOfWork` and `DataConstraintException` (`Ebm.Services.Data`).
- SQLite game database with EF Core 10.0.14 (`Ebm.Persistence`): `EbmDbContext`, Fluent mappings with unique
  indexes, check constraints and restrict-on-delete foreign keys, generic repository and unit of work.
- Versioned migrations; first migration `20261109114155_InitialSchema` (17 tables).
- `DatabaseInitializer`: creates and migrates `<DataRoot>\databases\ebm.db` at startup and logs the schema version.
- Local tool manifest `dotnet-tools.json` with `dotnet-ef` 10.0.14.
- Unit tests for entity rules and statistics; integration tests for database creation, migrations, round trips,
  constraints and player history across clubs.
- `docs/DATABASE_SCHEMA.md`: entities, fields, relationships, constraints and migration procedures.

### Changed
- `AppPaths` adds `DatabasesDirectory` and `DatabaseFile`; `AddPersistence` now takes the database path.
- `.editorconfig` treats generated migrations as generated code.
- Version 0.2.0.

## [0.1.0] — 2026-11-09 — Step 01: Project foundation and application architecture

### Added
- .NET 10 solution `EuropeanBasketballManager.slnx` with pinned SDK, shared build settings,
  central package management and enforced code style.
- Layered projects: `Ebm.Domain`, `Ebm.Services`, `Ebm.Persistence`, `Ebm.DataImport`,
  `Ebm.Hosting` (composition root) and `Ebm.Desktop` (WPF application).
- WPF application shell showing product name, version, data folder and log folder.
- Configuration via `appsettings.json`, environment variables and command line (`Ebm` section).
- User data folder resolution (default `%LOCALAPPDATA%\EuropeanBasketballManager`).
- Serilog logging to daily rolling files (`logs/ebm-YYYYMMDD.log`, 14 kept) and debug output.
- Global error handling: UI-thread, background-thread and unobserved-task exceptions;
  startup-failure log; defined exit codes.
- `--smoke-test` launch switch for automated launch verification.
- Unit tests (architecture rules, paths, version info, error policy, startup failure log, service registration)
  and integration tests (host composition, logging, configurable log level, real executable launch).
- Documentation: README, technical architecture, project state, Step 01 log.

### Changed
- Development roadmap moved from `project_state.md` to `docs/ROADMAP.md` (content unchanged).
  On Windows the old name collides with the required `PROJECT_STATE.md` status file.
