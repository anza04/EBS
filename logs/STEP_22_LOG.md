# STEP 22 — Injuries and medical management

## 1. Status
- Status: COMPLETED
- Date: 2026-11-17
- Build version: 0.22.0
- Commit/reference: STEP 22 — Injuries and medical management
- Previous step: STEP 21 — Training, development and player aging
- Next step: STEP 23 — Financial management

## 2. Objectives

From `docs/ROADMAP.md`, Step 22: injury types, severity, recovery estimates, medical assessment, rehabilitation,
training restrictions, return-to-play fitness, recurrence risk, injury history. Risk depends on workload, fatigue,
player condition and physical attributes.

| Criterion | Proof |
|---|---|
| Injuries occur during matches and training | `MedicalServiceTests.Injuries_happen_in_matches_and_get_a_medical_assessment`, `Injuries_happen_in_training_but_not_to_free_agents_and_physios_are_hired`; `InjuryRulesTests.Match_and_training_chances_follow_minutes_intensity_and_frequency` |
| Injured players are unavailable when appropriate | `MedicalServiceTests.Injured_players_are_unavailable_and_ai_coaches_leave_them_out`; `InjuryRulesTests.Minor_injuries_leave_the_player_doubtful_…` (minor = doubtful, others = out) |
| Recovery progresses over time | `MedicalServiceTests.Injured_players_recover_over_time_and_return_short_of_fitness`; `InjuryRulesTests.An_injury_goes_through_treatment_and_rehabilitation_to_recovery`, `Better_physios_heal_faster_and_estimate_better` |
| Medical information is stored | `Injuries` table; same recovery test (history, reassessment), `The_medical_screen_shows_injuries_and_history` |
| AI clubs manage injured players | `MedicalServiceTests.Ai_clubs_sign_cover_for_long_term_injuries`, `Injured_players_are_unavailable_and_ai_coaches_leave_them_out`; `InjuryRulesTests.The_ai_coach_rests_hurt_players_and_avoids_hard_training_in_an_injury_crisis` |
| Risk factors (workload, fatigue, condition, physique) | `InjuryRulesTests.Risk_grows_with_workload_fatigue_age_and_history_and_falls_with_physique_and_medical_care` |

## 3. Implementation summary

* **Domain (`Ebm.Domain/Medical`):** `InjuryCatalog` (types, body parts, weights, recovery ranges, severity),
  `Injury` (medical record: phases, status, training restriction, assessment, recovery, aggravation), `InjuryRules`
  (risk multiplier, match/training chance, type choice with recurrence, recovery days and speed, physio estimate,
  return fitness; configurable, validated). `TrainingPlanner` handles injured players.
* **Services:** `IMedicalService` / `MedicalService` (medical day, physio hiring, medical room, player record).
  `CareerService` runs the medical day after the development day with every match of the day (`PlayedOnAsync`, which
  also fixes the user's match not tiring his players) and hires physios at career start; `GameDayReport.Medical`.
  `ClubManagementService.InjuryCover`: long-term injuries make an AI club short of players and are covered by signings.
* **Hosting:** `EbmOptions.Injuries` bound from `Ebm:Injuries`, validated, registered before the game services.
* **Persistence:** `InjuryConfiguration`, migration `20261117143312_Medical` (create-table only).
* **Desktop:** *Medical* tab (`MedicalViewModel`, `Themes/Medical.xaml`); injury news on the career screen.
* **Tests support:** `TestDatabase.CreateAsync(time, configure)` lets a test replace game-service registrations.

## 4. Files changed
### Created
- `src/Ebm.Domain/Medical/` (`InjuryCatalog.cs`, `Injury.cs`, `InjuryRules.cs`)
- `src/Ebm.Services/Medical/` (`IMedicalService.cs`, `MedicalService.cs`)
- `src/Ebm.Persistence/Configurations/MedicalConfigurations.cs`, `Migrations/20261117143312_Medical.cs` (+ designer)
- `src/Ebm.Desktop/ViewModels/Medical/MedicalViewModel.cs`, `Themes/Medical.xaml`
- `tests/Ebm.Tests.Unit/Medical/InjuryRulesTests.cs`, `tests/Ebm.Tests.Integration/Medical/MedicalServiceTests.cs`
- `docs/INJURIES.md`, `logs/STEP_22_LOG.md`

### Modified
- Domain: `TrainingPlanner`
- Services: `ServiceCollectionExtensions`, `CareerService`, `ICareerService`, `ClubManagementService`
- Hosting: `EbmOptions`, `EbmHost`; Persistence: model snapshot
- Desktop: `App.xaml`, `MainWindow.xaml`, `DatabaseEditorViewModel`, `CareerViewModel`
- Tests: `TestDatabase`, migration pins and table list, `CareerViewModelTests` (counts result lines only)
- Docs: `DATABASE_SCHEMA.md`, `AI_MANAGEMENT.md`, `DEVELOPMENT.md`; `CHANGELOG.md`, `PROJECT_STATE.md`, `Directory.Build.props`

### Removed
- None

## 5. Tests
- Build: Debug and Release, 0 warnings, 0 errors.
- Unit: 400/400 PASS (16 new).
- Integration, fast suite (`Category!=Launch&Category!=Slow`): 142/142 PASS in 1 min 35 s (7 new). A first run failed
  the training-injury test (chance below 1 for a strong player with a good physio); fixed by a certain risk in the test.
- Launch test: PASS.
- Slow suite: not run (no milestone release). The medical day loads every active player daily, so the European
  dataset tests will take somewhat longer.

## 6. Known limitations
See `docs/INJURIES.md` §8: post-match injury decision (no in-game injuries), no treatment choices or medical budget,
one physio counts, catalogue and formulas in code, editor edits the condition not the record, cover signings use the
emergency-signing rules, injuries not yet in morale or valuations. No injury history on the player profile screen
(the *Medical* tab shows the club's last year; `IMedicalService.GetInjuriesAsync` serves a player's full record).

## 7. Handoff instructions
Step 23 (finances): physio salaries are contracts like other staff (`MedicalService.PhysioSalary`); a medical budget
or facilities could scale `RecoverySpeed` and the physio factor in `RiskMultiplier`. The medical day is
`IMedicalService.ProcessDayAsync`, called by `CareerService.AdvanceDayAsync` after development.

## 8. Outstanding issues
- None blocking.
