# Database Schema

Game database: one SQLite file, `<DataRoot>\databases\ebm.db`. It is managed by EF Core 10 with versioned migrations.
Schema version: **`20261109114155_InitialSchema`** (Step 02).

Entities are plain C# classes in `src/Ebm.Domain`. Their mapping (tables, keys, indexes, constraints) lives in
`src/Ebm.Persistence/Configurations`. The domain validates every value on construction, and the schema
enforces the same rules again so data written by other routes (imports, editors, raw SQL) cannot break them.

## 1. Conventions

| Rule | Detail |
|---|---|
| Primary keys | Every table has an `Id` column holding a GUID stored as TEXT. In code each entity has its own ID type (`PlayerId`, `ClubId`, …), so IDs of different entities cannot be mixed up. IDs are assigned by the application, never by the database (`CreateVersion7` for new records; imported records keep the ID from the data file). This makes player and club IDs permanent across databases, imports and save games. |
| Foreign keys | Always enforced (`Foreign Keys=True` in the connection string). Every foreign key is `ON DELETE RESTRICT`, so a record that anything refers to cannot be deleted and history is never removed as a side effect. The only cascade: `PlayerAttributes` is deleted with its player (a player can only be deleted if nothing else refers to it). |
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
         ├─< Player (NationalityId) ── PlayerAttributes (1:1)
         └─< StaffMember (NationalityId)

Season ─┬─< CompetitionParticipant >─ Competition, Club      (club entered in a competition for a season)
        ├─< LeagueStanding         >─ Competition, Club
        └─< Fixture >─ Competition, Club (home), Club (away)
                 └── Match (1:1) ─< PlayerMatchStatistics >─ Player, Club (club represented)

Club ─┬─< Contract >─ Player | StaffMember (exactly one)
      └─< PlayerClubHistory >─ Player
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

### PlayerAttributes
1:1 with Players, always loaded with the player (EF owned type). This is the initial attribute set; Step 05 finalises the catalogue.

| Column | Type | Rules |
|---|---|---|
| PlayerId | id | PK and FK Players (cascade) |
| Shooting, InsideScoring, Passing, BallHandling, Rebounding, Defense, Athleticism, BasketballIq | int | Each 1–100 |

### Staff
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| FirstName / LastName | text(60) | Required |
| DateOfBirth | date | |
| NationalityId | id | FK Countries |
| Role | text | `HeadCoach`, `AssistantCoach`, `Scout`, `Physio`, `FitnessCoach` |

### Contracts
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| ClubId | id | FK Clubs |
| PlayerId | id? | FK Players |
| StaffMemberId | id? | FK Staff. **Exactly one** of `PlayerId` and `StaffMemberId` is set |
| StartDate / EndDate | date | `EndDate > StartDate` |
| AnnualSalary | int | Euros, 0–100,000,000 (`>= 0` in schema) |

Contracts are never deleted; expired ones are history.

### PlayerClubHistory
One row per spell of a player at a club.

| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| PlayerId / ClubId | id | FKs. Indexes (`PlayerId, StartDate`) and (`ClubId`) |
| StartDate | date | |
| EndDate | date? | Null while current; `EndDate >= StartDate` |

**At most one current spell per player:** a unique index on `PlayerId` filtered by `EndDate IS NULL`
(`IX_PlayerClubHistory_PlayerId_Current`). A move is recorded by closing the current spell and opening a new one.
Overlap checks between closed spells and loans are added with transfers (Step 08).

### Fixtures
| Column | Type | Rules |
|---|---|---|
| Id | id | PK |
| CompetitionId / SeasonId | id | FKs. Index (`CompetitionId, SeasonId, Round`) |
| Round | int | 1–200 |
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
