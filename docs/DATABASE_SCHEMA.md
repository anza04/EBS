# Database Schema

Game database: one SQLite file, `<DataRoot>\databases\ebm.db`. It is managed by EF Core 10 with versioned migrations.
Schema version: **`20261118133803_TacticalEngine`** (Step 27).

| Migration | Step | Change |
|---|---|---|
| `20261109114155_InitialSchema` | 02 | 17 tables |
| `20261109124652_FixtureStages` | 04 | `Fixtures.Stage` and `Fixtures.GameNumber` with defaults, so existing fixtures stay regular-season games. Add-column only, so the upgrade runs in one transaction (a check constraint would force a table rebuild on SQLite; the domain enforces the range) |
| `20261109125814_PlayerModel` | 05 | Players: profile and condition columns (with defaults). `PlayerAttributes` replaced by the 27-attribute table (old table renamed, new one created and filled from the old ratings, old one dropped) and new `PlayerTendencies`. Hand-written `Up` so nothing is rebuilt and the upgrade stays in one transaction; mapping in `docs/PLAYER_MODEL.md` §7 |
| `20261109171322_ClubsAndRosters` | 06 | Clubs: `City`, `Reputation`, `Balance`, `WageBudget`, `BoardExpectation` (add-column with defaults; `City` copied from the home arena). New `RosterSpots` table |
| `20261109194813_Contracts` | 07 | Contracts: `PaymentFrequency`, `SigningBonus`, `PerformanceBonuses`, `ReleaseClause`, `ClubOptionUntil`, `PlayerOptionUntil`, `Status`, `TerminationDate`, `TerminationCompensation` (add-column with defaults; existing contracts become `Active` with monthly pay and no extras). Index `ClubId` replaced by (`ClubId`, `EndDate`) |
| `20261109204206_Transfers` | 08 | New `Transfers` table (create-table only, so the upgrade runs in one transaction). No existing table changes; `Contracts.Status` gains the values `OnLoan` and `Transferred` (text column, no schema change) |
| `20261112140336_Tactics` | 09 | New `Tactics` table (create-table only) |
| `20261112144630_Careers` | 12 | New `Careers` table; `Matches.IsForfeit` (add-column, default false) |
| `20261112154154_CareerAssistant` | 14 | `Careers.AssistantManages` (add-column, default false) |
| `20261116135956_CupCompetitions` | 17 | `CompetitionParticipants.Seed` (nullable add-column). New fixture stages `CupGroup` and `Cup` (stored as text, no schema change); `Cup` fixtures use `GameNumber` 1–2 for the legs |
| `20261116144825_InternationalCompetitions` | 18 | `CompetitionParticipants.PrizeMoney` (long?) and `ReputationGain` (int?): cup rewards paid when the competition ended (nullable add-columns) |
| `20261116151616_LoanAgreements` | 19 | `Transfers.SalaryContribution` (default 0) and `OptionFee` (long?): loan agreements (add-columns, no check constraint). New kind `OptionToBuy` (text column, no schema change) |
| `20261117124356_Scouting` | 20 | `Staff.Ability` (default 50). New tables `ScoutingAssignments`, `ScoutReports` and `ScoutReportAttributes` (create-table and add-column only) |
| `20261117140121_Development` | 21 | `Players.RetiredOn`. New table `TrainingPlans` (create-table and add-column only) |
| `20261117143312_Medical` | 22 | New table `Injuries` (create-table only) |
| `20261117150208_Finances` | 23 | New tables `ClubBudgets` and `FinancialTransactions` (create-table only) |
| `20261118081325_ContractNegotiations` | 25 | New tables `Agents`, `AgentRelationships`, `ContractNegotiations`; `Contracts` gains `PromisedRole`, `PromisedMinutes`, `PromiseStatus` (default `None`), `PromiseReviewedOn` (add-column, nullable or defaulted) |
| `20261118090203_PlayerChemistry` | 26 | New table `PlayerRelationships` (create-table only) |
| `20261118133803_TacticalEngine` | 27 | Tactics: twelve `Situation*` columns (add-column with defaults; existing "AI plan" tactics get coach adjustments and two-for-ones). Matches: `TacticalNotes` (JSON, nullable) |

Entities are plain C# classes in `src/Ebm.Domain`. Their mapping (tables, keys, indexes, constraints) lives in
`src/Ebm.Persistence/Configurations`. The domain validates every value on construction, and the schema
enforces the same rules again so data written by other routes (imports, editors, raw SQL) cannot break them.

## 1. Conventions

| Rule | Detail |
|---|---|
| Primary keys | Every table has an `Id` column holding a GUID stored as TEXT. In code each entity has its own ID type (`PlayerId`, `ClubId`, …), so IDs of different entities cannot be mixed up. IDs are assigned by the application, never by the database (`CreateVersion7` for new records; imported records keep the ID from the data file). This makes player and club IDs permanent across databases, imports and save games. |
| Foreign keys | Always enforced (`Foreign Keys=True` in the connection string). Every foreign key is `ON DELETE RESTRICT`, so a record that anything refers to cannot be deleted and history is never removed as a side effect. The only cascades: `PlayerAttributes` and `PlayerTendencies` are deleted with their player (a player can only be deleted if nothing else refers to it). |
| Enums | Stored as their name (TEXT), e.g. `SmallForward`, so the data stays readable and stable when enum members are reordered. |
| Dates | In-game dates are `DateOnly`, stored as TEXT `yyyy-MM-dd`. |
| Money | Whole euros, `INTEGER` (`long`). No floating point. |
| Derived values | Not stored: `Player.FullName`, `BoxScore.Points`/`Rebounds`, `LeagueStanding.Played`/`PointDifference`, `Match.HomeWon`, `PlayerClubHistoryEntry.IsCurrent`. |
| Rules as data | Competition rules are a JSON object in `Competitions.RulesJson`. No country-specific rule is in code. |

## 2. Relationships

```text
Country ─┬─< League ─< Division ─< Competition (DivisionId, optional)
         ├─< Competition (CountryId, optional; null = international)
         ├─< Arena ─< Club (HomeArenaId, optional)
         ├─< Club
         ├─< Player (NationalityId) ── PlayerAttributes, PlayerTendencies (1:1)
         └─< StaffMember (NationalityId)

Season ─┬─< CompetitionParticipant >─ Competition, Club      (club entered in a competition for a season)
        ├─< LeagueStanding         >─ Competition, Club
        └─< Fixture >─ Competition, Club (home), Club (away)
                 └── Match (1:1) ─< PlayerMatchStatistics >─ Player, Club (club represented)

Club ─┬─< Contract >─ Player | StaffMember (exactly one)
      ├─< PlayerClubHistory >─ Player
      ├─< RosterSpot >─ Player (unique: one squad role per player)
      ├─< Tactic (assignments as JSON, players by ID without FK)
      ├─< ScoutingAssignment >─ StaffMember (scout); Player | Country | Competition (target, optional)
      ├─< ScoutReport >─ Player, StaffMember?, ScoutingAssignment? ── ScoutReportAttributes (1:1)
      └─< Transfer (FromClubId, optional / ToClubId) >─ Player, Contract (ContractId, ParentContractId; optional)
```

`─<` means one-to-many. All relationships are by ID only (the domain has no navigation properties), so entities stay small and loading is explicit.

## 3. Tables

Column types: `id` = GUID TEXT, `int` = INTEGER, `text(n)` = TEXT with maximum length n (checked by the domain).

### Countries
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| Code | text(3) | Required, ISO 3166-1 alpha-3, upper case, **unique** |
| Name | text(100) | Required |

### Leagues
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| Name | text(100) | Required |
| ShortName | text(20) | Required |
| CountryId | id? | FK Countries; null for international leagues |

### Divisions
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| LeagueId | id | FK Leagues |
| Name | text(100) | Required |
| Level | int | 1 = top tier, 1–10. **Unique per league** (`LeagueId, Level`) |

### Competitions
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| Name / ShortName | text(100) / text(20) | Required |
| Type | text | `League`, `Cup`, `Tournament` |
| CountryId | id? | FK Countries; null = international |
| DivisionId | id? | FK Divisions; set for a division's championship |
| RulesJson | text | Required JSON object, checked with `json_valid` and `json_type = 'object'`. Default `{}`. The rule schema is defined in Step 04. |

### Seasons
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| Name | text(20) | Required, **unique** (e.g. `2026-27`) |
| StartDate / EndDate | date | `EndDate > StartDate` |

### CompetitionParticipants
A club's entry in a competition for a season. This is how a club takes part in several competitions, and which division it plays in each season.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| CompetitionId / SeasonId / ClubId | id | FKs. **Unique** (`CompetitionId, SeasonId, ClubId`). Index (`ClubId, SeasonId`) |
| Seed | int? | A cup's seed (1 = best), set by the draw (Step 17); null in leagues and before the draw |
| PrizeMoney | long? | Cup prize money paid when the competition ended (Step 18); null = not paid yet, no rewards paid or a league |
| ReputationGain | int? | Reputation points gained with the prize money (0–100) |

### Arenas
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| Name / City | text(100) | Required |
| CountryId | id | FK Countries |
| Capacity | int | 1–100,000 (`> 0` in schema) |

### Clubs
| Column | Type | Rules |
|---|---|---|
| Id | id | PK, permanent |
| Name / ShortName | text(100) / text(20) | Required |
| CountryId | id | FK Countries |
| HomeArenaId | id? | FK Arenas; arenas can be shared |
| FoundedYear | int? | 1850–2100 |
| City | text(100)? | Null = show the home arena's city |
| Reputation | int | 1–100, default 50 |
| Balance | int | Euros, ±10,000,000,000, default 0 |
| WageBudget | int | Euros a year, 0–1,000,000,000, default 0 |
| BoardExpectation | text(32) | `WinTitle`, `ReachPlayoffs`, `MidTable` (default), `AvoidRelegation`, `WinPromotion` |

Step 06 columns are range-checked by the domain only (decision 21). `BoardExpectation` has an EF sentinel of `-1`,
so `WinTitle` (the enum's CLR default) is always written instead of being replaced by the column default.

### Players
| Column | Type | Rules |
|---|---|---|
| Id | id | PK, permanent |
| FirstName / LastName | text(60) | Required. Index (`LastName, FirstName`) |
| DateOfBirth | date | |
| NationalityId | id | FK Countries |
| HeightCm | int | 150–250 |
| WeightKg | int | 50–200 |
| Position | text | `PointGuard`, `ShootingGuard`, `SmallForward`, `PowerForward`, `Center` |
| WingspanCm | int | Height ± 30 (domain) |
| SecondaryPositions | text(100) | Comma-separated position names, default `''`; never the primary position (domain) |
| Experience / Reputation / Potential | int | 1–100 (domain); defaults 1 / 1 / 50 |
| Fitness / Morale | int | 0–100 / 1–100 (domain); defaults 100 / 50 |
| InjuryStatus | text(16) | `Healthy` (default), `DayToDay`, `Injured` |
| InjuryReturnDate | date? | Required when `Injured`, null when `Healthy` (domain). Follows the active injury's estimate (docs/INJURIES.md) |
| RetiredOn | date? | First day after the player retired; null while active (Step 21, `docs/DEVELOPMENT.md` §5) |

Step 05 columns are checked by the domain only: adding check constraints to an existing table would force a rebuild
(decision 21).

### PlayerAttributes
1:1 with Players, always loaded with the player (EF owned type). Catalogue and meaning: `docs/PLAYER_MODEL.md`.

| Column | Type | Rules |
|---|---|---|
| PlayerId | id | PK and FK Players (cascade) |
| CloseShot, MidRange, ThreePoint, FreeThrow, Layup, Dunk, PostMoves, Passing, CourtVision, BallHandling, DecisionMaking, PerimeterDefense, InteriorDefense, Steal, Block, DefensivePositioning, OffensiveRebound, DefensiveRebound, Speed, Strength, Vertical, Agility, Stamina, Concentration, Leadership, Consistency, WorkRate | int | Each 1–100 (`CK_PlayerAttributes_Range`) |

### PlayerTendencies
1:1 with Players, always loaded with the player (EF owned type).

| Column | Type | Rules |
|---|---|---|
| PlayerId | id | PK and FK Players (cascade) |
| ThreePointShot, MidRangeShot, DriveToBasket, PostUp, PassFirst, Isolation, CrashOffensiveGlass, DefensiveGambling | int | Each 1–100 (`CK_PlayerTendencies_Range`) |

### Staff
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| FirstName / LastName | text(60) | Required |
| DateOfBirth | date | |
| NationalityId | id | FK Countries |
| Role | text | `HeadCoach`, `AssistantCoach`, `Scout`, `Physio`, `FitnessCoach` |
| Ability | int | 1–100, default 50: how good he is at his job; for scouts, report accuracy (Step 20) |

### Contracts
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| ClubId | id | FK Clubs |
| PlayerId | id? | FK Players |
| StaffMemberId | id? | FK Staff. **Exactly one** of `PlayerId` and `StaffMemberId` is set |
| StartDate / EndDate | date | `EndDate > StartDate`; at most 10 years (domain) |
| AnnualSalary | int | Euros, 0–100,000,000 (`>= 0` in schema) |
| PaymentFrequency | text(16) | `Monthly` (default), `Quarterly`, `Annually` |
| SigningBonus | int | Euros, default 0 |
| PerformanceBonuses | text(200) | `Condition:amount;…`, default empty |
| ReleaseClause | int? | Euros; null = none |
| ClubOptionUntil / PlayerOptionUntil | date? | Option end dates; after `EndDate`, at most 3 years |
| Status | text(16) | `Active` (default), `Expired`, `Terminated`, `Renewed`, `OnLoan` (Step 08), `Transferred` (Step 08) |
| TerminationDate | date? | First day not under contract after an early end |
| TerminationCompensation | int | Settlement paid on termination, default 0 |
| PromisedRole | text(16)? | Step 25: `Reserve`, `Prospect`, `Rotation`, `Starter`, `Star`; null = no role promise |
| PromisedMinutes | int? | Minutes per game promised, 1–40 (domain) |
| PromiseStatus | text(16) | `None` (default), `Kept`, `AtRisk`, `Broken`: latest promise review |
| PromiseReviewedOn | date? | Date of the latest promise review |

Index (`ClubId, EndDate`). Columns added in Step 07 have defaults and domain-only range checks (decision 21).
Contracts are never deleted; expired, terminated and renewed ones are history. Lifecycle and salary rules:
`docs/CONTRACTS.md`.

### PlayerClubHistory
One row per spell of a player at a club.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| PlayerId / ClubId | id | FKs. Indexes (`PlayerId, StartDate`) and (`ClubId`) |
| StartDate | date | |
| EndDate | date? | Null while current; `EndDate >= StartDate` |

**At most one current spell per player:** a unique index on `PlayerId` filtered by `EndDate IS NULL`
(`IX_PlayerClubHistory_PlayerId_Current`). A move is recorded by closing the current spell and opening a new one
(`ClubMembership`). A loan opens a spell at the loan club and, on return, a new spell at the parent club; the
`Transfers` table records which spells were loans.

### Transfers
One row per player movement: free-agent signing, transfer, loan, loan return, option to buy (rules in `docs/TRANSFERS.md` §1).

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| PlayerId | id | FK Players. Index (`PlayerId, Date`) |
| FromClubId | id? | FK Clubs; null = free agent. Index (`FromClubId, Date`). `FromClubId <> ToClubId` |
| ToClubId | id | FK Clubs. Index (`ToClubId, Date`) |
| Kind | text(20) | `FreeAgentSigning`, `Transfer`, `ReleaseClause`, `Loan`, `LoanReturn`, `OptionToBuy`. Index (`Kind, ReturnedOn`) |
| Date | date | Day the player joined `ToClubId` |
| Fee | int | Euros, `>= 0` |
| ContractId | id? | FK Contracts: the contract signed with `ToClubId` (the loan contract for a loan) |
| ParentContractId | id? | FK Contracts: for loans and returns, the contract with the parent club |
| LoanEndDate | date? | Set if and only if `Kind = 'Loan'`; `> Date` (check constraint) |
| ReturnedOn | date? | Loans only, `> Date` (check constraint): the day the loan ended (return or option to buy); null while the loan is open |
| SalaryContribution | int | Loans: the parent club's share of the salary, paid to the loan club at the start; default 0 (Step 19) |
| OptionFee | int? | Loans: fee of the loan club's option to buy; null without an option (Step 19) |

Transfers are never deleted (restrict-on-delete on every FK), so a player, club or contract with transfer history
cannot be removed.

### ScoutingAssignments
One row per scouting assignment (rules in `docs/SCOUTING.md` §2–§3). Never deleted.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| ClubId | id | FK Clubs. Index (`ClubId, Status`) |
| ScoutId | id | FK Staff |
| Focus | text(16) | `Player`, `Region`, `League`, `Position`, `Youth` |
| PlayerId | id? | FK Players: target of a player assignment |
| CountryId | id? | FK Countries: region target, optional youth country |
| CompetitionId | id? | FK Competitions: league target |
| Position | text(16)? | Position target, else optional filter |
| MaxAge | int? | Oldest player watched; set for youth assignments |
| StartDate / DueDate | date | `DueDate > StartDate` (check constraint). Index (`Status, DueDate`) |
| Status | text(16) | `Active`, `Completed`, `Cancelled` |
| EndedOn | date? | Day it was completed or cancelled |

### ScoutReports
One row per report: what a club believes about a player on a date (`docs/SCOUTING.md` §4). The newest per club and
player is the club's view; older ones are history. Never deleted.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| ClubId | id | FK Clubs. Index (`ClubId, PlayerId, Date`) |
| PlayerId | id | FK Players |
| ScoutId | id? | FK Staff; null for background knowledge |
| AssignmentId | id? | FK ScoutingAssignments |
| Date | date | |
| Accuracy | int | 5–98 (check constraint) |
| Margin / PotentialMargin | int | Largest error of an attribute or the overall / of the potential |
| EstimatedOverall / EstimatedPotential | int | 1–100 |
| EstimatedValue / EstimatedSalary | int | Euros, `>= 0` (check constraint) |

`ScoutReportAttributes`: the 27 estimated attributes, keyed by `ScoutReportId` (1:1), same columns as `PlayerAttributes`.

### RosterSpots
A player's role in his current club's squad: current state, not history (rules in `docs/ROSTER_RULES.md`).

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| PlayerId | id | FK Players, **unique** (one spot per player) |
| ClubId | id | FK Clubs, indexed |
| Role | text(16) | `Starter`, `Rotation`, `Bench`, `Inactive`, `Youth` |
| LineupPosition | text(32)? | Set if and only if `Role = 'Starter'` (check constraint) |
| RotationOrder | int? | Set if and only if `Role = 'Rotation'`, ≥ 1 (check constraint) |

A club player without a row is on the bench. Rows of players who left the club are moved or dropped when the squad
is saved.

### Tactics
A club's saved tactical configurations (rules in `docs/TACTICS.md`).

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| ClubId | id | FK Clubs. Unique (`ClubId, Name`); unique filtered index `IX_Tactics_SelectedPerClub` on `ClubId` where `IsSelected = 1` (one selected tactic per club) |
| Name | text(60) | |
| OffensiveSystem, DefensiveSystem | text(32) | Enum names |
| OffenseTempo, OffenseShotSelection, OffenseBallMovement, OffenseSpacing, OffensePickAndRollFrequency | int | 1–5 (domain-checked) |
| OffenseFocus | text(16) | `Balanced`, `Inside`, `Perimeter`, `KeyPlayers` |
| DefenseIntensity, DefenseBallPressure, DefenseHelpDefense | int | 1–5 (domain-checked) |
| DefenseSwitching, DefensePickAndRollCoverage, DefenseRebounding | text(16) | Enum names |
| SituationMatchupTargeting, SituationPlayCalling, SituationZoneOffense, SituationEndOfQuarter, SituationFoulManagement, SituationRotation | text(16) | Enum names (defaults `Off`, `Flow`, `Standard`, `Normal`, `Normal`, `Standard`) |
| SituationMilkClockWhenLeading, SituationHurryWhenTrailing, SituationFoulWhenTrailing, SituationFoulUpThree, SituationRestStartersInBlowouts, SituationCoachAdjusts | bool | Defaults true, true, true, false, true, false (docs/TACTICS.md §7) |
| IsSelected | bool | |
| Assignments | json? | Array of `{ PlayerId, Role, ShotPriority, MinutesTarget, Matchup }` (`Matchup` missing in older rows = `Positional`). Current state; player IDs have no foreign key, so tactics never block deleting a player, and assignments of players who left the club are ignored |

### TrainingPlans
A club's training programme (rules in `docs/DEVELOPMENT.md`).

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| ClubId | id | FK Clubs, **unique** (one plan per club) |
| TeamFocus | text(16) | `Balanced`, `Shooting`, `Tactical`, `Defense`, `Physical`, `Recovery` |
| Intensity | text(16) | `Light`, `Normal`, `Hard` |
| Individuals | json? | Array of `{ PlayerId, Focus }`, at most 30, each player once. Current state; no player foreign key |

### Agents
Player agents (Step 25, `docs/CONTRACTS.md` §4.1). Created once, the first time anyone negotiates.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| Name | text(60) | Unique |
| Toughness | int | 1–100 |

### AgentRelationships
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| AgentId | id | FK Agents; unique (AgentId, ClubId) |
| ClubId | id | FK Clubs |
| Score | int | −100…100, 0 = no history |

### PlayerRelationships
How well two players get on (`docs/CHEMISTRY.md` §4); one row per pair, kept when they part.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| PlayerAId | id | FK Players; the smaller id of the pair; unique (PlayerAId, PlayerBId) |
| PlayerBId | id | FK Players; indexed |
| Score | int | −100…100, 0 = strangers |
| UpdatedOn | date | Last change |

### ContractNegotiations
Rounds of contract talks between a club and a player (`docs/CONTRACTS.md` §4.5); history, never deleted.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| PlayerId | id | FK Players; index (PlayerId, ClubId, StartedOn) |
| ClubId | id | FK Clubs |
| RenewalOfId | id? | FK Contracts: the contract being renewed; null for a new contract |
| StartedOn / LastActivityOn | date | |
| Rounds | int | Offers made |
| Patience | int | Rounds left before the player breaks off |
| Status | text(16) | `Open`, `Agreed`, `BrokenOff` |
| ClosedOn | date? | Agreement or break-off date (talks resume 30 days after a break-off) |

### Injuries
Medical records: one row per injury, history, never deleted (rules in `docs/INJURIES.md`).

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| PlayerId | id | FK Players; index (PlayerId, OccurredOn) |
| ClubId | id? | FK Clubs: the player's club when injured (null for a free agent) |
| Type | text(32) | `InjuryType` name (`AnkleSprain`, `TornAcl`, …) |
| Severity | text(16) | `Minor`, `Moderate`, `Serious`, `Severe` (from the recovery days) |
| Source | text(16) | `Match`, `Training` |
| OccurredOn | date | |
| MatchId | id? | FK Matches, for match injuries |
| RecoveryDays | int | True recovery time at normal speed, 1–400 (grows when aggravated) |
| Progress | real | Recovery days done, 0–RecoveryDays |
| EstimatedReturn | date | Latest medical estimate |
| AssessedOn | date | Date of that estimate |
| IsRecurrence | bool | Same body part injured within the recurrence window |
| Aggravations | int | Times made worse by playing or training on it |
| RecoveredOn | date? | First fit day; null while active (indexed) |

### ClubBudgets
The board's budget of a club for a season (docs/FINANCES.md §3); history.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| ClubId / SeasonId | id | FKs; unique (ClubId, SeasonId) |
| PlannedOn | date | |
| Reputation | int | Club reputation when planned, 1–100 |
| OpeningBalance | long | Balance when planned |
| Sponsorship, TicketSales, ArenaCosts, TravelCosts, StaffWages | long | Expected for the year |
| TicketPrice / Attendance / HomeGames | long / int / int | Regular-season matchday assumptions |
| WageBudget / TransferBudget | long | Copied wage budget (also in `Clubs.WageBudget`); fees, loan payments and bonuses allowed |
| MinimumBalance | long | Financial target for the year end |

### FinancialTransactions
The ledger: one row per booking, never deleted (docs/FINANCES.md §1–2).

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| ClubId | id | FK Clubs; index (ClubId, Date) |
| Date | date | |
| Category | text(32) | `FinanceCategory` name (`PlayerSalaries`, `TicketSales`, …) |
| Amount | long | Non-zero; + income, − expenditure |
| Description | text(200)? | |

### Fixtures
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| CompetitionId / SeasonId | id | FKs. Index (`CompetitionId, SeasonId, Round`) |
| Stage | text | `RegularSeason`, `Playoffs`, `SecondPhase`, `PlayIn`, `CupGroup` or `Cup` (default `RegularSeason`) |
| Round | int | 1–200, within the stage (playoff round 1 = first round) |
| GameNumber | int | Game in a playoff series, 1–7; leg of a cup tie, 1–2; otherwise 1 (default 1) |
| Date | date | Indexed |
| HomeClubId / AwayClubId | id | FKs Clubs; must differ |

### Matches
The played result of a fixture.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| FixtureId | id | FK Fixtures, **unique** (one result per fixture) |
| HomeScore / AwayScore | int | 0–300, **no draws** (`HomeScore <> AwayScore`) |
| Overtimes | int | 0–10 |
| IsForfeit | bool | Game awarded 20–0 because a club could not field five players; no box score (default false) |
| TacticalNotes | json? | Array of `{ Period, Clock, Home, Text }` (≤ 60, text ≤ 200): game plans, tactical changes and situational summaries (Step 27). Null for older results |

### Careers
The user's career (at most one per game database, enforced by `ICareerService`; rules in `docs/SEASON_LIFECYCLE.md`).

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| ManagerName | text(60) | |
| ClubId | id | FK Clubs: the managed club |
| SeasonId | id | FK Seasons: the season being played |
| StartDate / CurrentDate | date | `CurrentDate` is the game date; it only moves forward |
| SeasonsPlayed | int | Seasons started, the first included |
| AssistantManages | bool | Holiday mode: the user's club is run by the AI (default false) |

### PlayerMatchStatistics
One box-score line per player per match.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| MatchId / PlayerId | id | FKs. **Unique** (`MatchId, PlayerId`). Index (`PlayerId`) |
| ClubId | id | FK Clubs: the club the player **represented in that match**. Statistics stay with the right club after a transfer. |
| Minutes | int | 0–100 |
| TwoPointMade/Attempted, ThreePointMade/Attempted, FreeThrowMade/Attempted | int | Made ≤ attempted (also checked in schema) |
| OffensiveRebounds, DefensiveRebounds, Assists, Steals, Blocks, Turnovers, PersonalFouls | int | 0–200 |

Points = 2 × two-point made + 3 × three-point made + free throws made, computed and not stored.

### LeagueStandings
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| CompetitionId / SeasonId / ClubId | id | FKs. **Unique** (`CompetitionId, SeasonId, ClubId`) |
| Won / Lost | int | ≥ 0; played = won + lost (no draws) |
| PointsFor / PointsAgainst | int | ≥ 0 |
| StandingPoints | int | ≥ 0. Awarded per result according to the competition rules, passed in by the caller |

## 4. Historical data guarantees

* Players and clubs keep their IDs for ever, so every record about them keeps pointing at the same entity.
* Club spells, contracts, matches, statistics and standings are kept for good. Restrict-on-delete makes it impossible to delete a player, club, competition or season that history refers to.
* Statistics store the club represented, so a player's career totals and a club's historical totals are both correct after transfers.

## 5. Data access

Ports (in `Ebm.Services.Data`): `IRepository<TEntity, TId>` (`FindAsync`, `ListAsync(predicate)`, `Add`, `Remove`) and
`IUnitOfWork.SaveChangesAsync`. Both are scoped: create one DI scope per unit of work. A rejected save throws
`DataConstraintException` and writes nothing. Implementations: `EfRepository`, `EfUnitOfWork` (in `Ebm.Persistence`).

Entities change only through their edit methods (`UpdateDetails`, `UpdateTerms`, `UpdateAttributes`, `UpdateRules`,
`Rename`, …), which apply the same validation as the constructors. Identity fields never change: IDs, a country's
code, a division's league, and a contract's club and person. Natural duplicate keys used by the editor and the
importer are defined in `Ebm.Services.Data.DuplicateKeys` (see `docs/IMPORT_FORMATS.md` §4).

## 6. Initialisation and migrations

* **At startup** `DatabaseInitializer` (a hosted service) creates the `databases` folder and database file if they are missing and applies every pending migration in order. It logs the result (`Database ready, schema version …`). If it fails, startup fails (exit code 2, startup-failure log). `EnsureCreated` is never used.
* **Applied migrations** are recorded in the `__EFMigrationsHistory` table. The latest migration ID is the schema version.
* **Adding a migration** after changing an entity or configuration:

  ```powershell
  dotnet tool restore
  dotnet ef migrations add <PascalCaseName> --project src/Ebm.Persistence --startup-project src/Ebm.Persistence --output-dir Migrations
  ```

  Review the generated `Up`/`Down` code, update this document and the schema version above, and run the tests.
  The test `Model_has_no_changes_missing_from_migrations` fails if the model changed without a migration.
* **Removing the last, unreleased migration:** `dotnet ef migrations remove --project src/Ebm.Persistence --startup-project src/Ebm.Persistence`.
* **SQL script for review:** `dotnet ef migrations script --project src/Ebm.Persistence --startup-project src/Ebm.Persistence`.
* **Compatibility:** after a release, never edit or delete a migration that has shipped; add a new one. Existing databases are upgraded in place at the next start.
