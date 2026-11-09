# PROJECT STATE

## Project
European Basketball Manager

## Current milestone
Milestone 1 — Playable core game

## Current step
None in progress

## Last completed step
STEP 03 — Data import and database editor (2026-11-09)

## Next step
STEP 04 — Countries, leagues and competitions

## Application status
- Build: PASS (Debug and Release, 0 warnings, 0 errors)
- Tests: PASS (131/131: 80 unit, 51 integration)
- Run status: Launches. Creates/migrates the game database at startup and opens the database editor (players, clubs, contracts, leagues, competitions, import). No game features yet.

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
- [x] Domain layer (`Ebm.Domain`): 17 entities with strongly typed IDs and validation
- [x] Services layer (`Ebm.Services`): `TimeProvider`, data access ports (`IRepository<,>`, `IUnitOfWork`)
- [x] Persistence layer (`Ebm.Persistence`): EF Core/SQLite context, mappings, migrations, repositories, startup initialiser
- [x] Data import layer (`Ebm.DataImport`): CSV/JSON import, validation, duplicate detection, import logs
- [x] Composition root (`Ebm.Hosting`): configuration, data paths, logging, error policy
- [x] Desktop shell (`Ebm.Desktop`): main window, global exception handler, smoke-test mode
- [x] Automated tests (unit, integration, architecture, launch)
- [x] Database and entity model (Step 02)
- [x] Data import and database editor (Step 03)
- [ ] Competitions (Step 04) … see docs/ROADMAP.md

## Database
- Current schema version: `20261109114155_InitialSchema`
- Latest migration: `20261109114155_InitialSchema` (17 tables)
- Data status: empty database created at startup. Sample dataset `data/sample/italy-sample.json` (fictional league) and templates in `data/templates` can be imported from the Import tab. Test-only mock data in `tests/Ebm.Tests.Integration/TestSupport/SampleWorld.cs`.

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

Details: `docs/TECHNICAL_ARCHITECTURE.md`, `docs/DATABASE_SCHEMA.md`, `docs/IMPORT_FORMATS.md`.

## Known issues
- The launch integration test opens a real window for about one second and needs an interactive desktop session. Skip it on headless machines with `--filter "Category!=Launch"`.
- Not yet under version control. The owner will run `git init`. `.gitignore` is already present.
- The player attribute set is an initial one (8 ratings, 1–100). Step 05 finalises it.
- The competition rules JSON has no schema yet (only "must be a JSON object"). Step 04 defines it.
- Import covers countries, arenas, clubs, leagues, divisions, competitions, players and player contracts only (see `docs/IMPORT_FORMATS.md` §7).
- The editor screens were verified by the smoke-test launch and view-model tests; no interactive screenshot review was possible in the Step 03 session.

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
logs/STEP_03_LOG.md

## Instructions for continuing
1. Read this file.
2. Read the last completed step log.
3. Inspect the existing implementation.
4. Follow the next step specification in `docs/ROADMAP.md`.
5. Do not repeat completed work.
6. Generate a new step log upon completion.
