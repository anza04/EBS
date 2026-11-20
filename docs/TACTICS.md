# Tactics

Step 09. Code: `src/Ebm.Domain/Tactics`, `src/Ebm.Services/Tactics`, screen *Tactics*
(`src/Ebm.Desktop/ViewModels/Tactical`, `Themes/Tactics.xaml`).

## 1. Model

A club keeps any number of saved **tactics** (`Tactic`, table `Tactics`), each with a unique name per club. At most one
is **selected**: the one the club plays its next match with. A club's first tactic is selected automatically;
deleting the selected tactic selects the remaining one first by name. A club without a selected tactic plays
`TeamTactics.Default` (motion offense, man-to-man, every setting normal, suggested roles).

A tactic holds:

| Part | Values |
|---|---|
| Offensive system | `MotionOffense`, `PickAndRoll`, `FiveOut`, `FourOutOneIn`, `PostOriented`, `Isolation`, `Transition` |
| Offensive settings | `Tempo`, `ShotSelection`, `BallMovement`, `Spacing`, `PickAndRollFrequency` (levels 1–5) and `Focus` (`Balanced`, `Inside`, `Perimeter`, `KeyPlayers`) |
| Defensive system | `ManToMan`, `Zone23`, `Zone32`, `Zone131`, `FullCourtPress`, `HalfCourtPress` |
| Defensive settings | `Intensity`, `BallPressure`, `HelpDefense` (levels 1–5), `Switching` (`Never`, `SameSize`, `Everything`), `PickAndRollCoverage` (`Drop`, `Hedge`, `Switch`, `Blitz`, `Ice`, `Under`), `Rebounding` (`GetBack`, `Balanced`, `CrashBoards`) |
| Game management (Step 27) | `SituationSettings`: matchup hunting, play calling, zone offense, end of quarter, foul management, rotation, four end-of-game switches, blowout rest, coach adjustments (§7) |
| Player assignments | per player: `Role`, `ShotPriority` (1–5), `MinutesTarget` (0–40 or none), `Matchup` (Step 27, §7.1) |

Levels (`TacticLevel`): 1 very low, 2 low, 3 normal, 4 high, 5 very high. `TacticLevel.Offset(level)` maps them to
−1 … +1 (normal = 0); the match engine scales every effect by this offset.

Assignments are current state stored as a JSON array in `Tactics.Assignments`. Only current club players can be
assigned when saving; assignments of players who later leave are kept in the row but dropped by
`ITacticsService.GetMatchTacticsAsync`. Players without an assignment get the suggested role (§4) at shot priority 3.

## 2. Offensive systems — what the engine should do

| System | Shot mix | Who gets the ball | Other |
|---|---|---|---|
| Motion offense | Balanced | Spread across the five | More assists, fewer turnovers with good passers |
| Pick and roll | More drives and rim shots, open threes from kick-outs | Ball handler and roll man | Pick-and-roll coverage of the defense matters most |
| Five-out | Most threes | Shooters | Pulls rim protectors away: better drives, fewer offensive rebounds |
| Four-out-one-in | Threes plus post touches | Shooters and the big man | |
| Post-oriented | Most post-ups and close shots | Bigs | More fouls drawn, more offensive rebounds, slower pace |
| Isolation | Mid-range and drives | Highest shot priority / best creator | Fewer assists, depends on one-on-one matchups |
| Transition | More early shots | Fast players | Highest pace, more turnovers, more fast-break points |

Settings: **Tempo** changes possessions per game and how early shots come; **Shot selection** trades shot quality for
more early/contested attempts; **Ball movement** raises assists and shot quality but costs a little time and adds
turnovers when passing is poor; **Spacing** raises three-point share and drive success; **Pick-and-roll frequency**
shifts plays to pick and roll; **Focus** biases shot types (inside / perimeter) or the share of the key players (by
shot priority).

## 3. Defensive systems — what the engine should do

| System | Strong against | Weak against | Other |
|---|---|---|---|
| Man-to-man | — (baseline) | Mismatches | Individual matchups count most |
| 2-3 zone | Drives, post-ups | Three-point shooting, offensive rebounds | Fewer fouls |
| 3-2 zone | Perimeter shooting | Post play, baseline drives | |
| 1-3-1 zone | Passing lanes (steals) | Corner threes, rebounding | |
| Full-court press | Forces turnovers, speeds the game up | Easy baskets when beaten; heavy fatigue | |
| Half-court press | Forces turnovers | Drives when beaten | More fatigue and fouls |

Settings: **Intensity** forces mistakes and lowers shooting but costs energy and adds fouls; **Ball pressure** adds
steals/turnovers and blow-bys; **Help defense** protects the rim but leaves shooters open; **Switching** limits
pick-and-roll advantages at the cost of size mismatches; **Pick-and-roll coverage** (`Drop` concedes mid-range,
`Hedge`/`Blitz` force turnovers but concede rolls, `Switch` creates mismatches, `Ice` protects the rim and concedes
mid-range, `Under` takes away the drive and concedes pull-up threes); **Rebounding** `CrashBoards` adds
defensive and offensive rebounds but concedes fast breaks, `GetBack` the reverse.

The exact formulas of the match engine (Step 10) are in `docs/MATCH_ENGINE.md` §2–§3.

## 4. Player roles

| Role | Fit (mean of attributes) | Engine use |
|---|---|---|
| Primary ball handler | ball handling, passing, court vision, decision making | Brings the ball up, most assists, pick-and-roll handler |
| Shot creator | mid-range, ball handling, three point, agility | Creates his own shot; isolation option |
| Spot-up shooter | three point ×2, mid-range, consistency | Catch-and-shoot threes |
| Slasher | layup, dunk, speed, ball handling | Drives to the rim |
| Post scorer | post moves ×2, close shot, strength | Post-ups |
| Roll man | dunk, close shot, vertical, strength | Finishes pick and rolls |
| Stretch big | three point ×2, mid-range, defensive rebound | Pick-and-pop, spaces the floor |
| Rim protector | block ×2, interior defense, vertical | Blocks, guards the paint |
| Perimeter stopper | perimeter defense ×2, steal, agility | Guards the best opposing scorer |
| Rebounder | defensive and offensive rebound, strength, vertical | Crashes the glass |

`TacticalRoles.Suggest(player)` picks, among the roles typical for the player's primary position
(`TacticalRoles.TypicalFor`), the best fit; the position's first role wins unless another fits more than 3 points
better. Roles are a preference, not a restriction: the engine combines role, shot priority, tendencies and attributes.

**Shot priority** 1–5 scales a player's share of the team's shots (3 = normal). **Minutes target** guides the
engine's rotation; none = the engine decides from the squad order and fatigue.

## 5. Access for the match engine

`ITacticsService.GetMatchTacticsAsync(clubId)` returns a `TeamTactics` record (immutable): systems, settings and the
assignments of current club players. `TeamTactics.AssignmentOf(player)` returns the stored assignment or the
suggestion. Changing tactics before a match = selecting or editing the club's tactic; during a match the live
session replaces the `TeamTactics` it holds (Step 10).

## 6. Screen

*Tactics* tab: club, saved tactics (new, use for the next match, delete), the form (name, systems, settings, game
management) and the player-role grid (role, shot priority, minutes target, defensive matchup "Guards", suggested role)
of the club's first-team players. *Save tactic* writes systems, settings, game management and roles together.

## 7. Advanced tactics (Step 27)

Code: `SituationSettings` and the new enums (`Ebm.Domain/Tactics`), engine in
`Ebm.Domain/Matches/Simulation/MatchSimulation.Situations.cs`; numbers in `docs/MATCH_ENGINE.md` §8. Stored in
`Tactics.Situation*` columns and in the assignment JSON (`Matchup`).

### 7.1 Options

| Option | Values (default first) | Effect in the engine |
|---|---|---|
| Defensive matchup (per player) | `Positional`, `BestScorer`, `BestShooter`, `BestPlaymaker`, `BestBig` | In man-based defenses (man-to-man and both presses) the player guards that opponent; everyone else takes the nearest free slot. A *perimeter stopper* left positional takes the best scorer (most points tonight; before that the main option) |
| Matchup hunting | `Off`, `WeakestDefender`, `SizeMismatch` | Against man-based defenses the hunted player (guarded by the weakest defender, or 6 cm+ taller than his man) gets ×1.5 shot share; posts up a smaller man more. Costs time and a few turnovers |
| Pick-and-roll coverage | + `Ice`, `Under` | See §3 |
| Switching | `SameSize`, `Never`, `Everything` | Now used: on a pick and roll the defenders of the ball handler and the screener switch (always with *Everything* or *Switch* coverage, within 7 cm with *SameSize*). A switched play loses the coverage edge; handler and screener attack the mismatch (bigger defender on the guard, guard on the big) |
| Play calling | `Flow`, `HotHand`, `SetPlays` | *Hot hand*: +15% shot share per made field goal (less half the misses), up to +150%. *Set plays*: slower possessions, better shots, fewer turnovers, more assists |
| Zone offense | `Standard`, `Overload`, `HighPost`, `AttackGaps` | Only against zones: more and better threes / mid-range and post / drives |
| End of quarter (Q1–Q3) | `Normal`, `TwoForOne`, `HoldForLastShot` | Shoot by ~30 s left to get the ball back, or run the clock down for the last shot |
| Foul management | `Normal`, `Protect`, `Ignore` | *Protect*: players in foul trouble defend carefully (fewer shooting fouls, softer defense) and sit at four fouls in the whole fourth quarter. *Ignore*: no one sits for fouls |
| Rotation | `Standard`, `Tight`, `Deep` | *Tight*: eight-man rotation, starters play through more fatigue. *Deep*: earlier rest, fresher players |
| Milk the clock when leading | on | Last two minutes of Q4/OT, leading: use the whole shot clock |
| Hurry when trailing | on | Last two minutes, trailing by 4+: shoot early; within 3 with the shot clock off: hold for the last shot (down 3: a three) |
| Foul when trailing | on | Last minute, trailing by 3–8 (by 1–2 in the last 15 s when the offense can run out the clock): foul the worst free-throw shooter on court |
| Foul when up three | off | Last 7 s, leading by 3 and in the bonus: foul before the tying three |
| Rest starters in blowouts | on | Last five minutes of a 20-point game: starters sit and stay out |
| Coach adjusts | off (AI plans: on) | The engine's coach adapts the tactic before and during the match (§7.3) |

Special situations: the first shot after the team's own timeout is a drawn-up play (+3% make). Situational plays are
commentary events (`MatchEventKind.Situation`) and are counted in the match's tactical notes.

### 7.2 Tactical notes (match reports)

Every match keeps `TacticalNote`s (`Matches.TacticalNotes`, JSON, at most 60): each side's game plan, every change
(the engine's coach and live changes by the user) with its reason, and at the end a summary of each side's situational
plays ("2 two-for-ones, 3 intentional fouls, …"). The match report shows them below the quarter scores.

### 7.3 The engine's coach

Runs only for sides that are automatic (not the user's side in live mode) with *Coach adjusts* on.

- **Game plan** (start of the match), from the opponent's starting five and first four reserves: 2-3 zone against poor
  shooters who score inside (mean three-point ≤ 42 and inside skill ≥ three-point + 6), man-to-man instead of a zone
  against good shooters (≥ 58); +1 ball pressure against a weak ball handler (handling/decision ≤ 45); matchup hunting
  against a weak man defender (≤ 45); a zone offense against a zone (shooters → overload, a skilled big → high post,
  else attack the gaps); the best perimeter (or, against a big, interior) defender (≥ 55) on the opponent's main option.
- **Reviews** at the start of every period after the first, after every automatic timeout and with four minutes left
  in Q4 and each overtime. Late (Q4/OT, ≤ 4 minutes): down 8+ → full-court press, tempo and intensity 5; down 4–7 →
  ball pressure 5, tempo 4+; up 8+ → tempo 1, presses back to man-to-man. Otherwise: 2-3 zone when the opponent scores
  60%+ of its field-goal points inside and shoots < 34% from three; back to man-to-man when it hits 40%+ of 7+ threes
  against the zone; +1 ball pressure when it turns the ball over on 20% of 15+ possessions; inside focus when the own
  team shoots ≤ 25% on 10+ threes. Any time: a stopper on a scorer with 10+ points and 30% of his team's, a zone
  offense against a zone.

Each change is applied at once (`TacticsChanged` event) and written to the tactical notes. The AI's saved tactic
(`TacticPlanner`) is unchanged by in-match adjustments.

The club's head coach (Step 28, docs/STAFF.md §5) sets how well this works: below tactical knowledge 40 there is no game
plan and the closing review comes with two minutes left; from 70 the automatic timeout stops a 6–0 run (else 8–0).
