# Match Engine

Step 10. Code: `src/Ebm.Domain/Matches/Simulation` (pure, deterministic engine) and `src/Ebm.Services/Matches`
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
   gambling); 15% of the rest are offensive fouls. After a steal: 45% fast break.
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
energy); + the shooter's form of the day `N(0, 0.01 + 0.03·(100 − consistency)/100)`; home +0.015; +0.05 per missing
defender; fast-break rim +0.12; hurried last shot −0.10. Clamped 0.05–0.90.

The **defender** is the player in the same slot (PG…C); in man-to-man a *perimeter stopper* guards the opponent's
main scorer on non-post shots.

**Shooting fouls**: rim 0.23 (fast break 0.27), post 0.20, mid 0.06, three 0.025, × (1 + 0.25·intensity). The fouler
is the defender (inside shots: 40% the best shot blocker). The shot counts only when made (`0.5·p`, and-one: one free
throw); otherwise 2 or 3 free throws. **Free throws**: `0.53 + 0.0042·FT − 0.0006·(100 − energy) + form/2`, 0.30–0.95.

**Assists** on made shots: three 0.76, mid 0.45, rim 0.50, post 0.28, fast break 0.45; × (1 + 0.15·ballMovement)
× system (motion 1.08, pick and roll 1.05, isolation 0.7) × passers' mean (passing + vision)/2 / 60. Passer weighted
by ((passing + vision)/2)² × role (ball handler 2.2, creator 1.2) × (0.5 + pass-first/100).

**Blocks** on misses: inside `0.11 + 0.002·(best block − 50)`, mid 0.025, three 0.01; blocker weighted by block² ×
height.

**Rebounds**: offensive chance `0.29 (+0.02 after a three; 0.14 after a missed free throw) + 0.004·(offense mean board
− defense mean board) + TacticalModel.OffensiveReboundEdge + 0.04 per player of difference on court`, clamped
0.08–0.45, where board = rebound attribute + 1.2·(height − 195). Edges: offense crash +0.04 / get back −0.05, defense
crash −0.03 / get back +0.03, post offense +0.03, five-out −0.04, transition −0.02, 2-3 zone +0.02, 1-3-1/full press
+0.03. Rebounder weighted by board² × role (rebounder 1.4, rim protector/roll man 1.15). After a defensive rebound a
fast break follows with `0.11 + 0.06·tempo (+0.08 transition) ± 0.05 opponent crash/get back`.

## 3. Tactics → engine

Every tactical setting is read through `TacticLevel.Offset` (−1…+1) as in §2; system tables live in
`TacticalModel` (shot mix, make edges, turnover edges, pace, defensive load, rebound edges, fast breaks, pick-and-roll
coverage). Tactics can be replaced during a live match (`ChangeTactics`); assignments (role, shot priority, minutes
target) are re-read for every player.

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

The same setup and seed give the same match. Box scores store whole minutes (rounded); the engine keeps seconds.

## 7. Statistical validation

`MatchStatisticsValidationTests` (unit tests, run on every build) simulate generated teams (`SimulationTeams`: 12
players with position-typical attributes; quality 60 against 60). Results on 400 matches (per team and game):

| Statistic | Engine | Accepted range |
|---|---|---|
| Points | 77.3 | 72–88 |
| Possessions | 73.0 | 68–78 |
| 2P% / 3P% / FT% | 53.9% / 34.4% / 78.6% | 48–57 / 32–39 / 70–80 |
| Three-point share of field goals | 36.3% | 32–45% |
| Free-throw attempts | 20.3 | 16–26 |
| Rebounds (offensive) | 33.1 (9.5) | 32–40 (8–13) |
| Assists / turnovers / steals / blocks | 15.9 / 12.5 / 6.0 / 2.0 | 14–21 / 11–16 / 5–9 / 1.5–4.5 |
| Personal fouls | 19.6 | 17–24 |
| Games to overtime | 2.3% | 2–12% |
| Top scorer's share of team points | 25% | 18–35% |
| Starters' minutes | 28.9 | 24–33 |
| Home wins | 52.5% | 50–65% |

Other checks: quality 70 vs 55 wins 95% (margin +22); tempo 1 + post offense 63.8 possessions / 63.4 points against
tempo 5 + transition 97.7 / 108.1; five-out three-point share 40.4% against 31.9% for a post offense; a full-court
press forces more turnovers; low fitness (45) costs more than 3 points of margin; 60 complete matches and 150
short-handed matches (5 players) never reach an invalid state (score = sum of points, 200 player-minutes per team and
25 per overtime, nobody above 5 fouls, nobody plays after fouling out).
