# Player Model and Ratings

How a player is described, what every value means, and how ratings and position suitability are calculated.
Code: `src/Ebm.Domain/Players` (model) and `src/Ebm.Domain/Players/Ratings` (formulas). Storage:
`docs/DATABASE_SCHEMA.md`. Import columns: `docs/IMPORT_FORMATS.md`.

## 1. Player information

| Value | Range / values | Default (new or pre-Step 05 player) | Notes |
|---|---|---|---|
| First name, last name | 1–60 characters | — | |
| Date of birth | date | — | Age is calculated (`Player.AgeOn(date)`), never stored |
| Nationality | country | — | |
| Height | 150–250 cm | — | |
| Weight | 50–200 kg | — | |
| Wingspan | height ± 30 cm | height | Gives reach (§4.2) |
| Position | PG, SG, SF, PF, C | — | Primary position; the overall rating is measured here |
| Secondary positions | any set of the other positions | none | Never contains the primary position (changing the primary removes it from the set) |
| Current club | — | — | Not stored on the player: the open `PlayerClubHistory` spell, else the club of the running contract |
| Contract | — | — | Not stored on the player: the `Contracts` row running today |
| Experience | 1–100 | 1 | Professional experience; grows with games played (Step 12) |
| Reputation | 1–100 | 1 | How well known and respected the player is |
| Potential | 1–100 | 50 | The highest overall rating the player can reach (§4.5) |
| Fitness | 0–100 | 100 | Match fitness; 100 = fully fresh |
| Morale | 1–100 | 50 | 50 = neutral |
| Injury status | Healthy, DayToDay, Injured | Healthy | Healthy: no return date. Injured: expected return date required. DayToDay: date optional. Detailed injuries are Step 22 |

## 2. Attributes

27 attributes, each **1–100** (1 = unusable, 50 = average professional, 75 = very good, 90+ = elite).
The order below is the storage and display order (`PlayerAttribute` enum); add new attributes only at the end,
with a migration.

| Category | Attribute | Measures |
|---|---|---|
| Shooting | Close shot | Short jumpers and floaters, 1–3 m |
| | Mid range | Jump shots inside the three-point line |
| | Three point | Three-point shooting |
| | Free throw | Free-throw shooting |
| Finishing | Layup | Finishing at the rim with layups, including through contact |
| | Dunk | Ability to dunk in traffic |
| | Post moves | Back-to-the-basket scoring and footwork |
| Playmaking | Passing | Accuracy and range of passes |
| | Court vision | Seeing open team-mates and plays developing |
| | Ball handling | Dribbling under pressure |
| | Decision making | Choosing the right option; avoiding turnovers |
| Defense | Perimeter defense | Guarding ball-handlers and shooters |
| | Interior defense | Guarding the post and the paint |
| | Steal | Stealing the ball and deflections |
| | Block | Blocking and contesting shots |
| | Defensive positioning | Help defense, rotations, taking charges |
| Rebounding | Offensive rebound | Rebounds on own missed shots |
| | Defensive rebound | Rebounds on opponents' missed shots |
| Physical | Speed | Running speed with and without the ball |
| | Strength | Physical strength in contact |
| | Vertical | Jumping ability |
| | Agility | Quickness, change of direction, lateral movement |
| | Stamina | How long the player keeps performing; fitness recovery |
| Mental | Concentration | Focus over the whole game; fewer lapses |
| | Leadership | Influence on team-mates |
| | Consistency | How often the player plays to his level |
| | Work rate | Effort in games and training |

## 3. Tendencies

How often a player chooses an action (1–100), independent of how well he does it. Read by the match engine
(Step 10). New and imported players get **suggested** tendencies unless values are given; the editor has
*Suggest from attributes*.

| Tendency | Suggested value = rounded weighted mean of |
|---|---|
| Three point shot | Three point ×3, Mid range ×1 |
| Mid range shot | Mid range ×3, Close shot ×1 |
| Drive to basket | Layup ×2, Ball handling ×1, Speed ×1 |
| Post up | Post moves ×3, Strength ×1 |
| Pass first | Passing ×1, Court vision ×1 |
| Isolation | Ball handling ×2, Mid range ×1 |
| Crash offensive glass | Offensive rebound ×3, Work rate ×1 |
| Defensive gambling | Steal ×2, Block ×1 |

Rounding everywhere is half away from zero (`MidpointRounding.AwayFromZero`; SQLite `ROUND` does the same).

## 4. Rating formulas

All formulas are pure functions in `PlayerRatingCalculator` over a `RatingModel` (`RatingModel.Default`);
the same player always gets the same ratings. `PlayerEvaluation` returns every derived value, so callers use
the part that matters (a category, a position, a tendency) instead of relying on one overall number.

### 4.1 Category ratings (position-independent)
`category = round(mean of the category's attributes)`. Seven values: Shooting, Finishing, Playmaking, Defense,
Rebounding, Physical, Mental.

### 4.2 Position rating
```
skill(p)   = Σ weight(p, a) × attribute(a) / Σ weight(p, a)
reach      = height + (wingspan − height) / 2
penalty(p) = min(15, 0.6 × cm below ideal reach(p)   or   0.3 × cm above ideal reach(p))
rating(p)  = clamp(round(skill(p) − penalty(p)), 1, 100)
```

Attribute weights (0–5) and ideal reach:

| Attribute | PG | SG | SF | PF | C |
|---|---|---|---|---|---|
| Close shot | 2 | 2 | 2 | 3 | 3 |
| Mid range | 3 | 4 | 3 | 2 | 1 |
| Three point | 4 | 5 | 4 | 2 | 1 |
| Free throw | 2 | 2 | 2 | 1 | 1 |
| Layup | 3 | 3 | 3 | 2 | 2 |
| Dunk | 0 | 1 | 2 | 2 | 3 |
| Post moves | 0 | 0 | 1 | 4 | 5 |
| Passing | 5 | 3 | 2 | 1 | 1 |
| Court vision | 5 | 2 | 2 | 1 | 1 |
| Ball handling | 5 | 4 | 2 | 1 | 0 |
| Decision making | 4 | 3 | 3 | 2 | 2 |
| Perimeter defense | 4 | 4 | 4 | 2 | 1 |
| Interior defense | 0 | 1 | 2 | 4 | 5 |
| Steal | 3 | 3 | 2 | 1 | 1 |
| Block | 0 | 1 | 2 | 3 | 5 |
| Defensive positioning | 3 | 3 | 3 | 3 | 3 |
| Offensive rebound | 0 | 1 | 2 | 3 | 4 |
| Defensive rebound | 1 | 1 | 2 | 4 | 5 |
| Speed | 4 | 3 | 3 | 2 | 1 |
| Strength | 1 | 1 | 2 | 4 | 5 |
| Vertical | 1 | 2 | 3 | 3 | 3 |
| Agility | 4 | 4 | 3 | 2 | 1 |
| Stamina | 2 | 2 | 2 | 2 | 2 |
| Concentration | 2 | 2 | 2 | 2 | 2 |
| Leadership | 1 | 1 | 1 | 1 | 1 |
| Consistency | 2 | 2 | 2 | 2 | 2 |
| Work rate | 1 | 1 | 1 | 1 | 1 |
| **Ideal reach (cm)** | 183–196 | 191–201 | 198–207 | 203–212 | 208–222 |

### 4.3 Familiarity and effective rating
`effective(p) = max(1, rating(p) − familiarity penalty)`, penalty: primary 0, secondary 2, unfamiliar 6.

### 4.4 Overall rating and best position
* **Overall** = effective rating at the primary position (= its position rating).
* **Best position** = highest effective rating; ties go to the primary position, then to position order (PG → C).
  It can differ from the primary position: that is a signal for the coach, not an error.

### 4.5 Potential rating
`potential rating = max(Potential, Overall)`: a player who already exceeds his stored potential has reached it.

### 4.6 Position suitability
`gap(p) = effective(best position) − effective(p)`; **Ideal** ≤ 2, **Good** ≤ 5, **Adequate** ≤ 10, else **Poor**.

Example (all attributes 60, 200 cm, wingspan 200 cm, SF): ratings PG 59, SG 60, SF 60, PF 58, C 55; effective
53, 54, 60, 52, 49; overall 60; suitability Adequate, Adequate, Ideal, Adequate, Poor.

## 5. Screens and services

* **Editor → Players**: every value above, attributes grouped by category, tendencies with *Suggest from attributes*.
* **Player profile** (`IPlayerProfileService.GetProfileAsync`): details, current club and contract, condition,
  overall/potential/best position, category ratings, position table, attributes and tendencies.
* **Compare players** (`CompareAsync`): two or more players side by side; *Better* names the single best value
  (none on ties, and none for age, size and tendencies, where higher is not better).

## 6. Changing the model

* Weights, reach ranges, penalties and thresholds: edit `RatingModel.Default` (validated by `Validate()` at start).
  Ratings are calculated, never stored, so a change applies to every player at once; update this document and the
  tests in `PlayerRatingCalculatorTests`.
* A new attribute or tendency: add it at the end of the enum, add the property to `PlayerAttributes` /
  `PlayerTendencies`, add a migration (new column with a default), and update the weights and this document.
* Loading the model from a data file (modding) is planned for Step 33.

## 7. Upgrade of pre-Step 05 data (migration `PlayerModel`)

The 8 initial ratings were mapped to the 27 attributes (integer division):

| New attribute | From |
|---|---|
| Mid range, Three point, Free throw | Shooting |
| Close shot | (Shooting + InsideScoring) / 2 |
| Layup, Post moves | InsideScoring |
| Dunk | (InsideScoring + Athleticism) / 2 |
| Passing, Ball handling | same name |
| Court vision | (Passing + BasketballIq) / 2 |
| Decision making, Concentration, Leadership, Consistency, Work rate | BasketballIq |
| Perimeter defense, Interior defense, Steal | Defense |
| Block | (Defense + Rebounding) / 2 |
| Defensive positioning | (Defense + BasketballIq) / 2 |
| Offensive rebound, Defensive rebound | Rebounding |
| Speed, Vertical, Agility, Stamina | Athleticism |
| Strength | (Athleticism + Rebounding) / 2 |

Wingspan = height; potential = mean of the 8 old ratings; tendencies = suggested values; other new fields take
their defaults (§1).
