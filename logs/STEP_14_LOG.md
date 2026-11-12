# STEP 14 — Milestone 1 integration and release

**Date:** 2026-11-12
**Version:** 0.14.0
**Status:** Completed. All acceptance criteria met. **Milestone 1 delivered.**

## 1. Objectives

From `docs/ROADMAP.md`, Step 14: integrate all modules, regression testing, fix critical bugs, validate career
creation, match simulation, season progression, save and load, long-term simulation, prepare the Windows executable.

| Criterion | Proof |
|---|---|
| The game can be installed and launched | `build/publish.ps1`: self-contained single-file build, smoke test of the published exe (exit 0), zip; `DesktopLaunchTests` |
| A career can be created | `MilestoneOneTests` (career from the club options) |
| A club can be managed | Same test: free-agent signing on the career date, starting five, tactic |
| Matches can be simulated | Same test: live match with a timeout, saved like the match centre; instant matches |
| Transfers can be completed | Same test (signing); `TransferServiceTests`; AI transfers in `CareerSimulationTests` |
| A full season can be completed | `MilestoneOneTests`, `CareerServiceTests` |
| A second season can be started | Same tests: next season scheduled, first match of the second season played |
| Save files can be loaded successfully | `MilestoneOneTests` (save, play on, load → date and results restored); `Save_names_and_foreign_files_…` |

## 2. Files

### Created
| File | Purpose |
|---|---|
| `src/Ebm.Services/Saves/ISaveGameService.cs` | Save-file use cases |
| `src/Ebm.Persistence/SaveGameStore.cs` | SQLite backup implementation |
| `src/Ebm.Persistence/Migrations/20261112154154_CareerAssistant*.cs` | `Careers.AssistantManages` |
| `src/Ebm.Desktop/ViewModels/Saves/SaveGamesViewModel.cs`, `Themes/Saves.xaml` | *Saved games* screen |
| `build/publish.ps1` | Windows release build |
| `docs/SAVE_FILES.md`, `docs/RELEASE.md` | Handoff documentation |
| `tests/Ebm.Tests.Integration/Careers/MilestoneOneTests.cs` | Acceptance tests |

### Modified
`Career` (assistant), `ICareerService`/`CareerService` (`SetAssistantAsync`, assistant in the calendar),
`PersistenceOptions`, Persistence `ServiceCollectionExtensions` (saves folder, `ISaveGameService`), `AppPaths`
(`SavesDirectory`), `EbmHost`, `DatabaseEditorViewModel` (saves page), `CareerViewModel` and `Careers.xaml`
(assistant check box), `MainWindow.xaml`, `App.xaml`, `CareerSimulationTests` (`EBM_SIM_SEASONS`, assistant),
tests expecting the latest migration, `Directory.Build.props`, `README.md`, `CHANGELOG.md`, `PROJECT_STATE.md`,
`docs/DATABASE_SCHEMA.md`, `docs/AI_MANAGEMENT.md`, `docs/SEASON_LIFECYCLE.md`.

### Removed
None.

## 3. Features implemented

* Save files: save (backup to a temporary file, then rename), load (compatibility check, copy over the live game,
  migrations, career date), delete, new game, list with career and schema (docs/SAVE_FILES.md).
* Assistant (holiday) mode.
* Windows release build with smoke test (docs/RELEASE.md).

## 4. Architectural decisions

Decisions 47–49 in `PROJECT_STATE.md` (save files as SQLite backups of the always-current live database; assistant
mode; self-contained releases). The version scheme stays `0.<step>.<patch>`: the Milestone 1 release is 0.14.0.

## 5. Database changes

Migration `20261112154154_CareerAssistant`: `Careers.AssistantManages` (add-column, default false). Upgrades in one transaction.

## 6. Tests and validation

| Run | Result |
|---|---|
| `dotnet build` (Debug, Release) | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including launch) | PASS 404/404 (291 unit, 113 integration) |
| `build/publish.ps1` | PASS: publish, smoke test of `Ebm.Desktop.exe` (exit 0), zip (≈ 60 MB) |
| Long simulation `EBM_SIM_SEASONS=6` | PASS (4 min): six seasons, no forfeits, every club 10–18 players with five starters and a tactic, no negative balance, ≤ 6 moves per club and season; strongest three squads finish 2.4th on average, the others 5.3th |

Regression found and fixed: in long careers the user's club, if nobody manages it, loses its players at contract
expiry and forfeits (season 4 of the six-season run). Fix: assistant mode, used by the simulation.
Second finding: turnover grew to 90 moves in season 6 (above the 6-per-club limit) because only the best 8 players were
renewed. Fix: renewals cover the best 10 (the minimum squad), up to age 33. Transfers per season with the fix:
34, 43, 67, 66, 76 (seasons 2–6); the remaining growth comes from ageing without youth intake (Milestone 2).

## 7. Known limitations

* One career per game database; an unfinished live match is lost when the application closes.
* The executable is unsigned (SmartScreen warning) and there is no installer: the release is a zip.
* AI weaknesses: `docs/AI_MANAGEMENT.md` §9.

## 8. Outstanding issues

None.

## 9. Instructions for the next step

1. Read `PROJECT_STATE.md`, this log and `docs/ROADMAP.md` (Milestone 2, Step 15: European country and league expansion).
2. New countries and leagues should come from data (`docs/COMPETITION_FORMATS.md`, `docs/IMPORT_FORMATS.md`); the
   career, AI and saves work for any league data with divisions and seasons.
3. Keep `MilestoneOneTests` and `CareerSimulationTests` green: they are the Milestone 1 regression suite.
