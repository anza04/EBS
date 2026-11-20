# STEP 32 — Long-term world simulation and historical records

## 1. Status
- Completed 2026-11-20. Version 0.32.0.
- Build: PASS (Debug, 0 warnings, 0 errors). Tests: see §5.

## 2. Objectives
Player career histories, club histories, competition winners, season records, individual awards, retirements, hall
of fame, historical statistics, generated youth players, new coaching careers, long-term league evolution.

| Acceptance criterion | Where |
|---|---|
| The game world progresses over many seasons | `LongTermWorldTests` (slow): three seasons of the sample world, each with winners, awards and snapshots in every league; reputations in range, world mean within ±10 |
| Player and club histories remain accessible | `GetPlayerCareerAsync` (seasons, totals, awards, spells, titles), `GetClubHonoursAsync`; tested for a retired player, an MVP over seasons, champions' trophies |
| Retired players are preserved | Retired players keep rows, statistics and careers (every retired player's career loads after three seasons); hall of fame and coaching career for a forced retiree (`HistoryServiceTests`) |
| New players enter the database | Youth intake: 1–2 academy players per club per year on youth contracts in the youth squad; AI promotions |
| Historical competitions and awards are recorded | `SeasonAwards` (12 per league season), `GetWinnersAsync`, `GetRecordsAsync` (checked against stored box scores), `LeagueSeasonSnapshots` |

## 3. Implementation summary
- Domain `Ebm.Domain.History`: `SeasonAward` (+ `AwardKind`), `HallOfFameEntry`, `LeagueSeasonSnapshot`, pure
  `HistoryRules` (awards, coach of the season, hall of fame score, coaching aptitude/role/profile, intake size and
  prospects, promotions, drift). `PlayerGenerator.CreateYouth` (same random sequence for `Create`). `Player.YouthClubId`,
  `StaffMember.FormerPlayerId`, `ReputationCause.LongTermDrift`.
- Services: `IHistoryService`/`HistoryService`; `CareerService` calls `ProcessSeasonEndAsync` in `StartNextSeasonAsync`
  (before the news verdict) and `ProcessRetirementsAsync` + `ProcessYouthIntakeAsync` on the season end date;
  `GameDayReport.World`.
- Persistence: `HistoryConfigurations.cs`, migration `20261120184921_LongTermHistory` (create-table and add-column only).
- UI: *History* tab (`HistoryViewModel`, `Themes/History.xaml`), club synced with the locked club.
- Decisions: store only what cannot be derived later; derive careers and records from box scores; youth players and
  former-player coaches are ordinary players/staff with a link column; reputation drift recorded like any change.

## 4. Files changed
- New: `src/Ebm.Domain/History/HistoryRecords.cs`, `HistoryRules.cs`, `src/Ebm.Services/History/IHistoryService.cs`,
  `HistoryService.cs`, `src/Ebm.Persistence/Configurations/HistoryConfigurations.cs`, migration + designer,
  `src/Ebm.Desktop/ViewModels/History/HistoryViewModel.cs`, `src/Ebm.Desktop/Themes/History.xaml`,
  `tests/Ebm.Tests.Unit/History/HistoryRulesTests.cs`, `tests/Ebm.Tests.Integration/History/HistoryServiceTests.cs`,
  `LongTermWorldTests.cs`, `docs/HISTORY.md`, this log.
- Modified: `Player.cs`, `StaffMember.cs`, `PlayerGenerator.cs`, `ReputationChange.cs`, `NewsWriter.cs`,
  `CareerService.cs`, `ICareerService.cs`, `ServiceCollectionExtensions.cs`, `DatabaseEditorViewModel.cs`, `App.xaml`,
  `MainWindow.xaml`, model snapshot, `MediaRulesTests.cs`, `ContractServiceTests.cs`, `DatabaseInitializerTests.cs`,
  `Directory.Build.props` (0.32.0), `docs/DATABASE_SCHEMA.md`, `docs/DEVELOPMENT.md`, `CHANGELOG.md`, `PROJECT_STATE.md`.

## 5. Tests
- Unit: affected areas PASS (113: history 14 new, news, management, development, staff, players, architecture).
- Integration (affected classes, by user decision): 64 PASS (`HistoryServiceTests`, `NewsServiceTests`, careers, staff,
  competitions, cups, international, development, finances, medical, contracts, database initializer, render tests).
- Slow: `LongTermWorldTests` PASS (three seasons, ~2.5 min).
- Full fast suite, the other slow tests and the real-leagues dataset not run.

## 6. Known limitations
- Awards for leagues only; no playoff MVP or cup awards (cups record winners).
- Records only from seasons played in the game (imported careers have no box scores; the hall of fame uses
  reputation for them).
- Youth positions are random; no youth leagues; the user's youth players are promoted by hand.

## 7. Handoff instructions
- Step 33 (modding/custom databases): history tables have no foreign keys; an imported database starts with empty
  history. Thresholds and formulas are in `HistoryRules` constants (not a data file yet).
- Run the full fast suite and the slow suite on `data/real-leagues` before the milestone release (youth intake adds
  ~200 players a year there; watch season-2+ day time, a known issue).

## 8. Outstanding issues
- Full integration fast suite and slow suite not run on Steps 29–32 code (user instruction).
