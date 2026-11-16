# Cup formats

Step 17. Cups are `Cup` competitions whose rules JSON (`Competitions.RulesJson`) has a `cup` section. The engine is
pure domain code (`Ebm.Domain.Competitions.Engine.CupSeason`); `ICompetitionService` draws, schedules and stores.
League formats: `docs/COMPETITION_FORMATS.md`.

## 1. Supported formats

| Feature | How |
|---|---|
| Knockout rounds | `cup.rounds`, first round first, the last one is the final. 1–6 rounds (up to 64 clubs) |
| Single games | `legs: 1` (default). Hosted by the better seed, or the weaker one with `host: "LowerSeed"` |
| Two-legged ties | `legs: 2`: the weaker seed hosts leg 1, the better seed leg 2; decided on aggregate. Leg 2 goes to overtime while the aggregate is level (`MatchSetup.OvertimeMargin`), so it always has a winner. A level aggregate after a forfeit or an edited result goes to the winner of leg 2 |
| Byes | When fewer clubs than `2^rounds` enter, the top seeds go straight to round 2. Byes only in the first round: more than half the bracket must play it |
| Group stage | `groupStage`: `groups` (1–8, up to 20 clubs each: one group can be a whole league phase), `legs`, `advance` (top N per group), `daysBetweenRounds`. Pots of as many seeds as groups, one club of each pot per group. Qualifiers enter the knockout ordered by group finish, then group (A1, B1, …, A2, B2, …), so in a bracket each group winner meets another group's runner-up |
| Draw | `Bracket`: fixed bracket by seed (1 v 8, 4 v 5, 2 v 7, 3 v 6; groups serpentine), no randomness. `Seeded` (default): each round the better half of the seeds is drawn against the other half. `Open`: any pairing. Draws are random but reproducible: the seed comes from the competition and season IDs (`CupSeason.DrawSeed`), so a draw never changes once made |
| Neutral venue | `rounds[].neutral: true` (single games only): no home advantage, e.g. a Final Four (Step 18) |
| Rewards | `rewards`: prize money and reputation paid when the cup is over (docs/INTERNATIONAL_COMPETITIONS.md §2) |
| Seeding | Stored on `CompetitionParticipants.Seed` (1 = best) when the cup is drawn. Qualified clubs: their table position. Entered clubs: division level of their league this season (no league last), then club reputation, then ID |
| Qualification | `qualification`: `level`, `positions`, `afterRound`: the top N of the country's league at that division level, from the regular-season table counting rounds up to `afterRound` only (e.g. a Final Eight at the halfway point). Drawn as soon as those rounds are played; such cups cannot take clubs by hand and start again from the table every season |
| Finals | The last round. Final Eight / Final Four weekends: give the round a short `daysAfter` |
| History | Fixtures, results and seeds are never deleted. `ICompetitionService.GetHistoryAsync` lists every season of a competition with its champion and runner-up (leagues too); `GetCupAsync` gives a season's seeds, groups, byes and ties |

## 2. Rules schema (`cup`)

```json
{
  "registration": { "minTeams": 8, "maxTeams": 8 },
  "cup": {
    "qualification": { "level": 1, "positions": 8, "afterRound": 15 },
    "groupStage": { "groups": 2, "legs": 2, "advance": 2, "daysBetweenRounds": 7 },
    "draw": "Bracket",
    "host": "HigherSeed",
    "startOffsetDays": 145,
    "daysBetweenRounds": 7,
    "daysBetweenLegs": 7,
    "rounds": [
      { "name": "Quarter-finals" },
      { "name": "Semi-finals", "legs": 2, "daysAfter": 2 },
      { "name": "Final", "daysAfter": 1 }
    ]
  }
}
```

| Property | Default | Meaning |
|---|---|---|
| `rounds[].name` | — | Required |
| `rounds[].legs` | 1 | 1 or 2 |
| `rounds[].daysAfter` | `daysBetweenRounds` | Days from the previous round's (or group stage's) last game; 1–120 |
| `qualification` | none | Without it, the clubs entered in the cup take part |
| `groupStage` | none | Straight knockout without it |
| `draw` / `host` | `Seeded` / `HigherSeed` | See §1 |
| `startOffsetDays` | 0 | First cup games this many days after the season start (0–300); a qualification cup starts no earlier than the day after its last qualifying game |
| `daysBetweenRounds` | 7 | 1–60 |
| `daysBetweenLegs` | 7 | 1–30 |

Validation: a cup section only on a `Cup` competition; no playoffs, second phase, promotion or relegation with it;
the clubs reaching the knockout (entered, qualified or `groups × advance`) must be more than half of and at most
`2^rounds`; groups need at least `advance + 1` and at most 20 clubs each. A `Cup` competition without a `cup` section
(older data) is never scheduled.

## 3. Running a cup (`ICompetitionService`)

- `GenerateScheduleAsync` makes the draw (seeds stored, group stage or first round scheduled) or says why it cannot yet.
- `AdvanceAsync` draws a cup as soon as it can (entered clubs, or the qualification decided), then schedules the next
  round (`daysAfter` / `daysBetweenRounds` after the cup's last game) when every tie of the current one is decided,
  and each second leg `daysBetweenLegs` after its first leg. Every cup game is moved on to the first day from its
  planned date on which neither club has another fixture.
- Phases: `Registration` (not drawn), `GroupStage`, `Knockout`, `Completed`. `GetOutcomeAsync` gives the winner
  (`Champion`) and `RunnerUp`; `GetStandingsAsync` is empty for a cup; `GetGroupTablesAsync` gives its groups.
- The career runs cups like leagues: game days play their fixtures and advance them; the season is complete when
  every drawn cup is finished too; the season review lists cup winners; the next season keeps the entries of cups
  without qualification. The *Career* screen shows a cup's groups as standings and its ties, byes, winner and past
  winners below them.

## 4. Country configurations (`data/europe`, `data/italy`, `data/real-leagues`)

Generated by `build/generate-europe-dataset.py` (`CUPS`); 2025-26 formats simplified to the engine, **Unverified**.

| Country | Cup | Entry | Format |
|---|---|---|---|
| Italy | Coppa Italia Frecciarossa | Top 8 of LBA after round 15 | Final Eight: QF, SF, F on consecutive days, bracket |
| Spain | Copa del Rey | Top 8 of Liga Endesa after round 17 | Final Eight, bracket (real: host + top 7) |
| Germany | BBL-Pokal | Top 16 of BBL after round 4 | R16, QF, Final Four; seeded draw (real: previous-season table) |
| France | Coupe de France | Betclic ÉLITE clubs | R16, QF, Final Four; open draw, lower seed hosts (real: lower divisions too) |
| Greece | Greek Cup | Stoiximan GBL clubs (12) | R16 (4 byes), QF, Final Four; seeded |
| Turkey | Turkish Cup | BSL clubs (16) | 4 groups of 4 (single round robin), top 2 to a Final Eight; bracket |
| United Kingdom | SLB Cup | SLB clubs (10) | 2 groups of 5, top 2 to two-legged semi-finals, final |
| Lithuania | Karaliaus Mindaugo taurė | LKL clubs (10) | First round (6 byes), two-legged QF, Final Four |
| Serbia | Kup Radivoja Koraća | Top 8 of KLS after round 15 | Final Eight, bracket (real: ABA League clubs enter directly) |
| Croatia | Kup Krešimira Ćosića | Premijer liga clubs (12) | First round (4 byes), QF, Final Four |
| Slovenia | Pokal Spar | Top 8 of Liga Nova KBM after round 9 | Final Eight, bracket |
| Belgium | Beker van België | Belgian BNXT clubs (10) | First round, QF, two-legged SF, final |
| Netherlands | NBB Beker | Dutch BNXT clubs (10) | First round, QF, Final Four |
| Portugal | Taça de Portugal | Liga Betclic clubs (14) | First round (2 byes), QF, Final Four; open draw, lower seed hosts |
| Poland | Puchar Polski | Top 8 of OBL after round 15 | Final Eight, bracket |
| Czech Republic | Český pohár | Kooperativa NBL clubs (12) | First round (4 byes), QF, Final Four |
| Finland | Suomen cup | Korisliiga clubs (12) | First round (4 byes), QF, Final Four |
| Sweden | — | — | No domestic cup |

`data/real-leagues` gets the cups of the countries whose top division it contains; `data/sample/italy-sample.json`
keeps its format-less "Italian Cup" (tests configure it).

## 5. Limitations

- Domestic Final Eights/Fours in the datasets are still hosted by the better seed (rounds can be made `neutral` since Step 18).
- Legs are never drawn: like every engine game, a level first leg goes to overtime (FIBA allows a drawn first leg).
- Qualification only from the current season's table of the cup's own country; no entry by previous-season
  position, cup holders, host clubs, lower-division entrants joining in later rounds, or supercups.
- No reseeding inside a bracket draw; seeded and open draws are made again every round.
- (Fixed in Step 18) League playoff, play-in and second-phase games now also avoid days with other fixtures.

International competitions (Step 18, `cup.entry`): docs/INTERNATIONAL_COMPETITIONS.md.
