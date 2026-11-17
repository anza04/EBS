# STEP 23 — Financial management

## 1. Status
- Status: COMPLETED
- Date: 2026-11-17
- Build version: 0.23.0
- Commit/reference: STEP 23 — Financial management
- Previous step: STEP 22 — Injuries and medical management
- Next step: STEP 24 — Milestone 2 integration and release

## 2. Objectives

From `docs/ROADMAP.md`, Step 23: club and wage budgets, player and staff salaries, transfer fees, signing bonuses,
sponsorship and ticket revenue, arena and travel costs, prize money, financial reports, board expectations; AI clubs
manage their finances and avoid systematic bankruptcy.

| Criterion | Proof |
|---|---|
| Financial transactions are recorded | `FinanceServiceTests.A_career_plans_budgets_and_books_every_income_and_expense` (every balance change of every club = sum of its ledger rows; all categories present), `Transfers_and_bonuses_are_booked_and_use_up_the_transfer_budget` |
| Budgets are updated | Same career test (a budget per club, wage budget copied, idempotent); `CareerServiceTests.A_full_season_is_played_and_the_next_one_is_generated` (next season's budgets); `FinanceModelTests.Renewed_sponsorship_follows_reputation_and_results_within_limits` |
| Salary commitments are calculated | `ClubFinanceReport.PlayerCommitments` / `StaffWageBill`, asserted in the career test |
| Revenue and expenditure reports are available | `IFinanceService.GetReportAsync`, *Finances* tab (`FinancesViewModel`, asserted in the career test) |
| AI clubs make financially constrained decisions | Transfer budget caps AI window spending (`TransferService.RunAiTransferWindowAsync`); `FinanceModelTests.The_board_spends_part_of_the_reserves_and_makes_a_club_in_debt_pay_it_back`, `A_crushing_debt_still_leaves_wages_for_the_squad`; transfer budget accounting in the transfer test |

## 3. Implementation summary

* **Domain (`Ebm.Domain/Finances`):** `FinanceCategory`, `FinancialTransaction` (ledger row), `ClubBudget` (season
  budget), `FinanceModel` (attendance, ticket price, matchday/upkeep/travel costs, budget planning, financial year,
  board target). `ClubFinances` loses the placeholder monthly income.
* **Services (`Ebm.Services/Finances`):** `Ledger.Book` (the only route for game money), `IFinanceService` /
  `FinanceService` (budget planning, reports, ledger queries, daily bookings, transfer budget left). Contract, transfer
  and competition services book through the ledger. `CareerService` books each day via `FinanceService.BookDayAsync`,
  plans budgets at start, next season (with the season's objective verdicts) and on load. `TransferService` caps AI
  cash by the transfer budget. `ManagementDay.Costs` added.
* **Persistence:** `FinanceConfigurations.cs`, migration `20261117150208_Finances` (create-table only).
* **Desktop:** *Finances* tab (`FinancesViewModel`, `Themes/Finances.xaml`).

## 4. Files changed
### Created
- `src/Ebm.Domain/Finances/` (`FinancialTransaction.cs`, `ClubBudget.cs`, `FinanceModel.cs`)
- `src/Ebm.Services/Finances/` (`IFinanceService.cs`, `FinanceService.cs`, `Ledger.cs`)
- `src/Ebm.Persistence/Configurations/FinanceConfigurations.cs`, `Migrations/20261117150208_Finances.cs` (+ designer)
- `src/Ebm.Desktop/ViewModels/Finances/FinancesViewModel.cs`, `Themes/Finances.xaml`
- `tests/Ebm.Tests.Unit/Finances/FinanceModelTests.cs`, `tests/Ebm.Tests.Integration/Finances/FinanceServiceTests.cs`
- `docs/FINANCES.md`, `logs/STEP_23_LOG.md`

### Modified
- Domain: `ClubPolicies` (`ClubFinances`)
- Services: `ServiceCollectionExtensions`, `CareerService`, `ICareerService`, `ClubManagementService` (bookings moved
  out), `ContractService`, `TransferService`, `CompetitionService`
- Persistence: model snapshot; Desktop: `App.xaml`, `MainWindow.xaml`, `DatabaseEditorViewModel`
- Tests: migration pins and table list, `CareerServiceTests` (next-season budgets)
- Docs: `DATABASE_SCHEMA.md`, `AI_MANAGEMENT.md`, `TRANSFERS.md`; `CHANGELOG.md`, `PROJECT_STATE.md`, `Directory.Build.props`

### Removed
- `ClubFinances.MonthlyIncome`, `ClubManagementService.AddFinancesAsync` (replaced by the finance service)

## 5. Tests
- Build: Debug and Release, 0 warnings, 0 errors.
- Unit: 408/408 PASS (8 new).
- Integration, fast suite (`Category!=Launch&Category!=Slow`): 144/144 PASS in 3 min 35 s (2 new; the career finance
  test plays five weeks of the sample career).
- Launch test: PASS.
- Slow suite: not run (Step 24 runs it for the milestone release). The three-season simulation asserts AI balances stay
  ≥ 0, which now depends on the revenue model.

## 6. Known limitations
See `docs/FINANCES.md` §7: no broadcasting/merchandise income, bank loans, taxes or amortisation; neutral venues booked
as home games; wage budget not enforced for the user; no board sanctions; implicit yearly sponsorship deals; rules are
code defaults.

## 7. Handoff instructions
Step 24 (milestone integration): run the slow suite in the background; check AI balances over three seasons on the
European dataset (`CareerSimulationTests` asserts balances ≥ 0). If clubs drift into debt, tune `FinanceModel`
(`WageShare`, `DebtRepayment`) and docs/FINANCES.md together. Step 25 (negotiations) can read the transfer budget via
`IFinanceService.GetReportAsync(...).TransferBudgetLeft`.

## 8. Outstanding issues
- None blocking.
