# STEP 26 — Advanced roster management and chemistry

## 1. Status
- Completed 2026-11-18. Version 0.26.0.
- Build: PASS (0 warnings, 0 errors). Tests: see §5.

## 2. Objectives
Squad hierarchy, player roles, playing-time expectations, leadership, relationships, team chemistry, tactical
compatibility, morale, internal conflicts, role satisfaction. Chemistry must not overpower ability or tactics.

| Acceptance criterion | Where |
|---|---|
| Player morale changes over time | Weekly update (`ProcessWeekAsync`, Mondays in `CareerService`) — `A_week_changes_morale_relationships_and_reports_conflicts`, `Morale_follows_results_playing_time_and_leaders_and_stays_in_range` |
| Playing time affects satisfaction | `ChemistryModel.Satisfaction` from four-week minutes — `Satisfaction_compares_minutes_with_the_expectation`, integration test (star at 5 min → unhappy) |
| Relationships can develop | `PlayerRelationship` (+ shared court time / nationality, − conflicts) — both test classes |
| Chemistry affects match performance in a controlled way | `TeamSheet.Chemistry` in make/turnover rates, morale in form — `Chemistry_changes_results_in_a_controlled_way` (±1 ≈ 5.5 pts vs 8.7 for +6 overall); `MatchServiceTests` checks the sheet value |
| AI clubs manage squad balance | `LineupPlanner` balance bonus, release order — `Ai_coach_gives_an_unhappy_player_minutes_over_a_slightly_better_teammate` |

## 3. Implementation summary
- Domain (`Ebm.Domain.Chemistry`): `ChemistryModel` (hierarchy, leaders/captain, satisfaction, weekly morale,
  relationship changes, conflicts, team chemistry, match constants), `PlayerRelationship`.
- Match engine: `TeamSheet.Chemistry` (validated −1…1), `TeamSheet.Core`; make probability, turnover chance and form.
- Services: `IChemistryService` (`ProcessWeekAsync`, `GetClubChemistryAsync`, `RateAsync`); `MatchService` sets sheet
  chemistry; `CareerService` runs the weekly update (`GameDayReport.Chemistry`); `ClubManagementService` squad balance.
- `LineupCandidate.Balance` (optional, default 0).
- UI: *Dressing room* tab (`DressingRoomViewModel`, `Themes/DressingRoom.xaml`); conflict news on the *Career* screen.
- Formulas: `docs/CHEMISTRY.md`.

## 4. Files changed
- New: `src/Ebm.Domain/Chemistry/ChemistryModel.cs`, `PlayerRelationship.cs`; `src/Ebm.Services/Chemistry/IChemistryService.cs`,
  `ChemistryService.cs`; `src/Ebm.Persistence/Configurations/ChemistryConfigurations.cs`; migration
  `20261118090203_PlayerChemistry`; `src/Ebm.Desktop/ViewModels/Chemistry/DressingRoomViewModel.cs`,
  `Themes/DressingRoom.xaml`; `tests/Ebm.Tests.Unit/Chemistry/ChemistryModelTests.cs`,
  `tests/Ebm.Tests.Integration/Clubs/ChemistryServiceTests.cs`; `docs/CHEMISTRY.md`.
- Modified: `MatchSetup.cs`, `MatchSimulation.Teams.cs`, `MatchSimulation.Play.cs`, `LineupPlanner.cs`,
  `ClubManagementService.cs`, `MatchService.cs`, `CareerService.cs`, `ICareerService.cs`, Services DI, snapshot,
  `DatabaseEditorViewModel.cs`, `CareerViewModel.cs`, `MainWindow.xaml`, `App.xaml`, tests (`DatabaseInitializerTests`,
  `ContractServiceTests`, `MatchServiceTests`), docs (`MATCH_ENGINE.md`, `AI_MANAGEMENT.md`, `DATABASE_SCHEMA.md`),
  `CHANGELOG.md`, `PROJECT_STATE.md`, `Directory.Build.props`.

## 5. Tests
- Unit: PASS 440/440 (12 new).
- Integration fast suite (`Category!=Launch&Category!=Slow`): PASS 151/151 (1 new test, 1 extended). Launch: PASS.
- Slow suite (`Category=Slow`: `CareerSimulationTests`, `MilestoneTwoTests`, `EuropeanTransferWindowTests`, European
  datasets): **not run** (needs the user's go-ahead). Pending since Step 25 as well.
- Manual UI: *Dressing room* tab verified only by the launch smoke test and build (no off-screen render).

## 6. Known limitations
- Morale ignores individual performances, transfers, talks; no transfer requests, no user captain choice.
- Chemistry fixed for a match; relationships only from court time.
- The Monday update loads rosters and four weeks of box scores of every club that played: cost on the European
  dataset unmeasured.

## 7. Handoff instructions
- Run the slow suite one class at a time (with approval): `CareerSimulationTests` first (exercises the weekly update
  across three seasons), then `MilestoneTwoTests`, `EuropeanTransferWindowTests`; check Monday-update timing.
- Step 27 (advanced tactical engine) can read `TeamSheet.Chemistry`/morale; AI coaches' situational tactics should
  not change chemistry rules.

## 8. Outstanding issues
- Slow-suite confirmation pending (§5).
