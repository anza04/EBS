# Training, development and ageing (Step 21)

Code: `Ebm.Domain/Development` (`TrainingPlan`, `PlayerDevelopment`, `RetirementRules`),
`Ebm.Domain/Management/TrainingPlanner.cs`, `Ebm.Services/Development` (`IDevelopmentService`). All formulas are pure;
randomness comes from seeded `Random` instances (`ScoutingRules.StableSeed`), so a save develops the same way on every
run but equal players develop differently.

## 1. When it happens

| When (career calendar) | What |
|---|---|
| Career start | Missing coaches hired (§2.3); AI training plans |
| Every game day, after the matches | Fitness: fatigue from the day's minutes (every match of the day, the user's included), then recovery (§3); then the medical day (docs/INJURIES.md) |
| Mondays | AI clubs re-plan training (§6); missing coaches hired; one week of development for every active player (§4) |
| Last day of a season (before the AI transfer window) | Potential revised (§4.4); retirements (§5) |

The day is processed once by the calendar (decision 42); `ProcessDayAsync` is not idempotent on its own.

## 2. Training

### 2.1 Plans
One `TrainingPlan` per club (table `TrainingPlans`): a **team focus**, an **intensity** and up to 30 **individual
programmes** (player → focus, JSON, no foreign key: players who left are ignored). Without a stored plan a club trains
*Balanced* / *Normal*. The user's club keeps the plan saved on the *Training* screen; AI clubs (and the user's club in
assistant mode) are re-planned every Monday.

### 2.2 Focuses
| Focus | Trains |
|---|---|
| Balanced | every trainable attribute equally |
| Shooting | Shooting and Finishing categories |
| Tactical | Playmaking category, Concentration, Consistency |
| Defense | Defense and Rebounding categories |
| Physical | Physical category (speed, strength, vertical, agility, stamina) |
| Recovery | nothing in particular: growth × 0.85, fitness recovery +2 a day |

Leadership and Work rate are personality: training never changes them. Leadership grows with experience (§4.3).
Each focus (team and individual, added) gives its attributes +0.75 weight; weights are then scaled to an average of 1
over all attributes, so a focus moves development to its attributes without adding to the total. Focused attributes
decline at 70 % of the normal rate.

### 2.3 Staff
Coaching ability = mean effective *Development* attribute (with the experience bonus) of the club's head, assistant
and development coaches under contract (Step 28); fitness coach ability = best fitness coach's role ability; a missing
coach counts as 30. The head coach's coaching style multiplies growth (§4.1). Staff counts, hiring from the market and
salaries: docs/STAFF.md.

## 3. Fitness and recovery (daily)

`fitness' = fitness − 0.25 × minutes played today + recovery`, clamped 0–100, where recovery is 6 (light), 4 (normal)
or 2 (hard) — 6 while injured (rest) — plus 2 with a Recovery focus, plus `(fitness coach ability − 50) / 25`.
A player without a club trains alone (light, coaches 30). Fitness caps the match engine's starting energy
(`MatchSimulation`), so tired players play worse.

## 4. Weekly development

### 4.1 Growth
`growth/year = (potential − overall) × share(age) × coaching × style × intensity × recovery × playing time × competition × professionalism × condition`

Style (Step 28): *Youth development* × 1.10 up to age 23, *Disciplinarian* × 1.05, other styles × 1.

| Age | ≤ 18 | 19–20 | 21–22 | 23–24 | 25–26 | 27–28 | ≥ 29 |
|---|---|---|---|---|---|---|---|
| share of the gap closed per year | 0.32 | 0.28 | 0.22 | 0.14 | 0.08 | 0.04 | 0 |

| Factor | Formula |
|---|---|
| Coaching | 0.7 + 0.6 × ability / 100 |
| Intensity | light 0.8, normal 1.0, hard 1.15 |
| Playing time | 0.85 + 0.8 × min(share, 0.5), share = minutes in the last 28 days / (club games × 40); 1.0 when the club did not play |
| Competition | division level 1: 1.10, 2: 1.00, lower: 0.90, no club: 0.80; +0.05 in an international competition. A club without a league division counts as level 2 |
| Professionalism | 0.75 + 0.5 × work rate / 100 |
| Condition | injured 0, day-to-day 0.5; × 0.8 below 60 fitness |

No growth once the overall reaches the potential.

### 4.2 Decline (age curve)
`decline/year = D(age) × (1.25 − 0.5 × work rate / 100)`

| Age | ≤ 29 | 30 | 31 | 32 | 33 | 34 | 35 | ≥ 36 |
|---|---|---|---|---|---|---|---|---|
| D (overall points) | 0 | 0.5 | 1.0 | 1.5 | 2.0 | 2.6 | 3.2 | 4.0 + 0.5 per year over 36 |

Relative decline per attribute: Physical 1.6, Finishing 1.2, Defense 1.1, Rebounding 1.0, Playmaking 0.7, Shooting
0.5, Concentration/Consistency/Leadership/Work rate 0 (scaled to an average of 1).

### 4.3 One week
Each attribute changes by `(growth × luck × weight − decline × (2 − luck) × decline weight) × form / 52`, with
`form` uniform 0.5–1.5 per week and `luck` uniform 0.6–1.4 per player and season (`SeasonLuck`); the fraction is
rounded up with its probability, so expected changes are exact but outcomes vary. Experience grows by
`minutes this week × competition / 600` (same rounding); each experience point has a 25 % chance of +1 leadership.

### 4.4 Potential realisation (season end)
Up to 22: potential ± 3 (late bloomers and busts). 23–24: moves 15 % of the way to the overall; 25–26: 30 %;
27–28: 60 %; from 29 it equals the overall. Never below the overall.

The *Training* screen shows each player's **outlook** = growth − decline per year at average luck with the current plan.

## 5. Retirement (season end)

Configurable: `RetirementRules`, bound from the settings section `Ebm:Retirement` (appsettings.json or command line,
e.g. `--Ebm:Retirement:MinAge=35`), validated at start-up.

| Setting | Default | Meaning |
|---|---|---|
| MinAge | 33 | nobody younger retires |
| ForcedAge | 41 | everybody retires (except while on loan) |
| ChancePerYear | 0.12 | chance at `MinAge`, + 0.12 per further year |
| FreeAgentBonus | 0.25 | added without a club |
| LowRatingBonus / LowRating | 0.15 / 40 | added below this overall |
| HighRatingReduction / HighRating | 0.10 / 70 | subtracted from this overall |
| InjuryBonus | 0.10 | added while injured |
| UnderContractFactor | 0.5 | multiplies the chance when a contract runs into next season |

A retiring player: `Players.RetiredOn` = the next day (final); running and upcoming contracts terminated from that day
without compensation; club spell closed, roster spot removed. Players on loan finish the loan first. Retired players
are left out of development, the free-agent list, the transfer market, scouting and contract negotiations.

## 6. AI training (`TrainingPlanner`)

* Team focus: the area (Shooting, Tactical, Defense, Physical) where the 9 best players are weakest, if at least 3
  points below the mean of the areas; otherwise Balanced.
* Intensity: Light when the squad's mean fitness is below 85; Hard when it is at least 95 and the median age ≤ 24;
  otherwise Normal.
* Individual programmes: fitness below 70 → Recovery; age ≥ 31 → Physical; age ≤ 23 with ≥ 5 points to potential →
  his own weakest area (skipped when equal to the team focus).

## 7. Limitations
* No youth intake: retirements are replaced by generated free agents (Step 13) and transfers.
* Development history is not stored (only current attributes); scout reports are snapshots and age (`docs/SCOUTING.md`).
* Matches change fitness, not morale. Injuries: docs/INJURIES.md (Step 22).
* Formulas are code (`PlayerDevelopment` properties and tables), not a data file; only retirement is configurable.
