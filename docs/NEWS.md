# News, media and club reputation

Step 31. Code: `Ebm.Domain.News` (`NewsItem`, `ReputationChange`, `MediaRules`, `NewsWriter`), `Ebm.Services.News`
(`INewsService`, `NewsService`), the *News* tab (`NewsViewModel`, `Themes/News.xaml`).

## 1. Principles
- **Events first.** A story is written only after the event it reports was stored: a played match, a completed transfer,
  a new injury, a running contract, a standings row, a reputation change. Every number in a story comes from those
  records; nothing is invented.
- **Once per event.** Each story has a unique `Key` naming its event (table below). The writer skips keys already
  stored or already written in the same run, so repeating a day, a season end or a season start writes nothing new.
  This, plus writing only from stored facts, is what keeps reports from contradicting each other.
- **History.** Stories and reputation changes are never edited or deleted. They link to their records by plain IDs
  (no foreign keys), so they outlive records removed in the editor.
- **Deterministic.** The wording variant is chosen by an FNV-1a hash of the key (`NewsWriter.Pick`); no `Random`.

## 2. Event triggers
The career calendar calls the service; news is written last on each game day, after matches, transfers, injuries,
contracts and chemistry.

| Category | Trigger (when) | Who | Key |
|---|---|---|---|
| MatchReview | Match played on the day (not forfeited) | User's club | `review:{matchId}` |
| PressConference | Same match; quote by the career manager | User's club | `press:{matchId}` |
| Interview | Same match; the club's best player (points + rebounds + assists) | User's club | `interview:{matchId}` |
| MediaReaction (streak) | After a match, the run of wins/defeats (all competitions) is a multiple of 5 | User's club | `streak:{matchId}:{clubId}` |
| MediaReaction (upset) | Winner's reputation ≥ 15 below the loser's (`MediaRules.UpsetGap`) | Every club | `upset:{matchId}` |
| MatchPreview | The day before a fixture not yet played; standings position (league regular season), form (last 5), unavailable players (open injuries above minor) | User's club | `preview:{fixtureId}` |
| Transfer | Completed transfer dated in the last 7 days | User's club (any kind); elsewhere transfers, release clauses, options to buy | `transfer:{transferId}` |
| Injury | New injury dated in the last 7 days | User's club (any); elsewhere serious and above | `injury:{injuryId}` |
| Rumor | Mondays: active contract with the user's club ending within 120 days and no later contract signed | User's club | `rumor:contract:{contractId}` |
| FanExpectations (start) | Career start and each new season | User's club | `fans:{seasonId}:{clubId}:start` |
| FanExpectations (mood) | Mondays, after 5 league games, when the mood differs from the last mood story of the season | User's club | `fans:{seasonId}:{clubId}:{date}` |
| Reputation | Season end, when the club's reputation changed | Every club | `reputation:{seasonId}:{clubId}` |

A transfer story says it "confirms the reports" when a rumour about the player was published before.

## 3. Writing
`NewsWriter` turns facts into a `Story` (headline, body, sentiment −2…+2). Reviews pick their verb from the margin
(≤ 3 close, ≥ 20 heavy) and mention a run of 3 or more; sentiment ±2 for margins of 15 or more. Fan stories carry the
mood as sentiment. Headlines are cut at 200 characters, bodies at 2000.

## 4. Club reputation
Reputation (1–100, `Club.Reputation`) moves only through `ReputationChange` rows, each with its cause and event:

| Cause | Points | Event |
|---|---|---|
| UpsetWin / UpsetDefeat | +1 / −1 | The match (`MatchId`), on the match day |
| LeagueTitle | +3 | Completed league, season end |
| CupWin | +2 | Completed cup or international competition |
| Promotion / Relegation | +2 / −3 | Completed league outcome |
| ObjectiveMet / ObjectiveMissed | +1 / −1 | Board objectives of the season (the career's verdict) |
| CompetitionReward | the competition's `reputation*` rewards | Paid by `CompetitionService` when a cup with rewards ends (last fixture date); no story of its own |

- Each change is keyed (`reputation:{matchId}:{clubId}`, `reputation:{seasonId}:{clubId}:{cause}`,
  `reputation:{competitionId}:{seasonId}:{clubId}:reward`), so an event moves
  a reputation once. A change that would not move the value (at 1 or 100) is not stored.
- Season-end changes are applied before the next season's board objectives and fan expectations are set
  (`CareerService` calls `ProcessSeasonEndAsync` right after judging the objectives).
- A club's changes chain: each `Before` equals the previous `After`, and the last `After` is the club's reputation
  (`GetReputationHistoryAsync`).

## 5. Fans
- **Expectation** from the club's reputation rank in its league (ties by name): rank 1 → title; top third → playoffs;
  up to two thirds → mid-table; else survival. Target position: 1, clubs / 3, (2 × clubs + 2) / 3, clubs − 2.
- **Mood** after 5 league games: score = (target − position) / max(1, clubs / 6) + (wins in last 5 × 5 / games − 2.5) / 2.5;
  ≥ 1 delighted, ≥ 0 content, ≥ −1 restless, else angry.
- `GetFansAsync` gives the current view (reputation, rank, expectation, position, form, mood) for the News tab.

## 6. News tab
Filters by club (main or other club of a story) and category; newest first (max 500). Shows the selected story, the
club's fans line and its reputation history (newest first). Read-only.

## 7. Limits
- Stories are written for the user's club in detail; other clubs get upsets, headline transfers, serious injuries and
  season verdicts only.
- No user-chosen press-conference answers: quotes follow the result.
- Rumours cover expiring contracts only (no invented transfer interest).
- English text only.
