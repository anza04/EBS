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
| `MatchEvent.Shot` / `MatchEvent.Offense` | The engine's `ShotRecord` of a field-goal event (spot, contest, block), and the new offensive system on a `TacticsChanged` event. Set at emission; no extra random draws. |
| `CourtTracker` (`Matches/Visualization`) | Turns each `MatchEvent` into one `CourtFrame`: players with positions, ball from/to, holder, ball in the air, and for shots, turnovers and fouls the possession leading to it (`Lead`, `Play`). Keeps both line-ups by slot from `Substitution`/`FouledOut` events and each side's offensive system (tip-off tactics, then `TacticsChanged`). `Update(events)` places only the events not placed yet, so it follows a live match incrementally. |
| `PossessionScript` (`CourtChoreography.cs`) | Scripts a possession as `CourtKey`s (§3.1). |
| `CourtPlayback` | Queue of frames played over real time: each frame's keys, then the event. Players follow smooth paths, the ball stays with its holder and flies on passes and shots; pass trails, current holder and screener for the view. Pause, resume, skip to latest, speed 0.25–8×. |
| `CourtView` (`Controls`) | `FrameworkElement` drawing court, players (home blue, away orange, initials), the ball handler (gold ring, surname tag), screens (a bar facing the ball), pass trails (dashed, fading), the ball with shadow and arc height, and under the court the play being run ("Pick-and-roll → Rossi · corner three") and the play-by-play line. Ticks the playback from `CompositionTarget.Rendering`. |
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

A foul right after a shot or free throw (on the rebound, intentional) is drawn where the players stand, with no lead-up.

### 3.1 Possessions

Before a field goal, turnover, shot-clock violation or foul the tracker scripts the possession as keyframes
(`CourtFrame.Lead`), ending exactly in the event's layout (the shooter on the recorded spot with the ball). Phases:

1. **Getting the ball** (when it is not already in the frontcourt): outlet pass to the guard after a defensive
   rebound, inbound under the basket after a score, from the sideline after other dead balls; then the guard brings it
   up while the others run to their spots. Fast-break shots (`ShotRecord.FastBreak`) push at once with the lanes filled.
2. **Set**: the offense in its system's formation (standard; five-out/isolation spread; four-out one-in; post).
3. **The play**, picked deterministically (hash of the event index) from candidates that fit the event, weighted by
   the offensive system: pick-and-roll, pick-and-pop, drive and kick, post-up, hand-off, off a screen, ball movement,
   isolation, backdoor cut, early offense, late clock; putbacks right after an offensive rebound have no lead-up.
   Assisted shots (`OtherPlayer` on a made field goal) end with the pass to the shooter; unassisted ones with his
   dribble into the spot. Defenders stay goal-side of their man unless the play moves them (help on a drive, hedge on
   a screen, trailing a cutter).
4. **The event** itself (shot, turnover, foul) as in the table above.

Timing follows the game clock: the lead-up lasts 0.5 s + 0.11 s per game second used (0.9–3.4 s at 1×), and no key is
shorter than its longest run at `CourtTracker.MaxRunSpeed` (9 m per playback second); the event's own segment is
stretched the same way (free-throw line-ups, tip-offs). `CourtPlayback` runs a possession at an even pace with a short
speed-up and slow-down, players on cubic paths through the keys (time-aware tangents capped at a sprint), so motion
never stops dead between keys or jumps (`Playback_moves_players_and_ball_smoothly`: < 0.3 m per 60 fps frame).

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
- Event durations at 1×: shot 1.4 s, free throw/tip-off 1.0 s, turnover 0.9 s, rebound/foul 0.8 s, substitution
  0.6 s, dead ball 0.5 s (longer when players have far to go), plus the possession's lead-up (§3.1). A possession
  shows in about 3–6 s.

## 6. Limitations

- Stored reports have no play-by-play, so the court is only available for matches played live in the session.
- Plays, screens and passes other than the assist are not in the events: the lead-up is a plausible illustration
  consistent with the event (shooter, assist, spot, clock, system), not a record of what the engine computed.
- An offensive foul is drawn with the fouling team's opponents on offense for that one frame.
