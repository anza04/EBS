# Changelog

All notable changes to this project are documented here, one section per development step.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.12.0] — 2026-11-12 — Step 12: Season progression and career management

### Added
- Career (`ICareerService`, `Careers` table): choose a club, start a career, game days (`AdvanceDayAsync`), continue to
  the next match (`ContinueAsync`), instant play of the user's match, playoffs advancing automatically, AI transfer
  window at season end, season review, next season with promotion/relegation and schedules, club history.
- `GameClock`: the career date is the game's "today" for every service.
- Forfeits (`Match.IsForfeit`, `IMatchService.PlayOrForfeitAsync`): clubs without five players lose 20–0.
- `ICompetitionService.GetPhaseAsync`, `SeasonTransition`, `SeasonCalendar`.
- *Career* screen: new career, calendar controls, standings, news, club history.
- Sample `data/sample/italy-squads.json` (squads for every club, more free agents). Docs `docs/SEASON_LIFECYCLE.md`.
- Tests: full career season with next season and offseason, forfeits, career screen, season lifecycle unit tests.

### Changed
- Services take the date from `GameClock` instead of `TimeProvider`. Migration `20261112144630_Careers`.

## [0.11.0] — 2026-11-12 — Step 11: Match presentation and statistics

### Added
- Screen *Matches* (match centre): competition season and schedule (with *Create schedule*), match preview (starting
  lineups, bench, tactics), live match (scoreboard with period clock, team fouls and timeouts; play-by-play; live box
  scores with energy and plus-minus; team statistics; report with leaders and quarter scores), game controls (next
  possession, to the end of the quarter / match), substitution, timeout and tactical-adjustment controls for the
  managed side, instant play, automatic saving at the end and stored match reports for played fixtures.
- `IMatchService.GetCompetitionSeasonsAsync`. Documentation `docs/MATCH_UI.md`.
- Tests: the render test plays a live match through the screen (preview, possessions, substitution, timeout, tactics,
  quarter and match end, saved result, reopened box score, preview error for short squads).

 — 2026-11-12 — Step 10: Basketball match simulation engine

### Added
- `MatchSimulation` (`Ebm.Domain.Matches.Simulation`): possession-based engine under FIBA rules — four 10-minute
  quarters, overtimes, 24/14-second shot clock, two- and three-point shots by zone (rim, post, mid-range, three), free
  throws, shooting and non-shooting fouls with the team-foul bonus, foul-outs, rebounds, assists, steals, blocks,
  turnovers, shot-clock violations, jump ball and alternating possession, substitutions, timeouts, fatigue, form of
  the day, home advantage, game lost by default. Deterministic for a seed.
- Tactics in the engine (`TacticalModel`): systems, settings, roles, shot priority and minutes targets change pace,
  shot mix, make probabilities, turnovers, fouls, rebounds, fast breaks and fatigue.
- Simulation modes: instant, commentary (play-by-play `MatchEvent`s) and live (substitutions, timeouts, tactic changes,
  user-managed side).
- `TeamSheet.Pick` (matchday squad from a roster), `PlayerLine`, `TeamStatistics`, `MatchResult`.
- `IMatchService`: fixtures with results, preview, start/save sessions, instant play, stored box scores and reports.
- `docs/MATCH_ENGINE.md` with the formulas and the statistical validation results.
- Tests: rule invariants, overtime, determinism, fatigue, live interventions, short-handed teams, squad picking,
  large-scale statistical validation and tactical effects (unit); match service (integration).

### Changed
- `CompetitionService.RecordResultAsync` shares `AddResultAsync` with the match service (result + standings + box
  score in one transaction). No schema change.

 — 2026-11-12 — Step 09: Tactical system

### Added
- `Tactics` table and `Tactic` entity (`Ebm.Domain.Tactics`): seven offensive systems, six defensive systems,
  offensive settings (tempo, shot selection, ball movement, spacing, pick-and-roll frequency on a 1–5 scale; focus)
  and defensive settings (intensity, ball pressure, help defense; switching, pick-and-roll coverage, rebounding).
- Player tactical roles (ten roles) and assignments (role, shot priority, minutes target); `TacticalRoles` suggests a
  role from the player's attributes and position.
- `TeamTactics`: the immutable configuration the match engine reads, with suggested roles for unassigned players.
- `ITacticsService`: club tactics, create/update, select for the next match, delete, `GetMatchTacticsAsync`.
- Screen *Tactics*. Documentation `docs/TACTICS.md` (options, settings, roles and how the engine should read them).
- Tests: tactic rules and role suggestions (unit); tactics service, schema, render of the tactics screen (integration).

### Changed
- Migration `Tactics` (create-table only). Schema version `20261112140336_Tactics`; 21 tables.

 — 2026-11-09 — Step 08: Transfers and free agents

### Added
- `Transfers` table and `Transfer` entity (`Ebm.Domain.Transfers`): the movement history of every player — free-agent
  signings, transfers, release-clause buy-outs, loans (with end date and return date) and loan returns. Never deleted.
- Contract statuses `OnLoan` (the contract keeps running while a loan club pays the player) and `Transferred` (ended
  early by a transfer); `Contract.Transfer`, `SendOnLoan`, `ReturnFromLoan`. Wage bills and salary commitments skip
  contracts of players out on loan.
- `TransferNegotiator`: deterministic market value (market salary × 2.5 × age, potential and remaining-contract
  factors), asking fee (starter and rotation premiums, discounts for clubs in debt or over budget), fee answers
  (accepted / counter-offer / rejected; a release clause cannot be refused) and loan answers (no starters or rotation
  players; 10% of the value per year).
- `ClubRecruiter`: the simple AI rules — surplus to sell (squad size, foreign limit, wage budget), needs (two players
  per position, squad up to the matchday squad + 1), and signings within the cash, the wage budget and the
  registration limits, preferring free agents. `SquadRules.Strictest` combines a club's competition rules.
- `ITransferService`: player search with filters priced for the searching club, valuations, fee / loan / contract
  negotiation, `BuyAsync` (fee between balances, old contract transferred, new contract, squad move, history),
  `LoanAsync` (parent contract on loan, loan contract at the borrower, fee), `RecallAsync`, club and player transfer
  history, `ProcessTransferDatesAsync` (loan returns, then contract dates; run at startup) and
  `RunAiTransferWindowAsync` (AI clubs sell to each other, sign free agents, buy, and release surplus nobody buys;
  managed clubs are left alone).
- Financial and roster limits: a fee never takes a balance below zero; first-team size, foreign-player and minimum-age
  limits of the club and its competitions block a move; the wage budget warns the user and binds AI clubs.
- Screen *Transfer market*: search and filters, player card with value and asking fee, fee and contract negotiation
  with counter-offers, buy, pay the release clause, sign a free agent, loan form, loans out (with recall) and in,
  the club's transfer history and the AI transfer window report. Documented in `docs/TRANSFERS.md`.
- Free-agent signings through `IContractService.SignAsync` are recorded in the transfer history.
- Tests: transfer and loan rules, negotiator, recruiter, strictest squad rules (unit); transfer service (purchase,
  refusals and limits, release clause, loan and automatic return, starter refusal and recall, search and pricing,
  free-agent history, AI window with free agents, AI sales and releases, startup processing), schema, render of the
  market screen with a purchase and an AI window (integration).

### Changed
- Migration `Transfers` (create-table only). Schema version `20261109204206_Transfers`; 20 tables.
- Startup applies `ITransferService.ProcessTransferDatesAsync` (loan returns first, then contract dates) instead of
  calling the contract service directly.
- `DatabaseEditorViewModel` takes `ITransferService`; contract status texts use display names ("On loan").
- Club profile wage bill and `SalaryCommitments` ignore `OnLoan` contracts.

## [0.7.0] — 2026-11-09 — Step 07: Contracts and player availability

### Added
- Contract terms: payment frequency (monthly, quarterly, annually), signing bonus, performance bonuses (per game,
  playoffs, league title, cup title, promotion, avoid relegation), release clause, club option and player option.
  Grouped as `ContractTerms`; importable and editable in *Contracts (editor)*.
- Contract lifecycle: status `Active`, `Expired`, `Terminated`, `Renewed`, with the early end date and the
  termination settlement. `Contract.RunningOn(date)` is the single "running" rule used by every service.
- `IContractService`: sign a free agent (overlap check, player acceptance, signing bonus paid, player joins the club),
  renew (the old contract is kept as `Renewed`), terminate by release (remaining salary paid) or mutual agreement
  (agreed settlement), club and player option decisions, free-agent list, club and player contract views, and
  `ProcessContractDatesAsync` (expiry frees the player; contracts signed in advance move him on their start date).
  Run at application start.
- Salary calculations (`SalaryCalculator`): day-by-day accrual per contract year, remaining and total salary,
  payment schedule by frequency, next payment. Club `SalaryCommitments`: wage bill, salary still owed, commitments
  per 12 months, maximum season bonuses.
- Simplified, deterministic negotiation (`ContractNegotiator`): asking salary from overall, reputation, age,
  potential and club prestige; offers accepted, countered or rejected; player option decisions.
- Screens: *Club contracts* (current contracts and history, wages and commitments, renew, terminate, options, sign
  a free agent with *Ask the player* and counter-offers); the player profile shows contract status and history.
- `Club.ChangeBalance` for one-off payments. Documented in `docs/CONTRACTS.md`.
- Sample dataset: 4 free agents; some Aurora Milano contracts have bonuses, a release clause or options.
- Tests: contract rules, bonuses, salary accrual and schedules, commitments, negotiation, contract service (signing,
  refusal, pre-contract, expiry, release, mutual agreement, renewal, options), editor and import of the new terms,
  Step 06 database upgrade, club contracts screen render.

### Changed
- Migration `Contracts` (add-column only, with defaults); existing contracts become active, paid monthly, no extras.
- Terminated and expired contracts no longer make a player a club member, in the squad, club profile wage bill and
  player profile (`Contract.RunningOn`).
- Club spell and roster-spot moves are shared by squad management and contracts (`ClubMembership`).
- `ContractDraft` gains the new terms (optional parameters); `DatabaseEditorViewModel` and
  `PlayerProfileViewModel` take `IContractService`. The editor tab is now *Contracts (editor)*.
- `.gitignore` no longer ignores the root `logs/` folder, so step logs are versioned.

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
