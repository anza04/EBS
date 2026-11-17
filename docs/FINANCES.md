# Club finances

Step 23. Every club has a bank balance (`Clubs.Balance`), a ledger of every income and expense
(`FinancialTransactions`) and, per season, a budget set by its board (`ClubBudgets`). The rules are pure code in
`Ebm.Domain.Finances.FinanceModel` (singleton, defaults below); `IFinanceService` plans budgets and builds reports.
All amounts are whole euros.

## 1. The ledger

* Every game-driven balance change goes through `Ledger.Book` (Services): it changes `Club.Balance` and writes a
  `FinancialTransaction` in the same unit of work. Positive = income, negative = expenditure. Never deleted.
* Data tools (database editor, importer) set balances directly, without bookings: they edit data, they are not events.
* Therefore, for any period: balance at the end − balance at the start = sum of the club's transactions in between
  (guarded by `FinanceServiceTests`).

## 2. What is booked, and when

| Category | When | Amount |
|---|---|---|
| `PlayerSalaries` / `StaffSalaries` | Each instalment's due date (`SalaryCalculator.PaymentSchedule`); one row per club, category and day | Salary due. Loaned-out players' parent contracts are not paid (the loan club pays). Staff (scouts, coaches, physios) are contracts like players |
| `SigningBonuses` | Signing, renewal, transfer, option to buy | The bonus |
| `Settlements` | Contract termination (release or buyout) | The settlement |
| `TransferFees` | Transfer or option to buy | + seller, − buyer |
| `LoanFees` | Loan start, recall, option to buy | Loan fee (+ parent, − borrower); the parent's salary contribution (− parent, + borrower) and its refund |
| `PrizeMoney` | When a cup with rewards ends (`CompetitionService`) | The prize (dated on the final) |
| `TicketSales` | Every home game played (not forfeits) | Attendance × ticket price |
| `ArenaCosts` | Every home game (operations); last day of each month (upkeep) | See §3 |
| `TravelCosts` | Every away game | Domestic or foreign trip |
| `Sponsorship` | Last day of each month | One twelfth of the budget's sponsorship deal |

The career calendar books the day (`FinanceService.BookDayAsync`) when it closes the game day, in the same transaction as
the date change, after every match of the day, the user's included.

### Matchday

* Attendance = capacity × min(0.95, 0.35 + 0.006 × reputation); playoff, second-phase, play-in, cup and
  international home games draw ×1.15 (up to capacity). Capacity: the home arena's, else 2,500.
* Ticket price = 6 + 0.3 × reputation (rep 50 → € 21, rep 90 → € 33).
* Matchday operations: € 3,000 + € 1.5 per seat. Arena upkeep a month: € 2,000 + € 1 per seat.
* Away trip: in the club's own country € 3,000 + € 50 × reputation; abroad (international games, cross-border
  leagues) € 12,000 + € 150 × reputation.

## 3. Budgets (`FinanceModel.Plan`)

The board sets each club's budget when a season is planned: at career start (after staff are hired), at the start of
every next season (`StartNextSeasonAsync`, after the objectives) and, for careers saved before Step 23, when the career
is loaded. Planning is idempotent (one budget per club and season). The wage budget is copied to `Club.WageBudget`,
which contracts, transfers and the AI read.

Expected figures: ticket sales (regular attendance × price × scheduled home games), arena costs (12 months of upkeep +
operations of every home game), travel (scheduled away games, domestic and abroad) and staff wages (staff contracts
running or starting).

* **First budget** of a club: the wage budget stays as in the data; the sponsorship deal is sized to cover wages and
  running costs less ticket sales, plus 5 % (minimum € 2,000 per reputation point). So imported clubs start in balance.
* **Renewals**: the deal changes by 2 % per reputation point gained or lost since the last budget, +3 % if the board's
  sporting objective was met, −3 % if missed (from the season review), limited to −15 % … +20 %. The operating surplus
  (sponsorship + tickets − running costs) goes 95 % to player wages, plus 10 % of a positive balance, or minus 50 % of a
  debt (repaid within the year). Wages never fall below 40 % of the surplus.
* **Transfer budget**: 50 % of a positive balance (0 in debt). Spending on fees, loan payments and signing bonuses this
  financial year uses it up; sales and loan income replenish it.
* **Financial target** (`MinimumBalance`): with money in the bank, end the year above the balance less the planned
  reserve spending less 5 % of the expected income (never below 0); in debt, halve the debt.

A **financial year** runs from 30 days before its season starts to the day before the next one's (preseason and the
summer window belong to the coming season).

## 4. Reports

`IFinanceService.GetReportAsync(club)` (the *Finances* tab): the financial year holding the game date, its budget,
income and expenditure by category, profit or loss, salary commitments (players: wage bill, salary still owed, 12-month
windows; staff wage bill), transfer budget left, the board's verdict (balance against the target) and the latest 50
bookings. `GetTransactionsAsync` returns the ledger of any period.

## 5. AI clubs

* The wage budget comes from the club's revenue (§3), and AI clubs keep within it when they sign, renew and buy
  (docs/AI_MANAGEMENT.md §4, §7): a club whose income falls gets a smaller budget and sells or releases players.
* In a transfer window the cash an AI club may spend is the lower of its balance and its transfer budget left.
* A club in debt gets no transfer budget and a wage budget cut to repay half of the debt, so debts do not build up
  season after season. Fees can never take a balance below zero (docs/TRANSFERS.md §5).

## 6. Persistence

`ClubBudgets` (one per club and season, unique) and `FinancialTransactions` (index club + date), migration
`Finances` (create-table only). See docs/DATABASE_SCHEMA.md.

## 7. Known limitations

* No broadcasting, merchandise, youth or facility income/costs; no loans from banks, no taxes, no fee amortisation.
* Neutral-venue games (Final Fours) are booked as home games of the nominal home club.
* Performance bonuses are calculated but still not paid (docs/CONTRACTS.md).
* The wage budget is enforced for AI clubs only; the user gets warnings and the board's verdict but no sanctions
  (board confidence and sackings are Step 27's).
* Sponsorship deals are yearly and implicit (no sponsor names, lengths or negotiations).
* The financial rules are code defaults (`FinanceModel`), not settings.
