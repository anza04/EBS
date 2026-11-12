# STEP 04 — Countries, leagues and competitions

## 1. Status
- Status: COMPLETED
- Date: 2026-11-09
- Build version: 0.4.0
- Commit/reference: uncommitted on top of `c65573f` (init)
- Previous step: STEP 03 — Data import and database editor
- Next step: STEP 05 — Player model and attributes

## 2. Objectives
- Initial playable database: one country, first and second division, regular season, playoffs, promotion and relegation.
- Engine support for: different numbers of teams, different formats, configurable points systems, home and away
  fixtures, playoff qualification, promotion and relegation, registration rules. No country rule in the engine.
- Deliverables: competition configuration system, league setup, fixture generator, standings calculator,
  competition format documentation.
- Acceptance: complete schedule generated; correct number of matches per team; correct standings; configurable
  tie-breakers; playoff qualification; promotion and relegation calculated.

## 3. Implementation summary
- **Rules schema** (`Ebm.Domain.Competitions.Rules.CompetitionRules`): JSON in `Competitions.RulesJson`, parsed with
  System.Text.Json (camelCase, case-insensitive, enums as text, unknown properties refused). Sections: points,
  `registration`, `regularSeason`, `tieBreakers`, `playoffs`, `promotion`, `relegation`, all with defaults.
  `Parse` checks club-independent rules; `ValidateForClubs(n)` checks rules against the entered clubs.
  `Competition.UpdateRules` now validates through `Parse` (so the editor and the importer use it automatically);
  `Competition.ParseRules()` returns the object.
- **Engine** (`Ebm.Domain.Competitions.Engine`, pure and stateless):
  `RoundRobinScheduler` (circle method; odd counts use a fixed bye slot; legs mirror home/away),
  `StandingsCalculator` (points, then tie-breakers; split groups restart from the first criterion so head-to-head
  is a mini-league; club ID as the final deterministic order), `PlayoffBracket` (fixed bracket order 1-N, 4-5, 2-7,
  3-6 …; higher seed home court H-A-H / 2-2-1 / 2-2-1-1-1; builds series state from games and returns the next
  games), `SeasonOutcomeCalculator`, `CompetitionRegistration`.
- **Service** `ICompetitionService` / `CompetitionService` (`Ebm.Services.Competitions`, one DI scope per call):
  `RegisterClubsAsync`, `GenerateScheduleAsync`, `RecordResultAsync` (creates the `Match`, updates
  `LeagueStandings` for regular-season games), `AdvanceAsync` (phase detection, playoff seeding and on-demand game
  scheduling), `GetStandingsAsync`, `GetOutcomeAsync`. Checks division structure (promotion needs a division above,
  relegation one below) and that the schedule fits the season.
- **Fixtures**: new `Stage` and `GameNumber` (optional constructor arguments, so existing callers are unchanged).
- **Import**: `seasons` and `participants` record types (enum order = dependency order: … Competition, Season,
  Participant, Player, Contract). Participants are checked with `CompetitionRegistration` against stored and
  accepted entries, and refused for a competition season that already has fixtures.
- **Data**: sample dataset extended (6 clubs, season 2026-27, 14 entries, full rules for Serie A and Serie A2);
  templates `seasons.csv`, `participants.csv`, JSON template sections.

## 4. Files changed
### Created
- `docs/COMPETITION_FORMATS.md` — formats, rules schema, tie-breakers, running a season, adding a league (handoff)
- `logs/STEP_04_LOG.md` — this log
- `src/Ebm.Domain/Competitions/Rules/CompetitionRules.cs` — schema records and `TieBreaker`
- `src/Ebm.Domain/Competitions/Engine/RoundRobinScheduler.cs`, `StandingsCalculator.cs`, `PlayoffBracket.cs`,
  `SeasonOutcome.cs`, `CompetitionRegistration.cs`
- `src/Ebm.Services/Competitions/ICompetitionService.cs`, `CompetitionService.cs`
- `src/Ebm.Persistence/Migrations/20261109124652_FixtureStages.cs` (+ `.Designer.cs`)
- `data/templates/seasons.csv`, `data/templates/participants.csv`
- `tests/Ebm.Tests.Unit/Competitions/CompetitionRulesTests.cs`, `RoundRobinSchedulerTests.cs`,
  `StandingsCalculatorTests.cs`, `PlayoffAndOutcomeTests.cs`
- `tests/Ebm.Tests.Integration/Competitions/CompetitionServiceTests.cs`

### Modified
- `src/Ebm.Domain/Competitions/Competition.cs` — schema validation, `ParseRules`
- `src/Ebm.Domain/Matches/Fixture.cs` — `FixtureStage`, `Stage`, `GameNumber`
- `src/Ebm.Domain/Seasons/Season.cs` — `UpdateDetails`
- `src/Ebm.Persistence/Configurations/MatchConfigurations.cs`, `Migrations/EbmDbContextModelSnapshot.cs`
- `src/Ebm.Services/ServiceCollectionExtensions.cs` (registers `ICompetitionService`), `Data/DuplicateKeys.cs`
  (season, participant), `Import/ImportContracts.cs` (record kinds)
- `src/Ebm.DataImport/Sources/ImportFields.cs`, `Validation/EntityStore.cs` (`All`), `Validation/ImportSession.cs`
- `data/sample/italy-sample.json`, `data/templates/dataset-template.json`
- `tests/Ebm.Tests.Unit/Domain/EntityRulesTests.cs`, `tests/Ebm.Tests.Integration/Editing/DatabaseEditorTests.cs`
  (schema-valid rules), `Import/ImportTests.cs` (new counts, entry rules), `Persistence/DatabaseInitializerTests.cs`
  (upgrade test)
- `Directory.Build.props` (0.4.0), `README.md`, `docs/DATABASE_SCHEMA.md`, `docs/IMPORT_FORMATS.md`,
  `docs/TECHNICAL_ARCHITECTURE.md`, `CHANGELOG.md`, `PROJECT_STATE.md`

### Removed
- none

## 5. Architecture and technical decisions
- **Engine in Domain, orchestration in Services.** Scheduling, ranking, brackets and outcomes are pure functions over
  domain types: unit-testable without a database and reusable by later steps (simulation, AI, season progression).
- **Strict schema with defaults.** `{}` stays valid (cups, old data), but unknown properties are refused, so a typo
  is reported instead of silently ignored. No comments/trailing commas, to match the database's `json_valid` check.
- **Rankings from results.** The table is always calculated from match results (needed for head-to-head anyway).
  `LeagueStandings` keeps stored totals in step for cheap reads; a test checks they agree after a full season.
- **Playoffs on demand.** Only the next game of each series is scheduled, after the previous one is played, so no
  unneeded games (e.g. game 5 of a 3-0 series) exist. Game 1 of a round follows the latest fixture of the
  competition; later games follow the previous game of the series.
- **Fixed bracket, no reseeding**, standard home-court patterns. Promotion via playoffs requires the playoff
  positions to start after the direct places and to exclude relegated positions (validated).
- **Registration rules in one place** (`CompetitionRegistration`), used by the service and the importer;
  registration closes once the schedule exists.
- **Deterministic results.** Clubs are scheduled in club-ID order; full ties fall back to club ID. No randomness in
  the engine (a randomised draw can be added by passing a shuffled club list).
- **Migration without check constraint.** A check on `GameNumber` would make SQLite rebuild `Fixtures` outside a
  transaction (EF warning); the migration only adds columns so upgrades of existing saves are atomic. Recorded as
  architectural decision 21 in `PROJECT_STATE.md`.

## 6. Database changes
- New entities: none
- Modified entities: `Fixture` (`Stage` text, default `RegularSeason`; `GameNumber` int, default 1)
- Removed entities: none
- Migrations: `20261109124652_FixtureStages` (two `AddColumn`s)
- Compatibility considerations: Step 02/03 databases upgrade in place at start-up; existing fixtures become
  regular-season games (tested). Stored competition rules that are not schema-valid would now be refused when
  edited; the sample and templates were updated (only `pointsForWin`/`pointsForLoss` were used before, both valid).

## 7. Tests and validation
| Test | Result | Notes |
|---|---|---|
| Build | PASS | Debug and Release, 0 warnings, 0 errors |
| Unit tests | PASS | 170/170 (90 new): rules defaults/parsing/13 invalid cases/club-count checks; scheduler for 2–20 clubs × 1–4 legs (game counts, rounds, one game per round, pair meetings, home/away balance and streaks); standings (points systems, head-to-head vs difference order, 3-way mini-league, final order, invalid results); bracket order, home-court patterns, series progression, invalid games; outcome; registration |
| Integration tests | PASS | 58/58 (7 new): schedule of the sample Serie A (56 games, 14 per club, 7 home, dates), full simulated seasons of both divisions to completion (stored vs calculated standings, playoff rounds, champion, 2 relegated, 1 + playoff winner promoted), registration rules and closing, schedule/result validation, promotion without a division above, participant import rules, Step 02 database upgrade |
| Manual tests | PASS | Release `Ebm.Desktop.exe --smoke-test` with a fresh data root exits 0; log shows `schema version 20261109124652_FixtureStages` and no warnings/errors; `seasons.csv`/`participants.csv` copied to the output |
| Regression tests | PASS | All Step 01–03 tests, including the real executable launch and the no-pending-model-changes check |

Command: `dotnet test EuropeanBasketballManager.slnx`

## 8. Known limitations
- Only `League` competitions can be scheduled; cups/knockout formats are Step 17. Not supported: groups/conferences,
  relegation playoffs, play-ins, two-legged ties, reseeding, overtime-loss points.
- Season outcome is calculated, not applied: moving promoted/relegated clubs into next season's competitions is
  Step 12.
- No UI for schedules, tables or playoffs (Step 11). Competition rules are edited as JSON in the editor.
- Playoff dates are not checked against the season end (the regular season is).
- The importer loads all fixtures to know which competition seasons are scheduled (fine for now; revisit with
  Step 16's data volume, as for the other in-memory checks).
- Schedule order is deterministic (club-ID order); a random draw is not implemented.

## 9. Outstanding issues
- None blocking. The editor layout still has not been reviewed visually (carried over from Step 03).

## 10. Handoff instructions
Step 05 — Player model and attributes:
1. Read `PROJECT_STATE.md`, this log, `docs/DATABASE_SCHEMA.md` and `docs/IMPORT_FORMATS.md`.
2. The player model is `src/Ebm.Domain/Players` (`Player`, `PlayerAttributes`: 8 ratings, owned type in table
   `PlayerAttributes`). Extending it needs a new migration; keep it add-column only where possible (decision 21)
   and give new columns defaults so existing saves upgrade.
3. Import columns are defined in `src/Ebm.DataImport/Sources/ImportFields.cs` and mapped in
   `Validation/ImportSession.cs` (`Attributes`); update `data/templates/players.csv`, the JSON template, the sample
   dataset's 24 players and `docs/IMPORT_FORMATS.md`. The editor's player page is `PlayersPageViewModel` +
   `Themes/Editor.xaml`.
4. The competition engine does not use player data; no changes needed there.
5. Bump `<Version>` to `0.5.0`; update `CHANGELOG.md`, `PROJECT_STATE.md` and write `logs/STEP_05_LOG.md`.

## 11. Project state
- Working features: application shell, configuration, logging, error handling, game database with migrations,
  CSV/JSON import (now with seasons and competition entries), database editor, competition engine (rules schema,
  registration, round-robin schedule, results, standings with tie-breakers, playoffs, promotion/relegation)
- Current application status: builds, launches, all tests pass
- Database version: `20261109124652_FixtureStages`
- Build command: `dotnet build EuropeanBasketballManager.slnx`
- Test command: `dotnet test EuropeanBasketballManager.slnx` (headless: add `--filter "Category!=Launch"`)
- Run command: `dotnet run --project src/Ebm.Desktop`, then Import tab → `data\sample\italy-sample.json`

## 12. Completion checklist
- [x] Objectives implemented
- [x] Build successful
- [x] Tests passed
- [x] No critical regression
- [x] Documentation updated
- [x] PROJECT_STATE.md updated
- [x] Next step can start independently
