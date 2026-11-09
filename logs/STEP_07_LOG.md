# STEP 07 — Contracts and player availability

**Date:** 2026-11-09
**Version:** 0.7.0
**Status:** Completed. All acceptance criteria met.

## 1. Objectives

From `docs/ROADMAP.md`, Step 07: implement the contract system.

* Contract fields: start date, end date, salary, payment frequency, signing bonus, performance bonuses, release
  clause, club option, player option, contract status.
* New contracts, renewals, expiry, termination, contract history, player free-agent status, salary commitments.
* Contract negotiations with simplified calculations.

Acceptance criteria and where they are proven:

| Criterion | Proof |
|---|---|
| Players can sign contracts | `ContractServiceTests.A_free_agent_signs_joins_the_squad_and_the_club_pays_the_signing_bonus`, `A_player_refuses_a_low_offer_and_nothing_is_saved`; the render test signs a free agent through the *Club contracts* screen |
| Contracts are linked to clubs | Same tests (club contracts view, wage bill, squad shows the contract); `A_contracted_player_can_only_agree_a_contract_that_starts_after_his_current_one` |
| Contract expiry is handled correctly | `An_expired_contract_frees_the_player_and_stays_in_the_history`, pre-contract test (old contract expires, new one moves the player), `ContractRulesTests.Expiry_is_recorded_only_after_the_end_date` |
| Terminated players become available | `Releasing_a_player_pays_his_remaining_salary_and_makes_him_a_free_agent` (free-agent list, spell closed, signs elsewhere at once) |
| Salary commitments are calculated | `SalaryCalculatorTests` (accrual, schedules, early end), `Club_commitments_add_running_and_upcoming_contracts`, club view wage bill / budget left |
| Historical contracts remain accessible | Expiry, termination and renewal tests read the history from `GetClubContractsAsync` and `GetPlayerContractsAsync`; contracts are never deleted |

## 2. Environment note

The machine had only the .NET runtime, not the SDK pinned in `global.json`. With the user's approval, SDK 10.0.203
was installed user-locally with Microsoft's `dotnet-install.ps1` into `%USERPROFILE%\.dotnet` (not on `PATH`). To
build in a new shell:

```bash
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH      # Git Bash
$env:DOTNET_ROOT="$env:USERPROFILE\.dotnet"; $env:PATH="$env:DOTNET_ROOT;$env:PATH"   # PowerShell
dotnet tool restore
```

`logs/STEP_06_LOG.md` (and the earlier logs) did not exist in the repository: the Visual Studio `.gitignore`
ignored every `logs/` folder. This step re-includes the root `logs/` folder (`!/logs/`). Step 06 was inspected
from `PROJECT_STATE.md`, `CHANGELOG.md`, `docs/ROSTER_RULES.md` and the code.

## 3. Files

### Created
| File | Purpose |
|---|---|
| `src/Ebm.Domain/Contracts/ContractEnums.cs` | `PaymentFrequency`, `ContractStatus`, `OptionHolder`, `TerminationKind` |
| `src/Ebm.Domain/Contracts/ContractTerms.cs` | Negotiable terms record |
| `src/Ebm.Domain/Contracts/PerformanceBonuses.cs` | `BonusCondition` and the immutable bonus set (parse/format) |
| `src/Ebm.Domain/Contracts/SalaryCalculator.cs` | Accrual, remaining/total salary, payment schedule, next payment |
| `src/Ebm.Domain/Contracts/SalaryCommitments.cs` | Club commitments: wage bill, owed salary, 12-month periods, bonuses |
| `src/Ebm.Domain/Contracts/ContractNegotiator.cs` | Asking salary, offer value, accept/counter/reject, player option rule |
| `src/Ebm.Services/Contracts/IContractService.cs` | Use cases and view records (`ContractLine`, `ClubContracts`, `PlayerContracts`, `FreeAgent`, `ContractDateChanges`) |
| `src/Ebm.Services/Contracts/ContractService.cs` | Implementation |
| `src/Ebm.Services/Clubs/ClubMembership.cs` | Shared join/leave/current-club rules (spells + roster spots) |
| `src/Ebm.Persistence/Migrations/20261109194813_Contracts*.cs` | Migration |
| `src/Ebm.Desktop/ViewModels/Contracts/ClubContractsViewModel.cs` | *Club contracts* screen |
| `src/Ebm.Desktop/Themes/Contracts.xaml` | Its template |
| `docs/CONTRACTS.md` | Contract lifecycle, salary calculations, negotiation, transaction rules |
| `tests/Ebm.Tests.Unit/Contracts/ContractRulesTests.cs` | Contract and bonus rules |
| `tests/Ebm.Tests.Unit/Contracts/SalaryAndNegotiationTests.cs` | Salary, commitments, negotiation |
| `tests/Ebm.Tests.Integration/Contracts/ContractServiceTests.cs` | Service, editor, upgrade |
| `tests/Ebm.Tests.Integration/TestSupport/TestClock.cs` | Movable test clock |
| `logs/STEP_07_LOG.md` | This log |

### Modified
| File | Change |
|---|---|
| `src/Ebm.Domain/Contracts/Contract.cs` | New terms, status, early end, settlement; `RunningOn`/`RunningOrUpcomingOn` queries; `Terminate`, `Supersede`, `Expire`, `ExerciseOption`, `DeclineOption`, `StatusOn`, `EffectiveEndDate` |
| `src/Ebm.Domain/Clubs/Club.cs` | `ChangeBalance` |
| `src/Ebm.Persistence/Configurations/PeopleConfigurations.cs` | Contract column mappings and defaults, index (`ClubId`, `EndDate`) |
| `src/Ebm.Persistence/Migrations/EbmDbContextModelSnapshot.cs` | Snapshot |
| `src/Ebm.Services/Clubs/ClubService.cs` | Uses `ClubMembership` and `Contract.RunningOn`; wage bill counts player contracts only |
| `src/Ebm.Services/Players/PlayerProfileService.cs` | Active contract respects early ends |
| `src/Ebm.Services/Editing/IDatabaseEditor.cs`, `DatabaseEditor.cs` | `ContractDraft` with all terms |
| `src/Ebm.Services/ServiceCollectionExtensions.cs` | Registers `ContractNegotiator.Default`, `IContractService` |
| `src/Ebm.DataImport/Sources/ImportFields.cs`, `Validation/ImportSession.cs` | Optional contract fields |
| `src/Ebm.Desktop/App.xaml(.cs)`, `MainWindow.xaml` | Theme, startup contract processing, *Club contracts* tab, editor tab renamed |
| `src/Ebm.Desktop/ViewModels/Editor/*` | Editor contract form fields and status; shell VM wiring |
| `src/Ebm.Desktop/ViewModels/Players/PlayerViewModels.cs`, `Themes/Players.xaml` | Contract status and history on the profile |
| `src/Ebm.Desktop/Themes/Editor.xaml`, `Themes/Clubs.xaml` | Editor form fields; squad hint text |
| `data/sample/italy-sample.json`, `data/templates/contracts.csv`, `dataset-template.json` | Free agents and new contract fields |
| `docs/DATABASE_SCHEMA.md`, `IMPORT_FORMATS.md`, `ROSTER_RULES.md`, `README.md`, `CHANGELOG.md`, `PROJECT_STATE.md` | Documentation |
| `Directory.Build.props` | Version 0.7.0 |
| `.gitignore` | Root `logs/` re-included |
| Tests: `DatabaseInitializerTests`, `ImportTests`, `DatabaseEditorTests`, `PlayerProfileTests`, `PlayerScreensRenderTests` | Latest migration, sample counts, new constructors, contract import and screen checks |

### Removed
None.

## 4. Features implemented

* **Contract terms** (`ContractTerms`): dates (max 10 years), salary, payment frequency, signing bonus,
  performance bonuses, release clause, club/player option dates (max 3 years beyond the end).
* **Lifecycle** (`docs/CONTRACTS.md` §2): `Active` → `Expired` / `Terminated` / `Renewed`; early end recorded in
  `TerminationDate`; options extend `EndDate`.
* **Signing** with overlap check, player acceptance, signing bonus payment and squad entry; pre-contracts.
* **Renewal** (contiguous, accepted by the player, old contract kept as `Renewed`).
* **Termination**: club release (remaining salary) or mutual agreement (agreed settlement); player leaves and becomes
  a free agent.
* **Options**: club decides; player decides by market value.
* **Expiry and start processing** at startup (`ProcessContractDatesAsync`).
* **Free agents**: no running or upcoming contract; list for the signing form; status on the player profile.
* **Salary calculations** and **club commitments**.
* **Negotiation**: asking salary, offer value, accept / counter-offer / reject.
* **Screens**: *Club contracts*, player profile contract history, editor fields.

## 5. Architectural decisions

Recorded in `PROJECT_STATE.md` as decisions 27–32:

27. One "running" rule: `Contract.RunningOn(date)`; early ends set `TerminationDate`, keep `EndDate`; no deletes.
28. Lifecycle only through `IContractService`; editor and importer remain data tools (terms only).
29. One-off payments booked to `Club.Balance` immediately; salaries/bonuses calculated, booked from Steps 12/27.
30. Contract dates applied by the idempotent `ProcessContractDatesAsync` (startup now; daily from Step 12).
31. Deterministic, parameterised negotiation (`ContractNegotiator`).
32. Membership moves shared in `ClubMembership`.

Other choices:
* Status is stored only for lifecycle events (`Expired` is set by processing; `StatusOn` reports an unprocessed
  past-end contract as expired), so queries never depend on processing having run.
* Performance bonuses are a value object stored in one text column (like `PositionSet`), not a new table: the set
  is small, always loaded with the contract, and the add-column migration keeps SQLite from rebuilding `Contracts`.
* Salary accrues per contract year (anniversaries), so full years always equal the annual salary and leap years
  need no special case; instalments are differences of rounded running totals.
* Players with no club history (imported contracts only) are not given spells by the processing: they keep
  belonging through the contract, as in Step 06.

## 6. Database changes

Migration `20261109194813_Contracts` (add-column only, no table rebuild):

| Column | Type | Default |
|---|---|---|
| `PaymentFrequency` | TEXT(16) | `Monthly` |
| `SigningBonus` | INTEGER | 0 |
| `PerformanceBonuses` | TEXT(200) | `''` |
| `ReleaseClause` | INTEGER NULL | — |
| `ClubOptionUntil`, `PlayerOptionUntil` | TEXT (date) NULL | — |
| `Status` | TEXT(16) | `Active` |
| `TerminationDate` | TEXT (date) NULL | — |
| `TerminationCompensation` | INTEGER | 0 |

Index `IX_Contracts_ClubId` replaced by `IX_Contracts_ClubId_EndDate`. Upgrade of Step 06 data verified by
`Step_06_contracts_upgrade_as_active_contracts_with_default_terms`. Still 19 tables.

## 7. Tests

| Run | Result |
|---|---|
| `dotnet build` (Debug) | PASS, 0 warnings, 0 errors |
| `dotnet build -c Release` | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including the launch test) | PASS 331/331 (240 unit, 91 integration) |

New tests: 32 unit (contract rules, bonuses, salary, commitments, negotiation) and 12 integration (11 contract
service/editor/upgrade tests, 1 import field-error test), plus extended import and render tests. Screens rendered
off-screen with `EBM_SCREENSHOT_DIR` and reviewed (club contracts, player profile, contracts editor).

## 8. Known limitations

* Salaries and performance bonuses are calculated but not paid from the club balance (needs the calendar,
  Step 12, and finances, Step 27). Only signing bonuses and settlements move money.
* The wage budget is reported (budget left / over budget), not enforced.
* Options are not decided automatically; an undecided option lapses at expiry. No reminders before Step 12.
* A player under contract cannot be signed during his contract (buy-outs, release clauses and transfers: Step 08).
* The editor and the importer do not check overlapping contracts and do not move players or charge money.
* Staff contracts share the entity and expire, but have no screens or service use cases.
* Free agents are listed by overall only; search and filters are Step 08.
* Negotiation ignores morale, playing time, nationality and competing offers; AI clubs do not negotiate yet (Step 08).

## 9. Outstanding issues

* Off-screen renders collapse star-width columns of the editor grids (pre-existing; the running app is fine).
* The *Club contracts* grid needs horizontal scrolling at 1280 px to show the release clause and bonus columns.

## 10. Instructions for the next step (Step 08 — Transfers and free agents)

1. Read `PROJECT_STATE.md`, this log and `docs/CONTRACTS.md` (§8 lists the hooks for transfers).
2. Free-agent list: extend `IContractService.GetFreeAgentsAsync` (or a new market service) with search and
   filters; free agents sign through `SignAsync`.
3. Transfers/buy-outs: pay the fee (`Contract.ReleaseClause` or negotiated) between club balances
   (`Club.ChangeBalance`), end the old contract (add a `TerminationKind`/status for transfers rather than reusing
   `ClubRelease`, so history tells them apart), sign the new contract and move the player with `ClubMembership`
   — all in one unit of work. Player acceptance reuses `ContractNegotiator`.
4. Loans need a link from the loan contract (or a new entity) to the parent club and a return date; returning uses
   `ClubMembership.JoinAsync`.
5. AI clubs: roster gaps from `RosterValidator`/`ClubRoster`, budgets from `SalaryCommitments` and `Club.WageBudget`;
   enforce financial limits here (Step 07 only reports them).
6. Keep using `Contract.RunningOn` for "under contract"; add a migration only through `dotnet ef migrations add`.

## 11. Build and run status

* Build: PASS (Debug and Release, 0 warnings).
* Tests: PASS 331/331.
* Run: the application launches (launch test), migrates the database to `20261109194813_Contracts`, applies
  contract dates at startup and opens the editor with the new *Club contracts* tab.
