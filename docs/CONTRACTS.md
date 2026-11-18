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
| `ReleaseClause` | Optional, 1–1,000,000,000: the fee another club can pay to buy the player out; the club cannot refuse it (docs/TRANSFERS.md §2) |
| `ClubOptionUntil`, `PlayerOptionUntil` | Optional: the date the club / the player may extend the contract to. After `EndDate`, at most 3 years later |
| `Status` | `Active`, `Expired`, `Terminated`, `Renewed`, `OnLoan`, `Transferred` (§2) |
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
                                   │   │   │   │
            option taken up ───────┘   │   │   └── renewal (RenewAsync) ──► Renewed  + new Active contract
            (EndDate moves)            │   │
                                       │   └── termination (TerminateAsync) ──► Terminated
                                       │
                                       ├── transfer (ITransferService.BuyAsync) ──► Transferred  + new Active contract elsewhere
                                       │
                                       └── loan (LoanAsync) ──► OnLoan ──(loan ends or recall)──► Active again
```

* **`OnLoan`** (docs/TRANSFERS.md §4): the contract keeps running (the player stays bound to the club) while the
  player plays for a loan club under a loan contract. It cannot be renewed or terminated until he returns; the wage
  bill and the salary commitments skip it, because the loan club pays him meanwhile.
* **`Transferred`** (docs/TRANSFERS.md §3): ended early on the transfer date, no compensation; the player signed
  with his new club the same day.

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
   with his own club is refused with "Renew it instead". A player under contract is bought out through the transfer
   market (docs/TRANSFERS.md §3).
2. The start date cannot be before today.
3. The player must **accept** the terms (§4); a counter-offer or rejection is refused with the player's message.
4. The club pays the signing bonus now (`Club.ChangeBalance`).
5. If the contract runs today, the player joins the club today (his open club spell ends, a new one starts, his
   roster spot moves) in the chosen role: bench (default), inactive or youth. A pre-contract moves him on its
   start date (§5).
6. The signing is recorded in the transfer history as a `FreeAgentSigning` (docs/TRANSFERS.md §1).

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
| `MutualAgreement` | A **buyout** agreed with the player: at least what he accepts (below), at most the remaining salary; default the least he accepts |

**Contract buyouts (Step 19, `NegotiateBuyoutAsync`).** The player accepts a buyout of at least a share of the salary
still owed to him (`ContractNegotiator.MinimumBuyout`, rounded up to € 1,000): 30% when he would earn at least his
current salary on the open market (`AskingSalary` at an average club, reputation 50), plus the pay cut he would face
(market salary 60% of his salary → 30% + 40% = 70%), at most 90%. A lower settlement is refused. AI clubs releasing
surplus players pay this minimum.

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
* Contracts with status `OnLoan` are skipped: the loan club pays the player while he is away (docs/TRANSFERS.md §4).
* **Wage budget left** = `Club.WageBudget − WageBill` (negative = over budget). For the user's club it is reported,
  not enforced (a transfer that exceeds it is completed with a warning); AI clubs never exceed it when recruiting
  (docs/TRANSFERS.md §5). Finances (Step 27) decide whether the board enforces it.

## 4. Negotiation (`ContractNegotiator`, Step 07, extended in Step 25)

Deterministic: the same offer in the same state of the talks always gets the same answer. Parameters are properties
of `ContractNegotiator` (defaults below), registered as a singleton.

**Market salary** (`AskingSalary`; annual, rounded to € 1,000, minimum € 15,000). The transfer market, AI recruitment
and buyouts use it unchanged:

```text
asking = 30,000 × e^(0.08 × (overall − 40))
       × (0.85 + 0.30 × playerReputation / 100)      better known players ask more   (×0.85 … ×1.15)
       × (1.10 − 0.20 × clubReputation / 100)        prestigious clubs pay less       (×1.10 … ×0.90)
       × (1 + min(0.15, 0.005 × (potential − overall)))   age ≤ 23 and potential above overall
       × max(0.60, 1 − 0.05 × (age − 31))                 age ≥ 32
asking = max(asking, 0.9 × current salary)                renewals: at most a 10% pay cut
```

Overall 60 ≈ € 150,000; 70 ≈ € 330,000; 80 ≈ € 735,000 (average reputations).

### 4.1 Agents and relationships (`Agent`, `AgentRelationship`, `AgentPool`)

* A fixed pool of 24 fictional agents is created the first time anyone negotiates (table `Agents`). A player's agent
  is a stable function of his id (hash modulo the pool, ordered by name), so player data never changes. Players with
  reputation below 15 negotiate for themselves (toughness 50, no relationship).
* **Toughness** (30–99): the rejection threshold is `0.75 + 0.10 × (toughness − 50) / 50` (0.60–0.90) of the
  minimum value; agents of toughness ≥ 70 have one round less patience.
* **Relationship** with each club (−100…100, table `AgentRelationships`, shared by all the agent's clients):
  asking value × `(1 − 0.05 × relationship / 100)`; patience +1 at ≥ 40, −1 at ≤ −40.

  | Event | Change |
  |---|---|
  | Contract signed or renewed with the club | +5 |
  | Talks broken off (§4.5) | −10 |
  | Promise review: kept | +2 |
  | Promise broken (first time per contract) | −15 |

### 4.2 Demands (`Demands` → `PlayerDemands`)

```text
asking value  = market salary × (1 + 0.04 × competing offers (max 3)
                                   − 0.05 × relationship / 100
                                   + 0.10 if the club broke a promise in his current contract)   rounded to € 1,000
minimum value = asking value × (1 − 0.01 × min(3, round − 1))          small concessions in later rounds
reject below  = rejection threshold (§4.1) × minimum value
```

| Demand | Rule |
|---|---|
| Contract length | Accepted without penalty: 1–3 years for players aged ≤ 23 with potential > overall + 5 (they want to cash in later); 2–5 for ages 31–34 (security); 1–5 otherwise. Each year outside costs 7% of the asking value |
| Expected role | From his rank by overall among the club's players under contract (§4.3) |
| Role promise | A player with reputation ≥ 60 whose expected role is starter or star **insists** on being promised it (a role at least that high, or at least its minutes) |
| Release clause | A player aged ≤ 30 whose reputation is ≥ the club's attractiveness + 25 **insists** on a release clause of at most 20 × the asking value |

**Offer value** (annual, as the player sees it):

```text
value = salary + signingBonus / years + 0.25 × seasonBonuses
      + 5% of salary with a player option − 5% of salary with a club option
      + promise value: asking × min(10%, 4% per role level above his expected role
                                        + 0.5% per promised minute above that role's minutes)
      + release clause: 4% of asking if ≤ 10 × asking, 2% if ≤ 20 × asking
      − 7% of asking per year outside his preferred lengths
```

`years` is the length rounded to a quarter year, at least 1. Promises are worth nothing when his expected role is
unknown (transfer personal terms).

**Answer:** length over 5 years → rejected. No unmet demand and `value ≥ minimum` → **accepted**. `value ≥ reject
below` → **counter-offer** (`Meet`): the length moved into his range (options that no longer fit are dropped), the
role he insists on promised, the release clause he insists on added, then the salary raised to the next € 1,000 until
the value reaches the minimum; the counter-offer is always acceptable. Otherwise → **rejected**, naming his unmet
demands.

### 4.3 Squad roles and playing time (`SquadRole`, `PromiseRules`)

| Rank at the club (1 = best, counting him) | Expected role | Minutes per game |
|---|---|---|
| 1–2 | Star | 30 |
| 3–5 | Starter | 25 |
| 6–8 | Rotation | 17 |
| 6+ and aged ≤ 21 | Prospect | 12 |
| 9+ | Reserve | 8 |

A contract may promise a role, minutes per game (1–40), or both (`ContractPromises`; `Contracts.PromisedRole`,
`PromisedMinutes`). The target is the larger of the role's minutes and the promised minutes. Only negotiations set
promises: the database editor and the importer keep a contract's promises when they update its terms.

### 4.4 Competing offers

For a free agent, or a player whose contract ends within 180 days, up to three other clubs bid: clubs with reputation
at least the player's − 10, whose wage budget minus wage bill covers his market salary at their reputation, and for
whom that salary is at least 75% of their average salary (he would matter to them). Most reputable first. Each raises
his asking value by 4%; the screen lists them with the salary each would offer. The AI transfer window and emergency
signings remain the way rivals actually sign players.

### 4.5 Rounds of talks (`ContractNegotiation`, table `ContractNegotiations`)

* `GetNegotiationAsync` shows the demands and the talks (agent, relationship, round, patience, competing offers)
  without counting a round. `NegotiateAsync` / `NegotiateRenewalAsync` count one **round** and save it.
* Patience of new talks: 4, −1 with a tough agent, ±1 with the relationship (§4.1), at least 1. A counter-offer
  costs 1, a rejection 2, an acceptance nothing. At 0 the player **breaks off**: no talks or signing with that club for
  30 days (other clubs are unaffected); the agent's relationship drops by 10.
* Talks idle for 30 days, or ended by a signing, start again from round 1. `SignAsync` / `RenewAsync` evaluate the
  terms at the current round (no round counted), refuse while talks are broken off, and close the talks as agreed.
* Transfer personal terms (`ITransferService`) use the same demands (without expected role, agent, competing offers
  or rounds).

### 4.6 Promises and satisfaction (`ReviewPromisesAsync`)

Every day the career calendar reviews each running contract with promises once every 28 days (from its start):
the player's average minutes over the club's non-forfeited games in the last 28 days (0 when he did not play).
Windows with fewer than 3 club games (off-season) and injured players are skipped.

| Average minutes / target | Status | Morale |
|---|---|---|
| ≥ 85% | Kept | +3 |
| 60–85% | At risk | −3 |
| < 60% | Broken | −8 (agent relationship −15 the first time) |

The status is shown in the contract list (*Promises* column). A broken promise makes the player ask 10% more to
renew with that club (§4.2), and its agent's clients ask more of the club through the relationship.

### 4.7 AI clubs

* **Renewals** (`RenewContractsAsync`): offer the current salary for the policy length; accept the player's
  counter-offer (promises and clause included) if the salary rise is within the policy, the wage budget allows it and
  it is at most one year longer than the policy. Each attempt is a round, so a club that keeps failing makes the
  player break off for 30 days.
* **Emergency signings** (`FillSquadsAsync`): read the demands (no round, `GetNegotiationsAsync` in chunks), meet them
  with `Meet` and sign the best player whose salary fits the balance and the wage budget left; below the squad minimum,
  the cheapest player the balance covers when none fits (`docs/AI_MANAGEMENT.md`); players who broke off talks with the
  club are skipped.
* **Transfer window** (`RunAiTransferWindowAsync`): the recruiter plans at market salaries; each free agent's demands
  are then read (no round) and met, the extra salary taken from the wage budget left after the plan (a club below the
  squad minimum signs anyway); a purchase meets the player's personal-terms demands (`Meet`) and the wage budget check
  of the purchase still applies. A player who wants too much is skipped and reported.

### 4.8 Screen

*Club contracts* offer form: promised role and minutes, *His demands* (no round), *Ask the player* (one round; the
answer shows his demands, the agent, round, patience and competing offers), *Use his counter-offer* (copies every
term of the counter-offer into the form), *Submit*.

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
him, which ends that spell. The free-agent list (best overall first) feeds the *Sign a free agent* form; the
*Transfer market* screen searches and filters every player, free agents included, and signs them too
(docs/TRANSFERS.md §7).

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

* Transfers and loans are Step 08: see `docs/TRANSFERS.md` (the transfer ends the old contract as `Transferred`;
  a loan keeps the parent contract `OnLoan` and adds a loan contract linked through the `Transfers` row).
* Calendar (Step 12): call `ITransferService.ProcessTransferDatesAsync` every day (it returns loans, then applies
  contract dates); prompt for option decisions and renewals before contracts expire; book salary instalments
  (`SalaryCalculator.PaymentSchedule`).
* Season review (Steps 12, 27): pay performance bonuses from `PerformanceBonuses`; enforce the wage budget.
* Staff contracts use the same entity; staff management comes later.
