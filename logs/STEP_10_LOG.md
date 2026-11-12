# STEP 10 — Basketball match simulation engine

**Date:** 2026-11-12
**Version:** 0.10.0
**Status:** Completed. All acceptance criteria met.

## 1. Objectives

From `docs/ROADMAP.md`, Step 10: a complete possession-based simulation with FIBA rules (quarters, overtime, shot
clock, two/three-point shots, free throws, fouls and bonus, rebounds, assists, steals, blocks, turnovers,
substitutions, timeouts, fatigue); each possession considering the offensive and defensive systems, the players,
attributes, matchups, tactical advantages, fatigue and random variation; three simulation modes; large-scale
statistical validation.

| Criterion | Proof |
|---|---|
| Complete matches can be simulated | `MatchSimulationTests.Complete_matches_follow_the_rules_and_never_reach_an_invalid_state`; `MatchServiceTests` (instant and live) |
| Scores follow basketball rules | Same test: no draws, score = sum of points, team fouls/bonus, foul-outs at 5, clock never runs backwards |
| Individual statistics are generated | `PlayerLine` / box scores validated per player; stored and reopened in `MatchServiceTests` |
| Team statistics are generated | `TeamStatistics`, `MatchReport.HomeStatistics` (service test), validation averages |
| Fatigue affects performance | `Fatigue_drains_energy_and_tired_teams_perform_worse` |
| Tactical settings influence match outcomes | `MatchStatisticsValidationTests.Tactical_settings_change_the_game` (pace, shot mix, turnovers) |
| Overtime works | `Overtime_is_played_until_the_tie_is_broken` |
| No invalid match states occur | Invariant checks on 60 full matches, 150 short-handed matches and the live test |
| Statistical validation | `Average_matches_produce_plausible_basketball_distributions`, `Better_teams_win_most_games_without_winning_all`; results in `docs/MATCH_ENGINE.md` §7 |

## 2. Files

### Created
| File | Purpose |
|---|---|
| `src/Ebm.Domain/Matches/Simulation/MatchSetup.cs` | `TeamSide`, `SimulationMode`, `MatchRules`, `SheetPlayer`, `TeamSheet` (+ `Pick`), `MatchSetup` |
| `src/Ebm.Domain/Matches/Simulation/MatchEvents.cs` | `MatchEvent`, `PlayerLine`, `TeamStatistics`, `MatchResult` |
| `src/Ebm.Domain/Matches/Simulation/TacticalModel.cs` | Tactical effect tables, `ShotType` |
| `src/Ebm.Domain/Matches/Simulation/MatchSimulation.cs`, `.Teams.cs`, `.Play.cs` | The engine |
| `src/Ebm.Services/Matches/IMatchService.cs`, `MatchService.cs` | Fixtures, preview, sessions, storage, reports |
| `docs/MATCH_ENGINE.md` | Handoff documentation |
| `tests/Ebm.Tests.Unit/Matches/SimulationTeams.cs`, `MatchSimulationTests.cs`, `MatchStatisticsValidationTests.cs` | Unit tests and generator |
| `tests/Ebm.Tests.Integration/Matches/MatchServiceTests.cs` | Service tests |

### Modified
`CompetitionService` (`AddResultAsync` extracted), Services `ServiceCollectionExtensions`, `Directory.Build.props`
(0.10.0), `README.md`, `CHANGELOG.md`, `PROJECT_STATE.md`, `docs/TACTICS.md`.

### Removed
None.

## 3. Features implemented

See `CHANGELOG.md` 0.10.0 and `docs/MATCH_ENGINE.md`: possession flow (§2), tactics (§3), fatigue (§4), automatic
coach (§5), modes and service (§6), validation (§7).

## 4. Architectural decisions

Decision 39 in `PROJECT_STATE.md`. Also:
* The engine is one class with partial files (API, team state, possession flow) and no dependency outside the domain;
  randomness comes from one seeded `Random`, so a match is reproducible.
* Interventions are allowed between possessions only, which is always a dead ball in the model.
* `MatchSession` is in memory; nothing is stored until the match is over and saved (abandoning a live match leaves the
  fixture unplayed).
* Player statistics are stored for players with playing time; DNP players have no row.

## 5. Database changes

None. Results use `Matches`, `LeagueStandings` and `PlayerMatchStatistics` (schema version `20261112140336_Tactics`).

## 6. Tests

| Run | Result |
|---|---|
| `dotnet build` (Debug, Release) | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including launch) | PASS 379/379 (273 unit, 106 integration) |

## 7. Known limitations

* Substitutions/timeouts only between possessions; no technical, unsportsmanlike or intentional fouls, no held balls,
  no end-of-game fouling or clock management (Step 27 *Advanced tactical engine*).
* Matches do not change fitness, morale or injuries yet (Steps 12, 22); experience is not awarded.
* AI clubs play with their selected tactic or the default; no AI tactical adjustments beyond timeouts and rotation.
* Generated validation teams are synthetic; retune with real data when the database grows.

## 8. Outstanding issues

None.

## 9. Instructions for the next step

1. Read `PROJECT_STATE.md`, this log and `docs/MATCH_ENGINE.md` §6.
2. Build the match UI on `IMatchService`: `GetFixturesAsync` for the list, `GetPreviewAsync` for the preview,
   `StartAsync(..., SimulationMode.Live)` and the `MatchSimulation` API (`Step`, `RunToEndOfPeriod`, `RunToEnd`,
   `Substitute`, `CallTimeout`, `ChangeTactics`, `SetAutomatic`, `Lines`, `OnCourt`, `Events`) for live matches,
   `SaveAsync` at the end and `GetReportAsync` for historical box scores.
3. Keep UI logic out of the engine; the engine's events already carry clock, score and commentary text.
