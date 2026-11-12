# STEP 11 — Match presentation and statistics

**Date:** 2026-11-12
**Version:** 0.11.0
**Status:** Completed. All acceptance criteria met.

## 1. Objectives

From `docs/ROADMAP.md`, Step 11: match preview, starting lineups, bench, live score, quarter clock, play-by-play,
substitution and timeout controls, tactical adjustments, box score, player and team statistics, match report.

| Criterion | Proof |
|---|---|
| Matches can be followed from beginning to end | Render test: start, possessions, end of quarter, end of match through the *Matches* screen |
| Live statistics update correctly | Render test: live box scores and team stats after each step; final box-score points equal the score |
| Tactical changes affect the ongoing match | *Apply tactics* → `MatchSimulation.ChangeTactics` (play-by-play line, render test); effect on the engine proven by `MatchStatisticsValidationTests.Tactical_settings_change_the_game` |
| Match results are saved | Automatic save at the end (render test: schedule shows the result); `MatchServiceTests` |
| Historical box scores can be reopened | Render test reopens the played fixture after viewing another; `MatchServiceTests` (`GetReportAsync`) |

## 2. Files

### Created
| File | Purpose |
|---|---|
| `src/Ebm.Desktop/ViewModels/Matches/MatchCenterViewModel.cs` | Match centre view model and row records |
| `src/Ebm.Desktop/Themes/Matches.xaml` | Its template |
| `docs/MATCH_UI.md` | Handoff documentation |
| `logs/STEP_11_LOG.md` | This log |

### Modified
`IMatchService` / `MatchService` (`GetCompetitionSeasonsAsync`, `CompetitionSeason`), `DatabaseEditorViewModel`,
`MainWindow.xaml`, `App.xaml`, `Directory.Build.props` (0.11.0), `README.md`, `CHANGELOG.md`, `PROJECT_STATE.md`;
tests `PlayerScreensRenderTests` (match centre flow, screenshot), `DatabaseEditorTests` (constructor),
`MatchServiceTests`.

### Removed
None.

## 3. Features implemented

See `docs/MATCH_UI.md`: schedule, preview, instant play, live/commentary matches, scoreboard, play-by-play, live and
stored box scores, team statistics, report with leaders and quarter scores, substitution/timeout/tactics controls,
automatic saving, reopening past matches.

## 4. Architectural decisions

Decision 40 in `PROJECT_STATE.md`. Also:
* The live view is rebuilt from the simulation after every step (no incremental UI state), so it can never disagree
  with the engine.
* Only box scores are stored; play-by-play stays in memory (no schema change). Storing events can come with the
  career mode if needed.

## 5. Database changes

None (schema `20261112140336_Tactics`).

## 6. Tests

| Run | Result |
|---|---|
| `dotnet build` (Debug, Release) | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including launch) | PASS 379/379 (273 unit, 106 integration) |

Screenshot of the match report reviewed (`EBM_SCREENSHOT_DIR`).

## 7. Known limitations

* Past matches have no play-by-play, energy or plus-minus (not stored).
* An unfinished live match is lost when the application closes; the fixture stays unplayed.
* The sample dataset gives full squads to Aurora Milano only: other clubs need at least five players to play.
* Live tactical changes cover systems, tempo and intensity; other settings and roles are changed on the *Tactics*
  screen before the match.
* Enum names in combo boxes (`PickAndRoll`, `Zone23`); off-screen renders collapse star-width grid columns (known).

## 8. Outstanding issues

None.

## 9. Instructions for the next step

1. Read `PROJECT_STATE.md`, this log, `docs/MATCH_ENGINE.md` and `docs/MATCH_UI.md`, then the Step 12 specification.
2. The career calendar should play AI fixtures with `IMatchService.PlayAsync` and the user's fixtures through the
   match centre (or instantly), call `ITransferService.ProcessTransferDatesAsync` every game day, and advance
   competitions with `ICompetitionService.AdvanceAsync`.
3. AI clubs need lineups and tactics (`IClubService.SaveRosterAsync`, `ITacticsService.SaveAsync`) and squads of at
   least five available players.
