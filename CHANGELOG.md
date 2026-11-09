# Changelog

All notable changes to this project are documented here, one section per development step.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.6.0] — 2026-11-09 — Step 06: Club and roster management

### Added
- Club profile fields: city (defaults to the home arena's city), reputation, balance, wage budget and board
  expectation; edited in the *Clubs* tab and importable (optional columns with defaults).
- Squad roles (`Ebm.Domain.Rosters`): starter (with lineup position), rotation (with order), bench, inactive and
  youth, stored one per player in the new `RosterSpots` table; club players without a stored role are on the bench.
- Roster validation (`RosterValidator`): starting five complete with distinct positions, injured starters, squad
  size, matchday squad size, foreign/domestic limits, minimum age (errors) and doubtful starters, injured reserves,
  unfamiliar lineup positions, short rotation, missing contracts (warnings).
- Data-driven squad rules (`SquadRules`): club defaults plus per-competition registration rules in the rules JSON
  (`registration.squad`), checked for every competition the club plays this season.
- `IClubService`: club profile (country, arena, current season and competitions, wage bill vs budget), squad,
  save of all roles (rotation order from list order), and player assignment to a club (closes/opens club spells,
  moves the squad role).
- Screens: *Club profile* and *Squad* (role/lineup/rotation editing with live validation, starting-five panel,
  add a player to the squad). Documented in `docs/ROSTER_RULES.md`.
- Sample dataset: club profiles for every club and 9 more players, so Aurora Milano has a complete 12-player squad.
- Tests: roster rules and validator, club profile rules, competition squad rules, club service (complete roster,
  rotation order, invalid configurations, injured starter, registration limits, assignment), club import, Step 05
  database upgrade, database constraints, squad screen save and render.

### Changed
- Migration `ClubsAndRosters` (add-column only on `Clubs`, plus the new table); existing clubs keep their data.
- `ClubDraft` gains the profile fields (optional parameters); `DatabaseEditorViewModel` takes `IClubService`.

### Fixed
- `BoardExpectation.WinTitle` (enum value 0) would have been replaced by the column default on insert; the EF
  sentinel is now `-1`.

## [0.5.0] — 2026-11-09 — Step 05: Player model and attributes

### Added
- Complete player model: wingspan, secondary positions, experience, reputation, potential, fitness, morale and
  injury status (with expected return date), all validated by the domain. Current club and contract come from the
  club history and contracts. Documented in `docs/PLAYER_MODEL.md`.
- 27 attributes in 7 categories (shooting, finishing, playmaking, defense, rebounding, physical, mental), 1–100,
  with a catalogue (`PlayerAttribute`, `PlayerCatalog`) for groups, display names and import field names.
- Player tendencies (8 values, `PlayerTendencies`), suggested from the attributes when not given.
- Rating system (`Ebm.Domain.Players.Ratings`): category ratings, position ratings (weighted attributes plus a
  reach adjustment from height and wingspan), familiarity, overall (primary position), potential rating, best
  position and position suitability (Ideal/Good/Adequate/Poor). Tunable through `RatingModel`.
- `IPlayerProfileService`: player profile (age, club, contract, evaluation) and multi-player comparison.
- Screens: *Player profile* and *Compare players* tabs; the player editor edits every field, attributes by category
  and tendencies (*Suggest from attributes*).
- Import of the new player fields (optional ones default; missing tendencies are suggested).
- Tests: model rules, catalogue, tendencies, rating formulas, persistence of every field, Step 04 database upgrade
  (attribute mapping), profile and comparison, import of optional fields, player page, off-screen render of the
  three player screens (set `EBM_SCREENSHOT_DIR` to save PNGs).

### Changed
- `PlayerAttributes`: the 8 initial ratings are replaced by the 27 attributes. Migration `PlayerModel` converts
  existing players (mapping in `docs/PLAYER_MODEL.md` §7) and adds the new columns and the `PlayerTendencies` table
  in one transaction.
- Player import columns: the 27 attribute columns replace `shooting`, `insideScoring`, …; sample dataset and
  templates converted. `PlayerDraft` carries every player field.

## [0.4.0] — 2026-11-09 — Step 04: Countries, leagues and competitions

### Added
- Competition rules schema (`Ebm.Domain.Competitions.Rules.CompetitionRules`): points system, registration limits,
  regular-season legs and round spacing, ordered tie-breakers, optional playoffs (2–32 clubs, best-of per round,
  seeding from any table position), promotion (direct places and/or playoff winner) and relegation. Validated on
  every save and import; unknown properties are refused. Documented in `docs/COMPETITION_FORMATS.md`.
- Competition engine (`Ebm.Domain.Competitions.Engine`): `RoundRobinScheduler` (circle method, 1–4 legs, any
  number of clubs, balanced home/away), `StandingsCalculator` (points, configurable tie-breakers with head-to-head
  mini-leagues), `PlayoffBracket` (fixed bracket, home-court patterns, game-by-game scheduling),
  `SeasonOutcomeCalculator` (champion, promoted, relegated) and `CompetitionRegistration`.
- `ICompetitionService` (`Ebm.Services.Competitions`): register clubs, generate the schedule, record results
  (updates `LeagueStandings`), advance into and through the playoffs, standings, season outcome.
- Import of `seasons` and `participants` (competition entries, checked against the registration rules); CSV
  templates `seasons.csv`, `participants.csv`.
- Sample dataset: two divisions with full rules (Serie A: 8 clubs, top-4 playoffs, 2 relegated; Serie A2: 6 clubs,
  1 promoted plus the playoff winner), 6 more clubs, season 2026-27 and its 14 entries.
- `Season.UpdateDetails`; `Competition.ParseRules`.
- Tests: rules schema, scheduler properties (2–20 clubs, 1–4 legs), standings and tie-breakers, playoff bracket,
  outcome, registration; full simulated seasons of both sample divisions; registration and import rules;
  Step 02 database upgrade.

### Changed
- `Fixtures` gain `Stage` (`RegularSeason`/`Playoffs`) and `GameNumber` (migration `FixtureStages`; existing
  fixtures become regular-season games).
- `Competition.UpdateRules` validates against the rules schema instead of accepting any JSON object.

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
