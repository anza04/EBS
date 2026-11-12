# Season lifecycle and career calendar

Step 12. Code: `Ebm.Services.Careers` (`ICareerService`, `CareerService`), `Ebm.Services.GameClock`,
`Ebm.Domain.Careers.Career`, `Ebm.Domain.Competitions.Engine.SeasonTransition`, `Ebm.Domain.Seasons.SeasonCalendar`.
Screen: *Career* tab (`CareerViewModel`, `Themes/Careers.xaml`).

## 1. The career and the game date

* A game database holds at most one `Career`: manager name, managed club, current season, game date.
* `GameClock.Today` is the date every game service uses (contracts, transfers, squads, profiles). While a career is
  loaded it is the career date; otherwise the date of the injected `TimeProvider` (real date, or a test clock).
* `ICareerService.LoadAsync` sets the clock from the stored career; the desktop app calls it at startup before applying
  contract and transfer dates.
* The career date only moves forward (`Career.MoveTo`).

## 2. Starting a career

`StartAsync(club, manager)`:
1. Takes the earliest season with clubs entered in a league competition that is not finished.
2. The club must be entered in one of its league competitions (`GetClubOptionsAsync` lists them).
3. Generates the regular season of every league competition of that season without a schedule (first round = season start).
4. Sets the date to **7 days before the season start** (preseason), then applies contract and transfer dates.

## 3. A game day (`AdvanceDayAsync`)

On the current date D:
1. Refused while the user's club has an unplayed fixture dated D or earlier (play it live in the match centre or with
   *Play match* = `PlayUserMatchAsync`, instant).
2. Plays every other unplayed fixture of the season's league competitions dated ≤ D, instantly
   (`IMatchService.PlayOrForfeitAsync`: a club that cannot field five players loses 20–0 by forfeit, no box score; if
   neither can, the home club forfeits).
3. Advances every league competition (`ICompetitionService.AdvanceAsync`): playoffs are scheduled when the regular season
   ends, next games and rounds as results come in.
4. Moves the date to D + 1 and applies contract and transfer dates (`ITransferService.ProcessTransferDatesAsync`: loan
   returns, contract expiries and starts).
5. If D is the last day of a season (`Season.EndDate`), the AI transfer window runs (`RunAiTransferWindowAsync`, the
   user's club excluded): offseason recruitment.

With the assistant on (`SetAssistantAsync(true)`, "holiday"), step 1 does not apply: the user's fixtures are played
like the others and the club is AI-managed (docs/AI_MANAGEMENT.md).

`ContinueAsync` repeats game days until the user's club plays today, the season is complete or a day limit (366) is
reached. It is refused once the season is complete.

## 4. Season completion

The season is complete when every scheduled league competition of the career season is `Completed` (regular season and
playoffs played). `GetSeasonReviewAsync` gives each competition's champion, promoted and relegated clubs (from the rules,
`SeasonOutcomeCalculator`) and the user's final position.

## 5. The next season (`StartNextSeasonAsync`)

Only for a complete season:
1. Next season = an existing season starting exactly one year later, else a new one with the same dates one year later
   (`SeasonCalendar.Next`, name `2027-28`).
2. Entries: every competition keeps its clubs, except league competitions of a division: per league,
   `SeasonTransition.NextEntries` removes clubs promoted (to the division above) or relegated (to the division below)
   and adds those coming down and up. Registration rules are checked again (`RegisterClubsAsync`).
3. League competitions are scheduled with the same first-round offset from the season start as the previous season.
4. The career moves on to the new season (`SeasonsPlayed + 1`). The date does not jump: the user continues through the
   offseason (contract expiries on 30 June, the AI window) to the first match.

Each part is its own unit of work; competitions that already have entries or a schedule are skipped, so the call can be
repeated after a failure.

## 6. Histories

Nothing is deleted: fixtures, results, box scores, standings, contracts, transfers and club spells of past seasons stay.
`GetClubHistoryAsync(club)` lists each league season of a club (position, wins, losses, champion/promoted/relegated),
calculated from the stored results. Player history is the club spells (`PlayerClubHistory`), contracts, transfers and
box scores, all kept per season.

## 7. Not in this step

Fitness, morale and injuries do not change (Step 22); cups are not scheduled (Step 17). Step 13 added the AI clubs,
salaries and income to the game day (docs/AI_MANAGEMENT.md §1).
