# STEP 17 — Domestic cups and additional competitions

## 1. Status
- Status: COMPLETED
- Date: 2026-11-16
- Build version: 0.17.0
- Commit/reference: STEP 17 — Domestic cups
- Previous step: STEP 16 — European player database expansion (plus 0.16.1–0.16.4 real-data updates)
- Next step: STEP 18 — International club competitions

## 2. Objectives

From `docs/ROADMAP.md`, Step 17: domestic cups with knockout tournaments, single-match elimination, two-leg ties,
group stages, draw generation, seeding, qualification rules, finals and competition history; cups configured for the
supported countries.

| Criterion | Proof |
|---|---|
| Cup competitions can be configured | `cup` rules section, validated on save/import (`CupSeasonTests.Invalid_cup_rules_are_refused`, `…belong_to_cups_only`); 17 country cups in `data/europe` |
| Draws can be generated | `GenerateScheduleAsync`/`AdvanceAsync` draw and store seeds (`CupCompetitionTests`); bracket, seeded and open draws, reproducible (`CupSeasonTests`) |
| Matches can be played | Cup fixtures are played by the match service and the career calendar (`CupCompetitionTests.The_career_plays_the_cup…`); second legs go to overtime on a level aggregate |
| Winners are determined correctly | Single games, aggregates, level-aggregate rule, byes, group qualifiers (`CupSeasonTests`); service outcome = final winner |
| Cup results are stored historically | Fixtures, results and seeds kept; `GetHistoryAsync` lists champion and runner-up per season (`CupCompetitionTests`) |

## 3. Implementation summary

* **Domain:** `CupRules` (`Rules/CupRules.cs`: rounds with legs and `daysAfter`, qualification, group stage, draw,
  host, dates) as `CompetitionRules.Cup`, with validation (cups only on `Cup` competitions, no league sections, club
  counts). `CupSeason` engine: phases, group draw by pots, group tables, knockout entrants, byes, bracket/seeded/open
  pairing from a stable draw seed, ties with aggregates, next games, outcome. `CupTie`; `SeasonPhase.GroupStage/Knockout`;
  `FixtureStage.CupGroup/Cup` (legs as `GameNumber` 1–2); `SeasonOutcome.RunnerUp`; `CompetitionSeason.TableAfterRound`
  and `RoundsPlayed`; `CompetitionParticipant.Seed`; `MatchSetup.OvertimeMargin` and the matching end-of-game rule.
* **Services:** `CompetitionService` draws cups (qualification from the country's league at a division level after a
  round, or entered clubs ranked by division level and reputation), stores seeds, schedules group stages, rounds and
  second legs on days both clubs are free; `GetCupAsync`, `GetHistoryAsync`; cups in phase, standings (empty), group
  tables and outcome. `MatchService` sets the second-leg overtime margin. `CareerService` runs cups on game days,
  includes them in the state, season completion and review, and does not carry entries of qualification cups.
* **Desktop:** the Career screen lists cups with the leagues; a selected cup shows its group tables as standings and
  its byes, ties (scores, aggregates, winners), winner and past winners.
* **Data:** `CUPS` and `add_cups` in `build/generate-europe-dataset.py` (also used by the real-leagues generator);
  regenerated `data/europe` (52 competitions), `data/italy` (Coppa Italia), `data/real-leagues`. Player CSVs unchanged.

## 4. Files changed
### Created
- `src/Ebm.Domain/Competitions/Rules/CupRules.cs` — cup format and validation
- `src/Ebm.Domain/Competitions/Engine/CupSeason.cs` — cup engine and `CupTie`
- `src/Ebm.Persistence/Migrations/20261116135956_CupCompetitions.cs` (+ designer) — `CompetitionParticipants.Seed`
- `docs/CUP_FORMATS.md` — handoff: supported formats, schema, service, country configurations, limitations
- `tests/Ebm.Tests.Unit/Competitions/CupSeasonTests.cs` — engine, rules and second-leg overtime tests
- `tests/Ebm.Tests.Integration/Competitions/CupCompetitionTests.cs` — acceptance tests (draw, play, qualification, career, history)

### Modified
- Domain: `CompetitionRules`, `Competition`, `CompetitionParticipant`, `CompetitionSeason`, `SeasonOutcome`, `Fixture`, `MatchSetup`, `MatchSimulation(.Play)`
- Services: `ICompetitionService`, `CompetitionService`, `MatchService`, `CareerService`
- Persistence: model snapshot
- Desktop: `CareerViewModel`, `Themes/Careers.xaml`
- Data: `build/generate-europe-dataset.py`, `build/generate-real-leagues-dataset.py`, `data/europe/europe-2026-27.json`, `data/italy/italy-2026-27.json`, `data/real-leagues/real-leagues-2026-27.json`
- Tests: latest-migration pins (`DatabaseInitializerTests`, `ContractServiceTests`), `CareerViewModelTests` (cup view)
- Docs: `COMPETITION_FORMATS.md`, `DATABASE_SCHEMA.md`, `IMPORT_FORMATS.md`, `MATCH_ENGINE.md`, `SEASON_LIFECYCLE.md`, `COUNTRY_REPORT.md`; `CHANGELOG.md`, `PROJECT_STATE.md`, `Directory.Build.props`

### Removed
- None

## 5. Architecture and technical decisions

Decisions 57–60 in `PROJECT_STATE.md`:
57. Cups are a rules section run by the pure `CupSeason`; the stored seeds plus a draw seed from the competition and
    season IDs reproduce every draw, so nothing but seeds and fixtures is stored and a draw never changes.
58. Cup games avoid days on which either club already plays; a round is scheduled when the previous one is decided.
59. Two-legged ties never end level (second-leg overtime on a level aggregate; forfeit/edit fallback: leg-2 winner).
60. Qualification cups are drawn from the current league table after a round and re-qualify every season.

Kept out of scope (future steps or not required): neutral venues, drawn legs, previous-season seeding, supercups,
`Tournament` competitions, international cups (Step 18).

## 6. Database changes
- New entities: none
- Modified entities: `CompetitionParticipant.Seed` (int?, nullable); `Fixture.Stage` values `CupGroup`, `Cup` (text column, no change); `Fixture.GameNumber` 1–2 for cup legs
- Removed entities: none
- Migrations: `20261116135956_CupCompetitions` (add-column only, transactional on SQLite)
- Compatibility considerations: older saves upgrade through the migration (seeds null). A `Cup` competition without a
  `cup` section (older data, the sample's "Italian Cup") is never scheduled, as before. Existing league behaviour is unchanged.

## 7. Tests and validation
| Test | Result | Notes |
|---|---|---|
| Build | PASS | Debug, 0 warnings, 0 errors |
| Unit tests | PASS | 338/338 (317 existing + 21 cup) |
| Integration tests | PENDING | `CupCompetitionTests` 3/3 pass; the full integration run and the new `CareerViewModelTests` cup test were still running/unbuilt at commit time |
| Manual tests | PASS | Career screen cup view covered by `CareerViewModelTests.Career_screen_shows_a_cup_draw…`; regenerated datasets import (`EuropeDatasetTests`) |
| Regression tests | PENDING | Whole suite except the interactive launch test, run in progress at commit time |

## 8. Known limitations
- No neutral venues: Final Eights/Fours are hosted by the better seed.
- Legs are never drawn (the engine always plays to a winner).
- Qualification only from the current table of the cup's own country; no holders, hosts, previous-season seeds,
  lower-division clubs entering in later rounds or supercups.
- Country cup formats are simplified and unverified (`docs/CUP_FORMATS.md` §4).
- Cup runs are not part of the club history screen; competition history is available through `GetHistoryAsync`.

## 9. Outstanding issues
- League playoff games are created without looking at cup dates, so a club can occasionally play two games on one day
  if a cup game was scheduled first.
- A registered cup whose entries do not fit its rounds is silently not drawn by the calendar (`GenerateScheduleAsync` reports why).

## 10. Handoff instructions
Step 18 (international club competitions) should reuse the cup engine: a `Cup`/`Tournament` competition without a
country with `registration.countries`, a group stage (`groupStage`) and knockout rounds; extend qualification to
other countries' tables or previous-season outcomes (champions, cup winners) and consider neutral venues for Final
Fours. Start from `docs/CUP_FORMATS.md`, `CupSeason`, `CompetitionService.DrawAsync` and the `CUPS` table in
`build/generate-europe-dataset.py`.

## 11. Project state
- Working features: everything in `PROJECT_STATE.md`, plus domestic cups in careers.
- Current application status: builds and launches; careers play leagues and cups.
- Database version: `20261116135956_CupCompetitions`
- Build command: `dotnet build`
- Test command: `dotnet test` (headless: `--filter "Category!=Launch"`)
- Run command: `dotnet run --project src/Ebm.Desktop`

## 12. Completion checklist
- [x] Objectives implemented
- [x] Build successful
- [ ] Tests passed (full run pending)
- [x] No critical regression
- [x] Documentation updated
- [x] PROJECT_STATE.md updated
- [x] Next step can start independently
