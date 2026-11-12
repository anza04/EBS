# AI club management

Step 13. Every club except the career club (`Career.ClubId`) runs itself; with *Assistant manages* (`Career.AssistantManages`, Step 14) the career club too, and its matches are played by the calendar. Pure decision rules live in
`Ebm.Domain.Management` (`LineupPlanner`, `TacticPlanner`, `SeasonObjectives`, `ClubFinances`, `RenewalPolicy`,
`PlayerGenerator`); `IClubManagementService` (Services) applies them; the career calendar (`CareerService.AdvanceDayAsync`,
docs/SEASON_LIFECYCLE.md) decides when. Recruitment itself is the Step 08 `ClubRecruiter` (docs/TRANSFERS.md §6).

## 1. When the AI acts

| When | What |
|---|---|
| Career start | Board objectives for the season; AI tactics for every AI club |
| Before each game day's matches | Squad roles of every AI club that plays that day (saved only when they change) |
| End of every game day | Salaries due that day; month-end income (one transaction with the date change) |
| Last day of a season, 14 days before a season, any Monday an AI club is short of players | Transfer window: free-agent replenishment, `RunAiTransferWindowAsync`, releases above the squad limit, emergency signings, AI tactics re-planned |
| Last day of each month | Contract renewals |
| Next season | Board objectives for the new season |

"Short of players" = first team below `SquadRules.MinPlayers` (10) or fewer than five available players.

## 2. Starting lineups and rotation (`LineupPlanner`)

* Unavailable players are left out; doubtful players count 5 points lower; youth players keep their role.
* Starters: repeatedly the best remaining (player, position) pair by effective rating, one per position.
* Rotation: the next 4 by overall, in that order. Bench: up to the 12-player matchday squad. Everyone else: inactive.

## 3. Tactics (`TacticPlanner`)

Over the 9 best players: mean category ratings. A category at least 2 above the squad's mean picks the system
(highest wins): Shooting → *Five out* / perimeter focus / spacing 4; Finishing → *Post oriented* / inside focus;
Playmaking → *Pick and roll* / P&R frequency 4; Physical → *Transition* / tempo 4; otherwise *Motion offense*.
Defence: strong defence with athletic players → man-to-man, intensity and ball pressure 4; weak defence with good
rebounders → 2-3 zone; else man-to-man. Roles: `TacticalRoles.Suggest`; the three best get shot priority 4. Saved as
the club's tactic "AI plan" and selected.

## 4. Contracts

* Renewals (`RenewalPolicy`): players among the club's best 10 by overall, aged ≤ 33, whose contract ends within 150 days.
  Length 3 years (< 27), 2 (< 31), else 1. Offer: the current salary; a counter-offer is accepted up to 1.5× the
  current salary if the wage bill stays within the wage budget.
* Expiries: contracts end on their date (`ProcessContractDatesAsync`); the player becomes a free agent.
* Releases: above `SquadRules.MaxPlayers` (18) the weakest players with a single, own contract leave by mutual agreement.

## 5. Finances (`ClubFinances`)

* Salaries are paid on each instalment's due date (`SalaryCalculator.PaymentSchedule`) by the club holding the contract;
  a loaned-out player's parent contract is not paid (the loan club pays its loan contract). Applies to every club.
* Income (placeholder until Step 27): on the last day of each month every club receives one twelfth of its wage budget.
  A club within its wage budget therefore never loses money on wages; transfer fees and bonuses move balances.

## 6. Season objectives (`SeasonObjectives`)

* Set per league competition by reputation rank: 1st → *Win title* (or *Win promotion* where the competition promotes),
  within the promotion places → *Win promotion*, within the playoff places → *Reach playoffs*, the bottom
  (relegation places + 1) → *Avoid relegation*, else *Mid-table*. Stored as `Club.BoardExpectation` (the user's club too).
* Judged in the season review: title = champion; promotion = promoted; playoffs = finished in a playoff place (top quarter
  without playoffs); mid-table = top two thirds and not relegated; avoid relegation = not relegated. No consequence yet
  (board confidence comes with Step 27).

## 7. Free agents and recruitment

* `RunAiTransferWindowAsync` (Step 08): AI clubs list their surplus, then shop (free agents and other AI clubs' surplus)
  to reach 13 players and cover positions, within wage budget and cash.
* Replenishment (`PlayerGenerator`, before each window): the market keeps 2 free agents per club; missing ones are
  created as fictional players aged 18–23, quality 38–60, nationality of a random club, names recombined from existing
  players of that nationality (data, not code). Stand-in for youth intake (later step).
* Emergency signings: a club still below 10 players after a window signs the best free agents whose asking salary its
  balance covers for a year, ignoring the wage budget.

## 8. Simulation results

`CareerSimulationTests.Three_seasons_run_without_user_intervention` plays three seasons of the sample world (14 clubs)
with the user's matches played instantly. Typical run:

| Season | Days | Transfers | Renewals | Players created | Salaries paid | Income |
|---|---|---|---|---|---|---|
| 2026-27 | 117 | 0 | 0 | 0 | € 14.5 M | € 19.4 M |
| 2027-28 | 365 | 38 | 26 | 34 | € 44.1 M | € 58.3 M |
| 2028-29 | 368 | 52 | 24 | 0 | € 42.8 M | € 58.3 M |

(The first season runs from career start to the end of the playoffs, about four months with the sample schedule.)
Checked every season: no forfeits; every AI club has 10–18 players, five starters and the "AI plan" tactic; no AI club
balance below zero; at most 6 moves per club. Over the three seasons the three strongest squads of each league finished
2.6th on average, the others 5.2th. About half the clubs meet their objectives.

## 9. Known weaknesses

* Turnover grows over long careers (six-season run: 34 → 76 moves per season) as players age without youth intake or retirement.
* A user's club that nobody manages loses its players as contracts expire (found by the six-season run): use the assistant.
* Without youth intake or retirement, players only age; generated free agents keep the market supplied.
* Income equals the wage budget, so AI balances drift upwards; real revenues come with Step 27.
* The AI does not use loans, release clauses or bids for the user's players, and does not react to injuries beyond
  match-day roles (injuries arrive with Step 22).
* Tactics are re-planned only at transfer windows, not per opponent.
