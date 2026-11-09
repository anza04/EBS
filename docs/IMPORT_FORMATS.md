# Import Formats and Database Editor

How data gets into the game database: the CSV and JSON import formats, field mappings, validation rules,
the import modes, and the database editor. Code: `src/Ebm.DataImport` (import), `src/Ebm.Services/Import` and
`src/Ebm.Services/Editing` (use cases used by the UI), `src/Ebm.Desktop/ViewModels/Editor` (editor screens).

## 1. Files

| Format | Contents | How the record type is found |
|---|---|---|
| CSV (`.csv`) | One record type per file, header row first | File name: `players.csv`, or with a suffix after `-`, `_` or `.` (`players-2026.csv`) |
| JSON (`.json`) | Several record types in one file | Section name in the root object |

* Encoding UTF-8 (a BOM is accepted). CSV follows RFC 4180: comma separator, optional double quotes,
  `""` for a quote inside a quoted field, line breaks allowed inside quotes. Blank lines are skipped.
* Field and column names are case-insensitive. Unknown columns or fields are ignored and reported once per file.
* Several files can be imported together. Records are always processed in dependency order, whatever the
  file order: countries → arenas → clubs → leagues → divisions → competitions → players → contracts.
  Within one type, file order is kept.
* JSON root: `{ "format": "ebm-dataset", "version": 1, "countries": [ … ], "players": [ … ], … }`.
  `format` is informative; `version`, when present, must be `1`. Each section is an array of objects whose
  properties are the fields below. Numbers may be JSON numbers or strings; `rules` may be a JSON object.

Templates: `data/templates/<type>.csv` (one example row each; together they form a valid mini dataset) and
`data/templates/dataset-template.json`. Sample dataset: `data/sample/italy-sample.json` (fictional Italian
league: 5 countries, 1 league, 2 divisions, 3 competitions, 8 arenas, 8 clubs, 33 players, 33 contracts; Aurora Milano has a complete 12-player squad).
Both folders are copied next to the executable (`data\…`) and the import file dialog opens there.

## 2. Field mappings

`*` = required. Dates are `YYYY-MM-DD`. IDs are GUIDs. Text is trimmed.

| Section / file | Field | Maps to | Notes |
|---|---|---|---|
| `countries` | `id` | `Country.Id` | Optional; countries are identified by `code` |
| | `code`* | `Country.Code` | ISO 3166-1 alpha-3, any case (stored upper case) |
| | `name`* | `Country.Name` | ≤ 100 |
| `arenas` | `id` | `Arena.Id` | |
| | `name`*, `city`* | `Name`, `City` | ≤ 100 each |
| | `country`* | `CountryId` | Country **code** |
| | `capacity`* | `Capacity` | 1–100 000 |
| `clubs` | `id` | `Club.Id` | Permanent: keep it in every later file |
| | `name`* / `shortName`* | `Name` (≤ 100) / `ShortName` (≤ 20) | |
| | `country`* | `CountryId` | Country code |
| | `homeArenaId` | `HomeArenaId` | Arena ID |
| | `foundedYear` | `FoundedYear` | 1850–2100 |
| | `city` | `City` | ≤ 100; empty = the home arena's city |
| | `reputation` | `Reputation` | 1–100; default 50 |
| | `balance` / `wageBudget` | `Balance` / `WageBudget` | Whole euros; balance ±10 billion (may be negative), wage budget 0–1 billion a year; default 0 |
| | `boardExpectation` | `BoardExpectation` | `WinTitle`, `ReachPlayoffs`, `MidTable`, `AvoidRelegation`, `WinPromotion`; default `MidTable` |
| `leagues` | `id`, `name`* (≤ 100), `shortName`* (≤ 20) | `League` | |
| | `country` | `CountryId` | Country code; empty for international leagues |
| `divisions` | `id`, `name`* (≤ 100) | `Division` | |
| | `leagueId`* | `LeagueId` | League ID; cannot change on update |
| | `level`* | `Level` | 1 (top) – 10, unique within the league |
| `competitions` | `id`, `name`*, `shortName`* | `Competition` | |
| | `type`* | `Type` | `League`, `Cup` or `Tournament` |
| | `country` | `CountryId` | Country code; empty for international |
| | `divisionId` | `DivisionId` | Division ID, for league championships |
| | `rules` | `RulesJson` | Competition rules object (schema: `docs/COMPETITION_FORMATS.md`); default `{}`. In CSV, quote it: `"{""pointsForWin"":2}"` |
| `seasons` | `id`, `name`* (≤ 20, unique) | `Season` | e.g. `2026-27` |
| | `startDate`*, `endDate`* | | End after start |
| `participants` | `id` | `CompetitionParticipant` | A club's entry in a competition season |
| | `competitionId`*, `seasonId`*, `clubId`* | | IDs; cannot change on update. Registration rules apply (below) |
| `players` | `id` | `Player.Id` | Permanent: keep it in every later file |
| | `firstName`*, `lastName`* | names | ≤ 60 each |
| | `dateOfBirth`* | `DateOfBirth` | |
| | `nationality`* | `NationalityId` | Country code |
| | `heightCm`*, `weightKg`* | | 150–250, 50–200 |
| | `position`* | `Position` | `PointGuard`, `ShootingGuard`, `SmallForward`, `PowerForward`, `Center` or `PG`, `SG`, `SF`, `PF`, `C` |
| | `closeShot`*, `midRange`*, `threePoint`*, `freeThrow`*, `layup`*, `dunk`*, `postMoves`*, `passing`*, `courtVision`*, `ballHandling`*, `decisionMaking`*, `perimeterDefense`*, `interiorDefense`*, `steal`*, `block`*, `defensivePositioning`*, `offensiveRebound`*, `defensiveRebound`*, `speed`*, `strength`*, `vertical`*, `agility`*, `stamina`*, `concentration`*, `leadership`*, `consistency`*, `workRate`* | `PlayerAttributes` | 1–100 each (definitions: `docs/PLAYER_MODEL.md`) |
| | `wingspanCm` | `WingspanCm` | Height ± 30; default the height |
| | `secondaryPositions` | `SecondaryPositions` | Codes or names separated by `\|`, `;`, `,` or spaces (`SG\|SF`; in CSV avoid `,` unless quoted); must not include `position`; default none |
| | `experience`, `reputation`, `potential` | | 1–100; defaults 1, 1, 50 |
| | `fitness`, `morale` | | 0–100 and 1–100; defaults 100, 50 |
| | `injuryStatus`, `injuryReturnDate` | | `Healthy` (default), `DayToDay`, `Injured`; return date required when `Injured`, not allowed when `Healthy` |
| | `threePointShot`, `midRangeShot`, `driveToBasket`, `postUp`, `passFirst`, `isolation`, `crashOffensiveGlass`, `defensiveGambling` | `PlayerTendencies` | 1–100; each missing one is suggested from the attributes |
| `contracts` | `id` | `Contract.Id` | Player contracts only |
| | `clubId`*, `playerId`* | | Club and player IDs; cannot change on update |
| | `startDate`*, `endDate`* | | End after start |
| | `annualSalary`* | `AnnualSalary` | Whole euros, 0–100 000 000 |

## 3. Validation rules

Each record is checked in this order; the first failing stage rejects it with all messages of that stage.

1. **Shape.** CSV row has as many values as the header has columns; JSON item is an object.
2. **Fields.** Required fields present; numbers, dates, GUIDs and enum values parse; ratings in range;
   references resolve. A reference must point to a record that is in the database or was **accepted earlier in
   the same import** (so a contract whose player was rejected is rejected too).
3. **Domain rules.** The entity constructor runs (lengths, ranges, dates, rules JSON …); its message is reported.
4. **Identity and duplicates** (see §4).

Messages name the field and the bad value, for example
`'dateOfBirth' must be a date in the form YYYY-MM-DD (got '01/02/2000').` or
`'country' refers to the unknown country code 'XYZ'. Add the country first (countries section or countries.csv).`

File-level problems (unreadable or malformed file, unknown CSV file name, missing required columns, unknown
sections or fields, unsupported JSON version) are listed separately; a file that cannot be parsed is skipped.

## 4. IDs, duplicates and updates

* **Unique ID generation.** A record without `id` gets a new time-ordered GUID; the result notes
  `New ID assigned: …`. Give players and clubs an `id` in your files so later imports update them instead
  of being refused as duplicates.
* **Same ID twice in one import** → the second record is rejected.
* **Duplicate detection** (`Ebm.Services.Data.DuplicateKeys`, shared with the editor). Text is compared
  ignoring case and extra spaces.

  | Type | Same record when |
  |---|---|
  | Country | same code |
  | Player | same first name, last name and date of birth |
  | Club | same name in the same country |
  | Arena | same name and city |
  | League | same name |
  | Division | same league and level |
  | Competition | same name and country |
  | Contract | same club, player and start date |

  A record whose key matches a *different* record (in the database or earlier in the import) is rejected
  with the existing record's name and ID.
* **Modes.** Nothing is ever overwritten silently:

  | Mode (UI option) | ID exists in the database | ID is new |
  |---|---|---|
  | `AddNew` (default) | rejected: "already exists … choose 'update existing records'" | added |
  | `AddOrUpdate` ("Update existing records") | all fields replaced with the file values (explicit update) | added |

  Countries without `id` are matched by `code`. A division's league and a contract's club/player cannot change.
* **Commit.** Accepted records are written in **one transaction** after all records are checked.
  Options: *Validate only* (`DryRun`, writes nothing) and *All or nothing* (writes nothing if any record is
  rejected). If the database itself refuses the save, nothing is written and the reason is reported.

## 5. Import logs

Every import writes `<DataRoot>\logs\imports\import-YYYYMMDD-HHMMSS-fff.log`: mode, files, summary, file
problems and one line per record (outcome, type, `file:line` or `file#section:index`, name, ID, messages).
A summary line also goes to the application log.

## 6. Database editor

The main window shows the editor (tabs *Players*, *Player profile*, *Compare players*, *Clubs*, *Club profile*, *Squad*, *Contracts*, *Leagues and competitions*, *Import*).
Select a row to edit it, or *New* for a blank form; *Save* runs the same domain validation and duplicate rules as
the import and shows the result under the form. Each save is its own unit of work (`IDatabaseEditor`).
Players include profile, condition, the 27 attributes and the tendencies (*Suggest from attributes* fills them from the
attributes); a blank wingspan means "same as height". *Player profile* and *Compare players* are read-only views
(`IPlayerProfileService`). Clubs include city, reputation, balance, wage budget and board expectation; *Club profile*
and *Squad* use `IClubService` (`docs/ROSTER_RULES.md`). Contracts: club and player are fixed once created.

## 7. Known data limitations

* Importable types: countries, arenas, clubs, leagues, divisions, competitions, seasons, competition
  participants, players and player contracts. Staff, staff contracts, club history, squad roles, fixtures, matches,
  statistics and standings are not importable (fixtures and standings are generated by the competition engine).
* Participants are checked against the competition's registration rules (club country, maximum clubs, one
  league competition per club and season) and refused once the competition season has a schedule.
  The minimum number of clubs is checked when the schedule is generated, not at import. The editor does not edit countries,
  arenas or divisions (they come from imports).
* Updates replace every field of the record; partial updates (only some columns) are not supported. Optional player
  fields left out of an update take their defaults (fitness 100, healthy, suggested tendencies, …).
* Player files in the Step 03/04 format (8 ratings: `shooting`, `insideScoring`, …) are no longer accepted; use the
  27 attribute columns (templates in `data/templates`).
* Records are never deleted by an import or by the editor.
* Duplicate checks and imports load all records of a type into memory; fine for one country's data
  (thousands of records), to be revisited for the full European database (Step 16).
* While a record has field errors (stage 2), domain-rule errors of its other fields (stage 3) are reported
  only after those are fixed.
* Rule JSON must follow the competition rules schema; unknown properties are refused so typos are caught.
* The player attribute set is the initial one (8 ratings); Step 05 may add attributes and import columns.
