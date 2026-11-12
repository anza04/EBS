# STEP 13 — AI club management and career testing

**Date:** 2026-11-12
**Version:** 0.13.0
**Status:** Completed. All acceptance criteria met.

## 1. Objectives

From `docs/ROADMAP.md`, Step 13: AI management of rosters, starting lineups, rotations, tactics, contracts, transfers,
free agents, finances and season objectives; simulated careers to find incomplete rosters, unrealistic transfers,
financial instability, excessive turnover, inconsistent results and broken progression.

| Criterion | Proof |
|---|---|
| AI clubs operate without user intervention | `CareerSimulationTests` (three seasons, only `ContinueAsync`/`PlayUserMatchAsync`/`StartNextSeasonAsync`) |
| AI clubs maintain valid rosters | Same test: every AI club 10–18 players and five starters each season; no forfeits |
| AI clubs can recruit and release players | Transfers, renewals, replenishment and emergency signings counted in the test; releases above 18 (`ReleaseSurplusAsync`) |
| AI clubs select lineups and tactics | Five starters and the selected "AI plan" tactic checked each season; `ManagementPlannerTests` |
| Multiple seasons can be simulated without critical errors | Three seasons, two season transitions |

## 2. Files

### Created
| File | Purpose |
|---|---|
| `src/Ebm.Domain/Management/LineupPlanner.cs` | AI squad roles |
| `src/Ebm.Domain/Management/TacticPlanner.cs` | AI tactic |
| `src/Ebm.Domain/Management/ClubPolicies.cs` | `SeasonObjectives`, `ClubFinances`, `RenewalPolicy` |
| `src/Ebm.Domain/Management/PlayerGenerator.cs` | Fictional free agents |
| `src/Ebm.Services/Management/IClubManagementService.cs`, `ClubManagementService.cs` | AI use cases, daily finances |
| `docs/AI_MANAGEMENT.md` | Handoff documentation (rules, results, weaknesses) |
| Tests `Integration/Careers/CareerSimulationTests.cs`, `Unit/Management/ManagementPlannerTests.cs` | |

### Modified
`CareerService` / `ICareerService` (AI hooks, `ManagementDay`, objectives in the review, finances with the date move),
`ServiceCollectionExtensions`, `CareerViewModel` (AI news), `Directory.Build.props`, `README.md`, `CHANGELOG.md`,
`PROJECT_STATE.md`, `docs/SEASON_LIFECYCLE.md`.

### Removed
None.

## 3. Features implemented

See `docs/AI_MANAGEMENT.md`: AI line-ups before every matchday, AI tactics, renewals, releases, transfer windows
(offseason, preseason, emergency), free-agent replenishment, emergency signings, salary payments and income for all
clubs, board objectives set and judged.

## 4. Architectural decisions

Decisions 44–46 in `PROJECT_STATE.md`:
* AI decisions are pure domain planners; `IClubManagementService` applies them through the existing use cases
  (`IClubService`, `ITacticsService`, `IContractService`, `ITransferService`), so AI clubs obey the same rules as the user.
* Finances are booked when the career date moves (one transaction): salary instalments on due dates, placeholder income.
* The market is kept supplied by generated players (names from the database, never hard-coded).

## 5. Database changes

None (schema `20261112144630_Careers`). AI tactics are ordinary `Tactics` rows named "AI plan"; generated players are
ordinary `Players` rows.

## 6. Tests

| Run | Result |
|---|---|
| `dotnet build` (Debug, Release) | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including launch) | PASS 402/402 (291 unit, 111 integration) |

Simulation statistics: `docs/AI_MANAGEMENT.md` §8.

## 7. Known limitations

`docs/AI_MANAGEMENT.md` §9: no youth/retirement, placeholder income (balances drift up), no AI loans or bids for the
user's players, tactics not per opponent. The simulation test takes about two minutes.

## 8. Outstanding issues

None.

## 9. Instructions for the next step

1. Read `PROJECT_STATE.md`, this log, `docs/SEASON_LIFECYCLE.md` and `docs/AI_MANAGEMENT.md`, then the Step 14 specification.
2. Step 14 needs save files: the game database is the save (one career per database); add save/load (copy/open
   database files under `<DataRoot>\saves`) and a Windows executable (`dotnet publish`).
