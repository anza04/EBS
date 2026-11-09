# Technical Architecture

European Basketball Manager is an offline, single-player Windows desktop application.
This document records the technology stack, module boundaries and conventions that every
development step must preserve. Change it only with an explicit architectural decision
recorded in the step log.

## 1. Technology stack

| Concern | Choice | Why |
|---|---|---|
| Language / runtime | C# 14 on .NET 10 (LTS) | Mature, fast, first-class Windows support, long support window. |
| UI | WPF (`net10.0-windows`) with MVVM (CommunityToolkit.Mvvm) | Native Windows desktop, data-binding suited to stat-heavy management screens, no web runtime. |
| Composition / config | Microsoft.Extensions.Hosting (DI, configuration, options) | Standard, testable composition root shared by the app, tools and tests. |
| Logging | Microsoft.Extensions.Logging API, Serilog implementation (rolling file + debug sinks) | Code depends only on `ILogger<T>`; Serilog is swappable and confined to the host. |
| Database | SQLite via EF Core 10 (Microsoft.EntityFrameworkCore.Sqlite) | Embedded single-file database, no server, versioned migrations. See `docs/DATABASE_SCHEMA.md`. |
| Tests | xUnit 2, Microsoft.NET.Test.Sdk, coverlet | Standard .NET test stack; runs from `dotnet test`. |
| Build | .NET SDK 10.0.203 (`global.json`), `.slnx` solution, central package management | Reproducible builds from the command line, one place for package versions. |

All dependencies are local NuGet packages; nothing requires a network connection at runtime.

## 2. Solution layout

```text
EBS/
├── EuropeanBasketballManager.slnx   Solution
├── global.json                      Pinned SDK
├── dotnet-tools.json                Local .NET tools (dotnet-ef); restore with `dotnet tool restore`
├── Directory.Build.props            Shared build settings and product version
├── Directory.Packages.props         All NuGet versions (central package management)
├── .editorconfig                    Code style (enforced during build)
├── PROJECT_STATE.md / CHANGELOG.md / README.md
├── docs/                            Roadmap and technical documentation
├── logs/                            Development step logs (STEP_XX_LOG.md)
├── data/                            Import templates and sample datasets (copied next to the executable)
├── src/
│   ├── Ebm.Domain/                  Game domain layer
│   ├── Ebm.Services/                Game services layer
│   ├── Ebm.Persistence/             Persistence layer
│   ├── Ebm.DataImport/              Data import layer
│   ├── Ebm.Hosting/                 Composition root (config, logging, error policy)
│   └── Ebm.Desktop/                 Application/UI layer (WPF)
├── tests/
│   ├── Ebm.Tests.Unit/              Fast, isolated tests (net10.0)
│   └── Ebm.Tests.Integration/       Host composition and real-executable launch tests
└── tools/                           Developer tools (from Step 03)
```

`tools/` is created by the step that first needs it. A `tests/Ebm.Tests.Simulation`
project for large-scale match statistics is added in Step 10.

## 3. Layers and dependency rules

```text
            Ebm.Desktop (WPF)
              │        │
              ▼        ▼
        Ebm.Hosting ─► Ebm.Services ─► Ebm.Domain
           │   │            ▲   ▲
           │   └► Ebm.Persistence ─┘   (implements ports defined in Services)
           └────► Ebm.DataImport ─────┘ (writes through Services ports)
```

| Layer | Responsibility | May reference |
|---|---|---|
| **Domain** | Entities, value objects, game rules, rule violations (`DomainException`). Pure C#. | nothing (no NuGet packages either) |
| **Services** | Use cases and orchestration (career, season, transfers, match simulation). Defines *ports* (interfaces such as repositories) that outer layers implement. | Domain |
| **Persistence** | Database (`EbmDbContext`), migrations, repository implementations, database initialisation, save games. The only layer that references EF Core. | Domain, Services |
| **DataImport** | CSV/JSON parsing, validation, mapping to domain objects. Implements `IDataImportService` (Services). See `docs/IMPORT_FORMATS.md`. | Domain, Services |
| **Hosting** | Builds the generic host: configuration, logging, error-handling policy, DI wiring of all layers. UI-agnostic. | all of the above |
| **Desktop** | Windows, views, view models. Calls Services only. | Hosting, Services, Domain |

Rules:

1. Dependencies point inwards only. These rules are enforced by
   `tests/Ebm.Tests.Unit/Architecture/LayerDependencyTests.cs` and
   `tests/Ebm.Tests.Integration/DesktopArchitectureTests.cs`.
2. Game logic never lives in the UI. A view model may format and forward, never decide.
3. Country, league and competition rules are **data**, not code (see roadmap §1.1).
4. Only Hosting references Serilog. Everything else uses `Microsoft.Extensions.Logging.ILogger<T>`.
5. Every layer exposes one registration method (`AddGameServices`, `AddPersistence`,
   `AddDataImport`, `AddDesktopUi`) called by the composition root; registrations stay inside their layer.
6. Data access goes through the ports `IRepository<TEntity, TId>` and `IUnitOfWork` (`Ebm.Services.Data`), resolved
   from one DI scope per unit of work. Entity mappings use Fluent configuration in Persistence, never attributes on domain types.
7. Use cases the UI calls (e.g. `IDatabaseEditor`, `IDataImportService`) are interfaces in Services. Each call
   creates its own DI scope (one unit of work) and is registered as a singleton. The UI shows `DomainException`
   and `DataConstraintException` messages on the page instead of passing them to the global handler.
8. Real-world time comes from the injected `TimeProvider`, never `DateTime.Now`, so tests are deterministic.
   (In-game dates are a separate domain concept, introduced with the season calendar.)

## 4. Application startup

1. `App.OnStartup` parses desktop switches (`LaunchOptions`; currently `--smoke-test`).
2. `EbmHost.CreateBuilder(args)` loads configuration, resolves and creates the data folders,
   configures Serilog and registers every layer.
3. `AddDesktopUi` registers windows and view models; the host is built and started. Starting the host runs
   `DatabaseInitializer`, which creates `<DataRoot>\databases\ebm.db` if needed and applies pending migrations.
4. `GlobalExceptionHandler` is attached, the startup line is logged and `MainWindow` is shown.
5. On exit the host is stopped and disposed, which flushes the log files.

If anything fails before step 4, `StartupFailureLog` writes the exception to
`%LOCALAPPDATA%\EuropeanBasketballManager\logs\startup-failure-*.log` (falling back to `%TEMP%`),
a dialog is shown, and the process exits with code 2.

## 5. Configuration

Bound to `EbmOptions` from the `Ebm` section. Sources, lowest to highest priority:

1. `src/Ebm.Desktop/appsettings.json` (copied next to the executable)
2. `appsettings.{Environment}.json` (environment from `DOTNET_ENVIRONMENT`, default `Production`)
3. Environment variables, e.g. `Ebm__Paths__DataRoot=D:\EbmData`
4. Command line, e.g. `--Ebm:Paths:DataRoot=D:\EbmData`

| Key | Default | Meaning |
|---|---|---|
| `Ebm:Paths:DataRoot` | empty → `%LOCALAPPDATA%\EuropeanBasketballManager` | Root for all user data (`logs\`, `databases\ebm.db`). Environment variables are expanded. |
| `Ebm:Logging:MinimumLevel` | `Information` | `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`. |
| `Ebm:Logging:RetainedFileCount` | `14` | Daily log files kept. |

Invalid values fail fast at startup (exit code 2) rather than being silently ignored.

## 6. Logging

* Files: `<DataRoot>\logs\ebm-YYYYMMDD.log`, one per day, last 14 kept. Also written to the debugger output.
* Data imports also write one report each to `<DataRoot>\logs\imports\` (see `docs/IMPORT_FORMATS.md` §5).
* Format: `2026-11-09 12:09:46.113 +01:00 [INF] Ebm.Desktop.App: message`, with exception stack traces on following lines.
* `Microsoft.*` and `System.*` categories are limited to `Warning`.
* Use structured message templates (`"Loaded {Count} players"`), never string interpolation.
* Level guide: `Debug` diagnostic detail, `Information` lifecycle and user actions,
  `Warning` recoverable problems, `Error` failed operations, `Critical` the application must close.

## 7. Error handling

| Situation | Handling |
|---|---|
| Rule violation the player can fix | Throw a `DomainException` subtype with a player-readable message. If it reaches the UI unhandled, it is logged as a warning, shown, and the app continues. |
| Unexpected exception on the UI thread | Logged as critical; the player sees where the logs are; the app closes with exit code 1. Continuing in an unknown state could corrupt the database or save files. |
| Unhandled exception on a background thread | Logged as critical; the runtime terminates the process. |
| Unobserved faulted task | Logged as error and marked observed; no dialog. |
| Failure before logging exists | `StartupFailureLog` file, dialog, exit code 2. |

The rules live in `Ebm.Hosting.ErrorHandling.ExceptionHandlingPolicy` (unit-tested);
`Ebm.Desktop.Startup.GlobalExceptionHandler` only connects WPF events to it.
Expected failures inside services should be handled where they occur; the global handler is a safety net.

Exit codes: `0` success, `1` unhandled exception, `2` startup failure (`Ebm.Desktop.Startup.ExitCodes`).

## 8. Build, test and run

```powershell
dotnet build EuropeanBasketballManager.slnx                      # Debug build
dotnet build EuropeanBasketballManager.slnx -c Release           # Release build
dotnet test  EuropeanBasketballManager.slnx                      # all tests (launches the app once)
dotnet test  EuropeanBasketballManager.slnx --filter "Category!=Launch"   # headless machines
dotnet run   --project src/Ebm.Desktop                           # start the game
dotnet run   --project src/Ebm.Desktop -- --smoke-test           # start, render, exit 0
```

Build settings: nullable reference types on, warnings are errors, code style enforced in build.
A Windows installer/executable package is produced in Step 14.

Database migrations (details in `docs/DATABASE_SCHEMA.md` §6):

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/Ebm.Persistence --startup-project src/Ebm.Persistence --output-dir Migrations
```

## 9. Conventions

* Namespaces follow folders, file-scoped: `Ebm.<Layer>.<Feature>`.
* Entities derive from `Entity<TId>`, take their strongly typed ID in the constructor, validate every argument
  (throwing `DomainValidationException`) and expose `private set` properties; a private parameterless constructor is kept for EF Core.
* Internal EF entity configurations are grouped per feature file in `Ebm.Persistence/Configurations` (the
  one-public-type-per-file rule applies to public types). Generated migrations are excluded from code-style rules.
* Organise by feature inside a layer (e.g. `Ebm.Domain/Players`), not by technical type.
* Private fields `_camelCase`; one public type per file; `sealed` by default.
* Validate public arguments with `ArgumentNullException.ThrowIfNull` and similar guards.
* Tests: `Method_or_behaviour_condition_expected` names; one test class per production class; unit tests
  never touch the real `%LOCALAPPDATA%` or the network, they use temporary folders.
* UI colours, fonts and styles come from `src/Ebm.Desktop/Themes/Shell.xaml` resource keys.
* Versioning: `0.<step>.<patch>` during Milestone 1 (set in `Directory.Build.props`), `1.0.0` at the Milestone 1 release.
* Each step updates `CHANGELOG.md`, `PROJECT_STATE.md` and adds `logs/STEP_XX_LOG.md`.
