# Competition Formats

How league competitions are configured and run (Step 04). Competition rules are data: a JSON object in
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
| Fixture | `Fixtures` | `Stage` (`RegularSeason`/`Playoffs`), `Round`, `GameNumber` (game in a playoff series) |
| Result | `Matches` | One per fixture, no draws |
| Table | `LeagueStandings` | Stored totals per club (W/L, points for/against, standing points), updated with each regular-season result |

## 2. Supported formats

* **Regular season:** round robin with 1–4 legs (`legs: 2` = home and away). Any number of clubs from 2 to 40;
  with an odd number one club rests each round. Each club plays `(clubs − 1) × legs` games.
* **Playoffs (optional):** single-elimination bracket of 2, 4, 8, 16 or 32 clubs seeded from the final table
  (from any position, e.g. 2nd–9th), best-of-1/3/5/7 per round, fixed bracket (1 v N, 2 v N−1, …; no
  reseeding), higher seed has home court (best of 3: H-A-H, 5: 2-2-1, 7: 2-2-1-1-1). Only the games a series
  needs are scheduled.
* **Points systems:** any `pointsForWin` > `pointsForLoss` (0–10), e.g. 2/0 or 2/1.
* **Tie-breakers:** an ordered list, applied to clubs level on points (§4).
* **Promotion:** top N positions directly, plus optionally the playoff champion.
* **Relegation:** bottom N positions.
* **Champion:** the playoff winner, or the table leader without playoffs.

Not supported yet: cups and knockout-only formats (`Cup`/`Tournament` competitions cannot be scheduled; Step 17),
conferences/groups, relegation playoffs, play-in rounds, two-legged aggregate ties, reseeding, overtime-loss points.

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
| `regularSeason.legs` | 2 | 1–4 |
| `regularSeason.daysBetweenRounds` | 7 | 1–30 |
| `tieBreakers` | `["HeadToHeadWins","HeadToHeadPointDifference","PointDifference","PointsFor"]` | Distinct values from §4 |
| `playoffs` | none | Omit for no playoffs |
| `playoffs.teams` | — | 2, 4, 8, 16 or 32 (required with `playoffs`) |
| `playoffs.fromPosition` | 1 | Table position of the first seed |
| `playoffs.bestOf` | — | One odd number (1–7) per round, first round first; e.g. `[5,5,5]` for 8 teams |
| `playoffs.daysBetweenGames` | 2 | 1–30 |
| `promotion.direct` | 0 | Top positions promoted |
| `promotion.playoffWinner` | `false` | Needs `playoffs`; `fromPosition` must be after the direct places |
| `relegation.direct` | 0 | Bottom positions relegated |

Checked against the entered clubs before scheduling: club count within min/max; playoff positions exist;
promotion + relegation places ≤ clubs; with `playoffWinner`, no relegated position plays the playoffs; a
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
4. **Advance:** `AdvanceAsync` after results. When the regular season is complete it seeds the playoffs and
   schedules game 1 of each series; afterwards it schedules the next game of each undecided series and the
   next round when both feeder series are decided. It returns the phase (`Registration`, `RegularSeason`,
   `Playoffs`, `Completed`) and the fixtures created.
5. **Outcome:** `GetOutcomeAsync` for a completed season: champion, promoted and relegated clubs. Moving the
   clubs between divisions for the next season is done by season progression (Step 12).

Engine code: `Ebm.Domain.Competitions.Rules` (schema), `Ebm.Domain.Competitions.Engine` (`RoundRobinScheduler`,
`StandingsCalculator`, `PlayoffBracket`, `SeasonOutcomeCalculator`, `CompetitionRegistration`); orchestration in
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
