# STEP 18 — International club competitions

## 1. Status
- Status: COMPLETED
- Date: 2026-11-16
- Build version: 0.18.0
- Commit/reference: STEP 18 — International club competitions
- Previous step: STEP 17 — Domestic cups and additional competitions
- Next step: STEP 19 — European transfer market

## 2. Objectives

From `docs/ROADMAP.md`, Step 18: EuroLeague, EuroCup, Basketball Champions League and FIBA Europe Cup with
qualification rules, participating clubs, registration, group or league phases, playoffs, finals, international
calendars, prize money and reputation rewards; domestic and international competitions overlapping.

| Criterion | Proof |
|---|---|
| Clubs can participate in multiple competitions | `InternationalCompetitionTests.Clubs_play_league_domestic_cup_and_international…`: clubs play Serie A, the domestic cup and an international cup in one career season |
| International fixtures are generated | Group (league) phases and knockout rounds drawn and scheduled (same test; `EuropeDatasetTests.The_international_competitions_qualify…`: all four real-format competitions drawn inside the season) |
| Calendar conflicts are detected | `GetCalendarConflictsAsync` (`InternationalCompetitionTests.Calendar_conflicts_between_competitions_are_detected`); the career and the Europe dataset produce none |
| Domestic and international results coexist | Every fixture of the season played; league, domestic cup and both international cups completed |
| Competition winners are correctly recorded | `GetOutcomeAsync`/`GetHistoryAsync`, season review; rewards to winner and runner-up (`InternationalCompetitionTests`, `InternationalCompetitionTests` unit `Rewards_pay…`); the holder requalifies next season |

## 3. Implementation summary

* **Domain:** `CupRules.Entry` (`CupEntry`: tier and slots `Holder`/`Winner`/`League`/`Reputation`, `CupEntrySlot`),
  `CupRules.Rewards` (`CupRewards`), `CupRound.Neutral`; groups up to 20 clubs (`CupGroupStage.MaxGroupSize`).
  Pure `InternationalQualification` (tiers, one competition per club, skipping taken clubs, registration countries).
  `CupSeason.Reward` / `CupReward`. `CompetitionParticipant.PrizeMoney`, `ReputationGain`, `RecordReward`;
  `Club.ChangeReputation`; `MatchSetup.NeutralVenue` (no home advantage).
* **Services:** `ICompetitionService.QualifyAsync` (rankings from last season's champion and final table per country
  and level, cup winners; reputation without a finished season) and `GetCalendarConflictsAsync`; rewards paid when a
  cup completes; `RegisterClubsAsync` refuses a second international competition; league second-phase, play-in and
  playoff games use the same free-day rule as cups. `MatchService` plays neutral rounds without home advantage.
  `CareerService` qualifies at career start and next season and does not carry international entries over.
* **Data:** `INTERNATIONAL` and `add_international` in `build/generate-europe-dataset.py` (also used by the
  real-leagues generator); regenerated `data/europe` (56 competitions) and `data/real-leagues` (18); players unchanged.
* **Desktop:** no change needed: international competitions are cups, so the Career screen lists them with their
  groups, ties and winners.

## 4. Files changed
### Created
- `src/Ebm.Domain/Competitions/Engine/InternationalQualification.cs` — qualification engine
- `src/Ebm.Persistence/Migrations/20261116144825_InternationalCompetitions.cs` (+ designer)
- `docs/INTERNATIONAL_COMPETITIONS.md` — handoff: qualification logic, formats, calendar interaction, rewards, limitations
- `tests/Ebm.Tests.Unit/Competitions/InternationalCompetitionTests.cs`
- `tests/Ebm.Tests.Integration/Competitions/InternationalCompetitionTests.cs`

### Modified
- Domain: `CupRules`, `CupSeason`, `CompetitionParticipant`, `Club`, `MatchSetup`, `MatchSimulation(.Play)`
- Services: `ICompetitionService`, `CompetitionService`, `MatchService`, `CareerService`
- Persistence: model snapshot
- Data: `build/generate-europe-dataset.py`, `build/generate-real-leagues-dataset.py`, `data/europe/europe-2026-27.json`, `data/real-leagues/real-leagues-2026-27.json`
- Tests: latest-migration pins (`DatabaseInitializerTests`, `ContractServiceTests`), `EuropeDatasetTests` (cup count, international test)
- Docs: `CUP_FORMATS.md`, `DATABASE_SCHEMA.md`, `SEASON_LIFECYCLE.md`, `COUNTRY_REPORT.md`; `CHANGELOG.md`, `PROJECT_STATE.md`, `Directory.Build.props`

### Removed
- None

## 5. Architecture and technical decisions

Decisions 61–63 in `PROJECT_STATE.md`:
61. International competitions are cups without a country with `cup.entry` slots on the existing cup engine; they
    qualify every season from the previous season (first season: reputation), by tier, one per club.
62. Every fixture created after the league regular seasons moves to a day both clubs are free; remaining clashes are
    reported by `GetCalendarConflictsAsync`.
63. Rewards are paid once at the end of a cup and stored on the entry; neutral venues are a rules flag.

Kept out of scope: best-of series and play-ins inside cups, second group phases, qualifying rounds, licences,
country protection in draws, reputation decay (Step 31), finance records (Step 23), the ABA League as an
international competition in `data/europe`.

## 6. Database changes
- New entities: none
- Modified entities: `CompetitionParticipant.PrizeMoney` (long?), `ReputationGain` (int?)
- Removed entities: none
- Migrations: `20261116144825_InternationalCompetitions` (two nullable add-columns)
- Compatibility considerations: older saves upgrade through the migration (nothing paid). Cups without `entry`,
  `rewards` or `neutral` behave as before; a domestic cup completed before the upgrade pays nothing.

## 7. Tests and validation
| Test | Result | Notes |
|---|---|---|
| Build | PASS | Debug and Release, 0 warnings, 0 errors |
| Unit tests | PASS | 351/351 (338 existing + 13 international) |
| Integration tests | PASS (targeted) | 33/33: international, cup, Europe and real-leagues dataset, migration pins, contract, career and match service tests. Full suite not run (stopped on request) |
| Manual tests | PASS | Europe dataset: 104 clubs qualify (20/20/32/32, all distinct), all four drawn inside the season with no calendar conflicts |
| Regression tests | PARTIAL | Unit suite 351/351; `RealLeaguesDatasetTests` fixed (its competition count was stale since Step 17); full integration run pending |

## 8. Known limitations
- Formats simplified (`docs/INTERNATIONAL_COMPETITIONS.md` §5): no EuroLeague play-in or best-of-five, no best-of-three
  EuroCup final, no BCL second group phase, no qualifying rounds or licences.
- Group draws do not keep clubs of the same country apart.
- Reputation rewards only add; prize money is one end-of-competition payment without finance records.
- League playoff dates moved by the clash rule are not checked against the season end.

## 9. Outstanding issues
- Full integration suite not run after this step (only the step-relevant tests); run `dotnet test --filter "Category!=Launch"` at the start of Step 19.
- League regular seasons are not moved for fixtures already scheduled (only reachable by scheduling by hand; reported by `GetCalendarConflictsAsync`).
- No dedicated international screen: competitions show on the Career screen like cups.

## 10. Handoff instructions
Step 19 (European transfer market) can use club reputation (now changed by cup rewards) and international
participation (`CompetitionParticipants` of `cup.entry` competitions) for transfer attractiveness. Start from
`docs/TRANSFERS.md`, `ITransferService` and `docs/INTERNATIONAL_COMPETITIONS.md` §2.

## 11. Project state
- Working features: everything in `PROJECT_STATE.md`, plus international competitions in careers.
- Current application status: builds and launches; careers play leagues, domestic cups and international competitions.
- Database version: `20261116144825_InternationalCompetitions`
- Build command: `dotnet build`
- Test command: `dotnet test` (headless: `--filter "Category!=Launch"`)
- Run command: `dotnet run --project src/Ebm.Desktop`

## 12. Completion checklist
- [x] Objectives implemented
- [x] Build successful
- [x] Tests passed (unit full, integration targeted; full integration run pending)
- [x] No critical regression
- [x] Documentation updated
- [x] PROJECT_STATE.md updated
- [x] Next step can start independently
