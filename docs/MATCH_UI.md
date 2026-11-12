# Match Centre

Step 11. Code: `src/Ebm.Desktop/ViewModels/Matches/MatchCenterViewModel.cs`, `Themes/Matches.xaml`; tab *Matches*.
Everything runs through `IMatchService` (`docs/MATCH_ENGINE.md` §6); the screen keeps no game logic.

## 1. Layout

| Area | Content |
|---|---|
| Top bar | Competition season (`GetCompetitionSeasonsAsync`), *Create schedule* (`ICompetitionService.GenerateScheduleAsync`), title |
| Left | Schedule grid (date, round — playoffs as `PO round-game` —, home, away, result); *You manage* (None / Home / Away); *Start match*, *Play instantly* |
| Centre | Scoreboard (teams, score, period and game clock, team fouls and timeouts left) and five views: *Preview*, *Play-by-play*, *Box score*, *Team stats*, *Report* |
| Right (live only) | Game controls; for a managed side: substitution, timeout and tactics |

## 2. Flow

1. **Preview** (unplayed fixture): both matchday squads as `TeamSheet.Pick` builds them from the rosters (starters
   with their lineup position, then the bench in rotation order, overall rating) and each club's selected tactic. A
   club with fewer than five available players cannot play; the message says so.
2. **Play instantly**: `PlayAsync` simulates and stores the match; the stored report opens.
3. **Start match**: `StartAsync` in `Live` mode when a side is managed (that side's automatic coach is switched off:
   only forced substitutions happen without the user), otherwise `Commentary` mode (the engine coaches both sides).
   Controls: *Next possession*, *To the end of the quarter*, *To the end of the match*. After every step the scoreboard,
   play-by-play (newest first: time, score, team, commentary line), live box scores (● = on court, energy, plus-minus,
   fouled-out marker), team statistics and the report (leaders, quarter scores) are rebuilt from the simulation.
4. **In-game controls** (managed side, between possessions): *Substitute* (out: on-court players with energy and
   fouls; in: bench players not fouled out), *Timeout* (refused when none are left in the period/half), *Apply tactics*
   (offensive and defensive system, tempo, defensive intensity; the rest of the tactic is kept). Each control writes a
   play-by-play line and takes effect from the next possession.
5. **End**: the result, standings and box scores are saved as soon as the match ends (`SaveAsync`); the schedule
   shows the result and the final live view (with quarter scores) stays on screen.
6. **Historical box scores**: selecting a played fixture opens its stored report (`GetReportAsync`): score, box
   scores of both teams (minutes, points, FG, 3P, FT, rebounds, assists, steals, blocks, turnovers, fouls), team
   statistics and leaders. Play-by-play, energy and plus-minus are not stored, so they show "—" for past matches.

While a match is in progress the schedule is locked; leaving the tab does not stop it, and an unfinished match is not
saved (the fixture stays unplayed).
