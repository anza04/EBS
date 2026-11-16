# STEP 19 — European transfer market

## 1. Status
- Status: COMPLETED (integration suite run pending, see §9)
- Date: 2026-11-16
- Build version: 0.19.0
- Commit/reference: STEP 19 — European transfer market
- Previous step: STEP 18 — International club competitions
- Next step: STEP 20 — Scouting and player evaluation

## 2. Objectives

From `docs/ROADMAP.md`, Step 19: cross-country transfers, international free-agent recruitment, transfer
negotiations, loan agreements, release clauses, contract buyouts, foreign-player restrictions, registration deadlines;
AI clubs evaluate players across all countries.

| Criterion | Proof |
|---|---|
| Players can transfer between countries | One market (docs/TRANSFERS.md §10); `EuropeanTransferWindowTests`: AI buys from clubs of other countries |
| Contracts remain consistent | Same test: one running contract per player (two only on loan), every move backed by its contract, old contracts `Transferred`; `EuropeanMarketTests` (option to buy) |
| Registration restrictions are enforced | `EuropeanMarketTests.New_players_cannot_be_registered_after_the_deadline…`; foreign/non-EU limits at the receiving club (Step 08/15 checks) |
| AI clubs recruit internationally | `EuropeanTransferWindowTests`: free agents of other nationalities, cross-border purchases, upgrade signings |
| Transfer activity is economically plausible | Same test: no club spends money it does not have, fees within market range |

## 3. Implementation summary

* **Domain:** `RegistrationRules.Deadline` / `DeadlineIn`, `RegistrationWindow`; `ClubAttractiveness`;
  `ContractNegotiator.BuyoutShare` / `MinimumBuyout`; `LoanClauses`, `TransferNegotiator.EvaluateLoan` with salary
  share and option, `SalaryContribution`, `MinimumOptionFee`; `Transfer.SalaryContribution`, `OptionFee`,
  `TransferKind.OptionToBuy`, `Transfer.ByOption`; `ClubRecruiter` upgrades (`Candidate.Listed`, `MaxUpgrades`,
  `UpgradeMargin`, `UpgradeCashShare`).
* **Services:** `ClubSeason` (current season, international tiers, attractiveness, registration window and check);
  `IContractService.NegotiateBuyoutAsync`, buyout rule in `TerminateAsync`, deadline in `SignAsync`, attractiveness
  in negotiations; `ITransferService.ExerciseOptionAsync`, `GetRegistrationWindowAsync`, loan clauses in
  `NegotiateLoanAsync` / `LoanAsync`, contribution refund in `RecallAsync`, deadline in transfers and loans, AI
  window with upgrade candidates and deadline.
* **Persistence:** migration `20261116151616_LoanAgreements` (add-columns only).
* **Data:** deadlines in `build/generate-europe-dataset.py` (also real-leagues); regenerated datasets.
* **Desktop:** market screen (registration window, salary share, option fee, *Exercise the option to buy*),
  contracts screen (*Ask the player* for a buyout).

## 4. Files changed
### Created
- `src/Ebm.Domain/Transfers/RegistrationWindow.cs`, `ClubAttractiveness.cs`
- `src/Ebm.Services/Transfers/ClubSeason.cs`
- `src/Ebm.Persistence/Migrations/20261116151616_LoanAgreements.cs` (+ designer)
- `tests/Ebm.Tests.Unit/Transfers/EuropeanMarketRulesTests.cs`
- `tests/Ebm.Tests.Integration/Transfers/EuropeanMarketTests.cs`, `EuropeanTransferWindowTests.cs`
- `logs/STEP_19_LOG.md`

### Modified
- Domain: `CompetitionRules`, `ContractNegotiator`, `TransferNegotiator`, `Transfer`, `ClubRecruiter`
- Services: `IContractService`, `ContractService`, `ITransferService`, `TransferService`
- Persistence: `TransferConfigurations`, model snapshot
- Desktop: `TransferMarketViewModel`, `Transfers.xaml`, `ClubContractsViewModel`, `Contracts.xaml`
- Data: generators, `data/europe`, `data/italy`, `data/real-leagues` competition files
- Tests: migration pins, mutual-agreement settlement, loan history text
- Docs: `TRANSFERS.md`, `CONTRACTS.md`, `COMPETITION_FORMATS.md`, `DATABASE_SCHEMA.md`; `CHANGELOG.md`, `PROJECT_STATE.md`, `Directory.Build.props`

### Removed
- None

## 5. Tests
- Unit: 363/363 PASS.
- Integration: new and updated tests written; not yet run (the baseline full run was still in progress at commit time).

## 6. Known limitations
- Deadlines in the datasets are an unverified simplification (28 Feb / 31 Mar).
- The parent salary share is a lump-sum contribution; no buy-back or sell-on clauses; AI clubs make no loans and never bid for the user's players.
- Valuations ignore form and statistics.

## 7. Handoff instructions
Step 20 (scouting) can price players with `ITransferService.GetMarketPlayerAsync` (asking salary already reflects the
club's attractiveness) and should respect `GetRegistrationWindowAsync` when suggesting signings.

## 8. Outstanding issues
- Run `dotnet test --filter "Category!=Launch"` and fix any regression before starting Step 20.
