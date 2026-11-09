# PROJECT STATE

## Project
European Basketball Manager

## Current milestone
Milestone 1 — Playable core game

## Current step
None in progress

## Last completed step
STEP 06 — Club and roster management (2026-11-09)

## Next step
STEP 07 — Contracts and player availability

## Application status
- Build: PASS (Debug and Release, 0 warnings, 0 errors)
- Tests: PASS (287/287: 208 unit, 79 integration)
- Run status: Launches. Creates/migrates the game database at startup and opens the database editor (players, player profile, player comparison, clubs, club profile, squad, contracts, leagues, competitions, import). The competition engine is available as `ICompetitionService`; player ratings as `PlayerRatingCalculator` / `IPlayerProfileService`; clubs and squads as `IClubService`.

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
- [x] Domain layer (`Ebm.Domain`): 18 entities with strongly typed IDs and validation
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
- [ ] Contracts and player availability (Step 07) … see docs/ROADMAP.md

## Database
- Current schema version: `20261109171322_ClubsAndRosters`
- Latest migration: `20261109171322_ClubsAndRosters` (club city, reputation, balance, wage budget, board expectation; new `RosterSpots`; 19 tables)
- Data status: empty database created at startup. Sample dataset `data/sample/italy-sample.json` (fictional league: Serie A 8 clubs, Serie A2 6 clubs, season 2026-27 with entries and full competition rules; 33 players with full Step 05 profiles; club profiles; Aurora Milano has a complete 12-player squad) and templates in `data/templates` can be imported from the Import tab. Test-only mock data in `tests/Ebm.Tests.Integration/TestSupport/SampleWorld.cs`.

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

Details: `docs/TECHNICAL_ARCHITECTURE.md`, `docs/DATABASE_SCHEMA.md`, `docs/IMPORT_FORMATS.md`, `docs/COMPETITION_FORMATS.md`, `docs/PLAYER_MODEL.md`, `docs/ROSTER_RULES.md`.

## Known issues
- The launch integration test opens a real window for about one second and needs an interactive desktop session. Skip it on headless machines with `--filter "Category!=Launch"`.
- Experience, reputation, potential, fitness, morale and injury are stored and edited but not yet changed by the game (Steps 12, 21, 22). Rating weights are code (`RatingModel.Default`), not a data file (Step 33).
- Player import files in the old 8-rating format are no longer accepted.
- Only league competitions can be scheduled; cups/knockout formats come in Step 17. No relegation playoffs, groups or reseeding (see `docs/COMPETITION_FORMATS.md` §2).
- Season outcome (promoted/relegated) is calculated but not applied to next season's entries; Step 12 does that.
- No UI for competitions yet (schedule, table, playoffs); Step 11 adds presentation.
- Import covers countries, arenas, clubs, leagues, divisions, competitions, seasons, participants, players and player contracts only (see `docs/IMPORT_FORMATS.md` §7); squad roles are set in the game.
- Assigning a player to a club does not touch contracts (Step 07); the old contract stays until termination exists. Board expectations and finances are stored but not used by any game system yet (Steps 12, 27).
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
logs/STEP_06_LOG.md

## Instructions for continuing
1. Read this file.
2. Read the last completed step log.
3. Inspect the existing implementation.
4. Follow the next step specification in `docs/ROADMAP.md`.
5. Do not repeat completed work.
6. Generate a new step log upon completion.
