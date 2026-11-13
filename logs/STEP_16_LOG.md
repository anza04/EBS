# STEP 16 — European player database expansion

**Date:** 2026-11-13
**Version:** 0.16.0
**Status:** Completed. All acceptance criteria met. By the user's decision, real clubs are used and players are
generated (no licensed player source); see §7 and `docs/PLAYER_DATABASE.md`.

## 1. Objectives

From `docs/ROADMAP.md`, Step 16: populate the European world with players (nationality and citizenship, multiple
nationalities, history, positions, attributes, contracts where available, market value estimates) with a consistent
model and explicit handling of unknown, estimated, missing and unverified data.

| Criterion | Proof |
|---|---|
| Players can be imported from all supported countries | `EuropePlayerDatabaseTests.Every_country_imports…`: 6,144 players, contracts at clubs of all 18 countries, no rejection |
| Duplicate players are identified across leagues | `…Duplicates_across_leagues…`: `Đorđević` vs `Djordjevic` rejected; same name, close birth date noted; `PlayerDataQualityTests` (accent folding) |
| Player IDs remain stable | Re-import with *Update existing records*: 0 added, identical player and contract IDs; generator IDs are UUID v5 from identity |
| Player-club relationships are correct | One running contract per player, 12 per club, history closed before the contract and at another club, registration limits hold (one club per competition through `IClubService`) |
| Missing data is distinguishable from verified data | `DataStatus` on profile, attributes, contract, club; estimated attributes/salary marked `Estimated`; `Verified` needs a source; unverified real clubs |

## 2. Implementation

* **Domain:** `DataStatus` and `DataQuality` (`Ebm.Domain.Common`); `Player.SecondNationalityId`, `ProfileStatus`,
  `AttributesStatus`, `Source`, `HasNationality`; `Contract.TermsStatus`, `Source`; `Club.DataStatus`;
  `PositionProfile` (moved out of `PlayerGenerator`) and `AttributeEstimator`.
* **Rules:** EU/EEA status uses every nationality (`ClubService`, `TransferService`); domestic/foreign use the sporting nationality.
* **Import:** optional attributes and salary (estimated, with notes), statuses and sources, `secondNationality`,
  `playerHistory` section (closed, non-overlapping spells), accent-insensitive duplicate keys, possible-duplicate notes.
* **UI:** profile shows second nationality, club history, market value (estimate), data status and source; the
  players editor edits second nationality, statuses and source.
* **Data:** generator writes 240 real top-division clubs (Unverified), 272 placeholders (Generated, Step 15 IDs kept),
  6,144 players, 6,144 contracts, 1,500 past spells (CSV). Italian sample and templates declare their status.
* **Performance:** `ClubMembership.MembersOfAsync` loads a squad from its club's contracts and spells only. Without it
  a 512-club game day took ≈ 48 s (every squad load read every running contract); now < 1 s.

## 3. Files

### Created
| File | Purpose |
|---|---|
| `src/Ebm.Domain/Common/DataStatus.cs` | Data status and its rules |
| `src/Ebm.Domain/Players/PositionProfile.cs` | Position profiles and attribute estimator |
| `src/Ebm.Persistence/Migrations/20261113110006_PlayerDatabase*.cs` | Migration (hand-edited SQL for the FK column) |
| `data/europe/players-/contracts-/playerHistory-europe-2026-27.csv` | Generated player database |
| `docs/PLAYER_DATABASE.md` | Handoff: coverage, sources, data quality, gaps |
| `tests/Ebm.Tests.Integration/Import/EuropePlayerDatabaseTests.cs` | Acceptance tests (import, duplicates/missing data, career) |
| `tests/Ebm.Tests.Unit/Players/PlayerDataQualityTests.cs` | Domain and duplicate-key tests |

### Modified
Player, Contract, Club, PlayerGenerator; EF configurations and snapshot; ImportSession, ImportFields, FieldReader,
ImportContracts, DuplicateKeys; ClubService, ClubMembership, TransferService; PlayerProfileService and its contract;
DatabaseEditor and `PlayerDraft`; PlayersPageViewModel, EditorLookups, `Themes/Editor.xaml`, PlayerViewModels;
`build/generate-europe-dataset.py`, `data/europe/europe-2026-27.json`, `data/sample/*.json`, `data/templates/*`;
docs `IMPORT_FORMATS.md`, `COUNTRY_REPORT.md`; upgrade tests (latest migration); `Directory.Build.props`, `CHANGELOG.md`,
`PROJECT_STATE.md`.

## 4. Architectural decisions

Decisions 53–56 in `PROJECT_STATE.md`: explicit data status; sporting nationality vs citizenship; accent-folding
duplicate keys and immutable imported history; per-club reads must not scan the database.

## 5. Database changes

Migration `20261113110006_PlayerDatabase`: add-column only. `Players.SecondNationalityId` (FK to `Countries`,
restrict, added with `ALTER TABLE … REFERENCES` so SQLite does not rebuild the table, decision 21),
`ProfileStatus`, `AttributesStatus` (default `Unverified`), `Source`; `Contracts.TermsStatus`, `Source`;
`Clubs.DataStatus`. Upgrade test checks the defaults and the foreign key.

## 6. Tests and validation

| Run | Result |
|---|---|
| `dotnet build` (Release) | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including launch) | PASS 434/434 (317 unit, 117 integration) |
| `EuropePlayerDatabaseTests` | PASS (import ≈ 15 s; career test: 20 days, two matchdays, ≥ 240 matches, no forfeit) |

## 7. Known limitations

* No real players: no licensed source; real players come through import files with sources (`docs/PLAYER_DATABASE.md` §5).
* Real clubs are unverified for 2026-27; second divisions remain placeholders.
* Club history only with clubs in the database; no youth, staff or national-team data.
* A one-off 69-minute game day was measured during profiling and did not reproduce (39 s for the whole re-run);
  most likely the machine was suspended.

## 8. Outstanding issues

None.

## 9. Instructions for the next step

1. Read `PROJECT_STATE.md`, this log and `docs/ROADMAP.md` (Step 17: domestic cups).
2. Keep `EuropePlayerDatabaseTests`, `EuropeDatasetTests`, `MilestoneOneTests` and `CareerSimulationTests` green.
3. Regenerate data only through `build/generate-europe-dataset.py`.
