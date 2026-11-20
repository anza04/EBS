# STEP 31 — News, media and club reputation

## 1. Status
- Completed 2026-11-20. Version 0.31.0.
- Build: PASS (Debug and Release, 0 warnings, 0 errors). Tests: see §5.

## 2. Objectives
Transfer news, injury reports, match previews and reviews, press conferences, player interviews, rumours, media
reactions, club reputation changes and fan expectations, all generated from actual game events.

| Acceptance criterion | Where |
|---|---|
| News is generated from relevant events | `NewsServiceTests`: one review/press conference/interview per played user match with its score; previews the day before each user fixture; a story per user-club move and headline transfer, per user-club and serious injury, linking the record |
| No contradictory reports are created | Unique event `Key` (DB index + in-run set); stories only from stored records; previews never after the match; mood stories only on a change; rumours only on a running contract with nothing signed after; transfer stories confirm earlier rumours. Test: keys distinct, repeating a day writes nothing |
| Historical news can be accessed | `GetNewsAsync` (club, category, dates, max); last season's stories kept after the next season starts; *News* tab |
| Reputation changes are linked to game events | `ReputationChange` with cause and match (upsets) or season (titles, cups, promotion, relegation, objectives) or competition (rewards); per-club chain ends at the club's reputation (test over every club) |

## 3. Implementation summary
- Domain `Ebm.Domain.News`: `NewsItem`, `ReputationChange` (+ `ReputationCause`), pure `MediaRules` (points, upsets,
  streaks, fan expectation/mood) and `NewsWriter` (deterministic wording by key hash).
- Services: `INewsService`/`NewsService`; `CareerService` calls it at the end of each game day, at season start (fan
  expectations) and at season end right after the board objectives are judged; `GameDayReport.News`.
  `CompetitionService.PayRewardsAsync` now stores a `CompetitionReward` change for each reputation reward (found while
  checking the acceptance criteria: Step 18 rewards changed reputation without a record).
- Persistence: `NewsConfigurations.cs`, migration `20261120170405_NewsAndReputation` (two new tables, create-table only).
- UI: *News* tab (`NewsViewModel`, `Themes/News.xaml`), synced with the locked club.
- Decision: news tables keep plain IDs without foreign keys so the history survives editor deletions.

## 4. Files changed
- New: `src/Ebm.Domain/News/*` (4), `src/Ebm.Services/News/*` (2), `src/Ebm.Persistence/Configurations/NewsConfigurations.cs`,
  migration + designer, `src/Ebm.Desktop/ViewModels/News/NewsViewModel.cs`, `src/Ebm.Desktop/Themes/News.xaml`,
  `tests/Ebm.Tests.Unit/News/MediaRulesTests.cs`, `tests/Ebm.Tests.Integration/News/NewsServiceTests.cs`, `docs/NEWS.md`, this log.
- Modified: `CareerService.cs`, `ICareerService.cs`, `CompetitionService.cs`, `ServiceCollectionExtensions.cs`,
  `DatabaseEditorViewModel.cs`, `App.xaml`, `MainWindow.xaml`, model snapshot, `ContractServiceTests.cs`,
  `DatabaseInitializerTests.cs`, `InternationalCompetitionTests.cs`, `Directory.Build.props` (0.31.0),
  `docs/DATABASE_SCHEMA.md`, `CHANGELOG.md`, `PROJECT_STATE.md`.
- Database: migration `NewsAndReputation` (35 tables).

## 5. Tests
- Unit: `MediaRulesTests` 5/5, architecture tests 7/7 PASS. Full unit run not repeated.
- Integration (by user decision, affected classes only): `NewsServiceTests`, `InternationalCompetitionTests`,
  `CompetitionServiceTests`, `CareerServiceTests`, `ContractServiceTests`, `DatabaseInitializerTests`,
  `PlayerScreensRenderTests`, architecture: PASS. Launch: PASS. Full fast suite and slow suite not run.

## 6. Known limitations
- Detailed stories (reviews, press, interviews, previews, rumours, fans) only for the career club.
- Press-conference and interview quotes follow the result; no user answers.
- Rumours only about expiring contracts; no invented transfer interest.
- English text only. A cup winner with rewards gets both the reward and the +2 `CupWin` change.

## 7. Handoff instructions
- Step 32 (history and records): reuse `NewsItems` and `ReputationChanges` for club histories; season verdict keys
  are `reputation:{seasonId}:{clubId}`.
- Run the full integration fast suite (not run in Steps 29–31) and the slow suite on `data/real-leagues` (news adds a
  per-day pass; check season-2 day time) before the next milestone release.

## 8. Outstanding issues
- Full integration fast suite and slow suite not run on Step 29–31 code (user instruction).
