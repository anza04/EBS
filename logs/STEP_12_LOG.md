# STEP 12 — Season progression and career management

**Date:** 2026-11-12
**Version:** 0.12.0
**Status:** Completed. All acceptance criteria met.

## 1. Objectives

From `docs/ROADMAP.md`, Step 12: season calendar, fixture progression, matchday processing, standings, playoffs,
season completion, promotion and relegation, contract expiry, offseason, new season generation; the user selects a club,
starts a career, progresses through the calendar, plays matches, reviews standings, completes the season and starts another.

| Criterion | Proof |
|---|---|
| A complete season can be played | `CareerServiceTests.A_full_season_is_played_and_the_next_one_is_generated` (sample + squads, all fixtures incl. playoffs) |
| All fixtures are processed | Same test: results = fixtures, no forfeits |
| Standings are updated | Standings checked in the review and next-season tables; `CareerViewModelTests` (played 1 after the first match) |
| Champions are correctly determined | Review champion = club history `Champion` flag; `SeasonOutcomeCalculator` (Step 04 tests) |
| Promotion and relegation work | Next season: promoted A2 clubs in Serie A, relegated clubs in A2; `SeasonLifecycleTests` |
| The next season can be generated | 2027-28 created, entered and scheduled; offseason played to the first match |
| Player and club histories are preserved | Match count unchanged after the new season; `GetClubHistoryAsync` |

## 2. Files

### Created
| File | Purpose |
|---|---|
| `src/Ebm.Domain/Careers/Career.cs`, `CareerId.cs` | Career entity |
| `src/Ebm.Domain/Competitions/Engine/SeasonTransition.cs` | Promotion/relegation between divisions (pure) |
| `src/Ebm.Domain/Seasons/SeasonCalendar.cs` | Next season dates and name |
| `src/Ebm.Services/GameClock.cs` | Game date (career date or `TimeProvider`) |
| `src/Ebm.Services/Careers/ICareerService.cs`, `CareerService.cs` | Career use cases |
| `src/Ebm.Persistence/Configurations/CareerConfigurations.cs` | Mapping |
| `src/Ebm.Persistence/Migrations/20261112144630_Careers*.cs` | Migration |
| `src/Ebm.Desktop/ViewModels/Careers/CareerViewModel.cs`, `Themes/Careers.xaml` | Career screen |
| `data/sample/italy-squads.json` | 122 fictional players with contracts (11 per club) and 12 free agents |
| `docs/SEASON_LIFECYCLE.md` | Handoff documentation |
| Tests `Careers/CareerServiceTests.cs`, `Careers/CareerViewModelTests.cs`, `Unit/Competitions/SeasonLifecycleTests.cs` | |

### Modified
`Match` (`IsForfeit`, `Match.Forfeit`), `MatchService`/`IMatchService` (`PlayOrForfeitAsync`), `CompetitionService`/
`ICompetitionService` (`GetPhaseAsync`, forfeit results), `ClubService`, `ContractService`, `TransferService`,
`PlayerProfileService` (date from `GameClock`), `ServiceCollectionExtensions`, `DatabaseEditorViewModel` (optional
career), `MainWindow.xaml` (*Career* tab first), `App.xaml`, `App.xaml.cs` (loads the career at startup),
`Directory.Build.props`, `README.md`, `docs/DATABASE_SCHEMA.md`, `docs/IMPORT_FORMATS.md`, `CHANGELOG.md`,
`PROJECT_STATE.md`; tests expecting the latest migration and the table list.

### Removed
None.

## 3. Features implemented

See `docs/SEASON_LIFECYCLE.md`: career start, game days, continue-to-next-match, instant play of the user's match,
forfeits, playoffs advancing automatically, AI transfer window at season end, season review, next season with
promotion/relegation and schedules, club history; career screen with standings, news and history.

## 4. Architectural decisions

Decisions 41–43 in `PROJECT_STATE.md`:
* `GameClock` replaces direct `TimeProvider` reads in the services (the `TimeProvider` registration is unchanged).
* One career per database; the database file is the save (Step 14 adds save files).
* Clubs without five available players forfeit 20–0 instead of blocking the calendar.

## 5. Database changes

Migration `20261112144630_Careers`: new `Careers` table (create-table) and `Matches.IsForfeit` (add-column, default
false). Upgrades in one transaction.

## 6. Tests

| Run | Result |
|---|---|
| `dotnet build` (Debug) | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including launch) | PASS 387/387 (277 unit, 110 integration) |

The full-season test was run 7 times (random match seeds) without failures.

## 7. Known limitations

* Salaries and bonuses are not booked; AI clubs keep the default tactic and their stored roster roles (Step 13).
* Fitness, morale and injuries are not changed by matches (Step 22).
* The contracts and transfer-market screens still default new dates to the real date (`DateTime.Today`) when the date
  box is empty; type the date during a career.
* `StartNextSeasonAsync` is several units of work (repeatable, not atomic).
* Only one career per database.

## 8. Outstanding issues

None.

## 9. Instructions for the next step

1. Read `PROJECT_STATE.md`, this log and `docs/SEASON_LIFECYCLE.md`, then the Step 13 specification.
2. AI management should hook into `CareerService.AdvanceDayAsync` (daily/weekly AI decisions for every club but
   `Career.ClubId`): line-ups (`IClubService.SaveRosterAsync`), tactics (`ITacticsService`), recruitment and releases
   (`ITransferService`, `IContractService`), salaries.
3. Use `ContinueAsync` loops with `PlayUserMatchAsync` (as in `CareerServiceTests`) to simulate careers over several seasons.
