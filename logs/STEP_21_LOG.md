# STEP 21 — Training, development and player aging

## 1. Status
- Status: COMPLETED
- Date: 2026-11-17
- Build version: 0.21.0
- Commit/reference: STEP 21 — Training, development and player aging
- Previous step: STEP 20 — Scouting and player evaluation
- Next step: STEP 22 — Injuries and medical management

## 2. Objectives

From `docs/ROADMAP.md`, Step 21: individual and team training; shooting, physical, tactical and defensive
development; recovery; age-related development and decline; potential realisation; retirement. Development depends
on age, potential, playing time, training quality, staff, competition level, professionalism, fitness and injuries.

| Criterion | Proof |
|---|---|
| Players can improve | `PlayerDevelopmentTests.Young_players_with_potential_improve`; `DevelopmentServiceTests.Young_players_improve_and_old_players_decline_week_by_week` |
| Players can decline | `PlayerDevelopmentTests.Old_players_decline_and_athleticism_goes_first`; same integration test |
| Development is not deterministic | `PlayerDevelopmentTests.Development_is_not_deterministic_but_reproducible_for_a_seed` (weekly form, seasonal luck, random rounding) |
| Retirement through configurable rules | `PlayerDevelopmentTests.Retirement_follows_the_configured_rules`, `Retirement_rules_bind_from_settings`; `DevelopmentServiceTests.Players_retire_at_the_end_of_the_season_and_leave_the_game` |
| AI clubs manage player development | `PlayerDevelopmentTests.The_ai_coach_trains_the_weakest_area…`; `DevelopmentServiceTests.Ai_clubs_plan_their_own_training` |
| Dependencies (age, potential, playing time, coaching, level, professionalism, fitness, injuries) | `PlayerDevelopmentTests.Growth_depends_on_…`, `A_focus_moves_development_to_its_attributes`, `Fitness_drops_with_minutes_and_recovers_with_rest` |

## 3. Implementation summary

* **Domain (`Ebm.Domain/Development`):** `TrainingPlan` (team focus, intensity, individual programmes; `TrainingFocuses`),
  `PlayerDevelopment` (growth share by age, decline curve, factors, weekly step, daily fitness, potential revision,
  `SeasonLuck`), `RetirementRules` (configurable, validated). `Management/TrainingPlanner` (AI). `Player.RetiredOn`,
  `Retire`, `Develop`, `UpdateFitness`.
* **Services:** `IDevelopmentService` / `DevelopmentService` (training screen, save plan, hire coaches, daily fitness +
  Monday development, season end); `StaffHiring` (staff lookup and generated hiring, now shared with scouting);
  `IClubManagementService.PlanTrainingAsync` (bulk AI plans); `CareerService` calls them (start, every day before the
  date moves, Mondays, season end before the window); retired players filtered from free agents, market, scouting
  and negotiation. `GameDayReport.Development` / `SeasonEnd`.
* **Hosting:** `EbmOptions.Retirement` bound from `Ebm:Retirement`, validated and registered before the game services.
* **Persistence:** migration `20261117140121_Development` (add-column and create-table only).
* **Desktop:** *Training* tab (`TrainingViewModel`, `Themes/Training.xaml`); career news for training weeks and retirements.

## 4. Files changed
### Created
- `src/Ebm.Domain/Development/` (`TrainingPlan.cs`, `PlayerDevelopment.cs`, `RetirementRules.cs`), `src/Ebm.Domain/Management/TrainingPlanner.cs`
- `src/Ebm.Services/Development/` (`IDevelopmentService.cs`, `DevelopmentService.cs`), `src/Ebm.Services/Clubs/StaffHiring.cs`
- `src/Ebm.Persistence/Configurations/DevelopmentConfigurations.cs`, `Migrations/20261117140121_Development.cs` (+ designer)
- `src/Ebm.Desktop/ViewModels/Training/TrainingViewModel.cs`, `Themes/Training.xaml`
- `tests/Ebm.Tests.Unit/Development/PlayerDevelopmentTests.cs`, `tests/Ebm.Tests.Integration/Development/DevelopmentServiceTests.cs`
- `docs/DEVELOPMENT.md`, `logs/STEP_21_LOG.md`

### Modified
- Domain: `Player`
- Services: `ServiceCollectionExtensions`, `CareerService`, `ICareerService`, `ClubManagementService`, `IClubManagementService`, `ScoutingService`, `ContractService`, `TransferService`
- Hosting: `EbmOptions`, `EbmHost`; Persistence: `PeopleConfigurations`, model snapshot
- Desktop: `App.xaml`, `MainWindow.xaml`, `DatabaseEditorViewModel`, `CareerViewModel`
- Tests: migration pins and table list, `CareerViewModelTests` (news count ignores the training-week line)
- Docs: `DATABASE_SCHEMA.md`, `AI_MANAGEMENT.md`; `CHANGELOG.md`, `PROJECT_STATE.md`, `Directory.Build.props`

### Removed
- None

## 5. Tests
- Build: Debug and Release, 0 warnings, 0 errors.
- Unit: 384/384 PASS (13 new).
- Integration, fast suite (`Category!=Launch&Category!=Slow`): 135/135 PASS in ~1 min 10 s (7 new), two consecutive runs.
  The first run also showed `MilestoneOneTests.A_career_is_created…` failing once; it passed alone and in both later
  full runs (not reproduced; watch it).
- Launch test: PASS.
- Slow suite: not run (no milestone release). Development now runs every Monday over all players, so the European
  dataset tests will take somewhat longer.

## 6. Known limitations
- No youth intake; no stored development history; matches change fitness only (morale, injuries: Step 22).
- Formulas are code (documented in `docs/DEVELOPMENT.md`); only retirement is configurable.
- `ProcessDayAsync` relies on the calendar calling it once per day (not idempotent by itself).
- Scout reports do not age with development (Step 20 limitation remains).

## 7. Handoff instructions
Step 22 (injuries): `PlayerDevelopment.ConditionFactor` already stops development while injured and halves it
day-to-day; `DailyRecovery` gives injured players rest-level recovery. Injuries should come from matches/training load
(hard intensity is a natural risk factor) and the Physio role is unused so far — hire it via `StaffHiring` like coaches.

## 8. Outstanding issues
- None blocking.
