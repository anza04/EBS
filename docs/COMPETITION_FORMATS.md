# Competition Formats

How league competitions are configured and run (Step 04; second phases, play-in, byes, cross-border leagues and
EU limits since Step 15). Competition rules are data: a JSON object in
`Competitions.RulesJson`, edited in the database editor or imported (`rules` field, `docs/IMPORT_FORMATS.md`).
No country-specific rule exists in code.

## 1. Building blocks

| Concept | Stored as | Notes |
|---|---|---|
| League | `Leagues` | Organising body, owns divisions |
| Division | `Divisions` | Tier of a league; level 1 is the top. Promotion goes to level − 1, relegation to level + 1 |
| Competition | `Competitions` | `type` `League` = a division's championship (`divisionId` set) |
| Season | `Seasons` | Shared by all competitions; the schedule must fit between its start and end dates |
| Entry | `CompetitionParticipants` | Club × competition × season; registration rules apply |
| Fixture | `Fixtures` | `Stage` (`RegularSeason`/`SecondPhase`/`PlayIn`/`Playoffs`), `Round`, `GameNumber` (game in a playoff series) |
| Result | `Matches` | One per fixture, no draws |
| Table | `LeagueStandings` | Stored totals per club (W/L, points for/against, standing points), updated with each regular-season result |

## 2. Supported formats

* **Regular season:** round robin with 1–4 legs (`legs: 2` = home and away). Any number of clubs from 2 to 40;
  with an odd number one club rests each round. Each club plays `(clubs − 1) × legs` games. The first round is
  `regularSeason.startOffsetDays` after the season start unless a date is given.
* **Second phase (optional):** after the regular season the table is split into groups by position (e.g. a
  championship round 1–6 and a relegation round 7–12), each playing its own round robin. `carryOver` decides which
  first-phase games count in the group tables: `All`, `WithinGroup` (only games between group members) or `None`.
  With `byCountry` the group positions count within each club's country (cross-border leagues: "the top five of
  each country" form one group). The final table lists the groups in order, then the clubs in no group.
* **Playoffs (optional):** single-elimination bracket of 2–32 clubs seeded from the final table (from any
  position, e.g. 2nd–9th), best-of-1/3/5/7 per round, fixed bracket (1 v N, 2 v N−1, …; no reseeding), higher
  seed has home court (best of 3: H-A-H, 5: 2-2-1, 7: 2-2-1-1-1). When the number of clubs is not a power of two
  the top seeds get a first-round bye. With `playIn` the last two seeds come from a play-in of the next four
  positions (7 v 8 → seed 7; loser v winner of 9 v 10 → seed 8; single games). Only the games a series needs are
  scheduled.
* **Points systems:** any `pointsForWin` > `pointsForLoss` (0–10), e.g. 2/0 or FIBA 2/1.
* **Tie-breakers:** an ordered list, applied to clubs level on points (§4).
* **Promotion:** top N positions directly, plus optionally 1, 2 or 4 playoff places (champion, finalists,
  semi-finalists).
* **Relegation:** bottom N positions of the final table.
* **Champion:** the playoff winner, or the table leader without playoffs.
* **Registration:** clubs from the competition's country, from a list of countries (`registration.countries`,
  cross-border leagues such as the BNXT League) or any country; squad limits including foreign and non-EU players.

Not supported yet: cups and knockout-only formats (`Cup`/`Tournament` competitions cannot be scheduled; Step 17),
relegation playoffs/play-outs, two-legged aggregate ties, reseeding, overtime-loss points, clubs joining a
competition mid-season (e.g. ABA League clubs entering the Serbian or Croatian playoffs).

## 3. Rules schema

Every property is optional; missing ones take the default. Unknown properties are refused (typos are caught).
Property names are case-insensitive; enum values are text.

| Property | Default | Rules |
|---|---|---|
| `pointsForWin` | 2 | 0–10, greater than `pointsForLoss` |
| `pointsForLoss` | 0 | 0–10 |
| `registration.minTeams` | 2 | 2–40; checked when the schedule is generated |
| `registration.maxTeams` | 20 | `minTeams`–40; checked at every registration |
| `registration.sameCountry` | `true` | Clubs must be from the competition's country (ignored for international competitions) |
| `registration.countries` | `[]` | ISO alpha-3 codes of the countries whose clubs may enter (cross-border leagues); empty = any |
| `registration.squad` | club defaults | Player registration limits: `minPlayers`, `maxPlayers`, `matchdayPlayers`, `recommendedRotation`, `maxForeignPlayers`, `maxNonEuPlayers`, `minDomesticPlayers`, `minAge` (`docs/ROSTER_RULES.md` §4) |
| `regularSeason.legs` | 2 | 1–4 |
| `regularSeason.daysBetweenRounds` | 7 | 1–30 |
| `regularSeason.startOffsetDays` | 0 | Days after the season start of the first round when none is given (the league's calendar) |
| `secondPhase` | none | Omit for no second phase |
| `secondPhase.groups` | — | `[{"name":"Championship round","from":1,"to":6}, …]`: ascending, non-overlapping first-phase positions, at least two clubs each (one per country with `byCountry`) |
| `secondPhase.byCountry` | `false` | Positions count within each country |
| `secondPhase.legs` / `daysBetweenRounds` | 2 / 7 | 1–4 / 1–30 |
| `secondPhase.carryOver` | `All` | `All`, `WithinGroup` or `None` |
| `tieBreakers` | `["HeadToHeadWins","HeadToHeadPointDifference","PointDifference","PointsFor"]` | Distinct values from §4 |
| `playoffs` | none | Omit for no playoffs |
| `playoffs.teams` | — | 2–32 (required with `playoffs`); not a power of two = byes for the top seeds |
| `playoffs.fromPosition` | 1 | Table position of the first seed |
| `playoffs.bestOf` | — | One odd number (1–7) per round, first round first; e.g. `[5,5,5]` for 8 teams |
| `playoffs.playIn` | `false` | The last two seeds come from a play-in of the next four positions |
| `playoffs.daysBetweenGames` | 2 | 1–30 |
| `promotion.direct` | 0 | Top positions promoted |
| `promotion.playoffWinner` | `false` | Needs `playoffs`; `fromPosition` must be after the direct places |
| `promotion.playoffPlaces` | 0 | 1 (champion), 2 (finalists) or 4 (semi-finalists); not together with `playoffWinner` |
| `relegation.direct` | 0 | Bottom positions relegated |

Checked against the entered clubs before scheduling: club count within min/max; playoff positions exist;
second-phase positions exist; promotion + relegation places ≤ clubs; with `playoffWinner`, no relegated position plays the playoffs; a
division above exists when promoting and one below when relegating.

Example (sample Serie A2: 6 clubs, leader promoted, 2nd–5th play off for the second place):

```json
{
  "pointsForWin": 2, "pointsForLoss": 0,
  "registration": { "minTeams": 6, "maxTeams": 6 },
  "regularSeason": { "legs": 2, "daysBetweenRounds": 7 },
  "tieBreakers": ["HeadToHeadWins", "HeadToHeadPointDifference", "PointDifference", "PointsFor"],
  "playoffs": { "teams": 4, "fromPosition": 2, "bestOf": [3, 3], "daysBetweenGames": 2 },
  "promotion": { "direct": 1, "playoffWinner": true }
}
```

## 4. Standings and tie-breakers

Clubs are ranked by standing points. Clubs level on points are separated by the first tie-breaker that splits
them; each smaller group is then ranked again from the first tie-breaker, so head-to-head criteria are
recounted among only the clubs still level (mini-league). Clubs equal on everything are ordered by club ID, a
deterministic stand-in for drawing lots.

| Tie-breaker | Meaning |
|---|---|
| `HeadToHeadWins` | Wins in games among the tied clubs |
| `HeadToHeadPointDifference` | Point difference in those games |
| `HeadToHeadPointsFor` | Points scored in those games |
| `PointDifference` | Overall point difference |
| `PointsFor` | Overall points scored |
| `FewestPointsAgainst` | Overall points conceded (fewer is better) |
| `Wins` | Overall wins |

The ranked table is always calculated from the results (`StandingsCalculator`); `LeagueStandings` keeps the
stored totals in step and is used where only totals are needed.

## 5. Running a season (`ICompetitionService`)

1. **Register** clubs: `RegisterClubsAsync` (or import `participants`). Checked: club country, maximum clubs,
   one league competition per club and season, not already entered. Closed once the schedule exists.
2. **Schedule:** `GenerateScheduleAsync(competition, season, firstRound?)` creates every regular-season
   fixture (round *r* on `firstRound + (r − 1) × daysBetweenRounds`, default first round = season start).
3. **Results:** `RecordResultAsync(fixture, home, away, overtimes)`; one result per fixture.
4. **Advance:** `AdvanceAsync` after results. When the regular season is complete it schedules the second phase
   (if any), then the play-in (if any), then seeds the playoffs and schedules game 1 of each series; afterwards it schedules the next game of each undecided series and the
   next round when both feeder series are decided. It returns the phase (`Registration`, `RegularSeason`,
   `SecondPhase`, `PlayIn`, `Playoffs`, `Completed`) and the fixtures created.
5. **Outcome:** `GetOutcomeAsync` for a completed season: champion, promoted and relegated clubs. Moving the
   clubs between divisions for the next season is done by season progression (Step 12).

Engine code: `Ebm.Domain.Competitions.Rules` (schema), `Ebm.Domain.Competitions.Engine` (`RoundRobinScheduler`,
`StandingsCalculator`, `PlayoffBracket`, `SeasonOutcomeCalculator`, `CompetitionRegistration`, `CompetitionSeason` for a whole competition season); orchestration in
`Ebm.Services.Competitions.CompetitionService`.

## 6. Adding another league

1. Import the country (`countries`), the league (`leagues`) and one `divisions` record per tier (level 1 = top).
2. Import one `League` competition per division with `country`, `divisionId` and its `rules` (§3). Make the
   promotion places of a division match the relegation places of the one above if division sizes must stay
   constant.
3. Import the clubs and a `seasons` record (if the season does not exist yet), then one `participants` record
   per club and division.
4. Generate each competition's schedule. No code change is needed.

The sample dataset (`data/sample/italy-sample.json`) is a complete example: Serie A (8 clubs, top 4 play
best-of-5 playoffs, 2 relegated) and Serie A2 (6 clubs, 1 promoted directly plus the playoff winner of 2nd–5th),
season 2026-27.

The European dataset (`data/europe/europe-2026-27.json`, Step 15) configures the first two divisions of 18 countries
with these rules, including a second phase (Slovenia, Czech Republic), a play-in (Germany, France), a cross-border
league with a by-country second phase (BNXT League: Belgium and the Netherlands), finalist promotion (ProA, 1 Liga)
and non-EU limits (Spain). It is generated by `build/generate-europe-dataset.py`: edit the script, not the JSON, and
run `python build/generate-europe-dataset.py`. Formats and their verification status: `docs/COUNTRY_REPORT.md`.
