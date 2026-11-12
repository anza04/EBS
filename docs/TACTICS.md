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
| Defensive settings | `Intensity`, `BallPressure`, `HelpDefense` (levels 1–5), `Switching` (`Never`, `SameSize`, `Everything`), `PickAndRollCoverage` (`Drop`, `Hedge`, `Switch`, `Blitz`), `Rebounding` (`GetBack`, `Balanced`, `CrashBoards`) |
| Player assignments | per player: `Role`, `ShotPriority` (1–5), `MinutesTarget` (0–40 or none) |

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
`Hedge`/`Blitz` force turnovers but concede rolls, `Switch` creates mismatches); **Rebounding** `CrashBoards` adds
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

*Tactics* tab: club, saved tactics (new, use for the next match, delete), the form (name, systems, settings) and the
player-role grid (role, shot priority, minutes target, suggested role) of the club's first-team players. *Save tactic*
writes systems, settings and roles together.
