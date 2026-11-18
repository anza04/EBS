# STEP 25 — Advanced contract negotiations

## 1. Status
- Completed 2026-11-18. Version 0.25.0.
- Build: PASS (0 warnings, 0 errors). Tests: see §5.

## 2. Objectives
Agent relationships, salary expectations, length preferences, playing-time and squad-role promises, bonuses, options,
release clauses, negotiation rounds, competing offers, player demands; AI agents and players acting on their own
objectives.

| Acceptance criterion | Where |
|---|---|
| Negotiations have multiple possible outcomes | Accept / counter (length, promises, clause, salary) / reject / break-off — `AdvancedNegotiationTests`, `NegotiationServiceTests` |
| Players can reject offers | `Evaluate` rejection below the agent's threshold, break-off after patience runs out |
| Players can counteroffer | `ContractNegotiator.Meet` (always acceptable) |
| Promises affect player satisfaction | `ReviewPromisesAsync`: morale ±, agent relationship, renewal premium — `Promises_are_reviewed_every_four_weeks_against_minutes_played` |
| AI negotiations are functional | AI renewals, emergency signings and transfer window meet demands — `CareerSimulationTests` (3 seasons) passed 3/3 |

## 3. Implementation summary
- Domain (`Ebm.Domain.Contracts`): `Agents.cs` (`Agent`, `AgentRelationship`, `AgentPool`), `Promises.cs`
  (`SquadRole`, `ContractPromises`, `PromiseStatus`, `PromiseRules`, `ContractNegotiation`), `ContractNegotiator`
  extended (`Demands`, `PreferredYears`, `OfferValue(offer, demands)`, `Meet`; `AskingSalary` unchanged),
  `ContractTerms.Promises`, `Contract` promise columns and reviews.
- Services: `IContractService.GetNegotiationAsync` (no round), `ReviewPromisesAsync` (daily from `CareerService`,
  `GameDayReport.Promises`); `NegotiateAsync`/`NegotiateRenewalAsync` persist rounds; `SignAsync`/`RenewAsync` refuse
  broken-off talks and close agreed talks. AI: `ClubManagementService` (renewal counter at most one year longer,
  emergency signings via `Meet`), `TransferService` AI window (free agents meet demands within the wage slack,
  purchases meet personal-terms demands).
- Editor and importer keep contract promises.
- UI: *Club contracts* — promised role/minutes, *His demands*, talks info, full counter-offer copy, *Promises* column.
- Formulas and AI behaviour: `docs/CONTRACTS.md` §4.

## 4. Files changed
- New: `src/Ebm.Domain/Contracts/Agents.cs`, `Promises.cs`; `src/Ebm.Persistence/Configurations/NegotiationConfigurations.cs`;
  migration `20261118081325_ContractNegotiations`; `tests/Ebm.Tests.Unit/Contracts/AdvancedNegotiationTests.cs`;
  `tests/Ebm.Tests.Integration/Contracts/NegotiationServiceTests.cs`.
- Modified: contract domain/terms/negotiator, `PeopleConfigurations`, snapshot, `ContractService`, `IContractService`,
  `CareerService`, `ICareerService`, `ClubManagementService`, `TransferService`, `DatabaseEditor`, `ImportSession`,
  `ClubContractsViewModel`, `Contracts.xaml`, tests (`ContractServiceTests`, `DatabaseInitializerTests`), docs
  (`CONTRACTS.md`, `DATABASE_SCHEMA.md`, `AI_MANAGEMENT.md`), `CHANGELOG.md`, `PROJECT_STATE.md`, `Directory.Build.props`.

## 5. Tests
- Unit: PASS 428/428 (20 new).
- Integration fast suite (`Category!=Launch&Category!=Slow`): PASS 150/150 (6 new). Launch: PASS.
- `CareerSimulationTests` (slow): PASS 3/3 after the AI transfer-window fix (one earlier failure: AI window signed free
  agents at market salary, now refused by the new demands; fixed).
- Other slow tests (`MilestoneTwoTests`, `EuropeanTransferWindowTests`, European datasets): **not run** on this step's
  code; the pending Step 24 confirmation run was stopped on request before finishing.

## 6. Known limitations
- Transfer personal terms: no rounds, agent or competing offers. Rival bids are a premium, not stored offers.
- Talks load the whole running-contract list per call (rank, rivals): heavier on the European dataset; check slow-suite
  timings.
- Promise morale is not yet used by the match engine (Step 26).

## 7. Handoff instructions
- Before Step 26 work (or the next release), run the slow suite one test class at a time:
  `MilestoneTwoTests`, `EuropeanTransferWindowTests`, European dataset tests; watch AI window duration.
- Step 26 (chemistry/morale) should build on `Player.Morale`, `PromiseRules` roles and `Contract.PromiseStatus`.

## 8. Outstanding issues
- Slow-suite confirmation pending (§5).
