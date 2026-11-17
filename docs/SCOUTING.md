# Scouting

Step 20. How clubs learn about players: scouts, assignments, reports and the uncertainty of what they say.
Code: `src/Ebm.Domain/Scouting` (rules, pure), `src/Ebm.Services/Scouting` (`IScoutingService`, `ScoutingKnowledge`),
screen *Scouting* in the desktop app.

## 1. Scouts

A scout is a `StaffMember` with role `Scout` and a contract running today at the club. `StaffMember.Ability`
(1–100, default 50; migration `Scouting`) is how good he is.

The datasets have no staff, so clubs hire their scouts themselves (`HireMissingScoutsAsync`, run when a career starts
and at the start of every game day's scouting): a club employs `1 + reputation / 40` scouts (1–3). Each new scout gets
ability `25 + 0.55 × reputation ± 10`, a name recombined from the players of the club's country, an age of 35–60, a
three-year contract and a salary of € 20,000 + € 600 per ability point (paid like every contract). When a contract
ends the club hires a replacement the next day.

## 2. Assignments

`AssignAsync(club, AssignmentRequest)` sends an available scout (one assignment at a time) from today:

| Focus | Target | Days | Players reported | Study days per player |
|---|---|---|---|---|
| `Player` | one player (not the club's own) | 7 | 1 | 28 (watched closely: 4 per day) |
| `Region` | a country: players at its clubs, free agents of its nationality | 21 | up to 10 | 21 |
| `League` | a competition: players at the clubs entered this season | 21 | up to 10 | 21 |
| `Position` | a position (primary or secondary), anywhere | 21 | up to 10 | 21 |
| `Youth` | players aged ≤ `MaxAge` (default 21), optionally of one country | 28 | up to 10 | 28 |

Group assignments also take optional filters: a position and a maximum age. They never report on the club's own
players. Among the matching players a scout reports first on those the club has no report on from the last 90 days,
then the most visible (§5), then the best known (reputation). `CancelAsync` stops an assignment; nothing is written.

## 3. Processing

`ProcessDayAsync` runs every game day (`ICareerService.AdvanceDayAsync`, after the contract and transfer dates): it
hires missing scouts, then every active assignment whose `DueDate` has come writes its reports and is `Completed`.
An assignment whose scout no longer works for the club is `Cancelled`. Processing is idempotent and the result is in
`GameDayReport.Scouting`.

## 4. Reports

A `ScoutReport` is a snapshot of what the club believes on its date: the 27 estimated attributes, the estimated
overall rating (computed from the estimated attributes exactly as for a real player), the estimated potential, the
accuracy and the error margins, and two prices on the estimated ratings:

* **Estimated value**: `TransferNegotiator.MarketValue` with the estimated overall and potential, the player's
  reputation, age and years left on his contract.
* **Salary expectation**: `ContractNegotiator.AskingSalary` with the estimates and the scouting club's attractiveness
  (reputation and international competition), so it is what he would ask *that* club.

Reports are never deleted. The newest report of a club on a player is its view (`SearchReportsAsync`: the player
search over a club's knowledge, by name, position, age, nationality, estimated overall, potential and salary);
`GetPlayerReportsAsync` lists the history. A report does not follow the player's later changes.

## 5. Uncertainty

All formulas are in `ScoutingRules`; estimates in `ScoutingEstimator`.

* **Visibility** (1–100) = player reputation, + 20 when he plays in the scouting club's country (free agents: holds
  its nationality), + 15 when his club plays an international competition, − 15 when he is 20 or younger.
* **Accuracy** (5–98) = 0.4 × scout ability + 0.3 × visibility + 30 × min(1, study days / 28).
  A repeated report builds on the last: at least the previous accuracy + 5.
* **Margin** = (100 − accuracy) / 4, at least 1 (accuracy 60 → ± 10, 90 → ± 3). **Potential margin** = 1.5 × margin.
* **Errors**: each attribute's error is the rounded mean of two uniform draws in [−margin, margin] (triangular, so
  usually small and never beyond the margin). The overall rating is a weighted mean of the attributes, so its error is
  within the margin too. Potential: the true potential plus an error within the potential margin, never below the
  estimated overall.
* **Determinism**: the draws are seeded from the club, the player and the date (`ScoutingRules.StableSeed`, stable
  across runs), so the same report is reproduced on a reload.

So scouting quality depends on the staff (ability), the time (focus and days) and the player's visibility; a range
shown as `low–high` always holds the true value.

## 6. AI clubs

AI clubs make no assignments. When they recruit (`RunAiTransferWindowAsync`), each candidate's overall rating is the
club's belief (`ScoutingKnowledge`): its newest report on the player, else **background knowledge** — an estimate
with the accuracy its best scout reaches in 7 study days (no scouts: the typical ability of its reputation), seeded
per club, player and year. Better-staffed clubs therefore misjudge fewer players; a club can overrate a weak player
and sign him ahead of better ones. Fees and salaries still come from the market (the selling club and the player
know the truth). The user's club works the same way while the assistant manages it.

## 7. Limitations and later steps

* The player database screens (profile, market, squad) still show true ratings: they double as the database editor.
  A career-mode view that shows only scouted information is a later step.
* Scouts are generated, not imported; no hiring or firing screen, no scout specialities (regions, youth) or
  experience (staff management: Step 28).
* Reports are snapshots: training and ageing (Step 21) do not update them; a new assignment does.
* Scout salaries are ordinary contract payments; scouting budgets belong to financial management (Step 23).
