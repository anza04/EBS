# Changelog

All notable changes to this project are documented here, one section per development step.
Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.0] — 2026-11-21 — STEP 34: Final optimization and release

### Added
- Save integrity (docs/SAVE_FILES.md §4): saves are verified (`PRAGMA quick_check`) before they replace an existing
  save; damaged saves are refused by *Load*.
- Automatic backups: *Load* and *New game* first keep the replaced career as an `Autobackup <timestamp>` save, and the
  game is backed up at startup before a database upgrade; the newest five are kept.
- Startup recovery: a damaged game database is kept aside (`ebm.db.damaged-<timestamp>`) and restored from the newest
  intact automatic backup.
- `PRAGMA optimize` when the game closes.

### Changed
- Performance: the Monday transfer window opens only for AI clubs short of players that can afford a signing (balance
  for the minimum salary and, above the squad minimum, wage-budget room); clubs that could sign nobody no longer reload
  the market every week (the season-2 slowdown in the known issues).
- Zone defences read "2-3 zone", "3-2 zone", "1-3-1 zone" on screen.
- Version 1.0.0 (Milestone 3 release).

## [0.33.0] — 2026-11-21 — STEP 33: Modding and custom database support

### Added
- Database export (docs/MODDING.md §3): the whole importable world as one `ebm-dataset` JSON file in the import
  format; it imports into a new game unchanged and exports back identically.
- Whole-database validation (§4): overlapping or post-retirement contracts, short squads in current competitions,
  unreadable competition rules, unlinked leagues and country mismatches, as errors and warnings.
- Community database packages (`.ebmpack`, §2): a zip with an `ebmpack.json` manifest and import files, kept in
  `<DataRoot>\mods`. Packages are checked by a dry-run import, installed as a new game or into the current game, and
  created from the current database.
- Install protection (§5): the game in progress is saved as a restore point first; a failed import or new validation
  errors load it back. Saved careers are separate copies and never change.
- *Mods and export* tab (packages, check, install, create, export, database check).

### Changed
- UI refresh ("Courtside Light", plain WPF, no new packages): restyled buttons (primary, secondary, destructive), text
  boxes with placeholders, combo boxes, check boxes, tables, sub-tabs, scroll bars and tooltips; grouped sidebar
  navigation with icons instead of the 25-tab strip; one type scale and card padding across screens.
- Values read the same everywhere: enum values as words ("Shooting guard", "Under contract"), dates in the user's
  short-date format, numbers in the user's culture; ratings shown as coloured badges; narrow grid columns widened.

## [0.32.0] — 2026-11-20 — STEP 32: Long-term world simulation and historical records

### Added
- Long-term world (docs/HISTORY.md): season awards per league (MVP, top scorer/rebounder/passer, best defender, young
  player, all-league team, coach of the season); hall of fame for retired players; retired players with the right
  attributes start coaching careers (new staff on the market); yearly youth intake for every club (academy players
  on youth contracts, AI promotions); reputation drift towards the middle and league snapshots for long-term evolution.
- `IHistoryService`: player careers (retired players included), club honours, awards, hall of fame, winners, records,
  league evolution. The career calendar runs it at season end, after retirements and for the youth intake;
  `GameDayReport.World`.
- *History* tab: competition (winners, awards, records, evolution), hall of fame, player career, club honours.
- Migration `LongTermHistory` (tables `SeasonAwards`, `HallOfFame`, `LeagueSeasonSnapshots`; `Players.YouthClubId`,
  `Staff.FormerPlayerId`).
- Tests: `HistoryRulesTests` (14 unit); `HistoryServiceTests` (a season: awards, records, winners, snapshots, drift,
  hall of fame, coaching career, youth intake, repeat-safety, screen and render); `LongTermWorldTests` (slow: three
  seasons of the sample world).

## [0.31.0] — 2026-11-20 — STEP 31: News, media and club reputation

### Added
- News written from stored game events (docs/NEWS.md): match reviews, press conferences, player interviews, streaks
  and previews for the career club; upsets, headline transfers and serious injuries for every club; contract rumours;
  fan expectations (season start) and mood changes; season reputation verdicts. Each story is keyed by its event, so
  nothing is reported twice; stories are kept forever and filterable by club, category and date.
- Club reputation history: `ReputationChange` rows for upsets, league titles, cup wins, promotion, relegation, board
  objectives and competition rewards, chaining from one value to the next.
- `INewsService` (`ProcessDayAsync`, `ProcessSeasonEndAsync`, `ProcessSeasonStartAsync`, `GetNewsAsync`,
  `GetReputationHistoryAsync`, `GetFansAsync`); the career calendar calls it each day and at season start/end;
  `GameDayReport.News`.
- *News* tab: stories by club and category, story text, fans line and reputation history.
- Migration `NewsAndReputation` (tables `NewsItems`, `ReputationChanges`).
- Tests: `MediaRulesTests` (5 unit); `NewsServiceTests` (a played season and the next season's start: no duplicates,
  stories match matches/transfers/injuries/contracts, reputation chains, history filters, repeat-safe, News screen
  and render); `InternationalCompetitionTests` checks reward reputation changes.

### Changed
- Competition reputation rewards (Step 18) are now stored as `ReputationChange` rows (`CompetitionReward`).

## [0.30.0] — 2026-11-20 — STEP 30: Advanced match visualization

### Added
- 2D court for live matches (docs/MATCH_VISUALIZATION.md): `CourtTracker` places every play-by-play event on a FIBA
  court (half-court formations, matchups, recorded shot spots, contest distance, blocks, rebounds, steals, fouls, free
  throws, substitutions, tip-off); `CourtPlayback` tweens the frames over time with pause, resume, skip and speed.
- `MatchEvent.Shot`: field-goal events carry their `ShotRecord` (no new random draws; results unchanged).
- Match centre *Court* tab (`CourtView`): players with initials, ball with arc and shadow, current play line; pause,
  skip to latest, speed 0.5–4×, *Watch the match* (possessions play at the court's pace, paused with it). Live
  matches open on the court.
- Tests: `MatchVisualizationTests` (6 unit: sync with the engine, positions vs events, ends switching at half time,
  identical results with/without visualization and across modes, playback pause/resume/skip, determinism);
  render-test checks of the court and watch/pause in the match centre.

## [0.29.0] — 2026-11-20 — STEP 29: Advanced basketball simulation

### Added
- Shot detail in the match engine (`MatchSimulation.Shots.cs`, docs/MATCH_ENGINE.md §9): FIBA shot zones and court
  coordinates (`ShotZone`, `ShotZones`, `ShotRecord`), defensive contest quality (open/contested/tight: make, foul and
  block effects), player decisions (isolation tendency, passing up tight shots), foul drawing, turnover causes
  (`TurnoverCause`), transition (speed edge, secondary breaks, long rebounds), rebounding position, clutch time and
  clutch rating, primary-defender attempts (individual matchups).
- `TeamAnalytics` on `MatchResult`/`MatchSimulation`: turnover causes, fast-break and secondary-break possessions,
  fast-break and second-chance points, passed-up shots.
- Box scores store on-court points for/against and possessions, defended attempts and the shot chart (`ShotData`,
  validated against the attempts); plus/minus is stored through them. Migration `20261120154321_AdvancedSimulation`
  (add-column only).
- `AdvancedStatistics`: TS%, eFG%, usage, assist %, rebound %, turnover %, on-court offensive/defensive/net rating,
  plus/minus; team possessions, pace, ratings, four factors (docs/MATCH_ENGINE.md §10).
- Match centre *Advanced* and *Shot chart* tabs (`ShotChartView`, zone table); *Season stats* TS%, eFG%, TOV%, +/-,
  ORtg/DRtg/Net columns and the selected player's season shot chart.
- Tests: `AdvancedSimulationTests` (8 unit); stored-report shot-chart checks; render of the new screens.

### Changed
- Fast-break rim edge 0.12 → 0.10, inside block base 0.11 → 0.12 (contest compensation); league averages now 81.7
  points, 75.7 possessions (docs/MATCH_ENGINE.md §7). A quality gap weighs more (70 vs 55 wins 96%).
- `PlayerLine.PlusMinus` is derived from `PointsFor`/`PointsAgainst`; steals by interception have their own commentary.

## [0.28.0] — 2026-11-20 — STEP 28: Coaching and staff management

### Added
- Staff attributes (`StaffProfile`): tactical knowledge, development, motivation, scouting, medical expertise,
  reputation, experience (+1 per four years to every attribute, at most +5), coaching style and a head coach's preferred
  offense/defense. Role ability derived per role (`StaffRules.RoleAbility`). New role `DevelopmentCoach`. `docs/STAFF.md`.
- `IStaffService`: club staff and responsibilities, staff market (topped up at the career start and on Mondays), hiring
  (demand from ability and reputation, reputation gap, per-role maximum, 1–5 years) and dismissal (remaining salary
  booked as a settlement).
- Head coaches for every club; AI clubs recruit every role from the market (else generated) and replace head coaches
  who missed the board objectives at season end; the user's club is staffed once at the career start.
- Influence: head coach tactical knowledge in the match engine (±1% make probability, game plan from 40, earlier
  timeouts from 70, late closing review below 40); motivation and style in the weekly morale; style in growth (youth
  development ×1.10 up to 23, disciplinarian ×1.05) and in AI tactics (`TacticPlanner`); preferred systems.
- *Staff* tab: staff with attributes and contracts, responsibilities with the ratings used, market with hire/dismiss.
- Migration `20261120145953_StaffManagement` (`Ability` → `TacticalKnowledge`, new attribute columns filled from it).
- Tests: `StaffRulesTests` (7 unit), `StaffServiceTests` (6 integration), Step 28 upgrade test.

### Changed
- Coaching ability of training = mean effective development of head, assistant and development coaches (was the mean
  ability of head and assistant coaches); staff salaries come from `StaffRules` (same rates for existing roles).
- Career start hires staff before planning AI tactics.

## [0.27.0] — 2026-11-18 — STEP 27: Advanced tactical engine

### Added
- Game management in every tactic (`SituationSettings`): matchup hunting, play calling (flow / hot hand / set plays),
  zone offenses, end-of-quarter plans (two-for-one, hold for the last shot), foul management, rotation strategies,
  late-game clock management and intentional fouls, blowout rest, coach adjustments. `docs/TACTICS.md` §7.
- Per-player defensive matchups (`TacticalAssignment.Matchup`) and new pick-and-roll coverages `Ice` and `Under`;
  switching rules now act on pick and rolls (size mismatches). `docs/MATCH_ENGINE.md` §8.
- The engine's coach for AI tactics: a game plan against each opponent and in-game adjustments (zones, presses,
  stoppers, pace, zone offenses), with reasons.
- Tactical notes stored with each result (`Matches.TacticalNotes`) and shown in the match report; situational plays as
  commentary events (`MatchEventKind.Situation`).
- *Tactics* screen: game-management section and a *Guards* column. Migration `20261118133803_TacticalEngine`.
- `TacticalEngineTests` (12 measured effects), persistence and report tests.

### Changed
- AI tactics (`TacticPlanner`) let the coach adjust, play two-for-ones and size the rotation to the bench.
- Statistical validation: 79.1 points and 21.4 free throws per team (was 77.3 / 20.3) from the default late-game
  management.

### Changed (since 0.26.0)
- The slow suite plays `data/real-leagues` (138 clubs, 2,598 players) instead of the generated European dataset (512
  clubs, 6,168 players): `EuropeanTransferWindowTests`, `MilestoneTwoTests`, `RealLeaguesCompetitionTests` (was
  `EuropeDatasetTests`) and `RealLeaguesDatasetTests` (now also stable IDs, duplicates, a 20-day career); shared
  `TestSupport/RealLeagues`.
- `build/generate-europe-dataset.py` became the helper module `build/dataset_common.py`; the real-leagues generator no
  longer reads `data/europe` (curated club identities frozen in `build/real-leagues-clubs.json`; output unchanged).

### Fixed
- AI emergency signings (`FillSquadsAsync`) respect the wage budget left: the best free agent within budget and balance;
  below the squad minimum the cheapest one the balance covers; injury cover only within budget (27 of 138 real-leagues
  clubs were in debt after two seasons). `docs/AI_MANAGEMENT.md`, `docs/CONTRACTS.md`, `docs/INJURIES.md`.
- Emergency signings no longer stall the off-season (~45 s per signing with 900 free agents): the free-agent pool is read
  once and demands in chunks (`IContractService.GetNegotiationsAsync`, shared reads memoized).
- `MilestoneTwoTests`: cross-border moves checked over the whole run (none in the first season on real squads); no
  scout-report check (AI clubs make no assignments, `docs/SCOUTING.md` §6); season timings printed before the checks.

### Performance
- Game days about 3.5× faster on `data/real-leagues` (60 days: 356 s → 101 s):
  - Matchday squads, short-squad checks, AI tactics, surplus releases and the chemistry week read all squads at once
    (`IClubService.GetRostersAsync`) and save changed squads in one unit of work (`SaveRostersAsync`); a single squad
    save reads only its club's members (was every player's club).
  - Contract dates read only open club spells (was the whole club history, scanned once per running contract).
  - Untracked reads (`IRepository.ListUntrackedAsync`, `ClubMembership.SavedClubsAsync`) for the daily read-only loads:
    memberships, fixtures, results and box scores in medical, development, training, chemistry, competition phases
    and the career day setup.

### Removed
- The generated European dataset `data/europe` and its Italian slice `data/italy`, with `EuropePlayerDatabaseTests` and
  `ItalyDatasetTests`.

## [0.26.0] — 2026-11-18 — STEP 26: Advanced roster management and chemistry

### Added
- Squad hierarchy (`ChemistryModel.Hierarchy`): rank by overall, expected role and minutes (Step 25 role table, raised
  by contract promises), leader score (leadership and experience), captain and leaders (`docs/CHEMISTRY.md` §1).
- Role satisfaction from minutes per game over four weeks (happy / content / unhappy) (§2).
- Weekly morale on Mondays (`IChemistryService.ProcessWeekAsync`, `GameDayReport.Chemistry`): results, playing time,
  drift and leaders (§3).
- Player relationships (`PlayerRelationship`): grow with shared court time and nationality, fall with conflicts;
  internal conflicts between an unhappy player and the teammate ahead of him, reported as career news (§4).
- Team chemistry of the core from morale, relationships, leadership and tactical role fit (`ChemistryModel.Team`) (§5).
- Match engine: chemistry changes make probability (±2%) and turnovers (∓0.8%), morale changes a player's form
  (±1%); `TeamSheet.Chemistry` set by `MatchService` (§6).
- AI squad balance: unhappy players get rotation minutes over slightly better teammates and are released first (§7).
- *Dressing room* tab: chemistry, hierarchy, satisfaction, morale, leaders, conflicts and relationships.
- Migration `PlayerChemistry`: table `PlayerRelationships`.

## [0.25.0] — 2026-11-18 — STEP 25: Advanced contract negotiations

### Added
- Player demands (`ContractNegotiator.Demands`, `PlayerDemands`): asking value with premiums for competing offers, the
  agent relationship and broken promises; preferred contract lengths by age; an expected squad role from the player's
  rank at the club; stars insist on a role promise, players far above the club on a release clause
  (`docs/CONTRACTS.md` §4).
- Offer value counts playing-time promises, release clauses and the length penalty on top of salary, bonus, bonuses
  and options; counter-offers adjust length, promises and clause before the salary (`ContractNegotiator.Meet`).
- Agents (`Agent`, `AgentRelationship`, `AgentPool`): 24 fictional agents with toughness (patience, rejection
  threshold) and a relationship with each club that moves with deals, broken-off talks and kept or broken promises.
- Rounds of talks (`ContractNegotiation`): every offer is a round with a patience cost; the player breaks off for 30
  days when out of patience; small concessions in later rounds. `IContractService.GetNegotiationAsync` reads demands
  without a round.
- Competing offers from rival clubs that can afford the player (free agents and contracts ending within 180 days).
- Playing-time promises in contracts (role, minutes per game), reviewed every 28 days against minutes played
  (`ReviewPromisesAsync`, daily in the career calendar): morale and agent relationships change; status shown in the
  *Club contracts* list.
- *Club contracts* offer form: promised role and minutes, *His demands*, talks state and competing offers in the answer.
- Migration `ContractNegotiations`: tables `Agents`, `AgentRelationships`, `ContractNegotiations`; promise columns
  on `Contracts`.

### Changed
- `NegotiateAsync` / `NegotiateRenewalAsync` now count and save a round (previously nothing was saved).
- AI clubs meet player demands: emergency signings and transfer-window free agents within the wage budget left,
  purchases through the personal-terms counter-offer; renewals accept counter-offers at most one year longer than
  the policy.
- *Use his counter-offer* copies every term of the counter-offer, not only the salary.
- The database editor and the importer keep a contract's promises when they update its terms.

## [0.24.0] — 2026-11-17 — STEP 24: Milestone 2 integration and release

### Added
- `MilestoneTwoTests` (slow): the whole European dataset run by AI clubs for two seasons (`EBM_M2_SEASONS`): every
  league, domestic cup and international competition finished, cross-country moves, consistent contracts and club
  histories, operational AI squads, no systematic debt, scouting, injuries, and every balance reconciled with its ledger.

### Changed
- Monday emergency transfer windows run only for the AI clubs short of players (previously every AI club shopped).
- The AI transfer window reloads the market only after a club's deals, not for every club.

### Fixed
- `EuropeanTransferWindowTests` counted purchases funded by sales in the same window as overspending.
- An AI emergency signing for injury cover after a registration deadline stopped the career calendar; the club now
  skips it (found by `MilestoneTwoTests` in the 2026-27 European season).

## [0.23.0] — 2026-11-17 — STEP 23: Financial management

### Added
- Ledger (`FinancialTransaction`, table `FinancialTransactions`): every game-driven balance change is booked by
  category (player and staff salaries, signing bonuses, settlements, transfer and loan fees, sponsorship, ticket sales,
  prize money, arena and travel costs), so balances always reconcile with their bookings (`docs/FINANCES.md`).
- Matchday finances: ticket sales by reputation and arena capacity (big games draw more), matchday operations, travel
  for away games (domestic or abroad); monthly sponsorship instalments and arena upkeep.
- Season budgets (`ClubBudget`, table `ClubBudgets`, `FinanceModel`): sponsorship deal, expected ticket sales and
  running costs, wage budget, transfer budget and the board's financial target; renewed every season by reputation,
  results, reserves and debt. Set at career start, each new season and when an older save is loaded.
- Financial reports (`IFinanceService`) and a *Finances* tab: budget, income and expenditure of the year by category,
  profit or loss, salary commitments, transfer budget left, the board's verdict and the latest bookings.
- AI clubs spend at most their transfer budget in a window; debts cut next season's wage budget. Migration `Finances`.

### Changed
- The placeholder income (one twelfth of the wage budget a month) is replaced by the revenue model.

## [0.22.0] — 2026-11-17 — STEP 22: Injuries and medical management

### Added
- Injuries (`Injury`, table `Injuries`): 18 types by body part with recovery ranges, severity (minor to severe),
  source (match or training), recurrence and aggravation; history never deleted (`docs/INJURIES.md`).
- Injury risk (`InjuryRules`, configurable `Ebm:Injuries`): minutes played and training intensity, fatigue, weekly
  workload, age, strength and stamina, physio, recent injuries, recent return, playing hurt and a hidden proneness.
- Medical day (`IMedicalService`, after training every game day): recovery by physio and age, treatment and
  rehabilitation phases with training restrictions, weekly reassessment of the estimated return, return-to-play
  fitness; physios hired automatically.
- *Medical* tab: physio, squad status, injury, phase, restriction, expected return, risk and the year's history.
  Career news reports the user's club's injuries.
- AI: training plans leave injured players out, rest players with knocks and avoid hard training in an injury crisis;
  clubs sign cover for long-term injuries. Migration `Medical`.

### Fixed
- The user's own match now tires his players: the day's fitness and injuries use every match of the day.

## [0.21.0] — 2026-11-17 — STEP 21: Training, development and player aging

### Added
- Training plans (`TrainingPlan`, table `TrainingPlans`): team focus (Balanced, Shooting, Tactical, Defense, Physical,
  Recovery), intensity (Light, Normal, Hard) and individual programmes; *Training* screen with coaches and a yearly
  outlook per player (`IDevelopmentService`).
- Weekly development (`PlayerDevelopment`, Mondays): growth towards potential by age, coaching, intensity, playing
  time, competition level, professionalism (work rate), fitness and injuries; age-related decline led by physical
  attributes; seasonal luck and weekly form, so development is not deterministic; experience and leadership grow with
  minutes.
- Daily fitness: fatigue from minutes played, recovery by intensity, recovery focus and fitness coach.
- Season end: potential realisation (revised by age, never below the overall) and retirement by configurable
  `RetirementRules` (`Ebm:Retirement` settings): contracts terminated, club spell closed, `Players.RetiredOn`.
- Coaches: assistant coaches (1–3 by reputation) and a fitness coach hired automatically; staff hiring shared with
  scouts (`StaffHiring`).
- AI clubs plan their training every Monday (`TrainingPlanner`, `IClubManagementService.PlanTrainingAsync`).
- Career news: training weeks and retirements. Migration `Development`. Documentation: `docs/DEVELOPMENT.md`.

### Changed
- Retired players are excluded from free agents, the transfer market, scouting and negotiations.
- `GameDayReport` carries `DevelopmentDay` and `SeasonDevelopment`.

## [0.20.0] — 2026-11-17 — STEP 20: Scouting and player evaluation

### Added
- Scouts: `StaffMember.Ability` (1–100); every club employs 1–3 scouts by reputation, hired automatically on a
  three-year contract with an ability that follows its reputation (`IScoutingService.HireMissingScoutsAsync`).
- Scouting assignments (`ScoutingAssignment`): one player (7 days), a region, a league, a position (21 days) or youth
  (28 days, age ≤ 21 by default), with optional position and age filters; one at a time per scout; cancellable.
- Scout reports (`ScoutReport`), written when an assignment is due (`IScoutingService.ProcessDayAsync`, every game
  day): estimated attributes, overall and potential with error margins, estimated market value and salary expectation.
- Attribute uncertainty (`ScoutingRules`, `ScoutingEstimator`): accuracy from scout ability, study time and player
  visibility (reputation, home country, international competition, age); bounded triangular errors; repeated reports
  improve; deterministic seeds.
- Player search over a club's reports (`SearchReportsAsync`) and report history (`GetPlayerReportsAsync`).
- AI clubs judge transfer candidates by their reports or background scouting knowledge instead of true ratings.
- *Scouting* screen: scouts, assignments, scouted-player search, report details with attribute ranges.
- Migration `Scouting`: `Staff.Ability`, tables `ScoutingAssignments`, `ScoutReports`, `ScoutReportAttributes`.
- Documentation: `docs/SCOUTING.md`.

### Changed
- `GameDayReport` carries the day's `ScoutingDay`; scout salaries are paid like every contract.

## [0.19.0] — 2026-11-16 — STEP 19: European transfer market

### Added
- Registration deadlines: `registration.deadline` ("MM-dd") in competition rules; after the earliest deadline of its
  competitions a club cannot register new first-team players until the season's last day, unless it is short of
  players (`RegistrationWindow`, `ITransferService.GetRegistrationWindowAsync`); enforced for free-agent signings,
  transfers and loans. Datasets: top divisions and international competitions 28 February, second divisions 31 March.
- Club attractiveness (`ClubAttractiveness`): reputation plus a bonus for the international competition played this
  season, used in every contract negotiation and asking salary.
- Loan agreements: parent-club salary share (up to 50% for players aged 23 or less, 25% otherwise, paid up front and
  refunded pro rata on a recall) and option to buy (at least the club's transfer price;
  `ITransferService.ExerciseOptionAsync`, transfer kind `OptionToBuy`). Migration `LoanAgreements`:
  `Transfers.SalaryContribution`, `OptionFee`.
- Contract buyouts: the player accepts a mutual termination for at least 30–90% of the remaining salary depending on
  what he would earn elsewhere (`ContractNegotiator.MinimumBuyout`, `IContractService.NegotiateBuyoutAsync`).
- AI upgrade signings: after its needs, an AI club may buy one player of a lower-reputation AI club of any country who
  clearly beats its starting five (`ClubRecruiter.MaxUpgrades`, `UpgradeMargin`, `UpgradeCashShare`).
- Market screen: registration window, loan salary share and option fields, *Exercise the option to buy*; contracts
  screen: *Ask the player* for a buyout.
- Documentation: international transfer rules and country-specific limits (docs/TRANSFERS.md §9–§10).

### Changed
- A mutual-agreement termination without a settlement pays the player's minimum buyout instead of half the
  remaining salary, and a lower settlement is refused.
- Loan history text: "(ended …)" instead of "(back …)", with the option fee when there is one.

## [0.18.0] — 2026-11-16 — STEP 18: International club competitions

### Added
- International competitions: cups without a country with a `cup.entry` block (docs/INTERNATIONAL_COMPETITIONS.md).
  Season-by-season qualification by slots (title holder, another competition's winner, best clubs of each country's
  league by last season's champion and final table, reputation wildcards), filled by tier so a club plays one
  international competition; pure engine `InternationalQualification`; `ICompetitionService.QualifyAsync`.
- EuroLeague, EuroCup, Basketball Champions League and FIBA Europe Cup in `data/europe` and `data/real-leagues`
  (formats simplified, unverified): league phases, groups, two-legged ties, Final Fours.
- Neutral-venue rounds (`rounds[].neutral`, `MatchSetup.NeutralVenue`: no home advantage).
- Cup rewards (`cup.rewards`): prize money to the club balance and reputation, paid once when the cup ends
  (`CupSeason.Reward`, `Club.ChangeReputation`).
- `ICompetitionService.GetCalendarConflictsAsync`: clubs with two fixtures on one day.
- Career: international competitions qualify when a career starts and for every next season.
- Migration `InternationalCompetitions`: `CompetitionParticipants.PrizeMoney`, `ReputationGain`.

### Changed
- A cup group can hold up to 20 clubs (a whole league phase).
- League second-phase, play-in and playoff games move to a day on which neither club has another fixture (fixes the
  Step 17 cup/playoff same-day clash).

## [0.17.0] — 2026-11-16 — STEP 17: Domestic cups

### Added
- Cup competitions (`cup` section of the competition rules, docs/CUP_FORMATS.md): knockout rounds of single games or
  two-legged ties on aggregate, byes for the top seeds, group stages, bracket/seeded/open draws (reproducible),
  seeding, qualification from a league table after a given round (Final Eight/Four), per-round dates and finals.
- Pure engine `CupSeason`; `ICompetitionService` draws cups (`GenerateScheduleAsync`/`AdvanceAsync`), schedules each
  game on a day its clubs are free, and adds `GetCupAsync` and `GetHistoryAsync` (champion and runner-up per season,
  leagues too). `SeasonOutcome.RunnerUp`.
- Second legs go to overtime while the aggregate is level (`MatchSetup.OvertimeMargin`).
- Career: game days play and advance cups, the season ends when they are finished, the review lists cup winners,
  qualification cups are drawn again every season. Career screen: cup groups, ties, byes, winner and past winners.
- Domestic cups for 17 countries in `data/europe` (Sweden has none), the Coppa Italia in `data/italy` and the cups of
  the countries in `data/real-leagues`.
- Migration `CupCompetitions`: `CompetitionParticipants.Seed`; fixture stages `CupGroup` and `Cup`.

## [0.16.4] — 2026-11-16 — Real leagues dataset

### Added
- `data/real-leagues`: a standalone dataset of eight real 2026-27 divisions (ABA League, Liga Endesa, easyCredit BBL,
  Betclic ÉLITE, Stoiximan GBL, Basketbol Süper Ligi, Serie A, Serie A2) with 138 real clubs and 2,598 real players
  from the author's RealGM export (`build/player-stats/europe-*.csv`): 2,045 under contract, 553 free agents. Serie A
  keeps the curated squads. Ratings from career statistics on the existing Serie A scale (`build/realgm_ratings.py`);
  contracts estimated from the current run at the club; past spells at the dataset's clubs.
  Generator: `build/generate-real-leagues-dataset.py`. docs/PLAYER_DATABASE.md §7.

## [0.16.3] — 2026-11-13 — Stat-based ratings for Serie A players

### Changed
- Level, potential, reputation and box-score attributes of 200 real Serie A players come from their career per-game
  statistics (`build/player-stats/player-stats-per-game.csv`, `build/player_ratings.py`): Game Score and minutes
  adjusted by competition strength, recent seasons weighted most, blended with the hand estimates by sample size.
  The 16 players without statistics keep the hand estimates; generated players are unchanged.

## [0.16.2] — 2026-11-13 — Real squads: Serie A

### Added
- Real 2026-27 squads of the other 15 Serie A clubs (216 real players with Cantù), from the clubs' Wikipedia season
  pages: `build/real-rosters/*.json`. Profiles, attributes and contract terms Estimated (birth years only, tier-based
  levels); 2025-26 previous clubs as history where they are in the dataset; 20 more nationalities.

### Changed
- Serie A club list matches 2026-27 (Maxima Roma, BC Roma, Scafati, Verona in; Trapani, Brescia, Cremona, Sassari out).
- Real players' IDs derive from name and birth year (Cantù IDs changed once).

## [0.16.1] — 2026-11-13 — Real squad: Pallacanestro Cantù

### Added
- Real Pallacanestro Cantù 2026-27 squad (14 players as of 13 Nov 2026, with departure and signing dates) from the
  club site, Wikipedia and FIBA: `build/real-rosters/cantu-2026-27.json`, used by the generator instead of the
  generated players. Profiles Verified (one Unverified), attributes and contract terms Estimated.
- Countries Latvia and Nigeria; generated players of every other club keep their IDs.

## [0.16.0] — 2026-11-13 — Step 16: European player database

### Added
- Real clubs (240, marked Unverified) in the 17 top divisions of the European dataset; 6,144 generated players with
  contracts and 1,500 past club spells (`data/europe/*-europe-2026-27.csv`). The European world plays as a career.
- Data quality: `DataStatus` (Unverified, Verified, Estimated, Generated) on player profile and attributes, contracts
  and clubs, with a source (required when verified). Migration `20261113110006_PlayerDatabase`.
- Second nationality (`Player.SecondNationalityId`), counted for EU/EEA status; sporting nationality decides domestic
  and foreign counts.
- Import: optional attributes (missing ones estimated, `AttributeEstimator`), optional salary (estimated), statuses
  and sources, `playerHistory` section, accent-insensitive duplicate keys, possible-duplicate notes.
- Player profile: second nationality, club history, estimated market value, data status and source; editor fields.
- Docs: `docs/PLAYER_DATABASE.md` (coverage, sources, data quality, gaps).

### Changed
- Squads load only their club's contracts and spells (`ClubMembership.MembersOfAsync`), so game days scale to 512 clubs.
- Italian sample and templates declare their data status.

## [0.15.0] — 2026-11-13 — Step 15: European country and league expansion

### Added
- European dataset `data/europe/europe-2026-27.json` (generator `build/generate-europe-dataset.py`): first two
  divisions of 18 countries — 20 leagues, 35 competitions, 512 fictional placeholder clubs with 2026-27 entries.
  Country-by-country status in `docs/COUNTRY_REPORT.md`.
- Competition engine (`CompetitionSeason`): second phases from the first-phase table (`All`/`WithinGroup`/`None`
  carry-over, optionally by country), play-in for the last two playoff seeds, playoff byes, promotion of 1/2/4
  playoff places, `registration.countries` for cross-border leagues, `regularSeason.startOffsetDays`. Fixture
  stages `SecondPhase` and `PlayIn` shown in the career and match centre.
- EU/EEA nationalities: `Country.IsEuEea` (migration `20261113102815_CountryEuEea`, import field `euEea`) and the
  squad limit `maxNonEuPlayers`, enforced by roster validation, transfers and AI recruitment.
- Tests: every European league plays a full season with promotion/relegation (`EuropeDatasetTests`), engine unit
  tests (`CompetitionSeasonTests`).

## [0.14.0] — 2026-11-12 — Step 14: Milestone 1 integration and release

### Added
- Save files (`ISaveGameService`, *Saved games* screen): save, load (with schema upgrade and career-date reload),
  delete, new game; saves from newer versions and foreign files are refused. Documentation `docs/SAVE_FILES.md`.
- Assistant (holiday) mode: `ICareerService.SetAssistantAsync`, *Assistant manages* on the career screen; the user's
  club is AI-managed and its matches are played by the calendar. Migration `20261112154154_CareerAssistant`.
- Windows release: `build/publish.ps1` (self-contained single-file win-x64, smoke test, zip). `docs/RELEASE.md`.
- Tests: Milestone 1 acceptance (career, management, signing, live match, save/load, second season), save-file rules,
  long simulation (`EBM_SIM_SEASONS`).

### Fixed
- A user's club nobody manages ran out of players in long careers and forfeited (assistant mode; found by the
  six-season run).

## [0.13.0] — 2026-11-12 — Step 13: AI club management and career testing

### Added
- AI clubs (`IClubManagementService`, `Ebm.Domain.Management`): matchday line-ups and rotation (`LineupPlanner`),
  tactics from squad strengths (`TacticPlanner`, tactic "AI plan"), contract renewals (`RenewalPolicy`), releases
  above the squad limit, preseason/offseason/emergency transfer windows, emergency free-agent signings, free-agent
  replenishment with generated players (`PlayerGenerator`), board objectives set and judged (`SeasonObjectives`).
- Finances on the calendar (`ClubFinances`): salary instalments on their due dates, month-end income.
- Season review: every club's objective and whether it was met. Career news shows AI activity.
- Tests: three-season career simulation, planner unit tests. Documentation `docs/AI_MANAGEMENT.md`.

## [0.12.0] — 2026-11-12 — Step 12: Season progression and career management

### Added
- Career (`ICareerService`, `Careers` table): choose a club, start a career, game days (`AdvanceDayAsync`), continue to
  the next match (`ContinueAsync`), instant play of the user's match, playoffs advancing automatically, AI transfer
  window at season end, season review, next season with promotion/relegation and schedules, club history.
- `GameClock`: the career date is the game's "today" for every service.
- Forfeits (`Match.IsForfeit`, `IMatchService.PlayOrForfeitAsync`): clubs without five players lose 20–0.
- `ICompetitionService.GetPhaseAsync`, `SeasonTransition`, `SeasonCalendar`.
- *Career* screen: new career, calendar controls, standings, news, club history.
- Sample `data/sample/italy-squads.json` (squads for every club, more free agents). Docs `docs/SEASON_LIFECYCLE.md`.
- Tests: full career season with next season and offseason, forfeits, career screen, season lifecycle unit tests.

### Changed
- Services take the date from `GameClock` instead of `TimeProvider`. Migration `20261112144630_Careers`.

## [0.11.0] — 2026-11-12 — Step 11: Match presentation and statistics

### Added
- Screen *Matches* (match centre): competition season and schedule (with *Create schedule*), match preview (starting
  lineups, bench, tactics), live match (scoreboard with period clock, team fouls and timeouts; play-by-play; live box
  scores with energy and plus-minus; team statistics; report with leaders and quarter scores), game controls (next
  possession, to the end of the quarter / match), substitution, timeout and tactical-adjustment controls for the
  managed side, instant play, automatic saving at the end and stored match reports for played fixtures.
- `IMatchService.GetCompetitionSeasonsAsync`. Documentation `docs/MATCH_UI.md`.
- Tests: the render test plays a live match through the screen (preview, possessions, substitution, timeout, tactics,
  quarter and match end, saved result, reopened box score, preview error for short squads).

 — 2026-11-12 — Step 10: Basketball match simulation engine

### Added
- `MatchSimulation` (`Ebm.Domain.Matches.Simulation`): possession-based engine under FIBA rules — four 10-minute
  quarters, overtimes, 24/14-second shot clock, two- and three-point shots by zone (rim, post, mid-range, three), free
  throws, shooting and non-shooting fouls with the team-foul bonus, foul-outs, rebounds, assists, steals, blocks,
  turnovers, shot-clock violations, jump ball and alternating possession, substitutions, timeouts, fatigue, form of
  the day, home advantage, game lost by default. Deterministic for a seed.
- Tactics in the engine (`TacticalModel`): systems, settings, roles, shot priority and minutes targets change pace,
  shot mix, make probabilities, turnovers, fouls, rebounds, fast breaks and fatigue.
- Simulation modes: instant, commentary (play-by-play `MatchEvent`s) and live (substitutions, timeouts, tactic changes,
  user-managed side).
- `TeamSheet.Pick` (matchday squad from a roster), `PlayerLine`, `TeamStatistics`, `MatchResult`.
- `IMatchService`: fixtures with results, preview, start/save sessions, instant play, stored box scores and reports.
- `docs/MATCH_ENGINE.md` with the formulas and the statistical validation results.
- Tests: rule invariants, overtime, determinism, fatigue, live interventions, short-handed teams, squad picking,
  large-scale statistical validation and tactical effects (unit); match service (integration).

### Changed
- `CompetitionService.RecordResultAsync` shares `AddResultAsync` with the match service (result + standings + box
  score in one transaction). No schema change.

 — 2026-11-12 — Step 09: Tactical system

### Added
- `Tactics` table and `Tactic` entity (`Ebm.Domain.Tactics`): seven offensive systems, six defensive systems,
  offensive settings (tempo, shot selection, ball movement, spacing, pick-and-roll frequency on a 1–5 scale; focus)
  and defensive settings (intensity, ball pressure, help defense; switching, pick-and-roll coverage, rebounding).
- Player tactical roles (ten roles) and assignments (role, shot priority, minutes target); `TacticalRoles` suggests a
  role from the player's attributes and position.
- `TeamTactics`: the immutable configuration the match engine reads, with suggested roles for unassigned players.
- `ITacticsService`: club tactics, create/update, select for the next match, delete, `GetMatchTacticsAsync`.
- Screen *Tactics*. Documentation `docs/TACTICS.md` (options, settings, roles and how the engine should read them).
- Tests: tactic rules and role suggestions (unit); tactics service, schema, render of the tactics screen (integration).

### Changed
- Migration `Tactics` (create-table only). Schema version `20261112140336_Tactics`; 21 tables.

 — 2026-11-09 — Step 08: Transfers and free agents

### Added
- `Transfers` table and `Transfer` entity (`Ebm.Domain.Transfers`): the movement history of every player — free-agent
  signings, transfers, release-clause buy-outs, loans (with end date and return date) and loan returns. Never deleted.
- Contract statuses `OnLoan` (the contract keeps running while a loan club pays the player) and `Transferred` (ended
  early by a transfer); `Contract.Transfer`, `SendOnLoan`, `ReturnFromLoan`. Wage bills and salary commitments skip
  contracts of players out on loan.
- `TransferNegotiator`: deterministic market value (market salary × 2.5 × age, potential and remaining-contract
  factors), asking fee (starter and rotation premiums, discounts for clubs in debt or over budget), fee answers
  (accepted / counter-offer / rejected; a release clause cannot be refused) and loan answers (no starters or rotation
  players; 10% of the value per year).
- `ClubRecruiter`: the simple AI rules — surplus to sell (squad size, foreign limit, wage budget), needs (two players
  per position, squad up to the matchday squad + 1), and signings within the cash, the wage budget and the
  registration limits, preferring free agents. `SquadRules.Strictest` combines a club's competition rules.
- `ITransferService`: player search with filters priced for the searching club, valuations, fee / loan / contract
  negotiation, `BuyAsync` (fee between balances, old contract transferred, new contract, squad move, history),
  `LoanAsync` (parent contract on loan, loan contract at the borrower, fee), `RecallAsync`, club and player transfer
  history, `ProcessTransferDatesAsync` (loan returns, then contract dates; run at startup) and
  `RunAiTransferWindowAsync` (AI clubs sell to each other, sign free agents, buy, and release surplus nobody buys;
  managed clubs are left alone).
- Financial and roster limits: a fee never takes a balance below zero; first-team size, foreign-player and minimum-age
  limits of the club and its competitions block a move; the wage budget warns the user and binds AI clubs.
- Screen *Transfer market*: search and filters, player card with value and asking fee, fee and contract negotiation
  with counter-offers, buy, pay the release clause, sign a free agent, loan form, loans out (with recall) and in,
  the club's transfer history and the AI transfer window report. Documented in `docs/TRANSFERS.md`.
- Free-agent signings through `IContractService.SignAsync` are recorded in the transfer history.
- Tests: transfer and loan rules, negotiator, recruiter, strictest squad rules (unit); transfer service (purchase,
  refusals and limits, release clause, loan and automatic return, starter refusal and recall, search and pricing,
  free-agent history, AI window with free agents, AI sales and releases, startup processing), schema, render of the
  market screen with a purchase and an AI window (integration).

### Changed
- Migration `Transfers` (create-table only). Schema version `20261109204206_Transfers`; 20 tables.
- Startup applies `ITransferService.ProcessTransferDatesAsync` (loan returns first, then contract dates) instead of
  calling the contract service directly.
- `DatabaseEditorViewModel` takes `ITransferService`; contract status texts use display names ("On loan").
- Club profile wage bill and `SalaryCommitments` ignore `OnLoan` contracts.

## [0.7.0] — 2026-11-09 — Step 07: Contracts and player availability

### Added
- Contract terms: payment frequency (monthly, quarterly, annually), signing bonus, performance bonuses (per game,
  playoffs, league title, cup title, promotion, avoid relegation), release clause, club option and player option.
  Grouped as `ContractTerms`; importable and editable in *Contracts (editor)*.
- Contract lifecycle: status `Active`, `Expired`, `Terminated`, `Renewed`, with the early end date and the
  termination settlement. `Contract.RunningOn(date)` is the single "running" rule used by every service.
- `IContractService`: sign a free agent (overlap check, player acceptance, signing bonus paid, player joins the club),
  renew (the old contract is kept as `Renewed`), terminate by release (remaining salary paid) or mutual agreement
  (agreed settlement), club and player option decisions, free-agent list, club and player contract views, and
  `ProcessContractDatesAsync` (expiry frees the player; contracts signed in advance move him on their start date).
  Run at application start.
- Salary calculations (`SalaryCalculator`): day-by-day accrual per contract year, remaining and total salary,
  payment schedule by frequency, next payment. Club `SalaryCommitments`: wage bill, salary still owed, commitments
  per 12 months, maximum season bonuses.
- Simplified, deterministic negotiation (`ContractNegotiator`): asking salary from overall, reputation, age,
  potential and club prestige; offers accepted, countered or rejected; player option decisions.
- Screens: *Club contracts* (current contracts and history, wages and commitments, renew, terminate, options, sign
  a free agent with *Ask the player* and counter-offers); the player profile shows contract status and history.
- `Club.ChangeBalance` for one-off payments. Documented in `docs/CONTRACTS.md`.
- Sample dataset: 4 free agents; some Aurora Milano contracts have bonuses, a release clause or options.
- Tests: contract rules, bonuses, salary accrual and schedules, commitments, negotiation, contract service (signing,
  refusal, pre-contract, expiry, release, mutual agreement, renewal, options), editor and import of the new terms,
  Step 06 database upgrade, club contracts screen render.

### Changed
- Migration `Contracts` (add-column only, with defaults); existing contracts become active, paid monthly, no extras.
- Terminated and expired contracts no longer make a player a club member, in the squad, club profile wage bill and
  player profile (`Contract.RunningOn`).
- Club spell and roster-spot moves are shared by squad management and contracts (`ClubMembership`).
- `ContractDraft` gains the new terms (optional parameters); `DatabaseEditorViewModel` and
  `PlayerProfileViewModel` take `IContractService`. The editor tab is now *Contracts (editor)*.
- `.gitignore` no longer ignores the root `logs/` folder, so step logs are versioned.

## [0.6.0] — 2026-11-09 — Step 06: Club and roster management

### Added
- Club profile fields: city (defaults to the home arena's city), reputation, balance, wage budget and board
  expectation; edited in the *Clubs* tab and importable (optional columns with defaults).
- Squad roles (`Ebm.Domain.Rosters`): starter (with lineup position), rotation (with order), bench, inactive and
  youth, stored one per player in the new `RosterSpots` table; club players without a stored role are on the bench.
- Roster validation (`RosterValidator`): starting five complete with distinct positions, injured starters, squad
  size, matchday squad size, foreign/domestic limits, minimum age (errors) and doubtful starters, injured reserves,
  unfamiliar lineup positions, short rotation, missing contracts (warnings).
- Data-driven squad rules (`SquadRules`): club defaults plus per-competition registration rules in the rules JSON
  (`registration.squad`), checked for every competition the club plays this season.
- `IClubService`: club profile (country, arena, current season and competitions, wage bill vs budget), squad,
  save of all roles (rotation order from list order), and player assignment to a club (closes/opens club spells,
  moves the squad role).
- Screens: *Club profile* and *Squad* (role/lineup/rotation editing with live validation, starting-five panel,
  add a player to the squad). Documented in `docs/ROSTER_RULES.md`.
- Sample dataset: club profiles for every club and 9 more players, so Aurora Milano has a complete 12-player squad.
- Tests: roster rules and validator, club profile rules, competition squad rules, club service (complete roster,
  rotation order, invalid configurations, injured starter, registration limits, assignment), club import, Step 05
  database upgrade, database constraints, squad screen save and render.

### Changed
- Migration `ClubsAndRosters` (add-column only on `Clubs`, plus the new table); existing clubs keep their data.
- `ClubDraft` gains the profile fields (optional parameters); `DatabaseEditorViewModel` takes `IClubService`.

### Fixed
- `BoardExpectation.WinTitle` (enum value 0) would have been replaced by the column default on insert; the EF
  sentinel is now `-1`.

## [0.5.0] — 2026-11-09 — Step 05: Player model and attributes

### Added
- Complete player model: wingspan, secondary positions, experience, reputation, potential, fitness, morale and
  injury status (with expected return date), all validated by the domain. Current club and contract come from the
  club history and contracts. Documented in `docs/PLAYER_MODEL.md`.
- 27 attributes in 7 categories (shooting, finishing, playmaking, defense, rebounding, physical, mental), 1–100,
  with a catalogue (`PlayerAttribute`, `PlayerCatalog`) for groups, display names and import field names.
- Player tendencies (8 values, `PlayerTendencies`), suggested from the attributes when not given.
- Rating system (`Ebm.Domain.Players.Ratings`): category ratings, position ratings (weighted attributes plus a
  reach adjustment from height and wingspan), familiarity, overall (primary position), potential rating, best
  position and position suitability (Ideal/Good/Adequate/Poor). Tunable through `RatingModel`.
- `IPlayerProfileService`: player profile (age, club, contract, evaluation) and multi-player comparison.
- Screens: *Player profile* and *Compare players* tabs; the player editor edits every field, attributes by category
  and tendencies (*Suggest from attributes*).
- Import of the new player fields (optional ones default; missing tendencies are suggested).
- Tests: model rules, catalogue, tendencies, rating formulas, persistence of every field, Step 04 database upgrade
  (attribute mapping), profile and comparison, import of optional fields, player page, off-screen render of the
  three player screens (set `EBM_SCREENSHOT_DIR` to save PNGs).

### Changed
- `PlayerAttributes`: the 8 initial ratings are replaced by the 27 attributes. Migration `PlayerModel` converts
  existing players (mapping in `docs/PLAYER_MODEL.md` §7) and adds the new columns and the `PlayerTendencies` table
  in one transaction.
- Player import columns: the 27 attribute columns replace `shooting`, `insideScoring`, …; sample dataset and
  templates converted. `PlayerDraft` carries every player field.

## [0.4.0] — 2026-11-09 — Step 04: Countries, leagues and competitions

### Added
- Competition rules schema (`Ebm.Domain.Competitions.Rules.CompetitionRules`): points system, registration limits,
  regular-season legs and round spacing, ordered tie-breakers, optional playoffs (2–32 clubs, best-of per round,
  seeding from any table position), promotion (direct places and/or playoff winner) and relegation. Validated on
  every save and import; unknown properties are refused. Documented in `docs/COMPETITION_FORMATS.md`.
- Competition engine (`Ebm.Domain.Competitions.Engine`): `RoundRobinScheduler` (circle method, 1–4 legs, any
  number of clubs, balanced home/away), `StandingsCalculator` (points, configurable tie-breakers with head-to-head
  mini-leagues), `PlayoffBracket` (fixed bracket, home-court patterns, game-by-game scheduling),
  `SeasonOutcomeCalculator` (champion, promoted, relegated) and `CompetitionRegistration`.
- `ICompetitionService` (`Ebm.Services.Competitions`): register clubs, generate the schedule, record results
  (updates `LeagueStandings`), advance into and through the playoffs, standings, season outcome.
- Import of `seasons` and `participants` (competition entries, checked against the registration rules); CSV
  templates `seasons.csv`, `participants.csv`.
- Sample dataset: two divisions with full rules (Serie A: 8 clubs, top-4 playoffs, 2 relegated; Serie A2: 6 clubs,
  1 promoted plus the playoff winner), 6 more clubs, season 2026-27 and its 14 entries.
- `Season.UpdateDetails`; `Competition.ParseRules`.
- Tests: rules schema, scheduler properties (2–20 clubs, 1–4 legs), standings and tie-breakers, playoff bracket,
  outcome, registration; full simulated seasons of both sample divisions; registration and import rules;
  Step 02 database upgrade.

### Changed
- `Fixtures` gain `Stage` (`RegularSeason`/`Playoffs`) and `GameNumber` (migration `FixtureStages`; existing
  fixtures become regular-season games).
- `Competition.UpdateRules` validates against the rules schema instead of accepting any JSON object.

## [0.3.0] — 2026-11-09 — Step 03: Data import and database editor

### Added
- Import service `IDataImportService` (`Ebm.Services.Import`, implemented in `Ebm.DataImport`): CSV files (one record
  type per file, named after it) and JSON dataset files (several sections), processed in dependency order.
  Importable: countries, arenas, clubs, leagues, divisions, competitions, players (with attributes), player contracts.
- Validation with per-record, field-level error messages; file-level problem reporting; references checked against
  the database and the records accepted earlier in the same import.
- Duplicate detection by ID and by natural key (`Ebm.Services.Data.DuplicateKeys`), shared by importer and editor.
- Unique ID generation for records without an ID.
- Import modes: add new only (default; existing records are rejected) and explicit add-or-update; options
  "validate only" (dry run) and "all or nothing". Accepted records are written in one transaction.
- Import logs: one report per import in `<DataRoot>\logs\imports\`, plus a summary in the application log.
- Database editor (`IDatabaseEditor`, `Ebm.Services.Editing`) and editor screens in the main window: players (with
  attribute ratings), clubs, player contracts, leagues, competitions (with rules JSON) and an import page.
- Domain edit methods: `Player.UpdateDetails`, `Club.UpdateDetails`, `Arena.UpdateDetails`, `Country.Rename`,
  `Country.NormaliseCode`, `League.UpdateDetails`, `Division.UpdateDetails`, `Competition.UpdateDetails`,
  `Contract.UpdateTerms`; `DuplicateRecordException`.
- `data/templates` (CSV and JSON templates) and `data/sample/italy-sample.json` (fictional league, 24 players),
  copied next to the executable.
- `docs/IMPORT_FORMATS.md`: formats, field mappings, validation rules, modes, editor, known data limitations.
- Unit tests (CSV parser, file naming, edit rules, duplicate keys) and integration tests (sample and template
  imports, invalid records, duplicates, update mode, dry run, all-or-nothing, malformed files, editor use cases,
  editor view model).

### Changed
- Entity constructors delegate to the edit methods (same validation); some reference-check messages are now
  worded for players (e.g. "Nationality must be set." instead of "nationalityId must be set.").
- `AddDataImport(importLogDirectory)` takes the import log folder; `AppPaths.ImportLogsDirectory` added.
- Main window: the welcome card is replaced by the database editor; record counts and an "Open logs folder"
  button are in the status bar.
- Package `Microsoft.Extensions.Logging.Abstractions` 10.0.14 added to DataImport.
- Version 0.3.0.

## [0.2.0] — 2026-11-09 — Step 02: Core database and entity model

### Added
- Domain entities (`Ebm.Domain`): Country, League, Division, Competition (rules stored as JSON data),
  CompetitionParticipant, Season, Club, Arena, Player with PlayerAttributes, StaffMember, Contract,
  PlayerClubHistoryEntry, Fixture, Match, PlayerMatchStatistics (with BoxScore) and LeagueStanding.
- Strongly typed, application-assigned GUID IDs for every entity (`Entity<TId>`, `IEntityId<T>`), and
  `DomainValidationException` for rule violations.
- Data access ports `IRepository<TEntity, TId>`, `IUnitOfWork` and `DataConstraintException` (`Ebm.Services.Data`).
- SQLite game database with EF Core 10.0.14 (`Ebm.Persistence`): `EbmDbContext`, Fluent mappings with unique
  indexes, check constraints and restrict-on-delete foreign keys, generic repository and unit of work.
- Versioned migrations; first migration `20261109114155_InitialSchema` (17 tables).
- `DatabaseInitializer`: creates and migrates `<DataRoot>\databases\ebm.db` at startup and logs the schema version.
- Local tool manifest `dotnet-tools.json` with `dotnet-ef` 10.0.14.
- Unit tests for entity rules and statistics; integration tests for database creation, migrations, round trips,
  constraints and player history across clubs.
- `docs/DATABASE_SCHEMA.md`: entities, fields, relationships, constraints and migration procedures.

### Changed
- `AppPaths` adds `DatabasesDirectory` and `DatabaseFile`; `AddPersistence` now takes the database path.
- `.editorconfig` treats generated migrations as generated code.
- Version 0.2.0.

## [0.1.0] — 2026-11-09 — Step 01: Project foundation and application architecture

### Added
- .NET 10 solution `EuropeanBasketballManager.slnx` with pinned SDK, shared build settings,
  central package management and enforced code style.
- Layered projects: `Ebm.Domain`, `Ebm.Services`, `Ebm.Persistence`, `Ebm.DataImport`,
  `Ebm.Hosting` (composition root) and `Ebm.Desktop` (WPF application).
- WPF application shell showing product name, version, data folder and log folder.
- Configuration via `appsettings.json`, environment variables and command line (`Ebm` section).
- User data folder resolution (default `%LOCALAPPDATA%\EuropeanBasketballManager`).
- Serilog logging to daily rolling files (`logs/ebm-YYYYMMDD.log`, 14 kept) and debug output.
- Global error handling: UI-thread, background-thread and unobserved-task exceptions;
  startup-failure log; defined exit codes.
- `--smoke-test` launch switch for automated launch verification.
- Unit tests (architecture rules, paths, version info, error policy, startup failure log, service registration)
  and integration tests (host composition, logging, configurable log level, real executable launch).
- Documentation: README, technical architecture, project state, Step 01 log.

### Changed
- Development roadmap moved from `project_state.md` to `docs/ROADMAP.md` (content unchanged).
  On Windows the old name collides with the required `PROJECT_STATE.md` status file.
