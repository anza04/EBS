# STEP 30 — Advanced match visualization

## 1. Status
- Completed 2026-11-20. Version 0.30.0.
- Build: PASS (Debug and Release, 0 warnings, 0 errors). Tests: see §5.

## 2. Objectives
2D basketball court: court rendering, player positions, ball movement, offensive spacing, defensive positioning, shot
animation, rebounds, substitutions, play-by-play synchronisation. The match engine stays the source of truth.

| Acceptance criterion | Where |
|---|---|
| Match events can be visualized | Every `MatchEvent` gets one `CourtFrame` (`Every_event_is_placed_and_line_ups_stay_in_sync_with_the_engine`); *Court* tab renders them (`PlayerScreensRenderTests`, `match-court.png`) |
| Player and ball positions are synchronized with simulation events | Line-ups equal `MatchSimulation.OnCourt` after every `Step()` (30 matches, short benches included); shooter on the recorded spot, ball shooter → rim, rebounder/thief hold the ball, substitutions swap players (`Players_and_ball_follow_the_events`, `Teams_attack_the_other_basket_after_half_time`) |
| The visualization can be paused and resumed | `CourtPlayback` Pause/Resume/SkipToEnd/Speed (`Playback_can_be_paused_resumed_and_skipped`); match centre pause holds watch mode, resume continues (`PlayerScreensRenderTests`) |
| Simulation results remain identical regardless of visualization mode | `Simulation_results_are_identical_with_or_without_the_visualization`: scores, possessions, every box score with/without tracker + playback (paused, ticking); same events and frames in commentary and live mode |

## 3. Implementation summary
- Domain: `MatchEvent.Shot` (init property) set on field-goal events from the existing `ShotRecord` — no new random
  draws. New `Matches/Visualization/CourtTracker.cs` (`CourtPoint`, `CourtAction`, `CourtPlayer`, `CourtFrame`,
  tracker: formations, matchups, contest distance, free-throw lanes, rebounds, steals, fouls, substitutions, fouled-out
  players, tip-off; ends switch at half time) and `CourtPlayback.cs` (queue, smoothstep tween, ball arc, pause/resume,
  skip, speed, `Version` for redraws).
- UI: `Controls/CourtView` (court lines, players with initials, holder/entering rings, ball and shadow, caption; ticks
  on `CompositionTarget.Rendering`, redraws only when something changes). Match centre: *Court* tab (index 7, opened on
  start), pause/resume, skip to latest, speed, *Watch the match* (`DispatcherTimer` advancing a possession when the court
  is idle and not paused); end-of-quarter/match skip the court.
- Architectural decision: visualization is a pure consumer of events in the domain layer (testable without WPF); no
  persisted data, so no migration and no save-file change.

## 4. Files changed
- New: `src/Ebm.Domain/Matches/Visualization/CourtTracker.cs`, `CourtPlayback.cs`, `src/Ebm.Desktop/Controls/CourtView.cs`,
  `tests/Ebm.Tests.Unit/Matches/MatchVisualizationTests.cs`, `docs/MATCH_VISUALIZATION.md`, this log.
- Modified: `MatchEvents.cs` (`Shot`), `MatchSimulation.Play.cs` (shot on emission), `MatchCenterViewModel.cs`,
  `Themes/Matches.xaml`, `tests/Ebm.Tests.Integration/Players/PlayerScreensRenderTests.cs`, `Directory.Build.props`
  (0.30.0), `docs/MATCH_UI.md`, `CHANGELOG.md`, `PROJECT_STATE.md`.
- Database: no changes.

## 5. Tests
- Unit: full unit run PASS (exit code 0; 467 Step 29 tests + 6 new `MatchVisualizationTests`, total not printed by the run).
- Integration: by user decision only `PlayerScreensRenderTests` and `MatchServiceTests` (PASS 4/4). Full fast suite and
  slow suite not run.
- Launch: PASS. Release build: PASS (0 warnings).
- Manual UI: court render reviewed (`EBM_SCREENSHOT_DIR`): FIBA lines, home attacking right in Q1, half-court
  formation, defenders between man and rim, shooter at the top of the key, ball in flight to the rim.

## 6. Known limitations
- Live matches only: stored reports have no play-by-play.
- Players move between event layouts; off-ball paths, passes (assists), screens and help rotations are not drawn.
- Offensive fouls: one frame drawn with the other team on offense.
- Watch mode and the render loop run on the UI thread; a very slow machine just shows fewer tween steps.

## 7. Handoff instructions
- Step 31 (news, media, reputation): build news from stored data and events (`MatchEvent`, `TacticalNote`, box scores,
  injuries, transfers); the court is not involved.
- Run the full integration fast suite (not run in Steps 29–30) and the slow suite before the next milestone release.

## 8. Outstanding issues
- Full integration fast suite not run on Step 29–30 code (user instruction); slow suite pending as before.
