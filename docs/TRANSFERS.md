# Transfers and the Player Market

Transfer lifecycle, valuations and fee negotiation, loans, free agents and the AI clubs' recruitment rules (Step 08).
Code: `Ebm.Domain.Transfers`, `Ebm.Services.Transfers.ITransferService`, screen *Transfer market*.
Contracts themselves are described in `docs/CONTRACTS.md`.

## 1. Transfer records

One `Transfers` row per player movement. Rows are never deleted: they are the transfer history of the player and of
both clubs (*Transfer market* → *Transfer history of your club*). Money is in whole euros.

| Field | Rule |
|---|---|
| `PlayerId` | The player who moved |
| `FromClubId` | Club he left; `null` for a free agent |
| `ToClubId` | Club he joined (for a loan return, the parent club). Never equal to `FromClubId` |
| `Kind` | `FreeAgentSigning`, `Transfer`, `ReleaseClause`, `Loan`, `LoanReturn` (table below) |
| `Date` | Day he joined `ToClubId` |
| `Fee` | Paid by `ToClubId` to `FromClubId` on that day, 0–1,000,000,000; 0 for free agents and returns |
| `ContractId` | The contract he signed with `ToClubId` (the loan contract for a loan); `null` for a loan return |
| `ParentContractId` | Loans and returns: the contract with the parent club he keeps |
| `LoanEndDate` | Loans: his agreed last day at the loan club (at most 2 years after `Date`) |
| `ReturnedOn` | Loans: the day he went back; `null` while he is out (**open loan**) |

| Kind | Created by | What happened |
|---|---|---|
| `FreeAgentSigning` | `IContractService.SignAsync` | A player without a running or upcoming contract signed (docs/CONTRACTS.md §2.1). `FromClubId` is the club he belonged to without a contract, if any |
| `Transfer` | `ITransferService.BuyAsync` | Permanent move for a fee the selling club accepted |
| `ReleaseClause` | `BuyAsync` | Permanent move for the release clause, which the selling club could not refuse (fee below what it asked) |
| `Loan` | `LoanAsync` | Temporary move; `ReturnedOn` is set when he goes back |
| `LoanReturn` | `ProcessTransferDatesAsync`, `RecallAsync` | He went back to his parent club |

## 2. Valuation and fee negotiation (`TransferNegotiator`)

Deterministic and parameterised (properties of `TransferNegotiator`, registered as a singleton; defaults below).
The selling club's side only looks at the player and its own situation, never at the buyer.

**Market value** (rounded to € 10,000, minimum € 10,000):

```text
marketSalary = ContractNegotiator.AskingSalary(overall, potential, reputation, age, club reputation 50)
value = marketSalary × 2.5
      × age factor:   ≤22 1.40 · ≤25 1.25 · ≤28 1.10 · ≤30 1.00 · ≤32 0.75 · ≤34 0.55 · else 0.40
      × 1 + min(0.30, 0.01 × (potential − overall))
      × 0.5 + 0.25 × min(2, years left on the contract)      half a year left ×0.625 · one year ×0.75 · two or more ×1
```

A 70-rated 27-year-old with two years left is worth about € 900,000; an 80-rated one about € 2,000,000.

**Asking fee** = market value × importance × need, rounded to € 10,000:

| Factor | Value |
|---|---|
| Starter | ×1.5 |
| Rotation | ×1.25 |
| Bench, youth, unknown role | ×1.0 |
| Inactive | ×0.8 |
| Selling club in debt (balance < 0) | ×0.85 |
| Selling club over its wage budget | ×0.85 (cumulative with the line above) |

**Answer to a fee:** a fee that meets the **release clause** is always accepted ("the club cannot refuse").
Otherwise `fee ≥ asking` → **accepted**; `fee ≥ 80% of asking` → **counter-offer** at the asking fee;
below → **rejected**. `NegotiateFeeAsync` returns the answer without saving anything; *Ask the club* on the market
screen shows it and *Use its counter* copies the counter fee into the form. `PlayerValuation.BuyNowFee` is the fee
that completes the deal (the clause when it is lower than the asking fee); the search results and the form use it.

**Loans:** a club does not loan out starters or rotation players. For the others it asks
`10% of the market value per year of loan` (at least a quarter year, rounded to € 1,000), with the same
accept / counter / reject thresholds (`EvaluateLoan`).

**The player's contract** after a transfer is negotiated with the `ContractNegotiator` of Step 07, with his old
salary as the current salary (he accepts at most a 10% pay cut) and the buyer's reputation
(`NegotiateContractAsync`). The player does not negotiate a loan: the loan contract copies his salary.

## 3. Permanent transfers (`BuyAsync`)

A player **under a running contract** with another club is bought in one unit of work:

1. The player must be purchasable: not a free agent (sign him instead), not out on loan (his parent club must recall
   him first), not already the buyer's, and not signed in advance with a third club for a period the new contract
   would overlap.
2. The new contract must **start today** (a transfer takes effect at once).
3. The selling club must **accept the fee** (§2) and the player must **accept the contract** (docs/CONTRACTS.md §4).
4. **Limits** (§5): the buyer must be able to pay the fee plus the signing bonus from its balance, and must be able to
   register the player (first-team size, foreign-player limit, minimum age of its competitions). The wage budget and
   the matchday-squad size are reported as warnings; AI clubs treat the wage budget as a hard limit.
5. The old contract ends today as `Transferred` (`Contract.Transfer`: `TerminationDate` = today, no compensation),
   the new contract is added, the fee moves from the buyer's to the seller's balance, the buyer pays the signing
   bonus, the player joins the buyer's squad (`ClubMembership.JoinAsync`: his spell at the seller ends today, a new
   one starts, his roster spot moves) in the chosen role (bench, inactive or youth), and the transfer row is written.

Statistics keep the club the player represented, so history stays right after the move (decision 12).

## 4. Loans (`LoanAsync`, `RecallAsync`, `ProcessTransferDatesAsync`)

A loan moves a player temporarily to a **loan club** while his **parent contract** continues:

* **Conditions:** the player has a running, active contract with another club; the loan ends after today, no later
  than the parent contract's end date, within 2 years (`Loan.MaxYears`), and before any contract he has signed in
  advance starts. The parent club must accept the fee (§2) and the loan club must pass the limits (§5) for his salary.
* **Effects:** the parent contract becomes `OnLoan` (it still runs: nobody else can sign the player, and it cannot
  be renewed or terminated until he returns). A **loan contract** with the loan club is created on the same salary
  and payment frequency, from today to the loan end date. The loan club pays the fee and, from now on, **his
  salary**: wage bills and salary commitments skip `OnLoan` contracts (`SalaryCommitments.Calculate`, club profile).
  The player joins the loan club's squad; the loan row is open (`ReturnedOn = null`).
* **Return:** `ProcessTransferDatesAsync` returns every open loan whose end date has passed, on the day after the
  end date: the loan contract expires, the parent contract is active again, the player's spell at the loan club
  ends on the loan end date and he rejoins his parent club's bench (or leaves as a free agent when the parent
  contract ran out with the loan). A `LoanReturn` row is written and `ReturnedOn` is set. It then runs
  `IContractService.ProcessContractDatesAsync`, so loans are always settled before contract expiries. Run at
  application start and, from Step 12, every game day. Idempotent.
* **Recall:** the parent club may recall the player from the day after the loan started (`RecallAsync`): the loan
  contract is terminated today without compensation, and the return happens as above with today's date.

## 5. Financial and roster limits

| Limit | Transfers (managed club) | AI clubs |
|---|---|---|
| Fee + signing bonus ≤ balance (a fee never takes the balance below zero) | refused | refused |
| First-team size, foreign players, minimum age (`RosterValidator` errors `TooManyPlayers`, `TooManyForeignPlayers`, `UnderMinimumAge` for the club rules and every competition it plays in) | refused | refused |
| Wage bill ≤ wage budget | warning in the receipt | refused |
| Matchday squad of 12 (bench role) | warning | the AI joins as inactive when the matchday squad is full |

Only problems the newcomer **causes** are reported: a squad that was already short or had no lineup stays the
club's business. A player joining the youth squad is not registered, so the size limits do not apply to him.

## 6. AI recruitment (`ClubRecruiter`)

Simple, deterministic rules (the same situation always gives the same plan), over a `ClubSituation` (balance, wage
budget, wage bill, first team, strictest squad rules of the club's competitions — `SquadRules.Strictest`) and a list
of `Candidate`s priced for the club (asking salary from the `ContractNegotiator`, fee from §2, foreign status).

**Sales (`Surplus`)**, in this order: over the registration maximum → the weakest first-team players; over the
foreign-player limit → the weakest foreigners; over the wage budget → the most expensive non-starters, never below
the minimum squad. Players on loan at the club and players without a contract are never listed.

**Needs (`Needs`)**: every position should be covered by at least 2 first-team players (primary or secondary
position) — positional needs come first; then the squad is filled to the minimum, then to the target
(`TargetPlayers` = the matchday squad + 1 = 13 by default), never beyond the maximum.

**Signings (`Plan`)**: for each need, the best eligible candidate: fits the needed position, fee within the cash
(balance minus a 10% reserve), asking salary within the wage budget left, foreign limit respected. A **free agent is
preferred** unless a player with a fee is better by more than 5 overall points. Offer: his asking salary, for 3 years
(age ≤ 24), 1 year (age ≥ 32) or 2 years. Needs nobody affordable can fill are reported as unmet.

**The transfer window (`ITransferService.RunAiTransferWindowAsync(managedClubs)`)**:

1. Every AI club (every club not managed by the user) lists its surplus for sale at its asking fee.
2. Clubs shop in turn, highest reputation first, with the market reloaded after each club: candidates are the free
   agents and the surplus of other AI clubs (**AI clubs never buy from managed clubs**: offers to the user's club
   wait for the career mode). Free agents are signed through `IContractService.SignAsync`, the others through
   `BuyAsync` with the wage budget enforced. Refusals (a limit, a failed negotiation) are recorded, not thrown.
3. Clubs still over the registration maximum release their weakest players by mutual agreement
   (`IContractService.TerminateAsync`); a club still over its wage budget says so in the report.

The `TransferWindowReport` lists, per club, its needs and every action taken. The market screen's
*Run the AI transfer window* button runs it for every club but the chosen one.

## 7. Search and the market screen

`SearchAsync(PlayerSearch)` returns every player (best overall first) as a `MarketPlayer`: status (`FreeAgent`,
`UnderContract`, `OnLoan`), club, binding contract, role, `PlayerValuation` and the salary he would ask the
searching club (an average club when none). Filters: name, position (primary or secondary), age range, overall
range, nationality, status (everyone / free agents / under contract / available for loan), maximum fee
(`BuyNowFee`; free agents always pass), maximum asking salary, `ForClub` (excludes that club's own players and
prices salaries for it), maximum results.

**Transfer market** screen: choose *your club*; search and filter; the selected player's card (status, value, what
his club asks, loan terms, what he asks you); the offer form (fee with *Ask the club* / *Use its counter*; salary,
end date, signing bonus, role with *Ask the player* / *Use his counter*; *Buy*, *Pay the release clause* or *Sign the
free agent*); the loan form (*Loan until*, fee, *Ask the club*, *Take on loan*); your players out on loan with
*Recall the selected player*; players on loan with you; the club's transfer history; the AI window report.

## 8. Transaction rules

* Every use case is **one unit of work**: contracts, balances, spells, roster spots and the transfer row are saved
  together or not at all. Negotiation calls save nothing.
* Club money moves at once for fees (buyer → seller), signing bonuses and settlements (`Club.ChangeBalance`).
  Salaries are still not booked (Steps 12, 27).
* Club membership changes only through `ClubMembership` (shared with squads and contracts).
* A loan is the only case of two running contracts for one player: the parent one (`OnLoan`) and the loan
  contract. `ClubMembership.CurrentClubsAsync` picks the loan club (latest start, and the open spell).

## 9. For later steps

* Career mode (Steps 09–12): persist pending offers to and from the user's club (today an AI club never bids for a
  managed club's players); run `ProcessTransferDatesAsync` every game day; transfer windows as calendar periods.
* Finances (Step 27): book salaries, enforce the wage budget for the user, amortise fees.
* Step 13 (AI tuning) and Step 19 (European market): richer valuations (form, statistics, nationality), AI clubs
  bidding for any player, loan contracts with salary sharing, buy-back clauses and options to buy.
