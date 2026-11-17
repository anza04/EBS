# Injuries and medical management (Step 22)

Injuries, recovery, medical assessment and player availability. Code: `src/Ebm.Domain/Medical` (`InjuryCatalog`,
`Injury`, `InjuryRules`), `src/Ebm.Services/Medical/MedicalService.cs` (`IMedicalService`), AI handling in
`TrainingPlanner` and `ClubManagementService`. Rates are configurable (§7); the formulas are code.

## 1. Injury types and severity

| Type | Body part | Weight | Recovery (days) | Contact |
|---|---|---|---|---|
| Ankle sprain | Ankle | 22 | 3–35 | yes |
| Knee sprain | Knee | 9 | 7–42 | yes |
| Torn ACL | Knee | 0.8 | 200–300 | |
| Torn meniscus | Knee | 2 | 30–90 | |
| Patellar tendinitis | Knee | 6 | 3–21 | |
| Hamstring strain | Thigh | 9 | 7–35 | |
| Thigh bruise | Thigh | 8 | 1–10 | yes |
| Groin strain | Hip | 6 | 5–28 | |
| Calf strain | Leg | 6 | 5–28 | |
| Achilles rupture | Leg | 0.4 | 180–300 | |
| Stress fracture | Foot | 1.5 | 42–100 | |
| Plantar fasciitis | Foot | 3 | 7–42 | |
| Back spasms | Back | 8 | 2–14 | |
| Finger sprain | Hand | 8 | 1–10 | yes |
| Broken hand | Hand | 1.5 | 28–56 | yes |
| Shoulder sprain | Shoulder | 3 | 7–28 | yes |
| Concussion | Head | 2.5 | 5–21 | yes |
| Illness | — | 5 | 1–7 | |

- **Type:** chosen by weight; contact injuries ×0.3 in training; a body part injured in the recurrence window
  (365 days) ×2.
- **Recovery days** (true time at normal speed): `min + (max − min) × u²` (short recoveries more likely), ×1.25 for a
  recurrence.
- **Severity** from recovery days: Minor ≤ 7, Moderate ≤ 21, Serious ≤ 56, Severe > 56.

## 2. Injury risk

Checked daily for every active player, after training and fitness (`IDevelopmentService.ProcessDayAsync`):

- **Match:** a player who played `m` minutes today: `1 − exp(−0.0011 × frequency × m × risk)`. About 3.5 % for 30
  minutes at average risk; ~1.3 match injuries a season for a rotation player.
- **Training:** a club player who did not play today: `0.0012 × frequency × load × risk`, load Light 0.5, Normal 1,
  Hard 1.8. Free agents do not train with a team (no risk). ~0.3 training injuries a season.
- **Risk multiplier** (`InjuryRules.RiskMultiplier`, 1 = average): fatigue `1 + max(0, 80 − fitness)/40` (fitness
  after the day's minutes) × workload `1 + max(0, week minutes − 100)/150` × age `1 + 4 % per year from 30` × physique
  `1.3 − 0.6 × mean(strength, stamina)/100` × medical care `1.1 − 0.2 × physio/100` × history `1 + 0.25 per injury in
  the last 365 days` × 1.5 when back for less than 21 days × 1.5 when playing hurt × proneness (0.7–1.3, a stable
  hidden trait per player).

Players under treatment (no training, §3) have no risk. Players with an active injury who play (minor knocks) or do
light work (minor knocks, rehabilitation) can **aggravate** it instead of getting a new one: +2–6 recovery days, the
severity is recomputed (a minor knock can become a moderate injury and rule the player out).

## 3. Recovery, rehabilitation and return to play

| Phase | When | Status | Training |
|---|---|---|---|
| Treatment | first 70 % of the recovery | Injured (minor: day-to-day) | none (minor: light work) |
| Rehabilitation | last 30 % | Injured (minor: day-to-day) | individual light work only |
| Recovered | from the day after progress reaches the recovery days | Healthy | full |

- **Progress per day** (`RecoverySpeed`): `0.85 + 0.3 × physio/100` (0.85–1.15), × `1 − 1.5 % per year from 29`
  (at least 0.7).
- **Availability:** `Players.InjuryStatus` follows the active injury. *Injured* players are unavailable (match
  engine, line-ups, roster validation); *day-to-day* players are doubtful: they may play, AI coaches rank them lower
  (`LineupPlanner.DoubtfulPenalty`) and they risk aggravation.
- **Development:** unchanged from Step 21: injured players do not develop, day-to-day ones at half rate, and injured
  players recover fitness as if resting.
- **Return-to-play fitness:** on return, fitness is capped at `100 − 0.6 × days out` (at least 55): long absences cost
  match sharpness, which training then restores (docs/DEVELOPMENT.md §3).

## 4. Medical assessment and recurrence

- Every club has one physio, hired automatically (Mondays and at career start, `StaffHiring`, ability by club
  reputation ± 10, salary € 20,000 + € 600 per ability point). Without one, ability 30 is assumed.
- **Assessment** on the day of the injury and every Monday: estimated return = today + remaining days / speed ×
  (1 ± error), error up to `50 % − ability/250` (±10 % at 100, ±38 % at 30). The club and the news see the estimate
  (`Injuries.EstimatedReturn`, `Players.InjuryReturnDate`); the player returns when he has actually healed.
- **Recurrence:** injuries in the last 365 days raise the risk (§2), make their body part twice as likely and a
  recurrence (same body part) heals 25 % slower (`Injuries.IsRecurrence`).

## 5. AI clubs

- Line-ups: unavailable players are never named; day-to-day ones only when needed (Step 13, unchanged).
- Training (`TrainingPlanner`, Mondays): injured players get no programme, day-to-day players a recovery focus, the
  intensity uses the fitness of healthy players and is never hard with a quarter or more of the squad injured.
- Cover: players expected back in more than 28 days do not count towards the 10-player minimum; a club short because
  of them opens the Monday window and signs free agents (`ClubsShortOfPlayersAsync`, `FillSquadsAsync`), within the
  18-player maximum.

## 6. Medical records

Table `Injuries` (docs/DATABASE_SCHEMA.md): one row per injury, never deleted: type, severity, source (match with
`MatchId`, or training), club, dates, recovery days and progress, latest estimate, recurrence, aggravations, return
date. The *Medical* tab shows the club's physio, every squad player's status, injury, phase, training restriction,
expected return, risk and injuries in the last year, and the club's injury history. Career news lists the user's
club's injuries and a count for the other clubs.

A condition set without a medical record (editor, import) ends on its return date; a day-to-day status without a
date ends the next day.

## 7. Configuration (`Ebm:Injuries`)

| Setting | Default | Range |
|---|---|---|
| `Frequency` | 1.0 | 0–5 (0 switches injuries off; recovery continues) |
| `MatchRiskPerMinute` | 0.0011 | 0–0.05 |
| `TrainingRiskPerDay` | 0.0012 | 0–0.05 |
| `RecurrenceWindowDays` | 365 | 1–1,000 |
| `RecurrenceRecoveryFactor` | 1.25 | 1–3 |
| `ReturnWindowDays` | 21 | 1–1,000 |

Validated at start-up (`InjuryRules.Validated`).

## 8. Known limitations

- Injuries are decided after the match from the minutes played: the injured player finishes the game (no in-game
  injuries or forced substitutions).
- No surgery or treatment choices, no rushing players back, no medical budget; one physio per club (only the best
  physio counts).
- The injury catalogue and formulas are code; only the rates in §7 are configurable.
- The editor edits a player's condition, not his medical record: while an injury record is active, the next medical
  day sets the condition back to the injury's.
- AI cover signings use the existing emergency signing (one-year contracts, cheapest affordable free agents).
- Injuries are not part of morale, valuations or transfer decisions yet.
