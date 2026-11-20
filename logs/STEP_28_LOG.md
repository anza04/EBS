# STEP 28 — Coaching and staff management

## 1. Status
- Completed 2026-11-20. Version 0.28.0.
- Build: PASS (0 warnings, 0 errors). Tests: see §5.

## 2. Objectives
Head coaches, assistant coaches, scouts, medical staff, fitness coaches and player development staff; staff
attributes (tactical knowledge, player development, motivation, scouting, medical expertise, reputation, experience);
staff contracts, recruitment, dismissal, responsibilities, coaching styles and tactical preferences.

| Acceptance criterion | Where |
|---|---|
| Staff can be hired and dismissed | `IStaffService.HireAsync` / `DismissAsync`, *Staff* tab — `StaffServiceTests.The_market_is_supplied_and_staff_can_be_hired_and_dismissed`, `The_staff_screen_hires_and_dismisses`, `Staff_refuse_clubs_far_below_their_reputation` |
| Staff attributes influence relevant systems | Tactics/match (head coach knowledge), morale (motivation), training (development), fitness, medical, scouting (role ability) — `StaffRulesTests.Tactical_knowledge_wins_games_and_weak_coaches_prepare_no_game_plan`, `Coaching_styles_change_growth_and_motivation_changes_morale`, `StaffServiceTests.Development_coaches_raise_the_coaching_ability_of_training`; docs/STAFF.md §5 |
| AI clubs recruit staff | Market-first `StaffHiring.HireMissingAsync` for every role, head-coach replacement at season end — `Ai_clubs_recruit_from_the_market_and_replace_failing_head_coaches`, `The_managed_club_is_staffed_by_the_user_after_its_first_staff` |
| Coaching styles affect tactics and development | `TacticPlanner.Plan(squad, headCoach)`, `TrainingConditions.Style` — `The_head_coach_shapes_the_ai_tactic`, `Coaching_styles_change_growth_and_motivation_changes_morale` |

## 3. Implementation summary
- Domain (`Ebm.Domain.Staff`): `StaffProfile` (attributes, style, preferences), `StaffMember` attribute properties with
  derived `Ability` (role ability) and `AgeOn`; `StaffRole.DevelopmentCoach`; `CoachingStyle`; `StaffRules` (role
  ability, experience bonus, wanted/maximum per role, salaries and demands, reputation gap, AI salary cap, generation,
  style growth factor, morale effect, coach edge, game-plan/timeout/closing-review thresholds); `CoachProfile`.
- Match engine: `TeamSheet.Coach`; make-probability edge; `Prepare` only from knowledge 40; closing review at 2:00
  below 40; automatic timeout after a 6–0 run from 70. Neutral (50) when a club has no head coach, so engine
  statistics are unchanged.
- Development: `TrainingConditions.Style` multiplies growth; coaching ability = mean effective development of head,
  assistant and development coaches.
- Chemistry: weekly morale gets the head coach's `StaffRules.MoraleEffect`.
- AI: `TacticPlanner.Plan` takes the head coach (preferences when no category stands out, style settings);
  `ClubManagementService.PlanTacticsAsync` passes it.
- Services: `IStaffService`/`StaffService` (club staff + duties, market, hire, dismiss, market supply, head-coach
  review); `StaffHiring` rewritten (market-first AI recruitment for any role, never rehiring staff the club dismissed;
  managed club staffed once; market supply); `HireMissingCoachesAsync` now hires head, assistant, development and
  fitness coaches; career start supplies the market and hires staff before planning AI tactics; Mondays top up the
  market; next season dismisses failing AI head coaches and rehires.
- Persistence: Staff attribute columns; migration `20261120145953_StaffManagement` (rename `Ability` →
  `TacticalKnowledge`, add columns, copy the old ability into every attribute; no table rebuild).
- UI: *Staff* tab (`StaffViewModel`, `Themes/Staff.xaml`): staff with attributes and contracts, responsibilities with
  the ratings used, market by role with demand and willingness, hire (salary, years) and dismiss.

## 4. Files changed
- New: `src/Ebm.Domain/Staff/StaffRules.cs`, `src/Ebm.Services/Clubs/IStaffService.cs`, `StaffService.cs`,
  `src/Ebm.Desktop/ViewModels/Staff/StaffViewModel.cs`, `src/Ebm.Desktop/Themes/Staff.xaml`, migration
  `20261120145953_StaffManagement` (+ Designer), `docs/STAFF.md`, `tests/Ebm.Tests.Unit/Staff/StaffRulesTests.cs`,
  `tests/Ebm.Tests.Integration/Clubs/StaffServiceTests.cs`.
- Modified: `StaffMember.cs`, `StaffRole.cs`, `TacticPlanner.cs`, `PlayerDevelopment.cs`, `MatchSetup.cs`,
  `MatchSimulation.Play.cs`, `MatchSimulation.Situations.cs`, `StaffHiring.cs`, `DevelopmentService.cs`,
  `IDevelopmentService.cs`, `ScoutingService.cs`, `MedicalService.cs`, `ChemistryService.cs`, `MatchService.cs`,
  `ClubManagementService.cs`, `CareerService.cs`, `ServiceCollectionExtensions.cs`, `PeopleConfigurations.cs`,
  snapshot, `DatabaseEditorViewModel.cs`, `MainWindow.xaml`, `App.xaml`; tests (`DatabaseInitializerTests` + Step 28
  upgrade test, `ContractServiceTests`, `PlayerScreensRenderTests` helpers internal); docs (`DATABASE_SCHEMA.md`,
  `AI_MANAGEMENT.md` §1/§3, `DEVELOPMENT.md` §2.3/§4.1, `SCOUTING.md` §1, `TACTICS.md` §7.3), `CHANGELOG.md`,
  `PROJECT_STATE.md`, `Directory.Build.props`.

## 5. Tests
- Unit: PASS 459/459 (7 new in `StaffRulesTests`).
- Integration fast suite (`Category!=Launch&Category!=Slow`): PASS (7 new `StaffServiceTests` incl. the screen render,
  1 new upgrade test, latest-migration assertions updated). Launch: PASS.
- Slow suite: not run (not a milestone release).
- Measured: 300 equal games, home head coach knowledge 95 vs 10: home margin +2.7 vs −0.4.
- Manual UI: *Staff* screen checked from the off-screen render (`EBM_SCREENSHOT_DIR`); build and launch.

## 6. Known limitations
- No staff renewals, negotiation rounds or agents; attributes and reputation never change; staff do not retire.
- A club without a head coach plays as with an average one (50); responsibilities are fixed per role (no delegation).
- AI head coaches are judged only on the season's board objectives; the user's staff is never changed by the AI
  unless the assistant manages the club.
- Staff are generated (datasets carry none); names recombine player names.

## 7. Handoff instructions
- Step 29 (advanced simulation): `CoachProfile` is on `TeamSheet`; a coach's tactical knowledge could weight shot
  selection or matchup decisions. Keep `CoachProfile.Neutral` neutral so the statistical validation stays comparable.
- Monday staff work (market supply + six role hirings) adds a few queries per Monday; profile with the real-leagues
  dataset if game days slow down.

## 8. Outstanding issues
- Slow suite pending as before (`CareerSimulationTests`, `RealLeaguesCompetitionTests`, `RealLeaguesDatasetTests`).
