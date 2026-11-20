# Match visualization (Step 30)

The match centre's **Court** tab draws a live match on a 2D FIBA court: ten players, the ball, shots, rebounds,
turnovers, fouls, free throws and substitutions, synchronised with the play-by-play. The match engine stays the only
source of truth: the visualization reads its events and never decides anything.

## 1. Architecture

```
MatchSimulation ──Events──▶ CourtTracker ──CourtFrame[]──▶ CourtPlayback ──▶ CourtView (WPF)
 (Ebm.Domain)               (Ebm.Domain, pure)            (Ebm.Domain, pure)   (Ebm.Desktop, render only)
```

| Piece | Role |
|---|---|
| `MatchEvent.Shot` | The engine's `ShotRecord` of a field-goal event (spot, contest, block). Set at emission; no extra random draws. |
| `CourtTracker` (`Matches/Visualization`) | Turns each `MatchEvent` into one `CourtFrame`: players with positions, ball from/to, holder, ball in the air. Keeps both line-ups by slot from `Substitution`/`FouledOut` events. `Update(events)` places only the events not placed yet, so it follows a live match incrementally. |
| `CourtPlayback` | Queue of frames played over real time: each frame tweens (smoothstep) from the previous layout. Pause, resume, skip to latest, speed 0.25–8×. |
| `CourtView` (`Controls`) | `FrameworkElement` drawing court, players (home blue, away orange, initials), ball with shadow and arc height, and the current play-by-play line. Ticks the playback from `CompositionTarget.Rendering`. |
| `MatchCenterViewModel` | Owns tracker and playback for the live session; enqueues new frames after every advance; *Watch the match* mode. |

## 2. Court model

- Full court in metres: `X` 0–28 from the left baseline, `Y` 0–15 across; rims 1.575 m from the baselines.
- Home attacks the right basket in periods 1–2 and the left one from period 3 (overtimes keep the second-half ends).
- A recorded shot (`ShotRecord`, decimetres on the attacked half court) maps with `CourtTracker.ShotSpot`; the
  distance to the rim on the court equals `ShotRecord.Distance`.

## 3. Event synchronisation

| Event | Frame |
|---|---|
| `PeriodStart`, `JumpBall` | Tip-off: both teams in their own half around the centre circle; the ball goes to the winner's guard. |
| `FieldGoalMade/Missed` | Offense in its half-court formation (PG top, SG wing, SF corner, PF short corner, C low post), each defender between his man and the rim; shooter on the recorded spot; his defender (or the blocker) at the contest distance (open 2.8 m, contested 1.2 m, tight 0.6 m, block 0.4 m); ball flies shooter → rim. |
| `FreeThrowMade/Missed` | Shooter at the line, defenders on the lane spots nearest the basket, attackers on the next ones; ball shooter → rim. |
| `Offensive/DefensiveRebound` | The rebounder near the rim that was attacked; the ball goes to him; the rebounding side has the ball next. |
| `Turnover`, `ShotClockViolation` | Ball handler loses it; on a steal the thief jumps the lane and takes it. |
| `Foul` | Fouler next to the fouled player, who holds the ball. |
| `Substitution` | The entering player takes the slot (and spot) of the one leaving, highlighted green. |
| `FouledOut` | The player leaves the court; the following substitution fills the slot. |
| Others (timeout, tactics, situation, period end, final) | Dead ball: previous layout. |

Positions are a deterministic function of the events (a hash-based ±0.5 m jitter keeps formations from looking
frozen), so the same match always draws the same way. `MatchVisualizationTests` checks that after every `Step()` the
tracker's line-ups equal `MatchSimulation.OnCourt`, that every event has its frame, and that the shooter, rebounder,
thief and ball sit where the events say.

## 4. Independence from the result

The tracker and playback only read events. The match plays identically with or without them and in commentary or live
mode (`Simulation_results_are_identical_with_or_without_the_visualization`: scores, possessions and every box score).
Pausing the court never pauses the engine; in *Watch the match* mode the view model simply waits to call `Step()` until
the court has shown the last possession (and not while paused). *To the end of the quarter/match* skip the court to the
latest frame.

## 5. Performance

- Placing an event is O(players) over a few lists of at most ten entries; the tracker runs incrementally after each
  possession (not timed separately: the six `MatchVisualizationTests`, which place 40+ full matches, run in < 1 s).
- Rendering uses frozen brushes/pens and immediate-mode `DrawingContext`; it redraws only while a tween runs or when the
  playback changes (`CourtPlayback.Version`), so an idle court costs nothing.
- Frame durations at 1×: shot 1.4 s, free throw/tip-off 1.0 s, turnover 0.9 s, rebound/foul 0.8 s, substitution
  0.6 s, dead ball 0.5 s. A possession shows in about 2–4 s.

## 6. Limitations

- Stored reports have no play-by-play, so the court is only available for matches played live in the session.
- Off-ball movement, screens and passes are not in the events: players move between event layouts, not along real
  play paths; assists are not drawn as passes.
- An offensive foul is drawn with the fouling team's opponents on offense for that one frame.
