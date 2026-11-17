# PROJECT STATE

## Project
European Basketball Manager

## Current milestone
Milestone 3 — Advanced management and simulation (Milestone 2 completed, release 0.24.0)

## Current step
None in progress

## Last completed step
STEP 24 — Milestone 2 integration and release (2026-11-17)

## Next step
STEP 25 — Advanced contract negotiations (docs/ROADMAP.md)

## Application status
- Build: PASS (Debug and Release, 0 warnings, 0 errors)
- Tests: unit PASS (408/408); integration fast suite PASS (144/144, `Category!=Launch&Category!=Slow`, 3.5 min); launch PASS. Slow suite (`Category=Slow`): see logs/STEP_24_LOG.md §5 (stopped once for low memory; `MilestoneTwoTests` was still running at commit time)
- Run status: Launches (also as the published self-contained `Ebm.Desktop.exe`, `build/publish.ps1`). Creates/migrates the game database at startup, loads the career (its date becomes the game date), applies transfer and contract dates (loan returns, expiries, contracts starting) and opens the main window (career, saved games, players, player profile, player comparison, clubs, club profile, squad, club contracts, transfer market, scouting, tactics, training, medical, match centre, contracts editor, leagues, competitions, finances, import). The competition engine (leagues, domestic cups and international competitions, qualification, calendar conflicts) is available as `ICompetitionService`; player ratings as `PlayerRatingCalculator` / `IPlayerProfileService`; clubs and squads as `IClubService`; contracts as `IContractService`; the player market, loan agreements, registration windows and AI recruitment as `ITransferService`; scouts, assignments and reports as `IScoutingService`; training, development, fitness and retirement as `IDevelopmentService`; injuries, recovery and medical records as `IMedicalService`; budgets, the ledger and financial reports as `IFinanceService`; club tactics as `ITacticsService` (`GetMatchTacticsAsync` for the match engine); the match engine as `MatchSimulation` (domain) and `IMatchService` (play, live sessions, storage, box scores, forfeits); the career and calendar as `ICareerService` with the game date in `GameClock`; AI clubs as `IClubManagementService`; save files as `ISaveGameService`.

## Technology stack
- Language: C# 14 / .NET 10 (SDK 10.0.203, pinned in `global.json`)
- Framework: Microsoft.Extensions.Hosting (DI, configuration, options, logging)
- Database: SQLite via EF Core 10.0.14, versioned migrations, file `<DataRoot>\databases\ebm.db`
- UI: WPF (`net10.0-windows`), MVVM with CommunityToolkit.Mvvm
- Build system: .NET SDK / MSBuild, `.slnx` solution, central package management, local tool `dotnet-ef`
- Logging: Serilog (rolling file and debug sinks), used through `ILogger<T>`
- Tests: xUnit 2.9.3

## Implemented modules
- [x] Solution structure and build configuration
- [x] Domain layer (`Ebm.Domain`): 19 entities with strongly typed IDs and validation
- [x] Services layer (`Ebm.Services`): `TimeProvider`, data access ports (`IRepository<,>`, `IUnitOfWork`)
- [x] Persistence layer (`Ebm.Persistence`): EF Core/SQLite context, mappings, migrations, repositories, startup initialiser
- [x] Data import layer (`Ebm.DataImport`): CSV/JSON import, validation, duplicate detection, import logs
- [x] Composition root (`Ebm.Hosting`): configuration, data paths, logging, error policy
- [x] Desktop shell (`Ebm.Desktop`): main window, global exception handler, smoke-test mode
- [x] Automated tests (unit, integration, architecture, launch)
- [x] Database and entity model (Step 02)
- [x] Data import and database editor (Step 03)
- [x] Competition configuration, fixture generator, standings, playoffs, promotion/relegation (Step 04)
- [x] Player model: 27 attributes, tendencies, ratings, position suitability, profile and comparison screens (Step 05)
- [x] Club profile, squad roles, starting five, rotation, roster validation, competition registration (Step 06)
- [x] Contracts: terms, lifecycle (sign, renew, terminate, options, expiry), free agents, salary calculations and commitments, negotiation (Step 07)
- [x] Transfer market: search, valuations, fee negotiation, transfers, release clauses, loans and returns, transfer history, AI recruitment (Step 08)
- [x] Tactics: offensive/defensive systems and settings, player roles and assignments, selected tactic per club, match-engine access (Step 09)
- [x] Match engine: possession-based FIBA simulation, statistics, fatigue, substitutions, timeouts, overtime, instant/commentary/live modes, statistical validation (Step 10)
- [x] Match centre: schedule, preview, live match with play-by-play, scoreboard, substitutions, timeouts and tactical changes, live and stored box scores, team stats, match report (Step 11)
- [x] Career: club selection, calendar and game days, forfeits, playoffs, season review, promotion/relegation, next season, club history, career screen (Step 12)
- [x] AI clubs: line-ups, tactics, renewals, releases, transfer windows, free-agent replenishment, salaries and income, board objectives; multi-season simulation test (Step 13)
- [x] Milestone 1 release: save files (save, load with upgrade, delete, new game), assistant (holiday) mode, Windows self-contained build with smoke test, acceptance and long-run tests (Step 14)
- [x] European leagues: first two divisions of 18 countries as data (`data/europe`), second phases, play-in, byes, cross-border leagues, EU/EEA limits (Step 15)
- [x] European player database: real top-division clubs (Unverified), 6,144 generated players with contracts and history, data status and sources, second nationality, estimated attributes/salaries, cross-league duplicate detection, market value (Step 16); real leagues dataset: 8 real divisions, 138 clubs, 2,598 real players (`data/real-leagues`)
- [x] Domestic cups: knockout rounds, single games and two-legged ties, group stages, bracket/seeded/open draws, seeding, qualification from a league table, finals, cup history; career integration and cup view; cups for 17 countries in the datasets (Step 17)
- [x] International club competitions: EuroLeague, EuroCup, BCL, FIBA Europe Cup; season-by-season qualification by tier, league phases, neutral Final Fours, prize money and reputation rewards, calendar conflict detection (Step 18)
- [x] European transfer market: one market across countries, club attractiveness (international competitions), loan agreements (salary share, option to buy), contract buyouts, registration deadlines, AI upgrade signings across borders; international rules documented (Step 19)
- [x] Scouting: scouts (staff ability), player/region/league/position/youth assignments, reports with attribute uncertainty, estimated value and salary expectation, report search; AI clubs recruit on scouted estimates (Step 20)
- [x] Training and development: training plans (team/individual focus, intensity), coaches, weekly growth and age decline, daily fitness, potential realisation, configurable retirement; AI training plans (Step 21)
- [x] Injuries and medical management: injury types and severity, risk from workload/fatigue/age/physique/history, match and training injuries, recovery and rehabilitation phases, training restrictions, physio assessment, return-to-play fitness, recurrence, injury history; AI handling (Step 22)
- [x] Financial management: ledger of every booking by category, matchday ticket sales/arena/travel, monthly sponsorship and upkeep, season budgets (wage, transfer, financial target) renewed by reputation, results and debt, financial reports and *Finances* tab; AI transfer budgets (Step 23)
- [x] Milestone 2 integration: European two-season acceptance test (`MilestoneTwoTests`), targeted emergency windows, faster AI windows (Step 24)
- [ ] Step 25 onwards (Milestone 3) … see docs/ROADMAP.md

## Database
- Current schema version: `20261117150208_Finances`
- Latest migration: `20261117150208_Finances` (`ClubBudgets`, `FinancialTransactions`; 29 tables)
- Data status: empty database created at startup. Sample dataset `data/sample/italy-sample.json` (fictional league: Serie A 8 clubs, Serie A2 6 clubs, season 2026-27 with entries and full competition rules; 37 players with full Step 05 profiles, 4 of them free agents; club profiles; Aurora Milano has a complete 12-player squad and contracts with bonuses, a release clause and options) and templates in `data/templates` can be imported from the Import tab. `data/sample/italy-squads.json` (import after the sample) fills every club to 11 players and adds 12 free agents, so the sample plays as a full career. `data/europe/` (generated by `build/generate-europe-dataset.py`; import all four files into a new game): 18 countries + 23 player nationalities, 20 leagues, 56 competitions (35 leagues, 17 domestic cups, `docs/CUP_FORMATS.md` §4, 4 international competitions, `docs/INTERNATIONAL_COMPETITIONS.md` §4), 512 clubs (240 real, Unverified), 6,168 players (generated, plus real squads of all 16 Serie A clubs from `build/real-rosters`), 6,168 contracts, 1,509 past spells (`docs/COUNTRY_REPORT.md`, `docs/PLAYER_DATABASE.md`). Test-only mock data in `tests/Ebm.Tests.Integration/TestSupport/SampleWorld.cs`.

## Important architectural decisions
1. Layered architecture with inward-only dependencies (Domain ← Services ← Persistence/DataImport ← Hosting ← Desktop). Enforced by architecture tests.
2. Services defines ports (repository interfaces); Persistence and DataImport implement or consume them. The UI never references Persistence or DataImport.
3. `Ebm.Hosting` is the only composition root and the only project that references Serilog. Only `Ebm.Persistence` references EF Core.
4. Each layer exposes one `Add<Layer>()` DI registration method.
5. Unexpected exceptions close the application to protect data. `DomainException` messages are player-facing and allow the app to continue.
6. All user data lives under a configurable data root (default `%LOCALAPPDATA%\EuropeanBasketballManager`).
7. Real-world time comes from the injected `TimeProvider`.
8. Competition, league and player rules are data-driven. Competition rules are a JSON object in `Competitions.RulesJson`.
9. Version scheme `0.<step>.<patch>` during Milestone 1.
10. Entity IDs are strongly typed GUIDs assigned by the application, never by the database. Player and club IDs are permanent.
11. Every foreign key is restrict-on-delete. History (club spells, contracts, matches, statistics, standings) is never deleted.
12. Statistics store the club the player represented, so history stays correct after transfers.
13. Schema changes are made only through new EF Core migrations, applied by `DatabaseInitializer` at startup. Never `EnsureCreated`, never edit a shipped migration.
14. Repositories and the unit of work are scoped: one DI scope per unit of work.
15. UI-facing use cases (`IDatabaseEditor`, `IDataImportService`) are Services interfaces; each call opens its own DI scope. Desktop never references DataImport or Persistence.
16. Imports never overwrite silently: existing records are rejected unless the explicit update mode is chosen. Accepted records are saved in one transaction. Duplicate rules live in `DuplicateKeys`, shared by importer and editor.
17. Import files reference countries by ISO alpha-3 code and every other record by its GUID; records without an ID get a new one.
18. The competition engine is pure, stateless domain code (`Ebm.Domain.Competitions.Rules` / `.Engine`); `ICompetitionService` orchestrates it over the repositories. Rules are validated by `CompetitionRules.Parse` on every save/import (unknown properties refused).
19. Standings rankings are always calculated from match results; `LeagueStandings` stores the totals, updated by `RecordResultAsync`.
20. Playoff fixtures are created on demand (`AdvanceAsync`), one game per series at a time, so no unneeded games exist. Fixtures carry `Stage` and `GameNumber`.
21. Migrations must stay transactional on SQLite: prefer add-column changes; avoid operations that force a table rebuild (e.g. adding check constraints) on existing tables. To restructure a table, rename it, create the new one, copy with SQL and drop the old one (hand-written `Up`, as in `PlayerModel`).
22. Player ratings are calculated, never stored: pure formulas in `Ebm.Domain.Players.Ratings` over `RatingModel.Default`. Overall = effective rating at the primary position; decisions should use category/position ratings and tendencies too (`PlayerEvaluation`).
23. Attributes and tendencies are fixed catalogues (`PlayerAttribute`, `PlayerTendency` enums); their order is the storage order. Import field names are the camel-case enum names.
24. A player's current club = open club spell, else latest running contract. Squad roles live in `RosterSpots` (one per player, current state, not history); no row = bench. Moves go through `IClubService.AssignPlayerAsync`.
25. Roster rules are a pure validator (`RosterValidator`) over `SquadRules`: club defaults in code, competition registration limits in the rules JSON (`registration.squad`). Invalid rosters can be saved; validation reports errors and warnings instead of blocking, so squads can be rearranged in steps.
26. An enum column with `HasDefaultValue` needs an EF sentinel outside the enum (`HasSentinel((T)(-1))`) unless the default is the enum's value 0; otherwise value 0 is replaced by the column default.
27. A contract runs on a date by `Contract.RunningOn(date)` only (dates plus `TerminationDate`); never re-implement the rule. Early ends keep the agreed `EndDate` and set `TerminationDate` (first day not under contract); contracts are never deleted.
28. Contract lifecycle changes go through `IContractService` (negotiation, payments, club moves in one unit of work). The editor and the importer are data tools: they set terms only, without negotiation, payments, overlap checks or moves.
29. One-off contract payments (signing bonus, settlement) are booked to `Club.Balance` at once (`Club.ChangeBalance`). Salaries are booked by the career calendar since Step 13 (decision 45); performance bonuses are calculated but not booked (Step 27).
30. Contract dates take effect through `IContractService.ProcessContractDatesAsync` (idempotent): at application start now, every game day from Step 12.
31. Negotiation is deterministic and parameterised (`ContractNegotiator` properties, registered as a singleton): the same offer always gets the same answer.
32. Club membership moves (spells and roster spots) go through `ClubMembership` (Services), shared by the squad, contract and transfer use cases.
33. Every player movement is a `Transfer` row (free-agent signing, transfer, release clause, loan, loan return), written in the same unit of work as the contract and membership changes; never deleted. `IContractService.SignAsync` writes the free-agent rows.
34. A transfer ends the old contract as `Transferred` (no compensation) and signs the new one the same day; the fee moves between balances at once. A loan keeps the parent contract running as `OnLoan` and adds a loan contract at the loan club on the same salary: wage bills and commitments skip `OnLoan` contracts (the loan club pays). Loans are the only case of two running contracts per player.
35. Market pricing and AI recruitment are pure, deterministic domain code (`TransferNegotiator`, `ClubRecruiter`), parameterised and registered as singletons; `ITransferService` orchestrates them. AI clubs never trade with managed clubs (offers to the user's club come with the career mode).
36. Limits on moves: fees never take a balance below zero; registration limits (first-team size, foreigners, minimum age, from the club rules and every competition it plays in) block; the wage budget warns the user and binds AI clubs. Only problems the newcomer causes are reported.
37. `ITransferService.ProcessTransferDatesAsync` is the single "apply the calendar" entry point: loan returns first, then `ProcessContractDatesAsync`. Called at startup (every game day from Step 12).
38. Tactics (`Tactic`) are club-owned configurations; at most one is selected per club (filtered unique index) and is what the club plays its next match with; no selection = `TeamTactics.Default`. Player assignments are current state in a JSON column without player FK (tactics never block deleting a player); `ITacticsService.GetMatchTacticsAsync` drops assignments of players who left and hands the engine an immutable `TeamTactics`; unassigned players get `TacticalRoles.Suggest`.
39. The match engine (`Ebm.Domain.Matches.Simulation`) is pure and deterministic for a setup and seed; all its numbers live in `MatchSimulation.Play` and `TacticalModel` and are documented in `docs/MATCH_ENGINE.md`. `IMatchService` builds the matchday squads (`TeamSheet.Pick`) from the rosters and the selected tactics, and stores a finished match (result, standings, box scores) in one transaction through `CompetitionService.AddResultAsync`; a match in progress (`MatchSession`) lives in memory until saved. Statistical ranges are guarded by unit tests (`MatchStatisticsValidationTests`); retune them together with the docs.
40. The match centre (`MatchCenterViewModel`) only drives `IMatchService` and the `MatchSimulation` API; a live match is saved automatically when it ends. Managing a side switches that side's automatic coach off (forced substitutions still happen).
41. The game date is `GameClock.Today` (Services): the career date while a career is loaded, else the `TimeProvider` date. Services never read the `TimeProvider` date directly. The career date only moves forward, one game day at a time, through `ICareerService` (docs/SEASON_LIFECYCLE.md).
42. A game database holds at most one career (`Careers` table); the database file is the save. A game day plays every other fixture of the day instantly, advances the competitions, moves the date and applies contract/transfer dates; the user's fixture must be played first (live or instantly). The AI transfer window runs on the last day of each season.
43. A club that cannot field five players forfeits 20–0 (`Match.IsForfeit`, no box score) so the calendar never blocks. The next season keeps every competition's entries except divisions, which exchange clubs by `SeasonTransition` (promotion/relegation from the rules); schedules keep the previous first-round offset.
44. AI decisions are pure planners in `Ebm.Domain.Management`, applied by `IClubManagementService` through the same use cases as the user (rosters, tactics, contracts, transfers), so AI clubs obey the same rules. Every club but `Career.ClubId` is AI-managed; the calendar decides when (docs/AI_MANAGEMENT.md §1).
45. Money moves with the calendar: salary instalments are paid on their due dates and the placeholder income (wage budget / 12) at month end, booked in the same transaction as the date change.
46. The free-agent market is kept at two free agents per club by generated fictional players whose names recombine existing players' names of the nationality (no hard-coded name lists).
47. The live game database is always up to date; save files are SQLite backups (`<DataRoot>\saves\*.ebmsave`) made and restored with the online backup API. Loading upgrades the save through the normal migrations and reloads the career date; saves from a newer schema are refused (docs/SAVE_FILES.md).
48. With `Career.AssistantManages` the user's club is AI-managed and its matches are played by the calendar (holiday mode); the user takes over by switching it off.
49. Releases are self-contained single-file win-x64 builds (`build/publish.ps1`), smoke-tested before zipping (docs/RELEASE.md).
50. A competition season is evaluated as a whole by the pure `CompetitionSeason` (phases, group and final tables, next games, outcome); `CompetitionService` only loads and stores. The final table (groups in order, then clubs in no group) drives playoffs, promotion and relegation.
51. EU/EEA membership is a country flag (`Country.IsEuEea`, EU + EEA + Switzerland); non-EU limits are a squad rule like the foreign-player limit.
52. The European dataset is generated by a deterministic script (UUID v5 IDs from identity): edit `build/generate-europe-dataset.py`, not the data files. Division sizes stay constant (promotion places = relegation places of the division above); second divisions relegate nobody until third tiers exist.
53. Data quality is explicit: `DataStatus` (Unverified/Verified/Estimated/Generated) on player profile, player attributes, contract terms and club; `Verified` requires a source; missing attributes and salaries are estimated and marked `Estimated`, never verified. Game-created records are `Generated`; imports default to `Unverified` (docs/PLAYER_DATABASE.md).
54. `NationalityId` is the sporting nationality (domestic/foreign counts); `SecondNationalityId` is a further citizenship counted only for EU/EEA status (`Player.HasNationality`).
55. Duplicate keys fold accents and common transliterations (`DuplicateKeys.Normalise`); same name with birth dates ≤ 366 days apart is accepted with a possible-duplicate note. Imported club history is closed, non-overlapping spells and is never rewritten.
56. A squad loads only its club's contracts and spells (`ClubMembership.MembersOfAsync`, same rule as `CurrentClubsAsync`); per-club reads must not scan the whole database.
57. Cups are `Cup` competitions with a `cup` rules section, run by the pure `CupSeason` (docs/CUP_FORMATS.md). The draw stores each club's seed on its `CompetitionParticipants` row; groups and ties are derived from the seeds, the rules and a draw seed fixed by the competition and season IDs, so a draw never changes once made and the fixtures are its record. Knockout games are `Cup` fixtures with `GameNumber` = leg.
58. Cup games are scheduled on the first day from their planned date on which neither club has another fixture; a cup's next round is scheduled when all ties of the current one are decided.
59. Two-legged ties never end level: the second leg goes to overtime while the aggregate is level (`MatchSetup.OvertimeMargin`); a level aggregate after a forfeit or an edit goes to the winner of the second leg.
60. Qualification cups take their clubs from the country's league table after a given round and are drawn again every season (entries not carried over); other cups keep their entries like leagues.
61. International competitions are cups without a country and with `cup.entry` slots, run by the same engine. They qualify every season (career start, next season) from the previous season's champions, final tables and cup winners (first season: reputation), filled by tier; a club plays one international competition per season. Entries are not carried over.
62. Every fixture created after the league regular seasons (cups, international games, league second phases, play-ins, playoffs) moves to the first day on which neither club plays; `GetCalendarConflictsAsync` reports any remaining clash.
63. Cup rewards are paid once, when the cup ends, and recorded on the entry (`PrizeMoney`, `ReputationGain`); neutral-venue rounds are a rules flag read by the match service.
64. One market for every country: nationality only decides foreign/non-EU status at the receiving club. Contract negotiations use the club's attractiveness (`ClubAttractiveness`: reputation + international tier bonus), never the bare reputation.
65. Registration deadlines are competition rules (`registration.deadline`); the earliest deadline of a club's current competitions closes new first-team registrations until the season's last day, except up to the squad minimum (`RegistrationWindow`, `ClubSeason.CheckRegistrationAsync`, shared by contracts and transfers).
66. A loan's parent salary share is paid up front as `Transfers.SalaryContribution` (refunded pro rata on recall), so loan contracts keep the full salary and wage bills keep skipping `OnLoan` contracts. An option to buy converts the loan without a new registration.
67. A mutual termination is a buyout the player must accept (`ContractNegotiator.MinimumBuyout`); the default settlement is his minimum.
68. Scouting knowledge is per club: stored reports are snapshots (never deleted, newest wins); anything else a club "knows" is background knowledge computed on the fly from its best scout, seeded per club, player and year (`ScoutingKnowledge`), so AI clubs need no stored reports. Every estimate is bounded by the report's margin and seeded with `ScoutingRules.StableSeed` (never `HashCode`).
69. The deadline exemption ("short of players") uses the competitions' own squad minimum; `SquadRules.Default` applies only to a club without competitions. The AI recruiter keeps the default as the club's baseline.
70. Development is pure (`PlayerDevelopment`, singleton) with seeded randomness (`ScoutingRules.StableSeed`): fitness daily, development on Mondays, potential and retirement on a season's last day before the transfer window (docs/DEVELOPMENT.md §1). Training plans are current state, one per club; AI clubs are re-planned weekly in bulk by `IClubManagementService.PlanTrainingAsync`. Retirement is final (`Player.RetiredOn`) and its rules are the only configurable part (`Ebm:Retirement`, bound by the host). Retired players are filtered wherever players are offered (free agents, market, scouting, negotiation).
71. Generated staff (scouts, coaches, physios) are hired through `StaffHiring` (Services), one formula for ability and naming.
72. Injuries are medical records (`Injuries`, history) and `Players.InjuryStatus`/`InjuryReturnDate` mirror the active one, so availability code (match setup, line-ups, roster validation) stays unchanged. The medical day runs after the development day on every match of the day (the user's included); rules are pure (`InjuryRules`, singleton, `Ebm:Injuries`) with seeded randomness. The club sees the physio's estimate; the true recovery time stays hidden (docs/INJURIES.md).
73. Money moves only through `Ledger.Book` (Services): balance change + `FinancialTransactions` row in the same unit of work, so balances reconcile with the ledger; data tools set balances without bookings. Budgets are per club and season (`ClubBudgets`), set by the pure `FinanceModel` when a season is planned and copied to `Club.WageBudget`; a financial year starts 30 days before its season (docs/FINANCES.md). Supersedes the placeholder income of decision 45.

Details: `docs/TECHNICAL_ARCHITECTURE.md`, `docs/DATABASE_SCHEMA.md`, `docs/IMPORT_FORMATS.md`, `docs/COMPETITION_FORMATS.md`, `docs/PLAYER_MODEL.md`, `docs/ROSTER_RULES.md`, `docs/CONTRACTS.md`, `docs/TRANSFERS.md`, `docs/TACTICS.md`, `docs/MATCH_ENGINE.md`, `docs/MATCH_UI.md`, `docs/SEASON_LIFECYCLE.md`, `docs/AI_MANAGEMENT.md`, `docs/SAVE_FILES.md`, `docs/RELEASE.md`, `docs/COUNTRY_REPORT.md`, `docs/PLAYER_DATABASE.md`, `docs/CUP_FORMATS.md`, `docs/INTERNATIONAL_COMPETITIONS.md`, `docs/SCOUTING.md`, `docs/DEVELOPMENT.md`, `docs/INJURIES.md`, `docs/FINANCES.md`.

## Known issues
- AI: no youth intake (generated free agents keep the market supplied); first-season sponsorship is sized to fund each dataset wage budget (docs/FINANCES.md §3); AI makes no loans or bids for the user's players; the three-season simulation test takes about two minutes.
- Career: one career per database; `StartNextSeasonAsync` is repeatable but not atomic.
- The launch integration test opens a real window for about one second and needs an interactive desktop session. Skip it on headless machines with `--filter "Category!=Launch"`.
- Reputation and morale are stored and edited but not yet changed by the game (Steps 23+). Injuries: decided after the match (no in-game injuries), no treatment choices or medical budget (docs/INJURIES.md §8). Development history is not stored; no youth intake (docs/DEVELOPMENT.md §7). Rating weights are code (`RatingModel.Default`), not a data file (Step 33).
- Player import files in the old 8-rating format are no longer accepted.
- Leagues and cups can be scheduled; `Tournament` competitions cannot. No relegation playoffs, two-legged ties inside league playoffs, reseeding or mid-season entrants (see `docs/COMPETITION_FORMATS.md` §2, `docs/COUNTRY_REPORT.md` §3).
- Cups: domestic Final Eights/Fours are hosted by the better seed (rounds can be neutral), legs are never drawn, qualification only from the current table of the cup's own country (no previous-season seeding, holders, hosts, late-entering lower-division clubs or supercups); cup formats are unverified simplifications; cup runs are not in the club history (use `ICompetitionService.GetHistoryAsync`)(`docs/CUP_FORMATS.md` §5).
- International competitions: simplified formats (no play-in, best-of series, second group phase, qualifying rounds or licences), group draws do not separate countries, reputation rewards never decay, prize money paid as one sum without finance records (`docs/INTERNATIONAL_COMPETITIONS.md` §5).
- European dataset: players are fictional except the 16 Serie A squads (`build/real-rosters`; birth dates to the year; attributes estimated from career statistics for 200 players, `build/player_ratings.py`; real squads exceed the game's first-team foreign limit, which the real LBA applies to the matchday sheet); second divisions and NBL/TDM1/Promotiedivisie clubs are placeholders; the 240 real clubs are unverified for 2026-27; most formats on a 2025-26 basis (`docs/PLAYER_DATABASE.md` §5, `docs/COUNTRY_REPORT.md`).
- Competition UI: the match centre shows schedules and results and can create a regular-season schedule; standings are on the *Career* screen; playoffs advance automatically with the career calendar.
- Import covers countries, arenas, clubs, leagues, divisions, competitions, seasons, participants, players, player contracts and past club spells only (see `docs/IMPORT_FORMATS.md` §7); squad roles are set in the game.
- The squad screen's *Add a player to the squad* (`AssignPlayerAsync`) still moves a player without a contract (squad tool); the game flow signs through *Club contracts*. Board expectations are judged at season end and steer budgets; board confidence comes with Step 27.
- Finances: no broadcasting/merchandise income, bank loans, taxes or fee amortisation; neutral venues booked as home games; no board sanctions (docs/FINANCES.md §7).
- Contracts: performance bonuses are calculated but not paid (Step 27); the wage budget is enforced for AI clubs only (the user gets a warning; Step 27 decides the board's rules); undecided options lapse at expiry (no reminders before Step 12); the editor and the importer do not check overlapping contracts.
- Transfers: no persisted offers (negotiations answer at once and AI clubs never bid for a managed club's players; pending offers come with the career mode); AI clubs buy free agents, other AI clubs' surplus and one upgrade per window from lower-reputation AI clubs; AI clubs make no loans; registration deadlines in the datasets are an unverified simplification (28 Feb / 31 Mar); valuations ignore form and statistics; loans copy the player's salary (the parent's share is a lump-sum contribution) and the player does not negotiate them; no buy-back or sell-on clauses; a loaned player cannot be bought until recalled; transfer history is not importable (`Transfers` is created by the game only); the loan club's termination of a loan contract does not close the loan (use *Recall*).
- Scouting: the database screens (profile, market, squad) still show true ratings; scouts are generated (no hiring screen, specialities or experience); reports do not follow later attribute changes (`docs/SCOUTING.md` §7).
- Performance: an AI transfer window on the European dataset takes many minutes (the market is reloaded for every club); `EuropeanTransferWindowTests` take about 25 minutes.
- A player with an open club spell but no contract counts as a free agent; signing him elsewhere ends that spell.
- Match engine: substitutions and timeouts only between possessions; no technical/unsportsmanlike/intentional fouls or held balls; morale is not changed by matches yet; injuries are decided after the game from the minutes played; box scores store whole minutes; play-by-play, energy and plus-minus are not stored (only box scores), so past matches show box scores and team stats only; an unfinished live match is lost when the application closes.
- Tactics: the tactics screen shows enum names (`PickAndRoll`, `Zone23`).
- Off-screen renders of the editor grids collapse star-width columns (render-test artefact, not seen in the running app).
- Player, club profile and squad screens are verified by an off-screen render test (screenshots reviewed in Steps 05–06); the other editor tabs only by the smoke-test launch and view-model tests.

## Current dependencies
| Package | Version | Used by |
|---|---|---|
| Microsoft.Extensions.Hosting | 10.0.14 | Hosting |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.14 | Persistence |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | Services, Persistence, DataImport |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | DataImport |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.14 | Persistence |
| Microsoft.EntityFrameworkCore.Design | 10.0.14 | Persistence (design time only, not shipped) |
| dotnet-ef (local tool) | 10.0.14 | Creating migrations |
| Serilog | 4.4.0 | Hosting |
| Serilog.Extensions.Logging | 10.0.0 | Hosting |
| Serilog.Sinks.File | 7.0.0 | Hosting |
| Serilog.Sinks.Debug | 3.0.0 | Hosting |
| CommunityToolkit.Mvvm | 8.4.2 | Desktop |
| xunit / xunit.runner.visualstudio | 2.9.3 / 3.1.4 | Tests |
| Microsoft.NET.Test.Sdk | 17.14.1 | Tests |
| coverlet.collector | 6.0.4 | Tests |

## Last completed step log
logs/STEP_24_LOG.md

## Instructions for continuing
1. Read this file.
2. Read the last completed step log.
3. Inspect the existing implementation.
4. Follow the next step specification in `docs/ROADMAP.md`.
5. Do not repeat completed work.
6. Generate a new step log upon completion.
7. Tests: during a step run unit tests plus the fast integration suite (`--filter "Category!=Launch&Category!=Slow"`) and the step's own tests; run the slow suite (`Category=Slow`) once before a milestone release, in the background. New tests that import a full dataset or simulate seasons get `[Trait("Category", "Slow")]`.
