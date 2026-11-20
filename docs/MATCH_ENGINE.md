# Match Engine

Step 10 (shot detail and advanced statistics: Step 29, §9–10). Code: `src/Ebm.Domain/Matches/Simulation` (pure, deterministic engine) and `src/Ebm.Services/Matches`
(`IMatchService`: squads, tactics, storage). Tactics are described in `docs/TACTICS.md`.

## 1. Rules (`MatchRules.Fiba`)

| Rule | Value |
|---|---|
| Periods | 4 × 10 minutes; overtimes of 5 minutes until the tie is broken |
| Shot clock | 24 s; 14 s after an offensive rebound or a non-shooting foul late in the clock |
| Team fouls | Bonus from the 5th team foul of a period: every defensive foul gives two free throws. Overtimes continue the 4th period's count |
| Personal fouls | Foul-out at 5; the player leaves at once and cannot return |
| Timeouts | 2 in the first half, 3 in the second (at most 2 in the last two minutes of Q4), 1 per overtime; unused timeouts are lost |
| Possession | Jump ball to start, then the alternating possession arrow at the start of each period |
| Squad | 5–12 players, five starters (one per position) |
| Game lost by default | A team with fewer than two players on court loses (FIBA art. 21): the score stands if the opponent leads, otherwise 2–0 |

Simplifications: substitutions and timeouts happen between possessions (always a dead ball in the model); no jump
balls after held balls; no technical, unsportsmanlike or intentional fouls; offensive fouls are turnovers with a
personal and team foul; shot-clock violations are team turnovers (not charged to a player).

## 2. Possession flow

`MatchSimulation.Step()` plays one possession (or starts/ends a period). Before each possession both sides may call an
automatic timeout and make substitutions (§5). Then, until the possession ends:

1. **Time**: half-court action lasts `N(mean, 4.5 s)` (minimum 2 s); second chances `N(6.5, 3)`, fast breaks
   `N(5, 1.5)`. `mean = 15.6 − 2.0·tempo + 1.0·ballMovement − 1.0·shotSelection + system + defense` (offsets −1…+1,
   clamped 8–21 s); system: transition −1.5, post +1, motion/isolation +0.5; defense: full press −1.5, half press −0.5,
   zones +0.75. An action longer than the shot clock is a **shot-clock violation**. At the end of the period the
   last action is cut to the remaining time and becomes a hurried shot.
2. **Turnover** (not on a last shot): probability `0.165 + defense system (press +0.035, half press +0.02, 1-3-1 +0.012,
   2-3/3-2 −0.005) + 0.02·ballPressure + 0.012·intensity + 0.012·ballMovement − 0.008·shotSelection (+0.01 transition)
   − 0.0012·(handler's (handling+decision)/2 − 50) + 0.0008·(defense's mean steal − 50) + 0.0004·(100 − handler energy)`
   (+pick-and-roll coverage: blitz +0.03, hedge +0.012), ×0.6 on a fast break, clamped 0.04–0.30. The player losing
   the ball is weighted by usage × (110 − handling). Steal share `0.48 + 0.1·ballPressure` (thief weighted by steal² ×
   gambling); `0.15 × (0.6 + 0.8·drive/100)` of the rest are offensive fouls. Causes and fast breaks after steals: §9.5–9.6.
3. **Non-shooting foul** (half court): `0.15 + 0.025·intensity + 0.02·ballPressure + press (0.03 / 0.02)`. In the bonus
   two free throws end the possession; otherwise play goes on with at least 14 s on the shot clock.
4. **Shot**: shooter, shot type, defender and make probability below.

### 2.2 Shooter

Weight = role usage (shot creator 1.35, slasher/post scorer 1.15, ball handler/spot-up 1.0, stretch big 0.9, roll man
0.8, stopper/rebounder 0.65, rim protector 0.6) × system boost (pick and roll: ball handler 1.25, roll man 1.5; post:
post scorer 1.5, bigs 1.3; five-out: shooters 1.25; four-out-one-in: post/roll 1.2; isolation: creator 1.4;
transition: slasher 1.25) × shot priority (0.45, 0.7, 1, 1.4, 1.9; to the power 1.6 with *Key players* focus) ×
(rating at his slot / 60)^2.5 (^3.5 in isolation) × (0.6 + 0.4·energy/100).

### 2.3 Shot type

Weight per type = scale × tendency^1.3 (three 1.0 × three-point tendency, mid-range 0.5, rim 0.95 × drive, post 0.45)
× role mix (spot-up: three 1.5, rim 0.6; stretch big: three 1.4, post 0.6; slasher: rim 1.4; post scorer: post 1.6;
roll man: rim 1.6, three 0.3; rim protector/rebounder: three 0.4, rim 1.2; creator: mid 1.4) × offensive system mix ×
defensive system mix (`TacticalModel.ShotMix`) × spacing (three 1+0.2·s, rim 1+0.08·s) × help defense (three 1+0.08·h,
rim 1−0.1·h) × pick-and-roll frequency (rim 1+0.1·f) × focus (inside: rim 1.25, post 1.3, three 0.75; perimeter:
three 1.3, mid 1.1, rim 0.85, post 0.7); halved for threes by players with three-point < 35. Fast breaks: 80% rim,
20% three; hurried last shots: 60% three.

### 2.4 Make probability

`p = base + attack·(skill − 50) − guard·(defense − 50)` with

| Type | Base | Attack | Guard | Shooter skill | Defense |
|---|---|---|---|---|---|
| Rim | 0.57 | 0.004 | 0.003 | 0.45 layup + 0.3 dunk + 0.15 close + 0.1 vertical | ½ defender interior + ½ best (0.6 block + 0.4 interior) on court |
| Post | 0.47 | 0.004 | 0.003 | 0.6 post + 0.25 close + 0.15 strength | 0.6 interior + 0.4 strength |
| Mid-range | 0.38 | 0.004 | 0.002 | mid-range | 0.7 perimeter + 0.3 agility (zones: mean of the five) |
| Three | 0.31 | 0.004 | 0.0015 | three point | as mid-range |

Then: + system pairing (`TacticalModel.MakeEdge`: 2-3 zone −0.03 inside / +0.015 three; 3-2 −0.02 three / +0.02
post; 1-3-1 +0.01 three; presses +0.03/+0.02 at the rim when beaten; five-out/motion vs zones +0.015, isolation vs
zones −0.02, isolation/post vs man +0.01) + 0.01·ballMovement − 0.015·shotSelection − 0.012·defensive intensity;
rim +0.01·spacing − 0.02·help, three +0.012·help; pick-and-roll coverage on pick-and-roll plays (drop: mid +0.03, rim
−0.02; hedge: rim +0.015; blitz: three +0.03, rim +0.02); − 0.0012·(100 − shooter energy) + 0.0006·(100 − defender
energy); + zone, contest and clutch edges (§9); + the shooter's form of the day `N(0, 0.01 + 0.03·(100 − consistency)/100)`; home +0.015; +0.05 per missing
defender; fast-break rim +0.10 (+0.12 before Step 29); hurried last shot −0.10. Clamped 0.05–0.90.

The **defender** is the player in the same slot (PG…C) against zones; in man-based defenses the matchup map of §8.1
(explicit matchups, the perimeter stopper on the best scorer, switches on pick and rolls).

**Shooting fouls**: rim 0.23 (fast break 0.27), post 0.20, mid 0.06, three 0.025, × (1 + 0.25·intensity). The fouler
is the defender (inside shots: 40% the best shot blocker); × contest and foul-drawing factors (§9.2, §9.4). The shot counts only when made (`0.5·p`, and-one: one free
throw); otherwise 2 or 3 free throws. **Free throws**: `0.53 + 0.0042·FT − 0.0006·(100 − energy) + form/2`, 0.30–0.95.

**Assists** on made shots: three 0.76, mid 0.45, rim 0.50, post 0.28, fast break 0.45; × (1 + 0.15·ballMovement)
× system (motion 1.08, pick and roll 1.05, isolation 0.7) × passers' mean (passing + vision)/2 / 60. Passer weighted
by ((passing + vision)/2)² × role (ball handler 2.2, creator 1.2) × (0.5 + pass-first/100).

**Blocks** on misses: inside `0.12 + 0.002·(best block − 50)` (0.11 before Step 29) × contest factor (§9.2), mid 0.025, three 0.01; blocker weighted by block² ×
height.

**Rebounds**: offensive chance `0.29 (+0.02 after a three; 0.14 after a missed free throw) + 0.004·(offense mean board
− defense mean board) + TacticalModel.OffensiveReboundEdge + 0.04 per player of difference on court`, clamped
0.08–0.45, where board = rebound attribute + 1.2·(height − 195). Edges: offense crash +0.04 / get back −0.05, defense
crash −0.03 / get back +0.03, post offense +0.03, five-out −0.04, transition −0.02, 2-3 zone +0.02, 1-3-1/full press
+0.03. Rebounder weighted by board² × role (rebounder 1.4, rim protector/roll man 1.15) × court position (§9.6). After a defensive rebound a
fast break follows with `0.11 + 0.06·tempo (+0.08 transition) ± 0.05 opponent crash/get back` + speed and long-rebound
edges (§9.6).

## 3. Tactics → engine

Every tactical setting is read through `TacticLevel.Offset` (−1…+1) as in §2; system tables live in
`TacticalModel` (shot mix, make edges, turnover edges, pace, defensive load, rebound edges, fast breaks, pick-and-roll
coverage). Tactics can be replaced during a live match (`ChangeTactics`); assignments (role, shot priority, minutes
target) are re-read for every player.

### Advanced tactics (Step 27)
See §8.

### Chemistry and morale (Step 26)
Make probability + 0.02 × `TeamSheet.Chemistry`, turnover chance − 0.008 × chemistry, player form + 0.01 × (morale − 50) / 50
(docs/CHEMISTRY.md §6).

## 4. Fatigue

Energy starts at the player's fitness (minimum 40) and is capped there. On court, per second:
`0.06 × (1 + 0.2·own tempo + 0.25·own defensive intensity + defensive load) × (1.5 − stamina/100)`; defensive load:
full press +0.35, half press +0.2, zones −0.1. On the bench +0.055 per second. Breaks: +8 between quarters, +25 at
half-time, timeouts +4 to the five on court. Effects: make probability −0.0012 per missing point (shooter), +0.0006 per
missing point of the defender, turnovers +0.0004, free throws −0.0006, usage ×(0.6 + 0.4·energy/100), and the coach's
substitutions.

## 5. Substitutions and timeouts

Forced (both sides, also when managed by the user): a fouled-out player or an empty slot is replaced by the best
bench player; with nobody left the team plays short.

Automatic coach (sides with `IsAutomatic`, default): a player needs rest when in foul trouble (Q1 2 fouls, Q2 3,
Q3 4; in Q4/OT 4 fouls with more than 5 minutes left), when he reached his minutes target, or when his energy is below
60 (starters) / 68 (others) — 6 lower with a minutes target of 32+, 15 lower in the last 4 minutes of a game within 10
points. He is replaced by the bench player (energy ≥ 70) with the best value at the slot: `rating at slot × (0.75 +
0.25·energy/100) − 0.8·rotation priority` (−15 past the minutes target, −10 in foul trouble) if that value is at least
80% of his (always below 55 energy). Otherwise a rested player (energy ≥ 88, 80 at a period start) with a better
rotation priority comes back when his value is within 2 points. Automatic timeout: after an 8–0 run, at most once
every three minutes.

## 6. Simulation modes and service

| Mode | Use |
|---|---|
| `Instant` | `RunToEnd()`; `IMatchService.PlayAsync` simulates and stores at once |
| `Commentary` | `Step()` possession by possession; every `MatchEvent` carries the clock, score and a commentary line; interventions refused |
| `Live` | As commentary, plus `SetAutomatic(side, false)` (the user manages the side), `Substitute`, `CallTimeout`, `ChangeTactics` between possessions |

`IMatchService`: `GetFixturesAsync`, `GetPreviewAsync` (matchday squads from the rosters via `TeamSheet.Pick`, tactics
via `ITacticsService`), `StartAsync(fixture, mode, seed)` → `MatchSession`, `SaveAsync` (result, standings and box
scores in one transaction), `PlayAsync` (instant), `GetReportAsync` (stored box score). The squad picks starters,
rotation (in order) and bench, leaves out injured, inactive and youth players (unless fewer than five remain) and fills
missing starting positions with the best available player, so clubs without squad roles can play.

The second leg of a two-legged cup tie sets `MatchSetup.OvertimeMargin` to the first-leg margin, so it goes to overtime
while the aggregate is level as well as when the score is level (docs/CUP_FORMATS.md §1).

The same setup and seed give the same match. Box scores store whole minutes (rounded); the engine keeps seconds.

## 7. Statistical validation

`MatchStatisticsValidationTests` (unit tests, run on every build) simulate generated teams (`SimulationTeams`: 12
players with position-typical attributes; quality 60 against 60). Results on 400 matches (per team and game):

| Statistic | Engine | Accepted range |
|---|---|---|
| Points | 81.7 | 72–88 |
| Possessions | 75.7 | 68–78 |
| 2P% / 3P% / FT% | 55.1% / 34.7% / 78.5% | 48–57 / 32–39 / 70–80 |
| Three-point share of field goals | 36.2% | 32–45% |
| Free-throw attempts | 22.0 | 16–26 |
| Rebounds (offensive) | 33.7 (9.8) | 32–40 (8–13) |
| Assists / turnovers / steals / blocks | 16.3 / 13.0 / 6.2 / 2.0 | 14–21 / 11–16 / 5–9 / 1.5–4.5 |
| Personal fouls | 20.7 | 17–24 |
| Games to overtime | 4.0% | 2–12% |
| Top scorer's share of team points | 25% | 18–35% |
| Starters' minutes | 28.6 | 24–33 |
| Home wins | 54% | 50–65% |

(Step 29 values; Step 28: 79.1 points, 74.0 possessions. Step 27 values: the default game management — clock management and intentional fouls late, blowout rest — adds
about one point and one free throw per game.)

Other checks: quality 70 vs 55 wins 96% (margin +26; 89%/+21 before the contest model of Step 29); tempo 1 + post offense 63.8 possessions / 63.4 points against
tempo 5 + transition 97.7 / 108.1; five-out three-point share 40.4% against 31.9% for a post offense; a full-court
press forces more turnovers; low fitness (45) costs more than 3 points of margin; 60 complete matches and 150
short-handed matches (5 players) never reach an invalid state (score = sum of points, 200 player-minutes per team and
25 per overtime, nobody above 5 fouls, nobody plays after fouling out).

## 8. Advanced tactics (Step 27)

Options and the engine's coach: `docs/TACTICS.md` §7. Code: `MatchSimulation.Situations.cs`, tables in `TacticalModel`.

### 8.1 Matchups and switches (man-to-man and presses)
For every shot the defense builds a matchup map: defenders with an explicit matchup (a perimeter stopper without one:
best scorer) take the free opponent with the highest value — best scorer `points + 8 × usage`, best shooter three-point,
best playmaker passing + ball handling, best big `post moves + close shot + 1.5 × (height − 195)` — then the others take
the nearest free slot. On a pick-and-roll shot (§2.4) the screener is the roll man, else the tallest player; the
handler's and the screener's defenders switch when the coverage is *Switch*, switching is *Everything*, or *SameSize*
with heights within 7 cm. A switched play gets no coverage edge; the handler and the screener shoot against each
other's defender.

Size mismatch on the shot type (shooter − defender height): ≥ 8 cm post ×2.2 (×2.8 when hunted); ≤ −8 cm post ×0.6,
rim ×1.1, mid-range and three ×1.15.

Matchup hunting (offense, against man-based defenses): the hunted player (lowest `(perimeter + interior defense) / 2` of
his defender, or the largest height advantage of 6 cm+) has shot weight ×1.5; possessions +0.8 s, turnovers +0.004.

### 8.2 Coverage, play calling, zone offense
| Item | Effect |
|---|---|
| Ice | rim −0.03, mid +0.015 on pick-and-roll plays; turnovers +0.005 (pick-and-roll offense) |
| Under | three +0.035, mid +0.01, rim −0.025 on pick-and-roll plays |
| Set plays | possession +1.5 s, half-court make +0.015, turnovers −0.008, assists ×1.05 |
| Hot hand | shot weight × (1 + 0.15 × clamp(made − missed/2, 0, 10)) |
| Overload (vs zone) | three weight ×1.15, make +0.012; turnovers +0.005 |
| High post (vs zone) | mid ×1.3 (+0.02), post ×1.1 (+0.01) |
| Attack gaps (vs zone) | rim ×1.2 (+0.015); turnovers +0.01 |
| After own timeout | first shot of the next possession +0.03 |

### 8.3 Clock and fouls
Half-court possessions (not fast breaks or second chances) can follow a clock plan:
- Q1–Q3: *two-for-one* with 30–44 s left → shoot after `clock − 30 − U(0, 4)` s (min 2), make −0.015; *hold for the
  last shot* with the shot clock off (≤ 24.5 s, > 4 s) → shoot at `clock − 0.5 − U(0, 2)` s.
- Q4/OT, last 120 s: leading and *milk the clock* → `shot clock − 0.5 − U(0, 1.5)` s (or the rest of the period);
  trailing within 3 with the shot clock off and *hurry* → shoot at `clock − 1.5 − U(0, 2)` s, a three when down 3;
  trailing by 4+ → `7 + U(0, 3)` s, make −0.015.

Intentional fouls (Q4/OT, before every action of a possession): the defense trailing by 3–8 with ≤ 60 s, or by 1–2 with
≤ 15 s and a shot clock longer than the game clock (*foul when trailing*), or leading by 3 with ≤ 7 s in the bonus
(*foul up three*), fouls after `1 + U(0, 2)` s: the fouled player is weighted by `(100 − free throw)²`, the fouler is the
defender with fewest fouls (bench players first). In the bonus: two free throws; otherwise play goes on with at least
14 s on the shot clock.

Foul management changes foul trouble (§5): *Ignore* never; *Protect* as normal in Q1–Q3 and at four fouls in the whole
fourth quarter and overtimes. A defender of a *Protect* team in foul trouble: shooting fouls ×0.7, make +0.02.
Rotation: rest thresholds −8 (*Tight*, bench players beyond rotation priority 3 are not used by choice) or +6 (*Deep*,
rested players return from 84 energy instead of 88). Blowout (Q4/OT, ≤ 300 s, margin ≥ 20, *rest starters*): starters
need rest, lose 30 value points and are not brought back.

### 8.4 Measured effects (`TacticalEngineTests`, 150 matches each, quality 60)
| Instruction | Effect |
|---|---|
| Elite defender on the opposing star (best scorer) | star 26.6 → 24.9 points |
| Hunting the weakest defender | hunted player's shot share 15.3% → 19.3% |
| Ice vs Under against a pick-and-roll offense | 3P% 34.8% vs 38.7% |
| Switch everything vs never, against a blitz | post shots 5.1 → 5.4, 3P% 37.6% → 35.9% |
| Set plays vs flow | possessions 148 → 137, turnovers 12.8 → 10.7 |
| Hot hand | top shooter's share 23.1% → 24.3% |
| Overload / attack gaps vs a 2-3 zone | three share 41.9% → 45.0% / 40.5% |
| Two-for-one vs hold for the last shot | possessions 151.0 vs 148.7 |
| Late clock management and intentional fouls | free throws in the last minute 1.8 → 4.4 |
| Tight / standard / deep rotation | starters' minutes 30.1 / 28.7 / 27.4; players used 8.6 / 10.4 / 10.4 |
| Foul management ignore / normal / protect | foul-outs 333 / 274 / 140 |
| Rest starters in blowouts (80 vs 45) | winners' starters 31.8 → 30.4 minutes |
| Coach adjusts, 50 vs 70 | late full-court press in 49 of 60 games |

Engine cost: about 10–20% more time per match than Step 26 (400 matches ≈ 1.1 s in Release).

## 9. Shot detail and player decisions (Step 29)

Code: `MatchSimulation.Shots.cs`; shot records and court geometry in `src/Ebm.Domain/Matches/ShotChart.cs`. Tests:
`AdvancedSimulationTests`. Every field-goal attempt counted in the box score (fouled misses are not attempts) is
recorded as a `ShotRecord`: period, clock, court spot, made, contest, assisted, fast break, clutch, blocked.

### 9.1 Shot location
FIBA half court in decimetres: x −75…75, y 0…140 from the baseline, rim at (0, 15.75). Three-pointers: |x| ≥ 66 below
y = 29.9 (corners), else distance ≥ 6.75 m. Zones (`ShotZones.Classify`): restricted area (≤ 1.25 m), paint
(|x| ≤ 2.4 m, y ≤ 5.8 m), mid-range left/centre (within 30° of the axis)/right, corner threes, wing threes, top of
the key (within 22.5°). Zone and value always come from the coordinates, so a chart cannot disagree with the points.

| Shot type | Zones |
|---|---|
| Rim | restricted area 85% (fast break 95%), paint (floaters) 15% |
| Post | restricted area 30%, paint 70% |
| Mid-range | left 35%, centre 30%, right 35% |
| Three | corners 0.11 each (spot-up/stretch big ×1.8, ball handler/creator ×0.6, five-out ×1.3, against a 2-3 zone ×1.3), wings 0.27 each, top 0.24 (ball handler/creator ×1.5) |
| Hurried last shot | top of the key (up to 14 m) or mid-range centre |

The spot is drawn uniformly inside the zone (threes within 7.8 m). Make edge (zero on average): rim at the restricted
area +0.02, floater −0.11; post at the rim +0.05, paint −0.02; corner three +0.025, wing −0.004, top −0.008 − 0.03 per
metre beyond 7.5 m; mid-range under 4 m +0.02, over 5.5 m −0.02.

### 9.2 Defensive contest
`s` = 0.02·(guard − 50) − 0.008·(create − 50) + zone defense (inside +0.25, jumpers −0.2) + help (inside +0.12, jumpers
−0.08)·level + 0.2·intensity − 0.2·ball movement + 0.15·shot selection + 0.012·(50 − decision making) +
0.003·(isolation − 50) − 0.006·(defender gambling − 50) + inside 0.03·(defender − shooter height, cm) − 0.35 secondary
break − 0.6 kick-out + 0.8 hurried − 0.8 per missing defender; fast breaks `s = −0.6`.
Guard: inside 0.6 interior + 0.2 positioning + 0.2 block, jumpers 0.6 perimeter + 0.2 positioning + 0.2 agility, ×
(0.7 + 0.3·energy/100); no defender 20. Create: inside 0.35 strength + 0.35 speed + 0.3 vertical, jumpers 0.4 ball
handling + 0.3 agility + 0.3 speed. `s + N(0, 1)` below −0.5 is **open**, above 0.55 **tight**, else contested.

| Contest | Make | Shooting fouls | Blocks |
|---|---|---|---|
| Open | +0.035 | ×0.6 | ×0.5 |
| Contested | 0 | ×1.0 | ×1.0 |
| Tight | −0.04 | ×1.35 | ×1.6 |

Shooting fouls are also × `clamp(1 − 0.006·(defender positioning − 50), 0.7, 1.3)` (disciplined defenders).

### 9.3 Player decisions and tendencies
- Shooter weight × isolation `0.85 + 0.3·isolation/100`; assist chance × `1.15 − 0.3·isolation/100`.
- **Passing up a tight shot** (not on fast breaks, last or forced shots): `0.10 + 0.006·(decision − 50) + 0.002·(pass
  first − 50) − 0.002·(isolation − 50) − 0.08·shot selection + 0.04·ball movement`, 0–0.45. Another player takes the
  same type of shot (kick-out: contest −0.6; the passer gets the assist on 85% of makes).
- Poor decision makers take more tight shots (contest term above).

### 9.4 Foul drawing
Shooting-foul chance × `clamp(1 + 0.012·(draw − 50), 0.6, 1.5)`; draw = inside 0.4 drive tendency + 0.3 strength +
0.3 ball handling, jumpers 0.5 ball handling + 0.5 isolation tendency.

### 9.5 Turnover causes (`TurnoverCause`, `TeamAnalytics.Turnovers`)
Pass risk `(110 − passing)·(0.5 + pass first/100)·(1 + 0.15·ball movement)`, dribble risk `(110 − handling)·(0.5 +
isolation/100)`. Steals: intercepted pass (bad pass) with probability pass/(pass + dribble), thief weighted by steal² ×
(0.5 + positioning/100); otherwise stripped (lost ball), steal² × (1 + gambling/100). Dead-ball turnovers: offensive foul
(above), else bad pass / travelling / out of bounds in proportion pass : 0.6·dribble : 0.5·dribble. Shot-clock
violations are team turnovers (not in the box score).

### 9.6 Transition and rebounding position
After a steal: fast break 0.55 (interception) or 0.40 (strip) + speed edge; after a defensive rebound: the §2 chance +
speed edge + 0.03 after a missed three (long rebound), −0.01 after a miss inside. Speed edge `0.004·(mean speed of the
running five − mean speed of the other five)`, ±0.1. Without a fast break, a **secondary break** follows with `0.16 +
0.08·tempo (+0.1 transition offense)` (0–0.5): possession time ×0.6, contest −0.35.
Rebounds: misses from three — guards (PG/SG slot) ×1.35, small forward ×1.1, bigs ×0.85; misses inside — guards ×0.8,
bigs ×1.15, the shooter ×1.3 on his own miss (put-back).

### 9.7 Clutch
Clutch time: fourth quarter or overtime, ≤ 5:00 left, margin ≤ 5. Clutch rating 0.45 concentration + 0.35 consistency +
0.2 leadership. Shooter weight × `(rating at slot/60)^1.5 × clamp(1 + 0.01·(clutch − 50), 0.6, 1.5)`; make + 0.0015 ×
(clutch − 50).

### 9.8 Measured (`AdvancedSimulationTests`, quality 60, 200 matches unless stated)
| Check | Result |
|---|---|
| Open / contested / tight shots (FG%) | 40.6% (52.3%) / 38.9% (46.7%) / 20.5% (40.7%) |
| Restricted area share of twos (FG%), paint, mid-range | 51.8% (64.2%), 50.9%, 41.4% |
| Corner share of threes (3P%) vs above the break | 22.9% (37.4%) vs 34.1% |
| Assisted makes, blocked shots, clutch shots | 57.4%, 3.3%, 4.3% |
| Per team: fast breaks (points), secondary breaks, second-chance points, passed-up shots | 5.8 (7.9), 4.0, 8.7, 2.7 |
| Turnover causes per team | bad pass 5.9, lost ball 3.1, offensive foul 1.2, travelling 1.5, out of bounds 1.3, shot clock 2.5 |
| Tight contests, weak (30) vs elite (85) defenders | 10.1% vs 29.6% |
| Isolation scorer vs pass-first player (same ratings): shot share, assisted makes | 18.8% / 54.8% vs 14.1% / 74.8% |
| Poor (15) vs good (95) decisions: tight shots | 41.3% vs 9.9% |
| Slasher vs jump shooter: free-throw attempts | 3.2 vs 2.2 |
| Clutch FG% calm (95) vs nervous (15), 300 matches | 49.4% vs 44.8% (other shots 46.2% vs 47.5%) |
| Fast breaks, speed 90 vs 30 | 8.8 vs 2.7 |
| Guards' share of defensive rebounds vs five-out / post offense | 11.7% / 10.4% |

## 10. Advanced statistics (Step 29)

`AdvancedStatistics` (pure, `src/Ebm.Domain/Matches/AdvancedStatistics.cs`), over box scores; rates are fractions.

| Statistic | Formula |
|---|---|
| True shooting | PTS / (2 × (FGA + 0.44 × FTA)) |
| Effective FG% | (FGM + 0.5 × 3PM) / FGA |
| Usage | (FGA + 0.44 FTA + TOV) × (team min / 5) / (min × (team FGA + 0.44 team FTA + team TOV)) |
| Assist % | AST / ((min / (team min / 5)) × team FGM − FGM) |
| Rebound % (off./def./total) | REB × (team min / 5) / (min × (team REB + opponent REB of the other kind / total)) |
| Turnover % | TOV / (FGA + 0.44 × FTA + TOV) |
| Offensive / defensive rating (player) | 100 × on-court points for / against ÷ on-court offensive / defensive possessions |
| Net rating | offensive − defensive rating |
| Plus/minus | on-court points for − against |
| Team possessions | mean of both teams' FGA − ORB + TOV + 0.44 × FTA (so one team's ORtg is the other's DRtg); pace per 40 minutes |

The engine records on-court points and possessions, primary-defender attempts (`DefendedAttempted/Made`, individual
matchups) and the shots in every box score (docs/DATABASE_SCHEMA.md). Checked on 80 matches: Σ points for = 5 × team
points, Σ plus/minus = 5 × margin, Σ possessions on court = 5 × possessions, defended attempts = opponent attempts,
home ORtg = away DRtg, minute-weighted usage and rebound shares add up, turnover causes = box-score turnovers.
Stored reports show box-score analytics only; turnover causes, fast-break and second-chance points come from the
engine (live or just-played matches). Season figures (*Season stats*) sum the stored lines: TS%, eFG%, TOV%, +/−,
on-court ratings (rows from Step 29 on) and the season shot chart.
