# STEP 27 — Advanced tactical engine

## 1. Status
- Completed 2026-11-18. Version 0.27.0.
- Build: PASS (0 warnings, 0 errors). Tests: see §5.

## 2. Objectives
Individual defensive assignments, matchup targeting, pick-and-roll coverage variations, offensive play calling,
special situations, end-of-quarter plays, end-of-game tactics, zone attack variations, defensive switching rules, foul
management, rotation strategies; situational tactical decisions for AI coaches.

| Acceptance criterion | Where |
|---|---|
| Tactical instructions have measurable effects | `TacticalEngineTests` (one test per instruction group; numbers in `docs/MATCH_ENGINE.md` §8.4) |
| Teams adapt to opponent strengths and weaknesses | Game plan (`Prepare`): zone vs poor shooters, stopper on the main option, pressure on weak handlers, hunting weak defenders, zone offense — `The_coach_adapts_his_game_plan_to_the_opponent` |
| AI coaches can adjust tactics | In-game reviews (`Review`): presses when trailing late, zone/man switches, stoppers, focus — `The_coach_presses_when_far_behind_late_and_the_notes_record_it`; AI plans enable it (`ManagementPlannerTests`) |
| Tactical decisions are observable in match reports | `TacticalNote`s on `MatchResult`, stored in `Matches.TacticalNotes`, shown in the report — `MatchServiceTests` (reopened report), engine tests |

## 3. Implementation summary
- Domain (`Ebm.Domain.Tactics`): `SituationSettings` (12 options), enums `DefensiveMatchup`, `MatchupTargeting`,
  `PlayCalling`, `ZoneOffense`, `EndOfQuarter`, `FoulManagement`, `RotationStrategy`; `PickAndRollCoverage.Ice/Under`;
  `TacticalAssignment.Matchup`; `Tactic.Situations`; `TeamTactics.Situations`, `WithMatchup`.
- Match engine: `MatchSimulation.Situations.cs` (matchup map, switches, hunting, hot hand, clock plans, intentional
  fouls, the engine's coach, notes); `Play.cs` (shot flow, mismatches, zone offenses, set plays, after-timeout plays,
  foul management, rotation, blowouts); `TacticalModel` tables; `TacticalNote` (`Ebm.Domain.Matches`),
  `Match.TacticalNotes`, `MatchResult.TacticalNotes`, `MatchEventKind.Situation`.
- AI: `TacticPlan.Situations` (coach adjusts, two-for-one, rotation by bench depth).
- Services: `TacticDraft.Situations`; `MatchService` stores notes; `MatchReport.TacticalNotes`.
- Persistence: owned `Situations` columns, `Matchup` in the assignment JSON, `Matches.TacticalNotes` JSON; migration
  `20261118133803_TacticalEngine` (defaults set by hand; existing "AI plan" tactics get coach adjustments).
- UI: *Tactics* screen game-management section and *Guards* column; match report lists the tactical notes.

## 4. Files changed
- New: `src/Ebm.Domain/Matches/Simulation/MatchSimulation.Situations.cs`, `src/Ebm.Domain/Matches/TacticalNote.cs`,
  migration `20261118133803_TacticalEngine` (+ Designer), `tests/Ebm.Tests.Unit/Matches/TacticalEngineTests.cs`.
- Modified: `TacticEnums.cs`, `TacticSettings.cs`, `Tactic.cs`, `TacticPlanner.cs`, `Match.cs`, `MatchEvents.cs`,
  `MatchSimulation.cs`, `MatchSimulation.Play.cs`, `MatchSimulation.Teams.cs`, `TacticalModel.cs`,
  `TacticConfigurations.cs`, `MatchConfigurations.cs`, snapshot, `ITacticsService.cs`, `TacticsService.cs`,
  `ClubManagementService.cs`, `IMatchService.cs`, `MatchService.cs`, `TacticsViewModel.cs`, `Themes/Tactics.xaml`,
  `MatchCenterViewModel.cs`; tests (`TacticsServiceTests`, `MatchServiceTests`, `ManagementPlannerTests`,
  `DatabaseInitializerTests`, `ContractServiceTests`); docs (`TACTICS.md` §7, `MATCH_ENGINE.md` §2.4/§7/§8,
  `AI_MANAGEMENT.md` §3, `DATABASE_SCHEMA.md`), `CHANGELOG.md`, `PROJECT_STATE.md`, `Directory.Build.props`.

## 5. Tests
- Unit: PASS 452/452 (12 new in `TacticalEngineTests`, planner test extended). Statistical validation still in range
  (table updated in `docs/MATCH_ENGINE.md` §7).
- Integration fast suite (`Category!=Launch&Category!=Slow`): PASS 153/153 (1 new, 1 extended, latest-migration
  assertions updated). Launch: PASS.
- Slow suite: not run (not a milestone release).
- Engine cost: 400 generated matches ≈ 1.1 s vs ≈ 0.98 s at Step 26 (Release, three runs each).
- Manual UI: the new *Tactics* controls and report lines verified by build and launch only.

## 6. Known limitations
- Switches only on pick and rolls (no off-ball screens or help rotations); hunting and mismatches only against
  man-based defenses.
- Intentional fouls are ordinary fouls (no unsportsmanlike rule); the coach reviews at fixed moments only.
- The coach's in-match changes are not saved to the club's tactic; the user's side is adjusted only if its tactic has
  *Coach adjusts* and the side is automatic.
- The tactics screen still shows enum names.

## 7. Handoff instructions
- Step 28 (coaching staff): the engine's coach (`Prepare`/`Review` in `MatchSimulation.Situations.cs`) is the natural
  hook for head-coach tactical knowledge (thresholds, how often he reviews) and coaching styles (`TacticPlanner`
  defaults, `SituationSettings`).
- If game days get slower on `data/real-leagues`, profile `Guards`/`Shot` first (allocations per shot).

## 8. Outstanding issues
- Slow suite pending as before (`CareerSimulationTests`, `RealLeaguesCompetitionTests`, `RealLeaguesDatasetTests`).
