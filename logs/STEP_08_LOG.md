# STEP 08 — Transfers and free agents

**Date:** 2026-11-09
**Version:** 0.8.0
**Status:** Completed. All acceptance criteria met.

## 1. Objectives

From `docs/ROADMAP.md`, Step 08: implement the player market — free-agent list, player search and filters, transfer
offers, fee negotiation, contract negotiation, player acceptance and rejection, loans, loan returns, contract
buy-outs, transfers between clubs; AI clubs that identify roster gaps, search, sign free agents, sell, negotiate and
respect financial limits (simple rules).

Acceptance criteria and where they are proven:

| Criterion | Proof |
|---|---|
| Players can move between clubs | `TransferServiceTests.A_player_under_contract_is_bought_for_an_accepted_fee` (fee, contracts, spells, roster spot, history), `A_release_clause_forces_the_sale`, render test buys a player through the *Transfer market* screen |
| Free agents can be signed | `Free_agent_signings_are_part_of_the_transfer_history`, `AI_clubs_fill_their_rosters_with_free_agents_within_their_budgets`; market screen *Sign the free agent* (Step 07 signing reused) |
| Transfers update player-club history | Spell assertions in the purchase, loan and recall tests; `Transfers` rows for every move |
| Contracts are updated correctly | Old contract `Transferred` on the transfer date, new contract active; loan: parent `OnLoan` → `Active`, loan contract expired or terminated (`A_loan_moves_the_player_and_he_comes_back_when_it_ends`, `A_starter_is_not_loaned_and_a_recall_brings_a_player_back_early`, `Startup_processing_returns_loans_before_applying_contract_dates`) |
| AI clubs can complete transactions | `AI_clubs_fill_their_rosters…`, `AI_clubs_sell_their_surplus_to_each_other_and_release_what_nobody_buys`, `ClubRecruiterTests` |
| Financial and roster restrictions are respected | `A_refused_fee_an_empty_wallet_or_a_full_squad_blocks_the_transfer_and_nothing_is_saved`, wage-budget assertions in the AI tests, `ClubRecruiterTests.Financial_and_registration_limits_are_respected` |

## 2. Files

### Created
| File | Purpose |
|---|---|
| `src/Ebm.Domain/Transfers/TransferId.cs`, `Transfer.cs` | `Transfer` entity, `TransferKind`, `Loan.MaxYears` |
| `src/Ebm.Domain/Transfers/TransferNegotiator.cs` | Market value, asking fee, fee and loan answers (`ValuationContext`, `PlayerValuation`, `TransferResponse`) |
| `src/Ebm.Domain/Transfers/ClubRecruiter.cs` | AI rules: surplus, needs, plan (`ClubSituation`, `SquadMember`, `Candidate`, `RecruitmentPlan`) |
| `src/Ebm.Persistence/Configurations/TransferConfigurations.cs` | `Transfers` mapping, check constraints, indexes |
| `src/Ebm.Persistence/Migrations/20261109204206_Transfers*.cs` | Migration |
| `src/Ebm.Services/Transfers/ITransferService.cs` | Use cases and view records (`PlayerSearch`, `MarketPlayer`, `TransferReceipt`, `TransferLine`, `ClubTransfers`, `TransferDateChanges`, `TransferWindowReport`) |
| `src/Ebm.Services/Transfers/TransferService.cs` | Implementation (market snapshot, purchase, loan, recall, returns, AI window) |
| `src/Ebm.Desktop/ViewModels/Transfers/TransferMarketViewModel.cs` | *Transfer market* screen |
| `src/Ebm.Desktop/Themes/Transfers.xaml` | Its template |
| `docs/TRANSFERS.md` | Transfer lifecycle, negotiation formulas, limits, AI recruitment, screen |
| `tests/Ebm.Tests.Unit/Transfers/TransferRulesTests.cs`, `TransferNegotiatorTests.cs`, `ClubRecruiterTests.cs` | Unit tests |
| `tests/Ebm.Tests.Integration/Transfers/TransferServiceTests.cs` | Service tests |
| `logs/STEP_08_LOG.md` | This log |

### Modified
| File | Change |
|---|---|
| `src/Ebm.Domain/Contracts/ContractEnums.cs`, `Contract.cs` | Statuses `OnLoan`, `Transferred`; `Transfer`, `SendOnLoan`, `ReturnFromLoan`; on-loan message in `RequireActive` |
| `src/Ebm.Domain/Contracts/SalaryCommitments.cs` | Skips `OnLoan` contracts |
| `src/Ebm.Domain/Rosters/SquadRules.cs` | `Strictest(rules)` |
| `src/Ebm.Services/Contracts/ContractService.cs` | `SignAsync` writes the `FreeAgentSigning` transfer row; status names in messages |
| `src/Ebm.Services/Contracts/IContractService.cs` | `ContractLine.StatusText` uses display names ("On loan", "Transferred") |
| `src/Ebm.Services/Clubs/ClubService.cs` | Club profile wage bill skips `OnLoan` contracts |
| `src/Ebm.Services/ServiceCollectionExtensions.cs` | Registers `TransferNegotiator.Default`, `ClubRecruiter.Default`, `ITransferService` |
| `src/Ebm.Persistence/Migrations/EbmDbContextModelSnapshot.cs` | Snapshot |
| `src/Ebm.Desktop/App.xaml(.cs)`, `MainWindow.xaml`, `ViewModels/Editor/DatabaseEditorViewModel.cs` | Theme, startup `ProcessTransferDatesAsync`, *Transfer market* tab, view-model wiring |
| `docs/CONTRACTS.md`, `DATABASE_SCHEMA.md`, `ROSTER_RULES.md`, `IMPORT_FORMATS.md`, `README.md`, `CHANGELOG.md`, `PROJECT_STATE.md` | Documentation |
| `Directory.Build.props` | Version 0.8.0 |
| Tests: `DatabaseInitializerTests`, `ContractServiceTests`, `DatabaseEditorTests`, `PlayerScreensRenderTests` | Latest migration and table list, editor constructor, market screen flow (search, ask, buy, AI window) |

### Removed
None.

## 3. Features implemented

* **Transfer history** (`Transfers`): free-agent signings, transfers, release-clause buy-outs, loans, loan returns.
* **Valuation and fee negotiation** (`docs/TRANSFERS.md` §2): market value, asking fee, accept / counter / reject,
  binding release clause, loan availability and fees.
* **Permanent transfers** (§3): fee between balances, old contract `Transferred`, new contract (player acceptance with
  his old salary as the floor), squad move, limits.
* **Loans** (§4): parent contract `OnLoan`, loan contract at the borrower on the same salary, fee, automatic return
  after the end date (`ProcessTransferDatesAsync`, before contract expiries), early recall.
* **Limits** (§5): balance, registration limits of the club and its competitions, wage budget (warning / AI hard limit).
* **AI recruitment** (§6): surplus listing, needs, signings within budgets, free agents preferred, sales between AI
  clubs, releases of unsold surplus, per-club report. Managed clubs are never touched.
* **Search** (§7) with filters, priced for the searching club.
* **Screen** *Transfer market*; startup processing extended.

## 4. Architectural decisions

Recorded in `PROJECT_STATE.md` as decisions 33–37:

33. Every movement is a `Transfer` row written in the same unit of work; never deleted. `SignAsync` writes the
    free-agent rows.
34. Transfers end the old contract as `Transferred`; loans keep the parent contract `OnLoan` plus a loan contract;
    `OnLoan` contracts are skipped by wage bills and commitments. Loans are the only case of two running contracts.
35. `TransferNegotiator` and `ClubRecruiter` are pure, deterministic, parameterised domain code; AI clubs never
    trade with managed clubs.
36. Limits: fees never take a balance below zero; registration limits block; wage budget warns the user, binds the
    AI; only problems the newcomer causes are reported.
37. `ProcessTransferDatesAsync` is the single "apply the calendar" entry point (loans, then contracts).

Other choices:
* The loan state lives in `Contract.Status` (`OnLoan`) plus the `Transfers` row (loan club, end date, loan contract,
  parent contract), instead of new contract columns: no change to the `Contracts` table, and `RunningOn` keeps
  working unchanged (the player stays bound to his parent club).
* Offers are not persisted: every negotiation answers at once (like Step 07 contracts). Persisted, pending offers
  belong to the career mode, when there is a user-controlled club to receive them.
* The player does not negotiate a loan (the loan contract copies his salary); clubs negotiate the fee only.
* Market pricing is based on `ContractNegotiator.AskingSalary` at a neutral club reputation, so salaries and values
  share one scale.
* Market data is loaded once per call into an in-memory `Market` snapshot (players, contracts, spells, spots,
  wage bills); the AI window reloads it after each club's deals.

## 5. Database changes

Migration `20261109204206_Transfers`: new table `Transfers` (create-table only; no change to existing tables, so the
upgrade runs in one transaction on SQLite). Columns, constraints and indexes: `docs/DATABASE_SCHEMA.md` → *Transfers*.
`Contracts.Status` gains the values `OnLoan` and `Transferred` (text column, no schema change). 20 tables.

## 6. Tests

| Run | Result |
|---|---|
| `dotnet build` (Debug) | PASS, 0 warnings, 0 errors |
| `dotnet build -c Release` | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including the launch test) | PASS 358/358 (257 unit, 101 integration) |

New tests: 17 unit (transfer and loan rules, commitments on loan, strictest rules, negotiator, recruiter) and
10 integration (transfer service), plus the extended render test (market screen: search, ask the club and the
player, buy, AI window; screenshots reviewed with `EBM_SCREENSHOT_DIR`).

## 7. Known limitations

* No persisted offers: AI clubs never bid for a managed club's players; the user's club receives no offers yet.
* AI clubs buy only free agents and the surplus other AI clubs list; valuations ignore form, statistics and
  nationality; no transfer windows as calendar periods (any day is a market day until Step 12).
* Loans copy the salary (no salary sharing, no option to buy); the player does not negotiate a loan; a loaned player
  cannot be bought until recalled; a loan contract terminated by the loan club through *Club contracts* does not
  close the loan (use *Recall* on the parent club's market screen).
* The wage budget is enforced for AI clubs only; the user gets a warning (Step 27 decides the board's rules).
* Transfer history is not importable; the `Transfers` table is created by the game only.
* Fees are booked to the balances at once; no amortisation, no agent fees (Step 27).
* The market screen's filter combo shows enum names (`UnderContract`); the editor's *Contracts (editor)* grid shows
  raw status names (`OnLoan`).

## 8. Outstanding issues

* Off-screen renders collapse star-width columns of the editor grids (pre-existing; the running app is fine).
* The market results grid needs horizontal scrolling at 1280 px for the value, fee, clause and loan columns.

## 9. Instructions for the next step

1. Read `PROJECT_STATE.md`, this log and `docs/TRANSFERS.md` (§9 lists the hooks for later steps), then the Step 09
   specification in `docs/ROADMAP.md`.
2. Everything that moves players should keep going through `IContractService` / `ITransferService` and
   `ClubMembership`; read "under contract" with `Contract.RunningOn`, and remember `OnLoan` contracts when summing
   wages.
3. When a game calendar exists (Step 12), call `ITransferService.ProcessTransferDatesAsync` every game day and make
   `RunAiTransferWindowAsync(managedClubs)` run in the transfer windows with the user's club in `managedClubs`.
4. Add a migration only through `dotnet ef migrations add`; prefer add-column / create-table changes.

## 10. Build and run status

* Build: PASS (Debug and Release, 0 warnings).
* Tests: PASS 358/358.
* Run: the application launches (launch test), migrates the database to `20261109204206_Transfers`, applies loan
  returns and contract dates at startup and opens the editor with the new *Transfer market* tab.
