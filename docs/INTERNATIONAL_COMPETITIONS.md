# International club competitions

Step 18. International competitions are `Cup` competitions without a country whose `cup` rules section has an
`entry` block. They run on the cup engine (`CupSeason`, docs/CUP_FORMATS.md): a group (league) phase, knockout rounds,
neutral-venue finals. This document covers qualification, the formats in the datasets, rewards and how the
calendar handles clubs playing several competitions.

## 1. Qualification (`cup.entry`)

```json
"entry": {
  "tier": 1,
  "slots": [
    { "type": "Holder" },
    { "type": "Winner", "competition": "EC" },
    { "type": "League", "countries": ["ESP"], "level": 1, "clubs": 3 },
    { "type": "Reputation", "level": 1, "countries": [], "clubs": 0 }
  ]
}
```

| Slot | Clubs taken |
|---|---|
| `Holder` | Last season's winner of this competition |
| `Winner` | Last season's winner of the cup whose short name is `competition` (e.g. the EuroCup winner into the EuroLeague) |
| `League` | For each listed country in turn, its best `clubs` clubs at division `level` (default 1) |
| `Reputation` | The `clubs` clubs of highest reputation playing a league at `level` this season (`countries` empty = any); `clubs: 0` = until the competition is full. Defaults to 1 club |

Rules (`ICompetitionService.QualifyAsync`, pure engine `InternationalQualification`):

- Competitions are filled by `tier` (1 first), then by ID; slots in order until `registration.maxTeams` is reached.
- A club plays one international competition per season. A club already taken (by a lower tier, an earlier slot or
  a hand entry) is skipped and the next club in the ranking moves up: "best `clubs` clubs" means the best ones still free.
- Clubs entered by hand before qualification (wildcards) keep their place and count towards `maxTeams`;
  `RegisterClubsAsync` refuses a club already entered in another international competition that season.
- `registration.countries` restricts every slot. A competition already drawn takes nobody.
- **Country ranking:** for each league at the level, last season's champion first, then its final table
  (`CompetitionSeason.FinalTable`). Without a finished last season (the first season of a game) the league's clubs
  this season by reputation. A club's country is the club's own country, so cross-border leagues (BNXT, ABA)
  rank each country's clubs separately. When a country has clubs in several leagues of one level, the stronger
  league (mean club reputation) ranks first.
- The draw (when the calendar first advances the competition) seeds the clubs by division level, then reputation;
  groups are filled from pots of seeds (docs/CUP_FORMATS.md §1).

## 2. Season cycle

- **New career** (`ICareerService.StartAsync`): `QualifyAsync` fills the competitions from club reputation.
- **Next season** (`StartNextSeasonAsync`): entries of `entry` competitions are not carried over; after promotion and
  relegation the competitions qualify again from the season just finished (holder, winners, final tables).
- The calendar draws each competition when it can, schedules the group phase at once, then each knockout round when
  the previous one is decided. The season completes when every drawn competition is finished.
- **Rewards** (`cup.rewards`) are paid when the competition ends, once per entry (`CompetitionParticipants.PrizeMoney`
  and `ReputationGain`): `entry` to every club, `win` per game won, `knockout` for reaching the knockout rounds after a
  group stage, `winner`/`runnerUp`; reputation alike (`reputationEntry`, `reputationKnockout`, `reputationWinner`,
  `reputationRunnerUp`, 0–10, club reputation capped at 100). Prize money goes to the club balance. Domestic cups
  may use rewards too.
- **History:** `GetHistoryAsync` gives champion and runner-up per season; the season review lists the winners.

## 3. Calendar interaction

- Domestic league seasons are scheduled first (season start). Every later fixture — cup and international group
  games, knockout rounds, second legs, and since Step 18 league second phases, play-ins and playoff games — is
  moved to the first day from its planned date on which neither club already has a fixture.
- `ICompetitionService.GetCalendarConflictsAsync(season)` lists every club with more than one fixture on a day
  (e.g. a league scheduled by hand onto existing cup dates). The career calendar produces none.
- International rounds are planned on different weekdays from the domestic leagues (`startOffsetDays`), so few games move.
- Rounds marked `"neutral": true` (single games only) are played without home advantage (`MatchSetup.NeutralVenue`).

## 4. Formats (`data/real-leagues`)

Defined in `build/dataset_common.py` (`INTERNATIONAL`); 2025-26 formats simplified to the engine,
**Unverified**. Every competition: groups drawn in pots, `Bracket` knockout (group winners meet runners-up).

| Competition | Tier | Clubs | Entry | Format | Rewards (winner) |
|---|---|---|---|---|---|
| Turkish Airlines EuroLeague (EL) | 1 | 16–20 | Holder, EuroCup winner, ESP 3, TUR 2, GRE 2, ITA 2, FRA 2, SRB 2, GER 1, LTU 1, reputation | One 20-club league phase (38 rounds, every 6 days), top 8 to two-legged playoffs, Final Four at a neutral venue | €2M, +3 rep. |
| BKT EuroCup (EC) | 2 | 16–20 | Holder, ESP/ITA/GER/FRA/TUR 2, GRE/LTU/SRB/SLO/POL 1, reputation | 2 groups of 10, top 6 each; group winners and runners-up bye to the quarter-finals; eighth-finals, QF, SF single games, two-legged final | €0.8M, +2 |
| Basketball Champions League (BCL) | 3 | 24–32 | Holder, 2 for the six strongest countries, 1 for the other twelve, reputation | 8 groups of 4, top 2; two-legged R16 and QF, Final Four at a neutral venue | €1M, +2 |
| FIBA Europe Cup (FEC) | 4 | 24–32 | Holder, 1 per country, reputation | 8 groups of 4, top 2; two-legged R16, QF, SF and final | €0.3M, +1 |

In `data/real-leagues` (seven countries) the reputation slots fill the places of the missing countries.

## 5. Limitations

- Real formats simplified: no EuroLeague play-in or best-of-five playoffs (two-legged ties), no EuroCup best-of-three
  final, no BCL second group phase, no qualifying rounds, no EuroLeague licences (holders, winners, league places and
  reputation stand in for them).
- Group draws do not keep clubs of one country apart.
- Reputation rewards only add; there is no decay yet (club reputation, Step 31).
- Prize money is paid as one sum at the end; no per-round payments or finance records (Step 23).
- League regular seasons are scheduled as before and do not move for existing fixtures; conflicts can only arise
  from manual scheduling and are reported by `GetCalendarConflictsAsync`.
- League playoff dates moved by the clash rule are not checked against the season end.
