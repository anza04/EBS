# Long-term world and historical records

Step 32. Code: `Ebm.Domain.History` (`SeasonAward`, `HallOfFameEntry`, `LeagueSeasonSnapshot`, `HistoryRules`),
`PlayerGenerator.CreateYouth`, `Ebm.Services.History` (`IHistoryService`, `HistoryService`), the *History* tab
(`HistoryViewModel`, `Themes/History.xaml`).

## 1. Principles
- **Nothing is deleted.** Fixtures, results, box scores, standings, contracts, transfers, club spells, news and
  reputation changes already stay forever (docs/SEASON_LIFECYCLE.md §6, docs/NEWS.md). Step 32 adds what cannot be
  derived later (awards, hall of fame, league snapshots) and derives the rest (careers, records) from stored results.
- **Retired players stay.** A retired player keeps his row (`Players.RetiredOn`), statistics, spells and awards;
  every history query includes him.
- **Plain IDs.** History tables link by ID without foreign keys, so records survive editor deletions.
- **Repeat-safe.** Each yearly step checks what it already wrote (unique indexes, keys) and skips it.
- **Deterministic.** Seeds come from dates and IDs (`ScoutingRules.StableSeed`), never `HashCode`.

## 2. Yearly cycle (career calendar)

| When | Step | Service call |
|---|---|---|
| `StartNextSeasonAsync` (season complete), before the news verdict | Awards, league snapshots, reputation drift | `ProcessSeasonEndAsync(season, date)` |
| Season end date (30 June), after retirements | Hall of fame, coaching careers | `ProcessRetirementsAsync(retired, date)` |
| Same day | Youth intake, AI youth promotions | `ProcessYouthIntakeAsync(date, managedClubs)` |

The game day report carries the last two as `GameDayReport.World` (`WorldYear`).

## 3. Awards (`SeasonAwards`)
Per completed **league** competition and season, from the regular season's box scores:

| Award | Rule |
|---|---|
| MostValuablePlayer | Best value score = efficiency × (0.6 + 0.8 × team win share); efficiency per game = pts + 1.2 reb + 1.5 ast + 2 stl + 2 blk − tov |
| TopScorer / TopRebounder / TopAssists | Best per-game average |
| BestDefender | Best (2 stl + 2 blk + 0.5 defensive reb) per game |
| YoungPlayer | Best value score, aged ≤ 22 on the season's first day |
| AllLeagueTeam | Top five value scores (`Rank` 1–5) |
| CoachOfTheSeason | Club with the most places gained over its reputation rank (ties: better position); the head coach under contract, or the career manager / "club coach" when none |

- Candidates must have played at least half the most games anyone played (`MinGameShare`). Ties go to fewer games,
  then the lower ID.
- A player traded within the league counts all his games, for the club where he played most.
- `Value` stores the figure ×10 (places for the coach). Unique (`SeasonId, CompetitionId, Kind, Rank`).

## 4. Careers and records (derived)
- **Player career** (`GetPlayerCareerAsync`): one line per season, competition and club (all stages: games, MPG, PPG,
  RPG, APG, points), career totals, awards, club spells, titles (competitions won by his club in a season he played
  for it), hall of fame entry, coaching role, academy.
- **Club honours** (`GetClubHonoursAsync`): trophies (every competition won), awards of its players and coach,
  academy players produced. League seasons stay in `ICareerService.GetClubHistoryAsync`.
- **Competition** (`GetWinnersAsync`, `GetRecordsAsync`): winners and runners-up; records: most points, rebounds and
  assists in a game, most points by a team in a game, best scoring average in a season (award eligibility), best
  regular-season record, most titles.

## 5. Hall of fame and coaching careers
Applied to each player who retired that day.
- **Score** = games / 10 + points / 200 + (rebounds + assists) / 300 + 8 per title + 15 per MVP + 5 per other award +
  3 per all-league selection + (reputation − 60 if above). Integer divisions. Inducted at **≥ 50**
  (`HallOfFameThreshold`); `HallOfFame` keeps the score and career figures at induction. The reputation term lets
  stars whose careers began before the game's records qualify.
- **Coaching**: aptitude = mean of leadership, decision making, court vision and work rate. ≥ 60 → assistant coach;
  ≥ 72 or hall of fame → head coach. A new `Staff` row (`FormerPlayerId` = the player) without a contract, so he is on
  the staff market (docs/STAFF.md §5). Attributes: tactical knowledge = mean(decision making, court vision, defensive
  positioning) − 5; development = work rate − 10; motivation = leadership; scouting = concentration − 15; medical 20;
  reputation = playing reputation; experience 0. Style: Motivator if leadership ≥ work rate + 10, YouthDevelopment
  if the reverse, else Balanced.

## 6. Youth intake
- Every club, every year: 1 player, 2 from reputation 60 (`IntakeSize`). Age 17; quality 25 + reputation / 5 (± 4,
  20–60); potential quality + 20 + reputation / 5 + 0…15 (≤ 95). Names recombine existing players of the club's
  country (`PlayerGenerator`), positions at random.
- Signed on a youth contract (minimum salary, 3 years, starting that day), in the youth squad (`RosterRole.Youth`),
  `Players.YouthClubId` = the club. No transfer record.
- **Promotion** (AI clubs only): youth players aged ≥ 19 rated at least as high as the weakest first-team player move
  to the first team (inactive), best first, up to the squad maximum. The user promotes his own in the squad screen.
- Youth players whose contracts run out join the free-agent pool like anybody else.

## 7. Long-term league evolution
- **Reputation drift**: every season each club's reputation moves back towards 50 by 5% of the distance, rounded
  towards zero (90 → −2, 70 → −1, 60 → 0), recorded as a `LongTermDrift` reputation change. With the news verdicts
  (docs/NEWS.md §4) this keeps reputations from piling up at 100 over many seasons while titles still lift clubs.
- **Snapshots** (`LeagueSeasonSnapshots`, one per league season): clubs, mean and top reputation, mean overall of the
  clubs' best eight players at season end, champion. `GetLeagueEvolutionAsync` lists them over time.
- Promotion and relegation (docs/SEASON_LIFECYCLE.md §5), development and retirement (docs/DEVELOPMENT.md), AI
  recruitment and the free-agent top-up (docs/AI_MANAGEMENT.md) run unchanged.

## 8. History tab
Competition (winners, awards, records, league evolution), Hall of fame, Player career (any player, retired included),
Club honours (synced with the locked club). Read-only.

## 9. Limits
- Awards for league competitions only (cups record their winners); no playoff MVP.
- Records cover what the game stored; seasons before the career have no box scores.
- Youth players have random positions; no youth competitions.
- Former players coach from the staff market only (no assistant-to-head promotion path).
