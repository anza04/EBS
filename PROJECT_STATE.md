# PROJECT STATE

## Project
European Basketball Manager

## Current milestone
Milestone 1 — Playable core game

## Current step
None in progress

## Last completed step
STEP 12 — Season progression and career management (2026-11-12)

## Next step
STEP 13 — see docs/ROADMAP.md

## Application status
- Build: PASS (Debug and Release, 0 warnings, 0 errors)
- Tests: PASS (387/387: 277 unit, 110 integration)
- Run status: Launches. Creates/migrates the game database at startup, loads the career (its date becomes the game date), applies transfer and contract dates (loan returns, expiries, contracts starting) and opens the main window (career, players, player profile, player comparison, clubs, club profile, squad, club contracts, transfer market, tactics, match centre, contracts editor, leagues, competitions, import). The competition engine is available as `ICompetitionService`; player ratings as `PlayerRatingCalculator` / `IPlayerProfileService`; clubs and squads as `IClubService`; contracts as `IContractService`; the player market and AI recruitment as `ITransferService`; club tactics as `ITacticsService` (`GetMatchTacticsAsync` for the match engine); the match engine as `MatchSimulation` (domain) and `IMatchService` (play, live sessions, storage, box scores, forfeits); the career and calendar as `ICareerService` with the game date in `GameClock`.

## Technology stack
- Language: C# 14 / .NET 10 (SDK 10.0.203, pinned in `global.json`)
- Framework: Microsoft.Extensions.Hosting (DI, configuration, options, logging)
- Database: SQLite via EF Core 10.0.14, versioned migrations, file `<DataRoot>\databases\ebm.db`
- UI: WPF (`net10.0-windows`), MVVM with CommunityToolkit.Mvvm
- Build system: .NET SDK / MSBuild, `.slnx` solution, central package management, local tool `dotnet-ef`
- Logging: Serilog (rolling file and debug sinks), used through `ILogger<T>`
- Tests: xUnit 2.9.3

## Implemented modules
- [x] Solution structure and build configuration
- [x] Domain layer (`Ebm.Domain`): 19 entities with strongly typed IDs and validation
- [x] Services layer (`Ebm.Services`): `TimeProvider`, data access ports (`IRepository<,>`, `IUnitOfWork`)
- [x] Persistence layer (`Ebm.Persistence`): EF Core/SQLite context, mappings, migrations, repositories, startup initialiser
- [x] Data import layer (`Ebm.DataImport`): CSV/JSON import, validation, duplicate detection, import logs
- [x] Composition root (`Ebm.Hosting`): configuration, data paths, logging, error policy
- [x] Desktop shell (`Ebm.Desktop`): main window, global exception handler, smoke-test mode
- [x] Automated tests (unit, integration, architecture, launch)
- [x] Database and entity model (Step 02)
- [x] Data import and database editor (Step 03)
- [x] Competition configuration, fixture generator, standings, playoffs, promotion/relegation (Step 04)
- [x] Player model: 27 attributes, tendencies, ratings, position suitability, profile and comparison screens (Step 05)
- [x] Club profile, squad roles, starting five, rotation, roster validation, competition registration (Step 06)
- [x] Contracts: terms, lifecycle (sign, renew, terminate, options, expiry), free agents, salary calculations and commitments, negotiation (Step 07)
- [x] Transfer market: search, valuations, fee negotiation, transfers, release clauses, loans and returns, transfer history, AI recruitment (Step 08)
- [x] Tactics: offensive/defensive systems and settings, player roles and assignments, selected tactic per club, match-engine access (Step 09)
- [x] Match engine: possession-based FIBA simulation, statistics, fatigue, substitutions, timeouts, overtime, instant/commentary/live modes, statistical validation (Step 10)
- [x] Match centre: schedule, preview, live match with play-by-play, scoreboard, substitutions, timeouts and tactical changes, live and stored box scores, team stats, match report (Step 11)
- [x] Career: club selection, calendar and game days, forfeits, playoffs, season review, promotion/relegation, next season, club history, career screen (Step 12)
- [ ] Step 13 onwards … see docs/ROADMAP.md

## Database
- Current schema version: `20261112144630_Careers`
- Latest migration: `20261112144630_Careers` (new `Careers` table, `Matches.IsForfeit`; 22 tables)
- Data status: empty database created at startup. Sample dataset `data/sample/italy-sample.json` (fictional league: Serie A 8 clubs, Serie A2 6 clubs, season 2026-27 with entries and full competition rules; 37 players with full Step 05 profiles, 4 of them free agents; club profiles; Aurora Milano has a complete 12-player squad and contracts with bonuses, a release clause and options) and templates in `data/templates` can be imported from the Import tab. `data/sample/italy-squads.json` (import after the sample) fills every club to 11 players and adds 12 free agents, so the sample plays as a full career. Test-only mock data in `tests/Ebm.Tests.Integration/TestSupport/SampleWorld.cs`.

## Important architectural decisions
1. Layered architecture with inward-only dependencies (Domain ← Services ← Persistence/DataImport ← Hosting ← Desktop). Enforced by architecture tests.
2. Services defines ports (repository interfaces); Persistence and DataImport implement or consume them. The UI never references Persistence or DataImport.
3. `Ebm.Hosting` is the only composition root and the only project that references Serilog. Only `Ebm.Persistence` references EF Core.
4. Each layer exposes one `Add<Layer>()` DI registration method.
5. Unexpected exceptions close the application to protect data. `DomainException` messages are player-facing and allow the app to continue.
6. All user data lives under a configurable data root (default `%LOCALAPPDATA%\EuropeanBasketballManager`).
7. Real-world time comes from the injected `TimeProvider`.
8. Competition, league and player rules are data-driven. Competition rules are a JSON object in `Competitions.RulesJson`.
9. Version scheme `0.<step>.<patch>` during Milestone 1.
10. Entity IDs are strongly typed GUIDs assigned by the application, never by the database. Player and club IDs are permanent.
11. Every foreign key is restrict-on-delete. History (club spells, contracts, matches, statistics, standings) is never deleted.
12. Statistics store the club the player represented, so history stays correct after transfers.
13. Schema changes are made only through new EF Core migrations, applied by `DatabaseInitializer` at startup. Never `EnsureCreated`, never edit a shipped migration.
14. Repositories and the unit of work are scoped: one DI scope per unit of work.
15. UI-facing use cases (`IDatabaseEditor`, `IDataImportService`) are Services interfaces; each call opens its own DI scope. Desktop never references DataImport or Persistence.
16. Imports never overwrite silently: existing records are rejected unless the explicit update mode is chosen. Accepted records are saved in one transaction. Duplicate rules live in `DuplicateKeys`, shared by importer and editor.
17. Import files reference countries by ISO alpha-3 code and every other record by its GUID; records without an ID get a new one.
18. The competition engine is pure, stateless domain code (`Ebm.Domain.Competitions.Rules` / `.Engine`); `ICompetitionService` orchestrates it over the repositories. Rules are validated by `CompetitionRules.Parse` on every save/import (unknown properties refused).
19. Standings rankings are always calculated from match results; `LeagueStandings` stores the totals, updated by `RecordResultAsync`.
20. Playoff fixtures are created on demand (`AdvanceAsync`), one game per series at a time, so no unneeded games exist. Fixtures carry `Stage` and `GameNumber`.
21. Migrations must stay transactional on SQLite: prefer add-column changes; avoid operations that force a table rebuild (e.g. adding check constraints) on existing tables. To restructure a table, rename it, create the new one, copy with SQL and drop the old one (hand-written `Up`, as in `PlayerModel`).
22. Player ratings are calculated, never stored: pure formulas in `Ebm.Domain.Players.Ratings` over `RatingModel.Default`. Overall = effective rating at the primary position; decisions should use category/position ratings and tendencies too (`PlayerEvaluation`).
23. Attributes and tendencies are fixed catalogues (`PlayerAttribute`, `PlayerTendency` enums); their order is the storage order. Import field names are the camel-case enum names.
24. A player's current club = open club spell, else latest running contract. Squad roles live in `RosterSpots` (one per player, current state, not history); no row = bench. Moves go through `IClubService.AssignPlayerAsync`.
25. Roster rules are a pure validator (`RosterValidator`) over `SquadRules`: club defaults in code, competition registration limits in the rules JSON (`registration.squad`). Invalid rosters can be saved; validation reports errors and warnings instead of blocking, so squads can be rearranged in steps.
26. An enum column with `HasDefaultValue` needs an EF sentinel outside the enum (`HasSentinel((T)(-1))`) unless the default is the enum's value 0; otherwise value 0 is replaced by the column default.
27. A contract runs on a date by `Contract.RunningOn(date)` only (dates plus `TerminationDate`); never re-implement the rule. Early ends keep the agreed `EndDate` and set `TerminationDate` (first day not under contract); contracts are never deleted.
28. Contract lifecycle changes go through `IContractService` (negotiation, payments, club moves in one unit of work). The editor and the importer are data tools: they set terms only, without negotiation, payments, overlap checks or moves.
29. One-off contract payments (signing bonus, settlement) are booked to `Club.Balance` at once (`Club.ChangeBalance`). Salaries and performance bonuses are calculated (`SalaryCalculator`, `SalaryCommitments`) but not booked until the calendar and finances exist (Steps 12, 27).
30. Contract dates take effect through `IContractService.ProcessContractDatesAsync` (idempotent): at application start now, every game day from Step 12.
31. Negotiation is deterministic and parameterised (`ContractNegotiator` properties, registered as a singleton): the same offer always gets the same answer.
32. Club membership moves (spells and roster spots) go through `ClubMembership` (Services), shared by the squad, contract and transfer use cases.
33. Every player movement is a `Transfer` row (free-agent signing, transfer, release clause, loan, loan return), written in the same unit of work as the contract and membership changes; never deleted. `IContractService.SignAsync` writes the free-agent rows.
34. A transfer ends the old contract as `Transferred` (no compensation) and signs the new one the same day; the fee moves between balances at once. A loan keeps the parent contract running as `OnLoan` and adds a loan contract at the loan club on the same salary: wage bills and commitments skip `OnLoan` contracts (the loan club pays). Loans are the only case of two running contracts per player.
35. Market pricing and AI recruitment are pure, deterministic domain code (`TransferNegotiator`, `ClubRecruiter`), parameterised and registered as singletons; `ITransferService` orchestrates them. AI clubs never trade with managed clubs (offers to the user's club come with the career mode).
36. Limits on moves: fees never take a balance below zero; registration limits (first-team size, foreigners, minimum age, from the club rules and every competition it plays in) block; the wage budget warns the user and binds AI clubs. Only problems the newcomer causes are reported.
37. `ITransferService.ProcessTransferDatesAsync` is the single "apply the calendar" entry point: loan returns first, then `ProcessContractDatesAsync`. Called at startup (every game day from Step 12).
38. Tactics (`Tactic`) are club-owned configurations; at most one is selected per club (filtered unique index) and is what the club plays its next match with; no selection = `TeamTactics.Default`. Player assignments are current state in a JSON column without player FK (tactics never block deleting a player); `ITacticsService.GetMatchTacticsAsync` drops assignments of players who left and hands the engine an immutable `TeamTactics`; unassigned players get `TacticalRoles.Suggest`.
39. The match engine (`Ebm.Domain.Matches.Simulation`) is pure and deterministic for a setup and seed; all its numbers live in `MatchSimulation.Play` and `TacticalModel` and are documented in `docs/MATCH_ENGINE.md`. `IMatchService` builds the matchday squads (`TeamSheet.Pick`) from the rosters and the selected tactics, and stores a finished match (result, standings, box scores) in one transaction through `CompetitionService.AddResultAsync`; a match in progress (`MatchSession`) lives in memory until saved. Statistical ranges are guarded by unit tests (`MatchStatisticsValidationTests`); retune them together with the docs.
40. The match centre (`MatchCenterViewModel`) only drives `IMatchService` and the `MatchSimulation` API; a live match is saved automatically when it ends. Managing a side switches that side's automatic coach off (forced substitutions still happen).
41. The game date is `GameClock.Today` (Services): the career date while a career is loaded, else the `TimeProvider` date. Services never read the `TimeProvider` date directly. The career date only moves forward, one game day at a time, through `ICareerService` (docs/SEASON_LIFECYCLE.md).
42. A game database holds at most one career (`Careers` table); the database file is the save. A game day plays every other fixture of the day instantly, advances the competitions, moves the date and applies contract/transfer dates; the user's fixture must be played first (live or instantly). The AI transfer window runs on the last day of each season.
43. A club that cannot field five players forfeits 20–0 (`Match.IsForfeit`, no box score) so the calendar never blocks. The next season keeps every competition's entries except divisions, which exchange clubs by `SeasonTransition` (promotion/relegation from the rules); schedules keep the previous first-round offset.

Details: `docs/TECHNICAL_ARCHITECTURE.md`, `docs/DATABASE_SCHEMA.md`, `docs/IMPORT_FORMATS.md`, `docs/COMPETITION_FORMATS.md`, `docs/PLAYER_MODEL.md`, `docs/ROSTER_RULES.md`, `docs/CONTRACTS.md`, `docs/TRANSFERS.md`, `docs/TACTICS.md`, `docs/MATCH_ENGINE.md`, `docs/MATCH_UI.md`, `docs/SEASON_LIFECYCLE.md`.

## Known issues
- Career: salaries not booked yet (Step 13/27); the contracts and transfer-market screens default empty dates to the real date, not the career date; one career per database; `StartNextSeasonAsync` is repeatable but not atomic.
- The launch integration test opens a real window for about one second and needs an interactive desktop session. Skip it on headless machines with `--filter "Category!=Launch"`.
- Experience, reputation, potential, fitness, morale and injury are stored and edited but not yet changed by the game (Steps 12, 21, 22). Rating weights are code (`RatingModel.Default`), not a data file (Step 33).
- Player import files in the old 8-rating format are no longer accepted.
- Only league competitions can be scheduled; cups/knockout formats come in Step 17. No relegation playoffs, groups or reseeding (see `docs/COMPETITION_FORMATS.md` §2).
- Competition UI: the match centre shows schedules and results and can create a regular-season schedule; standings are on the *Career* screen; playoffs advance automatically with the career calendar.
- Import covers countries, arenas, clubs, leagues, divisions, competitions, seasons, participants, players and player contracts only (see `docs/IMPORT_FORMATS.md` §7); squad roles are set in the game.
- The squad screen's *Add a player to the squad* (`AssignPlayerAsync`) still moves a player without a contract (squad tool); the game flow signs through *Club contracts*. Board expectations and finances are stored but not used by any game system yet (Steps 12, 27).
- Contracts: salaries and performance bonuses are calculated but not paid from the club balance (Steps 12, 27); the wage budget is enforced for AI clubs only (the user gets a warning; Step 27 decides the board's rules); undecided options lapse at expiry (no reminders before Step 12); the editor and the importer do not check overlapping contracts.
- Transfers: no persisted offers (negotiations answer at once and AI clubs never bid for a managed club's players; pending offers come with the career mode); AI clubs only buy surplus players of other AI clubs and free agents; valuations ignore form and statistics; loans copy the player's salary (no salary sharing, no option to buy) and the player does not negotiate them; a loaned player cannot be bought until recalled; transfer history is not importable (`Transfers` is created by the game only); the loan club's termination of a loan contract does not close the loan (use *Recall*).
- A player with an open club spell but no contract counts as a free agent; signing him elsewhere ends that spell.
- Match engine: substitutions and timeouts only between possessions; no technical/unsportsmanlike/intentional fouls or held balls; fitness, morale and injuries are not changed by matches yet (Steps 12, 22); box scores store whole minutes; play-by-play, energy and plus-minus are not stored (only box scores), so past matches show box scores and team stats only; an unfinished live match is lost when the application closes.
- Tactics: AI clubs keep the default tactic until Step 13 (AI lineups and tactics). The tactics screen shows enum names (`PickAndRoll`, `Zone23`).
- Off-screen renders of the editor grids collapse star-width columns (render-test artefact, not seen in the running app).
- Player, club profile and squad screens are verified by an off-screen render test (screenshots reviewed in Steps 05–06); the other editor tabs only by the smoke-test launch and view-model tests.

## Current dependencies
| Package | Version | Used by |
|---|---|---|
| Microsoft.Extensions.Hosting | 10.0.14 | Hosting |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.14 | Persistence |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | Services, Persistence, DataImport |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | DataImport |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.14 | Persistence |
| Microsoft.EntityFrameworkCore.Design | 10.0.14 | Persistence (design time only, not shipped) |
| dotnet-ef (local tool) | 10.0.14 | Creating migrations |
| Serilog | 4.4.0 | Hosting |
| Serilog.Extensions.Logging | 10.0.0 | Hosting |
| Serilog.Sinks.File | 7.0.0 | Hosting |
| Serilog.Sinks.Debug | 3.0.0 | Hosting |
| CommunityToolkit.Mvvm | 8.4.2 | Desktop |
| xunit / xunit.runner.visualstudio | 2.9.3 / 3.1.4 | Tests |
| Microsoft.NET.Test.Sdk | 17.14.1 | Tests |
| coverlet.collector | 6.0.4 | Tests |

## Last completed step log
logs/STEP_12_LOG.md

## Instructions for continuing
1. Read this file.
2. Read the last completed step log.
3. Inspect the existing implementation.
4. Follow the next step specification in `docs/ROADMAP.md`.
5. Do not repeat completed work.
6. Generate a new step log upon completion.
