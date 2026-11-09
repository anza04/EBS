# Contracts

Contract lifecycle, salary calculations, negotiation and transaction rules (Step 07).
Code: `Ebm.Domain.Contracts`, `Ebm.Services.Contracts.IContractService`, screens *Club contracts*, *Player profile*
and *Contracts (editor)*.

## 1. Contract fields

One `Contracts` row per contract between a club and a player (or a staff member). Money is in whole euros.

| Field | Rule |
|---|---|
| `ClubId`, `PlayerId` / `StaffMemberId` | Fixed once created; exactly one person |
| `StartDate`, `EndDate` | End after start; at most 10 years (`Contract.MaxLengthYears`). `EndDate` is the agreed last day (extended when an option is taken up) |
| `AnnualSalary` | 0–100,000,000 |
| `PaymentFrequency` | `Monthly` (default), `Quarterly`, `Annually` |
| `SigningBonus` | 0–100,000,000, paid by the club when the contract is signed |
| `PerformanceBonuses` | At most one amount per condition: `PerGamePlayed`, `ReachPlayoffs`, `LeagueTitle`, `CupTitle`, `Promotion`, `AvoidRelegation`; 1–100,000,000 each. Stored as `ReachPlayoffs:20000;LeagueTitle:50000` |
| `ReleaseClause` | Optional, 1–1,000,000,000: the buy-out fee for other clubs (used by transfers, Step 08) |
| `ClubOptionUntil`, `PlayerOptionUntil` | Optional: the date the club / the player may extend the contract to. After `EndDate`, at most 3 years later |
| `Status` | `Active`, `Expired`, `Terminated`, `Renewed` (§2) |
| `TerminationDate` | First day the contract no longer runs after an early end; `null` otherwise |
| `TerminationCompensation` | Settlement paid to the player on termination |

`ContractTerms` groups the negotiable fields (dates, salary, frequency, bonuses, clause, options): it is what the
negotiation, the editor and the importer pass to the domain. Lifecycle fields change only through the domain
methods below, never through the editor or an import.

## 2. Lifecycle

```text
                 sign (IContractService.SignAsync, or editor/import)
                                   │
                                   ▼
   upcoming ──(start date)──► Active, running ──(end date passes, ProcessContractDatesAsync)──► Expired
                                   │   │   │
            option taken up ───────┘   │   └── renewal (RenewAsync) ──► Renewed  + new Active contract
            (EndDate moves)            │
                                       └── termination (TerminateAsync) ──► Terminated
```

* **Running on a date** = `StartDate ≤ date ≤ EndDate` and (no `TerminationDate` or `date < TerminationDate`).
  `Contract.RunningOn(date)` is the same rule as a query the repositories translate; every service uses it (wage
  bill, current club, squad, profile). **Upcoming** = signed, not started, not cancelled.
* `Contract.StatusOn(date)` reports an `Active` contract past its end date as `Expired` even before the expiry has
  been processed. The screens show "Running", "Starts yyyy-MM-dd", "Expired", "Terminated" or "Renewed".
* **Contracts are never deleted.** Expired, terminated and renewed contracts are the contract history of the player
  (player profile) and of the club (*Club contracts* → history).

### 2.1 Signing (`SignAsync`)

1. The player must be **free for the whole offered period**: no running or upcoming contract that overlaps it. A
   player under contract can sign a contract that starts after his current one ends (a pre-contract); a contract
   with his own club is refused with "Renew it instead". Buying a player out of a running contract is Step 08.
2. The start date cannot be before today.
3. The player must **accept** the terms (§4); a counter-offer or rejection is refused with the player's message.
4. The club pays the signing bonus now (`Club.ChangeBalance`).
5. If the contract runs today, the player joins the club today (his open club spell ends, a new one starts, his
   roster spot moves) in the chosen role: bench (default), inactive or youth. A pre-contract moves him on its
   start date (§5).

The database editor and the importer can still create any contract directly (no negotiation, no payments, no
club move): they are data tools.

### 2.2 Renewal (`RenewAsync`)

* Only an `Active` contract that has not run out can be renewed. The renewal must start after the current
  contract's start, not before today, and **no later than the day after it ends** (no gap).
* The player must accept; his asking salary is at least 90% of his current salary (§4).
* The old contract becomes `Renewed`. If the renewal starts before the old end date, the old contract ends early
  (`TerminationDate` = renewal start). The new contract is a new row: history keeps both.
* The club pays the new signing bonus. The player stays at the club; nothing moves.

### 2.3 Termination (`TerminateAsync`)

Effective today (`TerminationDate` = today; a contract that has not started is cancelled, `TerminationDate` =
its start date, and never runs).

| Kind | Settlement paid by the club |
|---|---|
| `ClubRelease` | The remaining salary in full (§3) |
| `MutualAgreement` | The agreed amount, 0 to the remaining salary; default half of it |

The club pays the settlement at once. Unless another contract with the club runs today, the player **leaves the
club**: his club spell ends today, his roster spot is removed, and he is a free agent who can sign anywhere at once.

### 2.4 Options (`DecideClubOptionAsync`, `DecidePlayerOptionAsync`)

* An option is decided while the contract is `Active`, on or before its end date.
* Taking it up moves `EndDate` to the option date and clears the option; the other option is cleared too if it
  no longer extends the contract. Declining clears the option and keeps the end date.
* The club decides its own option. The player decides his: he takes it up when the contract's salary is at least
  his market asking salary (§4), otherwise he declines and leaves at the end date.
* Options are not decided automatically when a contract runs out: an undecided option lapses with the expiry.
  The season calendar (Step 12) will prompt for decisions before contracts end.

### 2.5 Expiry and start (`ProcessContractDatesAsync`)

Applies the calendar up to today; idempotent. Run at application start and, from Step 12, on every game day.

1. Every `Active` contract whose end date has passed becomes `Expired`. If the player has no other running or
   upcoming contract with that club, he leaves it: his spell there ends on the contract's end date and his roster
   spot is removed. Staff contracts expire too (status only).
2. Every running player contract whose player is not yet at its club moves him there from the contract's start
   date (new spell, roster spot on the bench), when his open spell elsewhere started before the contract. Players
   whose club history is not tracked (imported contracts without spells) keep belonging to the club through the
   contract, as in Step 06.

## 3. Salary calculations (`SalaryCalculator`, `SalaryCommitments`)

* **Accrual.** Salary is earned day by day within each *contract year* (from the start date's anniversary to the
  day before the next one): a full contract year costs exactly `AnnualSalary`, 365 or 366 days. A day is worth
  `AnnualSalary / days in that contract year`. Amounts are rounded to whole euros at the end (half away from zero).
* `SalaryBetween(contract, from, to)` — salary for the running days in the range; `RemainingSalary(contract, date)`
  — from the date to the effective end (the day before an early end); `TotalSalary(contract)`.
* **Payment schedule.** Instalments of 1, 3 or 12 months counted from the start date, the last one cut at the
  effective end; each is due on the last day of its period. Instalment = difference of the rounded running totals,
  so the instalments add up exactly to the total salary. `NextPayment(contract, date)`.
* **Club commitments** (`SalaryCommitments.Calculate(contracts, date)`), over the club's running and upcoming player
  contracts:

  | Value | Meaning |
  |---|---|
  | `WageBill` | Annual salaries of the contracts running on the date (same as the club profile) |
  | `RemainingSalary` | Salary still owed under running and upcoming contracts, to their effective ends |
  | `Periods` | The salary owed per 12-month window starting on the date, and how many contracts fall in each |
  | `MaxSeasonBonuses` | Season bonuses owed if every condition were met in one season (per-game bonuses excluded) |
  | `RunningContracts`, `UpcomingContracts` | Counts |

  Each period is rounded separately, so the periods can differ from `RemainingSalary` by a euro or two.
* **Wage budget left** = `Club.WageBudget − WageBill` (negative = over budget). It is reported, not enforced: the
  board and AI clubs respect financial limits from Step 08 (transfers) and Step 27 (finances).

## 4. Negotiation (`ContractNegotiator`)

Simplified and deterministic: the same offer always gets the same answer. Parameters are properties of
`ContractNegotiator` (defaults below), registered as a singleton.

**Asking salary** (annual, rounded to € 1,000, minimum € 15,000):

```text
asking = 30,000 × e^(0.08 × (overall − 40))
       × (0.85 + 0.30 × playerReputation / 100)      better known players ask more   (×0.85 … ×1.15)
       × (1.10 − 0.20 × clubReputation / 100)        prestigious clubs pay less       (×1.10 … ×0.90)
       × (1 + min(0.15, 0.005 × (potential − overall)))   age ≤ 23 and potential above overall
       × max(0.60, 1 − 0.05 × (age − 31))                 age ≥ 32
asking = max(asking, 0.9 × current salary)                renewals: at most a 10% pay cut
```

Overall 60 ≈ € 150,000; 70 ≈ € 330,000; 80 ≈ € 735,000 (average reputations).

**Offer value** (annual, as the player sees it):

```text
value = salary + signingBonus / years + 0.25 × seasonBonuses
      + 5% of salary with a player option − 5% of salary with a club option
```

`years` is the length rounded to a quarter year, at least 1.

**Answer:** length over 5 years → rejected. `value ≥ asking` → **accepted**. `value ≥ 75% of asking` →
**counter-offer**: the same terms with the salary raised (to the next € 1,000) until the value reaches the asking
salary; the counter-offer is always acceptable. Below 75% → **rejected**.

`NegotiateAsync` / `NegotiateRenewalAsync` return the answer without saving anything; *Ask the player* on the
*Club contracts* screen shows it, and *Use his counter-offer* copies the counter salary into the form.

## 5. Transaction rules

* Every use case is **one unit of work**: the contract change, the club's balance and the player's club spell and
  roster spot are saved together or not at all.
* Club money moves only for one-off payments: the **signing bonus** (on signing or renewal) and the **settlement**
  (on termination), through `Club.ChangeBalance` (balance stays within ±10 billion; it may go negative).
  Salaries and performance bonuses are calculated but **not yet booked**: periodic payments need the game
  calendar (Step 12) and the finance system (Step 27), which will pay each due instalment from the schedule.
* Membership changes go through `ClubMembership` (shared with `IClubService.AssignPlayerAsync`): joining closes the
  open spell and opens a new one; leaving closes the spell at the club and removes the roster spot.
* A player has at most one running contract at a time through the game flow (signing refuses overlaps). The editor
  and the importer do not check overlaps; with two running contracts, the latest-starting one decides the club.

## 6. Free agents

A player is a **free agent** when he has no running and no upcoming contract
(`IContractService.GetFreeAgentsAsync`, `PlayerContracts.IsFreeAgent`). He may still belong to a club through an
open club spell without a contract (data entered that way, or the squad screen's *Add a player*): any club can sign
him, which ends that spell. The free-agent list (best overall first) feeds the *Sign a free agent* form; search and
filters come with the transfer market (Step 08).

## 7. Screens

* **Club contracts:** running and upcoming contracts (status, end, salary, frequency, remaining salary, next
  payment, options, release clause, bonuses), contract history (status, early end, settlement), wages and
  commitments per 12 months, actions on the selected contract (*Renew…*, *Terminate* with kind and settlement,
  club option *Take up*/*Decline*, *Ask the player to decide* on his option), and the offer form (*Sign a free
  agent* / *Renew*): terms, *Ask the player*, *Use his counter-offer*, *Submit*.
* **Player profile:** salary, contract status ("free agent", "under contract until …", "signed with … from …") and
  the full contract history.
* **Contracts (editor):** every negotiable term and the status, for data maintenance.

## 8. For later steps

* Transfers (Step 08): buy-outs pay the `ReleaseClause` or a negotiated fee, then terminate the old contract
  (a new `TerminationKind`) and sign the new one; reuse `ContractNegotiator` for player acceptance and AI clubs.
  Loans need a contract link to the parent club.
* Calendar (Step 12): call `ProcessContractDatesAsync` every day; prompt for option decisions and renewals before
  contracts expire; book salary instalments (`SalaryCalculator.PaymentSchedule`).
* Season review (Steps 12, 27): pay performance bonuses from `PerformanceBonuses`; enforce the wage budget.
* Staff contracts use the same entity; staff management comes later.
