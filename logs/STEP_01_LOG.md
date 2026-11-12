# STEP 01 — Project foundation and application architecture

## 1. Status
- Status: COMPLETED
- Date: 2026-11-09
- Build version: 0.1.0
- Commit/reference: none yet. The repository is initialised after this step.
- Previous step: none
- Next step: STEP 02 — Core database and entity model

## 2. Objectives
- Create the application project.
- Configure the development environment.
- Establish the main application entry point.
- Define the application architecture and module boundaries.
- Configure logging and error handling.
- Establish a consistent folder structure.
- Configure automated testing.
- Configure application build and execution.

## 3. Implementation summary
- Created a .NET 10 solution with six source projects, one for each architectural layer plus a composition root, and two test projects.
- Pinned the SDK (`global.json`) and centralised build settings (`Directory.Build.props`). All NuGet versions are pinned in one place (`Directory.Packages.props`). Code style is enforced during the build (`.editorconfig`) and warnings are errors.
- `Ebm.Hosting.EbmHost.CreateBuilder` is the single composition root. It loads configuration (`Ebm` section), resolves and creates the user data folder, configures Serilog and registers every layer.
- Logging writes daily rolling files under `<DataRoot>\logs` plus debug output. Application code uses only `ILogger<T>`.
- Error handling: `ExceptionHandlingPolicy` (UI-agnostic, unit-tested) decides how unhandled exceptions are logged and whether the app continues. `GlobalExceptionHandler` connects the WPF dispatcher, AppDomain and TaskScheduler events to it. `StartupFailureLog` covers failures that happen before logging exists. Exit codes are defined.
- WPF shell (`MainWindow` + `ShellViewModel`) shows product name, version, data folder and log folder, and can open the log folder. Visual styles live in a shared theme dictionary.
- The `--smoke-test` switch launches the app, waits for the window to render, logs the result and exits with code 0. It is used by the automated launch test.
- Architecture tests enforce the layer dependency rules.

## 4. Files changed
### Created
- `EuropeanBasketballManager.slnx`: solution
- `global.json`: pinned .NET SDK 10.0.203 (roll forward to the latest feature band)
- `Directory.Build.props`: shared compiler settings, product name, version 0.1.0
- `Directory.Packages.props`: central NuGet versions
- `.editorconfig`: code style rules
- `.gitignore`: standard .NET ignore file
- `README.md`, `CHANGELOG.md`, `PROJECT_STATE.md`: project documents
- `docs/TECHNICAL_ARCHITECTURE.md`: stack, layers, conventions, commands
- `logs/STEP_01_LOG.md`: this log
- `src/Ebm.Domain/Ebm.Domain.csproj`, `Common/DomainException.cs`: domain layer and rule-violation base exception
- `src/Ebm.Services/Ebm.Services.csproj`, `ServiceCollectionExtensions.cs`: services layer; registers `TimeProvider`
- `src/Ebm.Persistence/Ebm.Persistence.csproj`, `ServiceCollectionExtensions.cs`: persistence layer registration point (empty)
- `src/Ebm.DataImport/Ebm.DataImport.csproj`, `ServiceCollectionExtensions.cs`: data import registration point (empty)
- `src/Ebm.Hosting/Ebm.Hosting.csproj`: composition root project
- `src/Ebm.Hosting/EbmHost.cs`: host builder that wires all layers
- `src/Ebm.Hosting/AppPaths.cs`: user data folder resolution
- `src/Ebm.Hosting/ApplicationInfo.cs`: product name and version
- `src/Ebm.Hosting/Configuration/EbmOptions.cs`: configuration model
- `src/Ebm.Hosting/Logging/LoggingSetup.cs`: Serilog configuration
- `src/Ebm.Hosting/ErrorHandling/ExceptionHandlingPolicy.cs`: unhandled-exception rules
- `src/Ebm.Hosting/ErrorHandling/StartupFailureLog.cs`: pre-logging failure record
- `src/Ebm.Desktop/Ebm.Desktop.csproj`, `app.manifest`, `appsettings.json`: WPF application, Per-Monitor V2 DPI, default config
- `src/Ebm.Desktop/App.xaml(.cs)`: entry point, host lifetime, smoke-test mode
- `src/Ebm.Desktop/MainWindow.xaml(.cs)`: shell window
- `src/Ebm.Desktop/ViewModels/ShellViewModel.cs`: shell view model
- `src/Ebm.Desktop/Themes/Shell.xaml`: shared colours, fonts and styles
- `src/Ebm.Desktop/Startup/LaunchOptions.cs`, `ExitCodes.cs`, `GlobalExceptionHandler.cs`, `DesktopServiceCollectionExtensions.cs`: startup infrastructure
- `tests/Ebm.Tests.Unit/…`: architecture, hosting, services tests and `RecordingLogger` test double
- `tests/Ebm.Tests.Integration/…`: host composition, desktop architecture and launch tests, `TempDataRoot` helper

### Modified
- none

### Removed
- `project_state.md` was moved to `docs/ROADMAP.md` with identical content (SHA-256 `82CA6543…FA89EE8C`). Windows file names are case-insensitive, so the roadmap file would have been overwritten by the `PROJECT_STATE.md` status file that roadmap §1.2 requires.

## 5. Architecture and technical decisions
- **C# / .NET 10 / WPF.** Native Windows, long-term support, strong tooling, good data-binding for table-heavy management screens. It needs no web runtime and no online dependency. Unity, Electron and Avalonia were rejected: this is a data and simulation game with a 2D UI, and cross-platform support is out of scope.
- **Separate composition root (`Ebm.Hosting`)** instead of wiring inside the WPF project. Integration tests and future tools (importer CLI, simulation runner) build exactly the same host without WPF.
- **Ports in Services.** Persistence and DataImport depend on Services (not the reverse), so game logic can be tested without a database.
- **Microsoft.Extensions.Logging facade + Serilog.** Only Hosting knows Serilog. The logger is owned by the host and disposed with it; the static `Log.Logger` is not used, so tests stay isolated.
- **Fail-closed on unexpected exceptions.** Continuing after an unknown error risks writing corrupted state into the database or a save file, which roadmap §1.1 asks us to protect.
- **Configuration errors fail fast** (exit code 2, readable startup-failure log) instead of falling back to defaults silently.
- **SQLite + EF Core** is the intended persistence technology for Step 02. It is not added yet (roadmap rule: no future-step features).
- **xUnit 2.9.3** (template default, stable) rather than xUnit v3. Migration is possible later without architectural impact.

## 6. Database changes
- New entities: none
- Modified entities: none
- Removed entities: none
- Migrations: none
- Compatibility considerations: none (no save format exists yet)

## 7. Tests and validation
| Test | Result | Notes |
|---|---|---|
| Build | PASS | Debug and Release, 0 warnings, 0 errors (warnings are errors) |
| Unit tests | PASS | 22/22: layer dependency rules, AppPaths, ApplicationInfo, ExceptionHandlingPolicy, StartupFailureLog, service registration |
| Integration tests | PASS | 7/7: host composition, log file creation and content, configurable minimum level, policy logging with stack trace, desktop architecture, real executable launch |
| Manual tests | PASS | `--smoke-test` run exits 0 with startup/render/exit log lines; invalid `MinimumLevel=Bogus` exits 2 and writes `startup-failure-*.log` with the reason |
| Regression tests | N/A | First step |

Command: `dotnet test EuropeanBasketballManager.slnx`

## 8. Known limitations
- `Ebm.Persistence` and `Ebm.DataImport` contain only empty registration methods. This is intentional: Steps 02 and 03 fill them.
- The shell window has no navigation. A navigation/screen framework will be introduced when the first real screens arrive (Step 03 database editor).
- No packaged executable or installer yet (Step 14). The app runs from the build output or with `dotnet run`.
- The launch test requires an interactive Windows session.

## 9. Outstanding issues
- No version control yet. The owner will initialise git after this step. `.gitignore` is ready. Suggested first commit: everything except `bin/`, `obj/` and `TestResults/`.

## 10. Handoff instructions
Step 02 — Core database and entity model:
1. Read `PROJECT_STATE.md`, this log and `docs/TECHNICAL_ARCHITECTURE.md`.
2. Put entities and their invariants in `src/Ebm.Domain`, organised by feature folder (`Countries/`, `Competitions/`, `Clubs/`, `Players/`, …). Keep Domain free of EF Core and other packages; the architecture test `Domain_has_no_package_dependencies` will fail otherwise.
3. Define repository/unit-of-work interfaces (ports) in `src/Ebm.Services`.
4. Implement them in `src/Ebm.Persistence` with SQLite + EF Core. Add the packages to `Directory.Packages.props` (versions only there). Use Fluent configuration in Persistence, not attributes on domain types. Register them in `Ebm.Persistence.ServiceCollectionExtensions.AddPersistence`.
5. Put the database file under the data root. Extend `AppPaths` with a databases folder and `EbmOptions` with any new settings, and document the keys in `docs/TECHNICAL_ARCHITECTURE.md` §5.
6. Use versioned EF Core migrations. Apply them through an explicit, logged initialisation service called during startup. Do not use `EnsureCreated`.
7. Integration tests for persistence should use a temporary SQLite file (see `TempDataRoot`), never the user's real data folder.
8. Create `docs/DATABASE_SCHEMA.md`, bump `<Version>` in `Directory.Build.props` to `0.2.0`, and update `CHANGELOG.md`, `PROJECT_STATE.md` and `logs/STEP_02_LOG.md`.

## 11. Project state
- Working features: application shell, configuration, logging, global error handling, smoke-test launch mode
- Current application status: builds, launches, all tests pass
- Database version: none
- Build command: `dotnet build EuropeanBasketballManager.slnx`
- Test command: `dotnet test EuropeanBasketballManager.slnx` (headless: add `--filter "Category!=Launch"`)
- Run command: `dotnet run --project src/Ebm.Desktop`

## 12. Completion checklist
- [x] Objectives implemented
- [x] Build successful
- [x] Tests passed
- [x] No critical regression
- [x] Documentation updated
- [x] PROJECT_STATE.md updated
- [x] Next step can start independently
